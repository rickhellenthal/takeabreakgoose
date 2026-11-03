using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace TakeABreakGoose;

/// <summary>Edits the messages the goose picks from. Opened from Settings, with its own Save.</summary>
public partial class MessagesPage : UserControl
{
    private IGooseHost _host = null!;
    private readonly ObservableCollection<EditableMessage> _messages = new();
    private List<string> _saved = new();

    /// <summary>Raised after saving, with the number of messages (the Settings card shows it).</summary>
    public event Action<int>? Saved;

    public MessagesPage()
    {
        InitializeComponent();
        // Before the text box gets the key, like WPF's PreviewKeyDown.
        NewMessageBox.AddHandler(KeyDownEvent, NewMessageBox_KeyDown, RoutingStrategies.Tunnel);
    }

    public void Initialize(IGooseHost host) => _host = host;

    /// <summary>Shows the saved messages, ready to edit.</summary>
    public void Open()
    {
        _saved = _host.Settings.Messages.ToList();
        ShowMessages(_saved);
        NewMessageBox.Clear();
        Status.Text = "";
        Scroller.ScrollToHome();
    }

    private void ShowMessages(IEnumerable<string> messages)
    {
        _messages.Clear();
        foreach (var message in messages) _messages.Add(new EditableMessage(message));
        MessageList.ItemsSource = _messages;
    }

    /// <summary>The messages as they are now, including one typed in the "Add a message" box.</summary>
    private List<string> CurrentMessages() =>
        _messages.Select(m => m.Text)
            .Append(NewMessageBox.Text ?? "")
            .Select(m => m.Trim())
            .Where(m => m.Length > 0)
            .ToList();

    private async void ResetMessages_Click(object? sender, RoutedEventArgs e)
    {
        var defaults = GooseSettings.DefaultMessages(_host.Settings.Language);
        bool alreadyDefault = _messages.Select(m => m.Text.Trim()).SequenceEqual(defaults);
        if (!alreadyDefault && !await ConfirmDialog.AskAsync(OwnerWindow(), Strings.MessagesResetTitle, Strings.MessagesResetHeading,
                string.Format(Strings.MessagesResetMessage, defaults.Count), Strings.MessagesResetConfirm))
        {
            return;
        }
        ShowMessages(defaults);
        Scroller.ScrollToHome();
    }

    private void RemoveMessage_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is EditableMessage message) _messages.Remove(message);
    }

    private void AddMessage_Click(object? sender, RoutedEventArgs e)
    {
        AddNewMessage();
        NewMessageBox.Focus(); // ready for the next one
    }

    private void NewMessageBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        AddNewMessage();
    }

    private void NewMessageBox_TextChanged(object? sender, TextChangedEventArgs e) =>
        NewMessageHint.IsVisible = string.IsNullOrEmpty(NewMessageBox.Text);

    private void AddNewMessage()
    {
        string text = (NewMessageBox.Text ?? "").Trim();
        if (text.Length == 0) return;
        _messages.Add(new EditableMessage(text));
        NewMessageBox.Clear();
        // Once the new message is laid out, so the end is where it is.
        Dispatcher.UIThread.Post(() => Scroller.ScrollToEnd(), DispatcherPriority.Background);
    }

    // ---- Saving ----

    private void Save_Click(object? sender, RoutedEventArgs e) => TrySave();

    /// <summary>Saves the messages. Returns false (and shows why) if it can't.</summary>
    private bool TrySave()
    {
        var messages = CurrentMessages();
        if (messages.Count == 0)
        {
            ShowStatus(Strings.MessagesAtLeastOne, error: true);
            return false;
        }

        var settings = _host.Settings.Clone();
        settings.Messages = messages;
        string? error = _host.ApplySettings(settings, LoginItem.IsEnabled);
        if (error != null)
        {
            ShowStatus(error, error: true);
            return false;
        }
        AddNewMessage(); // a message typed in the "Add a message" box is in the list now
        _saved = messages;
        ShowStatus(Strings.Saved, error: false);
        Saved?.Invoke(messages.Count);
        return true;
    }

    private void ShowStatus(string message, bool error)
    {
        Status.Text = message;
        Status.Foreground = error ? Brushes.Firebrick : Brushes.SeaGreen;
    }

    private Window OwnerWindow() => (Window)TopLevel.GetTopLevel(this)!;

    public bool HasUnsavedChanges => !CurrentMessages().SequenceEqual(_saved);

    /// <summary>
    /// Asks what to do with unsaved changes. Returns true when it's fine to leave (nothing changed,
    /// saved or discarded), false to stay.
    /// </summary>
    public async Task<bool> ResolveUnsavedChangesAsync()
    {
        if (!HasUnsavedChanges) return true;
        switch (await UnsavedChangesDialog.AskAsync(OwnerWindow(), Strings.MessagesUnsaved))
        {
            case UnsavedChangesChoice.Save:
                return TrySave();
            case UnsavedChangesChoice.Discard:
                return true;
            default:
                return false;
        }
    }
}
