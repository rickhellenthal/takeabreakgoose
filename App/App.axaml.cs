using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace TakeABreakGoose;

/// <summary>
/// Starts the app and wires it together: settings, the scheduler, the goose, the tray or menu bar
/// icon, the global shortcut and the app window.
/// </summary>
public partial class App : Application, IGooseHost
{
    private StatusIcon? _statusIcon;
    private HotkeyManager? _hotkey;
    private GooseCoordinator _goose = null!;
    private MainWindow? _mainWindow;
    private DispatcherTimer? _schedulerTick;
    private DispatcherTimer? _callCheck;
    private SettingsStore _settingsStore = null!;
    private bool _whatsNewPending;
    private DispatcherTimer? _updateCheck;
    private DateTime _startedAt;
    private DateTime? _lastUpdateCheck;
    private bool _checkingForUpdate;

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
        // On a Mac, opening the app again from Finder or Launchpad brings this copy forward: show its window.
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            activatable.Activated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Reopen) ShowMainWindow();
            };

        _settingsStore = new SettingsStore(SettingsFolder());
        bool hadSettings = _settingsStore.Exists;
        Settings = _settingsStore.Load();
        Languages.Use(Settings.Language);
        // The first time a new version runs, it says what's new once its window is open. Only for
        // an app that Velopack installed, which brings the release notes along.
        if (Updater.CurrentVersionName is { } version)
        {
            _whatsNewPending = WhatsNew.ShouldShow(Settings.LastRunVersion, version, hadSettings);
            Settings.LastRunVersion = version;
        }
        if (Settings.RestoreStartAtLogin) RestoreStartAtLogin();
        TrySave(); // writes back settings carried over from older versions, and the version

        Scheduler = new BreakScheduler(Settings, SystemClock.Instance, UserActivity.Instance);
        _schedulerTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _schedulerTick.Tick += (_, _) => Scheduler.Tick();
        _schedulerTick.Start();
        Scheduler.StateChanged += UpdateStatusIcon;
        _goose = new GooseCoordinator(Scheduler, () => Settings,
            (visit, snoozeMinutes, breakLength) => new GooseWindow(visit, snoozeMinutes, breakLength));

        var statusIcon = _statusIcon = new StatusIcon();
        statusIcon.OpenRequested += ShowMainWindow;
        statusIcon.SummonRequested += _goose.SummonBreakGoose;
        statusIcon.StopSessionRequested += _goose.StopSession;
        statusIcon.DoNotDisturbToggled += () => _goose.SetDoNotDisturb(!Scheduler.DoNotDisturb);
        statusIcon.QuitRequested += () => desktop.Shutdown();
        statusIcon.UpdateRequested += () =>
        {
            ShowMainWindow();
            _mainWindow?.InstallUpdate();
        };

        // After an update, the session that was running carries on; otherwise maybe a new one.
        if (!TryResumeSession() && Settings.AutoStartSession)
            Scheduler.StartSession(Settings.AutoStartIntervalMinutes, TimeSpan.FromSeconds(Settings.LastSessionBreakSeconds),
                endsAt: null, automatically: true);
        UpdateStatusIcon();

        Calls.Start();
        _callCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _callCheck.Tick += (_, _) => _goose.CheckForCall(Calls.InProgress());
        _callCheck.Start();
        _goose.CallStarted += ExplainCallsOnce;

        _startedAt = DateTime.Now;
        _updateCheck = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _updateCheck.Tick += (_, _) =>
        {
            if (UpdateSchedule.IsDue(DateTime.Now, _startedAt, _lastUpdateCheck)) CheckForUpdate();
        };
        _updateCheck.Start();

        _hotkey = new HotkeyManager();
        _hotkey.Pressed += ShowMainWindow;
        bool hotkeyOk = _hotkey.Register(Settings.HotkeyModifiers, Settings.HotkeyKey);

        desktop.Exit += (_, _) =>
        {
            _callCheck.Stop();
            _schedulerTick.Stop();
            _updateCheck.Stop();
            Calls.Stop();
            _hotkey.Dispose();
            statusIcon.Dispose();
        };

        // Started with Windows: stays in the tray, and says so. (A Mac login item can't be told apart
        // from being opened by hand, so there the window always opens.) Closing the window leaves the
        // goose in the tray or menu bar.
        string[] args = desktop.Args ?? Array.Empty<string>();
        bool atLogin = (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) && Array.IndexOf(args, "--autostart") >= 0;
        if (!atLogin) ShowMainWindow();
        if (!hotkeyOk)
            statusIcon.ShowNotice(string.Format(
                OperatingSystem.IsMacOS() ? Strings.NoticeShortcutTakenMac : Strings.NoticeShortcutTakenWindows,
                HotkeyText.Format(Settings.HotkeyModifiers, Settings.HotkeyKey)));
        else if (atLogin)
            statusIcon.ShowNotice(Settings.AutoStartSession
                ? string.Format(Strings.NoticeSessionStarted, Settings.AutoStartIntervalMinutes)
                : string.Format(Strings.NoticeGooseIsHere, HotkeyText.Format(Settings.HotkeyModifiers, Settings.HotkeyKey)));
        if (Array.IndexOf(args, "--summon") >= 0) _goose.SummonBreakGoose();
    }

    /// <summary>
    /// Where the settings are: %APPDATA%\TakeABreakGoose on Windows (TakeABreakGoose.Debug for a Debug
    /// build), ~/Library/Application Support/TakeABreakGoose on a Mac, ~/.config/TakeABreakGoose on Linux.
    /// </summary>
    public static string SettingsFolder()
    {
        if (OperatingSystem.IsMacOS())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "TakeABreakGoose");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.Name);
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

    public AvailableUpdate? AvailableUpdate { get; private set; }

    public event Action? UpdateFound;

    public void CheckForUpdateSoon()
    {
        if (UpdateSchedule.IsDueOnOpen(DateTime.Now, _lastUpdateCheck)) CheckForUpdate();
    }

    public void PrepareForUpdate()
    {
        Settings.RestoreStartAtLogin = OperatingSystem.IsMacOS() && StartAtLogin.IsEnabled;
        TrySave();
        try
        {
            if (Scheduler.Snapshot() is { } snapshot) File.WriteAllText(SessionFile, snapshot.ToJson());
        }
        catch (Exception)
        {
            // Then the new version starts without it, as after a normal restart.
        }
    }

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
            return string.Format(OperatingSystem.IsMacOS() ? Strings.ShortcutTakenMac : Strings.ShortcutTakenWindows,
                HotkeyText.Format(newSettings.HotkeyModifiers, newSettings.HotkeyKey));
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
            UpdateStatusIcon();
            // Once the Settings page is done saving, its window makes way for one in the new language.
            Dispatcher.UIThread.Post(ReopenMainWindow);
        }

        try
        {
            if (startAtLogin != StartAtLogin.IsEnabled) StartAtLogin.SetEnabled(startAtLogin);
            _settingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            return string.Format(Strings.SettingsNotSaved, ex.Message);
        }
        return null;
    }

    // ---- Helpers ----

    /// <summary>
    /// Asks GitHub for a newer version: with Velopack when it installed the app, otherwise only to
    /// link to the release page. Quietly finds nothing when offline.
    /// </summary>
    private async void CheckForUpdate()
    {
        if (_checkingForUpdate) return;
        _checkingForUpdate = true;
        _lastUpdateCheck = DateTime.Now;
        var update = Updater.CanUpdate ? await Updater.CheckAsync() : await UpdateChecker.CheckAsync();
        _checkingForUpdate = false;
        if (update == null || update == AvailableUpdate) return;
        AvailableUpdate = update;
        UpdateFound?.Invoke();
        UpdateStatusIcon();
        // Once per new version, a word from the tray or menu bar, for a goose whose window is closed.
        if (Settings.UpdateNoticeShownFor == update.DisplayName) return;
        Settings.UpdateNoticeShownFor = update.DisplayName;
        TrySave();
        _statusIcon?.ShowNotice(string.Format(Strings.NoticeUpdateAvailable, update.DisplayName));
    }

    // The focus session that was running before an update, for the new version to carry on with.
    private static string SessionFile => Path.Combine(SettingsFolder(), "session.json");

    /// <summary>Carries on with the session from before an update, if there is one from just now.</summary>
    private bool TryResumeSession()
    {
        try
        {
            if (!File.Exists(SessionFile)) return false;
            var snapshot = SessionSnapshot.FromJson(File.ReadAllText(SessionFile));
            File.Delete(SessionFile);
            if (snapshot == null || !snapshot.IsFresh(DateTime.Now)) return false;
            Scheduler.Resume(snapshot);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>After an update on a Mac: start at login again if macOS forgot it was on.</summary>
    private void RestoreStartAtLogin()
    {
        Settings.RestoreStartAtLogin = false;
        if (!OperatingSystem.IsMacOS() || !StartAtLogin.IsAvailable || StartAtLogin.IsEnabled) return;
        try
        {
            StartAtLogin.SetEnabled(true);
        }
        catch (Exception)
        {
            // Then it's off, as the Settings tab will show; the user can turn it on again there.
        }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null) OpenMainWindow(new MainWindow(this));
        if (_mainWindow!.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        ShowWhatsNewOnce(_mainWindow);
    }

    /// <summary>After an update, over the app window: what's new in this version.</summary>
    private void ShowWhatsNewOnce(Window owner)
    {
        if (!_whatsNewPending || Updater.CurrentVersionName is not { } version) return;
        _whatsNewPending = false;
        // Once the window is on screen, which it has to be to own a dialog.
        Dispatcher.UIThread.Post(async () =>
        {
            // Handed over by the version before, when it updated from inside the app. Otherwise (installed
            // over an older version, which can't hand them over) this version's notes from GitHub.
            var notes = Updater.TakeReleaseNotes()
                        ?? await Updater.FetchReleaseNotesAsync(version);
            // Nothing to tell, or the window was closed meanwhile: no window.
            if (notes == null || !notes.Versions.Any(v => ReleaseNotes.Highlights(v.Markdown).Count > 0)) return;
            if (!owner.IsVisible) return;
            await WhatsNewWindow.ShowAsync(owner, version, notes);
        });
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
        _statusIcon?.ShowNotice(Strings.NoticeInACall);
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

    private void UpdateStatusIcon() =>
        _statusIcon?.Update(Scheduler.Describe(_goose.DoNotDisturbForCall), Scheduler.DoNotDisturb,
            _goose.DoNotDisturbForCall, Scheduler.Session != null, Settings.HotkeyModifiers, Settings.HotkeyKey,
            AvailableUpdate is { } update ? string.Format(Strings.MenuUpdate, update.DisplayName) : null);
}
