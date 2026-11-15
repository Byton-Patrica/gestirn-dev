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
// Two variants: dark (default) and light. Apply() swaps them at runtime.
internal static class Theme
{
    public static bool IsLight;
    public static Color Bg, Panel, PanelHi, Raised, Hover, Edge, EdgeSoft;
    public static Color Text, TextDim, Muted, Faint;
    public static Color Orange, OrangeHot, OrangeDeep, Gold, Lime, Red, Blue, Violet, Ink;
    public static Color EditorBg, CurrentLine, Selection, SelectionInactive, GutterText, FindMatch, BracketMatch, TerminalBg;
    public static Color SynPlain, SynKeyword, SynType, SynString, SynNumber, SynComment, SynPreproc, SynFunction, SynOperator;

    static Theme() { Apply(false); }

    public static void Apply(bool light)
    {
        IsLight = light;
        if (!light)
        {
            Bg = C(11, 11, 13); Panel = C(18, 18, 21); PanelHi = C(24, 24, 28); Raised = C(32, 32, 37); Hover = C(42, 42, 48);
            Edge = C(38, 38, 44); EdgeSoft = C(28, 28, 33);
            Text = C(238, 235, 226); TextDim = C(176, 172, 162); Muted = C(118, 115, 108); Faint = C(78, 76, 72);
            Orange = C(240, 96, 24); OrangeHot = C(255, 124, 48); OrangeDeep = C(196, 74, 16); Gold = C(245, 179, 53);
            Lime = C(196, 242, 58); Red = C(255, 99, 99); Blue = C(120, 180, 255); Violet = C(201, 160, 255); Ink = C(14, 12, 10);
            EditorBg = C(14, 14, 17); CurrentLine = C(22, 22, 27); Selection = Color.FromArgb(78, 240, 96, 24);
            SelectionInactive = Color.FromArgb(48, 176, 172, 162); GutterText = C(74, 72, 68); FindMatch = Color.FromArgb(70, 245, 179, 53);
            BracketMatch = Color.FromArgb(120, 196, 242, 58); TerminalBg = C(12, 12, 15);
            SynPlain = C(222, 218, 208); SynKeyword = C(255, 124, 48); SynType = C(255, 184, 108); SynString = C(196, 242, 58);
            SynNumber = C(245, 179, 53); SynComment = C(108, 105, 98); SynPreproc = C(201, 160, 255); SynFunction = C(132, 196, 255);
            SynOperator = C(170, 166, 156);
        }
        else
        {
            Bg = C(238, 235, 228); Panel = C(247, 245, 240); PanelHi = C(255, 255, 255); Raised = C(232, 228, 220); Hover = C(222, 217, 207);
            Edge = C(214, 209, 199); EdgeSoft = C(226, 222, 213);
            Text = C(30, 28, 25); TextDim = C(78, 74, 68); Muted = C(128, 123, 115); Faint = C(170, 165, 156);
            Orange = C(222, 84, 14); OrangeHot = C(240, 102, 30); OrangeDeep = C(180, 64, 8); Gold = C(176, 120, 0);
            Lime = C(86, 138, 0); Red = C(214, 52, 52); Blue = C(40, 104, 210); Violet = C(124, 78, 200); Ink = C(255, 252, 246);
            EditorBg = C(255, 255, 255); CurrentLine = C(248, 245, 238); Selection = Color.FromArgb(70, 240, 96, 24);
            SelectionInactive = Color.FromArgb(60, 120, 116, 108); GutterText = C(176, 171, 162); FindMatch = Color.FromArgb(90, 245, 179, 53);
            BracketMatch = Color.FromArgb(150, 86, 138, 0); TerminalBg = C(252, 251, 248);
            SynPlain = C(36, 34, 30); SynKeyword = C(206, 70, 6); SynType = C(166, 90, 10); SynString = C(72, 126, 0);
            SynNumber = C(150, 100, 0); SynComment = C(140, 136, 128); SynPreproc = C(124, 70, 196); SynFunction = C(30, 98, 190);
            SynOperator = C(96, 92, 84);
        }
    }

    private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

    // Snapshot of every palette colour, used to remap live controls when the theme changes.
    public static Dictionary<string, Color> Snapshot()
    {
        var d = new Dictionary<string, Color>();
        foreach (var f in typeof(Theme).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            if (f.FieldType == typeof(Color)) d[f.Name] = (Color)f.GetValue(null);
        return d;
    }

    public static bool SystemPrefersLight()
    {
        try
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object v = key != null ? key.GetValue("AppsUseLightTheme") : null;
                return v is int && (int)v == 1;
            }
        }
        catch (System.Security.SecurityException) { return false; }
    }

    // Repaints a control tree in the new palette by mapping each old palette colour to its new value.
    public static void Remap(Control root, Dictionary<string, Color> before)
    {
        var after = Snapshot();
        var map = new Dictionary<int, Color>();
        foreach (var kv in before)
        {
            int key = kv.Value.ToArgb();
            if (!map.ContainsKey(key)) map[key] = after[kv.Key];
        }
        RemapTree(root, map);
    }

    private static void RemapTree(Control c, Dictionary<int, Color> map)
    {
        Color n;
        var card = c as Card;
        if (card != null)
        {
            if (map.TryGetValue(card.Fill.ToArgb(), out n)) card.Fill = n;
            if (card.Outline.A > 0 && map.TryGetValue(card.Outline.ToArgb(), out n)) card.Outline = n;
        }
        else if (c.BackColor.A == 255 && map.TryGetValue(c.BackColor.ToArgb(), out n)) c.BackColor = n;
        if (map.TryGetValue(c.ForeColor.ToArgb(), out n)) c.ForeColor = n;
        var strip = c as ToolStrip;
        if (strip != null)
            foreach (ToolStripItem item in strip.Items) RemapItem(item, map);
        foreach (Control child in c.Controls) RemapTree(child, map);
        if (c.ContextMenuStrip != null) RemapTree(c.ContextMenuStrip, map);
        c.Invalidate();
    }

    private static void RemapItem(ToolStripItem item, Dictionary<int, Color> map)
    {
        Color n;
        if (map.TryGetValue(item.ForeColor.ToArgb(), out n)) item.ForeColor = n;
        var menuItem = item as ToolStripMenuItem;
        if (menuItem == null) return;
        if (map.TryGetValue(menuItem.DropDown.BackColor.ToArgb(), out n)) menuItem.DropDown.BackColor = n;
        foreach (ToolStripItem child in menuItem.DropDownItems) RemapItem(child, map);
    }

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
        { "profile", new[] { "......#", "......#", "....#.#", "....#.#", "..#.#.#", "..#.#.#", "#.#.#.#" } },
        { "update", new[] { "...#...", "...#...", ".#.#.#.", "..###..", "...#...", "#.....#", "#######" } },
        { "spark", new[] { "...#...", "...#...", "..###..", "#######", "..###..", "...#...", "...#..." } },
        { "link", new[] { ".......", ".##.##.", "#..#..#", "#.....#", "#..#..#", ".##.##.", "......." } },
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
