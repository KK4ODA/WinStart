using System.Diagnostics;
using System.Text;
using WinStart.Native;

namespace WinStart.Core;

/// <summary>Answers "is this application already running in my session?".</summary>
internal static class ProcessHelper
{
    public static bool IsRunning(StartupEntry entry)
    {
        int session;
        using (Process current = Process.GetCurrentProcess())
            session = current.SessionId;

        if (entry.IsShellApp)
        {
            // Packaged apps have an AppUserModelID of the form "<PackageFamilyName>!<AppId>".
            string id = entry.AppUserModelId;
            int bang = id.IndexOf('!');
            return bang > 0 && IsPackageRunning(id.Substring(0, bang), session);
        }

        string path = entry.ResolvedPath;
        string processName;
        try
        {
            processName = Path.GetFileNameWithoutExtension(path);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (string.IsNullOrEmpty(processName)) return false;

        foreach (Process process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != session) continue;

                    // A process we cannot inspect (elevated or protected) is matched by name alone.
                    string? image = GetImagePath(process.Id);
                    if (image == null || SamePath(image, path)) return true;
                }
                catch (Exception)
                {
                    // The process exited while we were looking at it.
                }
            }
        }

        return false;
    }

    private static bool IsPackageRunning(string packageFamilyName, int session)
    {
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != session) continue;
                    string? family = GetPackageFamilyName(process.Id);
                    if (family != null && string.Equals(family, packageFamilyName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (Exception)
                {
                    // The process exited while we were looking at it.
                }
            }
        }

        return false;
    }

    private static string? GetImagePath(int processId)
    {
        IntPtr handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static string? GetPackageFamilyName(int processId)
    {
        IntPtr handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            uint length = 0;
            if (NativeMethods.GetPackageFamilyName(handle, ref length, null) != NativeMethods.ERROR_INSUFFICIENT_BUFFER || length == 0)
                return null; // not a packaged process

            var buffer = new StringBuilder((int)length);
            return NativeMethods.GetPackageFamilyName(handle, ref length, buffer) == 0 ? buffer.ToString() : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
