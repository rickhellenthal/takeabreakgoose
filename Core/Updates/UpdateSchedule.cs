using System;

namespace TakeABreakGoose;

/// <summary>When the app asks GitHub whether there's a newer version.</summary>
public static class UpdateSchedule
{
    /// <summary>The first check, a moment after the app starts, so it doesn't slow the start down.</summary>
    public static readonly TimeSpan FirstCheckAfter = TimeSpan.FromMinutes(1);

    /// <summary>Then every so often while the app runs, for a goose that's never restarted.</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(12);

    /// <summary>Opening the app window checks too, unless that was done not long ago.</summary>
    public static readonly TimeSpan WindowCheckAfter = TimeSpan.FromHours(1);

    /// <summary>Whether the regular check is due.</summary>
    public static bool IsDue(DateTime now, DateTime startedAt, DateTime? lastCheck) =>
        lastCheck is { } last ? now - last >= CheckEvery : now - startedAt >= FirstCheckAfter;

    /// <summary>Whether opening the app window should check.</summary>
    public static bool IsDueOnOpen(DateTime now, DateTime? lastCheck) =>
        lastCheck is not { } last || now - last >= WindowCheckAfter;
}
