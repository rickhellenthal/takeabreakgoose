using Microsoft.Win32;
using Xunit;

namespace TakeABreakGoose.Windows.Tests;

/// <summary>
/// Starting with Windows, against a scratch registry key (under HKCU\Software\TakeABreakGoose.Tests)
/// that stands in for HKEY_CURRENT_USER, so the real startup entry is never touched.
/// </summary>
public sealed class AutostartTests : IDisposable
{
    private const string ScratchRoot = @"Software\TakeABreakGoose.Tests";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    // "TakeABreakGoose", or "TakeABreakGoose.Debug" when the tests run on a Debug build.
    private static string ValueName => Autostart.ValueName;
    private const string ExePath = @"C:\Apps\TakeABreakGoose.exe";

    private readonly string _scratchPath = $@"{ScratchRoot}\{Guid.NewGuid():N}";
    private readonly RegistryKey _user;

    public AutostartTests()
    {
        _user = Registry.CurrentUser.CreateSubKey(_scratchPath);
    }

    public void Dispose()
    {
        _user.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_scratchPath, throwOnMissingSubKey: false);
        using var root = Registry.CurrentUser.OpenSubKey(ScratchRoot, writable: true);
        if (root is { SubKeyCount: 0 }) Registry.CurrentUser.DeleteSubKey(ScratchRoot, throwOnMissingSubKey: false);
    }

    [Fact]
    public void Off_when_there_is_no_startup_entry()
    {
        Assert.False(Autostart.IsEnabledIn(_user));
    }

    [Fact]
    public void Turning_it_on_adds_a_startup_entry_that_starts_quietly()
    {
        Autostart.SetEnabledIn(_user, true, ExePath);

        Assert.True(Autostart.IsEnabledIn(_user));
        using var run = _user.OpenSubKey(RunKey)!;
        Assert.Equal($"\"{ExePath}\" --autostart", run.GetValue(ValueName));
    }

    [Fact]
    public void Disabled_in_task_manager_counts_as_off()
    {
        Autostart.SetEnabledIn(_user, true, ExePath);
        SetTaskManagerState(0x03);

        Assert.False(Autostart.IsEnabledIn(_user));
    }

    [Fact]
    public void Enabled_in_task_manager_counts_as_on()
    {
        Autostart.SetEnabledIn(_user, true, ExePath);
        SetTaskManagerState(0x02);

        Assert.True(Autostart.IsEnabledIn(_user));
    }

    [Fact]
    public void Turning_it_on_again_undoes_disable_in_task_manager()
    {
        Autostart.SetEnabledIn(_user, true, ExePath);
        SetTaskManagerState(0x03);

        Autostart.SetEnabledIn(_user, true, ExePath);

        Assert.True(Autostart.IsEnabledIn(_user));
        Assert.Null(TaskManagerState());
    }

    [Fact]
    public void Turning_it_off_removes_the_startup_entry_and_the_task_manager_state()
    {
        Autostart.SetEnabledIn(_user, true, ExePath);
        SetTaskManagerState(0x02);

        Autostart.SetEnabledIn(_user, false, ExePath);

        Assert.False(Autostart.IsEnabledIn(_user));
        using var run = _user.OpenSubKey(RunKey)!;
        Assert.Null(run.GetValue(ValueName));
        Assert.Null(TaskManagerState());
    }

    /// <summary>What Task Manager writes: 12 bytes, the first one saying enabled (02) or disabled (03).</summary>
    private void SetTaskManagerState(byte state)
    {
        using var key = _user.CreateSubKey(StartupApprovedKey);
        var value = new byte[12];
        value[0] = state;
        key.SetValue(ValueName, value, RegistryValueKind.Binary);
    }

    private object? TaskManagerState()
    {
        using var key = _user.OpenSubKey(StartupApprovedKey);
        return key?.GetValue(ValueName);
    }
}
