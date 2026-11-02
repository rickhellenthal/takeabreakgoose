using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TakeABreakGoose;

/// <summary>
/// Which apps are using a microphone right now, by bundle ID, from Core Audio's process objects.
/// Those only exist from macOS 14.2; on older versions this finds nothing and only the camera tells
/// that the user is in a call. Like the camera, it says who's listening, not what's heard, so it
/// needs no permission.
/// </summary>
public static unsafe partial class MicrophoneActivity
{
    private const uint SystemObject = 1;
    private const uint ProcessObjectList = 0x70727323; // 'prs#'
    private const uint IsRunningInput = 0x70697269;    // 'piri'
    private const uint BundleId = 0x70626964;          // 'pbid'
    private const uint GlobalScope = 0x676C6F62;       // 'glob'
    private const uint MainElement = 0;

    public static bool IsSupported => OperatingSystem.IsMacOSVersionAtLeast(14, 2);

    public static List<string> Apps()
    {
        var apps = new List<string>();
        if (!IsSupported) return apps;
        try
        {
            var list = new PropertyAddress { Selector = ProcessObjectList, Scope = GlobalScope, Element = MainElement };
            if (AudioObjectGetPropertyDataSize(SystemObject, &list, 0, null, out uint size) != 0 || size == 0) return apps;
            var processes = new uint[size / sizeof(uint)];
            fixed (uint* processesPointer = processes)
            {
                if (AudioObjectGetPropertyData(SystemObject, &list, 0, null, ref size, processesPointer) != 0) return apps;
            }

            var input = new PropertyAddress { Selector = IsRunningInput, Scope = GlobalScope, Element = MainElement };
            var bundle = new PropertyAddress { Selector = BundleId, Scope = GlobalScope, Element = MainElement };
            foreach (uint process in processes)
            {
                uint running = 0;
                uint runningSize = sizeof(uint);
                if (AudioObjectGetPropertyData(process, &input, 0, null, ref runningSize, &running) != 0 || running == 0) continue;

                IntPtr name = IntPtr.Zero;
                uint nameSize = (uint)IntPtr.Size;
                if (AudioObjectGetPropertyData(process, &bundle, 0, null, ref nameSize, &name) != 0 || name == IntPtr.Zero) continue;
                // The bundle ID is a CFString the caller owns.
                if (CFStringText(name) is { Length: > 0 } id) apps.Add(id);
                CFRelease(name);
            }
        }
        catch (Exception)
        {
            // No way to tell: the goose comes as usual.
        }
        return apps;
    }

    private static string? CFStringText(IntPtr text)
    {
        nint length = CFStringGetLength(text);
        if (length <= 0) return null;
        var characters = new char[length];
        fixed (char* charactersPointer = characters)
        {
            CFStringGetCharacters(text, new CFRange { Location = 0, Length = length }, charactersPointer);
        }
        return new string(characters);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyAddress
    {
        public uint Selector;
        public uint Scope;
        public uint Element;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CFRange
    {
        public nint Location;
        public nint Length;
    }

    private const string CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [LibraryImport(CoreAudio)]
    private static partial int AudioObjectGetPropertyDataSize(uint objectId, PropertyAddress* address,
        uint qualifierSize, void* qualifier, out uint dataSize);

    [LibraryImport(CoreAudio)]
    private static partial int AudioObjectGetPropertyData(uint objectId, PropertyAddress* address,
        uint qualifierSize, void* qualifier, ref uint dataSize, void* data);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringGetLength(IntPtr text);

    [LibraryImport(CoreFoundation)]
    private static partial void CFStringGetCharacters(IntPtr text, CFRange range, char* buffer);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr value);
}
