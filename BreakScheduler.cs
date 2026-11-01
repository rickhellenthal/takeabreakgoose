using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace TakeABreakGoose;

public sealed record WorkSession(int IntervalMinutes, DateTime StartedAt, DateTime? EndsAt);

/// <summary>
/// Decides when the goose should show up. Reminders come from the running work session,
/// or, when no session is running and always-on is enabled, from the always-on interval.
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
    public int? CurrentInterval =>
        Session?.IntervalMinutes ?? (_settings.AlwaysOn ? _settings.AlwaysOnIntervalMinutes : null);

    public void StartSession(int intervalMinutes, int? lengthMinutes)
    {
        var now = DateTime.Now;
        Session = new WorkSession(intervalMinutes, now, lengthMinutes > 0 ? now.AddMinutes(lengthMinutes.Value) : null);
        IsPaused = false;
        Reschedule();
    }

    public void StopSession()
    {
        Session = null;
        IsPaused = false;
        Reschedule();
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
        bool alwaysOnChanged = settings.AlwaysOn != _settings.AlwaysOn
                               || settings.AlwaysOnIntervalMinutes != _settings.AlwaysOnIntervalMinutes;
        _settings = settings;
        if (Session == null && alwaysOnChanged) Reschedule();
        else StateChanged?.Invoke();
    }

    public void GooseArrived()
    {
        GooseVisiting = true;
        NextBreakAt = null;
        StateChanged?.Invoke();
    }

    public void GooseLeft(GooseOutcome outcome)
    {
        GooseVisiting = false;
        var now = DateTime.Now;
        BreakEndsAt = null;
        if (outcome == GooseOutcome.Snoozed)
        {
            SetNext(now.AddMinutes(_settings.SnoozeMinutes));
        }
        else if (CurrentInterval is int interval)
        {
            // The next reminder starts counting once the break is over.
            if (outcome == GooseOutcome.Dismissed && _settings.BreakMinutes > 0)
            {
                BreakEndsAt = now.AddMinutes(_settings.BreakMinutes);
                SetNext(BreakEndsAt.Value.AddMinutes(interval));
            }
            else
            {
                SetNext(now.AddMinutes(interval));
            }
        }
        else
        {
            SetNext(null);
        }
        StateChanged?.Invoke();
    }

    /// <summary>A one-line summary for the tray tooltip and the window header.</summary>
    public string Describe()
    {
        if (GooseVisiting) return "The goose is here!";
        if (IsPaused) return "Reminders paused";
        if (IsAway) return "Away from the keyboard";
        if (BreakEndsAt is DateTime breakEnd) return $"On a break until {breakEnd:HH:mm}";
        if (NextBreakAt is DateTime next)
        {
            string mode = Session != null ? "Work session" : _settings.AlwaysOn ? "Always on" : "Snoozed";
            return $"{mode} · next break at {next:HH:mm}";
        }
        return "No work session running";
    }

    private void Tick()
    {
        var now = DateTime.Now;

        if (Session?.EndsAt is DateTime end && now >= end && !GooseVisiting)
        {
            Session = null;
            IsPaused = false;
            Reschedule();
            SessionCompleted?.Invoke();
            return;
        }

        if (BreakEndsAt is DateTime breakEnd && now >= breakEnd) BreakEndsAt = null;

        if (_settings.AwayMinutes > 0 && !GooseVisiting && !IsPaused)
        {
            bool away = IdleTime() >= TimeSpan.FromMinutes(_settings.AwayMinutes);
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

        if (!IsAway && !IsPaused && !GooseVisiting && NextBreakAt is DateTime next && now >= next)
        {
            NextBreakAt = null;
            BreakDue?.Invoke();
        }

        StateChanged?.Invoke();
    }

    private void Reschedule()
    {
        BreakEndsAt = null;
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

    private static TimeSpan IdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
