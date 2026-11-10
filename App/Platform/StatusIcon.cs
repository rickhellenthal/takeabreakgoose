using System;

namespace TakeABreakGoose;

/// <summary>
/// The goose's icon and menu: in the notification area on Windows (WindowsTrayIcon), in the menu bar
/// on macOS and the system tray on Linux (MenuBarIcon, Avalonia's own tray icon). Also tells the
/// user short things while the window is closed.
/// </summary>
public sealed class StatusIcon : IDisposable
{
    private readonly WindowsTrayIcon? _tray;
    private readonly MenuBarIcon? _menuBar;

    public event Action? OpenRequested;
    public event Action? SummonRequested;
    public event Action? StopSessionRequested;
    public event Action? DoNotDisturbToggled;
    public event Action? QuitRequested;
    public event Action? UpdateRequested;

    public StatusIcon()
    {
        if (OperatingSystem.IsWindows())
        {
            var tray = _tray = new WindowsTrayIcon();
            tray.OpenRequested += () => OpenRequested?.Invoke();
            tray.SummonRequested += () => SummonRequested?.Invoke();
            tray.StopSessionRequested += () => StopSessionRequested?.Invoke();
            tray.DoNotDisturbToggled += () => DoNotDisturbToggled?.Invoke();
            tray.QuitRequested += () => QuitRequested?.Invoke();
            tray.UpdateRequested += () => UpdateRequested?.Invoke();
        }
        else
        {
            var menuBar = _menuBar = new MenuBarIcon();
            menuBar.OpenRequested += () => OpenRequested?.Invoke();
            menuBar.SummonRequested += () => SummonRequested?.Invoke();
            menuBar.StopSessionRequested += () => StopSessionRequested?.Invoke();
            menuBar.DoNotDisturbToggled += () => DoNotDisturbToggled?.Invoke();
            menuBar.QuitRequested += () => QuitRequested?.Invoke();
            menuBar.UpdateRequested += () => UpdateRequested?.Invoke();
        }
    }

    /// <param name="inCall">Do not disturb is on because the user is in a call.</param>
    /// <param name="hotkeyKey">The global shortcut, shown next to Open.</param>
    /// <param name="updateText">"Update to version 1.12.0" when there's a newer version, otherwise null.</param>
    public void Update(string status, bool doNotDisturb, bool inCall, bool sessionRunning,
        HotkeyModifiers hotkeyModifiers, string hotkeyKey, string? updateText)
    {
        if (OperatingSystem.IsWindows())
            _tray?.Update(status, doNotDisturb, inCall, sessionRunning, HotkeyText.Format(hotkeyModifiers, hotkeyKey), updateText);
        _menuBar?.Update(status, doNotDisturb, inCall, sessionRunning, hotkeyModifiers, hotkeyKey, updateText);
    }

    /// <summary>A short message: a balloon from the tray icon on Windows, a small card under the menu bar on macOS.</summary>
    public void ShowNotice(string message)
    {
        if (OperatingSystem.IsWindows() && _tray != null) _tray.ShowBalloon(message);
        else Notice.Show(message);
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows()) _tray?.Dispose();
        _menuBar?.Dispose();
    }
}
