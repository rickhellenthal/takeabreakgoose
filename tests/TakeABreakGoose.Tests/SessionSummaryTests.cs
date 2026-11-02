using Xunit;
using static TakeABreakGoose.Tests.SchedulerHarness;

namespace TakeABreakGoose.Tests;

public class SessionSummaryTests
{
    private static readonly DateTime Start = new(2026, 11, 2, 9, 0, 0);

    private static BreakRecord Break(TimeSpan length) => new(Start, Start + length, BreakKind.Taken);

    [Fact]
    public void No_breaks_gets_a_nudge()
    {
        Assert.Equal("No breaks this time, though. Let's fix that next time!", SessionSummary.Breaks(new List<BreakRecord>()));
    }

    [Fact]
    public void Breaks_are_counted_in_words_with_the_rest_they_added_up_to()
    {
        Assert.Equal("You took one break along the way, about 5 minutes of rest.",
            SessionSummary.Breaks(new[] { Break(Minutes(5)) }));
        Assert.Equal("You took three breaks along the way, about 20 minutes of rest.",
            SessionSummary.Breaks(new[] { Break(Minutes(5)), Break(Minutes(7)), Break(Minutes(8.2)) }));
    }

    [Fact]
    public void Very_short_breaks_are_quick_ones()
    {
        Assert.Equal("You took two quick breaks along the way.",
            SessionSummary.Breaks(new[] { Break(TimeSpan.FromSeconds(20)), Break(TimeSpan.FromSeconds(20)) }));
    }

    [Fact]
    public void Longer_rests_are_rounded_to_hours()
    {
        Assert.Equal("You took one break along the way, about a minute of rest.",
            SessionSummary.Breaks(new[] { Break(TimeSpan.FromSeconds(70)) }));
        Assert.Equal("You took two breaks along the way, over an hour of rest.",
            SessionSummary.Breaks(new[] { Break(Minutes(40)), Break(Minutes(30)) }));
        Assert.Equal("You took 14 breaks along the way, over 2 hours of rest.",
            SessionSummary.Breaks(Enumerable.Repeat(Break(Minutes(10)), 14).ToList()));
    }
}
