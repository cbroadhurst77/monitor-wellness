using System.Globalization;

namespace MonitorWellness.Core;

/// <summary>
/// The one-line answer to "when is my next break, and if it hasn't come, why?" shown in the tray menu.
/// The reminder can wait for several reasons (a snooze, the chosen hours, a call, a full-screen app,
/// migraine mode, being away from the keyboard), and without this the only way to find out is to wait.
/// Pure so it can be tested.
/// </summary>
public static class BreakReminderStatusText
{
    /// <summary>The stand-up part of the status line, or nothing when that reminder is off.</summary>
    public static string StandUpSuffix(bool enabled, TimeSpan accumulated, TimeSpan interval)
    {
        if (!enabled)
            return "";

        TimeSpan remaining = interval - accumulated;
        if (remaining <= TimeSpan.Zero)
            return " · Stand-up break due";

        int minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return minutes >= 60
            ? string.Create(CultureInfo.InvariantCulture, $" · Stand-up break in about {minutes / 60} h {minutes % 60} min")
            : string.Create(CultureInfo.InvariantCulture, $" · Stand-up break in about {minutes} min");
    }

    public static string Describe(
        bool enabled,
        DateTime? snoozedUntilLocal,
        bool withinHours,
        string? hoursFromLocal,
        TimeSpan accumulated,
        TimeSpan interval,
        BreakReminderDecision? heldFor)
    {
        if (!enabled)
            return "Break reminders are off";

        if (snoozedUntilLocal is DateTime until)
            return string.Create(CultureInfo.InvariantCulture, $"Break reminders snoozed until {until:HH:mm}");

        if (!withinHours)
            return string.IsNullOrWhiteSpace(hoursFromLocal)
                ? "Outside your reminder hours"
                : $"Outside your reminder hours (back at {hoursFromLocal})";

        if (heldFor is BreakReminderDecision reason && reason != BreakReminderDecision.Show)
        {
            return reason switch
            {
                BreakReminderDecision.SuppressedForCall => "A break is due — waiting for your call to end",
                BreakReminderDecision.SuppressedForFullscreen => "A break is due — waiting for the full-screen app to close",
                BreakReminderDecision.SuppressedForMigraine => "A break is due — paused while Migraine Mode is on",
                BreakReminderDecision.SuppressedForIdle => "A break is due — waiting until you are back at the keyboard",
                _ => "A break is due",
            };
        }

        TimeSpan remaining = interval - accumulated;
        if (remaining <= TimeSpan.Zero)
            return "A break is due now";

        int minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return string.Create(CultureInfo.InvariantCulture, $"Next break in about {minutes} min");
    }
}
