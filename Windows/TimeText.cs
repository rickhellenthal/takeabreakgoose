using System;

namespace TakeABreakGoose;

/// <summary>How times and durations are written in the app window.</summary>
public static class TimeText
{
    /// <summary>"4:59", or "1:04:59" from an hour up.</summary>
    public static string Countdown(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time:mm\\:ss}"
            : time.ToString(@"m\:ss");
    }

    /// <summary>"at 17:00", or "tomorrow at 01:00" when it's past midnight.</summary>
    public static string End(DateTime end, DateTime now) =>
        end.Date > now.Date ? $"tomorrow at {end:HH:mm}" : $"at {end:HH:mm}";

    /// <summary>"2 h 14 min", "2 h" or "14 min", rounded up to the next minute.</summary>
    public static string Duration(TimeSpan time)
    {
        int minutes = (int)Math.Ceiling(Math.Max(0, time.TotalMinutes));
        int hours = minutes / 60;
        minutes %= 60;
        if (hours == 0) return $"{minutes} min";
        return minutes == 0 ? $"{hours} h" : $"{hours} h {minutes} min";
    }
}
