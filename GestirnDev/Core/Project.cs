using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GestirnDev
{
internal sealed class ProjectUnit
{
    public string FileName = "";
    public bool Compile = true, Link = true;
    public string Folder = "";
}

// A Dev-C++ .dev project. Unknown keys are kept, so Dev-C++ can still open the file afterwards.
internal sealed class DevProject
{
    public const int TypeGui = 0, TypeConsole = 1, TypeStaticLib = 2, TypeDll = 3;

    private readonly List<KeyValuePair<string, List<KeyValuePair<string, string>>>> sections = new List<KeyValuePair<string, List<KeyValuePair<string, string>>>>();

    public string FilePath;
    public string Name = "Project";
    public int Type = TypeConsole;
    public bool IsCpp = true;
    public string CompilerFlags = "", CppFlags = "", LinkerFlags = "", IncludesRaw = "", LibsRaw = "";
    public string ObjectOutput = "", ExeOutput = "", OverrideOutputName = "";
    public bool OverrideOutput;
    public readonly List<ProjectUnit> Units = new List<ProjectUnit>();

    public string Directory { get { return Path.GetDirectoryName(FilePath); } }
    public string Includes { get { return IncludesRaw; } }

    public string ObjectDir
    {
        get { return string.IsNullOrEmpty(ObjectOutput) ? Directory : Path.GetFullPath(Path.Combine(Directory, ObjectOutput)); }
    }

    public string OutputPath
    {
        get
        {
            string dir = string.IsNullOrEmpty(ExeOutput) ? Directory : Path.GetFullPath(Path.Combine(Directory, ExeOutput));
            if (OverrideOutput && !string.IsNullOrEmpty(OverrideOutputName)) return Path.Combine(dir, OverrideOutputName);
            switch (Type)
            {
                case TypeStaticLib: return Path.Combine(dir, "lib" + Name + ".a");
                case TypeDll: return Path.Combine(dir, Name + ".dll");
                default: return Path.Combine(dir, Name + ".exe");
            }
        }
    }

    public string IncludeFlags
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var inc in IncludesRaw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) sb.Append("-I\"").Append(inc.Trim()).Append("\" ");
            return sb.ToString();
        }
    }

    public string Libs
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var lib in LibsRaw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) sb.Append("-L\"").Append(lib.Trim()).Append("\" ");
            return sb.ToString();
        }
    }

    public string FullPath(string relative) { return Path.GetFullPath(Path.Combine(Directory, relative)); }

    public string Relative(string full)
    {
        var baseUri = new Uri(Directory.TrimEnd('\\') + "\\");
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(new Uri(full)).ToString()).Replace('/', '\\');
    }

    public bool Contains(string fullPath)
    {
        return Units.Any(u => string.Equals(FullPath(u.FileName), fullPath, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsSource(string file)
    {
        string ext = Path.GetExtension(file).ToLowerInvariant();
        return ext == ".c" || ext == ".cpp" || ext == ".cc" || ext == ".cxx" || ext == ".c++" || ext == ".rc";
    }

    private static string Flags(string raw) { return (raw ?? "").Replace("_@@_", " ").Trim(); }
    private static string Unflags(string flags)
    {
        var parts = (flags ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "" : string.Join("_@@_", parts) + "_@@_";
    }

    private string Get(string section, string key, string fallback = "")
    {
        var s = sections.FirstOrDefault(x => string.Equals(x.Key, section, StringComparison.OrdinalIgnoreCase));
        if (s.Value == null) return fallback;
        var kv = s.Value.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        return kv.Key == null ? fallback : kv.Value;
    }

    private void Set(string section, string key, string value)
    {
        var s = sections.FirstOrDefault(x => string.Equals(x.Key, section, StringComparison.OrdinalIgnoreCase));
        if (s.Value == null)
        {
            s = new KeyValuePair<string, List<KeyValuePair<string, string>>>(section, new List<KeyValuePair<string, string>>());
            sections.Add(s);
        }
        int i = s.Value.FindIndex(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        var kv = new KeyValuePair<string, string>(key, value);
        if (i >= 0) s.Value[i] = kv; else s.Value.Add(kv);
    }

    public static DevProject Load(string path)
    {
        var p = new DevProject { FilePath = Path.GetFullPath(path) };
        List<KeyValuePair<string, string>> current = null;
        foreach (var raw in File.ReadAllLines(path, Encoding.Default))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(";")) continue;
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                current = new List<KeyValuePair<string, string>>();
                p.sections.Add(new KeyValuePair<string, List<KeyValuePair<string, string>>>(line.Substring(1, line.Length - 2), current));
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0 || current == null) continue;
            current.Add(new KeyValuePair<string, string>(line.Substring(0, eq), line.Substring(eq + 1)));
        }
        p.Name = p.Get("Project", "Name", Path.GetFileNameWithoutExtension(path));
        int type;
        if (int.TryParse(p.Get("Project", "Type", "1"), out type)) p.Type = type;
        p.IsCpp = p.Get("Project", "IsCpp", "1") != "0";
        p.CompilerFlags = Flags(p.Get("Project", "Compiler"));
        p.CppFlags = Flags(p.Get("Project", "CppCompiler"));
        p.LinkerFlags = Flags(p.Get("Project", "Linker"));
        p.IncludesRaw = p.Get("Project", "Includes");
        p.LibsRaw = p.Get("Project", "Libs");
        p.ObjectOutput = p.Get("Project", "ObjectOutput");
        p.ExeOutput = p.Get("Project", "ExeOutput");
        p.OverrideOutput = p.Get("Project", "OverrideOutput", "0") == "1";
        p.OverrideOutputName = p.Get("Project", "OverrideOutputName");
        int count;
        int.TryParse(p.Get("Project", "UnitCount", "0"), out count);
        for (int i = 1; i <= count; i++)
        {
            string section = "Unit" + i;
            string file = p.Get(section, "FileName");
            if (file.Length == 0) continue;
            p.Units.Add(new ProjectUnit
            {
                FileName = file,
                Compile = p.Get(section, "Compile", "1") != "0",
                Link = p.Get(section, "Link", "1") != "0",
                Folder = p.Get(section, "Folder"),
            });
        }
        return p;
    }

    public void Save()
    {
        Set("Project", "FileName", Path.GetFileName(FilePath));
        Set("Project", "Name", Name);
        Set("Project", "Type", Type.ToString());
        Set("Project", "Ver", "2");
        Set("Project", "IsCpp", IsCpp ? "1" : "0");
        Set("Project", "Compiler", Unflags(CompilerFlags));
        Set("Project", "CppCompiler", Unflags(CppFlags));
        Set("Project", "Linker", Unflags(LinkerFlags));
        Set("Project", "Includes", IncludesRaw);
        Set("Project", "Libs", LibsRaw);
        Set("Project", "ObjectOutput", ObjectOutput);
        Set("Project", "ExeOutput", ExeOutput);
        Set("Project", "OverrideOutput", OverrideOutput ? "1" : "0");
        Set("Project", "OverrideOutputName", OverrideOutputName);
        Set("Project", "UnitCount", Units.Count.ToString());
        sections.RemoveAll(s => Regex.IsMatch(s.Key, "^Unit\\d+$", RegexOptions.IgnoreCase));
        for (int i = 0; i < Units.Count; i++)
        {
            var u = Units[i];
            string section = "Unit" + (i + 1);
            Set(section, "FileName", u.FileName);
            Set(section, "CompileCpp", IsCpp && !u.FileName.EndsWith(".c", StringComparison.OrdinalIgnoreCase) ? "1" : "0");
            Set(section, "Folder", u.Folder);
            Set(section, "Compile", u.Compile ? "1" : "0");
            Set(section, "Link", u.Link ? "1" : "0");
            Set(section, "Priority", "1000");
            Set(section, "OverrideBuildCmd", "0");
            Set(section, "BuildCmd", "");
        }
        var sb = new StringBuilder();
        foreach (var s in sections)
        {
            sb.Append('[').Append(s.Key).Append("]\r\n");
            foreach (var kv in s.Value) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
            sb.Append("\r\n");
        }
        File.WriteAllText(FilePath, sb.ToString(), Encoding.Default);
    }

    public void AddUnit(string fullPath)
    {
        if (Contains(fullPath)) return;
        Units.Add(new ProjectUnit { FileName = Relative(fullPath) });
    }

    public static DevProject Create(string folder, string name, int type, bool cpp)
    {
        System.IO.Directory.CreateDirectory(folder);
        var p = new DevProject { FilePath = Path.Combine(folder, name + ".dev"), Name = name, Type = type, IsCpp = cpp };
        if (type == TypeGui) p.LinkerFlags = "-mwindows";
        return p;
    }
}

internal sealed class TemplateUnit { public string CName, CppName, CFile, CppFile; }

internal sealed class ProjectTemplate
{
    public string Name, Description, Category, Glyph;
    public int Type = DevProject.TypeConsole;
    public string Directory;
    public readonly List<TemplateUnit> Units = new List<TemplateUnit>();
    public string CompilerFlags = "", CppFlags = "", LinkerFlags = "";
}

internal static class Templates
{
    private static readonly Dictionary<string, string> ZhNames = new Dictionary<string, string>
    {
        { "Console Application", "控制台程序" }, { "Windows Application", "Windows 窗口程序" }, { "Static Library", "静态库" },
        { "DLL", "动态链接库 DLL" }, { "Empty Project", "空项目" }, { "Hello World", "Hello World" },
        { "Hello World with input", "带输入的 Hello World" }, { "File Editor", "文件编辑器" }, { "MDI Editor", "多文档编辑器" },
        { "OpenGL", "OpenGL 程序" }, { "OpenMP", "OpenMP 并行程序" }, { "std::thread", "std::thread 多线程" },
        { "Jackpot", "猜数字游戏" }, { "Direct3D", "Direct3D 程序" }, { "Windows Animation", "Windows 动画" },
    };

    public static List<ProjectTemplate> Load()
    {
        var list = new List<ProjectTemplate>();
        string dir = Toolchain.TemplatesDir();
        if (dir != null)
        {
            foreach (var file in System.IO.Directory.GetFiles(dir, "*.template").OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            {
                try { list.Add(Parse(file)); }
                catch (IOException) { }
            }
        }
        if (list.Count == 0) list.AddRange(BuiltIn());
        return list;
    }

    private static ProjectTemplate Parse(string file)
    {
        var t = new ProjectTemplate { Directory = Path.GetDirectoryName(file) };
        string section = "";
        var units = new SortedDictionary<int, TemplateUnit>();
        foreach (var raw in File.ReadAllLines(file, Encoding.Default))
        {
            string line = raw.Trim();
            if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line.Substring(0, eq), v = line.Substring(eq + 1);
            if (section == "Template")
            {
                if (key == "Name") t.Name = v;
                else if (key == "Description") t.Description = v;
                else if (key == "Category") t.Category = v;
            }
            else if (section == "Project")
            {
                int type;
                if (key == "Type" && int.TryParse(v, out type)) t.Type = type;
                else if (key == "Compiler") t.CompilerFlags = v.Replace("_@@_", " ").Trim();
                else if (key == "CppCompiler") t.CppFlags = v.Replace("_@@_", " ").Trim();
                else if (key == "Linker") t.LinkerFlags = v.Replace("_@@_", " ").Trim();
            }
            else if (section.StartsWith("Unit"))
            {
                int n;
                if (!int.TryParse(section.Substring(4), out n)) continue;
                TemplateUnit u;
                if (!units.TryGetValue(n, out u)) units[n] = u = new TemplateUnit();
                if (key == "CName") u.CName = v;
                else if (key == "CppName") u.CppName = v;
                else if (key == "C") u.CFile = v;
                else if (key == "Cpp") u.CppFile = v;
            }
        }
        t.Units.AddRange(units.Values);
        if (string.IsNullOrEmpty(t.Name)) t.Name = Path.GetFileNameWithoutExtension(file);
        string zh;
        if (L.Zh && ZhNames.TryGetValue(t.Name, out zh)) t.Name = zh;
        t.Glyph = t.Type == DevProject.TypeGui ? "palette" : t.Type == DevProject.TypeStaticLib || t.Type == DevProject.TypeDll ? "project" : t.Units.Count == 0 ? "file" : "term";
        return t;
    }

    private static IEnumerable<ProjectTemplate> BuiltIn()
    {
        var hello = new ProjectTemplate { Name = "Hello World", Description = L.T("A classic Hello World program", "经典的 Hello World 程序"), Glyph = "term" };
        hello.Units.Add(new TemplateUnit { CName = "main.c", CppName = "main.cpp" });
        yield return hello;
        yield return new ProjectTemplate { Name = L.T("Empty Project", "空项目"), Description = L.T("A project without files", "不含文件的项目"), Glyph = "file" };
    }

    public static string UnitText(ProjectTemplate t, TemplateUnit u, bool cpp)
    {
        string file = cpp ? u.CppFile : u.CFile;
        if (!string.IsNullOrEmpty(file) && t.Directory != null)
        {
            string path = Path.Combine(t.Directory, file);
            if (File.Exists(path)) return File.ReadAllText(path, Encoding.Default).Replace("\r\n", "\n");
        }
        return cpp ? "#include <iostream>\n\nint main()\n{\n    std::cout << \"Hello, Gestirn dev!\" << std::endl;\n    return 0;\n}\n"
                   : "#include <stdio.h>\n\nint main(void)\n{\n    printf(\"Hello, Gestirn dev!\\n\");\n    return 0;\n}\n";
    }

    public static DevProject Instantiate(ProjectTemplate t, string folder, string name, bool cpp)
    {
        var p = DevProject.Create(folder, name, t.Type, cpp);
        p.CompilerFlags = t.CompilerFlags;
        p.CppFlags = t.CppFlags;
        if (t.LinkerFlags.Length > 0) p.LinkerFlags = t.LinkerFlags;
        foreach (var u in t.Units)
        {
            string fileName = cpp ? (u.CppName ?? u.CName) : (u.CName ?? u.CppName);
            if (string.IsNullOrEmpty(fileName)) continue;
            string full = Path.Combine(folder, fileName);
            if (!File.Exists(full)) File.WriteAllText(full, UnitText(t, u, cpp).Replace("\t", "    "), new UTF8Encoding(false));
            p.AddUnit(full);
        }
        p.Save();
        return p;
    }
}

internal enum SymbolKind { Function, Class, Struct, Enum, Namespace, Macro }

internal sealed class SymbolInfo
{
    public string Name, Detail;
    public SymbolKind Kind;
    public int Line, Depth;
}

// Outline of a C/C++ file from its text: namespaces, classes, functions and macros.
internal static class Symbols
{
    private static readonly HashSet<string> NotFunctions = new HashSet<string> { "if", "for", "while", "switch", "return", "catch", "sizeof", "else", "do", "case", "new", "delete", "throw" };
    private static readonly Regex TypeDecl = new Regex(@"^\s*(?:template\s*<[^>]*>\s*)?(class|struct|union|enum(?:\s+class)?|namespace)\s+([A-Za-z_]\w*)", RegexOptions.Compiled);
    private static readonly Regex FuncDecl = new Regex(@"^\s*((?:[\w:<>,\*&~]+\s+)*?[\w:<>,\*&~]+[\s\*&]+)?([A-Za-z_~][\w~]*(?:::[A-Za-z_~][\w~]*)*)\s*\(([^;]*)\)\s*(?:const\s*)?(?:noexcept\s*)?(?:override\s*)?(?:->\s*[\w:<>]+\s*)?(\{.*)?$", RegexOptions.Compiled);
    private static readonly Regex Define = new Regex(@"^\s*#\s*define\s+([A-Za-z_]\w*)", RegexOptions.Compiled);

    public static List<SymbolInfo> Parse(CodeEditor editor)
    {
        var result = new List<SymbolInfo>();
        var stack = new List<string>();
        var tokens = new List<Token>();
        int state = 0;
        string pending = null;
        int count = Math.Min(editor.LineCount, 30000);
        for (int l = 0; l < count; l++)
        {
            string raw = editor.LineText(l);
            int startState = state;
            state = CppSyntax.Tokenize(raw, state, tokens);
            var code = new StringBuilder(raw);
            foreach (var t in tokens)
                if (t.Kind == Tok.Comment || t.Kind == Tok.String)
                    for (int i = t.Start; i < t.Start + t.Length && i < code.Length; i++) code[i] = ' ';
            string line = code.ToString();
            if (startState == CppSyntax.BlockComment && line.Trim().Length == 0) continue;

            bool insideCode = stack.Any(s => s == "func" || s == "block");
            string lineKind = null;
            var dm = Define.Match(line);
            if (dm.Success)
                result.Add(new SymbolInfo { Name = dm.Groups[1].Value, Kind = SymbolKind.Macro, Line = l, Detail = "#define", Depth = 0 });
            else if (!insideCode)
            {
                var tm = TypeDecl.Match(line);
                if (tm.Success && !(line.TrimEnd().EndsWith(";") && !line.Contains("{")))
                {
                    string kw = tm.Groups[1].Value;
                    var kind = kw == "namespace" ? SymbolKind.Namespace : kw.StartsWith("enum") ? SymbolKind.Enum : kw == "class" ? SymbolKind.Class : SymbolKind.Struct;
                    result.Add(new SymbolInfo { Name = tm.Groups[2].Value, Kind = kind, Line = l, Detail = kw, Depth = stack.Count });
                    lineKind = kind == SymbolKind.Namespace ? "ns" : kind == SymbolKind.Enum ? "block" : "type";
                }
                else
                {
                    var fm = FuncDecl.Match(line);
                    string name = fm.Success ? fm.Groups[2].Value : null;
                    bool typed = fm.Success && (fm.Groups[1].Success && fm.Groups[1].Value.Trim().Length > 0 || name.Contains("::"));
                    if (fm.Success && typed && !NotFunctions.Contains(name) && !NotFunctions.Contains(fm.Groups[1].Value.Trim()) && !line.TrimEnd().EndsWith(";"))
                    {
                        string args = Regex.Replace(fm.Groups[3].Value.Trim(), "\\s+", " ");
                        result.Add(new SymbolInfo { Name = name, Kind = SymbolKind.Function, Line = l, Detail = "(" + args + ")", Depth = stack.Count });
                        lineKind = "func";
                    }
                }
            }
            foreach (char c in line)
            {
                if (c == '{') { stack.Add(lineKind ?? pending ?? (insideCode ? "block" : "block")); lineKind = null; pending = null; }
                else if (c == '}' && stack.Count > 0) stack.RemoveAt(stack.Count - 1);
            }
            if (lineKind != null) pending = lineKind;
            else if (line.Trim().Length > 0 && !line.Contains("{")) pending = pending != null && line.Trim().StartsWith(":") ? pending : (line.Trim().EndsWith(";") ? null : pending);
        }
        return result;
    }
}
}
