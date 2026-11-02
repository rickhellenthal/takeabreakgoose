# Porting the goose to macOS

The plan for bringing Take a Break Goose to macOS, worked on in a branch of its own so `main`
(the Windows app) stays releasable. Each step is reviewed before the next one starts.

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

## Step 3: the macOS platform layer

- **Menu bar**: started in step 2. Still to do: a template (monochrome) icon, the shortcut next to
  "Open", and notifications instead of the tray's balloons. No Dock icon: `LSUIElement` in
  `Info.plist`.
- **Global shortcut**: Carbon `RegisterEventHotKey` (needs no Accessibility permission, unlike an
  event tap). The Windows key in the settings means Command.
- **Idle time**: done in step 2 (`CGEventSourceSecondsSinceLastEventType`, behind `IUserActivity`).
- **Start at login**: `SMAppService.mainApp` (macOS 13+). On older versions the option is hidden.
- **Napping above the Dock**: the visible frame of the screen (`NSScreen.visibleFrame`) tells where
  the Dock is: bottom, left or right. When it auto-hides, nap on the screen's bottom edge.
- **Settings** in `~/Library/Application Support/TakeABreakGoose`: done in step 2.
- **Single instance**: a lock file in the settings folder; a second start tells the first one to
  open its window, and quits.
- **Calls**:
  - Camera in use: CoreMediaIO, `kCMIODevicePropertyDeviceIsRunningSomewhere` on each video device,
    watched with a property listener.
  - Who uses the microphone: on macOS 14.2+, Core Audio's process objects
    (`kAudioHardwarePropertyProcessObjectList`, then `kAudioProcessPropertyIsRunningInput` and
    `kAudioProcessPropertyBundleID` on each). Meeting apps are matched by bundle ID (zoom.us,
    Microsoft Teams, Webex, Slack, browsers), a list of its own next to the Windows one.
  - Before 14.2 only the camera counts. As on Windows, missing a call means the goose shows up as
    usual, which is better than it quietly staying away for hours.
  - None of this needs a permission prompt: it reads whether devices are in use, not what they record.

Native calls go through `[LibraryImport]` to the system frameworks and `objc_msgSend` for the
Objective-C APIs, kept in `Mac/Platform/`, like `Platform/` in the Windows app.

## Step 4: packaging and releases

- `dotnet publish` for `osx-arm64` and `osx-x64`, self-contained, then `lipo` the app host into one
  universal binary, in a `Take a Break Goose.app` bundle with `Info.plist` (`LSUIElement`, version,
  `LSMinimumSystemVersion`), the icon as `.icns`, ad-hoc signed (`codesign --force --deep -s -`),
  which Apple Silicon Macs require to run it at all.
- A `.dmg` with the app and a link to Applications (`hdiutil create`).
- A `macos` job in `.github/workflows/release.yml` on `macos-latest`: runs the core tests, builds the
  `.dmg` and adds it to the same GitHub release as the Windows installer.
- A README section on opening an unsigned app on macOS 15: open it once, then System Settings →
  Privacy & Security → Open Anyway; or in Terminal
  `xattr -dr com.apple.quarantine "/Applications/Take a Break Goose.app"`.

## Testing

Development happens on Windows, so the macOS app can be compiled here but not run. The core tests
run on both, and the release workflow builds on a macOS runner. Everything that touches the screen,
the menu bar, the shortcut, calls and login is tried out on a real Mac by a friend, with a checklist
per step: Apple Silicon and Intel, Dock on each side and hidden, a full-screen app, several Spaces, a
Zoom or Teams call, and macOS 13, 14 and 15.
