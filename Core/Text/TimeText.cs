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

    /// <summary>
    /// How long something took: "under a minute", or "14 min" / "1 h 5 min" rounded to the nearest
    /// minute, so it matches the clock times shown next to it.
    /// </summary>
    public static string Length(TimeSpan time) =>
        time < TimeSpan.FromSeconds(30)
            ? "under a minute"
            : Duration(TimeSpan.FromMinutes(Math.Round(time.TotalMinutes, MidpointRounding.AwayFromZero)));

    /// <summary>A break length, short: "5 min", "20 sec" or "1 min 30 sec".</summary>
    public static string BreakLength(TimeSpan length)
    {
        int minutes = (int)length.TotalMinutes, seconds = length.Seconds;
        if (minutes == 0) return $"{seconds} sec";
        return seconds == 0 ? $"{minutes} min" : $"{minutes} min {seconds} sec";
    }

    /// <summary>A break length, written out: "5 minutes", "20 seconds" or "1 minute and 30 seconds".</summary>
    public static string BreakLengthInWords(TimeSpan length)
    {
        int minutes = (int)length.TotalMinutes, seconds = length.Seconds;
        string Plural(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";
        if (minutes == 0) return Plural(seconds, "second");
        return seconds == 0 ? Plural(minutes, "minute") : $"{Plural(minutes, "minute")} and {Plural(seconds, "second")}";
    }

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
