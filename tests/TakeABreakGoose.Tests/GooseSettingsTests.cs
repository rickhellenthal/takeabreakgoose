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
    public void The_shortcut_from_older_versions_is_read_as_it_was_saved()
    {
        // Written by 1.7.0, when the settings still used WPF's ModifierKeys and Key.
        var settings = SettingsStore.Parse("""{ "HotkeyModifiers": "Alt, Control, Shift", "HotkeyKey": "D5" }""");
        settings.Sanitize();

        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, settings.HotkeyModifiers);
        Assert.Equal("D5", settings.HotkeyKey);
        Assert.Equal("Ctrl+Alt+Shift+5", settings.HotkeyText);
    }

    [Fact]
    public void The_shortcut_is_saved_the_way_older_versions_saved_it()
    {
        var settings = new GooseSettings { HotkeyModifiers = HotkeyModifiers.Windows | HotkeyModifiers.Alt, HotkeyKey = "F1" };

        string json = SettingsStore.Serialize(settings);

        Assert.Contains("\"HotkeyModifiers\": \"Alt, Windows\"", json);
        Assert.Contains("\"HotkeyKey\": \"F1\"", json);
    }

    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "HotkeyModifiers": "None", "HotkeyKey": "None" }""")]
    [InlineData("""{ "HotkeyKey": "" }""")]
    public void Without_a_shortcut_it_is_ctrl_alt_g(string json)
    {
        var settings = SettingsStore.Parse(json);
        settings.Sanitize();

        Assert.Equal("Ctrl+Alt+G", settings.HotkeyText);
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

    // ---- Language ----

    [Fact]
    public void The_language_is_English_by_default_and_for_older_files()
    {
        Assert.Equal(Languages.English, new GooseSettings().Language);

        var settings = SettingsStore.Parse("""{ "SnoozeMinutes": 10 }""");
        settings.Sanitize();
        Assert.Equal(Languages.English, settings.Language);
        Assert.Equal(GooseSettings.DefaultMessages(), settings.Messages);
    }

    [Fact]
    public void The_language_is_saved_and_read_back()
    {
        var settings = new GooseSettings { Language = Languages.Dutch };

        var read = SettingsStore.Parse(SettingsStore.Serialize(settings));

        Assert.Equal(Languages.Dutch, read.Language);
    }

    [Fact]
    public void A_language_the_app_doesnt_know_becomes_English()
    {
        // A newer version's language, say, or a typo in a hand-edited file.
        var settings = SettingsStore.Parse("""{ "Language": "fy" }""");
        settings.Sanitize();
        Assert.Equal(Languages.English, settings.Language);
    }

    [Fact]
    public void Every_language_has_as_many_default_messages()
    {
        var english = GooseSettings.DefaultMessages();
        foreach (var language in Languages.All)
        {
            var defaults = GooseSettings.DefaultMessages(language.Code);
            Assert.Equal(english.Count, defaults.Count);
            Assert.All(defaults, m => Assert.False(string.IsNullOrWhiteSpace(m)));
        }
        Assert.Equal("Heb je vandaag al iets gegeten?", GooseSettings.DefaultMessages(Languages.Dutch)[^1]);
    }

    [Fact]
    public void Untouched_default_messages_switch_to_the_new_language()
    {
        var settings = new GooseSettings { Language = Languages.Dutch };

        settings.TranslateDefaultMessages();

        Assert.Equal(GooseSettings.DefaultMessages(Languages.Dutch), settings.Messages);

        settings.Language = Languages.English;
        settings.TranslateDefaultMessages();

        Assert.Equal(GooseSettings.DefaultMessages(), settings.Messages);
    }

    [Fact]
    public void Messages_the_user_wrote_or_changed_stay_as_they_are()
    {
        var english = GooseSettings.DefaultMessages();
        var settings = new GooseSettings
        {
            Language = Languages.Dutch,
            Messages = new List<string> { english[0], english[1] + " Really!", "Drink some water." },
        };

        settings.TranslateDefaultMessages();

        Assert.Equal(new[]
        {
            GooseSettings.DefaultMessages(Languages.Dutch)[0],
            english[1] + " Really!",
            "Drink some water.",
        }, settings.Messages);
    }

    [Fact]
    public void Loading_puts_default_messages_in_the_settings_language()
    {
        // Also an old wording of an English default, written before Dutch existed.
        const string file = """
            {
              "Language": "nl",
              "Messages": [
                "Have you eaten something today?",
                "Step away from the keyboard. The bug will still be there in five minutes.",
                "Mijn eigen bericht."
              ]
            }
            """;

        var settings = SettingsStore.Parse(file);
        settings.Sanitize();

        Assert.Equal(new[]
        {
            "Heb je vandaag al iets gegeten?",
            "Stap even weg van het scherm. Waar je ook mee bezig bent, het is er over vijf minuten nog steeds.",
            "Mijn eigen bericht.",
        }, settings.Messages);
    }

    [Fact]
    public void An_empty_list_gets_the_defaults_in_the_settings_language()
    {
        var settings = new GooseSettings { Language = Languages.Dutch, Messages = new List<string>() };
        settings.Sanitize();
        Assert.Equal(GooseSettings.DefaultMessages(Languages.Dutch), settings.Messages);
    }
}
