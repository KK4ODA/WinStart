using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WinStart.Core;

namespace WinStart.Update;

/// <summary>An error with a message that can be shown to the user as-is.</summary>
internal sealed class UpdateException : Exception
{
    public UpdateException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>A published GitHub release of WinStart.</summary>
internal sealed class ReleaseInfo
{
    public SemVer Version { get; set; } = new SemVer(0, 0, 0);
    public string PageUrl { get; set; } = AppInfo.ReleasesUrl;
    public string Notes { get; set; } = "";
    public string? InstallerName { get; set; }
    public string? InstallerUrl { get; set; }
    public long InstallerSize { get; set; }
    public string? ChecksumsUrl { get; set; }

    /// <summary>True when the release carries an installer and checksums WinStart can install by itself.</summary>
    public bool CanInstall => InstallerUrl != null && ChecksumsUrl != null;
}

/// <summary>Asks GitHub Releases whether a newer WinStart exists.</summary>
internal static class UpdateChecker
{
    public const string ChecksumsFileName = "SHA256SUMS.txt";

    // Only files published under this repository's releases are ever downloaded.
    private const string DownloadPrefix = AppInfo.RepoUrl + "/releases/download/";
    private static readonly Regex InstallerPattern =
        new Regex(@"^WinStart-Setup-[0-9A-Za-z.\-]+\.exe$", RegexOptions.CultureInvariant);

    internal static readonly HttpClient Http = CreateClient();

    public static bool IsNewer(ReleaseInfo release) => release.Version.CompareTo(AppInfo.SemVer) > 0;

    /// <summary>
    /// Returns the latest stable release, or null if nothing has been published yet.
    /// Pre-releases and drafts are never offered (GitHub's "latest" excludes them).
    /// </summary>
    public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellation)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, AppInfo.LatestReleaseApiUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using HttpResponseMessage response = await Http.SendAsync(request, cancellation).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
                throw new UpdateException("GitHub is limiting requests from this network at the moment. Please try again later.");
            response.EnsureSuccessStatusCode();

            return ParseRelease(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("GitHub could not be reached. Check your internet connection and try again.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellation.IsCancellationRequested)
        {
            throw new UpdateException("GitHub did not answer in time. Please try again later.", ex);
        }
    }

    /// <summary>Reads the fields WinStart needs from GitHub's "release" JSON.</summary>
    internal static ReleaseInfo ParseRelease(string json)
    {
        IDictionary<string, object?> root;
        try
        {
            root = Json.ParseObject(json);
        }
        catch (Exception ex)
        {
            throw new UpdateException("GitHub returned an answer WinStart could not understand.", ex);
        }

        string tag = root.GetString("tag_name");
        if (!SemVer.TryParse(tag, out SemVer? version))
            throw new UpdateException("The latest release has an unexpected version number (" + tag + ").");

        var release = new ReleaseInfo
        {
            Version = version!,
            Notes = root.GetString("body").Trim(),
        };

        string page = root.GetString("html_url");
        if (page.StartsWith(AppInfo.RepoUrl + "/", StringComparison.OrdinalIgnoreCase)) release.PageUrl = page;

        foreach (object? item in root.GetArray("assets"))
        {
            if (item is not IDictionary<string, object?> asset) continue;
            string name = asset.GetString("name");
            string url = asset.GetString("browser_download_url");
            if (!url.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase)) continue;

            if (InstallerPattern.IsMatch(name))
            {
                release.InstallerName = name;
                release.InstallerUrl = url;
                release.InstallerSize = asset.GetLong("size", 0);
            }
            else if (name.Equals(ChecksumsFileName, StringComparison.OrdinalIgnoreCase))
            {
                release.ChecksumsUrl = url;
            }
        }

        return release;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.Name + "/" + AppInfo.Version);
        return client;
    }
}
