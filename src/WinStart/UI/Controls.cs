using System.Drawing;
using System.Windows.Forms;
using WinStart.Core;
using WinStart.Native;

namespace WinStart.UI;

/// <summary>A flicker-free list view with the modern Explorer look.</summary>
internal sealed class AppListView : ListView
{
    private bool _creatingHandle;

    public AppListView()
    {
        DoubleBuffered = true;
    }

    /// <summary>
    /// Raised when a row (not its checkbox) is double-clicked. With check boxes enabled the stock
    /// control toggles the check on double-click, which is surprising; this replaces that behaviour.
    /// </summary>
    public event Action<ListViewItem>? RowDoubleClicked;

    /// <summary>Hint painted in the middle of the list while it has no rows.</summary>
    public string EmptyText { get; set; } = "";

    protected override void OnHandleCreated(EventArgs e)
    {
        _creatingHandle = true;
        try
        {
            base.OnHandleCreated(e); // inserts the existing rows into the native control
        }
        finally
        {
            _creatingHandle = false;
        }

        NativeMethods.SetWindowTheme(Handle, "explorer", null);
    }

    protected override void OnItemChecked(ItemCheckedEventArgs e)
    {
        // While the native control is being filled it reports every row as "unchecked, then
        // checked". Those are not user actions and must not reach handlers that save state.
        if (!_creatingHandle) base.OnItemChecked(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_LBUTTONDBLCLK && CheckBoxes)
        {
            long lParam = m.LParam.ToInt64();
            var point = new Point((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
            ListViewHitTestInfo hit = HitTest(point);
            if (hit.Item != null && hit.Location != ListViewHitTestLocations.StateImage)
            {
                RowDoubleClicked?.Invoke(hit.Item);
                return;
            }

            // Two quick clicks on the checkbox itself should simply toggle it twice.
            m.Msg = NativeMethods.WM_LBUTTONDOWN;
        }

        base.WndProc(ref m);
        if (m.Msg == NativeMethods.WM_PAINT && Items.Count == 0 && EmptyText.Length > 0) PaintEmptyText();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Items.Count == 0 && EmptyText.Length > 0) Invalidate(); // keep the hint centred
    }

    // Painted rather than shown in a child label: a label inside the list hides the rows from
    // screen readers and lends its text to neighbouring controls as their accessible name.
    private void PaintEmptyText()
    {
        const TextFormatFlags Flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
        int width = Math.Min(this.Px(440), ClientSize.Width - this.Px(40));
        if (width <= 0) return;

        using Graphics g = CreateGraphics();
        Size size = TextRenderer.MeasureText(g, EmptyText, Font, new Size(width, int.MaxValue), Flags);
        var area = new Rectangle((ClientSize.Width - width) / 2, Math.Max(this.Px(48), (ClientSize.Height - size.Height) / 2), width, size.Height);
        TextRenderer.DrawText(g, EmptyText, Font, area, SystemColors.GrayText, BackColor, Flags);
    }
}

/// <summary>Number + unit editor for a delay ("30 seconds", "5 minutes").</summary>
internal sealed class DelayPicker : FlowLayoutPanel
{
    private readonly NumericUpDown _value = new NumericUpDown
    {
        Minimum = 0,
        Maximum = 9999,
        Width = 64,
        TextAlign = HorizontalAlignment.Right,
        Margin = new Padding(0, 0, 6, 0),
    };

    private readonly ComboBox _unit = new ComboBox
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 92,
        Margin = Padding.Empty,
    };

    public DelayPicker()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = false;
        Margin = new Padding(0, 3, 0, 3);
        Anchor = AnchorStyles.Left;
        _unit.Items.AddRange(new object[] { "seconds", "minutes" });
        _unit.SelectedIndex = 0;
        Controls.Add(_value);
        Controls.Add(_unit);
        Seconds = StartupEntry.DefaultDelaySeconds;
    }

    public int Seconds
    {
        get => (int)_value.Value * (_unit.SelectedIndex == 1 ? 60 : 1);
        set
        {
            int seconds = Math.Max(0, Math.Min(StartupEntry.MaxDelaySeconds, value));
            bool wholeMinutes = seconds >= 60 && seconds % 60 == 0;
            if (!wholeMinutes && seconds > _value.Maximum) // too long to show in seconds
            {
                wholeMinutes = true;
                seconds = seconds / 60 * 60;
            }

            _unit.SelectedIndex = wholeMinutes ? 1 : 0;
            _value.Value = wholeMinutes ? seconds / 60 : seconds;
        }
    }
}
