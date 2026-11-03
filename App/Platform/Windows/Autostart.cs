using System;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TakeABreakGoose;

/// <summary>Starts the app when the user signs in to Windows, via the per-user Run key.</summary>
[SupportedOSPlatform("windows")]
public static class Autostart
{
    public const string Argument = "--autostart";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Where Task Manager's Startup apps (and Settings > Apps > Startup) keep their on/off switch.
    // Turning the app off there leaves the Run entry alone and only marks it disabled here.
    private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    // "TakeABreakGoose", or "TakeABreakGoose.Debug" for a Debug build (see AppIdentity).
    internal static string ValueName => AppIdentity.Name;

    public static bool IsEnabled => IsEnabledIn(Registry.CurrentUser);

    public static void SetEnabled(bool enabled) => SetEnabledIn(Registry.CurrentUser, enabled, Environment.ProcessPath);

    // The same, for any registry root, so the tests can use a scratch key instead of the real one.
    internal static bool IsEnabledIn(RegistryKey user)
    {
        using var key = user.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) != null && !DisabledInTaskManager(user);
    }

    internal static void SetEnabledIn(RegistryKey user, bool enabled, string? exePath)
    {
        using (var key = user.CreateSubKey(RunKey))
        {
            if (enabled) key.SetValue(ValueName, $"\"{exePath}\" {Argument}");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        // No value means "enabled" to Windows, so this also undoes an earlier "Disable" in Task Manager.
        using var approved = user.OpenSubKey(StartupApprovedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>The first byte of the value is odd (e.g. 03) when the app is disabled, even (02) when enabled.</summary>
    private static bool DisabledInTaskManager(RegistryKey user)
    {
        using var key = user.OpenSubKey(StartupApprovedKey);
        return key?.GetValue(ValueName) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
    }
}
