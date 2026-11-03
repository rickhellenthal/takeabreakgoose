using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TakeABreakGoose;

/// <summary>A small "are you sure?" pop-up in the app's style.</summary>
public partial class ConfirmDialog : Window
{
    private bool _confirmed;

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Returns true if the user confirmed; Cancel, Esc and closing the window all count as no.
    /// Modal, but unlike WPF's ShowDialog it doesn't block: await it.
    /// </summary>
    public static async Task<bool> AskAsync(Window owner, string title, string heading, string message, string confirmText)
    {
        var dialog = new ConfirmDialog { Title = title };
        dialog.HeadingText.Text = heading;
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirmText;
        await dialog.ShowDialog(owner);
        return dialog._confirmed;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
