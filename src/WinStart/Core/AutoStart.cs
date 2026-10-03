using System.Reflection;
using Microsoft.Win32;

namespace WinStart.Core;

/// <summary>
/// Registers WinStart to start when the user signs in, using the per-user Run key.
/// No administrator rights are needed, and the entry shows up in Task Manager's Startup apps.
/// </summary>
internal static class AutoStart
{
    public const string StartupArgument = "--startup";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = AppInfo.Name;

    private static string Command =>
        "\"" + (Assembly.GetEntryAssembly() ?? typeof(AutoStart).Assembly).Location + "\" " + StartupArgument;

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(ValueName) is not string command || command.Length == 0) return false;

            // Task Manager and Settings record "disabled" here instead of deleting the Run value:
            // the first byte is odd when the user switched the entry off.
            using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            if (approved?.GetValue(ValueName) is byte[] state && state.Length > 0 && (state[0] & 1) != 0)
                return false;

            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not read the startup registration", ex);
            return false;
        }
    }

    public static void Enable()
    {
        using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey))
            run.SetValue(ValueName, Command, RegistryValueKind.String);

        // Clear a "disabled" marker left by Task Manager, otherwise Windows would keep ignoring the entry.
        using RegistryKey? approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static void Disable()
    {
        using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        run?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
