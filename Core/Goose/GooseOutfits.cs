using System;

namespace TakeABreakGoose;

public enum GooseOutfit { Raincoat, Bonnet, Bandana, PartyHat }

public static class GooseOutfits
{
    public static readonly GooseOutfit[] All = Enum.GetValues<GooseOutfit>();

    public static GooseOutfit Random() => All[System.Random.Shared.Next(All.Length)];

    public static string Name(GooseOutfit outfit) => outfit switch
    {
        GooseOutfit.Raincoat => "Rainy day raincoat",
        GooseOutfit.Bonnet => "Sunday bonnet",
        GooseOutfit.Bandana => "Polka dot bandana",
        GooseOutfit.PartyHat => "Party hat",
        _ => outfit.ToString(),
    };

    /// <summary>What the goose says when it comes over just to show off this outfit.</summary>
    public static string ShowOffLine(GooseOutfit outfit) => outfit switch
    {
        GooseOutfit.Raincoat => "Just showing off my raincoat! Rain or shine, I'll keep reminding you to take breaks.",
        GooseOutfit.Bonnet => "How do you like my Sunday bonnet? Very fancy, I know.",
        GooseOutfit.Bandana => "Check out my polka dot bandana! Ready for a little adventure.",
        GooseOutfit.PartyHat => "Party hat on! Every break deserves a small celebration.",
        _ => "What do you think of my outfit?",
    };

    public static string Description(GooseOutfit outfit) => outfit switch
    {
        GooseOutfit.Raincoat => "For honking you outside, whatever the weather.",
        GooseOutfit.Bonnet => "Fancy enough for a stroll around the block.",
        GooseOutfit.Bandana => "Ready for a little adventure away from the screen.",
        GooseOutfit.PartyHat => "Every break deserves a small celebration.",
        _ => "",
    };
}
