using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GestirnDev
{
// Find / replace bar that slides down over the top-right of the editor.
internal sealed class FindBar : Card
{
    public readonly RoundInput Find = new RoundInput(L.T("Find", "查找"));
    public readonly RoundInput Replace = new RoundInput(L.T("Replace", "替换"));
    private readonly PixelButton expand = new PixelButton { Glyph = "chevron-right", Kind = ButtonKind.Ghost, Radius = 6 };
    private readonly PixelButton caseBtn = new PixelButton { Glyph = "case", Kind = ButtonKind.Ghost, Radius = 6 };
    private readonly PixelButton wordBtn = new PixelButton { Glyph = "word", Kind = ButtonKind.Ghost, Radius = 6 };
    private readonly PixelButton prev = new PixelButton { Glyph = "up", Kind = ButtonKind.Ghost, Radius = 6 };
    private readonly PixelButton next = new PixelButton { Glyph = "down", Kind = ButtonKind.Ghost, Radius = 6 };
    private readonly PixelButton close = new PixelButton { Glyph = "close", Kind = ButtonKind.Ghost, Radius = 6 };
    private readonly PixelButton replaceOne = new PixelButton { Glyph = "replace", Kind = ButtonKind.Ghost, Radius = 6 };
    private readonly PixelButton replaceAll = new PixelButton { Glyph = "replaceall", Kind = ButtonKind.Ghost, Radius = 6 };
    public readonly AnimatedFloat Slide = new AnimatedFloat(0f);
    private bool replaceMode;
    private string status = "";
    private Color statusColor = Theme.Muted;
    public CodeEditor Target;
    public event Action Closed;
    public event Action LayoutNeeded;

    public FindBar()
    {
        Fill = Theme.PanelHi;
        Radius = 10;
        Visible = false;
        var tip = new ToolTip();
        tip.SetToolTip(expand, L.T("Toggle replace", "切换替换"));
        tip.SetToolTip(caseBtn, L.T("Match case", "区分大小写"));
        tip.SetToolTip(wordBtn, L.T("Whole word", "全字匹配"));
        tip.SetToolTip(prev, L.T("Previous (Shift+Enter)", "上一个 (Shift+Enter)"));
        tip.SetToolTip(next, L.T("Next (Enter)", "下一个 (Enter)"));
        tip.SetToolTip(close, L.T("Close (Esc)", "关闭 (Esc)"));
        tip.SetToolTip(replaceOne, L.T("Replace", "替换"));
        tip.SetToolTip(replaceAll, L.T("Replace all", "全部替换"));
        foreach (Control c in new Control[] { Find, Replace, expand, caseBtn, wordBtn, prev, next, close, replaceOne, replaceAll }) Controls.Add(c);
        Find.Fill = Replace.Fill = Theme.Raised;
        Find.Box.BackColor = Replace.Box.BackColor = Theme.Raised;
        Find.Box.TextChanged += (_, _) => Search(true);
        Find.Box.KeyDown += OnKey;
        Replace.Box.KeyDown += OnKey;
        expand.Click += (_, _) => SetReplaceMode(!replaceMode);
        caseBtn.Click += (_, _) => { caseBtn.Active = !caseBtn.Active; caseBtn.Invalidate(); Search(true); };
        wordBtn.Click += (_, _) => { wordBtn.Active = !wordBtn.Active; wordBtn.Invalidate(); Search(true); };
        prev.Click += (_, _) => Step(false);
        next.Click += (_, _) => Step(true);
        close.Click += (_, _) => Close();
        replaceOne.Click += (_, _) => { if (Target != null) { Target.ReplaceCurrent(Replace.Text); Search(false); } };
        replaceAll.Click += (_, _) =>
        {
            if (Target == null) return;
            int n = Target.ReplaceAll(Replace.Text);
            status = string.Format(L.T("{0} replaced", "已替换 {0} 处"), n);
            statusColor = Theme.Lime;
            Invalidate();
        };
        SetReplaceMode(false);
    }

    public bool ReplaceMode { get { return replaceMode; } }

    private void SetReplaceMode(bool on)
    {
        replaceMode = on;
        expand.Glyph = on ? "chevron-down" : "chevron-right";
        Replace.Visible = replaceOne.Visible = replaceAll.Visible = on;
        Size = new Size(Ui.D(470), on ? Ui.D(82) : Ui.D(44));
        PerformLayout();
        expand.Invalidate();
        if (LayoutNeeded != null) LayoutNeeded();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int b = Ui.D(26), pad = Ui.D(8), y1 = Ui.D(9);
        expand.SetBounds(pad, y1, Ui.D(20), b);
        int right = Width - pad;
        foreach (var btn in new[] { close, next, prev, wordBtn, caseBtn })
        {
            right -= b;
            btn.SetBounds(right, y1, b, b);
            right -= Ui.D(2);
        }
        int countW = Ui.D(64);
        Find.SetBounds(pad + Ui.D(24), Ui.D(7), right - countW - pad - Ui.D(24), Ui.D(30));
        if (replaceMode)
        {
            int y2 = Ui.D(45);
            Replace.SetBounds(pad + Ui.D(24), y2, Find.Width, Ui.D(30));
            replaceOne.SetBounds(Replace.Right + Ui.D(6), y2 + Ui.D(2), b, b);
            replaceAll.SetBounds(replaceOne.Right + Ui.D(2), y2 + Ui.D(2), b, b);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var r = new Rectangle(Find.Right + Ui.D(4), Ui.D(7), Ui.D(64), Ui.D(30));
        Shapes.Text(e.Graphics, status, Theme.UiSmall, r, statusColor, Shapes.Center);
    }

    public void Open(bool replace, string initial)
    {
        SetReplaceMode(replace);
        if (!string.IsNullOrEmpty(initial) && initial.IndexOf('\n') < 0) Find.Text = initial;
        if (!Visible)
        {
            Visible = true;
            Slide.Snap(0f);
            Slide.To(1f, 220, Ease.OutBack);
        }
        BringToFront();
        Find.Box.Focus();
        Find.Box.SelectAll();
        Search(false);
    }

    public void Close()
    {
        if (!Visible) return;
        if (Target != null) { Target.SetSearch("", false, false); Target.Focus(); }
        Slide.To(0f, 160);
        Slide.Finished += Hide1;
        if (Closed != null) Closed();
    }

    private void Hide1()
    {
        Slide.Finished -= Hide1;
        if (Slide.Value <= 0.01f) Visible = false;
    }

    public void Retarget(CodeEditor editor)
    {
        if (Target != null && Target != editor) Target.SetSearch("", false, false);
        Target = editor;
        if (Visible) Search(false);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            if (sender == Replace.Box && Target != null) { Target.ReplaceCurrent(Replace.Text); Search(false); }
            else Step(!e.Shift);
        }
        else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
        else if (e.KeyCode == Keys.F3) { e.SuppressKeyPress = true; Step(!e.Shift); }
    }

    public void Step(bool forward)
    {
        if (Target == null) return;
        Target.SetSearch(Find.Text, caseBtn.Active, wordBtn.Active);
        Target.FindNext(forward);
        UpdateCount();
    }

    private void Search(bool seek)
    {
        if (Target == null) return;
        Target.SetSearch(Find.Text, caseBtn.Active, wordBtn.Active);
        if (seek && Find.Text.Length > 0)
        {
            var start = Target.SelStart;
            Target.Select(start, start);
            Target.FindNext(true);
        }
        UpdateCount();
    }

    private void UpdateCount()
    {
        if (Target == null || Find.Text.Length == 0) { status = ""; Invalidate(); return; }
        int cur;
        int n = Target.CountMatches(out cur);
        status = n == 0 ? L.T("No results", "无结果") : (cur > 0 ? cur + "/" + n : n.ToString());
        statusColor = n == 0 ? Theme.Red : Theme.TextDim;
        Invalidate();
    }
}

// Command palette / quick open: one input, many modes (">" commands, ":" line, "@" symbol, files).
internal sealed class Palette : Card
{
    public sealed class Entry
    {
        public string Title, Detail, Glyph, Shortcut, Alt;
        public Action Run;
        public int Score;
        public List<int> Matches = new List<int>();
    }

    private readonly RoundInput input = new RoundInput("");
    private readonly VirtualList list = new VirtualList();
    private List<Entry> entries = new List<Entry>();
    public readonly AnimatedFloat Appear = new AnimatedFloat(0f);
    public Func<string, List<Entry>> Provider;
    public event Action Closed;

    public Palette()
    {
        Fill = Theme.PanelHi;
        Radius = 12;
        Visible = false;
        input.Fill = Theme.Raised;
        input.Box.BackColor = Theme.Raised;
        input.Box.Font = Theme.Ui10;
        list.BackColor = Theme.PanelHi;
        list.RowHeight = Ui.D(32);
        list.DrawRow = DrawRow;
        list.RowClicked += (i, e) => Execute(i);
        list.RowActivated += Execute;
        input.Box.TextChanged += (_, _) => Refresh2();
        input.Box.KeyDown += OnKey;
        input.Box.LostFocus += (_, _) => BeginInvoke(new Action(() => { if (!ContainsFocus) Close(); }));
        Controls.Add(input);
        Controls.Add(list);
    }

    public void Open(string prefix, string placeholder)
    {
        Native.CueBanner(input.Box, placeholder);
        input.Text = prefix;
        Visible = true;
        Appear.Snap(0f);
        Appear.To(1f, 200, Ease.OutCubic);
        BringToFront();
        input.Box.Focus();
        input.Box.SelectionStart = input.Text.Length;
        Refresh2();
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false;
        if (Closed != null) Closed();
    }

    private void Refresh2()
    {
        entries = Provider != null ? Provider(input.Text) : new List<Entry>();
        list.Reset(entries.Count, false);
        list.SelectedIndex = entries.Count > 0 ? 0 : -1;
        int rows = Math.Min(10, Math.Max(1, entries.Count));
        Height = Ui.D(52) + rows * list.RowHeight + Ui.D(8);
        PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        input.SetBounds(Ui.D(10), Ui.D(10), Width - Ui.D(20), Ui.D(34));
        list.SetBounds(Ui.D(4), Ui.D(50), Width - Ui.D(8), Math.Max(0, Height - Ui.D(56)));
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down: list.MoveSelection(1); e.SuppressKeyPress = true; break;
            case Keys.Up: list.MoveSelection(-1); e.SuppressKeyPress = true; break;
            case Keys.PageDown: list.MoveSelection(8); e.SuppressKeyPress = true; break;
            case Keys.PageUp: list.MoveSelection(-8); e.SuppressKeyPress = true; break;
            case Keys.Enter: e.SuppressKeyPress = true; Execute(list.SelectedIndex); break;
            case Keys.Escape: e.SuppressKeyPress = true; Close(); break;
        }
    }

    private void Execute(int i)
    {
        if (i < 0 || i >= entries.Count) return;
        var entry = entries[i];
        Close();
        if (entry.Run != null) entry.Run();
    }

    private void DrawRow(Graphics g, int i, Rectangle r, bool hover, bool selected)
    {
        var e = entries[i];
        VirtualList.RowBackground(g, r, hover, selected, true);
        int x = r.X + Ui.D(18);
        if (e.Glyph != null)
        {
            Glyphs.Draw(g, e.Glyph, new Rectangle(x, r.Y + (r.Height - Ui.D(12)) / 2, Ui.D(12), Ui.D(12)), selected ? Theme.Orange : Theme.TextDim);
            x += Ui.D(22);
        }
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        int tx = x;
        string title = e.Title ?? "";
        int k = 0;
        while (k < title.Length)
        {
            bool m = e.Matches.Contains(k);
            int j = k + 1;
            while (j < title.Length && e.Matches.Contains(j) == m) j++;
            string part = title.Substring(k, j - k);
            TextRenderer.DrawText(g, part, Theme.Ui10, new Rectangle(tx, r.Y, r.Right - tx, r.Height), m ? Theme.OrangeHot : Theme.Text, flags);
            tx += TextRenderer.MeasureText(g, part, Theme.Ui10, Size.Empty, flags).Width;
            k = j;
        }
        int sw = 0;
        if (!string.IsNullOrEmpty(e.Shortcut))
        {
            sw = TextRenderer.MeasureText(e.Shortcut, Theme.UiSmall, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.D(14);
            var chip = new RectangleF(r.Right - sw - Ui.D(12), r.Y + Ui.D(7), sw, r.Height - Ui.D(14));
            Shapes.FillRound(g, Theme.Raised, chip, Ui.D(5));
            Shapes.Text(g, e.Shortcut, Theme.UiSmall, Rectangle.Round(chip), Theme.TextDim, Shapes.Center);
        }
        if (!string.IsNullOrEmpty(e.Detail))
            Shapes.Text(g, e.Detail, Theme.UiSmall, new Rectangle(tx + Ui.D(12), r.Y, r.Right - tx - sw - Ui.D(36), r.Height), Theme.Muted, Shapes.Left | TextFormatFlags.PathEllipsis);
    }

    public static List<Entry> Rank(IEnumerable<Entry> source, string query, int max = 60)
    {
        var result = new List<Entry>();
        foreach (var e in source)
        {
            List<int> matches;
            int score = CompletionProvider.Score(e.Title, query, out matches);
            if (score < 0 && query.Length > 0)
            {
                int idx = e.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    matches = Enumerable.Range(idx, query.Length).ToList();
                    score = 300 - idx;
                }
                else if (e.Alt != null && e.Alt.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Chinese UI: English names still match ("comp" finds 编译).
                    matches = new List<int>();
                    score = 250 - e.Alt.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                }
                else continue;
            }
            e.Score = score;
            e.Matches = matches;
            result.Add(e);
        }
        return result.OrderByDescending(x => x.Score).Take(max).ToList();
    }
}
}
