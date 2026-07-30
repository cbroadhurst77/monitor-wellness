using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonitorWellness.Core;

public sealed record GeocodingResult(double Latitude, double Longitude, string DisplayName);

/// <summary>
/// Looks up a town name, postcode/zip, or general place query and returns its coordinates,
/// via OpenStreetMap's Nominatim search API — free, no API key required. This is the one
/// place in the app that makes a network call; everything else (settings, logs, the schedule
/// engine itself) is fully local. Only used when the user explicitly searches for a place in
/// the settings window.
///
/// Nominatim's usage policy requires a descriptive User-Agent identifying the application
/// (not a browser-style default) and reasonable request volume — fine for occasional manual
/// lookups from a settings window, which is the only way this gets called.
/// </summary>
public sealed class GeocodingService
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MonitorWellness/1.0 (+https://github.com/cbroadhurst77/monitor-wellness)");
        return client;
    }

    private sealed record NominatimEntry(
        [property: JsonPropertyName("lat")] string Lat,
        [property: JsonPropertyName("lon")] string Lon,
        [property: JsonPropertyName("display_name")] string DisplayName);

    /// <summary>
    /// Searches for a place by name, town, or postcode/zip. Returns null if nothing was
    /// found or the lookup failed (network error, timeout, malformed response) — callers
    /// should treat null as "couldn't find that," not throw a user-facing exception for it.
    /// </summary>
    public async Task<GeocodingResult?> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        string url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(query)}&format=json&limit=1";

        try
        {
            using var response = await Client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                DebugLog.Write($"GeocodingService: HTTP {(int)response.StatusCode} for query '{query}'");
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var entries = JsonSerializer.Deserialize<List<NominatimEntry>>(json);
            if (entries is null || entries.Count == 0)
            {
                DebugLog.Write($"GeocodingService: no results for query '{query}'");
                return null;
            }

            var first = entries[0];
            if (!double.TryParse(first.Lat, System.Globalization.CultureInfo.InvariantCulture, out double lat)
                || !double.TryParse(first.Lon, System.Globalization.CultureInfo.InvariantCulture, out double lon))
            {
                DebugLog.Write($"GeocodingService: malformed lat/lon in response for query '{query}'");
                return null;
            }

            return new GeocodingResult(lat, lon, first.DisplayName);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            DebugLog.Write($"GeocodingService: lookup failed for query '{query}': {ex.Message}");
            return null;
        }
    }
}
