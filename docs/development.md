# Development and releases

## What you need

- The [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- Windows, for the Windows app. The core, its tests and the Mac app build on any platform.
- A Mac only to package the Mac app into a `.dmg` (`Mac/package.sh` needs `codesign` and `hdiutil`).
  GitHub Actions does that for releases.

## Running it

From the repository root:

```bash
dotnet run
```

That's the Windows app. The Mac app also runs on Windows (without its macOS-only parts), which is
handy for working on its windows and pages:

```bash
dotnet run --project Mac/TakeABreakGoose.Mac.csproj
```

**Quit the installed goose first** (tray icon → **Quit**). Only one copy runs at a time, so a second
one just says the goose is already running.

Useful while trying things out:

| | What it does |
|---|---|
| `-- --summon` | The break goose walks in right away, without a session |
| `-- --autostart` | Starts as it does when the computer starts: in the tray, without opening the window |
| A break length of a few seconds | Set under **Other…** on the Focus session tab, to try breaks quickly |

The app uses your real settings file (`%APPDATA%\TakeABreakGoose\settings.json`). Back it up before
testing something that changes settings, and put it back afterwards.

To try a release build of the Windows app, without installing it:

```bash
dotnet publish TakeABreakGoose.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

## Tests

```bash
dotnet test tests/TakeABreakGoose.Tests
dotnet test tests/TakeABreakGoose.Windows.Tests
```

- **`TakeABreakGoose.Tests`** (xUnit, `net8.0`) covers the core: the scheduler, the goose's visits
  and behaviour, the nap watch, session plans, the break history and goodbye sentence, calls and Do
  not disturb, settings (including files from older versions), and the time texts. It runs on
  Windows and macOS.
  - `SchedulerHarness` gives the scheduler a fake clock and fake idle time: `Work(...)`,
    `BeAway(...)` and `ComeBack()` move time forward and call `Tick()` along the way.
  - `GooseBehaviorTests` has a `GooseHarness` with a fake view, which runs a visit frame by frame
    (`WalkIn()`, `WorkUntil(...)`), records what the goose shows and answers, and clicks its buttons.
- **`TakeABreakGoose.Windows.Tests`** (`net8.0-windows`) covers the Windows-only parts: starting
  with Windows (on a scratch registry key, not the real one) and which programs count as meeting
  apps.

What happens on screen (the windows, the goose's walk, the tray, calls) isn't covered by tests:
try it in the app.

## Making a change

- **Behaviour goes in the core** where possible, with a test, so both apps get it.
- **The app window exists twice**: `Windows/` (WPF) and `Mac/Windows/` (Avalonia). A change to a
  page or a text usually needs both. The same goes for the goose's drawing in `Goose/` and
  `Mac/Goose/`.
- **Settings** that are renamed or replaced keep the old property around for reading, so older
  settings files carry over (see [Architecture](architecture.md#settings)).
- **Texts** are written for people who aren't programmers: friendly, short and without jargon.
- The README describes how the app works for users. Keep it in sync with changes in behaviour.

## Making a release

1. Set the new version in `Directory.Build.props`.
2. Write `release-notes/vX.Y.Z.md`: what's new for users, followed by the **Install** section
   (copy it from the previous release and change the version numbers).
3. Commit, then tag and push:

   ```bash
   git tag vX.Y.Z && git push origin main vX.Y.Z
   ```

Pushing a `v*` tag runs `.github/workflows/release.yml`:

1. Runs both test projects.
2. Publishes the Windows app as one self-contained `.exe`, so users don't need .NET installed.
3. Builds the installer with Inno Setup (`installer/TakeABreakGoose.iss`): a per-user install into
   `%LOCALAPPDATA%\Programs`, with no admin rights needed, and a Start menu shortcut. It optionally
   adds a desktop shortcut and starts the goose with Windows.
4. Zips the `.exe` as the portable download.
5. Creates the GitHub release with `release-notes/vX.Y.Z.md` as its description.
6. Then calls `.github/workflows/mac-build.yml`, which runs the core tests on macOS, builds a `.dmg`
   for Apple Silicon and one for Intel Macs with `Mac/package.sh`, and adds both to the release.

Running apps notice the new release by themselves (`UpdateChecker`) and show a link to it at the
bottom of the Settings tab.

Pushing to any other branch than `main` also runs the Mac build, so a branch's `.dmg`s can be
downloaded from the run's Artifacts.

## Other tools

- `tools/make-icon.ps1` draws the app icon from vector paths and writes `Assets/goose.ico`,
  `Mac/Assets/goose.icns` and `docs/icon.png`:

  ```bash
  powershell -File tools/make-icon.ps1
  ```
