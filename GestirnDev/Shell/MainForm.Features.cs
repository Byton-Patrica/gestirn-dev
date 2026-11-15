using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace GestirnDev
{
internal sealed partial class MainForm
{
    private readonly AnimatedFloat viewSlide = new AnimatedFloat(0f);

    // ------------------------------------------------------------------ theme & animations

    public static bool ResolveLight(string theme)
    {
        return theme == "light" || (theme == "auto" && Theme.SystemPrefersLight());
    }

    private void ApplyTheme(bool light)
    {
        if (light == Theme.IsLight) return;
        var before = Theme.Snapshot();
        Theme.Apply(light);
        Theme.Remap(this, before);
        Native.StyleWindow(this);
        terminal.ApplyTheme();
        foreach (var d in docs) { d.Editor.BackColor = Theme.EditorBg; d.Editor.Invalidate(); }
        Invalidate(true);
    }

    private void ToggleTheme()
    {
        settings.Theme = Theme.IsLight ? "dark" : "light";
        ApplyTheme(settings.Theme == "light");
        settings.Save();
    }

    private void ToggleAnimations()
    {
        settings.Animations = !settings.Animations;
        Animator.Reduced = !settings.Animations;
        settings.Save();
        toasts.Show(settings.Animations ? L.T("Rich animations on", "已开启复杂动画") : L.T("Rich animations off", "已关闭复杂动画"), null, ToastKind.Info, 1800);
    }

    // ------------------------------------------------------------------ terminal placement

    private void PlaceTerminal(bool inSidebar)
    {
        settings.TerminalInSidebar = inSidebar;
        terminal.InSidebar = inSidebar;
        if (inSidebar && terminal.Parent != sidebar)
        {
            bottom.Controls.Remove(terminal);
            sidebar.Controls.Add(terminal);
            if (settings.SidebarWidth < 440) settings.SidebarWidth = 480;
            if (panelTabs.Active == "terminal") ShowPanel("problems", false);
            ShowView("terminal");
            ToggleSidebar(true);
        }
        else if (!inSidebar && terminal.Parent != bottom)
        {
            sidebar.Controls.Remove(terminal);
            bottom.Controls.Add(terminal);
            if (settings.SidebarView == "terminal") ShowView("explorer");
            ShowPanel("terminal");
        }
        LayoutShell();
        LayoutSidebar();
        LayoutBottom();
        terminal.FocusInput();
    }

    private void OpenTerminal()
    {
        if (settings.TerminalInSidebar) { ShowView("terminal"); ToggleSidebar(true); terminal.FocusInput(); }
        else ShowPanel("terminal");
    }

    private void NewTerminal()
    {
        OpenTerminal();
        terminal.NewSession(terminal.DefaultProfile());
    }

    private IDictionary<string, string> TerminalEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string compiler = Toolchain.FindCompiler(settings.CompilerPath);
        if (compiler != null)
            env["PATH"] = Path.GetDirectoryName(compiler) + ";" + Environment.GetEnvironmentVariable("PATH");
        return env;
    }

    // ------------------------------------------------------------------ Dev-C++ features

    private async void SyntaxCheck()
    {
        if (active == null || building) return;
        if (!EnsureOnDisk(active) || !active.IsSource) return;
        SaveAllQuiet();
        string compiler = Toolchain.FindCompiler(settings.CompilerPath);
        if (compiler == null) { CheckCompiler(); return; }
        status.Busy = true;
        status.Message = L.T("Checking syntax…", "正在检查语法…");
        try
        {
            var diags = await Toolchain.SyntaxCheck(active.Path, Options(compiler, false));
            ApplyDiagnostics(diags);
            int errors = diags.Count(d => d.Severity == Severity.Error);
            if (errors == 0) { status.Message = L.T("No syntax errors", "没有语法错误"); status.Flash(Theme.Lime); }
            else { status.Message = string.Format(L.T("{0} syntax errors", "{0} 个语法错误"), errors); status.Flash(Theme.Red); status.Shake(); ShowPanel("problems"); }
        }
        finally { status.Busy = false; status.Invalidate(); }
    }

    private void SetProgramArguments()
    {
        string value = Prompt.Show(this, L.T("Program arguments", "程序运行参数"), L.T("Arguments passed to the program when it runs (F10/F11/F5)", "运行程序时传入的参数（F10/F11/F5）"), settings.ProgramArgs);
        if (value == null) return;
        settings.ProgramArgs = value.Trim();
        settings.Save();
        status.Message = L.T("Arguments: ", "运行参数：") + (settings.ProgramArgs.Length > 0 ? settings.ProgramArgs : L.T("(none)", "（无）"));
        status.Invalidate();
    }

    private void GoToDefinition()
    {
        if (active == null) return;
        string word = active.Editor.WordAtCaret();
        if (string.IsNullOrEmpty(word)) return;
        GoToDefinition(word);
    }

    private void GoToDefinition(string word)
    {
        Func<IEnumerable<SymbolInfo>, SymbolInfo> pick = list =>
            list.Where(s => s.Name == word || s.Name.EndsWith("::" + word))
                .OrderBy(s => s.Kind == SymbolKind.Function ? 0 : 1).FirstOrDefault();
        if (active != null)
        {
            var s = pick(Symbols.Parse(active.Editor));
            if (s != null) { active.Editor.GoTo(s.Line, 0); active.Editor.Focus(); return; }
        }
        foreach (var d in docs.Where(d => d != active))
        {
            var s = pick(Symbols.Parse(d.Editor));
            if (s != null) { Activate(d); d.Editor.GoTo(s.Line, 0); return; }
        }
        var files = new List<string>();
        if (project != null) files.AddRange(project.Units.Select(u => project.FullPath(u.FileName)));
        files.AddRange(WorkspaceFiles().Where(f => DevProject.IsSource(f) || f.EndsWith(".h", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase)));
        foreach (var f in files.Distinct(StringComparer.OrdinalIgnoreCase).Take(400))
        {
            if (!File.Exists(f) || docs.Any(d => string.Equals(d.Path, f, StringComparison.OrdinalIgnoreCase))) continue;
            string[] lines;
            try { lines = ReadSource(f).Replace("\r\n", "\n").Split('\n'); }
            catch (IOException) { continue; }
            var s = pick(Symbols.Parse(i => lines[i], lines.Length));
            if (s != null) { OpenFile(f, s.Line, 0); return; }
        }
        status.Message = string.Format(L.T("No definition found for '{0}'", "未找到“{0}”的定义"), word);
        status.Shake();
        status.Invalidate();
    }

    private void InsertText(string text)
    {
        if (active == null) return;
        active.Editor.InsertText(text);
        active.Editor.Focus();
    }

    private void InsertFileHeader()
    {
        if (active == null) return;
        string name = active.Title;
        string text = "/*\n * " + name + "\n * " + L.T("Author: ", "作者：") + Environment.UserName + "\n * " + L.T("Date: ", "日期：") + DateTime.Now.ToString("yyyy-MM-dd") +
                      "\n * " + L.T("Description: ", "说明：") + "\n */\n";
        active.Editor.Select(new TextPos(0, 0), new TextPos(0, 0));
        active.Editor.InsertText(text);
    }

    private void InsertIncludeGuard()
    {
        if (active == null) return;
        string guard = new string(active.Title.ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        if (char.IsDigit(guard[0])) guard = "_" + guard;
        string body = active.Editor.Text;
        active.Editor.ReplaceAllText("#ifndef " + guard + "\n#define " + guard + "\n\n" + body.TrimEnd('\n') + "\n\n#endif // " + guard + "\n");
    }

    private void InsertSnippet(string name)
    {
        if (active == null) return;
        if (name == "main")
            InsertText("int main()\n{\n    \n    return 0;\n}\n");
        else if (name == "class")
            InsertText("class Name {\npublic:\n    Name();\n    ~Name();\n\nprivate:\n};\n");
    }

    // ------------------------------------------------------------------ project menu

    private void NewFileInProject()
    {
        if (project == null) { NewProject(); return; }
        string name = Prompt.Show(this, L.T("New file in project", "在项目中新建文件"), L.T("File name (e.g. util.cpp)", "文件名（例如 util.cpp）"), "untitled.cpp");
        if (string.IsNullOrWhiteSpace(name)) return;
        string path = Path.Combine(project.Directory, name.Trim());
        if (!File.Exists(path))
        {
            try { File.WriteAllText(path, "", new UTF8Encoding(false)); }
            catch (IOException ex) { toasts.Show(L.T("Can't create file", "无法创建文件"), ex.Message, ToastKind.Error); return; }
        }
        SetProjectMembership(path, true);
        explorer.Reload();
        OpenFile(path);
    }

    private void AddFilesToProject()
    {
        if (project == null) { toasts.Show(L.T("No project open", "未打开项目"), null, ToastKind.Info); return; }
        using (var dialog = new OpenFileDialog { Multiselect = true, InitialDirectory = project.Directory, Filter = L.T("C/C++ files", "C/C++ 文件") + "|*.c;*.cpp;*.cc;*.cxx;*.h;*.hpp;*.rc|" + L.T("All files", "所有文件") + "|*.*" })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            foreach (var f in dialog.FileNames) SetProjectMembership(f, true);
        }
    }

    private void RemoveActiveFromProject()
    {
        if (project == null || active == null || active.Path == null) return;
        SetProjectMembership(active.Path, false);
    }

    private void ShowProjectOptions()
    {
        if (project == null) { toasts.Show(L.T("No project open", "未打开项目"), L.T("Open or create a .dev project first.", "请先打开或新建 .dev 项目。"), ToastKind.Info); return; }
        using (var dialog = new ProjectOptionsDialog(project))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
        }
        try { project.Save(); }
        catch (IOException ex) { toasts.Show(L.T("Can't save project", "无法保存项目"), ex.Message, ToastKind.Error); return; }
        UpdateChrome();
        toasts.Show(L.T("Project options saved", "项目属性已保存"), project.Name, ToastKind.Success, 2000);
    }

    // ------------------------------------------------------------------ encoding, print, export

    private void ChooseEncoding()
    {
        if (active == null) return;
        active.Gbk = !active.Gbk;
        if (active.Path != null) Save(active, false, true);
        UpdateCaretStatus();
        status.Message = L.T("Encoding: ", "编码：") + (active.Gbk ? "GBK" : "UTF-8");
        status.Invalidate();
    }

    private void PrintActive()
    {
        if (active == null) return;
        var lines = Enumerable.Range(0, active.Editor.LineCount).Select(active.Editor.LineText).ToList();
        int next = 0, page = 0;
        using (var font = new Font(Theme.CodeFamily, 9f))
        using (var doc = new PrintDocument { DocumentName = active.Title })
        using (var dialog = new PrintDialog { Document = doc, UseEXDialog = true })
        {
            doc.PrintPage += (_, e) =>
            {
                page++;
                float y = e.MarginBounds.Top;
                float lh = font.GetHeight(e.Graphics);
                e.Graphics.DrawString(active.Title + "   —   " + page, font, Brushes.Gray, e.MarginBounds.Left, y - lh * 2);
                while (next < lines.Count && y + lh < e.MarginBounds.Bottom)
                {
                    e.Graphics.DrawString(string.Format("{0,4}  {1}", next + 1, lines[next].Replace("\t", "    ")), font, Brushes.Black, e.MarginBounds.Left, y);
                    y += lh;
                    next++;
                }
                e.HasMorePages = next < lines.Count;
            };
            if (dialog.ShowDialog(this) == DialogResult.OK) doc.Print();
        }
    }

    private void ExportHtml()
    {
        if (active == null) return;
        using (var dialog = new SaveFileDialog { Filter = "HTML|*.html", FileName = Path.GetFileNameWithoutExtension(active.Title) + ".html" })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var sb = new StringBuilder();
            sb.Append("<!doctype html><meta charset=\"utf-8\"><title>").Append(WebUtility.HtmlEncode(active.Title)).Append("</title>");
            sb.Append("<style>body{background:#fff;margin:24px}pre{font:13px/1.5 Consolas,monospace;color:#24221e}.k{color:#ce4606}.t{color:#a65a0a}.s{color:#487e00}.n{color:#966400}.c{color:#8c8880}.p{color:#7c46c4}.f{color:#1e62be}.ln{color:#b0aba2;user-select:none}</style><pre>");
            var tokens = new List<Token>();
            int state = 0;
            for (int l = 0; l < active.Editor.LineCount; l++)
            {
                string line = active.Editor.LineText(l);
                state = CppSyntax.Tokenize(line, state, tokens);
                sb.Append("<span class=\"ln\">").Append((l + 1).ToString().PadLeft(4)).Append("  </span>");
                int pos = 0;
                foreach (var t in tokens)
                {
                    if (t.Start > pos) sb.Append(WebUtility.HtmlEncode(line.Substring(pos, t.Start - pos)));
                    string cls = t.Kind == Tok.Keyword ? "k" : t.Kind == Tok.Type ? "t" : t.Kind == Tok.String || t.Kind == Tok.Include ? "s" :
                                 t.Kind == Tok.Number ? "n" : t.Kind == Tok.Comment ? "c" : t.Kind == Tok.Preproc ? "p" : t.Kind == Tok.Function ? "f" : null;
                    string text = WebUtility.HtmlEncode(line.Substring(t.Start, t.Length));
                    if (cls != null) sb.Append("<span class=\"").Append(cls).Append("\">").Append(text).Append("</span>"); else sb.Append(text);
                    pos = t.Start + t.Length;
                }
                if (pos < line.Length) sb.Append(WebUtility.HtmlEncode(line.Substring(pos)));
                sb.Append('\n');
            }
            sb.Append("</pre>");
            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(false));
            toasts.Show(L.T("Exported", "已导出"), dialog.FileName, ToastKind.Success);
        }
    }
}
}
