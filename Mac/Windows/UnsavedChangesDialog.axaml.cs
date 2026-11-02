using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TakeABreakGoose;

public enum UnsavedChangesChoice { KeepEditing, Discard, Save }

/// <summary>Asks what to do with settings that were changed but not saved.</summary>
public partial class UnsavedChangesDialog : Window
{
    private UnsavedChangesChoice _choice = UnsavedChangesChoice.KeepEditing;

    public UnsavedChangesDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Closing the dialog any other way (Esc, closing the window) counts as "keep editing".
    /// Modal, but unlike WPF's ShowDialog it doesn't block: await it.
    /// </summary>
    public static async Task<UnsavedChangesChoice> AskAsync(Window owner,
        string message = "You changed some settings but didn't save them yet.")
    {
        var dialog = new UnsavedChangesDialog();
        dialog.MessageText.Text = message;
        await dialog.ShowDialog(owner);
        return dialog._choice;
    }

    private void Choose(UnsavedChangesChoice choice)
    {
        _choice = choice;
        Close();
    }

    private void KeepEditing_Click(object? sender, RoutedEventArgs e) => Choose(UnsavedChangesChoice.KeepEditing);

    private void Discard_Click(object? sender, RoutedEventArgs e) => Choose(UnsavedChangesChoice.Discard);

    private void Save_Click(object? sender, RoutedEventArgs e) => Choose(UnsavedChangesChoice.Save);
}
