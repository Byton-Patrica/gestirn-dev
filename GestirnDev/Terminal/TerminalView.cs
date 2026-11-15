using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace GestirnDev
{
internal sealed class TerminalProfile
{
    public string Name, CommandLine, StartupInput, Glyph = "term";
    public bool IsAgent;
}

internal sealed class TerminalSession
{
    public TerminalProfile Profile;
    public PseudoConsole Pty;
    public VtScreen Screen;
    public bool Exited;
    public readonly StringBuilder Pending = new StringBuilder();
    public string Name { get { string t = Screen.Title ?? ""; return t.Length == 0 || Profile.IsAgent || t.Contains("\\") || t.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase) ? Profile.Name : Shorten(t); } }

    private static string Shorten(string title)
    {
        title = title.Trim();
        if (title.Length > 28) title = "…" + title.Substring(title.Length - 27);
        return title;
    }
}

// Draws a VtScreen and turns keyboard / mouse / IME input into the byte sequences terminal programs expect.
internal sealed class TerminalControl : Control
{
    private Font font, bold, cjk;
    private float cw;
    private int lh, textH;
    private int scroll;                 // lines scrolled back into history (0 = live)
    private int selA = -1, selB = -1;    // absolute cell offsets (line * cols + col)
    private bool selecting;
    private bool blinkOn = true;
    private readonly Timer blink = new Timer { Interval = 530 };
    public TerminalSession Session;
    public event Action<int, int> GridResized;

    private static readonly Color[] DarkAnsi =
    {
        Color.FromArgb(30, 30, 34), Color.FromArgb(255, 99, 99), Color.FromArgb(160, 214, 74), Color.FromArgb(245, 179, 53),
        Color.FromArgb(120, 170, 255), Color.FromArgb(201, 160, 255), Color.FromArgb(94, 210, 210), Color.FromArgb(214, 210, 200),
        Color.FromArgb(110, 108, 104), Color.FromArgb(255, 140, 130), Color.FromArgb(196, 242, 58), Color.FromArgb(255, 210, 110),
        Color.FromArgb(160, 200, 255), Color.FromArgb(222, 190, 255), Color.FromArgb(140, 235, 235), Color.FromArgb(250, 248, 240),
    };
    private static readonly Color[] LightAnsi =
    {
        Color.FromArgb(40, 38, 35), Color.FromArgb(200, 40, 40), Color.FromArgb(60, 130, 0), Color.FromArgb(160, 105, 0),
        Color.FromArgb(30, 90, 200), Color.FromArgb(130, 60, 190), Color.FromArgb(0, 130, 140), Color.FromArgb(120, 116, 108),
        Color.FromArgb(100, 96, 90), Color.FromArgb(225, 70, 60), Color.FromArgb(80, 150, 0), Color.FromArgb(185, 125, 0),
        Color.FromArgb(50, 110, 220), Color.FromArgb(150, 80, 210), Color.FromArgb(0, 150, 160), Color.FromArgb(30, 28, 25),
    };

    public TerminalControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Cursor = Cursors.IBeam;
        ImeMode = ImeMode.On;
        blink.Tick += (_, _) => { blinkOn = !blinkOn; InvalidateCursor(); };
        SetFont(Theme.CodeFamily, 10.5f);
    }

    protected override bool CanEnableIme { get { return true; } }

    public void SetFont(string family, float size)
    {
        if (font != null) { font.Dispose(); bold.Dispose(); cjk.Dispose(); }
        font = new Font(family, size);
        bold = new Font(family, size, FontStyle.Bold);
        cjk = new Font(Theme.FontExists("Microsoft YaHei UI") ? "Microsoft YaHei UI" : family, size * 0.94f);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        using (var g = Graphics.FromHwnd(IntPtr.Zero))
        {
            cw = TextRenderer.MeasureText(g, new string('M', 100), font, Size.Empty, flags).Width / 100f;
            textH = TextRenderer.MeasureText(g, "Ag", font, Size.Empty, flags).Height;
        }
        lh = (int)Math.Round(textH * 1.18f);
        UpdateGrid();
    }

    private int Pad { get { return Ui.D(8); } }
    public int GridCols { get { return Math.Max(10, (int)((ClientSize.Width - Pad * 2 - Ui.D(8)) / cw)); } }
    public int GridRows { get { return Math.Max(3, (ClientSize.Height - Pad * 2) / lh); } }

    public void UpdateGrid()
    {
        if (Session == null || ClientSize.Width < 20 || ClientSize.Height < 20) { Invalidate(); return; }
        int c = GridCols, r = GridRows;
        if (c != Session.Screen.Cols || r != Session.Screen.Rows)
        {
            Session.Screen.Resize(c, r);
            if (GridResized != null) GridResized(c, r);
        }
        Invalidate();
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateGrid(); }

    public void Send(string s)
    {
        if (Session == null) return;
        scroll = 0;
        if (Session.Exited) { if (s == "\r" && Restart != null) Restart(Session); return; }
        Session.Pty.Write(s);
    }

    public event Action<TerminalSession> Restart;

    // ---------------------------------------------------------------- keyboard

    protected override bool IsInputKey(Keys keyData) { return true; }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!Focused || Session == null) return base.ProcessCmdKey(ref msg, keyData);
        Keys code = keyData & Keys.KeyCode;
        bool ctrl = (keyData & Keys.Control) != 0, shift = (keyData & Keys.Shift) != 0, alt = (keyData & Keys.Alt) != 0;

        // A few application shortcuts stay with the IDE; everything else belongs to the program.
        if (ctrl && code == Keys.Oemtilde) return base.ProcessCmdKey(ref msg, keyData);
        if (ctrl && shift && (code == Keys.P)) return base.ProcessCmdKey(ref msg, keyData);
        if (ctrl && code == Keys.Tab) return base.ProcessCmdKey(ref msg, keyData);
        if (alt && code == Keys.F4) return base.ProcessCmdKey(ref msg, keyData);

        if (ctrl && shift && code == Keys.C) { CopySelection(); return true; }
        if ((ctrl && shift && code == Keys.V) || (ctrl && !shift && code == Keys.V) || (shift && code == Keys.Insert)) { Paste(); return true; }
        if (ctrl && !shift && code == Keys.C && HasSelection) { CopySelection(); return true; }

        string seq = Translate(code, ctrl, shift, alt);
        if (seq != null) { Send(seq); ClearSelection(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private string Translate(Keys code, bool ctrl, bool shift, bool alt)
    {
        int mod = 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (ctrl ? 4 : 0);
        string csiMod = mod > 1 ? "1;" + mod : "";
        Func<char, string> arrow = c => mod > 1 ? "\x1b[" + csiMod + c : (Session.Screen.AppCursor ? "\x1bO" + c : "\x1b[" + c);
        Func<int, string> tilde = n => "\x1b[" + n + (mod > 1 ? ";" + mod : "") + "~";
        switch (code)
        {
            case Keys.Enter: return alt ? "\x1b\r" : "\r";
            case Keys.Back: return ctrl ? "\x08" : alt ? "\x1b\x7f" : "\x7f";
            case Keys.Tab: return shift ? "\x1b[Z" : "\t";
            case Keys.Escape: return "\x1b";
            case Keys.Up: return arrow('A');
            case Keys.Down: return arrow('B');
            case Keys.Right: return arrow('C');
            case Keys.Left: return arrow('D');
            case Keys.Home: return mod > 1 ? "\x1b[" + csiMod + "H" : (Session.Screen.AppCursor ? "\x1bOH" : "\x1b[H");
            case Keys.End: return mod > 1 ? "\x1b[" + csiMod + "F" : (Session.Screen.AppCursor ? "\x1bOF" : "\x1b[F");
            case Keys.Insert: return tilde(2);
            case Keys.Delete: return tilde(3);
            case Keys.PageUp:
                if (shift) { ScrollBy(GridRows - 1); return ""; }
                return tilde(5);
            case Keys.PageDown:
                if (shift) { ScrollBy(-(GridRows - 1)); return ""; }
                return tilde(6);
            case Keys.F1: return mod > 1 ? "\x1b[" + csiMod + "P" : "\x1bOP";
            case Keys.F2: return mod > 1 ? "\x1b[" + csiMod + "Q" : "\x1bOQ";
            case Keys.F3: return mod > 1 ? "\x1b[" + csiMod + "R" : "\x1bOR";
            case Keys.F4: return mod > 1 ? "\x1b[" + csiMod + "S" : "\x1bOS";
            case Keys.F5: return tilde(15);
            case Keys.F6: return tilde(17);
            case Keys.F7: return tilde(18);
            case Keys.F8: return tilde(19);
            case Keys.F9: return tilde(20);
            case Keys.F10: return tilde(21);
            case Keys.F11: return tilde(23);
            case Keys.F12: return tilde(24);
        }
        if (ctrl && !alt)
        {
            if (code >= Keys.A && code <= Keys.Z) return ((char)(code - Keys.A + 1)).ToString();
            switch (code)
            {
                case Keys.Space: case Keys.D2: return "\0";
                case Keys.OemOpenBrackets: return "\x1b";
                case Keys.OemPipe: case Keys.OemBackslash: return "\x1c";
                case Keys.OemCloseBrackets: return "\x1d";
                case Keys.D6: return "\x1e";
                case Keys.OemMinus: return "\x1f";
            }
        }
        if (alt && !ctrl && code >= Keys.A && code <= Keys.Z)
            return "\x1b" + (char)((shift ? 'A' : 'a') + (code - Keys.A));
        return null;
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (e.KeyChar >= 32 && e.KeyChar != 127) { Send(e.KeyChar.ToString()); ClearSelection(); }
        e.Handled = true;
    }

    private const int WM_IME_STARTCOMPOSITION = 0x010D, WM_IME_COMPOSITION = 0x010F;

    protected override void WndProc(ref Message m)
    {
        if ((m.Msg == WM_IME_STARTCOMPOSITION || m.Msg == WM_IME_COMPOSITION) && Session != null)
        {
            IntPtr imc = Native.ImmGetContext(Handle);
            if (imc != IntPtr.Zero)
            {
                var p = CellPoint(Session.Screen.CursorX, Session.Screen.CursorY);
                var form = new Native.CompositionForm { Style = 0x0002, Position = p, Area = Rectangle.Empty };
                Native.ImmSetCompositionWindow(imc, ref form);
                var lf = new Native.LogFont();
                font.ToLogFont(lf);
                Native.ImmSetCompositionFontW(imc, lf);
                Native.ImmReleaseContext(Handle, imc);
            }
        }
        base.WndProc(ref m);
    }

    private Point CellPoint(int x, int y) { return new Point((int)(Pad + x * cw), Pad + y * lh); }

    // ---------------------------------------------------------------- clipboard & selection

    private bool HasSelection { get { return selA >= 0 && selB >= 0 && selA != selB; } }
    private void ClearSelection() { if (selA >= 0) { selA = selB = -1; Invalidate(); } }

    private TermCell[] LineAt(int absolute)
    {
        var s = Session.Screen;
        if (absolute < s.Scrollback.Count) return absolute >= 0 ? s.Scrollback[absolute] : null;
        int y = absolute - s.Scrollback.Count;
        return y >= 0 && y < s.Rows ? s.Line(y) : null;
    }

    private void CopySelection()
    {
        if (!HasSelection || Session == null) return;
        int cols = Session.Screen.Cols;
        int a = Math.Min(selA, selB), b = Math.Max(selA, selB);
        var sb = new StringBuilder();
        for (int line = a / cols; line <= b / cols; line++)
        {
            var cells = LineAt(line);
            if (cells == null) continue;
            int from = line == a / cols ? a % cols : 0, to = line == b / cols ? b % cols : cols;
            var text = new StringBuilder();
            for (int x = from; x < to && x < cells.Length; x++)
            {
                if ((cells[x].Attr & VtScreen.WideTail) != 0) continue;
                text.Append(cells[x].Ch == '\0' ? ' ' : cells[x].Ch);
                if (cells[x].Low != '\0') text.Append(cells[x].Low);
            }
            sb.Append(text.ToString().TrimEnd());
            if (line != b / cols) sb.Append("\r\n");
        }
        try { Clipboard.SetDataObject(sb.ToString(), true, 5, 60); }
        catch (ExternalException) { }
        ClearSelection();
    }

    public void Paste()
    {
        string text = null;
        try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); }
        catch (ExternalException) { }
        if (string.IsNullOrEmpty(text) || Session == null) return;
        text = text.Replace("\r\n", "\r").Replace('\n', '\r');
        Send(Session.Screen.BracketedPaste ? "\x1b[200~" + text + "\x1b[201~" : text);
    }

    private int CellAt(Point p)
    {
        var s = Session.Screen;
        int x = Math.Max(0, Math.Min(s.Cols, (int)Math.Round((p.X - Pad) / cw)));
        int y = Math.Max(0, Math.Min(s.Rows - 1, (p.Y - Pad) / lh));
        int absolute = s.Scrollback.Count - scroll + y;
        return absolute * s.Cols + x;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (Session == null) return;
        if (e.Button == MouseButtons.Right)
        {
            if (HasSelection) CopySelection(); else Paste();
            return;
        }
        selA = selB = CellAt(e.Location);
        selecting = true;
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!selecting || Session == null) return;
        if (e.Y < 0) ScrollBy(1);
        else if (e.Y > Height) ScrollBy(-1);
        selB = CellAt(e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        selecting = false;
        Capture = false;
        if (selA == selB) ClearSelection();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Session == null) return;
        int lines = Math.Sign(e.Delta) * 3;
        if (Session.Screen.AltScreen) { for (int i = 0; i < Math.Abs(lines); i++) Session.Pty.Write(lines > 0 ? "\x1b[A" : "\x1b[B"); return; }
        ScrollBy(lines);
    }

    private void ScrollBy(int lines)
    {
        if (Session == null) return;
        scroll = Math.Max(0, Math.Min(Session.Screen.Scrollback.Count, scroll + lines));
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        blinkOn = true;
        blink.Start();
        if (Session != null && Session.Screen.FocusReporting && !Session.Exited) Session.Pty.Write("\x1b[I");
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        blink.Stop();
        if (Session != null && Session.Screen.FocusReporting && !Session.Exited) Session.Pty.Write("\x1b[O");
        Invalidate();
    }

    private void InvalidateCursor()
    {
        if (Session == null) return;
        var p = CellPoint(Session.Screen.CursorX, Session.Screen.CursorY);
        Invalidate(new Rectangle(p.X - 2, p.Y, (int)(cw * 2) + 4, lh));
    }

    public void ResetBlink() { blinkOn = true; }

    // ---------------------------------------------------------------- painting

    private Color DefaultFg { get { return Theme.IsLight ? Color.FromArgb(30, 28, 25) : Color.FromArgb(225, 221, 211); } }
    private Color DefaultBg { get { return Theme.TerminalBg; } }

    private Color ColorOf(int c, bool foreground, bool boldText)
    {
        if (c == -1) return foreground ? DefaultFg : DefaultBg;
        if ((c & 0x1000000) != 0) return Color.FromArgb((c >> 16) & 255, (c >> 8) & 255, c & 255);
        var ansi = Theme.IsLight ? LightAnsi : DarkAnsi;
        if (c < 16) return ansi[boldText && foreground && c < 8 ? c + 8 : c];
        if (c < 232)
        {
            int i = c - 16;
            Func<int, int> lvl = v => v == 0 ? 0 : 55 + v * 40;
            return Color.FromArgb(lvl(i / 36), lvl(i / 6 % 6), lvl(i % 6));
        }
        int g = 8 + (c - 232) * 10;
        return Color.FromArgb(g, g, g);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(DefaultBg);
        if (Session == null)
        {
            Shapes.Text(g, L.T("Starting terminal…", "正在启动终端…"), Theme.Ui9, ClientRectangle, Theme.Muted, Shapes.Center);
            return;
        }
        var s = Session.Screen;
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoClipping;
        int cols = s.Cols;
        int selLo = HasSelection ? Math.Min(selA, selB) : -1, selHi = HasSelection ? Math.Max(selA, selB) : -1;
        var run = new StringBuilder();
        for (int y = 0; y < s.Rows; y++)
        {
            int absolute = s.Scrollback.Count - scroll + y;
            var line = LineAt(absolute);
            if (line == null) continue;
            int py = Pad + y * lh;
            int ty = py + (lh - textH) / 2;
            int runStart = 0;
            Color runFg = Color.Empty;
            Font runFont = font;
            Action flush = () =>
            {
                if (run.Length == 0) return;
                TextRenderer.DrawText(g, run.ToString(), runFont, new Point((int)Math.Round(Pad + runStart * cw), ty), runFg, flags);
                run.Clear();
            };
            for (int x = 0; x < line.Length && x < cols; x++)
            {
                var cell = line[x];
                if ((cell.Attr & VtScreen.WideTail) != 0) continue;
                bool inverse = (cell.Attr & VtScreen.Inverse) != 0;
                bool isBold = (cell.Attr & VtScreen.Bold) != 0;
                Color fgc = ColorOf(inverse ? cell.Bg : cell.Fg, !inverse, isBold);
                Color bgc = ColorOf(inverse ? cell.Fg : cell.Bg, inverse, false);
                if (inverse && cell.Bg == -1) fgc = DefaultBg;
                if (inverse && cell.Fg == -1) bgc = DefaultFg;
                if ((cell.Attr & VtScreen.Dim) != 0) fgc = Ui.Lerp(fgc, bgc, 0.45f);
                int width = (cell.Attr & VtScreen.Wide) != 0 ? 2 : 1;
                int offset = absolute * cols + x;
                bool selected = selLo >= 0 && offset >= selLo && offset < selHi;
                if (selected) bgc = Theme.Selection.A > 0 ? Ui.Lerp(bgc, Color.FromArgb(Theme.Selection.R, Theme.Selection.G, Theme.Selection.B), 0.45f) : bgc;
                if (bgc != DefaultBg)
                    using (var b = new SolidBrush(bgc)) g.FillRectangle(b, Pad + x * cw, py, cw * width + 0.5f, lh);
                char ch = cell.Ch;
                bool blank = ch == ' ' || ch == '\0';
                if ((cell.Attr & VtScreen.Underline) != 0)
                    using (var pen = new Pen(fgc, Math.Max(1, Ui.Scale))) g.DrawLine(pen, Pad + x * cw, py + lh - Ui.D(2), Pad + (x + width) * cw, py + lh - Ui.D(2));
                if (width == 2 || ch >= 0x80)
                {
                    flush();
                    if (!blank)
                    {
                        string glyph = cell.Low != '\0' ? new string(new[] { ch, cell.Low }) : ch.ToString();
                        var f = width == 2 ? cjk : (isBold ? bold : font);
                        var gs = TextRenderer.MeasureText(g, glyph, f, Size.Empty, flags);
                        float gx = Pad + x * cw + (width * cw - gs.Width) / 2f;
                        TextRenderer.DrawText(g, glyph, f, new Point((int)Math.Round(gx), ty + (textH - gs.Height) / 2), fgc, flags);
                    }
                    continue;
                }
                var cellFont = isBold ? bold : font;
                if (run.Length > 0 && (fgc != runFg || cellFont != runFont)) flush();
                if (run.Length == 0) { runStart = x; runFg = fgc; runFont = cellFont; }
                run.Append(blank ? ' ' : ch);
            }
            flush();
        }
        if (scroll == 0 && s.CursorVisible && !Session.Exited)
        {
            var p = CellPoint(s.CursorX, s.CursorY);
            if (Focused)
            {
                if (blinkOn) using (var b = new SolidBrush(Ui.Alpha(Theme.Orange, 200))) g.FillRectangle(b, p.X, p.Y, Math.Max(2f, cw), lh);
            }
            else using (var pen = new Pen(Ui.Alpha(Theme.Orange, 160), Math.Max(1, Ui.Scale))) g.DrawRectangle(pen, p.X, p.Y, Math.Max(2f, cw) - 1, lh - 1);
        }
        if (scroll > 0)
        {
            string hint = L.T("Scrolled back " + scroll + " lines · type to return", "已回滚 " + scroll + " 行 · 输入即返回底部");
            var size = TextRenderer.MeasureText(hint, Theme.UiSmall);
            var r = new RectangleF(Width - size.Width - Ui.D(28), Ui.D(6), size.Width + Ui.D(16), Ui.D(22));
            Shapes.FillRound(g, Theme.Raised, r, Ui.D(6));
            Shapes.Text(g, hint, Theme.UiSmall, Rectangle.Round(r), Theme.TextDim, Shapes.Center);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { blink.Dispose(); font.Dispose(); bold.Dispose(); cjk.Dispose(); }
        base.Dispose(disposing);
    }
}

// The terminal panel: session tabs, a launcher for shells and AI agents, and the terminal itself.
internal sealed class TerminalView : Panel
{
    private readonly List<TerminalSession> sessions = new List<TerminalSession>();
    private TerminalSession active;
    private readonly TerminalControl term = new TerminalControl();
    private readonly SessionTabs tabsBar = new SessionTabs();
    private readonly PixelButton add = new PixelButton { Glyph = "plus", Kind = ButtonKind.Ghost, Radius = 7 };
    private readonly PixelButton launcher = new PixelButton { Glyph = "chevron-down", Kind = ButtonKind.Ghost, Radius = 7 };
    private readonly PixelButton move = new PixelButton { Glyph = "sidebar", Kind = ButtonKind.Ghost, Radius = 7 };
    private readonly PixelButton kill = new PixelButton { Glyph = "trash", Kind = ButtonKind.Ghost, Radius = 7 };
    private readonly ToolTip tip = new ToolTip();
    private readonly Timer flushTimer = new Timer { Interval = 15 };
    private List<TerminalProfile> profiles;
    private bool inSidebar;

    public Func<string> StartFolder = () => null;
    public Func<IDictionary<string, string>> ExtraEnvironment = () => null;
    public string DefaultShell = "auto";
    public event Action<bool> MoveRequested;

    public TerminalView()
    {
        BackColor = Theme.TerminalBg;
        tabsBar.Owner = this;
        tip.SetToolTip(add, L.T("New terminal", "新建终端"));
        tip.SetToolTip(launcher, L.T("Shells and AI agents", "选择 Shell 或 AI 智能体"));
        tip.SetToolTip(kill, L.T("Close this terminal", "关闭当前终端"));
        add.Click += (_, _) => NewSession(DefaultProfile());
        launcher.Click += (_, _) => ShowLauncher();
        move.Click += (_, _) => { if (MoveRequested != null) MoveRequested(!inSidebar); };
        kill.Click += (_, _) => { if (active != null) CloseSession(active); };
        term.GridResized += (c, r) => { if (active != null && !active.Exited) active.Pty.Resize(c, r); };
        term.Restart += s => { int i = sessions.IndexOf(s); CloseSession(s, false); NewSession(s.Profile, i); };
        flushTimer.Tick += (_, _) => Flush();
        Controls.Add(term);
        Controls.Add(tabsBar);
        Controls.Add(add);
        Controls.Add(launcher);
        Controls.Add(move);
        Controls.Add(kill);
        InSidebar = false;
    }

    public bool InSidebar
    {
        get { return inSidebar; }
        set
        {
            inSidebar = value;
            move.Glyph = value ? "panel" : "sidebar";
            tip.SetToolTip(move, value ? L.T("Move to bottom panel", "移到底部面板") : L.T("Move to sidebar", "移到侧边栏"));
            move.Invalidate();
        }
    }

    public IList<TerminalSession> Sessions { get { return sessions; } }
    public TerminalSession Active { get { return active; } }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int bar = Ui.D(34), b = Ui.D(26);
        int x = Width - Ui.D(6);
        foreach (var btn in new[] { kill, move, launcher, add })
        {
            x -= b;
            btn.SetBounds(x, (bar - b) / 2 + Ui.D(2), b, b);
            x -= Ui.D(2);
        }
        tabsBar.SetBounds(Ui.D(4), Ui.D(2), Math.Max(10, x - Ui.D(8)), bar);
        term.SetBounds(Ui.D(2), bar + Ui.D(4), Math.Max(10, Width - Ui.D(4)), Math.Max(10, Height - bar - Ui.D(6)));
    }

    public void ApplyTheme()
    {
        BackColor = Theme.TerminalBg;
        tabsBar.Invalidate();
        term.Invalidate();
    }

    public void SetFontSize(float size) { term.SetFont(Theme.CodeFamily, size); }

    // ---------------------------------------------------------------- profiles

    private static string FindOnPath(params string[] names)
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Select(d => d.Trim().Trim('"')).Where(d => d.Length > 0).ToList();
        dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
        dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"));
        foreach (var dir in dirs)
            foreach (var n in names)
                foreach (var ext in new[] { ".exe", ".cmd", ".bat", "" })
                {
                    try
                    {
                        string p = Path.Combine(dir, n + ext);
                        if (File.Exists(p)) return p;
                    }
                    catch (ArgumentException) { }
                }
        return null;
    }

    public List<TerminalProfile> Profiles()
    {
        if (profiles != null) return profiles;
        var list = new List<TerminalProfile>();
        string pwsh = FindOnPath("pwsh") ?? (File.Exists(@"C:\Program Files\PowerShell\7\pwsh.exe") ? @"C:\Program Files\PowerShell\7\pwsh.exe" : null);
        if (pwsh != null) list.Add(new TerminalProfile { Name = "PowerShell 7", CommandLine = "\"" + pwsh + "\" -NoLogo" });
        list.Add(new TerminalProfile { Name = "Windows PowerShell", CommandLine = "powershell.exe -NoLogo" });
        list.Add(new TerminalProfile { Name = L.T("Command Prompt", "命令提示符"), CommandLine = "cmd.exe" });
        string bash = new[] { @"C:\Program Files\Git\bin\bash.exe", @"C:\Program Files (x86)\Git\bin\bash.exe" }.FirstOrDefault(File.Exists);
        if (bash != null) list.Add(new TerminalProfile { Name = "Git Bash", CommandLine = "\"" + bash + "\" --login -i" });
        var agents = new[]
        {
            new[] { "Claude Code", "claude" }, new[] { "Codex CLI", "codex" }, new[] { "Gemini CLI", "gemini" },
            new[] { "Qwen Code", "qwen" }, new[] { "OpenCode", "opencode" }, new[] { "Aider", "aider" }, new[] { "Kimi CLI", "kimi" },
        };
        foreach (var a in agents)
            if (FindOnPath(a[1]) != null)
                list.Add(new TerminalProfile { Name = a[0], StartupInput = a[1] + "\r", IsAgent = true, Glyph = "palette" });
        profiles = list;
        return list;
    }

    public TerminalProfile DefaultProfile()
    {
        var all = Profiles();
        if (DefaultShell == "cmd") return all.First(p => p.CommandLine == "cmd.exe");
        if (DefaultShell == "bash") return all.FirstOrDefault(p => p.Name == "Git Bash") ?? all[0];
        if (DefaultShell == "powershell") return all.First(p => p.Name == "Windows PowerShell");
        return all[0];
    }

    private TerminalProfile ShellFor(TerminalProfile p)
    {
        return p.IsAgent || p.CommandLine == null ? DefaultProfile() : p;
    }

    private void ShowLauncher()
    {
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), Font = Theme.Ui9, BackColor = Theme.PanelHi, ShowImageMargin = false };
        foreach (var p in Profiles().Where(x => !x.IsAgent))
        {
            var profile = p;
            menu.Items.Add(new ToolStripMenuItem(p.Name, null, (_, _) => NewSession(profile)) { ForeColor = Theme.Text });
        }
        menu.Items.Add(new ToolStripSeparator());
        var agents = Profiles().Where(x => x.IsAgent).ToList();
        if (agents.Count == 0)
            menu.Items.Add(new ToolStripMenuItem(L.T("No AI agent CLI found on PATH", "未在 PATH 中找到 AI 智能体命令行")) { Enabled = false });
        foreach (var p in agents)
        {
            var profile = p;
            menu.Items.Add(new ToolStripMenuItem(L.T("AI: ", "AI：") + p.Name, null, (_, _) => NewSession(profile)) { ForeColor = Theme.Text });
        }
        menu.Items.Add(new ToolStripMenuItem(L.T("Run a command in a new terminal…", "在新终端中运行命令…"), null, (_, _) =>
        {
            string cmd = Prompt.Show(FindForm(), L.T("New terminal", "新建终端"), L.T("Command to run (e.g. claude, codex, npm run dev)", "要运行的命令（例如 claude、codex、npm run dev）"), "");
            if (!string.IsNullOrWhiteSpace(cmd)) NewSession(new TerminalProfile { Name = cmd.Trim().Split(' ')[0], StartupInput = cmd.Trim() + "\r", IsAgent = true });
        }) { ForeColor = Theme.Text });
        menu.Items.Add(new ToolStripMenuItem(L.T("Refresh list", "刷新列表"), null, (_, _) => { profiles = null; }) { ForeColor = Theme.Text });
        menu.Show(launcher, new Point(0, launcher.Height));
    }

    // ---------------------------------------------------------------- sessions

    public TerminalSession NewSession(TerminalProfile profile, int index = -1)
    {
        var shell = ShellFor(profile);
        int cols = term.GridCols, rows = term.GridRows;
        var session = new TerminalSession { Profile = profile, Screen = new VtScreen(cols, rows), Pty = new PseudoConsole() };
        session.Screen.Respond = reply => { if (!session.Exited) session.Pty.Write(reply); };
        session.Pty.Output += text => { lock (session.Pending) session.Pending.Append(text); };
        session.Pty.Exited += code =>
        {
            try { BeginInvoke(new Action(() => OnExited(session, code))); }
            catch (InvalidOperationException) { }
        };
        string folder = StartFolder();
        if (folder == null || !Directory.Exists(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "TERM", "xterm-256color" }, { "COLORTERM", "truecolor" }, { "TERM_PROGRAM", "GestirnDev" } };
        var extra = ExtraEnvironment();
        if (extra != null) foreach (var kv in extra) env[kv.Key] = kv.Value;
        try
        {
            session.Pty.Start(shell.CommandLine, folder, cols, rows, env);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is EntryPointNotFoundException)
        {
            session.Screen.Feed(L.T("Could not start the terminal: ", "无法启动终端：") + ex.Message + "\r\n");
            session.Exited = true;
        }
        if (index < 0 || index > sessions.Count) sessions.Add(session); else sessions.Insert(index, session);
        Activate(session);
        if (!string.IsNullOrEmpty(profile.StartupInput) && !session.Exited)
        {
            var once = new Timer { Interval = 700 };
            once.Tick += (_, _) => { once.Stop(); once.Dispose(); if (!session.Exited) session.Pty.Write(profile.StartupInput); };
            once.Start();
        }
        flushTimer.Start();
        return session;
    }

    public void Activate(TerminalSession session)
    {
        active = session;
        term.Session = session;
        term.UpdateGrid();
        tabsBar.Invalidate();
        if (Visible) term.Focus();
    }

    private void OnExited(TerminalSession session, int code)
    {
        Flush();
        session.Exited = true;
        session.Screen.Feed("\r\n\x1b[90m" + string.Format(L.T("[process exited with code {0} · press Enter to restart]", "[进程已退出，代码 {0} · 按 Enter 重新启动]"), code) + "\x1b[0m");
        tabsBar.Invalidate();
        term.Invalidate();
    }

    public void CloseSession(TerminalSession session, bool activateNext = true)
    {
        if (!session.Exited) session.Pty.Kill();
        session.Pty.Dispose();
        int i = sessions.IndexOf(session);
        sessions.Remove(session);
        if (active == session)
        {
            active = null;
            term.Session = null;
            if (activateNext && sessions.Count > 0) Activate(sessions[Math.Max(0, Math.Min(i, sessions.Count - 1))]);
        }
        tabsBar.Invalidate();
        term.Invalidate();
    }

    private void Flush()
    {
        bool any = false;
        foreach (var s in sessions.ToList())
        {
            string text;
            lock (s.Pending)
            {
                if (s.Pending.Length == 0) continue;
                text = s.Pending.ToString();
                s.Pending.Clear();
            }
            s.Screen.Feed(text);
            if (s == active) any = true;
        }
        if (any) { term.ResetBlink(); term.Invalidate(); tabsBar.Invalidate(); }
        if (sessions.Count == 0) flushTimer.Stop();
    }

    public void EnsureStarted() { if (sessions.Count == 0) NewSession(DefaultProfile()); }
    public void FocusInput() { EnsureStarted(); term.Focus(); }
    public void Run(string command) { EnsureStarted(); active.Pty.Write(command + "\r"); }
    public void Clear() { if (active != null) { active.Screen.Clear(); term.Invalidate(); } }
    public void Kill() { foreach (var s in sessions.ToList()) CloseSession(s, false); }
    public string ScreenText()
    {
        if (active == null) return "";
        var sb = new StringBuilder();
        foreach (var line in active.Screen.Scrollback.Concat(Enumerable.Range(0, active.Screen.Rows).Select(active.Screen.Line)))
            sb.Append(new string(line.Select(c => c.Ch == '\0' ? ' ' : c.Ch).ToArray()).TrimEnd()).Append('\n');
        return sb.ToString();
    }

    protected override void Dispose(bool disposing) { if (disposing) { Kill(); flushTimer.Dispose(); } base.Dispose(disposing); }

    private sealed class SessionTabs : Control
    {
        public TerminalView Owner;
        private readonly List<Rectangle> rects = new List<Rectangle>();
        private int hover = -1;

        public SessionTabs()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = rects.FindIndex(r => r.Contains(e.Location)); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = rects.FindIndex(r => r.Contains(e.Location));
            if (i < 0 || i >= Owner.sessions.Count) return;
            var s = Owner.sessions[i];
            if (e.Button == MouseButtons.Middle || e.X > rects[i].Right - Ui.D(22)) Owner.CloseSession(s);
            else Owner.Activate(s);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Owner.BackColor);
            rects.Clear();
            int x = Ui.D(2);
            for (int i = 0; i < Owner.sessions.Count; i++)
            {
                var s = Owner.sessions[i];
                string name = s.Name;
                int w = Math.Min(Ui.D(220), TextRenderer.MeasureText(name, Theme.Ui9, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.D(58));
                var r = new Rectangle(x, Ui.D(4), w, Height - Ui.D(8));
                rects.Add(r);
                bool act = s == Owner.active;
                if (act) Shapes.FillRound(g, Theme.Raised, r, Ui.D(8));
                else if (i == hover) Shapes.FillRound(g, Ui.Alpha(Theme.Hover, 140), r, Ui.D(8));
                var color = s.Exited ? Theme.Muted : s.Profile.IsAgent ? Theme.Lime : Theme.Orange;
                Glyphs.Draw(g, s.Profile.Glyph, new Rectangle(r.X + Ui.D(10), r.Y + (r.Height - Ui.D(11)) / 2, Ui.D(11), Ui.D(11)), color);
                Shapes.Text(g, name, Theme.Ui9, new Rectangle(r.X + Ui.D(28), r.Y, r.Width - Ui.D(54), r.Height), act ? Theme.Text : Theme.TextDim, Shapes.Left);
                if (act || i == hover) Glyphs.Draw(g, "close", new Rectangle(r.Right - Ui.D(18), r.Y + (r.Height - Ui.D(8)) / 2, Ui.D(8), Ui.D(8)), Theme.Muted);
                x += w + Ui.D(4);
            }
            if (Owner.sessions.Count == 0)
                Shapes.Text(g, L.T("TERMINAL", "终端"), Theme.UiSmall, new Rectangle(Ui.D(10), 0, Width, Height), Theme.Muted, Shapes.Left);
        }
    }
}
}
