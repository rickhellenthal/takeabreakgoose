using System;

namespace TakeABreakGoose;

public enum NapWatchState { Asleep, Stirring, ShouldAsk }

/// <summary>
/// While the goose naps during a break, keeps an eye on the mouse and keyboard. Using the computer
/// makes the goose stir (half wake up); keep at it and it's time to ask whether you're really taking
/// your break; leave it alone for a moment and the goose dozes off again.
/// </summary>
internal sealed class NapWatch
{
    /// <summary>Time to get up and go after the nap starts (or resumes), before anything counts.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

    /// <summary>How long the computer is used, in total, before the goose asks.</summary>
    public static readonly TimeSpan UseBeforeAsking = TimeSpan.FromSeconds(6);

    public static readonly TimeSpan IdleBeforeDozingOff = TimeSpan.FromSeconds(4);

    /// <summary>Nobody answers the question because they did get up after all.</summary>
    public static readonly TimeSpan IdleBeforeGivingUpAsking = TimeSpan.FromSeconds(20);

    // Input this recent counts as using the computer right now.
    private static readonly TimeSpan InUse = TimeSpan.FromSeconds(1);

    private DateTime _watchFrom;
    private TimeSpan _used;

    public NapWatch(DateTime now) => Restart(now);

    public NapWatchState State { get; private set; }

    /// <summary>The goose (re)starts napping: asleep, and only counting from after the grace period.</summary>
    public void Restart(DateTime now)
    {
        _watchFrom = now + Grace;
        _used = TimeSpan.Zero;
        State = NapWatchState.Asleep;
    }

    /// <summary>Called every frame with the time since the last one, and how long the mouse and keyboard have been idle.</summary>
    public NapWatchState Update(DateTime now, TimeSpan idle, TimeSpan elapsed)
    {
        if (now < _watchFrom) return State;
        // Only input after the grace period counts, not the click that started the nap.
        if (idle < InUse && idle < now - _watchFrom)
        {
            _used += elapsed;
            State = _used >= UseBeforeAsking ? NapWatchState.ShouldAsk : NapWatchState.Stirring;
        }
        else if (idle >= IdleBeforeDozingOff)
        {
            _used = TimeSpan.Zero;
            State = NapWatchState.Asleep;
        }
        return State;
    }

    public static bool GaveUpAsking(TimeSpan idle) => idle >= IdleBeforeGivingUpAsking;
}
