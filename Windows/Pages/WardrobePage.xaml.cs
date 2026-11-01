using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TakeABreakGoose;

/// <summary>The Wardrobe tab: the goose's outfits, each of which can be sent over to show off.</summary>
public partial class WardrobePage : UserControl
{
    private IGooseHost _host = null!;

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

    /// <summary>The "goose is already on your screen" note only makes sense while it still is.</summary>
    public void Refresh()
    {
        if (!_host.GooseOnScreen) WardrobeBusyText.Visibility = Visibility.Collapsed;
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
        sendButton.Click += (_, _) =>
        {
            bool sent = _host.ShowOffOutfit(outfit);
            WardrobeBusyText.Visibility = Ui.Show(!sent);
        };

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
