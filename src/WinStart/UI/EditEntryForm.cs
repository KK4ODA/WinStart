using System.Drawing;
using System.Windows.Forms;
using WinStart.Core;
using WinStart.Discovery;

namespace WinStart.UI;

/// <summary>Edits one application's name, program, delay and options.</summary>
internal sealed class EditEntryForm : Form
{
    private readonly StartupEntry _entry;
    private readonly TextBox _name = new TextBox();
    private readonly TextBox _path = new TextBox();
    private readonly TextBox _arguments = new TextBox();
    private readonly DelayPicker _delay = new DelayPicker();
    private readonly CheckBox _minimized = new CheckBox { Text = "Start minimized (if the application supports it)", AutoSize = true };
    private readonly CheckBox _enabled = new CheckBox { Text = "Enabled", AutoSize = true };

    /// <param name="entry">The entry to edit; it is updated when the dialog returns OK.</param>
    public EditEntryForm(StartupEntry entry)
    {
        _entry = entry;

        SuspendLayout();
        Ui.PrepareDialog(this, "Edit Application");
        ClientSize = new Size(540, 0);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        foreach (TextBox box in new[] { _name, _path, _arguments })
        {
            box.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            box.Margin = new Padding(0, 4, 0, 4);
        }

        Button browse = Ui.MakeButton("Browse…");
        browse.Anchor = AnchorStyles.Left;
        browse.Click += (_, _) => Browse();

        _name.Text = entry.Name;
        _arguments.Text = entry.Arguments;
        _delay.Seconds = entry.DelaySeconds;
        _minimized.Checked = entry.StartMinimized;
        _enabled.Checked = entry.Enabled;
        if (entry.IsShellApp)
        {
            // Store apps are launched by their id; there is no file, argument list or window state to set.
            _path.Text = "Windows app (" + entry.AppUserModelId + ")";
            _path.ReadOnly = true;
            browse.Enabled = _arguments.Enabled = _minimized.Enabled = false;
        }
        else
        {
            _path.Text = entry.TargetPath;
        }

        var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(12) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        int row = 0;
        grid.Controls.Add(Ui.MakeLabel("Name:"), 0, row);
        grid.Controls.Add(_name, 1, row);
        grid.SetColumnSpan(_name, 2);
        row++;
        grid.Controls.Add(Ui.MakeLabel("Program:"), 0, row);
        grid.Controls.Add(_path, 1, row);
        grid.Controls.Add(browse, 2, row);
        row++;
        grid.Controls.Add(Ui.MakeLabel("Arguments:"), 0, row);
        grid.Controls.Add(_arguments, 1, row);
        grid.SetColumnSpan(_arguments, 2);
        row++;
        grid.Controls.Add(Ui.MakeLabel("Start after:"), 0, row);
        grid.Controls.Add(_delay, 1, row);
        row++;
        _minimized.Margin = new Padding(0, 8, 0, 2);
        grid.Controls.Add(_minimized, 1, row);
        grid.SetColumnSpan(_minimized, 2);
        row++;
        grid.Controls.Add(_enabled, 1, row);
        grid.SetColumnSpan(_enabled, 2);
        row++;

        Button ok = Ui.MakeButton("OK");
        ok.Click += (_, _) => Accept();
        Button cancel = Ui.MakeButton("Cancel", DialogResult.Cancel);
        FlowLayoutPanel buttons = Ui.ButtonRow(ok, cancel);
        grid.Controls.Add(buttons, 0, row);
        grid.SetColumnSpan(buttons, 3);

        Controls.Add(grid);
        AcceptButton = ok;
        CancelButton = cancel;
        Ui.FinishLayout(this);
    }

    private void Browse()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose a Program",
            Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        try
        {
            string current = Environment.ExpandEnvironmentVariables(_path.Text.Trim());
            if (File.Exists(current))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(current);
                dialog.FileName = Path.GetFileName(current);
            }
        }
        catch (ArgumentException)
        {
            // The box holds something that is not a path; start in the default folder.
        }

        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _path.Text = dialog.FileName;
        if (_name.Text.Trim().Length == 0) _name.Text = AppDiscovery.FriendlyName(dialog.FileName);
    }

    private void Accept()
    {
        string name = _name.Text.Trim();
        string path = _path.Text.Trim().Trim('"');

        if (name.Length == 0)
        {
            Warn("Please enter a name.", _name);
            return;
        }

        if (!_entry.IsShellApp)
        {
            if (path.Length == 0)
            {
                Warn("Please choose the program to start.", _path);
                return;
            }

            if (!File.Exists(Environment.ExpandEnvironmentVariables(path)))
            {
                DialogResult answer = MessageBox.Show(this,
                    "This file does not exist:\r\n\r\n" + path + "\r\n\r\nSave it anyway?",
                    AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes)
                {
                    _path.Focus();
                    return;
                }
            }

            _entry.TargetPath = path;
            _entry.Arguments = _arguments.Text.Trim();
            _entry.StartMinimized = _minimized.Checked;
        }

        _entry.Name = name;
        _entry.DelaySeconds = _delay.Seconds;
        _entry.Enabled = _enabled.Checked;
        DialogResult = DialogResult.OK;
    }

    private void Warn(string message, Control focus)
    {
        MessageBox.Show(this, message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        focus.Focus();
    }
}
