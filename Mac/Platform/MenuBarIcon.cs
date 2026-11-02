using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace TakeABreakGoose;

/// <summary>The goose in the menu bar and its menu, like the tray icon in the Windows app.</summary>
public sealed class MenuBarIcon : IDisposable
{
    private readonly TrayIcon _icon;
    private readonly NativeMenuItem _breakNowItem;
    private readonly NativeMenuItem _doNotDisturbItem;
    private readonly NativeMenuItem _stopSessionItem;

    public event Action? OpenRequested;
    public event Action? SummonRequested;
    public event Action? StopSessionRequested;
    public event Action? DoNotDisturbToggled;
    public event Action? QuitRequested;

    public MenuBarIcon()
    {
        var openItem = new NativeMenuItem("Open Take a Break Goose");
        openItem.Click += (_, _) => OpenRequested?.Invoke();
        _breakNowItem = new NativeMenuItem("Take a break now");
        _breakNowItem.Click += (_, _) => SummonRequested?.Invoke();
        _doNotDisturbItem = new NativeMenuItem("Do not disturb") { ToggleType = NativeMenuItemToggleType.CheckBox };
        _doNotDisturbItem.Click += (_, _) => DoNotDisturbToggled?.Invoke();
        _stopSessionItem = new NativeMenuItem("Stop focus session");
        _stopSessionItem.Click += (_, _) => StopSessionRequested?.Invoke();
        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => QuitRequested?.Invoke();

        using var iconStream = AssetLoader.Open(new Uri("avares://TakeABreakGoose/Assets/goose.ico"));
        _icon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "Take a Break Goose",
            Menu = new NativeMenu
            {
                openItem, _breakNowItem, _doNotDisturbItem, _stopSessionItem, new NativeMenuItemSeparator(), quitItem,
            },
            IsVisible = true,
        };
        // On Windows a click opens the window; on macOS a click opens the menu, which has Open at the top.
        _icon.Clicked += (_, _) => OpenRequested?.Invoke();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _icon });
    }

    /// <param name="inCall">Do not disturb is on because the user is in a call.</param>
    public void Update(string status, bool doNotDisturb, bool inCall, bool sessionRunning)
    {
        _icon.ToolTipText = $"Take a Break Goose\n{status}";
        _doNotDisturbItem.IsChecked = doNotDisturb;
        _doNotDisturbItem.Header = inCall ? "Do not disturb (you're in a call)" : "Do not disturb";
        // Breaks, Do not disturb and stopping only mean something during a focus session.
        // Do not disturb can outlast its session (the goose's goodbye waits for it), so it stays while on.
        _breakNowItem.IsVisible = _stopSessionItem.IsVisible = sessionRunning;
        _doNotDisturbItem.IsVisible = sessionRunning || doNotDisturb;
    }

    public void Dispose() => _icon.Dispose();
}
