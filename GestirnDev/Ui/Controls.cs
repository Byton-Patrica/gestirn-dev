using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GestirnDev
{
internal static class L
{
    public static bool Zh;
    public static string T(string en, string zh) { return Zh ? zh : en; }
}

internal static class Native
{
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    // WinForms "parks" a control whose window is created before its parent's; a parked control that is
    // hidden when the form first shows is never re-attached. Create every handle in tree order and
    // re-attach anything parked, so controls shown later (debug toolbar, panels, restored tabs) appear.
    public static void AttachHandles(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (!child.IsHandleCreated)
            {
                var unused = child.Handle;
            }
            else if (GetParent(child.Handle) != parent.Handle && !(child is Form))
            {
                SetParent(child.Handle, parent.Handle);
                child.Bounds = child.Bounds;
            }
            AttachHandles(child);
        }
    }
    [DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
    [DllImport("imm32.dll")] public static extern IntPtr ImmGetContext(IntPtr hWnd);
    [DllImport("imm32.dll")] public static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
    [DllImport("imm32.dll")] public static extern bool ImmSetCompositionWindow(IntPtr hIMC, ref CompositionForm form);
    [DllImport("imm32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern bool ImmSetCompositionFontW(IntPtr hIMC, [In] LogFont font);

    [StructLayout(LayoutKind.Sequential)]
    public struct CompositionForm { public int Style; public Point Position; public Rectangle Area; }

    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public class LogFont
    {
        public int Height, Width, Escapement, Orientation, Weight;
        public byte Italic, Underline, StrikeOut, FontCharSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FaceName;
    }

    private static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

    // Dark, ink-coloured native title bar with Windows 11 rounded corners.
    public static void StyleWindow(Form form)
    {
        try
        {
            int on = Theme.IsLight ? 0 : 1;
            if (DwmSetWindowAttribute(form.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(form.Handle, 19, ref on, 4);
            int caption = ColorRef(Theme.Bg);
            DwmSetWindowAttribute(form.Handle, 35, ref caption, 4);
            int text = ColorRef(Theme.TextDim);
            DwmSetWindowAttribute(form.Handle, 36, ref text, 4);
            int round = 2;
            DwmSetWindowAttribute(form.Handle, 33, ref round, 4);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    // Dark native scrollbars (Windows 10 1809+); harmless elsewhere.
    public static void DarkScrollbars(Control c)
    {
        Action apply = () => { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { } };
        if (c.IsHandleCreated) apply(); else c.HandleCreated += (_, _) => apply();
    }

    public static void CueBanner(TextBox box, string text)
    {
        if (box.IsHandleCreated) SendMessage(box.Handle, 0x1501, new IntPtr(1), text);
        else box.HandleCreated += (_, _) => SendMessage(box.Handle, 0x1501, new IntPtr(1), text);
    }
}

// Rounded panel; corners show the parent's colour so the card floats on it.
internal class Card : Panel
{
    private Color fill = Theme.Panel;

    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Outline = Theme.Edge;
        Radius = 12;
        BackColor = fill;
    }

    public Color Fill { get { return fill; } set { fill = value; BackColor = value; Invalidate(); } }
    public Color Outline { get; set; }
    public float Radius { get; set; }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var r = new RectangleF(0, 0, Width, Height);
        Shapes.FillRound(e.Graphics, fill, r, Ui.D(Radius));
        if (Outline.A > 0) Shapes.StrokeRound(e.Graphics, Outline, r, Ui.D(Radius), Math.Max(1f, Ui.Scale * 0.75f));
        base.OnPaint(e);
    }
}

internal enum ButtonKind { Secondary, Primary, Ghost, Danger, Tint }
internal enum ButtonSides { All, Left, Right }

// Rounded button with a pixel glyph. Hover and press glide instead of snapping.
internal sealed class PixelButton : Control
{
    private readonly AnimatedFloat hover = new AnimatedFloat(0f);
    private readonly AnimatedFloat press = new AnimatedFloat(0f);
    private readonly AnimatedFloat ripple = new AnimatedFloat(1f);
    private Point rippleAt;
    private bool busy;
    private double busyPhase;
    private readonly Action<double> busyTicker;

    public PixelButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        Cursor = Cursors.Hand;
        Font = Theme.UiSemi;
        Radius = 9;
        hover.Changed += Invalidate;
        press.Changed += Invalidate;
        ripple.Changed += Invalidate;
        busyTicker = now => { busyPhase = now / 700.0; Invalidate(); };
    }

    public string Glyph { get; set; }
    public ButtonKind Kind { get; set; }
    public float Radius { get; set; }
    public ButtonSides Sides { get; set; }
    public bool Active { get; set; }
    public string Tip { get; set; }

    public bool Busy
    {
        get { return busy; }
        set
        {
            if (busy == value) return;
            busy = value;
            if (busy) Animator.AddTicker(busyTicker); else Animator.RemoveTicker(busyTicker);
            Invalidate();
        }
    }

    public int PreferredWidth()
    {
        int glyph = Glyph != null ? Ui.D(14) : 0;
        int text = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        int gap = glyph > 0 && text > 0 ? Ui.D(8) : 0;
        if (text == 0) return Math.Max(Height, glyph + Ui.D(16));
        return glyph + gap + text + Ui.D(24);
    }

    private GraphicsPath BodyPath(RectangleF rect)
    {
        return Sides == ButtonSides.All ? Shapes.Round(rect, Ui.D(Radius)) : Shapes.RoundSides(rect, Ui.D(Radius), Sides == ButtonSides.Left, Sides == ButtonSides.Right);
    }

    protected override void OnMouseEnter(EventArgs e) { hover.To(1f, 120); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover.To(0f, 220); press.To(0f, 120); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            press.To(1f, 60);
            if (!Animator.Reduced) { rippleAt = e.Location; ripple.Snap(0f); ripple.To(1f, 480); }
        }
        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e) { press.To(0f, 160); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Animator.RemoveTicker(busyTicker);
        base.Dispose(disposing);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        float h = hover.Value, p = press.Value;
        Color fill, edge, ink, glyphColor;
        if (!Enabled)
        {
            fill = Kind == ButtonKind.Ghost ? Color.Transparent : Theme.PanelHi;
            edge = Kind == ButtonKind.Secondary ? Theme.EdgeSoft : Color.Transparent;
            ink = glyphColor = Theme.Faint;
        }
        else switch (Kind)
        {
            case ButtonKind.Primary:
                fill = Ui.Lerp(Theme.Orange, Theme.OrangeHot, h);
                edge = Theme.IsLight ? Theme.Outline : Color.Transparent;
                ink = glyphColor = Theme.Ink;
                break;
            case ButtonKind.Tint:
                // the "trial" half of the run button: a pale wash of the brand colour
                fill = Ui.Lerp(Ui.Lerp(Theme.Panel, Theme.Orange, Theme.IsLight ? 0.12f : 0.18f), Ui.Lerp(Theme.Panel, Theme.Orange, Theme.IsLight ? 0.26f : 0.32f), h);
                edge = Theme.IsLight ? Theme.Outline : Ui.Alpha(Theme.Orange, 200);
                ink = Theme.Text;
                glyphColor = Ui.Lerp(Theme.Orange, Theme.OrangeHot, h);
                break;
            case ButtonKind.Danger:
                fill = Ui.Lerp(Theme.Raised, Ui.Alpha(Theme.Red, 70), h);
                edge = Ui.Lerp(Theme.Edge, Theme.Red, h);
                ink = Theme.Text;
                glyphColor = Theme.Red;
                break;
            case ButtonKind.Ghost:
                fill = Active ? Ui.Lerp(Theme.Raised, Theme.Hover, h) : Ui.Alpha(Theme.Hover, (int)(255 * h));
                edge = Color.Transparent;
                ink = Ui.Lerp(Active ? Theme.Text : Theme.TextDim, Theme.Text, h);
                glyphColor = Active ? Theme.Orange : Ui.Lerp(Theme.TextDim, Theme.Text, h);
                break;
            default:
                fill = Ui.Lerp(Theme.Raised, Theme.Hover, h);
                edge = Ui.Lerp(Theme.Edge, Ui.Alpha(Theme.Orange, 170), h);
                ink = Theme.Text;
                glyphColor = Ui.Lerp(Theme.Orange, Theme.OrangeHot, h);
                break;
        }

        float inset = p * Ui.Scale * 1.2f;
        var rect = new RectangleF(inset, inset, Width - inset * 2, Height - inset * 2);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var body = BodyPath(rect))
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, body);
        if (edge.A > 0)
        {
            float sw = Math.Max(1f, Ui.Scale * 0.75f);
            using (var body = BodyPath(new RectangleF(rect.X + sw / 2, rect.Y + sw / 2, rect.Width - sw, rect.Height - sw)))
            using (var pen = new Pen(edge, sw)) g.DrawPath(pen, body);
        }
        if (ripple.Value < 1f && Enabled)
        {
            float r = ripple.Value * Math.Max(Width, Height) * 1.4f;
            var state = g.Save();
            using (var clip = BodyPath(rect)) g.SetClip(clip);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rc = Kind == ButtonKind.Primary ? Theme.Ink : Theme.Orange;
            using (var b = new SolidBrush(Ui.Alpha(rc, (int)(90 * (1f - ripple.Value))))) g.FillEllipse(b, rippleAt.X - r, rippleAt.Y - r, r * 2, r * 2);
            g.Restore(state);
        }
        if (busy && Enabled && !Animator.Reduced)
        {
            // breathing outline plus a light band that sweeps across while the work runs
            double s = 0.5 + 0.5 * Math.Sin(busyPhase * Math.PI * 1.4);
            var ringColor = Kind == ButtonKind.Primary ? Theme.Gold : Theme.Orange;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Shapes.StrokeRound(g, Ui.Alpha(ringColor, (int)(80 + 150 * s)), new RectangleF(rect.X + 0.75f, rect.Y + 0.75f, rect.Width - 1.5f, rect.Height - 1.5f), Ui.D(Radius), Math.Max(1.5f, Ui.Scale * 1.3f));
            float sweep = (float)((busyPhase * 0.6) % 1.0);
            float band = Math.Max(Ui.D(26), Width * 0.35f);
            float sx = -band + sweep * (Width + band * 2);
            var state = g.Save();
            using (var clip = BodyPath(rect)) g.SetClip(clip);
            using (var lg = new LinearGradientBrush(new RectangleF(sx - band / 2, 0, band, Height), Color.White, Color.White, 15f))
            {
                var tint = Kind == ButtonKind.Primary ? Color.White : Theme.Orange;
                lg.InterpolationColors = new ColorBlend(3)
                {
                    Colors = new[] { Ui.Alpha(tint, 0), Ui.Alpha(tint, Kind == ButtonKind.Primary ? 95 : 55), Ui.Alpha(tint, 0) },
                    Positions = new[] { 0f, 0.5f, 1f },
                };
                g.FillRectangle(lg, sx - band / 2, 0, band, Height);
            }
            g.Restore(state);
        }

        int glyphSize = Ui.D(14);
        bool hasGlyph = Glyph != null || busy;
        int textWidth = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        int gap = hasGlyph && textWidth > 0 ? Ui.D(8) : 0;
        int total = (hasGlyph ? glyphSize : 0) + gap + textWidth;
        int x = (Width - total) / 2;
        if (hasGlyph)
        {
            var box = new Rectangle(x, (Height - glyphSize) / 2, glyphSize, glyphSize);
            if (busy) Glyphs.Spinner(g, box, busyPhase, Kind == ButtonKind.Primary ? Theme.Ink : Theme.Lime, Kind == ButtonKind.Primary ? Theme.Ink : Theme.Orange);
            else Glyphs.Draw(g, Glyph, box, glyphColor);
            x += glyphSize + gap;
        }
        if (textWidth > 0)
            Shapes.Text(g, Text, Font, new Rectangle(x, 0, textWidth + Ui.D(2), Height), ink, Shapes.Left);
    }
}

// Dark drop-down: a rounded field that opens a themed menu of choices.
internal sealed class ChoiceBox : Control
{
    private readonly AnimatedFloat hover = new AnimatedFloat(0f);
    private readonly string[] labels;
    private int index;
    public event EventHandler SelectionChanged;

    public ChoiceBox(string[] labels, int selected)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        this.labels = labels;
        index = Math.Max(0, Math.Min(labels.Length - 1, selected));
        Size = Ui.D(220, 30);
        Cursor = Cursors.Hand;
        hover.Changed += Invalidate;
    }

    public int SelectedIndex { get { return index; } }

    protected override void OnMouseEnter(EventArgs e) { hover.To(1f, 120); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover.To(0f, 220); base.OnMouseLeave(e); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), Font = Theme.Ui9, BackColor = Theme.PanelHi, ShowImageMargin = false, ShowCheckMargin = true };
        for (int i = 0; i < labels.Length; i++)
        {
            int k = i;
            var item = new ToolStripMenuItem(labels[i]) { Checked = i == index, ForeColor = Theme.Text, Padding = Ui.Pad(2, 3, 2, 3) };
            item.Click += (_, _) => { index = k; Invalidate(); if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty); };
            menu.Items.Add(item);
        }
        menu.MinimumSize = new Size(Width, 0);
        menu.Show(this, new Point(0, Height + Ui.D(2)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : Theme.Panel);
        var r = new RectangleF(0, 0, Width, Height);
        Shapes.FillRound(g, Ui.Lerp(Theme.Raised, Theme.Hover, hover.Value), r, Ui.D(8));
        Shapes.StrokeRound(g, Ui.Lerp(Theme.Edge, Ui.Alpha(Theme.Orange, 170), hover.Value), r, Ui.D(8), Math.Max(1f, Ui.Scale * 0.75f));
        Shapes.Text(g, labels[index], Theme.Ui9, new Rectangle(Ui.D(12), 0, Width - Ui.D(40), Height), Theme.Text, Shapes.Left);
        Glyphs.Draw(g, "chevron-down", new Rectangle(Width - Ui.D(24), (Height - Ui.D(10)) / 2, Ui.D(10), Ui.D(10)), Theme.TextDim);
    }
}

// Animated on/off switch.
internal sealed class ToggleSwitch : Control
{
    private readonly AnimatedFloat knob = new AnimatedFloat(0f);
    private bool on;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = Ui.D(40, 22);
        Cursor = Cursors.Hand;
        knob.Changed += Invalidate;
    }

    public event EventHandler CheckedChanged;

    public bool Checked
    {
        get { return on; }
        set
        {
            if (on == value) return;
            on = value;
            if (IsHandleCreated && Visible) knob.To(on ? 1f : 0f, 180, Ease.OutBack); else knob.Snap(on ? 1f : 0f);
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
    protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg); }

    protected override void OnPaint(PaintEventArgs e)
    {
        float t = Math.Max(0f, Math.Min(1f, knob.Value));
        var track = new RectangleF(0, 0, Width, Height);
        Shapes.FillRound(e.Graphics, Ui.Lerp(Theme.Raised, Theme.Orange, t), track, Height / 2f);
        Shapes.StrokeRound(e.Graphics, Ui.Lerp(Theme.Edge, Theme.Orange, t), track, Height / 2f, Math.Max(1f, Ui.Scale * 0.75f));
        float d = Height - Ui.D(6);
        float x = Ui.D(3) + (Width - d - Ui.D(6)) * knob.Value;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Ui.Lerp(Theme.TextDim, Theme.Ink, t))) e.Graphics.FillEllipse(b, x, Ui.D(3), d, d);
    }
}

// Rounded single-line input: a borderless TextBox inside a card whose outline lights up on focus.
internal sealed class RoundInput : Card
{
    private readonly AnimatedFloat focus = new AnimatedFloat(0f);
    public readonly TextBox Box = new TextBox();

    public RoundInput(string placeholder)
    {
        Fill = Theme.Raised;
        Radius = 8;
        Box.BorderStyle = BorderStyle.None;
        Box.BackColor = Theme.Raised;
        Box.ForeColor = Theme.Text;
        Box.Font = Theme.Ui9;
        Controls.Add(Box);
        Height = Ui.D(30);
        Native.CueBanner(Box, placeholder);
        focus.Changed += () => { Outline = Ui.Lerp(Theme.Edge, Theme.Orange, focus.Value); Invalidate(); };
        Box.GotFocus += (_, _) => focus.To(1f, 150);
        Box.LostFocus += (_, _) => focus.To(0f, 200);
        Click += (_, _) => Box.Focus();
    }

    public override string Text { get { return Box.Text; } set { Box.Text = value; } }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int pad = Ui.D(10);
        Box.SetBounds(pad, (Height - Box.Height) / 2, Math.Max(10, Width - pad * 2), Box.Height);
    }
}

// Thin draggable strip between layout regions; glows on hover.
internal sealed class DragHandle : Control
{
    private readonly AnimatedFloat hover = new AnimatedFloat(0f);
    private Point last;
    private bool dragging;

    public DragHandle(bool vertical)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Vertical = vertical;
        Cursor = vertical ? Cursors.SizeWE : Cursors.SizeNS;
        hover.Changed += Invalidate;
    }

    public bool Vertical { get; private set; }
    public event Action<int> Dragged;
    public event Action DragEnded;

    protected override void OnMouseEnter(EventArgs e) { hover.To(1f, 150); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { if (!dragging) hover.To(0f, 250); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { dragging = true; last = Cursor.Position; Capture = true; base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        dragging = false; Capture = false; hover.To(0f, 250);
        if (DragEnded != null) DragEnded();
        base.OnMouseUp(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging)
        {
            var now = Cursor.Position;
            int delta = Vertical ? now.X - last.X : now.Y - last.Y;
            last = now;
            if (delta != 0 && Dragged != null) Dragged(delta);
        }
        base.OnMouseMove(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
        if (hover.Value <= 0.01f) return;
        int w = Math.Max(1, Ui.D(2));
        var line = Vertical ? new Rectangle((Width - w) / 2, 0, w, Height) : new Rectangle(0, (Height - w) / 2, Width, w);
        using (var b = new SolidBrush(Ui.Alpha(Theme.Orange, (int)(200 * hover.Value)))) e.Graphics.FillRectangle(b, line);
    }
}

internal enum ToastKind { Info, Success, Warning, Error }

// Bottom-right notifications that slide in, stack, and slide away.
internal sealed class ToastHost
{
    private readonly Control host;
    private readonly List<Toast> toasts = new List<Toast>();
    public int BottomInset;

    public ToastHost(Control host)
    {
        this.host = host;
        host.Resize += (_, _) => Arrange();
    }

    public void Show(string title, string message, ToastKind kind, int milliseconds = 3200)
    {
        var toast = new Toast(title, message, kind);
        toasts.Add(toast);
        host.Controls.Add(toast);
        toast.BringToFront();
        toast.Slide.Changed += Arrange;
        toast.Slide.To(1f, 280, Ease.OutBack);
        toast.Click += (_, _) => Dismiss(toast);
        var life = new Timer { Interval = milliseconds };
        life.Tick += (_, _) => { life.Stop(); life.Dispose(); Dismiss(toast); };
        life.Start();
        if (toasts.Count > 4) Dismiss(toasts[0]);
        Arrange();
    }

    private void Dismiss(Toast toast)
    {
        if (toast.Closing) return;
        toast.Closing = true;
        toast.Slide.Finished += () =>
        {
            toasts.Remove(toast);
            host.Controls.Remove(toast);
            toast.Dispose();
            Arrange();
        };
        toast.Slide.To(0f, 220, Ease.InOutCubic);
    }

    private void Arrange()
    {
        int y = host.ClientSize.Height - BottomInset - Ui.D(14);
        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            var toast = toasts[i];
            if (toast.IsDisposed) continue;
            int offset = (int)((1f - toast.Slide.Value) * (toast.Width + Ui.D(30)));
            y -= toast.Height;
            toast.Location = new Point(host.ClientSize.Width - toast.Width - Ui.D(16) + offset, y);
            y -= Ui.D(10);
        }
    }

    private sealed class Toast : Control
    {
        public readonly AnimatedFloat Slide = new AnimatedFloat(0f);
        public bool Closing;
        private readonly string title, message;
        private readonly ToastKind kind;

        public Toast(string title, string message, ToastKind kind)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            this.title = title; this.message = message; this.kind = kind;
            Cursor = Cursors.Hand;
            int width = Ui.D(340);
            int msgHeight = string.IsNullOrEmpty(message) ? 0
                : TextRenderer.MeasureText(message, Theme.Ui9, new Size(width - Ui.D(62), 1000), TextFormatFlags.WordBreak).Height;
            Size = new Size(width, Ui.D(30) + Math.Min(msgHeight, Ui.D(60)) + Ui.D(12));
        }

        private Color Accent
        {
            get
            {
                switch (kind)
                {
                    case ToastKind.Success: return Theme.Lime;
                    case ToastKind.Warning: return Theme.Gold;
                    case ToastKind.Error: return Theme.Red;
                    default: return Theme.Orange;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            var r = new RectangleF(0, 0, Width, Height);
            Shapes.FillRound(g, Theme.Raised, r, Ui.D(12));
            Shapes.StrokeRound(g, Ui.Alpha(Accent, 140), r, Ui.D(12), Math.Max(1f, Ui.Scale));
            Shapes.FillRound(g, Accent, new RectangleF(Ui.D(6), Ui.D(10), Ui.D(3), Height - Ui.D(20)), Ui.D(2));
            string glyph = kind == ToastKind.Success ? "check" : kind == ToastKind.Error ? "error" : kind == ToastKind.Warning ? "warning" : "info";
            Glyphs.Draw(g, glyph, new Rectangle(Ui.D(18), Ui.D(12), Ui.D(14), Ui.D(14)), Accent);
            Shapes.Text(g, title, Theme.UiSemi, new Rectangle(Ui.D(42), Ui.D(8), Width - Ui.D(52), Ui.D(22)), Theme.Text, Shapes.Left);
            if (!string.IsNullOrEmpty(message))
                TextRenderer.DrawText(g, message, Theme.Ui9, new Rectangle(Ui.D(42), Ui.D(30), Width - Ui.D(54), Height - Ui.D(36)), Theme.TextDim,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }
}

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkMenuColors()) { RoundedEdges = false; }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var r = new Rectangle(Ui.D(3), 1, e.Item.Width - Ui.D(6), e.Item.Height - 2);
        bool top = e.Item.Owner is MenuStrip;
        Shapes.FillRound(e.Graphics, top ? Theme.Raised : Theme.Hover, r, Ui.D(6));
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? Theme.Faint : (e.Item.Selected ? Theme.Text : (e.Item.Owner is MenuStrip ? Theme.TextDim : Theme.Text));
        if (e.Item is ToolStripMenuItem && !(e.Item.Owner is MenuStrip) && e.Text == ((ToolStripMenuItem)e.Item).ShortcutKeyDisplayString)
            e.TextColor = Theme.Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Muted;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using (var pen = new Pen(Theme.Edge)) e.Graphics.DrawLine(pen, Ui.D(10), y, e.Item.Width - Ui.D(10), y);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is MenuStrip) return;
        using (var pen = new Pen(Theme.Edge)) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        Glyphs.Draw(e.Graphics, "check", r, Theme.Orange);
    }
}

internal sealed class DarkMenuColors : ProfessionalColorTable
{
    public override Color MenuBorder { get { return Theme.Edge; } }
    public override Color MenuItemBorder { get { return Color.Transparent; } }
    public override Color MenuItemSelected { get { return Theme.Hover; } }
    public override Color MenuItemSelectedGradientBegin { get { return Theme.Raised; } }
    public override Color MenuItemSelectedGradientEnd { get { return Theme.Raised; } }
    public override Color MenuItemPressedGradientBegin { get { return Theme.Raised; } }
    public override Color MenuItemPressedGradientEnd { get { return Theme.Raised; } }
    public override Color MenuStripGradientBegin { get { return Theme.Bg; } }
    public override Color MenuStripGradientEnd { get { return Theme.Bg; } }
    public override Color ToolStripDropDownBackground { get { return Theme.PanelHi; } }
    public override Color ImageMarginGradientBegin { get { return Theme.PanelHi; } }
    public override Color ImageMarginGradientMiddle { get { return Theme.PanelHi; } }
    public override Color ImageMarginGradientEnd { get { return Theme.PanelHi; } }
    public override Color SeparatorDark { get { return Theme.Edge; } }
    public override Color SeparatorLight { get { return Theme.Edge; } }
    public override Color CheckBackground { get { return Color.Transparent; } }
    public override Color CheckSelectedBackground { get { return Color.Transparent; } }
    public override Color CheckPressedBackground { get { return Color.Transparent; } }
}
}
