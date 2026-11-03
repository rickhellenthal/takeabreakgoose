# One app: the Avalonia app on Windows too

An assessment of dropping the WPF Windows app and shipping the Avalonia app (`Mac/`) on Windows as
well, so every page, dialog and the goose's drawing exist once. Written on `feature/languages`
(November 2026); no app code was changed for it.

## Recommendation: go

The Avalonia app already runs on Windows and looks right there; what's missing is the Windows
platform layer, most of which is plain P/Invoke and COM code that moves over as it is. Nothing
found blocks it. The one real problem (the goose's window eats clicks, see below) has a fix that
was tried on the running app and works. It also makes the download a third smaller.

Do it as a branch of its own after `feature/languages` is merged, since that branch touches every
page. Total effort: **about 4–6 days**, most of it trying things by hand on Windows.

## Where it stands

Done, on the branch `feature/avalonia-windows`: the WPF app is gone, and the Avalonia app moved
from `Mac/` to `App/` (`App/TakeABreakGoose.App.csproj`). What it took, beyond the plan below:

- `App/Platform/` is split into `Windows/` and `MacOS/`, with one class per job that picks the
  operating system: `Calls`, `UserActivity`, `HotkeyManager`, `StartAtLogin`, `StatusIcon`,
  `OverlayWindow`, `SingleInstance`.
- **The goose's window** lets clicks through wherever the goose, bubble and card aren't, never takes
  the focus, and stays out of Alt+Tab. The peeking goose now sees the cursor on Windows.
- **The tray icon** is Windows' own (`Shell_NotifyIcon`, `TrackPopupMenu`) rather than Avalonia's,
  whose menu was bigger than other tray menus and didn't look like them. **Open** is bold again,
  and the tray balloons are back.
- **Start with Windows**, `--autostart` (in the tray, with a balloon), the shortcut (`RegisterHotKey`),
  calls (camera and microphone), idle time, and the mutex the installer looks for.
- **Orange instead of the Windows accent colour** (check boxes and focus borders were red):
  Fluent's palette now has the goose's own accent.
- **The party hat** was cut off: Avalonia doesn't draw what sticks out above the figure (the pompom,
  while walking). The figure now has room for it.
- Windows texts on the Settings page, `Ctrl+Alt+G` instead of `⌃⌥G`.
- The `.exe` has the goose icon. The Windows tests (31) and `release.yml` use the Avalonia project.
- A Debug build on Windows keeps its own settings (`%APPDATA%\TakeABreakGoose.Debug`), lock and Run
  entry, so it runs next to the installed goose. A Release build uses the real ones, like the WPF app.

The rest of this document is the assessment as it was written beforehand, with the names from then.

## What was duplicated

| | WPF (`Windows/`, `Goose/`) | Avalonia (`Mac/Windows/`, `Mac/Goose/`) |
|---|---|---|
| App window, pages, dialogs, styles | 1,854 lines in 16 files | 1,967 lines in 19 files |
| The goose's window and drawing | 798 lines in 4 files | 1,061 lines in 5 files (with `AnimatedValue`) |
| Startup and wiring | `App.xaml.cs`, 245 lines | `App.axaml.cs` + `Program.cs`, 272 lines |

So about **2,700 lines exist twice** (not counting the platform code, which is different by nature). The two are a line-by-line port of each other: the same
controls, names and `Strings` in every page (checked by comparing which `Strings.X` and named
controls each pair uses). In the code-behind, 20–75% of the lines differ, but almost all of that is
API spelling (`Visibility` vs `IsVisible`, `FindResource` vs `Classes`, `ScrollToTop` vs
`ScrollToHome`), not behaviour.

Where they have drifted apart:

- **Settings page**: Windows texts vs Mac texts (`…Windows`/`…Mac` strings, *Start with Windows* vs
  *Start at login*), and the Mac app hides the login item when it isn't available. The Mac
  shortcut box refuses Option alone (macOS 15 rule), lets Tab through, and spells shortcuts as
  `⌃⌥G` (`HotkeyText.Mac`) instead of `Ctrl+Alt+G` (`GooseSettings.HotkeyText`).
- **Focus session page**: the Avalonia version only reacts to the *Other…* chip's own Checked
  event, because Avalonia raises them in a different order; a fix of that kind is easy to miss in
  the other copy.
- **App window**: the unsaved-changes dialog can't block in Avalonia, so closing and switching tabs
  is written differently.
- **Goose window**: WPF sets `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` and finds the cursor with
  `GetCursorPos`; the Avalonia one does neither on Windows (see the risks).
- **Short messages**: the Windows app has tray balloons (session started with Windows, first call,
  shortcut taken); the Mac app has its own `Notice` window instead.
- **Starting with the computer**: the Windows app starts hidden with `--autostart` and says so in a
  balloon; the Mac app always opens its window.

## What the Avalonia app needs from Windows

Each part of `Platform/` and `Settings/Autostart.cs`, and how it plugs in. The model is
`Mac/Platform/`: one class per job, which checks the OS itself and does nothing elsewhere (as
`UserActivity` and `OverlayWindow` already do). The project stays plain `net8.0`; the Windows code
gets `[SupportedOSPlatform("windows")]` and `OperatingSystem.IsWindows()` checks, the way the Mac
code has `IsMacOS()`. Classes with the same name on both sides (`Calls`, `UserActivity`,
`HotkeyManager`, `CameraActivity`, `MicrophoneActivity`) need one public face each, for example
`Calls.InProgress()` calling `WindowsCalls` or `MacCalls`, with the OS code in
`Platform/Windows/` and `Platform/Mac/`.

| Part | Today (WPF app) | In the Avalonia app | Effort |
|---|---|---|---|
| Idle time | `UserActivity`: `GetLastInputInfo` | Copy as is, behind `IsWindows()` | 1 h |
| Camera | `CameraActivity`: Media Foundation, COM interop | Copy as is; COM interop works on `net8.0` on Windows. Keep `Start()`/`Stop()` in `App` | 1 h |
| Microphone | `MicrophoneActivity`: Core Audio, COM interop | Copy as is | 1 h |
| Meeting apps | `Calls.MeetingPrograms` by process name | Keep both lists; pick by OS | 1 h |
| Tray icon and menu | `TrayIcon`: WinForms `NotifyIcon` | `MenuBarIcon` already works on Windows (Avalonia's `TrayIcon`; a click opens the window). Rename it `TrayIcon` | 2 h |
| Tray balloons | `NotifyIcon.ShowBalloonTip` | Avalonia has none. Use the `Notice` window on both (check where it appears on Windows), or a small `Shell_NotifyIcon` call for a real balloon | 2–4 h |
| Global shortcut | `HotkeyManager`: `RegisterHotKey` on a WPF `HwndSource` | `RegisterHotKey` on a message-only window made with `CreateWindowEx` (no WPF), and a key-name → virtual key table instead of WPF's `KeyInterop` (the settings keep WPF `Key` names; Avalonia's `Key` has the same names, so the settings file is read the same) | 4 h |
| Start with Windows | `Autostart`: `Run` key + `StartupApproved` | Copy as is: `Microsoft.Win32.Registry` is in `net8.0`. Next to `LoginItem`, one `StartAtLogin` face picks by OS | 1 h |
| `--autostart` | Starts hidden, balloon | Port the same lines into `App.axaml.cs` (Windows only; on macOS a login item can't be told apart) | 1 h |
| One copy at a time | Mutex `Local\TakeABreakGoose` + message box | Keep the file lock and pipe (a second copy brings the first one's window forward, which is nicer), **and** hold the same mutex on Windows: the installer's `AppMutex` needs it | 1 h |
| Goose window | `WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW`, `GetCursorPos` | A Windows half for `OverlayWindow` (see the next section) | 4 h |
| Settings folder | `%APPDATA%\TakeABreakGoose` | `App.SettingsFolder()` returns that on Windows instead of `TakeABreakGoose.Mac` | 15 min |
| Texts | `…Windows` strings | Pick `…Windows` or `…Mac` by OS (`SettingsPage`, notices, `HotkeyText`) | 2 h |

## Tried on Windows

The Avalonia app (Debug build, and the published `.exe`) on Windows 11, one 2560×1440 screen at
150%, next to its own settings in `%APPDATA%\TakeABreakGoose.Mac` (backed up and put back).

- **The goose's window**: transparent, always on top, the right size (660×540 pixels for 440×360 at
  150%), and standing exactly on the taskbar. The goose, bubble and buttons look the same as in
  WPF. Not in the taskbar.
- **It eats clicks: the one real problem.** The whole 440×360 rectangle catches the mouse,
  transparent or not (`WindowFromPoint` on empty corners returned the goose's window). WPF's
  `AllowsTransparency` lets those through; Avalonia draws with DirectComposition and doesn't. While
  the goose walks or naps, a large invisible patch above the taskbar would ignore clicks.
  **Fix, tried**: adding `WS_EX_LAYERED | WS_EX_TRANSPARENT` to the running window (from outside)
  made clicks go through and the goose still drew correctly. So it's the macOS approach on
  Windows: `OverlayWindow.SetClickThrough` toggles `WS_EX_TRANSPARENT` whenever the cursor isn't over
  the goose, bubble or card, with `CursorInWindow` from `GetCursorPos`. That also makes
  `IsCursorBetween` work on Windows, which it doesn't today: **the peeking goose never sees the
  cursor on Windows**, so it doesn't duck back.
- **Focus and Alt+Tab**: the window has no `WS_EX_NOACTIVATE` or `WS_EX_TOOLWINDOW`
  (`ShowActivated="False"` and `ShowInTaskbar="False"` don't set them). Set them in the same
  Windows half of `OverlayWindow.Configure`, as WPF's `OnSourceInitialized` does.
- **CPU**: about 12% of one core with the goose standing on screen (a frame per screen refresh with
  `RequestAnimationFrame`). Probably the same as WPF's software-drawn transparent window, but not
  compared; worth a look with Task Manager side by side.
- **A second copy** quits at once and brings the first one's window forward.
- **The app window**: the Fluent theme with the app's own styles looks right: chips, buttons,
  fonts (Segoe UI, Windows' default) and colours match the WPF app's. Not compared pixel by pixel.
- **The published `.exe`** starts. It unpacks Skia, HarfBuzz and ANGLE (16 MB) to
  `%TEMP%\.net\TakeABreakGoose\…` on first run, as the WPF one does with its own native files (8 MB).

Not tried (needs a person or other hardware):

- **The tray menu**: Avalonia draws it as its own pop-up on Windows, not a Windows menu. Check how it
  looks, that it closes when clicking elsewhere, and that **Open** is no longer bold (it can't be).
- **Several screens and mixed scaling**: both apps use the primary screen's working area only, so the
  behaviour should match; Avalonia places windows in pixels with the primary screen's scaling, which
  is right for the primary screen. Try a second screen at a different scaling, and the taskbar on
  the left or top.
- Calls, the shortcut and Start with Windows: they don't exist in the Avalonia app on Windows yet.

## Packaging

Self-contained single-file `win-x64`, with the release workflow's flags (compressed, native
libraries included):

| | `.exe` |
|---|---|
| WPF app | 68.5 MB |
| Avalonia app | **42.8 MB** |

Avalonia needs neither WPF nor WinForms in the bundle, which is where the difference comes from.

Changes:

- **The project**: give `Mac/TakeABreakGoose.Mac.csproj` `<ApplicationIcon>..\Assets\goose.ico</ApplicationIcon>`
  (the `.exe` has no icon today) and `InternalsVisibleTo` for the Windows tests. Later, rename the
  folder and project (`App/TakeABreakGoose.App.csproj`) and update `Mac/package.sh`,
  `mac-build.yml` and the docs; that can be its own step.
- **`release.yml`**: publish the Avalonia project instead of `TakeABreakGoose.csproj`; everything
  else stays. Keep `AssemblyName` `TakeABreakGoose`, so the file is still `TakeABreakGoose.exe`.
- **`installer/TakeABreakGoose.iss`**: no changes, as long as the `.exe` name, the `AppId`, and the
  `Local\TakeABreakGoose` mutex stay the same. Update the comment that points at `App.xaml.cs`.
- **Existing users**: the installer updates in place (same `AppId` and folder). Settings carry over
  once `SettingsFolder()` returns `%APPDATA%\TakeABreakGoose` on Windows: same `settings.json`, and
  the hotkey's key names read the same in Avalonia. Start with Windows carries over untouched: the
  `Run` value points at the same path with `--autostart`, which the new app must keep handling.
  Development runs should then use a flag or a Debug-only folder rather than `TakeABreakGoose.Mac`,
  or `dotnet run` would use the real settings (as the WPF app's `dotnet run` does today).

## Tests

- `tests/TakeABreakGoose.Tests` (the core) doesn't change.
- `tests/TakeABreakGoose.Windows.Tests` (18 tests before this work: `AutostartTests` on a scratch registry key,
  `CallsTests` for meeting app names) keeps its tests, but references the Avalonia project instead
  of `TakeABreakGoose.csproj` and drops `UseWPF`. It can stay `net8.0-windows`.
- New, cheap: a test for the hotkey's key name → virtual key table, and the Mac `Calls.IsMeetingApp`
  bundle IDs (in a test project that runs on both, with OS-specific tests skipped elsewhere).

## Step by step

1. **Merge `feature/languages`** first. (–)
2. **Fix the goose's window on Windows**: the Windows half of `OverlayWindow` (click-through,
   cursor, no-activate, tool window). Then try the goose by hand: walking, clicking through around
   it, peeking, napping, a second screen. This is the step that decides it, so it goes first.
   (½ day plus trying)
3. **Move the Windows platform layer** into the Avalonia app, behind OS checks: idle time, camera,
   microphone, meeting apps, Run key, mutex, `--autostart`, settings folder. Mostly copying.
   (½–1 day)
4. **Global shortcut** without WPF: message-only window and key table, with a test. (½ day)
5. **Notices and tray**: `Notice` instead of balloons (or a real balloon), Windows texts per OS,
   check the tray menu by hand. (½ day)
6. **Point the Windows tests and the release** at the Avalonia project; add the `.exe` icon; build
   the installer once by hand and install over the current version, checking that settings and Start
   with Windows carry over. (½ day)
7. **Try everything by hand on Windows** for a few days of real use, next to the old one's
   behaviour: calls, shortcut, Start with Windows, Dutch, the tray, CPU. (1–2 days, spread out)
8. **Delete the WPF app**: `TakeABreakGoose.csproj`, `App.xaml*`, `Windows/`, `Goose/`, `Platform/`,
   `Settings/`, and update `architecture.md`, `platform.md`, `development.md` and the README.
   Optionally rename `Mac/` to `App/`. (½ day)

## What to try by hand first

Before committing to it, half an hour with the Avalonia app as it is:

```bash
dotnet run --project Mac/TakeABreakGoose.Mac.csproj -- --summon
```

- Does the goose walk smoothly, compared with the installed one? Watch CPU in Task Manager.
- Open the tray icon's menu (in the hidden icons): how does it look, and does it close properly?
- Look through all three tabs and the Messages page next to the installed goose: anything that
  looks off (checkboxes, text boxes, focus outlines, scrolling)?
- With a second screen or another scaling: where does the goose stand?

Expect the goose's empty surroundings to block clicks; that's the known issue from step 2.
