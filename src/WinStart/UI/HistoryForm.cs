using System.Drawing;
using System.Windows.Forms;
using WinStart.Core;

namespace WinStart.UI;

/// <summary>Shows what WinStart launched, when, and whether it worked. Newest first.</summary>
internal sealed class HistoryForm : Form
{
    private readonly History _history;
    private readonly AppListView _list = new AppListView();

    public HistoryForm(History history)
    {
        _history = history;

        SuspendLayout();
        Text = "Startup History";
        Font = Ui.DefaultFont;
        ShowIcon = false;
        ShowInTaskbar = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(700, 420);
        MinimumSize = new Size(480, 280);

        _list.Dock = DockStyle.Fill;
        _list.Margin = Padding.Empty;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.AccessibleName = "Startup history";
        _list.EmptyText = "Nothing has been launched yet.";
        _list.Columns.Add("Time", this.Px(150));
        _list.Columns.Add("Application", this.Px(180));
        _list.Columns.Add("Result", this.Px(80));
        _list.Columns.Add("Details", this.Px(260));
        _list.Resize += (_, _) => FitColumns();

        Button openLog = Ui.MakeButton("Open Log File");
        openLog.Margin = Padding.Empty;
        openLog.Anchor = AnchorStyles.Left;
        openLog.Click += (_, _) =>
        {
            if (File.Exists(AppPaths.LogFile)) Ui.Open(AppPaths.LogFile);
            else MessageBox.Show(this, "Nothing has been logged yet.", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        Button clear = Ui.MakeButton("Clear History");
        clear.Click += (_, _) => Clear();
        Button close = Ui.MakeButton("Close", DialogResult.Cancel);

        var bottom = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(openLog, 0, 0);
        bottom.Controls.Add(clear, 2, 0);
        bottom.Controls.Add(close, 3, 0);

        var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_list, 0, 0);
        root.Controls.Add(bottom, 0, 1);
        Controls.Add(root);

        AcceptButton = close;
        CancelButton = close;
        Ui.FinishLayout(this);
        LoadRecords();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FitColumns();
    }

    private void LoadRecords()
    {
        List<HistoryRecord> records = _history.Read();
        records.Reverse();

        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (HistoryRecord record in records)
            {
                var item = new ListViewItem(record.Time.ToString("g"));
                item.SubItems.Add(record.App);
                item.SubItems.Add(record.Result);
                item.SubItems.Add(record.Detail);
                if (record.Result == History.Failed) item.ForeColor = Color.FromArgb(196, 43, 28);
                else if (record.Result != History.Launched) item.ForeColor = SystemColors.GrayText;
                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }
    }

    private void FitColumns()
    {
        if (_list.Columns.Count < 4) return;
        Ui.FillLastColumn(_list, this.Px(120));
    }

    private void Clear()
    {
        if (_list.Items.Count == 0) return;
        DialogResult answer = MessageBox.Show(this, "Clear the startup history?", AppInfo.Name,
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;
        _history.Clear();
        LoadRecords();
    }
}
