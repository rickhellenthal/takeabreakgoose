namespace TakeABreakGoose.Tests;

/// <summary>
/// A scheduler on a fake clock: tests let time pass second by second, with the user either at the
/// computer or away from it, and see what the scheduler does.
/// </summary>
internal sealed class SchedulerHarness : IClock, IUserActivity
{
    public DateTime Now { get; private set; } = new(2026, 11, 2, 9, 0, 0);

    /// <summary>How long the mouse and keyboard haven't been used.</summary>
    public TimeSpan Idle { get; private set; }

    public GooseSettings Settings { get; } = new() { LastSessionBreakSeconds = 5 * 60, SnoozeMinutes = 5 };
    public BreakScheduler Scheduler { get; }

    public int BreaksDue { get; private set; }
    public List<WorkSession> CompletedSessions { get; } = new();

    public SchedulerHarness()
    {
        Scheduler = new BreakScheduler(Settings, this, this);
        Scheduler.BreakDue += () => BreaksDue++;
        Scheduler.SessionCompleted += session => CompletedSessions.Add(session);
    }

    /// <summary>Starts a session the way the start card does (with breaks in minutes, or an exact <paramref name="breakLength"/>).</summary>
    public void StartSession(int intervalMinutes, int breakMinutes = 5, TimeSpan? endsAfter = null, TimeSpan? breakLength = null) =>
        Scheduler.StartSession(intervalMinutes, breakLength ?? Minutes(breakMinutes),
            endsAfter is { } length ? Now + length : null);

    /// <summary>Time passes while the user works at the computer.</summary>
    public void Work(TimeSpan time) => Pass(time, away: false);

    /// <summary>Time passes while nobody touches the mouse or keyboard.</summary>
    public void BeAway(TimeSpan time) => Pass(time, away: true);

    /// <summary>The user touches the mouse or keyboard again.</summary>
    public void ComeBack()
    {
        Idle = TimeSpan.Zero;
        Scheduler.Tick();
    }

    private void Pass(TimeSpan time, bool away)
    {
        var step = TimeSpan.FromSeconds(1);
        for (var passed = TimeSpan.Zero; passed < time; passed += step)
        {
            Now += step;
            Idle = away ? Idle + step : TimeSpan.Zero;
            Scheduler.Tick();
        }
    }

    TimeSpan IUserActivity.IdleTime() => Idle;

    public static TimeSpan Minutes(double minutes) => TimeSpan.FromMinutes(minutes);
}
