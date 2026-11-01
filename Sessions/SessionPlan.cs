using System;

namespace TakeABreakGoose;

public enum SessionEndMode { None, After, At }

/// <summary>What the user chose when starting a focus session.</summary>
/// <param name="BreakMinutes">How long each break lasts in this session.</param>
/// <param name="LengthMinutes">Used when ending after a while.</param>
/// <param name="EndTime">Time of day, used when ending at a clock time.</param>
public sealed record SessionPlan(int IntervalMinutes, int BreakMinutes, SessionEndMode EndMode, int LengthMinutes, TimeSpan EndTime)
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

    /// <summary>Reads a time of day the way people type it: 17:00, 17.00, 1700, 9:30 or just 17.</summary>
    public static bool TryParseTimeOfDay(string text, out TimeSpan time)
    {
        time = default;
        string t = text.Trim().Replace('.', ':');
        int hours, minutes = 0;
        if (t.Contains(':'))
        {
            var parts = t.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out hours) || !int.TryParse(parts[1], out minutes)) return false;
        }
        else if (t.Length is 3 or 4 && int.TryParse(t, out int hhmm))
        {
            hours = hhmm / 100;
            minutes = hhmm % 100;
        }
        else if (!int.TryParse(t, out hours))
        {
            return false;
        }
        if (hours is < 0 or > 23 || minutes is < 0 or > 59) return false;
        time = new TimeSpan(hours, minutes, 0);
        return true;
    }
}
