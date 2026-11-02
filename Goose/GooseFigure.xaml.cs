using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TakeABreakGoose;

/// <summary>The drawing of the goose, with its walking pose, head tilt and outfit.</summary>
public partial class GooseFigure : UserControl
{
    private GooseOutfit _outfit;
    private bool _sleeping;
    private NapLook _look;
    private bool _huffy;

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
            BackLeg.Visibility = FrontLeg.Visibility = Show(!value);
            OpenEye.Visibility = Show(!value);
            ClosedEye.Visibility = Show(value);
            DrowsyEye.Visibility = UnimpressedEye.Visibility = Visibility.Collapsed;
            ShowHuh(false);
            Snore.Visibility = Show(value);
            HeadTilt.BeginAnimation(RotateTransform.AngleProperty, null);
            HeadTilt.Angle = value ? SleepingHeadAngle : 0;

            if (value)
            {
                // Settle down onto the belly, then breathe slowly.
                var breathe = new DoubleAnimation(SleepingDrop - 1, SleepingDrop + 1, TimeSpan.FromSeconds(1.6))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                BodyBob.BeginAnimation(TranslateTransform.YProperty, breathe);
                StartSnoring();
            }
            else
            {
                BodyBob.BeginAnimation(TranslateTransform.YProperty, null);
                BodyBob.Y = 0;
                foreach (var z in new[] { Z1, Z2, Z3 })
                {
                    z.BeginAnimation(OpacityProperty, null);
                    z.RenderTransform.BeginAnimation(TranslateTransform.XProperty, null);
                    z.RenderTransform.BeginAnimation(TranslateTransform.YProperty, null);
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

            ClosedEye.Visibility = Show(value == NapLook.Asleep);
            Snore.Visibility = Show(value == NapLook.Asleep);
            DrowsyEye.Visibility = Show(value == NapLook.Drowsy);
            UnimpressedEye.Visibility = Show(value == NapLook.Unimpressed);
            ShowHuh(value == NapLook.Drowsy);

            // Looking up is a bit quicker than nodding off again.
            double angle = value switch
            {
                NapLook.Drowsy => DrowsyHeadAngle,
                NapLook.Unimpressed => UnimpressedHeadAngle,
                _ => SleepingHeadAngle,
            };
            var tilt = new DoubleAnimation(angle, TimeSpan.FromSeconds(value == NapLook.Asleep ? 1.0 : 0.6))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            HeadTilt.BeginAnimation(RotateTransform.AngleProperty, tilt);
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
            OpenEye.Visibility = Show(!value);
            UnimpressedEye.Visibility = Show(value);
        }
    }

    private void ShowHuh(bool show)
    {
        Huh.BeginAnimation(OpacityProperty, null);
        HuhBob.BeginAnimation(TranslateTransform.YProperty, null);
        Huh.Visibility = Show(show);
        if (!show) return;

        // Over the head, on the side the goose faces.
        Huh.Margin = new Thickness(Facing > 0 ? 86 : Width - 86 - 9, 18, 0, 0);
        Huh.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));
        HuhBob.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -4, TimeSpan.FromSeconds(0.9))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
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
            Canvas.SetTop(z, 44);
            var begin = TimeSpan.FromSeconds(i * 0.9);
            var duration = TimeSpan.FromSeconds(2.7);

            var rise = new DoubleAnimation(0, -42, duration) { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever };
            var drift = new DoubleAnimation(0, facing * 16, duration) { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever };
            var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever, Duration = duration };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.25)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));

            z.RenderTransform.BeginAnimation(TranslateTransform.YProperty, rise);
            z.RenderTransform.BeginAnimation(TranslateTransform.XProperty, drift);
            z.BeginAnimation(OpacityProperty, fade);
        }
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

public enum NapLook { Asleep, Drowsy, Unimpressed }
