using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace TakeABreakGoose;

/// <summary>The Settings tab, with its own Save bar and a check for unsaved changes.</summary>
public partial class SettingsPage : UserControl
{
    private IGooseHost _host = null!;
    private HotkeyModifiers _hotkeyModifiers;
    private string _hotkeyKey = "";
    private string _savedForm = "";
    private AvailableUpdate? _update;


    public SettingsPage()
    {
        InitializeComponent();
        // Before the text box gets the key, like WPF's PreviewKeyDown.
        HotkeyBox.AddHandler(KeyDownEvent, HotkeyBox_PreviewKeyDown, RoutingStrategies.Tunnel);
        // The XAML has the Mac's words for these.
        if (OperatingSystem.IsWindows())
        {
            StartAtLoginCheck.Content = Strings.SettingsStartWithWindows;
            StartAtLoginHint.Text = Strings.SettingsStartWithWindowsHint;
            AutoStartHint.Text = Strings.SettingsAutoStartHintWindows;
            ToolTip.SetTip(HotkeyBox, Strings.SettingsShortcutToolTipWindows);
        }
    }

    public void Initialize(IGooseHost host)
    {
        _host = host;
        foreach (var language in Languages.All)
        {
            var choice = new RadioButton { Content = language.Name, Tag = language.Code, GroupName = "Language" };
            choice.Classes.Add("SmallChoiceChip");
            LanguageChoices.Children.Add(choice);
        }
        LoadForm(host.Settings);
        VersionText.Text = $"Take a Break Goose {Updater.CurrentVersionName ?? UpdateChecker.Display(UpdateChecker.CurrentVersion)}";
    }

    public void ScrollToTop() => Scroller.ScrollToHome();

    public void ClearStatus() => SettingsStatus.Text = "";

    private void LoadForm(GooseSettings settings)
    {
        AutoStartCheck.IsChecked = settings.AutoStartSession;
        AutoStartIntervalBox.Text = settings.AutoStartIntervalMinutes.ToString();
        AllowSnoozeCheck.IsChecked = settings.AllowSnooze;
        SnoozeBox.Text = settings.SnoozeMinutes.ToString();
        StartAtLoginCheck.IsChecked = StartAtLogin.IsEnabled;
        // Older macOS, or the app run from a build folder rather than its .app: nothing to turn on.
        StartAtLoginCheck.IsVisible = StartAtLoginHint.IsVisible = StartAtLogin.IsAvailable;
        CallDoNotDisturbCheck.IsChecked = settings.DoNotDisturbDuringCalls;
        _hotkeyModifiers = settings.HotkeyModifiers;
        _hotkeyKey = settings.HotkeyKey;
        HotkeyBox.Text = HotkeyText.Format(settings.HotkeyModifiers, settings.HotkeyKey);
        foreach (var choice in LanguageChoices.Children.OfType<RadioButton>())
            choice.IsChecked = (string?)choice.Tag == settings.Language;
        ShowMessageCount(settings.Messages.Count);
        _savedForm = FormState();
    }

    private void HotkeyBox_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // Tab and Shift+Tab move on to the next control as usual.
        if (e.Key == Key.Tab) return;
        e.Handled = true;
        // Command is LWin/RWin.
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        KeyModifiers modifiers = e.KeyModifiers;
        if (OperatingSystem.IsMacOS())
        {
            // Option alone isn't enough: it types special characters, and since macOS 15 a shortcut with
            // only Option can't be registered.
            if ((modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0)
            {
                ShowStatus(Strings.SettingsShortcutNeedsModifierMac, error: true);
                return;
            }
        }
        else if ((modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0)
        {
            ShowStatus(Strings.SettingsShortcutNeedsModifierWindows, error: true);
            return;
        }

        // Avalonia's KeyModifiers have the same values as HotkeyModifiers (Meta, Command on a Mac, is Windows).
        _hotkeyModifiers = (HotkeyModifiers)modifiers;
        _hotkeyKey = e.Key.ToString();
        HotkeyBox.Text = HotkeyText.Format(_hotkeyModifiers, _hotkeyKey);
        SettingsStatus.Text = "";
    }

    // ---- Messages ----

    /// <summary>The user wants to edit the messages, which have a page of their own.</summary>
    public event Action? EditMessagesRequested;

    public void ShowMessageCount(int count) =>
        MessageCountText.Text = string.Format(count == 1 ? Strings.MessageCountOne : Strings.MessageCountMany, count);

    private void EditMessages_Click(object? sender, RoutedEventArgs e) => EditMessagesRequested?.Invoke();

    // ---- Saving ----

    private void SaveSettings_Click(object? sender, RoutedEventArgs e) => TrySave();

    /// <summary>Validates and saves the settings form. Returns false (and shows why) if it can't.</summary>
    private bool TrySave()
    {
        Action<string> fail = message => ShowStatus(message, error: true);
        if (!Ui.TryParseNumber(AutoStartIntervalBox.Text, 1, 24 * 60, Strings.FieldAutoStartInterval, out int autoStartInterval, fail)) return false;
        // A greyed-out box can't be fixed, so it only needs to make sense while snoozing is on.
        int snooze = _host.Settings.SnoozeMinutes;
        if (AllowSnoozeCheck.IsChecked == true
            && !Ui.TryParseNumber(SnoozeBox.Text, 1, 24 * 60, Strings.FieldSnoozeLength, out snooze, fail)) return false;

        var settings = _host.Settings.Clone();
        settings.AutoStartSession = AutoStartCheck.IsChecked == true;
        settings.AutoStartIntervalMinutes = autoStartInterval;
        settings.AllowSnooze = AllowSnoozeCheck.IsChecked == true;
        settings.SnoozeMinutes = snooze;
        settings.DoNotDisturbDuringCalls = CallDoNotDisturbCheck.IsChecked == true;
        settings.HotkeyModifiers = _hotkeyModifiers;
        settings.HotkeyKey = _hotkeyKey;
        settings.Language = SelectedLanguage;

        string? error = _host.ApplySettings(settings, StartAtLoginCheck.IsChecked == true);
        if (error != null)
        {
            fail(error);
            return false;
        }
        ShowSaved();
        _savedForm = FormState();
        return true;
    }

    public void ShowSaved() => ShowStatus(Strings.Saved, error: false);

    private string SelectedLanguage =>
        LanguageChoices.Children.OfType<RadioButton>().FirstOrDefault(c => c.IsChecked == true)?.Tag as string
        ?? _host.Settings.Language;

    private void ShowStatus(string message, bool error)
    {
        SettingsStatus.Text = message;
        SettingsStatus.Foreground = error ? Brushes.Firebrick : Brushes.SeaGreen;
    }

    // ---- Unsaved changes ----

    /// <summary>Everything the form shows, to tell whether anything changed since loading or saving.</summary>
    private string FormState() => string.Join("\u001f",
        AutoStartCheck.IsChecked, AutoStartIntervalBox.Text, AllowSnoozeCheck.IsChecked, SnoozeBox.Text,
        StartAtLoginCheck.IsChecked, CallDoNotDisturbCheck.IsChecked, HotkeyBox.Text, SelectedLanguage);

    public bool HasUnsavedChanges => FormState() != _savedForm;

    /// <summary>
    /// Asks what to do with unsaved changes. Returns true when it's fine to move on
    /// (nothing changed, saved or discarded), false to stay in Settings.
    /// </summary>
    public async Task<bool> ResolveUnsavedChangesAsync()
    {
        if (!HasUnsavedChanges) return true;
        switch (await UnsavedChangesDialog.AskAsync((Window)TopLevel.GetTopLevel(this)!))
        {
            case UnsavedChangesChoice.Save:
                return TrySave();
            case UnsavedChangesChoice.Discard:
                LoadForm(_host.Settings);
                SettingsStatus.Text = "";
                return true;
            default:
                return false;
        }
    }

    // ---- Updates ----

    public void ShowUpdate(AvailableUpdate update)
    {
        _update = update;
        UpdateRun.Text = string.Format(Strings.SettingsUpdateAvailable, update.DisplayName);
        // Installed with Velopack: it updates itself. Otherwise: a link to the release page.
        DownloadUpdateButton.Content = Updater.CanUpdate ? Strings.SettingsUpdateNow : Strings.SettingsDownload;
        UpdateText.IsVisible = true;
    }

    private async void DownloadUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (_update == null) return;
        if (!Updater.CanUpdate)
        {
            UpdateChecker.OpenInBrowser(_update.Url);
            return;
        }
        // Restarting the app takes the goose off the screen and forgets the break.
        if (!UpdateMoment.IsGood(_host.Scheduler, _host.GooseOnScreen, _host.DoNotDisturbForCall))
        {
            ShowStatus(Strings.SettingsUpdateNotNow, error: false);
            return;
        }
        DownloadUpdateButton.IsEnabled = false;
        ShowStatus(Strings.SettingsUpdating, error: false);
        try
        {
            await Updater.DownloadAndRestartAsync(); // quits and starts the new version
        }
        catch (Exception ex)
        {
            ShowStatus(string.Format(Strings.SettingsUpdateFailed, ex.Message), error: true);
            DownloadUpdateButton.IsEnabled = true;
        }
    }

    private const string KoFiUrl = "https://ko-fi.com/rickhellenthal";

    private void KoFi_Click(object? sender, RoutedEventArgs e) => UpdateChecker.OpenInBrowser(KoFiUrl);
}
