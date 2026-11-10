using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TakeABreakGoose;

/// <summary>
/// The bits of X11 the Linux side needs that Avalonia doesn't offer: the time since the last input
/// (the XScreenSaver extension), where the cursor is, and letting clicks through a window (the
/// Shape extension). Avalonia draws through X11 on Linux, also in a Wayland session (via XWayland).
/// </summary>
/// <remarks>
/// It talks to the X server over its own connection, apart from Avalonia's, and only from one
/// thread at a time (<see cref="Lock"/>). The global shortcut has a connection of its own (LinuxHotkey).
/// </remarks>
[SupportedOSPlatform("linux")]
internal static unsafe partial class X11
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXext = "libXext.so.6";
    private const string LibXss = "libXss.so.1";

    public const int KeyPress = 2;
    public const uint ShiftMask = 1, LockMask = 2, ControlMask = 4, Mod1Mask = 8, Mod2Mask = 16, Mod4Mask = 64;
    public const int GrabModeAsync = 1;

    // The Shape extension: the input shape, replaced as a whole.
    private const int ShapeInput = 2;
    private const int ShapeSet = 0;
    private const int Unsorted = 0;

    public static readonly object Lock = new();
    private static IntPtr _display;
    private static bool _opened;
    private static bool? _hasScreenSaver;

    /// <summary>The shared connection, or zero when there's no X server to talk to.</summary>
    private static IntPtr Display
    {
        get
        {
            if (_opened) return _display;
            _opened = true;
            try
            {
                _display = XOpenDisplay(IntPtr.Zero);
            }
            catch (Exception)
            {
                _display = IntPtr.Zero; // libX11 missing
            }
            return _display;
        }
    }

    /// <summary>Time since the last mouse or keyboard input anywhere on the screen, or null when the X server can't tell.</summary>
    public static TimeSpan? IdleTime()
    {
        lock (Lock)
        {
            if (Display == IntPtr.Zero) return null;
            try
            {
                _hasScreenSaver ??= XScreenSaverQueryExtension(_display, out _, out _) != 0;
                if (_hasScreenSaver != true) return null;
                var info = new XScreenSaverInfo();
                if (XScreenSaverQueryInfo(_display, XDefaultRootWindow(_display), ref info) == 0) return null;
                return TimeSpan.FromMilliseconds(info.Idle);
            }
            catch (Exception)
            {
                _hasScreenSaver = false; // libXss missing
                return null;
            }
        }
    }

    /// <summary>The cursor's position on the screen, in pixels.</summary>
    public static (int X, int Y)? CursorPosition()
    {
        lock (Lock)
        {
            if (Display == IntPtr.Zero) return null;
            return XQueryPointer(_display, XDefaultRootWindow(_display), out _, out _, out int x, out int y, out _, out _, out _) != 0
                ? (x, y)
                : null;
        }
    }

    /// <summary>Lets clicks through the window (an empty input shape), or takes them again (the whole window).</summary>
    public static void SetClickThrough(IntPtr window, bool clickThrough)
    {
        lock (Lock)
        {
            if (Display == IntPtr.Zero) return;
            try
            {
                if (clickThrough) XShapeCombineRectangles(_display, window, ShapeInput, 0, 0, IntPtr.Zero, 0, ShapeSet, Unsorted);
                else XShapeCombineMask(_display, window, ShapeInput, 0, 0, IntPtr.Zero, ShapeSet);
                XFlush(_display);
            }
            catch (Exception)
            {
                // libXext missing: the window keeps taking clicks.
            }
        }
    }

    /// <summary>
    /// Makes the window one the window manager leaves alone (override-redirect, like menus and
    /// tooltips): it isn't kept inside the screen, so the goose can walk in from past its edge, and it
    /// never gets the focus. Only works before the window is first shown.
    /// </summary>
    public static void SetOverrideRedirect(IntPtr window)
    {
        lock (Lock)
        {
            if (Display == IntPtr.Zero) return;
            var attributes = new XSetWindowAttributes { OverrideRedirect = 1 };
            XChangeWindowAttributes(_display, window, CWOverrideRedirect, ref attributes);
            XFlush(_display);
        }
    }

    /// <summary>Puts the window on top of the others: the window manager doesn't keep an override-redirect window there.</summary>
    public static void Raise(IntPtr window)
    {
        lock (Lock)
        {
            if (Display == IntPtr.Zero) return;
            XRaiseWindow(_display, window);
            XFlush(_display);
        }
    }

    private const nuint CWOverrideRedirect = 1 << 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct XSetWindowAttributes
    {
        public IntPtr BackgroundPixmap;
        public nuint BackgroundPixel;
        public IntPtr BorderPixmap;
        public nuint BorderPixel;
        public int BitGravity;
        public int WinGravity;
        public int BackingStore;
        public nuint BackingPlanes;
        public nuint BackingPixel;
        public int SaveUnder;
        public nint EventMask;
        public nint DoNotPropagateMask;
        public int OverrideRedirect;
        public IntPtr Colormap;
        public IntPtr Cursor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XScreenSaverInfo
    {
        public IntPtr Window;
        public int State;
        public int Kind;
        public nuint TilOrSince;
        public nuint Idle;
        public nuint EventMask;
    }

    /// <summary>An XEvent: a union of 24 longs, of which only the type is read here.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 192)]
    public struct XEvent
    {
        public int Type;
    }

    [LibraryImport(LibX11)]
    public static partial IntPtr XOpenDisplay(IntPtr name);

    [LibraryImport(LibX11)]
    public static partial int XCloseDisplay(IntPtr display);

    [LibraryImport(LibX11)]
    public static partial IntPtr XDefaultRootWindow(IntPtr display);

    [LibraryImport(LibX11)]
    public static partial int XFlush(IntPtr display);

    [LibraryImport(LibX11)]
    public static partial int XSync(IntPtr display, int discard);

    [LibraryImport(LibX11)]
    public static partial int XPending(IntPtr display);

    [LibraryImport(LibX11)]
    public static partial int XNextEvent(IntPtr display, out XEvent ev);

    [LibraryImport(LibX11, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr XStringToKeysym(string name);

    [LibraryImport(LibX11)]
    public static partial byte XKeysymToKeycode(IntPtr display, IntPtr keysym);

    [LibraryImport(LibX11)]
    public static partial int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr window, int ownerEvents,
        int pointerMode, int keyboardMode);

    [LibraryImport(LibX11)]
    public static partial int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr window);

    [LibraryImport(LibX11)]
    public static partial IntPtr XSetErrorHandler(IntPtr handler);

    [LibraryImport(LibX11)]
    private static partial int XChangeWindowAttributes(IntPtr display, IntPtr window, nuint valueMask,
        ref XSetWindowAttributes attributes);

    [LibraryImport(LibX11)]
    private static partial int XRaiseWindow(IntPtr display, IntPtr window);

    [LibraryImport(LibX11)]
    private static partial int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child,
        out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);

    [LibraryImport(LibXss)]
    private static partial int XScreenSaverQueryExtension(IntPtr display, out int eventBase, out int errorBase);

    [LibraryImport(LibXss)]
    private static partial int XScreenSaverQueryInfo(IntPtr display, IntPtr drawable, ref XScreenSaverInfo info);

    [LibraryImport(LibXext)]
    private static partial void XShapeCombineRectangles(IntPtr display, IntPtr window, int kind, int x, int y,
        IntPtr rectangles, int count, int operation, int ordering);

    [LibraryImport(LibXext)]
    private static partial void XShapeCombineMask(IntPtr display, IntPtr window, int kind, int x, int y, IntPtr mask,
        int operation);
}
