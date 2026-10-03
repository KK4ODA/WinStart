using System.Text;

namespace WinStart.Core;

/// <summary>Loads and saves config.json.</summary>
internal sealed class ConfigStore
{
    private readonly string _path;

    public ConfigStore(string path)
    {
        _path = path;
    }

    /// <summary>True when no configuration existed yet (first run).</summary>
    public bool CreatedNew { get; private set; }

    /// <summary>Set when the existing file could not be read; a message suitable for the user.</summary>
    public string? LoadWarning { get; private set; }

    public AppConfig Load()
    {
        if (!File.Exists(_path))
        {
            CreatedNew = true;
            return new AppConfig();
        }

        try
        {
            return AppConfig.FromJson(File.ReadAllText(_path, Encoding.UTF8));
        }
        catch (Exception ex)
        {
            // Never silently discard what the user had: keep a copy before defaults overwrite it.
            string backup = _path + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try
            {
                File.Copy(_path, backup, overwrite: true);
            }
            catch (Exception copyError)
            {
                Log.Error("Could not back up the unreadable settings file", copyError);
            }

            Log.Error("Could not read " + _path + "; starting with default settings", ex);
            LoadWarning = "The settings file could not be read, so WinStart started with default settings. "
                          + "A copy of the old file was kept as " + Path.GetFileName(backup) + ".";
            return new AppConfig();
        }
    }

    /// <summary>Writes the configuration atomically, so a crash or power loss cannot leave half a file.</summary>
    public void Save(AppConfig config)
    {
        string? folder = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        string temp = _path + ".tmp";
        File.WriteAllText(temp, config.ToJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (File.Exists(_path))
            File.Replace(temp, _path, destinationBackupFileName: null);
        else
            File.Move(temp, _path);
    }
}
