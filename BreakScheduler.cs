using System;
using System.Windows.Threading;

namespace TakeABreakGoose;

/// <param name="StartedAutomatically">Started by the "start a focus session when the app starts" setting.</param>
public sealed record WorkSession(int IntervalMinutes, DateTime StartedAt, DateTime? EndsAt, bool StartedAutomatically);

/// <summary>
/// Decides when the goose should show up. Reminders only come during a focus session;
/// outside one the goose stays quiet.
/// </summary>
public sealed class BreakScheduler
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private GooseSettings _settings;
    private TimeSpan? _remainingWhenPaused;

    public WorkSession? Session { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsAway { get; private set; }
    public bool GooseVisiting { get; private set; }
    public DateTime? NextBreakAt { get; private set; }
    public DateTime? BreakEndsAt { get; private set; }

    /// <summary>
    /// A break was taken and the user hasn't said they're ready to focus again yet.
    /// No reminders are counted down until they do.
    /// </summary>
    public bool AwaitingReturn { get; private set; }

    /// <summary>
    /// The user doesn't want the goose to show up right now (e.g. during a meeting). The countdown
    /// keeps running; a break that comes due meanwhile waits until this is turned off.
    /// </summary>
    public bool DoNotDisturb { get; private set; }

    /// <summary>A break came due during Do not disturb and is waiting for it to be turned off.</summary>
    public bool BreakWaiting { get; private set; }

    /// <summary>Raised every second and whenever the state changes, so UIs can refresh countdowns.</summary>
    public event Action? StateChanged;
    public event Action? BreakDue;
    public event Action? SessionCompleted;

    public BreakScheduler(GooseSettings settings)
    {
        _settings = settings;
        _tick.Tick += (_, _) => Tick();
        _tick.Start();
        Reschedule();
    }

    /// <summary>Minutes between reminders right now, or null when the goose should stay quiet.</summary>
    public int? CurrentInterval => Session?.IntervalMinutes;

    public void StartSession(int intervalMinutes, DateTime? endsAt, bool automatically = false)
    {
        Session = new WorkSession(intervalMinutes, DateTime.Now, endsAt, automatically);
        IsPaused = false;
        Reschedule();
    }

    public void StopSession()
    {
        Session = null;
        IsPaused = false;
        DoNotDisturb = false;
        Reschedule();
    }

    /// <summary>Only during a focus session. Turning it off brings a break that's been waiting.</summary>
    public void SetDoNotDisturb(bool on)
    {
        if (on == DoNotDisturb || (on && Session == null)) return;
        DoNotDisturb = on;
        StateChanged?.Invoke();
        if (!on && BreakWaiting)
        {
            BreakWaiting = false;
            if (Session != null) BreakDue?.Invoke();
        }
    }

    /// <summary>A goose asking for a break was sent away by Do not disturb: the break waits.</summary>
    public void PostponeBreakForDoNotDisturb()
    {
        GooseVisiting = false;
        NextBreakAt = null;
        BreakWaiting = true;
        StateChanged?.Invoke();
    }

    public void SetPaused(bool paused)
    {
        if (paused == IsPaused) return;
        if (paused)
        {
            _remainingWhenPaused = NextBreakAt - DateTime.Now;
            NextBreakAt = null;
            IsPaused = true;
        }
        else
        {
            IsPaused = false;
            NextBreakAt = DateTime.Now + _remainingWhenPaused;
            _remainingWhenPaused = null;
        }
        StateChanged?.Invoke();
    }

    public void ApplySettings(GooseSettings settings)
    {
        _settings = settings;
        StateChanged?.Invoke();
    }

    public void GooseArrived()
    {
        GooseVisiting = true;
        NextBreakAt = null;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Called as soon as the user answers the goose (it may stay on screen for a while after,
    /// e.g. to nap during the break).
    /// </summary>
    /// <param name="startsBreak">Whether this answer starts a break of the configured length.</param>
    public void GooseAnswered(GooseOutcome outcome, bool startsBreak)
    {
        GooseVisiting = false;
        var now = DateTime.Now;
        BreakEndsAt = null;
        if (startsBreak)
        {
            // The next reminder only starts counting once the user says they're ready to focus again.
            BreakEndsAt = now.AddMinutes(_settings.BreakMinutes);
            AwaitingReturn = true;
            SetNext(null);
        }
        else if (outcome == GooseOutcome.Snoozed)
        {
            SetNext(now.AddMinutes(_settings.SnoozeMinutes));
        }
        else
        {
            SetNext(CurrentInterval is int interval ? now.AddMinutes(interval) : null);
        }
        StateChanged?.Invoke();
    }

    /// <summary>
    /// The user was away long enough for a break while the goose waited for an answer: that counts
    /// as the break, and the goose now asks whether they're ready to focus.
    /// </summary>
    public void BreakTakenWhileAway()
    {
        GooseVisiting = false;
        AwaitingReturn = true;
        BreakEndsAt = null;
        SetNext(null);
        StateChanged?.Invoke();
    }

    /// <summary>The user asked for a few more minutes of break.</summary>
    public void ExtendBreak(DateTime endsAt)
    {
        BreakEndsAt = endsAt;
        StateChanged?.Invoke();
    }

    /// <summary>The user is back from their break and ready to focus: start counting towards the next one.</summary>
    public void ReadyToFocus()
    {
        AwaitingReturn = false;
        BreakEndsAt = null;
        SetNext(CurrentInterval is int interval ? DateTime.Now.AddMinutes(interval) : null);
        StateChanged?.Invoke();
    }

    /// <summary>A one-line summary for the tray tooltip and the window header.</summary>
    public string Describe()
    {
        if (GooseVisiting) return "The goose is here!";
        if (IsPaused) return "Reminders paused";
        if (DoNotDisturb)
        {
            return BreakWaiting ? "Do not disturb · a break is waiting"
                : NextBreakAt is DateTime due ? $"Do not disturb · next break at {due:HH:mm}"
                : "Do not disturb";
        }
        if (AwaitingReturn)
        {
            return BreakEndsAt is DateTime end && end > DateTime.Now
                ? $"On a break until {end:HH:mm}"
                : "Break's over, waiting for you";
        }
        if (IsAway) return "Away from the keyboard";
        if (BreakEndsAt is DateTime breakEnd) return $"On a break until {breakEnd:HH:mm}";
        if (NextBreakAt is DateTime next)
        {
            string mode = Session != null ? "Focus session" : "Snoozed";
            return $"{mode} · next break at {next:HH:mm}";
        }
        return "No focus session running";
    }

    private void Tick()
    {
        var now = DateTime.Now;

        if (Session?.EndsAt is DateTime end && now >= end && !GooseVisiting)
        {
            // A session that ends during Do not disturb ends quietly: no "session complete" goose,
            // and no break that was waiting.
            bool quietly = DoNotDisturb;
            Session = null;
            IsPaused = false;
            DoNotDisturb = false;
            Reschedule();
            if (!quietly) SessionCompleted?.Invoke();
            return;
        }

        if (!AwaitingReturn && BreakEndsAt is DateTime breakEnd && now >= breakEnd) BreakEndsAt = null;

        // While waiting for the user to come back from a break, the goose handles that itself.
        if (_settings.AwayMinutes > 0 && !GooseVisiting && !IsPaused && !AwaitingReturn)
        {
            bool away = UserActivity.IdleTime() >= TimeSpan.FromMinutes(_settings.AwayMinutes);
            if (IsAway && !away)
            {
                // Welcome back: the time away counted as a break, so start a fresh interval.
                IsAway = false;
                Reschedule();
                return;
            }
            IsAway = away;
        }
        else
        {
            IsAway = false;
        }

        if (!IsAway && !IsPaused && !GooseVisiting && !AwaitingReturn && NextBreakAt is DateTime next && now >= next)
        {
            NextBreakAt = null;
            if (DoNotDisturb) BreakWaiting = true;
            else BreakDue?.Invoke();
        }

        StateChanged?.Invoke();
    }

    private void Reschedule()
    {
        if (!AwaitingReturn) BreakEndsAt = null;
        BreakWaiting = false; // a fresh countdown replaces a break that was waiting
        SetNext(GooseVisiting || CurrentInterval is not int interval ? null : DateTime.Now.AddMinutes(interval));
        StateChanged?.Invoke();
    }

    private void SetNext(DateTime? at)
    {
        if (IsPaused)
        {
            _remainingWhenPaused = at - DateTime.Now;
            NextBreakAt = null;
        }
        else
        {
            NextBreakAt = at;
        }
    }

}
