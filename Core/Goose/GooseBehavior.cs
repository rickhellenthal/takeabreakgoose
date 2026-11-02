using System;

namespace TakeABreakGoose;

public enum GoosePhase { WalkingIn, Talking, ToEdge, Peeking, Returning, Sleeping, BreakOver, WalkingOut }

/// <summary>What the speech bubble is asking, which decides what its two buttons do.</summary>
public enum GooseQuestion { TakeABreak, ReadyToFocus, EndBreakEarly, StillOnBreak, EndBreakAnyway }

/// <summary>
/// What the goose does during one visit: walks in from a screen edge, shows a break reminder, then
/// walks back out. If the user needs to finish up first, the goose waits peeking around the screen
/// edge until it's clicked. If the user takes the break, the goose naps until the break is over and
/// the user is back, then asks whether they're ready to focus again. If the computer is used during
/// the nap, the goose half wakes up, and asks whether they're really taking their break if it goes on.
/// Each app's goose window draws it (<see cref="IGooseView"/>) and passes on its frames and clicks.
/// </summary>
public sealed class GooseBehavior
{
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

    private const double WalkSpeed = 150;   // DIPs per second
    private const double StepRate = 12;     // leg swing speed, radians per second
    private const double PeekVisible = 40;  // how much of the goose shows while peeking
    private const double PeekHoverVisible = 58;
    private const double HuffyHeadAngle = -14;

    private readonly IGooseView _view;
    private readonly GooseStage _stage;
    private readonly IClock _clock;
    private readonly IUserActivity _activity;
    private readonly Random _random;
    private readonly bool _enteredFromLeft;
    private TimeSpan _lastFrame;
    private double _left;
    private double _targetLeft;
    private double _stepPhase;
    private readonly TimeSpan? _breakLength;
    private readonly int _snoozeMinutes;
    private DateTime _breakStart;
    private DateTime _breakEnd;
    private SpeechBubble _bubble;
    private bool _awayLongEnoughForABreak;
    private DateTime _awaySince;
    private DateTime _askedAt;
    private readonly NapWatch _napWatch;

    /// <summary>Raised as soon as the user answers the goose.</summary>
    public event Action<GooseOutcome>? Answered;

    /// <summary>Raised when the user asks for a few more minutes of break, with the new end time.</summary>
    public event Action<DateTime>? BreakExtended;

    /// <summary>Raised when the user says they're back from the break and ready to focus.</summary>
    public event Action? ReadyToFocus;

    /// <summary>
    /// Raised when the user was away for at least a break's length while the goose asked them to
    /// take one, so that time counts as the break. Comes with when they left the computer.
    /// </summary>
    public event Action<DateTime>? TookBreakWhileAway;

    /// <summary>Raised once the goose has left the screen.</summary>
    public event Action? Finished;

    /// <param name="breakLength">If set, the goose naps for this long when the user takes the break.</param>
    /// <param name="random">Which side it comes from, how far it walks in and what it says.</param>
    public GooseBehavior(IGooseView view, GooseStage stage, GooseVisit visit, int snoozeMinutes, TimeSpan? breakLength,
        IClock clock, IUserActivity activity, Random random)
    {
        _view = view;
        _stage = stage;
        _clock = clock;
        _activity = activity;
        _random = random;
        _breakLength = breakLength;
        _snoozeMinutes = snoozeMinutes;
        _napWatch = new NapWatch(clock.Now);

        _bubble = new SpeechBubble(visit.Title, visit.Message, visit.Hint, visit.DismissText,
            // Finishing up is the better answer than a snooze timer, so it gets the button and snooze the link.
            SecondaryButton: visit.AllowFinishUp ? "Let me finish up" : null,
            LinkButton: visit.AllowSnooze ? $"Snooze {snoozeMinutes} min" : null);

        _enteredFromLeft = random.Next(2) == 0;
        // Start with the goose itself just off screen (the window is wider than the goose).
        Left = _enteredFromLeft ? stage.AreaLeft - FigureRight : stage.AreaRight - FigureLeft;

        // Stop a short walk in from the side it came from. The window stays on screen,
        // so the speech bubble is never cut off.
        double reach = random.NextDouble() * (stage.AreaRight - stage.AreaLeft) * 0.08;
        double target = _enteredFromLeft
            ? stage.AreaLeft + reach
            : stage.AreaRight - stage.WindowWidth - reach;
        _targetLeft = Math.Clamp(target, stage.AreaLeft, Math.Max(stage.AreaLeft, stage.AreaRight - stage.WindowWidth));
        _view.Facing = InwardDirection;
    }

    public GoosePhase Phase { get; private set; } = GoosePhase.WalkingIn;

    public GooseQuestion Question { get; private set; } = GooseQuestion.TakeABreak;

    /// <summary>The window's left edge on screen, in DIPs.</summary>
    private double Left
    {
        get => _left;
        set
        {
            _left = value;
            _view.WindowLeft = value;
        }
    }

    /// <summary>The direction pointing from the goose's edge into the screen.</summary>
    private int InwardDirection => _enteredFromLeft ? 1 : -1;

    // Where the goose figure sits horizontally inside the window.
    private double FigureLeft => (_stage.WindowWidth - _stage.FigureWidth) / 2;
    private double FigureRight => FigureLeft + _stage.FigureWidth;

    /// <summary>The goose came to ask for a break and the user hasn't taken it yet (including "let me finish up").</summary>
    public bool StillAskingForBreak =>
        Phase is GoosePhase.WalkingIn or GoosePhase.ToEdge or GoosePhase.Peeking or GoosePhase.Returning
        || (Phase == GoosePhase.Talking && Question == GooseQuestion.TakeABreak);

    /// <summary>The user is on the break this goose asked for, or is just back from it.</summary>
    public bool OnBreak =>
        Phase is GoosePhase.Sleeping or GoosePhase.BreakOver
        || (Phase == GoosePhase.Talking
            && Question is GooseQuestion.ReadyToFocus or GooseQuestion.EndBreakEarly
                or GooseQuestion.StillOnBreak or GooseQuestion.EndBreakAnyway);

    /// <summary>Disappears right away (quick fade), e.g. when the user turns on Do not disturb.</summary>
    public void LeaveNow()
    {
        Phase = GoosePhase.WalkingOut;
        _view.FadeOutAndClose();
    }

    /// <summary>
    /// Stops whatever it's doing (asking, peeking, napping) and walks off the screen,
    /// e.g. when the user stops the session. Raises none of the answer events.
    /// </summary>
    public void WalkAway()
    {
        if (Phase == GoosePhase.WalkingOut) return;
        StopTalking();
        _view.HideBreakCard();
        _view.Sleeping = false;
        _view.FigureToolTip = null;
        Phase = GoosePhase.WalkingOut;
    }

    /// <summary>The window has closed.</summary>
    public void WindowClosed() => Finished?.Invoke();

    /// <summary>Called once per frame while the window is open, with the time since it opened.</summary>
    public void NextFrame(TimeSpan now)
    {
        double dt = Math.Min((now - _lastFrame).TotalSeconds, 0.05);
        _lastFrame = now;

        switch (Phase)
        {
            case GoosePhase.WalkingIn:
            case GoosePhase.Returning:
                if (WalkTowards(_targetLeft, dt)) StartTalking();
                break;

            case GoosePhase.Talking:
                // Idle: a gentle head bob while waiting for an answer (standing up; sitting, it keeps its pose).
                if (!_view.Sleeping)
                    _view.HeadAngle = (_view.Huffy ? HuffyHeadAngle / 2 : 0) + Math.Sin(now.TotalSeconds * 2.5) * 4;
                // If the break runs out while the goose asks about it, the question changes.
                if (Question is GooseQuestion.EndBreakEarly or GooseQuestion.StillOnBreak or GooseQuestion.EndBreakAnyway
                    && _clock.Now >= _breakEnd) AskIfReady();
                if (Question == GooseQuestion.TakeABreak) WatchForBreakWhileAway();
                // No answer because they did get up after all: back to the nap, without a fuss.
                if (Question is GooseQuestion.StillOnBreak or GooseQuestion.EndBreakAnyway
                    && NapWatch.GaveUpAsking(_activity.IdleTime()))
                    ResumeNap();
                break;

            case GoosePhase.ToEdge:
                if (WalkTowards(PeekLeft(PeekVisible), dt))
                {
                    Phase = GoosePhase.Peeking;
                    _view.Facing = InwardDirection;
                    _view.FigureToolTip = "Click me when you're ready for your break";
                }
                break;

            case GoosePhase.Peeking:
                // Lean out a bit further while the mouse is near the goose.
                double peekLeft = PeekLeft(IsCursorNearPeek() ? PeekHoverVisible : PeekVisible);
                Left += (peekLeft - Left) * Math.Min(1, dt * 10);
                _view.HeadAngle = Math.Sin(now.TotalSeconds * 1.6) * 6;
                break;

            case GoosePhase.Sleeping:
                UpdateBreakProgress();
                if (_clock.Now >= _breakEnd) BreakIsOver();
                else WatchForUseDuringBreak(dt);
                break;

            case GoosePhase.BreakOver:
                // Keep napping until the computer is used again after the break ended.
                if (_activity.IdleTime() < _clock.Now - _breakEnd) AskIfReady();
                break;

            case GoosePhase.WalkingOut:
                Walk(-InwardDirection, dt);
                if (Left + FigureLeft > _stage.AreaRight || Left + FigureRight < _stage.AreaLeft)
                {
                    Phase = GoosePhase.WalkingIn; // stop moving while closing
                    _view.CloseWindow();
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
        _view.SetStride(0);
        return true;
    }

    private void Walk(int direction, double dt)
    {
        _view.Facing = direction;
        Left += direction * WalkSpeed * dt;
        _stepPhase += dt * StepRate;
        _view.SetStride(Math.Sin(_stepPhase));
    }

    /// <summary>The window position at which only the front of the goose shows past the screen edge.</summary>
    private double PeekLeft(double visible)
    {
        return _enteredFromLeft
            ? _stage.AreaLeft + visible - (FigureLeft + _stage.BeakTipX)
            : _stage.AreaRight - visible - (FigureRight - _stage.BeakTipX);
    }

    /// <summary>
    /// Whether the cursor is in the strip at the screen edge where the goose peeks. Checked against
    /// a fixed area rather than mouse enter/leave, because the transparent gaps around the goose
    /// would otherwise slide under the cursor as it leans out and make it bounce back and forth.
    /// </summary>
    private bool IsCursorNearPeek()
    {
        double reach = PeekHoverVisible + 10;
        return _enteredFromLeft
            ? _view.IsCursorBetween(_stage.AreaLeft, _stage.AreaLeft + reach)
            : _view.IsCursorBetween(_stage.AreaRight - reach, _stage.AreaRight);
    }

    /// <summary>Shows the speech bubble. The goose waits as long as it takes for an answer.</summary>
    private void StartTalking()
    {
        Phase = GoosePhase.Talking;
        _askedAt = _clock.Now;
        _awayLongEnoughForABreak = false;
        _view.Facing = InwardDirection;
        _view.ShowBubble(_bubble);
    }

    private void StopTalking()
    {
        _view.HideBubble();
        _view.HeadAngle = 0;
    }

    private void Leave(GooseOutcome outcome)
    {
        if (Phase != GoosePhase.Talking) return;
        StopTalking();
        Answered?.Invoke(outcome);

        if (outcome == GooseOutcome.Dismissed && _breakLength is { } length && length > TimeSpan.Zero)
            StartSleeping(length);
        else
            Phase = GoosePhase.WalkingOut;
    }

    private void StartSleeping(TimeSpan length, string title = "Enjoy your break")
    {
        Phase = GoosePhase.Sleeping;
        _breakStart = _clock.Now;
        _breakEnd = _breakStart + length;
        _view.Sleeping = true;
        _view.FigureToolTip = NapToolTip;
        _napWatch.Restart(_clock.Now);

        _view.BreakTitle = title;
        UpdateBreakProgress();
        _view.ShowBreakCard(popUp: true);
    }

    private void UpdateBreakProgress()
    {
        var total = _breakEnd - _breakStart;
        var left = _breakEnd - _clock.Now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        double progress = total > TimeSpan.Zero ? 1 - left / total : 1;

        _view.ShowBreakProgress(progress, $"{FormatTimeLeft(left)} left");
    }

    private static string FormatTimeLeft(TimeSpan left)
    {
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        return left.TotalHours >= 1 ? $"{(int)left.TotalHours}:{left:mm\\:ss}" : $"{left:m\\:ss}";
    }

    private string NapToolTip => $"Napping until {_breakEnd:HH:mm}. Click me if you need to end your break early.";

    /// <summary>
    /// Mid-nap: using the computer half wakes the goose (head up, one eye open). Keep at it and it
    /// asks whether you're really taking your break; leave it alone and it dozes off again.
    /// </summary>
    private void WatchForUseDuringBreak(double dt)
    {
        switch (_napWatch.Update(_clock.Now, _activity.IdleTime(), TimeSpan.FromSeconds(dt)))
        {
            case NapWatchState.Asleep: _view.Look = NapLook.Asleep; break;
            case NapWatchState.Stirring: _view.Look = NapLook.Drowsy; break;
            case NapWatchState.ShouldAsk: AskIfStillOnBreak(); break;
        }
    }

    /// <summary>Still sitting, half awake: "are you actually taking your break?"</summary>
    private void AskIfStillOnBreak()
    {
        _view.Look = NapLook.Drowsy;
        _view.FigureToolTip = null;
        _view.HideBreakCard();

        string message = StillOnBreakLines[_random.Next(StillOnBreakLines.Length)];
        // Getting up is the highlighted choice, on purpose.
        Ask(GooseQuestion.StillOnBreak, "Psst…", message,
            hint: $"There's still {FormatTimeLeft(_breakEnd - _clock.Now)} of your break left.",
            primary: "I'm going!", secondary: "End my break");
    }

    /// <summary>They'd rather end the break: the goose checks once more, not hiding what it thinks of that.</summary>
    private void AskToEndBreakAnyway()
    {
        // Sitting (it caught you working) or standing (you woke it up), it's not impressed either way.
        if (_view.Sleeping) _view.Look = NapLook.Unimpressed;
        else _view.Huffy = true;
        string message = EndBreakAnywayLines[_random.Next(EndBreakAnywayLines.Length)];
        Ask(GooseQuestion.EndBreakAnyway, "Really?", message, hint: null,
            primary: "Fine, I'll go", secondary: "Yes, end my break");
    }

    /// <summary>The break time is up; the goose keeps napping until the user is back.</summary>
    private void BreakIsOver()
    {
        _view.Look = NapLook.Asleep;
        Phase = GoosePhase.BreakOver;
        _view.FigureToolTip = "Break's over. I'll wake up when you're back.";
        _view.ShowBreakProgress(1, "Waiting for you to come back");
        _view.BreakTitle = "Break's over";
    }

    /// <summary>The user is back: wake up and ask whether they're ready to focus again.</summary>
    private void AskIfReady(string message = "Break's over. Ready to focus again?")
    {
        _view.Sleeping = false;
        _view.Huffy = false; // the break ran out while it was sulking: all is forgiven
        _view.FigureToolTip = null;
        _view.HideBreakCard();

        // The next focus stretch only starts once the user says so.
        Ask(GooseQuestion.ReadyToFocus, "Welcome back!", message, hint: null,
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
        var idle = _activity.IdleTime();
        var sinceAsked = _clock.Now - _askedAt;
        var awayWhileAsking = idle < sinceAsked ? idle : sinceAsked;
        if (awayWhileAsking >= length)
        {
            if (!_awayLongEnoughForABreak) _awaySince = _clock.Now - awayWhileAsking;
            _awayLongEnoughForABreak = true;
        }
        else if (_awayLongEnoughForABreak && idle < TimeSpan.FromSeconds(2))
        {
            _awayLongEnoughForABreak = false;
            StopTalking();
            TookBreakWhileAway?.Invoke(_awaySince);
            AskIfReady("Looks like you already took a break while I waited. Ready to focus again?");
        }
    }

    /// <summary>The goose was clicked mid-nap: wake up (a bit grumpy) and check before ending the break.</summary>
    private void AskToEndBreakEarly()
    {
        _view.Sleeping = false;
        _view.FigureToolTip = null;
        _view.HideBreakCard();

        var left = _breakEnd - _clock.Now;
        string message = EndBreakEarlyLines[_random.Next(EndBreakEarlyLines.Length)];
        // Resting is the highlighted choice, on purpose.
        Ask(GooseQuestion.EndBreakEarly, "Hey! I was napping…", message,
            hint: $"There's still {FormatTimeLeft(left)} of your break left.",
            primary: "Keep resting", secondary: "End my break");
    }

    private void Ask(GooseQuestion question, string title, string message, string? hint, string primary, string secondary)
    {
        Question = question;
        _bubble = new SpeechBubble(title, message, hint, primary, secondary, LinkButton: null);
        StartTalking();
    }

    /// <summary>The user is back (or cut the break short) and ready to focus: off the goose goes.</summary>
    private void ConfirmReady(bool endedEarly = false)
    {
        StopTalking();
        _view.Sleeping = false; // it may still be sitting down
        if (endedEarly)
        {
            // Off it goes in a huff: beak in the air, not looking back.
            _view.Huffy = true;
            _view.HeadAngle = HuffyHeadAngle;
        }
        ReadyToFocus?.Invoke();
        Phase = GoosePhase.WalkingOut;
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
        Phase = GoosePhase.Sleeping;
        _view.Sleeping = true;
        _view.Look = NapLook.Asleep; // it may still be sitting there half awake
        _view.FigureToolTip = NapToolTip;
        _napWatch.Restart(_clock.Now);
        UpdateBreakProgress();
        _view.ShowBreakCard(popUp: false);
    }

    /// <summary>"Let me finish up": the goose waits at the edge of the screen until it's clicked.</summary>
    private void FinishUp()
    {
        if (Phase != GoosePhase.Talking) return;
        StopTalking();
        // It only offers this once; when it comes back it's break time.
        _bubble = _bubble with { SecondaryButton = null };
        Phase = GoosePhase.ToEdge;
    }

    /// <summary>The quiet link on the left: only there when it's time for a break, to snooze.</summary>
    public void LinkClicked()
    {
        if (Question == GooseQuestion.TakeABreak) Leave(GooseOutcome.Snoozed);
    }

    /// <summary>The primary (orange) button.</summary>
    public void PrimaryClicked()
    {
        switch (Question)
        {
            case GooseQuestion.TakeABreak: Leave(GooseOutcome.Dismissed); break;
            case GooseQuestion.ReadyToFocus: ConfirmReady(); break;
            case GooseQuestion.EndBreakEarly: ResumeNap(); break;
            case GooseQuestion.StillOnBreak:
            case GooseQuestion.EndBreakAnyway: ResumeNap(); break;
        }
    }

    /// <summary>The secondary (outlined) button.</summary>
    public void SecondaryClicked()
    {
        switch (Question)
        {
            case GooseQuestion.TakeABreak: FinishUp(); break;
            case GooseQuestion.ReadyToFocus: NapLonger(); break;
            case GooseQuestion.StillOnBreak:
            case GooseQuestion.EndBreakEarly: AskToEndBreakAnyway(); break;
            case GooseQuestion.EndBreakAnyway: ConfirmReady(endedEarly: true); break;
        }
    }

    /// <summary>The goose itself was clicked.</summary>
    public void FigureClicked()
    {
        if (Phase == GoosePhase.Peeking)
        {
            // The user is done finishing up: come back and ask again.
            _view.FigureToolTip = null;
            _bubble = _bubble with { Message = "All finished up and saved? Then you're ready for your break!" };
            Phase = GoosePhase.Returning;
            return;
        }
        if (Phase == GoosePhase.Sleeping)
        {
            AskToEndBreakEarly();
            return;
        }
        if (Phase == GoosePhase.BreakOver)
        {
            AskIfReady();
            return;
        }
        // Poking the goose makes it flap its head indignantly (not while it's sitting with its head in a pose).
        if (!_view.Sleeping) _view.Flap();
    }
}
