using Xunit;
using static TakeABreakGoose.Tests.SchedulerHarness;

namespace TakeABreakGoose.Tests;

public class SessionSnapshotTests
{
    private readonly SchedulerHarness _before = new();

    /// <summary>A 30-minute session that has had one break and is 10 minutes into the next interval.</summary>
    private SessionSnapshot SessionWithABreakBehindIt()
    {
        _before.StartSession(intervalMinutes: 30, endsAfter: Minutes(180));
        _before.Work(Minutes(30));
        _before.Scheduler.GooseArrived();
        _before.Scheduler.GooseAnswered(GooseOutcome.Dismissed, startsBreak: true);
        _before.Work(Minutes(5));
        _before.Scheduler.ReadyToFocus();
        _before.Work(Minutes(10));
        return _before.Scheduler.Snapshot()!;
    }

    /// <summary>A new scheduler, as in the restarted app, that picks up the snapshot when it was taken.</summary>
    private static SchedulerHarness AfterRestart(SessionSnapshot snapshot)
    {
        var after = new SchedulerHarness();
        after.Work(snapshot.SavedAt - after.Now);
        after.Scheduler.Resume(snapshot);
        return after;
    }

    [Fact]
    public void Without_a_session_there_is_nothing_to_keep()
    {
        Assert.Null(_before.Scheduler.Snapshot());
    }

    [Fact]
    public void After_the_restart_the_session_carries_on_where_it_was()
    {
        var snapshot = SessionWithABreakBehindIt();
        var after = AfterRestart(snapshot);

        Assert.Equal(_before.Scheduler.Session, after.Scheduler.Session);
        Assert.Equal(_before.Scheduler.NextBreakAt, after.Scheduler.NextBreakAt);
        Assert.Equal(_before.Scheduler.Breaks, after.Scheduler.Breaks);
    }

    [Fact]
    public void The_next_break_comes_when_it_was_due()
    {
        var snapshot = SessionWithABreakBehindIt();
        var after = AfterRestart(snapshot);

        after.Work(Minutes(20) - TimeSpan.FromSeconds(1));
        Assert.Equal(0, after.BreaksDue);
        after.Work(TimeSpan.FromSeconds(1));
        Assert.Equal(1, after.BreaksDue);
    }

    [Fact]
    public void Do_not_disturb_stays_on()
    {
        _before.StartSession(intervalMinutes: 30);
        _before.Scheduler.SetDoNotDisturb(true);
        var after = AfterRestart(_before.Scheduler.Snapshot()!);

        Assert.True(after.Scheduler.DoNotDisturb);
    }

    [Fact]
    public void Survives_being_written_down_and_read_back()
    {
        var snapshot = SessionWithABreakBehindIt();

        var read = SessionSnapshot.FromJson(snapshot.ToJson())!;

        Assert.Equal(snapshot.Session, read.Session);
        Assert.Equal(snapshot.NextBreakAt, read.NextBreakAt);
        Assert.Equal(snapshot.Breaks, read.Breaks);
        Assert.Equal(snapshot.SavedAt, read.SavedAt);
    }

    [Fact]
    public void Is_only_picked_up_within_ten_minutes()
    {
        var snapshot = SessionWithABreakBehindIt();

        Assert.True(snapshot.IsFresh(snapshot.SavedAt.AddMinutes(10)));
        Assert.False(snapshot.IsFresh(snapshot.SavedAt.AddMinutes(10).AddSeconds(1)));
        Assert.False(snapshot.IsFresh(snapshot.SavedAt.AddMinutes(-1)));
    }

    [Fact]
    public void Text_that_is_not_a_snapshot_gives_nothing()
    {
        Assert.Null(SessionSnapshot.FromJson("{ not json"));
    }
}
