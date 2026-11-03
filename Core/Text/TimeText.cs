using System;

namespace TakeABreakGoose;

/// <summary>How times and durations are written in the app window, in the app's language.</summary>
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
        string.Format(end.Date > now.Date ? Strings.TimeTomorrowAt : Strings.TimeAt, end);

    /// <summary>
    /// How long something took: "under a minute", or "14 min" / "1 h 5 min" rounded to the nearest
    /// minute, so it matches the clock times shown next to it.
    /// </summary>
    public static string Length(TimeSpan time) =>
        time < TimeSpan.FromSeconds(30)
            ? Strings.TimeUnderAMinute
            : Duration(TimeSpan.FromMinutes(Math.Round(time.TotalMinutes, MidpointRounding.AwayFromZero)));

    /// <summary>A break length, short: "5 min", "20 sec" or "1 min 30 sec".</summary>
    public static string BreakLength(TimeSpan length)
    {
        int minutes = (int)length.TotalMinutes, seconds = length.Seconds;
        if (minutes == 0) return string.Format(Strings.TimeSeconds, seconds);
        return seconds == 0 ? Minutes(minutes) : string.Format(Strings.TimeMinutesSeconds, minutes, seconds);
    }

    /// <summary>A break length, written out: "5 minutes", "20 seconds" or "1 minute and 30 seconds".</summary>
    public static string BreakLengthInWords(TimeSpan length)
    {
        int minutes = (int)length.TotalMinutes, seconds = length.Seconds;
        string Seconds(int n) => string.Format(n == 1 ? Strings.WordsSecondOne : Strings.WordsSecondMany, n);
        string Minutes(int n) => string.Format(n == 1 ? Strings.WordsMinuteOne : Strings.WordsMinuteMany, n);
        if (minutes == 0) return Seconds(seconds);
        return seconds == 0 ? Minutes(minutes) : string.Format(Strings.WordsAnd, Minutes(minutes), Seconds(seconds));
    }

    /// <summary>"2 h 14 min", "2 h" or "14 min", rounded up to the next minute.</summary>
    public static string Duration(TimeSpan time)
    {
        int minutes = (int)Math.Ceiling(Math.Max(0, time.TotalMinutes));
        int hours = minutes / 60;
        minutes %= 60;
        if (hours == 0) return Minutes(minutes);
        return minutes == 0 ? string.Format(Strings.TimeHours, hours) : string.Format(Strings.TimeHoursMinutes, hours, minutes);
    }

    /// <summary>"14 min".</summary>
    public static string Minutes(int minutes) => string.Format(Strings.TimeMinutes, minutes);
}
