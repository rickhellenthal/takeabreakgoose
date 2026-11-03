using System;

namespace TakeABreakGoose;

/// <summary>
/// The name the app goes by on the computer: its settings folder on Windows, the lock that keeps it
/// to one copy, and its entry for starting with Windows.
/// </summary>
internal static class AppIdentity
{
#if DEBUG
    // A Debug build on Windows (dotnet run) keeps apart from the installed goose: its own settings,
    // its own lock, so both can run, and it never replaces the installed one's Run entry.
    public static string Name => OperatingSystem.IsWindows() ? "TakeABreakGoose.Debug" : "TakeABreakGoose";
#else
    public static string Name => "TakeABreakGoose";
#endif
}
