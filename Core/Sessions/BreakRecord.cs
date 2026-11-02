using System;

namespace TakeABreakGoose;

public enum BreakKind
{
    /// <summary>The goose's break, taken until it was over.</summary>
    Taken,

    /// <summary>The goose's break, ended before its time was up.</summary>
    EndedEarly,

    /// <summary>Time away from the computer that counted as a break.</summary>
    Away,
}

/// <summary>A break taken during the focus session.</summary>
public sealed record BreakRecord(DateTime Start, DateTime End, BreakKind Kind)
{
    public TimeSpan Length => End - Start;
}
