using System.IO;
using System.Net.Http;
using System.Text;
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
    private const int MaximumQueryLength = 256;
    private const int MaximumResponseBytes = 64 * 1024;
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MonitorWellness/1.0 (+https://github.com/cbroadhurst77/monitor-wellness)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
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
    public static async Task<GeocodingResult?> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > MaximumQueryLength)
            return null;

        string url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(query)}&format=json&limit=1";

        try
        {
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                DebugLog.Write($"GeocodingService: HTTP {(int)response.StatusCode}");
                return null;
            }
            if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaximumResponseBytes)
            {
                DebugLog.Write("GeocodingService: response exceeded the allowed size");
                return null;
            }

            string? json = await ReadResponseAtMostAsync(response.Content, cancellationToken);
            if (json is null)
            {
                DebugLog.Write("GeocodingService: response exceeded the allowed size");
                return null;
            }
            var entries = JsonSerializer.Deserialize<List<NominatimEntry>>(json);
            if (entries is null || entries.Count == 0)
            {
                DebugLog.Write("GeocodingService: no results");
                return null;
            }

            var first = entries[0];
            if (!TryCreateResult(first.Lat, first.Lon, first.DisplayName, out var result))
            {
                DebugLog.Write("GeocodingService: response had invalid coordinates");
                return null;
            }

            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            DebugLog.Write($"GeocodingService: lookup failed: {ex.Message}");
            return null;
        }
    }

    internal static bool TryCreateResult(string? latitudeText, string? longitudeText, string? displayName, out GeocodingResult? result)
    {
        result = null;
        if (!double.TryParse(latitudeText, System.Globalization.CultureInfo.InvariantCulture, out double latitude)
            || !double.TryParse(longitudeText, System.Globalization.CultureInfo.InvariantCulture, out double longitude)
            || !double.IsFinite(latitude) || !double.IsFinite(longitude)
            || latitude is < -90 or > 90 || longitude is < -180 or > 180
            || string.IsNullOrWhiteSpace(displayName) || displayName.Length > 512)
            return false;

        result = new GeocodingResult(latitude, longitude, displayName.Trim());
        return true;
    }

    private static async Task<string?> ReadResponseAtMostAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        byte[] readBuffer = new byte[8 * 1024];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(readBuffer, cancellationToken)) > 0)
        {
            if (buffer.Length + bytesRead > MaximumResponseBytes)
                return null;

            buffer.Write(readBuffer, 0, bytesRead);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
