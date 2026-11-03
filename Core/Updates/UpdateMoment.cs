namespace TakeABreakGoose;

/// <summary>Whether now is a fine moment to restart the app for an update.</summary>
public static class UpdateMoment
{
    /// <summary>
    /// Not while a goose is on screen or on its way, during a break, while the goose waits for the
    /// user to come back to work, while a break waits for Do not disturb to end, or during a call.
    /// </summary>
    public static bool IsGood(BreakScheduler scheduler, bool gooseOnScreen, bool inCall) =>
        !gooseOnScreen && !inCall
        && !scheduler.GooseVisiting
        && scheduler.BreakEndsAt == null
        && !scheduler.AwaitingReturn
        && !scheduler.BreakWaiting;
}
