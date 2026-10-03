using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinStart.Core;

namespace WinStart.Discovery;

/// <summary>
/// Finds the applications installed on this PC, the way a user thinks of them:
/// 1. the Start menu's "All apps" list (shortcuts and Microsoft Store apps),
/// 2. programs registered under "App Paths",
/// 3. installed programs listed in "Apps &amp; features" (the Uninstall registry keys).
/// </summary>
internal static class AppDiscovery
{
    private const string AppPathsKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    // File-name fragments of installers, updaters and other helpers that are not applications in their own right.
    private static readonly string[] HelperWords =
    {
        "unins", "uninst", "setup", "install", "update", "redist", "crashpad", "crashreport",
        "helper", "repair", "msiexec", "bootstrap", "dpinst",
    };

    // Folders that hold Windows components, package internals and installer caches rather than applications.
    private static readonly string[] HelperFolders =
    {
        @"\WindowsApps\", @"\Package Cache\", @"\DIFX\", @"\Common Files\", @"\Internet Explorer\",
        @"\Windows Mail\", @"\Windows NT\", @"\Windows Media Player\", @"\Windows Photo Viewer\",
    };

    /// <summary>Returns every application found, sorted by name. Call from an STA thread.</summary>
    public static List<InstalledApp> FindAll()
    {
        var apps = new List<InstalledApp>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // "path|arguments"
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        // The Start menu is authoritative. The registry sources only fill in programs it does not
        // show, so they never add a second entry for a known executable or a known name.
        void Add(InstalledApp app, bool fillInOnly)
        {
            if (app.Name.Length == 0 || app.TargetPath.Length == 0) return;
            if (!app.IsShellApp) app.TargetPath = NormalizePath(app.TargetPath);
            if (fillInOnly && (seenPaths.Contains(app.TargetPath) || seenNames.Contains(app.Name))) return;
            if (!seenTargets.Add(app.TargetPath + "|" + app.Arguments)) return;
            seenPaths.Add(app.TargetPath);
            seenNames.Add(app.Name);
            apps.Add(app);
        }

        RunSource("Start menu", () =>
        {
            foreach (InstalledApp app in FromStartMenu()) Add(app, fillInOnly: false);
        });
        RunSource("App Paths", () =>
        {
            foreach (InstalledApp app in FromAppPaths()) Add(app, fillInOnly: true);
        });
        RunSource("installed programs", () =>
        {
            foreach (InstalledApp app in FromInstalledPrograms()) Add(app, fillInOnly: true);
        });

        apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return apps;
    }

    private static void RunSource(string name, Action source)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            source();
            Log.Info($"Application discovery: {name} took {watch.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            // One failing source must not empty the whole list.
            Log.Error("Application discovery failed for " + name, ex);
        }
    }

    /// <summary>
    /// Enumerates shell:AppsFolder, the folder behind the Start menu's "All apps" list. It merges
    /// per-user and all-users shortcuts with Store apps and provides the friendly display names.
    /// </summary>
    private static List<InstalledApp> FromStartMenu()
    {
        var result = new List<InstalledApp>();
        Type? shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType == null) return result;

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic? folder = shell.NameSpace("shell:AppsFolder");
            if (folder == null) return result;

            foreach (dynamic item in folder.Items())
            {
                try
                {
                    string name = ((string?)item.Name ?? "").Trim();
                    string id = (string?)item.Path ?? "";
                    if (name.Length == 0 || id.Length == 0) continue;

                    string target = item.ExtendedProperty("System.Link.TargetParsingPath") as string ?? "";
                    string arguments = item.ExtendedProperty("System.Link.Arguments") as string ?? "";
                    string shellName = StartupEntry.ShellAppPrefix + id;

                    if (target.Length > 0)
                    {
                        // A classic shortcut. Keep programs; skip documents, web links and uninstallers.
                        if (!IsExe(target) || LooksLikeUninstaller(name, target)) continue;
                        // The icon comes from the program file: asking the shell for the Start menu item's
                        // own icon is roughly ten times slower, which adds up over a few hundred entries.
                        result.Add(new InstalledApp { Name = name, TargetPath = target, Arguments = arguments, IconSource = target });
                    }
                    else if (id.IndexOf('!') > 0)
                    {
                        // A packaged (Microsoft Store) app; it has no .exe to point at and is launched by its id.
                        result.Add(new InstalledApp { Name = name, TargetPath = shellName, IconSource = shellName });
                    }
                }
                catch (Exception)
                {
                    // Skip items the shell cannot describe.
                }
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }

        return result;
    }

    private static List<InstalledApp> FromAppPaths()
    {
        var result = new List<InstalledApp>();
        foreach (RegistryKey root in OpenAll(AppPathsKey))
        {
            using (root)
            {
                foreach (string name in root.GetSubKeyNames())
                {
                    try
                    {
                        using RegistryKey? key = root.OpenSubKey(name);
                        string? exe = ExePathFromCommand(key?.GetValue(null) as string);
                        if (exe == null || !IsLikelyApplication(exe) || !File.Exists(exe)) continue;
                        result.Add(new InstalledApp { Name = FriendlyName(exe), TargetPath = exe, IconSource = exe });
                    }
                    catch (Exception)
                    {
                        // Unreadable entry.
                    }
                }
            }
        }

        return result;
    }

    private static List<InstalledApp> FromInstalledPrograms()
    {
        var result = new List<InstalledApp>();
        foreach (RegistryKey root in OpenAll(UninstallKey))
        {
            using (root)
            {
                foreach (string name in root.GetSubKeyNames())
                {
                    try
                    {
                        using RegistryKey? key = root.OpenSubKey(name);
                        if (key == null) continue;
                        if (key.GetValue("DisplayName") is not string displayName || displayName.Trim().Length == 0) continue;
                        if (key.GetValue("SystemComponent") is int hidden && hidden == 1) continue;
                        if (key.GetValue("ParentKeyName") is string) continue; // an update to another product
                        if (displayName.IndexOf("driver", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                        string? exe = ExePathFromCommand(key.GetValue("DisplayIcon") as string);
                        if (exe == null || !IsLikelyApplication(exe) || !File.Exists(exe))
                            exe = MainExeInFolder(key.GetValue("InstallLocation") as string, displayName);
                        if (exe == null) continue;

                        result.Add(new InstalledApp { Name = displayName.Trim(), TargetPath = exe, IconSource = exe });
                    }
                    catch (Exception)
                    {
                        // Unreadable entry.
                    }
                }
            }
        }

        return result;
    }

    private static List<RegistryKey> OpenAll(string subKey)
    {
        var keys = new List<RegistryKey>();
        var locations = new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
        };
        foreach ((RegistryHive hive, RegistryView view) in locations)
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                RegistryKey? key = baseKey.OpenSubKey(subKey);
                if (key != null) keys.Add(key);
            }
            catch (Exception)
            {
                // Hive or view not available.
            }
        }

        return keys;
    }

    /// <summary>
    /// Picks the program in an install folder: the only candidate, or the one named like the product.
    /// </summary>
    private static string? MainExeInFolder(string? folder, string displayName)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        folder = Environment.ExpandEnvironmentVariables(folder!.Trim().Trim('"'));
        try
        {
            if (!Directory.Exists(folder) || Path.GetPathRoot(folder) == folder) return null;
            List<string> candidates = Directory.EnumerateFiles(folder, "*.exe").Where(IsLikelyApplication).Take(50).ToList();
            if (candidates.Count == 1) return candidates[0];

            string product = LettersAndDigits(displayName);
            return candidates.FirstOrDefault(c =>
            {
                string file = LettersAndDigits(Path.GetFileNameWithoutExtension(c));
                return file.Length >= 3 && product.Contains(file);
            });
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the .exe from registry values such as <c>"C:\App\app.exe",0</c> or <c>C:\App\app.exe /flag</c>.
    /// Returns null when the value does not point at an .exe.
    /// </summary>
    internal static string? ExePathFromCommand(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string text = Environment.ExpandEnvironmentVariables(value!.Trim());

        if (text[0] == '"')
        {
            int close = text.IndexOf('"', 1);
            if (close < 0) return null;
            text = text.Substring(1, close - 1);
        }
        else
        {
            int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe < 0) return null;
            text = text.Substring(0, exe + 4);
        }

        text = text.Trim();
        return IsExe(text) && Path.IsPathRooted(text) ? text : null;
    }

    /// <summary>False for installers, updaters, Windows components and other programs users do not launch.</summary>
    internal static bool IsLikelyApplication(string exePath)
    {
        string file = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
        if (HelperWords.Any(file.Contains)) return false;
        if (HelperFolders.Any(folder => exePath.IndexOf(folder, StringComparison.OrdinalIgnoreCase) >= 0)) return false;

        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
        return !exePath.StartsWith(windows, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool LooksLikeUninstaller(string name, string target)
    {
        if (name.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        string file = Path.GetFileNameWithoutExtension(target);
        return file.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
               || file.Equals("msiexec", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The name a program gives itself (its file description), or its file name.</summary>
    internal static string FriendlyName(string exePath)
    {
        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(exePath);
            string? description = info.FileDescription?.Trim();
            if (!string.IsNullOrEmpty(description)) return description!;
            string? product = info.ProductName?.Trim();
            if (!string.IsNullOrEmpty(product)) return product!;
        }
        catch (Exception)
        {
            // Fall back to the file name.
        }

        return Path.GetFileNameWithoutExtension(exePath);
    }

    private static bool IsExe(string path) => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Removes differences such as doubled backslashes so the same file compares equal.</summary>
    internal static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    private static string LettersAndDigits(string text) =>
        new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
