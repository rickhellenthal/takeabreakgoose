using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace TakeABreakGoose;

/// <summary>
/// What Avalonia can't do itself for the goose's window: never take the focus from what the user is
/// doing, stay out of Alt+Tab and Cmd+`, float over everything (on macOS: full-screen apps too, on
/// every Space), and let clicks through where there's no goose.
/// </summary>
internal static partial class OverlayWindow
{
    // NSStatusWindowLevel: above normal and floating windows, like the menu bar's own menus.
    private const nint StatusWindowLevel = 25;

    // NSWindowCollectionBehavior: on every Space, staying put when switching Spaces, left out of
    // Cmd+` window cycling, and allowed next to a full-screen app.
    private const nuint CanJoinAllSpaces = 1 << 0;
    private const nuint Stationary = 1 << 4;
    private const nuint IgnoresCycle = 1 << 6;
    private const nuint FullScreenAuxiliary = 1 << 8;

    // Win32's extended window styles.
    private const int ExStyle = -20;
    private const uint Transparent = 0x00000020;
    private const uint ToolWindow = 0x00000080;
    private const uint Layered = 0x00080000;
    private const uint NoActivate = 0x08000000;

    private const uint MouseActivate = 0x0021; // WM_MOUSEACTIVATE
    private const nint NoActivateOnClick = 3;  // MA_NOACTIVATE

    /// <summary>
    /// Call in the window's constructor, before it's shown. On Windows, a game in exclusive full-screen
    /// drops out of it (back to the desktop) as soon as an ordinary window appears on top of it, even
    /// for a moment. A layered window doesn't count, so the window is layered from the very start, and
    /// stays layered whenever Avalonia works out the window's styles again. It's also a no-activate
    /// tool window, so it never takes the focus, not even when clicked.
    /// </summary>
    public static void Prepare(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        const uint overlay = NoActivate | ToolWindow | Layered;
        Win32Properties.AddWindowStylesCallback(window, (style, exStyle) =>
        {
            // Keep the click-through of SetClickThrough.
            uint clickThrough = Hwnd(window) is { } hwnd ? (uint)GetWindowLong(hwnd, ExStyle) & Transparent : 0;
            return (style, exStyle | overlay | clickThrough);
        });
        // The native window already exists (hidden): give it the styles before it's ever shown.
        if (Hwnd(window) is { } hwnd)
            SetWindowLong(hwnd, ExStyle, (int)((uint)GetWindowLong(hwnd, ExStyle) | overlay));
        Win32Properties.AddWndProcHookCallback(window, (IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message != MouseActivate) return IntPtr.Zero;
            handled = true;
            return NoActivateOnClick;
        });
    }

    /// <summary>Call once the window is open (it has no native window before that).</summary>
    public static void Configure(Window window)
    {
        if (Hwnd(window) is { } hwnd)
        {
            // Layered, so that WS_EX_TRANSPARENT (SetClickThrough) lets clicks through to other apps.
            SetWindowLong(hwnd, ExStyle, (int)((uint)GetWindowLong(hwnd, ExStyle) | NoActivate | ToolWindow | Layered));
            return;
        }
        if (NativeWindow(window) is not { } nsWindow) return;
        ObjC.Send(nsWindow, ObjC.Selector("setLevel:"), StatusWindowLevel);
        ObjC.Send(nsWindow, ObjC.Selector("setCollectionBehavior:"), CanJoinAllSpaces | Stationary | IgnoresCycle | FullScreenAuxiliary);
        // An app without a Dock icon still deactivates; the goose should stay when it does.
        ObjC.Send(nsWindow, ObjC.Selector("setHidesOnDeactivate:"), false);
    }

    /// <summary>
    /// Whether clicks go through the window to whatever is under it. Windows and macOS hit-test the whole
    /// window, transparent or not, so the goose's window turns this on whenever the cursor isn't over the goose.
    /// </summary>
    public static void SetClickThrough(Window window, bool clickThrough)
    {
        if (Hwnd(window) is { } hwnd)
        {
            int style = GetWindowLong(hwnd, ExStyle);
            SetWindowLong(hwnd, ExStyle, (int)(clickThrough ? (uint)style | Transparent : (uint)style & ~Transparent));
            return;
        }
        if (NativeWindow(window) is not { } nsWindow) return;
        ObjC.Send(nsWindow, ObjC.Selector("setIgnoresMouseEvents:"), clickThrough);
    }

    /// <summary>
    /// Where the cursor is, in the window's own coordinates (from its top left), or null when that
    /// can't be told. Works while clicks go through the window, unlike pointer events.
    /// </summary>
    public static Point? CursorInWindow(Window window)
    {
        if (Hwnd(window) is not null)
            return GetCursorPos(out var cursor) ? window.PointToClient(new PixelPoint(cursor.X, cursor.Y)) : null;
        if (NativeWindow(window) is not { } nsWindow) return null;
        // Both in points, from the bottom left of the main screen.
        var mouse = ObjC.SendPoint(ObjC.Class("NSEvent"), ObjC.Selector("mouseLocation"));
        var frame = ObjC.SendRect(nsWindow, ObjC.Selector("frame"));
        return new Point(mouse.X - frame.X, frame.Y + frame.Height - mouse.Y);
    }

    private static IntPtr? NativeWindow(Window window) =>
        OperatingSystem.IsMacOS() && window.TryGetPlatformHandle() is IMacOSTopLevelPlatformHandle handle
            && handle.NSWindow != IntPtr.Zero
            ? handle.NSWindow
            : null;

    private static IntPtr? Hwnd(Window window) =>
        OperatingSystem.IsWindows() && window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle
            && handle.Handle != IntPtr.Zero
            ? handle.Handle
            : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong(IntPtr hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial int SetWindowLong(IntPtr hwnd, int index, int value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out CursorPoint point);
}
