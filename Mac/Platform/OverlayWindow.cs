using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace TakeABreakGoose;

/// <summary>
/// What Avalonia can't do itself for the goose's window on macOS: float over everything (full-screen
/// apps too) on every Space, and let clicks through where there's no goose. Elsewhere (trying the app
/// out on Windows) it does nothing.
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

    /// <summary>Call once the window is open (it has no NSWindow before that).</summary>
    public static void Configure(Window window)
    {
        if (NativeWindow(window) is not { } nsWindow) return;
        SendNInt(nsWindow, Selector("setLevel:"), StatusWindowLevel);
        SendNUInt(nsWindow, Selector("setCollectionBehavior:"), CanJoinAllSpaces | Stationary | IgnoresCycle | FullScreenAuxiliary);
        // An app without a Dock icon still deactivates; the goose should stay when it does.
        SendBool(nsWindow, Selector("setHidesOnDeactivate:"), false);
    }

    /// <summary>
    /// Whether clicks go through the window to whatever is under it. macOS hit-tests the whole window,
    /// transparent or not, so the goose's window turns this on whenever the cursor isn't over the goose.
    /// </summary>
    public static void SetClickThrough(Window window, bool clickThrough)
    {
        if (NativeWindow(window) is not { } nsWindow) return;
        SendBool(nsWindow, Selector("setIgnoresMouseEvents:"), clickThrough);
    }

    /// <summary>
    /// Where the cursor is, in the window's own coordinates (from its top left), or null when that
    /// can't be told. Works while clicks go through the window, unlike pointer events.
    /// </summary>
    public static Point? CursorInWindow(Window window)
    {
        if (NativeWindow(window) is not { } nsWindow) return null;
        // Both in points, from the bottom left of the main screen.
        var mouse = SendPoint(Class("NSEvent"), Selector("mouseLocation"));
        var frame = SendRect(nsWindow, Selector("frame"));
        return new Point(mouse.X - frame.X, frame.Y + frame.Height - mouse.Y);
    }

    private static IntPtr? NativeWindow(Window window) =>
        OperatingSystem.IsMacOS() && window.TryGetPlatformHandle() is IMacOSTopLevelPlatformHandle handle
            && handle.NSWindow != IntPtr.Zero
            ? handle.NSWindow
            : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct NSPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NSRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    private static IntPtr Selector(string name) => sel_registerName(name);

    private static IntPtr Class(string name) => objc_getClass(name);

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    // objc_msgSend has no fixed signature: each kind of call gets its own import. NSPoint comes back in
    // registers on both Apple Silicon and Intel; NSRect is too big for that on Intel, which uses
    // objc_msgSend_stret instead.
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendNInt(IntPtr receiver, IntPtr selector, nint value);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendNUInt(IntPtr receiver, IntPtr selector, nuint value);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.U1)] bool value);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial NSPoint SendPoint(IntPtr receiver, IntPtr selector);

    private static NSRect SendRect(IntPtr receiver, IntPtr selector)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64) return SendRectArm64(receiver, selector);
        SendRectStret(out var rect, receiver, selector);
        return rect;
    }

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial NSRect SendRectArm64(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend_stret")]
    private static partial void SendRectStret(out NSRect result, IntPtr receiver, IntPtr selector);
}
