using System;
using System.Text;

namespace TakeABreakGoose;

/// <summary>How the app shows the global shortcut: "Ctrl+Alt+G" on Windows, "⌃⌥G" on a Mac.</summary>
internal static class HotkeyText
{
    public static string Format(HotkeyModifiers modifiers, string key) =>
        OperatingSystem.IsMacOS() ? Mac(modifiers, key) : GooseSettings.FormatHotkey(modifiers, key);

    /// <summary>
    /// The shortcut the way macOS menus write it, e.g. "⌃⌥G" or "⌘⇧5": the modifier symbols in
    /// Apple's order (Control, Option, Shift, Command), then the key. Windows in the settings means Command.
    /// </summary>
    public static string Mac(HotkeyModifiers modifiers, string key)
    {
        var text = new StringBuilder();
        if (modifiers.HasFlag(HotkeyModifiers.Control)) text.Append('⌃');
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) text.Append('⌥');
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) text.Append('⇧');
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) text.Append('⌘');
        // The number keys are called D0 to D9.
        text.Append(key is ['D', >= '0' and <= '9'] ? key[1..] : key);
        return text.ToString();
    }
}
