using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

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

    public static string Description(GooseOutfit outfit) => outfit switch
    {
        GooseOutfit.Raincoat => "For honking you outside, whatever the weather.",
        GooseOutfit.Bonnet => "Fancy enough for a stroll around the block.",
        GooseOutfit.Bandana => "Ready for a little adventure away from the screen.",
        GooseOutfit.PartyHat => "Every break deserves a small celebration.",
        _ => "",
    };
}

/// <summary>The drawing of the goose, with its walking pose, head tilt and outfit.</summary>
public partial class GooseFigure : UserControl
{
    private GooseOutfit _outfit;

    public GooseFigure()
    {
        InitializeComponent();
        Outfit = GooseOutfit.Raincoat;
    }

    /// <summary>X of the beak tip when facing right, in this control's coordinates.</summary>
    public const double BeakTipX = 109;

    public GooseOutfit Outfit
    {
        get => _outfit;
        set
        {
            _outfit = value;
            RaincoatCape.Visibility = RaincoatHood.Visibility = Show(value == GooseOutfit.Raincoat);
            Bonnet.Visibility = Show(value == GooseOutfit.Bonnet);
            Bandana.Visibility = Show(value == GooseOutfit.Bandana);
            PartyHat.Visibility = Show(value == GooseOutfit.PartyHat);
        }
    }

    /// <summary>1 to face right, -1 to face left.</summary>
    public int Facing
    {
        get => (int)Flip.ScaleX;
        set => Flip.ScaleX = value;
    }

    public double HeadAngle
    {
        set => HeadTilt.Angle = value;
    }

    /// <summary>Poses the legs and body for a point in the walk cycle (-1..1); 0 is standing still.</summary>
    public void SetStride(double swing)
    {
        LegBackRotate.Angle = swing * 28;
        LegFrontRotate.Angle = -swing * 28;
        BodyBob.Y = -Math.Abs(swing) * 3;
    }

    /// <summary>A quick indignant head flap.</summary>
    public void Flap()
    {
        var flap = new DoubleAnimation(0, -18, TimeSpan.FromMilliseconds(90))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(2),
        };
        flap.Completed += (_, _) => HeadTilt.BeginAnimation(RotateTransform.AngleProperty, null);
        HeadTilt.BeginAnimation(RotateTransform.AngleProperty, flap);
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
