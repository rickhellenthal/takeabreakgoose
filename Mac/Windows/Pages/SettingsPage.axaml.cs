using System;
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
    }

    public void Initialize(IGooseHost host)
    {
        _host = host;
        LoadForm(host.Settings);
        VersionText.Text = $"Take a Break Goose {UpdateChecker.Display(UpdateChecker.CurrentVersion)}";
    }

    public void ScrollToTop() => Scroller.ScrollToHome();

    public void ClearStatus() => SettingsStatus.Text = "";

    private void LoadForm(GooseSettings settings)
    {
        AutoStartCheck.IsChecked = settings.AutoStartSession;
        AutoStartIntervalBox.Text = settings.AutoStartIntervalMinutes.ToString();
        AllowSnoozeCheck.IsChecked = settings.AllowSnooze;
        SnoozeBox.Text = settings.SnoozeMinutes.ToString();
        StartAtLoginCheck.IsChecked = LoginItem.IsEnabled;
        // Older macOS, or the app run from a build folder rather than its .app: nothing to turn on.
        StartAtLoginCheck.IsVisible = StartAtLoginHint.IsVisible = LoginItem.IsAvailable;
        CallDoNotDisturbCheck.IsChecked = settings.DoNotDisturbDuringCalls;
        _hotkeyModifiers = settings.HotkeyModifiers;
        _hotkeyKey = settings.HotkeyKey;
        HotkeyBox.Text = HotkeyText.Mac(settings.HotkeyModifiers, settings.HotkeyKey);
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
        // Option alone isn't enough: it types special characters, and since macOS 15 a shortcut with
        // only Option can't be registered.
        if ((modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0)
        {
            ShowStatus("The shortcut needs Control or Command, so it doesn't clash with normal typing.", error: true);
            return;
        }

        // Avalonia's KeyModifiers have the same values as HotkeyModifiers (Meta, Command on a Mac, is Windows).
        _hotkeyModifiers = (HotkeyModifiers)modifiers;
        _hotkeyKey = e.Key.ToString();
        HotkeyBox.Text = HotkeyText.Mac(_hotkeyModifiers, _hotkeyKey);
        SettingsStatus.Text = "";
    }

    // ---- Messages ----

    /// <summary>The user wants to edit the messages, which have a page of their own.</summary>
    public event Action? EditMessagesRequested;

    public void ShowMessageCount(int count) =>
        MessageCountText.Text = count == 1 ? "1 message" : $"{count} messages";

    private void EditMessages_Click(object? sender, RoutedEventArgs e) => EditMessagesRequested?.Invoke();

    // ---- Saving ----

    private void SaveSettings_Click(object? sender, RoutedEventArgs e) => TrySave();

    /// <summary>Validates and saves the settings form. Returns false (and shows why) if it can't.</summary>
    private bool TrySave()
    {
        Action<string> fail = message => ShowStatus(message, error: true);
        if (!Ui.TryParseNumber(AutoStartIntervalBox.Text, 1, 24 * 60, "The automatic session interval", out int autoStartInterval, fail)) return false;
        // A greyed-out box can't be fixed, so it only needs to make sense while snoozing is on.
        int snooze = _host.Settings.SnoozeMinutes;
        if (AllowSnoozeCheck.IsChecked == true
            && !Ui.TryParseNumber(SnoozeBox.Text, 1, 24 * 60, "Snooze length", out snooze, fail)) return false;

        var settings = _host.Settings.Clone();
        settings.AutoStartSession = AutoStartCheck.IsChecked == true;
        settings.AutoStartIntervalMinutes = autoStartInterval;
        settings.AllowSnooze = AllowSnoozeCheck.IsChecked == true;
        settings.SnoozeMinutes = snooze;
        settings.DoNotDisturbDuringCalls = CallDoNotDisturbCheck.IsChecked == true;
        settings.HotkeyModifiers = _hotkeyModifiers;
        settings.HotkeyKey = _hotkeyKey;

        string? error = _host.ApplySettings(settings, StartAtLoginCheck.IsChecked == true);
        if (error != null)
        {
            fail(error);
            return false;
        }
        ShowStatus("Saved ✓", error: false);
        _savedForm = FormState();
        return true;
    }

    private void ShowStatus(string message, bool error)
    {
        SettingsStatus.Text = message;
        SettingsStatus.Foreground = error ? Brushes.Firebrick : Brushes.SeaGreen;
    }

    // ---- Unsaved changes ----

    /// <summary>Everything the form shows, to tell whether anything changed since loading or saving.</summary>
    private string FormState() => string.Join("\u001f",
        AutoStartCheck.IsChecked, AutoStartIntervalBox.Text, AllowSnoozeCheck.IsChecked, SnoozeBox.Text,
        StartAtLoginCheck.IsChecked, CallDoNotDisturbCheck.IsChecked, HotkeyBox.Text);

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
        UpdateRun.Text = $"Version {UpdateChecker.Display(update.Version)} is available.";
        UpdateText.IsVisible = true;
    }

    private void DownloadUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (_update != null) UpdateChecker.OpenInBrowser(_update.Url);
    }

    private const string KoFiUrl = "https://ko-fi.com/rickhellenthal";

    private void KoFi_Click(object? sender, RoutedEventArgs e) => UpdateChecker.OpenInBrowser(KoFiUrl);
}
