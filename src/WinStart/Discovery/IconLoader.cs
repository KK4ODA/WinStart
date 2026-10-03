using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using WinStart.Core;
using WinStart.Native;

namespace WinStart.Discovery;

/// <summary>Loads application icons from the Windows shell.</summary>
internal static class IconLoader
{
    private static readonly Guid ImageFactoryId = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>
    /// Returns the icon the shell shows for a file path or shell item ("shell:AppsFolder\..."),
    /// or null if it has none. Call from an STA thread.
    /// </summary>
    public static Bitmap? Load(string source, int size)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        IShellItemImageFactory? factory = null;
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            Guid id = ImageFactoryId;
            string name = source.StartsWith(StartupEntry.ShellAppPrefix, StringComparison.OrdinalIgnoreCase)
                ? source
                : Environment.ExpandEnvironmentVariables(source);
            if (NativeMethods.SHCreateItemFromParsingName(name, IntPtr.Zero, ref id, out factory) != 0 || factory == null)
                return null;

            var wanted = new NativeMethods.SIZE { cx = size, cy = size };
            if (factory.GetImage(wanted, NativeMethods.SIIGBF_ICONONLY, out bitmap) != 0 || bitmap == IntPtr.Zero)
                return null;

            return ToBitmap(bitmap);
        }
        catch (Exception ex)
        {
            Log.Warn("Could not load the icon for " + source + ": " + ex.Message);
            return null;
        }
        finally
        {
            if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
            if (factory != null) Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>Copies a GDI bitmap into a managed one, keeping its transparency.</summary>
    private static Bitmap ToBitmap(IntPtr source)
    {
        var info = new NativeMethods.BITMAP();
        NativeMethods.GetObject(source, Marshal.SizeOf(typeof(NativeMethods.BITMAP)), ref info);
        int width = info.bmWidth, height = info.bmHeight;

        var header = new NativeMethods.BITMAPINFO
        {
            biSize = 40,
            biWidth = width,
            biHeight = -height, // top-down rows, whatever the source orientation
            biPlanes = 1,
            biBitCount = 32,
        };
        var pixels = new byte[width * height * 4];
        IntPtr dc = NativeMethods.GetDC(IntPtr.Zero);
        try
        {
            if (NativeMethods.GetDIBits(dc, source, 0, (uint)height, pixels, ref header, 0) == 0)
                return Image.FromHbitmap(source);
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, dc);
        }

        // Shell icons carry premultiplied alpha; an all-zero alpha channel means "opaque".
        bool hasAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                hasAlpha = true;
                break;
            }
        }

        var result = new Bitmap(width, height, hasAlpha ? PixelFormat.Format32bppPArgb : PixelFormat.Format32bppRgb);
        BitmapData data = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, result.PixelFormat);
        try
        {
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            result.UnlockBits(data);
        }

        return result;
    }
}

/// <summary>
/// Loads icons on a background thread so lists appear immediately and fill in their icons as they arrive.
/// Create and call it on the UI thread; callbacks run on the UI thread.
/// </summary>
internal sealed class IconService : IDisposable
{
    private sealed class Job
    {
        public string Key = "";
        public string Source = "";
        public int Size;
        public readonly List<Action<Bitmap?>> Callbacks = new();
    }

    private readonly object _gate = new object();
    private readonly LinkedList<Job> _queue = new();                                                   // guarded by _gate
    private readonly Dictionary<string, LinkedListNode<Job>> _pending = new(StringComparer.OrdinalIgnoreCase); // guarded by _gate
    private readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);       // UI thread only
    private readonly SemaphoreSlim _available = new SemaphoreSlim(0);
    private readonly SynchronizationContext _ui;
    private volatile bool _disposed;

    public IconService()
    {
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("IconService must be created on the UI thread.");
        var worker = new Thread(Work) { IsBackground = true, Name = "Icon loader" };
        worker.SetApartmentState(ApartmentState.STA); // required by the shell
        worker.Start();
    }

    /// <summary>
    /// Requests the icon for a file path or shell item. <paramref name="loaded"/> receives the icon,
    /// or null if there is none; the bitmap is shared and must not be disposed by the caller.
    /// </summary>
    public void Request(string source, int size, Action<Bitmap?> loaded)
    {
        string key = KeyOf(source, size);
        if (_cache.TryGetValue(key, out Bitmap? cached))
        {
            loaded(cached);
            return;
        }

        lock (_gate)
        {
            if (_pending.TryGetValue(key, out LinkedListNode<Job>? waiting))
            {
                waiting.Value.Callbacks.Add(loaded);
                return;
            }

            var job = new Job { Key = key, Source = source, Size = size };
            job.Callbacks.Add(loaded);
            _pending[key] = _queue.AddLast(job);
        }

        _available.Release();
    }

    /// <summary>
    /// Moves icons that are still waiting to the front of the queue, in the given order,
    /// so what the user is looking at right now loads first.
    /// </summary>
    public void Prioritize(IEnumerable<string> sources, int size)
    {
        lock (_gate)
        {
            foreach (string source in sources.Reverse())
            {
                if (!_pending.TryGetValue(KeyOf(source, size), out LinkedListNode<Job>? node) || node.List == null) continue;
                _queue.Remove(node);
                _queue.AddFirst(node);
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _available.Release();
    }

    private static string KeyOf(string source, int size) => size + "|" + source;

    private void Work()
    {
        while (true)
        {
            _available.Wait();
            if (_disposed) return;

            Job? job = null;
            lock (_gate)
            {
                if (_queue.First != null)
                {
                    job = _queue.First.Value;
                    _queue.RemoveFirst();
                }
            }

            if (job == null) continue;
            Bitmap? icon = IconLoader.Load(job.Source, job.Size);
            _ui.Post(_ =>
            {
                _cache[job.Key] = icon;
                Action<Bitmap?>[] callbacks;
                lock (_gate)
                {
                    _pending.Remove(job.Key);
                    callbacks = job.Callbacks.ToArray();
                }

                foreach (Action<Bitmap?> callback in callbacks) callback(icon);
            }, null);
        }
    }
}