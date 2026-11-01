using System;
using System.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TakeABreakGoose;

/// <summary>The notification-area icon and its context menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _gooseIcon;
    private readonly Forms.ToolStripMenuItem _openItem;
    private readonly Forms.ToolStripMenuItem _breakNowItem;
    private readonly Forms.ToolStripMenuItem _doNotDisturbItem;
    private readonly Forms.ToolStripMenuItem _stopSessionItem;

    public event Action? OpenRequested;
    public event Action? SummonRequested;
    public event Action? StopSessionRequested;
    public event Action? DoNotDisturbToggled;
    public event Action? QuitRequested;

    public TrayIcon()
    {
        _gooseIcon = LoadGooseIcon();

        var menu = new Forms.ContextMenuStrip();
        _openItem = new Forms.ToolStripMenuItem("Open Take a Break Goose", null, (_, _) => OpenRequested?.Invoke());
        _openItem.Font = new Drawing.Font(_openItem.Font, Drawing.FontStyle.Bold);
        menu.Items.Add(_openItem);
        _breakNowItem = new Forms.ToolStripMenuItem("Take a break now", null, (_, _) => SummonRequested?.Invoke());
        menu.Items.Add(_breakNowItem);
        _doNotDisturbItem = new Forms.ToolStripMenuItem("Do not disturb", null, (_, _) => DoNotDisturbToggled?.Invoke());
        menu.Items.Add(_doNotDisturbItem);
        _stopSessionItem = new Forms.ToolStripMenuItem("Stop focus session", null, (_, _) => StopSessionRequested?.Invoke());
        menu.Items.Add(_stopSessionItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => QuitRequested?.Invoke());

        _icon = new Forms.NotifyIcon
        {
            Icon = _gooseIcon,
            Text = "Take a Break Goose",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke();
        };
    }

    public void Update(string status, bool doNotDisturb, bool sessionRunning, string hotkeyText)
    {
        string text = $"Take a Break Goose\n{status}";
        if (text.Length > 63) text = text[..63]; // NotifyIcon.Text is limited to 63 chars
        if (_icon.Text != text) _icon.Text = text;
        _doNotDisturbItem.Checked = doNotDisturb;
        // Breaks, Do not disturb and stopping only mean something during a focus session.
        // Do not disturb can outlast its session (the goose's goodbye waits for it), so it stays while on.
        _breakNowItem.Visible = _stopSessionItem.Visible = sessionRunning;
        _doNotDisturbItem.Visible = sessionRunning || doNotDisturb;
        _openItem.ShortcutKeyDisplayString = hotkeyText;
    }

    public void ShowBalloon(string message) =>
        _icon.ShowBalloonTip(4000, "Take a Break Goose", message, Forms.ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _gooseIcon.Dispose();
    }

    private static Drawing.Icon LoadGooseIcon()
    {
        // goose.ico is also the exe's icon, so the tray and the shortcut always match.
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/goose.ico"));
        using var stream = resource.Stream;
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }
}
