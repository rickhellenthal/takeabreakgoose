using System.Windows;

namespace TakeABreakGoose;

public enum UnsavedChangesChoice { KeepEditing, Discard, Save }

/// <summary>Asks what to do with settings that were changed but not saved.</summary>
public partial class UnsavedChangesDialog : Window
{
    private UnsavedChangesChoice _choice = UnsavedChangesChoice.KeepEditing;

    private UnsavedChangesDialog()
    {
        InitializeComponent();
    }

    /// <summary>Closing the dialog any other way (Esc, the X) counts as "keep editing".</summary>
    public static UnsavedChangesChoice Ask(Window owner, string? message = null)
    {
        var dialog = new UnsavedChangesDialog { Owner = owner };
        dialog.MessageText.Text = message ?? Strings.UnsavedSettings;
        dialog.ShowDialog();
        return dialog._choice;
    }

    protected override void OnContentRendered(System.EventArgs e)
    {
        base.OnContentRendered(e);
        InvalidateVisual(); // avoids a black strip where the frame came out bigger than the content
    }

    private void Choose(UnsavedChangesChoice choice)
    {
        _choice = choice;
        Close();
    }

    private void KeepEditing_Click(object sender, RoutedEventArgs e) => Choose(UnsavedChangesChoice.KeepEditing);

    private void Discard_Click(object sender, RoutedEventArgs e) => Choose(UnsavedChangesChoice.Discard);

    private void Save_Click(object sender, RoutedEventArgs e) => Choose(UnsavedChangesChoice.Save);
}
