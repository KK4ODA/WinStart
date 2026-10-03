using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinStart.Core;

namespace WinStart.UI;

/// <summary>WinStart's options: starting with Windows, how applications are launched, and updates.</summary>
internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly bool _autoStartWas;

    private readonly CheckBox _autoStart = Check("Start WinStart when I sign in to Windows");
    private readonly CheckBox _minimized = Check("Start hidden in the notification area (system tray)");
    private readonly CheckBox _network = Check("Wait for a network connection first, for up to");
    private readonly NumericUpDown _networkTimeout = new NumericUpDown { Minimum = 5, Maximum = 3600, Width = 60, TextAlign = HorizontalAlignment.Right, Margin = new Padding(0, 1, 4, 0) };
    private readonly CheckBox _skipRunning = Check("Don't launch applications that are already running");
    private readonly RadioButton _independent = Radio("Count every delay from sign-in");
    private readonly RadioButton _sequential = Radio("Launch one after another (each delay starts when the previous application was launched)");
    private readonly CheckBox _autoUpdate = Check("Check for updates automatically");

    public SettingsForm(AppSettings settings, Func<IWin32Window, Task> checkForUpdates)
    {
        _settings = settings;
        _autoStartWas = AutoStart.IsEnabled();

        SuspendLayout();
        Ui.PrepareDialog(this, "Settings");
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _autoStart.Checked = _autoStartWas;
        _minimized.Checked = settings.StartMinimized;
        _network.Checked = settings.WaitForNetwork;
        _networkTimeout.Value = Math.Max(_networkTimeout.Minimum, Math.Min(_networkTimeout.Maximum, settings.NetworkTimeoutSeconds));
        _skipRunning.Checked = settings.SkipIfRunning;
        _independent.Checked = !settings.Sequential;
        _sequential.Checked = settings.Sequential;
        _autoUpdate.Checked = settings.AutoCheckUpdates;
        _network.CheckedChanged += (_, _) => _networkTimeout.Enabled = _network.Checked;
        _networkTimeout.Enabled = _network.Checked;

        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14, 4, 14, 12),
        };

        // ----- Windows startup -----
        root.Controls.Add(Ui.MakeHeading("Windows startup"));
        root.Controls.Add(_autoStart);
        root.Controls.Add(_minimized);

        // ----- Launching -----
        root.Controls.Add(Ui.MakeHeading("Launching applications"));
        _network.Margin = new Padding(3, 3, 0, 3);
        Label seconds = Ui.MakeLabel("seconds");
        seconds.Margin = new Padding(0, 4, 0, 0);
        root.Controls.Add(Row(_network, _networkTimeout, seconds));
        root.Controls.Add(_skipRunning);
        root.Controls.Add(_independent);
        root.Controls.Add(_sequential);

        // ----- Updates -----
        root.Controls.Add(Ui.MakeHeading("Updates"));
        Button checkNow = Ui.MakeButton("Check Now");
        checkNow.Margin = new Padding(12, 0, 0, 0);
        checkNow.Click += async (_, _) =>
        {
            checkNow.Enabled = false;
            try
            {
                await checkForUpdates(this);
            }
            finally
            {
                if (!IsDisposed) checkNow.Enabled = true;
            }
        };
        _autoUpdate.Margin = new Padding(3, 5, 0, 3);
        root.Controls.Add(Row(_autoUpdate, checkNow));

        var about = new LinkLabel
        {
            Text = "WinStart " + AppInfo.Version + "  ·  Release notes  ·  Open settings folder",
            AutoSize = true,
            Margin = new Padding(3, 8, 0, 0),
        };
        AddLink(about, "Release notes", () => Ui.OpenUrl(AppInfo.ReleasesUrl));
        AddLink(about, "Open settings folder", () => Ui.Open(AppPaths.DataDir));
        about.LinkClicked += (_, e) => (e.Link.LinkData as Action)?.Invoke();
        root.Controls.Add(about);

        // ----- Buttons -----
        Button ok = Ui.MakeButton("OK");
        ok.Click += (_, _) => Accept();
        Button cancel = Ui.MakeButton("Cancel", DialogResult.Cancel);
        FlowLayoutPanel buttons = Ui.ButtonRow(ok, cancel);
        buttons.Dock = DockStyle.None;
        buttons.Anchor = AnchorStyles.Right;
        buttons.Margin = new Padding(0, 16, 0, 0);
        root.Controls.Add(buttons);

        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
        Ui.FinishLayout(this);
    }

    private static CheckBox Check(string text) => new CheckBox { Text = text, AutoSize = true, Margin = new Padding(3, 3, 3, 3) };

    private static RadioButton Radio(string text) => new RadioButton { Text = text, AutoSize = true, Margin = new Padding(3, 3, 3, 3) };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
        row.Controls.AddRange(controls);
        return row;
    }

    private static void AddLink(LinkLabel label, string text, Action action)
    {
        int start = label.Text.IndexOf(text, StringComparison.Ordinal);
        if (start >= 0) label.Links.Add(start, text.Length, action);
    }

    private void Accept()
    {
        if (_autoStart.Checked != _autoStartWas)
        {
            try
            {
                if (_autoStart.Checked) AutoStart.Enable();
                else AutoStart.Disable();
            }
            catch (Exception ex)
            {
                Log.Error("Could not change the startup registration", ex);
                MessageBox.Show(this, "WinStart could not change its Windows startup setting.\r\n\r\n" + ex.Message,
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        _settings.StartMinimized = _minimized.Checked;
        _settings.WaitForNetwork = _network.Checked;
        _settings.NetworkTimeoutSeconds = (int)_networkTimeout.Value;
        _settings.SkipIfRunning = _skipRunning.Checked;
        _settings.Sequential = _sequential.Checked;
        _settings.AutoCheckUpdates = _autoUpdate.Checked;
        DialogResult = DialogResult.OK;
    }
}
