# Porting the goose to macOS

How Take a Break Goose came to macOS, step by step. The port was built in a branch of its own,
so `main` (the Windows app) stayed releasable, and merged into `main` for release 1.8.0.

The macOS app is built with [Avalonia](https://avaloniaui.net/): its XAML is close to WPF's, the
goose's vector paths carry over, and Linux could follow later from the same code. It is shared
through GitHub releases only, not the App Store, and is not signed with an Apple Developer ID.

## Step 1: a shared core (done)

What the goose does without any windows now lives in `Core/` (`TakeABreakGoose.Core`, plain
`net8.0`, no WPF or WinForms). The Windows app references it and works as before.

- `Sessions/`: the break scheduler, focus sessions, the break history and the goodbye summary,
  Do not disturb during calls.
- `Settings/`: the settings and their file. The folder is passed in by the app
  (`%APPDATA%\TakeABreakGoose` on Windows; `~/Library/Application Support/TakeABreakGoose` on macOS).
- `Goose/NapWatch.cs`, `Text/TimeText.cs`, `Updates/UpdateChecker.cs`.
- `Platform/`: what the core needs from the computer, `IClock` and `IUserActivity` (idle time).
  The scheduler no longer runs its own timer: each app calls `Tick()` every second on its UI thread.

The shortcut used WPF's `ModifierKeys` and `Key`. The settings now keep `HotkeyModifiers` (same
names and values, so the file is unchanged) and the key's name as text ("G", "D5", "F1"), which
Avalonia's `Key` shares. A settings file from 1.7.0 reads and writes back byte for byte the same.

The tests are split: `tests/TakeABreakGoose.Tests` (`net8.0`) tests the core and can run on a Mac;
`tests/TakeABreakGoose.Windows.Tests` tests the Windows-only parts (starting with Windows, meeting
app names).

## Step 2: the Avalonia app (done)

`Mac/TakeABreakGoose.Mac.csproj`, on plain `net8.0` with Avalonia 11.3 (Avalonia brings its own
macOS backend, so `net8.0-macos` and Xcode aren't needed). It builds on Windows and runs there too,
which is how the windows and pages were checked; the macOS-only parts in `Mac/Platform/` do
nothing there. It publishes for `osx-arm64`.

- More of the shared code moved into the core: `GooseVisit`, `GooseOutfits`, `NapLook`,
  `EditableMessage`, `IGooseHost` and `GooseCoordinator`. The coordinator makes each visit's window
  through `IGooseWindow`, so both apps decide the goose's visits the same way, and it has tests now.
- `Mac/Goose/`: the goose figure with its outfits (the same paths and shapes), and the goose's
  window. WPF's animations became a small tween in `AnimatedValue.cs` that holds and stops values
  the way WPF's do; walking uses the same per-frame code.
- `Mac/Platform/OverlayWindow.cs`: what Avalonia can't do for the goose's window. It sets the
  `NSWindow`'s level (`NSStatusWindowLevel`), its collection behavior (all Spaces, next to
  full-screen apps, out of window cycling) and `hidesOnDeactivate = NO`. macOS hit-tests the whole
  window, transparent or not, so each frame the goose's window lets clicks through
  (`ignoresMouseEvents`) unless the cursor is over the goose, its bubble or its break card.
- `Mac/Windows/`: the main window with the Focus session, Wardrobe, Settings and Messages pages,
  and the dialogs. Avalonia's dialogs don't block, so checking for unsaved changes is async; quitting
  the app from the menu bar doesn't wait for that check.
- `Mac/Platform/MenuBarIcon.cs` (Avalonia's tray icon, which is an `NSStatusItem` on macOS) and
  `UserActivity.cs` (idle time from `CGEventSourceSecondsSinceLastEventType`) came along early,
  because the app needs them to be usable at all.
- macOS wording: "Start at login", "log in to your Mac", "Login Items in System Settings", and the
  shortcut shown as ⌃⌥⇧⌘.

Still to check on a real Mac: that `Position` and the screen's working area use the same units (or
the goose starts in the wrong place and walks at the wrong speed); that the goose naps just above
the Dock; that clicks go through the goose's window except on the goose; whether clicking the goose
takes focus from the app you're in (if it does, the window needs a non-activating `NSPanel`, made
with the Objective-C runtime); that animations keep their speed next to full-screen apps; and how
the system font changes the bubble's text wrapping.

## Step 3: the macOS platform layer (done)

All in `Mac/Platform/`, through `[LibraryImport]` to the system frameworks and `objc_msgSend` for
the Objective-C APIs (`ObjC.cs`). On Windows each of these does nothing, so the app can still be
tried out there.

- **Menu bar** (`MenuBarIcon.cs`): the same menu as the tray icon, with the shortcut shown next to
  "Open". No Dock icon (`ShowInDock = false` now, `LSUIElement` once it's an .app).
- **Messages** (`Windows/Notice.axaml`): what the tray shows as a balloon on Windows (the shortcut
  is taken, the first call that keeps the goose away) is a small card under the menu bar that goes
  away by itself. macOS's own notifications would need a signed app and the user's permission.
- **Global shortcut** (`HotkeyManager.cs`): Carbon's `RegisterEventHotKey`, which needs no
  Accessibility permission. The key names from the settings map to macOS key codes; the Windows
  key in the settings means Command. Since macOS 15 a shortcut needs Control or Command, so the
  Settings page asks for one of those.
- **Idle time** (`UserActivity.cs`): `CGEventSourceSecondsSinceLastEventType`.
- **Start at login** (`LoginItem.cs`): `SMAppService.mainAppService` (macOS 13+). Hidden on older
  versions and when the app isn't run from its .app bundle. A login item can't be told apart from
  opening the app by hand, so the Mac app always opens its window when it starts (the Windows app
  stays in the tray when Windows starts it).
- **Napping above the Dock**: the screen's working area leaves out the menu bar and the Dock,
  wherever the Dock is, so the goose naps just above (or beside) it. With the Dock hidden, it naps on
  the bottom edge.
- **Settings** in `~/Library/Application Support/TakeABreakGoose`.
- **One copy at a time** (`SingleInstance.cs`): opening the app again from Finder brings the running
  copy forward, which then opens its window (Avalonia's Reopen activation). A copy started another
  way (`open -n`, Terminal) finds the lock file in the settings folder taken, asks the first copy to
  open its window through a local pipe, and quits.
- **Calls** (`Calls.cs`), checked every 2 seconds like on Windows:
  - `CameraActivity.cs`: CoreMediaIO's `DeviceIsRunningSomewhere` on each video device.
  - `MicrophoneActivity.cs`: on macOS 14.2+, Core Audio's process objects: which processes are
    recording, and their bundle IDs. Meeting apps are matched by bundle ID (Zoom, Teams, Webex, Slack,
    FaceTime, the browsers and their helper processes, WebKit's media process for Safari).
  - Before 14.2 only the camera counts. As on Windows, missing a call means the goose shows up as
    usual, which is better than it quietly staying away for hours.
  - None of this needs a permission prompt: it reads whether devices are in use, not what they record.

Still to check on a real Mac: the shortcut (and that it's refused, not silently ignored, when
another app has it); starting at login from the .app; that a camera and a Zoom, Teams or Meet call
turn Do not disturb on, and that the microphone list needs no permission; the notice's position;
and opening the app a second time.

## Step 4: packaging and releases (done)

- Two downloads, one for Apple Silicon and one for Intel Macs, rather than one universal app. A
  self-contained .NET app can't simply be merged with `lipo`: Avalonia's and Skia's native libraries
  are universal already, but the .NET runtime's aren't, and about 100 of the framework's .dlls are
  precompiled for one processor, so they differ between the two builds too. One universal app would
  mean both builds in one bundle plus a launcher that picks one, at twice the size.
- `Mac/package.sh <arm64|x64> [version]` (on a Mac) publishes the app self-contained into a
  `Take a Break Goose.app` bundle with `Mac/Info.plist` (`LSUIElement`, the version, macOS 12 or
  later, which is what .NET 8 needs) and `Mac/Assets/goose.icns`. It signs it ad hoc, libraries first
  (Apple Silicon Macs won't run unsigned code at all), and wraps it in a `.dmg` with a link to
  Applications: `dist/TakeABreakGoose-x.y.z-mac-apple-silicon.dmg` or `…-mac-intel.dmg`.
- `goose.icns` comes from the same drawing as the Windows icon: `tools/make-icon.ps1` writes both,
  the Mac one with the margin macOS icons have.
- `.github/workflows/mac-build.yml` runs the core tests on macOS and builds both `.dmg`s. The
  release workflow calls it once the Windows job has made the release, and it adds them to that
  release. On a push to any other branch than main it builds them to try out, under the run's
  Artifacts.
- Both apps take their version from `Directory.Build.props` (releases set it from the tag).
- The README has a download section for Macs, including how to open an app that isn't signed with
  a Developer ID (Privacy & Security → Open Anyway, or `xattr -dr com.apple.quarantine`).

Checked: the Mac build on GitHub's macOS runner (the core tests, signing every file and the
bundle), and the Apple Silicon `.dmg` on a MacBook Pro with macOS 26.6. Opened straight from the
disk image, macOS refuses it with only an OK button ("operation not permitted": Gatekeeper, with
no Open Anyway); copied to Applications with the quarantine mark removed, it runs. The README
says so. Not checked yet: the Intel `.dmg`.

## Testing

Development happens on Windows, so the macOS app can be compiled here but not run. The core tests
run on both, and the release workflow builds on a macOS runner. Everything that touches the screen,
the menu bar, the shortcut, calls and login is tried out on a real Mac by a friend, with a checklist
per step: Apple Silicon and Intel, Dock on each side and hidden, a full-screen app, several Spaces, a
Zoom or Teams call, and macOS 13, 14 and 15.
