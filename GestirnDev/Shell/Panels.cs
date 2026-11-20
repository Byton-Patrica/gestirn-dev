using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GestirnDev
{
// One open file (or untitled buffer) and its editor.
internal sealed class EditorDoc
{
    private static int untitledCounter;
    public string Path;
    public readonly int Number;
    public readonly CodeEditor Editor;
    public DateTime DiskTime;
    public bool CrLf = true;
    public bool Gbk;

    public EditorDoc(string path, CodeEditor editor)
    {
        Path = path;
        Editor = editor;
        if (path == null) Number = ++untitledCounter;
    }

    public string Title { get { return Path == null ? L.T("Untitled-", "未命名-") + Number : System.IO.Path.GetFileName(Path); } }
    public bool Dirty { get { return Editor.IsModified; } }
    public bool IsSource { get { return Path != null && DevProject.IsSource(Path) && !Path.EndsWith(".rc", StringComparison.OrdinalIgnoreCase); } }
}

// Small uppercase title with icon buttons on the right.
internal sealed class PanelHeader : Control
{
    private readonly List<PixelButton> buttons = new List<PixelButton>();
    private readonly ToolTip tip = new ToolTip();

    public PanelHeader(string title)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Text = title;
        Height = Ui.D(38);
        BackColor = Theme.Panel;
    }

    public string Glyph;

    public PixelButton AddButton(string glyph, string tipText, Action action)
    {
        var b = new PixelButton { Glyph = glyph, Kind = ButtonKind.Ghost, Size = Ui.D(26, 26), Radius = 7 };
        b.Click += (_, _) => action();
        tip.SetToolTip(b, tipText);
        buttons.Add(b);
        Controls.Add(b);
        PerformLayout();
        return b;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int x = Width - Ui.D(8);
        for (int i = buttons.Count - 1; i >= 0; i--)
        {
            x -= buttons[i].Width;
            buttons[i].Location = new Point(x, (Height - buttons[i].Height) / 2);
            x -= Ui.D(2);
        }
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        int x = Ui.D(16);
        if (Glyph != null)
        {
            Glyphs.Draw(e.Graphics, Glyph, new Rectangle(x, (Height - Ui.D(11)) / 2, Ui.D(11), Ui.D(11)), Theme.Orange);
            x += Ui.D(18);
        }
        Shapes.Text(e.Graphics, Text.ToUpperInvariant(), Theme.UiSmall, new Rectangle(x, 0, Width - x - buttons.Count * Ui.D(28) - Ui.D(10), Height), Theme.TextDim, Shapes.Left);
    }
}

// ---------------------------------------------------------------------------------- explorer

internal sealed class ExplorerPanel : Panel
{
    private sealed class Node
    {
        public string Path, Name;
        public bool IsDir, Expanded, Loaded;
        public int Depth;
        public List<Node> Children = new List<Node>();
    }

    public readonly PanelHeader Header = new PanelHeader(L.T("Explorer", "资源管理器"));
    private readonly VirtualList list = new VirtualList();
    private readonly PixelButton openFolder = new PixelButton { Text = L.T("Open Folder", "打开文件夹"), Glyph = "folder", Kind = ButtonKind.Primary };
    private readonly PixelButton newProject = new PixelButton { Text = L.T("New Project", "新建项目"), Glyph = "project" };
    private Node root;
    private readonly List<Node> visible = new List<Node>();
    private FileSystemWatcher watcher;
    private readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer { Interval = 350 };

    public event Action<string> FileOpened;
    public event Action OpenFolderRequested, NewProjectRequested;
    public event Action<string, bool> ProjectMembershipChanged;
    public Func<string, bool> InProject = _ => false;
    public Func<bool> HasProject = () => false;
    public string Root { get { return root != null ? root.Path : null; } }

    public ExplorerPanel()
    {
        BackColor = Theme.Panel;
        list.RowHeight = Ui.D(26);
        list.EmptyText = L.T("Open a folder to browse its files, or create a project.", "打开一个文件夹来浏览文件，或新建一个项目。");
        list.DrawRow = DrawRow;
        list.RowClicked += (i, e) => { if (e.Button == MouseButtons.Left) Activate(i); };
        list.RowActivated += Activate;
        list.RowRightClicked += ShowContext;
        openFolder.Click += (_, _) => { if (OpenFolderRequested != null) OpenFolderRequested(); };
        newProject.Click += (_, _) => { if (NewProjectRequested != null) NewProjectRequested(); };
        Header.AddButton("new", L.T("New file", "新建文件"), () => CreateEntry(false, SelectedFolder()));
        Header.AddButton("newfolder", L.T("New folder", "新建文件夹"), () => CreateEntry(true, SelectedFolder()));
        Header.AddButton("refresh", L.T("Refresh", "刷新"), Reload);
        Header.AddButton("collapse", L.T("Collapse all", "全部折叠"), CollapseAll);
        Controls.Add(list);
        Controls.Add(openFolder);
        Controls.Add(newProject);
        Controls.Add(Header);
        openFolder.BringToFront();
        newProject.BringToFront();
        refresh.Tick += (_, _) => { refresh.Stop(); Reload(); };
        UpdateEmpty();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Header.SetBounds(0, 0, Width, Header.Height);
        list.SetBounds(0, Header.Height, Width, Math.Max(0, Height - Header.Height));
        int bw = Math.Min(Width - Ui.D(32), Ui.D(200));
        openFolder.SetBounds((Width - bw) / 2, Header.Height + Ui.D(70), bw, Ui.D(34));
        newProject.SetBounds((Width - bw) / 2, Header.Height + Ui.D(112), bw, Ui.D(34));
    }

    private void UpdateEmpty()
    {
        bool empty = root == null;
        openFolder.Visible = newProject.Visible = empty;
        foreach (var b in Header.Controls.OfType<PixelButton>()) b.Visible = !empty;
    }

    public void SetRoot(string folder, string projectName)
    {
        if (watcher != null) { watcher.Dispose(); watcher = null; }
        if (folder == null || !Directory.Exists(folder))
        {
            root = null;
            Header.Text = L.T("Explorer", "资源管理器");
            Header.Glyph = null;
            visible.Clear();
            list.Reset(0, false);
            UpdateEmpty();
            return;
        }
        root = new Node { Path = folder, Name = System.IO.Path.GetFileName(folder.TrimEnd('\\')), IsDir = true, Expanded = true, Depth = -1 };
        if (string.IsNullOrEmpty(root.Name)) root.Name = folder;
        Header.Text = projectName ?? root.Name;
        Header.Glyph = projectName != null ? "project" : "folder";
        Load(root);
        Flatten();
        list.Reset(visible.Count, false);
        UpdateEmpty();
        try
        {
            watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
            FileSystemEventHandler changed = (_, ev) =>
            {
                if (ev.FullPath.Contains("\\.") ) return;
                try { BeginInvoke(new Action(() => { refresh.Stop(); refresh.Start(); })); }
                catch (InvalidOperationException) { }
            };
            watcher.Created += changed;
            watcher.Deleted += changed;
            watcher.Renamed += (s, ev) => changed(s, ev);
            watcher.EnableRaisingEvents = true;
        }
        catch (ArgumentException) { }
        catch (IOException) { }
    }

    public void Reload()
    {
        if (root == null) return;
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(root, expanded);
        root.Loaded = false;
        Load(root);
        Reexpand(root, expanded);
        Flatten();
        list.Reset(visible.Count);
    }

    public void InvalidateList() { list.Invalidate(); }

    private static void Collect(Node n, HashSet<string> set)
    {
        if (n.Expanded) set.Add(n.Path);
        foreach (var c in n.Children) Collect(c, set);
    }

    private void Reexpand(Node n, HashSet<string> set)
    {
        foreach (var c in n.Children.Where(c => c.IsDir && set.Contains(c.Path)))
        {
            c.Expanded = true;
            Load(c);
            Reexpand(c, set);
        }
    }

    private static bool Hidden(string path)
    {
        string name = System.IO.Path.GetFileName(path);
        if (name.StartsWith(".")) return true;
        try { return (File.GetAttributes(path) & FileAttributes.Hidden) != 0; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private static void Load(Node n)
    {
        n.Children.Clear();
        n.Loaded = true;
        try
        {
            foreach (var d in Directory.GetDirectories(n.Path).Where(p => !Hidden(p)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                n.Children.Add(new Node { Path = d, Name = System.IO.Path.GetFileName(d), IsDir = true, Depth = n.Depth + 1 });
            foreach (var f in Directory.GetFiles(n.Path).Where(p => !Hidden(p)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                n.Children.Add(new Node { Path = f, Name = System.IO.Path.GetFileName(f), Depth = n.Depth + 1 });
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void Flatten()
    {
        visible.Clear();
        if (root == null) return;
        Action<Node> walk = null;
        walk = n =>
        {
            foreach (var c in n.Children)
            {
                visible.Add(c);
                if (c.IsDir && c.Expanded) walk(c);
            }
        };
        walk(root);
    }

    private void Activate(int i)
    {
        if (i < 0 || i >= visible.Count) return;
        var n = visible[i];
        if (n.IsDir)
        {
            n.Expanded = !n.Expanded;
            if (n.Expanded && !n.Loaded) Load(n);
            Flatten();
            list.Reset(visible.Count);
        }
        else if (FileOpened != null) FileOpened(n.Path);
    }

    private void CollapseAll()
    {
        if (root == null) return;
        Action<Node> walk = null;
        walk = n => { foreach (var c in n.Children) { c.Expanded = false; walk(c); } };
        walk(root);
        Flatten();
        list.Reset(visible.Count);
    }

    public void Reveal(string path)
    {
        if (root == null || path == null || !path.StartsWith(root.Path, StringComparison.OrdinalIgnoreCase)) return;
        var node = root;
        string rel = path.Substring(root.Path.Length).TrimStart('\\');
        foreach (var part in rel.Split('\\'))
        {
            if (!node.Loaded) Load(node);
            var next = node.Children.FirstOrDefault(c => string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
            if (next == null) break;
            if (next.IsDir) next.Expanded = true;
            node = next;
        }
        Flatten();
        list.Reset(visible.Count);
        int idx = visible.IndexOf(node);
        if (idx >= 0) list.SelectedIndex = idx;
    }

    private string SelectedFolder()
    {
        int i = list.SelectedIndex;
        if (i < 0 || i >= visible.Count) return root != null ? root.Path : null;
        var n = visible[i];
        return n.IsDir ? n.Path : System.IO.Path.GetDirectoryName(n.Path);
    }

    private void CreateEntry(bool folder, string parent)
    {
        if (parent == null) return;
        string name = Prompt.Show(FindForm(), folder ? L.T("New folder", "新建文件夹") : L.T("New file", "新建文件"),
            folder ? L.T("Folder name", "文件夹名称") : L.T("File name (e.g. main.cpp)", "文件名（例如 main.cpp）"), folder ? "" : "untitled.cpp");
        if (string.IsNullOrWhiteSpace(name)) return;
        string full = System.IO.Path.Combine(parent, name.Trim());
        try
        {
            if (folder) Directory.CreateDirectory(full);
            else if (!File.Exists(full)) File.WriteAllText(full, "", new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
        {
            MessageBox.Show(FindForm(), ex.Message, "Gestirn dev", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Reload();
        Reveal(full);
        if (!folder)
        {
            if (HasProject() && DevProject.IsSource(full) && ProjectMembershipChanged != null) ProjectMembershipChanged(full, true);
            if (FileOpened != null) FileOpened(full);
        }
    }

    private void ShowContext(int i, Point p)
    {
        if (i < 0 || i >= visible.Count) return;
        var n = visible[i];
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), Font = Theme.Ui9, BackColor = Theme.PanelHi, ForeColor = Theme.Text, ShowImageMargin = false };
        string folder = n.IsDir ? n.Path : System.IO.Path.GetDirectoryName(n.Path);
        if (!n.IsDir) menu.Items.Add(L.T("Open", "打开"), null, (_, _) => { if (FileOpened != null) FileOpened(n.Path); });
        menu.Items.Add(L.T("New File...", "新建文件..."), null, (_, _) => CreateEntry(false, folder));
        menu.Items.Add(L.T("New Folder...", "新建文件夹..."), null, (_, _) => CreateEntry(true, folder));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.T("Rename...", "重命名..."), null, (_, _) => Rename(n));
        menu.Items.Add(L.T("Delete", "删除"), null, (_, _) => Delete(n));
        menu.Items.Add(new ToolStripSeparator());
        if (!n.IsDir && HasProject() && DevProject.IsSource(n.Path) || (!n.IsDir && HasProject() && IsHeader(n.Path)))
        {
            bool member = InProject(n.Path);
            menu.Items.Add(member ? L.T("Remove from Project", "从项目移除") : L.T("Add to Project", "加入项目"), null,
                (_, _) => { if (ProjectMembershipChanged != null) ProjectMembershipChanged(n.Path, !member); list.Invalidate(); });
        }
        menu.Items.Add(L.T("Reveal in File Explorer", "在文件资源管理器中显示"), null, (_, _) => Process.Start("explorer.exe", "/select,\"" + n.Path + "\""));
        menu.Items.Add(L.T("Copy Path", "复制路径"), null, (_, _) => { try { Clipboard.SetText(n.Path); } catch (System.Runtime.InteropServices.ExternalException) { } });
        foreach (ToolStripItem item in menu.Items) { item.ForeColor = Theme.Text; item.Padding = Ui.Pad(4, 2, 4, 2); }
        menu.Show(list, p);
    }

    private static bool IsHeader(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext == ".h" || ext == ".hpp" || ext == ".hh";
    }

    private void Rename(Node n)
    {
        string name = Prompt.Show(FindForm(), L.T("Rename", "重命名"), L.T("New name", "新名称"), n.Name);
        if (string.IsNullOrWhiteSpace(name) || name == n.Name) return;
        string target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(n.Path), name.Trim());
        try
        {
            if (n.IsDir) Directory.Move(n.Path, target); else File.Move(n.Path, target);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
        {
            MessageBox.Show(FindForm(), ex.Message, "Gestirn dev", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (Renamed != null) Renamed(n.Path, target);
        Reload();
    }

    public event Action<string, string> Renamed;

    private void Delete(Node n)
    {
        var answer = MessageBox.Show(FindForm(), string.Format(L.T("Move \"{0}\" to the Recycle Bin?", "将“{0}”移到回收站？"), n.Name), "Gestirn dev",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;
        try
        {
            if (n.IsDir) Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(n.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            else Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(n.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is OperationCanceledException)
        {
            MessageBox.Show(FindForm(), ex.Message, "Gestirn dev", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!n.IsDir && HasProject() && InProject(n.Path) && ProjectMembershipChanged != null) ProjectMembershipChanged(n.Path, false);
        Reload();
    }

    private static Color FileColor(string name)
    {
        string ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
        switch (ext)
        {
            case ".cpp": case ".cc": case ".cxx": return Theme.Orange;
            case ".c": return Theme.Gold;
            case ".h": case ".hpp": case ".hh": return Theme.Violet;
            case ".dev": return Theme.Lime;
            case ".exe": case ".o": case ".a": case ".dll": return Theme.Faint;
            default: return Theme.Muted;
        }
    }

    private void DrawRow(Graphics g, int i, Rectangle r, bool hover, bool selected)
    {
        var n = visible[i];
        VirtualList.RowBackground(g, r, hover, selected, list.Focused);
        int x = r.X + Ui.D(14) + n.Depth * Ui.D(14);
        int cy = r.Y + r.Height / 2;
        if (n.IsDir)
        {
            Glyphs.Draw(g, n.Expanded ? "chevron-down" : "chevron-right", new Rectangle(x, cy - Ui.D(5), Ui.D(10), Ui.D(10)), Theme.Muted);
            Glyphs.Draw(g, "folder", new Rectangle(x + Ui.D(15), cy - Ui.D(6), Ui.D(12), Ui.D(12)), n.Expanded ? Theme.Gold : Ui.Alpha(Theme.Gold, 170));
        }
        else
        {
            Shapes.FillRound(g, FileColor(n.Name), new RectangleF(x + Ui.D(17), cy - Ui.D(4), Ui.D(8), Ui.D(8)), Ui.D(2));
        }
        bool member = !n.IsDir && HasProject() && InProject(n.Path);
        var color = n.IsDir ? Theme.Text : (FileColor(n.Name) == Theme.Faint ? Theme.Muted : Theme.Text);
        Shapes.Text(g, n.Name, Theme.Ui9, new Rectangle(x + Ui.D(32), r.Y, r.Right - x - Ui.D(48), r.Height), color, Shapes.Left);
        if (member) Glyphs.Draw(g, "dot", new Rectangle(r.Right - Ui.D(20), cy - Ui.D(3), Ui.D(6), Ui.D(6)), Theme.Lime);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { if (watcher != null) watcher.Dispose(); refresh.Dispose(); }
        base.Dispose(disposing);
    }
}

// ---------------------------------------------------------------------------------- outline

internal sealed class OutlinePanel : Panel
{
    public readonly PanelHeader Header = new PanelHeader(L.T("Outline", "大纲"));
    private readonly RoundInput filter = new RoundInput(L.T("Filter symbols", "筛选符号"));
    private readonly VirtualList list = new VirtualList();
    private List<SymbolInfo> all = new List<SymbolInfo>();
    private List<SymbolInfo> shown = new List<SymbolInfo>();
    public event Action<int> LineActivated;

    public OutlinePanel()
    {
        BackColor = Theme.Panel;
        list.EmptyText = L.T("No symbols in this file.", "当前文件没有符号。");
        list.DrawRow = DrawRow;
        list.RowClicked += (i, e) => { if (i < shown.Count && LineActivated != null) LineActivated(shown[i].Line); };
        list.RowActivated += i => { if (i < shown.Count && LineActivated != null) LineActivated(shown[i].Line); };
        filter.Box.TextChanged += (_, _) => Apply();
        Controls.Add(list);
        Controls.Add(filter);
        Controls.Add(Header);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Header.SetBounds(0, 0, Width, Header.Height);
        filter.SetBounds(Ui.D(12), Header.Height, Width - Ui.D(24), Ui.D(30));
        list.SetBounds(0, Header.Height + Ui.D(38), Width, Math.Max(0, Height - Header.Height - Ui.D(38)));
    }

    public void SetSymbols(List<SymbolInfo> symbols) { all = symbols ?? new List<SymbolInfo>(); Apply(); }

    private void Apply()
    {
        string f = filter.Text.Trim();
        shown = f.Length == 0 ? all : all.Where(s => s.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        list.Reset(shown.Count);
    }

    private void DrawRow(Graphics g, int i, Rectangle r, bool hover, bool selected)
    {
        var s = shown[i];
        VirtualList.RowBackground(g, r, hover, selected, list.Focused);
        int x = r.X + Ui.D(16) + Math.Min(4, s.Depth) * Ui.D(12);
        Color c; string letter;
        switch (s.Kind)
        {
            case SymbolKind.Function: c = Theme.SynFunction; letter = "f"; break;
            case SymbolKind.Class: c = Theme.SynType; letter = "C"; break;
            case SymbolKind.Struct: c = Theme.SynType; letter = "S"; break;
            case SymbolKind.Enum: c = Theme.Gold; letter = "E"; break;
            case SymbolKind.Namespace: c = Theme.Violet; letter = "N"; break;
            default: c = Theme.SynPreproc; letter = "#"; break;
        }
        var badge = new RectangleF(x, r.Y + (r.Height - Ui.D(16)) / 2f, Ui.D(16), Ui.D(16));
        Shapes.FillRound(g, Ui.Alpha(c, 46), badge, Ui.D(4));
        Shapes.Text(g, letter, Theme.UiSmall, Rectangle.Round(badge), c, Shapes.Center);
        int nameW = TextRenderer.MeasureText(s.Name, Theme.Ui9, Size.Empty, TextFormatFlags.NoPadding).Width;
        Shapes.Text(g, s.Name, Theme.Ui9, new Rectangle(x + Ui.D(24), r.Y, r.Right - x - Ui.D(30), r.Height), Theme.Text, Shapes.Left);
        if (!string.IsNullOrEmpty(s.Detail) && x + Ui.D(32) + nameW < r.Right - Ui.D(20))
            Shapes.Text(g, s.Detail, Theme.UiSmall, new Rectangle(x + Ui.D(30) + nameW, r.Y, r.Right - x - Ui.D(36) - nameW, r.Height), Theme.Muted, Shapes.Left);
    }
}

// ---------------------------------------------------------------------------------- search

internal sealed class SearchPanel : Panel
{
    private sealed class Row { public string File; public int Line, Col, Count; public string Text; public bool IsFile; }

    public readonly PanelHeader Header = new PanelHeader(L.T("Search", "搜索"));
    public readonly RoundInput Query = new RoundInput(L.T("Search in files (Enter)", "在文件中搜索（回车）"));
    private readonly PixelButton caseBtn = new PixelButton { Glyph = "case", Kind = ButtonKind.Ghost, Radius = 6 };
    private readonly PixelButton wordBtn = new PixelButton { Glyph = "word", Kind = ButtonKind.Ghost, Radius = 6 };
    private readonly VirtualList list = new VirtualList();
    private readonly List<Row> rows = new List<Row>();
    private readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer { Interval = 350 };
    private CancellationTokenSource cancel;
    private string term = "";
    public Func<string> RootFolder = () => null;
    public event Action<string, int, int> MatchActivated;

    public SearchPanel()
    {
        BackColor = Theme.Panel;
        list.RowHeight = Ui.D(26);
        list.EmptyText = L.T("Type to search all source files in the open folder.", "输入关键字，搜索已打开文件夹中的所有源文件。");
        list.DrawRow = DrawRow;
        list.RowClicked += (i, e) => Open(i);
        list.RowActivated += Open;
        var tip = new ToolTip();
        tip.SetToolTip(caseBtn, L.T("Match case", "区分大小写"));
        tip.SetToolTip(wordBtn, L.T("Whole word", "全字匹配"));
        caseBtn.Click += (_, _) => { caseBtn.Active = !caseBtn.Active; caseBtn.Invalidate(); Run(); };
        wordBtn.Click += (_, _) => { wordBtn.Active = !wordBtn.Active; wordBtn.Invalidate(); Run(); };
        Query.Box.TextChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
        Query.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; debounce.Stop(); Run(); } };
        debounce.Tick += (_, _) => { debounce.Stop(); Run(); };
        Controls.Add(list);
        Controls.Add(Query);
        Controls.Add(caseBtn);
        Controls.Add(wordBtn);
        Controls.Add(Header);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Header.SetBounds(0, 0, Width, Header.Height);
        int y = Header.Height;
        Query.SetBounds(Ui.D(12), y, Width - Ui.D(24) - Ui.D(60), Ui.D(30));
        caseBtn.SetBounds(Width - Ui.D(68), y + Ui.D(2), Ui.D(26), Ui.D(26));
        wordBtn.SetBounds(Width - Ui.D(40), y + Ui.D(2), Ui.D(26), Ui.D(26));
        list.SetBounds(0, y + Ui.D(40), Width, Math.Max(0, Height - y - Ui.D(40)));
    }

    public void Run()
    {
        term = Query.Text;
        if (cancel != null) cancel.Cancel();
        rows.Clear();
        list.Reset(0, false);
        string root = RootFolder();
        if (term.Length < 1 || root == null) return;
        var cts = cancel = new CancellationTokenSource();
        bool matchCase = caseBtn.Active, word = wordBtn.Active;
        string t = term;
        Header.Text = L.T("Searching...", "正在搜索...");
        Task.Run(() =>
        {
            var found = new List<Row>();
            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".c", ".cpp", ".cc", ".cxx", ".h", ".hpp", ".hh", ".txt", ".md", ".rc", ".dev", ".in" };
            var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int total = 0;
            foreach (var file in SafeFiles(root))
            {
                if (cts.IsCancellationRequested || total > 3000) break;
                if (!exts.Contains(Path.GetExtension(file))) continue;
                string[] lines;
                try { lines = File.ReadAllLines(file); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                var hits = new List<Row>();
                for (int l = 0; l < lines.Length; l++)
                {
                    int idx = 0;
                    while ((idx = lines[l].IndexOf(t, idx, cmp)) >= 0)
                    {
                        int end = idx + t.Length;
                        bool ok = !word || ((idx == 0 || !CppSyntax.IsIdentChar(lines[l][idx - 1])) && (end >= lines[l].Length || !CppSyntax.IsIdentChar(lines[l][end])));
                        if (ok) { hits.Add(new Row { File = file, Line = l, Col = idx, Text = lines[l] }); break; }
                        idx = end;
                    }
                }
                if (hits.Count == 0) continue;
                found.Add(new Row { File = file, IsFile = true, Count = hits.Count });
                found.AddRange(hits);
                total += hits.Count;
            }
            return Tuple.Create(found, total);
        }).ContinueWith(task =>
        {
            if (cts.IsCancellationRequested || IsDisposed) return;
            rows.AddRange(task.Result.Item1);
            int files = rows.Count(r => r.IsFile);
            Header.Text = L.T("Search", "搜索") + (task.Result.Item2 > 0 ? "  ·  " + string.Format(L.T("{0} results in {1} files", "{1} 个文件中 {0} 处结果"), task.Result.Item2, files) : "");
            list.EmptyText = L.T("No results.", "没有结果。");
            list.Reset(rows.Count, false);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static IEnumerable<string> SafeFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            string[] files = new string[0], dirs = new string[0];
            try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            foreach (var f in files) yield return f;
            foreach (var d in dirs)
            {
                string name = Path.GetFileName(d);
                if (name.StartsWith(".") || name == "node_modules") continue;
                stack.Push(d);
            }
        }
    }

    private void Open(int i)
    {
        if (i < 0 || i >= rows.Count) return;
        var r = rows[i];
        if (MatchActivated != null) MatchActivated(r.File, r.IsFile ? 0 : r.Line, r.IsFile ? 0 : r.Col);
    }

    private void DrawRow(Graphics g, int i, Rectangle r, bool hover, bool selected)
    {
        var row = rows[i];
        VirtualList.RowBackground(g, r, hover, selected, list.Focused);
        if (row.IsFile)
        {
            Glyphs.Draw(g, "file", new Rectangle(r.X + Ui.D(16), r.Y + (r.Height - Ui.D(11)) / 2, Ui.D(11), Ui.D(11)), Theme.Orange);
            string name = Path.GetFileName(row.File);
            int w = TextRenderer.MeasureText(name, Theme.UiSemi, Size.Empty, TextFormatFlags.NoPadding).Width;
            Shapes.Text(g, name, Theme.UiSemi, new Rectangle(r.X + Ui.D(34), r.Y, r.Width - Ui.D(80), r.Height), Theme.Text, Shapes.Left);
            string root = RootFolder() ?? "";
            string rel = Path.GetDirectoryName(row.File);
            if (rel.StartsWith(root, StringComparison.OrdinalIgnoreCase)) rel = rel.Substring(root.Length).TrimStart('\\');
            Shapes.Text(g, rel, Theme.UiSmall, new Rectangle(r.X + Ui.D(40) + w, r.Y, r.Width - w - Ui.D(90), r.Height), Theme.Muted, Shapes.Left);
            Shapes.Text(g, row.Count.ToString(), Theme.UiSmall, new Rectangle(r.X, r.Y, r.Width - Ui.D(14), r.Height), Theme.Gold, Shapes.Right);
            return;
        }
        string text = row.Text.TrimStart();
        int trimmed = row.Text.Length - text.Length;
        int col = Math.Max(0, row.Col - trimmed);
        int x = r.X + Ui.D(34);
        Shapes.Text(g, (row.Line + 1).ToString(), Theme.UiSmall, new Rectangle(r.X + Ui.D(6), r.Y, Ui.D(24), r.Height), Theme.Faint, Shapes.Right);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        string before = text.Substring(0, Math.Min(col, text.Length));
        if (before.Length > 24) before = "..." + before.Substring(before.Length - 21);
        string match = text.Substring(Math.Min(col, text.Length), Math.Min(term.Length, Math.Max(0, text.Length - col)));
        string after = text.Substring(Math.Min(text.Length, col + match.Length));
        int bw = TextRenderer.MeasureText(g, before, Theme.Ui9, Size.Empty, flags).Width;
        int mw = TextRenderer.MeasureText(g, match, Theme.Ui9, Size.Empty, flags).Width;
        TextRenderer.DrawText(g, before, Theme.Ui9, new Rectangle(x, r.Y, r.Right - x, r.Height), Theme.TextDim, flags);
        Shapes.FillRound(g, Theme.FindMatch, new RectangleF(x + bw - Ui.D(1), r.Y + Ui.D(4), mw + Ui.D(2), r.Height - Ui.D(8)), Ui.D(3));
        TextRenderer.DrawText(g, match, Theme.Ui9, new Rectangle(x + bw, r.Y, r.Right - x - bw, r.Height), Theme.Text, flags);
        TextRenderer.DrawText(g, after, Theme.Ui9, new Rectangle(x + bw + mw, r.Y, Math.Max(0, r.Right - x - bw - mw - Ui.D(8)), r.Height), Theme.TextDim, flags | TextFormatFlags.EndEllipsis);
    }
}

// ---------------------------------------------------------------------------------- debug

internal sealed class DebugPanel : Panel
{
    private sealed class Row { public int Kind; public string Name, Value, Detail; public object Data; }
    private const int Header = 0, Var = 1, Watch = 2, Frame = 3, Bp = 4, Hint = 5;

    public readonly PanelHeader HeaderBar = new PanelHeader(L.T("Run and Debug", "运行和调试"));
    private readonly RoundInput watchInput = new RoundInput(L.T("Add watch expression (Enter)", "添加监视表达式（回车）"));
    private readonly VirtualList list = new VirtualList();
    private readonly List<Row> rows = new List<Row>();
    public List<GdbVar> Locals = new List<GdbVar>();
    public List<KeyValuePair<string, string>> Watches = new List<KeyValuePair<string, string>>();
    public List<GdbFrame> Frames = new List<GdbFrame>();
    public List<KeyValuePair<string, int>> Breakpoints = new List<KeyValuePair<string, int>>();
    public bool Debugging;

    public event Action<string> WatchAdded, WatchRemoved;
    public event Action<GdbFrame> FrameActivated;
    public event Action<string, int> BreakpointActivated;

    public DebugPanel()
    {
        BackColor = Theme.Panel;
        list.RowHeight = Ui.D(26);
        list.DrawRow = DrawRow;
        list.IsSelectable = i => i < rows.Count && rows[i].Kind != Header && rows[i].Kind != Hint;
        list.RowClicked += (i, e) => Activate(i);
        list.RowRightClicked += (i, p) =>
        {
            if (i < rows.Count && rows[i].Kind == Watch && WatchRemoved != null) WatchRemoved(rows[i].Name);
        };
        watchInput.Box.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            string expr = watchInput.Text.Trim();
            if (expr.Length > 0 && WatchAdded != null) WatchAdded(expr);
            watchInput.Text = "";
        };
        Controls.Add(list);
        Controls.Add(watchInput);
        Controls.Add(HeaderBar);
        Rebuild();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        HeaderBar.SetBounds(0, 0, Width, HeaderBar.Height);
        watchInput.SetBounds(Ui.D(12), HeaderBar.Height, Width - Ui.D(24), Ui.D(30));
        list.SetBounds(0, HeaderBar.Height + Ui.D(38), Width, Math.Max(0, Height - HeaderBar.Height - Ui.D(38)));
    }

    public void Rebuild()
    {
        rows.Clear();
        if (!Debugging)
        {
            rows.Add(new Row { Kind = Hint, Name = L.T("Press F5 to debug. Click the gutter left of a line number to set a breakpoint.", "按 F5 开始调试。点击行号左侧的边栏可设置断点。") });
        }
        else
        {
            rows.Add(new Row { Kind = Header, Name = L.T("VARIABLES", "变量") });
            if (Locals.Count == 0) rows.Add(new Row { Kind = Hint, Name = L.T("No local variables", "没有局部变量") });
            foreach (var v in Locals) rows.Add(new Row { Kind = Var, Name = v.Name, Value = v.Value, Detail = v.Type });
            rows.Add(new Row { Kind = Header, Name = L.T("CALL STACK", "调用堆栈") });
            foreach (var f in Frames) rows.Add(new Row { Kind = Frame, Name = f.Func, Value = Path.GetFileName(f.File) + ":" + (f.Line + 1), Data = f });
        }
        rows.Add(new Row { Kind = Header, Name = L.T("WATCH", "监视") });
        if (Watches.Count == 0) rows.Add(new Row { Kind = Hint, Name = L.T("Type an expression above. Right-click a watch to remove it.", "在上方输入表达式。右键单击可移除监视。") });
        foreach (var w in Watches) rows.Add(new Row { Kind = Watch, Name = w.Key, Value = Debugging ? w.Value : "" });
        rows.Add(new Row { Kind = Header, Name = L.T("BREAKPOINTS", "断点") });
        foreach (var b in Breakpoints) rows.Add(new Row { Kind = Bp, Name = Path.GetFileName(b.Key), Value = L.T("line ", "第 ") + (b.Value + 1) + L.T("", " 行"), Data = b });
        list.Reset(rows.Count);
    }

    private void Activate(int i)
    {
        if (i < 0 || i >= rows.Count) return;
        var r = rows[i];
        if (r.Kind == Frame && FrameActivated != null) FrameActivated((GdbFrame)r.Data);
        if (r.Kind == Bp && BreakpointActivated != null) { var b = (KeyValuePair<string, int>)r.Data; BreakpointActivated(b.Key, b.Value); }
    }

    private void DrawRow(Graphics g, int i, Rectangle r, bool hover, bool selected)
    {
        var row = rows[i];
        if (row.Kind == Header)
        {
            Shapes.Text(g, row.Name, Theme.UiSmall, new Rectangle(r.X + Ui.D(16), r.Y + Ui.D(4), r.Width, r.Height), Theme.Muted, Shapes.Left);
            return;
        }
        if (row.Kind == Hint)
        {
            TextRenderer.DrawText(g, row.Name, Theme.UiSmall, new Rectangle(r.X + Ui.D(16), r.Y, r.Width - Ui.D(24), r.Height), Theme.Faint,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            return;
        }
        VirtualList.RowBackground(g, r, hover, selected, list.Focused);
        int x = r.X + Ui.D(18);
        string glyph = row.Kind == Frame ? "arrow" : row.Kind == Bp ? "breakpoint" : row.Kind == Watch ? "search" : null;
        if (glyph != null)
        {
            Glyphs.Draw(g, glyph, new Rectangle(x, r.Y + (r.Height - Ui.D(9)) / 2, Ui.D(9), Ui.D(9)), row.Kind == Bp ? Theme.Red : Theme.Orange);
            x += Ui.D(16);
        }
        int nameW = Math.Min(r.Width / 2, TextRenderer.MeasureText(row.Name, Theme.Code, Size.Empty, TextFormatFlags.NoPadding).Width);
        Shapes.Text(g, row.Name, Theme.Code, new Rectangle(x, r.Y, nameW + Ui.D(4), r.Height), row.Kind == Frame ? Theme.SynFunction : Theme.SynPlain, Shapes.Left);
        if (!string.IsNullOrEmpty(row.Value))
        {
            string sep = row.Kind == Var || row.Kind == Watch ? " = " : "  ";
            Shapes.Text(g, sep + row.Value.Replace("\n", " "), Theme.Code, new Rectangle(x + nameW, r.Y, r.Right - x - nameW - Ui.D(10), r.Height),
                row.Kind == Var || row.Kind == Watch ? Theme.SynNumber : Theme.Muted, Shapes.Left);
        }
    }
}

// ---------------------------------------------------------------------------------- bottom views

internal sealed class ProblemsView : Panel
{
    private readonly VirtualList list = new VirtualList();
    private List<Diagnostic> items = new List<Diagnostic>();
    public event Action<Diagnostic> Activated;

    public ProblemsView()
    {
        BackColor = Theme.Panel;
        list.RowHeight = Ui.D(46);
        list.EmptyText = L.T("No problems. Build (F9) to check your code.", "没有问题。按 F9 编译以检查代码。");
        list.DrawRow = DrawRow;
        list.Dock = DockStyle.Fill;
        list.RowClicked += (i, e) => { if (i < items.Count && Activated != null) Activated(items[i]); };
        list.RowActivated += i => { if (i < items.Count && Activated != null) Activated(items[i]); };
        Controls.Add(list);
    }

    public void Set(IEnumerable<Diagnostic> diagnostics)
    {
        items = diagnostics.Where(d => d.Severity != Severity.Note).OrderBy(d => d.Severity).ThenBy(d => d.File).ThenBy(d => d.Line).ToList();
        list.Reset(items.Count, false);
    }

    private void DrawRow(Graphics g, int i, Rectangle r, bool hover, bool selected)
    {
        var d = items[i];
        VirtualList.RowBackground(g, r, hover, selected, list.Focused);
        var color = d.Severity == Severity.Error ? Theme.Red : Theme.Gold;
        Glyphs.Draw(g, d.Severity == Severity.Error ? "error" : "warning", new Rectangle(r.X + Ui.D(18), r.Y + Ui.D(8), Ui.D(10), Ui.D(10)), color);
        int x = r.X + Ui.D(38);
        string where = d.File != null ? Path.GetFileName(d.File) + (d.Line >= 0 ? ":" + (d.Line + 1) + (d.Col > 0 ? ":" + d.Col : "") : "") : "";
        int ww = TextRenderer.MeasureText(where, Theme.UiSmall, Size.Empty, TextFormatFlags.NoPadding).Width;
        Shapes.Text(g, d.Headline, Theme.Ui9, new Rectangle(x, r.Y + Ui.D(3), r.Right - x - ww - Ui.D(24), Ui.D(20)), Theme.Text, Shapes.Left);
        Shapes.Text(g, where, Theme.UiSmall, new Rectangle(r.X, r.Y + Ui.D(3), r.Width - Ui.D(14), Ui.D(20)), Theme.Muted, Shapes.Right);
        if (Hints.SimpleMode && !string.IsNullOrEmpty(d.Plain) && string.IsNullOrEmpty(d.Hint))
            Shapes.Text(g, d.Message, Theme.UiSmall, new Rectangle(x, r.Y + Ui.D(22), r.Right - x - Ui.D(24), Ui.D(18)), Theme.Muted, Shapes.Left);
        if (!string.IsNullOrEmpty(d.Hint))
        {
            Glyphs.Draw(g, "info", new Rectangle(x, r.Y + Ui.D(27), Ui.D(8), Ui.D(8)), Theme.Lime);
            Shapes.Text(g, d.Hint, Theme.UiSmall, new Rectangle(x + Ui.D(14), r.Y + Ui.D(22), r.Right - x - Ui.D(24), Ui.D(18)), Ui.Alpha(Theme.Lime, 220), Shapes.Left);
        }
    }
}

// Performance analysis results (gprof flat profile) with bars that grow in, rank by rank.
internal sealed class ProfileView : Panel
{
    private readonly VirtualList list = new VirtualList();
    private readonly AnimatedFloat grow = new AnimatedFloat(1f), shine = new AnimatedFloat(1f);
    private ProfileReport report;
    private List<ProfileEntry> rows = new List<ProfileEntry>();
    private double maxValue = 1;
    public event Action<ProfileEntry> FunctionActivated;
    public bool Waiting;
    public string WaitingText;

    public ProfileView()
    {
        BackColor = Theme.Panel;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        list.RowHeight = Ui.D(34);
        list.EmptyText = L.T("No profile yet. Build → Profile runs your program; when it exits, the time spent in each function shows here.",
                             "还没有性能数据。点 编译 → 性能分析 运行程序，程序结束后这里会列出每个函数的耗时。");
        list.DrawRow = DrawRow;
        list.RowActivated += i => { if (i < rows.Count && FunctionActivated != null) FunctionActivated(rows[i]); };
        list.RowClicked += (i, e) => { if (e.Clicks > 1 && i < rows.Count && FunctionActivated != null) FunctionActivated(rows[i]); };
        grow.Changed += () => { list.Invalidate(); Invalidate(); };
        shine.Changed += list.Invalidate;
        Controls.Add(list);
        Resize += (_, _) => LayoutList();
        Animator.AddTicker(Tick);
    }

    public ProfileReport Report { get { return report; } }
    public int Count { get { return rows.Count; } }

    private int HeaderHeight { get { return Ui.D(46); } }

    private void LayoutList() { list.SetBounds(0, HeaderHeight, Width, Math.Max(10, Height - HeaderHeight)); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Animator.RemoveTicker(Tick);
        base.Dispose(disposing);
    }

    private void Tick(double now) { if (Waiting && Visible) Invalidate(new Rectangle(0, 0, Width, HeaderHeight)); }

    public void Clear()
    {
        report = null;
        rows = new List<ProfileEntry>();
        list.Reset(0, false);
        Invalidate();
    }

    public void Set(ProfileReport r)
    {
        report = r;
        Waiting = false;
        rows = r.Entries.Where(e => r.HasSamples ? e.Self > 0 || e.Calls > 0 : e.Calls > 0).ToList();
        maxValue = rows.Count == 0 ? 1 : r.HasSamples ? Math.Max(0.0001, rows.Max(e => e.Self)) : Math.Max(1, rows.Max(e => e.Calls));
        LayoutList();
        list.Reset(rows.Count, false);
        grow.Snap(0f);
        grow.To(1f, 1100, Ease.OutCubic, 80);
        shine.Snap(0f);
        shine.To(1f, 900, Ease.InOutCubic, 900);
        Invalidate();
    }

    private float RowProgress(int i)
    {
        float t = grow.Value * (1f + Math.Min(12, rows.Count) * 0.07f) - Math.Min(i, 12) * 0.07f;
        return Ease.OutBack(Math.Max(0f, Math.Min(1f, t)));
    }

    private static string Seconds(double s) { return s >= 1 ? s.ToString("0.00") + " s" : (s * 1000).ToString("0") + " ms"; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int x = Ui.D(14);
        var box = new Rectangle(x, Ui.D(9), Ui.D(28), Ui.D(28));
        Shapes.FillRound(g, Ui.Alpha(Theme.Orange, 40), box, Ui.D(8));
        if (Waiting) Glyphs.Spinner(g, Rectangle.Inflate(box, -Ui.D(8), -Ui.D(8)), (Animator.Now / 900.0) % 1.0, Theme.OrangeHot, Theme.Orange);
        else Glyphs.Draw(g, "profile", Rectangle.Inflate(box, -Ui.D(8), -Ui.D(8)), Theme.Orange);
        x += Ui.D(38);
        string title, sub;
        if (Waiting)
        {
            title = WaitingText ?? L.T("Profiling…", "正在分析…");
            sub = L.T("Use the program as usual; results appear when it exits.", "像平常一样使用程序，程序退出后自动显示结果。");
        }
        else if (report == null)
        {
            title = L.T("Performance analysis", "性能分析");
            sub = L.T("Finds the functions where your program spends its time (gprof).", "找出程序把时间花在了哪些函数上（gprof）。");
        }
        else
        {
            title = Path.GetFileName(report.Exe) + "  ·  " + (report.HasSamples ? L.T("total ", "总计 ") + Seconds(report.TotalSeconds) : L.T("ran too briefly to sample time", "运行时间过短，未采样到耗时"));
            sub = report.HasSamples
                ? string.Format(L.T("{0} functions · sampled every {1} ms · double-click a row to jump to the code", "{0} 个函数 · 每 {1} ms 采样一次 · 双击行跳转到代码"), rows.Count, (report.SampleSeconds * 1000).ToString("0.#"))
                : L.T("Ranked by number of calls instead · profile longer work to get timings", "改为按调用次数排序 · 运行更耗时的任务可得到耗时数据");
        }
        int textRight = Width - Ui.D(12);
        if (report != null && rows.Count > 0) textRight = Width - Ui.D(270);
        Shapes.Text(g, title, Theme.UiSemi, new Rectangle(x, Ui.D(5), textRight - x, Ui.D(20)), Theme.Text, Shapes.Left);
        Shapes.Text(g, sub, Theme.UiSmall, new Rectangle(x, Ui.D(24), textRight - x, Ui.D(18)), Theme.Muted, Shapes.Left);
        if (report != null && rows.Count > 0)
        {
            int right = Width - Ui.D(18);
            var hdr = Theme.UiSmall;
            Shapes.Text(g, L.T("calls", "调用次数"), hdr, new Rectangle(right - Ui.D(250), Ui.D(26), Ui.D(110), Ui.D(18)), Theme.Faint, Shapes.Right);
            Shapes.Text(g, L.T("self", "自身耗时"), hdr, new Rectangle(right - Ui.D(136), Ui.D(26), Ui.D(70), Ui.D(18)), Theme.Faint, Shapes.Right);
            Shapes.Text(g, "%", hdr, new Rectangle(right - Ui.D(60), Ui.D(26), Ui.D(56), Ui.D(18)), Theme.Faint, Shapes.Right);
        }
        using (var pen = new Pen(Theme.EdgeSoft)) g.DrawLine(pen, Ui.D(10), HeaderHeight - 1, Width - Ui.D(10), HeaderHeight - 1);
    }

    private void DrawRow(Graphics g, int i, Rectangle r, bool hover, bool selected)
    {
        var e = rows[i];
        VirtualList.RowBackground(g, r, hover, selected, list.Focused);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float p = RowProgress(i);
        Color accent = i == 0 ? Theme.OrangeHot : i == 1 ? Theme.Orange : i == 2 ? Theme.Gold : Theme.Blue;
        var badge = new Rectangle(r.X + Ui.D(14), r.Y + Ui.D(8), Ui.D(22), Ui.D(18));
        Shapes.FillRound(g, Ui.Alpha(accent, i < 3 ? 60 : 30), badge, Ui.D(6));
        Shapes.Text(g, (i + 1).ToString(), Theme.UiSmall, badge, i < 3 ? accent : Theme.TextDim, Shapes.Center);
        int right = r.Right - Ui.D(18);
        int nameX = badge.Right + Ui.D(10);
        int barRight = right - Ui.D(262);
        int nameW = Math.Max(Ui.D(80), Math.Min(Ui.D(320), (barRight - nameX) / 2));
        Shapes.Text(g, e.Name, Theme.CodeSmall, new Rectangle(nameX, r.Y, nameW, r.Height), Theme.Text, Shapes.Left);
        int bx = nameX + nameW + Ui.D(10), bw = Math.Max(Ui.D(30), barRight - bx);
        var track = new RectangleF(bx, r.Y + r.Height / 2 - Ui.D(5), bw, Ui.D(10));
        Shapes.FillRound(g, Ui.Alpha(Theme.Edge, 90), track, Ui.D(5));
        double value = report.HasSamples ? e.Self : Math.Max(0, e.Calls);
        float frac = (float)(value / maxValue) * p;
        if (frac > 0.002f)
        {
            var fill = new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * Math.Min(1.04f, frac)), track.Height);
            using (var path = Shapes.Round(fill, track.Height / 2))
            using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(fill.X - 1, fill.Y, fill.Width + 2, fill.Height), Ui.Lerp(accent, Theme.Gold, 0.35f), accent, 0f))
            {
                g.FillPath(brush, path);
                if (!Animator.Reduced && shine.Value > 0f && shine.Value < 1f)
                {
                    float sx = fill.X + (fill.Width + Ui.D(60)) * shine.Value - Ui.D(30);
                    var old = g.Clip;
                    g.SetClip(path, System.Drawing.Drawing2D.CombineMode.Intersect);
                    using (var glow = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(sx - Ui.D(30), fill.Y, Ui.D(60), fill.Height), Color.White, Color.White, 0f))
                    {
                        var blend = new System.Drawing.Drawing2D.ColorBlend(3);
                        blend.Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) };
                        blend.Positions = new[] { 0f, 0.5f, 1f };
                        glow.InterpolationColors = blend;
                        g.FillRectangle(glow, sx - Ui.D(30), fill.Y, Ui.D(60), fill.Height);
                    }
                    g.Clip = old;
                }
            }
            if (!Animator.Reduced && p > 0.05f && p < 0.98f)
                using (var b = new SolidBrush(Ui.Alpha(Theme.Gold, (int)(255 * (1 - p)))))
                    g.FillRectangle(b, fill.Right - Ui.D(2), fill.Y - Ui.D(3), Ui.D(3), Ui.D(3));
        }
        string calls = e.Calls >= 0 ? e.Calls.ToString("N0") + (e.Recursive ? " ↻" : "") : "—";
        Shapes.Text(g, calls, Theme.UiSmall, new Rectangle(right - Ui.D(250), r.Y, Ui.D(110), r.Height), Theme.TextDim, Shapes.Right);
        Shapes.Text(g, report.HasSamples ? Seconds(e.Self) : "—", Theme.UiSmall, new Rectangle(right - Ui.D(136), r.Y, Ui.D(70), r.Height), Theme.TextDim, Shapes.Right);
        string pct = report.HasSamples ? (e.Percent * Math.Min(1f, p)).ToString("0.0") + "%" : "";
        Shapes.Text(g, pct, Theme.Ui9, new Rectangle(right - Ui.D(60), r.Y, Ui.D(56), r.Height), i < 3 ? accent : Theme.Text, Shapes.Right);
    }
}

internal sealed class OutputView : Panel
{
    public readonly RichTextBox Box = new RichTextBox();
    public event Action<string> LineDoubleClicked;

    public OutputView()
    {
        BackColor = Theme.Panel;
        Padding = Ui.Pad(14, 6, 6, 6);
        Box.Dock = DockStyle.Fill;
        Box.ReadOnly = true;
        Box.BorderStyle = BorderStyle.None;
        Box.BackColor = Theme.Panel;
        Box.ForeColor = Theme.TextDim;
        Box.Font = Theme.CodeSmall;
        Box.WordWrap = false;
        Box.DetectUrls = false;
        Native.DarkScrollbars(Box);
        Box.MouseDoubleClick += (_, e) =>
        {
            int line = Box.GetLineFromCharIndex(Box.GetCharIndexFromPosition(e.Location));
            var lines = Box.Lines;
            if (line >= 0 && line < lines.Length && LineDoubleClicked != null) LineDoubleClicked(lines[line]);
        };
        Controls.Add(Box);
    }

    public void Append(string text, Color color)
    {
        if (text == null) return;
        Box.SelectionStart = Box.TextLength;
        Box.SelectionLength = 0;
        Box.SelectionColor = color;
        Box.SelectedText = text.Replace("\r\n", "\n") + "\n";
        Box.ScrollToCaret();
    }

    public void AppendLog(string output)
    {
        foreach (var raw in output.Replace("\r", "").Split('\n'))
        {
            if (raw.Length == 0) continue;
            Color c = Theme.TextDim;
            if (raw.Contains(": error:") || raw.Contains("fatal error:") || raw.Contains("undefined reference")) c = Theme.Red;
            else if (raw.Contains(": warning:")) c = Theme.Gold;
            else if (raw.Contains(": note:") || raw.Contains("In function")) c = Theme.Muted;
            Append(raw, c);
        }
    }

    public void Clear() { Box.Clear(); }
}

}
