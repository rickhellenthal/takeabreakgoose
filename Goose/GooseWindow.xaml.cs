using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TakeABreakGoose;

/// <summary>
/// A transparent, always-on-top window that draws the goose for one visit: walking in from a screen
/// edge, asking, peeking, napping on the taskbar and walking back out. What the goose does is up to
/// <see cref="GooseBehavior"/>; this window draws it and passes on its frames and clicks.
/// </summary>
public partial class GooseWindow : Window, IGooseWindow, IGooseView
{
    private readonly Rect _area = SystemParameters.WorkArea;
    private readonly Stopwatch _clock = new();
    private readonly GooseBehavior _goose;

    /// <param name="breakLength">If set, the goose naps for this long when the user takes the break.</param>
    public GooseWindow(GooseVisit visit, int snoozeMinutes, TimeSpan? breakLength)
    {
        InitializeComponent();
        Figure.Outfit = visit.Outfit ?? GooseOutfits.Random();
        Top = _area.Bottom - Height;
        _goose = new GooseBehavior(this, new GooseStage(_area.Left, _area.Right, Width, Figure.Width, GooseFigure.BeakTipX),
            visit, snoozeMinutes, breakLength, SystemClock.Instance, UserActivity.Instance, Random.Shared);

        Loaded += (_, _) =>
        {
            _clock.Start();
            CompositionTarget.Rendering += OnRendering;
        };
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRendering;
            _goose.WindowClosed();
        };
    }

    public event Action<GooseOutcome>? Answered
    {
        add => _goose.Answered += value;
        remove => _goose.Answered -= value;
    }

    public event Action<DateTime>? BreakExtended
    {
        add => _goose.BreakExtended += value;
        remove => _goose.BreakExtended -= value;
    }

    public event Action? ReadyToFocus
    {
        add => _goose.ReadyToFocus += value;
        remove => _goose.ReadyToFocus -= value;
    }

    public event Action<DateTime>? TookBreakWhileAway
    {
        add => _goose.TookBreakWhileAway += value;
        remove => _goose.TookBreakWhileAway -= value;
    }

    public event Action? Finished
    {
        add => _goose.Finished += value;
        remove => _goose.Finished -= value;
    }

    public bool StillAskingForBreak => _goose.StillAskingForBreak;

    public bool OnBreak => _goose.OnBreak;

    public void LeaveNow() => _goose.LeaveNow();

    public void WalkAway() => _goose.WalkAway();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Never steal focus from whatever the user is doing, and stay out of Alt+Tab.
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void OnRendering(object? sender, EventArgs e) => _goose.NextFrame(_clock.Elapsed);

    int IGooseView.Facing { set => Figure.Facing = value; }

    double IGooseView.HeadAngle { set => Figure.HeadAngle = value; }

    void IGooseView.SetStride(double swing) => Figure.SetStride(swing);

    bool IGooseView.Sleeping
    {
        get => Figure.Sleeping;
        set => Figure.Sleeping = value;
    }

    NapLook IGooseView.Look { set => Figure.Look = value; }

    bool IGooseView.Huffy
    {
        get => Figure.Huffy;
        set => Figure.Huffy = value;
    }

    void IGooseView.Flap() => Figure.Flap();

    string? IGooseView.FigureToolTip { set => Figure.ToolTip = value; }

    void IGooseView.ShowBubble(SpeechBubble bubble)
    {
        TitleText.Text = bubble.Title;
        MessageText.Text = bubble.Message;
        HintText.Text = bubble.Hint ?? "";
        HintText.Visibility = bubble.Hint == null ? Visibility.Collapsed : Visibility.Visible;
        DismissButton.Content = bubble.PrimaryButton;
        SecondaryButton.Content = bubble.SecondaryButton;
        SecondaryButton.Visibility = bubble.SecondaryButton == null ? Visibility.Collapsed : Visibility.Visible;
        LinkButton.Content = bubble.LinkButton;
        LinkButton.Visibility = bubble.LinkButton == null ? Visibility.Collapsed : Visibility.Visible;

        Bubble.Visibility = Visibility.Visible;
        var pop = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(350))
        {
            EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut },
        };
        BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    void IGooseView.HideBubble() => Bubble.Visibility = Visibility.Collapsed;

    void IGooseView.ShowBreakCard(bool popUp)
    {
        BreakCard.Visibility = Visibility.Visible;
        if (!popUp) return;
        var pop = new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut },
        };
        BreakCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        BreakCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    void IGooseView.HideBreakCard() => BreakCard.Visibility = Visibility.Collapsed;

    string IGooseView.BreakTitle { set => BreakTitle.Text = value; }

    void IGooseView.ShowBreakProgress(double fraction, string text)
    {
        BreakRing.Data = RingArc(fraction, center: 18, radius: 16);
        BreakTimeText.Text = text;
    }

    double IGooseView.WindowLeft { set => Left = value; }

    bool IGooseView.IsCursorBetween(double fromX, double toX)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null || !GetCursorPos(out POINT p)) return false;
        Point cursor = source.CompositionTarget.TransformFromDevice.Transform(new Point(p.X, p.Y));

        double figureTop = Top + Height - Figure.ActualHeight;
        return cursor.X >= fromX && cursor.X <= toX && cursor.Y >= figureTop && cursor.Y <= Top + Height;
    }

    void IGooseView.FadeOutAndClose()
    {
        CompositionTarget.Rendering -= OnRendering;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    void IGooseView.CloseWindow()
    {
        CompositionTarget.Rendering -= OnRendering;
        Close();
    }

    /// <summary>A circular arc starting at 12 o'clock, going clockwise for the given fraction of a circle.</summary>
    private static Geometry RingArc(double fraction, double center, double radius)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction >= 0.9999) return new EllipseGeometry(new Point(center, center), radius, radius);
        if (fraction <= 0.0001) return Geometry.Empty;

        double angle = fraction * 2 * Math.PI;
        var start = new Point(center, center - radius);
        var end = new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        return new PathGeometry(new[] { figure });
    }

    private void Link_Click(object sender, RoutedEventArgs e) => _goose.LinkClicked();

    private void Dismiss_Click(object sender, RoutedEventArgs e) => _goose.PrimaryClicked();

    private void Secondary_Click(object sender, RoutedEventArgs e) => _goose.SecondaryClicked();

    private void Figure_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _goose.FigureClicked();

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);
}
