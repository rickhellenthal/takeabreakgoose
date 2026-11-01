using System;

namespace TakeABreakGoose;

public enum SessionEndMode { None, After, At }

/// <summary>What the user chose when starting a focus session.</summary>
/// <param name="LengthMinutes">Used when ending after a while.</param>
/// <param name="EndTime">Time of day, used when ending at a clock time.</param>
public sealed record SessionPlan(int IntervalMinutes, SessionEndMode EndMode, int LengthMinutes, TimeSpan EndTime)
{
    /// <summary>When the session ends, or null if it runs until it's stopped.</summary>
    public DateTime? EndsAt(DateTime now) => EndMode switch
    {
        SessionEndMode.After => now.AddMinutes(LengthMinutes),
        SessionEndMode.At => NextOccurrence(EndTime, now),
        _ => null,
    };

    /// <summary>Today at the given time, or tomorrow if that has already passed (e.g. 01:00 when it's 23:00).</summary>
    public static DateTime NextOccurrence(TimeSpan timeOfDay, DateTime now)
    {
        var today = now.Date + timeOfDay;
        return today > now ? today : today.AddDays(1);
    }
}
