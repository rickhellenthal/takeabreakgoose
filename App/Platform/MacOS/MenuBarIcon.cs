using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;

namespace TakeABreakGoose;

/// <summary>The goose in the menu bar and its menu.</summary>
internal sealed class MenuBarIcon : IDisposable
{
    private readonly TrayIcon _icon;
    private readonly NativeMenuItem _openItem;
    private readonly NativeMenuItem _breakNowItem;
    private readonly NativeMenuItem _doNotDisturbItem;
    private readonly NativeMenuItem _stopSessionItem;
    private readonly NativeMenuItem _quitItem;

    public event Action? OpenRequested;
    public event Action? SummonRequested;
    public event Action? StopSessionRequested;
    public event Action? DoNotDisturbToggled;
    public event Action? QuitRequested;

    public MenuBarIcon()
    {
        // The texts come with Update, in the app's language.
        var openItem = _openItem = new NativeMenuItem();
        openItem.Click += (_, _) => OpenRequested?.Invoke();
        _breakNowItem = new NativeMenuItem();
        _breakNowItem.Click += (_, _) => SummonRequested?.Invoke();
        _doNotDisturbItem = new NativeMenuItem { ToggleType = NativeMenuItemToggleType.CheckBox };
        _doNotDisturbItem.Click += (_, _) => DoNotDisturbToggled?.Invoke();
        _stopSessionItem = new NativeMenuItem();
        _stopSessionItem.Click += (_, _) => StopSessionRequested?.Invoke();
        _quitItem = new NativeMenuItem();
        _quitItem.Click += (_, _) => QuitRequested?.Invoke();

        using var iconStream = AssetLoader.Open(new Uri("avares://TakeABreakGoose/Assets/goose.ico"));
        _icon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "Take a Break Goose",
            Menu = new NativeMenu
            {
                openItem, _breakNowItem, _doNotDisturbItem, _stopSessionItem, new NativeMenuItemSeparator(), _quitItem,
            },
            IsVisible = true,
        };
        // On Windows a click opens the window; on macOS a click opens the menu, which has Open at the top.
        _icon.Clicked += (_, _) => OpenRequested?.Invoke();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _icon });
    }

    /// <param name="inCall">Do not disturb is on because the user is in a call.</param>
    /// <param name="hotkeyKey">The global shortcut, shown next to Open.</param>
    public void Update(string status, bool doNotDisturb, bool inCall, bool sessionRunning,
        HotkeyModifiers hotkeyModifiers, string hotkeyKey)
    {
        _icon.ToolTipText = $"Take a Break Goose\n{status}";
        _doNotDisturbItem.IsChecked = doNotDisturb;
        _openItem.Header = Strings.MenuOpen;
        _breakNowItem.Header = Strings.TakeABreakNow;
        _doNotDisturbItem.Header = inCall ? Strings.MenuDoNotDisturbInCall : Strings.MenuDoNotDisturb;
        _stopSessionItem.Header = Strings.MenuStopSession;
        _quitItem.Header = Strings.MenuQuit;
        // Breaks, Do not disturb and stopping only mean something during a focus session.
        // Do not disturb can outlast its session (the goose's goodbye waits for it), so it stays while on.
        _breakNowItem.IsVisible = _stopSessionItem.IsVisible = sessionRunning;
        _doNotDisturbItem.IsVisible = sessionRunning || doNotDisturb;
        // HotkeyModifiers have the same values as Avalonia's KeyModifiers.
        _openItem.Gesture = Enum.TryParse(hotkeyKey, out Key key) ? new KeyGesture(key, (KeyModifiers)hotkeyModifiers) : null;
    }

    public void Dispose() => _icon.Dispose();
}
