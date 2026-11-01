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
        Scheduler.BreakDue += SummonBreakGoose;
        Scheduler.SessionCompleted += () => SummonGoose(new GooseVisit(
            "Session complete!",
            "Your work session is done. Great job! Time to wrap up and give your brain some rest.",
            null, "Thanks, goose!", AllowSnooze: false));
        Scheduler.StateChanged += UpdateTray;

        _tray = new TrayIcon();
        _tray.OpenRequested += ShowMainWindow;
        _tray.SummonRequested += SummonBreakGoose;
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
            _tray.ShowBalloon($"The goose is here. Press {Settings.HotkeyText} to start a work session.");

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

    public void SummonBreakGoose()
    {
        string message = Settings.Messages[Random.Shared.Next(Settings.Messages.Count)];
        string? hint = Settings.BreakMinutes > 0
            ? $"Take {Settings.BreakMinutes} minutes. I'll start counting again after that."
            : null;
        SummonGoose(new GooseVisit("HONK!", message, hint, "Okay, break time!", AllowSnooze: true));
    }

    private void SummonGoose(GooseVisit visit)
    {
        if (_goose != null) return;
        _goose = new GooseWindow(visit, Settings.StaySeconds, Settings.SnoozeMinutes);
        _goose.Finished += outcome =>
        {
            _goose = null;
            Scheduler.GooseLeft(outcome);
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
