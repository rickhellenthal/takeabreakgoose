# In-app updates

People install the goose once and update it from inside the app, on Windows and on macOS. On a Mac
that also means no more `xattr -dr com.apple.quarantine …` for every new version. This document
describes how that works, from the release workflow to the restart, and why it's done this way.

## In short

- The app is packed and updated with [Velopack](https://velopack.io) (MIT licensed), with the
  GitHub releases as the source. Releases are made as before, by pushing a tag
  ([Making a release](development.md#making-a-release)).
- The app looks for a newer version now and then, tells the user once, and updates when the user
  says so: never in the middle of a break. It restarts by itself and shows what's new.
- Neither the Windows nor the Mac app is signed by Microsoft or Apple. On Windows nothing changes
  for that (SmartScreen still warns about the installer). On a Mac the installer has to be opened
  with **Open Anyway** once; updates aren't quarantined, so they open without asking.

## Installing

| | Windows | macOS |
|---|---|---|
| Installer | `TakeABreakGoose-win-Setup.exe` | `TakeABreakGoose-osx-arm64-Setup.pkg` (Apple Silicon), `TakeABreakGoose-osx-x64-Setup.pkg` (Intel) |
| Without installing | `TakeABreakGoose-win-Portable.zip` | None: a downloaded `.zip` is quarantined, so the `.pkg` is the better way |
| Installed in | `%LOCALAPPDATA%\TakeABreakGoose`, per user, no admin rights | `/Applications/Take a Break Goose.app` |
| What's there | `current\TakeABreakGoose.exe` (the app, replaced as a whole on every update), `Update.exe`, and `Take a Break Goose.exe` (a small stub for the Start menu shortcut) | the app bundle, with Velopack's `UpdateMac` in `Contents/MacOS` |
| Settings | `%APPDATA%\TakeABreakGoose`, apart from the install | `~/Library/Application Support/TakeABreakGoose` |
| Start at sign-in | The `Run` value points into `current\`, whose path stays the same across updates | `SMAppService`; turned on again after an update if macOS lost it (see below) |
| Uninstalling | *Settings → Apps*; removes the start with Windows entry too (`OnBeforeUninstallFastCallback` in `Program`) | Drag the app to the Trash |

Each platform has its own Velopack channel (`win`, `osx-arm64`, `osx-x64`): the file names carry
it, so all three fit in one GitHub release, and an installed app only looks at its own.

## Releasing

`.github/workflows/release.yml` (on a `v*` tag) and `mac-build.yml` (which it calls):

1. **Windows**: publish the app, fetch the previous release (`vpk download github`, for a small
   delta update) and `vpk pack`.
2. **Mac**, for each architecture: `App/package.sh` builds and signs the app ad hoc, `vpk pack`
   packs it, and a check makes sure the packed app is validly signed and keeps running.
3. **Publishing**, once all three are packed: `gh release create --draft` makes a draft release
   for the tag, with `release-notes/vX.Y.Z.md` as its description; `vpk upload github --merge`
   adds each platform's files to it; then it's published. When a build fails, nothing is published.
   (Letting `vpk upload` create the release itself was refused by GitHub with "Resource not
   accessible by integration", although the workflow may write.)

A tag with a dash (`v1.11.0-beta.1`) becomes a pre-release (`--pre`). A normal release only
updates to normal releases; a pre-release also to newer pre-releases (`Updater` decides from its own
version). That's the way to try a release, updates included, before everyone gets it.

A Mac whose files aren't in a release yet (in the seconds between the uploads) doesn't see an
update for itself and finds it at its next check.

## In the app

| Piece | Where | What it does |
|---|---|---|
| Velopack's hooks | `Program.Main` | `VelopackApp.Build().Run()` first of all: handles install, update and uninstall events |
| Updating | `App/Platform/Updater.cs` | Velopack's `UpdateManager` with `GithubSource`; checks, downloads, saves the release notes, restarts |
| When to check | `UpdateSchedule` (Core) | A minute after starting, every 12 hours, and when the window opens unless it checked within the hour |
| Turned off | `GooseSettings.CheckForUpdates` | **Look for new versions** in Settings: no checks at all when off |
| Telling the user | `App`, `StatusIcon`, `SettingsPage` | **Update now** at the bottom of Settings, **Update to version …** in the tray / menu bar menu, and a notice once per version (`GooseSettings.UpdateNoticeShownFor`) |
| Not now | `UpdateMoment` (Core) | No restart while a goose is on screen, during a break, while a break waits for Do not disturb, or during a call |
| Downloading | `MainWindow.InstallUpdate` | Unsaved settings first; then a card over the window with the progress and **Cancel** (closing the window cancels too) |
| Keeping the session | `SessionSnapshot`, `BreakScheduler.Snapshot` / `Resume` (Core) | Just before the restart, a running focus session goes into `session.json` in the settings folder (the session, the next break, Do not disturb, the breaks so far); the new version carries on with it if it's at most ten minutes old, instead of starting the automatic session |
| After the restart | `WhatsNew`, `ReleaseNotes`, `ReleaseHistory` (Core), `WhatsNewWindow` | What's new, once |
| A copy Velopack didn't install | `UpdateChecker` (Core) | A Debug build, or a version from before 1.11: only a link to the release page |

### What's new

The first time a new version runs (`GooseSettings.LastRunVersion`), it shows what's new over its
window. A new install shows nothing; installing over an older version (whose settings are still
there) counts as an update.

- **Which notes.** Velopack goes straight to the newest version and only brings its notes along.
  So while the update downloads, the app also fetches GitHub's list of releases and keeps the notes
  of every version since the installed one, newest first, at most three (`ReleaseHistory`). With
  more than one, each block says which version it's from; with more than three, the older ones are
  on the release page. If fetching the list fails, only the newest version's notes, from Velopack.
- **Handing them over.** Velopack 1.2 doesn't let the new version read its own notes, so the old
  version saves them as `whats-new.json` in the settings folder just before restarting, and the new
  one reads and deletes it. Without them (installed over 1.10.0, which can't hand them over), the
  new version fetches its own release's notes from GitHub; without any notes at all, offline or
  with update checks off, it shows nothing.
- **In short** (`ReleaseNotes`): the headings stay, a bullet becomes its bold start, a bullet without
  one stays whole, a section without bullets gets its first sentence, and the Install section is
  left out. So every bullet in the release notes starts with a short bold phrase.
- The release notes are in English, also for someone using the app in Dutch.

## macOS: signing and quarantine

**Signing.** Apple Silicon Macs only run signed code, and an ad hoc signature counts. `package.sh`
signs every file and the bundle ad hoc. Velopack then adds `UpdateMac` and a version file, which
would break the bundle's signature, and without a signing identity it doesn't sign at all. So
`vpk pack` gets `--signAppIdentity "-"` (ad hoc) with `--signDisableDeep`: it only signs what it
added and the bundle around it. Velopack always signs with the hardened runtime, so the app gets
`App/TakeABreakGoose.entitlements`, which lets .NET generate code and load the app's own ad hoc
signed libraries.

**Quarantine.** macOS only marks a file as quarantined when the app that wrote it asks for that
(browsers do; an app with `LSFileQuarantineEnabled` does). The goose doesn't, so the updates it
downloads aren't quarantined, and Gatekeeper, which only checks quarantined apps, lets them open.
The first download is different: the `.pkg` from the browser is quarantined and unsigned, so it
needs **Open Anyway** once. The app the installer puts in `/Applications` normally isn't
quarantined, so the `xattr` command shouldn't be needed any more; the README keeps it as a way out.

**Start at login.** `SMAppService` knows login items by bundle and signature, and an ad hoc
signature changes with every build. In case macOS drops the login item after an update, the old
version notes it was on (`GooseSettings.RestoreStartAtLogin`) and the new one turns it on again.

**Writing to `/Applications`.** When the app's folder isn't writable (a standard, non-admin
account), Velopack asks for an admin password with AppleScript.

## Why Velopack

Before this, the app only showed a link to the new release, and on a Mac every update meant the
`xattr` command again. Two ways to update from inside the app were compared:

- **An updater of our own**: download the same installer or app a person would, check it, and swap
  it in. That keeps the old installers and install folders, but the hard part, replacing the app on
  a Mac, would be ours to write, test without a Mac, and keep working as Apple changes things.
- **Velopack**: replacing, restarting, rolling back a failed update and asking for admin rights are
  done by a library many other apps use, and updates are small deltas. The cost was a new install
  folder on Windows (a one-time reinstall) and a release workflow built around `vpk`.

With hardly any users yet, the one-time reinstall cost next to nothing, and the updater is the one
piece of code an update can't fix, so Velopack won. The risk was that Velopack's Mac support is built
for apps signed with a Developer ID; a trial with an ad hoc signed app (1.11.0-spike.1 and .2)
showed it works: a valid signature, no quarantine, and the app runs, updates included on Windows.

## Moving over from 1.10.0 and older

Those were installed with Inno Setup into `%LOCALAPPDATA%\Programs\Take a Break Goose`, or dragged
from a `.dmg` on a Mac.

- **Windows**: uninstall the old goose first, then install the new one. The settings stay (they're
  in `%APPDATA%`, apart from both). Uninstalling the old one removes its start with Windows entry,
  so that has to be turned on again in Settings. The README and the first release's notes say so.
- **Mac**: the `.pkg` installs over the dragged app, at the same place.
- **Older versions** keep showing "a newer version is available" with the link, so they find the
  new release by themselves.

## Still to check on a real Mac

- The first install from a browser download: the **Open Anyway** step, and whether the installed
  app is free of the quarantine mark (`xattr -l "/Applications/Take a Break Goose.app"`).
- **Update now**: the goose quits and comes back as the new version, with no warning, no App
  Management prompt (macOS 13+ guards apps against being changed by others), and a valid signature
  (`codesign --verify --strict`).
- Start at login: still on after an update, in Settings and in *System Settings → General → Login
  Items*, and after logging out and in. Did a "Login item added" notice appear?
- From a standard (non-admin) account: the password prompt.
