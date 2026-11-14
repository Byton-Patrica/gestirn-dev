using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GestirnDevPreview
{
public sealed class MainForm : Form
{
    private const string Version = "0.12";

    private readonly AppSettings settings = AppSettings.Load();
    private readonly List<Document> documents = new List<Document>();
    private readonly List<Process> programs = new List<Process>();
    private Document active;

    private readonly Panel toolbar = new Panel();
    private readonly List<GlyphButton> toolbarButtons = new List<GlyphButton>();
    private readonly ToolTip tips = new ToolTip();
    private GlyphButton buildButton, runButton, stopButton, executeButton;

    private readonly Panel tabStrip = new Panel();
    private readonly Card editorSurface = new Card();
    private readonly ListBox completionList = new ListBox();

    private readonly TreeView workspaceTree = new TreeView();
    private readonly Label workspaceHeader = new Label();
    private string workspaceRoot;

    private readonly RichTextBox terminalOutput = new RichTextBox();
    private readonly TextBox commandBox = new TextBox();
    private readonly Label statusLabel = new Label();
    private readonly SplitContainer mainSplit = new SplitContainer();
    private readonly SplitContainer rightSplit = new SplitContainer();

    private readonly Timer highlightTimer = new Timer();
    private readonly Timer gutterTimer = new Timer();
    private Document pendingHighlight;

    private Process buildProcess;
    private Process shellProcess;
    private bool busy;
    private bool stopRequested;
    private string lastBuildDir;
    private string lastFind = "";

    private static readonly string[] CppWords = { "auto", "bool", "break", "case", "catch", "char", "class", "const", "constexpr", "continue", "default", "delete", "do", "double", "else", "enum", "false", "float", "for", "if", "inline", "int", "long", "namespace", "new", "nullptr", "operator", "private", "protected", "public", "return", "short", "signed", "sizeof", "static", "std", "struct", "switch", "template", "this", "throw", "true", "try", "typedef", "typename", "union", "unsigned", "using", "virtual", "void", "volatile", "while", "vector", "string", "unique_ptr", "shared_ptr", "make_unique", "make_shared", "cout", "cin", "endl", "map", "set", "pair" };

    public MainForm()
    {
        Text = "Gestirn dev " + Version;
        Icon = LoadIcon();
        Width = 1360; Height = 820; MinimumSize = new Size(980, 620);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background; ForeColor = Theme.Text; Font = Theme.UiFont;
        Shown += (_, _) => ApplySplitters();
        FormClosing += OnFormClosing;

        highlightTimer.Interval = 250;
        highlightTimer.Tick += (_, _) =>
        {
            highlightTimer.Stop();
            var doc = pendingHighlight;
            pendingHighlight = null;
            if (doc != null && documents.Contains(doc)) Highlighter.Apply(doc.Editor);
        };
        gutterTimer.Interval = 80;
        gutterTimer.Tick += (_, _) => { if (active != null) active.Gutter.Invalidate(); };
        gutterTimer.Start();

        // Docking rule: the control added last docks first. Fill goes first so it takes whatever
        // space is left; status, toolbar and menu then stack from the bottom up.
        BuildBody();
        BuildStatusBar();
        BuildToolbar();
        BuildMenu();

        if (!string.IsNullOrEmpty(settings.Workspace) && Directory.Exists(settings.Workspace))
            SetWorkspace(settings.Workspace, false);
        NewDocument();
        SetStatus("Ready");
    }

    private Icon LoadIcon()
    {
        var path = Path.Combine(Application.StartupPath, "GestirnEra-datarot.ico");
        return File.Exists(path) ? new Icon(path) : null;
    }

    // ---------------------------------------------------------------- layout

    private void BuildMenu()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top, BackColor = Theme.Background, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), Padding = new Padding(12, 2, 0, 2) };
        menu.Items.Add(MenuGroup("File",
            Item("New", Keys.Control | Keys.N, (_, _) => NewDocument()),
            Item("Open File...", Keys.Control | Keys.O, (_, _) => OpenFileDialog()),
            Item("Open Folder...", Keys.None, (_, _) => OpenFolderDialog()),
            new ToolStripSeparator(),
            Item("Save", Keys.Control | Keys.S, (_, _) => SaveActive(false)),
            Item("Save As...", Keys.Control | Keys.Shift | Keys.S, (_, _) => SaveActive(true)),
            Item("Close Tab", Keys.Control | Keys.W, (_, _) => { if (active != null) CloseDocument(active); }),
            new ToolStripSeparator(),
            Item("Exit", Keys.None, (_, _) => Close())));
        // Clipboard and undo keys stay with the focused control, so Ctrl+V in the terminal
        // never pastes into the editor.
        menu.Items.Add(MenuGroup("Edit",
            Item("Undo", Keys.None, (_, _) => { if (ActiveEditor != null && ActiveEditor.CanUndo) ActiveEditor.Undo(); }),
            Item("Redo", Keys.None, (_, _) => { if (ActiveEditor != null && ActiveEditor.CanRedo) ActiveEditor.Redo(); }),
            new ToolStripSeparator(),
            Item("Select All", Keys.None, (_, _) => { if (ActiveEditor != null) ActiveEditor.SelectAll(); }),
            Item("Find...", Keys.Control | Keys.F, (_, _) => FindDialog()),
            Item("Find Next", Keys.F3, (_, _) => FindNext()),
            Item("Code Completion", Keys.Control | Keys.Space, (_, _) => ShowCompletion())));
        menu.Items.Add(MenuGroup("Build",
            Item("Compile", Keys.F11, (_, _) => BuildActive(false)),
            Item("Compile && Run", Keys.F9, (_, _) => BuildActive(true)),
            Item("Stop", Keys.Control | Keys.F2, (_, _) => StopAll()),
            new ToolStripSeparator(),
            Item("Build Options...", Keys.None, (_, _) => ShowOptions())));
        menu.Items.Add(MenuGroup("View",
            Item("Focus Terminal", Keys.Control | Keys.Oemtilde, (_, _) => commandBox.Focus())));
        menu.Items.Add(MenuGroup("Help",
            Item("About Gestirn dev", Keys.None, (_, _) => MessageBox.Show(this, "Gestirn dev " + Version + "\nA C/C++ editor with compile and run, built from the Dev-C++ source line.", "About Gestirn dev", MessageBoxButtons.OK, MessageBoxIcon.Information))));
        foreach (ToolStripItem item in menu.Items) StyleMenuItem(item);
        MainMenuStrip = menu;
        Controls.Add(menu);
    }

    private static ToolStripMenuItem MenuGroup(string text, params ToolStripItem[] items)
    {
        var menu = new ToolStripMenuItem(text);
        menu.DropDownItems.AddRange(items);
        return menu;
    }

    private static ToolStripMenuItem Item(string text, Keys shortcut, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text, null, handler);
        if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
        return item;
    }

    private static void StyleMenuItem(ToolStripItem item)
    {
        item.ForeColor = Theme.Text; item.Font = Theme.UiFont;
        var menuItem = item as ToolStripMenuItem;
        if (menuItem == null || !menuItem.HasDropDownItems) return;
        menuItem.DropDown.BackColor = Theme.SurfaceRaised;
        menuItem.DropDown.ForeColor = Theme.Text;
        foreach (ToolStripItem child in menuItem.DropDownItems) StyleMenuItem(child);
    }

    private void BuildToolbar()
    {
        toolbar.Dock = DockStyle.Top; toolbar.Height = 76; toolbar.BackColor = Theme.Background;

        var logo = new Card { Location = new Point(18, 16), Size = new Size(44, 44), Fill = Color.FromArgb(10, 10, 12), Outline = Theme.Edge, Radius = 14, Padding = new Padding(6), Picture = LoadLogo() };
        var title = new Label { Text = "Gestirn dev", Font = Theme.Title, ForeColor = Theme.Text, BackColor = Theme.Background, AutoSize = true, Location = new Point(74, 13) };
        var subtitle = new Label { Text = Version + "  ·  C/C++ workspace", Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Background, AutoSize = true, Location = new Point(76, 42) };
        toolbar.Controls.Add(logo); toolbar.Controls.Add(title); toolbar.Controls.Add(subtitle);

        toolbarButtons.Add(ToolButton("New", "new", "New file (Ctrl+N)", (_, _) => NewDocument(), false));
        toolbarButtons.Add(ToolButton("Open", "open", "Open file (Ctrl+O)", (_, _) => OpenFileDialog(), false));
        toolbarButtons.Add(ToolButton("Save", "save", "Save (Ctrl+S)", (_, _) => SaveActive(false), false));
        buildButton = ToolButton("Compile", "build", "Compile (F11)", (_, _) => BuildActive(false), false);
        stopButton = ToolButton("Stop", "stop", "Stop the build and running programs (Ctrl+F2)", (_, _) => StopAll(), false);
        runButton = ToolButton("Run", "run", "Compile and run (F9)", (_, _) => BuildActive(true), true);
        toolbarButtons.Add(buildButton);
        toolbarButtons.Add(stopButton);
        toolbarButtons.Add(runButton);
        foreach (var button in toolbarButtons) toolbar.Controls.Add(button);

        toolbar.Resize += (_, _) => LayoutToolbarButtons();
        LayoutToolbarButtons();
        Controls.Add(toolbar);
    }

    private GlyphButton ToolButton(string text, string glyph, string tip, EventHandler handler, bool primary)
    {
        var button = new GlyphButton { Text = text, Glyph = glyph, Primary = primary, Height = 40 };
        button.FitWidth(14);
        button.Click += handler;
        tips.SetToolTip(button, tip);
        return button;
    }

    private Image LoadLogo()
    {
        var path = Path.Combine(Application.StartupPath, "GestirnEra-datarot-256.png");
        if (File.Exists(path))
            using (var source = Image.FromFile(path)) return new Bitmap(source);
        return Icon != null ? Icon.ToBitmap() : null;
    }

    private void LayoutToolbarButtons()
    {
        int x = toolbar.ClientSize.Width - 18;
        for (int i = toolbarButtons.Count - 1; i >= 0; i--)
        {
            var button = toolbarButtons[i];
            x -= button.Width;
            button.Location = new Point(x, (toolbar.ClientSize.Height - button.Height) / 2);
            x -= 8;
        }
    }

    private void BuildStatusBar()
    {
        var host = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(18, 6, 18, 12), BackColor = Theme.Background };
        var card = new Card { Dock = DockStyle.Fill, Fill = Theme.Surface, Outline = Theme.Edge, Radius = 14, Padding = new Padding(14, 0, 14, 0) };
        statusLabel.Dock = DockStyle.Fill; statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.Font = Theme.Small; statusLabel.ForeColor = Theme.Muted; statusLabel.BackColor = Theme.Surface;
        var dot = new Label { Text = "●", Dock = DockStyle.Left, Width = 22, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.Small, ForeColor = Theme.Lime, BackColor = Theme.Surface };
        card.Controls.Add(statusLabel); card.Controls.Add(dot);
        host.Controls.Add(card); Controls.Add(host);
    }

    private void BuildBody()
    {
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(18, 4, 18, 0) };

        mainSplit.Dock = DockStyle.Fill; mainSplit.Orientation = Orientation.Vertical;
        mainSplit.SplitterWidth = 14; mainSplit.BackColor = Theme.Background;
        mainSplit.Panel1.BackColor = Theme.Background; mainSplit.Panel2.BackColor = Theme.Background;

        rightSplit.Dock = DockStyle.Fill; rightSplit.Orientation = Orientation.Horizontal;
        rightSplit.SplitterWidth = 14; rightSplit.BackColor = Theme.Background;
        rightSplit.Panel1.BackColor = Theme.Background; rightSplit.Panel2.BackColor = Theme.Background;

        BuildWorkspaceCard(mainSplit.Panel1);
        BuildEditorCard(rightSplit.Panel1);
        BuildOutputCard(rightSplit.Panel2);
        mainSplit.Panel2.Controls.Add(rightSplit);

        body.Controls.Add(mainSplit);
        Controls.Add(body);
    }

    // Minimum sizes are applied once the form has its real size; setting them earlier throws.
    private void ApplySplitters()
    {
        mainSplit.Panel1MinSize = 200; mainSplit.Panel2MinSize = 420;
        SetSplitDistance(mainSplit, 262);
        rightSplit.Panel1MinSize = 240; rightSplit.Panel2MinSize = 170;
        SetSplitDistance(rightSplit, (int)(rightSplit.ClientSize.Height * 0.68));
    }

    private static void SetSplitDistance(SplitContainer split, int preferred)
    {
        int total = split.Orientation == Orientation.Vertical ? split.ClientSize.Width : split.ClientSize.Height;
        int max = total - split.Panel2MinSize - split.SplitterWidth;
        if (max < split.Panel1MinSize) return;
        split.SplitterDistance = Math.Max(split.Panel1MinSize, Math.Min(preferred, max));
    }

    private void BuildWorkspaceCard(Control parent)
    {
        var card = new Card { Dock = DockStyle.Fill, Fill = Theme.Surface, Outline = Theme.Edge, Radius = 18, Padding = new Padding(12, 10, 12, 12) };
        workspaceHeader.Text = "WORKSPACE";
        workspaceHeader.Dock = DockStyle.Top; workspaceHeader.Height = 34; workspaceHeader.Padding = new Padding(8, 8, 0, 0);
        workspaceHeader.ForeColor = Theme.Muted; workspaceHeader.BackColor = Theme.Surface; workspaceHeader.Font = Theme.Small;

        workspaceTree.Dock = DockStyle.Fill; workspaceTree.BorderStyle = BorderStyle.None;
        workspaceTree.BackColor = Theme.Surface; workspaceTree.ForeColor = Theme.Text;
        workspaceTree.Font = new Font("Segoe UI", 10f); workspaceTree.ItemHeight = 28; workspaceTree.Indent = 18;
        workspaceTree.ShowLines = false; workspaceTree.ShowRootLines = false; workspaceTree.FullRowSelect = true; workspaceTree.HideSelection = false;
        workspaceTree.DrawMode = TreeViewDrawMode.OwnerDrawText;
        workspaceTree.DrawNode += DrawTreeNode;
        workspaceTree.BeforeExpand += (_, e) => LazyExpand(e.Node);
        workspaceTree.NodeMouseDoubleClick += (_, e) =>
        {
            var path = e.Node.Tag as string;
            if (path != null && File.Exists(path)) OpenFile(path);
        };

        // Tree first (fills), header last (docks at the top): the last-added control docks first.
        card.Controls.Add(workspaceTree);
        card.Controls.Add(workspaceHeader);
        parent.Controls.Add(card);
        ShowWorkspacePlaceholder();
    }

    private void ShowWorkspacePlaceholder()
    {
        workspaceTree.Nodes.Clear();
        workspaceTree.Nodes.Add(new TreeNode("No folder open  (File > Open Folder)") { ForeColor = Theme.Muted });
    }

    private void BuildEditorCard(Control parent)
    {
        var card = new Card { Dock = DockStyle.Fill, Fill = Theme.Surface, Outline = Theme.Edge, Radius = 18, Padding = new Padding(12, 10, 12, 12) };

        tabStrip.Dock = DockStyle.Top; tabStrip.Height = 46; tabStrip.BackColor = Theme.Surface;

        editorSurface.Dock = DockStyle.Fill; editorSurface.Fill = Theme.Editor;
        editorSurface.Outline = Color.FromArgb(34, 34, 40); editorSurface.Radius = 12;
        editorSurface.Padding = new Padding(4, 8, 8, 8);
        editorSurface.Resize += (_, _) => LayoutHosts();

        completionList.Visible = false; completionList.Font = new Font("Consolas", 10f);
        completionList.Height = 190; completionList.Width = 240; completionList.BorderStyle = BorderStyle.FixedSingle;
        completionList.BackColor = Theme.SurfaceRaised; completionList.ForeColor = Theme.Text;
        completionList.DoubleClick += (_, _) => InsertCompletion();
        completionList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { InsertCompletion(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) completionList.Visible = false;
        };
        editorSurface.Controls.Add(completionList);

        // Surface first (fills), tab strip last (docks at the top).
        card.Controls.Add(editorSurface);
        card.Controls.Add(tabStrip);
        parent.Controls.Add(card);
    }

    private void BuildOutputCard(Control parent)
    {
        var card = new Card { Dock = DockStyle.Fill, Fill = Theme.Surface, Outline = Theme.Edge, Radius = 18, Padding = new Padding(12, 12, 12, 12) };

        var output = new Card { Dock = DockStyle.Fill, Fill = Theme.Output, Outline = Color.FromArgb(32, 32, 38), Radius = 12, Padding = new Padding(14, 10, 12, 10) };
        terminalOutput.Dock = DockStyle.Fill; terminalOutput.ReadOnly = true; terminalOutput.BackColor = Theme.Output;
        terminalOutput.ForeColor = Theme.EditorText; terminalOutput.BorderStyle = BorderStyle.None;
        terminalOutput.Font = new Font("Consolas", 10f); terminalOutput.WordWrap = false;
        terminalOutput.DetectUrls = false;
        terminalOutput.MouseDoubleClick += OnOutputDoubleClick;
        output.Controls.Add(terminalOutput);

        var commandBar = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = Theme.Surface, Padding = new Padding(0, 10, 0, 0) };
        var commandCard = new Card { Dock = DockStyle.Fill, Fill = Theme.SurfaceRaised, Outline = Theme.Edge, Radius = 12, Padding = new Padding(14, 12, 14, 12) };
        commandBox.Dock = DockStyle.Fill; commandBox.BorderStyle = BorderStyle.None;
        commandBox.BackColor = Theme.SurfaceRaised; commandBox.ForeColor = Theme.Text; commandBox.Font = new Font("Consolas", 10.5f);
        commandBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { ExecuteShellCommand(); e.SuppressKeyPress = true; } };
        commandCard.Controls.Add(commandBox);

        executeButton = ToolButton("Execute", "term", "Run a shell command in the current folder", (_, _) => ExecuteShellCommand(), false);
        executeButton.Dock = DockStyle.Right; executeButton.Width = 124; executeButton.Margin = new Padding(10, 0, 0, 0);
        // Last-added docks first: the button hugs the right edge, the command field fills the rest.
        commandBar.Controls.Add(executeButton);
        commandBar.Controls.Add(commandCard);

        // Output first (fills), command bar last (docks at the bottom).
        card.Controls.Add(output);
        card.Controls.Add(commandBar);
        parent.Controls.Add(card);

        AppendLine("Gestirn dev " + Version + "  ·  F9 compile & run  ·  F11 compile  ·  Ctrl+F2 stop  ·  double-click an error to jump to it", Theme.Muted);
    }

    // ------------------------------------------------------------- documents

    private RichTextBox ActiveEditor { get { return active == null ? null : active.Editor; } }

    private Document CreateDocument(string path, string text)
    {
        var doc = new Document(path);
        doc.Editor.Text = text;
        doc.SavedText = doc.Editor.Text;
        doc.Editor.TextChanged += (_, _) => OnDocumentChanged(doc);
        doc.Editor.KeyDown += EditorKeyDown;
        doc.ChipName.Click += (_, _) => Activate(doc);
        doc.ChipClose.Click += (_, _) => CloseDocument(doc);

        documents.Add(doc);
        editorSurface.Controls.Add(doc.Host);
        tabStrip.Controls.Add(doc.Chip);
        LayoutHosts();
        ScheduleHighlight(doc);
        return doc;
    }

    private void NewDocument()
    {
        Activate(CreateDocument(null, ""));
    }

    private void Activate(Document doc)
    {
        active = doc;
        foreach (var d in documents) d.Host.Visible = d == doc;
        LayoutHosts();
        LayoutTabs();
        UpdateTitle();
        doc.Editor.Focus();
    }

    private void LayoutHosts()
    {
        var area = editorSurface.DisplayRectangle;
        foreach (var doc in documents) doc.Host.Bounds = area;
    }

    private void LayoutTabs()
    {
        int x = 6;
        foreach (var doc in documents)
        {
            bool isActive = doc == active;
            string label = doc.Title + (doc.Dirty ? "  ●" : "");
            doc.ChipName.Text = label;
            int width = TextRenderer.MeasureText(label, doc.ChipName.Font).Width + 12 + 8 + 22 + 10;
            doc.Chip.Fill = isActive ? Theme.SurfaceRaised : Theme.Surface;
            doc.Chip.Outline = isActive ? Theme.Orange : Color.Empty;
            doc.Chip.Location = new Point(x, 6);
            doc.Chip.Size = new Size(width, 34);
            doc.ChipName.BackColor = doc.Chip.Fill;
            doc.ChipClose.BackColor = doc.Chip.Fill;
            doc.ChipName.ForeColor = isActive ? Theme.Text : Theme.Muted;
            doc.ChipClose.ForeColor = Theme.Muted;
            x += width + 6;
        }
    }

    private void OnDocumentChanged(Document doc)
    {
        doc.Dirty = doc.Editor.Text != doc.SavedText;
        doc.Gutter.Invalidate();
        LayoutTabs();
        UpdateTitle();
        ScheduleHighlight(doc);
    }

    private void ScheduleHighlight(Document doc)
    {
        pendingHighlight = doc;
        highlightTimer.Stop();
        highlightTimer.Start();
    }

    private void UpdateTitle()
    {
        Text = "Gestirn dev " + Version + (active == null ? "" : "  -  " + active.Title + (active.Dirty ? " *" : ""));
    }

    private bool CloseDocument(Document doc)
    {
        if (doc.Dirty)
        {
            Activate(doc);
            if (!ConfirmSave(doc)) return false;
        }
        int index = documents.IndexOf(doc);
        RemoveDocument(doc);
        if (documents.Count == 0) NewDocument();
        else if (active == null) Activate(documents[Math.Max(0, Math.Min(index - 1, documents.Count - 1))]);
        return true;
    }

    private void RemoveDocument(Document doc)
    {
        documents.Remove(doc);
        editorSurface.Controls.Remove(doc.Host);
        tabStrip.Controls.Remove(doc.Chip);
        if (pendingHighlight == doc) pendingHighlight = null;
        if (active == doc) active = null;
        doc.Host.Dispose();
        doc.Chip.Dispose();
        LayoutTabs();
    }

    private bool ConfirmSave(Document doc)
    {
        var answer = MessageBox.Show(this, "Save changes to " + doc.Title + "?", "Gestirn dev", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return false;
        if (answer == DialogResult.Yes) return SaveDocument(doc, false);
        return true;
    }

    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
        foreach (var doc in documents.ToList())
        {
            if (!doc.Dirty) continue;
            Activate(doc);
            if (!ConfirmSave(doc)) { e.Cancel = true; return; }
        }
        settings.Save();
        Toolchain.KillTree(buildProcess);
    }

    private void OpenFileDialog()
    {
        using (var dialog = new OpenFileDialog { Filter = "C/C++ files|*.c;*.cc;*.cpp;*.cxx;*.h;*.hh;*.hpp|All files|*.*", Multiselect = true })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            foreach (var file in dialog.FileNames) OpenFile(file);
        }
    }

    private void OpenFile(string path)
    {
        path = Path.GetFullPath(path);
        var existing = documents.FirstOrDefault(d => d.FilePath != null && string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { Activate(existing); return; }

        string text;
        try { text = ReadSource(path); }
        catch (IOException ex) { ShowError("Could not open the file:\n" + ex.Message); return; }
        catch (UnauthorizedAccessException ex) { ShowError("Could not open the file:\n" + ex.Message); return; }

        // An empty, untouched untitled tab is replaced rather than left behind.
        var blank = active != null && active.FilePath == null && !active.Dirty && active.Editor.TextLength == 0 ? active : null;
        var doc = CreateDocument(path, text);
        doc.SavedText = doc.Editor.Text;
        Activate(doc);
        if (blank != null) RemoveDocument(blank);
        SetStatus("Opened " + path);
    }

    // UTF-8 when the bytes are valid UTF-8, otherwise the system ANSI code page (typical for older Chinese sources).
    private static string ReadSource(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Toolchain.ConsoleEncoding.GetString(bytes); }
        return text.Length > 0 && text[0] == '﻿' ? text.Substring(1) : text;
    }

    private void SaveActive(bool saveAs)
    {
        if (active != null) SaveDocument(active, saveAs);
    }

    private bool SaveDocument(Document doc, bool forceDialog)
    {
        string path = doc.FilePath;
        if (path == null || forceDialog)
        {
            using (var dialog = new SaveFileDialog { Filter = "C++ source|*.cpp;*.cc;*.cxx|C source|*.c|Headers|*.h;*.hpp|All files|*.*", FileName = doc.Title })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                path = dialog.FileName;
            }
        }
        try
        {
            File.WriteAllText(path, doc.Editor.Text, new UTF8Encoding(false));
        }
        catch (IOException ex) { ShowError("Could not save the file:\n" + ex.Message); return false; }
        catch (UnauthorizedAccessException ex) { ShowError("Could not save the file:\n" + ex.Message); return false; }

        doc.FilePath = path;
        doc.SavedText = doc.Editor.Text;
        doc.Dirty = false;
        LayoutTabs();
        UpdateTitle();
        SetStatus("Saved " + path);
        return true;
    }

    private void ShowError(string message)
    {
        MessageBox.Show(this, message, "Gestirn dev", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    // --------------------------------------------------------------- workspace

    private void OpenFolderDialog()
    {
        using (var dialog = new FolderBrowserDialog { Description = "Choose a project folder", ShowNewFolderButton = true, SelectedPath = workspaceRoot ?? "" })
        {
            if (dialog.ShowDialog(this) == DialogResult.OK) SetWorkspace(dialog.SelectedPath, true);
        }
    }

    private void SetWorkspace(string folder, bool remember)
    {
        workspaceRoot = folder;
        if (remember)
        {
            settings.Workspace = folder;
            settings.Save();
        }
        string name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(name)) name = folder;

        workspaceTree.BeginUpdate();
        workspaceTree.Nodes.Clear();
        var root = new TreeNode(name) { Tag = folder, ToolTipText = folder };
        PopulateNode(root);
        workspaceTree.Nodes.Add(root);
        root.Expand();
        workspaceTree.EndUpdate();
        SetStatus("Folder " + folder);
    }

    private void LazyExpand(TreeNode node)
    {
        // A directory node holds a single placeholder (Tag == null) until it is first expanded.
        if (node.Tag is string && node.Nodes.Count == 1 && node.Nodes[0].Tag == null) PopulateNode(node);
    }

    private void PopulateNode(TreeNode node)
    {
        string folder = (string)node.Tag;
        node.Nodes.Clear();
        try
        {
            foreach (var dir in Directory.GetDirectories(folder).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                if (IsHidden(dir)) continue;
                var child = new TreeNode(Path.GetFileName(dir)) { Tag = dir };
                child.Nodes.Add(new TreeNode("..."));
                node.Nodes.Add(child);
            }
            foreach (var file in Directory.GetFiles(folder).OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            {
                if (IsHidden(file)) continue;
                node.Nodes.Add(new TreeNode(Path.GetFileName(file)) { Tag = file, ToolTipText = file });
            }
        }
        catch (UnauthorizedAccessException) { node.Nodes.Add(new TreeNode("(access denied)") { ForeColor = Theme.Muted }); }
        catch (IOException) { node.Nodes.Add(new TreeNode("(unreadable)") { ForeColor = Theme.Muted }); }
    }

    private static bool IsHidden(string path)
    {
        try
        {
            string name = Path.GetFileName(path);
            return name.StartsWith(".") || (File.GetAttributes(path) & FileAttributes.Hidden) != 0;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private void DrawTreeNode(object sender, DrawTreeNodeEventArgs e)
    {
        e.DrawDefault = false;
        var g = e.Graphics;
        bool selected = (e.State & TreeNodeStates.Selected) == TreeNodeStates.Selected;
        bool isRoot = e.Node.Parent == null;

        using (var clear = new SolidBrush(Theme.Surface))
            g.FillRectangle(clear, new Rectangle(0, e.Bounds.Y, workspaceTree.ClientSize.Width, e.Bounds.Height));

        if (selected)
        {
            var row = new Rectangle(4, e.Bounds.Y + 2, workspaceTree.ClientSize.Width - 8, e.Bounds.Height - 4);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Shapes.RoundRect(row, 10))
            using (var brush = new SolidBrush(Theme.SurfaceRaised))
                g.FillPath(brush, path);
            using (var accent = new SolidBrush(Theme.Orange))
                g.FillRectangle(accent, row.X + 3, row.Y + 8, 3, row.Height - 16);
        }

        Color color = e.Node.ForeColor != Color.Empty && e.Node.ForeColor != Theme.Text ? e.Node.ForeColor
            : selected ? Theme.Text : (isRoot ? Theme.Muted : Color.FromArgb(214, 210, 200));
        TextRenderer.DrawText(g, e.Node.Text, workspaceTree.Font,
            new Rectangle(e.Bounds.X + 2, e.Bounds.Y, workspaceTree.ClientSize.Width - e.Bounds.X, e.Bounds.Height),
            color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    // ----------------------------------------------------------- build & run

    private async void BuildActive(bool andRun)
    {
        if (busy) { SetStatus("A build is already running."); return; }
        var doc = active;
        if (doc == null) return;
        if (doc.FilePath == null || doc.Dirty)
        {
            if (!SaveDocument(doc, false)) return;
        }

        string compiler = CompilerLocator.Find(settings.CompilerPath);
        if (compiler == null)
        {
            MessageBox.Show(this, "No g++ compiler was found.\n\nInstall Dev-C++ (its bundled MinGW64 is detected automatically) or MSYS2, or choose g++.exe under Build > Build Options.", "Gestirn dev", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ShowOptions();
            return;
        }
        if (compiler != settings.CompilerPath)
        {
            settings.CompilerPath = compiler;
            settings.Save();
        }

        busy = true;
        stopRequested = false;
        UpdateBuildControls();
        lastBuildDir = Path.GetDirectoryName(doc.FilePath);
        AppendLine((andRun ? "> Compile & run " : "> Compile ") + doc.Title + "   [" + settings.Standard + "]", Theme.Orange);
        SetStatus("Compiling " + doc.Title + "...");

        try
        {
            var result = await Toolchain.CompileAsync(compiler, doc.FilePath, settings.Standard, settings.ExtraFlags, p => buildProcess = p);
            HandleBuildResult(result, andRun, stopRequested);
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
        {
            AppendLine("Gestirn dev: " + ex.Message, Theme.Red);
            SetStatus("Build error.");
        }
        finally
        {
            buildProcess = null;
            busy = false;
            UpdateBuildControls();
        }
    }

    private void HandleBuildResult(BuildResult result, bool andRun, bool stopped)
    {
        if (stopped)
        {
            AppendLine("Build stopped.", Theme.Muted);
            SetStatus("Build stopped.");
            return;
        }
        AppendDiagnostics(result.Output);
        if (result.ExitCode == 0)
        {
            AppendLine("Build succeeded: " + Path.GetFileName(result.ExePath), Theme.Lime);
            SetStatus("Build succeeded  ·  " + Path.GetFileName(result.ExePath));
            if (andRun) LaunchProgram(result.ExePath);
        }
        else
        {
            AppendLine("Build failed (exit code " + result.ExitCode + ").", Theme.Red);
            SetStatus("Build failed.");
        }
    }

    private void AppendDiagnostics(string output)
    {
        foreach (var raw in output.Replace("\r", "").Split('\n'))
        {
            if (raw.Length == 0) continue;
            Color color = Theme.EditorText;
            if (raw.Contains(": error:") || raw.Contains("fatal error:")) color = Theme.Red;
            else if (raw.Contains(": warning:")) color = Theme.Gold;
            else if (raw.Contains(": note:")) color = Theme.Muted;
            AppendLine(raw, color);
        }
    }

    private void LaunchProgram(string exePath)
    {
        programs.RemoveAll(p => p.HasExited);
        try
        {
            programs.Add(Toolchain.RunProgram(exePath));
            AppendLine("Running " + Path.GetFileName(exePath) + " in its own console window. Stop ends it.", Theme.Muted);
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
        {
            AppendLine("Could not start the program: " + ex.Message, Theme.Red);
        }
    }

    private void StopAll()
    {
        stopRequested = true;
        bool anything = buildProcess != null || shellProcess != null || programs.Count > 0;
        Toolchain.KillTree(buildProcess);
        Toolchain.KillTree(shellProcess);
        int count = 0;
        foreach (var program in programs.ToList())
        {
            if (program.HasExited) continue;
            Toolchain.KillTree(program);
            count++;
        }
        programs.Clear();
        if (anything)
        {
            AppendLine(count > 0 ? "Stopped " + count + " running program(s)." : "Stopped.", Theme.Muted);
            SetStatus("Stopped.");
        }
        else
        {
            SetStatus("Nothing to stop.");
        }
    }

    private void UpdateBuildControls()
    {
        buildButton.Enabled = !busy;
        runButton.Enabled = !busy;
    }

    private void ShowOptions()
    {
        using (var form = new OptionsForm(settings))
        {
            if (form.ShowDialog(this) != DialogResult.OK) return;
            settings.CompilerPath = form.CompilerPath;
            settings.Standard = form.Standard;
            settings.ExtraFlags = form.ExtraFlags;
            settings.Save();
            SetStatus("Build options saved.");
        }
    }

    private string WorkingFolder()
    {
        if (active != null && active.FilePath != null)
        {
            var dir = Path.GetDirectoryName(active.FilePath);
            if (Directory.Exists(dir)) return dir;
        }
        if (workspaceRoot != null && Directory.Exists(workspaceRoot)) return workspaceRoot;
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void OnOutputDoubleClick(object sender, MouseEventArgs e)
    {
        int index = terminalOutput.GetCharIndexFromPosition(e.Location);
        int lineIndex = terminalOutput.GetLineFromCharIndex(index);
        string[] lines = terminalOutput.Lines;
        if (lineIndex < 0 || lineIndex >= lines.Length) return;

        var match = Toolchain.DiagnosticPattern.Match(lines[lineIndex]);
        if (!match.Success) return;
        int lineNo, col = 1;
        if (!int.TryParse(match.Groups["line"].Value, out lineNo)) return;
        if (match.Groups["col"].Success) int.TryParse(match.Groups["col"].Value, out col);

        string file = match.Groups["file"].Value;
        string full = Path.IsPathRooted(file) ? file : Path.Combine(lastBuildDir ?? WorkingFolder(), file);
        if (!File.Exists(full)) { SetStatus("Cannot find " + file); return; }

        OpenFile(full);
        var editor = ActiveEditor;
        if (editor == null) return;
        int start = editor.GetFirstCharIndexFromLine(lineNo - 1);
        if (start < 0) return;
        int target = Math.Min(editor.TextLength, start + Math.Max(0, col - 1));
        editor.Select(target, 0);
        editor.ScrollToCaret();
        editor.Focus();
    }

    // ------------------------------------------------------------- shell & log

    private async void ExecuteShellCommand()
    {
        if (shellProcess != null || string.IsNullOrWhiteSpace(commandBox.Text)) return;
        string command = commandBox.Text.Trim();
        commandBox.Clear();
        AppendLine("> " + command, Theme.Orange);

        var process = new Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/C " + command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = WorkingFolder(),
                StandardOutputEncoding = Toolchain.ConsoleEncoding,
                StandardErrorEncoding = Toolchain.ConsoleEncoding,
            },
        };
        shellProcess = process;
        try
        {
            process.Start();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            await Task.Run(() => process.WaitForExit());
            string outText = await stdout;
            string errText = await stderr;
            AppendLine((outText + errText).TrimEnd('\r', '\n'), Theme.EditorText);
            AppendLine("[exit " + process.ExitCode + "]", Theme.Muted);
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
        {
            AppendLine("Gestirn dev: " + ex.Message, Theme.Red);
        }
        finally
        {
            shellProcess = null;
            process.Dispose();
        }
    }

    private void AppendLine(string text, Color color)
    {
        terminalOutput.SelectionStart = terminalOutput.TextLength;
        terminalOutput.SelectionLength = 0;
        terminalOutput.SelectionColor = color;
        terminalOutput.SelectedText = text + "\n";
        terminalOutput.ScrollToCaret();
    }

    private void SetStatus(string text)
    {
        statusLabel.Text = text + "     ·     " + settings.Standard + "     ·     Gestirn dev " + Version;
    }

    // --------------------------------------------------------------- editing

    private void EditorKeyDown(object sender, KeyEventArgs e)
    {
        var editor = (RichTextBox)sender;
        if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.None)
        {
            InsertAutoIndent(editor);
            e.SuppressKeyPress = true;
            e.Handled = true;
        }
    }

    // Enter keeps the current line's indentation, and adds one level after an opening brace.
    private static void InsertAutoIndent(RichTextBox editor)
    {
        string text = editor.Text;
        int pos = editor.SelectionStart;
        int lineStart = 0;
        for (int i = pos - 1; i >= 0; i--)
        {
            if (text[i] == '\n') { lineStart = i + 1; break; }
        }
        var indent = new StringBuilder();
        for (int i = lineStart; i < pos && (text[i] == ' ' || text[i] == '\t'); i++) indent.Append(text[i]);
        string before = text.Substring(lineStart, pos - lineStart).TrimEnd();
        if (before.EndsWith("{")) indent.Append("    ");
        editor.SelectedText = "\n" + indent;
    }

    private void ShowCompletion()
    {
        var doc = active;
        if (doc == null) return;
        var editor = doc.Editor;
        string prefix = CurrentWord(editor);
        completionList.Items.Clear();
        foreach (var word in CppWords.Where(w => prefix.Length == 0 || w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Distinct())
            completionList.Items.Add(word);
        if (completionList.Items.Count == 0) return;

        var caret = editor.GetPositionFromCharIndex(editor.SelectionStart);
        var surface = editorSurface.ClientSize;
        int left = doc.Host.Left + editor.Left + caret.X;
        int top = doc.Host.Top + editor.Top + caret.Y + editor.Font.Height + 4;
        completionList.Left = Math.Min(Math.Max(left, 8), Math.Max(8, surface.Width - completionList.Width - 8));
        completionList.Top = Math.Min(top, Math.Max(8, surface.Height - completionList.Height - 8));
        completionList.Visible = true;
        completionList.BringToFront();
        completionList.SelectedIndex = 0;
        completionList.Focus();
    }

    private static string CurrentWord(RichTextBox editor)
    {
        var value = editor.Text;
        var end = editor.SelectionStart;
        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(value[start - 1]) || value[start - 1] == '_')) start--;
        return value.Substring(start, end - start);
    }

    private void InsertCompletion()
    {
        var editor = ActiveEditor;
        if (editor == null || !(completionList.SelectedItem is string)) return;
        var word = (string)completionList.SelectedItem;
        var prefix = CurrentWord(editor);
        editor.SelectedText = word.Substring(prefix.Length);
        completionList.Visible = false;
        editor.Focus();
    }

    private void FindDialog()
    {
        var text = Prompt.Show(this, "Find", "Text to find", lastFind);
        if (text == null) return;
        lastFind = text;
        FindNext();
    }

    private void FindNext()
    {
        var editor = ActiveEditor;
        if (editor == null) return;
        if (lastFind.Length == 0) { FindDialog(); return; }

        int start = editor.SelectionStart + editor.SelectionLength;
        int index = editor.Find(lastFind, start, RichTextBoxFinds.None);
        if (index < 0 && start > 0) index = editor.Find(lastFind, 0, RichTextBoxFinds.None);
        if (index < 0)
        {
            SetStatus("Not found: " + lastFind);
            return;
        }
        editor.ScrollToCaret();
        editor.Focus();
        SetStatus("Found on line " + (editor.GetLineFromCharIndex(index) + 1));
    }
}
}
