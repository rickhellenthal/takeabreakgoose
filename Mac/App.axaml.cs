using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace TakeABreakGoose;

/// <summary>
/// Starts the app and wires it together: settings, the scheduler, the goose, the menu bar icon,
/// the global shortcut and the app window, like App.xaml.cs in the Windows app.
/// </summary>
public partial class App : Application, IGooseHost
{
    private MenuBarIcon? _menuBar;
    private HotkeyManager? _hotkey;
    private GooseCoordinator _goose = null!;
    private MainWindow? _mainWindow;
    private DispatcherTimer? _schedulerTick;
    private DispatcherTimer? _callCheck;
    private SettingsStore _settingsStore = null!;

    public GooseSettings Settings { get; private set; } = new();
    public BreakScheduler Scheduler { get; private set; } = null!;

    /// <summary>This copy of the app, which Program made sure is the only one.</summary>
    public static SingleInstance? SingleInstance { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        if (SingleInstance != null) SingleInstance.OpenRequested += ShowMainWindow;
        // Opening the app again from Finder or Launchpad brings this copy forward: show its window.
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            activatable.Activated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Reopen) ShowMainWindow();
            };

        _settingsStore = new SettingsStore(SettingsFolder());
        Settings = _settingsStore.Load();
        Languages.Use(Settings.Language);
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

        _callCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _callCheck.Tick += (_, _) => _goose.CheckForCall(Calls.InProgress());
        _callCheck.Start();
        _goose.CallStarted += ExplainCallsOnce;

        _hotkey = new HotkeyManager();
        _hotkey.Pressed += ShowMainWindow;
        bool hotkeyOk = _hotkey.Register(Settings.HotkeyModifiers, Settings.HotkeyKey);

        desktop.Exit += (_, _) =>
        {
            _callCheck.Stop();
            _schedulerTick.Stop();
            _hotkey.Dispose();
            _menuBar.Dispose();
        };

        // A login item can't be told apart from being opened by hand, so the window always opens;
        // closing it leaves the goose in the menu bar.
        ShowMainWindow();
        if (!hotkeyOk)
            Notice.Show(string.Format(Strings.NoticeShortcutTakenMac, HotkeyText.Mac(Settings.HotkeyModifiers, Settings.HotkeyKey)));
        if (Array.IndexOf(desktop.Args ?? Array.Empty<string>(), "--summon") >= 0) _goose.SummonBreakGoose();
    }

    /// <summary>~/Library/Application Support/TakeABreakGoose, where Mac apps keep their settings.</summary>
    public static string SettingsFolder()
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

    public bool SessionGooseOnScreen => _goose.SessionGooseOnScreen;

    public void SetDoNotDisturb(bool on) => _goose.SetDoNotDisturb(on);

    public bool DoNotDisturbForCall => _goose.DoNotDisturbForCall;

    public string? ApplySettings(GooseSettings newSettings, bool startAtLogin)
    {
        bool hotkeyChanged = newSettings.HotkeyModifiers != Settings.HotkeyModifiers
                             || newSettings.HotkeyKey != Settings.HotkeyKey;
        if (hotkeyChanged && _hotkey != null && !_hotkey.Register(newSettings.HotkeyModifiers, newSettings.HotkeyKey))
        {
            _hotkey.Register(Settings.HotkeyModifiers, Settings.HotkeyKey);
            return string.Format(Strings.ShortcutTakenMac, HotkeyText.Mac(newSettings.HotkeyModifiers, newSettings.HotkeyKey));
        }

        bool languageChanged = newSettings.Language != Settings.Language;
        if (languageChanged) newSettings.TranslateDefaultMessages();
        bool autoStartTurnedOn = newSettings.AutoStartSession && !Settings.AutoStartSession;
        Settings = newSettings;
        Scheduler.ApplySettings(Settings);
        // Turning it on shouldn't mean waiting for the next restart to see it work.
        if (autoStartTurnedOn && Scheduler.Session == null)
            Scheduler.StartSession(Settings.AutoStartIntervalMinutes, TimeSpan.FromSeconds(Settings.LastSessionBreakSeconds),
                endsAt: null, automatically: true);
        if (languageChanged)
        {
            Languages.Use(Settings.Language);
            UpdateMenuBar();
            // Once the Settings page is done saving, its window makes way for one in the new language.
            Dispatcher.UIThread.Post(ReopenMainWindow);
        }

        try
        {
            if (startAtLogin != LoginItem.IsEnabled) LoginItem.SetEnabled(startAtLogin);
            _settingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            return string.Format(Strings.SettingsNotSaved, ex.Message);
        }
        return null;
    }

    // ---- Helpers ----

    private void ShowMainWindow()
    {
        if (_mainWindow == null) OpenMainWindow(new MainWindow(this));
        if (_mainWindow!.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void OpenMainWindow(MainWindow window)
    {
        _mainWindow = window;
        window.Closed += (_, _) =>
        {
            if (_mainWindow == window) _mainWindow = null;
        };
        window.Show();
    }

    /// <summary>
    /// Swaps the app window for a new one in the same place, after the language changed: the window
    /// shows its texts in the language it was opened in. It opens on Settings, where the language was changed.
    /// </summary>
    private void ReopenMainWindow()
    {
        if (_mainWindow is not { } old) return;
        var window = new MainWindow(this)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = old.Position,
            Width = old.Width,
            Height = old.Height,
        };
        window.ShowSavedSettings();
        OpenMainWindow(window);
        old.Close();
    }

    /// <summary>The first time a call keeps the goose away, says so, so a skipped break isn't a mystery.</summary>
    private void ExplainCallsOnce()
    {
        if (Settings.CallNoticeShown) return;
        Settings.CallNoticeShown = true;
        TrySave();
        Notice.Show(Strings.NoticeInACall);
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
            _goose.DoNotDisturbForCall, Scheduler.Session != null, Settings.HotkeyModifiers, Settings.HotkeyKey);
}
