# Take a Break Goose

A small Windows 11 tray app. A goose walks onto your screen, on top of all your windows, and tells you to take a break so you don't burn out from hyperfocus.

## Download

Get the latest version from the [Releases page](https://github.com/rickhellenthal/takeabreakgoose/releases/latest):

- **`TakeABreakGoose-Setup-x.y.z.exe`** installs the app for your user account (no admin rights needed). It adds a Start menu shortcut and, if you want, a desktop shortcut and "start with Windows". You can uninstall it from *Settings → Apps → Installed apps*.
- **`TakeABreakGoose-x.y.z-portable.zip`** contains just the exe. Unzip it anywhere and run it.

Both include everything the app needs, so you don't have to install .NET.

> The app isn't code-signed, so Windows SmartScreen may say *"Windows protected your PC"* the first time. Click **More info**, then **Run anyway**.

## How it works

Open the app with the global shortcut (default **Ctrl+Alt+G**), the desktop shortcut, or by clicking the tray icon. The window has three tabs.

**Focus session**
- Choose how often you want a break reminder. You can also set when the session should end.
- While a session runs, the page shows a countdown to your next break and when the session ends. You can pause the session, stop it, or take a break right away.
- When a session ends, the goose comes to tell you it's done.

**Wardrobe**
- Shows the outfits the goose picks from on each visit: a raincoat, a bonnet, a bandana and a party hat.
- Click **Send this goose** to have the goose come over in that outfit.

**Settings**
- **Always on:** the goose also visits outside focus sessions, at its own interval. This is off by default.
- **Break length:** the next reminder starts counting after your break.
- **Snooze length**, and how long the goose waits for an answer before it leaves.
- **Away counts as a break:** if you don't touch the mouse or keyboard for a few minutes, that time counts as a break, and the countdown starts fresh when you come back.
- **Start with Windows:** turn this on to launch the app when you sign in. It then starts quietly in the tray.
- **Shortcut and messages:** change the global shortcut and the list of messages the goose picks from.

The goose comes in from the left or right edge, stops on that same side of the screen, and leaves the way it came.

When it asks you to take a break, you can also choose **Let me finish up**. The goose then walks back to the edge and peeks around it while you save your work. It leans out further when your mouse comes near. Click it when you're ready, and it comes back with the snooze and break options.

When you take the break, the goose sits down and naps on the taskbar until the break is over. A small card above it shows a ring that fills up as the break goes on, and how much time is left. When the break ends, the goose wakes up, welcomes you back and walks off.

Settings are saved to `%APPDATA%\TakeABreakGoose\settings.json`.

## Build & run

Needs the .NET 8 SDK.

```bash
dotnet run
```

To make the goose appear right away when the app starts (handy for testing):

```bash
dotnet run -- --summon
```

To publish the exe (the desktop shortcut points to `publish\TakeABreakGoose.exe`):

```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

The app icon (`goose.ico`) is generated from a vector drawing by `tools\make-icon.ps1`.

## Releasing a new version

Push a version tag, and the [Release workflow](.github/workflows/release.yml) builds the installer and the portable zip, then publishes them as a GitHub release:

```bash
git tag v1.0.0
```

```bash
git push origin v1.0.0
```

To give the release a description, add `release-notes\<tag>.md` (for example `release-notes\v1.1.0.md`) before pushing the tag. Without it, the release lists the commits since the previous version.

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php) from `installer\TakeABreakGoose.iss`.
