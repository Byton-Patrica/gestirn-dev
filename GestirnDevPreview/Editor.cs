using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace GestirnDevPreview
{
// One open file (or an untitled buffer) with its own editor and tab chip.
internal sealed class Document
{
    private static int counter;

    public Document(string filePath)
    {
        FilePath = filePath;
        Number = ++counter;
        Editor.BorderStyle = BorderStyle.None;
        Editor.BackColor = Theme.Editor;
        Editor.ForeColor = Theme.EditorText;
        Editor.Font = Theme.CodeFont;
        Editor.AcceptsTab = true;
        Editor.WordWrap = false;
        Editor.DetectUrls = false;
        Editor.Dock = DockStyle.Fill;
        Gutter.Target = Editor;
        Gutter.Dock = DockStyle.Left;
        Gutter.Width = 56;
        Host.BackColor = Theme.Editor;
        Host.Dock = DockStyle.Fill;
        Host.Visible = false;
        // Fill first, then the docked gutter.
        Host.Controls.Add(Editor);
        Host.Controls.Add(Gutter);

        Chip.Radius = 10;
        Chip.Padding = new Padding(12, 0, 6, 0);
        ChipName.Dock = DockStyle.Fill;
        ChipName.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        ChipName.Font = Theme.UiSemi;
        ChipClose.Dock = DockStyle.Right;
        ChipClose.Width = 22;
        ChipClose.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        ChipClose.Font = Theme.UiFont;
        ChipClose.Text = "×";
        ChipClose.Cursor = Cursors.Hand;
        ChipName.Cursor = Cursors.Hand;
        Chip.Controls.Add(ChipName);
        Chip.Controls.Add(ChipClose);
    }

    public string FilePath { get; set; }
    public readonly int Number;
    public string SavedText = "";
    public bool Dirty;

    public readonly Panel Host = new Panel();
    public readonly RichTextBox Editor = new RichTextBox();
    public readonly LineGutter Gutter = new LineGutter();
    public readonly Card Chip = new Card();
    public readonly Label ChipName = new Label();
    public readonly Label ChipClose = new Label();

    public string Title
    {
        get { return FilePath == null ? "untitled" + Number + ".cpp" : System.IO.Path.GetFileName(FilePath); }
    }
}

// Line numbers drawn beside a RichTextBox. The owner repaints it on a timer so it follows scrolling.
internal sealed class LineGutter : Control
{
    public RichTextBox Target;

    public LineGutter()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        BackColor = Theme.Editor;
        ForeColor = Theme.Muted;
        Font = new Font("Consolas", 9.5f);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (Target == null || Target.IsDisposed) return;

        int lastLine = Target.GetLineFromCharIndex(Math.Max(0, Target.TextLength));
        int firstLine = Target.GetLineFromCharIndex(Target.GetCharIndexFromPosition(new Point(1, 1)));
        for (int line = firstLine; line <= lastLine; line++)
        {
            int index = Target.GetFirstCharIndexFromLine(line);
            if (index < 0) break;
            Point position = Target.GetPositionFromCharIndex(index);
            if (position.Y > Height) break;
            TextRenderer.DrawText(g, (line + 1).ToString(), Font,
                new Rectangle(0, position.Y, Width - 10, Font.Height + 2), Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
        using (var pen = new Pen(Theme.Edge))
            g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
    }
}

// Colours keywords, literals, comments and preprocessor lines in one pass.
internal static class Highlighter
{
    private const int WM_SETREDRAW = 0x000B;

    private static readonly string[] Keywords =
    {
        "alignas", "alignof", "asm", "auto", "bool", "break", "case", "catch", "char", "char16_t", "char32_t", "class", "const",
        "constexpr", "const_cast", "continue", "decltype", "default", "delete", "do", "double", "dynamic_cast", "else", "enum",
        "explicit", "export", "extern", "false", "float", "for", "friend", "goto", "if", "inline", "int", "long", "mutable",
        "namespace", "new", "noexcept", "nullptr", "operator", "override", "private", "protected", "public", "register",
        "reinterpret_cast", "return", "short", "signed", "sizeof", "static", "static_assert", "static_cast", "struct", "switch",
        "template", "this", "thread_local", "throw", "true", "try", "typedef", "typeid", "typename", "union", "unsigned",
        "using", "virtual", "void", "volatile", "wchar_t", "while", "final", "std",
    };

    private static readonly Regex Pattern = new Regex(
        @"(?<comment>//[^\r\n]*|/\*[\s\S]*?\*/)" +
        @"|(?<string>""(?:\\.|[^""\\\r\n])*""|'(?:\\.|[^'\\\r\n])*')" +
        @"|(?<pre>^[ \t]*#[^\r\n]*)" +
        @"|(?<keyword>\b(?:" + string.Join("|", Keywords) + @")\b)" +
        @"|(?<number>\b\d[\w.']*)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static void Apply(RichTextBox box)
    {
        string text = box.Text;
        int selectionStart = box.SelectionStart;
        int selectionLength = box.SelectionLength;

        SendMessage(box.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try
        {
            box.Select(0, box.TextLength);
            box.SelectionColor = Theme.EditorText;
            foreach (Match match in Pattern.Matches(text))
            {
                Color color;
                if (match.Groups["comment"].Success) color = Theme.Comment;
                else if (match.Groups["string"].Success) color = Theme.StringText;
                else if (match.Groups["pre"].Success) color = Theme.Preprocessor;
                else if (match.Groups["keyword"].Success) color = Theme.Keyword;
                else color = Theme.Number;

                box.Select(match.Index, match.Length);
                box.SelectionColor = color;
            }
            box.Select(selectionStart, selectionLength);
        }
        finally
        {
            SendMessage(box.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
            box.Invalidate();
        }
    }
}
}
