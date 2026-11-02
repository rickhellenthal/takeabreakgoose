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
/// A transparent, always-on-top window that walks a goose in from a screen edge,
/// shows a break reminder, then walks back out. If the user needs to finish up first,
/// the goose waits peeking around the screen edge until it's clicked. If the user takes
/// the break, the goose naps on the taskbar until the break is over and the user is back,
/// then asks whether they're ready to focus again. If the computer is used during the nap,
/// the goose half wakes up, and asks whether they're really taking their break if it goes on.
/// </summary>
public partial class GooseWindow : Window
{
    private enum Phase { WalkingIn, Talking, ToEdge, Peeking, Returning, Sleeping, BreakOver, WalkingOut }

    /// <summary>What the speech bubble is asking, which decides what its two buttons do.</summary>
    private enum Question { TakeABreak, ReadyToFocus, EndBreakEarly, StillOnBreak, EndBreakAnyway }

    private static readonly string[] EndBreakEarlyLines =
    {
        "Cutting your break short? Bold move. Your future self will remember this.",
        "Back already? The break isn't over yet, but who am I to judge. (I'm judging.)",
        "Honk. Some of us take our breaks very seriously.",
        "Fine, fine. But don't come crying to me when your focus runs out.",
        "You woke me up for this? Your brain was enjoying that.",
    };

    private static readonly string[] StillOnBreakLines =
    {
        "I can hear you clicking. Are you actually taking your break?",
        "That doesn't sound like a break to me. Go stretch your legs!",
        "Still at the computer? Your break is waiting for you over there, away from the screen.",
        "Breaks work better when you get up. Just saying.",
        "Honk. I can't nap with all that typing going on.",
    };

    private static readonly string[] EndBreakAnywayLines =
    {
        "I'm not mad. I'm just disappointed.",
        "Wow. Okay. I'll just sit here and remember this.",
        "Ending your break to keep working? Bold. Your back is going to file a complaint.",
        "Your break, your call. I'm just a goose who believed in you.",
        "Fine. But don't come honking to me when your focus runs out.",
    };

    // Using the computer during the break first half wakes the goose, then it asks.
    private static readonly TimeSpan GraceAfterNapStarts = TimeSpan.FromSeconds(5);   // time to get up and go
    private const double UsedForSecondsBeforeAsking = 6;
    private static readonly TimeSpan IdleBeforeDozingOff = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan IdleBeforeGivingUpAsking = TimeSpan.FromSeconds(20);

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
    private DateTime _watchForUseFrom;
    private double _usedDuringBreakSeconds;

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

    /// <summary>The goose came to ask for a break and the user hasn't taken it yet (including "let me finish up").</summary>
    public bool StillAskingForBreak =>
        _phase is Phase.WalkingIn or Phase.ToEdge or Phase.Peeking or Phase.Returning
        || (_phase == Phase.Talking && _question == Question.TakeABreak);

    /// <summary>The user is on the break this goose asked for, or is just back from it.</summary>
    public bool OnBreak =>
        _phase is Phase.Sleeping or Phase.BreakOver
        || (_phase == Phase.Talking
            && _question is Question.ReadyToFocus or Question.EndBreakEarly
                or Question.StillOnBreak or Question.EndBreakAnyway);

    /// <summary>Disappears right away (quick fade), e.g. when the user turns on Do not disturb.</summary>
    public void LeaveNow()
    {
        CompositionTarget.Rendering -= OnRendering;
        _phase = Phase.WalkingOut;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// Stops whatever it's doing (asking, peeking, napping) and walks off the screen,
    /// e.g. when the user stops the session. Raises none of the answer events.
    /// </summary>
    public void WalkAway()
    {
        if (_phase == Phase.WalkingOut) return;
        StopTalking();
        BreakCard.Visibility = Visibility.Collapsed;
        Figure.Sleeping = false;
        Figure.ToolTip = null;
        _phase = Phase.WalkingOut;
    }

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
                // Idle: a gentle head bob while waiting for an answer (standing up; sitting, it keeps its pose).
                if (!Figure.Sleeping)
                    Figure.HeadAngle = (Figure.Huffy ? HuffyHeadAngle / 2 : 0) + Math.Sin(now.TotalSeconds * 2.5) * 4;
                // If the break runs out while the goose asks about it, the question changes.
                if (_question is Question.EndBreakEarly or Question.StillOnBreak or Question.EndBreakAnyway
                    && DateTime.Now >= _breakEnd) AskIfReady();
                if (_question == Question.TakeABreak) WatchForBreakWhileAway();
                // No answer because they did get up after all: back to the nap, without a fuss.
                if (_question is Question.StillOnBreak or Question.EndBreakAnyway
                    && UserActivity.IdleTime() >= IdleBeforeGivingUpAsking)
                    ResumeNap();
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
                else WatchForUseDuringBreak(dt);
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
        WatchForUseAfter(GraceAfterNapStarts);

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

    private void WatchForUseAfter(TimeSpan grace)
    {
        _watchForUseFrom = DateTime.Now + grace;
        _usedDuringBreakSeconds = 0;
    }

    /// <summary>
    /// Mid-nap: using the computer half wakes the goose (head up, one eye open). Keep at it and it
    /// asks whether you're really taking your break; leave it alone and it dozes off again.
    /// </summary>
    private void WatchForUseDuringBreak(double dt)
    {
        var now = DateTime.Now;
        if (now < _watchForUseFrom) return;
        var idle = UserActivity.IdleTime();
        if (idle < TimeSpan.FromSeconds(1) && idle < now - _watchForUseFrom)
        {
            Figure.Look = NapLook.Drowsy;
            _usedDuringBreakSeconds += dt;
            if (_usedDuringBreakSeconds >= UsedForSecondsBeforeAsking) AskIfStillOnBreak();
        }
        else if (idle >= IdleBeforeDozingOff)
        {
            Figure.Look = NapLook.Asleep;
            _usedDuringBreakSeconds = 0;
        }
    }

    /// <summary>Still sitting, half awake: "are you actually taking your break?"</summary>
    private void AskIfStillOnBreak()
    {
        Figure.Look = NapLook.Drowsy;
        Figure.ToolTip = null;
        BreakCard.Visibility = Visibility.Collapsed;

        string message = StillOnBreakLines[Random.Shared.Next(StillOnBreakLines.Length)];
        // Getting up is the highlighted choice, on purpose.
        Ask(Question.StillOnBreak, "Psst…", message,
            hint: $"There's still {FormatTimeLeft(_breakEnd - DateTime.Now)} of your break left.",
            primary: "I'm going!", secondary: "End my break");
    }

    /// <summary>They'd rather end the break: the goose checks once more, not hiding what it thinks of that.</summary>
    private void AskToEndBreakAnyway()
    {
        // Sitting (it caught you working) or standing (you woke it up), it's not impressed either way.
        if (Figure.Sleeping) Figure.Look = NapLook.Unimpressed;
        else Figure.Huffy = true;
        string message = EndBreakAnywayLines[Random.Shared.Next(EndBreakAnywayLines.Length)];
        Ask(Question.EndBreakAnyway, "Really?", message, hint: null,
            primary: "Fine, I'll go", secondary: "Yes, end my break");
    }

    /// <summary>The break time is up; the goose keeps napping until the user is back.</summary>
    private void BreakIsOver()
    {
        Figure.Look = NapLook.Asleep;
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
        Figure.Huffy = false; // the break ran out while it was sulking: all is forgiven
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
    private void ConfirmReady(bool endedEarly = false)
    {
        StopTalking();
        Figure.Sleeping = false; // it may still be sitting down
        if (endedEarly)
        {
            // Off it goes in a huff: beak in the air, not looking back.
            Figure.Huffy = true;
            Figure.HeadAngle = HuffyHeadAngle;
        }
        ReadyToFocus?.Invoke();
        _phase = Phase.WalkingOut;
    }

    private const double HuffyHeadAngle = -14;

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
        Figure.Look = NapLook.Asleep; // it may still be sitting there half awake
        Figure.ToolTip = NapToolTip;
        WatchForUseAfter(GraceAfterNapStarts);
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
            case Question.StillOnBreak:
            case Question.EndBreakAnyway: ResumeNap(); break;
        }
    }

    // The secondary (outlined) button.
    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        switch (_question)
        {
            case Question.TakeABreak: Leave(GooseOutcome.Snoozed); break;
            case Question.ReadyToFocus: NapLonger(); break;
            case Question.StillOnBreak:
            case Question.EndBreakEarly: AskToEndBreakAnyway(); break;
            case Question.EndBreakAnyway: ConfirmReady(endedEarly: true); break;
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
        // Poking the goose makes it flap its head indignantly (not while it's sitting with its head in a pose).
        if (!Figure.Sleeping) Figure.Flap();
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
