using Xunit;

namespace TakeABreakGoose.Tests;

/// <summary>The napping goose noticing that you're using the computer during your break.</summary>
public class NapWatchTests
{
    private DateTime _now = new(2026, 11, 2, 9, 0, 0);
    private TimeSpan _idle = TimeSpan.FromMinutes(1);
    private readonly NapWatch _watch;

    public NapWatchTests()
    {
        _watch = new NapWatch(_now);
    }

    [Fact]
    public void Using_the_computer_right_after_the_nap_starts_is_ignored()
    {
        Use(NapWatch.Grace - Seconds(0.1));

        Assert.Equal(NapWatchState.Asleep, _watch.State);
    }

    [Fact]
    public void Using_it_after_the_grace_period_makes_the_goose_stir()
    {
        Use(NapWatch.Grace + Seconds(0.5));

        Assert.Equal(NapWatchState.Stirring, _watch.State);
    }

    [Fact]
    public void Input_from_before_the_grace_period_ended_does_not_count()
    {
        // Still typing just before the grace period ends, then nothing.
        Use(NapWatch.Grace - Seconds(0.5));
        Rest(Seconds(0.8));

        Assert.Equal(NapWatchState.Asleep, _watch.State);
    }

    [Fact]
    public void Keeping_at_it_makes_the_goose_ask()
    {
        Use(NapWatch.Grace);

        Use(NapWatch.UseBeforeAsking - Seconds(0.1));
        Assert.Equal(NapWatchState.Stirring, _watch.State);

        Use(Seconds(0.1));
        Assert.Equal(NapWatchState.ShouldAsk, _watch.State);
    }

    [Fact]
    public void Short_pauses_do_not_reset_the_count()
    {
        Use(NapWatch.Grace);
        Use(Seconds(3));
        Rest(NapWatch.IdleBeforeDozingOff - Seconds(1));
        Assert.Equal(NapWatchState.Stirring, _watch.State);

        Use(Seconds(3));
        Assert.Equal(NapWatchState.ShouldAsk, _watch.State);
    }

    [Fact]
    public void Leaving_the_computer_alone_lets_the_goose_doze_off_and_start_counting_again()
    {
        Use(NapWatch.Grace);
        Use(Seconds(5));
        Rest(NapWatch.IdleBeforeDozingOff);
        Assert.Equal(NapWatchState.Asleep, _watch.State);

        // The 5 seconds from before are forgotten.
        Use(Seconds(5));
        Assert.Equal(NapWatchState.Stirring, _watch.State);
    }

    [Fact]
    public void Going_back_to_sleep_gives_time_to_get_up_again()
    {
        Use(NapWatch.Grace + Seconds(3));
        Assert.Equal(NapWatchState.Stirring, _watch.State);

        // "I'm going!"
        _watch.Restart(_now);
        Assert.Equal(NapWatchState.Asleep, _watch.State);

        Use(NapWatch.Grace - Seconds(0.1));
        Assert.Equal(NapWatchState.Asleep, _watch.State);
    }

    [Fact]
    public void The_goose_stops_asking_when_nobody_answers_for_a_while()
    {
        Assert.False(NapWatch.GaveUpAsking(NapWatch.IdleBeforeGivingUpAsking - Seconds(1)));
        Assert.True(NapWatch.GaveUpAsking(NapWatch.IdleBeforeGivingUpAsking));
    }

    /// <summary>Time passes while the mouse and keyboard are in use.</summary>
    private void Use(TimeSpan time) => Pass(time, inUse: true);

    /// <summary>Time passes while nobody touches the mouse or keyboard.</summary>
    private void Rest(TimeSpan time) => Pass(time, inUse: false);

    private void Pass(TimeSpan time, bool inUse)
    {
        var frame = Seconds(0.1);
        for (var passed = TimeSpan.Zero; passed < time; passed += frame)
        {
            _now += frame;
            _idle = inUse ? TimeSpan.Zero : _idle + frame;
            _watch.Update(_now, _idle, frame);
        }
    }

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);
}
