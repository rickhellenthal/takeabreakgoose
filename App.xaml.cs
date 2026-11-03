using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace TakeABreakGoose;

/// <summary>
/// Starts the app and wires it together: settings, the scheduler, the goose, the tray icon,
/// the global shortcut and the app window.
/// </summary>
public partial class App : Application, IGooseHost
{
    private Mutex? _singleInstance;
    private TrayIcon? _tray;
    private HotkeyManager? _hotkey;
    private GooseCoordinator _goose = null!;
    private MainWindow? _mainWindow;
    private DispatcherTimer? _callCheck;
    private DispatcherTimer? _schedulerTick;

    private readonly SettingsStore _settingsStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TakeABreakGoose"));

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

        Settings = _settingsStore.Load();
        TrySave(); // writes back settings carried over from older versions

        Scheduler = new BreakScheduler(Settings, SystemClock.Instance, UserActivity.Instance);
        _schedulerTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _schedulerTick.Tick += (_, _) => Scheduler.Tick();
        _schedulerTick.Start();
        Scheduler.StateChanged += UpdateTray;
        _goose = new GooseCoordinator(Scheduler, () => Settings,
            (visit, snoozeMinutes, breakLength) => new GooseWindow(visit, snoozeMinutes, breakLength));

        _tray = new TrayIcon();
        _tray.OpenRequested += ShowMainWindow;
        _tray.SummonRequested += _goose.SummonBreakGoose;
        _tray.StopSessionRequested += _goose.StopSession;
        _tray.DoNotDisturbToggled += () => _goose.SetDoNotDisturb(!Scheduler.DoNotDisturb);
        _tray.QuitRequested += Shutdown;

        if (Settings.AutoStartSession)
            Scheduler.StartSession(Settings.AutoStartIntervalMinutes, TimeSpan.FromSeconds(Settings.LastSessionBreakSeconds),
                endsAt: null, automatically: true);
        UpdateTray();

        CameraActivity.Start();
        _callCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _callCheck.Tick += (_, _) => _goose.CheckForCall(Calls.InProgress());
        _callCheck.Start();
        _goose.CallStarted += ExplainCallsOnce;

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

        if (Array.IndexOf(e.Args, "--summon") >= 0) _goose.SummonBreakGoose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _callCheck?.Stop();
        _schedulerTick?.Stop();
        CameraActivity.Stop();
        _hotkey?.Dispose();
        _tray?.Dispose();
        if (_singleInstance != null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    // ---- IGooseHost: what the app window can ask for ----

    public void StartSession(SessionPlan plan)
    {
        Settings.LastSessionIntervalMinutes = plan.IntervalMinutes;
        Settings.LastSessionBreakSeconds = (int)plan.BreakLength.TotalSeconds;
        Settings.LastSessionEndMode = plan.EndMode;
        Settings.LastSessionLengthMinutes = plan.LengthMinutes;
        Settings.LastSessionEndTime = plan.EndTime;
        TrySave();

        Scheduler.StartSession(plan.IntervalMinutes, plan.BreakLength, plan.EndsAt(DateTime.Now));
    }

    public void StopSession() => _goose.StopSession();

    public void SummonBreakGoose() => _goose.SummonBreakGoose();

    public bool ShowOffOutfit(GooseOutfit outfit) => _goose.ShowOffOutfit(outfit);

    public bool GooseOnScreen => _goose.GooseOnScreen;

    public bool SessionGooseOnScreen => _goose.SessionGooseOnScreen;

    public void SetDoNotDisturb(bool on) => _goose.SetDoNotDisturb(on);

    public bool DoNotDisturbForCall => _goose.DoNotDisturbForCall;

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
            Scheduler.StartSession(Settings.AutoStartIntervalMinutes, TimeSpan.FromSeconds(Settings.LastSessionBreakSeconds),
                endsAt: null, automatically: true);

        try
        {
            if (startWithWindows != Autostart.IsEnabled) Autostart.SetEnabled(startWithWindows);
            _settingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            return $"Settings were applied but could not be saved: {ex.Message}";
        }
        return null;
    }

    // ---- Helpers ----

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

    /// <summary>The first time a call keeps the goose away, says so, so a skipped break isn't a mystery.</summary>
    private void ExplainCallsOnce()
    {
        if (Settings.CallNoticeShown) return;
        Settings.CallNoticeShown = true;
        TrySave();
        _tray?.ShowBalloon("You're in a call, so the goose stays away until it's over. You can turn this off in Settings.");
    }

    private void TrySave()
    {
        try
        {
            _settingsStore.Save(Settings);
        }
        catch (Exception)
        {
            // Remembering the last session is a convenience; not worth bothering the user about.
        }
    }

    private void UpdateTray() =>
        _tray?.Update(Scheduler.Describe(_goose.DoNotDisturbForCall), Scheduler.DoNotDisturb,
            _goose.DoNotDisturbForCall, Scheduler.Session != null, Settings.HotkeyText);
}
