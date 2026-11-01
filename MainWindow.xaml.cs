using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TakeABreakGoose;

/// <summary>What the landing page needs from the app.</summary>
public interface IGooseHost
{
    GooseSettings Settings { get; }
    BreakScheduler Scheduler { get; }
    void StartSession(int intervalMinutes, int? lengthMinutes);
    void SummonBreakGoose(GooseOutfit? outfit = null);

    /// <summary>Applies and saves new settings; returns an error message, or null on success.</summary>
    string? ApplySettings(GooseSettings settings, bool startWithWindows);
}

/// <summary>The landing page: start or follow a focus session, browse the wardrobe, and change settings.</summary>
public partial class MainWindow : Window
{
    private readonly IGooseHost _host;
    private ModifierKeys _hotkeyModifiers;
    private Key _hotkeyKey;

    public MainWindow(IGooseHost host)
    {
        InitializeComponent();
        _host = host;

        HeaderIcon.Source = LargestIconFrame();
        LoadSessionForm(host.Settings);
        LoadSettingsForm(host.Settings);
        BuildWardrobe();

        _host.Scheduler.StateChanged += RefreshStatus;
        Closed += (_, _) => _host.Scheduler.StateChanged -= RefreshStatus;
        RefreshStatus();
    }

    public void ShowSessionTab() => SessionTab.IsChecked = true;

    private static ImageSource LargestIconFrame()
    {
        var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/goose.ico"),
            BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (SessionPage == null) return; // fires during InitializeComponent
        SessionPage.Visibility = Show(SessionTab.IsChecked == true);
        WardrobePage.Visibility = Show(WardrobeTab.IsChecked == true);
        SettingsPage.Visibility = Show(SettingsTab.IsChecked == true);
        SettingsFooter.Visibility = Show(SettingsTab.IsChecked == true);
        SettingsStatus.Text = "";
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    // ---- Wardrobe ----

    private void BuildWardrobe()
    {
        foreach (var outfit in GooseOutfits.All)
        {
            var sendButton = new Button
            {
                Content = "Send this goose",
                Style = (Style)FindResource("BaseButton"),
                Padding = new Thickness(12, 5, 12, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0),
            };
            sendButton.Click += (_, _) => _host.SummonBreakGoose(outfit);

            var stage = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xB5, 0xB8, 0x98)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(0, 4, 0, 4),
                Child = new GooseFigure { Outfit = outfit },
            };

            WardrobeGrid.Children.Add(new Border
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
            });
        }
    }

    // ---- Focus session ----

    private void LoadSessionForm(GooseSettings settings)
    {
        SessionIntervalBox.Text = settings.LastSessionIntervalMinutes.ToString();
        SessionEndCheck.IsChecked = settings.LastSessionHasEnd;
        SetLengthBoxes(settings.LastSessionLengthMinutes);
    }

    private void SetLengthBoxes(int minutes)
    {
        SessionHoursBox.Text = (minutes / 60).ToString();
        SessionMinutesBox.Text = (minutes % 60).ToString();
    }

    private void RefreshStatus()
    {
        var scheduler = _host.Scheduler;
        var settings = _host.Settings;
        var now = DateTime.Now;
        HeaderStatus.Text = scheduler.Describe();

        var session = scheduler.Session;
        ActiveCard.Visibility = session != null ? Visibility.Visible : Visibility.Collapsed;
        StartCard.Visibility = session != null ? Visibility.Collapsed : Visibility.Visible;

        if (session != null)
        {
            if (scheduler.GooseVisiting)
            {
                CountdownText.Text = "Honk!";
                CountdownCaption.Text = "The goose is on your screen right now.";
            }
            else if (scheduler.IsPaused)
            {
                CountdownText.Text = "Paused";
                CountdownCaption.Text = "No reminders until you resume.";
            }
            else if (scheduler.IsAway)
            {
                CountdownText.Text = "Away";
                CountdownCaption.Text = "Time away from the keyboard counts as a break.";
            }
            else if (scheduler.BreakEndsAt is DateTime breakEnd)
            {
                CountdownText.Text = FormatCountdown(breakEnd - now);
                CountdownCaption.Text = "left of your break. Enjoy it!";
            }
            else if (scheduler.NextBreakAt is DateTime next)
            {
                CountdownText.Text = FormatCountdown(next - now);
                CountdownCaption.Text = $"until your next break (every {session.IntervalMinutes} min)";
            }
            else
            {
                CountdownText.Text = "";
                CountdownCaption.Text = "";
            }

            SessionEndText.Text = session.EndsAt is DateTime end
                ? $"Started at {session.StartedAt:HH:mm} · ends at {end:HH:mm} ({FormatCountdown(end - now)} left)"
                : $"Started at {session.StartedAt:HH:mm} · no end time, stop it whenever you're done.";
            PauseButton.Content = scheduler.IsPaused ? "Resume" : "Pause";
        }

        AlwaysOnNote.Text = settings.AlwaysOn
            ? $"Always on: outside a focus session the goose still visits every {settings.AlwaysOnIntervalMinutes} min."
            : "Outside a focus session the goose stays quiet. You can turn on always-on reminders in Settings.";
    }

    private static string FormatCountdown(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time:mm\\:ss}"
            : time.ToString(@"m\:ss");
    }

    private void IntervalPreset_Click(object sender, RoutedEventArgs e) =>
        SessionIntervalBox.Text = (string)((Button)sender).Tag;

    private void LengthPreset_Click(object sender, RoutedEventArgs e)
    {
        SessionEndCheck.IsChecked = true;
        SetLengthBoxes(int.Parse((string)((Button)sender).Tag));
    }

    private void StartSession_Click(object sender, RoutedEventArgs e)
    {
        SessionError.Visibility = Visibility.Collapsed;
        if (!TryParse(SessionIntervalBox.Text, 1, 600, "The break interval", out int interval, ShowSessionError)) return;

        int? length = null;
        if (SessionEndCheck.IsChecked == true)
        {
            if (!TryParse(SessionHoursBox.Text, 0, 24, "Hours", out int hours, ShowSessionError)) return;
            if (!TryParse(SessionMinutesBox.Text, 0, 59, "Minutes", out int minutes, ShowSessionError)) return;
            length = hours * 60 + minutes;
            if (length == 0)
            {
                ShowSessionError("The session needs to last at least a minute.");
                return;
            }
        }

        _host.StartSession(interval, length);
    }

    private void StopSession_Click(object sender, RoutedEventArgs e) => _host.Scheduler.StopSession();

    private void Pause_Click(object sender, RoutedEventArgs e) =>
        _host.Scheduler.SetPaused(!_host.Scheduler.IsPaused);

    private void BreakNow_Click(object sender, RoutedEventArgs e) => _host.SummonBreakGoose();

    private void ShowSessionError(string message)
    {
        SessionError.Text = message;
        SessionError.Visibility = Visibility.Visible;
    }

    // ---- Settings ----

    private void LoadSettingsForm(GooseSettings settings)
    {
        AlwaysOnCheck.IsChecked = settings.AlwaysOn;
        AlwaysOnIntervalBox.Text = settings.AlwaysOnIntervalMinutes.ToString();
        BreakBox.Text = settings.BreakMinutes.ToString();
        SnoozeBox.Text = settings.SnoozeMinutes.ToString();
        StayBox.Text = settings.StaySeconds.ToString();
        AwayBox.Text = settings.AwayMinutes.ToString();
        StartWithWindowsCheck.IsChecked = Autostart.IsEnabled;
        _hotkeyModifiers = settings.HotkeyModifiers;
        _hotkeyKey = settings.HotkeyKey;
        HotkeyBox.Text = settings.HotkeyText;
        MessagesBox.Text = string.Join(Environment.NewLine, settings.Messages);
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
            ShowSettingsStatus("The shortcut needs Ctrl, Alt or Win, so it doesn't clash with normal typing.", error: true);
            return;
        }

        _hotkeyModifiers = modifiers;
        _hotkeyKey = key;
        HotkeyBox.Text = GooseSettings.FormatHotkey(modifiers, key);
        SettingsStatus.Text = "";
    }

    private void ResetMessages_Click(object sender, RoutedEventArgs e) =>
        MessagesBox.Text = string.Join(Environment.NewLine, GooseSettings.DefaultMessages());

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        Action<string> fail = message => ShowSettingsStatus(message, error: true);
        if (!TryParse(AlwaysOnIntervalBox.Text, 1, 24 * 60, "The always-on interval", out int alwaysOnInterval, fail)) return;
        if (!TryParse(BreakBox.Text, 0, 120, "Break length", out int breakMinutes, fail)) return;
        if (!TryParse(SnoozeBox.Text, 1, 24 * 60, "Snooze length", out int snooze, fail)) return;
        if (!TryParse(StayBox.Text, 0, 3600, "Waiting time", out int stay, fail)) return;
        if (!TryParse(AwayBox.Text, 0, 120, "Away time", out int away, fail)) return;

        var messages = MessagesBox.Text
            .Split('\n')
            .Select(m => m.Trim())
            .Where(m => m.Length > 0)
            .ToList();
        if (messages.Count == 0)
        {
            fail("Give the goose at least one message to say.");
            return;
        }

        var settings = _host.Settings.Clone();
        settings.AlwaysOn = AlwaysOnCheck.IsChecked == true;
        settings.AlwaysOnIntervalMinutes = alwaysOnInterval;
        settings.BreakMinutes = breakMinutes;
        settings.SnoozeMinutes = snooze;
        settings.StaySeconds = stay;
        settings.AwayMinutes = away;
        settings.HotkeyModifiers = _hotkeyModifiers;
        settings.HotkeyKey = _hotkeyKey;
        settings.Messages = messages;

        string? error = _host.ApplySettings(settings, StartWithWindowsCheck.IsChecked == true);
        if (error != null) fail(error);
        else ShowSettingsStatus("Saved ✓", error: false);
        RefreshStatus();
    }

    private void Summon_Click(object sender, RoutedEventArgs e) => _host.SummonBreakGoose();

    private void ShowSettingsStatus(string message, bool error)
    {
        SettingsStatus.Text = message;
        SettingsStatus.Foreground = error ? Brushes.Firebrick : Brushes.SeaGreen;
    }

    private static bool TryParse(string text, int min, int max, string name, out int value, Action<string> fail)
    {
        if (int.TryParse(text.Trim(), out value) && value >= min && value <= max) return true;
        fail($"{name} must be a whole number between {min} and {max}.");
        return false;
    }
}
