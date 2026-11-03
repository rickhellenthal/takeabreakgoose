using System;
using System.Runtime.InteropServices;

namespace TakeABreakGoose;

/// <summary>
/// Tells whether any app is using a camera, from CoreMediaIO's "running somewhere" on each video
/// device. That only says whether a camera is on, not what it sees, so it needs no permission.
/// </summary>
public static unsafe partial class MacCameraActivity
{
    private const uint SystemObject = 1;
    private const uint Devices = 0x64657623;                // 'dev#'
    private const uint DeviceIsRunningSomewhere = 0x676F6E65; // 'gone'
    private const uint GlobalScope = 0x676C6F62;            // 'glob'
    private const uint MainElement = 0;

    public static bool InUse()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try
        {
            var devices = new PropertyAddress { Selector = Devices, Scope = GlobalScope, Element = MainElement };
            if (CMIOObjectGetPropertyDataSize(SystemObject, &devices, 0, null, out uint size) != 0 || size == 0) return false;
            var ids = new uint[size / sizeof(uint)];
            fixed (uint* idsPointer = ids)
            {
                if (CMIOObjectGetPropertyData(SystemObject, &devices, 0, null, size, out _, idsPointer) != 0) return false;
            }

            var running = new PropertyAddress { Selector = DeviceIsRunningSomewhere, Scope = GlobalScope, Element = MainElement };
            foreach (uint device in ids)
            {
                uint isRunning = 0;
                if (CMIOObjectGetPropertyData(device, &running, 0, null, sizeof(uint), out _, &isRunning) == 0 && isRunning != 0)
                    return true;
            }
            return false;
        }
        catch (Exception)
        {
            return false; // no way to tell: the goose comes as usual
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyAddress
    {
        public uint Selector;
        public uint Scope;
        public uint Element;
    }

    private const string CoreMediaIO = "/System/Library/Frameworks/CoreMediaIO.framework/CoreMediaIO";

    [LibraryImport(CoreMediaIO)]
    private static partial int CMIOObjectGetPropertyDataSize(uint objectId, PropertyAddress* address,
        uint qualifierSize, void* qualifier, out uint dataSize);

    [LibraryImport(CoreMediaIO)]
    private static partial int CMIOObjectGetPropertyData(uint objectId, PropertyAddress* address,
        uint qualifierSize, void* qualifier, uint dataSize, out uint dataUsed, void* data);
}
