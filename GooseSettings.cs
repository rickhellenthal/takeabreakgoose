using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace TakeABreakGoose;

public sealed class GooseSettings
{
    /// <summary>Start an open-ended focus session as soon as the app starts.</summary>
    public bool AutoStartSession { get; set; }

    public int AutoStartIntervalMinutes { get; set; } = 60;

    // Replaced by AutoStartSession. Only read to carry over the choice from older versions.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AlwaysOn { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AlwaysOnIntervalMinutes { get; set; }

    /// <summary>How long a break lasts; the next reminder starts counting after it. 0 = count right away.</summary>
    public int BreakMinutes { get; set; } = 5;

    public int SnoozeMinutes { get; set; } = 5;

    /// <summary>After this many idle minutes the user counts as away (on a break). 0 = off.</summary>
    public int AwayMinutes { get; set; } = 5;

    // The last session the user started, used to pre-fill the start form.
    public int LastSessionIntervalMinutes { get; set; } = 45;
    public SessionEndMode LastSessionEndMode { get; set; } = SessionEndMode.None;
    public int LastSessionLengthMinutes { get; set; } = 120;
    public TimeSpan LastSessionEndTime { get; set; } = new(17, 0, 0);

    public ModifierKeys HotkeyModifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;

    public Key HotkeyKey { get; set; } = Key.G;

    public List<string> Messages { get; set; } = DefaultMessages();

    public static List<string> DefaultMessages() => new()
    {
        "Time to stand up and stretch those legs.",
        "Look at something far away for 20 seconds. Your eyes will thank you.",
        "Hydration check! Go get a glass of water.",
        "Step away from the keyboard. The bug will still be there in five minutes.",
        "Shoulders down, jaw unclenched, take a deep breath.",
        "Go for a short walk. Even geese waddle around now and then.",
        "Hyperfocus is a superpower, but even superheroes take breaks.",
        "Have you eaten something today?",
    };

    public GooseSettings Clone()
    {
        var clone = (GooseSettings)MemberwiseClone();
        clone.Messages = new List<string>(Messages);
        return clone;
    }

    public string HotkeyText => FormatHotkey(HotkeyModifiers, HotkeyKey);

    public static string FormatHotkey(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key is >= Key.D0 and <= Key.D9 ? ((int)(key - Key.D0)).ToString() : key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>Clamps values loaded from disk so a hand-edited file can't break the app.</summary>
    public void Sanitize()
    {
        // "Always on" reminders from older versions become a session that starts automatically.
        if (AlwaysOn == true)
        {
            AutoStartSession = true;
            AutoStartIntervalMinutes = AlwaysOnIntervalMinutes ?? AutoStartIntervalMinutes;
        }
        AlwaysOn = null;
        AlwaysOnIntervalMinutes = null;

        AutoStartIntervalMinutes = Math.Clamp(AutoStartIntervalMinutes, 1, 24 * 60);
        BreakMinutes = Math.Clamp(BreakMinutes, 0, 120);
        SnoozeMinutes = Math.Clamp(SnoozeMinutes, 1, 24 * 60);
        AwayMinutes = Math.Clamp(AwayMinutes, 0, 120);
        LastSessionIntervalMinutes = Math.Clamp(LastSessionIntervalMinutes, 1, 24 * 60);
        LastSessionLengthMinutes = Math.Clamp(LastSessionLengthMinutes, 1, 24 * 60);
        if (LastSessionEndTime < TimeSpan.Zero || LastSessionEndTime >= TimeSpan.FromDays(1))
            LastSessionEndTime = new TimeSpan(17, 0, 0);
        Messages = (Messages ?? new List<string>())
            .Select(m => m?.Trim() ?? "")
            .Where(m => m.Length > 0)
            .ToList();
        if (Messages.Count == 0) Messages = DefaultMessages();
        if (HotkeyKey == Key.None) HotkeyKey = Key.G;
        if (HotkeyModifiers == ModifierKeys.None) HotkeyModifiers = ModifierKeys.Control | ModifierKeys.Alt;
    }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TakeABreakGoose",
        "settings.json");

    public static GooseSettings Load()
    {
        GooseSettings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<GooseSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new GooseSettings()
                : new GooseSettings();
        }
        catch (Exception)
        {
            // A corrupt settings file shouldn't keep the goose from showing up.
            settings = new GooseSettings();
        }
        settings.Sanitize();
        return settings;
    }

    public static void Save(GooseSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
