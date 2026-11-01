using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    private readonly ObservableCollection<EditableMessage> _messages = new();
    private string _savedForm = "";
    private AvailableUpdate? _update;

    /// <summary>Raised after saving, with the new default break length (the start card follows it).</summary>
    public event Action<int>? Saved;

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
        BreakBox.Text = settings.BreakMinutes.ToString();
        SnoozeBox.Text = settings.SnoozeMinutes.ToString();
        StartWithWindowsCheck.IsChecked = Autostart.IsEnabled;
        _hotkeyModifiers = settings.HotkeyModifiers;
        _hotkeyKey = settings.HotkeyKey;
        HotkeyBox.Text = settings.HotkeyText;
        ShowMessages(settings.Messages);
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

    private void ShowMessages(IEnumerable<string> messages)
    {
        _messages.Clear();
        foreach (var message in messages) _messages.Add(new EditableMessage(message));
        MessageList.ItemsSource = _messages;
    }

    private void ResetMessages_Click(object sender, RoutedEventArgs e)
    {
        var defaults = GooseSettings.DefaultMessages();
        bool alreadyDefault = _messages.Select(m => m.Text.Trim()).SequenceEqual(defaults);
        if (!alreadyDefault && !ConfirmDialog.Ask(Window.GetWindow(this)!, "Reset messages", "Reset the messages?",
                $"This replaces your list with the goose's {defaults.Count} default messages. "
                + "Your own messages will be gone after you save.", "Reset"))
        {
            return;
        }
        ShowMessages(defaults);
    }

    private void RemoveMessage_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is EditableMessage message) _messages.Remove(message);
    }

    private void AddMessage_Click(object sender, RoutedEventArgs e)
    {
        AddNewMessage();
        NewMessageBox.Focus(); // ready for the next one
    }

    private void NewMessageBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        AddNewMessage();
    }

    private void NewMessageBox_TextChanged(object sender, TextChangedEventArgs e) =>
        NewMessageHint.Visibility = Ui.Show(NewMessageBox.Text.Length == 0);

    private void AddNewMessage()
    {
        string text = NewMessageBox.Text.Trim();
        if (text.Length == 0) return;
        _messages.Add(new EditableMessage(text));
        NewMessageBox.Clear();
    }

    // ---- Saving ----

    private void SaveSettings_Click(object sender, RoutedEventArgs e) => TrySave();

    /// <summary>Validates and saves the settings form. Returns false (and shows why) if it can't.</summary>
    private bool TrySave()
    {
        Action<string> fail = message => ShowStatus(message, error: true);
        if (!Ui.TryParseNumber(AutoStartIntervalBox.Text, 1, 24 * 60, "The automatic session interval", out int autoStartInterval, fail)) return false;
        if (!Ui.TryParseNumber(BreakBox.Text, 0, 120, "Default break length", out int breakMinutes, fail)) return false;
        if (!Ui.TryParseNumber(SnoozeBox.Text, 1, 24 * 60, "Snooze length", out int snooze, fail)) return false;

        AddNewMessage(); // a message typed in the "Add a message" box but not added yet still counts

        var messages = _messages
            .Select(m => m.Text.Trim())
            .Where(m => m.Length > 0)
            .ToList();
        if (messages.Count == 0)
        {
            fail("Give the goose at least one message to say.");
            return false;
        }

        var settings = _host.Settings.Clone();
        settings.AutoStartSession = AutoStartCheck.IsChecked == true;
        settings.AutoStartIntervalMinutes = autoStartInterval;
        settings.BreakMinutes = breakMinutes;
        settings.SnoozeMinutes = snooze;
        settings.HotkeyModifiers = _hotkeyModifiers;
        settings.HotkeyKey = _hotkeyKey;
        settings.Messages = messages;

        string? error = _host.ApplySettings(settings, StartWithWindowsCheck.IsChecked == true);
        if (error != null)
        {
            fail(error);
            return false;
        }
        ShowStatus("Saved ✓", error: false);
        _savedForm = FormState();
        Saved?.Invoke(settings.BreakMinutes);
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
        AutoStartCheck.IsChecked, AutoStartIntervalBox.Text, BreakBox.Text, SnoozeBox.Text,
        StartWithWindowsCheck.IsChecked, HotkeyBox.Text, string.Join("\n", _messages.Select(m => m.Text)),
        NewMessageBox.Text.Trim());

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
}
