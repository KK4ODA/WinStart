namespace WinStart.Core;

/// <summary>One application that WinStart launches after sign-in.</summary>
internal sealed class StartupEntry
{
    /// <summary>
    /// Prefix of entries launched through the shell rather than from an .exe path:
    /// Microsoft Store apps and other Start menu items identified by an AppUserModelID.
    /// </summary>
    public const string ShellAppPrefix = @"shell:AppsFolder\";

    public const int DefaultDelaySeconds = 30;
    public const int MaxDelaySeconds = 9999 * 60;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public int DelaySeconds { get; set; } = DefaultDelaySeconds;
    public bool Enabled { get; set; } = true;
    public bool StartMinimized { get; set; }

    public bool IsShellApp => TargetPath.StartsWith(ShellAppPrefix, StringComparison.OrdinalIgnoreCase);

    public string AppUserModelId => IsShellApp ? TargetPath.Substring(ShellAppPrefix.Length) : "";

    /// <summary>The executable path with environment variables such as %ProgramFiles% expanded.</summary>
    public string ResolvedPath => IsShellApp ? TargetPath : Environment.ExpandEnvironmentVariables(TargetPath);

    public StartupEntry Clone() => (StartupEntry)MemberwiseClone();

    public bool SameTargetAs(StartupEntry other) =>
        string.Equals(TargetPath, other.TargetPath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Arguments, other.Arguments, StringComparison.Ordinal);

    public Dictionary<string, object?> ToJson() => new()
    {
        ["id"] = Id,
        ["name"] = Name,
        ["path"] = TargetPath,
        ["arguments"] = Arguments,
        ["delaySeconds"] = DelaySeconds,
        ["enabled"] = Enabled,
        ["startMinimized"] = StartMinimized,
    };

    /// <summary>Reads an entry, tolerating missing fields. Returns null if there is no path to launch.</summary>
    public static StartupEntry? FromJson(IDictionary<string, object?> json)
    {
        string path = json.GetString("path").Trim();
        if (path.Length == 0) return null;

        var entry = new StartupEntry
        {
            TargetPath = path,
            Name = json.GetString("name").Trim(),
            Arguments = json.GetString("arguments"),
            DelaySeconds = Math.Max(0, Math.Min(MaxDelaySeconds, json.GetInt("delaySeconds", DefaultDelaySeconds))),
            Enabled = json.GetBool("enabled", true),
            StartMinimized = json.GetBool("startMinimized", false),
        };

        string id = json.GetString("id").Trim();
        if (id.Length > 0) entry.Id = id;
        if (entry.Name.Length == 0) entry.Name = DefaultName(path);
        return entry;
    }

    private static string DefaultName(string path)
    {
        if (path.StartsWith(ShellAppPrefix, StringComparison.OrdinalIgnoreCase))
            return path.Substring(ShellAppPrefix.Length);
        try
        {
            return Path.GetFileNameWithoutExtension(path);
        }
        catch (ArgumentException)
        {
            return path;
        }
    }
}
