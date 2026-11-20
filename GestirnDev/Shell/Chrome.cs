using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GestirnDev
{
// A fast owner-drawn list with smooth scrolling; used by every panel.
internal sealed class VirtualList : Control
{
    private readonly AnimatedFloat scroll = new AnimatedFloat(0f);
    private readonly AnimatedFloat bar = new AnimatedFloat(0f);
    private readonly Timer barFade = new Timer { Interval = 900 };
    private int count, hover = -1, selected = -1;
    private bool thumbDrag;
    private float thumbGrab;

    public VirtualList()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        RowHeight = Ui.D(28);
        BackColor = Theme.Panel;
        scroll.Changed += Invalidate;
        bar.Changed += Invalidate;
        barFade.Tick += (_, _) => { barFade.Stop(); if (!thumbDrag) bar.To(0f, 350); };
    }

    public int RowHeight { get; set; }
    public string EmptyText { get; set; }
    public Action<Graphics, int, Rectangle, bool, bool> DrawRow;
    public Func<int, bool> IsSelectable;
    public event Action<int, MouseEventArgs> RowClicked;
    public event Action<int> RowActivated;
    public event Action<int, Point> RowRightClicked;

    public int Count { get { return count; } }
    public int SelectedIndex
    {
        get { return selected; }
        set { selected = value; EnsureVisible(value); Invalidate(); }
    }

    private float MaxScroll { get { return Math.Max(0, count * RowHeight - ClientSize.Height + Ui.D(6)); } }

    public void Reset(int newCount, bool keepScroll = true)
    {
        count = newCount;
        if (selected >= count) selected = count - 1;
        if (!keepScroll) scroll.Snap(0);
        else if (scroll.Value > MaxScroll) scroll.Snap(MaxScroll);
        hover = -1;
        Invalidate();
    }

    public void EnsureVisible(int index)
    {
        if (index < 0) return;
        float top = index * RowHeight, bottom = top + RowHeight;
        if (top < scroll.Target) scroll.To(top, 120);
        else if (bottom > scroll.Target + ClientSize.Height) scroll.To(Math.Min(MaxScroll, bottom - ClientSize.Height + Ui.D(4)), 120);
    }

    private int RowAt(int y) { int i = (int)((y + scroll.Value) / RowHeight); return i >= 0 && i < count ? i : -1; }

    protected override bool IsInputKey(Keys keyData)
    {
        var k = keyData & Keys.KeyCode;
        return k == Keys.Up || k == Keys.Down || k == Keys.Enter || k == Keys.PageUp || k == Keys.PageDown || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int step = e.KeyCode == Keys.Up ? -1 : e.KeyCode == Keys.Down ? 1 : e.KeyCode == Keys.PageUp ? -10 : e.KeyCode == Keys.PageDown ? 10 : 0;
        if (step != 0) { MoveSelection(step); e.Handled = true; }
        else if (e.KeyCode == Keys.Enter && selected >= 0) { if (RowActivated != null) RowActivated(selected); e.Handled = true; }
    }

    public void MoveSelection(int step)
    {
        if (count == 0) return;
        int i = Math.Max(0, Math.Min(count - 1, selected + step));
        int guard = 0;
        while (IsSelectable != null && !IsSelectable(i) && guard++ < count)
        {
            int next = i + Math.Sign(step == 0 ? 1 : step);
            if (next < 0 || next >= count) break;
            i = next;
        }
        SelectedIndex = i;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (thumbDrag)
        {
            float travel = ClientSize.Height - ThumbRect().Height;
            if (travel > 0) scroll.Snap(Math.Max(0, Math.Min(MaxScroll, (e.Y - thumbGrab) / travel * MaxScroll)));
            return;
        }
        int h = RowAt(e.Y);
        if (h != hover) { hover = h; Invalidate(); }
        if (e.X > Width - Ui.D(14) && MaxScroll > 0) ShowBar();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var thumb = ThumbRect();
        if (MaxScroll > 0 && e.X > Width - Ui.D(12))
        {
            if (thumb.Contains(e.Location)) { thumbDrag = true; thumbGrab = e.Y - thumb.Y; Capture = true; }
            else scroll.To(Math.Max(0, Math.Min(MaxScroll, scroll.Target + Math.Sign(e.Y - thumb.Y) * ClientSize.Height)), 160);
            return;
        }
        int i = RowAt(e.Y);
        if (i < 0) return;
        if (IsSelectable == null || IsSelectable(i)) { selected = i; Invalidate(); }
        if (e.Button == MouseButtons.Right) { if (RowRightClicked != null) RowRightClicked(i, e.Location); }
        else if (RowClicked != null) RowClicked(i, e);
    }

    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); thumbDrag = false; Capture = false; }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        int i = RowAt(e.Y);
        if (i >= 0 && RowActivated != null) RowActivated(i);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        scroll.To(Math.Max(0, Math.Min(MaxScroll, scroll.Target - e.Delta / 120f * RowHeight * 3)), 140);
        ShowBar();
    }

    private void ShowBar() { bar.To(1f, 120); barFade.Stop(); barFade.Start(); }

    private RectangleF ThumbRect()
    {
        float max = MaxScroll, view = ClientSize.Height;
        if (max <= 0) return RectangleF.Empty;
        float h = Math.Max(Ui.D(28), view * view / (view + max));
        return new RectangleF(Width - Ui.D(9), (view - h) * scroll.Value / max, Ui.D(6), h);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); if (scroll.Value > MaxScroll) scroll.Snap(MaxScroll); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (count == 0)
        {
            if (!string.IsNullOrEmpty(EmptyText))
                TextRenderer.DrawText(g, EmptyText, Theme.Ui9, new Rectangle(Ui.D(14), Ui.D(10), Width - Ui.D(28), Height - Ui.D(20)), Theme.Muted,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter);
            return;
        }
        int first = Math.Max(0, (int)(scroll.Value / RowHeight));
        int last = Math.Min(count - 1, (int)((scroll.Value + Height) / RowHeight) + 1);
        for (int i = first; i <= last; i++)
        {
            var r = new Rectangle(0, (int)Math.Round(i * RowHeight - scroll.Value), Width - Ui.D(4), RowHeight);
            if (DrawRow != null) DrawRow(g, i, r, i == hover, i == selected);
        }
        if (bar.Value > 0.01f && MaxScroll > 0) Shapes.FillRound(g, Ui.Alpha(Theme.TextDim, (int)(80 * bar.Value)), ThumbRect(), Ui.D(3));
    }

    protected override void Dispose(bool disposing) { if (disposing) barFade.Dispose(); base.Dispose(disposing); }

    // Standard row background: rounded hover / selection with the orange accent bar.
    public static void RowBackground(Graphics g, Rectangle r, bool hover, bool selected, bool focused)
    {
        var rr = new RectangleF(r.X + Ui.D(6), r.Y + Ui.D(1), r.Width - Ui.D(12), r.Height - Ui.D(2));
        if (selected)
        {
            Shapes.FillRound(g, focused ? Theme.Hover : Theme.Raised, rr, Ui.D(7));
            Shapes.FillRound(g, Theme.Orange, new RectangleF(rr.X + Ui.D(2), rr.Y + Ui.D(6), Ui.D(3), rr.Height - Ui.D(12)), Ui.D(1.5f));
        }
        else if (hover) Shapes.FillRound(g, Ui.Alpha(Theme.Hover, 150), rr, Ui.D(7));
    }
}

// Vertical icon rail on the far left. The orange indicator glides to the active view.
internal sealed class ActivityRail : Control
{
    private readonly List<Tuple<string, string, string>> items = new List<Tuple<string, string, string>>();
    private readonly AnimatedFloat indicator = new AnimatedFloat(0f);
    private readonly AnimatedFloat pop = new AnimatedFloat(1f);
    private int hover = -1, popIndex = -1;
    private string active;
    private readonly ToolTip tip = new ToolTip();

    public ActivityRail()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        indicator.Changed += Invalidate;
        pop.Changed += Invalidate;
        Cursor = Cursors.Hand;
    }

    public event Action<string> ItemClicked;
    public bool SidebarOpen = true;
    public HashSet<string> Badges = new HashSet<string>();

    public void Add(string id, string glyph, string title) { items.Add(Tuple.Create(id, glyph, title)); }

    public string Active
    {
        get { return active; }
        set
        {
            active = value;
            int i = items.FindIndex(t => t.Item1 == value);
            if (i >= 0) indicator.To(SlotY(i), 260, Ease.OutBack);
            Invalidate();
        }
    }

    private int Slot { get { return Ui.D(48); } }
    private float SlotY(int i) { return Ui.D(6) + i * Slot; }
    private bool IsBottom(int i) { return items[i].Item1 == "settings"; }

    private Rectangle SlotRect(int i)
    {
        if (IsBottom(i)) return new Rectangle(0, Height - Slot - Ui.D(6), Width, Slot);
        return new Rectangle(0, (int)SlotY(i), Width, Slot);
    }

    private int HitIndex(Point p)
    {
        for (int i = 0; i < items.Count; i++) if (SlotRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitIndex(e.Location);
        if (h != hover)
        {
            hover = h;
            tip.SetToolTip(this, h >= 0 ? items[h].Item3 : null);
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int i = HitIndex(e.Location);
        if (i >= 0 && !Animator.Reduced) { popIndex = i; pop.Snap(0f); pop.To(1f, 520, Ease.OutCubic); }
        if (i >= 0 && ItemClicked != null) ItemClicked(items[i].Item1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Bg);
        int ai = items.FindIndex(t => t.Item1 == active);
        if (ai >= 0 && SidebarOpen && !IsBottom(ai))
            Shapes.FillRound(g, Theme.Orange, new RectangleF(Ui.D(2), indicator.Value + Ui.D(12), Ui.D(3), Slot - Ui.D(24)), Ui.D(2));
        for (int i = 0; i < items.Count; i++)
        {
            var r = SlotRect(i);
            bool isActive = items[i].Item1 == active && SidebarOpen;
            var box = new RectangleF(r.X + Ui.D(8), r.Y + Ui.D(5), r.Width - Ui.D(16), r.Height - Ui.D(10));
            if (isActive) Shapes.FillRound(g, Theme.Raised, box, Ui.D(9));
            else if (i == hover) Shapes.FillRound(g, Ui.Alpha(Theme.Hover, 170), box, Ui.D(9));
            var glyph = new Rectangle(r.X + (r.Width - Ui.D(18)) / 2, r.Y + (r.Height - Ui.D(18)) / 2, Ui.D(18), Ui.D(18));
            var drawBox = glyph;
            if (i == popIndex && pop.Value < 1f)
            {
                // the icon pops (grows, settles back) and sends out a fading ring
                float t = pop.Value;
                int grow = (int)Math.Round(Ui.D(6) * Math.Sin(t * Math.PI) * (1f - t * 0.4f));
                drawBox = Rectangle.Inflate(glyph, grow, grow);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float rr = Ui.D(10) + t * Ui.D(16);
                using (var pen = new Pen(Ui.Alpha(Theme.Orange, (int)(200 * (1f - t))), Math.Max(1f, Ui.Scale * 1.5f * (1f - t * 0.5f))))
                    g.DrawEllipse(pen, glyph.X + glyph.Width / 2f - rr, glyph.Y + glyph.Height / 2f - rr, rr * 2, rr * 2);
            }
            Glyphs.Draw(g, items[i].Item2, drawBox, isActive ? Theme.Orange : i == hover ? Theme.Text : Theme.Muted);
            if (Badges.Contains(items[i].Item1))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Theme.Lime)) g.FillEllipse(b, glyph.Right - Ui.D(2), glyph.Y - Ui.D(3), Ui.D(7), Ui.D(7));
            }
        }
    }
}

// Editor tabs: pill-shaped, with a gliding underline and unsaved dots that turn into close buttons on hover.
internal sealed class TabStrip : Control
{
    private readonly AnimatedFloat indicatorX = new AnimatedFloat(0f), indicatorW = new AnimatedFloat(0f);
    private readonly AnimatedFloat offset = new AnimatedFloat(0f);
    private readonly List<Rectangle> rects = new List<Rectangle>();
    private int hover = -1;
    private bool hoverClose;

    public TabStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        indicatorX.Changed += Invalidate;
        indicatorW.Changed += Invalidate;
        offset.Changed += Invalidate;
    }

    public Func<IList<EditorDoc>> Docs;
    public Func<EditorDoc> ActiveDoc;
    public event Action<EditorDoc> TabSelected;
    public event Action<EditorDoc> TabClosed;
    public event Action<EditorDoc, Point> TabContext;

    private static Color TypeColor(string title)
    {
        string ext = Path.GetExtension(title).ToLowerInvariant();
        if (ext == ".c") return Theme.Gold;
        if (ext == ".h" || ext == ".hpp" || ext == ".hh") return Theme.Violet;
        if (ext == ".cpp" || ext == ".cc" || ext == ".cxx" || ext == "") return Theme.Orange;
        return Theme.Muted;
    }

    private void Measure()
    {
        rects.Clear();
        var docs = Docs != null ? Docs() : new List<EditorDoc>();
        int x = Ui.D(6);
        foreach (var d in docs)
        {
            int w = TextRenderer.MeasureText(d.Title, Theme.Ui9, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.D(58);
            rects.Add(new Rectangle(x, Ui.D(5), w, Height - Ui.D(10)));
            x += w + Ui.D(4);
        }
    }

    public void SyncIndicator(bool animate)
    {
        Measure();
        var docs = Docs != null ? Docs() : null;
        var act = ActiveDoc != null ? ActiveDoc() : null;
        if (docs == null || act == null) { Invalidate(); return; }
        int i = docs.IndexOf(act);
        if (i < 0 || i >= rects.Count) return;
        var r = rects[i];
        float total = rects.Count > 0 ? rects[rects.Count - 1].Right + Ui.D(6) : 0;
        float target = offset.Target;
        if (r.X - target < 0) target = r.X - Ui.D(6);
        else if (r.Right - target > Width) target = r.Right - Width + Ui.D(10);
        target = Math.Max(0, Math.Min(Math.Max(0, total - Width), target));
        if (animate) { indicatorX.To(r.X + Ui.D(14), 240, Ease.OutBack); indicatorW.To(r.Width - Ui.D(28), 240); offset.To(target, 200); }
        else { indicatorX.Snap(r.X + Ui.D(14)); indicatorW.Snap(r.Width - Ui.D(28)); offset.Snap(target); }
        Invalidate();
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); SyncIndicator(false); }

    private int HitTab(Point p, out bool onClose)
    {
        onClose = false;
        var q = new Point(p.X + (int)offset.Value, p.Y);
        for (int i = 0; i < rects.Count; i++)
        {
            if (!rects[i].Contains(q)) continue;
            onClose = q.X > rects[i].Right - Ui.D(26);
            return i;
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool c;
        int h = HitTab(e.Location, out c);
        if (h != hover || c != hoverClose) { hover = h; hoverClose = c; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var docs = Docs();
        bool onClose;
        int i = HitTab(e.Location, out onClose);
        if (i < 0 || i >= docs.Count) return;
        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && onClose)) { if (TabClosed != null) TabClosed(docs[i]); }
        else if (e.Button == MouseButtons.Left) { if (TabSelected != null) TabSelected(docs[i]); }
        else if (e.Button == MouseButtons.Right && TabContext != null) TabContext(docs[i], e.Location);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        float total = rects.Count > 0 ? rects[rects.Count - 1].Right + Ui.D(6) : 0;
        offset.To(Math.Max(0, Math.Min(Math.Max(0, total - Width), offset.Target - e.Delta / 120f * Ui.D(80))), 160);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.EditorBg);
        Measure();
        var docs = Docs != null ? Docs() : new List<EditorDoc>();
        var act = ActiveDoc != null ? ActiveDoc() : null;
        float off = offset.Value;
        for (int i = 0; i < docs.Count && i < rects.Count; i++)
        {
            var d = docs[i];
            var r = rects[i];
            r.Offset(-(int)Math.Round(off), 0);
            bool isActive = d == act, isHover = i == hover;
            if (isActive) Shapes.FillRound(g, Theme.Raised, r, Ui.D(9));
            else if (isHover) Shapes.FillRound(g, Ui.Alpha(Theme.Hover, 140), r, Ui.D(9));
            var badge = new RectangleF(r.X + Ui.D(12), r.Y + (r.Height - Ui.D(8)) / 2f, Ui.D(8), Ui.D(8));
            Shapes.FillRound(g, TypeColor(d.Title), badge, Ui.D(2));
            Shapes.Text(g, d.Title, Theme.Ui9, new Rectangle(r.X + Ui.D(26), r.Y, r.Width - Ui.D(54), r.Height), isActive ? Theme.Text : Theme.TextDim, Shapes.Left);
            var close = new Rectangle(r.Right - Ui.D(24), r.Y + (r.Height - Ui.D(16)) / 2, Ui.D(16), Ui.D(16));
            if (isHover && hoverClose) Shapes.FillRound(g, Theme.Hover, close, Ui.D(4));
            if (d.Dirty && !(isHover && hoverClose))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(isActive ? Theme.Orange : Theme.TextDim))
                    g.FillEllipse(b, close.X + Ui.D(4), close.Y + Ui.D(4), Ui.D(8), Ui.D(8));
            }
            else if (isActive || isHover)
                Glyphs.Draw(g, "close", Rectangle.Inflate(close, -Ui.D(4), -Ui.D(4)), isHover && hoverClose ? Theme.Text : Theme.Muted);
        }
        if (act != null && indicatorW.Value > 1)
            Shapes.FillRound(g, Theme.Orange, new RectangleF(indicatorX.Value - off, Height - Ui.D(4), indicatorW.Value, Ui.D(2.5f)), Ui.D(1.5f));
        using (var pen = new Pen(Theme.EdgeSoft)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}

// Bottom status line: clickable segments, a pixel progress strip while busy and a colour flash after builds.
internal sealed class StatusBar : Control
{
    public sealed class Segment { public string Id, Text, Glyph; public Color Color; public bool Right; public Rectangle Bounds; }

    private readonly List<Segment> segments = new List<Segment>();
    private readonly AnimatedFloat flash = new AnimatedFloat(0f);
    private readonly AnimatedFloat sweep = new AnimatedFloat(1f);
    private Color flashColor = Theme.Lime;
    private bool busy;
    private double phase;
    private readonly Action<double> ticker;
    private int hover = -1;

    public StatusBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        flash.Changed += Invalidate;
        sweep.Changed += Invalidate;
        ticker = now => { phase = now; Invalidate(); };
    }

    public string Message = "";
    public event Action<string> SegmentClicked;

    public void Set(string id, string text, string glyph = null, Color? color = null, bool right = true)
    {
        var s = segments.FirstOrDefault(x => x.Id == id);
        if (s == null) { s = new Segment { Id = id, Right = right }; segments.Add(s); }
        s.Text = text; s.Glyph = glyph; s.Color = color ?? Theme.TextDim;
        Invalidate();
    }

    public void Remove(string id) { segments.RemoveAll(s => s.Id == id); Invalidate(); }

    public bool Busy
    {
        get { return busy; }
        set
        {
            if (busy == value) return;
            busy = value;
            if (busy) Animator.AddTicker(ticker); else Animator.RemoveTicker(ticker);
            Invalidate();
        }
    }

    private readonly AnimatedFloat shake = new AnimatedFloat(0f);
    private bool shakeHooked;

    public void Shake()
    {
        if (!shakeHooked) { shake.Changed += Invalidate; shakeHooked = true; }
        shake.Snap(1f);
        shake.To(0f, 520, Ease.Linear);
    }

    public void Flash(Color color)
    {
        flashColor = color;
        flash.Snap(1f);
        flash.To(0f, 900, Ease.InOutCubic);
        if (!Animator.Reduced) { sweep.Snap(0f); sweep.To(1f, 1100, Ease.InOutCubic); }
    }

    private void Layout2(Graphics g)
    {
        int left = Ui.D(12), right = Width - Ui.D(10);
        int msgWidth = TextRenderer.MeasureText(g, Message ?? "", Theme.UiSmall, Size.Empty, TextFormatFlags.NoPadding).Width;
        left += Ui.D(busy ? 22 : 0) + msgWidth + Ui.D(18);
        foreach (var s in segments)
        {
            int w = TextRenderer.MeasureText(g, s.Text ?? "", Theme.UiSmall, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.D(s.Glyph != null ? 30 : 18);
            if (s.Right) { right -= w; s.Bounds = new Rectangle(right, Ui.D(3), w, Height - Ui.D(6)); right -= Ui.D(2); }
            else { s.Bounds = new Rectangle(left, Ui.D(3), w, Height - Ui.D(6)); left += w + Ui.D(2); }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = segments.FindIndex(s => s.Bounds.Contains(e.Location));
        if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var s = segments.FirstOrDefault(x => x.Bounds.Contains(e.Location));
        if (s != null && SegmentClicked != null) SegmentClicked(s.Id);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Lerp(Theme.Bg, Ui.Alpha(flashColor, 255), flash.Value * 0.18f));
        Layout2(g);
        if (sweep.Value > 0f && sweep.Value < 1f)
        {
            // a band of light that runs along the status bar after a build finishes
            float band = Math.Max(Ui.D(120), Width * 0.22f);
            float sx = -band + sweep.Value * (Width + band * 2);
            var blend = new System.Drawing.Drawing2D.ColorBlend(3)
            {
                Colors = new[] { Ui.Alpha(flashColor, 0), Ui.Alpha(flashColor, 85), Ui.Alpha(flashColor, 0) },
                Positions = new[] { 0f, 0.5f, 1f },
            };
            using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(sx - band / 2, 0, band, Height), Color.White, Color.White, 0f) { InterpolationColors = blend })
                g.FillRectangle(lg, sx - band / 2, 0, band, Height);
            blend.Colors = new[] { Ui.Alpha(flashColor, 0), Ui.Alpha(flashColor, 255), Ui.Alpha(flashColor, 0) };
            using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(sx - band / 2, 0, band, Height), Color.White, Color.White, 0f) { InterpolationColors = blend })
                g.FillRectangle(lg, sx - band / 2, 0, band, Math.Max(2, Ui.D(2)));
        }
        int x = Ui.D(12);
        if (busy)
        {
            Glyphs.Spinner(g, new Rectangle(x, (Height - Ui.D(12)) / 2, Ui.D(12), Ui.D(12)), phase / 700.0, Theme.Lime, Theme.Orange);
            x += Ui.D(22);
            int cell = Math.Max(2, Ui.D(3));
            int stride = cell * 3;
            int shift = (int)(phase / 40) % stride;
            int ci = 0;
            for (int px = -stride + shift; px < Width; px += stride, ci++)
                using (var b = new SolidBrush(Theme.IsLight ? Theme.Rainbow[(ci + (int)(phase / 90)) % Theme.Rainbow.Length] : Ui.Alpha(Theme.Orange, 200)))
                    g.FillRectangle(b, px, 0, cell, Math.Max(1, Ui.D(Theme.IsLight ? 3 : 2)));
        }
        x += (int)(Math.Sin(shake.Value * 26) * shake.Value * Ui.D(6));
        Shapes.Text(g, Message ?? "", Theme.UiSmall, new Rectangle(x, 0, Width / 2, Height), flash.Value > 0.05f ? Ui.Lerp(Theme.TextDim, flashColor, flash.Value) : Theme.TextDim, Shapes.Left);
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            if (i == hover) Shapes.FillRound(g, Theme.Raised, s.Bounds, Ui.D(6));
            int tx = s.Bounds.X + Ui.D(9);
            if (s.Glyph != null)
            {
                Glyphs.Draw(g, s.Glyph, new Rectangle(tx, (Height - Ui.D(10)) / 2, Ui.D(10), Ui.D(10)), s.Color);
                tx += Ui.D(14);
            }
            Shapes.Text(g, s.Text ?? "", Theme.UiSmall, new Rectangle(tx, 0, s.Bounds.Right - tx, Height), i == hover ? Theme.Text : s.Color, Shapes.Left);
        }
        using (var pen = new Pen(Theme.EdgeSoft)) g.DrawLine(pen, 0, 0, Width, 0);
    }

    protected override void Dispose(bool disposing) { if (disposing) Animator.RemoveTicker(ticker); base.Dispose(disposing); }
}

// Small tab header for the bottom panel (Output / Problems / Terminal) with a gliding underline.
internal sealed class PanelTabs : Control
{
    private readonly List<Tuple<string, string>> tabs = new List<Tuple<string, string>>();
    private readonly List<Rectangle> rects = new List<Rectangle>();
    private readonly AnimatedFloat ix = new AnimatedFloat(0f), iw = new AnimatedFloat(0f);
    private int hover = -1;
    public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
    public string Active;
    public event Action<string> TabClicked;

    public PanelTabs()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        ix.Changed += Invalidate; iw.Changed += Invalidate;
        BackColor = Theme.Panel;
    }

    public void Add(string id, string title) { tabs.Add(Tuple.Create(id, title)); }

    public void Select(string id, bool animate)
    {
        Active = id;
        Measure();
        int i = tabs.FindIndex(t => t.Item1 == id);
        if (i < 0) return;
        if (animate) { ix.To(rects[i].X + Ui.D(10), 220, Ease.OutBack); iw.To(rects[i].Width - Ui.D(20), 220); }
        else { ix.Snap(rects[i].X + Ui.D(10)); iw.Snap(rects[i].Width - Ui.D(20)); }
        Invalidate();
    }

    private string Label(Tuple<string, string> t)
    {
        int n;
        return Counts.TryGetValue(t.Item1, out n) && n > 0 ? t.Item2 + "  " + n : t.Item2;
    }

    private void Measure()
    {
        rects.Clear();
        int x = Ui.D(10);
        foreach (var t in tabs)
        {
            int w = TextRenderer.MeasureText(Label(t), Theme.UiSemi, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.D(24);
            rects.Add(new Rectangle(x, 0, w, Height));
            x += w + Ui.D(2);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = rects.FindIndex(r => r.Contains(e.Location));
        if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int i = rects.FindIndex(r => r.Contains(e.Location));
        if (i >= 0 && TabClicked != null) TabClicked(tabs[i].Item1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Measure();
        for (int i = 0; i < tabs.Count; i++)
        {
            bool act = tabs[i].Item1 == Active;
            var color = act ? Theme.Text : i == hover ? Theme.TextDim : Theme.Muted;
            Shapes.Text(g, Label(tabs[i]), Theme.UiSemi, rects[i], color, Shapes.Center);
        }
        if (iw.Value > 1) Shapes.FillRound(g, Theme.Orange, new RectangleF(ix.Value, Height - Ui.D(3), iw.Value, Ui.D(2.5f)), Ui.D(1.5f));
    }
}

// Shown when no file is open: logo with twinkling pixels, quick actions and recent items.
internal sealed class WelcomePage : Control
{
    public sealed class Action2 { public string Glyph, Title, Hint; public Action Run; public Rectangle Bounds; public AnimatedFloat Hover = new AnimatedFloat(0f); public AnimatedFloat Appear = new AnimatedFloat(1f); }

    public readonly List<Action2> Actions = new List<Action2>();
    public readonly List<Tuple<string, string, Action>> Recent = new List<Tuple<string, string, Action>>();
    private readonly List<Rectangle> recentRects = new List<Rectangle>();
    private int hoverRecent = -1;
    private Image logo;
    private readonly Random random = new Random(7);
    private readonly List<float[]> sparks = new List<float[]>();
    private readonly Action<double> ticker;
    private double lastSpark, lastFrame, shownAt;
    private readonly List<float[]> stars = new List<float[]>();

    public WelcomePage()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.EditorBg;
        ticker = now =>
        {
            if (!Visible) return;
            if (now - lastSpark > 260 && sparks.Count < 7)
            {
                lastSpark = now;
                sparks.Add(new[] { (float)random.NextDouble(), (float)random.NextDouble(), (float)now, random.Next(3) });
            }
            sparks.RemoveAll(s => now - s[2] > 1800);
            if (Animator.Reduced) { Invalidate(LogoArea()); return; }
            // the drifting starfield repaints the whole page, ~30 times a second
            if (now - lastFrame < 32) return;
            lastFrame = now;
            Invalidate();
        };
        for (int i = 0; i < 54; i++)
        {
            float layer = (float)random.NextDouble();
            stars.Add(new[] { (float)random.NextDouble(), (float)random.NextDouble(), 0.6f + layer * 2.2f, layer < 0.7f ? 1f : 2f, random.Next(4), (float)(random.NextDouble() * Math.PI * 2) });
        }
    }

    public string Version = "";
    public string Shortcuts = "";

    public void SetLogo(Image image) { logo = image; Invalidate(); }

    public void AddAction(string glyph, string title, string hint, Action run)
    {
        var a = new Action2 { Glyph = glyph, Title = title, Hint = hint, Run = run };
        a.Hover.Changed += Invalidate;
        a.Appear.Changed += Invalidate;
        Actions.Add(a);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) { Animator.AddTicker(ticker); shownAt = Animator.Now; } else Animator.RemoveTicker(ticker);
        if (Visible)
            for (int i = 0; i < Actions.Count; i++) { Actions[i].Appear.Snap(0f); Actions[i].Appear.To(1f, 420, Ease.OutBack, 80 + i * 70); }
    }

    protected override void Dispose(bool disposing) { if (disposing) Animator.RemoveTicker(ticker); base.Dispose(disposing); }

    private bool Compact { get { return Height < Ui.D(560); } }
    private int LogoSize { get { return Compact ? Ui.D(72) : Ui.D(96); } }

    // Rows are balanced (5 cards -> 3 + 2 rather than 4 + 1).
    private int CardRows
    {
        get
        {
            int fit = Math.Max(1, Math.Min(Actions.Count, (Width - Ui.D(40)) / (Ui.D(176) + Ui.D(12))));
            return (Actions.Count + fit - 1) / fit;
        }
    }
    private int CardsPerRow { get { return Math.Max(1, (Actions.Count + CardRows - 1) / CardRows); } }

    // Logo, title, cards and as many recent items as fit, centred vertically.
    private int BaseHeight
    {
        get { return LogoSize + Ui.D(14) + Ui.D(44) + (Compact ? Ui.D(14) : Ui.D(38)) + CardRows * (Ui.D(86) + Ui.D(12)); }
    }

    private int RecentRowsThatFit
    {
        get
        {
            int spare = Height - BaseHeight - Ui.D(80);
            if (Recent.Count == 0 || spare < Ui.D(80)) return 0;
            return Math.Min(Recent.Count, (spare - Ui.D(42)) / Ui.D(30));
        }
    }

    private int ContentTop
    {
        get
        {
            int total = BaseHeight + (RecentRowsThatFit > 0 ? Ui.D(42) + RecentRowsThatFit * Ui.D(30) : 0);
            return Math.Max(Ui.D(16), (Height - total - Ui.D(30)) / 2);
        }
    }

    private Rectangle LogoArea()
    {
        int size = LogoSize;
        return new Rectangle(Width / 2 - size, ContentTop - Ui.D(10), size * 2, size + Ui.D(20));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool any = false;
        foreach (var a in Actions)
        {
            bool h = a.Bounds.Contains(e.Location);
            a.Hover.To(h ? 1f : 0f, h ? 140 : 260);
            any |= h;
        }
        int hr = recentRects.FindIndex(r => r.Contains(e.Location));
        if (hr != hoverRecent) { hoverRecent = hr; Invalidate(); }
        Cursor = any || hr >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        foreach (var a in Actions) a.Hover.To(0f, 260);
        hoverRecent = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        foreach (var a in Actions) if (a.Bounds.Contains(e.Location)) { a.Run(); return; }
        int i = recentRects.FindIndex(r => r.Contains(e.Location));
        if (i >= 0 && i < Recent.Count) Recent[i].Item3();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        int top = ContentTop;
        int size = LogoSize;
        double now = Animator.Now;
        bool rich = !Animator.Reduced;
        if (rich)
        {
            // pixel stars drifting upward at three speeds; slower ones are dimmer, like distant layers
            foreach (var s in stars)
            {
                float yy = (float)((s[1] - now * s[2] / 160000.0) % 1.0);
                if (yy < 0) yy += 1f;
                float xx = (float)(s[0] + Math.Sin(now / 4000.0 + s[5]) * 0.004);
                double tw = 0.55 + 0.45 * Math.Sin(now / 650.0 * (0.6 + s[2] * 0.3) + s[5]);
                int alpha = (int)((Theme.IsLight ? 70 : 60 + s[2] * 30) * tw);
                var c = s[4] == 0 ? Theme.Orange : s[4] == 1 ? Theme.Gold : s[4] == 2 ? Theme.Lime : Theme.Muted;
                int px = Ui.D(s[3] + (s[2] > 2.2f ? 1 : 0));
                using (var b = new SolidBrush(Ui.Alpha(c, alpha))) g.FillRectangle(b, (int)(xx * Width), (int)(yy * Height), px, px);
            }
        }
        int bob = rich ? (int)Math.Round(Math.Sin(now / 900.0) * Ui.D(3)) : 0;
        var logoRect = new Rectangle((Width - size) / 2, top + bob, size, size);
        if (Theme.IsLight)
        {
            // like the icon: rainbow stripes run out sideways from behind the logo, growing in on entry
            double since = rich ? (Animator.Now - shownAt) : 99999;
            float grow = Ease.OutCubic((float)Math.Min(1.0, since / 900.0));
            int half = (int)((Width / 2) * grow);
            int bandH = Ui.D(30);
            var band = new Rectangle(Width / 2 - half, logoRect.Y + size / 2 - bandH / 2 + Ui.D(6), half * 2, bandH);
            Shapes.Stripes(g, band, rich ? (since / 2600.0) % 3.0 / 3.0 : -1);
        }
        if (rich && !Theme.IsLight)
        {
            // warm halo breathing behind the logo
            float pulse = (float)(0.75 + 0.25 * Math.Sin(now / 1100.0));
            var halo = RectangleF.Inflate(logoRect, size * 0.55f * pulse, size * 0.45f * pulse);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(halo);
                using (var pg = new PathGradientBrush(path) { CenterColor = Ui.Alpha(Theme.Orange, (int)(Theme.IsLight ? 40 : 55 * pulse)), SurroundColors = new[] { Ui.Alpha(Theme.Orange, 0) } })
                    g.FillPath(pg, path);
            }
        }
        if (logo != null)
        {
            if (Theme.IsLight)
            {
                // flat offset shadow, then the icon tile with a dark brown outline
                Shapes.FillRound(g, Theme.Outline, new RectangleF(logoRect.X + Ui.D(4), logoRect.Y + Ui.D(4), logoRect.Width, logoRect.Height), logoRect.Width * 0.224f);
                Shapes.DrawRounded(g, logo, logoRect, logoRect.Width * 0.224f);
                Shapes.StrokeRound(g, Theme.Outline, logoRect, logoRect.Width * 0.224f, Math.Max(2f, Ui.Scale * 1.5f));
            }
            else Shapes.DrawRounded(g, logo, logoRect, logoRect.Width * 0.224f);
        }
        foreach (var s in sparks)
        {
            float age = (float)((now - s[2]) / 1800.0);
            float alpha = age < 0.5f ? age * 2 : (1 - age) * 2;
            int px = Ui.D(4);
            float sx = logoRect.X - Ui.D(40) + s[0] * (size + Ui.D(80));
            float sy = logoRect.Y - Ui.D(8) + s[1] * (size + Ui.D(16));
            if (logoRect.Contains((int)sx, (int)sy)) continue;
            var c = s[3] == 0 ? Theme.Lime : s[3] == 1 ? Theme.Orange : Theme.Gold;
            using (var b = new SolidBrush(Ui.Alpha(c, (int)(230 * Math.Max(0, alpha))))) g.FillRectangle(b, sx, sy, px, px);
        }
        int y = logoRect.Bottom + Ui.D(14);
        Shapes.Text(g, "Gestirn dev", Theme.UiHero, new Rectangle(0, y, Width, Ui.D(42)), Theme.Text, Shapes.Center);
        y += Ui.D(44);
        if (!Compact) Shapes.Text(g, Version, Theme.Ui9, new Rectangle(0, y, Width, Ui.D(20)), Theme.Muted, Shapes.Center);
        y += Compact ? Ui.D(14) : Ui.D(38);

        int cardW = Ui.D(176), cardH = Ui.D(86), gap = Ui.D(12);
        int perRow = CardsPerRow;
        int rows = CardRows;
        for (int i = 0; i < Actions.Count; i++)
        {
            int row = i / perRow, col = i % perRow;
            int inRow = Math.Min(perRow, Actions.Count - row * perRow);
            int rowW = inRow * cardW + (inRow - 1) * gap;
            int x = (Width - rowW) / 2 + col * (cardW + gap);
            var a = Actions[i];
            float h = a.Hover.Value;
            if (a.Appear.Value <= 0.02f) { a.Bounds = new Rectangle(x, y + row * (cardH + gap), cardW, cardH); continue; }
            int lift = (int)(h * Ui.D(3)) - (int)((1f - Math.Min(1f, a.Appear.Value)) * Ui.D(26));
            a.Bounds = new Rectangle(x, y + row * (cardH + gap), cardW, cardH);
            var r = new RectangleF(x, a.Bounds.Y - lift, cardW, cardH);
            if (Theme.IsLight)
            {
                // printed-card look: flat offset shadow in the stripe colour of this card, dark outline
                var sh = Theme.Rainbow[(i * 2) % Theme.Rainbow.Length];
                Shapes.FillRound(g, sh, new RectangleF(r.X + Ui.D(4 - h * 2), r.Y + Ui.D(4 - h * 2), r.Width, r.Height), Ui.D(12));
                Shapes.FillRound(g, Theme.PanelHi, r, Ui.D(12));
                Shapes.StrokeRound(g, Theme.Outline, r, Ui.D(12), Math.Max(2f, Ui.Scale * 1.25f));
            }
            else
            {
                Shapes.FillRound(g, Ui.Lerp(Theme.Panel, Theme.Raised, h), r, Ui.D(12));
                Shapes.StrokeRound(g, Ui.Lerp(Theme.Edge, Ui.Alpha(Theme.Orange, 200), h), r, Ui.D(12), Math.Max(1f, Ui.Scale * 0.75f));
            }
            Glyphs.Draw(g, a.Glyph, new Rectangle(x + Ui.D(16), (int)r.Y + Ui.D(16), Ui.D(18), Ui.D(18)), Ui.Lerp(Theme.Orange, Theme.OrangeHot, h));
            Shapes.Text(g, a.Title, Theme.UiSemi, new Rectangle(x + Ui.D(16), (int)r.Y + Ui.D(42), cardW - Ui.D(24), Ui.D(20)), Theme.Text, Shapes.Left);
            Shapes.Text(g, a.Hint, Theme.UiSmall, new Rectangle(x + Ui.D(16), (int)r.Y + Ui.D(62), cardW - Ui.D(24), Ui.D(16)), Theme.Muted, Shapes.Left);
        }
        y += rows * (cardH + gap) + Ui.D(18);

        recentRects.Clear();
        int fit = RecentRowsThatFit;
        if (fit > 0)
        {
            int listW = Math.Min(Ui.D(560), Width - Ui.D(60));
            int x = (Width - listW) / 2;
            Shapes.Text(g, L.T("RECENT", "最近使用"), Theme.UiSmall, new Rectangle(x, y, listW, Ui.D(20)), Theme.Muted, Shapes.Left);
            y += Ui.D(24);
            for (int i = 0; i < fit; i++)
            {
                var r = new Rectangle(x, y, listW, Ui.D(28));
                recentRects.Add(r);
                if (i == hoverRecent) Shapes.FillRound(g, Theme.Raised, r, Ui.D(7));
                Glyphs.Draw(g, Directory.Exists(Recent[i].Item2) ? "folder" : Recent[i].Item2.EndsWith(".dev", StringComparison.OrdinalIgnoreCase) ? "project" : "file",
                    new Rectangle(x + Ui.D(10), y + Ui.D(8), Ui.D(12), Ui.D(12)), i == hoverRecent ? Theme.Orange : Theme.TextDim);
                Shapes.Text(g, Recent[i].Item1, Theme.Ui9, new Rectangle(x + Ui.D(32), y, Ui.D(200), Ui.D(28)), Theme.Text, Shapes.Left);
                Shapes.Text(g, Recent[i].Item2, Theme.UiSmall, new Rectangle(x + Ui.D(200), y, listW - Ui.D(210), Ui.D(28)), Theme.Muted, Shapes.Right | TextFormatFlags.PathEllipsis);
                y += Ui.D(30);
            }
        }
        if (!string.IsNullOrEmpty(Shortcuts) && y + Ui.D(60) < Height)
            Shapes.Text(g, Shortcuts, Theme.UiSmall, new Rectangle(0, Height - Ui.D(36), Width, Ui.D(20)), Theme.Faint, Shapes.Center);
    }
}
}
