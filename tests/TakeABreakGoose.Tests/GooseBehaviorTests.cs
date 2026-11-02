using Xunit;

namespace TakeABreakGoose.Tests;

/// <summary>What the goose does during one visit, on a fake clock with a stand-in for its window.</summary>
public class GooseBehaviorTests
{
    private static readonly TimeSpan BreakLength = TimeSpan.FromMinutes(5);

    private static readonly GooseVisit BreakVisit = new("HONK!", "Time to stretch your legs.", "Take 5 minutes.",
        "Okay, break time!", AllowSnooze: true, AllowFinishUp: true, StartsBreak: true);

    [Fact]
    public void It_walks_in_from_off_screen_then_asks_for_a_break()
    {
        var g = new GooseHarness();
        // Just the goose's width (not the window's) off the left edge, facing in.
        Assert.Equal(-GooseHarness.FigureRight, g.View.WindowLeft);
        Assert.Equal(1, g.View.Facing);
        Assert.Equal(GoosePhase.WalkingIn, g.Goose.Phase);

        g.Work(TimeSpan.FromSeconds(1));
        Assert.Equal(GoosePhase.WalkingIn, g.Goose.Phase);
        Assert.Null(g.View.Bubble);

        g.WalkIn();
        Assert.Equal(GoosePhase.Talking, g.Goose.Phase);
        Assert.Equal(GooseQuestion.TakeABreak, g.Goose.Question);
        // Stopped a short way in (here 4% of the screen's width, from the middle of the random range).
        Assert.Equal(GooseHarness.AreaWidth * 0.04, g.View.WindowLeft, precision: 6);
        Assert.Equal(0, g.View.Stride);
        Assert.Equal(new SpeechBubble("HONK!", "Time to stretch your legs.", "Take 5 minutes.", "Okay, break time!",
            "Let me finish up", "Snooze 5 min"), g.View.Bubble);
        Assert.Equal(1, g.View.BubblePops);
        Assert.True(g.Goose.StillAskingForBreak);
        Assert.False(g.Goose.OnBreak);
    }

    [Fact]
    public void It_can_come_in_from_the_right()
    {
        var g = new GooseHarness(fromRight: true);
        Assert.Equal(GooseHarness.AreaWidth - GooseHarness.FigureLeft, g.View.WindowLeft);
        Assert.Equal(-1, g.View.Facing);

        g.WalkIn();
        Assert.Equal(GooseHarness.AreaWidth - GooseHarness.WindowWidth - GooseHarness.AreaWidth * 0.04,
            g.View.WindowLeft, precision: 6);
        Assert.Equal(-1, g.View.Facing);
    }

    [Fact]
    public void Visits_without_finishing_up_or_snoozing_show_only_the_main_button()
    {
        var visit = new GooseVisit("Ta-da!", "Looking sharp.", null, "Looking good!",
            AllowSnooze: false, AllowFinishUp: false, StartsBreak: false, JustVisiting: true);
        var g = new GooseHarness(visit, naps: false);
        g.WalkIn();

        Assert.Equal(new SpeechBubble("Ta-da!", "Looking sharp.", null, "Looking good!", null, null), g.View.Bubble);
    }

    [Fact]
    public void Taking_the_break_naps_until_the_break_is_over_and_the_user_is_back()
    {
        var g = new GooseHarness();
        g.WalkIn();

        g.Goose.PrimaryClicked();
        Assert.Equal(new[] { GooseOutcome.Dismissed }, g.Answers);
        Assert.Equal(GoosePhase.Sleeping, g.Goose.Phase);
        Assert.Null(g.View.Bubble);
        Assert.True(g.View.Sleeping);
        Assert.True(g.View.BreakCardVisible);
        Assert.Equal(1, g.View.BreakCardPops);
        Assert.Equal("Enjoy your break", g.View.BreakTitle);
        Assert.Equal("5:00 left", g.View.BreakText);
        Assert.StartsWith($"Napping until {g.Now + BreakLength:HH:mm}.", g.View.ToolTip);
        Assert.False(g.Goose.StillAskingForBreak);
        Assert.True(g.Goose.OnBreak);

        g.BeAway(TimeSpan.FromMinutes(2));
        Assert.Equal("3:00 left", g.View.BreakText);
        Assert.Equal(0.4, g.View.BreakFraction, precision: 6);
        Assert.Equal(NapLook.Asleep, g.View.Look);

        // The break is over, but nobody's back yet: it naps on.
        g.BeAway(TimeSpan.FromMinutes(4));
        Assert.Equal(GoosePhase.BreakOver, g.Goose.Phase);
        Assert.Equal("Break's over", g.View.BreakTitle);
        Assert.Equal("Waiting for you to come back", g.View.BreakText);
        Assert.Equal(1, g.View.BreakFraction);
        Assert.Equal("Break's over. I'll wake up when you're back.", g.View.ToolTip);
        Assert.True(g.View.Sleeping);
        Assert.True(g.Goose.OnBreak);

        g.ComeBack();
        Assert.Equal(GoosePhase.Talking, g.Goose.Phase);
        Assert.Equal(GooseQuestion.ReadyToFocus, g.Goose.Question);
        Assert.False(g.View.Sleeping);
        Assert.False(g.View.BreakCardVisible);
        Assert.Null(g.View.ToolTip);
        Assert.Equal(new SpeechBubble("Welcome back!", "Break's over. Ready to focus again?", null,
            "Ready to focus", "5 more minutes", null), g.View.Bubble);
        Assert.True(g.Goose.OnBreak);

        g.Goose.PrimaryClicked();
        Assert.Equal(1, g.ReadyToFocusCount);
        Assert.Equal(GoosePhase.WalkingOut, g.Goose.Phase);
        Assert.False(g.Goose.OnBreak);
        Assert.False(g.Goose.StillAskingForBreak);
        // Back the way it came.
        g.Frame();
        Assert.Equal(-1, g.View.Facing);

        g.WalkOut();
        Assert.True(g.View.Closed);
        Assert.Equal(1, g.FinishedCount);
        Assert.Single(g.Answers);
        Assert.Empty(g.BreakExtensions);
        Assert.Empty(g.BreaksWhileAway);
    }

    [Fact]
    public void Clicking_the_goose_once_the_break_is_over_wakes_it_up()
    {
        var g = new GooseHarness();
        g.WalkIn();
        g.Goose.PrimaryClicked();
        g.BeAway(BreakLength + TimeSpan.FromSeconds(1));
        Assert.Equal(GoosePhase.BreakOver, g.Goose.Phase);

        g.Goose.FigureClicked();
        Assert.Equal(GooseQuestion.ReadyToFocus, g.Goose.Question);
        Assert.Equal(GoosePhase.Talking, g.Goose.Phase);
    }

    [Fact]
    public void A_few_more_minutes_naps_again_and_extends_the_break()
    {
        var g = new GooseHarness();
        g.WalkIn();
        g.Goose.PrimaryClicked();
        g.BeAway(BreakLength + TimeSpan.FromSeconds(1));
        g.ComeBack();

        g.Goose.SecondaryClicked();
        Assert.Equal(new[] { g.Now + TimeSpan.FromMinutes(5) }, g.BreakExtensions);
        Assert.Equal(GoosePhase.Sleeping, g.Goose.Phase);
        Assert.Equal("A little longer, then", g.View.BreakTitle);
        Assert.Equal(2, g.View.BreakCardPops);
        Assert.True(g.View.Sleeping);
        Assert.Equal(0, g.ReadyToFocusCount);
    }

    [Fact]
    public void Without_a_break_length_taking_the_break_just_walks_off()
    {
        var g = new GooseHarness(naps: false);
        g.WalkIn();

        g.Goose.PrimaryClicked();
        Assert.Equal(new[] { GooseOutcome.Dismissed }, g.Answers);
        Assert.Equal(GoosePhase.WalkingOut, g.Goose.Phase);
        Assert.False(g.View.Sleeping);

        g.WalkOut();
        Assert.Equal(1, g.FinishedCount);
    }

    [Fact]
    public void Finishing_up_peeks_at_the_edge_until_clicked_then_asks_again()
    {
        var g = new GooseHarness();
        g.WalkIn();

        g.Goose.SecondaryClicked();
        Assert.Empty(g.Answers);
        Assert.Equal(GoosePhase.ToEdge, g.Goose.Phase);
        Assert.Null(g.View.Bubble);
        Assert.True(g.Goose.StillAskingForBreak);

        g.WorkUntil(() => g.Goose.Phase == GoosePhase.Peeking);
        Assert.Equal(1, g.View.Facing);
        Assert.Equal("Click me when you're ready for your break", g.View.ToolTip);
        Assert.True(g.Goose.StillAskingForBreak);
        // Only the front of the goose shows past the left edge.
        double peekLeft = 40 - (GooseHarness.FigureLeft + GooseHarness.BeakTipX);
        Assert.Equal(peekLeft, g.View.WindowLeft, precision: 6);

        // It leans out a bit further while the mouse is at the edge, near it.
        g.View.CursorX = 20;
        g.Work(TimeSpan.FromSeconds(2));
        Assert.Equal(peekLeft + 18, g.View.WindowLeft, precision: 3);
        Assert.Equal((0d, 68d), g.View.CursorStrip);
        g.View.CursorX = null;
        g.Work(TimeSpan.FromSeconds(2));
        Assert.Equal(peekLeft, g.View.WindowLeft, precision: 3);

        g.Goose.FigureClicked();
        Assert.Equal(GoosePhase.Returning, g.Goose.Phase);
        Assert.Null(g.View.ToolTip);
        Assert.True(g.Goose.StillAskingForBreak);

        g.WalkIn();
        Assert.Equal(GooseQuestion.TakeABreak, g.Goose.Question);
        // The same question, and it offers to wait again.
        Assert.Equal(new SpeechBubble("HONK!", "All finished up and saved? Then you're ready for your break!",
            "Take 5 minutes.", "Okay, break time!", "Let me finish up", "Snooze 5 min"), g.View.Bubble);

        g.Goose.PrimaryClicked();
        Assert.Equal(new[] { GooseOutcome.Dismissed }, g.Answers);
        Assert.Equal(GoosePhase.Sleeping, g.Goose.Phase);
    }

    [Fact]
    public void A_forgotten_goose_at_the_edge_comes_back_to_ask_by_itself()
    {
        var g = new GooseHarness();
        g.WalkIn();
        g.Goose.SecondaryClicked();
        g.WorkUntil(() => g.Goose.Phase == GoosePhase.Peeking);

        g.Work(GooseBehavior.FinishUpReminder - TimeSpan.FromSeconds(1));
        Assert.Equal(GoosePhase.Peeking, g.Goose.Phase);

        g.Work(TimeSpan.FromSeconds(1));
        Assert.Equal(GoosePhase.Returning, g.Goose.Phase);
        Assert.Null(g.View.ToolTip);
        Assert.True(g.Goose.StillAskingForBreak);

        g.WalkIn();
        Assert.Equal(GooseQuestion.TakeABreak, g.Goose.Question);
        Assert.Equal(new SpeechBubble("HONK!", "Done finishing up? Your break is waiting for you!",
            "Take 5 minutes.", "Okay, break time!", "Let me finish up", "Snooze 5 min"), g.View.Bubble);
        Assert.Empty(g.Answers);

        // Ignored at the computer, it keeps asking: that's no reason to start a break.
        g.Work(TimeSpan.FromMinutes(10));
        Assert.Equal(GoosePhase.Talking, g.Goose.Phase);
        Assert.Equal(GooseQuestion.TakeABreak, g.Goose.Question);
        Assert.Empty(g.Answers);
        Assert.Empty(g.BreaksWhileAway);
    }

    [Fact]
    public void Finishing_up_again_peeks_again_and_the_reminder_starts_over()
    {
        var g = new GooseHarness();
        g.WalkIn();
        g.Goose.SecondaryClicked();
        g.WorkUntil(() => g.Goose.Phase == GoosePhase.Peeking);
        g.Work(GooseBehavior.FinishUpReminder);
        g.WalkIn();
        Assert.Equal("Let me finish up", g.View.Bubble!.SecondaryButton);

        // Still not done: it goes back to the edge, and waits the whole reminder time again.
        g.Goose.SecondaryClicked();
        Assert.Equal(GoosePhase.ToEdge, g.Goose.Phase);
        Assert.Empty(g.Answers);
        g.WorkUntil(() => g.Goose.Phase == GoosePhase.Peeking);
        Assert.Equal("Click me when you're ready for your break", g.View.ToolTip);
        g.Work(GooseBehavior.FinishUpReminder - TimeSpan.FromSeconds(1));
        Assert.Equal(GoosePhase.Peeking, g.Goose.Phase);

        // Leaving now still counts as the break.
        var leftAt = g.Now;
        g.BeAway(BreakLength + TimeSpan.FromSeconds(1));
        g.ComeBack();
        Assert.Equal(new[] { leftAt }, g.BreaksWhileAway);
        Assert.Equal(GooseQuestion.ReadyToFocus, g.Goose.Question);
        Assert.Empty(g.Answers);
    }

    [Fact]
    public void Leaving_while_it_waits_at_the_edge_counts_as_the_break()
    {
        var g = new GooseHarness();
        g.WalkIn();
        g.Goose.SecondaryClicked();
        g.WorkUntil(() => g.Goose.Phase == GoosePhase.Peeking);
        g.Work(TimeSpan.FromSeconds(30));
        var leftAt = g.Now;

        // Gone longer than the reminder: it comes back to ask while nobody's there, and keeps counting.
        g.BeAway(BreakLength + TimeSpan.FromSeconds(1));
        Assert.Equal(GoosePhase.Talking, g.Goose.Phase);
        Assert.Equal(GooseQuestion.TakeABreak, g.Goose.Question);
        Assert.Empty(g.BreaksWhileAway);

        g.ComeBack();
        Assert.Equal(new[] { leftAt }, g.BreaksWhileAway);
        Assert.Equal(GooseQuestion.ReadyToFocus, g.Goose.Question);
        Assert.Equal("Looks like you already took a break while I waited. Ready to focus again?", g.View.Bubble!.Message);
        Assert.Empty(g.Answers);
        Assert.True(g.Goose.OnBreak);
    }

    [Fact]
    public void Back_from_a_short_break_while_it_peeks_it_walks_over_to_ask_if_ready()
    {
        var g = new GooseHarness(breakLength: TimeSpan.FromMinutes(1));
        g.WalkIn();
        g.Goose.SecondaryClicked();
        g.WorkUntil(() => g.Goose.Phase == GoosePhase.Peeking);
        var leftAt = g.Now;

        g.BeAway(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        Assert.Equal(GoosePhase.Peeking, g.Goose.Phase); // waits until they're back

        g.ComeBack();
        Assert.Equal(new[] { leftAt }, g.BreaksWhileAway);
        Assert.Equal(GoosePhase.Returning, g.Goose.Phase);
        Assert.Null(g.View.Bubble); // asks once it's back where it asks, not from the edge
        Assert.Null(g.View.ToolTip);
        Assert.True(g.Goose.OnBreak);
        Assert.False(g.Goose.StillAskingForBreak);

        g.WalkIn();
        Assert.Equal(GooseQuestion.ReadyToFocus, g.Goose.Question);
        Assert.Equal("Looks like you already took a break while I waited. Ready to focus again?", g.View.Bubble!.Message);

        g.Goose.PrimaryClicked();
        Assert.Equal(1, g.ReadyToFocusCount);
        Assert.Empty(g.Answers);
    }

    [Fact]
    public void Snoozing_walks_off()
    {
        var g = new GooseHarness();
        g.WalkIn();

        g.Goose.LinkClicked();
        Assert.Equal(new[] { GooseOutcome.Snoozed }, g.Answers);
        Assert.Equal(GoosePhase.WalkingOut, g.Goose.Phase);
        Assert.Null(g.View.Bubble);
        Assert.False(g.Goose.StillAskingForBreak);

        g.WalkOut();
        Assert.Equal(1, g.FinishedCount);
    }

    [Fact]
    public void Clicking_the_napping_goose_asks_before_ending_the_break_and_resting_naps_on()
    {
        var g = new GooseHarness();
        g.WalkIn();
        g.Goose.PrimaryClicked();
        g.BeAway(TimeSpan.FromMinutes(1));

        g.Goose.FigureClicked();
        Assert.Equal(GoosePhase.Talking, g.Goose.Phase);
        Assert.Equal(GooseQuestion.EndBreakEarly, g.Goose.Question);
        Assert.False(g.View.Sleeping);
        Assert.False(g.View.BreakCardVisible);
        Assert.Null(g.View.ToolTip);
        var bubble = g.View.Bubble!;
        Assert.Equal("Hey! I was napping…", bubble.Title);
        Assert.Equal("There's still 4:00 of your break left.", bubble.Hint);
        Assert.Equal("Keep resting", bubble.PrimaryButton);
        Assert.Equal("End my break", bubble.SecondaryButton);
        Assert.Null(bubble.LinkButton);
        Assert.True(g.Goose.OnBreak);
        Assert.False(g.Goose.StillAskingForBreak);

        g.Goose.PrimaryClicked();
        Assert.Equal(GoosePhase.Sleeping, g.Goose.Phase);
        Assert.True(g.View.Sleeping);
        Assert.True(g.View.BreakCardVisible);
        Assert.Equal(1, g.View.BreakCardPops); // back to the nap without popping up again
        Assert.Equal("Enjoy your break", g.View.BreakTitle);
        Assert.Equal(0, g.ReadyToFocusCount);
    }

    [Fact]
    public void Ending_the_break_anyway_walks_off_in_a_huff()
    {
        var g = new GooseHarness();
        g.WalkIn();
        g.Goose.PrimaryClicked();
        g.BeAway(TimeSpan.FromMinutes(1));
        g.Goose.FigureClicked();

        g.Goose.SecondaryClicked();
        Assert.Equal(GooseQuestion.EndBreakAnyway, g.Goose.Question);
        Assert.True(g.View.Huffy); // standing up, it woke up for this
        var bubble = g.View.Bubble!;
        Assert.Equal("Really?", bubble.Title);
        Assert.Null(bubble.Hint);
        Assert.Equal("Fine, I'll go", bubble.PrimaryButton);
        Assert.Equal("Yes, end my break", bubble.SecondaryButton);
        Assert.True(g.Goose.OnBreak);

        g.Goose.SecondaryClicked();
        Assert.Equal(1, g.ReadyToFocusCount);
        Assert.Equal(GoosePhase.WalkingOut, g.Goose.Phase);
        Assert.Null(g.View.Bubble);
        Assert.True(g.View.Huffy);
        Assert.Equal(-14, g.View.HeadAngle);

        g.WalkOut();
        Assert.Equal(1, g.FinishedCount);
        Assert.Equal(new[] { GooseOutcome.Dismissed }, g.Answers);
    }

    [Fact]
    public void Going_after_all_naps_on_for_the_rest_of_the_break()
    {
        var g = new GooseHarness();
        g.WalkIn();
        g.Goose.PrimaryClicked();
        g.BeAway(TimeSpan.FromMinutes(1));
        g.Goose.FigureClicked();
        g.Goose.SecondaryClicked();

        g.Goose.PrimaryClicked();
        Assert.Equal(GoosePhase.Sleeping, g.Goose.Phase);
        Assert.True(g.View.Sleeping);
        Assert.False(g.View.Huffy);
        Assert.Equal(0, g.ReadyToFocusCount);
    }

    [Fact]
    public void If_the_break_runs_out_while_it_asks_about_ending_it_it_asks_if_ready_instead()
    {
        var g = new GooseHarness();
        g.WalkIn();
        g.Goose.PrimaryClicked();
        g.BeAway(TimeSpan.FromMinutes(4));
        g.Goose.FigureClicked();
        g.Goose.SecondaryClicked();
        Assert.True(g.View.Huffy);

        g.Work(TimeSpan.FromMinutes(1.5));
        Assert.Equal(GooseQuestion.ReadyToFocus, g.Goose.Question);
        Assert.False(g.View.Huffy); // all is forgiven
        Assert.Equal("Welcome back!", g.View.Bubble!.Title);
    }

    [Fact]
    public void Using_the_computer_during_the_nap_stirs_the_goose_then_it_asks_and_gives_up_asking()
    {
        var g = new GooseHarness();
        g.WalkIn();
        g.Goose.PrimaryClicked();

        // A moment to get up and go first.
        g.Work(NapWatch.Grace - TimeSpan.FromSeconds(0.5));
        Assert.Equal(NapLook.Asleep, g.View.Look);

        g.Work(TimeSpan.FromSeconds(2));
        Assert.Equal(NapLook.Drowsy, g.View.Look);
        Assert.Equal(GoosePhase.Sleeping, g.Goose.Phase);

        // Leaving it alone for a bit: it dozes off again.
        g.BeAway(NapWatch.IdleBeforeDozingOff + TimeSpan.FromSeconds(0.5));
        Assert.Equal(NapLook.Asleep, g.View.Look);

        g.Work(NapWatch.UseBeforeAsking + TimeSpan.FromSeconds(0.5));
        Assert.Equal(GoosePhase.Talking, g.Goose.Phase);
        Assert.Equal(GooseQuestion.StillOnBreak, g.Goose.Question);
        Assert.True(g.View.Sleeping); // still sitting there, half awake
        Assert.Equal(NapLook.Drowsy, g.View.Look);
        Assert.False(g.View.BreakCardVisible);
        Assert.Null(g.View.ToolTip);
        var bubble = g.View.Bubble!;
        Assert.Equal("Psst…", bubble.Title);
        Assert.StartsWith("There's still 4:", bubble.Hint);
        Assert.Equal("I'm going!", bubble.PrimaryButton);
        Assert.Equal("End my break", bubble.SecondaryButton);
        Assert.True(g.Goose.OnBreak);

        // They did get up after all: back to the nap, without a fuss.
        g.BeAway(NapWatch.IdleBeforeGivingUpAsking - TimeSpan.FromSeconds(1));
        Assert.Equal(GooseQuestion.StillOnBreak, g.Goose.Question);
        Assert.Equal(GoosePhase.Talking, g.Goose.Phase);
        g.BeAway(TimeSpan.FromSeconds(1.5));
        Assert.Equal(GoosePhase.Sleeping, g.Goose.Phase);
        Assert.Equal(NapLook.Asleep, g.View.Look);
        Assert.True(g.View.BreakCardVisible);
        Assert.Null(g.View.Bubble);
        Assert.Empty(g.BreakExtensions);
        Assert.Equal(0, g.ReadyToFocusCount);
    }

    [Fact]
    public void Ending_the_break_when_caught_working_looks_unimpressed_while_sitting()
    {
        var g = new GooseHarness();
        g.WalkIn();
        g.Goose.PrimaryClicked();
        g.Work(NapWatch.Grace + NapWatch.UseBeforeAsking + TimeSpan.FromSeconds(1));
        Assert.Equal(GooseQuestion.StillOnBreak, g.Goose.Question);

        g.Goose.SecondaryClicked();
        Assert.Equal(GooseQuestion.EndBreakAnyway, g.Goose.Question);
        Assert.True(g.View.Sleeping);
        Assert.Equal(NapLook.Unimpressed, g.View.Look);

        g.Goose.SecondaryClicked();
        Assert.Equal(1, g.ReadyToFocusCount);
        Assert.False(g.View.Sleeping);
        Assert.True(g.View.Huffy);
        Assert.Equal(GoosePhase.WalkingOut, g.Goose.Phase);
    }

    [Fact]
    public void Being_away_for_a_breaks_length_while_asked_counts_as_the_break()
    {
        var g = new GooseHarness();
        g.WalkIn();
        g.Work(TimeSpan.FromSeconds(10));
        var leftAt = g.Now;

        g.BeAway(BreakLength + TimeSpan.FromSeconds(1));
        Assert.Equal(GooseQuestion.TakeABreak, g.Goose.Question); // waits until they're back
        Assert.Empty(g.BreaksWhileAway);

        g.ComeBack();
        Assert.Equal(new[] { leftAt }, g.BreaksWhileAway);
        Assert.Equal(GooseQuestion.ReadyToFocus, g.Goose.Question);
        Assert.Equal(GoosePhase.Talking, g.Goose.Phase);
        Assert.Equal("Looks like you already took a break while I waited. Ready to focus again?", g.View.Bubble!.Message);
        Assert.Equal(2, g.View.BubblePops);
        Assert.Empty(g.Answers);
        Assert.True(g.Goose.OnBreak);
        Assert.False(g.Goose.StillAskingForBreak);
    }

    [Fact]
    public void Sitting_still_before_the_goose_came_is_no_break()
    {
        var g = new GooseHarness(idleAtStart: TimeSpan.FromHours(1));
        g.WalkIn(away: true);
        g.BeAway(BreakLength - TimeSpan.FromSeconds(10));
        g.ComeBack();

        Assert.Empty(g.BreaksWhileAway);
        Assert.Equal(GooseQuestion.TakeABreak, g.Goose.Question);
    }

    [Fact]
    public void Without_a_break_length_being_away_is_not_a_break()
    {
        var g = new GooseHarness(naps: false);
        g.WalkIn();
        g.BeAway(TimeSpan.FromMinutes(30));
        g.ComeBack();

        Assert.Empty(g.BreaksWhileAway);
        Assert.Equal(GooseQuestion.TakeABreak, g.Goose.Question);
    }

    [Fact]
    public void Leaving_now_fades_out_without_answering()
    {
        var g = new GooseHarness();
        g.WalkIn();

        g.Goose.LeaveNow();
        Assert.True(g.View.FadedOut);
        Assert.Equal(GoosePhase.WalkingOut, g.Goose.Phase);
        Assert.False(g.Goose.StillAskingForBreak);
        Assert.False(g.Goose.OnBreak);
        Assert.Equal(1, g.FinishedCount);
        Assert.Empty(g.Answers);
        Assert.Equal(0, g.ReadyToFocusCount);
    }

    [Fact]
    public void Walking_away_mid_nap_raises_no_answers()
    {
        var g = new GooseHarness();
        g.WalkIn();
        g.Goose.PrimaryClicked();
        g.BeAway(TimeSpan.FromMinutes(1));

        g.Goose.WalkAway();
        Assert.Equal(GoosePhase.WalkingOut, g.Goose.Phase);
        Assert.False(g.View.Sleeping);
        Assert.False(g.View.BreakCardVisible);
        Assert.Null(g.View.ToolTip);
        Assert.Null(g.View.Bubble);
        Assert.False(g.Goose.OnBreak);

        g.WalkOut();
        Assert.Equal(1, g.FinishedCount);
        Assert.Equal(new[] { GooseOutcome.Dismissed }, g.Answers); // only the one from taking the break
        Assert.Equal(0, g.ReadyToFocusCount);
        Assert.Empty(g.BreakExtensions);
    }

    [Fact]
    public void Walking_away_while_asking_or_peeking_raises_no_answers()
    {
        var asking = new GooseHarness();
        asking.WalkIn();
        asking.Goose.WalkAway();
        Assert.Null(asking.View.Bubble);
        asking.WalkOut();
        Assert.Equal(1, asking.FinishedCount);
        Assert.Empty(asking.Answers);

        var peeking = new GooseHarness();
        peeking.WalkIn();
        peeking.Goose.SecondaryClicked();
        peeking.WorkUntil(() => peeking.Goose.Phase == GoosePhase.Peeking);
        peeking.Goose.WalkAway();
        Assert.Equal(GoosePhase.WalkingOut, peeking.Goose.Phase);
        Assert.Null(peeking.View.ToolTip);
        peeking.WalkOut();
        Assert.Equal(1, peeking.FinishedCount);
        Assert.Empty(peeking.Answers);
    }

    [Fact]
    public void Poking_the_goose_makes_it_flap()
    {
        var g = new GooseHarness();
        g.WalkIn();
        g.Goose.FigureClicked();
        Assert.Equal(1, g.View.Flaps);
        Assert.Equal(GoosePhase.Talking, g.Goose.Phase);
    }

    [Fact]
    public void The_buttons_do_nothing_once_it_is_leaving()
    {
        var g = new GooseHarness();
        g.WalkIn();
        g.Goose.LinkClicked();

        g.Goose.PrimaryClicked();
        g.Goose.SecondaryClicked();
        Assert.Equal(new[] { GooseOutcome.Snoozed }, g.Answers);
        Assert.Equal(GoosePhase.WalkingOut, g.Goose.Phase);
    }

    /// <summary>
    /// One goose visit on a fake clock, frame by frame, with the user at the computer or away from it,
    /// and a stand-in for its window.
    /// </summary>
    private sealed class GooseHarness : IClock, IUserActivity
    {
        public const double AreaWidth = 1920;
        public const double WindowWidth = 440;
        public const double FigureWidth = 160;
        public const double BeakTipX = 109;
        public const double FigureLeft = (WindowWidth - FigureWidth) / 2;
        public const double FigureRight = FigureLeft + FigureWidth;

        private static readonly TimeSpan FrameTime = TimeSpan.FromMilliseconds(50);
        private TimeSpan _sinceShown;

        /// <param name="naps">Whether the goose naps during the break (it has a break length).</param>
        /// <param name="breakLength">How long the break is, if not <see cref="BreakLength"/>.</param>
        public GooseHarness(GooseVisit? visit = null, bool naps = true, bool fromRight = false, TimeSpan idleAtStart = default,
            TimeSpan? breakLength = null)
        {
            Idle = idleAtStart;
            View = new FakeView();
            Goose = new GooseBehavior(View, new GooseStage(0, AreaWidth, WindowWidth, FigureWidth, BeakTipX),
                visit ?? BreakVisit, 5, naps ? breakLength ?? BreakLength : null, this, this, new FakeRandom(fromRight));
            View.OnClosed = Goose.WindowClosed;
            Goose.Answered += Answers.Add;
            Goose.BreakExtended += BreakExtensions.Add;
            Goose.ReadyToFocus += () => ReadyToFocusCount++;
            Goose.TookBreakWhileAway += BreaksWhileAway.Add;
            Goose.Finished += () => FinishedCount++;
        }

        public DateTime Now { get; private set; } = new(2026, 11, 2, 9, 0, 0);
        public TimeSpan Idle { get; private set; }
        public FakeView View { get; }
        public GooseBehavior Goose { get; }

        public List<GooseOutcome> Answers { get; } = new();
        public List<DateTime> BreakExtensions { get; } = new();
        public List<DateTime> BreaksWhileAway { get; } = new();
        public int ReadyToFocusCount { get; private set; }
        public int FinishedCount { get; private set; }

        /// <summary>One frame passes, with the user at the computer or away from it.</summary>
        public void Frame(bool away = false)
        {
            Now += FrameTime;
            _sinceShown += FrameTime;
            Idle = away ? Idle + FrameTime : TimeSpan.Zero;
            Goose.NextFrame(_sinceShown);
        }

        /// <summary>Time passes while the user works at the computer.</summary>
        public void Work(TimeSpan time) => Pass(time, away: false);

        /// <summary>Time passes while nobody touches the mouse or keyboard.</summary>
        public void BeAway(TimeSpan time) => Pass(time, away: true);

        /// <summary>The user touches the mouse or keyboard again.</summary>
        public void ComeBack() => Frame(away: false);

        public void WorkUntil(Func<bool> done, bool away = false)
        {
            for (int frame = 0; !done(); frame++)
            {
                Assert.True(frame < 10_000, "The goose never got there.");
                Frame(away);
            }
        }

        /// <summary>Until it has walked in (or back) and is asking.</summary>
        public void WalkIn(bool away = false) => WorkUntil(() => Goose.Phase == GoosePhase.Talking, away);

        /// <summary>Until it has walked off the screen.</summary>
        public void WalkOut() => WorkUntil(() => View.Closed);

        private void Pass(TimeSpan time, bool away)
        {
            for (var passed = TimeSpan.Zero; passed < time; passed += FrameTime) Frame(away);
        }

        TimeSpan IUserActivity.IdleTime() => Idle;
    }

    /// <summary>Comes in from the chosen side, walks in half of the way it could, and always says the first line.</summary>
    private sealed class FakeRandom : Random
    {
        private bool _fromRight;

        public FakeRandom(bool fromRight) => _fromRight = fromRight;

        public override int Next(int maxValue)
        {
            // The first choice is the side it comes from.
            if (!_fromRight) return 0;
            _fromRight = false;
            return 1;
        }

        public override double NextDouble() => 0.5;
    }

    /// <summary>Remembers what the goose was told to look like, with the figure's own rules about sitting.</summary>
    private sealed class FakeView : IGooseView
    {
        private bool _sleeping;
        private bool _huffy;
        private NapLook _look;

        public int Facing { get; set; }
        public double HeadAngle { get; set; }
        public double Stride { get; private set; }
        public int Flaps { get; private set; }
        public string? ToolTip { get; private set; }
        public SpeechBubble? Bubble { get; private set; }
        public int BubblePops { get; private set; }
        public bool BreakCardVisible { get; private set; }
        public int BreakCardPops { get; private set; }
        public string? BreakTitle { get; private set; }
        public double BreakFraction { get; private set; }
        public string? BreakText { get; private set; }
        public double WindowLeft { get; private set; }
        public bool FadedOut { get; private set; }

        /// <summary>Where the cursor is along the screen, at the goose's height; null when it's elsewhere.</summary>
        public double? CursorX { get; set; }

        /// <summary>The strip the goose last asked about.</summary>
        public (double From, double To) CursorStrip { get; private set; }

        public bool Closed { get; private set; }

        /// <summary>What the window does once it has closed.</summary>
        public Action? OnClosed { get; set; }

        public void SetStride(double swing) => Stride = swing;

        public bool Sleeping
        {
            get => _sleeping;
            set
            {
                if (value == _sleeping) return;
                _sleeping = value;
                _look = NapLook.Asleep;
                _huffy = false;
            }
        }

        public NapLook Look
        {
            get => _look;
            set
            {
                if (_sleeping) _look = value;
            }
        }

        public bool Huffy
        {
            get => _huffy;
            set
            {
                if (!_sleeping) _huffy = value;
            }
        }

        public void Flap() => Flaps++;

        string? IGooseView.FigureToolTip { set => ToolTip = value; }

        public void ShowBubble(SpeechBubble bubble)
        {
            Bubble = bubble;
            BubblePops++;
        }

        public void HideBubble() => Bubble = null;

        public void ShowBreakCard(bool popUp)
        {
            BreakCardVisible = true;
            if (popUp) BreakCardPops++;
        }

        public void HideBreakCard() => BreakCardVisible = false;

        string IGooseView.BreakTitle { set => BreakTitle = value; }

        public void ShowBreakProgress(double fraction, string text)
        {
            BreakFraction = fraction;
            BreakText = text;
        }

        double IGooseView.WindowLeft { set => WindowLeft = value; }

        public bool IsCursorBetween(double fromX, double toX)
        {
            CursorStrip = (fromX, toX);
            return CursorX is { } x && x >= fromX && x <= toX;
        }

        public void FadeOutAndClose()
        {
            FadedOut = true;
            CloseWindow();
        }

        public void CloseWindow()
        {
            Closed = true;
            OnClosed?.Invoke();
        }
    }
}
