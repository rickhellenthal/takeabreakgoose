namespace TakeABreakGoose;

/// <summary>What the goose says during one visit. A null outfit means a random one.</summary>
/// <param name="JustVisiting">Not a reminder (e.g. showing off an outfit), so it leaves the session's timer alone.</param>
public sealed record GooseVisit(
    string Title,
    string Message,
    string? Hint,
    string DismissText,
    bool AllowSnooze,
    bool AllowFinishUp,
    bool StartsBreak,
    GooseOutfit? Outfit = null,
    bool JustVisiting = false);
