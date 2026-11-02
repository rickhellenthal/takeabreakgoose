using System;

namespace TakeABreakGoose;

/// <summary>Makes the window for one goose visit.</summary>
/// <param name="breakLength">If set, the goose naps for this long when the user takes the break.</param>
public delegate IGooseWindow NewGooseWindow(GooseVisit visit, int snoozeMinutes, TimeSpan? breakLength);

/// <summary>
/// The goose on screen for one visit: walking in, asking, peeking, napping and walking off again.
/// Each app draws it in its own window; <see cref="GooseCoordinator"/> decides when it comes.
/// </summary>
public interface IGooseWindow
{
    /// <summary>Raised as soon as the user answers the goose.</summary>
    event Action<GooseOutcome>? Answered;

    /// <summary>Raised when the user asks for a few more minutes of break, with the new end time.</summary>
    event Action<DateTime>? BreakExtended;

    /// <summary>Raised when the user says they're back from the break and ready to focus.</summary>
    event Action? ReadyToFocus;

    /// <summary>
    /// Raised when the user was away for at least a break's length while the goose asked them to
    /// take one, so that time counts as the break. Comes with when they left the computer.
    /// </summary>
    event Action<DateTime>? TookBreakWhileAway;

    /// <summary>Raised once the goose has left the screen.</summary>
    event Action? Finished;

    /// <summary>The goose came to ask for a break and the user hasn't taken it yet (including "let me finish up").</summary>
    bool StillAskingForBreak { get; }

    /// <summary>The user is on the break this goose asked for, or is just back from it.</summary>
    bool OnBreak { get; }

    /// <summary>Walks in from a screen edge.</summary>
    void Show();

    /// <summary>Disappears right away (quick fade), e.g. when the user turns on Do not disturb.</summary>
    void LeaveNow();

    /// <summary>
    /// Stops whatever it's doing (asking, peeking, napping) and walks off the screen,
    /// e.g. when the user stops the session. Raises none of the answer events.
    /// </summary>
    void WalkAway();
}
