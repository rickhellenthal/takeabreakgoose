# In-app updates

The plan for letting people install the goose once and update it from inside the app, on Windows and
on macOS. On a Mac that also means no more `xattr -dr com.apple.quarantine …` for every new version.

**Status:** a trial (spike) with [Velopack](https://velopack.io) is ready to run: the app can
update itself when Velopack installed it, and `.github/workflows/velopack-spike.yml` publishes test
builds as GitHub pre-releases. Whether Velopack becomes the way the goose is released depends on
how that goes on a real Mac. See [The spike](#the-spike) below.

## How it works today

| | Windows | macOS |
|---|---|---|
| Download | `TakeABreakGoose-Setup-x.y.z.exe` (Inno Setup, ~40 MB) or `…-portable.zip` | `…-mac-apple-silicon.dmg` / `…-mac-intel.dmg` (~50 MB), ad hoc signed |
| Installed in | `%LOCALAPPDATA%\Programs\Take a Break Goose\TakeABreakGoose.exe` (per user, no admin) | wherever the user drags it, normally `/Applications/Take a Break Goose.app` |
| Settings | `%APPDATA%\TakeABreakGoose` | `~/Library/Application Support/TakeABreakGoose` |
| Start at sign-in | `Run` key value `"<exe path>" --autostart` (`Autostart`) | `SMAppService.mainAppService` (`LoginItem`) |
| Update check | `UpdateChecker`: GitHub's `releases/latest`, once per run; Settings shows "a newer version is available" with a link to the release page | same |
| Updating | download the new installer and run it over the old one | download the new `.dmg`, drag it over the old app, run `xattr` again |

## The options

### Velopack

Velopack (the successor of Squirrel, MIT licensed) packs the app with its `vpk` tool into its own
installer and update packages. The app updates itself through `UpdateManager`, with GitHub
Releases as the source.

| | |
|---|---|
| **Windows** | `TakeABreakGoose-win-Setup.exe` installs per user, without admin rights, into `%LOCALAPPDATA%\TakeABreakGoose`: the app in `current\` (replaced as a whole on every update), `Update.exe`, and a small `Take a Break Goose.exe` that starts the app in `current\`, for the Start menu shortcut. |
| **macOS** | `TakeABreakGoose-osx-<arch>-Setup.pkg` installs into `/Applications`; Velopack adds its updater (`UpdateMac`) to the bundle. When the app's folder isn't writable, the updater asks for an admin password with AppleScript. Velopack's docs assume a signed and notarized app; with ad hoc signing we're off its documented path, which is what the spike checks. |
| **Release workflow** | `vpk download github` (the previous release, for small delta updates), `vpk pack` per platform with its own channel (`win`, `osx-arm64`, `osx-x64`), `vpk upload github` into one release. Inno Setup and the `.dmg` go. Packing for macOS has to run on a Mac (the GitHub runner already does). |
| **Downloads** | Per platform a setup (`.exe` / `.pkg`), a portable `.zip`, the update packages (`.nupkg`) and a small `releases.<channel>.json`. The asset names carry the channel, so all three platforms fit in one release. |
| **Start at sign-in** | Windows: the `Run` value points into `current\`, which keeps its path across updates. Mac: see below. |
| **Size of updates** | Delta packages: a small change is a few MB instead of ~40-50 MB. |
| **What we maintain** | The `vpk` steps in the workflow, the Velopack version, and a thin `Updater` in the app (~100 lines). Replacing the app, restarting it, rolling back a failed update and asking for admin rights are Velopack's, shared with every other app that uses it. |

### An updater of our own

The app downloads the same installer or `.app` a person would download by hand, checks its SHA-256
(GitHub publishes one for every release asset) and swaps it in: on Windows by running the Inno
installer silently, on a Mac with `ditto`, `codesign --verify` and a small script that replaces
the bundle once the app has quit.

| | |
|---|---|
| **Release workflow** | Small additions: a zipped `.app` per Mac architecture next to the `.dmg`, and a silent restart entry in the Inno script. |
| **Install location, start at sign-in** | Unchanged on both. |
| **Size of updates** | The full download every time. |
| **What we maintain** | ~500-700 lines: the download and check, the Windows silent install, and the whole Mac swap (the script, the admin-password fallback, App Translocation, the login item), and keeping that working as Apple changes Gatekeeper and App Management over the years. |

### Why Velopack

The first version of this document recommended our own updater, mostly to spare existing users a
reinstall and a move to a new folder. With hardly any users yet, that cost is close to nothing, and
it's cheaper now than it will ever be again. What's left weighs towards Velopack:

- **The updater is the one piece of code an update can't fix.** When it breaks, people are stuck on
  the old version until they notice and reinstall by hand. Our own updater would only be tried by
  us, one developer without a Mac, so its bugs would show up late. Velopack's replacing, restarting
  and rolling back is used by many other apps.
- **The hard part is the Mac swap**, and that's the part we can't test ourselves. With Velopack it's
  maintained by someone else; with our own, it's ours for good.
- **Smaller updates and a simpler app**: the app only has to ask "is there one?" and "install it".

The risk is the one thing Velopack isn't built for: an app signed ad hoc instead of with a
Developer ID. That's what the spike is for. If it fails, our own updater is the fallback, with
the maintenance above taken on knowingly.

## macOS: quarantine and signing

**Quarantine.** macOS only marks a file as quarantined when the app that wrote it asks for that:
browsers do, and so does any app with `LSFileQuarantineEnabled` in its `Info.plist`. Ours doesn't,
and Velopack's updater downloads with its own code, so an update shouldn't be quarantined. Gatekeeper
only checks quarantined apps, so the new version should open without a warning and without `xattr`.

The first install is different: the downloaded `.pkg` is quarantined and unsigned, so macOS blocks
it the first time (*System Settings → Privacy & Security → Open Anyway*, or right-click → **Open**).
The app that the installer puts in `/Applications` normally doesn't carry the quarantine mark, so
`xattr` may not be needed even for the first install. That needs checking on a real Mac.

**Ad hoc signing.** Apple Silicon Macs only need *a* valid signature to run code; an ad hoc one
counts. Velopack adds `UpdateMac` and a version file to the bundle after `package.sh` signed it,
which would break the bundle's signature, and without a signing identity Velopack doesn't sign at
all. So the spike passes `--signAppIdentity "-"` (ad hoc) with `--signDisableDeep`: Velopack only
signs what it added and the bundle around it. Velopack always signs with the hardened runtime, so
the app gets `App/TakeABreakGoose.entitlements`, which lets .NET generate code and load the app's
own ad hoc signed libraries.

**Where it can still go wrong:**

- **App Management** (macOS 13 and later) stops apps from changing other apps' bundles. An app
  replacing its own bundle should be fine, but an ad hoc signed app has no developer ID for macOS to
  match on. If macOS blocks it, it asks to allow it under *Privacy & Security → App Management*.
- **The login item.** `SMAppService` keeps login items by bundle ID and signature, and an ad hoc
  signature changes with every build. After an update macOS might show "Login item added" again, or
  report it as not registered. If so, the app can register it again after an update when it was on.
- **Standard (non-admin) accounts** can't write to `/Applications`; Velopack then asks for an admin
  password.

None of the app's other features need a permission tied to the signature (the shortcut uses
Carbon's `RegisterEventHotKey`, the call check reads device state), so there's nothing else for an
update to reset.

## The spike

### What's in it

- `App/Platform/Updater.cs`: Velopack's `UpdateManager` with GitHub as the source. Only when
  Velopack installed the app; otherwise (a build folder, the Inno install, the old portable `.exe`)
  the app falls back to `UpdateChecker` and its link to the release page. A test build (a version
  like `1.11.0-spike.3`) also looks for newer pre-releases; a normal release only for releases.
- `Program.Main` starts with `VelopackApp.Build().Run()`, which handles Velopack's install, update
  and uninstall events.
- Settings: when an update is found, the link says **Update now**. It downloads the update, quits
  and starts the new version. It refuses while a goose is on screen, during a break, while a break
  waits for Do not disturb, or during a call (`UpdateMoment`, tested in `UpdateMomentTests`), and
  says to try again after the break.
- `.github/workflows/velopack-spike.yml`: on every push to the `spike/velopack` branch, packs the app
  as `1.11.0-spike.<run number>` for Windows, Apple Silicon and Intel, and publishes it as one
  GitHub **pre-release**. The Mac jobs also check the packed app (signature, quarantine, the `.pkg`,
  whether it starts and keeps running) and put the answers on the run's summary page.

Pre-releases don't reach normal users: `UpdateChecker` asks for `releases/latest`, which skips
them, and the release page shows them marked as pre-release.

What's not in the spike yet: checking regularly instead of once per run, the menu item and notice,
keeping the focus session across the restart, and texts for after an update. Those are in
[The experience](#the-experience) below and come with the real thing.

### Results so far

`1.11.0-spike.1` (Apple Silicon, on the GitHub runner):

- `codesign --verify --strict --deep`: valid, including Velopack's `UpdateMac`. Signed ad hoc with
  the hardened runtime (`flags=adhoc,runtime`, no team ID) and the three entitlements.
- No quarantine attributes in the bundle. `spctl` rejects it, as expected without notarization.
- The `.pkg` is unsigned and installs into `/Applications`.
- The app starts and is still running after 20 seconds.
- Sizes: `.pkg` 44.6 MB, portable `.zip` 45.0 MB, update package 46.2 MB (the 1.10.0 `.dmg` was
  49.6 MB). Windows `Setup.exe` is 50.1 MB, against 40.2 MB for the Inno installer.

Still to see on a real Mac: the first install from a browser download, the in-app update, and
the login item.

### Running it

1. Push this branch as `spike/velopack`. The workflow publishes `1.11.0-spike.N`.
2. Read the Mac jobs' summary pages: `codesign --verify` should pass and the app should be running
   after 20 seconds. If not, the problem shows up here, before anyone installs anything.
3. Install that version (Windows: `TakeABreakGoose-win-Setup.exe`; Mac: the `.pkg` for the Mac's
   chip).
4. Push again (any small change) to publish `1.11.0-spike.N+1`.
5. In the installed goose: **Settings**, then **Update now** at the bottom.

On Windows the test build runs next to the Inno-installed goose's settings: both use
`%APPDATA%\TakeABreakGoose`, and only one runs at a time. Quit the normal goose first.

### For the friend with the MacBook

After installing `1.11.0-spike.N`:

1. **Install:** did macOS block the `.pkg`? How did you get past it (Open Anyway, right-click →
   Open)? Did the goose start after the installer finished?
2. In Terminal: `xattr -l "/Applications/Take a Break Goose.app"`. Is there a
   `com.apple.quarantine` line? (None would mean no Terminal command at all, ever.)
3. Turn on **Start at login** in Settings and save.

After the second version is out:

4. **Settings → Update now.** Does the goose quit and come back by itself as `1.11.0-spike.N+1`
   (shown at the bottom of Settings)? Any warning or prompt from macOS, such as App Management or
   a password?
5. `codesign --verify --strict "/Applications/Take a Break Goose.app"`: no output means valid.
6. `xattr -l "/Applications/Take a Break Goose.app"` again: still no quarantine?
7. Is **Start at login** still on in Settings and in *System Settings → General → Login Items*? Log
   out and in: does the goose start? Did a "Login item added" notice appear?
8. Optional: try **Update now** with the network off. The goose should say it didn't work and carry
   on as before.

## The experience

For the real version, after the spike:

**When to check.** At start-up (after a minute, so it doesn't compete with starting the session) and
then every 12 hours while the app runs. Today's check runs once per run, which misses updates on
computers that are never restarted. Offline or anything unexpected still means no news, quietly.

**Telling the user.** No popups. The **Update now** link in Settings (as in the spike), an item in
the tray / menu bar menu (*"Update to 1.11.0"*), and once per new version a notice like the one at
sign-in: *"There's a new goose in town! Version 1.11.0 is ready. Update from the menu whenever it
suits you."* Optionally, later, a setting to install updates by itself at a quiet moment.

**Never at a bad moment.** `UpdateMoment` decides, as in the spike. Rather than refusing, the real
version can wait: *"I'll update as soon as your break is done."*

**Keeping the session.** Just before restarting, the app writes a small `resume.json` next to the
settings with the running session (interval, break length, start, end, next break, the breaks so
far). The new version, started with `--updated`, picks it up if it's less than ten minutes old and
deletes it, then says *"Updated to 1.11.0. Your focus session is still running."*, or without a
session *"Updated to 1.11.0. Honk!"*. The `BreakScheduler` gets a way to resume a session, with tests.

**When it fails.** Velopack leaves the old version in place. The app says so quietly and offers the
release page as the way out.

**Texts.** In `Strings.resx` and `Strings.nl.resx`, friendly and short, the Dutch with an informal
"je". `LanguageTests` checks they're all there.

## Moving over from Inno Setup and the `.dmg`

With hardly any users, a one-time manual step is acceptable:

- **Windows.** The Velopack version installs next to the Inno one, in a different folder. The
  release notes ask to uninstall the old one (*Settings → Apps*) first. The settings are kept: they
  live in `%APPDATA%`, apart from both installs. Uninstalling the Inno version removes the start
  with Windows entry; turning it on again in the goose's Settings fixes that. (The app could also
  detect the Inno install and offer to remove it, but that isn't worth it for a handful of people.)
- **Mac.** The `.pkg` installs over the dragged app at the same place; nothing else to do.
- **Older versions** keep showing "a newer version is available" with the link, so they find the
  new release by themselves.

## Release workflow changes (if the spike works out)

- `release.yml`: replace the Inno Setup and zip steps with `vpk download github`, `vpk pack` and
  `vpk upload github`, as in `velopack-spike.yml`, without `--pre`. Keep `release-notes/vX.Y.Z.md`
  as the release's description (`vpk pack --releaseNotes`, or `gh release edit` afterwards).
- `mac-build.yml`: after `package.sh`, `vpk pack` and `vpk upload github --merge`. `package.sh` can
  stop making the `.dmg`.
- Remove `installer/TakeABreakGoose.iss`, the portable `.exe` zip, and the `AppMutex` remarks in
  `SingleInstance`.
- README install sections: the new file names, and on the Mac the `.pkg` with "Open Anyway"
  instead of dragging and `xattr`.
- `development.md`: the new release steps.

## Work estimate

| Part | Estimate |
|---|---|
| The spike (done): Velopack in the app, Update now, the spike workflow | 0.5 day |
| Running it, reading the results, a round of fixes | 0.5 day, plus waiting for the Mac test |
| Regular checks, menu item, notices, texts in English and Dutch | 0.5 day |
| Keeping the session across the restart (`BreakScheduler`, `resume.json`, tests) | 0.5 day |
| The real release workflow, README and `docs/` | 0.5 day |
| A second round on a real Mac | spread out |

About **2-3 days** in all if the spike works, against 4-5 days for our own updater, which would
also keep needing attention afterwards.
