namespace TakeABreakGoose;

/// <summary>
/// What <see cref="GooseBehavior"/> needs the goose's window to do: pose the goose, show the speech
/// bubble and the break card, and move the window. Each app draws these in its own way (and does the
/// pop-ups, fades and tweens that are only for show); what the goose does is up to the behavior.
/// </summary>
public interface IGooseView
{
    /// <summary>1 to face right, -1 to face left.</summary>
    int Facing { set; }

    double HeadAngle { set; }

    /// <summary>Poses the legs and body for a point in the walk cycle (-1..1); 0 is standing still.</summary>
    void SetStride(double swing);

    /// <summary>Sitting down with legs tucked in, head drooped, eyes closed and snoring.</summary>
    bool Sleeping { get; set; }

    /// <summary>How the goose looks while it sits napping. Only while sleeping.</summary>
    NapLook Look { set; }

    /// <summary>Standing up: the unimpressed eye instead of the open one. Not while sleeping.</summary>
    bool Huffy { get; set; }

    /// <summary>A quick indignant head flap.</summary>
    void Flap();

    /// <summary>What shows when the mouse rests on the goose; null for nothing.</summary>
    string? FigureToolTip { set; }

    /// <summary>Shows (and pops up) the speech bubble with these texts and buttons.</summary>
    void ShowBubble(SpeechBubble bubble);

    void HideBubble();

    /// <summary>Shows the card with the break's time left, popping it up or (back to a nap) just showing it.</summary>
    void ShowBreakCard(bool popUp);

    void HideBreakCard();

    string BreakTitle { set; }

    /// <summary>How much of the break has gone (0..1) and the text under the title.</summary>
    void ShowBreakProgress(double fraction, string text);

    /// <summary>The window's left edge on screen, in DIPs. Moves the window.</summary>
    double WindowLeft { set; }

    /// <summary>
    /// Whether the cursor is between these two screen x positions (both included), between the top of
    /// the goose and the bottom of the window.
    /// </summary>
    bool IsCursorBetween(double fromX, double toX);

    /// <summary>Stops the frames, fades out quickly and closes.</summary>
    void FadeOutAndClose();

    /// <summary>Stops the frames and closes.</summary>
    void CloseWindow();
}

/// <summary>What the speech bubble says. A null button text means that button isn't there.</summary>
/// <param name="PrimaryButton">The orange button.</param>
/// <param name="SecondaryButton">The outlined button next to it.</param>
/// <param name="LinkButton">The quiet link on the left.</param>
public sealed record SpeechBubble(
    string Title,
    string Message,
    string? Hint,
    string PrimaryButton,
    string? SecondaryButton,
    string? LinkButton);

/// <summary>Where the goose walks, in DIPs: the screen's working area and the size of its window.</summary>
/// <param name="FigureWidth">The goose figure's width; it sits in the middle of the window.</param>
/// <param name="BeakTipX">X of the beak tip when facing right, in the figure's coordinates.</param>
public sealed record GooseStage(
    double AreaLeft,
    double AreaRight,
    double WindowWidth,
    double FigureWidth,
    double BeakTipX);
