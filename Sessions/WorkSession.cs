using System;

namespace TakeABreakGoose;

/// <summary>A running focus session.</summary>
/// <param name="BreakLength">How long each break lasts in this session (zero: breaks aren't counted).</param>
/// <param name="StartedAutomatically">Started by the "start a focus session when the app starts" setting.</param>
public sealed record WorkSession(
    int IntervalMinutes, TimeSpan BreakLength, DateTime StartedAt, DateTime? EndsAt, bool StartedAutomatically);
