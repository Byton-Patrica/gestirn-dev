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

// Two variants. Dark is the original ink-black and orange palette. Light is the 70s look taken from the
// GestirnEra 70s icon: warm cream paper, dark brown ink, red-orange accents and a six-colour rainbow stripe.
// Apply() swaps them at runtime.
internal static class Theme
{
    public static bool IsLight;
    public static Color Bg, Panel, PanelHi, Raised, Hover, Edge, EdgeSoft;
    public static Color Text, TextDim, Muted, Faint;
    public static Color Orange, OrangeHot, OrangeDeep, Gold, Lime, Red, Blue, Violet, Ink, Outline, Frame;
    // The icon's rainbow, top to bottom: red, orange, yellow, green, blue, purple.
    public static readonly Color[] Rainbow =
    {
        Color.FromArgb(224, 56, 40), Color.FromArgb(232, 112, 32), Color.FromArgb(242, 188, 48),
        Color.FromArgb(108, 160, 64), Color.FromArgb(70, 112, 184), Color.FromArgb(126, 84, 150),
    };
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
            Outline = C(52, 52, 60); Frame = C(31, 31, 37);
        }
        else
        {
            Bg = C(226, 214, 184); Panel = C(244, 236, 214); PanelHi = C(251, 247, 233); Raised = C(236, 226, 200); Hover = C(224, 211, 178);
            Edge = C(160, 134, 100); EdgeSoft = C(207, 192, 160);
            Text = C(44, 28, 18); TextDim = C(96, 74, 58); Muted = C(140, 118, 96); Faint = C(184, 164, 136);
            Orange = C(226, 70, 34); OrangeHot = C(240, 96, 40); OrangeDeep = C(176, 48, 20); Gold = C(198, 134, 10);
            Lime = C(88, 134, 40); Red = C(208, 46, 38); Blue = C(54, 98, 168); Violet = C(120, 76, 140); Ink = C(252, 246, 230);
            Outline = C(70, 48, 32); Frame = C(172, 146, 112);
            EditorBg = C(251, 246, 232); CurrentLine = C(243, 233, 205); Selection = Color.FromArgb(70, 232, 88, 32);
            SelectionInactive = Color.FromArgb(60, 140, 118, 96); GutterText = C(176, 156, 128); FindMatch = Color.FromArgb(110, 242, 190, 40);
            BracketMatch = Color.FromArgb(150, 110, 160, 60); TerminalBg = C(250, 244, 228);
            SynPlain = C(48, 32, 22); SynKeyword = C(208, 56, 28); SynType = C(178, 92, 12); SynString = C(84, 128, 38);
            SynNumber = C(190, 120, 0); SynComment = C(150, 132, 108); SynPreproc = C(124, 72, 140); SynFunction = C(48, 92, 160);
            SynOperator = C(110, 88, 70);
        }
        if (Ui9 != null) ApplyFonts();
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
        CodeFamily = PickCodeFont(preferredCode);
        Code = new Font(CodeFamily, codeSize);
        CodeSmall = new Font(CodeFamily, 9f);
        ApplyFonts();
    }

    // Titles are set in a bold serif in the 70s theme (like the icon's G) and in the UI face in the dark one.
    private static void ApplyFonts()
    {
        string ui = FontExists("Segoe UI Semibold") ? "Segoe UI Semibold" : "Segoe UI";
        if (IsLight && FontExists("Georgia"))
        {
            UiTitle = new Font("Georgia", 12f, FontStyle.Bold);
            UiHero = new Font("Georgia", 22f, FontStyle.Bold);
        }
        else
        {
            UiTitle = new Font(ui, 12f);
            UiHero = new Font(ui, 22f);
        }
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

    // A rounded rectangle where only the chosen sides are rounded (two of these make a split button).
    public static GraphicsPath RoundSides(RectangleF r, float radius, bool left, bool right)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f) * 2f;
        if (d <= 0.5f || (!left && !right)) { path.AddRectangle(r); return path; }
        if (left) path.AddArc(r.X, r.Y, d, d, 180, 90); else path.AddLine(r.X, r.Y, r.X, r.Y);
        if (right) path.AddArc(r.Right - d, r.Y, d, d, 270, 90); else path.AddLine(r.Right, r.Y, r.Right, r.Y);
        if (right) path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); else path.AddLine(r.Right, r.Bottom, r.Right, r.Bottom);
        if (left) path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); else path.AddLine(r.X, r.Bottom, r.X, r.Bottom);
        path.CloseFigure();
        return path;
    }

    // Draws an image clipped to a rounded rectangle (the 70s icon is a plain square).
    public static void DrawRounded(Graphics g, Image image, Rectangle r, float radius)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Round(r, radius)) g.SetClip(path, CombineMode.Intersect);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(image, r);
        g.Restore(state);
    }

    // The icon's six rainbow bands filling r. A shimmer (0..1) slides a pale highlight across them.
    public static void Stripes(Graphics g, Rectangle r, double shimmer = -1)
    {
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        int n = Theme.Rainbow.Length;
        for (int i = 0; i < n; i++)
        {
            int y0 = r.Y + i * r.Height / n, y1 = r.Y + (i + 1) * r.Height / n;
            using (var b = new SolidBrush(Theme.Rainbow[i])) g.FillRectangle(b, r.X, y0, r.Width, Math.Max(1, y1 - y0));
        }
        if (shimmer >= 0 && shimmer <= 1 && !Animator.Reduced)
        {
            int w = Math.Max(Ui.D(40), r.Width / 8);
            int x = r.X - w + (int)((r.Width + w * 2) * shimmer);
            var clip = g.Clip;
            g.SetClip(r);
            using (var b = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
                g.FillPolygon(b, new[] { new Point(x, r.Bottom), new Point(x + w / 2, r.Bottom), new Point(x + w, r.Y), new Point(x + w / 2, r.Y) });
            g.Clip = clip;
        }
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
        { "bolt", new[] { "....##.", "...##..", "..##...", ".#####.", "...##..", "..##...", ".##...." } },
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
        float radius = cell >= 3 ? cell * 0.34f : 0f;   // soft, rounded cells: no sharp corners anywhere
        using (var brush = new SolidBrush(color))
        {
            if (radius > 0f)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = new GraphicsPath())
                {
                    for (int y = 0; y < n; y++)
                        for (int x = 0; x < rows[y].Length; x++)
                            if (rows[y][x] == '#')
                                AddRounded(path, x0 + x * (cell + gap), y0 + y * (cell + gap), cell, cell, radius);
                    g.FillPath(brush, path);
                }
            }
            else
            {
                g.SmoothingMode = SmoothingMode.None;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < rows[y].Length; x++)
                        if (rows[y][x] == '#')
                            g.FillRectangle(brush, x0 + x * (cell + gap), y0 + y * (cell + gap), cell, cell);
            }
        }
        g.SmoothingMode = mode;
    }

    private static void AddRounded(GraphicsPath path, float x, float y, float w, float h, float r)
    {
        float d = r * 2f;
        path.StartFigure();
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
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
            using (var path = new GraphicsPath())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                AddRounded(path, x0 + Ring[i].X * (cell + gap), y0 + Ring[i].Y * (cell + gap), cell, cell, cell * 0.34f);
                g.FillPath(b, path);
            }
        }
        g.SmoothingMode = mode;
    }
}
}
