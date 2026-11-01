using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TakeABreakGoose;

/// <summary>What the goose says during one visit. A null outfit means a random one.</summary>
/// <param name="JustVisiting">Not a reminder (e.g. showing off an outfit), so it leaves the session's timer alone.</param>
public sealed record GooseVisit(
    string Title,
    string Message,
    string? Hint,
    string DismissText,
    bool AllowSnooze,
    bool AllowFinishUp,
    bool StartsBreak,
    GooseOutfit? Outfit = null,
    bool JustVisiting = false);

public enum GooseOutcome { Dismissed, Snoozed }

/// <summary>
/// A transparent, always-on-top window that walks a goose in from a screen edge,
/// shows a break reminder, then walks back out. If the user needs to finish up first,
/// the goose waits peeking around the screen edge until it's clicked. If the user takes
/// the break, the goose naps on the taskbar until the break is over and the user is back,
/// then asks whether they're ready to focus again.
/// </summary>
public partial class GooseWindow : Window
{
    private enum Phase { WalkingIn, Talking, ToEdge, Peeking, Returning, Sleeping, BreakOver, WalkingOut }

    /// <summary>What the speech bubble is asking, which decides what its two buttons do.</summary>
    private enum Question { TakeABreak, ReadyToFocus, EndBreakEarly }

    private static readonly string[] EndBreakEarlyLines =
    {
        "Cutting your break short? Bold move. Your future self will remember this.",
        "Back already? The break isn't over yet, but who am I to judge. (I'm judging.)",
        "Honk. Some of us take our breaks very seriously.",
        "Fine, fine. But don't come crying to me when your focus runs out.",
        "You woke me up for this? Your brain was enjoying that.",
    };

    private const double WalkSpeed = 150;   // DIPs per second
    private const double StepRate = 12;     // leg swing speed, radians per second
    private const double PeekVisible = 40;  // how much of the goose shows while peeking
    private const double PeekHoverVisible = 58;

    private readonly Rect _area = SystemParameters.WorkArea;
    private readonly Stopwatch _clock = new();
    private readonly bool _enteredFromLeft;
    private TimeSpan _lastFrame;
    private Phase _phase = Phase.WalkingIn;
    private double _targetLeft;
    private double _stepPhase;
    private readonly TimeSpan? _breakLength;
    private readonly int _snoozeMinutes;
    private DateTime _breakStart;
    private DateTime _breakEnd;
    private Question _question = Question.TakeABreak;
    private bool _awayLongEnoughForABreak;
    private DateTime _askedAt;

    /// <summary>Raised as soon as the user answers the goose.</summary>
    public event Action<GooseOutcome>? Answered;

    /// <summary>Raised when the user asks for a few more minutes of break, with the new end time.</summary>
    public event Action<DateTime>? BreakExtended;

    /// <summary>Raised when the user says they're back from the break and ready to focus.</summary>
    public event Action? ReadyToFocus;

    /// <summary>
    /// Raised when the user was away for at least a break's length while the goose asked them to
    /// take one, so that time counts as the break.
    /// </summary>
    public event Action? TookBreakWhileAway;

    /// <summary>Raised once the goose has left the screen.</summary>
    public event Action? Finished;

    /// <param name="breakLength">If set, the goose naps for this long when the user takes the break.</param>
    public GooseWindow(GooseVisit visit, int snoozeMinutes, TimeSpan? breakLength)
    {
        _breakLength = breakLength;
        _snoozeMinutes = snoozeMinutes;
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
                // If the break runs out while the goose asks about ending it early, the question changes.
                if (_question == Question.EndBreakEarly && DateTime.Now >= _breakEnd) AskIfReady();
                if (_question == Question.TakeABreak) WatchForBreakWhileAway();
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
                if (DateTime.Now >= _breakEnd) BreakIsOver();
                break;

            case Phase.BreakOver:
                // Keep napping until the computer is used again after the break ended.
                if (UserActivity.IdleTime() < DateTime.Now - _breakEnd) AskIfReady();
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

    /// <summary>Shows the speech bubble. The goose waits as long as it takes for an answer.</summary>
    private void StartTalking()
    {
        _phase = Phase.Talking;
        _askedAt = DateTime.Now;
        _awayLongEnoughForABreak = false;
        Figure.Facing = InwardDirection;
        Bubble.Visibility = Visibility.Visible;
        var pop = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(350))
        {
            EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut },
        };
        BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    private void StopTalking()
    {
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

    private void StartSleeping(TimeSpan length, string title = "Enjoy your break")
    {
        _phase = Phase.Sleeping;
        _breakStart = DateTime.Now;
        _breakEnd = _breakStart + length;
        Figure.Sleeping = true;
        Figure.ToolTip = NapToolTip;

        BreakTitle.Text = title;
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
        BreakTimeText.Text = $"{FormatTimeLeft(left)} left";
    }

    private static string FormatTimeLeft(TimeSpan left)
    {
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        return left.TotalHours >= 1 ? $"{(int)left.TotalHours}:{left:mm\\:ss}" : $"{left:m\\:ss}";
    }

    private string NapToolTip => $"Napping until {_breakEnd:HH:mm}. Click me if you need to end your break early.";

    /// <summary>The break time is up; the goose keeps napping until the user is back.</summary>
    private void BreakIsOver()
    {
        _phase = Phase.BreakOver;
        Figure.ToolTip = "Break's over. I'll wake up when you're back.";
        BreakRing.Data = RingArc(1, center: 18, radius: 16);
        BreakTitle.Text = "Break's over";
        BreakTimeText.Text = "Waiting for you to come back";
    }

    /// <summary>The user is back: wake up and ask whether they're ready to focus again.</summary>
    private void AskIfReady(string message = "Break's over. Ready to focus again?")
    {
        Figure.Sleeping = false;
        Figure.ToolTip = null;
        BreakCard.Visibility = Visibility.Collapsed;

        // The next focus stretch only starts once the user says so.
        Ask(Question.ReadyToFocus, "Welcome back!", message, hint: null,
            primary: "Ready to focus", secondary: $"{_snoozeMinutes} more minutes");
    }

    /// <summary>
    /// While the goose asks the user to take a break: if they were away from the mouse and keyboard
    /// for at least a break's length, they've had their break, so once they're back the goose
    /// skips ahead to asking whether they're ready to focus. Only time since the goose asked counts;
    /// sitting still before it showed up (reading, watching a video) isn't a break.
    /// </summary>
    private void WatchForBreakWhileAway()
    {
        if (_breakLength is not { } length || length <= TimeSpan.Zero) return;
        var idle = UserActivity.IdleTime();
        var sinceAsked = DateTime.Now - _askedAt;
        var awayWhileAsking = idle < sinceAsked ? idle : sinceAsked;
        if (awayWhileAsking >= length)
        {
            _awayLongEnoughForABreak = true;
        }
        else if (_awayLongEnoughForABreak && idle < TimeSpan.FromSeconds(2))
        {
            _awayLongEnoughForABreak = false;
            StopTalking();
            TookBreakWhileAway?.Invoke();
            AskIfReady("Looks like you already took a break while I waited. Ready to focus again?");
        }
    }

    /// <summary>The goose was clicked mid-nap: wake up (a bit grumpy) and check before ending the break.</summary>
    private void AskToEndBreakEarly()
    {
        Figure.Sleeping = false;
        Figure.ToolTip = null;
        BreakCard.Visibility = Visibility.Collapsed;

        var left = _breakEnd - DateTime.Now;
        string message = EndBreakEarlyLines[Random.Shared.Next(EndBreakEarlyLines.Length)];
        // Resting is the highlighted choice, on purpose.
        Ask(Question.EndBreakEarly, "Hey! I was napping…", message,
            hint: $"There's still {FormatTimeLeft(left)} of your break left.",
            primary: "Keep resting", secondary: "End my break");
    }

    private void Ask(Question question, string title, string message, string? hint, string primary, string secondary)
    {
        _question = question;
        TitleText.Text = title;
        MessageText.Text = message;
        HintText.Text = hint ?? "";
        HintText.Visibility = hint == null ? Visibility.Collapsed : Visibility.Visible;
        FinishUpButton.Visibility = Visibility.Collapsed;
        DismissButton.Content = primary;
        SnoozeButton.Content = secondary;
        SnoozeButton.Visibility = Visibility.Visible;
        StartTalking();
    }

    /// <summary>The user is back (or cut the break short) and ready to focus: off the goose goes.</summary>
    private void ConfirmReady()
    {
        StopTalking();
        ReadyToFocus?.Invoke();
        _phase = Phase.WalkingOut;
    }

    private void NapLonger()
    {
        StopTalking();
        StartSleeping(TimeSpan.FromMinutes(_snoozeMinutes), "A little longer, then");
        BreakExtended?.Invoke(_breakEnd);
    }

    /// <summary>Back to sleep for the rest of the break that was interrupted.</summary>
    private void ResumeNap()
    {
        StopTalking();
        _phase = Phase.Sleeping;
        Figure.Sleeping = true;
        Figure.ToolTip = NapToolTip;
        UpdateBreakProgress();
        BreakCard.Visibility = Visibility.Visible;
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

    // The primary (orange) button.
    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        switch (_question)
        {
            case Question.TakeABreak: Leave(GooseOutcome.Dismissed); break;
            case Question.ReadyToFocus: ConfirmReady(); break;
            case Question.EndBreakEarly: ResumeNap(); break;
        }
    }

    // The secondary (outlined) button.
    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        switch (_question)
        {
            case Question.TakeABreak: Leave(GooseOutcome.Snoozed); break;
            case Question.ReadyToFocus: NapLonger(); break;
            case Question.EndBreakEarly: ConfirmReady(); break;
        }
    }

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
        if (_phase == Phase.Sleeping)
        {
            AskToEndBreakEarly();
            return;
        }
        if (_phase == Phase.BreakOver)
        {
            AskIfReady();
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
