using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TakeABreakGoose;

/// <summary>The drawing of the goose, with its walking pose, head tilt and outfit.</summary>
public partial class GooseFigure : UserControl
{
    private GooseOutfit _outfit;
    private bool _sleeping;
    private NapLook _look;
    private bool _huffy;

    // The transforms that pose the goose. (Avalonia only finds named controls, not named transforms.)
    private readonly ScaleTransform Flip;
    private readonly RotateTransform LegBackRotate;
    private readonly RotateTransform LegFrontRotate;
    private readonly TranslateTransform BodyBob;
    private readonly RotateTransform HeadTilt;

    // The animated parts. Each keeps the value set from code underneath any animation, like WPF does.
    private readonly AnimatedValue _bodyBob;
    private readonly AnimatedValue _headTilt;
    private readonly AnimatedValue _huhOpacity;
    private readonly AnimatedValue _huhBob;
    private readonly (AnimatedValue Opacity, AnimatedValue X, AnimatedValue Y)[] _snoreLetters;

    public GooseFigure()
    {
        InitializeComponent();
        Flip = (ScaleTransform)Drawing.RenderTransform!;
        LegBackRotate = (RotateTransform)BackLeg.RenderTransform!;
        LegFrontRotate = (RotateTransform)FrontLeg.RenderTransform!;
        BodyBob = (TranslateTransform)Body.RenderTransform!;
        HeadTilt = (RotateTransform)Head.RenderTransform!;
        var huhBob = (TranslateTransform)Huh.RenderTransform!;

        _bodyBob = new AnimatedValue(y => BodyBob.Y = y);
        _headTilt = new AnimatedValue(angle => HeadTilt.Angle = angle);
        _huhOpacity = new AnimatedValue(opacity => Huh.Opacity = opacity, 1);
        _huhBob = new AnimatedValue(y => huhBob.Y = y);
        _snoreLetters = new[] { SnoreLetter(Z1), SnoreLetter(Z2), SnoreLetter(Z3) };
        Outfit = GooseOutfit.Raincoat;

        // Nothing left breathing or snoring once the goose's window has closed.
        DetachedFromVisualTree += (_, _) =>
        {
            foreach (var value in AllAnimated()) value.Stop();
        };
    }

    private static (AnimatedValue, AnimatedValue, AnimatedValue) SnoreLetter(TextBlock letter)
    {
        var move = (TranslateTransform)letter.RenderTransform!;
        return (new AnimatedValue(opacity => letter.Opacity = opacity),
         new AnimatedValue(x => move.X = x),
         new AnimatedValue(y => move.Y = y));
    }

    private IEnumerable<AnimatedValue> AllAnimated()
    {
        yield return _bodyBob;
        yield return _headTilt;
        yield return _huhOpacity;
        yield return _huhBob;
        foreach (var (opacity, x, y) in _snoreLetters)
        {
            yield return opacity;
            yield return x;
            yield return y;
        }
    }

    /// <summary>X of the beak tip when facing right, in this control's coordinates.</summary>
    public const double BeakTipX = 109;

    public GooseOutfit Outfit
    {
        get => _outfit;
        set
        {
            _outfit = value;
            RaincoatCape.IsVisible = RaincoatHood.IsVisible = value == GooseOutfit.Raincoat;
            Bonnet.IsVisible = value == GooseOutfit.Bonnet;
            Bandana.IsVisible = value == GooseOutfit.Bandana;
            PartyHat.IsVisible = value == GooseOutfit.PartyHat;
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
        set => _headTilt.Value = value;
    }

    /// <summary>Poses the legs and body for a point in the walk cycle (-1..1); 0 is standing still.</summary>
    public void SetStride(double swing)
    {
        LegBackRotate.Angle = swing * 28;
        LegFrontRotate.Angle = -swing * 28;
        _bodyBob.Value = -Math.Abs(swing) * 3;
    }

    /// <summary>Sitting down with legs tucked in, head drooped, eyes closed and snoring.</summary>
    public bool Sleeping
    {
        get => _sleeping;
        set
        {
            if (value == _sleeping) return;
            _sleeping = value;

            _look = NapLook.Asleep;
            _huffy = false;
            BackLeg.IsVisible = FrontLeg.IsVisible = !value;
            OpenEye.IsVisible = !value;
            ClosedEye.IsVisible = value;
            DrowsyEye.IsVisible = UnimpressedEye.IsVisible = false;
            ShowHuh(false);
            Snore.IsVisible = value;
            _headTilt.Stop();
            _headTilt.Value = value ? SleepingHeadAngle : 0;

            if (value)
            {
                // Settle down onto the belly, then breathe slowly.
                _bodyBob.Animate(new Tween
                {
                    From = SleepingDrop - 1,
                    To = SleepingDrop + 1,
                    Duration = TimeSpan.FromSeconds(1.6),
                    AutoReverse = true,
                    Repeat = Tween.Forever,
                    Easing = Easings.SineInOut,
                });
                StartSnoring();
            }
            else
            {
                _bodyBob.Stop();
                _bodyBob.Value = 0;
                foreach (var (opacity, x, y) in _snoreLetters)
                {
                    opacity.Stop();
                    x.Stop();
                    y.Stop();
                }
            }
        }
    }

    /// <summary>
    /// How the goose looks while sitting down: asleep and snoring, half awake (head lifted a little,
    /// a sleepy eye and a puzzled "?"), or unimpressed (head up, flat heavy lid). Only while sleeping.
    /// </summary>
    public NapLook Look
    {
        get => _look;
        set
        {
            if (!_sleeping || value == _look) return;
            _look = value;

            ClosedEye.IsVisible = value == NapLook.Asleep;
            Snore.IsVisible = value == NapLook.Asleep;
            DrowsyEye.IsVisible = value == NapLook.Drowsy;
            UnimpressedEye.IsVisible = value == NapLook.Unimpressed;
            ShowHuh(value == NapLook.Drowsy);

            // Looking up is a bit quicker than nodding off again.
            double angle = value switch
            {
                NapLook.Drowsy => DrowsyHeadAngle,
                NapLook.Unimpressed => UnimpressedHeadAngle,
                _ => SleepingHeadAngle,
            };
            _headTilt.Animate(new Tween
            {
                To = angle,
                Duration = TimeSpan.FromSeconds(value == NapLook.Asleep ? 1.0 : 0.6),
                Easing = Easings.SineInOut,
            });
        }
    }

    /// <summary>Standing up: the unimpressed eye instead of the open one. The head angle is up to the caller.</summary>
    public bool Huffy
    {
        get => _huffy;
        set
        {
            if (_sleeping || value == _huffy) return;
            _huffy = value;
            OpenEye.IsVisible = !value;
            UnimpressedEye.IsVisible = value;
        }
    }

    private void ShowHuh(bool show)
    {
        _huhOpacity.Stop();
        _huhBob.Stop();
        Huh.IsVisible = show;
        if (!show) return;

        // Over the head, on the side the goose faces.
        Huh.Margin = new Thickness(Facing > 0 ? 86 : Width - 86 - 9, 28, 0, 0);
        _huhOpacity.Animate(new Tween { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(400) });
        _huhBob.Animate(new Tween
        {
            From = 0,
            To = -4,
            Duration = TimeSpan.FromSeconds(0.9),
            AutoReverse = true,
            Repeat = Tween.Forever,
            Easing = Easings.SineInOut,
        });
    }

    // How far the body sinks when sitting (the belly then rests on the ground) and how far the head droops.
    private const double SleepingDrop = 17;
    private const double SleepingHeadAngle = 38;
    private const double DrowsyHeadAngle = 16;
    private const double UnimpressedHeadAngle = 6;

    private void StartSnoring()
    {
        // The z's rise from in front of the drooped head (clear of any hat) and drift the way the goose faces.
        int facing = Facing;
        double startX = facing > 0 ? 100 : Width - 100 - 10;
        TextBlock[] letters = { Z1, Z2, Z3 };
        for (int i = 0; i < letters.Length; i++)
        {
            var z = letters[i];
            Canvas.SetLeft(z, startX);
            Canvas.SetTop(z, 54);
            var begin = TimeSpan.FromSeconds(i * 0.9);
            var duration = TimeSpan.FromSeconds(2.7);
            var (opacity, x, y) = _snoreLetters[i];

            y.Animate(new Tween { From = 0, To = -42, Duration = duration, Delay = begin, Repeat = Tween.Forever });
            x.Animate(new Tween { From = 0, To = facing * 16, Duration = duration, Delay = begin, Repeat = Tween.Forever });
            // Fades in over the first quarter, then out again for the rest of the way up.
            opacity.Animate(new Tween
            {
                From = 0,
                To = 1,
                Duration = duration,
                Delay = begin,
                Repeat = Tween.Forever,
                Easing = t => t < 0.25 ? t / 0.25 : (1 - t) / 0.75,
            });
        }
    }

    /// <summary>A quick indignant head flap.</summary>
    public void Flap()
    {
        _headTilt.Animate(new Tween
        {
            From = 0,
            To = -18,
            Duration = TimeSpan.FromMilliseconds(90),
            AutoReverse = true,
            Repeat = 2,
            Completed = _headTilt.Stop,
        });
    }
}
