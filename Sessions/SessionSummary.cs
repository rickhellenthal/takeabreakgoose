using System;
using System.Collections.Generic;
using System.Linq;

namespace TakeABreakGoose;

/// <summary>What the goose says about a finished session's breaks, in words rather than numbers.</summary>
public static class SessionSummary
{
    private static readonly string[] NumberWords =
        { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve" };

    /// <summary>
    /// "You took three breaks along the way, about 20 minutes of rest.", or a nudge when there
    /// were none.
    /// </summary>
    public static string Breaks(IReadOnlyList<BreakRecord> breaks)
    {
        if (breaks.Count == 0) return "No breaks this time, though. Let's fix that next time!";

        string count = breaks.Count < NumberWords.Length ? NumberWords[breaks.Count] : breaks.Count.ToString();
        string noun = breaks.Count == 1 ? "break" : "breaks";
        var rest = breaks.Aggregate(TimeSpan.Zero, (sum, b) => sum + b.Length);
        return rest < TimeSpan.FromMinutes(1)
            ? $"You took {count} quick {noun} along the way."
            : $"You took {count} {noun} along the way, {Rest(rest)} of rest.";
    }

    /// <summary>"about a minute", "about 20 minutes", "over an hour" or "over 2 hours".</summary>
    private static string Rest(TimeSpan rest)
    {
        int minutes = (int)Math.Round(rest.TotalMinutes, MidpointRounding.AwayFromZero);
        if (minutes < 60) return minutes == 1 ? "about a minute" : $"about {minutes} minutes";
        int hours = (int)rest.TotalHours;
        return hours == 1 ? "over an hour" : $"over {hours} hours";
    }
}
