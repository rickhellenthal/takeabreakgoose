using System;

namespace TakeABreakGoose;

/// <summary>
/// Starting the app when the user signs in: the Run registry key on Windows (Autostart), a login
/// item on macOS (LoginItem).
/// </summary>
public static class StartAtLogin
{
    /// <summary>Whether it can be turned on here at all (on macOS: 13 or later, and the app in its .app bundle).</summary>
    public static bool IsAvailable => OperatingSystem.IsWindows() || LoginItem.IsAvailable;

    public static bool IsEnabled =>
        OperatingSystem.IsWindows() ? Autostart.IsEnabled : LoginItem.IsEnabled;

    /// <summary>Throws, with the operating system's own explanation, when it can't be changed.</summary>
    public static void SetEnabled(bool enabled)
    {
        if (OperatingSystem.IsWindows()) Autostart.SetEnabled(enabled);
        else LoginItem.SetEnabled(enabled);
    }
}
