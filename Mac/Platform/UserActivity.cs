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

    public TimeSpan IdleTime() =>
        // Elsewhere (trying the app out on Windows) the user counts as always there.
        OperatingSystem.IsMacOS()
            ? TimeSpan.FromSeconds(CGEventSourceSecondsSinceLastEventType(CombinedSessionState, AnyInputEvent))
            : TimeSpan.Zero;

    [LibraryImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static partial double CGEventSourceSecondsSinceLastEventType(int sourceState, uint eventType);
}
