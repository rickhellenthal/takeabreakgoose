using System;
using System.Collections.Generic;
using System.Linq;

namespace TakeABreakGoose;

/// <summary>What the goose says about a finished session's breaks, in words rather than numbers.</summary>
public static class SessionSummary
{
    /// <summary>
    /// "You took three breaks along the way, about 20 minutes of rest.", or a nudge when there
    /// were none.
    /// </summary>
    public static string Breaks(IReadOnlyList<BreakRecord> breaks)
    {
        if (breaks.Count == 0) return Strings.SummaryNoBreaks;

        var numberWords = Languages.Lines(Strings.NumberWords);
        string count = breaks.Count < numberWords.Length ? numberWords[breaks.Count] : breaks.Count.ToString();
        string noun = BreakNoun(breaks.Count);
        var rest = breaks.Aggregate(TimeSpan.Zero, (sum, b) => sum + b.Length);
        return rest < TimeSpan.FromMinutes(1)
            ? string.Format(Strings.SummaryQuickBreaks, count, noun)
            : string.Format(Strings.SummaryBreaks, count, noun, Rest(rest));
    }

    /// <summary>"break" or "breaks".</summary>
    public static string BreakNoun(int count) => count == 1 ? Strings.BreakOne : Strings.BreakMany;

    /// <summary>"about a minute", "about 20 minutes", "over an hour" or "over 2 hours".</summary>
    private static string Rest(TimeSpan rest)
    {
        int minutes = (int)Math.Round(rest.TotalMinutes, MidpointRounding.AwayFromZero);
        if (minutes < 60) return minutes == 1 ? Strings.SummaryAboutAMinute : string.Format(Strings.SummaryAboutMinutes, minutes);
        int hours = (int)rest.TotalHours;
        return hours == 1 ? Strings.SummaryOverAnHour : string.Format(Strings.SummaryOverHours, hours);
    }
}
