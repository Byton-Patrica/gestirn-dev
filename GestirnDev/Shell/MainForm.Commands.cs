using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GestirnDev
{
internal sealed partial class MainForm
{
    private sealed class Cmd
    {
        public string Id, Title, English, Glyph, Display;
        public Keys Keys;
        public Action Run;
    }

    private readonly List<Cmd> commands = new List<Cmd>();
    private readonly List<Process> buildProcs = new List<Process>();
    private readonly List<Process> programs = new List<Process>();
    private List<Diagnostic> lastDiagnostics = new List<Diagnostic>();
    private bool building, stopRequested;
    private string lastExe;
    private GdbSession gdb;
    private List<string> cachedFiles;
    private DateTime cachedFilesAt;

    // ------------------------------------------------------------------ registry & menus

    private Cmd C(string id, string en, string zh, Keys keys, Action run, string glyph = null, string display = null)
    {
        var c = new Cmd { Id = id, Title = L.T(en, zh), English = en.Replace("&&", "&"), Keys = keys, Run = run, Glyph = glyph, Display = display };
        commands.Add(c);
        return c;
    }

    private static string KeyText(Keys keys)
    {
        if (keys == Keys.None) return null;
        string s = new KeysConverter().ConvertToString(keys) ?? "";
        return s.Replace("Oemtilde", "`").Replace("Oemcomma", ",").Replace("Oemplus", "=").Replace("OemMinus", "-").Replace("D0", "0");
    }

    private ToolStripMenuItem Item(Cmd c)
    {
        var item = new ToolStripMenuItem(c.Title, null, (_, _) => c.Run());
        if (c.Display != null) item.ShortcutKeyDisplayString = c.Display;
        else if (c.Keys != Keys.None)
        {
            try { item.ShortcutKeys = c.Keys; item.ShortcutKeyDisplayString = KeyText(c.Keys); }
            catch (InvalidEnumArgumentException) { item.ShortcutKeyDisplayString = KeyText(c.Keys); }
        }
        item.ForeColor = Theme.Text;
        item.Padding = Ui.Pad(2, 3, 2, 3);
        return item;
    }

    private ToolStripMenuItem MenuOf(string en, string zh, params object[] items)
    {
        var m = new ToolStripMenuItem(L.T(en, zh)) { ForeColor = Theme.TextDim, Padding = Ui.Pad(8, 2, 8, 2) };
        foreach (var o in items)
        {
            if (o == null) m.DropDownItems.Add(new ToolStripSeparator());
            else if (o is Cmd) m.DropDownItems.Add(Item((Cmd)o));
            else if (o is ToolStripItem) m.DropDownItems.Add((ToolStripItem)o);
        }
        m.DropDown.BackColor = Theme.PanelHi;
        ((ToolStripDropDownMenu)m.DropDown).ShowImageMargin = false;
        return m;
    }

    private void BuildCommands()
    {
        var newFile = C("file.new", "New File", "新建文件", Keys.Control | Keys.N, () => NewFile(), "new");
        var newProject = C("file.newProject", "New Project...", "新建项目...", Keys.None, NewProject, "project");
        var openFile = C("file.open", "Open File...", "打开文件...", Keys.Control | Keys.O, OpenFileDialog, "file");
        var openFolder = C("file.openFolder", "Open Folder...", "打开文件夹...", Keys.None, OpenFolderDialog, "folder");
        var openProject = C("file.openProject", "Open Project (.dev)...", "打开项目 (.dev)...", Keys.None, OpenProjectDialog, "project");
        var save = C("file.save", "Save", "保存", Keys.Control | Keys.S, () => Save(active, false), "save");
        var saveAs = C("file.saveAs", "Save As...", "另存为...", Keys.Control | Keys.Shift | Keys.S, () => Save(active, true), "save");
        var saveAll = C("file.saveAll", "Save All", "全部保存", Keys.Control | Keys.Alt | Keys.S, () => { SaveAllQuiet(); status.Message = L.T("All files saved", "已全部保存"); status.Invalidate(); }, "save");
        var close = C("file.close", "Close Tab", "关闭标签页", Keys.Control | Keys.W, () => { if (active != null) CloseDoc(active); }, "close");
        var reopen = C("file.reopen", "Reopen Closed Tab", "重新打开关闭的标签页", Keys.Control | Keys.Shift | Keys.T, ReopenClosed, "recent");
        var closeFolder = C("file.closeFolder", "Close Folder / Project", "关闭文件夹/项目", Keys.None, CloseFolder, "close");
        var exit = C("file.exit", "Exit", "退出", Keys.None, Close, null);

        var undo = C("edit.undo", "Undo", "撤销", Keys.None, () => Ed(e => e.Undo()), null, "Ctrl+Z");
        var redo = C("edit.redo", "Redo", "重做", Keys.None, () => Ed(e => e.Redo()), null, "Ctrl+Y");
        var cut = C("edit.cut", "Cut", "剪切", Keys.None, () => Ed(e => e.Cut()), null, "Ctrl+X");
        var copy = C("edit.copy", "Copy", "复制", Keys.None, () => Ed(e => e.Copy()), null, "Ctrl+C");
        var paste = C("edit.paste", "Paste", "粘贴", Keys.None, () => Ed(e => e.Paste()), null, "Ctrl+V");
        var selectAll = C("edit.selectAll", "Select All", "全选", Keys.None, () => Ed(e => e.SelectAll()), null, "Ctrl+A");
        var find = C("edit.find", "Find", "查找", Keys.Control | Keys.F, () => OpenFind(false), "search");
        var replace = C("edit.replace", "Replace", "替换", Keys.Control | Keys.H, () => OpenFind(true), "replace");
        var findNext = C("edit.findNext", "Find Next", "查找下一个", Keys.F3, () => { if (findBar.Visible) findBar.Step(true); else OpenFind(false); }, "down");
        var findPrev = C("edit.findPrev", "Find Previous", "查找上一个", Keys.Shift | Keys.F3, () => { if (findBar.Visible) findBar.Step(false); else OpenFind(false); }, "up");
        var findFiles = C("edit.findInFiles", "Find in Files", "在文件中查找", Keys.Control | Keys.Shift | Keys.F, FindInFiles, "search");
        var gotoLine = C("edit.gotoLine", "Go to Line...", "转到行...", Keys.Control | Keys.G, () => palette.Open(":", L.T("Type a line number", "输入行号")), "arrow");
        var gotoSymbol = C("edit.gotoSymbol", "Go to Symbol...", "转到符号...", Keys.Control | Keys.Shift | Keys.O, () => palette.Open("@", L.T("Type a function or class name", "输入函数或类名")), "outline");
        var comment = C("edit.comment", "Toggle Comment", "切换注释", Keys.None, () => Ed(e => e.ToggleComment()), null, "Ctrl+/");
        var duplicate = C("edit.duplicate", "Duplicate Line", "复制当前行", Keys.None, () => Ed(e => e.DuplicateLine()), null, "Ctrl+D");
        var moveUp = C("edit.moveUp", "Move Line Up", "上移行", Keys.None, () => Ed(e => e.MoveLines(-1)), null, "Alt+Up");
        var moveDown = C("edit.moveDown", "Move Line Down", "下移行", Keys.None, () => Ed(e => e.MoveLines(1)), null, "Alt+Down");
        var deleteLine = C("edit.deleteLine", "Delete Line", "删除行", Keys.None, () => Ed(e => e.DeleteLines()), null, "Ctrl+Shift+K");
        var format = C("edit.format", "Format Code", "格式化代码", Keys.Control | Keys.Shift | Keys.A, () => { if (active != null) FormatNow(active, false); }, "format");
        var suggest = C("edit.suggest", "Trigger Suggestions", "显示代码提示", Keys.None, () => Ed(e => e.ShowCompletion()), null, "Ctrl+Space");

        var commandPalette = C("view.palette", "Command Palette...", "命令面板...", Keys.Control | Keys.Shift | Keys.P, () => palette.Open(">", L.T("Type a command", "输入命令")), "palette");
        var quickOpen = C("view.quickOpen", "Quick Open File...", "快速打开文件...", Keys.Control | Keys.P, QuickOpen, "file");
        var viewExplorer = C("view.explorer", "Explorer", "资源管理器", Keys.Control | Keys.Shift | Keys.E, () => { ShowView("explorer"); ToggleSidebar(true); }, "folder");
        var viewSearch = C("view.search", "Search", "搜索", Keys.None, FindInFiles, "search");
        var viewOutline = C("view.outline", "Outline", "大纲", Keys.None, () => { ShowView("outline"); ToggleSidebar(true); }, "outline");
        var viewDebug = C("view.debug", "Run and Debug", "运行和调试", Keys.Control | Keys.Shift | Keys.D, () => { ShowView("debug"); ToggleSidebar(true); }, "debug");
        var toggleSide = C("view.sidebar", "Toggle Sidebar", "显示/隐藏侧边栏", Keys.Control | Keys.B, () => ToggleSidebar(), "sidebar");
        var togglePanel = C("view.panel", "Toggle Panel", "显示/隐藏底部面板", Keys.Control | Keys.J, () => TogglePanel(), "panel");
        var viewProblems = C("view.problems", "Problems", "问题", Keys.Control | Keys.Shift | Keys.M, () => ShowPanel("problems"), "error");
        var viewOutput = C("view.output", "Output", "输出", Keys.Control | Keys.Shift | Keys.U, () => ShowPanel("output"), "outline");
        var viewTerminal = C("view.terminal", "Terminal", "终端", Keys.Control | Keys.Oemtilde, () => ShowPanel("terminal"), "term");
        var zoomIn = C("view.zoomIn", "Zoom In", "放大", Keys.Control | Keys.Oemplus, () => Ed(e => e.Zoom(1)), "plus");
        var zoomOut = C("view.zoomOut", "Zoom Out", "缩小", Keys.Control | Keys.OemMinus, () => Ed(e => e.Zoom(-1)), "minus");
        var zoomReset = C("view.zoomReset", "Reset Zoom", "重置缩放", Keys.Control | Keys.D0, () => Ed(e => e.Zoom(0)), null);

        var compile = C("build.compile", "Compile", "编译", Keys.F9, () => Compile(false, false, false), "build");
        var run = C("build.run", "Run", "运行", Keys.F10, RunLast, "run");
        var compileRun = C("build.compileRun", "Compile && Run", "编译并运行", Keys.F11, () => Compile(true, false, false), "run");
        var rebuild = C("build.rebuild", "Rebuild All", "全部重新编译", Keys.F12, () => Compile(false, true, false), "build");
        var stop = C("build.stop", "Stop", "停止", Keys.F6, () => StopAll(), "stop");
        var buildSettings = C("build.settings", "Compiler Settings...", "编译器设置...", Keys.None, OpenSettings, "gear");

        var debugStart = C("debug.start", "Start / Continue", "开始/继续调试", Keys.F5, DebugStartOrContinue, "debug");
        var stepOver = C("debug.over", "Step Over", "单步跳过", Keys.F7, () => DebugStep("over"), "stepover");
        var stepInto = C("debug.into", "Step Into", "单步进入", Keys.F8, () => DebugStep("into"), "stepinto");
        var stepOut = C("debug.out", "Step Out", "单步跳出", Keys.Shift | Keys.F8, () => DebugStep("out"), "stepout");
        var debugStop = C("debug.stop", "Stop Debugging", "停止调试", Keys.Shift | Keys.F5, () => { if (gdb != null) gdb.Stop(); }, "stop");
        var toggleBp = C("debug.breakpoint", "Toggle Breakpoint", "切换断点", Keys.Control | Keys.F5, () => Ed(e => e.ToggleBreakpoint(e.Caret.Line)), "breakpoint");
        var addWatch = C("debug.watch", "Add Watch...", "添加监视...", Keys.None, () =>
        {
            string sel = active != null && active.Editor.HasSelection ? active.Editor.SelectedText : "";
            string expr = Prompt.Show(this, L.T("Add watch", "添加监视"), L.T("Expression", "表达式"), sel);
            if (!string.IsNullOrWhiteSpace(expr)) AddWatch(expr.Trim());
        }, "search");
        var clearBps = C("debug.clearBreakpoints", "Remove All Breakpoints", "删除所有断点", Keys.None, () =>
        {
            foreach (var d in docs) { foreach (int l in d.Editor.Breakpoints.ToList()) d.Editor.ToggleBreakpoint(l); }
        }, "trash");

        var openSettings = C("tools.settings", "Settings...", "设置...", Keys.Control | Keys.Oemcomma, OpenSettings, "gear");
        var openTerminal = C("tools.terminal", "Open Terminal", "打开终端", Keys.None, () => ShowPanel("terminal"), "term");
        var openScratch = C("tools.scratch", "Open Scratch Folder", "打开草稿文件夹", Keys.None, () => { string d = Path.Combine(DefaultDocumentsFolder(), "scratch"); Directory.CreateDirectory(d); Process.Start("explorer.exe", "\"" + d + "\""); }, "folder");
        var openData = C("tools.data", "Open Settings Folder", "打开配置文件夹", Keys.None, () => { Directory.CreateDirectory(AppSettings.Folder); Process.Start("explorer.exe", "\"" + AppSettings.Folder + "\""); }, "folder");

        var shortcuts = C("help.shortcuts", "Keyboard Shortcuts", "键盘快捷键", Keys.None, () => palette.Open(">", L.T("All commands and their shortcuts", "全部命令及其快捷键")), "palette");
        var about = C("help.about", "About Gestirn dev", "关于 Gestirn dev", Keys.None, ShowAbout, "info");
        var repo = C("help.repo", "Project on GitHub", "GitHub 项目主页", Keys.None, () => Process.Start("https://github.com/Byton-Patrica/gestirn-dev"), "info");

        var recent = new ToolStripMenuItem(L.T("Open Recent", "最近打开")) { ForeColor = Theme.Text, Padding = Ui.Pad(2, 3, 2, 3) };
        recent.DropDown.BackColor = Theme.PanelHi;
        ((ToolStripDropDownMenu)recent.DropDown).ShowImageMargin = false;
        recent.DropDownItems.Add("-");
        recent.DropDownOpening += (_, _) =>
        {
            recent.DropDownItems.Clear();
            foreach (var p in settings.RecentFiles.Concat(settings.RecentFolders).Where(x => File.Exists(x) || Directory.Exists(x)).Take(15))
            {
                string path = p;
                var item = new ToolStripMenuItem(path, null, (_, _) => { if (Directory.Exists(path)) OpenFolder(path); else OpenFile(path); }) { ForeColor = Theme.Text };
                recent.DropDownItems.Add(item);
            }
            if (recent.DropDownItems.Count == 0) recent.DropDownItems.Add(new ToolStripMenuItem(L.T("(empty)", "（空）")) { Enabled = false });
        };

        menu.Items.Add(MenuOf("File", "文件", newFile, newProject, null, openFile, openFolder, openProject, recent, null, save, saveAs, saveAll, null, close, reopen, closeFolder, null, exit));
        menu.Items.Add(MenuOf("Edit", "编辑", undo, redo, null, cut, copy, paste, selectAll, null, find, replace, findNext, findPrev, findFiles, null, gotoLine, gotoSymbol, null, comment, duplicate, moveUp, moveDown, deleteLine, format, suggest));
        menu.Items.Add(MenuOf("View", "视图", commandPalette, quickOpen, null, viewExplorer, viewSearch, viewOutline, viewDebug, null, toggleSide, togglePanel, viewProblems, viewOutput, viewTerminal, null, zoomIn, zoomOut, zoomReset));
        menu.Items.Add(MenuOf("Build", "编译", compile, run, compileRun, rebuild, stop, null, buildSettings));
        menu.Items.Add(MenuOf("Debug", "调试", debugStart, stepOver, stepInto, stepOut, debugStop, null, toggleBp, addWatch, clearBps));
        menu.Items.Add(MenuOf("Tools", "工具", openSettings, format, openTerminal, null, openScratch, openData));
        menu.Items.Add(MenuOf("Help", "帮助", commandPalette, shortcuts, null, repo, about));

        foreach (var c in new[] { cut, copy, paste, null, comment, format, gotoLine, null, toggleBp, addWatch })
        {
            if (c == null) editorMenu.Items.Add(new ToolStripSeparator());
            else editorMenu.Items.Add(Item(c));
        }
        LayoutTopBar();
    }

    private void Ed(Action<CodeEditor> action)
    {
        if (active != null) action(active.Editor);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.Shift | Keys.Tab))
        {
            if (docs.Count > 1)
            {
                int i = docs.IndexOf(active);
                int step = (keyData & Keys.Shift) != 0 ? -1 : 1;
                Activate(docs[(i + step + docs.Count) % docs.Count]);
            }
            return true;
        }
        if (keyData == Keys.Escape && palette.Visible) { palette.Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------------------------------------------------------ file commands

    private void OpenFileDialog()
    {
        using (var dialog = new OpenFileDialog
        {
            Filter = L.T("C/C++ files and projects", "C/C++ 文件和项目") + "|*.c;*.cpp;*.cc;*.cxx;*.h;*.hpp;*.hh;*.dev|" + L.T("All files", "所有文件") + "|*.*",
            Multiselect = true,
            InitialDirectory = workspace ?? DefaultDocumentsFolder(),
        })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            foreach (var f in dialog.FileNames) OpenFile(f);
        }
    }

    private void OpenFolderDialog()
    {
        using (var dialog = new FolderBrowserDialog { Description = L.T("Choose a folder to work in", "选择要打开的文件夹"), SelectedPath = workspace ?? DefaultDocumentsFolder() })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            project = null;
            settings.Project = "";
            OpenFolder(dialog.SelectedPath);
            ShowView("explorer");
            ToggleSidebar(true);
        }
    }

    private void OpenProjectDialog()
    {
        using (var dialog = new OpenFileDialog { Filter = L.T("Dev-C++ project", "Dev-C++ 项目") + "|*.dev", InitialDirectory = workspace ?? DefaultDocumentsFolder() })
            if (dialog.ShowDialog(this) == DialogResult.OK) OpenProject(dialog.FileName);
    }

    private void NewProject()
    {
        using (var dialog = new NewProjectDialog(Path.Combine(DefaultDocumentsFolder(), "Projects")))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var p = Templates.Instantiate(dialog.Template, dialog.Folder, dialog.ProjectName, dialog.Cpp);
                OpenProject(p.FilePath);
                ShowView("explorer");
                ToggleSidebar(true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                toasts.Show(L.T("Can't create project", "无法创建项目"), ex.Message, ToastKind.Error);
            }
        }
    }

    private void ReopenClosed()
    {
        while (closedTabs.Count > 0)
        {
            string p = closedTabs.Pop();
            if (File.Exists(p)) { OpenFile(p); return; }
        }
    }

    private void ShowTabMenu(EditorDoc doc, Point p)
    {
        var m = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), Font = Theme.Ui9, BackColor = Theme.PanelHi, ShowImageMargin = false };
        m.Items.Add(L.T("Close", "关闭"), null, (_, _) => CloseDoc(doc));
        m.Items.Add(L.T("Close Others", "关闭其他"), null, (_, _) => { foreach (var d in docs.Where(x => x != doc).ToList()) if (!CloseDoc(d)) break; });
        m.Items.Add(L.T("Close All", "全部关闭"), null, (_, _) => { foreach (var d in docs.ToList()) if (!CloseDoc(d)) break; });
        if (doc.Path != null)
        {
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(L.T("Copy Path", "复制路径"), null, (_, _) => { try { Clipboard.SetText(doc.Path); } catch (System.Runtime.InteropServices.ExternalException) { } });
            m.Items.Add(L.T("Reveal in File Explorer", "在文件资源管理器中显示"), null, (_, _) => Process.Start("explorer.exe", "/select,\"" + doc.Path + "\""));
        }
        foreach (ToolStripItem i in m.Items) i.ForeColor = Theme.Text;
        m.Show(tabs, p);
    }

    private void OpenFind(bool replace)
    {
        if (active == null) return;
        findBar.Retarget(active.Editor);
        findBar.Open(replace, active.Editor.HasSelection ? active.Editor.SelectedText : null);
        LayoutEditorArea();
    }

    private void FindInFiles()
    {
        if (workspace == null)
        {
            toasts.Show(L.T("No folder open", "未打开文件夹"), L.T("Open a folder first to search across files.", "请先打开一个文件夹，才能跨文件搜索。"), ToastKind.Info);
            return;
        }
        ShowView("search");
        ToggleSidebar(true);
        if (active != null && active.Editor.HasSelection && active.Editor.SelectedText.IndexOf('\n') < 0)
        {
            search.Query.Text = active.Editor.SelectedText;
            search.Run();
        }
        search.Query.Box.Focus();
        search.Query.Box.SelectAll();
    }

    private void QuickOpen() { palette.Open("", L.T("Search files by name  ( > commands   : line   @ symbol )", "按文件名搜索（> 命令   : 行号   @ 符号）")); }

    // ------------------------------------------------------------------ palette

    private List<Palette.Entry> PaletteQuery(string q)
    {
        if (q.StartsWith(">"))
        {
            string term = q.Substring(1).Trim();
            var entries = commands.Select(c => new Palette.Entry { Title = c.Title.Replace("&&", "&"), Alt = c.English + " " + c.Id, Glyph = c.Glyph ?? "palette", Shortcut = c.Display ?? KeyText(c.Keys), Run = c.Run });
            return Palette.Rank(entries, term, 80);
        }
        if (q.StartsWith(":"))
        {
            var list = new List<Palette.Entry>();
            if (active == null) return list;
            int line;
            string t = q.Substring(1).Trim();
            if (int.TryParse(t, out line))
            {
                int target = Math.Max(1, Math.Min(active.Editor.LineCount, line));
                list.Add(new Palette.Entry { Title = string.Format(L.T("Go to line {0}", "转到第 {0} 行"), target), Glyph = "arrow", Run = () => { active.Editor.GoTo(target - 1, 0); active.Editor.Focus(); } });
            }
            else list.Add(new Palette.Entry { Title = string.Format(L.T("Type a line number between 1 and {0}", "输入 1 到 {0} 之间的行号"), active.Editor.LineCount), Glyph = "info" });
            return list;
        }
        if (q.StartsWith("@"))
        {
            if (active == null) return new List<Palette.Entry>();
            var ed = active.Editor;
            var symbols = Symbols.Parse(ed).Where(s => s.Kind != SymbolKind.Macro || q.Length > 1);
            var entries = symbols.Select(s => new Palette.Entry
            {
                Title = s.Name, Detail = s.Detail, Glyph = s.Kind == SymbolKind.Function ? "outline" : "project",
                Run = () => { ed.GoTo(s.Line, 0); ed.Focus(); },
            });
            return Palette.Rank(entries, q.Substring(1).Trim());
        }
        var files = new List<string>();
        files.AddRange(docs.Where(d => d.Path != null).Select(d => d.Path));
        files.AddRange(WorkspaceFiles());
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileEntries = new List<Palette.Entry>();
        foreach (var f in files)
        {
            if (!seen.Add(f)) continue;
            string path = f;
            string dir = Path.GetDirectoryName(f);
            if (workspace != null && dir.StartsWith(workspace, StringComparison.OrdinalIgnoreCase)) dir = dir.Substring(workspace.Length).TrimStart('\\');
            fileEntries.Add(new Palette.Entry { Title = Path.GetFileName(f), Detail = dir, Glyph = "file", Run = () => OpenFile(path) });
        }
        if (q.Trim().Length == 0) return fileEntries.Take(40).ToList();
        var ranked = Palette.Rank(fileEntries, q.Trim());
        if (ranked.Count == 0) return Palette.Rank(commands.Select(c => new Palette.Entry { Title = c.Title.Replace("&&", "&"), Alt = c.English + " " + c.Id, Glyph = c.Glyph ?? "palette", Shortcut = c.Display ?? KeyText(c.Keys), Run = c.Run }), q.Trim(), 20);
        return ranked;
    }

    private List<string> WorkspaceFiles()
    {
        if (workspace == null) return new List<string>();
        if (cachedFiles != null && (DateTime.UtcNow - cachedFilesAt).TotalSeconds < 10) return cachedFiles;
        var list = new List<string>();
        var stack = new Stack<string>();
        stack.Push(workspace);
        var skipExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".o", ".exe", ".obj", ".a", ".dll", ".pdb", ".ilk", ".res" };
        while (stack.Count > 0 && list.Count < 8000)
        {
            string dir = stack.Pop();
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                    if (!skipExt.Contains(Path.GetExtension(f)) && !Path.GetFileName(f).StartsWith(".")) list.Add(f);
                foreach (var d in Directory.GetDirectories(dir))
                {
                    string n = Path.GetFileName(d);
                    if (!n.StartsWith(".") && n != "node_modules") stack.Push(d);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        cachedFiles = list;
        cachedFilesAt = DateTime.UtcNow;
        return list;
    }

    // ------------------------------------------------------------------ build & run

    private BuildOptions Options(string compiler, bool debug)
    {
        return new BuildOptions
        {
            Compiler = compiler,
            Standard = settings.Standard == "auto" ? Toolchain.DefaultStandard(compiler) : settings.Standard,
            ExtraFlags = settings.ExtraFlags,
            Optimization = settings.Optimization,
            Debug = debug,
            StaticLink = settings.StaticLink,
            ExecCharset = settings.ExecCharset,
        };
    }

    private void CheckCompiler()
    {
        if (Toolchain.FindCompiler(settings.CompilerPath) != null) return;
        toasts.Show(L.T("No C/C++ compiler found", "未找到 C/C++ 编译器"),
            L.T("Install Dev-C++ (its MinGW64 is detected automatically) or MSYS2, or set the g++ path in Settings.", "请安装 Dev-C++（会自动识别其自带的 MinGW64）或 MSYS2，或在设置中指定 g++ 路径。"),
            ToastKind.Warning, 9000);
    }

    // Decides what F9/F11 builds: the open project if the file belongs to it, otherwise the current file.
    private bool ResolveTarget(out DevProject buildProject, out string source)
    {
        buildProject = null;
        source = null;
        if (project != null && (active == null || (active.Path != null && (project.Contains(active.Path) || !active.IsSource))))
        {
            buildProject = project;
            return true;
        }
        if (active == null)
        {
            toasts.Show(L.T("Nothing to compile", "没有可编译的内容"), L.T("Open or create a .cpp file first.", "请先打开或新建一个 .cpp 文件。"), ToastKind.Info);
            return false;
        }
        if (!EnsureOnDisk(active)) return false;
        if (!active.IsSource)
        {
            toasts.Show(L.T("Not a source file", "不是源文件"), L.T("Switch to a .c or .cpp file to compile it.", "请切换到 .c 或 .cpp 文件再编译。"), ToastKind.Info);
            return false;
        }
        source = active.Path;
        return true;
    }

    private async void Compile(bool run, bool rebuild, bool debug)
    {
        if (building) { status.Message = L.T("A build is already running", "正在编译中"); status.Invalidate(); return; }
        DevProject target;
        string source;
        if (!ResolveTarget(out target, out source)) return;
        SaveAllQuiet();
        string compiler = Toolchain.FindCompiler(settings.CompilerPath);
        if (compiler == null)
        {
            CheckCompiler();
            OpenSettings();
            return;
        }
        if (compiler != settings.CompilerPath) settings.CompilerPath = compiler;
        if (gdb != null && gdb.Alive) gdb.Stop();

        building = true;
        stopRequested = false;
        var button = debug ? debugBtn : run ? runBtn : buildBtn;
        button.Busy = true;
        status.Busy = true;
        topBar.Busy = true;
        string name = target != null ? target.Name : Path.GetFileName(source);
        status.Message = (debug ? L.T("Building for debugging: ", "正在为调试编译：") : L.T("Compiling ", "正在编译 ")) + name + "…";
        status.Invalidate();
        output.Clear();
        output.Append("▸ " + (debug ? L.T("Debug build of ", "调试构建 ") : L.T("Building ", "开始编译 ")) + name + "   [" + Options(compiler, debug).Standard + ", GCC " + Toolchain.Version(compiler) + "]", Theme.Orange);
        try
        {
            var opts = Options(compiler, debug);
            BuildOutcome result;
            if (target != null)
                result = await Toolchain.BuildProject(target, opts, rebuild || debug, line => output.AppendLog(line), p => { lock (buildProcs) buildProcs.Add(p); });
            else
            {
                result = await Toolchain.BuildSingle(source, opts, p => { lock (buildProcs) buildProcs.Add(p); });
                output.AppendLog(result.Log.ToString());
            }
            ApplyDiagnostics(result.Diagnostics);
            int errors = result.Diagnostics.Count(d => d.Severity == Severity.Error);
            int warnings = result.Diagnostics.Count(d => d.Severity == Severity.Warning);
            if (stopRequested)
            {
                output.Append(L.T("Build stopped.", "编译已停止。"), Theme.Muted);
                status.Message = L.T("Build stopped", "编译已停止");
            }
            else if (result.Success)
            {
                lastExe = result.ExePath;
                string secs = result.Elapsed.TotalSeconds.ToString("0.00");
                output.Append((result.UpToDate ? L.T("Up to date: ", "已是最新：") : L.T("Build succeeded: ", "编译成功：")) + result.ExePath + "  (" + secs + "s)", Theme.Lime);
                status.Message = L.T("Build succeeded", "编译成功") + "  ·  " + secs + "s" + (warnings > 0 ? "  ·  " + warnings + L.T(" warnings", " 个警告") : "");
                status.Flash(Theme.Lime);
                if (warnings > 0 && !run && !debug)
                    toasts.Show(L.T("Built with warnings", "编译成功，但有警告"), string.Format(L.T("{0} warnings — see Problems.", "{0} 个警告，详见“问题”面板。"), warnings), ToastKind.Warning);
                else if (!run && !debug)
                    toasts.Show(L.T("Build succeeded", "编译成功"), Path.GetFileName(result.ExePath) + "  ·  " + secs + "s", ToastKind.Success, 2200);
                if (debug) StartDebugging(result.ExePath, target != null ? target.Directory : Path.GetDirectoryName(source));
                else if (run) Launch(result.ExePath);
            }
            else
            {
                output.Append(L.T("Build failed.", "编译失败。"), Theme.Red);
                status.Message = string.Format(L.T("Build failed · {0} errors", "编译失败 · {0} 个错误"), Math.Max(errors, 1));
                status.Flash(Theme.Red);
                var first = result.Diagnostics.FirstOrDefault(d => d.Severity == Severity.Error);
                toasts.Show(string.Format(L.T("Build failed · {0} errors", "编译失败 · {0} 个错误"), Math.Max(errors, 1)),
                    first != null ? (first.Hint ?? first.Message) : L.T("See the Output panel for details.", "详情见“输出”面板。"), ToastKind.Error, 5000);
                ShowPanel(result.Diagnostics.Count > 0 ? "problems" : "output");
                if (first != null && first.File != null && first.Line >= 0 && File.Exists(first.File)) GoToDiagnostic(first);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is Win32Exception || ex is UnauthorizedAccessException)
        {
            output.Append("Gestirn dev: " + ex.Message, Theme.Red);
            toasts.Show(L.T("Build error", "编译出错"), ex.Message, ToastKind.Error);
        }
        finally
        {
            lock (buildProcs) buildProcs.Clear();
            building = false;
            button.Busy = false;
            status.Busy = false;
            topBar.Busy = false;
            status.Invalidate();
        }
    }

    private void RunLast()
    {
        DevProject target;
        string source;
        if (!ResolveTarget(out target, out source)) return;
        string exe = target != null ? target.OutputPath : Path.ChangeExtension(source, ".exe");
        bool fresh = File.Exists(exe) && !docs.Any(d => d.Dirty) &&
            (target != null ? target.Units.Select(u => target.FullPath(u.FileName)).Where(File.Exists).All(f => File.GetLastWriteTimeUtc(f) <= File.GetLastWriteTimeUtc(exe))
                            : File.GetLastWriteTimeUtc(source) <= File.GetLastWriteTimeUtc(exe));
        if (fresh) Launch(exe);
        else Compile(true, false, false);
    }

    private void Launch(string exe)
    {
        if (!File.Exists(exe)) { toasts.Show(L.T("Nothing to run", "没有可运行的程序"), exe, ToastKind.Warning); return; }
        if (project != null && project.Type != DevProject.TypeConsole && project.Type != DevProject.TypeGui)
        {
            toasts.Show(L.T("Built a library", "已生成库文件"), L.T("Libraries can't be run directly.", "库文件不能直接运行。"), ToastKind.Info);
            return;
        }
        try
        {
            programs.RemoveAll(p => { try { return p.HasExited; } catch (InvalidOperationException) { return true; } });
            var proc = Toolchain.RunProgram(exe, null, Path.GetDirectoryName(exe));
            proc.EnableRaisingEvents = true;
            proc.Exited += (_, _) => BeginInvoke(new Action(() => { status.Message = L.T("Program finished", "程序已结束"); status.Invalidate(); }));
            programs.Add(proc);
            output.Append(L.T("Running ", "正在运行 ") + Path.GetFileName(exe) + L.T(" in its own console.", "（独立控制台窗口）"), Theme.TextDim);
            status.Message = L.T("Running ", "运行中 ") + Path.GetFileName(exe);
            status.Invalidate();
        }
        catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
        {
            toasts.Show(L.T("Can't start the program", "无法启动程序"), ex.Message, ToastKind.Error);
        }
    }

    private void StopAll(bool quiet = false)
    {
        stopRequested = true;
        bool any = false;
        lock (buildProcs) foreach (var p in buildProcs.ToList()) { Toolchain.KillTree(p); any = true; }
        foreach (var p in programs.ToList())
        {
            try { if (!p.HasExited) { Toolchain.KillTree(p); any = true; } }
            catch (InvalidOperationException) { }
        }
        programs.Clear();
        if (gdb != null && gdb.Alive) { gdb.Stop(); any = true; }
        if (quiet) return;
        status.Message = any ? L.T("Stopped", "已停止") : L.T("Nothing is running", "没有正在运行的任务");
        status.Invalidate();
    }

    private void ApplyDiagnostics(List<Diagnostic> list)
    {
        lastDiagnostics = list;
        foreach (var d in docs) ApplyDiagnostics(d);
        problems.Set(list);
        UpdateProblemsStatus();
    }

    private void ApplyDiagnostics(EditorDoc d)
    {
        if (d.Path == null) { d.Editor.SetDiagnostics(new Diagnostic[0]); return; }
        d.Editor.SetDiagnostics(lastDiagnostics.Where(x => x.Severity != Severity.Note && x.Line >= 0 && x.File != null &&
            string.Equals(x.File, d.Path, StringComparison.OrdinalIgnoreCase)));
    }

    private void UpdateProblemsStatus()
    {
        int errors = lastDiagnostics.Count(d => d.Severity == Severity.Error);
        int warnings = lastDiagnostics.Count(d => d.Severity == Severity.Warning);
        status.Set("errors", errors.ToString(), "error", errors > 0 ? Theme.Red : Theme.Muted, false);
        status.Set("warnings", warnings.ToString(), "warning", warnings > 0 ? Theme.Gold : Theme.Muted, false);
        panelTabs.Counts["problems"] = errors + warnings;
        panelTabs.Invalidate();
    }

    private void GoToDiagnostic(Diagnostic d)
    {
        if (d.File != null && File.Exists(d.File))
        {
            var doc = OpenFile(d.File);
            if (doc != null && d.Line >= 0) { doc.Editor.GoTo(d.Line, Math.Max(0, d.Col - 1)); doc.Editor.Focus(); }
        }
        else toasts.Show(L.T("Problem", "问题"), d.Message + (d.Hint != null ? "\n" + d.Hint : ""), ToastKind.Warning, 5000);
    }

    // ------------------------------------------------------------------ debugging

    private void DebugStartOrContinue()
    {
        if (gdb != null && gdb.Alive)
        {
            if (gdb.Paused) DebugStep("continue");
            return;
        }
        Compile(false, false, true);
    }

    private void StartDebugging(string exe, string dir)
    {
        string compiler = Toolchain.FindCompiler(settings.CompilerPath);
        string gdbPath = Toolchain.FindGdb(compiler);
        if (gdbPath == null)
        {
            toasts.Show(L.T("gdb not found", "未找到 gdb"), L.T("The debugger (gdb.exe) should sit next to g++.exe.", "调试器 gdb.exe 应与 g++.exe 位于同一目录。"), ToastKind.Error);
            return;
        }
        var bps = new List<KeyValuePair<string, int>>();
        foreach (var d in docs.Where(d => d.Path != null))
            foreach (int l in d.Editor.Breakpoints) bps.Add(new KeyValuePair<string, int>(d.Path, l));
        gdb = new GdbSession(this);
        gdb.Output += text => output.Append(text, Theme.Muted);
        gdb.Stopped += OnDebugStopped;
        gdb.Resumed += () => { foreach (var d in docs) d.Editor.ExecLine = -1; status.Message = L.T("Running…", "运行中…"); status.Invalidate(); };
        gdb.Exited += OnDebugExited;
        SetDebugMode(true);
        debugPanel.Breakpoints = bps;
        debugPanel.Rebuild();
        try { gdb.Start(gdbPath, exe, dir, bps); }
        catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
        {
            toasts.Show(L.T("Can't start gdb", "无法启动 gdb"), ex.Message, ToastKind.Error);
            SetDebugMode(false);
            gdb = null;
            return;
        }
        output.Append(L.T("Debugging ", "调试 ") + Path.GetFileName(exe) + (bps.Count == 0 ? L.T("  (no breakpoints set: click left of a line number to add one)", "（未设置断点：点击行号左侧可添加）") : ""), Theme.Orange);
        status.Message = L.T("Debugging ", "调试中 ") + Path.GetFileName(exe);
        status.Invalidate();
    }

    private void SetDebugMode(bool on)
    {
        foreach (var b in debugActions) b.Visible = on;
        debugBtn.Visible = !on;
        debugPanel.Debugging = on;
        if (on) { rail.Badges.Add("debug"); ShowView("debug"); ToggleSidebar(true); }
        else
        {
            rail.Badges.Remove("debug");
            debugPanel.Locals.Clear();
            debugPanel.Frames.Clear();
        }
        debugPanel.Rebuild();
        rail.Invalidate();
        LayoutTopBar();
    }

    private void OnDebugStopped(GdbFrame frame, string reason)
    {
        foreach (var d in docs) d.Editor.ExecLine = -1;
        if (frame != null && File.Exists(frame.FullName))
        {
            var doc = OpenFile(frame.FullName);
            if (doc != null) doc.Editor.ExecLine = frame.Line;
        }
        string where = frame != null ? Path.GetFileName(frame.File) + ":" + (frame.Line + 1) : "?";
        status.Message = (reason == "breakpoint-hit" ? L.T("Paused at breakpoint  ·  ", "命中断点  ·  ") : L.T("Paused  ·  ", "已暂停  ·  ")) + where +
                         L.T("    F5 continue · F7 step over · F8 step into", "    F5 继续 · F7 单步跳过 · F8 单步进入");
        status.Flash(Theme.Orange);
        if (reason == "signal-received") toasts.Show(L.T("The program crashed", "程序崩溃了"), L.T("Stopped at ", "停在 ") + where + L.T(". Check the variables and call stack.", "，请查看变量和调用堆栈。"), ToastKind.Error, 6000);
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        if (gdb == null) return;
        gdb.Locals(list => { debugPanel.Locals = list; debugPanel.Rebuild(); });
        gdb.Frames(list => { debugPanel.Frames = list; debugPanel.Rebuild(); });
        for (int i = 0; i < debugPanel.Watches.Count; i++)
        {
            int index = i;
            string expr = debugPanel.Watches[i].Key;
            gdb.Evaluate(expr, value =>
            {
                if (index < debugPanel.Watches.Count && debugPanel.Watches[index].Key == expr)
                    debugPanel.Watches[index] = new KeyValuePair<string, string>(expr, value);
                debugPanel.Rebuild();
            });
        }
    }

    private void OnDebugExited(string code)
    {
        foreach (var d in docs) d.Editor.ExecLine = -1;
        SetDebugMode(false);
        gdb = null;
        string text = code == null ? L.T("Debugging stopped", "调试已停止") : string.Format(L.T("Program exited with code {0}", "程序已退出，返回值 {0}"), code);
        output.Append(text, Theme.Muted);
        status.Message = text;
        status.Invalidate();
        toasts.Show(L.T("Debug session ended", "调试结束"), text, ToastKind.Info, 2500);
    }

    private void DebugStep(string kind)
    {
        if (gdb == null || !gdb.Paused) return;
        foreach (var d in docs) d.Editor.ExecLine = -1;
        switch (kind)
        {
            case "continue": gdb.Continue(); break;
            case "over": gdb.StepOver(); break;
            case "into": gdb.StepInto(); break;
            case "out": gdb.StepOut(); break;
        }
    }

    private void AddWatch(string expr)
    {
        if (debugPanel.Watches.Any(w => w.Key == expr)) return;
        debugPanel.Watches.Add(new KeyValuePair<string, string>(expr, ""));
        debugPanel.Rebuild();
        if (gdb != null && gdb.Paused)
            gdb.Evaluate(expr, value =>
            {
                int i = debugPanel.Watches.FindIndex(w => w.Key == expr);
                if (i >= 0) debugPanel.Watches[i] = new KeyValuePair<string, string>(expr, value);
                debugPanel.Rebuild();
            });
    }

    private void OnBreakpointToggled(EditorDoc doc, int line)
    {
        if (doc.Path == null) return;
        bool added = doc.Editor.Breakpoints.Contains(line);
        if (gdb != null && gdb.Alive)
        {
            if (added) gdb.AddBreakpoint(doc.Path, line); else gdb.RemoveBreakpoint(doc.Path, line);
        }
        var bps = new List<KeyValuePair<string, int>>();
        foreach (var d in docs.Where(d => d.Path != null))
            foreach (int l in d.Editor.Breakpoints.OrderBy(x => x)) bps.Add(new KeyValuePair<string, int>(d.Path, l));
        debugPanel.Breakpoints = bps;
        debugPanel.Rebuild();
    }

    // ------------------------------------------------------------------ format, settings, misc

    private void FormatNow(EditorDoc doc, bool quiet)
    {
        string astyle = Toolchain.FindAStyle(Toolchain.FindCompiler(settings.CompilerPath));
        if (astyle == null)
        {
            if (!quiet) toasts.Show(L.T("Formatter not found", "未找到格式化工具"), L.T("AStyle.exe ships with Gestirn dev and Dev-C++.", "AStyle.exe 随 Gestirn dev 和 Dev-C++ 一起提供。"), ToastKind.Warning);
            return;
        }
        string text = doc.Editor.Text;
        string result = null;
        try
        {
            var psi = new ProcessStartInfo(astyle, "--style=kr " + (settings.InsertSpaces ? "--indent=spaces=" + settings.TabSize + " --convert-tabs" : "--indent=tab=" + settings.TabSize) +
                " --pad-oper --pad-header --unpad-paren --align-pointer=type --lineend=linux")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
            };
            using (var p = Process.Start(psi))
            {
                var reader = p.StandardOutput.ReadToEndAsync();
                var errReader = p.StandardError.ReadToEndAsync();
                using (var input = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false))) input.Write(text);
                if (reader.Wait(10000) && p.WaitForExit(10000) && p.ExitCode == 0) result = reader.Result;
            }
        }
        catch (Exception ex) when (ex is Win32Exception || ex is IOException || ex is InvalidOperationException)
        {
            if (!quiet) toasts.Show(L.T("Format failed", "格式化失败"), ex.Message, ToastKind.Error);
            return;
        }
        if (string.IsNullOrEmpty(result) && text.Trim().Length > 0) return;
        result = result.TrimEnd('\n') + "\n";
        if (result == text || result.TrimEnd() == text.TrimEnd()) { if (!quiet) { status.Message = L.T("Already formatted", "格式已规范"); status.Invalidate(); } return; }
        doc.Editor.ReplaceAllText(result);
        if (!quiet) { status.Message = L.T("Code formatted (Ctrl+Z to undo)", "代码已格式化（Ctrl+Z 可撤销）"); status.Flash(Theme.Lime); }
    }

    private void OpenSettings()
    {
        string oldLanguage = settings.Language;
        using (var dialog = new SettingsDialog(settings))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
        }
        foreach (var d in docs)
        {
            d.Editor.TabSize = settings.TabSize;
            d.Editor.InsertSpaces = settings.InsertSpaces;
            d.Editor.AutoPairs = settings.AutoPairs;
            d.Editor.AutoComplete = settings.AutoComplete;
            d.Editor.SetCodeFont(Theme.CodeFamily, settings.FontSize);
        }
        autosaveTimer.Interval = Math.Max(300, settings.AutoSaveDelay);
        settings.Save();
        UpdateChrome();
        UpdateCaretStatus();
        toasts.Show(L.T("Settings saved", "设置已保存"), oldLanguage != settings.Language ? L.T("Restart Gestirn dev to switch the language.", "重启 Gestirn dev 后切换语言。") : null, ToastKind.Success, 2400);
    }

    private void OnStatusClicked(string id)
    {
        switch (id)
        {
            case "pos": palette.Open(":", L.T("Type a line number", "输入行号")); break;
            case "errors":
            case "warnings": ShowPanel("problems"); break;
            case "std":
            case "compiler":
            case "indent": OpenSettings(); break;
            case "project": ShowView("explorer"); ToggleSidebar(true); break;
            case "eol":
                if (active == null) break;
                active.CrLf = !active.CrLf;
                if (active.Path != null) Save(active, false, true);
                UpdateCaretStatus();
                break;
        }
    }

    private void ShowAbout()
    {
        string compiler = Toolchain.FindCompiler(settings.CompilerPath);
        MessageBox.Show(this,
            "Gestirn dev " + Version + "\n\n" +
            L.T("A modern, lightweight C/C++ IDE built from the Dev-C++ source line.", "基于 Dev-C++ 源码线的现代、轻量 C/C++ 开发工具。") + "\n\n" +
            L.T("Compiler: ", "编译器：") + (compiler ?? L.T("not found", "未找到")) + "\n" +
            L.T("Settings: ", "配置：") + AppSettings.Folder + "\n\n" +
            "GPL-2.0 · Bloodshed / Orwell Dev-C++ · AStyle · GDB",
            L.T("About Gestirn dev", "关于 Gestirn dev"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
}
