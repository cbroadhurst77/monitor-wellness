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

/// <summary>Location: world map picker, coordinates, place search, sun times and the "location not set" banner. (part of the settings window; see SettingsWindow.xaml.cs.)</summary>
public partial class SettingsWindow
{
    private void LoadWorldMapImage()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MonitorWellness.Assets.worldmap.jpg")
            ?? throw new InvalidOperationException("Embedded world map resource not found.");

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad; // load fully now so the stream can be disposed
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();

        MapImage.Source = bitmap;
    }

    /// <summary>
    /// Standard equirectangular projection: longitude maps linearly across the full image
    /// width (-180 to +180), latitude linearly down the full height (+90 to -90). The world
    /// map asset is a true equirectangular projection (see ATTRIBUTIONS.md), so this holds
    /// regardless of the container's on-screen size — MapContainer's Width/Height are fixed
    /// in XAML specifically so this math doesn't need to handle letterboxing.
    /// </summary>
    private void UpdateMapMarker()
    {
        if (!double.TryParse(LatitudeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) || !double.IsFinite(lat))
            return;
        if (!double.TryParse(LongitudeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon) || !double.IsFinite(lon))
            return;

        lat = Math.Clamp(lat, -90, 90);
        lon = Math.Clamp(lon, -180, 180);

        double x = (lon + 180.0) / 360.0 * MapContainer.Width;
        double y = (90.0 - lat) / 180.0 * MapContainer.Height;

        Canvas.SetLeft(MapMarker, x - MapMarker.Width / 2.0);
        Canvas.SetTop(MapMarker, y - MapMarker.Height / 2.0);
    }

    private void LatLongBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        UpdateMapMarker();
        UpdateSunTimesDisplay();
        UpdateLocationBanner();
    }

    /// <summary>Shows the "location not set" banner while the coordinates in the boxes are still London's and nobody has said that's right.</summary>
    private void UpdateLocationBanner()
    {
        bool show = double.TryParse(LatitudeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
            && double.TryParse(LongitudeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)
            && LocationStatus.IsUsingUnconfirmedDefault(lat, lon, _locationConfirmed);

        LocationDefaultBanner.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LocationDefaultBannerText.Text = show ? LocationStatus.BannerText : "";
    }

    private void ConfirmLondonButton_Click(object sender, RoutedEventArgs e)
    {
        _locationConfirmed = true;
        _londonConfirmedThisSession = true;
        UpdateLocationBanner();
        LocationSearchStatus.Text = "Keeping London as your location. Save to remember this.";
    }

    private void UpdateSunTimesDisplay()
    {
        if (!double.TryParse(LatitudeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
            || !double.TryParse(LongitudeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)
            || !double.IsFinite(lat) || !double.IsFinite(lon)
            || lat is < -90 or > 90 || lon is < -180 or > 180)
        {
            SunTimesText.Text = "";
            return;
        }

        // The person's calendar day (local midnight, expressed in UTC), not the UTC date, so
        // "today" is right far from Greenwich and near the polar day/night edge cases.
        DateTime localDayStartUtc = DateTime.Today.ToUniversalTime();
        var sunrise = SolarCalculator.FindSunriseInWindowUtc(localDayStartUtc, lat, lon);
        var sunset = SolarCalculator.FindSunsetInWindowUtc(localDayStartUtc, lat, lon);

        SunTimesText.Text = (sunrise, sunset) switch
        {
            (null, null) => "No sunrise or sunset today at this location (polar day or night).",
            (null, not null) => $"Today: no sunrise (polar day), sunset {sunset.Value.ToLocalTime():HH:mm}",
            (not null, null) => $"Today: sunrise {sunrise.Value.ToLocalTime():HH:mm}, no sunset (polar day)",
            _ => $"Today: sunrise {sunrise!.Value.ToLocalTime():HH:mm}, sunset {sunset!.Value.ToLocalTime():HH:mm} (local time) — this is what drives the schedule below."
        };
    }

    private void MapContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(MapContainer);

        double lon = pos.X / MapContainer.Width * 360.0 - 180.0;
        double lat = 90.0 - pos.Y / MapContainer.Height * 180.0;

        SetLocationCoordinates(lat, lon);
    }

    private void MapContainer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        const double SmallStepDegrees = 1.0;
        const double LargeStepDegrees = 10.0;
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? LargeStepDegrees : SmallStepDegrees;
        double lat = TryReadCoordinate(LatitudeBox.Text, 0.0);
        double lon = TryReadCoordinate(LongitudeBox.Text, 0.0);

        switch (e.Key)
        {
            case Key.Up:
                lat += step;
                break;
            case Key.Down:
                lat -= step;
                break;
            case Key.Right:
                lon += step;
                break;
            case Key.Left:
                lon -= step;
                break;
            default:
                return;
        }

        SetLocationCoordinates(lat, lon, "Location adjusted with the keyboard.");
        e.Handled = true;
    }

    private static double TryReadCoordinate(string text, double fallback)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
            ? value
            : fallback;

    private void SetLocationCoordinates(double lat, double lon, string status = "")
    {
        LatitudeBox.Text = Math.Clamp(lat, -90, 90).ToString("F4", CultureInfo.InvariantCulture);
        LongitudeBox.Text = Math.Clamp(lon, -180, 180).ToString("F4", CultureInfo.InvariantCulture);
        LocationSearchStatus.Text = status;
    }

    private async void FindLocationButton_Click(object sender, RoutedEventArgs e)
    {
        string query = LocationSearchBox.Text.Trim();
        if (query.Length == 0)
            return;

        FindLocationButton.IsEnabled = false;
        LocationSearchStatus.Text = "Searching...";

        try
        {
            var result = await GeocodingService.SearchAsync(query);

            if (result is null)
            {
                LocationSearchStatus.Text = $"Couldn't find \"{query}\" — try a different spelling, or a nearby larger town.";
            }
            else
            {
                SetLocationCoordinates(result.Latitude, result.Longitude, $"Found: {result.DisplayName}");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"Location search failed unexpectedly: {ex.Message}");
            LocationSearchStatus.Text = "Location search failed. Try again or enter coordinates manually.";
        }
        finally
        {
            // Nominatim's usage policy asks for roughly no more than 1 request/second. This is a
            // manual, one-off search box, not a batch process, so the real-world risk is low --
            // but keeping the button disabled a beat past the request itself closes the gap
            // between "compliant in practice" and "compliant by design" for this app's only
            // network call.
            await Task.Delay(TimeSpan.FromSeconds(1));
            FindLocationButton.IsEnabled = true;
        }
    }

    // --- Live preview -------------------------------------------------------------------
    // Dragging any Day/Night/migraine slider applies it directly to the real gamma
    // ramp/overlay right now, so the effect can be judged before Save commits anything.
    // App suspends the normal 30s schedule tick for as long as this window is open
    // (App._settingsPreviewActive) so the two don't fight over the same monitors — see
    // App.xaml.cs's OpenSettingsWindow/RunScheduleTick. Cancel needs no special revert
    // logic: closing this window (Save or Cancel) just lets the real schedule resume,
    // which naturally shows whatever is actually saved.
}
