using Xunit;

namespace TakeABreakGoose.Tests;

public class GooseSettingsTests
{
    [Fact]
    public void Always_on_from_older_versions_becomes_starting_a_session_automatically()
    {
        // A settings file from before "Start automatically" existed (StaySeconds and AwayMinutes are gone too).
        const string oldFile = """
            {
              "AlwaysOn": true,
              "AlwaysOnIntervalMinutes": 30,
              "StaySeconds": 60,
              "AwayMinutes": 5,
              "BreakMinutes": 10
            }
            """;

        var settings = SettingsStore.Parse(oldFile);
        settings.Sanitize();

        Assert.True(settings.AutoStartSession);
        Assert.Equal(30, settings.AutoStartIntervalMinutes);
        Assert.Equal(10 * 60, settings.LastSessionBreakSeconds); // the old default break length is remembered
        Assert.Null(settings.BreakMinutes);
        Assert.Null(settings.AlwaysOn);
        Assert.Null(settings.AlwaysOnIntervalMinutes);
    }

    [Fact]
    public void Without_always_on_nothing_starts_automatically()
    {
        var settings = SettingsStore.Parse("""{ "AlwaysOn": false, "AlwaysOnIntervalMinutes": 30 }""");
        settings.Sanitize();

        Assert.False(settings.AutoStartSession);
        Assert.Equal(60, settings.AutoStartIntervalMinutes);
    }

    [Fact]
    public void Settings_from_older_versions_keep_the_snooze_button()
    {
        var settings = SettingsStore.Parse("""{ "SnoozeMinutes": 10 }""");
        settings.Sanitize();

        Assert.True(settings.AllowSnooze);
        Assert.Equal(10, settings.SnoozeMinutes);
    }

    [Fact]
    public void Turning_snooze_off_is_remembered()
    {
        var settings = new GooseSettings { AllowSnooze = false, SnoozeMinutes = 10 };

        var loaded = SettingsStore.Parse(SettingsStore.Serialize(settings));

        Assert.False(loaded.AllowSnooze);
        Assert.Equal(10, loaded.SnoozeMinutes);
    }

    [Fact]
    public void Settings_from_older_versions_get_do_not_disturb_during_video_calls()
    {
        var settings = SettingsStore.Parse("""{ "BreakMinutes": 10 }""");

        Assert.True(settings.DoNotDisturbDuringCalls);
    }

    [Fact]
    public void Hand_edited_values_are_clamped_to_something_sensible()
    {
        var settings = new GooseSettings { LastSessionBreakSeconds = -5, SnoozeMinutes = 0, AutoStartIntervalMinutes = 100_000 };

        settings.Sanitize();

        Assert.Equal(0, settings.LastSessionBreakSeconds);
        Assert.Equal(1, settings.SnoozeMinutes);
        Assert.Equal(24 * 60, settings.AutoStartIntervalMinutes);
    }

    [Fact]
    public void Empty_messages_are_dropped_and_an_empty_list_gets_the_defaults()
    {
        var settings = new GooseSettings { Messages = new List<string> { "  ", "", "Drink some water.  " } };
        settings.Sanitize();
        Assert.Equal(new[] { "Drink some water." }, settings.Messages);

        settings.Messages = new List<string> { " " };
        settings.Sanitize();
        Assert.Equal(GooseSettings.DefaultMessages(), settings.Messages);
    }

    [Fact]
    public void Old_wordings_of_default_messages_get_the_new_wording()
    {
        var settings = new GooseSettings
        {
            Messages = new List<string>
            {
                "Step away from the keyboard. The bug will still be there in five minutes.",
                "Step away from the keyboard. My bug will still be there.", // the user's own: left alone
            },
        };

        settings.Sanitize();

        Assert.Equal(new[]
        {
            "Step away from the screen. Whatever you're working on will still be there in five minutes.",
            "Step away from the keyboard. My bug will still be there.",
        }, settings.Messages);
    }

    [Fact]
    public void A_copy_does_not_share_its_messages_with_the_original()
    {
        var settings = new GooseSettings();
        var copy = settings.Clone();

        copy.Messages.Add("Only in the copy");

        Assert.DoesNotContain("Only in the copy", settings.Messages);
    }
}
