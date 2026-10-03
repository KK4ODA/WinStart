using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WinStart.Core;

namespace WinStart.Update;

/// <summary>
/// Downloads a release's installer, verifies it and hands over to it.
/// WinStart never replaces its own files: the installer does that after WinStart has exited,
/// so the running executable is never overwritten in place.
/// </summary>
internal static class UpdateInstaller
{
    /// <summary>
    /// Downloads the installer and checks it against the release's SHA-256 checksum.
    /// Returns the path of the verified installer. Progress is reported in percent.
    /// </summary>
    public static async Task<string> DownloadAsync(ReleaseInfo release, IProgress<int>? progress, CancellationToken cancellation)
    {
        if (!release.CanInstall)
            throw new UpdateException("This release has no installer that WinStart can install by itself.");

        string name = Path.GetFileName(release.InstallerName!);
        string target = Path.Combine(AppPaths.UpdateDir, name);
        string partial = target + ".part";

        try
        {
            Directory.CreateDirectory(AppPaths.UpdateDir);

            string sums = await UpdateChecker.Http.GetStringAsync(release.ChecksumsUrl).ConfigureAwait(false);
            string expected = Checksums.Find(sums, name)
                              ?? throw new UpdateException("The release does not publish a checksum for " + name + ", so it was not installed.");

            await DownloadFileAsync(release.InstallerUrl!, partial, release.InstallerSize, progress, cancellation).ConfigureAwait(false);

            string actual = await Task.Run(() => Checksums.Sha256OfFile(partial), cancellation).ConfigureAwait(false);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                Log.Error($"Update checksum mismatch for {name}: expected {expected}, got {actual}");
                throw new UpdateException("The download did not match its published checksum, so it was not installed. Please try again.");
            }

            File.Delete(target);
            File.Move(partial, target);
            Log.Info($"Downloaded and verified {name} (SHA-256 {actual})");
            return target;
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("The download failed. Check your internet connection and try again.", ex);
        }
        catch (IOException ex)
        {
            throw new UpdateException("The update could not be saved: " + ex.Message, ex);
        }
        finally
        {
            TryDelete(partial);
        }
    }

    /// <summary>
    /// Starts the installer. The caller must exit WinStart right afterwards; the installer
    /// replaces the program files and starts the new version.
    /// </summary>
    public static void RunInstaller(string installerPath)
    {
        // /SILENT shows only a progress window; errors are still displayed.
        Process.Start(new ProcessStartInfo(installerPath, "/SILENT /NORESTART") { UseShellExecute = true })?.Dispose();
        Log.Info("Started installer " + installerPath);
    }

    /// <summary>Removes installers left over from earlier updates.</summary>
    public static void CleanUp()
    {
        try
        {
            if (Directory.Exists(AppPaths.UpdateDir))
                Directory.Delete(AppPaths.UpdateDir, recursive: true);
        }
        catch (Exception)
        {
            // Still in use (for example by the installer that just started us); try again next time.
        }
    }

    private static async Task DownloadFileAsync(string url, string path, long expectedSize, IProgress<int>? progress, CancellationToken cancellation)
    {
        using HttpResponseMessage response = await UpdateChecker.Http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? expectedSize;
        using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long done = 0;
        int lastPercent = -1;
        int read;
        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer, 0, read, cancellation).ConfigureAwait(false);
            done += read;
            if (total <= 0) continue;

            int percent = (int)Math.Min(100, done * 100 / total);
            if (percent != lastPercent)
            {
                lastPercent = percent;
                progress?.Report(percent);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Left for CleanUp.
        }
    }
}
