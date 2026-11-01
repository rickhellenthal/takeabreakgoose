using System;
using System.Windows.Threading;

namespace TakeABreakGoose;

/// <param name="BreakMinutes">How long each break lasts in this session.</param>
/// <param name="StartedAutomatically">Started by the "start a focus session when the app starts" setting.</param>
public sealed record WorkSession(
    int IntervalMinutes, int BreakMinutes, DateTime StartedAt, DateTime? EndsAt, bool StartedAutomatically);

/// <summary>
/// Decides when the goose should show up. Reminders only come during a focus session;
/// outside one the goose stays quiet.
/// </summary>
public sealed class BreakScheduler
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private GooseSettings _settings;

    public WorkSession? Session { get; private set; }
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

    /// <summary>
    /// The session reached its end time during Do not disturb. Do not disturb stays on, and the goose
    /// signs off as soon as it's turned off.
    /// </summary>
    public WorkSession? SignOffWaiting { get; private set; }

    /// <summary>Raised every second and whenever the state changes, so UIs can refresh countdowns.</summary>
    public event Action? StateChanged;
    public event Action? BreakDue;

    /// <summary>A session reached its end time (and Do not disturb isn't keeping the goose away).</summary>
    public event Action<WorkSession>? SessionCompleted;

    public BreakScheduler(GooseSettings settings)
    {
        _settings = settings;
        _tick.Tick += (_, _) => Tick();
        _tick.Start();
        Reschedule();
    }

    /// <summary>Minutes between reminders right now, or null when the goose should stay quiet.</summary>
    public int? CurrentInterval => Session?.IntervalMinutes;

    /// <summary>How long a break lasts right now: the session's choice, or the default from the settings.</summary>
    public int CurrentBreakMinutes => Session?.BreakMinutes ?? _settings.BreakMinutes;

    public void StartSession(int intervalMinutes, int breakMinutes, DateTime? endsAt, bool automatically = false)
    {
        Session = new WorkSession(intervalMinutes, breakMinutes, DateTime.Now, endsAt, automatically);
        SignOffWaiting = null; // a new session; the goodbye for the old one isn't needed anymore
        Reschedule();
    }

    public void StopSession()
    {
        Session = null;
        DoNotDisturb = false;
        Reschedule();
    }

    /// <summary>
    /// Only during a focus session (or while a sign-off is waiting for it). Turning it off brings
    /// whatever has been waiting: a break, or the goose saying goodbye.
    /// </summary>
    public void SetDoNotDisturb(bool on)
    {
        if (on == DoNotDisturb || (on && Session == null)) return;
        DoNotDisturb = on;
        StateChanged?.Invoke();
        if (on) return;

        if (SignOffWaiting is { } finished)
        {
            SignOffWaiting = null;
            SessionCompleted?.Invoke(finished);
        }
        else if (BreakWaiting)
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
            BreakEndsAt = now.AddMinutes(CurrentBreakMinutes);
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
        if (DoNotDisturb)
        {
            return SignOffWaiting != null ? "Do not disturb · your session is done"
                : BreakWaiting ? "Do not disturb · a break is waiting"
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

        if (Session is { EndsAt: DateTime end } finished && now >= end && !GooseVisiting)
        {
            // A break that was waiting isn't needed anymore. During Do not disturb, the goose's
            // goodbye waits until it's turned off; Do not disturb stays on until then.
            Session = null;
            Reschedule();
            if (DoNotDisturb) SignOffWaiting = finished;
            else SessionCompleted?.Invoke(finished);
            StateChanged?.Invoke();
            return;
        }

        if (!AwaitingReturn && BreakEndsAt is DateTime breakEnd && now >= breakEnd) BreakEndsAt = null;

        // While waiting for the user to come back from a break, the goose handles that itself.
        if (_settings.AwayMinutes > 0 && !GooseVisiting && !AwaitingReturn)
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

        if (!IsAway && !GooseVisiting && !AwaitingReturn && NextBreakAt is DateTime next && now >= next)
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

    private void SetNext(DateTime? at) => NextBreakAt = at;
}
