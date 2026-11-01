namespace TakeABreakGoose;

/// <summary>What the app window needs from the app.</summary>
public interface IGooseHost
{
    GooseSettings Settings { get; }
    BreakScheduler Scheduler { get; }
    void StartSession(SessionPlan plan);
    void SummonBreakGoose();

    /// <summary>Stops the running session; a goose that's there for it walks off.</summary>
    void StopSession();

    /// <summary>
    /// Sends the goose over just to show off an outfit; it doesn't touch the session's timer.
    /// Returns false if a goose is already on screen.
    /// </summary>
    bool ShowOffOutfit(GooseOutfit outfit);

    /// <summary>Whether a goose is on screen right now (walking, talking, peeking or napping).</summary>
    bool GooseOnScreen { get; }

    /// <summary>Keeps the goose away during a focus session (e.g. during a meeting), or lets it come again.</summary>
    void SetDoNotDisturb(bool on);

    /// <summary>Do not disturb is on because of a call, and goes off again when the call is over.</summary>
    bool DoNotDisturbForCall { get; }

    /// <summary>Applies and saves new settings; returns an error message, or null on success.</summary>
    string? ApplySettings(GooseSettings settings, bool startWithWindows);
}
