using System;
using System.Collections.Generic;
using System.Linq;
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

    // Replaced by remembering the last session's break length. Only read to carry it over.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BreakMinutes { get; set; }

    /// <summary>The goose offers a Snooze button when it's time for a break.</summary>
    public bool AllowSnooze { get; set; } = true;

    public int SnoozeMinutes { get; set; } = 5;

    /// <summary>Turn Do not disturb on during a focus session while the user is in a call (see <see cref="Calls"/>).</summary>
    public bool DoNotDisturbDuringCalls { get; set; } = true;

    /// <summary>The user has been told, once, that the goose stays away during calls.</summary>
    public bool CallNoticeShown { get; set; }

    // The last session the user started, used to pre-fill the start form.
    public int LastSessionIntervalMinutes { get; set; } = 45;

    /// <summary>
    /// How long each break lasts, in seconds; also used for sessions that start automatically.
    /// 0 = no breaks are counted.
    /// </summary>
    public int LastSessionBreakSeconds { get; set; } = 5 * 60;
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
        "Step away from the screen. Whatever you're working on will still be there in five minutes.",
        "Shoulders down, jaw unclenched, take a deep breath.",
        "Go for a short walk. Even geese waddle around now and then.",
        "Hyperfocus is a superpower, but even superheroes take breaks.",
        "Have you eaten something today?",
    };

    // Default messages from older versions that have been reworded since. A saved list still holding
    // the old wording gets the new one; messages the user edited themselves are left alone.
    private static readonly Dictionary<string, string> RewordedDefaults = new()
    {
        ["Step away from the keyboard. The bug will still be there in five minutes."] =
            "Step away from the screen. Whatever you're working on will still be there in five minutes.",
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
        // The "Default break length" setting from older versions becomes the remembered break length.
        if (BreakMinutes is int breakMinutes) LastSessionBreakSeconds = breakMinutes * 60;
        BreakMinutes = null;

        AutoStartIntervalMinutes = Math.Clamp(AutoStartIntervalMinutes, 1, 24 * 60);
        LastSessionBreakSeconds = Math.Clamp(LastSessionBreakSeconds, 0, 120 * 60);
        SnoozeMinutes = Math.Clamp(SnoozeMinutes, 1, 24 * 60);
        LastSessionIntervalMinutes = Math.Clamp(LastSessionIntervalMinutes, 1, 24 * 60);
        LastSessionLengthMinutes = Math.Clamp(LastSessionLengthMinutes, 1, 24 * 60);
        if (LastSessionEndTime < TimeSpan.Zero || LastSessionEndTime >= TimeSpan.FromDays(1))
            LastSessionEndTime = new TimeSpan(17, 0, 0);
        Messages = (Messages ?? new List<string>())
            .Select(m => m?.Trim() ?? "")
            .Where(m => m.Length > 0)
            .Select(m => RewordedDefaults.TryGetValue(m, out var reworded) ? reworded : m)
            .ToList();
        if (Messages.Count == 0) Messages = DefaultMessages();
        if (HotkeyKey == Key.None) HotkeyKey = Key.G;
        if (HotkeyModifiers == ModifierKeys.None) HotkeyModifiers = ModifierKeys.Control | ModifierKeys.Alt;
    }
}
