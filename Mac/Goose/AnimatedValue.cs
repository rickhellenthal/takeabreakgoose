using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Threading;

namespace TakeABreakGoose;

// What the goose figure animates with, in place of WPF's DoubleAnimation.

/// <summary>Easing curves matching WPF's easing functions.</summary>
internal static class Easings
{
    /// <summary>WPF's SineEase with EaseInOut.</summary>
    public static double SineInOut(double t) => (1 - Math.Cos(Math.PI * t)) / 2;

    /// <summary>WPF's BackEase with EaseOut: overshoots by an amount set by the amplitude, then settles.</summary>
    public static Func<double, double> BackOut(double amplitude) => t =>
    {
        double back = 1 - t;
        return 1 - (back * back * back - back * amplitude * Math.Sin(Math.PI * back));
    };
}

/// <summary>
/// One animation of a number, described like a WPF DoubleAnimation: from (or, if left out, from
/// wherever the value is now) to a value, optionally after a delay, back again and repeated.
/// </summary>
internal sealed class Tween
{
    public const double Forever = double.PositiveInfinity;

    public double? From { get; init; }
    public double To { get; init; }
    public TimeSpan Duration { get; init; }
    public TimeSpan Delay { get; init; }
    public bool AutoReverse { get; init; }

    /// <summary>How many times it plays (there and back counts as once).</summary>
    public double Repeat { get; init; } = 1;

    /// <summary>Maps the time (0..1) to how far along the value is (0..1).</summary>
    public Func<double, double>? Easing { get; init; }

    public Action? Completed { get; init; }
}

/// <summary>
/// A number shown on screen that can be animated the way WPF animates a dependency property: while
/// an animation runs, and after it ends (it holds its last value), the animation is what shows; the
/// value set from code is kept underneath and comes back once the animation is stopped.
/// This is simpler to stop and restart part way than Avalonia's own animations.
/// </summary>
internal sealed class AnimatedValue
{
    private readonly Action<double> _show;
    private double _value;
    private Tween? _tween;
    private double _from;
    private TimeSpan _started;

    public AnimatedValue(Action<double> show, double value = 0)
    {
        _show = show;
        _value = Shown = value;
    }

    /// <summary>The value without animation. Shows right away unless an animation is on.</summary>
    public double Value
    {
        get => _value;
        set
        {
            _value = value;
            if (_tween == null) Show(value);
        }
    }

    /// <summary>What's on screen right now.</summary>
    public double Shown { get; private set; }

    /// <summary>Starts an animation, replacing any that's on, from wherever the value is now if it has no From.</summary>
    public void Animate(Tween tween)
    {
        _tween = tween;
        _from = tween.From ?? Shown;
        _started = Ticker.Now;
        // The first frame right away; it only needs moving along if it hasn't already ended.
        if (Tick(_started)) Ticker.Add(this);
        else if (_tween == tween) Ticker.Remove(this);
    }

    /// <summary>Stops the animation, if any, and shows the value set from code again.</summary>
    public void Stop()
    {
        if (_tween == null) return;
        _tween = null;
        Ticker.Remove(this);
        Show(_value);
    }

    private void Show(double value)
    {
        Shown = value;
        _show(value);
    }

    /// <summary>Shows the animation at a moment; returns false once it has ended.</summary>
    private bool Tick(TimeSpan now)
    {
        if (_tween is not { } tween) return false;

        double elapsed = (now - _started - tween.Delay).TotalSeconds;
        if (elapsed < 0)
        {
            // Not started yet: the value from code shows until then.
            Show(_value);
            return true;
        }

        double length = tween.Duration.TotalSeconds * (tween.AutoReverse ? 2 : 1);
        double plays = length > 0 ? elapsed / length : tween.Repeat;
        bool ended = plays >= tween.Repeat;
        double progress = ended ? 1 : plays - Math.Floor(plays);
        // Going back plays the same curve backwards.
        double time = tween.AutoReverse ? (progress < 0.5 ? progress * 2 : 2 - progress * 2) : progress;
        double eased = tween.Easing?.Invoke(time) ?? time;
        Show(_from + (tween.To - _from) * eased);

        if (ended) tween.Completed?.Invoke();
        return !ended;
    }

    /// <summary>Moves all running animations along, about 60 times a second, only while there are any.</summary>
    private static class Ticker
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly List<AnimatedValue> Running = new();
        private static readonly DispatcherTimer Timer =
            new(TimeSpan.FromSeconds(1.0 / 60), DispatcherPriority.Render, (_, _) => TickAll());

        public static TimeSpan Now => Clock.Elapsed;

        public static void Add(AnimatedValue value)
        {
            if (!Running.Contains(value)) Running.Add(value);
            if (!Timer.IsEnabled) Timer.Start();
        }

        public static void Remove(AnimatedValue value)
        {
            Running.Remove(value);
            if (Running.Count == 0) Timer.Stop();
        }

        private static void TickAll()
        {
            var now = Now;
            // A copy, since an animation ending can start or stop others.
            foreach (var value in Running.ToArray())
            {
                var tween = value._tween;
                if (!value.Tick(now) && value._tween == tween) Running.Remove(value);
            }
            if (Running.Count == 0) Timer.Stop();
        }
    }
}
