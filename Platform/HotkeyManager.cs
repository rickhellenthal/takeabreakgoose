using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace TakeABreakGoose;

/// <summary>Registers a system-wide hotkey using a hidden message-only window.</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0x600D;
    private const uint MOD_NOREPEAT = 0x4000;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly HwndSource _source;
    private bool _registered;

    public event Action? Pressed;

    public HotkeyManager()
    {
        var parameters = new HwndSourceParameters("TakeABreakGooseHotkey")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <param name="key">The key's name in WPF's Key enum, as the settings keep it.</param>
    public bool Register(HotkeyModifiers modifiers, string key)
    {
        Unregister();
        // A key name that isn't one (a hand-edited settings file) can't be registered.
        if (!Enum.TryParse(key, out Key wpfKey)) return false;
        // HotkeyModifiers values match the Win32 MOD_* flags.
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(wpfKey);
        _registered = RegisterHotKey(_source.Handle, HotkeyId, (uint)modifiers | MOD_NOREPEAT, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
