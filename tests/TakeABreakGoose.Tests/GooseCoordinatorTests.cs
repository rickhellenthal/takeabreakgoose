using Xunit;
using static TakeABreakGoose.Tests.SchedulerHarness;

namespace TakeABreakGoose.Tests;

/// <summary>Which goose comes when, with a stand-in for the goose's window.</summary>
public class GooseCoordinatorTests
{
    private readonly SchedulerHarness _h = new();
    private readonly List<FakeGoose> _geese = new();
    private readonly GooseCoordinator _coordinator;

    public GooseCoordinatorTests()
    {
        _coordinator = new GooseCoordinator(_h.Scheduler, () => _h.Settings, (visit, _, breakLength) =>
        {
            var goose = new FakeGoose(visit, breakLength);
            _geese.Add(goose);
            return goose;
        });
    }

    private FakeGoose Goose => _geese[^1];

    [Fact]
    public void When_a_break_is_due_a_goose_comes_to_ask_for_it()
    {
        _h.StartSession(intervalMinutes: 30, breakMinutes: 5);
        _h.Work(Minutes(30));

        Assert.Single(_geese);
        Assert.True(Goose.Shown);
        Assert.True(Goose.Visit.StartsBreak);
        Assert.Equal(Minutes(5), Goose.BreakLength); // it naps during the break
        Assert.True(_h.Scheduler.GooseVisiting);
    }

    [Fact]
    public void Taking_the_break_starts_it_in_the_scheduler()
    {
        _h.StartSession(intervalMinutes: 30, breakMinutes: 5);
        _h.Work(Minutes(30));

        Goose.Answer(GooseOutcome.Dismissed);

        Assert.True(_h.Scheduler.AwaitingReturn);
        Assert.Equal(_h.Now + Minutes(5), _h.Scheduler.BreakEndsAt);
    }

    [Fact]
    public void Only_one_goose_shows_off_an_outfit_at_a_time()
    {
        Assert.True(_coordinator.ShowOffOutfit(GooseOutfit.Bonnet));
        Assert.False(_coordinator.ShowOffOutfit(GooseOutfit.Bandana));

        Assert.Single(_geese);
        Assert.Equal(GooseOutfit.Bonnet, Goose.Visit.Outfit);
    }

    [Fact]
    public void Stopping_the_session_sends_its_goose_away_but_not_one_showing_off()
    {
        _h.StartSession(intervalMinutes: 30);
        _h.Work(Minutes(30));
        var sessionGoose = Goose;
        _coordinator.StopSession();
        Assert.True(sessionGoose.WalkedAway);

        sessionGoose.Leave();
        _coordinator.ShowOffOutfit(GooseOutfit.Raincoat);
        _coordinator.StopSession();
        Assert.False(Goose.WalkedAway);
    }

    [Fact]
    public void Do_not_disturb_sends_the_goose_off_and_the_break_waits_for_it()
    {
        _h.StartSession(intervalMinutes: 30);
        _h.Work(Minutes(30));

        _coordinator.SetDoNotDisturb(true);

        Assert.True(Goose.LeftNow);
        Assert.True(_h.Scheduler.BreakWaiting);

        Goose.Leave();
        _coordinator.SetDoNotDisturb(false);
        Assert.Equal(2, _geese.Count); // the break that waited comes now
        Assert.True(Goose.Visit.StartsBreak);
    }

    [Fact]
    public void The_goodbye_waits_until_a_goose_showing_off_has_left()
    {
        _h.StartSession(intervalMinutes: 60, endsAfter: Minutes(30));
        _coordinator.ShowOffOutfit(GooseOutfit.Bandana);

        _h.Work(Minutes(30));
        Assert.Single(_geese);

        Goose.Leave();
        Assert.Equal(2, _geese.Count);
        Assert.Equal("That's a wrap!", Goose.Visit.Title);
        Assert.Equal(GooseOutfit.PartyHat, Goose.Visit.Outfit);
    }

    [Fact]
    public void A_call_turns_do_not_disturb_on_and_off_again()
    {
        _h.StartSession(intervalMinutes: 30);
        int callsStarted = 0;
        _coordinator.CallStarted += () => callsStarted++;

        _coordinator.CheckForCall(inCall: true);
        Assert.True(_h.Scheduler.DoNotDisturb);
        Assert.True(_coordinator.DoNotDisturbForCall);
        Assert.Equal(1, callsStarted);

        _coordinator.CheckForCall(inCall: false);
        Assert.False(_h.Scheduler.DoNotDisturb);
    }

    private sealed class FakeGoose : IGooseWindow
    {
        public FakeGoose(GooseVisit visit, TimeSpan? breakLength)
        {
            Visit = visit;
            BreakLength = breakLength;
        }

        public GooseVisit Visit { get; }
        public TimeSpan? BreakLength { get; }
        public bool Shown { get; private set; }
        public bool WalkedAway { get; private set; }
        public bool LeftNow { get; private set; }

        public event Action<GooseOutcome>? Answered;
        public event Action? Finished;

        // Answers these tests don't give.
        public event Action<DateTime>? BreakExtended { add { } remove { } }
        public event Action? ReadyToFocus { add { } remove { } }
        public event Action<DateTime>? TookBreakWhileAway { add { } remove { } }

        public bool StillAskingForBreak => Visit.StartsBreak;
        public bool OnBreak => false;

        public void Show() => Shown = true;
        public void LeaveNow() => LeftNow = true;
        public void WalkAway() => WalkedAway = true;

        public void Answer(GooseOutcome outcome) => Answered?.Invoke(outcome);

        /// <summary>The goose has walked off the screen.</summary>
        public void Leave() => Finished?.Invoke();
    }
}
