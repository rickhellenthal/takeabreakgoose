using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace TakeABreakGoose;

/// <summary>
/// Registers a system-wide shortcut with Carbon's RegisterEventHotKey, which (unlike watching all
/// key presses) needs no Accessibility permission. Since macOS 15, a shortcut also needs Control or
/// Command; with only Option (and Shift) it can't be registered.
/// </summary>
internal sealed unsafe partial class MacHotkey : IDisposable
{
    // Carbon's modifier flags.
    private const uint CommandKey = 1 << 8;
    private const uint ShiftKey = 1 << 9;
    private const uint OptionKey = 1 << 11;
    private const uint ControlKey = 1 << 12;

    private const uint KeyboardEvents = 0x6B657962; // 'keyb'
    private const uint HotKeyPressed = 5;
    private const uint Signature = 0x476F6F73; // 'Goos', tells our hotkey apart from others

    // Only one instance exists; the callback from Carbon finds it here.
    private static MacHotkey? _current;

    private IntPtr _handler;
    private IntPtr _hotKey;

    public event Action? Pressed;

    public MacHotkey()
    {
        _current = this;
        if (!OperatingSystem.IsMacOS()) return;
        var pressed = new EventTypeSpec { EventClass = KeyboardEvents, EventKind = HotKeyPressed };
        InstallEventHandler(GetApplicationEventTarget(), &OnHotKey, 1, &pressed, IntPtr.Zero, out _handler);
    }

    /// <param name="key">The key's name in Avalonia's (and WPF's) Key enum, as the settings keep it.</param>
    public bool Register(HotkeyModifiers modifiers, string key)
    {
        Unregister();
        if (!OperatingSystem.IsMacOS()) return true; // nothing to register when trying the app out on Windows
        // A key name that isn't one (a hand-edited settings file) can't be registered.
        if (!KeyCodes.TryGetValue(key, out uint keyCode)) return false;

        uint carbonModifiers = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) carbonModifiers |= ControlKey;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) carbonModifiers |= OptionKey;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) carbonModifiers |= ShiftKey;
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) carbonModifiers |= CommandKey;

        var id = new EventHotKeyID { Signature = Signature, Id = 1 };
        return RegisterEventHotKey(keyCode, carbonModifiers, id, GetApplicationEventTarget(), 0, out _hotKey) == 0;
    }

    public void Unregister()
    {
        if (_hotKey == IntPtr.Zero) return;
        UnregisterEventHotKey(_hotKey);
        _hotKey = IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        if (_handler != IntPtr.Zero) RemoveEventHandler(_handler);
        _handler = IntPtr.Zero;
        if (_current == this) _current = null;
    }

    [UnmanagedCallersOnly]
    private static int OnHotKey(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
        // Carbon calls this on the main thread, but in the middle of its event handling: let it finish first.
        Dispatcher.UIThread.Post(() => _current?.Pressed?.Invoke());
        return 0;
    }

    /// <summary>
    /// macOS's key codes, by the key names the settings use. These are positions on the keyboard
    /// (an ANSI layout), the way RegisterEventHotKey wants them.
    /// </summary>
    private static readonly Dictionary<string, uint> KeyCodes = new()
    {
        ["A"] = 0x00, ["S"] = 0x01, ["D"] = 0x02, ["F"] = 0x03, ["H"] = 0x04, ["G"] = 0x05, ["Z"] = 0x06,
        ["X"] = 0x07, ["C"] = 0x08, ["V"] = 0x09, ["B"] = 0x0B, ["Q"] = 0x0C, ["W"] = 0x0D, ["E"] = 0x0E,
        ["R"] = 0x0F, ["Y"] = 0x10, ["T"] = 0x11, ["O"] = 0x1F, ["U"] = 0x20, ["I"] = 0x22, ["P"] = 0x23,
        ["L"] = 0x25, ["J"] = 0x26, ["K"] = 0x28, ["N"] = 0x2D, ["M"] = 0x2E,
        ["D1"] = 0x12, ["D2"] = 0x13, ["D3"] = 0x14, ["D4"] = 0x15, ["D6"] = 0x16, ["D5"] = 0x17,
        ["D9"] = 0x19, ["D7"] = 0x1A, ["D8"] = 0x1C, ["D0"] = 0x1D,
        ["OemPlus"] = 0x18, ["OemMinus"] = 0x1B,
        ["OemCloseBrackets"] = 0x1E, ["Oem6"] = 0x1E, ["OemOpenBrackets"] = 0x21, ["Oem4"] = 0x21,
        ["OemQuotes"] = 0x27, ["Oem7"] = 0x27, ["OemSemicolon"] = 0x29, ["Oem1"] = 0x29,
        ["OemPipe"] = 0x2A, ["Oem5"] = 0x2A, ["OemBackslash"] = 0x2A, ["OemComma"] = 0x2B,
        ["OemQuestion"] = 0x2C, ["Oem2"] = 0x2C, ["OemPeriod"] = 0x2F, ["OemTilde"] = 0x32, ["Oem3"] = 0x32,
        ["Enter"] = 0x24, ["Return"] = 0x24, ["Tab"] = 0x30, ["Space"] = 0x31, ["Back"] = 0x33,
        ["Escape"] = 0x35, ["Delete"] = 0x75, ["Home"] = 0x73, ["End"] = 0x77,
        ["PageUp"] = 0x74, ["Prior"] = 0x74, ["PageDown"] = 0x79, ["Next"] = 0x79,
        ["Left"] = 0x7B, ["Right"] = 0x7C, ["Down"] = 0x7D, ["Up"] = 0x7E,
        ["F1"] = 0x7A, ["F2"] = 0x78, ["F3"] = 0x63, ["F4"] = 0x76, ["F5"] = 0x60, ["F6"] = 0x61,
        ["F7"] = 0x62, ["F8"] = 0x64, ["F9"] = 0x65, ["F10"] = 0x6D, ["F11"] = 0x67, ["F12"] = 0x6F,
        ["F13"] = 0x69, ["F14"] = 0x6B, ["F15"] = 0x71, ["F16"] = 0x6A, ["F17"] = 0x40, ["F18"] = 0x4F,
        ["F19"] = 0x50, ["F20"] = 0x5A,
        ["NumPad0"] = 0x52, ["NumPad1"] = 0x53, ["NumPad2"] = 0x54, ["NumPad3"] = 0x55, ["NumPad4"] = 0x56,
        ["NumPad5"] = 0x57, ["NumPad6"] = 0x58, ["NumPad7"] = 0x59, ["NumPad8"] = 0x5B, ["NumPad9"] = 0x5C,
        ["Multiply"] = 0x43, ["Add"] = 0x45, ["Subtract"] = 0x4E, ["Decimal"] = 0x41, ["Divide"] = 0x4B,
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyID
    {
        public uint Signature;
        public uint Id;
    }

    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    [LibraryImport(Carbon)]
    private static partial IntPtr GetApplicationEventTarget();

    [LibraryImport(Carbon)]
    private static partial int InstallEventHandler(IntPtr target, delegate* unmanaged<IntPtr, IntPtr, IntPtr, int> handler,
        uint numTypes, EventTypeSpec* list, IntPtr userData, out IntPtr handlerRef);

    [LibraryImport(Carbon)]
    private static partial int RemoveEventHandler(IntPtr handlerRef);

    [LibraryImport(Carbon)]
    private static partial int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyID id, IntPtr target,
        uint options, out IntPtr hotKeyRef);

    [LibraryImport(Carbon)]
    private static partial int UnregisterEventHotKey(IntPtr hotKeyRef);
}
