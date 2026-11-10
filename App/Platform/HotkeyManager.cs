using System;

namespace TakeABreakGoose;

/// <summary>The global shortcut that opens the app window: WindowsHotkey, MacHotkey or LinuxHotkey.</summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly WindowsHotkey? _windows;
    private readonly MacHotkey? _mac;
    private readonly LinuxHotkey? _linux;

    public event Action? Pressed;

    public HotkeyManager()
    {
        if (OperatingSystem.IsWindows())
        {
            _windows = new WindowsHotkey();
            _windows.Pressed += () => Pressed?.Invoke();
        }
        else if (OperatingSystem.IsMacOS())
        {
            _mac = new MacHotkey();
            _mac.Pressed += () => Pressed?.Invoke();
        }
        else if (OperatingSystem.IsLinux())
        {
            _linux = new LinuxHotkey();
            _linux.Pressed += () => Pressed?.Invoke();
        }
    }

    /// <param name="key">The key's name in Avalonia's (and WPF's) Key enum, as the settings keep it.</param>
    /// <returns>False when another app already has this shortcut, or it can't be one.</returns>
    public bool Register(HotkeyModifiers modifiers, string key)
    {
        if (OperatingSystem.IsWindows() && _windows != null) return _windows.Register(modifiers, key);
        if (OperatingSystem.IsLinux() && _linux != null) return _linux.Register(modifiers, key);
        return _mac?.Register(modifiers, key) ?? true;
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows()) _windows?.Dispose();
        if (OperatingSystem.IsLinux()) _linux?.Dispose();
        _mac?.Dispose();
    }
}
