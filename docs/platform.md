# Platform integration

What each app asks of its operating system. The shared core only needs the time (`IClock`) and
the time since the last mouse or keyboard input (`IUserActivity`); everything else here lives in
`Platform/` (Windows) and `Mac/Platform/` (macOS).

| | Windows (`Platform/`, `Settings/`) | macOS (`Mac/Platform/`) |
|---|---|---|
| Icon and menu | `TrayIcon`: a WinForms `NotifyIcon` | `MenuBarIcon`: Avalonia's `TrayIcon` with a native menu |
| Short messages | Tray balloons | A small `Notice` window (`Mac/Windows/Notice.axaml`) |
| Global shortcut | `HotkeyManager`: `RegisterHotKey` | `HotkeyManager`: Carbon's `RegisterEventHotKey` |
| Idle time | `UserActivity`: `GetLastInputInfo` | `UserActivity`: `CGEventSourceSecondsSinceLastEventType` |
| Start at sign-in | `Autostart`: the per-user `Run` registry key | `LoginItem`: `SMAppService` (macOS 13+) |
| One copy at a time | A named mutex | macOS itself, plus a named pipe (`SingleInstance`) |
| Calls | `CameraActivity`, `MicrophoneActivity`, `Calls` | The same three classes, on macOS APIs |
| Settings folder | `%APPDATA%\TakeABreakGoose` | `~/Library/Application Support/TakeABreakGoose` |

## Windows

### Tray icon

The icon's menu has **Open Take a Break Goose**, **Take a break now**, **Do not disturb** (*"(you're
in a call)"* when a call turned it on), **Stop focus session** and **Quit**. Its tooltip is the
scheduler's status line. Balloons tell the user about things that happen while the window is
closed: the session that started with Windows, the first call that kept the goose away, or a
shortcut that's already taken.

### Global shortcut

`HotkeyManager` registers the shortcut (Ctrl+Alt+G by default) with `RegisterHotKey`, on a hidden
message-only window. If another program already has it, registering fails: the app says so in a
balloon, and the Settings tab refuses a new shortcut that's taken and keeps the old one.

### Starting with Windows

`Autostart` writes the app's path, with `--autostart`, to
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Turning the app off in Task Manager's
**Startup apps** doesn't remove that entry, but marks it disabled under `…\Explorer\StartupApproved\Run`,
so `IsEnabled` checks both. With `--autostart`, the app starts in the tray without opening its
window.

### One copy at a time

The app holds the mutex `Local\TakeABreakGoose`. A second copy shows *"Take a Break Goose is
already running"* and quits. The installer uses the same mutex (`AppMutex`) to ask the user to
close the running goose before updating.

### Calls

`Calls.InProgress()` is true when a camera is in use, or when a meeting app records from a
microphone. It's checked every 2 seconds.

- **Camera** (`CameraActivity`): Media Foundation's sensor activity monitor
  (`MFCreateSensorActivityMonitor`), which is what drives the camera icon in the taskbar. It needs
  `MFStartup` first. If the monitor isn't available, the camera never counts as in use.
- **Microphone** (`MicrophoneActivity`): Core Audio's audio sessions (`IAudioSessionManager2`) on
  every active capture device, giving the names of the processes recording right now. The new Teams
  records from an `msedgewebview2` process, so for that one the parent process is used instead.
- **Meeting apps** (`Calls.MeetingPrograms`): Teams, Zoom, Webex, Slack, Skype, GoTo, and browsers
  (for Google Meet and Teams on the web). Games, Discord and Steam aren't on the list, since people
  voice chat there and still want the goose. An app missing from the list means the goose comes as
  usual, which is better than staying away for hours.

The registry's microphone and camera "consent store" isn't used: it no longer reports active use
on recent Windows 11 versions.

### The goose's window

`Goose/GooseWindow.xaml` is a 440×360 window at the bottom of the screen's working area, so the
goose stands on the taskbar.

- `AllowsTransparency="True"`: fully transparent pixels let clicks through to whatever is under
  them, so only the goose and its speech bubble catch clicks.
- `Topmost`, and `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`: it floats over other windows, never takes
  the keyboard focus from what you're doing, and stays out of the taskbar and Alt+Tab.
- **Frames** come from a `DispatcherTimer` every 16 ms at `DispatcherPriority.Input`, not from
  `CompositionTarget.Rendering`. A transparent WPF window is redrawn in software every frame, and
  at render priority that could keep clicks in the app's other window waiting until the goose
  stopped walking.

## macOS

The macOS side is described in more detail, with what still needs checking on a real Mac, in
[Porting to macOS](macos-port.md). In short:

- **Menu bar only**: `LSUIElement` in `Mac/Info.plist` keeps the app out of the Dock. It needs macOS 12
  or later.
- **Shortcut**: ⌃⌥G by default. `RegisterEventHotKey` needs no Accessibility permission, but since
  macOS 15 a shortcut needs Control or Command.
- **Calls**: the camera from CoreMediaIO ("is this device running somewhere"), and the microphone
  from Core Audio's process objects (macOS 14.2 and later; before that only the camera counts).
  Meeting apps are matched by bundle ID. Neither needs a permission, since they only say whether a
  device is in use, not what it sees or hears.
- **The goose's window** (`Mac/Goose/GooseWindow.axaml`, with `OverlayWindow`): above other windows
  at `NSStatusWindowLevel`, on every Space and next to full-screen apps. macOS hit-tests the whole
  window, transparent or not, so the window lets clicks through whenever the cursor isn't over the
  goose. Frames come from Avalonia's `RequestAnimationFrame`. It naps just above the Dock.
- **Objective-C calls** go through the small `ObjC` helper (`objc_msgSend` imports).
- **Not signed by Apple**: `Mac/package.sh` signs the app ad hoc, which Apple Silicon needs to run
  it at all. Users remove the quarantine flag once (see the main README).

The Mac app builds and runs on Windows too, which helps when working on its windows and pages. The
macOS-only parts then do nothing: the user counts as always present, and calls are never detected.
