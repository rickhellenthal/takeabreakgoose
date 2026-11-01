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

/// <summary>What the goose says during one visit.</summary>
public sealed record GooseVisit(string Title, string Message, string? Hint, string DismissText, bool AllowSnooze);

public enum GooseOutcome { Dismissed, Snoozed, TimedOut }

/// <summary>
/// A transparent, always-on-top window that walks a goose in from a screen edge,
/// shows a break reminder, then walks back out.
/// </summary>
public partial class GooseWindow : Window
{
    private enum Phase { WalkingIn, Talking, WalkingOut }

    private const double WalkSpeed = 150; // DIPs per second
    private const double StepRate = 12;   // leg swing speed, radians per second

    private readonly Rect _area = SystemParameters.WorkArea;
    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _stayTimer;
    private readonly bool _stayUntilDismissed;
    private TimeSpan _lastFrame;
    private Phase _phase = Phase.WalkingIn;
    private readonly bool _enteredFromLeft;
    private int _direction;
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

        _stayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, staySeconds)) };
        _stayTimer.Tick += (_, _) => Leave(GooseOutcome.TimedOut);
        _stayUntilDismissed = staySeconds <= 0;

        _enteredFromLeft = Random.Shared.Next(2) == 0;
        _direction = _enteredFromLeft ? 1 : -1;
        Top = _area.Bottom - Height;
        Left = _enteredFromLeft ? _area.Left - Width : _area.Right;

        // Stop somewhere on the side it came from instead of crossing the whole taskbar.
        double edgeMargin = _area.Width * 0.03;
        double reach = Random.Shared.NextDouble() * _area.Width * 0.2;
        double target = _enteredFromLeft
            ? _area.Left + edgeMargin + reach
            : _area.Right - Width - edgeMargin - reach;
        _targetLeft = Math.Clamp(target, _area.Left, Math.Max(_area.Left, _area.Right - Width));
        Face(_direction);

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
                Walk(dt);
                if ((_direction > 0 && Left >= _targetLeft) || (_direction < 0 && Left <= _targetLeft))
                {
                    Left = _targetLeft;
                    StartTalking();
                }
                break;

            case Phase.Talking:
                // Idle: a gentle head bob while waiting for an answer.
                HeadTilt.Angle = Math.Sin(now.TotalSeconds * 2.5) * 4;
                break;

            case Phase.WalkingOut:
                Walk(dt);
                if (Left > _area.Right || Left + Width < _area.Left)
                {
                    _phase = Phase.WalkingIn; // stop moving while closing
                    CompositionTarget.Rendering -= OnRendering;
                    Close();
                }
                break;
        }
    }

    private void Walk(double dt)
    {
        Left += _direction * WalkSpeed * dt;
        _stepPhase += dt * StepRate;
        double swing = Math.Sin(_stepPhase);
        LegBackRotate.Angle = swing * 28;
        LegFrontRotate.Angle = -swing * 28;
        BodyBob.Y = -Math.Abs(swing) * 3;
    }

    private void StartTalking()
    {
        _phase = Phase.Talking;
        LegBackRotate.Angle = 0;
        LegFrontRotate.Angle = 0;
        BodyBob.Y = 0;

        Bubble.Visibility = Visibility.Visible;
        var pop = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(350))
        {
            EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut },
        };
        BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

        if (!_stayUntilDismissed) _stayTimer.Start();
    }

    private void Leave(GooseOutcome outcome)
    {
        if (_phase != Phase.Talking) return;
        _outcome = outcome;
        _stayTimer.Stop();
        Bubble.Visibility = Visibility.Collapsed;
        HeadTilt.Angle = 0;

        // Go back out the way it came in.
        _direction = _enteredFromLeft ? -1 : 1;
        Face(_direction);
        _phase = Phase.WalkingOut;
    }

    private void Face(int direction) => GooseFlip.ScaleX = direction;

    private void Dismiss_Click(object sender, RoutedEventArgs e) => Leave(GooseOutcome.Dismissed);

    private void Snooze_Click(object sender, RoutedEventArgs e) => Leave(GooseOutcome.Snoozed);

    private void Goose_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Poking the goose makes it flap its head indignantly.
        var honk = new DoubleAnimation(0, -18, TimeSpan.FromMilliseconds(90))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(2),
        };
        HeadTilt.BeginAnimation(RotateTransform.AngleProperty, honk);
        honk.Completed += (_, _) => HeadTilt.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
