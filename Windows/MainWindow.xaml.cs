using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TakeABreakGoose;

/// <summary>
/// The app window: a header with the current status, and three tabs (Focus session, Wardrobe,
/// Settings), each a page of its own.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IGooseHost _host;
    private RadioButton? _currentTab;
    private bool _revertingTab;

    public MainWindow(IGooseHost host)
    {
        InitializeComponent();
        _host = host;

        HeaderIcon.Source = LargestIconFrame();
        SessionView.Initialize(host);
        WardrobeView.Initialize(host);
        SettingsView.Initialize(host);
        SettingsView.Saved += SessionView.SelectBreakLength; // the start card follows the new default
        Loaded += async (_, _) => ShowUpdate(await UpdateChecker.CheckAsync());

        _host.Scheduler.StateChanged += RefreshStatus;
        Closed += (_, _) => _host.Scheduler.StateChanged -= RefreshStatus;
        RefreshStatus();
    }

    private static ImageSource LargestIconFrame()
    {
        var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/goose.ico"),
            BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
    }

    private void RefreshStatus()
    {
        HeaderStatus.Text = _host.Scheduler.Describe();
        SessionView.Refresh();
        WardrobeView.Refresh();
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (SessionView == null || _revertingTab) return; // fires during InitializeComponent, or while going back

        // Leaving Settings with unsaved changes: ask first, and stay if they want to keep editing.
        if (_currentTab == SettingsTab && sender != SettingsTab && !SettingsView.ResolveUnsavedChanges())
        {
            _revertingTab = true;
            SettingsTab.IsChecked = true;
            _revertingTab = false;
            return;
        }
        _currentTab = (RadioButton)sender;

        SessionView.Visibility = Ui.Show(SessionTab.IsChecked == true);
        WardrobeView.Visibility = Ui.Show(WardrobeTab.IsChecked == true);
        SettingsView.Visibility = Ui.Show(SettingsTab.IsChecked == true);
        SettingsView.ClearStatus();
        // Each tab starts at the top, not where it was scrolled to last time.
        SessionView.ScrollToTop();
        WardrobeView.ScrollToTop();
        SettingsView.ScrollToTop();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel && !SettingsView.ResolveUnsavedChanges())
        {
            e.Cancel = true;
            SettingsTab.IsChecked = true; // show the problem, or the changes they want to keep editing
        }
    }

    private void ShowUpdate(AvailableUpdate? update)
    {
        if (update == null) return;
        SettingsView.ShowUpdate(update);
        SettingsTab.Content = "Settings •"; // a small hint that there's something new to see there
    }
}
