using Xunit;

namespace TakeABreakGoose.Tests;

public class UpdateScheduleTests
{
    private static readonly DateTime Start = new(2026, 11, 2, 9, 0, 0);

    [Fact]
    public void The_first_check_is_a_minute_after_starting()
    {
        Assert.False(UpdateSchedule.IsDue(Start.AddSeconds(59), Start, lastCheck: null));
        Assert.True(UpdateSchedule.IsDue(Start.AddMinutes(1), Start, lastCheck: null));
    }

    [Fact]
    public void Then_every_twelve_hours()
    {
        var last = Start.AddMinutes(1);

        Assert.False(UpdateSchedule.IsDue(last.AddHours(12).AddSeconds(-1), Start, last));
        Assert.True(UpdateSchedule.IsDue(last.AddHours(12), Start, last));
    }

    [Fact]
    public void Opening_the_window_checks_unless_it_was_done_within_the_hour()
    {
        var last = Start.AddMinutes(1);

        Assert.True(UpdateSchedule.IsDueOnOpen(Start, lastCheck: null));
        Assert.False(UpdateSchedule.IsDueOnOpen(last.AddMinutes(59), last));
        Assert.True(UpdateSchedule.IsDueOnOpen(last.AddHours(1), last));
    }
}
