using System.Diagnostics;
using System.Threading.Tasks;
using Timer = System.Windows.Forms.Timer;

namespace WinStart.Core;

internal enum EntryPhase
{
    WaitingForDesktop,
    WaitingForNetwork,
    Queued,
    Countdown,
    Launching,
    Launched,
    AlreadyRunning,
    Failed,
}

/// <summary>What is happening with one application, as shown in the Status column.</summary>
internal sealed class EntryStatus
{
    public EntryStatus(EntryPhase phase, int secondsLeft = 0, string message = "")
    {
        Phase = phase;
        SecondsLeft = secondsLeft;
        Message = message;
        Time = DateTime.Now;
    }

    public EntryPhase Phase { get; }
    public int SecondsLeft { get; }
    public string Message { get; }
    public DateTime Time { get; }

    public string Text => Phase switch
    {
        EntryPhase.WaitingForDesktop => "Waiting for desktop…",
        EntryPhase.WaitingForNetwork => "Waiting for network…",
        EntryPhase.Queued => "Queued",
        EntryPhase.Countdown => "Starts in " + DelayFormat.Countdown(SecondsLeft),
        EntryPhase.Launching => "Starting…",
        EntryPhase.Launched => "Started " + Time.ToShortTimeString(),
        EntryPhase.AlreadyRunning => "Already running",
        _ => Message.Length > 0 ? "Failed: " + Message : "Failed",
    };
}

/// <summary>
/// Runs the startup sequence: wait for the desktop, optionally wait for the network, then launch
/// each enabled application after its delay. Must be created and used on the UI thread.
/// </summary>
internal sealed class StartupRunner : IDisposable
{
    private const int DesktopTimeoutSeconds = 120;

    private enum Stage
    {
        Idle,
        Desktop,
        Network,
        Launching,
    }

    private readonly Func<AppConfig> _config;
    private readonly History _history;
    private readonly Timer _timer = new Timer { Interval = 500 };

    // Delays are measured on a monotonic clock so a time sync during boot cannot distort them.
    private readonly Stopwatch _clock = new Stopwatch();
    private readonly List<string> _pending = new();
    private readonly Dictionary<string, EntryStatus> _status = new();
    private Stage _stage = Stage.Idle;
    private TimeSpan _stageStarted;
    private TimeSpan _anchor;
    private bool _stepping;

    public StartupRunner(Func<AppConfig> config, History history)
    {
        _config = config;
        _history = history;
        _timer.Tick += OnTick;
    }

    /// <summary>Raised whenever a status or the overall state changed.</summary>
    public event EventHandler? Changed;

    public bool IsRunning => _stage != Stage.Idle;

    public string Description => _stage switch
    {
        Stage.Desktop => "Waiting for the Windows desktop…",
        Stage.Network => "Waiting for a network connection…",
        Stage.Launching => _pending.Count == 1
            ? "Startup in progress: 1 application waiting to start"
            : $"Startup in progress: {_pending.Count} applications waiting to start",
        _ => "",
    };

    public EntryStatus? GetStatus(string id) => _status.TryGetValue(id, out EntryStatus? status) ? status : null;

    /// <summary>Begins the sequence for every enabled application.</summary>
    /// <param name="waitForDesktop">True right after sign-in, when the desktop may still be loading.</param>
    public void Start(bool waitForDesktop)
    {
        if (IsRunning) return;

        _pending.Clear();
        foreach (StartupEntry entry in _config().Apps.Where(a => a.Enabled))
        {
            _pending.Add(entry.Id);
            _status.Remove(entry.Id);
        }

        Log.Info($"Startup sequence started with {_pending.Count} application(s)");
        if (_pending.Count == 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _clock.Restart();
        Enter(waitForDesktop ? Stage.Desktop : Stage.Network);
        _timer.Start();
        OnTick(this, EventArgs.Empty);
    }

    /// <summary>Stops the sequence; applications that have not started yet are not launched.</summary>
    public void Cancel()
    {
        if (!IsRunning) return;

        int count = _pending.Count;
        foreach (string id in _pending) _status.Remove(id);
        _pending.Clear();
        Note(count == 1
            ? "Startup cancelled; 1 application was not launched."
            : $"Startup cancelled; {count} applications were not launched.");
        Finish();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Launches one application immediately, whether or not a sequence is running.</summary>
    public Task LaunchNowAsync(StartupEntry entry)
    {
        _pending.Remove(entry.Id);
        return LaunchAsync(entry, skipIfRunning: false, "Launched manually");
    }

    public void Dispose() => _timer.Dispose();

    private double StageSeconds => (_clock.Elapsed - _stageStarted).TotalSeconds;

    private void Enter(Stage stage)
    {
        _stage = stage;
        _stageStarted = _clock.Elapsed;
        if (stage == Stage.Launching) _anchor = _clock.Elapsed;
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_stepping) return; // a launch from the previous tick is still in progress
        _stepping = true;
        try
        {
            await StepAsync();
        }
        catch (Exception ex)
        {
            Log.Error("The startup sequence hit an unexpected error", ex);
        }
        finally
        {
            _stepping = false;
        }
    }

    private async Task StepAsync()
    {
        if (!IsRunning) return;
        AppConfig config = _config();
        AppSettings settings = config.Settings;

        // Applications removed or disabled since the sequence began are dropped from it.
        foreach (string id in _pending.ToList())
        {
            StartupEntry? entry = config.Find(id);
            if (entry == null || !entry.Enabled)
            {
                _pending.Remove(id);
                _status.Remove(id);
            }
        }

        if (_stage == Stage.Desktop)
        {
            if (Readiness.IsDesktopReady())
            {
                Enter(Stage.Network);
            }
            else if (StageSeconds >= DesktopTimeoutSeconds)
            {
                Note($"The desktop was not detected after {DesktopTimeoutSeconds} seconds; continuing anyway.");
                Enter(Stage.Network);
            }
            else
            {
                SetPending(EntryPhase.WaitingForDesktop);
            }
        }

        if (_stage == Stage.Network)
        {
            if (!settings.WaitForNetwork || Readiness.IsNetworkReady())
            {
                Enter(Stage.Launching);
            }
            else if (StageSeconds >= settings.NetworkTimeoutSeconds)
            {
                Note($"No network connection after {settings.NetworkTimeoutSeconds} seconds; launching anyway.");
                Enter(Stage.Launching);
            }
            else
            {
                SetPending(EntryPhase.WaitingForNetwork);
            }
        }

        if (_stage == Stage.Launching)
            await LaunchDueAsync(config);

        if (IsRunning && _pending.Count == 0) Finish();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task LaunchDueAsync(AppConfig config)
    {
        bool sequential = config.Settings.Sequential;
        bool blocked = false; // sequential mode: an earlier application is still counting down

        // Walk a copy in list order; the user may edit the list while a launch is awaited.
        foreach (StartupEntry entry in config.Apps.ToList())
        {
            if (!_pending.Contains(entry.Id)) continue;
            if (blocked)
            {
                _status[entry.Id] = new EntryStatus(EntryPhase.Queued);
                continue;
            }

            int secondsLeft = (int)Math.Ceiling(entry.DelaySeconds - (_clock.Elapsed - _anchor).TotalSeconds);
            if (secondsLeft > 0)
            {
                _status[entry.Id] = new EntryStatus(EntryPhase.Countdown, secondsLeft);
                blocked = sequential;
                continue;
            }

            _pending.Remove(entry.Id);
            await LaunchAsync(entry, config.Settings.SkipIfRunning, "After " + DelayFormat.Describe(entry.DelaySeconds).ToLowerInvariant());
            if (_stage != Stage.Launching) return; // cancelled while the launch was in progress
            if (sequential) _anchor = _clock.Elapsed; // the next delay counts from this launch
        }
    }

    private async Task LaunchAsync(StartupEntry entry, bool skipIfRunning, string reason)
    {
        _status[entry.Id] = new EntryStatus(EntryPhase.Launching);
        Changed?.Invoke(this, EventArgs.Empty);

        LaunchResult result = await Launcher.LaunchAsync(entry.Clone(), skipIfRunning);
        switch (result.Outcome)
        {
            case LaunchOutcome.Launched:
                _status[entry.Id] = new EntryStatus(EntryPhase.Launched);
                _history.Add(entry.Name, History.Launched, reason);
                Log.Info($"Launched {entry.Name} ({entry.TargetPath})");
                break;
            case LaunchOutcome.AlreadyRunning:
                _status[entry.Id] = new EntryStatus(EntryPhase.AlreadyRunning);
                _history.Add(entry.Name, History.Skipped, "Already running");
                Log.Info($"Skipped {entry.Name}: already running");
                break;
            default:
                _status[entry.Id] = new EntryStatus(EntryPhase.Failed, message: result.Message);
                _history.Add(entry.Name, History.Failed, result.Detail);
                Log.Warn($"Could not launch {entry.Name} ({entry.TargetPath}): {result.Detail}");
                break;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetPending(EntryPhase phase)
    {
        foreach (string id in _pending)
            _status[id] = new EntryStatus(phase);
    }

    private void Note(string message)
    {
        _history.Add(AppInfo.Name, History.Info, message);
        Log.Info(message);
    }

    private void Finish()
    {
        _timer.Stop();
        _clock.Stop();
        _stage = Stage.Idle;
        Log.Info("Startup sequence finished");
    }
}
