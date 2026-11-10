using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace MonitorWellness.Core;

public sealed record UpdateInfo(Version Version, string TagName, Uri ReleaseUrl, bool IsPreRelease = false);

/// <summary>The outcome of an update check. A failed check (offline, timeout, HTTP error) is not the same as "up to date": the caller retries it at the next poll instead of waiting a day.</summary>
public readonly record struct UpdateCheckResult(UpdateInfo? Update, bool Failed);

/// <summary>
/// Zero-cost update notice: checks this repo's GitHub Releases API for the newest release tag and
/// compares it to the running assembly version (set via MonitorWellness.csproj's &lt;Version&gt;).
/// Deliberately notify-only — a balloon with a link to the release page, never a silent
/// download/install. A real auto-updater needs signed update packages so a user (and this app
/// itself) can trust what just got installed; that's a real cost item (see the code-signing
/// gap), not something to fake with an unsigned silent updater in the meantime.
///
/// It reads the release *list*, not "the latest release": GitHub's "latest" endpoint skips
/// pre-releases, and every release since 0.2.5 is one, so the check kept reporting 0.2.2 as the
/// newest version and could never tell anyone about 0.2.5 to 0.2.8. Pre-releases are included
/// unless the person turns that off (AppSettings.UpdateNotifyPreReleases).
///
/// Off by default and gated behind AppSettings.CheckForUpdatesEnabled — this is the one other
/// network call in the app besides the user-triggered location search (GeocodingService), so it
/// follows the same opt-in-by-default pattern as every other optional feature here (history
/// tracking, ambient light, rating prompts) rather than silently phoning home.
/// </summary>
public static class UpdateChecker
{
    // A page of eight releases. Their release notes are long, so the bound below is larger than
    // the one for the single-release request it replaced; it still stops a misbehaving or
    // compromised endpoint from making the app buffer an unbounded body into memory.
    private const string ReleasesApiUrl = "https://api.github.com/repos/cbroadhurst77/monitor-wellness/releases?per_page=8";
    private const int MaximumResponseBytes = 256 * 1024;
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
    /// Returns info about a newer release if one exists, or null if this is already the newest
    /// release (or the check failed for any reason). Never throws — this is a purely optional
    /// courtesy check that must never affect startup reliability, matching the same broad-catch
    /// reasoning already used for GeocodingService and AmbientLightSensor.
    /// </summary>
    public static async Task<UpdateCheckResult> CheckForUpdateAsync(bool includePreReleases, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client.GetAsync(ReleasesApiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                DebugLog.Write($"UpdateChecker: GitHub API returned HTTP {(int)response.StatusCode}");
                return new UpdateCheckResult(null, true);
            }
            if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaximumResponseBytes)
            {
                DebugLog.Write("UpdateChecker: response exceeded the allowed size");
                return new UpdateCheckResult(null, true);
            }

            string? json = await ReadResponseAtMostAsync(response.Content, cancellationToken);
            if (json is null)
            {
                DebugLog.Write("UpdateChecker: response exceeded the allowed size");
                return new UpdateCheckResult(null, true);
            }

            var currentVersion = typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new UpdateCheckResult(SelectLatest(json, currentVersion, includePreReleases), false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            DebugLog.Write($"UpdateChecker: check failed: {ex.Message}");
            return new UpdateCheckResult(null, true);
        }
    }

    /// <summary>
    /// From a GitHub releases list, the newest release that is newer than <paramref name="currentVersion"/>:
    /// drafts are ignored, pre-releases only count when <paramref name="includePreReleases"/> is set,
    /// and a tag that isn't a version is skipped. Malformed JSON gives null. Pure and testable.
    /// </summary>
    internal static UpdateInfo? SelectLatest(string json, Version currentVersion, bool includePreReleases)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            UpdateInfo? best = null;
            foreach (JsonElement release in doc.RootElement.EnumerateArray())
            {
                if (release.ValueKind != JsonValueKind.Object)
                    continue;
                if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                    continue;

                bool isPreRelease = release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True;
                if (isPreRelease && !includePreReleases)
                    continue;

                if (!release.TryGetProperty("tag_name", out var tagProp) || tagProp.GetString() is not string tagName
                    || !TryParseTag(tagName, out Version? version))
                {
                    continue;
                }

                if (version.CompareTo(currentVersion) <= 0 || (best is not null && version.CompareTo(best.Version) <= 0))
                    continue;

                Uri releaseUrl = release.TryGetProperty("html_url", out var urlProp)
                    && TryGetTrustedReleaseUrl(urlProp.GetString(), out var parsedUrl)
                        ? parsedUrl
                        : FallbackReleaseUrl;
                best = new UpdateInfo(version, tagName, releaseUrl, isPreRelease);
            }

            return best;
        }
        catch (JsonException ex)
        {
            DebugLog.Write($"UpdateChecker: couldn't read the release list: {ex.Message}");
            return null;
        }
    }

    /// <summary>Reads "v0.2.8" (or "0.2.8", or "v0.2.8-beta") as a version. Release tags here follow "vX.Y.Z". Pure and testable.</summary>
    internal static bool TryParseTag(string tag, out Version version)
    {
        string core = tag.Trim().TrimStart('v', 'V');
        int dash = core.IndexOf('-');
        int plus = core.IndexOf('+');
        int suffix = dash >= 0 && plus >= 0 ? Math.Min(dash, plus) : Math.Max(dash, plus); // whichever suffix marker comes first, if any
        if (suffix >= 0)
            core = core[..suffix];

        return Version.TryParse(core, out version!);
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
