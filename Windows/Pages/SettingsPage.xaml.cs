using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TakeABreakGoose;

/// <summary>The Settings tab, with its own Save bar and a check for unsaved changes.</summary>
public partial class SettingsPage : UserControl
{
    private IGooseHost _host = null!;
    private ModifierKeys _hotkeyModifiers;
    private Key _hotkeyKey;
    private string _savedForm = "";
    private AvailableUpdate? _update;


    public SettingsPage()
    {
        InitializeComponent();
    }

    public void Initialize(IGooseHost host)
    {
        _host = host;
        LoadForm(host.Settings);
        VersionText.Text = $"Take a Break Goose {UpdateChecker.Display(UpdateChecker.CurrentVersion)}";
    }

    public void ScrollToTop() => Scroller.ScrollToTop();

    public void ClearStatus() => SettingsStatus.Text = "";

    private void LoadForm(GooseSettings settings)
    {
        AutoStartCheck.IsChecked = settings.AutoStartSession;
        AutoStartIntervalBox.Text = settings.AutoStartIntervalMinutes.ToString();
        AllowSnoozeCheck.IsChecked = settings.AllowSnooze;
        SnoozeBox.Text = settings.SnoozeMinutes.ToString();
        StartWithWindowsCheck.IsChecked = Autostart.IsEnabled;
        CallDoNotDisturbCheck.IsChecked = settings.DoNotDisturbDuringCalls;
        _hotkeyModifiers = settings.HotkeyModifiers;
        _hotkeyKey = settings.HotkeyKey;
        HotkeyBox.Text = settings.HotkeyText;
        ShowMessageCount(settings.Messages.Count);
        _savedForm = FormState();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab)
        {
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
        {
            ShowStatus("The shortcut needs Ctrl, Alt or Win, so it doesn't clash with normal typing.", error: true);
            return;
        }

        _hotkeyModifiers = modifiers;
        _hotkeyKey = key;
        HotkeyBox.Text = GooseSettings.FormatHotkey(modifiers, key);
        SettingsStatus.Text = "";
    }

    // ---- Messages ----

    /// <summary>The user wants to edit the messages, which have a page of their own.</summary>
    public event Action? EditMessagesRequested;

    public void ShowMessageCount(int count) =>
        MessageCountText.Text = count == 1 ? "1 message" : $"{count} messages";

    private void EditMessages_Click(object sender, RoutedEventArgs e) => EditMessagesRequested?.Invoke();

    // ---- Saving ----

    private void SaveSettings_Click(object sender, RoutedEventArgs e) => TrySave();

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

        string? error = _host.ApplySettings(settings, StartWithWindowsCheck.IsChecked == true);
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
        StartWithWindowsCheck.IsChecked, CallDoNotDisturbCheck.IsChecked, HotkeyBox.Text);

    private bool HasUnsavedChanges => FormState() != _savedForm;

    /// <summary>
    /// Asks what to do with unsaved changes. Returns true when it's fine to move on
    /// (nothing changed, saved or discarded), false to stay in Settings.
    /// </summary>
    public bool ResolveUnsavedChanges()
    {
        if (!HasUnsavedChanges) return true;
        switch (UnsavedChangesDialog.Ask(Window.GetWindow(this)!))
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
        UpdateText.Visibility = Visibility.Visible;
    }

    private void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_update != null) UpdateChecker.OpenInBrowser(_update.Url);
    }

    private const string KoFiUrl = "https://ko-fi.com/rickhellenthal";

    private void KoFi_Click(object sender, RoutedEventArgs e) => UpdateChecker.OpenInBrowser(KoFiUrl);
}
