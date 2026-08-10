using System.Net.Http;
using System.Text.Json;

namespace MonitorWellness.Core;

public sealed record UpdateInfo(Version Version, string TagName, Uri ReleaseUrl);

/// <summary>
/// Zero-cost update notice: checks this repo's GitHub Releases API for the latest tag and
/// compares it to the running assembly version (set via MonitorWellness.csproj's &lt;Version&gt;).
/// Deliberately notify-only — a balloon with a link to the release page, never a silent
/// download/install. A real auto-updater needs signed update packages so a user (and this app
/// itself) can trust what just got installed; that's a real cost item (see the code-signing
/// gap), not something to fake with an unsigned silent updater in the meantime.
///
/// Off by default and gated behind AppSettings.CheckForUpdatesEnabled — this is the one other
/// network call in the app besides the user-triggered location search (GeocodingService), so it
/// follows the same opt-in-by-default pattern as every other optional feature here (history
/// tracking, ambient light, rating prompts) rather than silently phoning home.
/// </summary>
public static class UpdateChecker
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/cbroadhurst77/monitor-wellness/releases/latest";
    private static readonly Uri FallbackReleaseUrl = new("https://github.com/cbroadhurst77/monitor-wellness/releases/latest");

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // GitHub's REST API rejects requests with no descriptive User-Agent — same requirement
        // and same identifying string convention as GeocodingService's Nominatim client.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MonitorWellness/1.0 (+https://github.com/cbroadhurst77/monitor-wellness)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>
    /// Returns info about a newer release if one exists, or null if this is already the latest
    /// release (or the check failed for any reason). Never throws — this is a purely optional
    /// courtesy check that must never affect startup reliability, matching the same broad-catch
    /// reasoning already used for GeocodingService and AmbientLightSensor.
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client.GetAsync(ReleasesApiUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                DebugLog.Write($"UpdateChecker: GitHub API returned HTTP {(int)response.StatusCode}");
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("tag_name", out var tagProp) || tagProp.GetString() is not string tagName || tagName.Length == 0)
            {
                DebugLog.Write("UpdateChecker: release response had no tag_name");
                return null;
            }

            // Release tags here follow "vX.Y.Z" (see how this repo's own releases are cut) --
            // trimming a leading v/V so Version.Parse sees a plain "X.Y.Z".
            if (!Version.TryParse(tagName.TrimStart('v', 'V'), out var latestVersion))
            {
                DebugLog.Write($"UpdateChecker: couldn't parse tag '{tagName}' as a version");
                return null;
            }

            var currentVersion = typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);
            if (latestVersion.CompareTo(currentVersion) <= 0)
                return null;

            Uri releaseUrl = doc.RootElement.TryGetProperty("html_url", out var urlProp)
                && TryGetTrustedReleaseUrl(urlProp.GetString(), out var parsedUrl)
                    ? parsedUrl
                    : FallbackReleaseUrl;

            return new UpdateInfo(latestVersion, tagName, releaseUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            DebugLog.Write($"UpdateChecker: check failed: {ex.Message}");
            return null;
        }
    }

    internal static bool TryGetTrustedReleaseUrl(string? candidate, out Uri releaseUrl)
    {
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var parsedUrl)
            && parsedUrl.Scheme == Uri.UriSchemeHttps
            && string.Equals(parsedUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            && parsedUrl.AbsolutePath.StartsWith("/cbroadhurst77/monitor-wellness/releases/", StringComparison.Ordinal))
        {
            releaseUrl = parsedUrl;
            return true;
        }

        releaseUrl = FallbackReleaseUrl;
        return false;
    }
}
