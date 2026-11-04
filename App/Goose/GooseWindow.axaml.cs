using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace TakeABreakGoose;

/// <summary>
/// A transparent, always-on-top window that draws the goose for one visit: walking in from a screen
/// edge, asking, peeking, napping on the taskbar or just above the Dock and walking back out. What the
/// goose does is up to <see cref="GooseBehavior"/>; this window draws it and passes on its frames and clicks.
/// </summary>
public partial class GooseWindow : Window, IGooseWindow, IGooseView
{
    // The screen's working area in DIPs, without the taskbar, or the menu bar and the Dock.
    private readonly Rect _area;
    private readonly double _scaling;
    private double _left;
    private double _top;
    private readonly Stopwatch _clock = new();
    private bool _animating;
    private bool? _clickThrough;
    private readonly GooseBehavior _goose;

    // The pop-up of the bubble and the break card, and the quick fade of LeaveNow.
    private readonly AnimatedValue _bubbleScale;
    private readonly AnimatedValue _breakCardScale;
    private readonly AnimatedValue _fade;

    /// <summary>For the XAML previewer.</summary>
    public GooseWindow()
        : this(new GooseVisit("Time for a break", "Stretch your legs for a bit.", null, "Take a break",
            AllowSnooze: true, AllowFinishUp: true, StartsBreak: true), 5, null)
    {
    }

    /// <param name="breakLength">If set, the goose naps for this long when the user takes the break.</param>
    public GooseWindow(GooseVisit visit, int snoozeMinutes, TimeSpan? breakLength)
    {
        InitializeComponent();
        OverlayWindow.Prepare(this);
        // (Avalonia only finds named controls, not named transforms.)
        var bubbleScale = (ScaleTransform)Bubble.RenderTransform!;
        var breakCardScale = (ScaleTransform)BreakCard.RenderTransform!;
        _bubbleScale = new AnimatedValue(scale => bubbleScale.ScaleX = bubbleScale.ScaleY = scale, 1);
        _breakCardScale = new AnimatedValue(scale => breakCardScale.ScaleX = breakCardScale.ScaleY = scale, 1);
        _fade = new AnimatedValue(opacity => Opacity = opacity, 1);

        Figure.Outfit = visit.Outfit ?? GooseOutfits.Random();

        // Screen positions are in pixels; the walking works in DIPs.
        if (Screens.Primary is { } screen)
        {
            _scaling = screen.Scaling;
            _area = screen.WorkingArea.ToRect(_scaling);
        }
        else
        {
            _scaling = 1;
            _area = new Rect(0, 0, 1280, 800);
        }

        _top = _area.Bottom - Height;
        _goose = new GooseBehavior(this, new GooseStage(_area.Left, _area.Right, Width, Figure.Width, GooseFigure.BeakTipX),
            visit, snoozeMinutes, breakLength, SystemClock.Instance, UserActivity.Instance, Random.Shared);

        Opened += (_, _) =>
        {
            // Float over everything, on every Space, without taking focus from what the user is doing.
            OverlayWindow.Configure(this);
            _clock.Start();
            _animating = true;
            RequestAnimationFrame(OnFrame);
        };
        Closed += (_, _) =>
        {
            _animating = false;
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

    /// <summary>The window's left edge on screen, in DIPs. Moves the window.</summary>
    private double Left
    {
        get => _left;
        set
        {
            _left = value;
            Position = PixelPoint.FromPoint(new Point(_left, _top), _scaling);
        }
    }

    /// <summary>The window's top edge on screen, in DIPs.</summary>
    private double Top => _top;

    /// <summary>Runs once per frame while the window is open, asking for the next frame each time.</summary>
    private void OnFrame(TimeSpan _)
    {
        if (!_animating) return;
        _goose.NextFrame(_clock.Elapsed);
        // (Not once the goose has closed the window.)
        if (!_animating) return;
        UpdateClickThrough();
        RequestAnimationFrame(OnFrame);
    }

    /// <summary>
    /// Windows and macOS hand every click on the window to it, even on its transparent parts, so the
    /// window lets clicks through to whatever is underneath unless the cursor is over something drawn:
    /// the goose itself (not the gaps around its neck or between its legs), the bubble or the break card.
    /// </summary>
    private void UpdateClickThrough()
    {
        if (OverlayWindow.CursorInWindow(this) is not { } cursor) return;
        // Avalonia's own hit test finds the shape under the cursor; the window's transparent background
        // and empty panels don't count.
        bool overSomething = this.InputHitTest(cursor) is Visual hit
                             && (IsPartOf(Figure, hit) || IsPartOf(Bubble, hit) || IsPartOf(BreakCard, hit));
        if (_clickThrough == !overSomething) return;
        _clickThrough = !overSomething;
        OverlayWindow.SetClickThrough(this, !overSomething);
    }

    private static bool IsPartOf(Visual part, Visual hit) => hit == part || part.IsVisualAncestorOf(hit);

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

    string? IGooseView.FigureToolTip { set => ToolTip.SetTip(Figure, value); }

    void IGooseView.ShowBubble(SpeechBubble bubble)
    {
        TitleText.Text = bubble.Title;
        MessageText.Text = bubble.Message;
        HintText.Text = bubble.Hint ?? "";
        HintText.IsVisible = bubble.Hint != null;
        DismissButton.Content = bubble.PrimaryButton;
        SecondaryButton.Content = bubble.SecondaryButton;
        SecondaryButton.IsVisible = bubble.SecondaryButton != null;
        LinkButton.Content = bubble.LinkButton;
        LinkButton.IsVisible = bubble.LinkButton != null;

        Bubble.IsVisible = true;
        _bubbleScale.Animate(new Tween
        {
            From = 0.4,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(350),
            Easing = Easings.BackOut(amplitude: 0.5),
        });
    }

    void IGooseView.HideBubble() => Bubble.IsVisible = false;

    void IGooseView.ShowBreakCard(bool popUp)
    {
        BreakCard.IsVisible = true;
        if (!popUp) return;
        _breakCardScale.Animate(new Tween
        {
            From = 0.5,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(300),
            Easing = Easings.BackOut(amplitude: 0.4),
        });
    }

    void IGooseView.HideBreakCard() => BreakCard.IsVisible = false;

    string IGooseView.BreakTitle { set => BreakTitle.Text = value; }

    void IGooseView.ShowBreakProgress(double fraction, string text)
    {
        BreakRing.Data = RingArc(fraction, center: 18, radius: 16);
        BreakTimeText.Text = text;
    }

    double IGooseView.WindowLeft { set => Left = value; }

    bool IGooseView.IsCursorBetween(double fromX, double toX)
    {
        if (OverlayWindow.CursorInWindow(this) is not { } inWindow) return false;
        var cursor = new Point(Left + inWindow.X, Top + inWindow.Y);

        // (Without the room for hats above the head.)
        double figureTop = Top + Height - Figure.Bounds.Height - Figure.Margin.Top;
        return cursor.X >= fromX && cursor.X <= toX && cursor.Y >= figureTop && cursor.Y <= Top + Height;
    }

    void IGooseView.FadeOutAndClose()
    {
        _animating = false;
        _fade.Animate(new Tween { To = 0, Duration = TimeSpan.FromMilliseconds(250), Completed = Close });
    }

    void IGooseView.CloseWindow()
    {
        _animating = false;
        Close();
    }

    /// <summary>A circular arc starting at 12 o'clock, going clockwise for the given fraction of a circle.</summary>
    private static Geometry? RingArc(double fraction, double center, double radius)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction >= 0.9999) return new EllipseGeometry { Center = new Point(center, center), RadiusX = radius, RadiusY = radius };
        if (fraction <= 0.0001) return null;

        double angle = fraction * 2 * Math.PI;
        var start = new Point(center, center - radius);
        var end = new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle));
        var arc = new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            IsLargeArc = fraction > 0.5,
            SweepDirection = SweepDirection.Clockwise,
        };
        var figure = new PathFigure { StartPoint = start, IsClosed = false, Segments = new PathSegments { arc } };
        return new PathGeometry { Figures = new PathFigures { figure } };
    }

    private void Link_Click(object? sender, RoutedEventArgs e) => _goose.LinkClicked();

    private void Dismiss_Click(object? sender, RoutedEventArgs e) => _goose.PrimaryClicked();

    private void Secondary_Click(object? sender, RoutedEventArgs e) => _goose.SecondaryClicked();

    private void Figure_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Figure).Properties.IsLeftButtonPressed) return;
        _goose.FigureClicked();
    }
}
