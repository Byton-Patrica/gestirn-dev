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

// Settings page: grouped rows with toggles, choices and text fields.
internal sealed class SettingsDialog : DarkDialog
{
    private readonly Panel body = new Panel { AutoScroll = true, BackColor = Theme.Panel };
    private int y;
    private readonly List<Action> apply = new List<Action>();
    private readonly AppSettings settings;

    public SettingsDialog(AppSettings settings)
    {
        this.settings = settings;
        Text = L.T("Settings", "设置");
        ClientSize = Ui.D(640, 640);
        body.Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height - Ui.D(64));
        Native.DarkScrollbars(body);
        Controls.Add(body);
        y = Ui.D(16);

        Section(L.T("Editor", "编辑器"));
        Number(L.T("Font size", "字号"), (int)Math.Round(settings.FontSize), 8, 28, v => settings.FontSize = v);
        Choice(L.T("Tab size", "缩进宽度"), new[] { "2", "4", "8" }, new[] { "2", "4", "8" }, settings.TabSize.ToString(), v => settings.TabSize = int.Parse(v));
        Toggle(L.T("Indent with spaces", "用空格缩进"), L.T("Tab inserts spaces instead of a tab character.", "Tab 键插入空格而不是制表符。"), settings.InsertSpaces, v => settings.InsertSpaces = v);
        Toggle(L.T("Auto-close brackets and quotes", "自动补全括号和引号"), null, settings.AutoPairs, v => settings.AutoPairs = v);
        Toggle(L.T("Suggestions while typing", "输入时自动提示"), L.T("Keywords, library names, snippets and words from your files.", "关键字、库函数、代码片段和文件中的标识符。"), settings.AutoComplete, v => settings.AutoComplete = v);

        Section(L.T("Files", "文件"));
        Toggle(L.T("Auto save", "自动保存"), L.T("Saves files a moment after you stop typing and when the window loses focus.", "停止输入片刻后、窗口失去焦点时自动保存。"), settings.AutoSave, v => settings.AutoSave = v);
        Toggle(L.T("Format on save", "保存时格式化"), L.T("Runs AStyle (K&R style) when a file is saved by hand.", "手动保存时用 AStyle（K&R 风格）格式化。"), settings.FormatOnSave, v => settings.FormatOnSave = v);
        Toggle(L.T("Restore open files on start", "启动时恢复上次打开的文件"), null, settings.RestoreSession, v => settings.RestoreSession = v);

        Section(L.T("Compiler", "编译器"));
        Text2(L.T("g++ path", "g++ 路径"), settings.CompilerPath, L.T("Leave empty to detect automatically", "留空则自动检测"), v => settings.CompilerPath = v, true);
        Choice(L.T("C++ standard", "C++ 标准"), new[] { L.T("Auto (newest supported)", "自动（编译器支持的最新）"), "C++11", "C++14", "C++17", "C++20" },
            new[] { "auto", "c++11", "c++14", "c++17", "c++20" }, settings.Standard, v => settings.Standard = v);
        Choice(L.T("Optimization", "优化级别"), new[] { L.T("None (best for debugging)", "无（便于调试）"), "-O1", "-O2", "-O3", "-Os" },
            new[] { "", "-O1", "-O2", "-O3", "-Os" }, settings.Optimization, v => settings.Optimization = v);
        Text2(L.T("Extra flags", "额外编译选项"), settings.ExtraFlags, "-Wall", v => settings.ExtraFlags = v, false);
        Toggle(L.T("Link statically", "静态链接"), L.T("Adds -static so the .exe runs on other PCs without MinGW DLLs.", "加 -static，生成的 exe 在别的电脑上无需 MinGW 运行库。"), settings.StaticLink, v => settings.StaticLink = v);
        Choice(L.T("Program text encoding", "程序输出编码"), new[] { L.T("Auto (GBK on Chinese Windows)", "自动（中文系统用 GBK）"), "GBK", "UTF-8" },
            new[] { "auto", "gbk", "utf8" }, settings.ExecCharset, v => settings.ExecCharset = v);

        Section(L.T("Interface", "界面"));
        Choice(L.T("Language (restart to apply)", "语言（重启后生效）"), new[] { L.T("Follow Windows", "跟随系统"), "中文", "English" },
            new[] { "auto", "zh", "en" }, settings.Language, v => settings.Language = v);
        y += Ui.D(10);
        body.Controls.Add(new Panel { Bounds = new Rectangle(0, y, 1, 1), BackColor = Theme.Panel });

        var save = AddButton(L.T("Save", "保存"), ButtonKind.Primary, ClientSize.Width - Ui.D(232), ClientSize.Height - Ui.D(50), Ui.D(104));
        var cancel = AddButton(L.T("Cancel", "取消"), ButtonKind.Secondary, ClientSize.Width - Ui.D(120), ClientSize.Height - Ui.D(50), Ui.D(100));
        save.Click += (_, _) => { foreach (var a in apply) a(); DialogResult = DialogResult.OK; };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
    }

    private int RowWidth { get { return body.ClientSize.Width - Ui.D(40); } }

    private void Section(string title)
    {
        y += Ui.D(8);
        body.Controls.Add(new Label { Text = title.ToUpperInvariant(), Location = new Point(Ui.D(20), y), Size = new Size(RowWidth, Ui.D(22)), ForeColor = Theme.Orange, Font = Theme.UiSemi, BackColor = Theme.Panel });
        y += Ui.D(30);
    }

    private void Row(string title, string desc, Control control, int controlWidth)
    {
        int h = desc == null ? Ui.D(40) : Ui.D(56);
        body.Controls.Add(new Label { Text = title, Location = new Point(Ui.D(20), y + Ui.D(6)), Size = new Size(RowWidth - controlWidth - Ui.D(20), Ui.D(22)), ForeColor = Theme.Text, BackColor = Theme.Panel, Font = Theme.Ui10, AutoEllipsis = true });
        if (desc != null)
            body.Controls.Add(new Label { Text = desc, Location = new Point(Ui.D(20), y + Ui.D(30)), Size = new Size(RowWidth - controlWidth - Ui.D(20), Ui.D(20)), ForeColor = Theme.Muted, BackColor = Theme.Panel, Font = Theme.UiSmall, AutoEllipsis = true });
        control.Location = new Point(Ui.D(20) + RowWidth - controlWidth, y + (desc == null ? Ui.D(6) : Ui.D(12)));
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
        var box = new ChoiceBox(labels, idx >= 0 ? idx : 0) { Width = Ui.D(240) };
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

    private void Text2(string title, string value, string placeholder, Action<string> set, bool browse)
    {
        var input = new RoundInput(placeholder) { Size = Ui.D(browse ? 290 : 330, 30) };
        input.Text = value ?? "";
        Control control = input;
        if (browse)
        {
            var holder = new Panel { Size = Ui.D(330, 30), BackColor = Theme.Panel };
            var b = new PixelButton { Glyph = "folder", Kind = ButtonKind.Secondary, Bounds = new Rectangle(Ui.D(298), 0, Ui.D(32), Ui.D(30)), Radius = 7 };
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
}
