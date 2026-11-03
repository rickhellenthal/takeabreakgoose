using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TakeABreakGoose;

/// <summary>Tells which programs are recording from a microphone right now, from Windows' audio sessions.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsMicrophoneActivity
{
    /// <summary>The names of the programs (like "zoom" or "chrome") with an active recording session.</summary>
    public static List<string> Programs()
    {
        var programs = new List<string>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.EnumAudioEndpoints(DataFlowCapture, DeviceStateActive, out var devices);
            devices.GetCount(out uint deviceCount);
            for (uint d = 0; d < deviceCount; d++)
            {
                devices.Item(d, out var device);
                var iid = typeof(IAudioSessionManager2).GUID;
                device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out object activated);
                ((IAudioSessionManager2)activated).GetSessionEnumerator(out var sessions);
                sessions.GetCount(out int sessionCount);
                for (int s = 0; s < sessionCount; s++)
                {
                    sessions.GetSession(s, out var session);
                    var control = (IAudioSessionControl2)session;
                    control.GetState(out int state);
                    if (state != AudioSessionStateActive) continue;
                    control.GetProcessId(out uint processId);
                    if (ProgramName((int)processId) is string name) programs.Add(name);
                }
            }
        }
        catch (Exception)
        {
            // No audio devices, or no access: nothing is recording as far as the goose can tell.
        }
        return programs;
    }

    /// <summary>
    /// The program's name. For the WebView2 helper that apps like the new Teams record through, the
    /// name of the app that started it.
    /// </summary>
    private static string? ProgramName(int processId)
    {
        try
        {
            for (int depth = 0; depth < 4 && processId > 0; depth++)
            {
                using var process = Process.GetProcessById(processId);
                string name = process.ProcessName.ToLowerInvariant();
                if (name != "msedgewebview2") return name;
                processId = ParentProcessId(process.Handle);
            }
        }
        catch (Exception)
        {
            // The program just exited, or belongs to another user.
        }
        return null;
    }

    private static int ParentProcessId(IntPtr process)
    {
        var info = new ProcessBasicInformation();
        int status = NtQueryInformationProcess(process, 0, ref info, Marshal.SizeOf<ProcessBasicInformation>(), out _);
        return status == 0 ? (int)info.InheritedFromUniqueProcessId : 0;
    }

    // ---- Core Audio interop ----

    private const int DataFlowCapture = 1;
    private const int DeviceStateActive = 1;
    private const int ClsctxAll = 23;
    private const int AudioSessionStateActive = 1;

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        void GetCount(out uint count);
        void Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object activated);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        void GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
        void GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
        void GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        void GetCount(out int count);
        void GetSession(int index, out IAudioSessionControl session);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl
        void GetState(out int state);
        void GetDisplayName(out IntPtr name);
        void SetDisplayName(IntPtr name, IntPtr eventContext);
        void GetIconPath(out IntPtr path);
        void SetIconPath(IntPtr path, IntPtr eventContext);
        void GetGroupingParam(out Guid groupingId);
        void SetGroupingParam(ref Guid groupingId, IntPtr eventContext);
        void RegisterAudioSessionNotification(IntPtr client);
        void UnregisterAudioSessionNotification(IntPtr client);
        // IAudioSessionControl2
        void GetSessionIdentifier(out IntPtr id);
        void GetSessionInstanceIdentifier(out IntPtr id);
        void GetProcessId(out uint processId);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass,
        ref ProcessBasicInformation info, int size, out int returned);
}
