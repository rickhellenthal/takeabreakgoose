using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TakeABreakGoose;

/// <summary>The Wardrobe tab: the goose's outfits, each of which can be sent over to show off.</summary>
public partial class WardrobePage : UserControl
{
    private IGooseHost _host = null!;
    private const string BusyToolTip = "A goose is already on your screen. Once it has left, you can send another one.";
    private readonly List<Button> _sendButtons = new();

    public WardrobePage()
    {
        InitializeComponent();
    }

    public void Initialize(IGooseHost host)
    {
        _host = host;
        foreach (var outfit in GooseOutfits.All) WardrobeGrid.Children.Add(OutfitCard(outfit));
    }

    public void ScrollToTop() => Scroller.ScrollToTop();

    /// <summary>
    /// Only one goose fits on the screen: while one is there, the buttons wait, and their tooltip says
    /// why. The note above the cards only comes for a goose that's there for the session (one you
    /// just sent over explains itself), so the page doesn't jump around after every click.
    /// Called every second, so the buttons come back soon after the goose has left.
    /// </summary>
    public void Refresh()
    {
        bool busy = _host.GooseOnScreen;
        WardrobeBusyText.Visibility = Ui.Show(_host.SessionGooseOnScreen);
        foreach (var button in _sendButtons)
        {
            button.IsEnabled = !busy;
            button.ToolTip = busy ? BusyToolTip : null;
        }
    }

    private Border OutfitCard(GooseOutfit outfit)
    {
        var sendButton = new Button
        {
            Content = "Send this goose",
            Style = (Style)FindResource("BaseButton"),
            Padding = new Thickness(12, 5, 12, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0),
        };
        ToolTipService.SetShowOnDisabled(sendButton, true);
        sendButton.Click += (_, _) =>
        {
            _host.ShowOffOutfit(outfit);
            Refresh();
        };
        _sendButtons.Add(sendButton);

        var stage = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xB5, 0xB8, 0x98)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(0, 4, 0, 4),
            Child = new GooseFigure { Outfit = outfit },
        };

        return new Border
        {
            Style = (Style)FindResource("Card"),
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Children =
                {
                    stage,
                    new TextBlock
                    {
                        Text = GooseOutfits.Name(outfit),
                        Style = (Style)FindResource("CardTitle"),
                        Margin = new Thickness(0, 10, 0, 2),
                    },
                    new TextBlock { Text = GooseOutfits.Description(outfit), Style = (Style)FindResource("Hint") },
                    sendButton,
                },
            },
        };
    }
}
