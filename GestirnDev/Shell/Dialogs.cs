using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GestirnDev
{
internal class DarkDialog : Form
{
    public DarkDialog()
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Panel;
        ForeColor = Theme.Text;
        Font = Theme.Ui9;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.StyleWindow(this);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; e.Handled = true; }
    }

    protected PixelButton AddButton(string text, ButtonKind kind, int x, int y, int w)
    {
        var b = new PixelButton { Text = text, Kind = kind, Bounds = new Rectangle(x, y, w, Ui.D(34)) };
        Controls.Add(b);
        return b;
    }

    protected Label AddLabel(string text, int x, int y, int w, Font font = null, Color? color = null)
    {
        var l = new Label { Text = text, Location = new Point(x, y), Size = new Size(w, Ui.D(20)), ForeColor = color ?? Theme.TextDim, BackColor = BackColor, Font = font ?? Theme.Ui9, AutoEllipsis = true };
        Controls.Add(l);
        return l;
    }
}

internal static class Prompt
{
    public static string Show(IWin32Window owner, string title, string label, string initial)
    {
        using (var form = new DarkDialog { Text = title, ClientSize = Ui.D(460, 150) })
        {
            var caption = new Label { Text = label, Location = new Point(Ui.D(20), Ui.D(18)), AutoSize = true, ForeColor = Theme.TextDim, BackColor = Theme.Panel };
            var input = new RoundInput("") { Bounds = new Rectangle(Ui.D(20), Ui.D(44), Ui.D(420), Ui.D(34)) };
            input.Text = initial ?? "";
            var ok = new PixelButton { Text = L.T("OK", "确定"), Kind = ButtonKind.Primary, Bounds = new Rectangle(Ui.D(232), Ui.D(98), Ui.D(100), Ui.D(34)) };
            var cancel = new PixelButton { Text = L.T("Cancel", "取消"), Bounds = new Rectangle(Ui.D(340), Ui.D(98), Ui.D(100), Ui.D(34)) };
            form.Controls.AddRange(new Control[] { caption, input, ok, cancel });
            ok.Click += (_, _) => form.DialogResult = DialogResult.OK;
            cancel.Click += (_, _) => form.DialogResult = DialogResult.Cancel;
            input.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; form.DialogResult = DialogResult.OK; } };
            form.Shown += (_, _) =>
            {
                input.Box.Focus();
                int dot = input.Text.LastIndexOf('.');
                if (dot > 0) input.Box.Select(0, dot); else input.Box.SelectAll();
            };
            return form.ShowDialog(owner) == DialogResult.OK ? input.Text : null;
        }
    }
}

// Left-hand category list of the Settings dialog.
internal sealed class SettingsNav : Control
{
    public sealed class Item { public string Id, Glyph, Title, Badge; }

    private readonly List<Item> items = new List<Item>();
    private readonly AnimatedFloat indicator = new AnimatedFloat(0f);
    private int hover = -1, selected;
    public event Action<string> Selected;

    public SettingsNav()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
        Cursor = Cursors.Hand;
        indicator.Changed += Invalidate;
    }

    private int Slot { get { return Ui.D(42); } }
    private float SlotY(int i) { return Ui.D(16) + i * Slot; }

    public void Add(string id, string glyph, string title, string badge = null) { items.Add(new Item { Id = id, Glyph = glyph, Title = title, Badge = badge }); }

    public void Select(string id, bool animate)
    {
        int i = items.FindIndex(t => t.Id == id);
        if (i < 0) return;
        selected = i;
        if (animate) indicator.To(SlotY(i), 240, Ease.OutBack); else indicator.Snap(SlotY(i));
        Invalidate();
        if (Selected != null) Selected(id);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = (int)((e.Y - Ui.D(16)) / (float)Slot);
        if (e.Y < Ui.D(16) || h < 0 || h >= items.Count) h = -1;
        if (h != hover) { hover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (hover >= 0) Select(items[hover].Id, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Bg);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        for (int i = 0; i < items.Count; i++)
        {
            var box = new RectangleF(Ui.D(10), SlotY(i) + Ui.D(2), Width - Ui.D(20), Slot - Ui.D(4));
            bool on = i == selected;
            if (i == selected) Shapes.FillRound(g, Theme.Raised, new RectangleF(box.X, indicator.Value + Ui.D(2), box.Width, box.Height), Ui.D(9));
            else if (i == hover) Shapes.FillRound(g, Ui.Alpha(Theme.Hover, 150), box, Ui.D(9));
            if (i == selected)
                Shapes.FillRound(g, Theme.Orange, new RectangleF(Ui.D(3), indicator.Value + Ui.D(10), Ui.D(3), Slot - Ui.D(20)), Ui.D(2));
            Glyphs.Draw(g, items[i].Glyph, new Rectangle(Ui.D(24), (int)SlotY(i) + (Slot - Ui.D(16)) / 2, Ui.D(16), Ui.D(16)), on ? Theme.Orange : i == hover ? Theme.Text : Theme.Muted);
            int tw = TextRenderer.MeasureText(items[i].Title, Theme.Ui10, Size.Empty, TextFormatFlags.NoPadding).Width;
            Shapes.Text(g, items[i].Title, Theme.Ui10, new Rectangle(Ui.D(50), (int)SlotY(i), Width - Ui.D(60), Slot), on ? Theme.Text : Theme.TextDim, Shapes.Left);
            if (items[i].Badge != null)
            {
                var pill = new Rectangle(Ui.D(50) + tw + Ui.D(8), (int)SlotY(i) + (Slot - Ui.D(16)) / 2, TextRenderer.MeasureText(items[i].Badge, Theme.UiSmall, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.D(12), Ui.D(16));
                Shapes.FillRound(g, Ui.Alpha(Theme.Violet, 55), pill, Ui.D(8));
                Shapes.Text(g, items[i].Badge, Theme.UiSmall, pill, Theme.Violet, Shapes.Center);
            }
        }
        using (var pen = new Pen(Theme.EdgeSoft)) g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
    }
}

// Settings: categories on the left, the chosen category's options on the right.
internal sealed class SettingsDialog : DarkDialog
{
    private readonly SettingsNav nav = new SettingsNav();
    private readonly Panel pageHost = new Panel();
    private readonly Dictionary<string, Panel> pages = new Dictionary<string, Panel>();
    private readonly Label pageTitle = new Label(), pageSub = new Label();
    private readonly Dictionary<string, string[]> pageText = new Dictionary<string, string[]>();
    private readonly AnimatedFloat slide = new AnimatedFloat(1f);
    private Panel body;
    private int y;
    private string current;
    private readonly List<Action> apply = new List<Action>();
    private readonly AppSettings settings;
    private readonly Func<AppSettings, System.Threading.Tasks.Task<string>> aiTest;

    public SettingsDialog(AppSettings settings, Func<System.Threading.Tasks.Task<string>> checkNow = null,
                          string startPage = null, Func<AppSettings, System.Threading.Tasks.Task<string>> aiTest = null)
    {
        this.settings = settings;
        this.aiTest = aiTest;
        Text = L.T("Settings", "设置");
        ClientSize = Ui.D(880, 640);
        int navW = Ui.D(210), footerH = Ui.D(64), headerH = Ui.D(78);

        nav.SetBounds(0, 0, navW, ClientSize.Height - footerH);
        Controls.Add(nav);

        pageTitle.SetBounds(navW + Ui.D(28), Ui.D(16), ClientSize.Width - navW - Ui.D(56), Ui.D(34));
        pageTitle.Font = Theme.UiTitle; pageTitle.ForeColor = Theme.Text; pageTitle.BackColor = Theme.Panel;
        pageSub.SetBounds(navW + Ui.D(28), Ui.D(50), ClientSize.Width - navW - Ui.D(56), Ui.D(22));
        pageSub.Font = Theme.Ui9; pageSub.ForeColor = Theme.Muted; pageSub.BackColor = Theme.Panel;
        pageHost.SetBounds(navW, headerH, ClientSize.Width - navW, ClientSize.Height - headerH - footerH);
        pageHost.BackColor = Theme.Panel;
        Controls.Add(pageTitle); Controls.Add(pageSub); Controls.Add(pageHost);

        // ---- Editor
        Page("editor", "format", L.T("Editor", "编辑器"), L.T("How the code looks and how typing feels.", "代码的显示方式和输入手感。"));
        Section(L.T("Text", "文字"));
        Number(L.T("Font size", "字号"), (int)Math.Round(settings.FontSize), 8, 28, v => settings.FontSize = v);
        Choice(L.T("Tab size", "缩进宽度"), new[] { "2", "4", "8" }, new[] { "2", "4", "8" }, settings.TabSize.ToString(), v => settings.TabSize = int.Parse(v));
        Toggle(L.T("Indent with spaces", "用空格缩进"), L.T("Tab inserts spaces instead of a tab character.", "Tab 键插入空格而不是制表符。"), settings.InsertSpaces, v => settings.InsertSpaces = v);
        Section(L.T("While you type", "输入时"));
        Toggle(L.T("Auto-close brackets and quotes", "自动补全括号和引号"), null, settings.AutoPairs, v => settings.AutoPairs = v);
        Toggle(L.T("Suggestions while typing", "输入时自动提示"), L.T("Keywords, library names, snippets and words from your files.", "关键字、库函数、代码片段和文件中的标识符。"), settings.AutoComplete, v => settings.AutoComplete = v);

        // ---- Files
        Page("files", "folder", L.T("Files", "文件"), L.T("Saving and restoring your work.", "保存与恢复你的工作。"));
        Toggle(L.T("Auto save", "自动保存"), L.T("Saves files a moment after you stop typing and when the window loses focus.", "停止输入片刻后、窗口失去焦点时自动保存。"), settings.AutoSave, v => settings.AutoSave = v);
        Toggle(L.T("Format on save", "保存时格式化"), L.T("Runs AStyle (K&R style) when a file is saved by hand.", "手动保存时用 AStyle（K&R 风格）格式化。"), settings.FormatOnSave, v => settings.FormatOnSave = v);
        Toggle(L.T("Restore open files on start", "启动时恢复上次打开的文件"), null, settings.RestoreSession, v => settings.RestoreSession = v);

        // ---- Compiler
        Page("compiler", "build", L.T("Compiler", "编译器"), L.T("Which g++ builds your code and with which options.", "用哪个 g++、以什么选项编译。"));
        Text2(L.T("g++ path", "g++ 路径"), settings.CompilerPath, L.T("Leave empty to detect automatically", "留空则自动检测"), v => settings.CompilerPath = v, true);
        Choice(L.T("C++ standard", "C++ 标准"), new[] { L.T("Auto (newest supported)", "自动（编译器支持的最新）"), "C++11", "C++14", "C++17", "C++20" },
            new[] { "auto", "c++11", "c++14", "c++17", "c++20" }, settings.Standard, v => settings.Standard = v);
        Choice(L.T("Optimization", "优化级别"), new[] { L.T("None (best for debugging)", "无（便于调试）"), "-O1", "-O2", "-O3", "-Os" },
            new[] { "", "-O1", "-O2", "-O3", "-Os" }, settings.Optimization, v => settings.Optimization = v);
        Text2(L.T("Extra flags", "额外编译选项"), settings.ExtraFlags, "-Wall", v => settings.ExtraFlags = v, false);
        Toggle(L.T("Link statically", "静态链接"), L.T("Adds -static so the .exe runs on other PCs without MinGW DLLs.", "加 -static，生成的 exe 在别的电脑上无需 MinGW 运行库。"), settings.StaticLink, v => settings.StaticLink = v);
        Choice(L.T("Program text encoding", "程序输出编码"), new[] { L.T("Auto (GBK on Chinese Windows)", "自动（中文系统用 GBK）"), "GBK", "UTF-8" },
            new[] { "auto", "gbk", "utf8" }, settings.ExecCharset, v => settings.ExecCharset = v);

        // ---- Learning aids
        Page("learn", "check", L.T("Learning aids", "学习辅助"), L.T("Friendly help for beginners.", "给初学者的贴心帮助。"));
        Toggle(L.T("Simple error translation", "编译简洁翻译"),
            L.T("Explains compiler errors in plain words, with a tip on how to fix them. Great for beginners.", "把编译报错翻译成通俗易懂的说法，并告诉你怎么改，适合初学者。"),
            settings.SimpleErrors, v => settings.SimpleErrors = v);
        Toggle(L.T("Reward mode", "奖励模式"),
            L.T("An encouraging line each time you run your code, and a bigger cheer when it finally works after errors.", "每次运行代码都会鼓励你一句；出错后终于成功时，会有更热烈的夸奖。"),
            settings.RewardMode, v => settings.RewardMode = v);

        // ---- AI fix
        Page("ai", "spark", L.T("AI fix", "AI 纠错"), L.T("Let an AI model suggest fixes when your code doesn't compile.", "代码编译不过时，让 AI 模型帮你找出问题并修改。"), L.T("Experimental", "实验性"));
        Toggle(L.T("Enable AI fix", "启用 AI 纠错"), L.T("Adds an \"AI fix\" button to the status bar when there are errors.", "有报错时，在底部状态栏出现“AI 纠错”按钮。"), settings.AiFixEnabled, v => settings.AiFixEnabled = v);
        Section(L.T("Connection (OpenAI-compatible)", "连接（OpenAI 协议）"));
        var url = Text2(L.T("API address", "接口地址"), settings.AiBaseUrl, "https://api.openai.com/v1", v => settings.AiBaseUrl = v.Length > 0 ? v : "https://api.openai.com/v1", false);
        var key = Text2(L.T("API key", "API Key"), settings.AiKey, "sk-...", v => settings.AiKey = v, false, true);
        var model = Text2(L.T("Model", "模型"), settings.AiModel, "gpt-4o-mini", v => settings.AiModel = v.Length > 0 ? v : "gpt-4o-mini", false);
        TestRow(url, key, model);
        Note(L.T("Your key is stored encrypted for your Windows account. When you press \"AI fix\", the current file and the compiler messages are sent to the address above.",
                 "密钥会用你的 Windows 账户加密保存。点击“AI 纠错”时，当前文件和编译报错会被发送到上面的接口地址。"));

        // ---- Appearance
        Page("look", "palette", L.T("Appearance", "外观"), L.T("Theme, animation and language.", "主题、动画和语言。"));
        Choice(L.T("Theme", "主题"), new[] { L.T("Follow Windows", "跟随系统"), L.T("Light (70s retro)", "亮色（70s 复古）"), L.T("Dark (classic)", "暗色（经典）") },
            new[] { "auto", "light", "dark" }, settings.Theme, v => settings.Theme = v);
        Toggle(L.T("Rich animations", "复杂动画"), L.T("Ripples, gliding caret, particle bursts, splash screen and staggered entrances.", "点击涟漪、光标滑动、粒子迸发、启动画面、卡片依次出现等效果。"), settings.Animations, v => settings.Animations = v);
        Choice(L.T("Language (restart to apply)", "语言（重启后生效）"), new[] { L.T("Follow Windows", "跟随系统"), "中文", "English" },
            new[] { "auto", "zh", "en" }, settings.Language, v => settings.Language = v);

        // ---- Terminal
        Page("term", "term", L.T("Terminal", "终端"), L.T("The built-in terminal, for shells and AI agents.", "内置终端，可运行 Shell 和 AI 智能体。"));
        Choice(L.T("Default shell", "默认 Shell"), new[] { L.T("Auto (PowerShell)", "自动（PowerShell）"), "Windows PowerShell", L.T("Command Prompt", "命令提示符"), "Git Bash" },
            new[] { "auto", "powershell", "cmd", "bash" }, settings.TerminalShell, v => settings.TerminalShell = v);
        Toggle(L.T("Terminal in sidebar", "终端放在侧边栏"), L.T("Off: the terminal lives in the bottom panel.", "关闭时终端位于底部面板。"), settings.TerminalInSidebar, v => settings.TerminalInSidebar = v);

        // ---- Updates & about
        Page("about", "update", L.T("Updates & about", "更新与关于"), L.T("Gestirn dev is open source on GitHub.", "Gestirn dev 在 GitHub 上开源。"));
        UpdateRow(checkNow);
        Toggle(L.T("Check for updates automatically", "自动检查更新"), L.T("When online, looks for a newer release on GitHub at start. Shows nothing if you are up to date.", "联网时启动后检查 GitHub 是否有新版本；已是最新则不打扰。"), settings.CheckUpdates, v => settings.CheckUpdates = v);
        LinkRow(L.T("Project on GitHub", "GitHub 项目地址"), Updater.RepoUrl);
        LinkRow(L.T("All releases", "全部版本下载"), Updater.ReleasesUrl);

        nav.Add("editor", "format", L.T("Editor", "编辑器"));
        nav.Add("files", "folder", L.T("Files", "文件"));
        nav.Add("compiler", "build", L.T("Compiler", "编译器"));
        nav.Add("learn", "check", L.T("Learning aids", "学习辅助"));
        nav.Add("ai", "spark", L.T("AI fix", "AI 纠错"), L.T("Beta", "实验"));
        nav.Add("look", "palette", L.T("Appearance", "外观"));
        nav.Add("term", "term", L.T("Terminal", "终端"));
        nav.Add("about", "update", L.T("Updates", "更新与关于"));
        nav.Selected += ShowPage;
        slide.Changed += () => { if (current != null && pages.ContainsKey(current)) pages[current].Left = (int)((1f - slide.Value) * Ui.D(18)); };

        var footer = new Panel { Bounds = new Rectangle(0, ClientSize.Height - footerH, ClientSize.Width, footerH), BackColor = Theme.Panel };
        footer.Paint += (_, e) => { using (var pen = new Pen(Theme.EdgeSoft)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
        Controls.Add(footer);
        var save = new PixelButton { Text = L.T("Save", "保存"), Kind = ButtonKind.Primary, Bounds = new Rectangle(ClientSize.Width - Ui.D(236), Ui.D(14), Ui.D(104), Ui.D(36)) };
        var cancel = new PixelButton { Text = L.T("Cancel", "取消"), Kind = ButtonKind.Secondary, Bounds = new Rectangle(ClientSize.Width - Ui.D(120), Ui.D(14), Ui.D(100), Ui.D(36)) };
        footer.Controls.Add(save); footer.Controls.Add(cancel);
        save.Click += (_, _) => { foreach (var a in apply) a(); DialogResult = DialogResult.OK; };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        Shown += (_, _) => nav.Select(startPage != null && pages.ContainsKey(startPage) ? startPage : "editor", false);
    }

    private void ShowPage(string id)
    {
        foreach (var kv in pages) kv.Value.Visible = kv.Key == id;
        current = id;
        string[] t = pageText[id];
        pageTitle.Text = t[0];
        pageSub.Text = t[1];
        if (!Animator.Reduced) { slide.Snap(0f); slide.To(1f, 260, Ease.OutCubic); }
        pages[id].PerformLayout();
    }

    private void Page(string id, string glyph, string title, string subtitle, string badge = null)
    {
        body = new Panel { AutoScroll = true, BackColor = Theme.Panel, Visible = false, Bounds = new Rectangle(0, 0, pageHost.Width, pageHost.Height) };
        Native.DarkScrollbars(body);
        pageHost.Controls.Add(body);
        pages[id] = body;
        pageText[id] = new[] { title + (badge != null ? "  ·  " + badge : ""), subtitle };
        y = Ui.D(8);
    }

    private int RowWidth { get { return body.ClientSize.Width - Ui.D(56); } }
    private int Pad { get { return Ui.D(28); } }

    private void Section(string title)
    {
        y += Ui.D(10);
        body.Controls.Add(new Label { Text = title.ToUpperInvariant(), Location = new Point(Pad, y), Size = new Size(RowWidth, Ui.D(22)), ForeColor = Theme.Orange, Font = Theme.UiSemi, BackColor = Theme.Panel });
        y += Ui.D(30);
    }

    private void Note(string text)
    {
        y += Ui.D(6);
        int h = TextRenderer.MeasureText(text, Theme.UiSmall, new Size(RowWidth, 1000), TextFormatFlags.WordBreak).Height + Ui.D(8);
        body.Controls.Add(new Label { Text = text, Location = new Point(Pad, y), Size = new Size(RowWidth, h), ForeColor = Theme.Muted, Font = Theme.UiSmall, BackColor = Theme.Panel });
        y += h + Ui.D(8);
    }

    private void Row(string title, string desc, Control control, int controlWidth)
    {
        int h = desc == null ? Ui.D(44) : Ui.D(60);
        int labelW = RowWidth - controlWidth - Ui.D(24);
        body.Controls.Add(new Label { Text = title, Location = new Point(Pad, y + Ui.D(7)), Size = new Size(labelW, Ui.D(22)), ForeColor = Theme.Text, BackColor = Theme.Panel, Font = Theme.Ui10, AutoEllipsis = true });
        if (desc != null)
        {
            int dh = Math.Min(Ui.D(34), TextRenderer.MeasureText(desc, Theme.UiSmall, new Size(labelW, 1000), TextFormatFlags.WordBreak).Height + Ui.D(2));
            body.Controls.Add(new Label { Text = desc, Location = new Point(Pad, y + Ui.D(30)), Size = new Size(labelW, dh), ForeColor = Theme.Muted, BackColor = Theme.Panel, Font = Theme.UiSmall });
            if (dh > Ui.D(20)) h += Ui.D(14);
        }
        control.Location = new Point(Pad + RowWidth - controlWidth, y + (desc == null ? Ui.D(7) : Ui.D(14)));
        body.Controls.Add(control);
        y += h;
    }

    private void Toggle(string title, string desc, bool value, Action<bool> set)
    {
        var t = new ToggleSwitch { Checked = value };
        Row(title, desc, t, t.Width);
        apply.Add(() => set(t.Checked));
    }

    private void Choice(string title, string[] labels, string[] values, string current, Action<string> set)
    {
        int idx = Array.FindIndex(values, v => string.Equals(v, current ?? "", StringComparison.OrdinalIgnoreCase));
        var box = new ChoiceBox(labels, idx >= 0 ? idx : 0) { Width = Ui.D(250) };
        Row(title, null, box, box.Width);
        apply.Add(() => set(values[Math.Max(0, box.SelectedIndex)]));
    }

    private void Number(string title, int value, int min, int max, Action<int> set)
    {
        var holder = new Panel { Size = Ui.D(120, 28), BackColor = Theme.Panel };
        int current = value;
        var label = new Label { Text = value.ToString(), Bounds = new Rectangle(Ui.D(30), 0, Ui.D(60), Ui.D(28)), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Text, BackColor = Theme.Panel, Font = Theme.Ui10 };
        var minus = new PixelButton { Glyph = "minus", Kind = ButtonKind.Secondary, Bounds = new Rectangle(0, 0, Ui.D(28), Ui.D(28)), Radius = 7 };
        var plus = new PixelButton { Glyph = "plus", Kind = ButtonKind.Secondary, Bounds = new Rectangle(Ui.D(92), 0, Ui.D(28), Ui.D(28)), Radius = 7 };
        minus.Click += (_, _) => { current = Math.Max(min, current - 1); label.Text = current.ToString(); };
        plus.Click += (_, _) => { current = Math.Min(max, current + 1); label.Text = current.ToString(); };
        holder.Controls.AddRange(new Control[] { minus, label, plus });
        Row(title, null, holder, holder.Width);
        apply.Add(() => set(current));
    }

    private RoundInput Text2(string title, string value, string placeholder, Action<string> set, bool browse, bool password = false)
    {
        var input = new RoundInput(placeholder) { Size = Ui.D(browse ? 300 : 340, 30) };
        input.Text = value ?? "";
        if (password) input.Box.UseSystemPasswordChar = true;
        Control control = input;
        if (browse)
        {
            var holder = new Panel { Size = Ui.D(340, 30), BackColor = Theme.Panel };
            var b = new PixelButton { Glyph = "folder", Kind = ButtonKind.Secondary, Bounds = new Rectangle(Ui.D(308), 0, Ui.D(32), Ui.D(30)), Radius = 7 };
            b.Click += (_, _) =>
            {
                using (var dialog = new OpenFileDialog { Filter = "g++.exe|g++.exe|" + L.T("Programs", "程序") + "|*.exe" })
                    if (dialog.ShowDialog(this) == DialogResult.OK) input.Text = dialog.FileName;
            };
            input.Location = Point.Empty;
            holder.Controls.Add(input);
            holder.Controls.Add(b);
            control = holder;
        }
        Row(title, null, control, control.Width);
        apply.Add(() => set(input.Text.Trim()));
        return input;
    }

    private void TestRow(RoundInput url, RoundInput key, RoundInput model)
    {
        var button = new PixelButton { Glyph = "refresh", Text = L.T("Test connection", "测试连接"), Kind = ButtonKind.Secondary, Radius = 8, Height = Ui.D(30) };
        button.Width = button.PreferredWidth();
        int top = y;
        Row(L.T("Check the settings", "检查设置"), L.T("Sends one tiny request to make sure the address, key and model work.", "发送一个很小的请求，确认地址、密钥和模型可用。"), button, button.Width);
        var desc = body.Controls.OfType<Label>().LastOrDefault(l => l.Top == top + Ui.D(30));
        button.Enabled = aiTest != null;
        button.Click += async (_, _) =>
        {
            if (aiTest == null || button.Busy) return;
            var probe = new AppSettings { AiFixEnabled = true, AiBaseUrl = url.Text.Trim(), AiKey = key.Text.Trim(), AiModel = model.Text.Trim() };
            if (!AiFix.Configured(probe))
            {
                if (desc != null) { desc.Text = L.T("Fill in a valid address (https://...), an API key and a model first.", "请先填写有效的地址（https://...）、API Key 和模型。"); desc.ForeColor = Theme.Red; }
                return;
            }
            button.Busy = true;
            if (desc != null) { desc.Text = L.T("Contacting ", "正在连接 ") + AiFix.HostOf(probe.AiBaseUrl) + "…"; desc.ForeColor = Theme.Muted; }
            string result = await aiTest(probe);
            button.Busy = false;
            if (desc != null && !desc.IsDisposed)
            {
                bool ok = result.StartsWith(L.T("Connected", "连接成功"), StringComparison.Ordinal);
                desc.Text = result.Length > 90 ? result.Substring(0, 90) + "…" : result;
                desc.ForeColor = ok ? Theme.Lime : Theme.Red;
            }
        };
    }

    private void UpdateRow(Func<System.Threading.Tasks.Task<string>> checkNow)
    {
        var button = new PixelButton { Glyph = "refresh", Text = L.T("Check now", "立即检查"), Kind = ButtonKind.Secondary, Radius = 8, Height = Ui.D(30) };
        button.Width = button.PreferredWidth();
        int top = y;
        Row(L.T("Version ", "当前版本 ") + MainForm.Version, L.T("Gestirn dev updates come from the GitHub releases page.", "新版本来自 GitHub 发布页。"), button, button.Width);
        var desc = body.Controls.OfType<Label>().LastOrDefault(l => l.Top == top + Ui.D(30));
        button.Enabled = checkNow != null;
        button.Click += async (_, _) =>
        {
            if (checkNow == null || button.Busy) return;
            button.Busy = true;
            if (desc != null) { desc.Text = L.T("Checking GitHub…", "正在检查 GitHub…"); desc.ForeColor = Theme.Muted; }
            string result = await checkNow();
            button.Busy = false;
            if (desc != null && !desc.IsDisposed) { desc.Text = result; desc.ForeColor = Theme.Lime; }
        };
    }

    private void LinkRow(string title, string url)
    {
        var link = new LinkLabel
        {
            Text = url.Replace("https://", ""), AutoSize = false, Size = new Size(Ui.D(340), Ui.D(24)), TextAlign = ContentAlignment.MiddleRight,
            LinkColor = Theme.Orange, ActiveLinkColor = Theme.OrangeHot, VisitedLinkColor = Theme.Orange, BackColor = Theme.Panel,
            Font = Theme.Ui9, LinkBehavior = LinkBehavior.HoverUnderline, Cursor = Cursors.Hand,
        };
        link.LinkClicked += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(url); }
            catch (System.ComponentModel.Win32Exception) { Clipboard.SetText(url); }
        };
        new ToolTip().SetToolTip(link, url);
        Row(title, null, link, link.Width);
    }
}

// New project from Dev-C++ templates.
internal sealed class NewProjectDialog : DarkDialog
{
    private readonly List<ProjectTemplate> templates;
    private readonly VirtualList list = new VirtualList();
    private readonly RoundInput name = new RoundInput(L.T("Project name", "项目名称"));
    private readonly RoundInput location = new RoundInput(L.T("Folder", "位置"));
    private readonly PixelButton cpp = new PixelButton { Text = "C++", Kind = ButtonKind.Ghost, Active = true };
    private readonly PixelButton c = new PixelButton { Text = "C", Kind = ButtonKind.Ghost };

    public ProjectTemplate Template { get { return list.SelectedIndex >= 0 ? templates[list.SelectedIndex] : null; } }
    public string ProjectName { get { return name.Text.Trim(); } }
    public string Folder { get { return Path.Combine(location.Text.Trim(), ProjectName); } }
    public bool Cpp { get { return cpp.Active; } }

    public NewProjectDialog(string defaultLocation)
    {
        Text = L.T("New Project", "新建项目");
        ClientSize = Ui.D(640, 560);
        templates = Templates.Load();
        AddLabel(L.T("Template", "模板"), Ui.D(20), Ui.D(16), Ui.D(300), Theme.UiSemi, Theme.Orange);
        var frame = new Card { Fill = Theme.PanelHi, Bounds = new Rectangle(Ui.D(20), Ui.D(42), Ui.D(600), Ui.D(290)), Radius = 10 };
        list.BackColor = Theme.PanelHi;
        list.RowHeight = Ui.D(44);
        list.Bounds = new Rectangle(Ui.D(4), Ui.D(4), frame.Width - Ui.D(8), frame.Height - Ui.D(8));
        list.DrawRow = DrawRow;
        list.Reset(templates.Count);
        int hello = templates.FindIndex(t => t.Name.StartsWith("Hello", StringComparison.OrdinalIgnoreCase));
        int console = templates.FindIndex(t => t.Type == DevProject.TypeConsole && t.Units.Count > 0);
        list.SelectedIndex = hello >= 0 ? hello : console >= 0 ? console : 0;
        list.RowActivated += _ => Create();
        frame.Controls.Add(list);
        Controls.Add(frame);

        AddLabel(L.T("Name", "名称"), Ui.D(20), Ui.D(348), Ui.D(120));
        name.Bounds = new Rectangle(Ui.D(140), Ui.D(342), Ui.D(300), Ui.D(32));
        name.Text = "Project1";
        Controls.Add(name);
        cpp.Bounds = new Rectangle(Ui.D(456), Ui.D(342), Ui.D(78), Ui.D(32));
        c.Bounds = new Rectangle(Ui.D(540), Ui.D(342), Ui.D(78), Ui.D(32));
        cpp.Click += (_, _) => { cpp.Active = true; c.Active = false; cpp.Invalidate(); c.Invalidate(); };
        c.Click += (_, _) => { c.Active = true; cpp.Active = false; cpp.Invalidate(); c.Invalidate(); };
        Controls.Add(cpp);
        Controls.Add(c);

        AddLabel(L.T("Location", "位置"), Ui.D(20), Ui.D(396), Ui.D(120));
        location.Bounds = new Rectangle(Ui.D(140), Ui.D(390), Ui.D(430), Ui.D(32));
        location.Text = defaultLocation;
        Controls.Add(location);
        var browse = new PixelButton { Glyph = "folder", Kind = ButtonKind.Secondary, Bounds = new Rectangle(Ui.D(578), Ui.D(390), Ui.D(42), Ui.D(32)), Radius = 8 };
        browse.Click += (_, _) =>
        {
            using (var d = new FolderBrowserDialog { SelectedPath = location.Text })
                if (d.ShowDialog(this) == DialogResult.OK) location.Text = d.SelectedPath;
        };
        Controls.Add(browse);
        AddLabel(L.T("A folder with the project name is created inside this location.", "会在此位置下创建与项目同名的文件夹。"), Ui.D(140), Ui.D(428), Ui.D(480), Theme.UiSmall, Theme.Muted);

        var create = AddButton(L.T("Create", "创建"), ButtonKind.Primary, ClientSize.Width - Ui.D(232), ClientSize.Height - Ui.D(54), Ui.D(104));
        var cancel = AddButton(L.T("Cancel", "取消"), ButtonKind.Secondary, ClientSize.Width - Ui.D(120), ClientSize.Height - Ui.D(54), Ui.D(100));
        create.Click += (_, _) => Create();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
    }

    private void Create()
    {
        if (Template == null || ProjectName.Length == 0 || ProjectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            MessageBox.Show(this, L.T("Choose a template and a valid project name.", "请选择模板并输入有效的项目名称。"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (File.Exists(Path.Combine(Folder, ProjectName + ".dev")))
        {
            MessageBox.Show(this, L.T("A project with this name already exists there.", "该位置已存在同名项目。"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        DialogResult = DialogResult.OK;
    }

    private void DrawRow(Graphics g, int i, Rectangle r, bool hover, bool selected)
    {
        var t = templates[i];
        VirtualList.RowBackground(g, r, hover, selected, true);
        Glyphs.Draw(g, t.Glyph ?? "file", new Rectangle(r.X + Ui.D(20), r.Y + (r.Height - Ui.D(16)) / 2, Ui.D(16), Ui.D(16)), selected ? Theme.Orange : Theme.TextDim);
        Shapes.Text(g, t.Name, Theme.UiSemi, new Rectangle(r.X + Ui.D(50), r.Y + Ui.D(4), r.Width - Ui.D(60), Ui.D(20)), Theme.Text, Shapes.Left);
        Shapes.Text(g, t.Description ?? "", Theme.UiSmall, new Rectangle(r.X + Ui.D(50), r.Y + Ui.D(22), r.Width - Ui.D(60), Ui.D(18)), Theme.Muted, Shapes.Left);
    }
}

// Dev-C++ "Project Options": type, compiler and linker flags, folders and output.
internal sealed class ProjectOptionsDialog : DarkDialog
{
    private readonly DevProject project;
    private readonly List<Action> apply = new List<Action>();
    private readonly Panel body = new Panel { AutoScroll = true, BackColor = Theme.Panel };
    private int y = 0;

    public ProjectOptionsDialog(DevProject project)
    {
        this.project = project;
        Text = L.T("Project Options", "项目属性") + " - " + project.Name;
        ClientSize = Ui.D(640, 640);
        body.Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height - Ui.D(64));
        Native.DarkScrollbars(body);
        Controls.Add(body);
        y = Ui.D(16);
        Field(L.T("Project name", "项目名称"), project.Name, v => project.Name = v.Length > 0 ? v : project.Name);
        var types = new[] { L.T("Windows application (GUI)", "Windows 窗口程序"), L.T("Console application", "控制台程序"), L.T("Static library (.a)", "静态库 (.a)"), L.T("DLL", "动态链接库 (DLL)") };
        var type = new ChoiceBox(types, Math.Max(0, Math.Min(3, project.Type))) { Width = Ui.D(300) };
        Row(L.T("Type", "类型"), type);
        apply.Add(() => project.Type = type.SelectedIndex);
        Field(L.T("C compiler flags", "C 编译选项"), project.CompilerFlags, v => project.CompilerFlags = v);
        Field(L.T("C++ compiler flags", "C++ 编译选项"), project.CppFlags, v => project.CppFlags = v);
        Field(L.T("Linker flags (e.g. -lgdi32 -lws2_32)", "链接选项（如 -lgdi32 -lws2_32）"), project.LinkerFlags, v => project.LinkerFlags = v);
        Field(L.T("Include folders (; separated)", "头文件目录（用 ; 分隔）"), project.IncludesRaw, v => project.IncludesRaw = v);
        Field(L.T("Library folders (; separated)", "库目录（用 ; 分隔）"), project.LibsRaw, v => project.LibsRaw = v);
        Field(L.T("Executable output folder", "可执行文件输出目录"), project.ExeOutput, v => project.ExeOutput = v);
        Field(L.T("Object file folder", "目标文件目录"), project.ObjectOutput, v => project.ObjectOutput = v);
        Field(L.T("Output file name (empty = project name)", "输出文件名（留空 = 项目名）"), project.OverrideOutput ? project.OverrideOutputName : "", v =>
        {
            project.OverrideOutput = v.Length > 0;
            project.OverrideOutputName = v;
        });
        var useMake = new ToggleSwitch { Checked = project.UseCustomMakefile };
        Row(L.T("Use custom Makefile (mingw32-make)", "使用自定义 Makefile（mingw32-make）"), useMake);
        apply.Add(() => project.UseCustomMakefile = useMake.Checked);
        Field(L.T("Makefile", "Makefile 文件"), project.CustomMakefile, v => project.CustomMakefile = v);

        var ok = AddButton(L.T("Save", "保存"), ButtonKind.Primary, ClientSize.Width - Ui.D(232), ClientSize.Height - Ui.D(50), Ui.D(104));
        var cancel = AddButton(L.T("Cancel", "取消"), ButtonKind.Secondary, ClientSize.Width - Ui.D(120), ClientSize.Height - Ui.D(50), Ui.D(100));
        ok.Click += (_, _) => { foreach (var a in apply) a(); DialogResult = DialogResult.OK; };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
    }

    private void Row(string title, Control control)
    {
        body.Controls.Add(new Label { Text = title, Location = new Point(Ui.D(20), y + Ui.D(5)), Size = new Size(Ui.D(270), Ui.D(22)), ForeColor = Theme.Text, BackColor = Theme.Panel, Font = Theme.Ui9, AutoEllipsis = true });
        control.Location = new Point(body.ClientSize.Width - Ui.D(20) - control.Width, y);
        body.Controls.Add(control);
        y += Math.Max(Ui.D(44), control.Height + Ui.D(12));
    }

    private void Field(string title, string value, Action<string> set)
    {
        var input = new RoundInput("") { Size = Ui.D(300, 32) };
        input.Text = value ?? "";
        Row(title, input);
        apply.Add(() => set(input.Text.Trim()));
    }
}
}
