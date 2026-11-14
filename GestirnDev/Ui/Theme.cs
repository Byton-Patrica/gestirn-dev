using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace GestirnDev
{
// DPI scaling. The process is DPI-aware, so every layout constant written in
// "logical pixels" (96-DPI units) goes through D() before it reaches a control.
internal static class Ui
{
    public static float Scale = 1f;

    public static void Init()
    {
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = Math.Max(1f, g.DpiX / 96f);
    }

    public static int D(float logical) { return (int)Math.Round(logical * Scale); }
    public static Size D(int w, int h) { return new Size(D(w), D(h)); }
    public static Padding Pad(int all) { return new Padding(D(all)); }
    public static Padding Pad(int l, int t, int r, int b) { return new Padding(D(l), D(t), D(r), D(b)); }

    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Max(0f, Math.Min(1f, t));
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    public static Color Alpha(Color c, int alpha) { return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c.R, c.G, c.B); }
}

// Palette sampled from the GestirnEra DataRot logo: ink black, ivory, orange, gold and lime pixels.
internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(11, 11, 13);
    public static readonly Color Panel = Color.FromArgb(18, 18, 21);
    public static readonly Color PanelHi = Color.FromArgb(24, 24, 28);
    public static readonly Color Raised = Color.FromArgb(32, 32, 37);
    public static readonly Color Hover = Color.FromArgb(42, 42, 48);
    public static readonly Color Edge = Color.FromArgb(38, 38, 44);
    public static readonly Color EdgeSoft = Color.FromArgb(28, 28, 33);

    public static readonly Color Text = Color.FromArgb(238, 235, 226);
    public static readonly Color TextDim = Color.FromArgb(176, 172, 162);
    public static readonly Color Muted = Color.FromArgb(118, 115, 108);
    public static readonly Color Faint = Color.FromArgb(78, 76, 72);

    public static readonly Color Orange = Color.FromArgb(240, 96, 24);
    public static readonly Color OrangeHot = Color.FromArgb(255, 124, 48);
    public static readonly Color OrangeDeep = Color.FromArgb(196, 74, 16);
    public static readonly Color Gold = Color.FromArgb(245, 179, 53);
    public static readonly Color Lime = Color.FromArgb(196, 242, 58);
    public static readonly Color Red = Color.FromArgb(255, 99, 99);
    public static readonly Color Blue = Color.FromArgb(120, 180, 255);
    public static readonly Color Violet = Color.FromArgb(201, 160, 255);
    public static readonly Color Ink = Color.FromArgb(14, 12, 10);

    // Editor
    public static readonly Color EditorBg = Color.FromArgb(14, 14, 17);
    public static readonly Color CurrentLine = Color.FromArgb(22, 22, 27);
    public static readonly Color Selection = Color.FromArgb(78, 240, 96, 24);
    public static readonly Color SelectionInactive = Color.FromArgb(48, 176, 172, 162);
    public static readonly Color GutterText = Color.FromArgb(74, 72, 68);
    public static readonly Color FindMatch = Color.FromArgb(70, 245, 179, 53);
    public static readonly Color BracketMatch = Color.FromArgb(120, 196, 242, 58);

    // Syntax
    public static readonly Color SynPlain = Color.FromArgb(222, 218, 208);
    public static readonly Color SynKeyword = Color.FromArgb(255, 124, 48);
    public static readonly Color SynType = Color.FromArgb(255, 184, 108);
    public static readonly Color SynString = Color.FromArgb(196, 242, 58);
    public static readonly Color SynNumber = Color.FromArgb(245, 179, 53);
    public static readonly Color SynComment = Color.FromArgb(108, 105, 98);
    public static readonly Color SynPreproc = Color.FromArgb(201, 160, 255);
    public static readonly Color SynFunction = Color.FromArgb(132, 196, 255);
    public static readonly Color SynOperator = Color.FromArgb(170, 166, 156);

    public static Font Ui9, Ui10, UiSemi, UiSmall, UiTitle, UiHero, Code, CodeSmall;
    public static string CodeFamily = "Consolas";

    public static void InitFonts(string preferredCode, float codeSize)
    {
        string ui = FontExists("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        Ui9 = new Font(ui, 9f);
        Ui10 = new Font(ui, 10f);
        UiSemi = new Font(FontExists("Segoe UI Semibold") ? "Segoe UI Semibold" : ui, 9.5f);
        UiSmall = new Font(ui, 8.5f);
        UiTitle = new Font(FontExists("Segoe UI Semibold") ? "Segoe UI Semibold" : ui, 12f);
        UiHero = new Font(FontExists("Segoe UI Semibold") ? "Segoe UI Semibold" : ui, 22f);
        CodeFamily = PickCodeFont(preferredCode);
        Code = new Font(CodeFamily, codeSize);
        CodeSmall = new Font(CodeFamily, 9f);
    }

    public static string PickCodeFont(string preferred)
    {
        foreach (var name in new[] { preferred, "Cascadia Mono", "JetBrains Mono", "Consolas" })
            if (!string.IsNullOrEmpty(name) && FontExists(name)) return name;
        return "Courier New";
    }

    private static HashSet<string> installed;
    public static bool FontExists(string name)
    {
        if (installed == null)
        {
            installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var fonts = new InstalledFontCollection())
                foreach (var family in fonts.Families) installed.Add(family.Name);
        }
        return installed.Contains(name);
    }
}

internal static class Shapes
{
    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f) * 2f;
        if (d <= 0.5f) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRound(Graphics g, Color color, RectangleF r, float radius)
    {
        if (color.A == 0 || r.Width <= 0 || r.Height <= 0) return;
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Round(r, radius))
        using (var brush = new SolidBrush(color)) g.FillPath(brush, path);
        g.SmoothingMode = mode;
    }

    public static void StrokeRound(Graphics g, Color color, RectangleF r, float radius, float width = 1f)
    {
        if (color.A == 0 || r.Width <= 0 || r.Height <= 0) return;
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float inset = width / 2f;
        var rr = new RectangleF(r.X + inset, r.Y + inset, r.Width - width, r.Height - width);
        using (var path = Round(rr, radius))
        using (var pen = new Pen(color, width)) g.DrawPath(pen, path);
        g.SmoothingMode = mode;
    }

    public static void Text(Graphics g, string text, Font font, Rectangle r, Color color, TextFormatFlags flags)
    {
        TextRenderer.DrawText(g, text, font, r, color, flags | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }

    public const TextFormatFlags Left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
    public const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
    public const TextFormatFlags Right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
}

// Square-pixel icons in the logo's language. Each glyph is a 7x7 (or 5x5) grid drawn
// with whole-pixel cells, so it stays crisp at any scale.
internal static class Glyphs
{
    private static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>
    {
        { "new", new[] { "...#...", "...#...", "...#...", "#######", "...#...", "...#...", "...#..." } },
        { "file", new[] { "#####..", "#...##.", "#....#.", "#....#.", "#....#.", "#....#.", "######." } },
        { "folder", new[] { ".......", "###....", "#..####", "#.....#", "#.....#", "#.....#", "#######" } },
        { "save", new[] { "######.", "#.##.##", "#.##..#", "#.....#", "#.###.#", "#.###.#", "#######" } },
        { "run", new[] { ".#.....", ".##....", ".###...", ".####..", ".###...", ".##....", ".#....." } },
        { "build", new[] { "...#...", "..###..", ".#####.", "###.###", ".#####.", "..###..", "...#..." } },
        { "stop", new[] { ".......", ".#####.", ".#####.", ".#####.", ".#####.", ".#####.", "......." } },
        { "debug", new[] { ".#...#.", "..###..", ".#####.", "##.#.##", ".#####.", "##.#.##", "..###.." } },
        { "continue", new[] { "#.#....", "#.##...", "#.###..", "#.####.", "#.###..", "#.##...", "#.#...." } },
        { "stepover", new[] { ".####..", "#....#.", "#...###", "#....#.", ".......", "..##...", "..##..." } },
        { "stepinto", new[] { "...#...", "...#...", ".#.#.#.", "..###..", "...#...", ".......", "..###.." } },
        { "stepout", new[] { "...#...", "..###..", ".#.#.#.", "...#...", "...#...", ".......", "..###.." } },
        { "term", new[] { "#......", ".#.....", "..#....", ".#.....", "#......", ".......", "...####" } },
        { "search", new[] { ".###...", "#...#..", "#...#..", "#...#..", ".###...", ".....#.", "......#" } },
        { "outline", new[] { "##.####", ".......", "##.####", ".......", "##.####", ".......", "##.####" } },
        { "gear", new[] { "..#.#..", ".#####.", "##...##", ".#...#.", "##...##", ".#####.", "..#.#.." } },
        { "close", new[] { "#.....#", ".#...#.", "..#.#..", "...#...", "..#.#..", ".#...#.", "#.....#" } },
        { "format", new[] { "#######", ".......", "..#####", ".......", "#######", ".......", "..#####" } },
        { "project", new[] { "#######", "#.....#", "#######", "#.....#", "#.#.#.#", "#.....#", "#######" } },
        { "sidebar", new[] { "#######", "##....#", "##....#", "##....#", "##....#", "##....#", "#######" } },
        { "panel", new[] { "#######", "#.....#", "#.....#", "#######", "#######", "#######", "#######" } },
        { "refresh", new[] { "..###.#", ".#...##", "#...###", "#......", "#.....#", ".#...#.", "..###.." } },
        { "collapse", new[] { "#######", ".......", "..###..", ".#####.", "#######", ".......", "#######" } },
        { "newfolder", new[] { "###....", "#..####", "#...#.#", "#.###.#", "#...#.#", "#.....#", "#######" } },
        { "palette", new[] { "#######", "#.....#", "#.#...#", "#..#..#", "#.#.###", "#.....#", "#######" } },
        { "trash", new[] { "..###..", "#######", ".#...#.", ".#.#.#.", ".#.#.#.", ".#...#.", "..###.." } },
        { "clear", new[] { "....##.", "...##..", "..##...", ".#####.", "#######", "#######", "......." } },
        { "check", new[] { ".......", "......#", ".....#.", "#...#..", ".#.#...", "..#....", "......." } },
        { "error", new[] { "#...#", ".#.#.", "..#..", ".#.#.", "#...#" } },
        { "warning", new[] { "..#..", "..#..", "..#..", ".....", "..#.." } },
        { "info", new[] { "..#..", ".....", "..#..", "..#..", "..#.." } },
        { "chevron-right", new[] { ".#...", "..#..", "...#.", "..#..", ".#..." } },
        { "chevron-down", new[] { ".....", "#...#", ".#.#.", "..#..", "....." } },
        { "up", new[] { "..#..", ".###.", "#.#.#", "..#..", "..#.." } },
        { "down", new[] { "..#..", "..#..", "#.#.#", ".###.", "..#.." } },
        { "dot", new[] { ".###.", "#####", "#####", "#####", ".###." } },
        { "breakpoint", new[] { ".###.", "#####", "#####", "#####", ".###." } },
        { "arrow", new[] { "..#..", "..##.", "#####", "..##.", "..#.." } },
        { "plus", new[] { "..#..", "..#..", "#####", "..#..", "..#.." } },
        { "minus", new[] { ".....", ".....", "#####", ".....", "....." } },
        { "case", new[] { "#####", "#...#", "#####", "#...#", "#...#" } },
        { "word", new[] { "#...#", "#...#", "#.#.#", "##.##", "#...#" } },
        { "replace", new[] { "###..", "..#..", "###.#", "....#", "..###" } },
        { "replaceall", new[] { "##.##", ".#..#", "##.##", "#..#.", "##.##" } },
        { "regex", new[] { "#.#.#", ".###.", "#.#.#", ".....", "#...." } },
        { "help", new[] { ".###.", "#...#", "...#.", "..#..", "..#.." } },
        { "recent", new[] { ".###.", "#.#.#", "#.###", "#...#", ".###." } },
    };

    public static bool Has(string name) { return name != null && Map.ContainsKey(name); }

    // Draws the glyph centred in box, using the largest whole-pixel cell that fits.
    public static void Draw(Graphics g, string name, Rectangle box, Color color)
    {
        string[] rows;
        if (name == null || !Map.TryGetValue(name, out rows)) return;
        int n = rows.Length;
        int gap = Math.Max(1, (int)Math.Round(Ui.Scale * 0.5f));
        int cell = Math.Max(1, (Math.Min(box.Width, box.Height) + gap) / n - gap);
        int total = n * cell + (n - 1) * gap;
        int x0 = box.X + (box.Width - total) / 2;
        int y0 = box.Y + (box.Height - total) / 2;
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        using (var brush = new SolidBrush(color))
        {
            for (int y = 0; y < n; y++)
                for (int x = 0; x < rows[y].Length; x++)
                    if (rows[y][x] == '#')
                        g.FillRectangle(brush, x0 + x * (cell + gap), y0 + y * (cell + gap), cell, cell);
        }
        g.SmoothingMode = mode;
    }

    // A 3x3 pixel spinner: one bright pixel chases around the ring, leaving a fading trail.
    private static readonly Point[] Ring = { new Point(0, 0), new Point(1, 0), new Point(2, 0), new Point(2, 1), new Point(2, 2), new Point(1, 2), new Point(0, 2), new Point(0, 1) };
    public static void Spinner(Graphics g, Rectangle box, double phase, Color head, Color tail)
    {
        int gap = Math.Max(1, (int)Math.Round(Ui.Scale * 0.5f));
        int cell = Math.Max(1, (Math.Min(box.Width, box.Height) + gap) / 3 - gap);
        int total = 3 * cell + 2 * gap;
        int x0 = box.X + (box.Width - total) / 2, y0 = box.Y + (box.Height - total) / 2;
        int lead = (int)(phase * Ring.Length) % Ring.Length;
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        for (int i = 0; i < Ring.Length; i++)
        {
            int age = (lead - i + Ring.Length) % Ring.Length;
            Color c = age == 0 ? head : Ui.Alpha(tail, Math.Max(0, 200 - age * 42));
            using (var b = new SolidBrush(c))
                g.FillRectangle(b, x0 + Ring[i].X * (cell + gap), y0 + Ring[i].Y * (cell + gap), cell, cell);
        }
        g.SmoothingMode = mode;
    }
}
}
