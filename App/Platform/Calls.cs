using System;

namespace TakeABreakGoose;

/// <summary>
/// Tells whether the user is in a call: an app is using the camera, or a meeting app is using the
/// microphone. How depends on the operating system (WindowsCalls, MacCalls); elsewhere never.
/// </summary>
public static class Calls
{
    /// <summary>Call once at startup: Windows reports the camera through a monitor that has to be started.</summary>
    public static void Start()
    {
        if (OperatingSystem.IsWindows()) WindowsCameraActivity.Start();
    }

    public static void Stop()
    {
        if (OperatingSystem.IsWindows()) WindowsCameraActivity.Stop();
    }

    public static bool InProgress()
    {
        if (OperatingSystem.IsWindows()) return WindowsCalls.InProgress();
        if (OperatingSystem.IsMacOS()) return MacCalls.InProgress();
        return false;
    }
}
