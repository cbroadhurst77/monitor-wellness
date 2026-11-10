namespace MonitorWellness.Core;

/// <summary>
/// Whether the schedule is running on a location the person actually chose. A fresh install
/// starts at London's coordinates so the app works immediately, but anyone elsewhere gets the
/// wrong sunrise and sunset until they set their own. "Unconfirmed default" means: still the
/// default coordinates and never confirmed (by choosing a different place, or by explicitly
/// saying London is right). Pure, so every surface (settings banner, tray tooltip, status line,
/// one-time reminder) agrees on the answer.
/// </summary>
public static class LocationStatus
{
    public const double DefaultLatitude = 51.5072;
    public const double DefaultLongitude = -0.1276;

    private const double CoordinateTolerance = 0.0005;

    public static bool IsDefaultCoordinates(double latitude, double longitude)
        => Math.Abs(latitude - DefaultLatitude) < CoordinateTolerance
            && Math.Abs(longitude - DefaultLongitude) < CoordinateTolerance;

    public static bool IsUsingUnconfirmedDefault(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Fixed clock times need no location, so London is not a placeholder worth pointing out.
        if (ScheduleSource.UsesFixedTimes(settings))
            return false;

        return IsUsingUnconfirmedDefault(settings.Latitude, settings.Longitude, settings.LocationConfirmed);
    }

    public static bool IsUsingUnconfirmedDefault(double latitude, double longitude, bool confirmed)
        => !confirmed && IsDefaultCoordinates(latitude, longitude);

    /// <summary>
    /// What <c>LocationConfirmed</c> should be after a save: stays true once true, becomes true
    /// when the coordinates are no longer the default, or when the person confirmed London.
    /// </summary>
    public static bool ConfirmedAfterSave(bool previouslyConfirmed, bool confirmedThisSession, double latitude, double longitude)
        => previouslyConfirmed || confirmedThisSession || !IsDefaultCoordinates(latitude, longitude);

    public const string BannerText =
        "Location not set. The schedule is using London's sunrise and sunset. Search for your town above, "
        + "click the map, or confirm that London is right.";

    public const string StatusSuffix = " Location not set, so London's sunrise and sunset are being used.";

    public const string ReminderBalloon =
        "Monitor Wellness is still using London's sunrise and sunset. Open Settings from this menu and search for your town so the day/night schedule matches where you are.";
}
