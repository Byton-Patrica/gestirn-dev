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
    private readonly RichTextBox editor = new();
    private readonly RichTextBox terminalOutput = new();
    private readonly TextBox commandBox = new();
    private readonly Button runButton = new();
    private readonly Button stopButton = new();
    private readonly ListBox completionList = new();
    private Process runningProcess;

    private static readonly string[] CppWords =
    {
        "alignas", "alignof", "auto", "bool", "break", "case", "catch", "char",
        "class", "const", "constexpr", "continue", "decltype", "default", "delete",
        "do", "double", "else", "enum", "explicit", "export", "extern", "false",
        "float", "for", "friend", "if", "inline", "int", "long", "namespace",
        "new", "noexcept", "nullptr", "operator", "private", "protected", "public",
        "return", "short", "signed", "sizeof", "static", "std", "struct", "switch",
        "template", "this", "throw", "true", "try", "typedef", "typename", "union",
        "unsigned", "using", "virtual", "void", "volatile", "while", "vector",
        "string", "unique_ptr", "shared_ptr", "make_unique", "make_shared"
    };

    public MainForm()
    {
        Text = "Gestirn dev 0.1.0";
        Icon = LoadIcon();
        Width = 1180;
        Height = 760;
        MinimumSize = new Size(820, 520);
        StartPosition = FormStartPosition.CenterScreen;

        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("File");
        file.DropDownItems.Add("Exit", null, (_, _) => Close());
        var tools = new ToolStripMenuItem("Tools");
        tools.DropDownItems.Add("Embedded terminal", null, (_, _) => commandBox.Focus());
        tools.DropDownItems.Add("Code completion (Ctrl+Space)", null, (_, _) => ShowCompletion());
        menu.Items.Add(file);
        menu.Items.Add(tools);
        MainMenuStrip = menu;
        Controls.Add(menu);

        editor.Dock = DockStyle.Fill;
        editor.Font = new Font("Consolas", 11f);
        editor.AcceptsTab = true;
        editor.Text = "// Gestirn dev 0.1\r\n#include <iostream>\r\n\r\nint main()\r\n{\r\n    std::cout << \"Hello, Gestirn dev!\" << std::endl;\r\n    return 0;\r\n}\r\n";
        editor.KeyDown += EditorKeyDown;

        completionList.Visible = false;
        completionList.Font = new Font("Consolas", 10f);
        completionList.IntegralHeight = true;
        completionList.Height = 180;
        completionList.Width = 220;
        completionList.DoubleClick += (_, _) => InsertCompletion();
        completionList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { InsertCompletion(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) completionList.Visible = false;
        };

        var editorPanel = new Panel { Dock = DockStyle.Fill };
        editorPanel.Controls.Add(editor);
        editorPanel.Controls.Add(completionList);

        terminalOutput.Dock = DockStyle.Fill;
        terminalOutput.ReadOnly = true;
        terminalOutput.BackColor = Color.FromArgb(25, 25, 28);
        terminalOutput.ForeColor = Color.Gainsboro;
        terminalOutput.Font = new Font("Consolas", 10f);
        terminalOutput.WordWrap = false;

        commandBox.Dock = DockStyle.Fill;
        commandBox.Font = new Font("Consolas", 10f);
        commandBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) RunCommand(); };
        runButton.Text = "Run";
        runButton.AutoSize = true;
        runButton.Click += (_, _) => RunCommand();
        stopButton.Text = "Stop";
        stopButton.AutoSize = true;
        stopButton.Enabled = false;
        stopButton.Click += (_, _) => { if (runningProcess != null) runningProcess.Kill(); };

        var commandPanel = new Panel { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(4) };
        commandPanel.Controls.Add(commandBox);
        commandPanel.Controls.Add(stopButton);
        commandPanel.Controls.Add(runButton);
        stopButton.Dock = DockStyle.Right;
        runButton.Dock = DockStyle.Right;

        var terminalPanel = new Panel { Dock = DockStyle.Fill };
        terminalPanel.Controls.Add(terminalOutput);
        terminalPanel.Controls.Add(commandPanel);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 440,
            Panel1MinSize = 160,
            Panel2MinSize = 120
        };
        split.Panel1.Controls.Add(editorPanel);
        split.Panel2.Controls.Add(terminalPanel);
        Controls.Add(split);
        split.BringToFront();
        menu.BringToFront();
    }

    private Icon LoadIcon()
    {
        var path = Path.Combine(Application.StartupPath, "GestirnEra-datarot.ico");
        return File.Exists(path) ? new Icon(path) : null;
    }

    private void EditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Space)
        {
            ShowCompletion();
            e.SuppressKeyPress = true;
        }
    }

    private void ShowCompletion()
    {
        var prefix = CurrentWord();
        completionList.Items.Clear();
        foreach (var word in CppWords.Where(w => prefix.Length == 0 || w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            completionList.Items.Add(word);
        if (completionList.Items.Count == 0) return;
        var caret = editor.GetPositionFromCharIndex(editor.SelectionStart);
        completionList.Left = Math.Min(Math.Max(caret.X, 8), Math.Max(8, editor.ClientSize.Width - completionList.Width - 8));
        completionList.Top = Math.Min(caret.Y + editor.Font.Height + 4, Math.Max(8, editor.ClientSize.Height - completionList.Height - 8));
        completionList.BringToFront();
        completionList.Visible = true;
        completionList.SelectedIndex = 0;
        completionList.Focus();
    }

    private string CurrentWord()
    {
        var text = editor.Text;
        var end = editor.SelectionStart;
        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
        return text.Substring(start, end - start);
    }

    private void InsertCompletion()
    {
        if (completionList.SelectedItem is not string word) return;
        var prefix = CurrentWord();
        editor.SelectedText = word.Substring(prefix.Length);
        completionList.Visible = false;
        editor.Focus();
    }

    private async void RunCommand()
    {
        if (runningProcess != null || string.IsNullOrWhiteSpace(commandBox.Text)) return;
        var command = commandBox.Text.Trim();
        terminalOutput.AppendText("> " + command + Environment.NewLine);
        commandBox.Clear();
        runningProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/C " + command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };
        runButton.Enabled = false;
        stopButton.Enabled = true;
        try
        {
            runningProcess.Start();
            var outputTask = runningProcess.StandardOutput.ReadToEndAsync();
            var errorTask = runningProcess.StandardError.ReadToEndAsync();
            await Task.WhenAll(outputTask, errorTask);
            runningProcess.WaitForExit();
            terminalOutput.AppendText(outputTask.Result + errorTask.Result);
            terminalOutput.AppendText("[exit " + runningProcess.ExitCode + "]" + Environment.NewLine);
        }
        catch (Exception ex)
        {
            terminalOutput.AppendText("Gestirn dev: " + ex.Message + Environment.NewLine);
        }
        finally
        {
            runningProcess.Dispose();
            runningProcess = null;
            runButton.Enabled = true;
            stopButton.Enabled = false;
        }
    }

}
}

