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
    [InlineData(14, 37, 0, "14:45")]
    [InlineData(14, 30, 30, "14:45")]   // half past is already gone
    [InlineData(14, 45, 0, "15:00")]   // never right now
    [InlineData(0, 0, 0, "00:15")]
    [InlineData(23, 50, 0, "00:00")]   // midnight, which is tomorrow
    public void Opening_the_end_time_suggests_the_next_quarter_hour(int hour, int minute, int second, string expected)
    {
        var now = new DateTime(2026, 11, 2, hour, minute, second);

        Assert.Equal(TimeSpan.Parse(expected), SessionPlan.NextQuarterHour(now));
    }

    [Fact]
    public void The_suggested_end_time_is_always_in_the_future_today_or_just_after_midnight()
    {
        for (var now = Morning.Date; now < Morning.Date.AddDays(1); now = now.AddSeconds(30))
        {
            var end = SessionPlan.NextOccurrence(SessionPlan.NextQuarterHour(now), now);
            Assert.InRange(end - now, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(16));
        }
    }

    [Theory]
    [InlineData("17:00", 15, "17:15")]
    [InlineData("17:00", -15, "16:45")]
    [InlineData("12:00", -1, "11:59")]
    [InlineData("23:50", 15, "00:05")]
    [InlineData("00:05", -15, "23:50")]
    public void Stepping_the_end_time_goes_round_the_clock(string time, int minutes, string expected)
    {
        Assert.Equal(TimeSpan.Parse(expected), SessionPlan.AddToTimeOfDay(TimeSpan.Parse(time), minutes));
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
