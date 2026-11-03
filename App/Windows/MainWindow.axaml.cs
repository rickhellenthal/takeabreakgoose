using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace TakeABreakGoose;

/// <summary>
/// The app window: a header with the current status, and three tabs (Focus session, Wardrobe,
/// Settings), each a page of its own. Settings opens the messages on a page of their own, with a
/// back button where the tabs are.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IGooseHost _host;
    private RadioButton? _currentTab;
    private bool _revertingTab;
    private bool _closeAnswered;
    private CancellationTokenSource? _updateDownload;

    /// <summary>For the XAML loader and previewer only; the app uses the other constructor.</summary>
    public MainWindow() : this(null!)
    {
    }

    public MainWindow(IGooseHost host)
    {
        InitializeComponent();
        _host = host;
        _currentTab = SessionTab;
        if (host == null) return; // the previewer

        HeaderIcon.Source = UpdateIcon.Source = LargestIconFrame();
        SessionView.Initialize(host);
        WardrobeView.Initialize(host);
        SettingsView.Initialize(host);
        MessagesView.Initialize(host);
        SettingsView.EditMessagesRequested += OpenMessages;
        SettingsView.UpdateRequested += InstallUpdate;
        MessagesView.Saved += SettingsView.ShowMessageCount;
        // What the app already found, and a fresh look unless it just looked.
        Loaded += (_, _) =>
        {
            ShowUpdate();
            host.CheckForUpdateSoon();
        };
        host.UpdateFound += ShowUpdate;
        Closed += (_, _) => host.UpdateFound -= ShowUpdate;

        _host.Scheduler.StateChanged += RefreshStatus;
        Closed += (_, _) => _host.Scheduler.StateChanged -= RefreshStatus;
        RefreshStatus();
    }

    // Skia decodes the largest frame of the .ico.
    private static Bitmap LargestIconFrame() =>
        new(AssetLoader.Open(new Uri("avares://TakeABreakGoose/Assets/goose.ico")));

    private void RefreshStatus()
    {
        HeaderStatus.Text = _host.Scheduler.Describe(_host.DoNotDisturbForCall);
        SessionView.Refresh();
        WardrobeView.Refresh();
    }

    private async void Tab_Checked(object? sender, RoutedEventArgs e)
    {
        // Fires during InitializeComponent, while going back, and for the tab that's unchecked.
        if (SessionView == null || _revertingTab || sender is not RadioButton { IsChecked: true } tab) return;

        // Leaving Settings with unsaved changes: ask first, and stay if they want to keep editing.
        // The dialog doesn't block like WPF's, so Settings is selected again while it asks. The radio
        // group only unchecks Settings after this event, so that waits until the group is done.
        if (_currentTab == SettingsTab && tab != SettingsTab && SettingsView.HasUnsavedChanges)
        {
            await Dispatcher.UIThread.InvokeAsync(() => SelectTabQuietly(SettingsTab));
            if (!await SettingsView.ResolveUnsavedChangesAsync()) return;
            SelectTabQuietly(tab);
        }
        ShowTab(tab);
    }

    /// <summary>Checks a tab without asking about unsaved changes or switching pages.</summary>
    private void SelectTabQuietly(RadioButton tab)
    {
        _revertingTab = true;
        tab.IsChecked = true;
        _revertingTab = false;
    }

    private void ShowTab(RadioButton tab)
    {
        _currentTab = tab;

        // From the tab rather than IsChecked: the tab that's being unchecked still is at this point.
        SessionView.IsVisible = tab == SessionTab;
        WardrobeView.IsVisible = tab == WardrobeTab;
        SettingsView.IsVisible = tab == SettingsTab;
        SettingsView.ClearStatus();
        // Each tab starts at the top, not where it was scrolled to last time.
        SessionView.ScrollToTop();
        WardrobeView.ScrollToTop();
        SettingsView.ScrollToTop();
    }

    private bool MessagesOpen => MessagesView.IsVisible;

    private void OpenMessages()
    {
        MessagesView.Open();
        ShowMessagesPage(true);
    }

    private async void Back_Click(object? sender, RoutedEventArgs e)
    {
        if (await MessagesView.ResolveUnsavedChangesAsync()) ShowMessagesPage(false);
    }

    /// <summary>Swaps Settings for the messages page (and the tabs for a back button), or back.</summary>
    private void ShowMessagesPage(bool show)
    {
        MessagesView.IsVisible = show;
        SettingsView.IsVisible = !show;
        BackButton.IsVisible = show;
        Tabs.IsVisible = !show;
        SettingsView.ClearStatus();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        // Closing the window while an update downloads cancels it, as the Cancel button does.
        _updateDownload?.Cancel();
        if (e.Cancel || _closeAnswered) return;
        // Quitting the app can't wait for an answer.
        if (e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown) return;
        if (!(MessagesOpen && MessagesView.HasUnsavedChanges) && !SettingsView.HasUnsavedChanges) return;

        // The dialogs can't be awaited here: keep the window open, ask, and close it once that's fine.
        e.Cancel = true;
        CloseOnceChangesResolved();
    }

    private async void CloseOnceChangesResolved()
    {
        if (MessagesOpen && !await MessagesView.ResolveUnsavedChangesAsync()) return;
        if (!await SettingsView.ResolveUnsavedChangesAsync())
        {
            // Show the problem, or the changes they want to keep editing.
            if (MessagesOpen) ShowMessagesPage(false);
            SettingsTab.IsChecked = true;
            return;
        }
        _closeAnswered = true;
        Close();
    }

    /// <summary>Shows Settings, saved: for a window that replaces one in another language.</summary>
    public void ShowSavedSettings()
    {
        SettingsTab.IsChecked = true;
        SettingsView.ShowSaved();
    }

    private void ShowUpdate()
    {
        if (_host.AvailableUpdate is not { } update) return;
        SettingsView.ShowUpdate(update);
        SettingsTab.Content = Strings.TabSettingsWithNews; // a small hint that there's something new to see there
    }

    // ---- Installing an update ----

    /// <summary>
    /// Downloads the update with the window covered by a progress card, then the app restarts as the
    /// new version. Not while the goose is busy; unsaved settings are dealt with first.
    /// </summary>
    private async void InstallUpdate()
    {
        // Restarting the app takes the goose off the screen and forgets the break.
        if (!UpdateMoment.IsGood(_host.Scheduler, _host.GooseOnScreen, _host.DoNotDisturbForCall))
        {
            SettingsView.ShowUpdateStatus(Strings.SettingsUpdateNotNow, error: false);
            return;
        }
        if (!await SettingsView.ResolveUnsavedChangesAsync()) return;

        using var download = _updateDownload = new CancellationTokenSource();
        ShowUpdateOverlay(true);
        try
        {
            await Updater.DownloadAndRestartAsync(
                percent => Dispatcher.UIThread.Post(() => ShowUpdateProgress(percent)), _host.PrepareForUpdate, download.Token);
        }
        catch (OperationCanceledException)
        {
            SettingsView.ShowUpdateStatus(Strings.SettingsUpdateCancelled, error: false);
        }
        catch (Exception ex)
        {
            SettingsView.ShowUpdateStatus(string.Format(Strings.SettingsUpdateFailed, ex.Message), error: true);
        }
        // Only gets here when the update didn't go ahead.
        _updateDownload = null;
        ShowUpdateOverlay(false);
    }

    private void ShowUpdateOverlay(bool show)
    {
        UpdateOverlay.IsVisible = show;
        // Also out of reach of the keyboard.
        WindowContent.IsEnabled = !show;
        if (!show) return;
        UpdateHeading.Text = Strings.UpdateDownloadingHeading;
        UpdateHint.Text = Strings.UpdateDownloadingHint;
        CancelUpdateButton.IsEnabled = true;
        ShowUpdateProgress(0);
        CancelUpdateButton.Focus();
    }

    private void ShowUpdateProgress(int percent)
    {
        if (!UpdateOverlay.IsVisible) return;
        UpdateProgress.Value = percent;
        UpdatePercent.Text = $"{percent}%";
        if (percent < 100) return;
        // Downloaded: from here on it can't be stopped, and the app quits in a moment.
        UpdateHeading.Text = Strings.UpdateRestartingHeading;
        UpdateHint.Text = Strings.UpdateRestartingHint;
        CancelUpdateButton.IsEnabled = false;
    }

    private void CancelUpdate_Click(object? sender, RoutedEventArgs e) => _updateDownload?.Cancel();
}
