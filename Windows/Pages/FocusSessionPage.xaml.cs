using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TakeABreakGoose;

/// <summary>The Focus session tab: start a session, or follow the one that's running.</summary>
public partial class FocusSessionPage : UserControl
{
    private IGooseHost _host = null!;
    private bool _showingStatus;

    public FocusSessionPage()
    {
        InitializeComponent();
    }

    public void Initialize(IGooseHost host)
    {
        _host = host;
        LoadSessionForm(host.Settings);
    }

    public void ScrollToTop() => Scroller.ScrollToTop();

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

    /// <summary>Shows the running session's countdown, or the start card when there's none.</summary>
    public void Refresh()
    {
        var scheduler = _host.Scheduler;
        var settings = _host.Settings;
        var now = DateTime.Now;

        var session = scheduler.Session;
        ActiveCard.Visibility = Ui.Show(session != null);
        StartCard.Visibility = Ui.Show(session == null);

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
                CountdownText.Text = TimeText.Countdown(breakEnd - now);
                CountdownCaption.Text = "left of your break. Enjoy it!";
            }
            else if (scheduler.NextBreakAt is DateTime next)
            {
                CountdownText.Text = TimeText.Countdown(next - now);
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
                ? $"{started} · ends {TimeText.End(end, now)} ({TimeText.Countdown(end - now)} left)"
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

        SignOffBanner.Visibility = Ui.Show(session == null && scheduler.DoNotDisturb);
        AutoStartNote.Visibility = Ui.Show(session == null);
        AutoStartNote.Text = settings.AutoStartSession
            ? $"When the app starts, it starts a focus session by itself (a break every {settings.AutoStartIntervalMinutes} min). You can change this in Settings."
            : "Outside a focus session the goose stays quiet. Want it to start one by itself? You can turn that on in Settings.";
    }

    private void Interval_Checked(object sender, RoutedEventArgs e)
    {
        if (CustomIntervalPanel == null) return; // fires during InitializeComponent
        bool custom = IntervalCustom.IsChecked == true;
        CustomIntervalPanel.Visibility = Ui.Show(custom);
        if (custom && IsLoaded) SessionIntervalBox.Focus();
    }

    /// <summary>Selects a break length; the start card starts from the default in the settings.</summary>
    public void SelectBreakLength(int minutes)
    {
        var preset = BreakPresets.FirstOrDefault(r => (string)r.Tag == minutes.ToString());
        (preset ?? BreakCustom).IsChecked = true;
        SessionBreakBox.Text = minutes.ToString();
    }

    private void BreakLength_Checked(object sender, RoutedEventArgs e)
    {
        if (CustomBreakPanel == null) return; // fires during InitializeComponent
        bool custom = BreakCustom.IsChecked == true;
        CustomBreakPanel.Visibility = Ui.Show(custom);
        if (custom && IsLoaded) SessionBreakBox.Focus();
    }

    private void EndOptionsToggle_Changed(object sender, RoutedEventArgs e) => UpdateEndOptions();

    private void UpdateEndOptions()
    {
        bool open = EndOptionsToggle.IsChecked == true;
        EndOptionsPanel.Visibility = Ui.Show(open);
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
            if (!SessionPlan.TryParseTimeOfDay(SessionEndTimeBox.Text, out endTime))
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

    private void UpdateEndPreview()
    {
        // Also called while the page is still being built, before it knows the app.
        if (_host == null || EndPreview == null || EndOptionsToggle.IsChecked != true) return;
        var (mode, length, endTime, error) = ReadEndChoice();
        if (error != null)
        {
            EndPreview.Text = error;
            return;
        }
        var now = DateTime.Now;
        var end = new SessionPlan(1, 0, mode, length, endTime).EndsAt(now)!.Value;
        EndPreview.Text = $"The session ends {TimeText.End(end, now)}, in {TimeText.Duration(end - now)}.";
    }

    private void StartSession_Click(object sender, RoutedEventArgs e)
    {
        SessionError.Visibility = Visibility.Collapsed;

        int interval;
        var preset = IntervalPresets.FirstOrDefault(r => r.IsChecked == true);
        if (preset != null) interval = int.Parse((string)preset.Tag);
        else if (!Ui.TryParseNumber(SessionIntervalBox.Text, 1, 600, "The break interval", out interval, ShowSessionError)) return;

        int breakMinutes;
        var breakPreset = BreakPresets.FirstOrDefault(r => r.IsChecked == true);
        if (breakPreset != null) breakMinutes = int.Parse((string)breakPreset.Tag);
        else if (!Ui.TryParseNumber(SessionBreakBox.Text, 0, 120, "The break length", out breakMinutes, ShowSessionError)) return;

        var (mode, length, endTime, error) = ReadEndChoice();
        if (error != null)
        {
            ShowSessionError(error);
            return;
        }

        _host.StartSession(new SessionPlan(interval, breakMinutes, mode, length, endTime));
    }

    private void StopSession_Click(object sender, RoutedEventArgs e) => _host.StopSession();

    private void BreakNow_Click(object sender, RoutedEventArgs e) => _host.SummonBreakGoose();

    private void EndDoNotDisturb_Click(object sender, RoutedEventArgs e)
    {
        _host.SetDoNotDisturb(false);
        Refresh();
    }

    private void DoNotDisturbSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_showingStatus) return; // the switch is just being updated to match the app
        _host.SetDoNotDisturb(DoNotDisturbSwitch.IsChecked == true);
        Refresh();
    }

    private void ShowSessionError(string message)
    {
        SessionError.Text = message;
        SessionError.Visibility = Visibility.Visible;
    }
}
