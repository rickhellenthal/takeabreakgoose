using System;

namespace TakeABreakGoose;

/// <summary>The time of day, so tests can let time pass without waiting for it.</summary>
public interface IClock
{
    DateTime Now { get; }
}

/// <summary>The computer's own clock.</summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTime Now => DateTime.Now;
}
