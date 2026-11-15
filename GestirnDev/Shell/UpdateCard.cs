using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace GestirnDev
{
// The "new version available" card: slides in at the top right, with a light sweeping around its
// border, twinkling pixels by the icon and a striped progress bar while the update downloads.
internal sealed class UpdateCard : Control
{
    public enum Stage { Offer, Downloading, Ready, Failed }

    public readonly AnimatedFloat Appear = new AnimatedFloat(0f);
    private readonly AnimatedFloat progressAnim = new AnimatedFloat(0f);
    private readonly PixelButton primary = new PixelButton { Kind = ButtonKind.Primary, Radius = 9, Glyph = "update" };
    private readonly PixelButton secondary = new PixelButton { Kind = ButtonKind.Secondary, Radius = 9, Glyph = "link" };
    private readonly PixelButton tertiary = new PixelButton { Kind = ButtonKind.Ghost, Radius = 9 };
    private readonly PixelButton close = new PixelButton { Kind = ButtonKind.Ghost, Radius = 7, Glyph = "close" };
    private readonly ReleaseInfo release;
    private readonly string current;
    private readonly List<string> notes;
    private double phase;
    private string progressText = "";
    public Stage State { get; private set; }
    public bool Closing;

    public event Action UpdateRequested, PageRequested, SkipRequested, CancelRequested, RestartRequested, Dismissed;

    public UpdateCard(ReleaseInfo release, string current)
    {
        this.release = release;
        this.current = current;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Raised;
        notes = NoteLines(release.Notes, 3);
        Size = new Size(Ui.D(420), Ui.D(148 + 20 * Math.Max(1, notes.Count)));
        foreach (var b in new[] { primary, secondary, tertiary, close }) { b.Height = Ui.D(32); Controls.Add(b); }
        close.Size = new Size(Ui.D(26), Ui.D(26));
        primary.Click += (_, _) =>
        {
            if (State == Stage.Downloading) return;
            if (State == Stage.Ready) { if (RestartRequested != null) RestartRequested(); }
            else if (UpdateRequested != null) UpdateRequested();
        };
        secondary.Click += (_, _) => { if (State == Stage.Downloading) { if (CancelRequested != null) CancelRequested(); } else if (PageRequested != null) PageRequested(); };
        tertiary.Click += (_, _) => { if (State == Stage.Ready || State == Stage.Failed) { if (Dismissed != null) Dismissed(); } else if (SkipRequested != null) SkipRequested(); };
        close.Click += (_, _) => { if (State == Stage.Downloading && CancelRequested != null) CancelRequested(); if (Dismissed != null) Dismissed(); };
        progressAnim.Changed += Invalidate;
        Animator.AddTicker(Tick);
        SetStage(Stage.Offer);
        UpdateRegion();
    }

    public ReleaseInfo Release { get { return release; } }

    private static List<string> NoteLines(string text, int max)
    {
        var list = new List<string>();
        foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
        {
            if (raw.TrimStart().StartsWith("#")) continue; // headings repeat the version number
            string l = raw.Trim().TrimStart('#', '-', '*', '>', ' ').Replace("**", "").Replace("`", "").Trim();
            if (l.Length < 2 || l.StartsWith("http", StringComparison.OrdinalIgnoreCase) || l.StartsWith("[")) continue;
            list.Add("· " + l);
            if (list.Count == max) break;
        }
        return list;
    }

    public void SetStage(Stage stage, string message = null)
    {
        State = stage;
        switch (stage)
        {
            case Stage.Offer:
                primary.Text = L.T("Update now", "立即更新"); primary.Glyph = "update";
                secondary.Text = L.T("Release notes", "发布页"); secondary.Glyph = "link";
                tertiary.Text = L.T("Skip this version", "跳过此版本");
                break;
            case Stage.Downloading:
                primary.Text = L.T("Downloading…", "下载中…"); primary.Glyph = "update";
                secondary.Text = L.T("Cancel", "取消"); secondary.Glyph = "close";
                tertiary.Text = "";
                progressAnim.Snap(0f);
                progressText = L.T("Connecting…", "正在连接…");
                break;
            case Stage.Ready:
                primary.Text = L.T("Restart & update", "重启并更新"); primary.Glyph = "refresh";
                secondary.Text = L.T("Release notes", "发布页"); secondary.Glyph = "link";
                tertiary.Text = L.T("Later", "稍后");
                progressText = message ?? L.T("Downloaded. Your files are saved before restarting.", "已下载完成，重启前会自动保存文件。");
                progressAnim.To(1f, 200);
                break;
            case Stage.Failed:
                primary.Text = L.T("Try again", "重试"); primary.Glyph = "refresh";
                secondary.Text = L.T("Download page", "下载页"); secondary.Glyph = "link";
                tertiary.Text = L.T("Close", "关闭");
                progressText = message ?? L.T("Download failed.", "下载失败。");
                break;
        }
        primary.Busy = stage == Stage.Downloading;
        tertiary.Visible = tertiary.Text.Length > 0;
        LayoutButtons();
        Invalidate();
    }

    public void SetProgress(double fraction, long done, long total)
    {
        progressAnim.To((float)Math.Max(0, Math.Min(1, fraction)), 160);
        progressText = total > 0
            ? string.Format("{0:0}%  ·  {1:0.0} / {2:0.0} MB", fraction * 100, done / 1048576.0, total / 1048576.0)
            : string.Format("{0:0.0} MB", done / 1048576.0);
        Invalidate();
    }

    private void LayoutButtons()
    {
        int pad = Ui.D(18), y = Height - Ui.D(48);
        foreach (var b in new[] { primary, secondary, tertiary }) b.Width = b.PreferredWidth();
        primary.Location = new Point(pad, y);
        secondary.Location = new Point(primary.Right + Ui.D(8), y);
        tertiary.Location = new Point(secondary.Right + Ui.D(4), y);
        close.Location = new Point(Width - close.Width - Ui.D(10), Ui.D(10));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
        LayoutButtons();
    }

    private void UpdateRegion()
    {
        using (var path = Shapes.Round(new RectangleF(0, 0, Width, Height), Ui.D(14)))
            Region = new Region(path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Animator.RemoveTicker(Tick);
        base.Dispose(disposing);
    }

    private void Tick(double now)
    {
        if (!Visible || Animator.Reduced) return;
        phase = now / 1000.0;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Raised);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float rad = Ui.D(14);

        // soft warm wash in the top-left corner
        using (var wash = new LinearGradientBrush(new RectangleF(0, 0, Width, Height), Ui.Alpha(Theme.Orange, Theme.IsLight ? 26 : 34), Ui.Alpha(Theme.Raised, 0), 35f))
            g.FillRectangle(wash, 0, 0, Width, Height);

        // border, plus a highlight that travels around it
        Shapes.StrokeRound(g, Ui.Alpha(Theme.Orange, 110), r, rad, Math.Max(1f, Ui.Scale));
        if (!Animator.Reduced)
        {
            double t = (phase / 3.2) % 1.0;
            float cx = (float)(-Width * 0.4 + t * Width * 1.8);
            using (var path = Shapes.Round(r, rad))
            using (var brush = new LinearGradientBrush(new RectangleF(cx - Ui.D(90), 0, Ui.D(180), Height), Color.White, Color.White, 20f))
            {
                var blend = new ColorBlend(3)
                {
                    Colors = new[] { Ui.Alpha(Theme.Gold, 0), Ui.Alpha(Theme.OrangeHot, 255), Ui.Alpha(Theme.Gold, 0) },
                    Positions = new[] { 0f, 0.5f, 1f },
                };
                brush.InterpolationColors = blend;
                using (var pen = new Pen(brush, Math.Max(2f, Ui.Scale * 1.6f))) g.DrawPath(pen, path);
            }
        }

        // icon tile with a pixel arrow that bobs, and twinkling sparks
        var tile = new RectangleF(Ui.D(18), Ui.D(18), Ui.D(44), Ui.D(44));
        using (var tb = new LinearGradientBrush(tile, Theme.OrangeHot, Theme.OrangeDeep, 60f))
        using (var tp = Shapes.Round(tile, Ui.D(12)))
            g.FillPath(tb, tp);
        int bob = Animator.Reduced ? 0 : (int)Math.Round(Math.Sin(phase * 3.0) * Ui.D(2));
        Glyphs.Draw(g, "update", new Rectangle((int)tile.X + Ui.D(12), (int)tile.Y + Ui.D(12) + bob, Ui.D(20), Ui.D(20)), Color.White);
        if (!Animator.Reduced)
        {
            var sparks = new[] { new PointF(tile.Right + Ui.D(2), tile.Y - Ui.D(2)), new PointF(tile.X - Ui.D(5), tile.Bottom - Ui.D(8)), new PointF(tile.Right + Ui.D(6), tile.Bottom - Ui.D(2)) };
            for (int i = 0; i < sparks.Length; i++)
            {
                double s = Math.Sin(phase * 2.4 + i * 2.1);
                if (s <= 0) continue;
                int a = (int)(255 * s), size = Math.Max(1, (int)(Ui.D(3) * s + 1));
                using (var b = new SolidBrush(Ui.Alpha(i == 1 ? Theme.Lime : Theme.Gold, a)))
                {
                    g.FillRectangle(b, sparks[i].X - size / 2f, sparks[i].Y - size * 1.5f, size, size * 3);
                    g.FillRectangle(b, sparks[i].X - size * 1.5f, sparks[i].Y - size / 2f, size * 3, size);
                }
            }
        }

        int x = Ui.D(76);
        Shapes.Text(g, L.T("New version ", "发现新版本 ") + release.Version, Theme.UiTitle, new Rectangle(x, Ui.D(14), Width - x - Ui.D(44), Ui.D(28)), Theme.Text, Shapes.Left);
        var pill = new Rectangle(x, Ui.D(44), Ui.D(72), Ui.D(20));
        string tag = L.T("Recommended", "推荐更新");
        pill.Width = TextRenderer.MeasureText(tag, Theme.UiSmall, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.D(16);
        Shapes.FillRound(g, Ui.Alpha(Theme.Lime, 45), pill, Ui.D(10));
        Shapes.Text(g, tag, Theme.UiSmall, pill, Theme.Lime, Shapes.Center);
        Shapes.Text(g, L.T("Installed: ", "当前版本 ") + current + "  →  " + release.Version, Theme.UiSmall,
            new Rectangle(pill.Right + Ui.D(10), pill.Y, Width - pill.Right - Ui.D(24), pill.Height), Theme.Muted, Shapes.Left);

        int y = Ui.D(78);
        if (State == Stage.Offer)
        {
            foreach (var line in notes)
            {
                Shapes.Text(g, line, Theme.Ui9, new Rectangle(Ui.D(20), y, Width - Ui.D(40), Ui.D(20)), Theme.TextDim, Shapes.Left);
                y += Ui.D(20);
            }
            if (notes.Count == 0)
                Shapes.Text(g, L.T("Gestirn dev ", "Gestirn dev ") + release.Version + L.T(" is out on GitHub.", " 已在 GitHub 发布。"), Theme.Ui9, new Rectangle(Ui.D(20), y, Width - Ui.D(40), Ui.D(20)), Theme.TextDim, Shapes.Left);
        }
        else
        {
            var track = new RectangleF(Ui.D(20), y + Ui.D(4), Width - Ui.D(40), Ui.D(10));
            Shapes.FillRound(g, Ui.Alpha(Theme.Edge, 120), track, Ui.D(5));
            float f = State == Stage.Failed ? 1f : progressAnim.Value;
            if (f > 0.001f)
            {
                var fill = new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * f), track.Height);
                Color c1 = State == Stage.Failed ? Theme.Red : State == Stage.Ready ? Theme.Lime : Theme.Orange;
                Color c2 = State == Stage.Failed ? Theme.Red : State == Stage.Ready ? Ui.Lerp(Theme.Lime, Theme.Gold, 0.4f) : Theme.Gold;
                using (var path = Shapes.Round(fill, track.Height / 2))
                using (var brush = new LinearGradientBrush(new RectangleF(fill.X - 1, fill.Y, fill.Width + 2, fill.Height), c1, c2, 0f))
                {
                    g.FillPath(brush, path);
                    if (State == Stage.Downloading && !Animator.Reduced)
                    {
                        var old = g.Clip;
                        g.SetClip(path, CombineMode.Intersect);
                        using (var stripe = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
                        {
                            float step = Ui.D(14), off = (float)((phase * Ui.D(40)) % step);
                            for (float sx = fill.X - step * 2 + off; sx < fill.Right; sx += step)
                                g.FillPolygon(stripe, new[] { new PointF(sx, fill.Bottom), new PointF(sx + step / 2, fill.Bottom), new PointF(sx + step, fill.Y), new PointF(sx + step / 2, fill.Y) });
                        }
                        g.Clip = old;
                    }
                }
            }
            Color tc = State == Stage.Failed ? Theme.Red : State == Stage.Ready ? Theme.Lime : Theme.TextDim;
            Shapes.Text(g, progressText, Theme.UiSmall, new Rectangle(Ui.D(20), y + Ui.D(18), Width - Ui.D(40), Ui.D(20)), tc, Shapes.Left);
        }
    }
}
}
