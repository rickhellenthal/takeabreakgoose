using System;
using Microsoft.Win32;

namespace TakeABreakGoose;

/// <summary>Starts the app when the user signs in to Windows, via the per-user Run key.</summary>
public static class Autostart
{
    public const string Argument = "--autostart";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Where Task Manager's Startup apps (and Settings > Apps > Startup) keep their on/off switch.
    // Turning the app off there leaves the Run entry alone and only marks it disabled here.
    private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "TakeABreakGoose";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) != null && !DisabledInTaskManager;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {Argument}");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        // No value means "enabled" to Windows, so this also undoes an earlier "Disable" in Task Manager.
        using var approved = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>The first byte of the value is odd (e.g. 03) when the app is disabled, even (02) when enabled.</summary>
    private static bool DisabledInTaskManager
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupApprovedKey);
            return key?.GetValue(ValueName) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
        }
    }
}
