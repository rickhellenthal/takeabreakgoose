using System;
using Microsoft.Win32;

namespace TakeABreakGoose;

/// <summary>Starts the app when the user signs in to Windows, via the per-user Run key.</summary>
public static class Autostart
{
    public const string Argument = "--autostart";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TakeABreakGoose";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) != null;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {Argument}");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
