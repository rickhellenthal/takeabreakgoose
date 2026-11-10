# Development and releases

## What you need

- The [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- Any platform for the app, the core and its tests; Windows for the Windows tests.
- A Mac only to build the `.app` (`App/package.sh` needs `codesign`) and pack it with Velopack.
  GitHub Actions does that for releases.

## Running it

From the repository root:

```bash
dotnet run --project App
```

A Debug build like this keeps apart from the installed goose on Windows: its own settings
(`%APPDATA%\TakeABreakGoose.Debug`), its own lock, so both run at the same time, and its own Start
with Windows entry. On a Mac it uses the real settings and the installed goose has to quit first
(menu bar icon → **Quit**).

Useful while trying things out:

| | What it does |
|---|---|
| `-- --summon` | The break goose walks in right away, without a session |
| `-- --autostart` | Starts as it does when the computer starts: in the tray, without opening the window |
| A break length of a few seconds | Set under **Other…** on the Focus session tab, to try breaks quickly |

A Release build uses your real settings file (`%APPDATA%\TakeABreakGoose\settings.json`), and only
runs when the installed goose isn't. To try one without installing it:

```bash
dotnet publish App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Back up the settings file before testing something that changes settings, and put it back afterwards.

## Tests

```bash
dotnet test tests/TakeABreakGoose.Tests
dotnet test tests/TakeABreakGoose.Windows.Tests
```

- **`TakeABreakGoose.Tests`** (xUnit, `net8.0`) covers the core: the scheduler, the goose's visits
  and behaviour, the nap watch, session plans, the break history and goodbye sentence, calls and Do
  not disturb, settings (including files from older versions), the time texts, and the texts in
  Dutch (`LanguageTests`, which also checks that every text has a Dutch version with the same
  placeholders). It runs on Windows and macOS.
  - The app's language is one setting for everything, so the tests run one at a time. A test that
    needs Dutch uses `using var _ = InLanguage.Dutch();`, which switches back to English afterwards.
  - `SchedulerHarness` gives the scheduler a fake clock and fake idle time: `Work(...)`,
    `BeAway(...)` and `ComeBack()` move time forward and call `Tick()` along the way.
  - `GooseBehaviorTests` has a `GooseHarness` with a fake view, which runs a visit frame by frame
    (`WalkIn()`, `WorkUntil(...)`), records what the goose shows and answers, and clicks its buttons.
- **`TakeABreakGoose.Windows.Tests`** (`net8.0-windows`) covers the app's Windows-only
  parts: starting with Windows (on a scratch registry key, not the real one), which programs count
  as meeting apps, and the shortcut's keys.

What happens on screen (the windows, the goose's walk, the tray, calls) isn't covered by tests:
try it in the app.

## Making a change

- **Behaviour goes in the core** where possible, with a test.
- **The windows and pages are the same on Windows and macOS**. What differs goes behind an
  `OperatingSystem` check: in `App/Platform/` for what the app asks of the computer, and for the
  few texts that name Windows or macOS (`…Windows` and `…Mac` in the resources). Try a change on
  Windows; the macOS build runs in GitHub Actions on every branch.
- **Settings** that are renamed or replaced keep the old property around for reading, so older
  settings files carry over (see [Architecture](architecture.md#settings)).
- **Texts** are written for people who aren't programmers: friendly, short and without jargon. The
  Dutch ones too, with an informal "je".
- The README describes how the app works for users. Keep it in sync with changes in behaviour.

## Adding or changing a text

Every text the app shows is in `Core/Text/Strings.resx` (English) and `Core/Text/Strings.nl.resx`
(Dutch), never typed into the code or the XAML (see [Architecture](architecture.md#languages)).

- **To change a text**, change it in both files. Keep the placeholders (`{0}`, `{1:HH:mm}`) in both;
  the order of the words around them can differ.
- **To add one**, add an entry with the same name to both files (in Visual Studio's resource editor
  or as XML: `<data name="..." xml:space="preserve"><value>...</value></data>`), with a `<comment>`
  in the English file when it has placeholders. Build, and it's there as `Strings.TheName`:
  - In code: `Strings.TheName`, or `string.Format(Strings.TheName, value)`.
  - In XAML: `Text="{x:Static s:Strings.TheName}"`, with `xmlns:s` on the root element as in the
    other pages. Both apps' window usually needs it.
- **Numbers**: a text for one and one for more (`…One` and `…Many`), rather than adding an "s".
- **Lists** (the goose's lines, the number words, the default messages) are one text with an item
  per line. The default messages must keep the same number of lines, in the same order, in both
  languages: that's how a message that's still a default switches along with the language.
- `LanguageTests` fails when a text is missing in Dutch or its placeholders don't match.

## Making a release

1. Set the new version in `Directory.Build.props`.
2. Write `release-notes/vX.Y.Z.md`: what's new for users, followed by the **Install** section
   (copy it from the previous release and change the version numbers).
   - **Start every bullet with a short bold phrase** that says the point on its own, e.g.
     `- **A smaller download**: about a third smaller than before.` After an update the app shows
     what's new in short: the headings, and of each bullet only its bold start (a bullet without
     one is shown whole, a section without bullets by its first sentence, and the Install section
     not at all). See [In-app updates](in-app-updates.md).
   - The Install section, for a release made with Velopack (1.11.0 and later):

     ```markdown
     ## Install

     Already have the goose? It updates itself: look in its menu or at the bottom of the Settings tab.

     On Windows: **`TakeABreakGoose-win-Setup.exe`**. No admin rights are needed. Or
     **`TakeABreakGoose-win-Portable.zip`**: unzip it anywhere and run `Take a Break Goose.exe`.
     The app isn't code-signed, so Windows SmartScreen may warn you the first time: click
     **More info**, then **Run anyway**.

     On a Mac (macOS 12 or later): **`TakeABreakGoose-osx-arm64-Setup.pkg`** for a Mac with an
     Apple chip (M1 or later), **`TakeABreakGoose-osx-x64-Setup.pkg`** for an Intel Mac. The
     installer isn't signed by Apple, so macOS blocks it the first time: **System Settings →
     Privacy & Security → Open Anyway**.
     ```

3. Commit, then tag and push:

   ```bash
   git tag vX.Y.Z && git push origin main vX.Y.Z
   ```

Pushing a `v*` tag runs `.github/workflows/release.yml`:

1. Runs both test projects.
2. Publishes the app for Windows as one self-contained `.exe`, so users don't need .NET installed.
3. Packs it with [Velopack](https://velopack.io) (`vpk`): an installer (`TakeABreakGoose-win-Setup.exe`)
   that installs per user into `%LOCALAPPDATA%\TakeABreakGoose` without admin rights, a portable
   `.zip`, and the update package, with a small delta update from the previous release when there
   is one.
4. Calls `.github/workflows/mac-build.yml`, which runs the core tests on macOS, builds the ad hoc
   signed app for Apple Silicon and for Intel Macs with `App/package.sh`, packs each with Velopack
   (a `.pkg` and the update), and checks that the packed app is validly signed
   and keeps running.
5. Once all of that worked, creates the GitHub release with everything in it, and
   `release-notes/vX.Y.Z.md` as its description. The app reads the descriptions too, for the
   versions it skips when updating, so they have to stay the notes. When a build fails, nothing is
   published.

Running apps find the new release by themselves and offer to update (see [In-app updates](in-app-updates.md)).

**After every release, check the README download buttons.** They link straight to the installers
of the latest release (`releases/latest/download/TakeABreakGoose-win-Setup.exe` and
`…/TakeABreakGoose-osx-arm64-Setup.pkg`), so they follow each new release by themselves as long as
the file names stay the same. Once the release is out, make sure both still download, and if an
installer got a new name, update the button links in `README.md` (and the file names in its
Download section) in the same go:

```bash
for f in TakeABreakGoose-win-Setup.exe TakeABreakGoose-osx-arm64-Setup.pkg; do
  curl -sIL -o /dev/null -w "%{http_code} $f\n" https://github.com/rickhellenthal/takeabreakgoose/releases/latest/download/$f
done
```

**To try a release first**, tag it with a dash: `v1.11.0-beta.1` becomes a pre-release. Normal
releases never update to a pre-release, but a pre-release updates to newer pre-releases and
releases, so installing one is a way to test the whole update on a real computer.
Pre-releases of the same version are ordered by the word after the dash, alphabetically, then by
number: `1.11.0-alpha.2` < `1.11.0-beta.1` < `1.11.0-rc.1` < `1.11.0`. So keep to one word per
version, or pick a later one: a `beta` after a `spike` counts as older, and Velopack refuses to
pack it.

Pushing to any other branch than `main` also runs the Mac build, so a branch's Mac installers can
be downloaded from the run's Artifacts.

## Other tools

- `tools/make-icon.ps1` draws the app icon from vector paths and writes `Assets/goose.ico`,
  `App/Assets/goose.icns` and `docs/icon.png`, and the Windows installer's splash image
  `Assets/splash.png` (the goose's badge and name, shown with a progress bar while installing):

  ```bash
  powershell -File tools/make-icon.ps1
  ```
