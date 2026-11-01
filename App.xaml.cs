using System;
using System.Threading;
using System.Windows;

namespace TakeABreakGoose;

public partial class App : Application, IGooseHost
{
    private Mutex? _singleInstance;
    private TrayIcon? _tray;
    private HotkeyManager? _hotkey;
    private GooseWindow? _goose;
    private GooseVisit? _pendingVisit;
    private MainWindow? _mainWindow;

    public GooseSettings Settings { get; private set; } = new();
    public BreakScheduler Scheduler { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\TakeABreakGoose", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Take a Break Goose is already running. Look for the goose in the system tray.",
                "Take a Break Goose", MessageBoxButton.OK, MessageBoxImage.Information);
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        Settings = SettingsStore.Load();

        Scheduler = new BreakScheduler(Settings);
        Scheduler.BreakDue += () => SummonBreakGoose();
        Scheduler.SessionCompleted += () => SummonGoose(new GooseVisit(
            "Session complete!",
            "Your focus session is done. Great job! Time to wrap up and give your brain some rest.",
            null, "Thanks, goose!", AllowSnooze: false, AllowFinishUp: false, StartsBreak: false));
        Scheduler.StateChanged += UpdateTray;

        _tray = new TrayIcon();
        _tray.OpenRequested += ShowMainWindow;
        _tray.SummonRequested += () => SummonBreakGoose();
        _tray.PauseToggled += () => Scheduler.SetPaused(!Scheduler.IsPaused);
        _tray.StopSessionRequested += Scheduler.StopSession;
        _tray.QuitRequested += Shutdown;
        UpdateTray();

        _hotkey = new HotkeyManager();
        _hotkey.Pressed += ShowMainWindow;
        bool hotkeyOk = _hotkey.Register(Settings.HotkeyModifiers, Settings.HotkeyKey);

        if (Array.IndexOf(e.Args, Autostart.Argument) < 0) ShowMainWindow();

        if (!hotkeyOk)
            _tray.ShowBalloon($"{Settings.HotkeyText} is already used by another program. Open the goose from this tray icon to pick another shortcut.");
        else if (Array.IndexOf(e.Args, Autostart.Argument) >= 0)
            _tray.ShowBalloon($"The goose is here. Press {Settings.HotkeyText} to start a focus session.");

        if (Array.IndexOf(e.Args, "--summon") >= 0) SummonBreakGoose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _tray?.Dispose();
        if (_singleInstance != null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    public void StartSession(int intervalMinutes, int? lengthMinutes)
    {
        Settings.LastSessionIntervalMinutes = intervalMinutes;
        Settings.LastSessionHasEnd = lengthMinutes != null;
        if (lengthMinutes != null) Settings.LastSessionLengthMinutes = lengthMinutes.Value;
        TrySave();

        Scheduler.StartSession(intervalMinutes, lengthMinutes);
    }

    public void SummonBreakGoose(GooseOutfit? outfit = null)
    {
        string message = Settings.Messages[Random.Shared.Next(Settings.Messages.Count)];
        string? hint = Settings.BreakMinutes > 0
            ? $"Take {Settings.BreakMinutes} minutes. I'll start counting again after that."
            : null;
        SummonGoose(new GooseVisit("HONK!", message, hint, "Okay, break time!",
            AllowSnooze: true, AllowFinishUp: true, StartsBreak: true, outfit));
    }

    private void SummonGoose(GooseVisit visit)
    {
        if (_goose != null)
        {
            // Only one goose at a time. A message that isn't about a break (like "session complete")
            // shouldn't get lost, though, so it comes as soon as the current goose has left.
            if (!visit.StartsBreak) _pendingVisit ??= visit;
            return;
        }

        bool naps = visit.StartsBreak && Settings.BreakMinutes > 0;
        _goose = new GooseWindow(visit, Settings.StaySeconds, Settings.SnoozeMinutes,
            naps ? TimeSpan.FromMinutes(Settings.BreakMinutes) : null);
        _goose.Answered += outcome =>
            Scheduler.GooseAnswered(outcome, startsBreak: naps && outcome == GooseOutcome.Dismissed);
        _goose.BreakExtended += Scheduler.ExtendBreak;
        _goose.ReadyToFocus += Scheduler.ReadyToFocus;
        _goose.Finished += () =>
        {
            _goose = null;
            if (_pendingVisit is { } pending)
            {
                _pendingVisit = null;
                SummonGoose(pending);
            }
        };
        Scheduler.GooseArrived();
        _goose.Show();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow(this);
            _mainWindow.Closed += (_, _) => _mainWindow = null;
            _mainWindow.Show();
        }
        if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public string? ApplySettings(GooseSettings newSettings, bool startWithWindows)
    {
        bool hotkeyChanged = newSettings.HotkeyModifiers != Settings.HotkeyModifiers
                             || newSettings.HotkeyKey != Settings.HotkeyKey;
        if (hotkeyChanged && _hotkey != null && !_hotkey.Register(newSettings.HotkeyModifiers, newSettings.HotkeyKey))
        {
            _hotkey.Register(Settings.HotkeyModifiers, Settings.HotkeyKey);
            return $"{newSettings.HotkeyText} is already used by another program. Pick a different shortcut.";
        }

        Settings = newSettings;
        Scheduler.ApplySettings(Settings);

        try
        {
            if (startWithWindows != Autostart.IsEnabled) Autostart.SetEnabled(startWithWindows);
            SettingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            return $"Settings were applied but could not be saved: {ex.Message}";
        }
        return null;
    }

    private void TrySave()
    {
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (Exception)
        {
            // Remembering the last session is a convenience; not worth bothering the user about.
        }
    }

    private void UpdateTray() =>
        _tray?.Update(Scheduler.Describe(), Scheduler.IsPaused, Scheduler.Session != null, Settings.HotkeyText);
}
