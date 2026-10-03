namespace WinStart.Core;

/// <summary>User preferences that apply to WinStart as a whole.</summary>
internal sealed class AppSettings
{
    public bool StartMinimized { get; set; } = true;
    public bool WaitForNetwork { get; set; }
    public int NetworkTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// False: every delay is measured from sign-in. True: applications launch one after another,
    /// each delay counted from the launch of the previous application.
    /// </summary>
    public bool Sequential { get; set; }

    public bool SkipIfRunning { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;
    public string SkippedVersion { get; set; } = "";
    public bool TrayHintShown { get; set; }
}

/// <summary>Everything stored in config.json.</summary>
internal sealed class AppConfig
{
    public const int SchemaVersion = 1;

    public AppSettings Settings { get; } = new();
    public List<StartupEntry> Apps { get; } = new();

    public StartupEntry? Find(string id) => Apps.FirstOrDefault(a => a.Id == id);

    public string ToJson()
    {
        var root = new Dictionary<string, object?>
        {
            ["schemaVersion"] = SchemaVersion,
            ["settings"] = new Dictionary<string, object?>
            {
                ["startMinimized"] = Settings.StartMinimized,
                ["waitForNetwork"] = Settings.WaitForNetwork,
                ["networkTimeoutSeconds"] = Settings.NetworkTimeoutSeconds,
                ["launchMode"] = Settings.Sequential ? "sequential" : "independent",
                ["skipIfRunning"] = Settings.SkipIfRunning,
                ["autoCheckUpdates"] = Settings.AutoCheckUpdates,
                ["skippedVersion"] = Settings.SkippedVersion,
                ["trayHintShown"] = Settings.TrayHintShown,
            },
            ["apps"] = Apps.Select(a => a.ToJson()).ToList(),
        };
        return Json.Write(root) + "\r\n";
    }

    /// <summary>
    /// Reads a configuration. Missing or mistyped values fall back to their defaults so a
    /// hand-edited or older file still loads; only text that is not JSON at all throws.
    /// </summary>
    public static AppConfig FromJson(string text)
    {
        IDictionary<string, object?> root = Json.ParseObject(text);
        var config = new AppConfig();
        AppSettings s = config.Settings;

        if (root.GetObject("settings") is { } settings)
        {
            s.StartMinimized = settings.GetBool("startMinimized", s.StartMinimized);
            s.WaitForNetwork = settings.GetBool("waitForNetwork", s.WaitForNetwork);
            s.NetworkTimeoutSeconds = Math.Max(5, Math.Min(3600, settings.GetInt("networkTimeoutSeconds", s.NetworkTimeoutSeconds)));
            s.Sequential = string.Equals(settings.GetString("launchMode"), "sequential", StringComparison.OrdinalIgnoreCase);
            s.SkipIfRunning = settings.GetBool("skipIfRunning", s.SkipIfRunning);
            s.AutoCheckUpdates = settings.GetBool("autoCheckUpdates", s.AutoCheckUpdates);
            s.SkippedVersion = settings.GetString("skippedVersion");
            s.TrayHintShown = settings.GetBool("trayHintShown", s.TrayHintShown);
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (object? item in root.GetArray("apps"))
        {
            if (item is not IDictionary<string, object?> json) continue;
            StartupEntry? entry = StartupEntry.FromJson(json);
            if (entry == null) continue;
            if (!ids.Add(entry.Id))
            {
                // Ids must be unique; a copy-pasted block in a hand-edited file gets a fresh one.
                entry.Id = Guid.NewGuid().ToString("N");
                ids.Add(entry.Id);
            }

            config.Apps.Add(entry);
        }

        return config;
    }
}
