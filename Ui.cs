using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Argus;

// -------------------------------------------------------------------------------------------------
//  Argus design system: near-black canvas, large rounded dark cards, white "selected" state,
//  small uppercase captions, one soft-blue accent. Inspired by the dashboard reference in assets/source.
// -------------------------------------------------------------------------------------------------

internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(10, 10, 12);
    public static readonly Color Card = Color.FromArgb(24, 24, 27);
    public static readonly Color Chip = Color.FromArgb(38, 38, 43);
    public static readonly Color ChipHover = Color.FromArgb(50, 50, 57);
    public static readonly Color Line = Color.FromArgb(36, 36, 41);
    public static readonly Color Input = Color.FromArgb(14, 14, 16);
    public static readonly Color Text = Color.FromArgb(242, 242, 245);
    public static readonly Color Muted = Color.FromArgb(141, 141, 150);
    public static readonly Color Faint = Color.FromArgb(98, 98, 107);
    public static readonly Color Accent = Color.FromArgb(127, 182, 230);   // soft blue (same as the wordmark accent)
    public static readonly Color Good = Color.FromArgb(143, 214, 160);
    public static readonly Color Warn = Color.FromArgb(230, 214, 90);
    public static readonly Color Bad = Color.FromArgb(240, 97, 109);

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr h, string app, string? id);

    private static readonly HashSet<string> Installed =
        FontFamily.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static string Pick(params string[] names) => names.FirstOrDefault(Installed.Contains) ?? names[^1];
    private static readonly string UiFamily = Pick("Segoe UI Variable Text", "Segoe UI");
    private static readonly string IconFamily = Pick("Segoe Fluent Icons", "Segoe MDL2 Assets");
    private static readonly Dictionary<(float, FontStyle, bool), Font> Fonts = new();

    public static Font Ui(float pt, FontStyle style = FontStyle.Regular) => Get(UiFamily, pt, style, false);
    public static Font Icon(float pt) => Get(IconFamily, pt, FontStyle.Regular, true);

    private static Font Get(string family, float pt, FontStyle style, bool icon)
    {
        lock (Fonts)
        {
            if (!Fonts.TryGetValue((pt, style, icon), out var f))
                Fonts[(pt, style, icon)] = f = new Font(family, pt, style, GraphicsUnit.Point);
            return f;
        }
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static void Draw(Graphics g, string text, Font font, Rectangle r, Color color, TextFormatFlags flags) =>
        TextRenderer.DrawText(g, text, font, r, color, flags | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

    public static int Measure(string text, Font font) =>
        TextRenderer.MeasureText(text, font, new Size(int.MaxValue, 100), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;

    public static void DarkTitleBar(IntPtr h)
    {
        int on = 1; DwmSetWindowAttribute(h, 20, ref on, 4);                     // immersive dark mode
        int cap = Bg.B << 16 | Bg.G << 8 | Bg.R; DwmSetWindowAttribute(h, 35, ref cap, 4);   // caption colour (Win11)
        int txt = Text.B << 16 | Text.G << 8 | Text.R; DwmSetWindowAttribute(h, 36, ref txt, 4);
    }

    /// <summary>Dark scrollbars on controls that scroll natively.</summary>
    public static void DarkScroll(Control c)
    {
        void Apply() { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } }
        if (c.IsHandleCreated) Apply(); else c.HandleCreated += (_, _) => Apply();
    }

    /// <summary>Dark, roomy, owner-drawn list with custom check boxes.</summary>
    public static void StyleList(ListView lv, bool checks)
    {
        lv.OwnerDraw = true; lv.View = View.Details; lv.BorderStyle = BorderStyle.None;
        lv.BackColor = Card; lv.ForeColor = Text; lv.Font = Ui(9.5f);
        lv.FullRowSelect = true; lv.GridLines = false; lv.HideSelection = false; lv.CheckBoxes = checks;
        lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        lv.SmallImageList = new ImageList { ImageSize = new Size(1, 38) };      // row height
        typeof(ListView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lv, true);
        DarkScroll(lv);

        lv.DrawColumnHeader += (_, e) =>
        {
            using var b = new SolidBrush(Card); e.Graphics.FillRectangle(b, e.Bounds);
            using var p = new Pen(Line); e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            int pad = e.ColumnIndex == 0 && checks ? 34 : 8;
            Draw(e.Graphics, (e.Header?.Text ?? "").ToUpperInvariant(), Ui(7.5f, FontStyle.Bold),
                new Rectangle(e.Bounds.X + pad, e.Bounds.Y, Math.Max(0, e.Bounds.Width - pad), e.Bounds.Height), Faint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        lv.DrawItem += (_, _) => { };   // everything is painted per cell below
        lv.DrawSubItem += (_, e) =>
        {
            if (e.Item == null || e.SubItem == null) return;
            var g = e.Graphics; Smooth(g);
            using (var b = new SolidBrush(e.Item.Selected ? Color.FromArgb(34, 42, 52) : Card)) g.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Color.FromArgb(30, 30, 34))) g.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

            int x = e.Bounds.X + 8;
            if (e.ColumnIndex == 0 && checks)
            {
                var box = new RectangleF(e.Bounds.X + 8, e.Bounds.Y + (e.Bounds.Height - 17) / 2f, 17, 17);
                using var path = Round(box, 5);
                if (e.Item.Checked)
                {
                    using var fill = new SolidBrush(Accent); g.FillPath(fill, path);
                    using var tick = new Pen(Bg, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                    g.DrawLines(tick, new[] { new PointF(box.X + 4, box.Y + 8.5f), new PointF(box.X + 7.2f, box.Y + 11.8f), new PointF(box.X + 13, box.Y + 5) });
                }
                else { using var pen = new Pen(Faint, 1.5f); g.DrawPath(pen, path); }
                x = e.Bounds.X + 34;
            }
            var color = e.Item.ForeColor;
            Draw(g, e.SubItem.Text, lv.Font, new Rectangle(x, e.Bounds.Y, Math.Max(0, e.Bounds.Right - x - 6), e.Bounds.Height), color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        // last column soaks up the remaining width
        lv.Resize += (_, _) =>
        {
            if (lv.Columns.Count == 0) return;
            int others = 0; for (int i = 0; i < lv.Columns.Count - 1; i++) others += lv.Columns[i].Width;
            lv.Columns[^1].Width = Math.Max(120, lv.ClientSize.Width - others);
        };
    }
}

// ---- base ---------------------------------------------------------------------------------------

internal abstract class Ctl : Control
{
    protected bool Hover, Pressed;

    protected Ctl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card; ForeColor = Theme.Text; Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { Pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(BackColor);
}

// ---- cards -------------------------------------------------------------------------------------

/// <summary>Large rounded dark panel with an optional title and caption.</summary>
internal class Card : Panel
{
    private string _title = "", _sub = "";

    public Card()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card; ForeColor = Theme.Text; Margin = new Padding(8);
        UpdatePadding();
    }

    public string Title { get => _title; set { _title = value; UpdatePadding(); Invalidate(); } }
    public string Subtitle { get => _sub; set { _sub = value; UpdatePadding(); Invalidate(); } }

    public int HeaderHeight => _title.Length == 0 ? 20 : (_sub.Length == 0 ? 58 : 78);

    private void UpdatePadding() => Padding = new Padding(24, HeaderHeight, 24, 22);

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        Theme.Smooth(g);
        using var path = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), 22);
        using var b = new SolidBrush(Theme.Card);
        g.FillPath(b, path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_title.Length == 0) return;
        Theme.Draw(e.Graphics, _title, Theme.Ui(13f, FontStyle.Bold), new Rectangle(24, 20, Width - 48, 26), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        if (_sub.Length > 0)
            Theme.Draw(e.Graphics, _sub, Theme.Ui(9f), new Rectangle(24, 46, Width - 48, 22), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A card of label + hint + control rows (the Rules page).</summary>
internal sealed class RowCard : Card
{
    private const int RowH = 62;
    private readonly List<(string Label, string Hint, Control Ctl)> _rows = new();

    public RowCard() { Resize += (_, _) => Place(); }

    public void AddRow(string label, string hint, Control ctl)
    {
        _rows.Add((label, hint, ctl));
        Controls.Add(ctl);
        Height = HeaderHeight + _rows.Count * RowH + 14;
        Place();
    }

    private void Place()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            var c = _rows[i].Ctl;
            c.Left = Width - Padding.Right - c.Width;
            c.Top = HeaderHeight + i * RowH + (RowH - c.Height) / 2;
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        for (int i = 0; i < _rows.Count; i++)
        {
            int y = HeaderHeight + i * RowH;
            int maxW = Math.Max(40, _rows[i].Ctl.Left - 36 - Padding.Left);
            using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, Padding.Left, y, Width - Padding.Right, y);
            Theme.Draw(e.Graphics, _rows[i].Label, Theme.Ui(10f, FontStyle.Bold), new Rectangle(Padding.Left, y + 12, maxW, 20), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            if (_rows[i].Hint.Length > 0)
                Theme.Draw(e.Graphics, _rows[i].Hint, Theme.Ui(8.5f), new Rectangle(Padding.Left, y + 33, maxW, 20), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }
}

// ---- buttons ---------------------------------------------------------------------------------

internal enum PillStyle { Primary, Secondary, Danger, Ghost }

internal sealed class PillButton : Ctl
{
    public string Glyph { get; set; } = "";
    public PillStyle Style { get; set; }

    public PillButton(string text, string glyph = "", PillStyle style = PillStyle.Secondary, int width = 0)
    {
        Text = text; Glyph = glyph; Style = style; Height = 38;
        Width = width > 0 ? width : Theme.Measure(text, Theme.Ui(9.25f, FontStyle.Bold)) + 40 + (glyph.Length > 0 ? 28 : 0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Smooth(g);
        Color bg, fg; Color? border = null;
        switch (Style)
        {
            case PillStyle.Primary: bg = Hover ? Color.White : Theme.Text; fg = Theme.Bg; break;
            case PillStyle.Danger: bg = Hover ? Color.FromArgb(92, 38, 46) : Color.FromArgb(66, 30, 36); fg = Color.FromArgb(255, 150, 158); break;
            case PillStyle.Ghost: bg = Hover ? Theme.Chip : Theme.Card; fg = Theme.Muted; border = Theme.Line; break;
            default: bg = Hover ? Theme.ChipHover : Theme.Chip; fg = Theme.Text; break;
        }
        if (Pressed) bg = Theme.Blend(bg, Theme.Bg, 0.25f);
        if (!Enabled) { bg = Theme.Blend(bg, BackColor, 0.65f); fg = Theme.Faint; }

        using var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), (Height - 2) / 2f);
        using (var b = new SolidBrush(bg)) g.FillPath(b, path);
        if (border != null) { using var p = new Pen(border.Value); g.DrawPath(p, path); }

        var font = Theme.Ui(9.25f, FontStyle.Bold);
        int tw = Theme.Measure(Text, font);
        if (Glyph.Length > 0)
        {
            int total = 22 + 6 + tw, x0 = (Width - total) / 2;
            Theme.Draw(g, Glyph, Theme.Icon(10f), new Rectangle(x0, 0, 22, Height), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.Draw(g, Text, font, new Rectangle(x0 + 28, 0, tw + 6, Height), fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
        else Theme.Draw(g, Text, font, new Rectangle(0, 0, Width, Height), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Round icon button for the sidebar. Active = filled white circle (like the reference).</summary>
internal sealed class NavButton : Ctl
{
    public string Glyph { get; set; } = "";
    public bool Active { get; set; }
    public int Badge { get; set; }

    public NavButton(string glyph) { Glyph = glyph; Size = new Size(52, 52); BackColor = Theme.Bg; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Smooth(g);
        var r = new Rectangle(4, 4, Width - 9, Height - 9);
        if (Active) { using var b = new SolidBrush(Theme.Text); g.FillEllipse(b, r); }
        else if (Hover) { using var b = new SolidBrush(Theme.Chip); g.FillEllipse(b, r); }
        var col = Active ? Theme.Bg : (Hover ? Theme.Text : Theme.Muted);
        Theme.Draw(g, Glyph, Theme.Icon(14f), r, col, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (Badge > 0)
        {
            var br = new Rectangle(Width - 22, 2, 19, 19);
            using var b = new SolidBrush(Theme.Bad); g.FillEllipse(b, br);
            Theme.Draw(g, Badge > 9 ? "9+" : Badge.ToString(), Theme.Ui(7.5f, FontStyle.Bold), br, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

/// <summary>Brand mark at the top of the sidebar.</summary>
internal sealed class ImageBox : Ctl
{
    public Image? Picture { get; set; }

    public ImageBox(Image? img, Color back) { Picture = img; BackColor = back; Cursor = Cursors.Default; }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Picture == null) return;
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float s = Math.Min((float)Width / Picture.Width, (float)Height / Picture.Height);
        float w = Picture.Width * s, h = Picture.Height * s;
        g.DrawImage(Picture, (Width - w) / 2f, (Height - h) / 2f, w, h);
    }
}

// ---- inputs ----------------------------------------------------------------------------------

/// <summary>Pill-shaped choice switch ("Off | Ask | Auto"). Selected segment is white.</summary>
internal sealed class Segmented : Ctl
{
    private readonly string[] _items;
    private readonly int[] _w;
    private int _sel;

    public event EventHandler? Changed;

    public Segmented(params string[] items)
    {
        _items = items;
        _w = items.Select(i => Math.Max(62, Theme.Measure(i, Theme.Ui(9f, FontStyle.Bold)) + 30)).ToArray();
        Size = new Size(_w.Sum() + 8, 38);
        BackColor = Theme.Card;
    }

    public int SelectedIndex
    {
        get => _sel;
        set { if (value == _sel) return; _sel = value; Invalidate(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    public void SetSilently(int index) { _sel = index; Invalidate(); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int x = 4;
        for (int i = 0; i < _items.Length; i++) { if (e.X >= x && e.X < x + _w[i]) { SelectedIndex = i; return; } x += _w[i]; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Smooth(g);
        using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), (Height - 2) / 2f))
        using (var b = new SolidBrush(Theme.Chip)) g.FillPath(b, path);
        int x = 4;
        for (int i = 0; i < _items.Length; i++)
        {
            var r = new RectangleF(x, 4, _w[i], Height - 8);
            bool on = i == _sel;
            if (on) { using var p = Theme.Round(r, r.Height / 2); using var b = new SolidBrush(Theme.Text); g.FillPath(b, p); }
            Theme.Draw(g, _items[i], Theme.Ui(9f, FontStyle.Bold), Rectangle.Round(r), on ? Theme.Bg : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            x += _w[i];
        }
    }
}

internal sealed class Toggle : Ctl
{
    private bool _on;
    public event EventHandler? Changed;

    public Toggle() { Size = new Size(48, 28); }

    public bool Checked
    {
        get => _on;
        set { if (_on == value) return; _on = value; Invalidate(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    public void SetSilently(bool v) { _on = v; Invalidate(); }

    protected override void OnClick(EventArgs e) { Checked = !_on; base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Smooth(g);
        using (var path = Theme.Round(new RectangleF(0.5f, 2.5f, Width - 2, Height - 6), (Height - 6) / 2f))
        using (var b = new SolidBrush(_on ? Theme.Accent : Theme.Chip)) g.FillPath(b, path);
        float d = Height - 10, x = _on ? Width - d - 5 : 5;
        using var k = new SolidBrush(_on ? Theme.Bg : Theme.Muted);
        g.FillEllipse(k, x, 5, d, d);
    }
}

/// <summary>[ - ]  value  [ + ]</summary>
internal sealed class Stepper : Ctl
{
    private decimal _value;
    public decimal Minimum { get; set; }
    public decimal Maximum { get; set; } = 100;
    public decimal Step { get; set; } = 1;
    public int Decimals { get; set; }
    public string Suffix { get; set; } = "";
    public event EventHandler? Changed;

    public Stepper() { Size = new Size(170, 38); BackColor = Theme.Card; }

    public decimal Value
    {
        get => _value;
        set { value = Math.Min(Maximum, Math.Max(Minimum, value)); if (value == _value) return; _value = value; Invalidate(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    public void SetSilently(decimal v) { _value = Math.Min(Maximum, Math.Max(Minimum, v)); Invalidate(); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.X < Height) Value -= Step; else if (e.X > Width - Height) Value += Step;
    }

    protected override void OnMouseWheel(MouseEventArgs e) { Value += e.Delta > 0 ? Step : -Step; base.OnMouseWheel(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Smooth(g);
        using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), (Height - 2) / 2f))
        using (var b = new SolidBrush(Theme.Chip)) g.FillPath(b, path);
        Theme.Draw(g, "", Theme.Icon(9f), new Rectangle(0, 0, Height, Height), _value <= Minimum ? Theme.Faint : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        Theme.Draw(g, "", Theme.Icon(9f), new Rectangle(Width - Height, 0, Height, Height), _value >= Maximum ? Theme.Faint : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        Theme.Draw(g, _value.ToString("F" + Decimals) + Suffix, Theme.Ui(9.75f, FontStyle.Bold), new Rectangle(Height, 0, Width - 2 * Height, Height), Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

// ---- read-outs -------------------------------------------------------------------------------

internal sealed class StatusPill : Ctl
{
    public Color Dot { get; set; } = Theme.Good;
    public StatusPill() { Height = 34; Cursor = Cursors.Default; BackColor = Theme.Bg; }

    public void Set(string text, Color dot)
    {
        Text = text; Dot = dot;
        Width = Theme.Measure(text, Theme.Ui(9f, FontStyle.Bold)) + 46;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Smooth(g);
        using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), (Height - 2) / 2f))
        using (var b = new SolidBrush(Theme.Card)) { g.FillPath(b, path); using var p = new Pen(Theme.Line); g.DrawPath(p, path); }
        using (var d = new SolidBrush(Dot)) g.FillEllipse(d, 14, Height / 2f - 4, 8, 8);
        Theme.Draw(g, Text, Theme.Ui(9f, FontStyle.Bold), new Rectangle(28, 0, Width - 32, Height), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Small uppercase caption over a big number.</summary>
internal sealed class Stat : Ctl
{
    public string Caption { get; set; } = "";
    public string Value { get; set; } = "0";
    public Color ValueColor { get; set; } = Theme.Text;

    public Stat(string caption) { Caption = caption; Size = new Size(128, 66); Cursor = Cursors.Default; }

    public void Set(string value, Color? color = null) { Value = value; ValueColor = color ?? Theme.Text; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Draw(e.Graphics, Caption.ToUpperInvariant(), Theme.Ui(7.5f, FontStyle.Bold), new Rectangle(0, 0, Width, 18), Theme.Faint, TextFormatFlags.Left | TextFormatFlags.Top);
        Theme.Draw(e.Graphics, Value, Theme.Ui(21f, FontStyle.Bold), new Rectangle(0, 20, Width, 44), ValueColor, TextFormatFlags.Left | TextFormatFlags.Top);
    }
}

/// <summary>Label + value with a thin line and knob underneath (like "Altitude limited" in the reference).</summary>
internal sealed class Meter : Ctl
{
    public string Label { get; set; } = "";
    public string ValueText { get; set; } = "";
    public double Fraction { get; set; }
    public Color Fill { get; set; } = Theme.Text;

    public Meter(string label) { Label = label; Height = 54; Cursor = Cursors.Default; }

    public void Set(double fraction, string valueText, Color? fill = null)
    {
        Fraction = Math.Clamp(fraction, 0, 1); ValueText = valueText; Fill = fill ?? Theme.Text; Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Smooth(g);
        Theme.Draw(g, Label, Theme.Ui(9.5f, FontStyle.Bold), new Rectangle(0, 0, Width / 2, 22), Theme.Text, TextFormatFlags.Left | TextFormatFlags.Top);
        Theme.Draw(g, ValueText, Theme.Ui(9.5f, FontStyle.Bold), new Rectangle(Width / 2, 0, Width / 2, 22), Fill, TextFormatFlags.Right | TextFormatFlags.Top);
        float y = Height - 14, x = 6 + (float)(Width - 12) * (float)Fraction;
        using (var track = new Pen(Theme.Chip, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(track, 6, y, Width - 6, y);
        using (var fill = new Pen(Fill, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(fill, 6, y, Math.Max(7, x), y);
        using (var ring = new SolidBrush(BackColor)) g.FillEllipse(ring, x - 8, y - 8, 16, 16);
        using (var knob = new SolidBrush(Color.FromArgb(196, 196, 204))) g.FillEllipse(knob, x - 5.5f, y - 5.5f, 11, 11);
    }
}

internal sealed class KeyValue : Ctl
{
    public string Label { get; set; } = "";
    public string ValueText { get; set; } = "";

    public KeyValue(string label) { Label = label; Height = 30; Cursor = Cursors.Default; }

    public void Set(string value) { ValueText = value; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 0, 0, Width, 0);
        Theme.Draw(e.Graphics, Label.ToUpperInvariant(), Theme.Ui(7.5f, FontStyle.Bold), new Rectangle(0, 0, Width / 2, Height), Theme.Faint, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        Theme.Draw(e.Graphics, ValueText, Theme.Ui(9.5f, FontStyle.Bold), new Rectangle(Width / 2, 0, Width / 2, Height), Theme.Text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Round dial made of ticks (the compass in the reference), showing a 0-100 value.</summary>
internal sealed class RingGauge : Ctl
{
    public double Percent { get; private set; }
    public string Pill { get; private set; } = "";
    public string Center { get; private set; } = "";
    public string Sub { get; private set; } = "";
    public Color Fill { get; private set; } = Theme.Accent;

    public RingGauge() { Cursor = Cursors.Default; MinimumSize = new Size(180, 180); }

    public void Set(double percent, string pill, string center, string sub, Color fill)
    {
        Percent = Math.Clamp(percent, 0, 100); Pill = pill; Center = center; Sub = sub; Fill = fill; Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Smooth(g);
        int pillH = 28, top = pillH + 14;
        float size = Math.Min(Width, Height - top) - 6;
        if (size < 60) return;
        float cx = Width / 2f, cy = top + (Height - top) / 2f, R = size / 2f;

        const int ticks = 60;
        for (int i = 0; i < ticks; i++)
        {
            double a = (-90 + i * 360.0 / ticks) * Math.PI / 180;
            bool major = i % 5 == 0;
            float len = major ? 11 : 6;
            bool lit = i < Math.Round(Percent / 100.0 * ticks);
            using var pen = new Pen(lit ? Fill : Theme.Chip, major ? 2.4f : 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen, cx + (float)Math.Cos(a) * (R - len), cy + (float)Math.Sin(a) * (R - len), cx + (float)Math.Cos(a) * R, cy + (float)Math.Sin(a) * R);
        }
        float ir = R - 28;
        using (var b = new SolidBrush(Color.FromArgb(18, 18, 20))) g.FillEllipse(b, cx - ir, cy - ir, ir * 2, ir * 2);
        using (var p = new Pen(Theme.Line)) g.DrawEllipse(p, cx - ir, cy - ir, ir * 2, ir * 2);
        Theme.Draw(g, Center, Theme.Ui(Math.Max(14f, ir / 3.2f), FontStyle.Bold), new Rectangle((int)(cx - ir), (int)(cy - ir * 0.55f), (int)(ir * 2), (int)(ir * 0.7f)), Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        Theme.Draw(g, Sub, Theme.Ui(8.5f), new Rectangle((int)(cx - ir), (int)(cy + ir * 0.18f), (int)(ir * 2), 24), Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);

        int pw = Theme.Measure(Pill, Theme.Ui(9f, FontStyle.Bold)) + 30;
        var pr = new RectangleF(cx - pw / 2f, 2, pw, pillH);
        using (var path = Theme.Round(pr, pillH / 2f)) using (var b = new SolidBrush(Fill)) g.FillPath(b, path);
        Theme.Draw(g, Pill, Theme.Ui(9f, FontStyle.Bold), Rectangle.Round(pr), Theme.Bg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Live history graph (about two minutes): big current value, peak, light grid, soft filled line.</summary>
internal sealed class LineGraph : Ctl
{
    private readonly Queue<double> _v = new();

    public int Capacity { get; set; } = 120;
    public string? Unavailable { get; private set; }
    public int Count => _v.Count;
    public double? Last => _v.Count > 0 ? _v.Last() : null;

    public LineGraph() { Cursor = Cursors.Default; MinimumSize = new Size(150, 120); }

    public void Push(double percent)
    {
        _v.Enqueue(Math.Clamp(percent, 0, 100));
        while (_v.Count > Capacity) _v.Dequeue();
        Invalidate();
    }

    public void SetUnavailable(string? message)
    {
        if (Unavailable == message) return;
        Unavailable = message; Invalidate();
    }

    private static Color Level(double p) => p >= 90 ? Theme.Bad : p >= 70 ? Theme.Warn : Theme.Accent;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Smooth(g);
        if (Unavailable != null)
        {
            Theme.Draw(g, Unavailable, Theme.Ui(9.5f), new Rectangle(8, 0, Width - 16, Height), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        double last = Last ?? 0; var col = Level(last);
        Theme.Draw(g, Last == null ? "--" : $"{last:0}%", Theme.Ui(22f, FontStyle.Bold), new Rectangle(0, 0, Width / 2, 38), col, TextFormatFlags.Left | TextFormatFlags.Top);
        double peak = _v.Count > 0 ? _v.Max() : 0;
        Theme.Draw(g, $"peak {peak:0}%", Theme.Ui(8.5f), new Rectangle(Width / 2, 10, Width / 2, 20), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.Top);

        var plot = new RectangleF(0, 48, Width, Math.Max(10, Height - 48 - 22));
        using (var grid = new Pen(Theme.Line, 1f))
            foreach (double f in new[] { 0.0, 0.5, 1.0 })
            {
                float y = plot.Bottom - (float)f * plot.Height;
                g.DrawLine(grid, plot.Left, y, plot.Right, y);
            }
        Theme.Draw(g, "100", Theme.Ui(7f), new Rectangle((int)plot.Right - 32, (int)plot.Top + 2, 32, 12), Theme.Faint, TextFormatFlags.Right | TextFormatFlags.Top);
        Theme.Draw(g, "50", Theme.Ui(7f), new Rectangle((int)plot.Right - 32, (int)(plot.Top + plot.Height / 2) + 2, 32, 12), Theme.Faint, TextFormatFlags.Right | TextFormatFlags.Top);

        if (_v.Count >= 2)
        {
            int n = _v.Count; float step = plot.Width / (Capacity - 1);
            var pts = new PointF[n]; int i = 0;
            foreach (var v in _v) { pts[i] = new PointF(plot.Right - (n - 1 - i) * step, plot.Bottom - (float)(v / 100.0) * plot.Height); i++; }
            var area = pts.Concat(new[] { new PointF(pts[^1].X, plot.Bottom), new PointF(pts[0].X, plot.Bottom) }).ToArray();
            using (var br = new LinearGradientBrush(new RectangleF(plot.X, plot.Y, plot.Width, plot.Height + 1), Color.FromArgb(95, col), Color.FromArgb(0, col), LinearGradientMode.Vertical))
                g.FillPolygon(br, area);
            using (var pen = new Pen(col, 2.2f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLines(pen, pts);
            using (var dot = new SolidBrush(col)) g.FillEllipse(dot, pts[^1].X - 3.5f, pts[^1].Y - 3.5f, 7, 7);
        }

        string span = Capacity >= 60 ? $"{Capacity / 60} min ago" : $"{Capacity} s ago";
        Theme.Draw(g, span, Theme.Ui(7.5f), new Rectangle(0, Height - 18, Width / 2, 16), Theme.Faint, TextFormatFlags.Left | TextFormatFlags.Top);
        Theme.Draw(g, "now", Theme.Ui(7.5f), new Rectangle(Width / 2, Height - 18, Width / 2, 16), Theme.Faint, TextFormatFlags.Right | TextFormatFlags.Top);
    }
}
/// <summary>The latest few events as tidy rows (time + message, wrapped to two lines), newest first.</summary>
internal sealed class ActivityPeek : Ctl
{
    private string[] _lines = Array.Empty<string>();

    public ActivityPeek() { Cursor = Cursors.Default; }

    public void Set(IEnumerable<string> lines)
    {
        _lines = lines.Reverse().ToArray();       // newest first
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Smooth(g);
        if (_lines.Length == 0)
        {
            Theme.Draw(g, "Nothing yet.", Theme.Ui(9.5f), new Rectangle(0, 0, Width, 28), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.Top);
            return;
        }
        const int rowH = 56;
        for (int i = 0, y = 0; i < _lines.Length && y + rowH <= Height + 4; i++, y += rowH)
        {
            string raw = _lines[i];
            bool hasTime = raw.Length >= 8 && raw[2] == ':' && raw[5] == ':';
            string time = hasTime ? raw[..8] : "";
            string msg = hasTime ? raw[8..].TrimStart() : raw;
            if (i > 0) { using var p = new Pen(Theme.Line); g.DrawLine(p, 0, y, Width, y); }
            Theme.Draw(g, time, Theme.Ui(7.5f, FontStyle.Bold), new Rectangle(0, y + 6, Width, 14), Theme.Faint, TextFormatFlags.Left | TextFormatFlags.Top);
            Theme.Draw(g, msg, Theme.Ui(9f), new Rectangle(0, y + 20, Width, rowH - 22), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }
}