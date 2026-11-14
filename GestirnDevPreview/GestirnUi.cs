using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GestirnDevPreview
{
// Palette taken from the GestirnEra DataRot logo: ink black, ivory, orange and lime pixels.
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(12, 12, 14);
    public static readonly Color Surface = Color.FromArgb(22, 22, 25);
    public static readonly Color SurfaceRaised = Color.FromArgb(31, 31, 36);
    public static readonly Color SurfaceHover = Color.FromArgb(40, 40, 46);
    public static readonly Color Edge = Color.FromArgb(42, 42, 48);
    public static readonly Color Editor = Color.FromArgb(15, 15, 18);
    public static readonly Color Output = Color.FromArgb(9, 9, 11);
    public static readonly Color Text = Color.FromArgb(242, 239, 228);
    public static readonly Color Muted = Color.FromArgb(141, 138, 131);
    public static readonly Color Orange = Color.FromArgb(240, 96, 24);
    public static readonly Color OrangeHot = Color.FromArgb(255, 122, 46);
    public static readonly Color OrangeDeep = Color.FromArgb(200, 77, 18);
    public static readonly Color Lime = Color.FromArgb(196, 242, 58);
    public static readonly Color Ink = Color.FromArgb(12, 12, 14);
    public static readonly Color Disabled = Color.FromArgb(88, 86, 80);

    public static readonly Font UiFont = new Font("Segoe UI", 9.5f);
    public static readonly Font UiSemi = new Font("Segoe UI Semibold", 9.5f);
    public static readonly Font Small = new Font("Segoe UI", 8.5f);
    public static readonly Font Title = new Font("Segoe UI Semibold", 14f);
    public static readonly Font CodeFont = new Font("Consolas", 11f);
}

internal static class Shapes
{
    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f) * 2f;
        if (d <= 0f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

// 5x5 pixel glyphs in the same square-pixel language as the logo.
internal static class Glyphs
{
    public const int Size = 5;
    public const int Cell = 3;
    public const int Gap = 1;
    public const int Pixels = Size * Cell + (Size - 1) * Gap;

    private static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>
    {
        { "new", new[] { "..#..", "..#..", "#####", "..#..", "..#.." } },
        { "open", new[] { "##...", "#####", "#...#", "#...#", "#####" } },
        { "save", new[] { "#####", "#...#", "#.#.#", "#.#.#", "#####" } },
        { "run", new[] { "#....", "##...", "###..", "##...", "#...." } },
        { "term", new[] { "#....", ".#...", "..#..", ".....", "#####" } },
        { "stop", new[] { ".....", ".###.", ".###.", ".###.", "....." } },
    };

    public static void Draw(Graphics g, string name, Point origin, Color color)
    {
        string[] rows;
        if (!Map.TryGetValue(name, out rows)) return;
        g.SmoothingMode = SmoothingMode.None;
        using (var brush = new SolidBrush(color))
        {
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                    if (rows[y][x] == '#')
                        g.FillRectangle(brush, origin.X + x * (Cell + Gap), origin.Y + y * (Cell + Gap), Cell, Cell);
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
    }
}

// Rounded panel. Corners are painted in the parent's colour so the card reads as floating.
internal sealed class Card : Panel
{
    private Color fill = Theme.Surface;

    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Outline = Theme.Edge;
        Radius = 16;
    }

    public Color Fill
    {
        get { return fill; }
        set { fill = value; BackColor = value; Invalidate(); }
    }

    public Color Outline { get; set; }
    public int Radius { get; set; }
    public Image Picture { get; set; }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Background);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using (var path = Shapes.RoundRect(rect, Radius))
        {
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
            if (!Outline.IsEmpty)
                using (var pen = new Pen(Outline)) g.DrawPath(pen, path);
        }
        if (Picture != null)
        {
            var inner = new Rectangle(Padding.Left, Padding.Top, Width - Padding.Horizontal, Height - Padding.Vertical);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(Picture, inner);
        }
    }
}

// Pill-style button: rounded body, optional pixel glyph, centred label.
internal sealed class GlyphButton : Button
{
    private bool hover;
    private bool pressed;

    public GlyphButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = Theme.UiSemi;
        Cursor = Cursors.Hand;
        TabStop = false;
        Height = 38;
        Radius = 12;
    }

    public string Glyph { get; set; }
    public bool Primary { get; set; }
    public int Radius { get; set; }

    public void FitWidth(int padding)
    {
        int glyph = string.IsNullOrEmpty(Glyph) ? 0 : Glyphs.Pixels + 9;
        Width = padding * 2 + glyph + TextRenderer.MeasureText(Text, Font).Width;
    }

    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Background);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color fill, edge, ink, glyphColor;
        if (!Enabled)
        {
            fill = Theme.SurfaceRaised; edge = Theme.Edge; ink = Theme.Disabled; glyphColor = Theme.Disabled;
        }
        else if (Primary)
        {
            fill = pressed ? Theme.OrangeDeep : hover ? Theme.OrangeHot : Theme.Orange;
            edge = fill; ink = Theme.Ink; glyphColor = Theme.Ink;
        }
        else
        {
            fill = pressed ? Theme.Surface : hover ? Theme.SurfaceHover : Theme.SurfaceRaised;
            edge = hover ? Theme.Orange : Theme.Edge;
            ink = Theme.Text;
            glyphColor = hover ? Theme.OrangeHot : Theme.Orange;
        }

        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using (var path = Shapes.RoundRect(rect, Radius))
        {
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
            using (var pen = new Pen(edge)) g.DrawPath(pen, path);
        }

        int textWidth = TextRenderer.MeasureText(Text, Font).Width;
        bool hasGlyph = !string.IsNullOrEmpty(Glyph);
        int gap = hasGlyph && Text.Length > 0 ? 9 : 0;
        int total = (hasGlyph ? Glyphs.Pixels : 0) + gap + textWidth;
        int x = (Width - total) / 2;

        if (hasGlyph)
        {
            Glyphs.Draw(g, Glyph, new Point(x, (Height - Glyphs.Pixels) / 2), glyphColor);
            x += Glyphs.Pixels + gap;
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, textWidth + 8, Height), ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkMenuColors()) { }
}

internal sealed class DarkMenuColors : ProfessionalColorTable
{
    public override Color MenuBorder { get { return Theme.Edge; } }
    public override Color MenuItemSelected { get { return Theme.SurfaceHover; } }
    public override Color MenuItemBorder { get { return Theme.Orange; } }
    public override Color MenuItemSelectedGradientBegin { get { return Theme.SurfaceHover; } }
    public override Color MenuItemSelectedGradientEnd { get { return Theme.SurfaceHover; } }
    public override Color ToolStripDropDownBackground { get { return Theme.SurfaceRaised; } }
    public override Color ImageMarginGradientBegin { get { return Theme.SurfaceRaised; } }
    public override Color ImageMarginGradientMiddle { get { return Theme.SurfaceRaised; } }
    public override Color ImageMarginGradientEnd { get { return Theme.SurfaceRaised; } }
}
}
