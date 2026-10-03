using System.Threading;
using System.Windows.Forms;
using WinStart.Core;
using WinStart.Native;
using WinStart.UI;
using WinStart.Update;

namespace WinStart;

internal static class Program
{
    /// <summary>Passed by the installer after an update, so the new version can say so.</summary>
    private const string UpdatedArgument = "--updated";

    /// <summary>Asks a running WinStart to exit; used by the installer before it replaces files.</summary>
    private const string QuitArgument = "--quit";

    [STAThread]
    private static int Main(string[] args)
    {
        bool startup = HasArgument(args, AutoStart.StartupArgument);
        bool updated = HasArgument(args, UpdatedArgument);

        // One WinStart per signed-in user. A custom data folder gets its own instance, so a
        // development or portable copy can run next to the installed one.
        string scope = AppPaths.IsDefaultDataDir ? "" : "." + StableHash(AppPaths.DataDir);
        string mutexName = @"Local\WinStart.Instance" + scope;
        string showEventName = @"Local\WinStart.Show" + scope;
        string quitEventName = @"Local\WinStart.Quit" + scope;

        if (HasArgument(args, QuitArgument))
        {
            QuitRunningInstance(mutexName, quitEventName);
            return 0;
        }

        using var mutex = new Mutex(initiallyOwned: true, mutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Already running: opening WinStart again shows the existing window.
            if (!startup) Signal(showEventName);
            return 0;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);

        Log.Info($"WinStart {AppInfo.Version} started ({(args.Length > 0 ? string.Join(" ", args) : "no arguments")})");
        History.Default.Trim();
        UpdateInstaller.CleanUp();

        var store = new ConfigStore(AppPaths.ConfigFile);
        AppConfig config = store.Load();
        if (store.CreatedNew) FirstRun(store, config);

        using var form = new MainForm(store, config);
        form.FormClosed += (_, _) => Application.ExitThread();
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, showEventName);
        using var quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, quitEventName);
        _ = form.Handle; // create the window so other threads can post to it while it is hidden
        RegisteredWaitHandle showWait = ThreadPool.RegisterWaitForSingleObject(
            showEvent, (_, _) => Post(form, form.ShowFromTray), null, Timeout.Infinite, executeOnlyOnce: false);
        RegisteredWaitHandle quitWait = ThreadPool.RegisterWaitForSingleObject(
            quitEvent, (_, _) => Post(form, form.ExitApplication), null, Timeout.Infinite, executeOnlyOnce: false);

        // After sign-in WinStart stays in the tray unless the user prefers to see the window.
        bool hidden = startup && config.Settings.StartMinimized;
        if (!hidden) form.Show();
        if (startup) form.BeginStartup();
        if (updated) form.ShowNotice("WinStart was updated", "You are now running version " + AppInfo.Version + ".");
        if (store.LoadWarning != null) form.ShowNotice("WinStart settings were reset", store.LoadWarning);

        Application.Run();

        showWait.Unregister(null);
        quitWait.Unregister(null);
        Log.Info("WinStart exited");
        GC.KeepAlive(mutex);
        return 0;
    }

    private static void FirstRun(ConfigStore store, AppConfig config)
    {
        try
        {
            store.Save(config);

            // Starting with Windows is the whole point, so it is on from the first run (and can be
            // switched off in Settings). Copies running from a custom data folder leave it alone.
            if (AppPaths.IsDefaultDataDir) AutoStart.Enable();
        }
        catch (Exception ex)
        {
            Log.Error("First-run setup failed", ex);
        }
    }

    private static bool HasArgument(string[] args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    private static void Post(MainForm form, Action action)
    {
        try
        {
            if (!form.IsDisposed) form.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The window is closing.
        }
    }

    private static void Signal(string eventName)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(eventName, out EventWaitHandle? handle)) return;
            using (handle)
            {
                NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY); // let the other instance come to the front
                handle.Set();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not signal the running instance: " + ex.Message);
        }
    }

    private static void QuitRunningInstance(string mutexName, string quitEventName)
    {
        Signal(quitEventName);

        // Wait (briefly) until the other instance has released its files.
        for (int i = 0; i < 100; i++)
        {
            if (!Mutex.TryOpenExisting(mutexName, out Mutex? running)) return;
            running.Dispose();
            Thread.Sleep(100);
        }
    }

    private static void ReportCrash(Exception exception)
    {
        Log.Error("Unexpected error", exception);
        MessageBox.Show(
            "WinStart ran into an unexpected problem:\r\n\r\n" + exception.Message
            + "\r\n\r\nDetails were saved to:\r\n" + AppPaths.LogFile,
            AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>A hash that is the same in every process (string.GetHashCode is not guaranteed to be).</summary>
    private static string StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text.ToUpperInvariant())
            hash = (hash ^ c) * 16777619;
        return hash.ToString("x8");
    }
}
