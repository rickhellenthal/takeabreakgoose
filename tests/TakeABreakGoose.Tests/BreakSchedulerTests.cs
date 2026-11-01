using Xunit;
using static TakeABreakGoose.Tests.SchedulerHarness;

namespace TakeABreakGoose.Tests;

public class BreakSchedulerTests
{
    private readonly SchedulerHarness _h = new();

    // ---- The basics ----

    [Fact]
    public void Starting_a_session_schedules_the_first_break_one_interval_later()
    {
        _h.StartSession(intervalMinutes: 30);

        Assert.Equal(_h.Now + Minutes(30), _h.Scheduler.NextBreakAt);
        Assert.Equal("Focus session · next break at 09:30", _h.Scheduler.Describe());
    }

    [Fact]
    public void The_goose_comes_when_the_interval_is_over_and_not_a_second_earlier()
    {
        _h.StartSession(intervalMinutes: 30);

        _h.Work(Minutes(30) - TimeSpan.FromSeconds(1));
        Assert.Equal(0, _h.BreaksDue);

        _h.Work(TimeSpan.FromSeconds(1));
        Assert.Equal(1, _h.BreaksDue);
    }

    [Fact]
    public void Outside_a_session_the_goose_stays_quiet()
    {
        _h.Work(Minutes(120));

        Assert.Equal(0, _h.BreaksDue);
        Assert.Null(_h.Scheduler.NextBreakAt);
        Assert.Equal("No focus session running", _h.Scheduler.Describe());
    }

    // ---- Answering the goose ----

    [Fact]
    public void After_a_break_the_next_countdown_only_starts_once_the_user_is_ready_to_focus()
    {
        _h.StartSession(intervalMinutes: 30);
        _h.Work(Minutes(30));
        _h.Scheduler.GooseArrived();

        _h.Scheduler.GooseAnswered(GooseOutcome.Dismissed, startsBreak: true);
        Assert.True(_h.Scheduler.AwaitingReturn);
        Assert.Null(_h.Scheduler.NextBreakAt);
        Assert.Equal(_h.Now + Minutes(5), _h.Scheduler.BreakEndsAt);

        _h.Work(Minutes(60)); // a long break: still no new reminder
        Assert.Equal(1, _h.BreaksDue);

        _h.Scheduler.ReadyToFocus();
        Assert.False(_h.Scheduler.AwaitingReturn);
        Assert.Equal(_h.Now + Minutes(30), _h.Scheduler.NextBreakAt);
    }

    [Fact]
    public void Snoozing_brings_the_goose_back_after_the_snooze_length()
    {
        _h.StartSession(intervalMinutes: 30);
        _h.Work(Minutes(30));
        _h.Scheduler.GooseArrived();

        _h.Scheduler.GooseAnswered(GooseOutcome.Snoozed, startsBreak: false);
        Assert.Equal(_h.Now + Minutes(5), _h.Scheduler.NextBreakAt);

        _h.Work(Minutes(5));
        Assert.Equal(2, _h.BreaksDue);
    }

    [Fact]
    public void Being_away_while_the_goose_waited_counts_as_the_break()
    {
        _h.StartSession(intervalMinutes: 30);
        _h.Work(Minutes(30));
        _h.Scheduler.GooseArrived();

        _h.Scheduler.BreakTakenWhileAway();
        Assert.True(_h.Scheduler.AwaitingReturn);
        Assert.Null(_h.Scheduler.NextBreakAt);
        Assert.Equal("Break's over, waiting for you", _h.Scheduler.Describe());

        _h.Scheduler.ReadyToFocus();
        Assert.Equal(_h.Now + Minutes(30), _h.Scheduler.NextBreakAt);
    }

    // ---- Being away during the countdown ----

    [Fact]
    public void Being_away_for_a_break_length_counts_as_a_break_and_restarts_the_countdown()
    {
        _h.StartSession(intervalMinutes: 15, breakMinutes: 5);
        _h.Work(Minutes(10));

        _h.BeAway(Minutes(6)); // the break would have been due 5 minutes into this
        Assert.True(_h.Scheduler.IsAway);
        Assert.Equal(0, _h.BreaksDue);

        _h.ComeBack();
        Assert.False(_h.Scheduler.IsAway);
        Assert.Equal(_h.Now + Minutes(15), _h.Scheduler.NextBreakAt);
    }

    [Fact]
    public void Being_away_for_less_than_a_break_length_does_not_count()
    {
        _h.StartSession(intervalMinutes: 15, breakMinutes: 5);
        var firstBreak = _h.Scheduler.NextBreakAt;
        _h.Work(Minutes(5));

        _h.BeAway(Minutes(4));
        _h.ComeBack();

        Assert.False(_h.Scheduler.IsAway);
        Assert.Equal(firstBreak, _h.Scheduler.NextBreakAt);
    }

    [Fact]
    public void The_away_threshold_is_the_sessions_own_break_length()
    {
        _h.StartSession(intervalMinutes: 30, breakMinutes: 10);
        var firstBreak = _h.Scheduler.NextBreakAt;
        _h.Work(Minutes(5));

        _h.BeAway(Minutes(6)); // longer than the default 5, shorter than this session's 10
        _h.ComeBack();

        Assert.Equal(10, _h.Scheduler.CurrentBreakMinutes);
        Assert.Equal(firstBreak, _h.Scheduler.NextBreakAt);
    }

    // ---- Do not disturb ----

    [Fact]
    public void A_break_that_comes_due_during_do_not_disturb_waits_until_it_is_turned_off()
    {
        _h.StartSession(intervalMinutes: 30);
        _h.Scheduler.SetDoNotDisturb(true);

        _h.Work(Minutes(45));
        Assert.Equal(0, _h.BreaksDue);
        Assert.True(_h.Scheduler.BreakWaiting);
        Assert.Equal("Do not disturb · a break is waiting", _h.Scheduler.Describe());

        _h.Scheduler.SetDoNotDisturb(false);
        Assert.Equal(1, _h.BreaksDue);
        Assert.False(_h.Scheduler.BreakWaiting);
    }

    [Fact]
    public void Do_not_disturb_keeps_the_timer_running()
    {
        _h.StartSession(intervalMinutes: 30);
        var firstBreak = _h.Scheduler.NextBreakAt;

        _h.Scheduler.SetDoNotDisturb(true);
        _h.Work(Minutes(10));

        Assert.Equal(firstBreak, _h.Scheduler.NextBreakAt);
    }

    [Fact]
    public void Do_not_disturb_is_only_available_during_a_session()
    {
        _h.Scheduler.SetDoNotDisturb(true);

        Assert.False(_h.Scheduler.DoNotDisturb);
    }

    // ---- The end of a session ----

    [Fact]
    public void A_session_with_an_end_time_completes_at_that_time()
    {
        _h.StartSession(intervalMinutes: 30, endsAfter: Minutes(90));

        _h.Work(Minutes(90) - TimeSpan.FromSeconds(1));
        Assert.Empty(_h.CompletedSessions);

        _h.Work(TimeSpan.FromSeconds(1));
        Assert.Single(_h.CompletedSessions);
        Assert.Null(_h.Scheduler.Session);
    }

    [Fact]
    public void A_session_ending_during_do_not_disturb_signs_off_once_it_is_turned_off()
    {
        _h.StartSession(intervalMinutes: 30, endsAfter: Minutes(60));
        _h.Scheduler.SetDoNotDisturb(true);

        _h.Work(Minutes(60));
        Assert.Empty(_h.CompletedSessions);
        Assert.NotNull(_h.Scheduler.SignOffWaiting);
        Assert.True(_h.Scheduler.DoNotDisturb); // stays on until the user turns it off
        Assert.Equal("Do not disturb · your session is done", _h.Scheduler.Describe());

        _h.Scheduler.SetDoNotDisturb(false);
        Assert.Single(_h.CompletedSessions);
        Assert.Null(_h.Scheduler.SignOffWaiting);
    }

    [Fact]
    public void Stopping_during_a_break_leaves_nothing_behind_for_the_next_session()
    {
        _h.StartSession(intervalMinutes: 30);
        _h.Work(Minutes(30));
        _h.Scheduler.GooseArrived();
        _h.Scheduler.GooseAnswered(GooseOutcome.Dismissed, startsBreak: true);

        _h.Scheduler.StopSession();
        Assert.False(_h.Scheduler.AwaitingReturn);
        Assert.False(_h.Scheduler.GooseVisiting);
        Assert.Null(_h.Scheduler.BreakEndsAt);

        _h.StartSession(intervalMinutes: 30);
        _h.Work(Minutes(30));
        Assert.Equal(2, _h.BreaksDue);
    }
}
