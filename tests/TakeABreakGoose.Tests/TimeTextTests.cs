using Xunit;

namespace TakeABreakGoose.Tests;

public class TimeTextTests
{
    [Fact]
    public void Countdowns_show_minutes_and_seconds()
    {
        Assert.Equal("4:59", TimeText.Countdown(new TimeSpan(0, 4, 59)));
        Assert.Equal("0:05", TimeText.Countdown(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Countdowns_from_an_hour_up_show_hours_too()
    {
        Assert.Equal("1:04:59", TimeText.Countdown(new TimeSpan(1, 4, 59)));
    }

    [Fact]
    public void Countdowns_never_go_below_zero()
    {
        Assert.Equal("0:00", TimeText.Countdown(TimeSpan.FromSeconds(-3)));
    }

    [Theory]
    [InlineData(134, "2 h 14 min")]
    [InlineData(120, "2 h")]
    [InlineData(14, "14 min")]
    [InlineData(0, "0 min")]
    public void Durations_read_naturally(int minutes, string expected)
    {
        Assert.Equal(expected, TimeText.Duration(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void Durations_round_up_to_the_next_minute()
    {
        Assert.Equal("1 min", TimeText.Duration(TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void Lengths_round_to_the_nearest_minute()
    {
        Assert.Equal("under a minute", TimeText.Length(TimeSpan.FromSeconds(20)));
        Assert.Equal("1 min", TimeText.Length(TimeSpan.FromSeconds(80)));
        Assert.Equal("2 min", TimeText.Length(TimeSpan.FromSeconds(90)));
        Assert.Equal("1 h 5 min", TimeText.Length(TimeSpan.FromMinutes(65.2)));
    }

    [Fact]
    public void Break_lengths_show_minutes_and_seconds()
    {
        Assert.Equal("5 min", TimeText.BreakLength(TimeSpan.FromMinutes(5)));
        Assert.Equal("20 sec", TimeText.BreakLength(TimeSpan.FromSeconds(20)));
        Assert.Equal("1 min 30 sec", TimeText.BreakLength(TimeSpan.FromSeconds(90)));

        Assert.Equal("5 minutes", TimeText.BreakLengthInWords(TimeSpan.FromMinutes(5)));
        Assert.Equal("20 seconds", TimeText.BreakLengthInWords(TimeSpan.FromSeconds(20)));
        Assert.Equal("1 minute and 1 second", TimeText.BreakLengthInWords(TimeSpan.FromSeconds(61)));
    }

    [Fact]
    public void End_times_after_midnight_say_tomorrow()
    {
        var now = new DateTime(2026, 11, 2, 23, 0, 0);

        Assert.Equal("at 23:30", TimeText.End(now.AddMinutes(30), now));
        Assert.Equal("tomorrow at 01:00", TimeText.End(now.AddHours(2), now));
    }
}
