using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace TakeABreakGoose;

/// <summary>
/// What Avalonia can't do itself for the goose's window on macOS: float over everything (full-screen
/// apps too) on every Space, and let clicks through where there's no goose. Elsewhere (trying the app
/// out on Windows) it does nothing.
/// </summary>
internal static class OverlayWindow
{
    // NSStatusWindowLevel: above normal and floating windows, like the menu bar's own menus.
    private const nint StatusWindowLevel = 25;

    // NSWindowCollectionBehavior: on every Space, staying put when switching Spaces, left out of
    // Cmd+` window cycling, and allowed next to a full-screen app.
    private const nuint CanJoinAllSpaces = 1 << 0;
    private const nuint Stationary = 1 << 4;
    private const nuint IgnoresCycle = 1 << 6;
    private const nuint FullScreenAuxiliary = 1 << 8;

    /// <summary>Call once the window is open (it has no NSWindow before that).</summary>
    public static void Configure(Window window)
    {
        if (NativeWindow(window) is not { } nsWindow) return;
        ObjC.Send(nsWindow, ObjC.Selector("setLevel:"), StatusWindowLevel);
        ObjC.Send(nsWindow, ObjC.Selector("setCollectionBehavior:"), CanJoinAllSpaces | Stationary | IgnoresCycle | FullScreenAuxiliary);
        // An app without a Dock icon still deactivates; the goose should stay when it does.
        ObjC.Send(nsWindow, ObjC.Selector("setHidesOnDeactivate:"), false);
    }

    /// <summary>
    /// Whether clicks go through the window to whatever is under it. macOS hit-tests the whole window,
    /// transparent or not, so the goose's window turns this on whenever the cursor isn't over the goose.
    /// </summary>
    public static void SetClickThrough(Window window, bool clickThrough)
    {
        if (NativeWindow(window) is not { } nsWindow) return;
        ObjC.Send(nsWindow, ObjC.Selector("setIgnoresMouseEvents:"), clickThrough);
    }

    /// <summary>
    /// Where the cursor is, in the window's own coordinates (from its top left), or null when that
    /// can't be told. Works while clicks go through the window, unlike pointer events.
    /// </summary>
    public static Point? CursorInWindow(Window window)
    {
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
}
