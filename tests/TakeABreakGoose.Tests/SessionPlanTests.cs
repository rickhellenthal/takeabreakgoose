using Xunit;

namespace TakeABreakGoose.Tests;

public class SessionPlanTests
{
    private static readonly DateTime Morning = new(2026, 11, 2, 9, 0, 0);
    private static readonly DateTime LateEvening = new(2026, 11, 2, 23, 0, 0);

    [Fact]
    public void Without_an_end_time_the_session_runs_until_stopped()
    {
        var plan = new SessionPlan(30, 5, SessionEndMode.None, 120, new TimeSpan(17, 0, 0));

        Assert.Null(plan.EndsAt(Morning));
    }

    [Fact]
    public void End_after_adds_the_length_to_now()
    {
        var plan = new SessionPlan(30, 5, SessionEndMode.After, 150, new TimeSpan(17, 0, 0));

        Assert.Equal(Morning.AddMinutes(150), plan.EndsAt(Morning));
    }

    [Fact]
    public void End_at_a_time_later_today_is_today()
    {
        var plan = new SessionPlan(30, 5, SessionEndMode.At, 120, new TimeSpan(17, 0, 0));

        Assert.Equal(new DateTime(2026, 11, 2, 17, 0, 0), plan.EndsAt(Morning));
    }

    [Fact]
    public void End_at_a_time_that_has_passed_today_is_tomorrow()
    {
        var plan = new SessionPlan(30, 5, SessionEndMode.At, 120, new TimeSpan(1, 0, 0));

        Assert.Equal(new DateTime(2026, 11, 3, 1, 0, 0), plan.EndsAt(LateEvening));
    }

    [Theory]
    [InlineData("17:00", 17, 0)]
    [InlineData("17.00", 17, 0)]
    [InlineData("1700", 17, 0)]
    [InlineData("930", 9, 30)]
    [InlineData("9:30", 9, 30)]
    [InlineData("17", 17, 0)]
    [InlineData(" 08:05 ", 8, 5)]
    [InlineData("0:00", 0, 0)]
    public void Times_are_read_the_way_people_type_them(string text, int hours, int minutes)
    {
        Assert.True(SessionPlan.TryParseTimeOfDay(text, out var time));
        Assert.Equal(new TimeSpan(hours, minutes, 0), time);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("17:60")]
    [InlineData("24")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1:2:3")]
    [InlineData("-1")]
    public void Nonsense_times_are_refused(string text)
    {
        Assert.False(SessionPlan.TryParseTimeOfDay(text, out _));
    }
}
