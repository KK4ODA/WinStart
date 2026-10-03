using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinStart.Core;
using WinStart.Discovery;
using WinStart.Native;
using WinStart.Update;
using Timer = System.Windows.Forms.Timer;

namespace WinStart.UI;

/// <summary>The main window, the tray icon and the commands behind them.</summary>
internal sealed class MainForm : Form
{
    private const int StatusColumn = 2;
    private const int PathColumn = 3;
    private static readonly Color GlyphColor = Color.FromArgb(60, 60, 60);

    private enum BalloonAction
    {
        ShowWindow,
        ShowUpdate,
    }

    private readonly ConfigStore _store;
    private readonly AppConfig _config;
    private readonly IconService _icons;
    private readonly StartupRunner _runner;
    private readonly HashSet<string> _iconsRequested = new(StringComparer.OrdinalIgnoreCase);

    private readonly AppListView _list = new AppListView();
    private readonly ImageList _images = new ImageList();
    private readonly ToolStrip _toolbar = new ToolStrip();
    private readonly StatusStrip _statusBar = new StatusStrip();
    private readonly ToolStripStatusLabel _stateLabel = new ToolStripStatusLabel();
    private readonly ToolStripStatusLabel _cancelLink = new ToolStripStatusLabel("Cancel startup");
    private readonly ToolStripStatusLabel _updateLink = new ToolStripStatusLabel();
    private readonly NotifyIcon _tray = new NotifyIcon();
    private readonly Timer _updateTimer = new Timer();

    private readonly ToolStripButton _editButton;
    private readonly ToolStripButton _removeButton;
    private readonly ToolStripButton _upButton;
    private readonly ToolStripButton _downButton;
    private readonly ToolStripMenuItem _launchSelectedItem;
    private readonly ToolStripMenuItem _runSequenceItem;

    private readonly int _iconSize;
    private bool _loading;
    private bool _exiting;
    private bool _autoStartEnabled;
    private bool _checkingForUpdates;
    private bool _updateDialogOpen;
    private ReleaseInfo? _availableUpdate;
    private string _notifiedVersion = "";
    private BalloonAction _balloonAction;

    public MainForm(ConfigStore store, AppConfig config)
    {
        _store = store;
        _config = config;
        _icons = new IconService(); // created here, not in a field initializer: it captures the UI thread's context
        _runner = new StartupRunner(() => _config, History.Default);
        _runner.Changed += (_, _) => RefreshStatus();
        _autoStartEnabled = AutoStart.IsEnabled();
        _iconSize = this.Px(24);

        SuspendLayout();
        Text = AppInfo.Name;
        Font = Ui.DefaultFont;
        Icon = Ui.AppIcon(SystemInformation.IconSize);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(880, 460);
        MinimumSize = new Size(660, 320);

        // ----- Toolbar -----
        int glyphSize = this.Px(16);
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;
        _toolbar.Renderer = new FlatToolStripRenderer();
        _toolbar.ImageScalingSize = new Size(glyphSize, glyphSize);
        _toolbar.Padding = new Padding(6, 4, 6, 4);
        _toolbar.Dock = DockStyle.Top;
        _toolbar.AccessibleName = "Commands";

        ToolStripButton ToolButton(string text, string glyph, string tip, Action action)
        {
            Bitmap? image = Ui.Glyph(glyph, glyphSize, GlyphColor);
            var button = new ToolStripButton(text)
            {
                Image = image,
                DisplayStyle = image != null ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Text,
                ToolTipText = tip,
                Padding = new Padding(3, 2, 3, 2),
            };
            button.Click += (_, _) => action();
            return button;
        }

        ToolStripButton addButton = ToolButton("Add Application", Glyphs.Add, "Choose an application to start after sign-in (Ctrl+N)", AddApplications);
        _editButton = ToolButton("Edit", Glyphs.Edit, "Change the selected application's delay and options (Enter)", EditSelected);
        _removeButton = ToolButton("Remove", Glyphs.Remove, "Remove the selected applications from the list (Delete)", RemoveSelected);
        _upButton = ToolButton("Move Up", Glyphs.Up, "Move the selection up (Alt+Up)", () => MoveSelected(-1));
        _downButton = ToolButton("Move Down", Glyphs.Down, "Move the selection down (Alt+Down)", () => MoveSelected(1));

        Bitmap? playImage = Ui.Glyph(Glyphs.Play, glyphSize, GlyphColor);
        var launchButton = new ToolStripSplitButton("Launch Now")
        {
            Image = playImage,
            DisplayStyle = playImage != null ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Text,
            ToolTipText = "Start the selected applications right away (F5)",
            Padding = new Padding(3, 2, 3, 2),
        };
        _launchSelectedItem = new ToolStripMenuItem("Launch Selected Now", null, (_, _) => LaunchSelected());
        _runSequenceItem = new ToolStripMenuItem("Run Startup Sequence Now", null, (_, _) => RunSequenceNow())
        {
            ToolTipText = "Launch every enabled application after its delay, exactly as after sign-in",
        };
        launchButton.DropDownItems.Add(_launchSelectedItem);
        launchButton.DropDownItems.Add(_runSequenceItem);
        launchButton.DropDownOpening += (_, _) => UpdateCommands();
        launchButton.ButtonClick += (_, _) =>
        {
            if (_list.SelectedItems.Count > 0) LaunchSelected();
            else launchButton.ShowDropDown();
        };

        ToolStripButton settingsButton = ToolButton("Settings", Glyphs.Settings, "Startup, launching and update options", ShowSettings);
        ToolStripButton historyButton = ToolButton("History", Glyphs.History, "What was launched, when, and whether it worked", ShowHistory);
        settingsButton.Alignment = ToolStripItemAlignment.Right;
        historyButton.Alignment = ToolStripItemAlignment.Right;

        _toolbar.Items.AddRange(new ToolStripItem[]
        {
            addButton, _editButton, _removeButton, new ToolStripSeparator(),
            _upButton, _downButton, new ToolStripSeparator(),
            launchButton,
            settingsButton, historyButton, // right-aligned items are placed from the right edge inwards
        });

        // ----- Application list -----
        _images.ColorDepth = ColorDepth.Depth32Bit;
        _images.ImageSize = new Size(_iconSize, _iconSize);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.CheckBoxes = true;
        _list.HideSelection = false;
        _list.BorderStyle = BorderStyle.None;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.ShowItemToolTips = true;
        _list.SmallImageList = _images;
        _list.Columns.Add("Application", this.Px(250));
        _list.Columns.Add("Delay", this.Px(120));
        _list.Columns.Add("Status", this.Px(180));
        _list.Columns.Add("Path", this.Px(340));
        _list.SelectedIndexChanged += (_, _) => UpdateCommands();
        _list.ItemChecked += OnItemChecked;
        _list.RowDoubleClicked += _ => EditSelected();
        _list.ItemActivate += (_, _) => EditSelected();
        _list.KeyDown += OnListKeyDown;
        _list.Resize += (_, _) => LayoutList();

        _list.AccessibleName = "Applications";
        _list.EmptyText = "No applications yet.\r\n\r\n"
                          + "Click “Add Application” to choose the programs WinStart should launch after you sign in. "
                          + "Each one starts after its own delay.";

        var rowMenu = new ContextMenuStrip();
        ToolStripItem menuLaunch = rowMenu.Items.Add("Launch Now", null, (_, _) => LaunchSelected());
        ToolStripItem menuEdit = rowMenu.Items.Add("Edit…", null, (_, _) => EditSelected());
        ToolStripItem menuRemove = rowMenu.Items.Add("Remove", null, (_, _) => RemoveSelected());
        rowMenu.Items.Add(new ToolStripSeparator());
        ToolStripItem menuUp = rowMenu.Items.Add("Move Up", null, (_, _) => MoveSelected(-1));
        ToolStripItem menuDown = rowMenu.Items.Add("Move Down", null, (_, _) => MoveSelected(1));
        rowMenu.Items.Add(new ToolStripSeparator());
        ToolStripItem menuLocation = rowMenu.Items.Add("Open File Location", null, (_, _) => OpenFileLocation());
        rowMenu.Opening += (_, e) =>
        {
            List<StartupEntry> selected = SelectedEntries();
            if (selected.Count == 0)
            {
                e.Cancel = true;
                return;
            }

            menuLaunch.Enabled = menuRemove.Enabled = true;
            menuEdit.Enabled = selected.Count == 1;
            menuUp.Enabled = _upButton.Enabled;
            menuDown.Enabled = _downButton.Enabled;
            menuLocation.Enabled = selected.Count == 1 && !selected[0].IsShellApp;
        };
        _list.ContextMenuStrip = rowMenu;

        // ----- Status bar -----
        _stateLabel.Spring = true;
        _stateLabel.TextAlign = ContentAlignment.MiddleLeft;
        _cancelLink.IsLink = true;
        _cancelLink.Visible = false;
        _cancelLink.Click += (_, _) => _runner.Cancel();
        _updateLink.IsLink = true;
        _updateLink.Visible = false;
        _updateLink.Click += (_, _) => ShowUpdateDialog();
        var versionLabel = new ToolStripStatusLabel("Version " + AppInfo.Version) { ForeColor = SystemColors.GrayText };
        _statusBar.Items.AddRange(new ToolStripItem[] { _stateLabel, _cancelLink, _updateLink, versionLabel });
        _statusBar.ShowItemToolTips = true;
        _statusBar.AccessibleName = "Status";

        // Fill first, then the edges: docking is applied from the last control added.
        Controls.Add(_list);
        Controls.Add(_toolbar);
        Controls.Add(_statusBar);
        Ui.FinishLayout(this);

        BuildTray();
        RefreshList();

        // First automatic update check a minute after start, then once a day.
        _updateTimer.Interval = 60 * 1000;
        _updateTimer.Tick += async (_, _) =>
        {
            if (_config.Settings.AutoCheckUpdates) await CheckForUpdatesAsync(manual: false);
        };
        _updateTimer.Start();
    }

    // ------------------------------------------------------------------ entry points used by Program

    /// <summary>Starts the after-sign-in sequence.</summary>
    public void BeginStartup() => _runner.Start(waitForDesktop: true);

    /// <summary>Brings the window to the front, whether it was hidden, minimized or behind others.</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        NativeMethods.SetForegroundWindow(Handle);
    }

    public void ExitApplication()
    {
        _exiting = true;
        _tray.Visible = false;
        Application.Exit();
    }

    public void ShowNotice(string title, string text) => Balloon(title, text, BalloonAction.ShowWindow);

    // ------------------------------------------------------------------ window behaviour

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The close button sends WinStart to the tray; Exit in the tray menu really quits.
        if (e.CloseReason == CloseReason.UserClosing && !_exiting)
        {
            e.Cancel = true;
            Hide();
            if (!_config.Settings.TrayHintShown)
            {
                _config.Settings.TrayHintShown = true;
                Save();
                Balloon("WinStart is still running",
                    "It waits in the notification area. To close it completely, right-click its icon and choose Exit.",
                    BalloonAction.ShowWindow);
            }

            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LayoutList(); // the list's real width is only known once the window exists
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _tray.Visible = false;
        base.OnFormClosed(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.N:
                AddApplications();
                return true;
            case Keys.F5:
                LaunchSelected();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _updateTimer.Dispose();
            _runner.Dispose();
            _tray.Dispose();
            _icons.Dispose();
            _images.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        ToolStripItem open = menu.Items.Add("Open WinStart", null, (_, _) => ShowFromTray());
        open.Font = new Font(open.Font, FontStyle.Bold);
        ToolStripItem run = menu.Items.Add("Run Startup Sequence Now", null, (_, _) => RunSequenceNow());
        ToolStripItem cancel = menu.Items.Add("Cancel Startup", null, (_, _) => _runner.Cancel());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Check for Updates…", null, async (_, _) =>
        {
            ShowFromTray();
            await CheckForUpdatesAsync(manual: true);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        menu.Opening += (_, _) =>
        {
            run.Enabled = !_runner.IsRunning;
            cancel.Enabled = _runner.IsRunning;
        };

        _tray.Icon = Ui.AppIcon(SystemInformation.SmallIconSize);
        _tray.Text = AppInfo.Name;
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowFromTray();
        };
        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_balloonAction == BalloonAction.ShowUpdate) ShowUpdateDialog();
            else ShowFromTray();
        };
        _tray.Visible = true;
    }

    private void Balloon(string title, string text, BalloonAction action)
    {
        _balloonAction = action;
        _tray.ShowBalloonTip(8000, title, text, ToolTipIcon.Info);
    }

    // ------------------------------------------------------------------ list

    private List<StartupEntry> SelectedEntries() =>
        _list.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<StartupEntry>().ToList();

    private void RefreshList(IEnumerable<string>? selectIds = null)
    {
        var selected = new HashSet<string>(selectIds ?? SelectedEntries().Select(e => e.Id));
        _loading = true;
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (StartupEntry entry in _config.Apps)
            {
                var item = new ListViewItem(entry.Name)
                {
                    Tag = entry,
                    Checked = entry.Enabled,
                    ForeColor = entry.Enabled ? SystemColors.WindowText : SystemColors.GrayText,
                    ToolTipText = entry.Arguments.Length > 0 ? entry.TargetPath + " " + entry.Arguments : entry.TargetPath,
                };
                item.SubItems.Add(DelayFormat.Describe(entry.DelaySeconds));
                item.SubItems.Add(StatusText(entry));
                item.SubItems.Add(entry.IsShellApp
                    ? "Windows app"
                    : entry.Arguments.Length > 0 ? entry.TargetPath + " " + entry.Arguments : entry.TargetPath);
                _list.Items.Add(item);
                item.Selected = selected.Contains(entry.Id);
                AssignIcon(item, entry);
            }
        }
        finally
        {
            _list.EndUpdate();
            _loading = false;
        }

        LayoutList();
        UpdateCommands();
        UpdateState();
    }

    private void AssignIcon(ListViewItem item, StartupEntry entry)
    {
        string key = entry.TargetPath.ToLowerInvariant();
        if (_images.Images.ContainsKey(key))
        {
            item.ImageKey = key;
            return;
        }

        if (!_iconsRequested.Add(key)) return; // already on its way; every row with this key is updated on arrival
        _icons.Request(entry.TargetPath, _iconSize, icon =>
        {
            if (IsDisposed) return;
            _images.Images.Add(key, icon ?? Ui.GenericAppIcon(_iconSize));
            foreach (ListViewItem row in _list.Items)
            {
                if (row.Tag is StartupEntry e && e.TargetPath.ToLowerInvariant() == key)
                    row.ImageKey = key;
            }
        });
    }

    private void LayoutList()
    {
        if (_list.Columns.Count <= PathColumn) return;

        Ui.FillLastColumn(_list, this.Px(160));
    }

    private string StatusText(StartupEntry entry)
    {
        EntryStatus? status = _runner.GetStatus(entry.Id);
        if (status != null) return status.Text;
        if (!entry.Enabled) return "Disabled";
        return entry.IsShellApp || File.Exists(entry.ResolvedPath) ? "Ready" : "File not found";
    }

    private void RefreshStatus()
    {
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is not StartupEntry entry) continue;
            string text = StatusText(entry);
            if (item.SubItems[StatusColumn].Text != text) item.SubItems[StatusColumn].Text = text;
        }

        UpdateState();
    }

    private void UpdateCommands()
    {
        List<int> indices = _list.SelectedIndices.Cast<int>().OrderBy(i => i).ToList();
        var set = new HashSet<int>(indices);
        _editButton.Enabled = indices.Count == 1;
        _removeButton.Enabled = indices.Count > 0;
        _upButton.Enabled = indices.Any(i => i > 0 && !set.Contains(i - 1));
        _downButton.Enabled = indices.Any(i => i < _list.Items.Count - 1 && !set.Contains(i + 1));
        _launchSelectedItem.Enabled = indices.Count > 0;
        _runSequenceItem.Enabled = !_runner.IsRunning;
    }

    private void UpdateState()
    {
        bool running = _runner.IsRunning;
        _cancelLink.Visible = running;
        if (running)
        {
            _stateLabel.Text = _runner.Description;
        }
        else
        {
            int total = _config.Apps.Count;
            int enabled = _config.Apps.Count(a => a.Enabled);
            string text = total == 0 ? "No applications yet" : total == 1 ? "1 application" : total + " applications";
            if (enabled != total) text += $" ({enabled} enabled)";
            if (!_autoStartEnabled) text += "   ·   WinStart is not set to start with Windows (see Settings)";
            _stateLabel.Text = text;
        }

        string tip = running ? "WinStart: startup in progress" : AppInfo.Name;
        if (_tray.Text != tip) _tray.Text = tip;
    }

    private void OnItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (_loading || e.Item.Tag is not StartupEntry entry || entry.Enabled == e.Item.Checked) return;
        entry.Enabled = e.Item.Checked;
        e.Item.ForeColor = entry.Enabled ? SystemColors.WindowText : SystemColors.GrayText;
        Save();
        RefreshStatus();
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete)
        {
            RemoveSelected();
        }
        else if (e.Alt && e.KeyCode == Keys.Up)
        {
            MoveSelected(-1);
        }
        else if (e.Alt && e.KeyCode == Keys.Down)
        {
            MoveSelected(1);
        }
        else if (e.Control && e.KeyCode == Keys.A)
        {
            foreach (ListViewItem item in _list.Items) item.Selected = true;
        }
        else
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    // ------------------------------------------------------------------ commands

    private void AddApplications()
    {
        using var picker = new AppPickerForm(_icons);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Result.Count == 0) return;

        var select = new List<string>();
        foreach (StartupEntry entry in picker.Result)
        {
            // Picking something that is already listed selects the existing row instead of duplicating it.
            StartupEntry? existing = _config.Apps.FirstOrDefault(a => a.SameTargetAs(entry));
            if (existing == null) _config.Apps.Add(entry);
            select.Add((existing ?? entry).Id);
        }

        Save();
        RefreshList(select);
        if (_list.SelectedItems.Count > 0) _list.SelectedItems[0].EnsureVisible();
        _list.Focus();
    }

    private void EditSelected()
    {
        List<StartupEntry> selected = SelectedEntries();
        if (selected.Count != 1) return;

        StartupEntry edited = selected[0].Clone();
        using var dialog = new EditEntryForm(edited);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        int index = _config.Apps.IndexOf(selected[0]);
        if (index < 0) return;
        _config.Apps[index] = edited;
        Save();
        RefreshList(new[] { edited.Id });
    }

    private void RemoveSelected()
    {
        List<StartupEntry> selected = SelectedEntries();
        if (selected.Count == 0) return;

        string what = selected.Count == 1 ? "“" + selected[0].Name + "”" : selected.Count + " applications";
        DialogResult answer = MessageBox.Show(this,
            "Remove " + what + " from WinStart?\r\n\r\nThis only removes it from this list. Nothing is uninstalled.",
            AppInfo.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;

        foreach (StartupEntry entry in selected) _config.Apps.Remove(entry);
        Save();
        RefreshList();
    }

    private void MoveSelected(int direction)
    {
        List<int> indices = _list.SelectedIndices.Cast<int>().OrderBy(i => i).ToList();
        if (indices.Count == 0) return;

        List<string> ids = SelectedEntries().Select(e => e.Id).ToList();
        var selected = new HashSet<int>(indices);
        List<StartupEntry> apps = _config.Apps;
        if (direction > 0) indices.Reverse();

        bool moved = false;
        foreach (int index in indices)
        {
            int target = index + direction;
            if (target < 0 || target >= apps.Count || selected.Contains(target)) continue;
            (apps[index], apps[target]) = (apps[target], apps[index]);
            selected.Remove(index);
            selected.Add(target);
            moved = true;
        }

        if (!moved) return;
        Save();
        RefreshList(ids);
        if (_list.SelectedItems.Count > 0) _list.SelectedItems[0].EnsureVisible();
    }

    private async void LaunchSelected()
    {
        try
        {
            foreach (StartupEntry entry in SelectedEntries())
                await _runner.LaunchNowAsync(entry);
        }
        catch (Exception ex)
        {
            Log.Error("Launch Now failed", ex);
        }
    }

    private void RunSequenceNow()
    {
        if (_runner.IsRunning) return;
        if (!_config.Apps.Any(a => a.Enabled))
        {
            ShowFromTray();
            MessageBox.Show(this, "There are no enabled applications to launch yet.", AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _runner.Start(waitForDesktop: false);
    }

    private void OpenFileLocation()
    {
        List<StartupEntry> selected = SelectedEntries();
        if (selected.Count != 1 || selected[0].IsShellApp) return;
        try
        {
            Process.Start("explorer.exe", "/select,\"" + selected[0].ResolvedPath + "\"")?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open the file location: " + ex.Message);
        }
    }

    private void ShowSettings()
    {
        using var dialog = new SettingsForm(_config.Settings, owner => CheckForUpdatesAsync(manual: true, owner));
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Save();
        _autoStartEnabled = AutoStart.IsEnabled();
        UpdateState();
    }

    private void ShowHistory()
    {
        using var dialog = new HistoryForm(History.Default);
        dialog.ShowDialog(this);
    }

    private void Save()
    {
        try
        {
            _store.Save(_config);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
            MessageBox.Show(this, "WinStart could not save your settings.\r\n\r\n" + ex.Message, AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ------------------------------------------------------------------ updates

    /// <summary>
    /// Looks for a newer release. A manual check always reports its result; an automatic one
    /// only notifies when there is something new, and never installs anything by itself.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool manual, IWin32Window? owner = null)
    {
        if (_checkingForUpdates) return;
        _checkingForUpdates = true;
        try
        {
            ReleaseInfo? latest = await UpdateChecker.GetLatestAsync(CancellationToken.None);
            _updateTimer.Interval = 24 * 60 * 60 * 1000;
            if (IsDisposed) return;

            if (latest != null && UpdateChecker.IsNewer(latest))
            {
                string version = latest.Version.ToString();
                _availableUpdate = latest;
                _updateLink.Text = "Update available: " + version;
                _updateLink.ToolTipText = "You have " + AppInfo.Version + ". Click to see what's new.";
                _updateLink.Visible = true;

                if (manual)
                {
                    ShowUpdateDialog(owner);
                }
                else if (version != _config.Settings.SkippedVersion && version != _notifiedVersion)
                {
                    _notifiedVersion = version;
                    Balloon("WinStart update available",
                        "Version " + version + " is ready to install. Click here for details.",
                        BalloonAction.ShowUpdate);
                }
            }
            else
            {
                _availableUpdate = null;
                _updateLink.Visible = false;
                if (manual)
                {
                    MessageBox.Show(owner ?? this,
                        "You're up to date.\r\n\r\nWinStart " + AppInfo.Version + " is the latest version.",
                        AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Update check failed: " + ex.Message);
            _updateTimer.Interval = 60 * 60 * 1000; // try again in an hour
            if (manual && !IsDisposed)
            {
                string reason = ex is UpdateException ? ex.Message : "Unexpected error: " + ex.Message;
                MessageBox.Show(owner ?? this, "WinStart could not check for updates.\r\n\r\n" + reason,
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _checkingForUpdates = false;
        }
    }

    private void ShowUpdateDialog(IWin32Window? owner = null)
    {
        if (_availableUpdate == null || _updateDialogOpen) return;

        ReleaseInfo release = _availableUpdate;
        _updateDialogOpen = true;
        try
        {
            if (owner == null) ShowFromTray();
            using var dialog = new UpdateForm(release);
            dialog.ShowDialog(owner ?? this);

            if (dialog.Outcome == UpdateOutcome.Skip)
            {
                _config.Settings.SkippedVersion = release.Version.ToString();
                Save();
            }
            else if (dialog.Outcome == UpdateOutcome.InstallerStarted)
            {
                // The installer takes over from here: it replaces the files and starts the new version.
                ExitApplication();
            }
        }
        finally
        {
            _updateDialogOpen = false;
        }
    }
}
