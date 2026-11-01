using System;

namespace TakeABreakGoose;

/// <summary>A running focus session.</summary>
/// <param name="BreakMinutes">How long each break lasts in this session.</param>
/// <param name="StartedAutomatically">Started by the "start a focus session when the app starts" setting.</param>
public sealed record WorkSession(
    int IntervalMinutes, int BreakMinutes, DateTime StartedAt, DateTime? EndsAt, bool StartedAutomatically);
