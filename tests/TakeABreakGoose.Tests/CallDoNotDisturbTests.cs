using Xunit;

namespace TakeABreakGoose.Tests;

public class CallDoNotDisturbTests
{
    private readonly CallDoNotDisturb _rule = new();
    private bool _doNotDisturb;
    private bool _session = true;
    private bool _enabled = true;

    /// <summary>Tells the rule whether there is a call, and applies what it decides like the app does.</summary>
    private bool? Check(bool call)
    {
        var change = _rule.Update(call, _enabled, _session, _doNotDisturb);
        if (change is bool on) _doNotDisturb = on;
        return change;
    }

    [Fact]
    public void A_call_turns_do_not_disturb_on_and_off_again_when_it_is_over()
    {
        Assert.True(Check(call: true));
        Assert.True(_rule.TurnedOn);
        Assert.Null(Check(call: true));

        Assert.False(Check(call: false));
        Assert.False(_doNotDisturb);
        Assert.False(_rule.TurnedOn);
    }

    [Fact]
    public void Nothing_happens_without_a_session()
    {
        _session = false;

        Assert.Null(Check(call: true));
        Assert.False(_doNotDisturb);
    }

    [Fact]
    public void A_session_started_during_a_call_gets_do_not_disturb()
    {
        _session = false;
        Check(call: true);

        _session = true;

        Assert.True(Check(call: true));
    }

    [Fact]
    public void Nothing_happens_when_turned_off_in_the_settings()
    {
        _enabled = false;

        Assert.Null(Check(call: true));
        Assert.False(_doNotDisturb);
    }

    [Fact]
    public void Turning_the_setting_off_during_a_call_turns_do_not_disturb_off()
    {
        Check(call: true);

        _enabled = false;

        Assert.False(Check(call: true));
    }

    [Fact]
    public void Do_not_disturb_the_user_turned_on_stays_on_after_the_call()
    {
        _doNotDisturb = true;

        Assert.Null(Check(call: true));
        Assert.Null(Check(call: false));
        Assert.True(_doNotDisturb);
    }

    [Fact]
    public void Turned_off_by_the_user_during_the_call_it_stays_off_until_the_next_call()
    {
        Check(call: true);
        _doNotDisturb = false; // the user wants the goose after all

        Assert.Null(Check(call: true));
        Assert.False(_doNotDisturb);

        Check(call: false);
        Assert.True(Check(call: true)); // the next call
    }

    [Fact]
    public void A_session_that_stopped_during_the_call_and_a_new_one_gets_do_not_disturb_again()
    {
        Check(call: true);
        _session = false;
        _doNotDisturb = false; // stopping a session turns Do not disturb off

        Assert.Null(Check(call: true));

        _session = true;
        Assert.True(Check(call: true));
    }

    [Fact]
    public void A_session_that_ended_during_the_call_says_goodbye_when_the_call_is_over()
    {
        Check(call: true);
        _session = false; // ended; Do not disturb stays on until the goodbye

        Assert.Null(Check(call: true));
        Assert.False(Check(call: false));
    }
}
