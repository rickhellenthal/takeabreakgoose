using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TakeABreakGoose;

/// <summary>
/// The modifier keys of the global shortcut. The names and values are those of WPF's ModifierKeys
/// (and of the Win32 MOD_* flags), so settings files stay the same; on macOS, Windows means Command.
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

public sealed class GooseSettings
{
    /// <summary>The language the app speaks, by its code in <see cref="Languages"/>: "en" or "nl".</summary>
    public string Language { get; set; } = Languages.English;

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

    /// <summary>Turn Do not disturb on during a focus session while the user is in a call (see Calls in the app).</summary>
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

    public HotkeyModifiers HotkeyModifiers { get; set; } = HotkeyModifiers.Control | HotkeyModifiers.Alt;

    /// <summary>
    /// The shortcut's key, by its name in WPF's Key enum ("G", "D5", "F1"), which Avalonia's Key
    /// shares, so each app can turn it into its own key.
    /// </summary>
    public string HotkeyKey { get; set; } = DefaultHotkeyKey;

    private const string DefaultHotkeyKey = "G";

    /// <summary>
    /// What the goose says when it's time for a break, picking one at random. The ones that are still
    /// one of its default messages are in the app's language (see <see cref="TranslateDefaultMessages"/>).
    /// </summary>
    public List<string> Messages { get; set; } = DefaultMessages();

    /// <summary>The goose's own messages, in a language (English if it's not given).</summary>
    public static List<string> DefaultMessages(string language = Languages.English) =>
        Languages.Lines(Languages.Text(nameof(Strings.DefaultMessages), language)).ToList();

    /// <summary>
    /// Puts the messages that are still one of the goose's default messages, in any language, in this
    /// settings' language. Messages the user wrote or changed themselves are left as they are.
    /// </summary>
    public void TranslateDefaultMessages()
    {
        var translations = new Dictionary<string, string>();
        var own = DefaultMessages(Language);
        foreach (var language in Languages.All)
        {
            var defaults = DefaultMessages(language.Code);
            for (int i = 0; i < defaults.Count && i < own.Count; i++) translations.TryAdd(defaults[i], own[i]);
        }
        Messages = Messages.Select(m => translations.TryGetValue(m, out var translated) ? translated : m).ToList();
    }

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

    public static string FormatHotkey(HotkeyModifiers modifiers, string key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        // The number keys are called D0 to D9.
        parts.Add(key is ['D', >= '0' and <= '9'] ? key[1..] : key);
        return string.Join("+", parts);
    }

    /// <summary>Clamps values loaded from disk so a hand-edited file can't break the app.</summary>
    public void Sanitize()
    {
        if (!Languages.IsKnown(Language)) Language = Languages.English;
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
        if (Messages.Count == 0) Messages = DefaultMessages(Language);
        TranslateDefaultMessages();
        if (string.IsNullOrWhiteSpace(HotkeyKey) || HotkeyKey == "None") HotkeyKey = DefaultHotkeyKey;
        if (HotkeyModifiers == HotkeyModifiers.None) HotkeyModifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
    }
}
