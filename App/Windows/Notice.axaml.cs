using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace TakeABreakGoose;

/// <summary>
/// A short message in the top right corner, under the menu bar, like the tray icon's balloons on
/// Windows. macOS's own notifications need a signed app and the user's permission; this needs neither.
/// Clicking it, or waiting a few seconds, makes it go away.
/// </summary>
public partial class Notice : Window
{
    private static readonly TimeSpan ShownFor = TimeSpan.FromSeconds(6);

    public Notice() : this("")
    {
    }

    public Notice(string message)
    {
        InitializeComponent();
        OverlayWindow.Prepare(this);
        MessageText.Text = message;
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Close();
        };
        Opened += (_, _) =>
        {
            OverlayWindow.Configure(this);
            PlaceInCorner();
            DispatcherTimer.RunOnce(Close, ShownFor);
        };
    }

    public static void Show(string message) => new Notice(message).Show();

    private void PlaceInCorner()
    {
        if (Screens.Primary is not { } screen) return;
        var area = screen.WorkingArea.ToRect(screen.Scaling);
        double width = Bounds.Width > 0 ? Bounds.Width : Width;
        Position = PixelPoint.FromPoint(new Point(area.Right - width - 4, area.Top + 4), screen.Scaling);
    }
}
