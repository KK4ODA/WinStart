using System.Text;

namespace WinStart.Core;

/// <summary>Small diagnostic log (winstart.log). Logging never throws.</summary>
internal static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new object();

    public static void Info(string message) => Write("INFO ", message);

    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception == null ? message : message + Environment.NewLine + exception);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                string path = AppPaths.LogFile;
                var file = new FileInfo(path);
                if (file.Exists && file.Length > MaxBytes)
                {
                    // Keep one previous log so the file never grows without bound.
                    string previous = path + ".1";
                    File.Delete(previous);
                    File.Move(path, previous);
                }

                File.AppendAllText(
                    path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Nothing useful can be done if the log itself cannot be written.
        }
    }
}
