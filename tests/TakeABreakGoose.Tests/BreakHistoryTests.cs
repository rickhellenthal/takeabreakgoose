using Xunit;
using static TakeABreakGoose.Tests.SchedulerHarness;

namespace TakeABreakGoose.Tests;

public class BreakHistoryTests
{
    private readonly SchedulerHarness _h = new();

    /// <summary>The goose comes for the break, and the user takes it.</summary>
    private void TakeTheBreak()
    {
        _h.Scheduler.GooseArrived();
        _h.Scheduler.GooseAnswered(GooseOutcome.Dismissed, startsBreak: true);
    }

    [Fact]
    public void A_new_session_has_no_breaks_yet()
    {
        _h.StartSession(intervalMinutes: 30);

        Assert.Empty(_h.Scheduler.Breaks);
        Assert.Null(_h.Scheduler.BreakStartedAt);
    }

    [Fact]
    public void A_break_runs_from_taking_it_until_ready_to_focus()
    {
        _h.StartSession(intervalMinutes: 30, breakMinutes: 5);
        _h.Work(Minutes(30));
        var start = _h.Now;
        TakeTheBreak();
        Assert.Equal(start, _h.Scheduler.BreakStartedAt);

        _h.BeAway(Minutes(7)); // a bit longer than the 5 minutes
        _h.ComeBack();
        _h.Scheduler.ReadyToFocus();

        var record = Assert.Single(_h.Scheduler.Breaks);
        Assert.Equal(new BreakRecord(start, start + Minutes(7), BreakKind.Taken), record);
        Assert.Equal(Minutes(7), record.Length);
        Assert.Null(_h.Scheduler.BreakStartedAt);
    }

    [Fact]
    public void A_break_cut_short_is_marked_as_ended_early()
    {
        _h.StartSession(intervalMinutes: 30, breakMinutes: 10);
        _h.Work(Minutes(30));
        TakeTheBreak();

        _h.BeAway(Minutes(3));
        _h.ComeBack();
        _h.Scheduler.ReadyToFocus();

        var record = Assert.Single(_h.Scheduler.Breaks);
        Assert.Equal(BreakKind.EndedEarly, record.Kind);
        Assert.Equal(Minutes(3), record.Length);
    }

    [Fact]
    public void A_break_with_extra_minutes_only_ends_early_before_the_new_end()
    {
        _h.StartSession(intervalMinutes: 30, breakMinutes: 5);
        _h.Work(Minutes(30));
        TakeTheBreak();
        _h.BeAway(Minutes(5));
        _h.Scheduler.ExtendBreak(_h.Now + Minutes(5));

        _h.BeAway(Minutes(6));
        _h.ComeBack();
        _h.Scheduler.ReadyToFocus();

        var record = Assert.Single(_h.Scheduler.Breaks);
        Assert.Equal(BreakKind.Taken, record.Kind);
        Assert.Equal(Minutes(11), record.Length);
    }

    [Fact]
    public void Being_away_while_the_goose_waited_is_a_break_from_when_the_user_left()
    {
        _h.StartSession(intervalMinutes: 30, breakMinutes: 5);
        _h.Work(Minutes(30));
        _h.Scheduler.GooseArrived();
        var left = _h.Now;
        _h.BeAway(Minutes(8));
        _h.ComeBack();

        _h.Scheduler.BreakTakenWhileAway(left);
        _h.Scheduler.ReadyToFocus(); // doesn't add a second break

        var record = Assert.Single(_h.Scheduler.Breaks);
        Assert.Equal(new BreakRecord(left, left + Minutes(8), BreakKind.Away), record);
    }

    [Fact]
    public void Being_away_for_a_break_length_during_the_countdown_is_a_break()
    {
        _h.StartSession(intervalMinutes: 30, breakMinutes: 5);
        _h.Work(Minutes(10));
        var left = _h.Now;

        _h.BeAway(Minutes(12));
        Assert.Equal(left, _h.Scheduler.AwaySince); // shown as a break in progress
        _h.ComeBack();

        Assert.Null(_h.Scheduler.AwaySince);
        var record = Assert.Single(_h.Scheduler.Breaks);
        Assert.Equal(new BreakRecord(left, left + Minutes(12), BreakKind.Away), record);
    }

    [Fact]
    public void Being_away_for_less_than_a_break_length_is_not_a_break()
    {
        _h.StartSession(intervalMinutes: 30, breakMinutes: 5);
        _h.Work(Minutes(10));

        _h.BeAway(Minutes(4));
        _h.ComeBack();

        Assert.Empty(_h.Scheduler.Breaks);
    }

    [Fact]
    public void Breaks_add_up_in_order()
    {
        _h.StartSession(intervalMinutes: 30, breakMinutes: 5);
        _h.Work(Minutes(30));
        TakeTheBreak();
        _h.BeAway(Minutes(5));
        _h.ComeBack();
        _h.Scheduler.ReadyToFocus();
        _h.Work(Minutes(10));
        _h.BeAway(Minutes(6));
        _h.ComeBack();

        Assert.Equal(new[] { BreakKind.Taken, BreakKind.Away }, _h.Scheduler.Breaks.Select(b => b.Kind));
    }

    [Fact]
    public void A_new_session_starts_with_an_empty_history()
    {
        _h.StartSession(intervalMinutes: 30, breakMinutes: 5);
        _h.Work(Minutes(10));
        _h.BeAway(Minutes(6));
        _h.ComeBack();
        Assert.Single(_h.Scheduler.Breaks);

        _h.Scheduler.StopSession();
        _h.StartSession(intervalMinutes: 30);

        Assert.Empty(_h.Scheduler.Breaks);
    }
}
