using System;

namespace TakeABreakGoose;

/// <summary>
/// Starting the app when the user logs in, with SMAppService (macOS 13 and later). The app then
/// shows up under Login Items in System Settings, where the user can turn it off too.
/// </summary>
public static class LoginItem
{
    // SMAppServiceStatus
    private const nint Enabled = 1;
    private const nint RequiresApproval = 2;

    /// <summary>Only from macOS 13, and only for the app in its .app bundle (not when run from a build folder).</summary>
    public static bool IsAvailable => OperatingSystem.IsMacOSVersionAtLeast(13) && MainAppService() != IntPtr.Zero;

    /// <summary>On, or waiting for the user to allow it in System Settings, which still counts as their choice.</summary>
    public static bool IsEnabled
    {
        get
        {
            if (!IsAvailable) return false;
            nint status = ObjC.SendNInt(MainAppService(), ObjC.Selector("status"));
            return status is Enabled or RequiresApproval;
        }
    }

    /// <summary>Throws, with macOS's own explanation, when it can't be changed.</summary>
    public static void SetEnabled(bool enabled)
    {
        if (!IsAvailable) throw new InvalidOperationException("Starting at login needs macOS 13 or later.");
        string selector = enabled ? "registerAndReturnError:" : "unregisterAndReturnError:";
        if (ObjC.SendWithError(MainAppService(), ObjC.Selector(selector), out IntPtr error)) return;
        string? reason = error == IntPtr.Zero ? null : ObjC.Text(ObjC.Send(error, ObjC.Selector("localizedDescription")));
        throw new InvalidOperationException(reason ?? "macOS didn't allow it.");
    }

    private static IntPtr _mainAppService;

    /// <summary>[SMAppService mainAppService]: this app itself as a login item.</summary>
    private static IntPtr MainAppService()
    {
        if (_mainAppService != IntPtr.Zero || !OperatingSystem.IsMacOSVersionAtLeast(13)) return _mainAppService;
        // Outside an .app bundle there's nothing to register.
        if (!AppContext.BaseDirectory.Contains(".app/Contents/", StringComparison.Ordinal)) return IntPtr.Zero;
        ObjC.LoadFramework("ServiceManagement");
        _mainAppService = ObjC.Send(ObjC.Class("SMAppService"), ObjC.Selector("mainAppService"));
        return _mainAppService;
    }
}
