using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GestirnDevPreview
{
public sealed class MainForm : Form
{
    private readonly RichTextBox editor = new RichTextBox();
    private readonly RichTextBox terminalOutput = new RichTextBox();
    private readonly TextBox commandBox = new TextBox();
    private readonly GlyphButton runButton = new GlyphButton();
    private readonly GlyphButton stopButton = new GlyphButton();
    private readonly ListBox completionList = new ListBox();
    private readonly Label fileLabel = new Label();
    private readonly Card fileChip = new Card();
    private readonly Card editorSurface = new Card();
    private readonly Label statusLabel = new Label();
    private readonly TreeView projectTree = new TreeView();
    private readonly Panel toolbar = new Panel();
    private readonly List<GlyphButton> toolbarButtons = new List<GlyphButton>();
    private readonly SplitContainer mainSplit = new SplitContainer();
    private readonly SplitContainer rightSplit = new SplitContainer();
    private TreeNode mainNode;
    private Process runningProcess;
    private string currentFile = "main.cpp";
    private static readonly string[] CppWords = { "auto", "bool", "break", "case", "catch", "char", "class", "const", "constexpr", "continue", "default", "delete", "do", "double", "else", "enum", "false", "float", "for", "if", "inline", "int", "long", "namespace", "new", "nullptr", "operator", "private", "protected", "public", "return", "short", "signed", "sizeof", "static", "std", "struct", "switch", "template", "this", "throw", "true", "try", "typedef", "typename", "union", "unsigned", "using", "virtual", "void", "volatile", "while", "vector", "string", "unique_ptr", "shared_ptr", "make_unique", "make_shared" };

    public MainForm()
    {
        Text = "Gestirn dev 0.11";
        Icon = LoadIcon();
        Width = 1360; Height = 820; MinimumSize = new Size(980, 620);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background; ForeColor = Theme.Text; Font = Theme.UiFont;
        Shown += (_, _) => ApplySplitters();

        // Docking rule: the control added last docks first. Fill goes first so it takes
        // whatever space is left; then status, toolbar and menu stack from the bottom up.
        BuildBody();
        BuildStatusBar();
        BuildToolbar();
        BuildMenu();
    }

    private Icon LoadIcon()
    {
        var path = Path.Combine(Application.StartupPath, "GestirnEra-datarot.ico");
        return File.Exists(path) ? new Icon(path) : null;
    }

    private void BuildMenu()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top, BackColor = Theme.Background, ForeColor = Theme.Text, Renderer = new DarkMenuRenderer(), Padding = new Padding(12, 2, 0, 2) };
        var file = new ToolStripMenuItem("File");
        file.DropDownItems.Add("New file", null, (_, _) => NewFile());
        file.DropDownItems.Add("Open...", null, (_, _) => OpenFile());
        file.DropDownItems.Add("Save", null, (_, _) => SaveFile());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Exit", null, (_, _) => Close());
        var edit = new ToolStripMenuItem("Edit");
        edit.DropDownItems.Add("Code completion", null, (_, _) => ShowCompletion());
        edit.DropDownItems.Add("Select all", null, (_, _) => editor.SelectAll());
        var view = new ToolStripMenuItem("View");
        view.DropDownItems.Add("Focus terminal", null, (_, _) => commandBox.Focus());
        var run = new ToolStripMenuItem("Run");
        run.DropDownItems.Add("Run command...", null, (_, _) => commandBox.Focus());
        var help = new ToolStripMenuItem("Help");
        help.DropDownItems.Add("About Gestirn dev", null, (_, _) => MessageBox.Show("Gestirn dev 0.11\nA modern C/C++ workspace built from the Dev-C++ source line.", "About", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.AddRange(new ToolStripItem[] { file, edit, view, run, help });
        foreach (ToolStripItem item in menu.Items) StyleMenuItem(item);
        MainMenuStrip = menu; Controls.Add(menu);
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
        var subtitle = new Label { Text = "0.11  ·  C/C++ workspace", Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Background, AutoSize = true, Location = new Point(76, 42) };
        toolbar.Controls.Add(logo); toolbar.Controls.Add(title); toolbar.Controls.Add(subtitle);

        toolbarButtons.Add(MakeButton("New", "new", (_, _) => NewFile()));
        toolbarButtons.Add(MakeButton("Open", "open", (_, _) => OpenFile()));
        toolbarButtons.Add(MakeButton("Save", "save", (_, _) => SaveFile()));
        toolbarButtons.Add(MakeButton("Terminal", "term", (_, _) => commandBox.Focus()));
        toolbarButtons.Add(MakeButton("Run", "run", (_, _) => RunCommand(), true));
        foreach (var button in toolbarButtons) toolbar.Controls.Add(button);

        toolbar.Resize += (_, _) => LayoutToolbarButtons();
        LayoutToolbarButtons();
        Controls.Add(toolbar);
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

    private static GlyphButton MakeButton(string text, string glyph, EventHandler handler, bool primary = false)
    {
        var button = new GlyphButton { Text = text, Glyph = glyph, Primary = primary, Height = 40 };
        button.FitWidth(14);
        button.Click += handler;
        return button;
    }

    private void BuildStatusBar()
    {
        var host = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(18, 6, 18, 12), BackColor = Theme.Background };
        var card = new Card { Dock = DockStyle.Fill, Fill = Theme.Surface, Outline = Theme.Edge, Radius = 14, Padding = new Padding(14, 0, 14, 0) };
        statusLabel.Text = "Ready  •  Gestirn dev 0.11  •  C++ workspace";
        statusLabel.Dock = DockStyle.Fill; statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.Font = Theme.Small; statusLabel.ForeColor = Theme.Muted; statusLabel.BackColor = Theme.Surface;
        var dot = new Label { Text = "●", Dock = DockStyle.Left, Width = 22, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.Small, ForeColor = Theme.Lime, BackColor = Theme.Surface };
        // Fill first, then the docked dot: last-added docks first.
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
        BuildTerminalCard(rightSplit.Panel2);
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
        SetSplitDistance(rightSplit, (int)(rightSplit.ClientSize.Height * 0.66));
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
        var header = new Label { Text = "WORKSPACE", Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 8, 0, 0), ForeColor = Theme.Muted, BackColor = Theme.Surface, Font = Theme.Small };

        projectTree.Dock = DockStyle.Fill; projectTree.BorderStyle = BorderStyle.None;
        projectTree.BackColor = Theme.Surface; projectTree.ForeColor = Theme.Text;
        projectTree.Font = new Font("Segoe UI", 10f); projectTree.ItemHeight = 30; projectTree.Indent = 18;
        projectTree.ShowLines = false; projectTree.ShowRootLines = false; projectTree.FullRowSelect = true; projectTree.HideSelection = false;
        projectTree.DrawMode = TreeViewDrawMode.OwnerDrawText;
        projectTree.DrawNode += DrawTreeNode;
        var root = projectTree.Nodes.Add("Gestirn workspace");
        mainNode = root.Nodes.Add("main.cpp");
        root.Nodes.Add("include"); root.Nodes.Add("src"); root.Expand();
        projectTree.AfterSelect += (_, e) => { if (e.Node == mainNode) SetFileTitle("main.cpp"); };

        // Header first (docks at top), tree last (fills the rest).
        card.Controls.Add(projectTree);
        card.Controls.Add(header);
        parent.Controls.Add(card);
        projectTree.SelectedNode = mainNode;
    }

    private void DrawTreeNode(object sender, DrawTreeNodeEventArgs e)
    {
        e.DrawDefault = false;
        var g = e.Graphics;
        bool selected = (e.State & TreeNodeStates.Selected) == TreeNodeStates.Selected;
        bool isRoot = e.Node.Parent == null;

        using (var clear = new SolidBrush(Theme.Surface))
            g.FillRectangle(clear, new Rectangle(0, e.Bounds.Y, projectTree.ClientSize.Width, e.Bounds.Height));

        if (selected)
        {
            var row = new Rectangle(4, e.Bounds.Y + 2, projectTree.ClientSize.Width - 8, e.Bounds.Height - 4);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Shapes.RoundRect(row, 10))
            using (var brush = new SolidBrush(Theme.SurfaceRaised))
                g.FillPath(brush, path);
            using (var accent = new SolidBrush(Theme.Orange))
                g.FillRectangle(accent, row.X + 3, row.Y + 8, 3, row.Height - 16);
        }

        Color color = selected ? Theme.Text : (isRoot ? Theme.Muted : Color.FromArgb(214, 210, 200));
        TextRenderer.DrawText(g, e.Node.Text, projectTree.Font,
            new Rectangle(e.Bounds.X + 2, e.Bounds.Y, projectTree.ClientSize.Width - e.Bounds.X, e.Bounds.Height),
            color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    private void BuildEditorCard(Control parent)
    {
        var card = new Card { Dock = DockStyle.Fill, Fill = Theme.Surface, Outline = Theme.Edge, Radius = 18, Padding = new Padding(12, 10, 12, 12) };

        var tabBar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Surface, Padding = new Padding(0, 0, 0, 10) };
        fileChip.Dock = DockStyle.Left; fileChip.Fill = Theme.SurfaceRaised; fileChip.Outline = Color.Empty;
        fileChip.Radius = 11; fileChip.Padding = new Padding(14, 0, 14, 0);
        fileLabel.Dock = DockStyle.Fill; fileLabel.ForeColor = Theme.Text; fileLabel.BackColor = Theme.SurfaceRaised;
        fileLabel.Font = Theme.UiSemi; fileLabel.TextAlign = ContentAlignment.MiddleLeft;
        fileChip.Controls.Add(fileLabel);
        tabBar.Controls.Add(fileChip);
        SetFileTitle("main.cpp");

        editorSurface.Dock = DockStyle.Fill; editorSurface.Fill = Theme.Editor;
        editorSurface.Outline = Color.FromArgb(34, 34, 40); editorSurface.Radius = 12;
        editorSurface.Padding = new Padding(14, 12, 10, 10);

        editor.Dock = DockStyle.Fill; editor.BorderStyle = BorderStyle.None; editor.BackColor = Theme.Editor;
        editor.ForeColor = Color.FromArgb(230, 227, 218); editor.Font = Theme.CodeFont; editor.AcceptsTab = true; editor.WordWrap = false;
        editor.Text = "#include <iostream>\r\n\r\nint main()\r\n{\r\n    std::cout << \"Hello, Gestirn dev!\" << std::endl;\r\n    return 0;\r\n}\r\n";
        editor.KeyDown += EditorKeyDown;

        completionList.Visible = false; completionList.Font = new Font("Consolas", 10f);
        completionList.Height = 190; completionList.Width = 240; completionList.BorderStyle = BorderStyle.FixedSingle;
        completionList.BackColor = Theme.SurfaceRaised; completionList.ForeColor = Theme.Text;
        completionList.DoubleClick += (_, _) => InsertCompletion();
        completionList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { InsertCompletion(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) completionList.Visible = false;
        };

        // Editor first (fills), then the completion popup floats above it.
        editorSurface.Controls.Add(editor);
        editorSurface.Controls.Add(completionList);

        // Tab bar first (docks at top), editor surface last (fills the rest).
        card.Controls.Add(editorSurface);
        card.Controls.Add(tabBar);
        parent.Controls.Add(card);
    }

    private void BuildTerminalCard(Control parent)
    {
        var card = new Card { Dock = DockStyle.Fill, Fill = Theme.Surface, Outline = Theme.Edge, Radius = 18, Padding = new Padding(12, 12, 12, 12) };

        var output = new Card { Dock = DockStyle.Fill, Fill = Theme.Output, Outline = Color.FromArgb(32, 32, 38), Radius = 12, Padding = new Padding(14, 10, 12, 10) };
        terminalOutput.Dock = DockStyle.Fill; terminalOutput.ReadOnly = true; terminalOutput.BackColor = Theme.Output;
        terminalOutput.ForeColor = Color.FromArgb(206, 230, 178); terminalOutput.BorderStyle = BorderStyle.None;
        terminalOutput.Font = new Font("Consolas", 10f); terminalOutput.WordWrap = false;
        terminalOutput.Text = "Gestirn dev terminal  •  ready\r\n";
        output.Controls.Add(terminalOutput);

        var commandBar = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = Theme.Surface, Padding = new Padding(0, 10, 0, 0) };
        var commandCard = new Card { Dock = DockStyle.Fill, Fill = Theme.SurfaceRaised, Outline = Theme.Edge, Radius = 12, Padding = new Padding(14, 12, 14, 12) };
        commandBox.Dock = DockStyle.Fill; commandBox.BorderStyle = BorderStyle.None;
        commandBox.BackColor = Theme.SurfaceRaised; commandBox.ForeColor = Theme.Text; commandBox.Font = new Font("Consolas", 10.5f);
        commandBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { RunCommand(); e.SuppressKeyPress = true; } };
        commandCard.Controls.Add(commandBox);

        runButton.Text = "Run"; runButton.Glyph = "run"; runButton.Primary = true;
        runButton.Dock = DockStyle.Right; runButton.Width = 116; runButton.Margin = new Padding(10, 0, 0, 0);
        runButton.Click += (_, _) => RunCommand();

        stopButton.Text = "Stop"; stopButton.Glyph = "stop";
        stopButton.Dock = DockStyle.Right; stopButton.Width = 104; stopButton.Margin = new Padding(10, 0, 0, 0);
        stopButton.Enabled = false;
        stopButton.Click += (_, _) => { if (runningProcess != null) runningProcess.Kill(); };

        // Last-added docks first: run hugs the right edge, then stop, then the command field fills.
        commandBar.Controls.Add(commandCard);
        commandBar.Controls.Add(stopButton);
        commandBar.Controls.Add(runButton);

        // Output first (fills), command bar last (docks at bottom).
        card.Controls.Add(output);
        card.Controls.Add(commandBar);
        parent.Controls.Add(card);
    }

    private void NewFile() { currentFile = "untitled.cpp"; SetFileTitle("untitled.cpp"); editor.Clear(); statusLabel.Text = "New C++ source file"; }

    private void OpenFile()
    {
        using (var dialog = new OpenFileDialog { Filter = "C/C++ files|*.c;*.cc;*.cpp;*.h;*.hpp|All files|*.*" })
        { if (dialog.ShowDialog(this) != DialogResult.OK) return; editor.Text = File.ReadAllText(dialog.FileName); currentFile = dialog.FileName; SetFileTitle(Path.GetFileName(dialog.FileName)); statusLabel.Text = "Opened " + dialog.FileName; }
    }

    private void SaveFile()
    {
        if (currentFile == "main.cpp" || currentFile == "untitled.cpp")
        { using (var dialog = new SaveFileDialog { Filter = "C++ source|*.cpp|C source|*.c|All files|*.*", FileName = currentFile }) { if (dialog.ShowDialog(this) != DialogResult.OK) return; currentFile = dialog.FileName; } }
        File.WriteAllText(currentFile, editor.Text); SetFileTitle(Path.GetFileName(currentFile)); statusLabel.Text = "Saved " + currentFile;
    }

    private void SetFileTitle(string name)
    {
        fileLabel.Text = name + "   ×";
        fileChip.Width = TextRenderer.MeasureText(fileLabel.Text, fileLabel.Font).Width + fileChip.Padding.Horizontal + 18;
    }

    private void EditorKeyDown(object sender, KeyEventArgs e) { if (e.Control && e.KeyCode == Keys.Space) { ShowCompletion(); e.SuppressKeyPress = true; } }

    private void ShowCompletion()
    {
        var prefix = CurrentWord(); completionList.Items.Clear(); foreach (var word in CppWords.Where(w => prefix.Length == 0 || w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) completionList.Items.Add(word); if (completionList.Items.Count == 0) return;
        var caret = editor.GetPositionFromCharIndex(editor.SelectionStart);
        int left = editor.Left + caret.X;
        int top = editor.Top + caret.Y + editor.Font.Height + 4;
        var surface = editorSurface.ClientSize;
        completionList.Left = Math.Min(Math.Max(left, 8), Math.Max(8, surface.Width - completionList.Width - 8));
        completionList.Top = Math.Min(top, Math.Max(8, surface.Height - completionList.Height - 8));
        completionList.BringToFront(); completionList.Visible = true; completionList.SelectedIndex = 0; completionList.Focus();
    }

    private string CurrentWord()
    {
        var value = editor.Text; var end = editor.SelectionStart; var start = end; while (start > 0 && (char.IsLetterOrDigit(value[start - 1]) || value[start - 1] == '_')) start--; return value.Substring(start, end - start);
    }

    private void InsertCompletion()
    {
        if (!(completionList.SelectedItem is string)) return; var word = (string)completionList.SelectedItem; var prefix = CurrentWord(); editor.SelectedText = word.Substring(prefix.Length); completionList.Visible = false; editor.Focus();
    }

    private async void RunCommand()
    {
        if (runningProcess != null || string.IsNullOrWhiteSpace(commandBox.Text)) return; var command = commandBox.Text.Trim(); terminalOutput.AppendText("> " + command + Environment.NewLine); commandBox.Clear();
        runningProcess = new Process { StartInfo = new ProcessStartInfo { FileName = "cmd.exe", Arguments = "/C " + command, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } }; runButton.Enabled = false; stopButton.Enabled = true;
        try { runningProcess.Start(); var outputTask = runningProcess.StandardOutput.ReadToEndAsync(); var errorTask = runningProcess.StandardError.ReadToEndAsync(); await Task.WhenAll(outputTask, errorTask); runningProcess.WaitForExit(); terminalOutput.AppendText(outputTask.Result + errorTask.Result); terminalOutput.AppendText("[exit " + runningProcess.ExitCode + "]" + Environment.NewLine); }
        catch (Exception ex) { terminalOutput.AppendText("Gestirn dev: " + ex.Message + Environment.NewLine); }
        finally { runningProcess.Dispose(); runningProcess = null; runButton.Enabled = true; stopButton.Enabled = false; }
    }
}
}
