using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GestirnDev
{
internal enum CompletionKind { Keyword, Type, Function, Variable, Snippet, Header, Macro, Member }

internal sealed class CompletionItem
{
    public string Label, Insert, Detail;
    public CompletionKind Kind;
    public int Score;
    public List<int> Matches = new List<int>();

    public CompletionItem(string label, CompletionKind kind, string detail = null, string insert = null)
    {
        Label = label; Kind = kind; Detail = detail; Insert = insert ?? label;
    }

    public CompletionItem Clone() { return new CompletionItem(Label, Kind, Detail, Insert); }
}

internal enum CompletionContext { Word, Member, Include, Directive }

// Gathers candidates from the language, the standard library, snippets and every open document.
internal sealed class CompletionProvider
{
    public Func<IEnumerable<string>> OtherDocumentWords;

    private static readonly string[] StdTypes =
    {
        "string", "vector", "map", "set", "multimap", "multiset", "unordered_map", "unordered_set", "pair", "tuple", "queue",
        "priority_queue", "stack", "deque", "list", "array", "bitset", "stringstream", "istringstream", "ostringstream",
        "ifstream", "ofstream", "fstream", "size_t", "int64_t", "uint64_t", "int32_t", "uint32_t", "long long",
    };

    private static readonly string[] StdFunctions =
    {
        "sort", "stable_sort", "reverse", "max", "min", "swap", "abs", "fabs", "sqrt", "pow", "floor", "ceil", "round", "exp",
        "log", "log2", "log10", "sin", "cos", "tan", "atan2", "memset", "memcpy", "strlen", "strcpy", "strcat", "strcmp",
        "strncmp", "atoi", "atof", "to_string", "stoi", "stol", "stoll", "stod", "getline", "printf", "scanf", "puts",
        "putchar", "getchar", "fgets", "fopen", "fclose", "fprintf", "fscanf", "malloc", "calloc", "realloc", "free", "exit",
        "rand", "srand", "time", "clock", "lower_bound", "upper_bound", "binary_search", "unique", "fill", "find", "count",
        "accumulate", "next_permutation", "prev_permutation", "make_pair", "max_element", "min_element", "isdigit",
        "isalpha", "isupper", "islower", "toupper", "tolower", "system", "gcd", "iota", "transform", "remove", "replace",
    };

    private static readonly string[] StdObjects =
    {
        "cin", "cout", "cerr", "endl", "std", "ios", "sync_with_stdio", "INT_MAX", "INT_MIN", "LLONG_MAX", "LLONG_MIN",
        "UINT_MAX", "EOF", "nullptr", "NULL", "true", "false",
    };

    private static readonly string[] Members =
    {
        "push_back", "pop_back", "emplace_back", "push_front", "pop_front", "size", "empty", "clear", "begin", "end",
        "rbegin", "rend", "cbegin", "cend", "insert", "erase", "find", "count", "front", "back", "top", "push", "pop",
        "substr", "length", "c_str", "append", "first", "second", "at", "resize", "reserve", "data", "swap", "emplace",
        "lower_bound", "upper_bound", "str", "get", "put", "open", "close", "is_open", "eof", "good", "fail", "ignore",
    };

    public static readonly string[] Headers =
    {
        "iostream", "cstdio", "cstdlib", "cstring", "cmath", "string", "vector", "map", "set", "unordered_map",
        "unordered_set", "queue", "stack", "deque", "list", "algorithm", "numeric", "utility", "functional", "iomanip",
        "sstream", "fstream", "bitset", "climits", "cctype", "ctime", "cassert", "memory", "thread", "mutex", "chrono",
        "random", "array", "tuple", "iterator", "limits", "bits/stdc++.h", "windows.h", "conio.h", "stdio.h", "stdlib.h",
        "string.h", "math.h", "time.h", "ctype.h",
    };

    private static readonly string[] Directives = { "include", "define", "ifdef", "ifndef", "endif", "else", "elif", "if", "pragma", "undef", "error" };

    private sealed class Snippet
    {
        public string Name, Body, En, Zh;
        public Snippet(string name, string body, string en, string zh) { Name = name; Body = body; En = en; Zh = zh; }
    }

    // \t is replaced by one indent unit, $0 marks where the caret lands.
    private static readonly Snippet[] Snippets =
    {
        new Snippet("main", "int main()\n{\n\t$0\n\treturn 0;\n}", "main function", "main 函数"),
        new Snippet("for", "for (int i = 0; i < $0; i++) {\n\t\n}", "counting loop", "计数循环"),
        new Snippet("forr", "for (auto& x : $0) {\n\t\n}", "range-for loop", "范围 for 循环"),
        new Snippet("while", "while ($0) {\n\t\n}", "while loop", "while 循环"),
        new Snippet("do", "do {\n\t$0\n} while ();", "do-while loop", "do-while 循环"),
        new Snippet("if", "if ($0) {\n\t\n}", "if block", "if 语句"),
        new Snippet("ife", "if ($0) {\n\t\n} else {\n\t\n}", "if / else", "if / else 语句"),
        new Snippet("switch", "switch ($0) {\ncase 0:\n\tbreak;\ndefault:\n\tbreak;\n}", "switch block", "switch 语句"),
        new Snippet("class", "class $0 {\npublic:\n\t\nprivate:\n\t\n};", "class", "类定义"),
        new Snippet("struct", "struct $0 {\n\t\n};", "struct", "结构体"),
        new Snippet("cout", "cout << $0 << endl;", "print a line", "输出一行"),
        new Snippet("cin", "cin >> $0;", "read input", "读取输入"),
        new Snippet("printf", "printf(\"$0\\n\");", "formatted print", "格式化输出"),
        new Snippet("scanf", "scanf(\"%d\", &$0);", "formatted read", "格式化读取"),
        new Snippet("inc", "#include <$0>", "include a header", "包含头文件"),
        new Snippet("usingstd", "using namespace std;", "use namespace std", "使用 std 命名空间"),
        new Snippet("try", "try {\n\t$0\n} catch (const std::exception& e) {\n\t\n}", "try / catch", "异常捕获"),
        new Snippet("template", "template <typename T>\n$0", "template", "模板"),
    };

    public List<CompletionItem> Collect(CompletionContext context, string prefix, ICollection<string> documentWords, bool usesStdNamespace)
    {
        var pool = new List<CompletionItem>();
        switch (context)
        {
            case CompletionContext.Include:
                foreach (var h in Headers) pool.Add(new CompletionItem(h, CompletionKind.Header, L.T("header", "头文件")));
                break;
            case CompletionContext.Directive:
                foreach (var d in Directives) pool.Add(new CompletionItem(d, CompletionKind.Macro, L.T("directive", "预处理指令")));
                break;
            case CompletionContext.Member:
                foreach (var m in Members) pool.Add(new CompletionItem(m, CompletionKind.Member, L.T("member", "成员")));
                foreach (var w in documentWords) pool.Add(new CompletionItem(w, CompletionKind.Variable, L.T("in file", "文件内")));
                break;
            default:
                foreach (var s in Snippets)
                {
                    string body = s.Body;
                    if (!usesStdNamespace) body = body.Replace("cout << ", "std::cout << ").Replace("<< endl", "<< std::endl").Replace("cin >> ", "std::cin >> ");
                    pool.Add(new CompletionItem(s.Name, CompletionKind.Snippet, L.T(s.En, s.Zh), body));
                }
                foreach (var k in CppSyntax.Keywords) pool.Add(new CompletionItem(k, CompletionKind.Keyword, L.T("keyword", "关键字")));
                foreach (var t in CppSyntax.Types) pool.Add(new CompletionItem(t, CompletionKind.Type, L.T("type", "类型")));
                foreach (var t in StdTypes) pool.Add(new CompletionItem(t, CompletionKind.Type, "std"));
                foreach (var f in StdFunctions) pool.Add(new CompletionItem(f, CompletionKind.Function, "std"));
                foreach (var o in StdObjects) pool.Add(new CompletionItem(o, CompletionKind.Variable, "std"));
                foreach (var w in documentWords) pool.Add(new CompletionItem(w, CompletionKind.Variable, L.T("in file", "文件内")));
                if (OtherDocumentWords != null)
                    foreach (var w in OtherDocumentWords()) pool.Add(new CompletionItem(w, CompletionKind.Variable, L.T("open files", "已打开文件")));
                break;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<CompletionItem>();
        foreach (var item in pool)
        {
            if (!seen.Add(item.Label + "\u0001" + (item.Kind == CompletionKind.Snippet ? "s" : ""))) continue;
            if (item.Kind != CompletionKind.Snippet && prefix.Length > 0 && item.Label == prefix) continue;
            List<int> matches;
            int score = Score(item.Label, prefix, out matches);
            if (score < 0) continue;
            if (item.Kind == CompletionKind.Snippet) score += prefix.Length > 0 && item.Label.StartsWith(prefix, StringComparison.Ordinal) ? 30 : -200;
            if (item.Kind == CompletionKind.Variable && context == CompletionContext.Word) score += 15;
            item.Score = score;
            item.Matches = matches;
            result.Add(item);
        }
        return result.OrderByDescending(i => i.Score).ThenBy(i => i.Label.Length).ThenBy(i => i.Label, StringComparer.Ordinal).Take(60).ToList();
    }

    // Prefix matches rank highest; otherwise a case-insensitive subsequence match,
    // with a bonus when matched letters start words (snake_case or camelCase).
    public static int Score(string label, string prefix, out List<int> matches)
    {
        matches = new List<int>();
        if (prefix.Length == 0) return 1;
        if (label.StartsWith(prefix, StringComparison.Ordinal))
        {
            for (int i = 0; i < prefix.Length; i++) matches.Add(i);
            return 1000 - label.Length;
        }
        if (label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < prefix.Length; i++) matches.Add(i);
            return 900 - label.Length;
        }
        int p = 0, bonus = 0, gaps = 0, last = -1;
        for (int i = 0; i < label.Length && p < prefix.Length; i++)
        {
            if (char.ToLowerInvariant(label[i]) != char.ToLowerInvariant(prefix[p])) continue;
            bool boundary = i == 0 || label[i - 1] == '_' || (char.IsUpper(label[i]) && char.IsLower(label[i - 1]));
            if (boundary) bonus += 3;
            if (last >= 0) gaps += i - last - 1;
            matches.Add(i);
            last = i;
            p++;
        }
        if (p < prefix.Length) { matches.Clear(); return -1; }
        if (matches[0] > 0 && bonus == 0) return -1;
        return 500 + bonus * 10 - gaps - label.Length;
    }
}

// The suggestion list. It never takes focus: the editor routes Up/Down/Enter/Tab/Esc to it.
internal sealed class CompletionPopup : Control
{
    private List<CompletionItem> items = new List<CompletionItem>();
    private int selected, top;
    private readonly AnimatedFloat reveal = new AnimatedFloat(0f);
    private int fullHeight;
    private const int MaxRows = 9;

    public CompletionPopup()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Visible = false;
        Cursor = Cursors.Default;
        reveal.Changed += () =>
        {
            Height = Math.Max(1, (int)(fullHeight * (0.55f + 0.45f * reveal.Value)));
            Invalidate();
        };
    }

    public event Action<CompletionItem> Accepted;
    public int RowHeight { get { return Ui.D(26); } }
    public CompletionItem Selected { get { return selected >= 0 && selected < items.Count ? items[selected] : null; } }
    public int Count { get { return items.Count; } }

    public void ShowItems(List<CompletionItem> list, Point caretBottom, int lineHeight, Size area)
    {
        items = list;
        selected = 0;
        top = 0;
        int labelWidth = 0, detailWidth = 0;
        foreach (var item in items.Take(40))
        {
            labelWidth = Math.Max(labelWidth, TextRenderer.MeasureText(item.Label, Theme.Code).Width);
            if (item.Detail != null) detailWidth = Math.Max(detailWidth, TextRenderer.MeasureText(item.Detail, Theme.UiSmall).Width);
        }
        int width = Math.Max(Ui.D(240), Math.Min(Ui.D(480), Ui.D(44) + labelWidth + Ui.D(24) + detailWidth + Ui.D(14)));
        fullHeight = Math.Min(items.Count, MaxRows) * RowHeight + Ui.D(8);
        int x = Math.Max(Ui.D(4), Math.Min(caretBottom.X - Ui.D(40), area.Width - width - Ui.D(8)));
        int y = caretBottom.Y + Ui.D(4);
        if (y + fullHeight > area.Height - Ui.D(4) && caretBottom.Y - lineHeight - fullHeight - Ui.D(4) > 0)
            y = caretBottom.Y - lineHeight - fullHeight - Ui.D(4);
        Bounds = new Rectangle(x, y, width, fullHeight);
        if (!Visible)
        {
            Visible = true;
            reveal.Snap(0f);
            reveal.To(1f, 120);
        }
        else Height = fullHeight;
        BringToFront();
        Invalidate();
    }

    public void Hide2() { Visible = false; }

    public void MoveSelection(int delta)
    {
        if (items.Count == 0) return;
        selected = (selected + delta + items.Count) % items.Count;
        if (selected < top) top = selected;
        if (selected >= top + MaxRows) top = selected - MaxRows + 1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int row = (e.Y - Ui.D(4)) / RowHeight + top;
        if (row >= 0 && row < items.Count)
        {
            selected = row;
            Invalidate();
            if (Accepted != null) Accepted(items[row]);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        int max = Math.Max(0, items.Count - MaxRows);
        top = Math.Max(0, Math.Min(max, top - Math.Sign(e.Delta) * 2));
        Invalidate();
    }

    private static Color KindColor(CompletionKind kind)
    {
        switch (kind)
        {
            case CompletionKind.Keyword: return Theme.SynKeyword;
            case CompletionKind.Type: return Theme.SynType;
            case CompletionKind.Function: return Theme.SynFunction;
            case CompletionKind.Snippet: return Theme.Lime;
            case CompletionKind.Header: return Theme.SynString;
            case CompletionKind.Macro: return Theme.SynPreproc;
            case CompletionKind.Member: return Theme.Gold;
            default: return Theme.TextDim;
        }
    }

    private static string KindLetter(CompletionKind kind)
    {
        switch (kind)
        {
            case CompletionKind.Keyword: return "k";
            case CompletionKind.Type: return "T";
            case CompletionKind.Function: return "f";
            case CompletionKind.Snippet: return "#";
            case CompletionKind.Header: return "h";
            case CompletionKind.Macro: return "m";
            case CompletionKind.Member: return ".";
            default: return "v";
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : Theme.EditorBg);
        var r = new RectangleF(0, 0, Width, Height);
        Shapes.FillRound(g, Theme.PanelHi, r, Ui.D(10));
        Shapes.StrokeRound(g, Theme.Edge, r, Ui.D(10), Math.Max(1f, Ui.Scale * 0.75f));

        int rowH = RowHeight;
        for (int i = 0; i < MaxRows && top + i < items.Count; i++)
        {
            var item = items[top + i];
            int y = Ui.D(4) + i * rowH;
            if (y > Height) break;
            bool isSel = top + i == selected;
            if (isSel)
            {
                Shapes.FillRound(g, Theme.Hover, new RectangleF(Ui.D(4), y, Width - Ui.D(8), rowH), Ui.D(7));
                Shapes.FillRound(g, Theme.Orange, new RectangleF(Ui.D(5), y + Ui.D(6), Ui.D(3), rowH - Ui.D(12)), Ui.D(1.5f));
            }
            var color = KindColor(item.Kind);
            var badge = new RectangleF(Ui.D(14), y + (rowH - Ui.D(16)) / 2f, Ui.D(16), Ui.D(16));
            Shapes.FillRound(g, Ui.Alpha(color, 46), badge, Ui.D(4));
            Shapes.Text(g, KindLetter(item.Kind), Theme.UiSmall, Rectangle.Round(badge), color, Shapes.Center);

            int x = Ui.D(40);
            var textRect = new Rectangle(x, y, Width - x - Ui.D(10), rowH);
            DrawLabel(g, item, textRect, isSel);
            if (item.Detail != null)
                Shapes.Text(g, item.Detail, Theme.UiSmall, new Rectangle(x, y, Width - x - Ui.D(12), rowH), Theme.Muted, Shapes.Right);
        }
    }

    private static void DrawLabel(Graphics g, CompletionItem item, Rectangle rect, bool selectedRow)
    {
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        int x = rect.X;
        string label = item.Label;
        int i = 0;
        while (i < label.Length)
        {
            bool match = item.Matches.Contains(i);
            int j = i + 1;
            while (j < label.Length && item.Matches.Contains(j) == match) j++;
            string part = label.Substring(i, j - i);
            var color = match ? Theme.OrangeHot : (selectedRow ? Theme.Text : Theme.SynPlain);
            TextRenderer.DrawText(g, part, Theme.Code, new Rectangle(x, rect.Y, rect.Width, rect.Height), color, flags);
            x += TextRenderer.MeasureText(g, part, Theme.Code, Size.Empty, flags).Width;
            i = j;
        }
    }
}
}
