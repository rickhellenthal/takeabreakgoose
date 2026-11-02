using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace TakeABreakGoose;

/// <summary>
/// Starts the app and wires it together: settings, the scheduler, the goose, the menu bar icon and
/// the app window, like App.xaml.cs in the Windows app. What the Mac app doesn't have yet (the
/// global shortcut, call detection, starting at login, one copy at a time) comes in step 3 of
/// docs/macos-port.md.
/// </summary>
public partial class App : Application, IGooseHost
{
    private MenuBarIcon? _menuBar;
    private GooseCoordinator _goose = null!;
    private MainWindow? _mainWindow;
    private DispatcherTimer? _schedulerTick;
    private SettingsStore _settingsStore = null!;

    public GooseSettings Settings { get; private set; } = new();
    public BreakScheduler Scheduler { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        _settingsStore = new SettingsStore(SettingsFolder());
        Settings = _settingsStore.Load();
        TrySave(); // writes back settings carried over from older versions

        Scheduler = new BreakScheduler(Settings, SystemClock.Instance, UserActivity.Instance);
        _schedulerTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _schedulerTick.Tick += (_, _) => Scheduler.Tick();
        _schedulerTick.Start();
        Scheduler.StateChanged += UpdateMenuBar;
        _goose = new GooseCoordinator(Scheduler, () => Settings,
            (visit, snoozeMinutes, breakLength) => new GooseWindow(visit, snoozeMinutes, breakLength));

        _menuBar = new MenuBarIcon();
        _menuBar.OpenRequested += ShowMainWindow;
        _menuBar.SummonRequested += _goose.SummonBreakGoose;
        _menuBar.StopSessionRequested += _goose.StopSession;
        _menuBar.DoNotDisturbToggled += () => _goose.SetDoNotDisturb(!Scheduler.DoNotDisturb);
        _menuBar.QuitRequested += () => desktop.Shutdown();

        if (Settings.AutoStartSession)
            Scheduler.StartSession(Settings.AutoStartIntervalMinutes, TimeSpan.FromSeconds(Settings.LastSessionBreakSeconds),
                endsAt: null, automatically: true);
        UpdateMenuBar();

        desktop.Exit += (_, _) =>
        {
            _schedulerTick.Stop();
            _menuBar.Dispose();
        };

        ShowMainWindow();
        if (Array.IndexOf(desktop.Args ?? Array.Empty<string>(), "--summon") >= 0) _goose.SummonBreakGoose();
    }

    /// <summary>~/Library/Application Support/TakeABreakGoose, where Mac apps keep their settings.</summary>
    private static string SettingsFolder()
    {
        if (OperatingSystem.IsMacOS())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "TakeABreakGoose");
        }
        // Trying the Mac app out on Windows: not the Windows app's own settings.
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TakeABreakGoose.Mac");
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

    public void SetDoNotDisturb(bool on) => _goose.SetDoNotDisturb(on);

    public bool DoNotDisturbForCall => _goose.DoNotDisturbForCall;

    public string? ApplySettings(GooseSettings newSettings, bool startAtLogin)
    {
        bool autoStartTurnedOn = newSettings.AutoStartSession && !Settings.AutoStartSession;
        Settings = newSettings;
        Scheduler.ApplySettings(Settings);
        // Turning it on shouldn't mean waiting for the next restart to see it work.
        if (autoStartTurnedOn && Scheduler.Session == null)
            Scheduler.StartSession(Settings.AutoStartIntervalMinutes, TimeSpan.FromSeconds(Settings.LastSessionBreakSeconds),
                endsAt: null, automatically: true);

        try
        {
            if (startAtLogin != LoginItem.IsEnabled) LoginItem.SetEnabled(startAtLogin);
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

    private void UpdateMenuBar() =>
        _menuBar?.Update(Scheduler.Describe(_goose.DoNotDisturbForCall), Scheduler.DoNotDisturb,
            _goose.DoNotDisturbForCall, Scheduler.Session != null);
}
