using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Avalonia.Threading;

namespace TakeABreakGoose;

/// <summary>
/// A system-wide shortcut on Linux: the key grabbed on the X root window (XGrabKey), on a connection
/// of its own that a background thread reads. Works on X11, and on Wayland only while an X11 (XWayland)
/// window has the focus, as Wayland doesn't let apps listen to keys meant for others.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe class LinuxHotkey : IDisposable
{
    // NumLock and CapsLock count as modifiers in X11: grab the shortcut with and without them.
    private static readonly uint[] LockCombinations = { 0, X11.LockMask, X11.Mod2Mask, X11.LockMask | X11.Mod2Mask };

    private static int _grabFailed;
    private static IntPtr _grabDisplay;
    private static IntPtr _previousErrorHandler;

    private readonly IntPtr _display;
    private readonly IntPtr _root;
    private readonly Thread? _thread;
    private volatile bool _stopping;
    private int _keycode;
    private uint _modifiers;
    private long _lastPress;

    public event Action? Pressed;

    public LinuxHotkey()
    {
        try
        {
            _display = X11.XOpenDisplay(IntPtr.Zero);
        }
        catch (Exception)
        {
            _display = IntPtr.Zero;
        }
        if (_display == IntPtr.Zero) return;
        _root = X11.XDefaultRootWindow(_display);
        _thread = new Thread(Listen) { IsBackground = true, Name = "Global shortcut" };
        _thread.Start();
    }

    /// <param name="key">The key's name in Avalonia's (and WPF's) Key enum, as the settings keep it.</param>
    /// <returns>False when another app already has this shortcut, or it can't be one.</returns>
    public bool Register(HotkeyModifiers modifiers, string key)
    {
        if (_display == IntPtr.Zero) return true; // no X server: no shortcut, but nothing to complain about
        lock (this)
        {
            Unregister();
            if (KeySym(key) is not { } keysymName) return false;
            IntPtr keysym = X11.XStringToKeysym(keysymName);
            int keycode = keysym == IntPtr.Zero ? 0 : X11.XKeysymToKeycode(_display, keysym);
            if (keycode == 0) return false;
            uint mask = Mask(modifiers);

            // Another app holding the shortcut shows up as an X error (BadAccess), which goes to the
            // process's error handler: catch it for just this, and hand anything else on to Avalonia's.
            X11.XSync(_display, 0);
            _grabFailed = 0;
            _grabDisplay = _display;
            _previousErrorHandler = X11.XSetErrorHandler((IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int>)&OnError);
            foreach (uint locks in LockCombinations)
                X11.XGrabKey(_display, keycode, mask | locks, _root, 0, X11.GrabModeAsync, X11.GrabModeAsync);
            X11.XSync(_display, 0);
            X11.XSetErrorHandler(_previousErrorHandler);
            _grabDisplay = IntPtr.Zero;

            _keycode = keycode;
            _modifiers = mask;
            if (_grabFailed == 0) return true;
            Unregister();
            return false;
        }
    }

    [UnmanagedCallersOnly]
    private static int OnError(IntPtr display, IntPtr error)
    {
        if (display == _grabDisplay)
        {
            _grabFailed = 1;
            return 0;
        }
        return _previousErrorHandler != IntPtr.Zero
            ? ((delegate* unmanaged<IntPtr, IntPtr, int>)_previousErrorHandler)(display, error)
            : 0;
    }

    private void Unregister()
    {
        if (_keycode == 0) return;
        foreach (uint locks in LockCombinations) X11.XUngrabKey(_display, _keycode, _modifiers | locks, _root);
        X11.XFlush(_display);
        _keycode = 0;
    }

    private void Listen()
    {
        while (!_stopping)
        {
            bool pressed = false;
            lock (this)
            {
                while (!_stopping && X11.XPending(_display) > 0)
                {
                    X11.XNextEvent(_display, out var ev);
                    if (ev.Type == X11.KeyPress) pressed = true;
                }
            }
            // Holding the keys down repeats them: once is enough.
            if (pressed && Environment.TickCount64 - Interlocked.Exchange(ref _lastPress, Environment.TickCount64) > 500)
                Dispatcher.UIThread.Post(() => Pressed?.Invoke());
            Thread.Sleep(50);
        }
    }

    private static uint Mask(HotkeyModifiers modifiers)
    {
        uint mask = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) mask |= X11.ShiftMask;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) mask |= X11.ControlMask;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) mask |= X11.Mod1Mask;
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) mask |= X11.Mod4Mask;
        return mask;
    }

    /// <summary>X11's name for a key in Avalonia's Key enum ("G" → "g", "D5" → "5"), or null for keys that can't be a shortcut.</summary>
    internal static string? KeySym(string key)
    {
        if (key.Length == 1 && char.IsAsciiLetter(key[0])) return key.ToLowerInvariant();
        if (key.Length == 2 && key[0] == 'D' && char.IsAsciiDigit(key[1])) return key[1..];
        if (key.StartsWith("NumPad") && key.Length == 7 && char.IsAsciiDigit(key[6])) return "KP_" + key[6];
        if (key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key[1..], out int f) && f is >= 1 and <= 24) return key;
        return key switch
        {
            "Space" => "space",
            "Home" => "Home",
            "End" => "End",
            "Insert" => "Insert",
            "Delete" => "Delete",
            "PageUp" or "Prior" => "Prior",
            "PageDown" or "Next" => "Next",
            "Up" => "Up",
            "Down" => "Down",
            "Left" => "Left",
            "Right" => "Right",
            "Pause" => "Pause",
            "Scroll" => "Scroll_Lock",
            "Back" => "BackSpace",
            "Return" or "Enter" => "Return",
            "Tab" => "Tab",
            "OemMinus" => "minus",
            "OemPlus" => "equal",
            "OemComma" => "comma",
            "OemPeriod" => "period",
            "OemTilde" => "grave",
            _ => null,
        };
    }

    public void Dispose()
    {
        if (_display == IntPtr.Zero) return;
        _stopping = true;
        _thread?.Join(500);
        lock (this)
        {
            Unregister();
            X11.XCloseDisplay(_display);
        }
    }
}
