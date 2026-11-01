using System;
using System.Runtime.InteropServices;

namespace TakeABreakGoose;

/// <summary>
/// Knows whether any app is streaming from a camera, from Media Foundation's sensor activity
/// monitor (the same information behind the camera icon in the taskbar).
/// </summary>
public static class CameraActivity
{
    private static IMFSensorActivityMonitor? _monitor;
    private static ReportCallback? _callback;
    private static volatile bool _inUse;

    public static bool InUse => _inUse;

    /// <summary>Starts listening. If the monitor isn't available, the camera just never counts as in use.</summary>
    public static void Start()
    {
        try
        {
            Marshal.ThrowExceptionForHR(MFStartup(MfVersion, MfStartupFull));
            _callback = new ReportCallback();
            Marshal.ThrowExceptionForHR(MFCreateSensorActivityMonitor(_callback, out _monitor));
            _monitor.Start();
        }
        catch (Exception)
        {
            _monitor = null;
        }
    }

    public static void Stop()
    {
        if (_monitor == null) return;
        try
        {
            _monitor.Stop();
            MFShutdown();
        }
        catch (Exception)
        {
            // The app is closing anyway.
        }
        _monitor = null;
    }

    /// <summary>Called by Windows (on a worker thread) whenever camera activity changes.</summary>
    [ComVisible(true)]
    private sealed class ReportCallback : IMFSensorActivitiesReportCallback
    {
        public int OnActivitiesReport(IMFSensorActivitiesReport report)
        {
            try
            {
                _inUse = AnyStreaming(report);
            }
            catch (Exception)
            {
                _inUse = false;
            }
            return 0;
        }

        private static bool AnyStreaming(IMFSensorActivitiesReport report)
        {
            report.GetCount(out uint devices);
            for (uint d = 0; d < devices; d++)
            {
                report.GetActivityReport(d, out var device);
                device.GetProcessCount(out uint processes);
                for (uint p = 0; p < processes; p++)
                {
                    device.GetProcessActivity(p, out var process);
                    process.GetStreamingState(out bool streaming);
                    if (streaming) return true;
                }
            }
            return false;
        }
    }

    // ---- Media Foundation sensor activity interop ----

    private const int MfVersion = 0x00020070;
    private const int MfStartupFull = 0;

    [DllImport("mfplat.dll")]
    private static extern int MFStartup(int version, int flags);

    [DllImport("mfplat.dll")]
    private static extern int MFShutdown();

    [DllImport("mfsensorgroup.dll")]
    private static extern int MFCreateSensorActivityMonitor(IMFSensorActivitiesReportCallback callback,
        out IMFSensorActivityMonitor monitor);

    [ComImport, Guid("DE5072EE-DBE3-46DC-8A87-B6F631194751"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSensorActivitiesReportCallback
    {
        [PreserveSig] int OnActivitiesReport(IMFSensorActivitiesReport report);
    }

    [ComImport, Guid("D0CEF145-B3F4-4340-A2E5-7A5080CA05CB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSensorActivityMonitor
    {
        void Start();
        void Stop();
    }

    [ComImport, Guid("683F7A5E-4A19-43CD-B1A9-DBF4AB3F7777"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSensorActivitiesReport
    {
        void GetCount(out uint count);
        void GetActivityReport(uint index, out IMFSensorActivityReport report);
        void GetActivityReportByDeviceName([MarshalAs(UnmanagedType.LPWStr)] string name, out IMFSensorActivityReport report);
    }

    [ComImport, Guid("3E8C4BE1-A8C2-4528-90DE-2851BDE5FEAD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSensorActivityReport
    {
        void GetFriendlyName(IntPtr name, uint length, out uint written);
        void GetSymbolicLink(IntPtr link, uint length, out uint written);
        void GetProcessCount(out uint count);
        void GetProcessActivity(uint index, out IMFSensorProcessActivity activity);
    }

    [ComImport, Guid("39DC7F4A-B141-4719-813C-A7F46162A2B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSensorProcessActivity
    {
        void GetProcessId(out uint processId);
        void GetStreamingState([MarshalAs(UnmanagedType.Bool)] out bool streaming);
        void GetStreamingMode(out int mode);
        void GetReportTime(out long time);
    }
}
