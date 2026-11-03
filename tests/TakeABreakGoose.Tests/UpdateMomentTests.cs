using Xunit;
using static TakeABreakGoose.Tests.SchedulerHarness;

namespace TakeABreakGoose.Tests;

public class UpdateMomentTests
{
    private readonly SchedulerHarness _h = new();

    private bool IsGood(bool gooseOnScreen = false, bool inCall = false) =>
        UpdateMoment.IsGood(_h.Scheduler, gooseOnScreen, inCall);

    [Fact]
    public void Without_a_session_any_moment_is_fine()
    {
        Assert.True(IsGood());
    }

    [Fact]
    public void While_focusing_between_breaks_is_fine()
    {
        _h.StartSession(intervalMinutes: 30);
        _h.Work(Minutes(10));

        Assert.True(IsGood());
    }

    [Fact]
    public void Not_while_a_goose_is_on_screen()
    {
        Assert.False(IsGood(gooseOnScreen: true));
    }

    [Fact]
    public void Not_during_a_call()
    {
        Assert.False(IsGood(inCall: true));
    }

    [Fact]
    public void Not_once_a_break_is_due_and_the_goose_is_coming()
    {
        _h.StartSession(intervalMinutes: 30);
        _h.Work(Minutes(30));
        _h.Scheduler.GooseArrived();

        Assert.False(IsGood());
    }

    [Fact]
    public void Not_during_a_break_and_not_until_the_user_is_back_to_focus()
    {
        _h.StartSession(intervalMinutes: 30);
        _h.Work(Minutes(30));
        _h.Scheduler.GooseArrived();
        _h.Scheduler.GooseAnswered(GooseOutcome.Dismissed, startsBreak: true);
        Assert.False(IsGood());

        _h.Work(Minutes(10)); // the break is over, but the user hasn't said they're ready yet
        Assert.False(IsGood());

        _h.Scheduler.ReadyToFocus();
        Assert.True(IsGood());
    }

    [Fact]
    public void Not_while_a_break_waits_for_Do_not_disturb_to_end()
    {
        _h.StartSession(intervalMinutes: 30);
        _h.Scheduler.SetDoNotDisturb(true);
        _h.Work(Minutes(30));

        Assert.True(_h.Scheduler.BreakWaiting);
        Assert.False(IsGood());
    }

    [Fact]
    public void Not_while_the_goodbye_waits_for_Do_not_disturb_to_end()
    {
        _h.StartSession(intervalMinutes: 60, endsAfter: Minutes(30));
        _h.Scheduler.SetDoNotDisturb(true);
        _h.Work(Minutes(30));

        Assert.NotNull(_h.Scheduler.SignOffWaiting);
        Assert.False(IsGood());
    }
}
