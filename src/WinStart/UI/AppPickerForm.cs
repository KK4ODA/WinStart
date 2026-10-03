using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using WinStart.Core;
using WinStart.Discovery;

namespace WinStart.UI;

/// <summary>
/// The "Add Application" dialog: a searchable list of the applications installed on this PC,
/// with Browse as a fallback for portable or unusual programs.
/// </summary>
internal sealed class AppPickerForm : Form
{
    // Discovery takes a moment, so its result is kept for the rest of the session ("Refresh list" rescans).
    private static List<InstalledApp>? s_cache;

    private readonly IconService _icons;
    private readonly TextBox _search = new TextBox();
    private readonly AppListView _list = new AppListView();
    private readonly ImageList _images = new ImageList();
    private readonly Label _info = new Label();
    private readonly LinkLabel _refresh = new LinkLabel();
    private readonly DelayPicker _delay = new DelayPicker();
    private readonly Button _add = Ui.MakeButton("Add");
    private readonly int _iconSize;
    private List<InstalledApp> _apps = new();
    private bool _discovering;

    public AppPickerForm(IconService icons)
    {
        _icons = icons;
        _iconSize = this.Px(24);

        SuspendLayout();
        Text = "Add Application";
        Font = Ui.DefaultFont;
        ShowIcon = false;
        ShowInTaskbar = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 520);
        MinimumSize = new Size(520, 380);

        _search.Dock = DockStyle.Fill;
        _search.Margin = new Padding(0, 0, 0, 8);
        _search.TextChanged += (_, _) => ApplyFilter();
        _search.KeyDown += OnSearchKeyDown;
        Ui.SetCue(_search, "Search installed applications");

        _images.ColorDepth = ColorDepth.Depth32Bit;
        _images.ImageSize = new Size(_iconSize, _iconSize);

        _list.Dock = DockStyle.Fill;
        _list.Margin = Padding.Empty;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.MultiSelect = true;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.SmallImageList = _images;
        _list.AccessibleName = "Installed applications";
        _search.AccessibleName = "Search installed applications";
        _list.Columns.Add("Application", this.Px(250));
        _list.Columns.Add("Location", this.Px(340));
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.ItemActivate += (_, _) => AddSelected(); // double-click or Enter
        _list.Resize += (_, _) => FitColumns();

        _info.AutoSize = true;
        _info.Anchor = AnchorStyles.Left;
        _info.ForeColor = SystemColors.GrayText;
        _info.Margin = new Padding(0, 6, 0, 0);
        _refresh.Text = "Refresh list";
        _refresh.AutoSize = true;
        _refresh.Anchor = AnchorStyles.Right;
        _refresh.Margin = new Padding(0, 6, 0, 0);
        _refresh.LinkClicked += (_, _) => BeginDiscovery();

        var infoRow = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        infoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        infoRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        infoRow.Controls.Add(_info, 0, 0);
        infoRow.Controls.Add(_refresh, 1, 0);

        Button browse = Ui.MakeButton("Browse for EXE…");
        browse.Margin = Padding.Empty;
        browse.Anchor = AnchorStyles.Left;
        browse.Click += (_, _) => Browse();
        Button cancel = Ui.MakeButton("Cancel", DialogResult.Cancel);
        _add.Click += (_, _) => AddSelected();
        Label delayLabel = Ui.MakeLabel("Start after:");
        delayLabel.Anchor = AnchorStyles.Right;

        var bottom = new TableLayoutPanel { ColumnCount = 6, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(browse, 0, 0);
        bottom.Controls.Add(delayLabel, 2, 0);
        bottom.Controls.Add(_delay, 3, 0);
        bottom.Controls.Add(_add, 4, 0);
        bottom.Controls.Add(cancel, 5, 0);

        var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 4, Dock = DockStyle.Fill, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_search, 0, 0);
        root.Controls.Add(_list, 0, 1);
        root.Controls.Add(infoRow, 0, 2);
        root.Controls.Add(bottom, 0, 3);
        Controls.Add(root);

        AcceptButton = _add;
        CancelButton = cancel;
        Ui.FinishLayout(this);
        UpdateButtons();
    }

    /// <summary>The entries the user chose; filled when the dialog returns OK.</summary>
    public List<StartupEntry> Result { get; } = new();

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FitColumns();
        _search.Focus();
        if (s_cache != null) SetApps(s_cache);
        else BeginDiscovery();
    }

    private void BeginDiscovery()
    {
        if (_discovering) return;
        _discovering = true;
        _refresh.Enabled = false;
        _info.Text = "Finding installed applications…";

        var worker = new Thread(() =>
        {
            List<InstalledApp> apps;
            try
            {
                apps = AppDiscovery.FindAll();
            }
            catch (Exception ex)
            {
                Log.Error("Application discovery failed", ex);
                apps = new List<InstalledApp>();
            }

            try
            {
                BeginInvoke(new Action(() =>
                {
                    s_cache = apps;
                    _discovering = false;
                    _refresh.Enabled = true;
                    SetApps(apps);
                }));
            }
            catch (InvalidOperationException)
            {
                // The dialog was closed before discovery finished.
            }
        })
        {
            IsBackground = true,
            Name = "Application discovery",
        };
        worker.SetApartmentState(ApartmentState.STA); // the shell objects used for discovery need it
        worker.Start();
    }

    private void SetApps(List<InstalledApp> apps)
    {
        _apps = apps;
        ApplyFilter();

        foreach (InstalledApp app in apps)
        {
            string key = IconKey(app);
            if (_images.Images.ContainsKey(key)) continue;
            _icons.Request(app.IconSource, _iconSize, icon =>
            {
                if (IsDisposed || _images.Images.ContainsKey(key)) return;
                _images.Images.Add(key, icon ?? Ui.GenericAppIcon(_iconSize));
                foreach (ListViewItem item in _list.Items)
                {
                    if (!ReferenceEquals(item.Tag, app)) continue;
                    item.ImageKey = key;
                    break;
                }
            });
        }
    }

    private static string IconKey(InstalledApp app) => app.IconSource.ToLowerInvariant();

    private void ApplyFilter()
    {
        string[] terms = _search.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<InstalledApp> matches = _apps.Where(app => terms.All(term => Matches(app, term)));
        if (terms.Length > 0)
        {
            // Best matches first; the sort is stable, so each group stays alphabetical.
            matches = matches.OrderBy(app => Rank(app.Name, terms[0]));
        }

        var items = new List<ListViewItem>();
        foreach (InstalledApp app in matches)
        {
            var item = new ListViewItem(app.Name) { Tag = app };
            item.SubItems.Add(app.Location);
            string key = IconKey(app);
            if (_images.Images.ContainsKey(key)) item.ImageKey = key;
            items.Add(item);
        }

        // Whatever is listed now gets its icons before the rest of the queue.
        _icons.Prioritize(
            items.Where(i => i.ImageKey.Length == 0).Take(60).Select(i => ((InstalledApp)i.Tag).IconSource), _iconSize);

        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            _list.Items.AddRange(items.ToArray());
            if (terms.Length > 0 && items.Count > 0) items[0].Selected = true; // Enter adds the best match
        }
        finally
        {
            _list.EndUpdate();
        }

        if (!_discovering)
        {
            _info.Text = _apps.Count == 0
                ? "No installed applications were found. Use “Browse for EXE…” to pick a program."
                : items.Count == 0
                    ? "Nothing matches. Use “Browse for EXE…” to pick the program file yourself."
                    : items.Count == _apps.Count
                        ? _apps.Count + " applications"
                        : items.Count + " of " + _apps.Count + " applications";
        }

        FitColumns();
        UpdateButtons();
    }

    /// <summary>0: the name starts with the term; 1: a later word does ("Google Chrome" for "chr"); 2: other matches.</summary>
    private static int Rank(string name, string term)
    {
        if (name.StartsWith(term, StringComparison.CurrentCultureIgnoreCase)) return 0;
        return name.IndexOf(" " + term, StringComparison.CurrentCultureIgnoreCase) >= 0 ? 1 : 2;
    }

    private static bool Matches(InstalledApp app, string term)
    {
        if (app.Name.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;
        if (app.IsShellApp) return false;
        try
        {
            return Path.GetFileName(app.TargetPath).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void FitColumns()
    {
        if (_list.Columns.Count < 2) return;
        Ui.FillLastColumn(_list, this.Px(160));
    }

    private void UpdateButtons()
    {
        int count = _list.SelectedItems.Count;
        _add.Enabled = count > 0;
        _add.Text = count > 1 ? "Add " + count + " Applications" : "Add";
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Down || _list.Items.Count == 0) return;
        _list.Focus();
        if (_list.SelectedItems.Count == 0) _list.Items[0].Selected = true;
        _list.SelectedItems[0].Focused = true;
        e.Handled = true;
    }

    private void AddSelected()
    {
        foreach (ListViewItem item in _list.SelectedItems)
        {
            if (item.Tag is InstalledApp app) Result.Add(app.ToEntry(_delay.Seconds));
        }

        if (Result.Count > 0) DialogResult = DialogResult.OK;
    }

    private void Browse()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose a Program",
            Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        Result.Add(new StartupEntry
        {
            Name = AppDiscovery.FriendlyName(dialog.FileName),
            TargetPath = dialog.FileName,
            DelaySeconds = _delay.Seconds,
        });
        DialogResult = DialogResult.OK;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _images.Dispose();
        base.Dispose(disposing);
    }
}
