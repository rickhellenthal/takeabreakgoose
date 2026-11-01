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
    GooseOutfit? Outfit = null);

public enum GooseOutcome { Dismissed, Snoozed, TimedOut }

/// <summary>
/// A transparent, always-on-top window that walks a goose in from a screen edge,
/// shows a break reminder, then walks back out. If the user needs to finish up first,
/// the goose waits peeking around the screen edge until it's clicked.
/// </summary>
public partial class GooseWindow : Window
{
    private enum Phase { WalkingIn, Talking, ToEdge, Peeking, Returning, WalkingOut }

    private const double WalkSpeed = 150;   // DIPs per second
    private const double StepRate = 12;     // leg swing speed, radians per second
    private const double PeekVisible = 40;  // how much of the goose shows while peeking
    private const double PeekHoverVisible = 95;

    private readonly Rect _area = SystemParameters.WorkArea;
    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _stayTimer;
    private readonly bool _stayUntilDismissed;
    private readonly bool _enteredFromLeft;
    private TimeSpan _lastFrame;
    private Phase _phase = Phase.WalkingIn;
    private double _targetLeft;
    private double _stepPhase;
    private GooseOutcome _outcome = GooseOutcome.TimedOut;

    /// <summary>Raised once the goose has left the screen, with how the user answered it.</summary>
    public event Action<GooseOutcome>? Finished;

    public GooseWindow(GooseVisit visit, int staySeconds, int snoozeMinutes)
    {
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
            Finished?.Invoke(_outcome);
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
        _outcome = outcome;
        StopTalking();
        _phase = Phase.WalkingOut;
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
        // Poking the goose makes it flap its head indignantly.
        Figure.Flap();
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
