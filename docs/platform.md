# Platform integration

What the app asks of its operating system. The shared core only needs the time (`IClock`) and
the time since the last mouse or keyboard input (`IUserActivity`); everything else lives in
`App/Platform/`. Each job has one class there that picks the operating system with
`OperatingSystem.IsWindows()` or `IsMacOS()`, and the code for each is in `Platform/Windows/` and
`Platform/MacOS/` (marked `[SupportedOSPlatform("windows")]` on the Windows side). On another
operating system the jobs do nothing: the user counts as always present, and calls are never detected.

| Job | | Windows (`Platform/Windows/`) | macOS (`Platform/MacOS/`) |
|---|---|---|---|
| Icon and menu | `StatusIcon` | `WindowsTrayIcon`: `Shell_NotifyIcon` with Windows' own menu | `MenuBarIcon`: Avalonia's `TrayIcon` with a native menu |
| Short messages | `StatusIcon.ShowNotice` | Tray balloons | A small `Notice` window (`App/Windows/Notice.axaml`) |
| Global shortcut | `HotkeyManager` | `WindowsHotkey`: `RegisterHotKey` | `MacHotkey`: Carbon's `RegisterEventHotKey` |
| Idle time | `UserActivity` | `GetLastInputInfo` | `CGEventSourceSecondsSinceLastEventType` |
| Start at sign-in | `StartAtLogin` | `Autostart`: the per-user `Run` registry key | `LoginItem`: `SMAppService` (macOS 13+) |
| One copy at a time | `SingleInstance` | A lock file and a named pipe, plus a named mutex | macOS itself, plus the lock file and pipe |
| Calls | `Calls` | `WindowsCameraActivity`, `WindowsMicrophoneActivity`, `WindowsCalls` | `MacCameraActivity`, `MacMicrophoneActivity`, `MacCalls` |
| The goose's window | `OverlayWindow` | Extended window styles | `NSWindow` level and collection behaviour |
| Settings folder | `App.SettingsFolder()` | `%APPDATA%\TakeABreakGoose` | `~/Library/Application Support/TakeABreakGoose` |
| Installing and updating | `Updater` (Velopack) | Into `%LOCALAPPDATA%\TakeABreakGoose`, per user | The `.pkg` into `/Applications`; Velopack's `UpdateMac` swaps the app |

The tray icon and the shortcut on Windows get their messages through a `HiddenWindow`: a window
that's never shown, made with `CreateWindowEx`, whose messages arrive through Avalonia's own message
loop.

## Windows

### Tray icon

The icon's menu has **Open Take a Break Goose** (in bold, with the shortcut), **Take a break now**,
**Do not disturb** (*"(you're in a call)"* when a call turned it on), **Stop focus session**, when
a newer version is out **Update to version …**, and **Quit**. It's Windows' own menu
(`TrackPopupMenuEx`), made each time it opens; Avalonia's tray icon draws its menu itself, which
looked bigger than other tray menus. A click on the icon opens the
window. Its tooltip is the scheduler's status line. When Explorer restarts, the icon is added again
(`TaskbarCreated`).

Balloons tell the user about things that happen while the window is closed: the session that started
with Windows, the first call that kept the goose away, or a shortcut that's already taken.

### Global shortcut

`WindowsHotkey` registers the shortcut (Ctrl+Alt+G by default) with `RegisterHotKey`. The settings
keep the key by its name in Avalonia's `Key` enum (the same names as WPF's, which older versions
used), and `VirtualKey` turns that into Windows' key code. If another program already has the
shortcut, registering fails: the app says so in a balloon, and the Settings tab refuses a new
shortcut that's taken and keeps the old one.

### Starting with Windows

`Autostart` writes the app's path, with `--autostart`, to
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Turning the app off in Task Manager's
**Startup apps** doesn't remove that entry, but marks it disabled under `…\Explorer\StartupApproved\Run`,
so `IsEnabled` checks both. With `--autostart`, the app starts in the tray without opening its
window, and says so in a balloon.

### One copy at a time

A second copy asks the first one, through the named pipe, to open its window, and quits. The app
also holds the mutex `Local\TakeABreakGoose`, which versions from before the lock file used, so
those don't run next to it.

A Debug build (`dotnet run`) goes by `TakeABreakGoose.Debug` instead (`AppIdentity`): its own
settings folder, lock, pipe, mutex and Run entry, so it runs next to the installed goose and never
replaces its Start with Windows entry.

### Calls

`Calls.InProgress()` is true when a camera is in use, or when a meeting app records from a
microphone. It's checked every 2 seconds.

- **Camera** (`WindowsCameraActivity`): Media Foundation's sensor activity monitor
  (`MFCreateSensorActivityMonitor`), which is what drives the camera icon in the taskbar. It needs
  `MFStartup` first, which `Calls.Start()` does. If the monitor isn't available, the camera never
  counts as in use.
- **Microphone** (`WindowsMicrophoneActivity`): Core Audio's audio sessions (`IAudioSessionManager2`)
  on every active capture device, giving the names of the processes recording right now. The new
  Teams records from an `msedgewebview2` process, so for that one the parent process is used instead.
- **Meeting apps** (`WindowsCalls.MeetingPrograms`): Teams, Zoom, Webex, Slack, Skype, GoTo, and
  browsers (for Google Meet and Teams on the web). Games, Discord and Steam aren't on the list, since
  people voice chat there and still want the goose. An app missing from the list means the goose
  comes as usual, which is better than staying away for hours.

The registry's microphone and camera "consent store" isn't used: it no longer reports active use
on recent Windows 11 versions.

### The goose's window

`App/Goose/GooseWindow.axaml` is a 440×360 window at the bottom of the screen's working area, so the
goose stands on the taskbar.

- `OverlayWindow.Configure` adds `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`: it never takes the keyboard
  focus from what you're doing, and stays out of the taskbar and Alt+Tab.
- **Clicks**: Avalonia draws the transparent window with DirectComposition, and Windows hands every
  click on it to the window, transparent or not. So every frame, the window checks what's under the
  cursor (Avalonia's own hit test): over the goose's shapes, its bubble or its card it takes clicks,
  anywhere else it's `WS_EX_LAYERED | WS_EX_TRANSPARENT` and clicks go to whatever is underneath.
- **Frames** come from Avalonia's `RequestAnimationFrame`.

## macOS

The macOS side is described in more detail, with what still needs checking on a real Mac, in
[Porting to macOS](macos-port.md). In short:

- **Menu bar only**: `LSUIElement` in `App/Info.plist` keeps the app out of the Dock. It needs macOS 12
  or later.
- **Shortcut**: ⌃⌥G by default. `RegisterEventHotKey` needs no Accessibility permission, but since
  macOS 15 a shortcut needs Control or Command.
- **Calls**: the camera from CoreMediaIO ("is this device running somewhere"), and the microphone
  from Core Audio's process objects (macOS 14.2 and later; before that only the camera counts).
  Meeting apps are matched by bundle ID. Neither needs a permission, since they only say whether a
  device is in use, not what it sees or hears.
- **The goose's window**: above other windows at `NSStatusWindowLevel`, on every Space and next to
  full-screen apps. Like on Windows, the window lets clicks through whenever the cursor isn't over
  the goose. It naps just above the Dock.
- **Objective-C calls** go through the small `ObjC` helper (`objc_msgSend` imports).
- **Not signed by Apple**: `App/package.sh` signs the app ad hoc, which Apple Silicon needs to run
  it at all, and Velopack signs it again ad hoc after adding its updater, with the hardened runtime
  and `App/TakeABreakGoose.entitlements` (see [In-app updates](in-app-updates.md)). macOS blocks the
  downloaded installer the first time (Open Anyway); updates the app downloads itself aren't
  quarantined, so they open without asking.
