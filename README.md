# Take a Break Goose

A small Windows 11 tray app. A goose walks onto your screen, on top of all your windows, and tells you to take a break so you don't burn out from hyperfocus.

## How it works

Open the app with the global shortcut (default **Ctrl+Alt+G**), the desktop shortcut, or by clicking the tray icon. The window has two tabs.

**Work session**
- Choose how often you want a break reminder. You can also set when the session should end.
- While a session runs, the page shows a countdown to your next break and when the session ends. You can pause the session, stop it, or take a break right away.
- When a session ends, the goose comes to tell you it's done.

**Settings**
- **Always on:** the goose also visits outside work sessions, at its own interval. This is off by default.
- **Break length:** the next reminder starts counting after your break.
- **Snooze length**, and how long the goose waits for an answer before it leaves.
- **Away counts as a break:** if you don't touch the mouse or keyboard for a few minutes, that time counts as a break, and the countdown starts fresh when you come back.
- **Start with Windows:** turn this on to launch the app when you sign in. It then starts quietly in the tray.
- **Shortcut and messages:** change the global shortcut and the list of messages the goose picks from.

The goose comes in from the left or right edge, stops on that same side of the screen, and leaves the way it came. Settings are saved to `%APPDATA%\TakeABreakGoose\settings.json`.

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
