using WinStart.Core;

namespace WinStart.Discovery;

/// <summary>An application found on this PC that the user can add to WinStart.</summary>
internal sealed class InstalledApp
{
    public string Name { get; set; } = "";

    /// <summary>Full path of the executable, or "shell:AppsFolder\&lt;id&gt;" for Store and other shell apps.</summary>
    public string TargetPath { get; set; } = "";

    public string Arguments { get; set; } = "";

    /// <summary>What to ask the shell for when loading the icon (a file path or a shell item name).</summary>
    public string IconSource { get; set; } = "";

    public bool IsShellApp => TargetPath.StartsWith(StartupEntry.ShellAppPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Second column of the picker.</summary>
    public string Location => IsShellApp ? "Windows app" : TargetPath;

    public StartupEntry ToEntry(int delaySeconds) => new StartupEntry
    {
        Name = Name,
        TargetPath = TargetPath,
        Arguments = Arguments,
        DelaySeconds = delaySeconds,
    };
}
