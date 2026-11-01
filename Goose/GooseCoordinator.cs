using System;

namespace TakeABreakGoose;

/// <summary>
/// Directs the goose: which visit comes when, what happens to a goose on screen when the session
/// stops or Do not disturb goes on, and passing the user's answers on to the scheduler.
/// Only one goose is on screen at a time; visits that mustn't get lost wait until it has left.
/// </summary>
public sealed class GooseCoordinator
{
    private readonly BreakScheduler _scheduler;
    private readonly Func<GooseSettings> _settings;
    private readonly CallDoNotDisturb _call = new();
    private GooseWindow? _goose;
    private GooseVisit? _currentVisit;
    private GooseVisit? _pendingVisit;

    /// <param name="settings">The current settings (they can change while the app runs).</param>
    public GooseCoordinator(BreakScheduler scheduler, Func<GooseSettings> settings)
    {
        _scheduler = scheduler;
        _settings = settings;
        _scheduler.BreakDue += () => SummonBreakGoose();
        _scheduler.SessionCompleted += SignOff;
    }

    /// <summary>Whether a goose is on screen right now (walking, talking, peeking or napping).</summary>
    public bool GooseOnScreen => _goose != null;

    private bool GooseJustVisiting => _currentVisit?.JustVisiting == true;

    /// <summary>A break reminder with one of the user's messages.</summary>
    public void SummonBreakGoose()
    {
        var messages = _settings().Messages;
        string message = messages[Random.Shared.Next(messages.Count)];
        int breakMinutes = _scheduler.CurrentBreakMinutes;
        string? hint = breakMinutes > 0
            ? $"Take {breakMinutes} minutes. I'll start counting again after that."
            : null;
        Summon(new GooseVisit("HONK!", message, hint, "Okay, break time!",
            AllowSnooze: true, AllowFinishUp: true, StartsBreak: true));
    }

    /// <summary>Sends a goose over just to show off an outfit. Returns false if a goose is already on screen.</summary>
    public bool ShowOffOutfit(GooseOutfit outfit)
    {
        if (_goose != null) return false;
        Summon(new GooseVisit("Ta-da!", GooseOutfits.ShowOffLine(outfit), null, "Looking good!",
            AllowSnooze: false, AllowFinishUp: false, StartsBreak: false, outfit, JustVisiting: true));
        return true;
    }

    /// <summary>Stops the session; the session's goose (asking, peeking or napping) walks off with it.</summary>
    public void StopSession()
    {
        _scheduler.StopSession();
        if (_goose != null && !GooseJustVisiting) _goose.WalkAway();
        if (_pendingVisit?.StartsBreak == true) _pendingVisit = null;
    }

    /// <summary>Keeps the goose away (only during a session); a goose on screen disappears right away.</summary>
    public void SetDoNotDisturb(bool on)
    {
        if (on && _scheduler.Session == null) return;
        if (on && _goose != null)
        {
            if (!GooseJustVisiting && _currentVisit?.StartsBreak == true)
            {
                if (_goose.StillAskingForBreak) _scheduler.PostponeBreakForDoNotDisturb();
                // Already on the break: a meeting means being back at work, so the next countdown starts.
                else if (_goose.OnBreak) _scheduler.ReadyToFocus();
            }
            else if (!GooseJustVisiting)
            {
                _scheduler.GooseAnswered(GooseOutcome.Dismissed, startsBreak: false);
            }
            // A reminder that was queued behind this goose waits too.
            if (_pendingVisit?.StartsBreak == true) _scheduler.PostponeBreakForDoNotDisturb();
            _pendingVisit = null;
            _goose.LeaveNow();
        }
        _scheduler.SetDoNotDisturb(on);
    }

    /// <summary>Do not disturb is on because of a call, and goes off again when the call is over.</summary>
    public bool DoNotDisturbForCall => _call.TurnedOn;

    /// <summary>A call turned Do not disturb on.</summary>
    public event Action? CallStarted;

    /// <summary>Turns Do not disturb on when a call starts and off again when it is over.</summary>
    public void CheckForCall(bool inCall)
    {
        var change = _call.Update(inCall, _settings().DoNotDisturbDuringCalls,
            _scheduler.Session != null, _scheduler.DoNotDisturb);
        if (change is not bool on) return;
        SetDoNotDisturb(on);
        if (on) CallStarted?.Invoke();
    }

    /// <summary>The session reached its end time: the goose comes by, in its party hat, to say goodbye.</summary>
    private void SignOff(WorkSession session)
    {
        var focused = DateTime.Now - session.StartedAt;
        string duration = focused.TotalHours >= 1
            ? $"{(int)focused.TotalHours} h {focused.Minutes} min"
            : $"{Math.Max(1, (int)focused.TotalMinutes)} min";
        Summon(new GooseVisit(
            "That's a wrap!",
            $"That's it for this focus session: {duration} of focus since {session.StartedAt:HH:mm}. "
            + "Time to wrap up and give your brain some rest.",
            null, "Bye, goose!", AllowSnooze: false, AllowFinishUp: false, StartsBreak: false,
            Outfit: GooseOutfit.PartyHat));
    }

    private TimeSpan BreakLength()
    {
#if DEBUG
        // For testing breaks without waiting minutes: set TAKEABREAKGOOSE_BREAK_SECONDS (debug builds only).
        if (int.TryParse(Environment.GetEnvironmentVariable("TAKEABREAKGOOSE_BREAK_SECONDS"), out int seconds))
            return TimeSpan.FromSeconds(seconds);
#endif
        return TimeSpan.FromMinutes(_scheduler.CurrentBreakMinutes);
    }

    private void Summon(GooseVisit visit)
    {
        if (_goose != null)
        {
            // A reminder shouldn't get lost behind a goose that's just showing off, and "session
            // complete" shouldn't get lost behind anything, so those come as soon as this goose has left.
            if (GooseJustVisiting || !visit.StartsBreak) _pendingVisit ??= visit;
            return;
        }

        bool naps = visit.StartsBreak && _scheduler.CurrentBreakMinutes > 0;
        _currentVisit = visit;
        _goose = new GooseWindow(visit, _settings().SnoozeMinutes, naps ? BreakLength() : null);
        if (!visit.JustVisiting)
        {
            _goose.Answered += outcome =>
                _scheduler.GooseAnswered(outcome, startsBreak: naps && outcome == GooseOutcome.Dismissed);
            _goose.BreakExtended += _scheduler.ExtendBreak;
            _goose.ReadyToFocus += _scheduler.ReadyToFocus;
            _goose.TookBreakWhileAway += _scheduler.BreakTakenWhileAway;
            _scheduler.GooseArrived();
        }
        _goose.Finished += () =>
        {
            _goose = null;
            _currentVisit = null;
            if (_pendingVisit is { } pending)
            {
                _pendingVisit = null;
                Summon(pending);
            }
        };
        _goose.Show();
    }
}
