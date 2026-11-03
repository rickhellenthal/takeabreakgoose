using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TakeABreakGoose;

/// <summary>Registers a system-wide shortcut with RegisterHotKey, on a hidden window.</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsHotkey : IDisposable
{
    private const uint WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0x600D;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly HiddenWindow _window = new();
    private bool _registered;

    public event Action? Pressed;

    public WindowsHotkey()
    {
        _window.Message += (message, wParam, _) =>
        {
            if (message == WM_HOTKEY && wParam == HotkeyId) Pressed?.Invoke();
        };
    }

    /// <param name="key">The key's name in Avalonia's (and WPF's) Key enum, as the settings keep it.</param>
    public bool Register(HotkeyModifiers modifiers, string key)
    {
        Unregister();
        // A key name that isn't one (a hand-edited settings file) can't be registered.
        if (VirtualKey(key) is not { } vk) return false;
        // HotkeyModifiers values match the Win32 MOD_* flags.
        _registered = RegisterHotKey(_window.Handle, HotkeyId, (uint)modifiers | MOD_NOREPEAT, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_window.Handle, HotkeyId);
        _registered = false;
    }

    public void Dispose()
    {
        Unregister();
        _window.Dispose();
    }

    /// <summary>Windows' virtual key code for a key name from the settings, or null for one it doesn't know.</summary>
    internal static uint? VirtualKey(string key)
    {
        // Letters and digits are their own character; D0 to D9 are the digits on the main keyboard.
        if (key is [>= 'A' and <= 'Z']) return key[0];
        if (key is ['D', >= '0' and <= '9']) return key[1];
        if (key.StartsWith("NumPad") && key.Length == 7 && char.IsAsciiDigit(key[6])) return 0x60u + (uint)(key[6] - '0');
        if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out int f) && f is >= 1 and <= 24) return 0x6Fu + (uint)f;
        return OtherKeys.TryGetValue(key, out uint vk) ? vk : null;
    }

    // By the names in Avalonia's Key enum, which are WPF's; some keys have two.
    private static readonly Dictionary<string, uint> OtherKeys = new()
    {
        ["Back"] = 0x08, ["Tab"] = 0x09, ["Enter"] = 0x0D, ["Return"] = 0x0D, ["Pause"] = 0x13, ["Escape"] = 0x1B,
        ["Space"] = 0x20, ["PageUp"] = 0x21, ["Prior"] = 0x21, ["PageDown"] = 0x22, ["Next"] = 0x22,
        ["End"] = 0x23, ["Home"] = 0x24, ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["PrintScreen"] = 0x2C, ["Snapshot"] = 0x2C, ["Insert"] = 0x2D, ["Delete"] = 0x2E,
        ["Multiply"] = 0x6A, ["Add"] = 0x6B, ["Subtract"] = 0x6D, ["Decimal"] = 0x6E, ["Divide"] = 0x6F,
        ["Scroll"] = 0x91,
        ["OemSemicolon"] = 0xBA, ["Oem1"] = 0xBA, ["OemPlus"] = 0xBB, ["OemComma"] = 0xBC, ["OemMinus"] = 0xBD,
        ["OemPeriod"] = 0xBE, ["OemQuestion"] = 0xBF, ["Oem2"] = 0xBF, ["OemTilde"] = 0xC0, ["Oem3"] = 0xC0,
        ["OemOpenBrackets"] = 0xDB, ["Oem4"] = 0xDB, ["OemPipe"] = 0xDC, ["Oem5"] = 0xDC,
        ["OemCloseBrackets"] = 0xDD, ["Oem6"] = 0xDD, ["OemQuotes"] = 0xDE, ["Oem7"] = 0xDE,
        ["OemBackslash"] = 0xE2, ["Oem102"] = 0xE2,
    };

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hwnd, int id);
}
