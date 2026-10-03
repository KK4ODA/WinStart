using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace WinStart.Core;

internal enum LaunchOutcome
{
    Launched,
    AlreadyRunning,
    Failed,
}

internal sealed class LaunchResult
{
    public LaunchResult(LaunchOutcome outcome, string message = "", string? detail = null)
    {
        Outcome = outcome;
        Message = message;
        Detail = detail ?? message;
    }

    public LaunchOutcome Outcome { get; }

    /// <summary>Short reason, shown in the Status column.</summary>
    public string Message { get; }

    /// <summary>Full reason, recorded in the history and the log.</summary>
    public string Detail { get; }
}

/// <summary>Starts one application.</summary>
internal static class Launcher
{
    private const int ERROR_CANCELLED = 1223;

    /// <summary>Launches off the UI thread; inspecting running processes can take a moment.</summary>
    public static Task<LaunchResult> LaunchAsync(StartupEntry entry, bool skipIfRunning) =>
        Task.Run(() => Launch(entry, skipIfRunning));

    public static LaunchResult Launch(StartupEntry entry, bool skipIfRunning)
    {
        try
        {
            if (skipIfRunning && ProcessHelper.IsRunning(entry))
                return new LaunchResult(LaunchOutcome.AlreadyRunning);

            ProcessStartInfo start;
            if (entry.IsShellApp)
            {
                // Explorer activates Store apps and other Start menu items by their AppUserModelID.
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                start = new ProcessStartInfo(explorer, "\"" + entry.TargetPath + "\"") { UseShellExecute = false };
            }
            else
            {
                string path = entry.ResolvedPath;
                if (!File.Exists(path))
                    return new LaunchResult(LaunchOutcome.Failed, "File not found", "File not found: " + path);

                // ShellExecute behaves like a double-click: it honours "run as administrator"
                // manifests (showing the usual prompt) and file associations.
                start = new ProcessStartInfo(path, Environment.ExpandEnvironmentVariables(entry.Arguments))
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(path) ?? "",
                    WindowStyle = entry.StartMinimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal,
                };
            }

            using (Process.Start(start))
            {
            }

            return new LaunchResult(LaunchOutcome.Launched);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {
            return new LaunchResult(LaunchOutcome.Failed, "Permission prompt cancelled", "Cancelled at the administrator permission prompt");
        }
        catch (Exception ex)
        {
            return new LaunchResult(LaunchOutcome.Failed, ex.Message);
        }
    }
}
