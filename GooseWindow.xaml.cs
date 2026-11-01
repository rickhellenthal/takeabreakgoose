using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TakeABreakGoose;

/// <summary>What the goose says during one visit. A null outfit means a random one.</summary>
public sealed record GooseVisit(
    string Title,
    string Message,
    string? Hint,
    string DismissText,
    bool AllowSnooze,
    bool AllowFinishUp,
    bool StartsBreak,
    GooseOutfit? Outfit = null);

public enum GooseOutcome { Dismissed, Snoozed, TimedOut }

/// <summary>
/// A transparent, always-on-top window that walks a goose in from a screen edge,
/// shows a break reminder, then walks back out. If the user needs to finish up first,
/// the goose waits peeking around the screen edge until it's clicked. If the user takes
/// the break, the goose naps on the taskbar until the break is over.
/// </summary>
public partial class GooseWindow : Window
{
    private enum Phase { WalkingIn, Talking, ToEdge, Peeking, Returning, Sleeping, WakingUp, WalkingOut }

    private static readonly TimeSpan WakeUpPause = TimeSpan.FromSeconds(5);

    private const double WalkSpeed = 150;   // DIPs per second
    private const double StepRate = 12;     // leg swing speed, radians per second
    private const double PeekVisible = 40;  // how much of the goose shows while peeking
    private const double PeekHoverVisible = 58;

    private readonly Rect _area = SystemParameters.WorkArea;
    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _stayTimer;
    private readonly bool _stayUntilDismissed;
    private readonly bool _enteredFromLeft;
    private TimeSpan _lastFrame;
    private Phase _phase = Phase.WalkingIn;
    private double _targetLeft;
    private double _stepPhase;
    private readonly TimeSpan? _breakLength;
    private DateTime _breakStart;
    private DateTime _breakEnd;
    private DateTime _leaveAt;

    /// <summary>Raised as soon as the user answers (or the goose gives up waiting).</summary>
    public event Action<GooseOutcome>? Answered;

    /// <summary>Raised once the goose has left the screen.</summary>
    public event Action? Finished;

    /// <param name="breakLength">If set, the goose naps for this long when the user takes the break.</param>
    public GooseWindow(GooseVisit visit, int staySeconds, int snoozeMinutes, TimeSpan? breakLength)
    {
        _breakLength = breakLength;
        InitializeComponent();

        TitleText.Text = visit.Title;
        MessageText.Text = visit.Message;
        HintText.Text = visit.Hint ?? "";
        HintText.Visibility = visit.Hint == null ? Visibility.Collapsed : Visibility.Visible;
        DismissButton.Content = visit.DismissText;
        SnoozeButton.Content = $"Snooze {snoozeMinutes} min";
        SnoozeButton.Visibility = visit.AllowSnooze ? Visibility.Visible : Visibility.Collapsed;
        FinishUpButton.Visibility = visit.AllowFinishUp ? Visibility.Visible : Visibility.Collapsed;
        Figure.Outfit = visit.Outfit ?? GooseOutfits.Random();

        _stayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, staySeconds)) };
        _stayTimer.Tick += (_, _) => Leave(GooseOutcome.TimedOut);
        _stayUntilDismissed = staySeconds <= 0;

        _enteredFromLeft = Random.Shared.Next(2) == 0;
        Top = _area.Bottom - Height;
        // Start with the goose itself just off screen (the window is wider than the goose).
        Left = _enteredFromLeft ? _area.Left - FigureRight : _area.Right - FigureLeft;

        // Stop a short walk in from the side it came from. The window stays on screen,
        // so the speech bubble is never cut off.
        double reach = Random.Shared.NextDouble() * _area.Width * 0.08;
        double target = _enteredFromLeft
            ? _area.Left + reach
            : _area.Right - Width - reach;
        _targetLeft = Math.Clamp(target, _area.Left, Math.Max(_area.Left, _area.Right - Width));
        Figure.Facing = InwardDirection;

        Loaded += (_, _) =>
        {
            _clock.Start();
            CompositionTarget.Rendering += OnRendering;
        };
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRendering;
            _stayTimer.Stop();
            Finished?.Invoke();
        };
    }

    /// <summary>The direction pointing from the goose's edge into the screen.</summary>
    private int InwardDirection => _enteredFromLeft ? 1 : -1;

    // Where the goose figure sits horizontally inside the window.
    private double FigureLeft => (Width - Figure.Width) / 2;
    private double FigureRight => FigureLeft + Figure.Width;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Never steal focus from whatever the user is doing, and stay out of Alt+Tab.
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        double dt = Math.Min((now - _lastFrame).TotalSeconds, 0.05);
        _lastFrame = now;

        switch (_phase)
        {
            case Phase.WalkingIn:
            case Phase.Returning:
                if (WalkTowards(_targetLeft, dt)) StartTalking();
                break;

            case Phase.Talking:
                // Idle: a gentle head bob while waiting for an answer.
                Figure.HeadAngle = Math.Sin(now.TotalSeconds * 2.5) * 4;
                break;

            case Phase.ToEdge:
                if (WalkTowards(PeekLeft(PeekVisible), dt))
                {
                    _phase = Phase.Peeking;
                    Figure.Facing = InwardDirection;
                    Figure.ToolTip = "Click me when you're ready for your break";
                }
                break;

            case Phase.Peeking:
                // Lean out a bit further while the mouse is near the goose.
                double peekLeft = PeekLeft(IsCursorNearPeek() ? PeekHoverVisible : PeekVisible);
                Left += (peekLeft - Left) * Math.Min(1, dt * 10);
                Figure.HeadAngle = Math.Sin(now.TotalSeconds * 1.6) * 6;
                break;

            case Phase.Sleeping:
                UpdateBreakProgress();
                if (DateTime.Now >= _breakEnd) WakeUp();
                break;

            case Phase.WakingUp:
                Figure.HeadAngle = Math.Sin(now.TotalSeconds * 2.5) * 4;
                if (DateTime.Now >= _leaveAt)
                {
                    BreakCard.Visibility = Visibility.Collapsed;
                    Figure.HeadAngle = 0;
                    _phase = Phase.WalkingOut;
                }
                break;

            case Phase.WalkingOut:
                Walk(-InwardDirection, dt);
                if (Left + FigureLeft > _area.Right || Left + FigureRight < _area.Left)
                {
                    _phase = Phase.WalkingIn; // stop moving while closing
                    CompositionTarget.Rendering -= OnRendering;
                    Close();
                }
                break;
        }
    }

    /// <summary>Walks one frame towards a window position. Returns true on arrival.</summary>
    private bool WalkTowards(double targetLeft, double dt)
    {
        int direction = Math.Sign(targetLeft - Left);
        if (direction != 0)
        {
            Walk(direction, dt);
            bool stillWalking = direction > 0 ? Left < targetLeft : Left > targetLeft;
            if (stillWalking) return false;
        }
        Left = targetLeft;
        Figure.SetStride(0);
        return true;
    }

    private void Walk(int direction, double dt)
    {
        Figure.Facing = direction;
        Left += direction * WalkSpeed * dt;
        _stepPhase += dt * StepRate;
        Figure.SetStride(Math.Sin(_stepPhase));
    }

    /// <summary>The window position at which only the front of the goose shows past the screen edge.</summary>
    private double PeekLeft(double visible)
    {
        return _enteredFromLeft
            ? _area.Left + visible - (FigureLeft + GooseFigure.BeakTipX)
            : _area.Right - visible - (FigureRight - GooseFigure.BeakTipX);
    }

    /// <summary>
    /// Whether the cursor is in the strip at the screen edge where the goose peeks. Checked against
    /// a fixed area rather than mouse enter/leave, because the transparent gaps around the goose
    /// would otherwise slide under the cursor as it leans out and make it bounce back and forth.
    /// </summary>
    private bool IsCursorNearPeek()
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null || !GetCursorPos(out POINT p)) return false;
        Point cursor = source.CompositionTarget.TransformFromDevice.Transform(new Point(p.X, p.Y));

        double reach = PeekHoverVisible + 10;
        bool inStrip = _enteredFromLeft
            ? cursor.X >= _area.Left && cursor.X <= _area.Left + reach
            : cursor.X <= _area.Right && cursor.X >= _area.Right - reach;
        double figureTop = Top + Height - Figure.ActualHeight;
        return inStrip && cursor.Y >= figureTop && cursor.Y <= Top + Height;
    }

    private void StartTalking()
    {
        _phase = Phase.Talking;
        Figure.Facing = InwardDirection;
        Bubble.Visibility = Visibility.Visible;
        var pop = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(350))
        {
            EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut },
        };
        BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

        if (!_stayUntilDismissed) _stayTimer.Start();
    }

    private void StopTalking()
    {
        _stayTimer.Stop();
        Bubble.Visibility = Visibility.Collapsed;
        Figure.HeadAngle = 0;
    }

    private void Leave(GooseOutcome outcome)
    {
        if (_phase != Phase.Talking) return;
        StopTalking();
        Answered?.Invoke(outcome);

        if (outcome == GooseOutcome.Dismissed && _breakLength is { } length && length > TimeSpan.Zero)
            StartSleeping(length);
        else
            _phase = Phase.WalkingOut;
    }

    private void StartSleeping(TimeSpan length)
    {
        _phase = Phase.Sleeping;
        _breakStart = DateTime.Now;
        _breakEnd = _breakStart + length;
        Figure.Sleeping = true;
        Figure.ToolTip = $"Napping until {_breakEnd:HH:mm}. Enjoy your break!";

        BreakTitle.Text = "Enjoy your break";
        UpdateBreakProgress();
        BreakCard.Visibility = Visibility.Visible;
        var pop = new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut },
        };
        BreakCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        BreakCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    private void UpdateBreakProgress()
    {
        var total = _breakEnd - _breakStart;
        var left = _breakEnd - DateTime.Now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        double progress = total > TimeSpan.Zero ? 1 - left / total : 1;

        BreakRing.Data = RingArc(progress, center: 18, radius: 16);
        BreakTimeText.Text = left.TotalHours >= 1
            ? $"{(int)left.TotalHours}:{left:mm\\:ss} left"
            : $"{left:m\\:ss} left";
    }

    private void WakeUp()
    {
        _phase = Phase.WakingUp;
        _leaveAt = DateTime.Now + WakeUpPause;
        Figure.Sleeping = false;
        Figure.ToolTip = null;
        BreakRing.Data = RingArc(1, center: 18, radius: 16);
        BreakTitle.Text = "Break's over, welcome back!";
        BreakTimeText.Text = "Off you go, refreshed and ready";
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

    private void FinishUp_Click(object sender, RoutedEventArgs e)
    {
        if (_phase != Phase.Talking) return;
        StopTalking();
        // It only offers this once; when it comes back it's break time.
        FinishUpButton.Visibility = Visibility.Collapsed;
        _phase = Phase.ToEdge;
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e) => Leave(GooseOutcome.Dismissed);

    private void Snooze_Click(object sender, RoutedEventArgs e) => Leave(GooseOutcome.Snoozed);

    private void Figure_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_phase == Phase.Peeking)
        {
            // The user is done finishing up: come back and ask again.
            Figure.ToolTip = null;
            MessageText.Text = "All finished up and saved? Then you're ready for your break!";
            _phase = Phase.Returning;
            return;
        }
        // Poking the goose makes it flap its head indignantly, unless it's napping.
        if (_phase != Phase.Sleeping) Figure.Flap();
    }

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
