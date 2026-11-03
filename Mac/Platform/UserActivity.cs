using System;
using System.Runtime.InteropServices;

namespace TakeABreakGoose;

/// <summary>Tells how long ago the user last used the mouse or keyboard.</summary>
public sealed partial class UserActivity : IUserActivity
{
    public static readonly UserActivity Instance = new();

    // Any kind of input, in this login session.
    private const int CombinedSessionState = 0;
    private const uint AnyInputEvent = ~0u;

    public TimeSpan IdleTime()
    {
        if (OperatingSystem.IsMacOS())
            return TimeSpan.FromSeconds(CGEventSourceSecondsSinceLastEventType(CombinedSessionState, AnyInputEvent));
        if (OperatingSystem.IsWindows())
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
        }
        // Elsewhere the user counts as always there.
        return TimeSpan.Zero;
    }

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial double CGEventSourceSecondsSinceLastEventType(int sourceState, uint eventType);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo info);
}
