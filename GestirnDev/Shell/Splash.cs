using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace GestirnDev
{
// Start-up splash: the logo assembles itself from flying pixels, the title rises in letter by letter,
// then the splash fades out once the main window is up. Shown only with rich animations on.
internal sealed class SplashForm : Form
{
    private sealed class Particle { public PointF From, To; public Color Color; public float Delay; public float Spin; }

    public const double AssembleMs = 900;
    private readonly List<Particle> particles = new List<Particle>();
    private readonly Image logo;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Timer timer = new Timer { Interval = 15 };
    private readonly string version;
    private int cell;
    private Rectangle logoRect;
    private double fadeStart = -1;
    public bool Skipped { get; private set; }

    public SplashForm(string version)
    {
        this.version = version;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Bg;
        ClientSize = new Size(Ui.D(440), Ui.D(280));
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Text = "Gestirn dev";
        logo = LoadLogo();
        BuildParticles();
        using (var path = Shapes.Round(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), Ui.D(18)))
            Region = new Region(path);
        timer.Tick += (_, _) => OnTick();
        timer.Start();
        Click += (_, _) => Skipped = true;
        KeyDown += (_, _) => Skipped = true;
    }

    public double Elapsed { get { return clock.Elapsed.TotalMilliseconds; } }
    public bool Assembled { get { return Skipped || Elapsed >= AssembleMs; } }

    private static Image LoadLogo()
    {
        string path = Path.Combine(Application.StartupPath, "GestirnEra-70s-256.png");
        try { if (File.Exists(path)) using (var img = Image.FromFile(path)) return new Bitmap(img); }
        catch (OutOfMemoryException) { }
        catch (ArgumentException) { }
        return null;
    }

    private void BuildParticles()
    {
        int size = Ui.D(112);
        logoRect = new Rectangle((ClientSize.Width - size) / 2, Ui.D(42), size, size);
        const int grid = 22;
        cell = Math.Max(2, size / grid);
        var rnd = new Random(11);
        Bitmap sample = null;
        try
        {
            sample = logo != null ? new Bitmap(logo, grid, grid) : null;
            // the tile's paper colour is simply its most common opaque colour (the corners are transparent now)
            Color paper = Color.Empty;
            if (sample != null)
            {
                var counts = new Dictionary<int, int>();
                var sampleColor = new Dictionary<int, Color>();
                for (int yy = 0; yy < grid; yy++)
                    for (int xx = 0; xx < grid; xx++)
                    {
                        Color pc = sample.GetPixel(xx, yy);
                        if (pc.A < 200) continue;
                        int key = ((pc.R >> 4) << 8) | ((pc.G >> 4) << 4) | (pc.B >> 4);
                        int n;
                        counts.TryGetValue(key, out n);
                        counts[key] = n + 1;
                        sampleColor[key] = pc;
                    }
                int best = 0;
                foreach (var kv in counts) if (kv.Value > best) { best = kv.Value; paper = sampleColor[kv.Key]; }
            }
            for (int y = 0; y < grid; y++)
                for (int x = 0; x < grid; x++)
                {
                    Color c = sample != null ? sample.GetPixel(x, y) : ((x + y) % 3 == 0 ? Theme.Orange : Theme.Gold);
                    // fly only the motif (stripes, G, star); the paper colour of the tile fades in with the logo
                    if (c.A < 60 || Math.Abs(c.R - paper.R) + Math.Abs(c.G - paper.G) + Math.Abs(c.B - paper.B) < 60) continue;
                    var to = new PointF(logoRect.X + x * size / (float)grid, logoRect.Y + y * size / (float)grid);
                    double ang = rnd.NextDouble() * Math.PI * 2;
                    float dist = Ui.D(160) + (float)rnd.NextDouble() * Ui.D(160);
                    var from = new PointF(ClientSize.Width / 2f + (float)Math.Cos(ang) * dist, logoRect.Y + size / 2f + (float)Math.Sin(ang) * dist * 0.7f);
                    float centre = (float)Math.Sqrt(Math.Pow(x - grid / 2.0, 2) + Math.Pow(y - grid / 2.0, 2)) / grid;
                    particles.Add(new Particle { From = from, To = to, Color = Color.FromArgb(255, c), Delay = centre * 0.35f + (float)rnd.NextDouble() * 0.2f, Spin = (float)(rnd.NextDouble() - 0.5) * 2f });
                }
        }
        finally { if (sample != null) sample.Dispose(); }
    }

    private void OnTick()
    {
        if (fadeStart >= 0)
        {
            double t = (Elapsed - fadeStart) / 260.0;
            if (t >= 1) { timer.Stop(); Close(); return; }
            Opacity = Math.Max(0, 1 - t);
        }
        Invalidate();
    }

    // Loading the logo takes a moment; the animation starts when the window actually appears.
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        clock.Restart();
    }

    // Called when the main window is visible.
    public void FadeOut()
    {
        if (fadeStart < 0) fadeStart = Elapsed;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop();
        timer.Dispose();
        if (logo != null) logo.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Bg);
        double ms = Skipped ? AssembleMs + 400 : Elapsed;
        float p = (float)Math.Min(1.0, ms / AssembleMs);

        // backdrop: warm corner wash and a thin outline
        using (var wash = new LinearGradientBrush(ClientRectangle, Ui.Alpha(Theme.Orange, 40), Ui.Alpha(Theme.Bg, 0), 45f))
            g.FillRectangle(wash, ClientRectangle);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Shapes.StrokeRound(g, Ui.Alpha(Theme.Orange, 120), new RectangleF(0.5f, 0.5f, ClientSize.Width - 1.5f, ClientSize.Height - 1.5f), Ui.D(18), Math.Max(1f, Ui.Scale));

        // halo grows as the logo forms (dark theme only; the 70s look is flat)
        float halo = Ease.OutCubic(Math.Min(1f, p * 1.2f));
        if (halo > 0 && !Theme.IsLight)
            using (var path = new GraphicsPath())
            {
                var hr = RectangleF.Inflate(logoRect, logoRect.Width * 0.6f * halo, logoRect.Height * 0.5f * halo);
                path.AddEllipse(hr);
                using (var pg = new PathGradientBrush(path) { CenterColor = Ui.Alpha(Theme.Orange, (int)(70 * halo)), SurroundColors = new[] { Ui.Alpha(Theme.Orange, 0) } })
                    g.FillPath(pg, path);
            }

        if (Theme.IsLight)
        {
            // rainbow stripes run out sideways from behind the logo, like the icon
            int bandH = Ui.D(34);
            float gp = Ease.OutCubic(Math.Min(1f, p * 1.3f));
            int half = (int)(ClientSize.Width / 2 * gp);
            Shapes.Stripes(g, new Rectangle(ClientSize.Width / 2 - half, logoRect.Y + logoRect.Height / 2 - bandH / 2 + Ui.D(8), half * 2, bandH), (Elapsed / 2200.0) % 1.0);
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // flying pixels, then the crisp logo cross-fades in over them
        float settle = Math.Max(0f, Math.Min(1f, (p - 0.8f) / 0.2f));
        g.SmoothingMode = SmoothingMode.None;
        if (settle < 1f)
            foreach (var pt in particles)
            {
                float t = Math.Max(0f, Math.Min(1f, (p - pt.Delay) / Math.Max(0.2f, 0.8f - pt.Delay)));
                float e2 = Ease.OutCubic(t);
                float arc = (1f - e2) * pt.Spin * Ui.D(60);
                float x = pt.From.X + (pt.To.X - pt.From.X) * e2 + arc;
                float y = pt.From.Y + (pt.To.Y - pt.From.Y) * e2 - arc * 0.5f;
                int a = (int)(255 * Math.Min(1f, t * 3f) * (1f - settle));
                if (a <= 0) continue;
                int sz = t < 1f ? cell + (int)((1f - t) * cell) : cell;
                using (var b = new SolidBrush(Color.FromArgb(a, pt.Color))) g.FillRectangle(b, x, y, sz, sz);
            }
        if (settle > 0f && logo != null)
        {
            var attrs = new System.Drawing.Imaging.ImageAttributes();
            var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = settle };
            attrs.SetColorMatrix(cm);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var state = g.Save();
            using (var clip = Shapes.Round(logoRect, logoRect.Width * 0.224f)) g.SetClip(clip, CombineMode.Intersect);
            g.DrawImage(logo, logoRect, 0, 0, logo.Width, logo.Height, GraphicsUnit.Pixel, attrs);
            g.Restore(state);
            attrs.Dispose();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Shapes.StrokeRound(g, Ui.Alpha(Theme.Outline, (int)(255 * settle)), logoRect, logoRect.Width * 0.224f, Math.Max(2f, Ui.Scale * 1.5f));
        }

        // title letters rise in one by one
        string title = "Gestirn dev";
        var font = Theme.UiHero;
        int tw = TextRenderer.MeasureText(title, font, Size.Empty, TextFormatFlags.NoPadding).Width;
        int tx = (ClientSize.Width - tw) / 2, ty = logoRect.Bottom + Ui.D(18);
        int bar = TextRenderer.MeasureText("|", font, Size.Empty, TextFormatFlags.NoPadding).Width;
        for (int i = 0; i < title.Length; i++)
        {
            string ch = title[i].ToString();
            // place each letter where it falls in the whole word ("|" keeps trailing spaces measured)
            int cx = tx + (i == 0 ? 0 : TextRenderer.MeasureText(title.Substring(0, i) + "|", font, Size.Empty, TextFormatFlags.NoPadding).Width - bar);
            if (title[i] == ' ') continue;
            float lt = Math.Max(0f, Math.Min(1f, (p - 0.45f - i * 0.03f) / 0.25f));
            if (lt > 0)
            {
                int rise = (int)((1f - Ease.OutBack(lt)) * Ui.D(14));
                Color c = Ui.Lerp(Theme.Bg, i < 7 ? Theme.Text : Theme.Orange, lt);
                TextRenderer.DrawText(g, ch, font, new Point(cx, ty + rise), c, TextFormatFlags.NoPadding);
            }
        }
        float vt = Math.Max(0f, Math.Min(1f, (p - 0.8f) / 0.2f));
        if (vt > 0)
            Shapes.Text(g, L.T("Version ", "版本 ") + version + "  ·  C/C++ IDE", Theme.Ui9, new Rectangle(0, ty + Ui.D(46), ClientSize.Width, Ui.D(20)), Ui.Lerp(Theme.Bg, Theme.Muted, vt), Shapes.Center);

        // pixel loading strip
        int px = Math.Max(2, Ui.D(4)), gap = Math.Max(1, Ui.D(2)), count = 24;
        int total = count * (px + gap) - gap, sx = (ClientSize.Width - total) / 2, sy = ClientSize.Height - Ui.D(26);
        double run = Elapsed / 70.0;
        for (int i = 0; i < count; i++)
        {
            double wave = 0.5 + 0.5 * Math.Sin(run - i * 0.5);
            Color c = i < count * p ? Ui.Lerp(Theme.Orange, Theme.Gold, (float)wave) : Ui.Alpha(Theme.Edge, 160);
            using (var b = new SolidBrush(c)) g.FillRectangle(b, sx + i * (px + gap), sy, px, px);
        }
    }
}
}
