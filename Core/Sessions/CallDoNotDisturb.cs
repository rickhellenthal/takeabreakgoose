namespace TakeABreakGoose;

/// <summary>
/// Turns Do not disturb on during a call (the app's Calls tells when that is), and off again when the call is over.
/// </summary>
public sealed class CallDoNotDisturb
{
    private bool _keptOff;

    /// <summary>Do not disturb is on because of a call (not because the user turned it on).</summary>
    public bool TurnedOn { get; private set; }

    /// <summary>
    /// Looks at the call and the session, and returns whether to turn Do not disturb on or off,
    /// or null to leave it as it is.
    /// </summary>
    public bool? Update(bool inCall, bool enabled, bool sessionRunning, bool doNotDisturb)
    {
        if (TurnedOn && !doNotDisturb)
        {
            // Turned off by the user during the call: leave it off until the call is over.
            // (Or the session stopped: a new one during the same call gets Do not disturb again.)
            TurnedOn = false;
            _keptOff = sessionRunning;
        }

        if (!inCall || !enabled)
        {
            _keptOff = false;
            if (!TurnedOn) return null;
            TurnedOn = false;
            return false;
        }

        if (!doNotDisturb && sessionRunning && !_keptOff)
        {
            TurnedOn = true;
            return true;
        }
        return null;
    }
}
