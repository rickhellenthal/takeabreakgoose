using System;
using System.Runtime.InteropServices;

namespace TakeABreakGoose;

/// <summary>
/// Calling macOS's Objective-C APIs (AppKit, ServiceManagement) from C#: look up a class and a
/// selector, then send the message. objc_msgSend has no fixed signature, so each kind of call gets
/// its own import.
/// </summary>
internal static partial class ObjC
{
    private const string Library = "/usr/lib/libobjc.A.dylib";

    public static IntPtr Class(string name) => objc_getClass(name);

    public static IntPtr Selector(string name) => sel_registerName(name);

    /// <summary>Loads a system framework, so its classes can be looked up.</summary>
    public static void LoadFramework(string name) =>
        NativeLibrary.Load($"/System/Library/Frameworks/{name}.framework/{name}");

    /// <summary>An NSString's text.</summary>
    public static string? Text(IntPtr nsString) =>
        nsString == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(nsString, Selector("UTF8String")));

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    public static partial IntPtr Send(IntPtr receiver, IntPtr selector);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    public static partial nint SendNInt(IntPtr receiver, IntPtr selector);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    public static partial void Send(IntPtr receiver, IntPtr selector, nint value);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    public static partial void Send(IntPtr receiver, IntPtr selector, nuint value);

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    public static partial void Send(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.U1)] bool value);

    /// <summary>For methods like registerAndReturnError: that report failure with an NSError.</summary>
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SendWithError(IntPtr receiver, IntPtr selector, out IntPtr error);

    [StructLayout(LayoutKind.Sequential)]
    public struct NSPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NSRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    // NSPoint comes back in registers on both Apple Silicon and Intel.
    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    public static partial NSPoint SendPoint(IntPtr receiver, IntPtr selector);

    /// <summary>NSRect is too big for registers on Intel, which uses objc_msgSend_stret for it instead.</summary>
    public static NSRect SendRect(IntPtr receiver, IntPtr selector)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64) return SendRectArm64(receiver, selector);
        SendRectStret(out var rect, receiver, selector);
        return rect;
    }

    [LibraryImport(Library, EntryPoint = "objc_msgSend")]
    private static partial NSRect SendRectArm64(IntPtr receiver, IntPtr selector);

    [LibraryImport(Library, EntryPoint = "objc_msgSend_stret")]
    private static partial void SendRectStret(out NSRect result, IntPtr receiver, IntPtr selector);
}
