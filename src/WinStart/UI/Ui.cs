using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;
using WinStart.Core;
using WinStart.Native;

namespace WinStart.UI;

/// <summary>Icons from the "Segoe MDL2 Assets" font that ships with Windows 10 and 11.</summary>
internal static class Glyphs
{
    public const string Add = "";
    public const string Edit = "";
    public const string Remove = "";
    public const string Up = "";
    public const string Down = "";
    public const string Play = "";
    public const string History = "";
    public const string Settings = "";
}

/// <summary>Shared look-and-feel helpers.</summary>
internal static class Ui
{
    private const string GlyphFont = "Segoe MDL2 Assets";


    public static Font DefaultFont => SystemFonts.MessageBoxFont;

    /// <summary>Converts a size designed at 100% scaling to this control's DPI.</summary>
    public static int Px(this Control control, int pixels) =>
        (int)Math.Round(pixels * control.DeviceDpi / 96.0);

    /// <summary>
    /// Finishes building a form whose controls were laid out for 100% scaling:
    /// WinForms then scales everything to the monitor's DPI. Pair with SuspendLayout().
    /// </summary>
    public static void FinishLayout(Form form)
    {
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.ResumeLayout(false);
        form.PerformLayout();
    }

    public static void PrepareDialog(Form form, string title)
    {
        form.Text = title;
        form.Font = DefaultFont;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.ShowInTaskbar = false;
        form.ShowIcon = false;
        form.StartPosition = FormStartPosition.CenterParent;
    }

    public static Button MakeButton(string text, DialogResult result = DialogResult.None) => new Button
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(88, 28),
        Padding = new Padding(8, 0, 8, 0),
        Margin = new Padding(8, 0, 0, 0),
        DialogResult = result,
        UseVisualStyleBackColor = true,
    };

    /// <summary>A right-aligned row of dialog buttons; pass them in left-to-right order.</summary>
    public static FlowLayoutPanel ButtonRow(params Control[] buttons)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 0),
            WrapContents = false,
        };
        foreach (Control button in buttons.Reverse()) row.Controls.Add(button);
        return row;
    }

    public static Label MakeLabel(string text) => new Label
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 6, 8, 6),
    };

    public static Label MakeHeading(string text) => new Label
    {
        Text = text,
        AutoSize = true,
        Font = new Font(DefaultFont, FontStyle.Bold),
        Margin = new Padding(0, 10, 0, 4),
    };

    /// <summary>
    /// Stretches the last column over the space the other columns leave, so there is no blank
    /// strip and no horizontal scroll bar. Call after filling the list and when it is resized.
    /// </summary>
    public static void FillLastColumn(ListView list, int minimumWidth)
    {
        if (!list.IsHandleCreated || list.Columns.Count == 0) return;

        // Ask Windows directly: the client area shrinks when the vertical scroll bar appears.
        NativeMethods.GetClientRect(list.Handle, out NativeMethods.RECT client);
        int last = list.Columns.Count - 1;
        int others = 0;
        for (int i = 0; i < last; i++) others += list.Columns[i].Width;

        int width = Math.Max(minimumWidth, client.Right - others);
        if (list.Columns[last].Width != width) list.Columns[last].Width = width;
    }

    /// <summary>Greyed placeholder text for an empty text box.</summary>
    public static void SetCue(TextBox box, string text)
    {
        void Apply() => NativeMethods.SendMessage(box.Handle, NativeMethods.EM_SETCUEBANNER, (IntPtr)1, text);
        if (box.IsHandleCreated) Apply();
        box.HandleCreated += (_, _) => Apply();
    }

    /// <summary>The WinStart icon at the requested size.</summary>
    public static Icon AppIcon(Size size)
    {
        using Stream? stream = typeof(Ui).Assembly.GetManifestResourceStream("WinStart.ico");
        return stream != null ? new Icon(stream, size) : SystemIcons.Application;
    }

    /// <summary>Renders a toolbar icon, or returns null if the icon font is unavailable.</summary>
    public static Bitmap? Glyph(string glyph, int size, Color color)
    {
        try
        {
            using var font = new Font(GlyphFont, size, FontStyle.Regular, GraphicsUnit.Pixel);
            if (!font.Name.Equals(GlyphFont, StringComparison.OrdinalIgnoreCase)) return null;

            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using Graphics g = Graphics.FromImage(bitmap);
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using var brush = new SolidBrush(color);
            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), format);
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The generic "program" icon, for applications that have none of their own.</summary>
    public static Bitmap GenericAppIcon(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bitmap);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawIcon(SystemIcons.Application, new Rectangle(0, 0, size, size));
        return bitmap;
    }

    public static void OpenUrl(string url)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        Open(url);
    }

    /// <summary>Opens a file or folder with its default program.</summary>
    public static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not open " + target + ": " + ex.Message);
        }
    }
}

/// <summary>A flat, Explorer-like look for the toolbar.</summary>
internal sealed class FlatToolStripRenderer : ToolStripProfessionalRenderer
{
    public FlatToolStripRenderer() : base(new Colors())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // Menus keep their border; the toolbar itself has none.
        if (e.ToolStrip is ToolStripDropDown) base.OnRenderToolStripBorder(e);
    }

    private sealed class Colors : ProfessionalColorTable
    {
        private static readonly Color Hover = Color.FromArgb(229, 243, 255);
        private static readonly Color HoverBorder = Color.FromArgb(204, 232, 255);
        private static readonly Color Pressed = Color.FromArgb(204, 232, 255);
        private static readonly Color PressedBorder = Color.FromArgb(153, 209, 255);

        public Colors()
        {
            UseSystemColors = false;
        }

        public override Color ToolStripGradientBegin => SystemColors.Control;
        public override Color ToolStripGradientMiddle => SystemColors.Control;
        public override Color ToolStripGradientEnd => SystemColors.Control;
        public override Color ToolStripBorder => SystemColors.Control;
        public override Color ButtonSelectedHighlight => Hover;
        public override Color ButtonSelectedGradientBegin => Hover;
        public override Color ButtonSelectedGradientMiddle => Hover;
        public override Color ButtonSelectedGradientEnd => Hover;
        public override Color ButtonSelectedBorder => HoverBorder;
        public override Color ButtonPressedHighlight => Pressed;
        public override Color ButtonPressedGradientBegin => Pressed;
        public override Color ButtonPressedGradientMiddle => Pressed;
        public override Color ButtonPressedGradientEnd => Pressed;
        public override Color ButtonPressedBorder => PressedBorder;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemBorder => HoverBorder;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Pressed;
        public override Color MenuItemPressedGradientEnd => Pressed;
        public override Color SeparatorDark => SystemColors.ControlDark;
        public override Color SeparatorLight => SystemColors.Control;
    }
}
