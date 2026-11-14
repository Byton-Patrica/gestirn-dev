using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GestirnDevPreview
{
public sealed class MainForm : Form
{
    private readonly RichTextBox editor = new RichTextBox();
    private readonly RichTextBox terminalOutput = new RichTextBox();
    private readonly TextBox commandBox = new TextBox();
    private readonly Button runButton = new Button();
    private readonly Button stopButton = new Button();
    private readonly ListBox completionList = new ListBox();
    private readonly Label fileLabel = new Label();
    private readonly Label statusLabel = new Label();
    private readonly TreeView projectTree = new TreeView();
    private Process runningProcess;
    private string currentFile = "main.cpp";
    private readonly Color background = Color.FromArgb(30, 30, 34);
    private readonly Color panel = Color.FromArgb(38, 39, 46);
    private readonly Color panel2 = Color.FromArgb(46, 47, 56);
    private readonly Color text = Color.FromArgb(232, 234, 240);
    private static readonly string[] CppWords = { "auto", "bool", "break", "case", "catch", "char", "class", "const", "constexpr", "continue", "default", "delete", "do", "double", "else", "enum", "false", "float", "for", "if", "inline", "int", "long", "namespace", "new", "nullptr", "operator", "private", "protected", "public", "return", "short", "signed", "sizeof", "static", "std", "struct", "switch", "template", "this", "throw", "true", "try", "typedef", "typename", "union", "unsigned", "using", "virtual", "void", "volatile", "while", "vector", "string", "unique_ptr", "shared_ptr", "make_unique", "make_shared" };

    public MainForm()
    {
        Text = "Gestirn dev 0.1.0";
        Icon = LoadIcon();
        Width = 1360; Height = 820; MinimumSize = new Size(980, 620);
        StartPosition = FormStartPosition.CenterScreen; BackColor = background; ForeColor = text;
        BuildMenu(); BuildWorkspace();
    }

    private Icon LoadIcon()
    {
        var path = Path.Combine(Application.StartupPath, "GestirnEra-datarot.ico");
        return File.Exists(path) ? new Icon(path) : null;
    }

    private void BuildMenu()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top, BackColor = panel, ForeColor = text, Renderer = new DarkMenuRenderer() };
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
        help.DropDownItems.Add("About Gestirn dev", null, (_, _) => MessageBox.Show("Gestirn dev 0.1.0\nA modern C/C++ workspace built from the Dev-C++ source line.", "About", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.Add(file); menu.Items.Add(edit); menu.Items.Add(view); menu.Items.Add(run); menu.Items.Add(help);
        MainMenuStrip = menu; Controls.Add(menu);
    }

    private void BuildWorkspace()
    {
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = panel2, Padding = new Padding(12, 8, 10, 8) };
        toolbar.Controls.Add(new Label { Text = "◈  Gestirn dev", AutoSize = true, Font = new Font("Segoe UI Semibold", 13f), ForeColor = Color.White, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleLeft });
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 500, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Color.Transparent };
        actions.Controls.Add(MakeToolbarButton("Terminal", (_, _) => commandBox.Focus()));
        actions.Controls.Add(MakeToolbarButton("Run", (_, _) => RunCommand()));
        actions.Controls.Add(MakeToolbarButton("Save", (_, _) => SaveFile()));
        actions.Controls.Add(MakeToolbarButton("Open", (_, _) => OpenFile()));
        actions.Controls.Add(MakeToolbarButton("New", (_, _) => NewFile()));
        toolbar.Controls.Add(actions); Controls.Add(toolbar);

        var body = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 250, BackColor = background };
        body.Panel1.BackColor = panel; body.Panel2.BackColor = background;
        BuildProjectPanel(body.Panel1); BuildEditorPanel(body.Panel2); Controls.Add(body);
        statusLabel.Text = "Ready  •  Gestirn dev 0.1.0  •  C++ workspace";
        statusLabel.Dock = DockStyle.Bottom; statusLabel.Height = 24; statusLabel.Padding = new Padding(10, 4, 0, 0);
        statusLabel.BackColor = Color.FromArgb(38, 45, 58); statusLabel.ForeColor = Color.FromArgb(190, 205, 230); Controls.Add(statusLabel);
    }

    private Button MakeToolbarButton(string caption, EventHandler handler)
    {
        var button = new Button { Text = caption, Width = 82, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = panel2, ForeColor = text, Margin = new Padding(3, 0, 3, 0) };
        button.FlatAppearance.BorderColor = Color.FromArgb(80, 82, 94); button.Click += handler; return button;
    }

    private void BuildProjectPanel(Control parent)
    {
        parent.Controls.Add(new Label { Text = "WORKSPACE", Dock = DockStyle.Top, Height = 34, Padding = new Padding(12, 10, 0, 0), ForeColor = Color.FromArgb(150, 160, 180), Font = new Font("Segoe UI Semibold", 9f) });
        projectTree.Dock = DockStyle.Fill; projectTree.BorderStyle = BorderStyle.None; projectTree.BackColor = panel; projectTree.ForeColor = text; projectTree.Font = new Font("Segoe UI", 10f);
        var root = projectTree.Nodes.Add("Gestirn workspace"); root.Nodes.Add("main.cpp"); root.Nodes.Add("include"); root.Nodes.Add("src"); root.Expand();
        projectTree.AfterSelect += (_, e) => { if (e.Node.Text == "main.cpp") fileLabel.Text = "  main.cpp   ×"; }; parent.Controls.Add(projectTree);
    }

    private void BuildEditorPanel(Control parent)
    {
        var editorRoot = new Panel { Dock = DockStyle.Fill, BackColor = background };
        var tabBar = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = panel };
        fileLabel.Text = "  main.cpp   ×"; fileLabel.Dock = DockStyle.Left; fileLabel.Width = 180; fileLabel.Padding = new Padding(8, 8, 0, 0); fileLabel.ForeColor = Color.White; tabBar.Controls.Add(fileLabel); editorRoot.Controls.Add(tabBar);
        var codeArea = new Panel { Dock = DockStyle.Fill };
        editor.Dock = DockStyle.Fill; editor.Font = new Font("Consolas", 11f); editor.BackColor = Color.FromArgb(27, 28, 32); editor.ForeColor = Color.FromArgb(224, 228, 236); editor.BorderStyle = BorderStyle.None; editor.AcceptsTab = true;
        editor.Text = "#include <iostream>\r\n\r\nint main()\r\n{\r\n    std::cout << \"Hello, Gestirn dev!\" << std::endl;\r\n    return 0;\r\n}\r\n"; editor.KeyDown += EditorKeyDown; codeArea.Controls.Add(editor);
        completionList.Visible = false; completionList.Font = new Font("Consolas", 10f); completionList.Height = 190; completionList.Width = 240; completionList.BackColor = Color.FromArgb(45, 47, 55); completionList.ForeColor = text;
        completionList.DoubleClick += (_, _) => InsertCompletion(); completionList.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { InsertCompletion(); e.SuppressKeyPress = true; } if (e.KeyCode == Keys.Escape) completionList.Visible = false; }; codeArea.Controls.Add(completionList); editorRoot.Controls.Add(codeArea);
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 440, Panel1MinSize = 200, Panel2MinSize = 120 };
        split.Panel1.Controls.Add(editorRoot); split.Panel2.Controls.Add(BuildTerminal()); parent.Controls.Add(split);
    }

    private Control BuildTerminal()
    {
        terminalOutput.Dock = DockStyle.Fill; terminalOutput.ReadOnly = true; terminalOutput.BackColor = Color.FromArgb(20, 21, 24); terminalOutput.ForeColor = Color.FromArgb(196, 220, 196); terminalOutput.BorderStyle = BorderStyle.None; terminalOutput.Font = new Font("Consolas", 10f); terminalOutput.WordWrap = false; terminalOutput.Text = "Gestirn dev terminal  •  ready\r\n";
        var commandPanel = new Panel { Dock = DockStyle.Bottom, Height = 38, BackColor = panel2, Padding = new Padding(6, 5, 6, 5) };
        commandBox.Dock = DockStyle.Fill; commandBox.BackColor = Color.FromArgb(34, 35, 40); commandBox.ForeColor = text; commandBox.BorderStyle = BorderStyle.FixedSingle; commandBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) RunCommand(); };
        runButton.Text = "Run"; runButton.Dock = DockStyle.Right; runButton.Width = 72; runButton.Click += (_, _) => RunCommand(); stopButton.Text = "Stop"; stopButton.Dock = DockStyle.Right; stopButton.Width = 72; stopButton.Enabled = false; stopButton.Click += (_, _) => { if (runningProcess != null) runningProcess.Kill(); };
        commandPanel.Controls.Add(commandBox); commandPanel.Controls.Add(stopButton); commandPanel.Controls.Add(runButton); var root = new Panel { Dock = DockStyle.Fill }; root.Controls.Add(terminalOutput); root.Controls.Add(commandPanel); return root;
    }

    private void NewFile() { currentFile = "untitled.cpp"; fileLabel.Text = "  untitled.cpp   ×"; editor.Clear(); statusLabel.Text = "New C++ source file"; }

    private void OpenFile()
    {
        using (var dialog = new OpenFileDialog { Filter = "C/C++ files|*.c;*.cc;*.cpp;*.h;*.hpp|All files|*.*" })
        { if (dialog.ShowDialog(this) != DialogResult.OK) return; editor.Text = File.ReadAllText(dialog.FileName); currentFile = dialog.FileName; fileLabel.Text = "  " + Path.GetFileName(dialog.FileName) + "   ×"; statusLabel.Text = "Opened " + dialog.FileName; }
    }

    private void SaveFile()
    {
        if (currentFile == "main.cpp" || currentFile == "untitled.cpp")
        { using (var dialog = new SaveFileDialog { Filter = "C++ source|*.cpp|C source|*.c|All files|*.*", FileName = currentFile }) { if (dialog.ShowDialog(this) != DialogResult.OK) return; currentFile = dialog.FileName; } }
        File.WriteAllText(currentFile, editor.Text); fileLabel.Text = "  " + Path.GetFileName(currentFile) + "   ×"; statusLabel.Text = "Saved " + currentFile;
    }

    private void EditorKeyDown(object sender, KeyEventArgs e) { if (e.Control && e.KeyCode == Keys.Space) { ShowCompletion(); e.SuppressKeyPress = true; } }

    private void ShowCompletion()
    {
        var prefix = CurrentWord(); completionList.Items.Clear(); foreach (var word in CppWords.Where(w => prefix.Length == 0 || w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) completionList.Items.Add(word); if (completionList.Items.Count == 0) return;
        var caret = editor.GetPositionFromCharIndex(editor.SelectionStart); completionList.Left = Math.Min(Math.Max(caret.X, 8), Math.Max(8, editor.ClientSize.Width - completionList.Width - 8)); completionList.Top = Math.Min(caret.Y + editor.Font.Height + 4, Math.Max(8, editor.ClientSize.Height - completionList.Height - 8)); completionList.BringToFront(); completionList.Visible = true; completionList.SelectedIndex = 0; completionList.Focus();
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

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer { public DarkMenuRenderer() : base(new DarkMenuColors()) { } }
internal sealed class DarkMenuColors : ProfessionalColorTable
{
    public override Color MenuBorder { get { return Color.FromArgb(70, 72, 82); } }
    public override Color MenuItemSelected { get { return Color.FromArgb(64, 92, 140); } }
    public override Color MenuItemBorder { get { return Color.FromArgb(64, 92, 140); } }
    public override Color ToolStripDropDownBackground { get { return Color.FromArgb(42, 43, 50); } }
    public override Color ImageMarginGradientBegin { get { return Color.FromArgb(42, 43, 50); } }
    public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(42, 43, 50); } }
    public override Color ImageMarginGradientEnd { get { return Color.FromArgb(42, 43, 50); } }
}
}

