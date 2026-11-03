using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace TakeABreakGoose;

/// <summary>
/// Shown once after an update: the headings of the release notes with their points in short (see
/// ReleaseNotes), for every version since the one that was installed, and a link to the release page
/// for the whole story.
/// </summary>
public partial class WhatsNewWindow : Window
{
    private static readonly IBrush Orange = new SolidColorBrush(Color.Parse("#D9711A"));

    private string _releaseUrl = "";

    public WhatsNewWindow()
    {
        InitializeComponent();
    }

    /// <param name="notes">What the previous version saved before updating; null when there's nothing.</param>
    public static Task ShowAsync(Window owner, string version, UpdateNotes? notes)
    {
        var window = new WhatsNewWindow();
        window.Fill(version, notes);
        return window.ShowDialog(owner);
    }

    internal void Fill(string version, UpdateNotes? notes)
    {
        var versions = (notes?.Versions ?? new List<VersionNotes>())
            .Select(v => (v.Version, Sections: ReleaseNotes.Highlights(v.Markdown)))
            .Where(v => v.Sections.Count > 0)
            .ToList();
        bool several = versions.Count > 1;

        HeadingText.Text = string.Format(Strings.WhatsNewHeading, version);
        IntroText.Text = versions.Count > 0 ? Strings.WhatsNewIntro : Strings.WhatsNewNoNotes;
        foreach (var (v, sections) in versions)
        {
            // With more than one version, each says which one it's from.
            if (several)
                SectionsPanel.Children.Add(new TextBlock
                {
                    Text = string.Format(Strings.WhatsNewVersion, v), Foreground = Orange,
                    FontSize = 12, FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 6),
                });
            foreach (var section in sections) AddSection(section);
        }
        if (notes?.More == true && versions.Count > 0)
            SectionsPanel.Children.Add(new TextBlock { Text = Strings.WhatsNewOlder, Classes = { "Hint" } });
        _releaseUrl = several ? Updater.AllReleasesUrl : Updater.ReleaseUrl(version);
    }

    private void AddSection(ReleaseNotesSection section)
    {
        var panel = new StackPanel { Margin = new(0, 0, 0, 12) };
        panel.Children.Add(new TextBlock { Text = section.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        foreach (string point in section.Points)
        {
            var row = new DockPanel { Margin = new(0, 3, 0, 0) };
            var bullet = new TextBlock { Text = "•", Foreground = Orange, Margin = new(2, 0, 8, 0) };
            DockPanel.SetDock(bullet, Dock.Left);
            row.Children.Add(bullet);
            row.Children.Add(new TextBlock { Text = point, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(row);
        }
        SectionsPanel.Children.Add(panel);
    }

    private void AllNotes_Click(object? sender, RoutedEventArgs e) => UpdateChecker.OpenInBrowser(_releaseUrl);

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close();
}
