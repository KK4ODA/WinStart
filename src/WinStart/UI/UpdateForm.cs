using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using WinStart.Core;
using WinStart.Update;

namespace WinStart.UI;

internal enum UpdateOutcome
{
    Later,
    Skip,
    InstallerStarted,
}

/// <summary>
/// Offers a newer version: shows both version numbers and the release notes, and installs
/// only when the user clicks "Download and Install".
/// </summary>
internal sealed class UpdateForm : Form
{
    private readonly ReleaseInfo _release;
    private readonly Button _install = Ui.MakeButton("Download and Install");
    private readonly Button _skip = Ui.MakeButton("Skip This Version");
    private readonly Button _later = Ui.MakeButton("Remind Me Later");
    private readonly ProgressBar _progress = new ProgressBar { Dock = DockStyle.Fill, Height = 16, Margin = new Padding(0, 10, 0, 2), Visible = false };
    private readonly Label _status = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 2, 0, 0) };
    private CancellationTokenSource? _download;

    public UpdateForm(ReleaseInfo release)
    {
        _release = release;

        SuspendLayout();
        Ui.PrepareDialog(this, "WinStart Update");
        ClientSize = new Size(540, 430);

        var heading = new Label
        {
            Text = "A new version of WinStart is available",
            AutoSize = true,
            Font = new Font(Ui.DefaultFont.FontFamily, Ui.DefaultFont.SizeInPoints + 3f),
            Margin = new Padding(0, 0, 0, 8),
        };

        var versions = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        versions.Controls.Add(VersionLabel("Installed version:"), 0, 0);
        versions.Controls.Add(VersionLabel(AppInfo.Version), 1, 0);
        versions.Controls.Add(VersionLabel("Available version:"), 0, 1);
        versions.Controls.Add(VersionLabel(release.Version.ToString(), bold: true), 1, 1);

        var notes = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 6),
            TabStop = false,
            Text = release.Notes.Length > 0
                ? ReleaseNotes.ToPlainText(release.Notes)
                : "No release notes were published for this version.",
        };

        var link = new LinkLabel { Text = "View this release on GitHub", AutoSize = true, Margin = Padding.Empty };
        link.LinkClicked += (_, _) => Ui.OpenUrl(release.PageUrl);

        if (!release.CanInstall) _install.Text = "Open Download Page";
        _install.Click += OnInstall;
        _skip.Click += (_, _) =>
        {
            Outcome = UpdateOutcome.Skip;
            Close();
        };
        _later.Click += (_, _) =>
        {
            if (_download != null) _download.Cancel(); // while downloading this button reads "Cancel"
            else Close();
        };

        var root = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill, Padding = new Padding(14) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void AddRow(Control control, bool fill = false)
        {
            root.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            root.Controls.Add(control, 0, root.RowStyles.Count - 1);
        }

        AddRow(heading);
        AddRow(versions);
        AddRow(new Label { Text = "What's new:", AutoSize = true, Margin = Padding.Empty });
        AddRow(notes, fill: true);
        AddRow(link);
        AddRow(_progress);
        AddRow(_status);
        AddRow(Ui.ButtonRow(_install, _skip, _later));
        Controls.Add(root);

        AcceptButton = _install;
        Ui.FinishLayout(this);
    }

    public UpdateOutcome Outcome { get; private set; } = UpdateOutcome.Later;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _install.Focus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _download?.Cancel();
        base.OnFormClosing(e);
    }

    private static Label VersionLabel(string text, bool bold = false) => new Label
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 1, 12, 1),
        Font = bold ? new Font(Ui.DefaultFont, FontStyle.Bold) : Ui.DefaultFont,
    };

    private async void OnInstall(object? sender, EventArgs e)
    {
        if (!_release.CanInstall)
        {
            Ui.OpenUrl(_release.PageUrl);
            return;
        }

        var cancellation = new CancellationTokenSource();
        _download = cancellation;
        SetBusy(true);
        try
        {
            var progress = new Progress<int>(percent =>
            {
                if (IsDisposed) return;
                _progress.Value = percent;
                _status.Text = "Downloading… " + percent + "%";
            });
            _status.Text = "Downloading…";
            string installer = await UpdateInstaller.DownloadAsync(_release, progress, cancellation.Token);
            if (IsDisposed) return;

            _status.Text = "Verified. Starting the installer…";
            UpdateInstaller.RunInstaller(installer);
            Outcome = UpdateOutcome.InstallerStarted;
            _download = null;
            Close();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!IsDisposed)
            {
                SetBusy(false);
                _status.Text = "Download cancelled.";
            }
        }
        catch (Exception ex)
        {
            Log.Error("The update could not be installed", ex);
            if (IsDisposed) return;
            SetBusy(false);
            _status.Text = "";
            string reason = ex is UpdateException ? ex.Message : "The update could not be installed.\r\n\r\n" + ex.Message;
            MessageBox.Show(this, reason + "\r\n\r\nYour current version is unchanged.", AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (ReferenceEquals(_download, cancellation)) _download = null;
            cancellation.Dispose();
        }
    }

    private void SetBusy(bool busy)
    {
        _install.Enabled = !busy;
        _skip.Enabled = !busy;
        _later.Text = busy ? "Cancel" : "Remind Me Later";
        _progress.Visible = busy;
        if (!busy) _progress.Value = 0;
    }
}
