using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace TakeABreakGoose;

/// <summary>The Focus session tab: start a session, or follow the one that's running.</summary>
public partial class FocusSessionPage : UserControl
{
    private IGooseHost _host = null!;
    private bool _showingStatus;
    private bool _loadingForm;

    public FocusSessionPage()
    {
        InitializeComponent();
        // Before the text box gets the key (it would move the caret), like WPF's PreviewKeyDown.
        SessionEndTimeBox.AddHandler(KeyDownEvent, SessionEndTimeBox_PreviewKeyDown, RoutingStrategies.Tunnel);
    }

    public void Initialize(IGooseHost host)
    {
        _host = host;
        // "15 min", "1 h": the chips' texts come from their numbers of minutes, in the app's language.
        foreach (var chip in IntervalPresets.Concat(BreakPresets)) chip.Content = TimeText.Minutes(int.Parse((string)chip.Tag!));
        foreach (var chip in LengthPresets) chip.Content = TimeText.Duration(TimeSpan.FromMinutes(int.Parse((string)chip.Tag!)));
        LoadSessionForm(host.Settings);
    }

    public void ScrollToTop() => Scroller.ScrollToHome();

    private RadioButton[] IntervalPresets => new[] { Interval15, Interval30, Interval45, Interval60 };
    private RadioButton[] BreakPresets => new[] { Break5, Break10, Break15 };
    private RadioButton[] LengthPresets => new[] { Length60, Length120, Length180, Length240 };

    private void LoadSessionForm(GooseSettings settings)
    {
        _loadingForm = true;
        int interval = settings.LastSessionIntervalMinutes;
        var preset = IntervalPresets.FirstOrDefault(r => (string?)r.Tag == interval.ToString());
        (preset ?? IntervalCustom).IsChecked = true;
        SessionIntervalBox.Text = interval.ToString();
        SelectBreakLength(TimeSpan.FromSeconds(settings.LastSessionBreakSeconds));

        ResetEndChoice(settings);
        EndOptionsToggle.IsChecked = settings.LastSessionEndMode != SessionEndMode.None;
        _loadingForm = false;
        UpdateEndOptions();
    }

    /// <summary>Puts the end-time choices back to how the last session was started.</summary>
    private void ResetEndChoice(GooseSettings settings)
    {
        SelectLength(settings.LastSessionLengthMinutes);
        SessionEndTimeBox.Text = FormatTimeOfDay(settings.LastSessionEndTime);
        (settings.LastSessionEndMode == SessionEndMode.At ? EndAtRadio : EndAfterRadio).IsChecked = true;
    }

    private void SelectLength(int minutes)
    {
        var preset = LengthPresets.FirstOrDefault(r => (string?)r.Tag == minutes.ToString());
        (preset ?? LengthCustom).IsChecked = true;
        SessionHoursBox.Text = (minutes / 60).ToString();
        SessionMinutesBox.Text = (minutes % 60).ToString();
    }

    /// <summary>Tabbing or clicking into a number box selects the number, so typing replaces it (no "0" + "20" = "020").</summary>
    private void NumberBox_GotFocus(object? sender, RoutedEventArgs e)
    {
        // After the click that focused it has placed the caret, or that would undo the selection.
        if (sender is TextBox box) Dispatcher.UIThread.Post(box.SelectAll);
    }

    /// <summary>Puts the cursor in the box with its text selected, so typing replaces it.</summary>
    private static void FocusAndSelectAll(TextBox box)
    {
        box.Focus();
        box.SelectAll();
    }

    private static string FormatTimeOfDay(TimeSpan time) => time.ToString(@"hh\:mm");

    /// <summary>Shows the running session's countdown, or the start card when there's none.</summary>
    public void Refresh()
    {
        var scheduler = _host.Scheduler;
        var settings = _host.Settings;
        var now = DateTime.Now;

        var session = scheduler.Session;
        ActiveCard.IsVisible = session != null;
        StartCard.IsVisible = session == null;

        if (session != null)
        {
            if (scheduler.GooseVisiting)
            {
                CountdownText.Text = Strings.SessionHonk;
                CountdownCaption.Text = Strings.SessionGooseOnScreen;
            }
            else if (scheduler.AwaitingReturn && !(scheduler.BreakEndsAt > now))
            {
                CountdownText.Text = Strings.SessionWelcomeBack;
                CountdownCaption.Text = Strings.SessionBreakOverCaption;
            }
            else if (scheduler.BreakWaiting)
            {
                CountdownText.Text = Strings.SessionBreakTime;
                CountdownCaption.Text = Strings.SessionBreakWaitingCaption;
            }
            else if (scheduler.IsAway)
            {
                CountdownText.Text = Strings.SessionAway;
                CountdownCaption.Text = Strings.SessionAwayCaption;
            }
            else if (scheduler.BreakEndsAt is DateTime breakEnd)
            {
                CountdownText.Text = TimeText.Countdown(breakEnd - now);
                CountdownCaption.Text = Strings.SessionBreakLeftCaption;
            }
            else if (scheduler.NextBreakAt is DateTime next)
            {
                CountdownText.Text = TimeText.Countdown(next - now);
                CountdownCaption.Text = session.BreakLength > TimeSpan.Zero
                    ? string.Format(Strings.SessionUntilNextBreak, session.IntervalMinutes, TimeText.BreakLength(session.BreakLength))
                    : string.Format(Strings.SessionUntilNextBreakNoLength, session.IntervalMinutes);
            }
            else
            {
                CountdownText.Text = "";
                CountdownCaption.Text = "";
            }

            string started = string.Format(
                session.StartedAutomatically ? Strings.SessionStartedAutomaticallyAt : Strings.SessionStartedAt,
                session.StartedAt);
            SessionEndText.Text = session.EndsAt is DateTime end
                ? string.Format(Strings.SessionEnds, started, TimeText.End(end, now), TimeText.Countdown(end - now))
                : string.Format(Strings.SessionNoEnd, started);

            ShowBreakHistory(scheduler, now);

            _showingStatus = true;
            DoNotDisturbSwitch.IsChecked = scheduler.DoNotDisturb;
            _showingStatus = false;
            DoNotDisturbHint.Text = !scheduler.DoNotDisturb
                ? settings.DoNotDisturbDuringCalls
                    ? Strings.DoNotDisturbHintCalls
                    : Strings.DoNotDisturbHint
                : _host.DoNotDisturbForCall
                    ? scheduler.BreakWaiting
                        ? Strings.DoNotDisturbInCallBreakWaiting
                        : Strings.DoNotDisturbInCall
                    : scheduler.BreakWaiting
                        ? Strings.DoNotDisturbOnBreakWaiting
                        : Strings.DoNotDisturbOn;
        }
        else
        {
            UpdateEndPreview(); // "in 2 h 14 min" moves along with the clock
        }

        SignOffBanner.IsVisible = session == null && scheduler.DoNotDisturb;
        SignOffHint.Text = _host.DoNotDisturbForCall
            ? Strings.SignOffWaitingInCall
            : Strings.SignOffWaiting;
        AutoStartNote.IsVisible = session == null;
        AutoStartNote.Text = settings.AutoStartSession
            ? string.Format(Strings.AutoStartNoteOn, settings.AutoStartIntervalMinutes)
            : Strings.AutoStartNoteOff;
    }

    // ---- Break history ----

    private string _shownBreaks = "";

    /// <summary>The breaks of the running session, newest first, with the one in progress on top.</summary>
    private void ShowBreakHistory(BreakScheduler scheduler, DateTime now)
    {
        var rows = new List<(string Times, string Note, string Length, bool Now)>();
        if (scheduler.BreakStartedAt is DateTime onBreakSince)
            rows.Add((string.Format(Strings.HistoryUntilNow, onBreakSince), Strings.HistoryOnBreak, SoFar(now - onBreakSince), true));
        else if (scheduler.AwaySince is DateTime awaySince)
            rows.Add((string.Format(Strings.HistoryUntilNow, awaySince), Strings.HistoryAway, SoFar(now - awaySince), true));
        foreach (var record in scheduler.Breaks.Reverse())
            rows.Add(($"{record.Start:HH:mm} – {record.End:HH:mm}", BreakNote(record.Kind), TimeText.Length(record.Length), false));

        var done = scheduler.Breaks;
        var total = done.Aggregate(TimeSpan.Zero, (sum, b) => sum + b.Length);
        BreaksSummary.Text = done.Count == 0 ? ""
            : string.Format(Strings.HistorySummary, done.Count, SessionSummary.BreakNoun(done.Count), TimeText.Length(total));
        BreaksFooter.IsVisible = rows.Count > 0;

        // Only rebuild the list when something in it changed (this runs every second).
        string shown = string.Join("|", rows);
        if (shown == _shownBreaks) return;
        _shownBreaks = shown;
        BreakList.Children.Clear();
        foreach (var row in rows) BreakList.Children.Add(BreakRow(row.Times, row.Note, row.Length, row.Now));
    }

    private static string SoFar(TimeSpan time) =>
        time < TimeSpan.FromSeconds(30) ? Strings.HistoryJustStarted : string.Format(Strings.HistorySoFar, TimeText.Length(time));

    private static string BreakNote(BreakKind kind) => kind switch
    {
        BreakKind.EndedEarly => Strings.HistoryEndedEarly,
        BreakKind.Away => Strings.HistoryAway,
        _ => Strings.HistoryNapped,
    };

    /// <summary>One break; the one in progress looks like a selected chip (orange border and text).</summary>
    private static Border BreakRow(string times, string note, string length, bool inProgress)
    {
        var lengthText = new TextBlock { Text = length, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        if (inProgress) lengthText.Foreground = Color("#B85F12");
        DockPanel.SetDock(lengthText, Dock.Right);
        var details = new StackPanel();
        details.Children.Add(new TextBlock { Text = times });
        details.Children.Add(new TextBlock { Text = note, Classes = { "Hint" } });
        var row = new DockPanel();
        row.Children.Add(lengthText);
        row.Children.Add(details);
        return new Border
        {
            Background = Color(inProgress ? "#FDEBD8" : "White"),
            // Every row has the border's room, so the rows don't shift when a break ends.
            BorderBrush = inProgress ? (IBrush?)Application.Current!.FindResource("Accent") : Brushes.Transparent,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 0, 0, 4),
            Child = row,
        };
    }

    private static IBrush Color(string hex) => Brush.Parse(hex);

    private void Interval_Checked(object? sender, RoutedEventArgs e)
    {
        // Fires during InitializeComponent. Only "Other…" matters, and it raises this itself whenever it
        // changes (the others' events come while the group is still updating).
        if (CustomIntervalPanel == null || sender != IntervalCustom) return;
        bool custom = IntervalCustom.IsChecked == true;
        CustomIntervalPanel.IsVisible = custom;
        if (custom && IsLoaded) FocusAndSelectAll(SessionIntervalBox);
    }

    /// <summary>Selects a break length; the start card starts from the one picked last time.</summary>
    private void SelectBreakLength(TimeSpan length)
    {
        var preset = length.Seconds == 0
            ? BreakPresets.FirstOrDefault(r => (string?)r.Tag == ((int)length.TotalMinutes).ToString())
            : null;
        (preset ?? BreakCustom).IsChecked = true;
        SessionBreakBox.Text = ((int)length.TotalMinutes).ToString();
        SessionBreakSecondsBox.Text = length.Seconds.ToString();
    }

    private void BreakLength_Checked(object? sender, RoutedEventArgs e)
    {
        if (CustomBreakPanel == null || sender != BreakCustom) return; // fires during InitializeComponent; see Interval_Checked
        bool custom = BreakCustom.IsChecked == true;
        CustomBreakPanel.IsVisible = custom;
        if (custom && IsLoaded) FocusAndSelectAll(SessionBreakBox);
    }

    private void EndOptionsToggle_Changed(object? sender, RoutedEventArgs e)
    {
        if (EndOptionsPanel == null) return; // fires during InitializeComponent
        // Opening it starts "End at" from now, not from a time remembered from another day.
        if (EndOptionsToggle.IsChecked == true && !_loadingForm)
            SessionEndTimeBox.Text = FormatTimeOfDay(SessionPlan.NextQuarterHour(DateTime.Now));
        // Closing it throws away what was picked, so opening it again starts fresh.
        else if (EndOptionsToggle.IsChecked != true && _host != null)
            ResetEndChoice(_host.Settings);
        UpdateEndOptions();
    }

    private void UpdateEndOptions()
    {
        bool open = EndOptionsToggle.IsChecked == true;
        EndOptionsPanel.IsVisible = open;
        EndOptionsToggle.Content = open ? Strings.EndTimeRemove : Strings.EndTimeSet;
        UpdateEndModeLook();
        UpdateEndPreview();
    }

    private void UpdateEndModeLook()
    {
        if (EndAtInputs == null) return; // fires during InitializeComponent
        bool at = EndAtRadio.IsChecked == true;
        EndAfterRadio.Opacity = EndAfterInputs.Opacity = at ? 0.5 : 1;
        EndAtRadio.Opacity = EndAtInputs.Opacity = at ? 1 : 0.5;
    }

    private void EndAfterInput_Picked(object? sender, RoutedEventArgs e) => EndAfterRadio.IsChecked = true;

    private void EndAtInput_Picked(object? sender, RoutedEventArgs e) => EndAtRadio.IsChecked = true;

    private void EndOption_Changed(object? sender, RoutedEventArgs e)
    {
        UpdateEndModeLook();
        UpdateEndPreview();
    }

    private void Length_Checked(object? sender, RoutedEventArgs e)
    {
        if (CustomLengthPanel == null) return; // fires during InitializeComponent
        // See Interval_Checked. The preview runs for every change, so it ends up with the group's final state.
        if (sender == LengthCustom)
        {
            bool custom = LengthCustom.IsChecked == true;
            CustomLengthPanel.IsVisible = custom;
            if (custom && IsLoaded && !_loadingForm && EndOptionsToggle.IsChecked == true) FocusAndSelectAll(SessionHoursBox);
        }
        UpdateEndPreview();
    }

    private void EndTimeStep_Click(object? sender, RoutedEventArgs e) =>
        StepEndTime(int.Parse((string)((Button)sender!).Tag!));

    /// <summary>Up and Down move the end time by 5 minutes, or by 1 with Shift held.</summary>
    private void SessionEndTimeBox_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down)) return;
        int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : 5;
        StepEndTime(e.Key == Key.Up ? step : -step);
        SessionEndTimeBox.CaretIndex = SessionEndTimeBox.Text?.Length ?? 0;
        e.Handled = true;
    }

    private void StepEndTime(int minutes)
    {
        EndAtRadio.IsChecked = true;
        if (!SessionPlan.TryParseTimeOfDay(SessionEndTimeBox.Text ?? "", out var time)) time = SessionPlan.NextQuarterHour(DateTime.Now);
        SessionEndTimeBox.Text = FormatTimeOfDay(SessionPlan.AddToTimeOfDay(time, minutes));
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
            if (!SessionPlan.TryParseTimeOfDay(SessionEndTimeBox.Text ?? "", out endTime))
                return (SessionEndMode.At, length, settings.LastSessionEndTime, Strings.EndTimeInvalid);
            return (SessionEndMode.At, length, endTime, null);
        }

        var preset = LengthPresets.FirstOrDefault(r => r.IsChecked == true);
        if (preset != null) return (SessionEndMode.After, int.Parse((string)preset.Tag!), endTime, null);

        if (!int.TryParse((SessionHoursBox.Text ?? "").Trim(), out int hours) || hours is < 0 or > 24
            || !int.TryParse((SessionMinutesBox.Text ?? "").Trim(), out int minutes) || minutes is < 0 or > 59)
        {
            return (SessionEndMode.After, length, endTime, Strings.EndLengthInvalid);
        }
        length = hours * 60 + minutes;
        if (length == 0) return (SessionEndMode.After, length, endTime, Strings.EndLengthTooShort);
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
        var end = new SessionPlan(1, TimeSpan.Zero, mode, length, endTime).EndsAt(now)!.Value;
        EndPreview.Text = string.Format(Strings.EndPreview, TimeText.End(end, now), TimeText.Duration(end - now));
    }

    private void StartSession_Click(object? sender, RoutedEventArgs e)
    {
        SessionError.IsVisible = false;

        int interval;
        var preset = IntervalPresets.FirstOrDefault(r => r.IsChecked == true);
        if (preset != null) interval = int.Parse((string)preset.Tag!);
        else if (!Ui.TryParseNumber(SessionIntervalBox.Text, 1, 600, Strings.FieldBreakInterval, out interval, ShowSessionError)) return;

        TimeSpan breakLength;
        var breakPreset = BreakPresets.FirstOrDefault(r => r.IsChecked == true);
        if (breakPreset != null) breakLength = TimeSpan.FromMinutes(int.Parse((string)breakPreset.Tag!));
        else
        {
            // An empty box counts as 0, so "20 sec" can be typed without filling in "0 min".
            static string OrZero(string? text) => string.IsNullOrWhiteSpace(text) ? "0" : text;
            if (!Ui.TryParseNumber(OrZero(SessionBreakBox.Text), 0, 120, Strings.FieldBreakMinutes, out int minutes, ShowSessionError)) return;
            if (!Ui.TryParseNumber(OrZero(SessionBreakSecondsBox.Text), 0, 59, Strings.FieldBreakSeconds, out int seconds, ShowSessionError)) return;
            breakLength = new TimeSpan(0, minutes, seconds);
        }

        var (mode, length, endTime, error) = ReadEndChoice();
        if (error != null)
        {
            ShowSessionError(error);
            return;
        }

        _host.StartSession(new SessionPlan(interval, breakLength, mode, length, endTime));
    }

    private void StopSession_Click(object? sender, RoutedEventArgs e) => _host.StopSession();

    private void BreakNow_Click(object? sender, RoutedEventArgs e) => _host.SummonBreakGoose();

    private void EndDoNotDisturb_Click(object? sender, RoutedEventArgs e)
    {
        _host.SetDoNotDisturb(false);
        Refresh();
    }

    private void DoNotDisturbSwitch_Changed(object? sender, RoutedEventArgs e)
    {
        if (_showingStatus || _host == null) return; // the switch is just being updated to match the app
        _host.SetDoNotDisturb(DoNotDisturbSwitch.IsChecked == true);
        Refresh();
    }

    private void ShowSessionError(string message)
    {
        SessionError.Text = message;
        SessionError.IsVisible = true;
    }
}
