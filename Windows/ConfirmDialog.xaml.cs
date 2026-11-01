using System.Windows;

namespace TakeABreakGoose;

/// <summary>A small "are you sure?" pop-up in the app's style.</summary>
public partial class ConfirmDialog : Window
{
    private bool _confirmed;

    private ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>Returns true if the user confirmed; Cancel, Esc and the X all count as no.</summary>
    public static bool Ask(Window owner, string title, string heading, string message, string confirmText)
    {
        var dialog = new ConfirmDialog { Owner = owner, Title = title };
        dialog.HeadingText.Text = heading;
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirmText;
        dialog.ShowDialog();
        return dialog._confirmed;
    }

    protected override void OnContentRendered(System.EventArgs e)
    {
        base.OnContentRendered(e);
        InvalidateVisual(); // avoids a black strip where the frame came out bigger than the content
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
