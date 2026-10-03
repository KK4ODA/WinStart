using System.Reflection;
using WinStart.Update;

namespace WinStart.Core;

/// <summary>Product identity: name, version and where the project lives on GitHub.</summary>
internal static class AppInfo
{
    public const string Name = "WinStart";
    public const string RepoOwner = "KK4ODA";
    public const string RepoName = "WinStart";
    public const string RepoUrl = "https://github.com/" + RepoOwner + "/" + RepoName;
    public const string ReleasesUrl = RepoUrl + "/releases";
    public const string LatestReleaseApiUrl =
        "https://api.github.com/repos/" + RepoOwner + "/" + RepoName + "/releases/latest";

    /// <summary>The running version, e.g. "1.2.3" or "1.3.0-beta.1" (from Directory.Build.props).</summary>
    public static string Version { get; } = ReadVersion();

    public static SemVer SemVer { get; } =
        SemVer.TryParse(Version, out SemVer? parsed) ? parsed! : new SemVer(0, 0, 0);

    private static string ReadVersion()
    {
        Assembly assembly = typeof(AppInfo).Assembly;
        string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(version))
            version = assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        // The SDK appends "+<commit>" build metadata; it is not part of the version users see.
        int plus = version!.IndexOf('+');
        return plus >= 0 ? version.Substring(0, plus) : version;
    }
}
