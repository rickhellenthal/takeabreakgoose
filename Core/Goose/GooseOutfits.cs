using System;

namespace TakeABreakGoose;

public enum GooseOutfit { Raincoat, Bonnet, Bandana, PartyHat }

public static class GooseOutfits
{
    public static readonly GooseOutfit[] All = Enum.GetValues<GooseOutfit>();

    public static GooseOutfit Random() => All[System.Random.Shared.Next(All.Length)];

    public static string Name(GooseOutfit outfit) => outfit switch
    {
        GooseOutfit.Raincoat => Strings.OutfitRaincoatName,
        GooseOutfit.Bonnet => Strings.OutfitBonnetName,
        GooseOutfit.Bandana => Strings.OutfitBandanaName,
        GooseOutfit.PartyHat => Strings.OutfitPartyHatName,
        _ => outfit.ToString(),
    };

    /// <summary>What the goose says when it comes over just to show off this outfit.</summary>
    public static string ShowOffLine(GooseOutfit outfit) => outfit switch
    {
        GooseOutfit.Raincoat => Strings.OutfitRaincoatShowOff,
        GooseOutfit.Bonnet => Strings.OutfitBonnetShowOff,
        GooseOutfit.Bandana => Strings.OutfitBandanaShowOff,
        GooseOutfit.PartyHat => Strings.OutfitPartyHatShowOff,
        _ => Strings.OutfitShowOffOther,
    };

    public static string Description(GooseOutfit outfit) => outfit switch
    {
        GooseOutfit.Raincoat => Strings.OutfitRaincoatDescription,
        GooseOutfit.Bonnet => Strings.OutfitBonnetDescription,
        GooseOutfit.Bandana => Strings.OutfitBandanaDescription,
        GooseOutfit.PartyHat => Strings.OutfitPartyHatDescription,
        _ => "",
    };
}
