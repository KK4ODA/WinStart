namespace WinStart.Core;

/// <summary>Where WinStart keeps its files. Everything the user owns lives in one folder.</summary>
internal static class AppPaths
{
    /// <summary>Environment variable that redirects the data folder (portable use, development, testing).</summary>
    public const string DataDirVariable = "WINSTART_DATA_DIR";

    /// <summary>True when the data folder is the standard %AppData%\WinStart.</summary>
    public static bool IsDefaultDataDir { get; private set; } = true;

    public static string DataDir { get; } = ResolveDataDir();

    public static string ConfigFile => Path.Combine(DataDir, "config.json");
    public static string HistoryFile => Path.Combine(DataDir, "history.jsonl");
    public static string LogFile => Path.Combine(DataDir, "winstart.log");

    /// <summary>Scratch folder for downloaded installers; safe to delete at any time.</summary>
    public static string UpdateDir => Path.Combine(Path.GetTempPath(), "WinStart-Update");

    private static string ResolveDataDir()
    {
        string? overrideDir = Environment.GetEnvironmentVariable(DataDirVariable);
        string dir;
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            dir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(overrideDir!.Trim()));
            IsDefaultDataDir = false;
        }
        else
        {
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name);
        }

        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception)
        {
            // Reported when the first read or write of a file in the folder fails.
        }

        return dir;
    }
}
