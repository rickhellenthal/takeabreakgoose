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
    private bool _gooseJustVisiting;
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
        TrySave(); // writes back settings carried over from older versions

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

        if (Settings.AutoStartSession)
            Scheduler.StartSession(Settings.AutoStartIntervalMinutes, endsAt: null, automatically: true);
        UpdateTray();

        _hotkey = new HotkeyManager();
        _hotkey.Pressed += ShowMainWindow;
        bool hotkeyOk = _hotkey.Register(Settings.HotkeyModifiers, Settings.HotkeyKey);

        if (Array.IndexOf(e.Args, Autostart.Argument) < 0) ShowMainWindow();

        if (!hotkeyOk)
            _tray.ShowBalloon($"{Settings.HotkeyText} is already used by another program. Open the goose from this tray icon to pick another shortcut.");
        else if (Array.IndexOf(e.Args, Autostart.Argument) >= 0)
            _tray.ShowBalloon(Settings.AutoStartSession
                ? $"Focus session started. The goose will remind you to take a break every {Settings.AutoStartIntervalMinutes} min."
                : $"The goose is here. Press {Settings.HotkeyText} to start a focus session.");

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

    public void StartSession(SessionPlan plan)
    {
        Settings.LastSessionIntervalMinutes = plan.IntervalMinutes;
        Settings.LastSessionEndMode = plan.EndMode;
        Settings.LastSessionLengthMinutes = plan.LengthMinutes;
        Settings.LastSessionEndTime = plan.EndTime;
        TrySave();

        Scheduler.StartSession(plan.IntervalMinutes, plan.EndsAt(DateTime.Now));
    }

    public void SummonBreakGoose()
    {
        string message = Settings.Messages[Random.Shared.Next(Settings.Messages.Count)];
        string? hint = Settings.BreakMinutes > 0
            ? $"Take {Settings.BreakMinutes} minutes. I'll start counting again after that."
            : null;
        SummonGoose(new GooseVisit("HONK!", message, hint, "Okay, break time!",
            AllowSnooze: true, AllowFinishUp: true, StartsBreak: true));
    }

    public bool ShowOffOutfit(GooseOutfit outfit)
    {
        if (_goose != null) return false;
        SummonGoose(new GooseVisit("Ta-da!", GooseOutfits.ShowOffLine(outfit), null, "Looking good!",
            AllowSnooze: false, AllowFinishUp: false, StartsBreak: false, outfit, JustVisiting: true));
        return true;
    }

    private void SummonGoose(GooseVisit visit)
    {
        if (_goose != null)
        {
            // Only one goose at a time. A reminder shouldn't get lost behind a goose that's just
            // showing off, and "session complete" shouldn't get lost behind anything, so those come
            // as soon as the current goose has left.
            if (_gooseJustVisiting || !visit.StartsBreak) _pendingVisit ??= visit;
            return;
        }

        bool naps = visit.StartsBreak && Settings.BreakMinutes > 0;
        _gooseJustVisiting = visit.JustVisiting;
        _goose = new GooseWindow(visit, Settings.StaySeconds, Settings.SnoozeMinutes,
            naps ? TimeSpan.FromMinutes(Settings.BreakMinutes) : null);
        if (!visit.JustVisiting)
        {
            _goose.Answered += outcome =>
                Scheduler.GooseAnswered(outcome, startsBreak: naps && outcome == GooseOutcome.Dismissed);
            _goose.BreakExtended += Scheduler.ExtendBreak;
            _goose.ReadyToFocus += Scheduler.ReadyToFocus;
            Scheduler.GooseArrived();
        }
        _goose.Finished += () =>
        {
            _goose = null;
            if (_pendingVisit is { } pending)
            {
                _pendingVisit = null;
                SummonGoose(pending);
            }
        };
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

        bool autoStartTurnedOn = newSettings.AutoStartSession && !Settings.AutoStartSession;
        Settings = newSettings;
        Scheduler.ApplySettings(Settings);
        // Turning it on shouldn't mean waiting for the next restart to see it work.
        if (autoStartTurnedOn && Scheduler.Session == null)
            Scheduler.StartSession(Settings.AutoStartIntervalMinutes, endsAt: null, automatically: true);

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
