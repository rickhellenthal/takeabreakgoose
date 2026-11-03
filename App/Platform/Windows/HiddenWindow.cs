using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TakeABreakGoose;

/// <summary>
/// A window that's never shown, for Windows to send messages to: the global shortcut's WM_HOTKEY and
/// the tray icon's clicks. Its messages arrive on the thread that made it, through Avalonia's own
/// message loop.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class HiddenWindow : IDisposable
{
    private const string ClassName = "TakeABreakGooseHiddenWindow";
    private const uint PopupStyle = 0x80000000;
    private const uint ToolWindowStyle = 0x00000080;

    private static readonly Dictionary<IntPtr, HiddenWindow> Windows = new();
    private static bool _classRegistered;

    /// <summary>A message for this window: its number, wParam and lParam.</summary>
    public event Action<uint, IntPtr, IntPtr>? Message;

    public IntPtr Handle { get; private set; }

    public HiddenWindow()
    {
        IntPtr instance = GetModuleHandleW(null);
        if (!_classRegistered)
        {
            var windowClass = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                WindowProcedure = (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WindowProcedure,
                Instance = instance,
                // Kept for as long as the app runs, like the class itself.
                ClassName = Marshal.StringToHGlobalUni(ClassName),
            };
            RegisterClassExW(&windowClass);
            _classRegistered = true;
        }
        Handle = CreateWindowExW(ToolWindowStyle, ClassName, "Take a Break Goose", PopupStyle, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        Windows[Handle] = this;
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        Windows.Remove(Handle);
        DestroyWindow(Handle);
        Handle = IntPtr.Zero;
    }

    [UnmanagedCallersOnly]
    private static IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (Windows.TryGetValue(hwnd, out var window))
        {
            try
            {
                window.Message?.Invoke(message, wParam, lParam);
            }
            catch (Exception)
            {
                // An exception can't travel back through Windows; a missed click is better than a crash.
            }
        }
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public IntPtr MenuName;
        public IntPtr ClassName;
        public IntPtr SmallIcon;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandleW(string? moduleName);

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(WindowClass* windowClass);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
