using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MonitorWellness.Core;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace MonitorWellness;

/// <summary>History tracking, summary, clear and CSV export. (part of the settings window; see SettingsWindow.xaml.cs.)</summary>
public partial class SettingsWindow
{
    /// <summary>
    /// Live-preview only — like every other control in this window, the actual setting isn't
    /// committed until Save (see TryParseAll), so Cancel genuinely leaves it untouched. This
    /// previously saved immediately on toggle, bypassing Cancel entirely — a real bug caught on
    /// re-review, not by any earlier hand-testing, since a toggle-then-Cancel sequence is easy
    /// to not think to try. RefreshHistorySummary reads the checkbox's own IsChecked state
    /// directly (not _settings), so the live summary preview still works with no settings
    /// mutation needed here.
    /// </summary>
    private void HistoryTrackingCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        PromptForRatingCheckBox.IsEnabled = HistoryTrackingCheckBox.IsChecked == true;
        if (HistoryTrackingCheckBox.IsChecked != true)
            PromptForRatingCheckBox.IsChecked = false; // the rating prompt only makes sense alongside history tracking — see MigraineModeController.Deactivate

        if (!_loaded) return;
        RefreshHistorySummary();
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            "This will permanently delete your saved history. Are you sure?",
            "Monitor Wellness",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        HistoryStore.Clear();
        RefreshHistorySummary();
    }

    /// <summary>
    /// The aggregate summary above answers "how often and how well, on average" — this hands
    /// over the same raw HistoryEvent records HistoryStore.Load() already returns, one row per
    /// event, so a user can cross-reference activation timing against anything else they track
    /// (e.g. a personal migraine diary) rather than being limited to the on-screen averages.
    /// </summary>
    private void ExportHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = "MonitorWellness-history.csv",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Export Monitor Wellness History",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var events = HistoryStore.Load();
            var lines = new List<string> { "TimestampUtc,EventType,GentleResponse,DurationSeconds,Rating" };
            lines.AddRange(events.Select(evt => string.Join(",",
                evt.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
                evt.EventType,
                evt.Mild?.ToString() ?? "",
                evt.DurationSeconds?.ToString(CultureInfo.InvariantCulture) ?? "",
                evt.Rating?.ToString(CultureInfo.InvariantCulture) ?? "")));
            File.WriteAllLines(dialog.FileName, lines);
            System.Windows.MessageBox.Show(this, $"History exported to {dialog.FileName}.", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(this, $"Couldn't export history: {ex.Message}", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BreakHistoryCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        RefreshBreakHistorySummary();
    }

    /// <summary>The daily summary: one row per day from the first recorded event to today, for a spreadsheet.</summary>
    private void ExportDailyHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var events = HistoryStore.Load();
        if (BreakHistory.Range(events, DateTime.UtcNow, TimeZoneInfo.Local) is not (DateOnly from, DateOnly to))
        {
            System.Windows.MessageBox.Show(this, "There is no history to export yet.", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = "MonitorWellness-daily-summary.csv",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Export Daily Summary",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllText(dialog.FileName, BreakHistory.ToCsv(BreakHistory.BuildDaily(events, from, to, TimeZoneInfo.Local)));
            System.Windows.MessageBox.Show(this, $"Daily summary exported to {dialog.FileName}.", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(this, $"Couldn't export the daily summary: {ex.Message}", "Monitor Wellness", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshBreakHistorySummary()
    {
        if (BreakHistoryCheckBox.IsChecked != true)
        {
            BreakHistorySummaryText.Text = "Break history is off.";
            return;
        }

        BreakHistorySummaryText.Text = BreakHistory.Describe(HistoryStore.Load(), DateTime.UtcNow, TimeZoneInfo.Local);
    }

    private void RefreshHistorySummary()
    {
        RefreshBreakHistorySummary();

        if (HistoryTrackingCheckBox.IsChecked != true)
        {
            HistorySummaryText.Text = "History tracking is off.";
            HistoryTrendText.Text = string.Empty;
            HistoryTrendText.Visibility = Visibility.Collapsed;
            return;
        }

        var allEvents = HistoryStore.Load();
        HistoryTrendText.Text = HistoryTrend.Describe(allEvents, DateTime.UtcNow);
        HistoryTrendText.Visibility = Visibility.Visible;
        var summary = HistorySummarizer.Summarize(allEvents, DateTime.UtcNow);
        HistorySummaryText.Text = summary.TotalActivations == 0
            ? "No Migraine Mode activations recorded yet."
            : $"{summary.TotalActivations} total activation(s) ({summary.FullCount} strong, {summary.MildCount} gentle) — " +
              $"recent activity: {summary.ActivationsLast7Days} in the last 7 days and {summary.ActivationsPrevious7Days} in the 7 days before that; {summary.ActivationsLast30Days} in the last 30." +
              (summary.AverageDurationMinutes is double avg ? $" Average duration: {avg:F0} min." : "") +
              (summary.PauseCount > 0 ? $" Schedule paused {summary.PauseCount} time(s)." : "") +
              (summary.AverageRating is double avgRating ? $" Average helpfulness: {avgRating:F1}/5 ({summary.RatingCount} rating(s))." : "") +
              " Activity counts are personal records, not medical insight.";
    }
}
