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
    void StartSession(SessionPlan plan);
    void SummonBreakGoose();

    /// <summary>
    /// Sends the goose over just to show off an outfit; it doesn't touch the session's timer.
    /// Returns false if a goose is already on screen.
    /// </summary>
    bool ShowOffOutfit(GooseOutfit outfit);

    /// <summary>Whether a goose is on screen right now (walking, talking, peeking or napping).</summary>
    bool GooseOnScreen { get; }

    /// <summary>Keeps the goose away during a focus session (e.g. during a meeting), or lets it come again.</summary>
    void SetDoNotDisturb(bool on);

    /// <summary>Applies and saves new settings; returns an error message, or null on success.</summary>
    string? ApplySettings(GooseSettings settings, bool startWithWindows);
}

/// <summary>The landing page: start or follow a focus session, browse the wardrobe, and change settings.</summary>
public partial class MainWindow : Window
{
    private readonly IGooseHost _host;
    private bool _showingStatus;
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
        VersionText.Text = $"Take a Break Goose {UpdateChecker.Display(UpdateChecker.CurrentVersion)}";
        Loaded += async (_, _) => ShowUpdate(await UpdateChecker.CheckAsync());

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
        PageScroller.ScrollToTop(); // each tab starts at the top, not where the previous one was scrolled to
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
            sendButton.Click += (_, _) =>
            {
                bool sent = _host.ShowOffOutfit(outfit);
                WardrobeBusyText.Visibility = Show(!sent);
            };

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

    private RadioButton[] IntervalPresets => new[] { Interval15, Interval30, Interval45, Interval60 };
    private RadioButton[] BreakPresets => new[] { Break5, Break10, Break15 };

    private void LoadSessionForm(GooseSettings settings)
    {
        int interval = settings.LastSessionIntervalMinutes;
        var preset = IntervalPresets.FirstOrDefault(r => (string)r.Tag == interval.ToString());
        (preset ?? IntervalCustom).IsChecked = true;
        SessionIntervalBox.Text = interval.ToString();
        SelectBreakLength(settings.BreakMinutes);

        SetLengthBoxes(settings.LastSessionLengthMinutes);
        SessionEndTimeBox.Text = settings.LastSessionEndTime.ToString(@"hh\:mm");
        (settings.LastSessionEndMode == SessionEndMode.At ? EndAtRadio : EndAfterRadio).IsChecked = true;
        EndOptionsToggle.IsChecked = settings.LastSessionEndMode != SessionEndMode.None;
        UpdateEndOptions();
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
            else if (scheduler.AwaitingReturn && !(scheduler.BreakEndsAt > now))
            {
                CountdownText.Text = "Welcome back";
                CountdownCaption.Text = "Your break is over. Tell the goose when you're ready to focus again.";
            }
            else if (scheduler.BreakWaiting)
            {
                CountdownText.Text = "Break time";
                CountdownCaption.Text = "Your break is waiting. The goose comes as soon as you turn off Do not disturb.";
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
                CountdownCaption.Text = session.BreakMinutes > 0
                    ? $"until your next break (every {session.IntervalMinutes} min, {session.BreakMinutes} min breaks)"
                    : $"until your next break (every {session.IntervalMinutes} min)";
            }
            else
            {
                CountdownText.Text = "";
                CountdownCaption.Text = "";
            }

            string started = session.StartedAutomatically
                ? $"Started automatically at {session.StartedAt:HH:mm}"
                : $"Started at {session.StartedAt:HH:mm}";
            SessionEndText.Text = session.EndsAt is DateTime end
                ? $"{started} · ends {DescribeEnd(end, now)} ({FormatCountdown(end - now)} left)"
                : $"{started} · no end time, stop it whenever you're done.";

            _showingStatus = true;
            DoNotDisturbSwitch.IsChecked = scheduler.DoNotDisturb;
            _showingStatus = false;
            DoNotDisturbHint.Text = !scheduler.DoNotDisturb
                ? "Keep the goose away for now, for example during a meeting."
                : scheduler.BreakWaiting
                    ? "Your break is waiting until you turn this off."
                    : "The goose stays away until you turn this off. Your timer keeps running.";
        }
        else
        {
            UpdateEndPreview(); // "in 2 h 14 min" moves along with the clock
        }

        // The "goose is already on your screen" note only makes sense while it still is.
        if (!_host.GooseOnScreen) WardrobeBusyText.Visibility = Visibility.Collapsed;

        SignOffBanner.Visibility = Show(session == null && scheduler.DoNotDisturb);
        AutoStartNote.Visibility = Show(session == null);
        AutoStartNote.Text = settings.AutoStartSession
            ? $"When the app starts, it starts a focus session by itself (a break every {settings.AutoStartIntervalMinutes} min). You can change this in Settings."
            : "Outside a focus session the goose stays quiet. Want it to start one by itself? You can turn that on in Settings.";
    }

    private static string FormatCountdown(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time:mm\\:ss}"
            : time.ToString(@"m\:ss");
    }

    /// <summary>"at 17:00", or "tomorrow at 01:00" when it's past midnight.</summary>
    private static string DescribeEnd(DateTime end, DateTime now) =>
        end.Date > now.Date ? $"tomorrow at {end:HH:mm}" : $"at {end:HH:mm}";

    /// <summary>"2 h 14 min", "2 h" or "14 min".</summary>
    private static string DescribeDuration(TimeSpan time)
    {
        int minutes = (int)Math.Ceiling(Math.Max(0, time.TotalMinutes));
        int hours = minutes / 60;
        minutes %= 60;
        if (hours == 0) return $"{minutes} min";
        return minutes == 0 ? $"{hours} h" : $"{hours} h {minutes} min";
    }

    private void Interval_Checked(object sender, RoutedEventArgs e)
    {
        if (CustomIntervalPanel == null) return; // fires during InitializeComponent
        bool custom = IntervalCustom.IsChecked == true;
        CustomIntervalPanel.Visibility = Show(custom);
        if (custom && IsLoaded) SessionIntervalBox.Focus();
    }

    /// <summary>Starts from the default break length in the settings.</summary>
    private void SelectBreakLength(int minutes)
    {
        var preset = BreakPresets.FirstOrDefault(r => (string)r.Tag == minutes.ToString());
        (preset ?? BreakCustom).IsChecked = true;
        SessionBreakBox.Text = minutes.ToString();
    }

    private void BreakLength_Checked(object sender, RoutedEventArgs e)
    {
        if (CustomBreakPanel == null) return; // fires during InitializeComponent
        bool custom = BreakCustom.IsChecked == true;
        CustomBreakPanel.Visibility = Show(custom);
        if (custom && IsLoaded) SessionBreakBox.Focus();
    }

    private void EndOptionsToggle_Changed(object sender, RoutedEventArgs e) => UpdateEndOptions();

    private void UpdateEndOptions()
    {
        bool open = EndOptionsToggle.IsChecked == true;
        EndOptionsPanel.Visibility = Show(open);
        EndOptionsToggle.Content = open ? "Remove end time ▴" : "Set an end time ▾";
        UpdateEndPreview();
    }

    private void EndAfterInput_GotFocus(object sender, RoutedEventArgs e) => EndAfterRadio.IsChecked = true;

    private void EndAtInput_GotFocus(object sender, RoutedEventArgs e) => EndAtRadio.IsChecked = true;

    private void EndOption_Changed(object sender, RoutedEventArgs e) => UpdateEndPreview();

    private void LengthPreset_Click(object sender, RoutedEventArgs e)
    {
        EndAfterRadio.IsChecked = true;
        SetLengthBoxes(int.Parse((string)((Button)sender).Tag));
    }

    /// <summary>The end-time choice as typed so far, or an explanation of what's wrong with it.</summary>
    private (SessionEndMode Mode, int LengthMinutes, TimeSpan EndTime, string? Error) ReadEndChoice()
    {
        var settings = _host.Settings;
        int length = settings.LastSessionLengthMinutes;
        TimeSpan endTime = settings.LastSessionEndTime;
        if (EndOptionsToggle.IsChecked != true) return (SessionEndMode.None, length, endTime, null);

        if (EndAtRadio.IsChecked == true)
        {
            if (!TryParseTimeOfDay(SessionEndTimeBox.Text, out endTime))
                return (SessionEndMode.At, length, settings.LastSessionEndTime, "Enter the end time like 17:00.");
            return (SessionEndMode.At, length, endTime, null);
        }

        if (!int.TryParse(SessionHoursBox.Text.Trim(), out int hours) || hours is < 0 or > 24
            || !int.TryParse(SessionMinutesBox.Text.Trim(), out int minutes) || minutes is < 0 or > 59)
        {
            return (SessionEndMode.After, length, endTime, "Enter how long the session lasts, like 2 h 0 min.");
        }
        length = hours * 60 + minutes;
        if (length == 0) return (SessionEndMode.After, length, endTime, "The session needs to last at least a minute.");
        return (SessionEndMode.After, length, endTime, null);
    }

    /// <summary>Accepts 17:00, 17.00, 1700, 9:30 or just 17.</summary>
    private static bool TryParseTimeOfDay(string text, out TimeSpan time)
    {
        time = default;
        string t = text.Trim().Replace('.', ':');
        int hours, minutes = 0;
        if (t.Contains(':'))
        {
            var parts = t.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out hours) || !int.TryParse(parts[1], out minutes)) return false;
        }
        else if (t.Length is 3 or 4 && int.TryParse(t, out int hhmm))
        {
            hours = hhmm / 100;
            minutes = hhmm % 100;
        }
        else if (!int.TryParse(t, out hours))
        {
            return false;
        }
        if (hours is < 0 or > 23 || minutes is < 0 or > 59) return false;
        time = new TimeSpan(hours, minutes, 0);
        return true;
    }

    private void UpdateEndPreview()
    {
        if (EndPreview == null || EndOptionsToggle.IsChecked != true) return;
        var (mode, length, endTime, error) = ReadEndChoice();
        if (error != null)
        {
            EndPreview.Text = error;
            return;
        }
        var now = DateTime.Now;
        var end = new SessionPlan(1, 0, mode, length, endTime).EndsAt(now)!.Value;
        EndPreview.Text = $"The session ends {DescribeEnd(end, now)}, in {DescribeDuration(end - now)}.";
    }

    private void StartSession_Click(object sender, RoutedEventArgs e)
    {
        SessionError.Visibility = Visibility.Collapsed;

        int interval;
        var preset = IntervalPresets.FirstOrDefault(r => r.IsChecked == true);
        if (preset != null) interval = int.Parse((string)preset.Tag);
        else if (!TryParse(SessionIntervalBox.Text, 1, 600, "The break interval", out interval, ShowSessionError)) return;

        int breakMinutes;
        var breakPreset = BreakPresets.FirstOrDefault(r => r.IsChecked == true);
        if (breakPreset != null) breakMinutes = int.Parse((string)breakPreset.Tag);
        else if (!TryParse(SessionBreakBox.Text, 0, 120, "The break length", out breakMinutes, ShowSessionError)) return;

        var (mode, length, endTime, error) = ReadEndChoice();
        if (error != null)
        {
            ShowSessionError(error);
            return;
        }

        _host.StartSession(new SessionPlan(interval, breakMinutes, mode, length, endTime));
    }

    private void StopSession_Click(object sender, RoutedEventArgs e) => _host.Scheduler.StopSession();


    private void BreakNow_Click(object sender, RoutedEventArgs e) => _host.SummonBreakGoose();

    private AvailableUpdate? _update;

    private void ShowUpdate(AvailableUpdate? update)
    {
        _update = update;
        if (update == null) return;
        UpdateRun.Text = $"Version {UpdateChecker.Display(update.Version)} is available.";
        UpdateText.Visibility = Visibility.Visible;
        SettingsTab.Content = "Settings •"; // a small hint that there's something new to see there
    }

    private void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_update != null) UpdateChecker.OpenInBrowser(_update.Url);
    }

    private void EndDoNotDisturb_Click(object sender, RoutedEventArgs e)
    {
        _host.SetDoNotDisturb(false);
        RefreshStatus();
    }

    private void DoNotDisturbSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_showingStatus) return; // the switch is just being updated to match the app
        _host.SetDoNotDisturb(DoNotDisturbSwitch.IsChecked == true);
        RefreshStatus();
    }

    private void ShowSessionError(string message)
    {
        SessionError.Text = message;
        SessionError.Visibility = Visibility.Visible;
    }

    // ---- Settings ----

    private void LoadSettingsForm(GooseSettings settings)
    {
        AutoStartCheck.IsChecked = settings.AutoStartSession;
        AutoStartIntervalBox.Text = settings.AutoStartIntervalMinutes.ToString();
        BreakBox.Text = settings.BreakMinutes.ToString();
        SnoozeBox.Text = settings.SnoozeMinutes.ToString();
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
        if (!TryParse(AutoStartIntervalBox.Text, 1, 24 * 60, "The automatic session interval", out int autoStartInterval, fail)) return;
        if (!TryParse(BreakBox.Text, 0, 120, "Default break length", out int breakMinutes, fail)) return;
        if (!TryParse(SnoozeBox.Text, 1, 24 * 60, "Snooze length", out int snooze, fail)) return;

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
        settings.AutoStartSession = AutoStartCheck.IsChecked == true;
        settings.AutoStartIntervalMinutes = autoStartInterval;
        settings.BreakMinutes = breakMinutes;
        settings.SnoozeMinutes = snooze;
        settings.HotkeyModifiers = _hotkeyModifiers;
        settings.HotkeyKey = _hotkeyKey;
        settings.Messages = messages;

        string? error = _host.ApplySettings(settings, StartWithWindowsCheck.IsChecked == true);
        if (error != null) fail(error);
        else
        {
            ShowSettingsStatus("Saved ✓", error: false);
            SelectBreakLength(settings.BreakMinutes); // the start card follows the new default
        }
        RefreshStatus();
    }


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
