using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;
using static TakeABreakGoose.Tests.SchedulerHarness;

namespace TakeABreakGoose.Tests;

/// <summary>The texts in Dutch, and what's the same in every language.</summary>
public class LanguageTests
{
    private static readonly DateTime Start = new(2026, 11, 2, 9, 0, 0);

    private static BreakRecord Break(TimeSpan length) => new(Start, Start + length, BreakKind.Taken);

    // ---- The texts ----

    [Fact]
    public void English_is_spoken_until_the_app_says_otherwise()
    {
        Assert.Equal(Languages.English, Languages.Current);
        Assert.Equal("Take a break now", Strings.TakeABreakNow);
    }

    [Fact]
    public void Switching_the_language_switches_the_texts()
    {
        using (InLanguage.Dutch())
        {
            Assert.Equal(Languages.Dutch, Languages.Current);
            Assert.Equal("Nu pauze nemen", Strings.TakeABreakNow);
        }
        Assert.Equal("Take a break now", Strings.TakeABreakNow);
    }

    [Fact]
    public void An_unknown_language_is_English()
    {
        Languages.Use("xx");
        Assert.Equal(Languages.English, Languages.Current);
    }

    public static IEnumerable<object[]> AllTexts() =>
        Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, true)!
            .Cast<DictionaryEntry>().Select(e => new object[] { (string)e.Key });

    [Theory]
    [MemberData(nameof(AllTexts))]
    public void Every_text_is_in_Dutch_with_the_same_placeholders(string name)
    {
        string english = Strings.ResourceManager.GetString(name, CultureInfo.InvariantCulture)!;
        // Only the Dutch resources themselves, not English to fill the gaps.
        string? dutch = Strings.ResourceManager.GetResourceSet(Languages.Culture(Languages.Dutch), true, false)!.GetString(name);

        Assert.False(string.IsNullOrWhiteSpace(dutch), $"{name} has no Dutch text");
        Assert.Equal(Placeholders(english), Placeholders(dutch!));
    }

    private static string[] Placeholders(string text) =>
        Regex.Matches(text, @"\{\d+(:[^}]*)?\}").Select(m => m.Value).Order().ToArray();

    // ---- Counts and plurals ----

    [Fact]
    public void The_session_summary_counts_breaks_in_Dutch_words()
    {
        using var _ = InLanguage.Dutch();

        Assert.Equal("Wel zonder pauzes deze keer. Volgende keer beter!", SessionSummary.Breaks(new List<BreakRecord>()));
        Assert.Equal("Je nam onderweg één pauze, samen ongeveer 5 minuten rust.",
            SessionSummary.Breaks(new[] { Break(Minutes(5)) }));
        Assert.Equal("Je nam onderweg drie pauzes, samen ongeveer 20 minuten rust.",
            SessionSummary.Breaks(new[] { Break(Minutes(5)), Break(Minutes(7)), Break(Minutes(8.2)) }));
        Assert.Equal("Je nam onderweg twee korte pauzes.",
            SessionSummary.Breaks(new[] { Break(TimeSpan.FromSeconds(20)), Break(TimeSpan.FromSeconds(20)) }));
        Assert.Equal("Je nam onderweg één korte pauze.",
            SessionSummary.Breaks(new[] { Break(TimeSpan.FromSeconds(20)) }));
    }

    [Fact]
    public void Longer_rests_read_naturally_in_Dutch()
    {
        using var _ = InLanguage.Dutch();

        Assert.Equal("Je nam onderweg één pauze, samen ongeveer een minuut rust.",
            SessionSummary.Breaks(new[] { Break(TimeSpan.FromSeconds(70)) }));
        Assert.Equal("Je nam onderweg twee pauzes, samen ruim een uur rust.",
            SessionSummary.Breaks(new[] { Break(Minutes(40)), Break(Minutes(30)) }));
        // "uur", not "uren": with a number, Dutch keeps the hour singular.
        Assert.Equal("Je nam onderweg 14 pauzes, samen ruim 2 uur rust.",
            SessionSummary.Breaks(Enumerable.Repeat(Break(Minutes(10)), 14).ToList()));
    }

    [Theory]
    [InlineData(134, "2 uur 14 min")]
    [InlineData(120, "2 uur")]
    [InlineData(60, "1 uur")]
    [InlineData(14, "14 min")]
    public void Durations_read_naturally_in_Dutch(int minutes, string expected)
    {
        using var _ = InLanguage.Dutch();
        Assert.Equal(expected, TimeText.Duration(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void Break_lengths_in_Dutch()
    {
        using var _ = InLanguage.Dutch();

        Assert.Equal("5 min", TimeText.BreakLength(Minutes(5)));
        Assert.Equal("20 sec", TimeText.BreakLength(TimeSpan.FromSeconds(20)));
        Assert.Equal("1 min 30 sec", TimeText.BreakLength(TimeSpan.FromSeconds(90)));
        Assert.Equal("minder dan een minuut", TimeText.Length(TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void Break_lengths_in_Dutch_words_are_singular_or_plural()
    {
        using var _ = InLanguage.Dutch();

        Assert.Equal("1 minuut", TimeText.BreakLengthInWords(Minutes(1)));
        Assert.Equal("5 minuten", TimeText.BreakLengthInWords(Minutes(5)));
        Assert.Equal("1 seconde", TimeText.BreakLengthInWords(TimeSpan.FromSeconds(1)));
        Assert.Equal("20 seconden", TimeText.BreakLengthInWords(TimeSpan.FromSeconds(20)));
        Assert.Equal("1 minuut en 30 seconden", TimeText.BreakLengthInWords(TimeSpan.FromSeconds(90)));
    }

    [Fact]
    public void Clock_times_stay_on_the_24_hour_clock()
    {
        using var _ = InLanguage.Dutch();

        var now = new DateTime(2026, 11, 2, 23, 0, 0);
        Assert.Equal("om 23:30", TimeText.End(now.AddMinutes(30), now));
        Assert.Equal("morgen om 01:00", TimeText.End(now.AddHours(2), now));
    }

    [Fact]
    public void The_status_is_in_Dutch()
    {
        using var _ = InLanguage.Dutch();
        var h = new SchedulerHarness();

        Assert.Equal("Geen focussessie bezig", h.Scheduler.Describe());
        h.StartSession(intervalMinutes: 30);
        Assert.Equal("Focussessie · volgende pauze om 09:30", h.Scheduler.Describe());
        h.Scheduler.SetDoNotDisturb(true);
        Assert.Equal("In gesprek · volgende pauze om 09:30", h.Scheduler.Describe(inCall: true));
    }

    [Fact]
    public void Outfits_have_Dutch_names()
    {
        using var _ = InLanguage.Dutch();

        Assert.Equal("Feesthoedje", GooseOutfits.Name(GooseOutfit.PartyHat));
        Assert.All(GooseOutfits.All, outfit => Assert.NotEmpty(GooseOutfits.Description(outfit)));
    }
}
