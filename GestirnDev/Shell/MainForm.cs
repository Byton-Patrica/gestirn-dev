using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace GestirnDev
{
internal sealed partial class MainForm : Form
{
    public const string Version = "0.52";
    public static MainForm Instance;

    private readonly AppSettings settings;
    private readonly List<EditorDoc> docs = new List<EditorDoc>();
    private EditorDoc active;
    private DevProject project;
    private string workspace;
    private readonly CompletionProvider completion = new CompletionProvider();
    private readonly Stack<string> closedTabs = new Stack<string>();
    private Image logoImage;

    // chrome
    private readonly TopBar topBar = new TopBar();
    private MenuStrip menu;
    private readonly CommandPill pill = new CommandPill();
    private readonly List<PixelButton> actions = new List<PixelButton>();
    private readonly List<PixelButton> debugActions = new List<PixelButton>();
    private PixelButton runBtn, buildBtn, debugBtn, stopBtn;
    private readonly ToolTip tips = new ToolTip();
    private readonly ActivityRail rail = new ActivityRail();
    private readonly Card sidebar = new Card { Fill = Theme.Panel, Radius = 12, Outline = Theme.EdgeSoft };
    private readonly ExplorerPanel explorer = new ExplorerPanel();
    private readonly OutlinePanel outline = new OutlinePanel();
    private readonly SearchPanel search = new SearchPanel();
    private readonly DebugPanel debugPanel = new DebugPanel();
    private readonly DragHandle sideHandle = new DragHandle(true), panelHandle = new DragHandle(false);
    private readonly Card editorCard = new Card { Fill = Theme.EditorBg, Radius = 12, Outline = Theme.EdgeSoft };
    private readonly TabStrip tabs = new TabStrip();
    private readonly WelcomePage welcome = new WelcomePage();
    private readonly FindBar findBar = new FindBar();
    private readonly Card bottom = new Card { Fill = Theme.Panel, Radius = 12, Outline = Theme.EdgeSoft };
    private readonly PanelTabs panelTabs = new PanelTabs();
    private readonly OutputView output = new OutputView();
    private readonly ProblemsView problems = new ProblemsView();
    private readonly ProfileView profileView = new ProfileView();
    private readonly TerminalView terminal = new TerminalView();
    private readonly PixelButton panelClear = new PixelButton { Glyph = "clear", Kind = ButtonKind.Ghost, Radius = 7 };
    private readonly PixelButton panelClose = new PixelButton { Glyph = "close", Kind = ButtonKind.Ghost, Radius = 7 };
    private readonly StatusBar status = new StatusBar();
    private readonly Palette palette = new Palette();
    private ToastHost toasts;
    private readonly AnimatedFloat sideAnim = new AnimatedFloat(1f), panelAnim = new AnimatedFloat(1f), fade = new AnimatedFloat(0f);
    private ContextMenuStrip editorMenu;

    private readonly Timer autosaveTimer = new Timer();
    private readonly Timer outlineTimer = new Timer { Interval = 450 };
    private readonly Timer backupTimer = new Timer { Interval = 30000 };
    private bool closing;

    public MainForm(AppSettings settings, string[] args)
    {
        Instance = this;
        this.settings = settings;
        AutoScaleMode = AutoScaleMode.None;
        Text = "Gestirn dev";
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Ui9;
        Icon = LoadIcon();
        MinimumSize = new Size(Ui.D(900), Ui.D(600));
        KeyPreview = true;
        AllowDrop = true;
        Opacity = 0;
        DoubleBuffered = true;
        logoImage = LoadLogo();

        RestoreBounds2();
        BuildChrome();
        if (settings.TerminalInSidebar)
        {
            bottom.Controls.Remove(terminal);
            sidebar.Controls.Add(terminal);
            terminal.InSidebar = true;
            terminal.Visible = false;
        }
        BuildCommands();
        toasts = new ToastHost(this) { BottomInset = Ui.D(34) };
        completion.OtherDocumentWords = () => docs.Where(d => d != active).SelectMany(d => d.Editor.Words()).Distinct().Take(4000);

        autosaveTimer.Interval = Math.Max(300, settings.AutoSaveDelay);
        autosaveTimer.Tick += (_, _) => { autosaveTimer.Stop(); SaveAllQuiet(); };
        outlineTimer.Tick += (_, _) => { outlineTimer.Stop(); RefreshOutline(); };
        backupTimer.Tick += (_, _) => BackupUntitled();
        backupTimer.Start();
        sideAnim.Changed += LayoutShell;
        panelAnim.Changed += LayoutShell;
        sideAnim.Snap(settings.SidebarVisible ? 1f : 0f);
        panelAnim.Snap(settings.PanelVisible ? 1f : 0f);
        fade.Changed += () => { Opacity = Math.Min(1.0, fade.Value); };

        RestoreSession(args);
        UpdateChrome();
        LayoutShell();
    }

    private Icon LoadIcon()
    {
        string path = Path.Combine(Application.StartupPath, "GestirnEra-datarot.ico");
        try { return File.Exists(path) ? new Icon(path, Ui.D(32), Ui.D(32)) : null; }
        catch (ArgumentException) { return null; }
    }

    private Image LoadLogo()
    {
        string path = Path.Combine(Application.StartupPath, "GestirnEra-datarot-256.png");
        try
        {
            if (File.Exists(path)) using (var img = Image.FromFile(path)) return new Bitmap(img);
        }
        catch (OutOfMemoryException) { }
        return Icon != null ? Icon.ToBitmap() : null;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.StyleWindow(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Native.AttachHandles(this);
        LayoutShell();
        LayoutTopBar();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        fade.To(1f, 240);
        fade.Finished += () => Opacity = 1.0;
        if (active != null) active.Editor.Focus();
        CheckCompiler();
        ScheduleUpdateCheck();
    }

    // ------------------------------------------------------------------ chrome

    private void BuildChrome()
    {
        SuspendLayout();
        topBar.Logo = logoImage;
        menu = new MenuStrip
        {
            Dock = DockStyle.None, AutoSize = true, BackColor = Theme.Bg, ForeColor = Theme.TextDim, Renderer = new DarkMenuRenderer(),
            Font = Theme.Ui9, Padding = new Padding(0), GripStyle = ToolStripGripStyle.Hidden,
        };
        topBar.Controls.Add(menu);
        MainMenuStrip = menu;
        pill.Click += (_, _) => QuickOpen();
        topBar.Controls.Add(pill);

        buildBtn = TopButton("build", L.T("Compile", "编译"), L.T("Compile (F9)", "编译 (F9)"), ButtonKind.Secondary, () => Compile(false, false, false));
        runBtn = TopButton("run", L.T("Run", "运行"), L.T("Compile & Run (F11)", "编译并运行 (F11)"), ButtonKind.Primary, () => Compile(true, false, false));
        debugBtn = TopButton("debug", null, L.T("Debug (F5)", "调试 (F5)"), ButtonKind.Ghost, DebugStartOrContinue);
        stopBtn = TopButton("stop", null, L.T("Stop (F6)", "停止 (F6)"), ButtonKind.Ghost, () => StopAll());
        actions.AddRange(new[] { buildBtn, runBtn, debugBtn, stopBtn });
        var cont = TopButton("continue", null, L.T("Continue (F5)", "继续 (F5)"), ButtonKind.Ghost, () => DebugStep("continue"));
        var over = TopButton("stepover", null, L.T("Step over (F7)", "单步跳过 (F7)"), ButtonKind.Ghost, () => DebugStep("over"));
        var into = TopButton("stepinto", null, L.T("Step into (F8)", "单步进入 (F8)"), ButtonKind.Ghost, () => DebugStep("into"));
        var outBtn = TopButton("stepout", null, L.T("Step out (Shift+F8)", "单步跳出 (Shift+F8)"), ButtonKind.Ghost, () => DebugStep("out"));
        debugActions.AddRange(new[] { cont, over, into, outBtn });
        foreach (var b in debugActions) b.Visible = false;
        topBar.Resize += (_, _) => LayoutTopBar();

        rail.Add("explorer", "folder", L.T("Explorer (Ctrl+Shift+E)", "资源管理器 (Ctrl+Shift+E)"));
        rail.Add("search", "search", L.T("Search (Ctrl+Shift+F)", "搜索 (Ctrl+Shift+F)"));
        rail.Add("outline", "outline", L.T("Outline (Ctrl+Shift+O)", "大纲 (Ctrl+Shift+O)"));
        rail.Add("debug", "debug", L.T("Run and Debug", "运行和调试"));
        rail.Add("terminal", "term", L.T("Terminal (Ctrl+`)", "终端 (Ctrl+`)"));
        rail.Add("settings", "gear", L.T("Settings (Ctrl+,)", "设置 (Ctrl+,)"));
        rail.ItemClicked += OnRailClicked;

        foreach (Control p in new Control[] { explorer, outline, search, debugPanel }) { p.Visible = false; sidebar.Controls.Add(p); }
        sidebar.Resize += (_, _) => LayoutSidebar();
        viewSlide.Changed += LayoutSidebar;
        explorer.FileOpened += path => OpenFile(path);
        explorer.OpenFolderRequested += OpenFolderDialog;
        explorer.NewProjectRequested += NewProject;
        explorer.InProject = path => project != null && project.Contains(path);
        explorer.HasProject = () => project != null;
        explorer.ProjectMembershipChanged += SetProjectMembership;
        explorer.Renamed += OnFileRenamed;
        outline.LineActivated += line => { if (active != null) { active.Editor.GoTo(line, 0); active.Editor.Focus(); } };
        search.RootFolder = () => workspace;
        search.MatchActivated += (file, line, col) => OpenFile(file, line, col);
        debugPanel.WatchAdded += AddWatch;
        debugPanel.WatchRemoved += expr => { debugPanel.Watches.RemoveAll(w => w.Key == expr); debugPanel.Rebuild(); };
        debugPanel.FrameActivated += f => { if (File.Exists(f.FullName)) OpenFile(f.FullName, f.Line); };
        debugPanel.BreakpointActivated += (file, line) => OpenFile(file, line);

        sideHandle.Dragged += d =>
        {
            settings.SidebarWidth = Math.Max(180, Math.Min(1100, settings.SidebarWidth + (int)(d / Ui.Scale)));
            LayoutShell();
        };
        panelHandle.Dragged += d =>
        {
            settings.PanelHeight = Math.Max(110, Math.Min(Math.Max(140, (int)(ClientSize.Height / Ui.Scale) - 260), settings.PanelHeight - (int)(d / Ui.Scale)));
            LayoutShell();
        };

        tabs.Docs = () => docs;
        tabs.ActiveDoc = () => active;
        tabs.TabSelected += Activate;
        tabs.TabClosed += d => CloseDoc(d);
        tabs.TabContext += ShowTabMenu;
        editorCard.Controls.Add(tabs);
        editorCard.Controls.Add(welcome);
        editorCard.Controls.Add(findBar);
        editorCard.Resize += (_, _) => LayoutEditorArea();
        findBar.Slide.Changed += LayoutEditorArea;
        findBar.LayoutNeeded += LayoutEditorArea;
        welcome.Version = L.T("Version ", "版本 ") + Version + "  ·  " + L.T("a modern C/C++ IDE built on Dev-C++", "基于 Dev-C++ 的现代 C/C++ 开发工具");
        welcome.Shortcuts = L.T("F11 compile & run  ·  F5 debug  ·  Ctrl+P quick open  ·  Ctrl+Shift+P commands",
                                "F11 编译运行  ·  F5 调试  ·  Ctrl+P 快速打开  ·  Ctrl+Shift+P 命令面板");
        welcome.SetLogo(logoImage);
        welcome.AddAction("run", L.T("Quick start", "快速开始"), L.T("Hello World in one click", "一键创建 Hello World"), QuickStart);
        welcome.AddAction("new", L.T("New file", "新建文件"), "Ctrl+N", () => NewFile());
        welcome.AddAction("file", L.T("Open file", "打开文件"), "Ctrl+O", OpenFileDialog);
        welcome.AddAction("folder", L.T("Open folder", "打开文件夹"), L.T("Browse a project", "浏览项目文件"), OpenFolderDialog);
        welcome.AddAction("project", L.T("New project", "新建项目"), L.T("Dev-C++ templates", "Dev-C++ 模板"), NewProject);

        panelTabs.Add("problems", L.T("PROBLEMS", "问题"));
        panelTabs.Add("output", L.T("OUTPUT", "输出"));
        panelTabs.Add("terminal", L.T("TERMINAL", "终端"));
        panelTabs.Add("profile", L.T("PROFILE", "性能分析"));
        panelTabs.TabClicked += id => ShowPanel(id);
        tips.SetToolTip(panelClear, L.T("Clear", "清空"));
        tips.SetToolTip(panelClose, L.T("Hide panel (Ctrl+J)", "隐藏面板 (Ctrl+J)"));
        panelClear.Click += (_, _) => { if (panelTabs.Active == "terminal") terminal.Clear(); else if (panelTabs.Active == "profile") profileView.Clear(); else output.Clear(); };
        panelClose.Click += (_, _) => TogglePanel(false);
        foreach (Control c in new Control[] { panelTabs, output, problems, profileView, terminal, panelClear, panelClose }) bottom.Controls.Add(c);
        bottom.Resize += (_, _) => LayoutBottom();
        problems.Activated += GoToDiagnostic;
        profileView.FunctionActivated += e => GoToDefinition(e.ShortName);
        output.LineDoubleClicked += line =>
        {
            var d = Toolchain.ParseDiagnostics(line, project != null ? project.Directory : active != null && active.Path != null ? Path.GetDirectoryName(active.Path) : workspace ?? "").FirstOrDefault();
            if (d != null) GoToDiagnostic(d);
        };
        terminal.ExtraEnvironment = TerminalEnvironment;
        terminal.DefaultShell = settings.TerminalShell;
        terminal.MoveRequested += PlaceTerminal;
        terminal.StartFolder = () => workspace ?? (active != null && active.Path != null ? Path.GetDirectoryName(active.Path) : null);

        status.SegmentClicked += OnStatusClicked;
        palette.Provider = PaletteQuery;
        palette.Appear.Changed += LayoutShell;
        palette.Closed += () => { if (active != null) active.Editor.Focus(); };

        Controls.Add(palette);
        Controls.Add(topBar);
        Controls.Add(rail);
        Controls.Add(sidebar);
        Controls.Add(sideHandle);
        Controls.Add(editorCard);
        Controls.Add(panelHandle);
        Controls.Add(bottom);
        Controls.Add(status);
        palette.BringToFront();

        editorMenu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), Font = Theme.Ui9, BackColor = Theme.PanelHi, ShowImageMargin = false };
        ShowView(settings.SidebarView, false);
        ShowPanel("problems", false);
        ResumeLayout();
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        foreach (Control c in new Control[] { editorCard, welcome, tabs }) { c.AllowDrop = true; c.DragEnter += OnDragEnter; c.DragDrop += OnDragDrop; }
    }

    private PixelButton TopButton(string glyph, string text, string tip, ButtonKind kind, Action action)
    {
        var b = new PixelButton { Glyph = glyph, Text = text ?? "", Kind = kind, Radius = 9, Height = Ui.D(32) };
        b.Width = text == null ? Ui.D(34) : b.PreferredWidth();
        b.Click += (_, _) => action();
        tips.SetToolTip(b, tip);
        topBar.Controls.Add(b);
        return b;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutShell();
    }

    private void LayoutShell()
    {
        if (topBar == null || IsDisposed) return;
        int w = ClientSize.Width, h = ClientSize.Height;
        if (w <= 0 || h <= 0) return;
        int top = Ui.D(46), statusH = Ui.D(28), gap = Ui.D(8), railW = Ui.D(54);
        topBar.SetBounds(0, 0, w, top);
        status.SetBounds(0, h - statusH, w, statusH);
        rail.SetBounds(0, top, railW, h - top - statusH);
        int contentH = h - top - statusH - gap;
        int sideW = (int)(Ui.D(settings.SidebarWidth) * sideAnim.Value);
        bool sideOn = sideW > Ui.D(6);
        sidebar.Visible = sideOn;
        int x = railW;
        if (sideOn)
        {
            sidebar.SetBounds(x, top, sideW, contentH);
            x += sideW;
            sideHandle.SetBounds(x, top, gap, contentH);
            x += gap;
        }
        sideHandle.Visible = sideOn && sideAnim.Value > 0.98f;
        int right = w - gap;
        int panelH = (int)(Ui.D(settings.PanelHeight) * panelAnim.Value);
        bool panelOn = panelH > Ui.D(6);
        int editorH = contentH - (panelOn ? panelH + gap : 0);
        editorCard.SetBounds(x, top, Math.Max(10, right - x), Math.Max(10, editorH));
        bottom.Visible = panelOn;
        panelHandle.Visible = panelOn && panelAnim.Value > 0.98f;
        if (panelOn)
        {
            panelHandle.SetBounds(x, top + editorH, right - x, gap);
            bottom.SetBounds(x, top + editorH + gap, right - x, panelH);
        }
        int pw = Math.Min(Ui.D(640), w - Ui.D(80));
        palette.SetBounds((w - pw) / 2, top + Ui.D(4) - (int)((1 - palette.Appear.Value) * Ui.D(12)), pw, palette.Height);
        rail.SidebarOpen = sideOn;
        rail.Invalidate();
    }

    private void LayoutTopBar()
    {
        int h = topBar.Height;
        menu.Location = new Point(Ui.D(52), (h - menu.Height) / 2);
        int x = topBar.Width - Ui.D(10);
        foreach (var b in actions.AsEnumerable().Reverse())
        {
            x -= b.Width;
            b.Location = new Point(x, (h - b.Height) / 2);
            x -= Ui.D(6);
        }
        x -= Ui.D(4);
        foreach (var b in debugActions.AsEnumerable().Reverse())
        {
            if (!b.Visible) continue;
            x -= b.Width;
            b.Location = new Point(x, (h - b.Height) / 2);
            x -= Ui.D(4);
        }
        int pillW = Math.Min(Ui.D(420), Math.Max(Ui.D(160), x - menu.Right - Ui.D(40)));
        int center = (topBar.Width - pillW) / 2;
        center = Math.Max(menu.Right + Ui.D(16), Math.Min(center, x - pillW - Ui.D(16)));
        pill.SetBounds(center, (h - Ui.D(30)) / 2, pillW, Ui.D(30));
        pill.Visible = pillW >= Ui.D(160) && center + pillW < x;
    }

    private void LayoutSidebar()
    {
        var inner = new Rectangle(Ui.D(4) + (int)(viewSlide.Value * Ui.D(26)), Ui.D(4), Math.Max(0, sidebar.Width - Ui.D(8)), Math.Max(0, sidebar.Height - Ui.D(8)));
        foreach (Control c in sidebar.Controls) c.Bounds = inner;
    }

    private void LayoutEditorArea()
    {
        int w = editorCard.Width, h = editorCard.Height;
        tabs.SetBounds(Ui.D(6), Ui.D(4), Math.Max(10, w - Ui.D(12)), Ui.D(40));
        var area = new Rectangle(Ui.D(4), Ui.D(46), Math.Max(10, w - Ui.D(8)), Math.Max(10, h - Ui.D(50)));
        welcome.Bounds = area;
        foreach (var d in docs) d.Editor.Bounds = area;
        findBar.Location = new Point(area.Right - findBar.Width - Ui.D(18), area.Y + Ui.D(6) - (int)((1 - findBar.Slide.Value) * (findBar.Height + Ui.D(10))));
    }

    private void LayoutBottom()
    {
        int w = bottom.Width, h = bottom.Height;
        panelTabs.SetBounds(Ui.D(6), Ui.D(4), Math.Max(10, w - Ui.D(90)), Ui.D(34));
        panelClose.SetBounds(w - Ui.D(36), Ui.D(8), Ui.D(26), Ui.D(26));
        panelClear.SetBounds(w - Ui.D(64), Ui.D(8), Ui.D(26), Ui.D(26));
        var area = new Rectangle(Ui.D(4), Ui.D(40), Math.Max(10, w - Ui.D(8)), Math.Max(10, h - Ui.D(44)));
        output.Bounds = problems.Bounds = profileView.Bounds = area;
        if (terminal.Parent == bottom) terminal.Bounds = area;
        panelTabs.Select(panelTabs.Active, false);
    }

    private void ShowView(string id, bool animate = true)
    {
        if (id == null || id == "settings") id = "explorer";
        if (id == "terminal" && terminal.Parent != sidebar) id = "explorer";
        if (animate && id != settings.SidebarView) { viewSlide.Snap(1f); viewSlide.To(0f, 260); }
        settings.SidebarView = id;
        explorer.Visible = id == "explorer";
        search.Visible = id == "search";
        outline.Visible = id == "outline";
        debugPanel.Visible = id == "debug";
        if (terminal.Parent == sidebar) terminal.Visible = id == "terminal";
        rail.Active = id;
        LayoutSidebar();
        foreach (Control c in sidebar.Controls) if (c.Visible) { c.PerformLayout(); c.Invalidate(true); }
        if (id == "outline") RefreshOutline();
        if (id == "search") BeginInvoke(new Action(() => search.Query.Box.Focus()));
    }

    private void OnRailClicked(string id)
    {
        if (id == "settings") { OpenSettings(); return; }
        if (id == "terminal" && terminal.Parent != sidebar) { ShowPanel("terminal"); return; }
        if (id == settings.SidebarView && settings.SidebarVisible) { ToggleSidebar(false); return; }
        ShowView(id);
        ToggleSidebar(true);
    }

    private void ToggleSidebar(bool? show = null)
    {
        settings.SidebarVisible = show ?? !settings.SidebarVisible;
        sideAnim.To(settings.SidebarVisible ? 1f : 0f, 240, Ease.InOutCubic);
    }

    private void TogglePanel(bool? show = null)
    {
        settings.PanelVisible = show ?? !settings.PanelVisible;
        panelAnim.To(settings.PanelVisible ? 1f : 0f, 240, Ease.InOutCubic);
    }

    private void ShowPanel(string id, bool reveal = true)
    {
        if (id == "terminal" && terminal.Parent == sidebar)
        {
            if (!reveal) return;
            ShowView("terminal");
            ToggleSidebar(true);
            terminal.FocusInput();
            return;
        }
        panelTabs.Select(id, true);
        output.Visible = id == "output";
        problems.Visible = id == "problems";
        profileView.Visible = id == "profile";
        if (terminal.Parent == bottom) terminal.Visible = id == "terminal";
        panelClear.Visible = id != "problems";
        LayoutBottom();
        foreach (Control c in new Control[] { output, problems, profileView, terminal }) if (c.Visible) { c.PerformLayout(); c.Invalidate(true); }
        if (reveal && !settings.PanelVisible) TogglePanel(true);
        if (id == "terminal" && reveal) terminal.FocusInput();
    }

    // ------------------------------------------------------------------ documents

    private CodeEditor CreateEditor()
    {
        var ed = new CodeEditor
        {
            Provider = completion,
            TabSize = settings.TabSize,
            InsertSpaces = settings.InsertSpaces,
            AutoPairs = settings.AutoPairs,
            AutoComplete = settings.AutoComplete,
            Visible = false,
            ContextMenuStrip = editorMenu,
            AllowDrop = true,
        };
        ed.SetCodeFont(Theme.CodeFamily, settings.FontSize);
        ed.DragEnter += OnDragEnter;
        ed.DragDrop += OnDragDrop;
        editorCard.Controls.Add(ed);
        findBar.BringToFront();
        return ed;
    }

    private void WireEditor(EditorDoc doc)
    {
        var ed = doc.Editor;
        ed.TextChangedEx += (_, _) =>
        {
            tabs.Invalidate();
            UpdateTitle();
            if (settings.AutoSave && doc.Path != null) { autosaveTimer.Stop(); autosaveTimer.Start(); }
            if (doc == active) { outlineTimer.Stop(); outlineTimer.Start(); }
        };
        ed.CaretMoved += (_, _) => { if (doc == active) UpdateCaretStatus(); };
        ed.ZoomChanged += (_, _) =>
        {
            settings.FontSize = ed.FontSizePt;
            foreach (var d in docs) if (d.Editor != ed) d.Editor.SetCodeFont(Theme.CodeFamily, settings.FontSize);
            status.Message = L.T("Font size ", "字号 ") + Math.Round(settings.FontSize);
            status.Invalidate();
        };
        ed.BreakpointToggled += line => OnBreakpointToggled(doc, line);
        ed.EscapePressed += (_, _) => { if (findBar.Visible) findBar.Close(); };
        ed.DefinitionRequested += word => GoToDefinition(word);
    }

    public EditorDoc OpenFile(string path, int line = -1, int col = 0)
    {
        try { path = Path.GetFullPath(path); }
        catch (ArgumentException) { return null; }
        if (path.EndsWith(".dev", StringComparison.OrdinalIgnoreCase)) { OpenProject(path); return null; }
        var doc = docs.FirstOrDefault(d => d.Path != null && string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
        if (doc == null)
        {
            if (!File.Exists(path)) { toasts.Show(L.T("File not found", "找不到文件"), path, ToastKind.Warning); return null; }
            var info = new FileInfo(path);
            if (info.Length > 30 * 1024 * 1024) { toasts.Show(L.T("File too large", "文件过大"), path, ToastKind.Warning); return null; }
            string text;
            try { text = ReadSource(path); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                toasts.Show(L.T("Can't open file", "无法打开文件"), ex.Message, ToastKind.Error);
                return null;
            }
            doc = new EditorDoc(path, CreateEditor()) { CrLf = text.Contains("\r\n") || !text.Contains("\n"), Gbk = !IsUtf8File(path) };
            doc.Editor.LoadText(text);
            doc.Editor.MarkSaved();
            doc.DiskTime = File.GetLastWriteTimeUtc(path);
            WireEditor(doc);
            ReplaceBlankUntitled();
            docs.Add(doc);
            settings.AddRecent(settings.RecentFiles, path, 15);
        }
        Activate(doc);
        if (line >= 0) doc.Editor.GoTo(line, col);
        return doc;
    }

    // An empty, untouched untitled tab is replaced instead of piling up.
    private void ReplaceBlankUntitled()
    {
        var blank = docs.FirstOrDefault(d => d.Path == null && !d.Dirty && d.Editor.LineCount == 1 && d.Editor.LineText(0).Length == 0);
        if (blank != null && docs.Count == 1) RemoveDoc(blank);
    }

    // True when the file is plain ASCII or valid UTF-8; otherwise it is kept in the system code page (GBK).
    private static bool IsUtf8File(string path)
    {
        try { new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path)); return true; }
        catch (DecoderFallbackException) { return false; }
        catch (IOException) { return true; }
    }

    public static string ReadSource(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Toolchain.ConsoleEncoding.GetString(bytes); }
        return text.Length > 0 && text[0] == '﻿' ? text.Substring(1) : text;
    }

    private EditorDoc NewFile(string text = "")
    {
        var doc = new EditorDoc(null, CreateEditor());
        doc.Editor.LoadText(text);
        if (text.Length == 0) doc.Editor.MarkSaved();
        WireEditor(doc);
        docs.Add(doc);
        Activate(doc);
        return doc;
    }

    private void QuickStart()
    {
        var doc = NewFile("#include <iostream>\nusing namespace std;\n\nint main()\n{\n    cout << \"Hello, Gestirn dev!\" << endl;\n    return 0;\n}\n");
        doc.Editor.GoTo(5, 34);
        toasts.Show(L.T("Ready to run", "可以运行了"), L.T("Press F11 (or the Run button) to compile and run.", "按 F11（或右上角“运行”）即可编译运行。"), ToastKind.Info);
    }

    private void Activate(EditorDoc doc)
    {
        if (closing) return;
        active = doc;
        foreach (var d in docs) d.Editor.Visible = d == doc;
        welcome.Visible = doc == null;
        if (doc == null) welcome.BringToFront();
        LayoutEditorArea();
        tabs.SyncIndicator(true);
        findBar.Retarget(doc != null ? doc.Editor : null);
        if (findBar.Visible && doc == null) findBar.Close();
        UpdateTitle();
        UpdateCaretStatus();
        RefreshOutline();
        UpdateWelcome();
        if (doc != null)
        {
            doc.Editor.Focus();
            if (doc.Path != null && explorer.Visible && settings.SidebarVisible) explorer.Reveal(doc.Path);
        }
    }

    private bool CloseDoc(EditorDoc doc, bool force = false)
    {
        if (doc == null) return true;
        if (doc.Dirty && !force)
        {
            if (doc.Path != null && settings.AutoSave) Save(doc, false, true);
            else
            {
                Activate(doc);
                var answer = MessageBox.Show(this, string.Format(L.T("Save changes to {0}?", "是否保存对 {0} 的修改？"), doc.Title), "Gestirn dev",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return false;
                if (answer == DialogResult.Yes && !Save(doc, false)) return false;
            }
        }
        if (doc.Path != null) closedTabs.Push(doc.Path);
        int index = docs.IndexOf(doc);
        RemoveDoc(doc);
        if (active == doc || active == null)
            Activate(docs.Count == 0 ? null : docs[Math.Max(0, Math.Min(index, docs.Count - 1))]);
        return true;
    }

    private void RemoveDoc(EditorDoc doc)
    {
        docs.Remove(doc);
        editorCard.Controls.Remove(doc.Editor);
        doc.Editor.Dispose();
        if (active == doc) active = null;
        tabs.SyncIndicator(false);
    }

    public bool Save(EditorDoc doc, bool saveAs, bool quiet = false)
    {
        if (doc == null) return false;
        string path = doc.Path;
        if (path == null || saveAs)
        {
            using (var dialog = new SaveFileDialog
            {
                Filter = L.T("C++ source", "C++ 源文件") + "|*.cpp;*.cc;*.cxx|" + L.T("C source", "C 源文件") + "|*.c|" + L.T("Header", "头文件") + "|*.h;*.hpp|" + L.T("All files", "所有文件") + "|*.*",
                FileName = doc.Path != null ? Path.GetFileName(doc.Path) : "main.cpp",
                InitialDirectory = doc.Path != null ? Path.GetDirectoryName(doc.Path) : (workspace ?? DefaultDocumentsFolder()),
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                path = dialog.FileName;
            }
        }
        if (!quiet && settings.FormatOnSave && doc.IsSourceOrUntitledCode()) FormatNow(doc, true);
        if (!WriteFile(doc, path, quiet)) return false;
        if (!quiet) status.Message = L.T("Saved ", "已保存 ") + Path.GetFileName(path);
        status.Invalidate();
        return true;
    }

    private bool WriteFile(EditorDoc doc, string path, bool quiet)
    {
        try
        {
            File.WriteAllText(path, doc.Editor.GetText(doc.CrLf ? "\r\n" : "\n"), doc.Gbk ? Toolchain.ConsoleEncoding : new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            toasts.Show(L.T("Save failed", "保存失败"), ex.Message, ToastKind.Error);
            return false;
        }
        bool renamed = doc.Path == null || !string.Equals(doc.Path, path, StringComparison.OrdinalIgnoreCase);
        doc.Path = path;
        doc.Editor.MarkSaved();
        doc.DiskTime = File.GetLastWriteTimeUtc(path);
        if (renamed)
        {
            settings.AddRecent(settings.RecentFiles, path, 15);
            if (project != null && DevProject.IsSource(path) && path.StartsWith(project.Directory, StringComparison.OrdinalIgnoreCase) && !project.Contains(path))
                SetProjectMembership(path, true);
        }
        tabs.SyncIndicator(false);
        UpdateTitle();
        return true;
    }

    private void SaveAllQuiet()
    {
        foreach (var d in docs.Where(d => d.Path != null && d.Dirty).ToList()) Save(d, false, true);
        tabs.Invalidate();
        UpdateTitle();
    }

    private static string DefaultDocumentsFolder() { return AppSettings.DocumentsFolder; }

    // Untitled code is saved to Documents\Gestirn dev\scratch when it needs a file (to compile), no dialog.
    private bool EnsureOnDisk(EditorDoc doc)
    {
        if (doc.Path != null) return true;
        string dir = Path.Combine(DefaultDocumentsFolder(), "scratch");
        Directory.CreateDirectory(dir);
        bool looksC = doc.Editor.Text.Contains("#include <stdio.h>") && !doc.Editor.Text.Contains("iostream");
        string path;
        int n = doc.Number;
        do { path = Path.Combine(dir, "untitled" + n++ + (looksC ? ".c" : ".cpp")); } while (File.Exists(path));
        if (!WriteFile(doc, path, true)) return false;
        toasts.Show(L.T("Saved to scratch", "已保存到草稿目录"), path, ToastKind.Info, 4500);
        return true;
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (closing) return;
        foreach (var d in docs.Where(d => d.Path != null).ToList())
        {
            DateTime t;
            try { if (!File.Exists(d.Path)) continue; t = File.GetLastWriteTimeUtc(d.Path); }
            catch (IOException) { continue; }
            if (t == d.DiskTime) continue;
            d.DiskTime = t;
            if (!d.Dirty)
            {
                try
                {
                    string text = ReadSource(d.Path);
                    if (text.Replace("\r\n", "\n") == d.Editor.Text) continue;
                    d.Editor.ReplaceAllText(text);
                    d.Editor.MarkSaved();
                    toasts.Show(L.T("Reloaded", "已重新载入"), d.Title + L.T(" changed on disk.", " 在磁盘上被修改。"), ToastKind.Info);
                }
                catch (IOException) { }
            }
            else toasts.Show(L.T("File changed on disk", "文件在磁盘上被修改"), d.Title + L.T(" has unsaved edits here; saving will overwrite the disk version.", " 在此处有未保存修改，保存将覆盖磁盘上的版本。"), ToastKind.Warning, 6000);
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (settings.AutoSave && !closing) SaveAllQuiet();
    }

    private void OnFileRenamed(string from, string to)
    {
        var doc = docs.FirstOrDefault(d => d.Path != null && string.Equals(d.Path, from, StringComparison.OrdinalIgnoreCase));
        if (doc != null) { doc.Path = to; doc.DiskTime = File.Exists(to) ? File.GetLastWriteTimeUtc(to) : doc.DiskTime; tabs.SyncIndicator(false); UpdateTitle(); }
        if (project != null && project.Contains(from))
        {
            project.Units.RemoveAll(u => string.Equals(project.FullPath(u.FileName), from, StringComparison.OrdinalIgnoreCase));
            project.AddUnit(to);
            project.Save();
        }
    }

    // ------------------------------------------------------------------ workspace & project

    private void OpenFolder(string folder, bool remember = true)
    {
        if (!Directory.Exists(folder)) return;
        workspace = folder;
        explorer.SetRoot(folder, project != null ? project.Name : null);
        if (remember)
        {
            settings.Workspace = folder;
            settings.AddRecent(settings.RecentFolders, folder, 10);
        }
        cachedFiles = null;
        UpdateChrome();
    }

    private void OpenProject(string path)
    {
        try { project = DevProject.Load(path); }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            toasts.Show(L.T("Can't open project", "无法打开项目"), ex.Message, ToastKind.Error);
            return;
        }
        settings.Project = project.FilePath;
        settings.AddRecent(settings.RecentFiles, project.FilePath, 15);
        OpenFolder(project.Directory);
        var first = project.Units.Select(u => project.FullPath(u.FileName)).FirstOrDefault(f => File.Exists(f) && DevProject.IsSource(f));
        if (first != null && docs.All(d => d.Path == null)) OpenFile(first);
        toasts.Show(L.T("Project opened", "已打开项目"), project.Name + "  ·  " + project.Units.Count + L.T(" files", " 个文件"), ToastKind.Success);
        UpdateChrome();
    }

    private void CloseFolder()
    {
        project = null;
        workspace = null;
        settings.Project = settings.Workspace = "";
        explorer.SetRoot(null, null);
        UpdateChrome();
    }

    private void SetProjectMembership(string path, bool add)
    {
        if (project == null) return;
        if (add) project.AddUnit(path);
        else project.Units.RemoveAll(u => string.Equals(project.FullPath(u.FileName), path, StringComparison.OrdinalIgnoreCase));
        try { project.Save(); }
        catch (IOException ex) { toasts.Show(L.T("Can't save project", "无法保存项目"), ex.Message, ToastKind.Error); }
        explorer.InvalidateList();
        status.Message = (add ? L.T("Added to project: ", "已加入项目：") : L.T("Removed from project: ", "已从项目移除：")) + Path.GetFileName(path);
        status.Invalidate();
    }

    // ------------------------------------------------------------------ session

    private void RestoreBounds2()
    {
        var b = settings.WindowBounds;
        var screen = Screen.FromPoint(b.Location).WorkingArea;
        if (b.Width > Ui.D(600) && b.Height > Ui.D(400) && screen.IntersectsWith(b))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = b;
        }
        else
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(area.Width - Ui.D(80), Ui.D(1440)), Math.Min(area.Height - Ui.D(60), Ui.D(900)));
            StartPosition = FormStartPosition.CenterScreen;
        }
        if (settings.Maximized) WindowState = FormWindowState.Maximized;
    }

    private void RestoreSession(string[] args)
    {
        if (!string.IsNullOrEmpty(settings.Project) && File.Exists(settings.Project) && settings.RestoreSession)
        {
            try { project = DevProject.Load(settings.Project); }
            catch (IOException) { project = null; }
        }
        string folder = project != null ? project.Directory : settings.Workspace;
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder) && settings.RestoreSession) OpenFolder(folder);

        if (settings.RestoreSession)
        {
            foreach (var f in settings.Session)
            {
                if (!File.Exists(f.Path)) continue;
                var doc = OpenFile(f.Path);
                if (doc == null) continue;
                doc.Editor.SetBreakpoints(f.Breakpoints);
                doc.Editor.GoTo(f.Line, f.Col);
            }
            foreach (var name in settings.UntitledBackups)
            {
                string file = Path.Combine(AppSettings.BackupFolder, name);
                if (!File.Exists(file)) continue;
                try
                {
                    var doc = NewFile();
                    doc.Editor.LoadText(File.ReadAllText(file, Encoding.UTF8));
                }
                catch (IOException) { }
            }
            var activeDoc = docs.FirstOrDefault(d => d.Path != null && string.Equals(d.Path, settings.ActiveFile, StringComparison.OrdinalIgnoreCase));
            if (activeDoc != null) Activate(activeDoc);
        }
        foreach (var a in args ?? new string[0])
        {
            if (Directory.Exists(a)) OpenFolder(Path.GetFullPath(a));
            else if (File.Exists(a)) OpenFile(a);
        }
        if (docs.Count == 0) Activate(null);
    }

    private void BackupUntitled()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.BackupFolder);
            foreach (var old in Directory.GetFiles(AppSettings.BackupFolder, "untitled-*.txt")) File.Delete(old);
            settings.UntitledBackups.Clear();
            int i = 1;
            foreach (var d in docs.Where(d => d.Path == null && d.Editor.Text.Trim().Length > 0))
            {
                string name = "untitled-" + i++ + ".txt";
                File.WriteAllText(Path.Combine(AppSettings.BackupFolder, name), d.Editor.Text, new UTF8Encoding(false));
                settings.UntitledBackups.Add(name);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void EmergencyBackup()
    {
        try
        {
            BackupUntitled();
            foreach (var d in docs.Where(d => d.Path != null && d.Dirty))
                File.WriteAllText(d.Path + ".gestirn-recovered", d.Editor.GetText("\r\n"), new UTF8Encoding(false));
            SaveSessionSettings();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
    }

    private void SaveSessionSettings()
    {
        settings.Session = docs.Where(d => d.Path != null).Select(d => new SessionFile
        {
            Path = d.Path, Line = d.Editor.Caret.Line, Col = d.Editor.Caret.Col, Breakpoints = d.Editor.Breakpoints.OrderBy(b => b).ToList(),
        }).ToList();
        settings.ActiveFile = active != null && active.Path != null ? active.Path : "";
        settings.Maximized = WindowState == FormWindowState.Maximized;
        if (WindowState == FormWindowState.Normal) settings.WindowBounds = Bounds;
        settings.Save();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        foreach (var d in docs.Where(d => d.Path != null && d.Dirty).ToList())
        {
            if (settings.AutoSave) { Save(d, false, true); continue; }
            Activate(d);
            var answer = MessageBox.Show(this, string.Format(L.T("Save changes to {0}?", "是否保存对 {0} 的修改？"), d.Title), "Gestirn dev",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) { e.Cancel = true; return; }
            if (answer == DialogResult.Yes && !Save(d, false)) { e.Cancel = true; return; }
        }
        closing = true;
        if (!settings.RestoreSession)
        {
            foreach (var d in docs.Where(d => d.Path == null && d.Dirty && d.Editor.Text.Trim().Length > 0).ToList())
            {
                Activate(d);
                var answer = MessageBox.Show(this, string.Format(L.T("Save changes to {0}?", "是否保存对 {0} 的修改？"), d.Title), "Gestirn dev",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) { e.Cancel = true; closing = false; return; }
                if (answer == DialogResult.Yes && !Save(d, false)) { e.Cancel = true; closing = false; return; }
            }
        }
        BackupUntitled();
        if (!settings.RestoreSession) settings.UntitledBackups.Clear();
        SaveSessionSettings();
        StopAll(true);
        terminal.Kill();
    }

    // ------------------------------------------------------------------ status & title

    private void UpdateTitle()
    {
        string name = active != null ? active.Title + (active.Dirty ? " ●" : "") : null;
        string where = project != null ? project.Name : workspace != null ? Path.GetFileName(workspace.TrimEnd('\\')) : null;
        Text = (name != null ? name + "  —  " : "") + (where != null ? where + "  —  " : "") + "Gestirn dev " + Version;
        pill.Caption = where ?? "Gestirn dev";
        pill.Detail = active != null ? active.Title : L.T("Search files and commands", "搜索文件和命令");
        pill.Invalidate();
    }

    private void UpdateCaretStatus()
    {
        if (active == null)
        {
            foreach (var id in new[] { "pos", "indent", "eol", "enc" }) status.Remove(id);
            return;
        }
        var c = active.Editor.Caret;
        string sel = active.Editor.HasSelection ? L.T("  (" + active.Editor.SelectedText.Length + " selected)", "（已选 " + active.Editor.SelectedText.Length + "）") : "";
        status.Set("pos", L.T("Ln " + (c.Line + 1) + ", Col " + (c.Col + 1), "行 " + (c.Line + 1) + "，列 " + (c.Col + 1)) + sel);
        status.Set("indent", (settings.InsertSpaces ? L.T("Spaces: ", "空格: ") : "Tab: ") + settings.TabSize);
        status.Set("eol", active.CrLf ? "CRLF" : "LF");
        status.Set("enc", active.Gbk ? "GBK" : "UTF-8");
    }

    private void UpdateChrome()
    {
        string compiler = Toolchain.FindCompiler(settings.CompilerPath);
        string ver = compiler != null ? Toolchain.Version(compiler) : "";
        string std = settings.Standard == "auto" ? (compiler != null ? Toolchain.DefaultStandard(compiler) : "c++14") : settings.Standard;
        status.Set("std", std.ToUpperInvariant().Replace("C++", "C++"), null, Theme.TextDim);
        status.Set("compiler", compiler != null ? "GCC " + ver : L.T("No compiler", "未找到编译器"), compiler != null ? "build" : "warning", compiler != null ? Theme.TextDim : Theme.Gold);
        if (project != null) status.Set("project", project.Name, "project", Theme.Lime, false);
        else status.Remove("project");
        UpdateProblemsStatus();
        UpdateTitle();
        UpdateWelcome();
    }

    private void UpdateWelcome()
    {
        welcome.Recent.Clear();
        foreach (var f in settings.RecentFiles.Concat(settings.RecentFolders).Where(p => File.Exists(p) || Directory.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(7))
        {
            string path = f;
            welcome.Recent.Add(Tuple.Create(Path.GetFileName(path.TrimEnd('\\')), path, (Action)(() =>
            {
                if (Directory.Exists(path)) OpenFolder(path); else OpenFile(path);
            })));
        }
        welcome.Invalidate();
    }

    private void RefreshOutline()
    {
        if (!outline.Visible || active == null) { if (active == null) outline.SetSymbols(null); return; }
        outline.SetSymbols(Symbols.Parse(active.Editor));
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object sender, DragEventArgs e)
    {
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files == null) return;
        foreach (var f in files)
        {
            if (Directory.Exists(f)) OpenFolder(f);
            else OpenFile(f);
        }
    }

    // ------------------------------------------------------------------ small chrome controls

    private sealed class TopBar : Panel
    {
        public Image Logo;
        private bool busy;
        private double phase;
        private readonly Action<double> ticker;
        private readonly Random random = new Random();

        public TopBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            ticker = now => { phase = now; Invalidate(new Rectangle(0, 0, Ui.D(56), Height)); };
        }

        private readonly List<float[]> particles = new List<float[]>();
        private readonly Random rnd = new Random();
        private Action<double> particleTicker;
        private double lastFrame;

        // A burst of logo-coloured pixels: x, y, vx, vy, born, colour index.
        public void Burst(Point origin)
        {
            if (Animator.Reduced) return;
            double now = Animator.Now;
            for (int i = 0; i < 26; i++)
            {
                double a = rnd.NextDouble() * Math.PI * 2, speed = (0.25 + rnd.NextDouble() * 0.55) * Ui.Scale;
                particles.Add(new[] { origin.X, origin.Y, (float)(Math.Cos(a) * speed), (float)(Math.Sin(a) * speed - 0.35 * Ui.Scale), (float)now, rnd.Next(3) });
            }
            if (particleTicker == null)
            {
                lastFrame = now;
                particleTicker = t =>
                {
                    float dt = (float)Math.Min(40, t - lastFrame);
                    lastFrame = t;
                    foreach (var p in particles) { p[0] += p[2] * dt; p[1] += p[3] * dt; p[3] += 0.0016f * Ui.Scale * dt; }
                    particles.RemoveAll(p => t - p[4] > 900);
                    Invalidate();
                    if (particles.Count == 0) { Animator.RemoveTicker(particleTicker); particleTicker = null; }
                };
                Animator.AddTicker(particleTicker);
            }
        }

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

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            int size = Ui.D(26);
            var r = new Rectangle(Ui.D(14), (Height - size) / 2, size, size);
            if (Logo != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(Logo, r);
            }
            if (busy)
            {
                // Logo pixels "compile": a few lime and orange pixels blink around the mark.
                int px = Math.Max(2, Ui.D(2.5f));
                int frame = (int)(phase / 120);
                var rnd = new Random(frame);
                for (int i = 0; i < 5; i++)
                {
                    int x = r.X - Ui.D(6) + rnd.Next(size + Ui.D(12));
                    int y = r.Y - Ui.D(6) + rnd.Next(size + Ui.D(12));
                    if (r.Contains(x, y)) continue;
                    using (var b = new SolidBrush(i % 2 == 0 ? Theme.Lime : Theme.Orange)) g.FillRectangle(b, x, y, px, px);
                }
            }
            double now2 = Animator.Now;
            int size2 = Math.Max(2, Ui.D(3));
            foreach (var p in particles)
            {
                float life = (float)((now2 - p[4]) / 900.0);
                var c = p[5] == 0 ? Theme.Lime : p[5] == 1 ? Theme.Orange : Theme.Gold;
                using (var b = new SolidBrush(Ui.Alpha(c, (int)(255 * Math.Max(0, 1 - life))))) g.FillRectangle(b, p[0], p[1], size2, size2);
            }
            using (var pen = new Pen(Theme.EdgeSoft)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }

        protected override void Dispose(bool disposing) { if (disposing) Animator.RemoveTicker(ticker); base.Dispose(disposing); }
    }

    private sealed class CommandPill : Control
    {
        private readonly AnimatedFloat hover = new AnimatedFloat(0f);
        public string Caption = "Gestirn dev", Detail = "";

        public CommandPill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            hover.Changed += Invalidate;
        }

        protected override void OnMouseEnter(EventArgs e) { hover.To(1f, 140); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover.To(0f, 240); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            var r = new RectangleF(0, 0, Width, Height);
            Shapes.FillRound(g, Ui.Lerp(Theme.Panel, Theme.Raised, hover.Value), r, Height / 2f);
            Shapes.StrokeRound(g, Ui.Lerp(Theme.EdgeSoft, Ui.Alpha(Theme.Orange, 150), hover.Value), r, Height / 2f, Math.Max(1f, Ui.Scale * 0.75f));
            Glyphs.Draw(g, "search", new Rectangle(Ui.D(14), (Height - Ui.D(11)) / 2, Ui.D(11), Ui.D(11)), Theme.Muted);
            int x = Ui.D(34);
            int cw = TextRenderer.MeasureText(Caption, Theme.UiSemi, Size.Empty, TextFormatFlags.NoPadding).Width;
            Shapes.Text(g, Caption, Theme.UiSemi, new Rectangle(x, 0, Math.Min(cw + Ui.D(2), Width / 2), Height), Theme.Text, Shapes.Left);
            x += Math.Min(cw, Width / 2) + Ui.D(10);
            const string keys = "Ctrl+P";
            int kw = TextRenderer.MeasureText(keys, Theme.UiSmall, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.D(12);
            Shapes.Text(g, Detail, Theme.Ui9, new Rectangle(x, 0, Width - x - kw - Ui.D(16), Height), Theme.Muted, Shapes.Left);
            var chip = new RectangleF(Width - kw - Ui.D(8), Ui.D(6), kw, Height - Ui.D(12));
            Shapes.FillRound(g, Theme.Hover, chip, Ui.D(5));
            Shapes.Text(g, keys, Theme.UiSmall, Rectangle.Round(chip), Theme.TextDim, Shapes.Center);
        }
    }
}

internal static class DocExtensions
{
    public static bool IsSourceOrUntitledCode(this EditorDoc d)
    {
        return d.Path == null || DevProject.IsSource(d.Path) || d.Path.EndsWith(".h", StringComparison.OrdinalIgnoreCase) || d.Path.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase);
    }
}
}
