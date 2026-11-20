using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GestirnDev
{
internal sealed class BuildOptions
{
    public string Compiler;
    public string Standard = "c++14";
    public string ExtraFlags = "";
    public string Optimization = "";
    public bool Debug;
    public bool Profile;
    public bool StaticLink;
    public string ExecCharset = "auto";
}

internal sealed class BuildOutcome
{
    public bool Success, UpToDate;
    public string ExePath, TempDir;
    public readonly StringBuilder Log = new StringBuilder();
    public readonly List<Diagnostic> Diagnostics = new List<Diagnostic>();
    public TimeSpan Elapsed;
}

internal static class Toolchain
{
    public static string AppDir { get { return Application.StartupPath; } }

    public static Encoding ConsoleEncoding
    {
        get { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage); }
    }

    // ------------------------------------------------------------------ discovery

    public static string FindCompiler(string saved)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(saved)) candidates.Add(saved);
        candidates.Add(Path.Combine(AppDir, "MinGW64", "bin", "g++.exe"));
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var d = dir.Trim().Trim('"');
            if (d.Length > 0) candidates.Add(Path.Combine(d, "g++.exe"));
        }
        foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles") })
            if (!string.IsNullOrEmpty(root)) candidates.Add(Path.Combine(root, "Dev-Cpp", "MinGW64", "bin", "g++.exe"));
        candidates.Add(@"C:\msys64\ucrt64\bin\g++.exe");
        candidates.Add(@"C:\msys64\mingw64\bin\g++.exe");
        candidates.Add(@"C:\MinGW\bin\g++.exe");
        foreach (var c in candidates)
        {
            try { if (File.Exists(c)) return Path.GetFullPath(c); }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
        }
        return null;
    }

    private static readonly Dictionary<string, string> versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static string Version(string compiler)
    {
        if (string.IsNullOrEmpty(compiler)) return "";
        string v;
        if (versions.TryGetValue(compiler, out v)) return v;
        v = "";
        try
        {
            var psi = new ProcessStartInfo(compiler, "-dumpversion") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                v = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(4000);
            }
        }
        catch (System.ComponentModel.Win32Exception) { }
        catch (InvalidOperationException) { }
        versions[compiler] = v;
        return v;
    }

    public static int Major(string version)
    {
        int major;
        int.TryParse((version ?? "").Split('.')[0], out major);
        return major;
    }

    public static string DefaultStandard(string compiler)
    {
        int major = Major(Version(compiler));
        if (major >= 11) return "c++20";
        if (major >= 7) return "c++17";
        return "c++14";
    }

    public static string Sibling(string compiler, string name)
    {
        if (string.IsNullOrEmpty(compiler)) return null;
        string p = Path.Combine(Path.GetDirectoryName(compiler), name);
        return File.Exists(p) ? p : null;
    }

    public static string FindGdb(string compiler) { return Sibling(compiler, "gdb.exe"); }

    public static string FindAStyle(string compiler)
    {
        var candidates = new List<string> { Path.Combine(AppDir, "AStyle", "AStyle.exe"), Path.Combine(AppDir, "AStyle.exe") };
        if (!string.IsNullOrEmpty(compiler))
        {
            var root = Directory.GetParent(Path.GetDirectoryName(compiler));
            if (root != null && root.Parent != null) candidates.Add(Path.Combine(root.Parent.FullName, "AStyle", "AStyle.exe"));
        }
        foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles") })
            if (!string.IsNullOrEmpty(root)) candidates.Add(Path.Combine(root, "Dev-Cpp", "AStyle", "AStyle.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    public static string TemplatesDir()
    {
        var candidates = new[] { Path.Combine(AppDir, "Templates"), Path.Combine(AppDir, "..", "Templates"), Path.Combine(AppDir, "..", "..", "Templates") };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(Directory.Exists);
    }

    // ------------------------------------------------------------------ flags

    private static bool IsUtf8WithNonAscii(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (!bytes.Any(b => b >= 0x80)) return false;
            new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException) { return false; }
        catch (IOException) { return false; }
    }

    // Sources are saved as UTF-8; on Chinese Windows the console expects GBK, so string
    // literals are converted at compile time and Chinese output shows up correctly.
    public static string CharsetFlags(string source, string mode)
    {
        if (mode == "utf8") return "";
        bool gbkConsole = CultureInfo.CurrentCulture.TextInfo.ANSICodePage == 936;
        if ((mode == "gbk" || (mode == "auto" && gbkConsole)) && IsUtf8WithNonAscii(source))
            return "-finput-charset=UTF-8 -fexec-charset=GBK ";
        return "";
    }

    private static bool IsC(string path) { return string.Equals(Path.GetExtension(path), ".c", StringComparison.OrdinalIgnoreCase); }

    // For text that only exists in the editor: UTF-8 in, GBK out on Chinese Windows.
    public static string CharsetFlagsForText(string text, string mode)
    {
        if (mode == "utf8") return "";
        bool gbkConsole = CultureInfo.CurrentCulture.TextInfo.ANSICodePage == 936;
        bool nonAscii = (text ?? "").Any(c => c >= 0x80);
        if ((mode == "gbk" || (mode == "auto" && gbkConsole)) && nonAscii) return "-finput-charset=UTF-8 -fexec-charset=GBK ";
        return "";
    }

    private static string CompileFlags(string source, BuildOptions o, string text = null)
    {
        var sb = new StringBuilder();
        sb.Append(IsC(source) ? "-std=c11 " : "-std=" + o.Standard + " ");
        sb.Append(text != null ? CharsetFlagsForText(text, o.ExecCharset) : CharsetFlags(source, o.ExecCharset));
        if (o.Debug) sb.Append("-g -O0 ");
        else if (!string.IsNullOrEmpty(o.Optimization)) sb.Append(o.Optimization).Append(' ');
        if (o.Profile) sb.Append("-pg -fno-omit-frame-pointer -fno-inline-functions-called-once ");
        if (!string.IsNullOrEmpty(o.ExtraFlags)) sb.Append(o.ExtraFlags).Append(' ');
        return sb.ToString();
    }

    private static string CompilerFor(string source, string compiler)
    {
        if (!IsC(source)) return compiler;
        return Sibling(compiler, "gcc.exe") ?? compiler;
    }

    private static string Quote(string s) { return "\"" + s + "\""; }

    // ------------------------------------------------------------------ processes

    public sealed class ToolResult { public int ExitCode; public string Output; }

    public static async Task<ToolResult> RunTool(string exe, string args, string cwd, Action<Process> started, string stdin = null)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin != null,
            WorkingDirectory = cwd, StandardOutputEncoding = ConsoleEncoding, StandardErrorEncoding = ConsoleEncoding,
        };
        psi.EnvironmentVariables["PATH"] = Path.GetDirectoryName(exe) + ";" + Environment.GetEnvironmentVariable("PATH");
        // Messages are matched and translated from their English wording, so force it whatever the system language.
        psi.EnvironmentVariables["LC_ALL"] = "C";
        psi.EnvironmentVariables["LANG"] = "C";
        using (var p = new Process { StartInfo = psi })
        {
            p.Start();
            if (started != null) started(p);
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            if (stdin != null)
            {
                try
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(stdin);
                    p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                    p.StandardInput.Close();
                }
                catch (IOException) { }
            }
            await Task.Run(() => p.WaitForExit());
            return new ToolResult { ExitCode = p.ExitCode, Output = (await o) + (await e) };
        }
    }

    public static void KillTree(Process p)
    {
        try
        {
            if (p == null || p.HasExited) return;
            using (var k = Process.Start(new ProcessStartInfo("taskkill", "/PID " + p.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true }))
                k.WaitForExit(3000);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    // ------------------------------------------------------------------ builds

    public static async Task<BuildOutcome> BuildSingle(string source, BuildOptions o, Action<Process> started)
    {
        var watch = Stopwatch.StartNew();
        var outcome = new BuildOutcome();
        string dir = Path.GetDirectoryName(source);
        string exe = Path.Combine(dir, Path.GetFileNameWithoutExtension(source) + ".exe");
        outcome.ExePath = exe;
        if (!TryDelete(exe, outcome)) return outcome;
        string args = CompileFlags(source, o) + Quote(Path.GetFileName(source)) + " -o " + Quote(Path.GetFileName(exe)) + (o.StaticLink ? " -static" : "") + (o.Profile ? " -pg" : "");
        outcome.Log.AppendLine(Path.GetFileName(CompilerFor(source, o.Compiler)) + " " + args);
        var r = await RunTool(CompilerFor(source, o.Compiler), args, dir, started);
        outcome.Log.Append(r.Output);
        outcome.Diagnostics.AddRange(ParseDiagnostics(r.Output, dir));
        outcome.Success = r.ExitCode == 0 && File.Exists(exe);
        outcome.Elapsed = watch.Elapsed;
        return outcome;
    }

    private static bool TryDelete(string exe, BuildOutcome outcome)
    {
        try
        {
            if (File.Exists(exe)) File.Delete(exe);
            return true;
        }
        catch (IOException)
        {
            outcome.Diagnostics.Add(new Diagnostic { File = exe, Line = -1, Severity = Severity.Error,
                Message = L.T(Path.GetFileName(exe) + " is still running and cannot be replaced.", Path.GetFileName(exe) + " 仍在运行，无法覆盖。"),
                Hint = L.T("Close the program window (or press Stop), then build again.", "先关闭程序窗口（或点“停止”），再重新编译。") });
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            outcome.Diagnostics.Add(new Diagnostic { File = exe, Line = -1, Severity = Severity.Error, Message = L.T("No permission to write ", "没有权限写入 ") + exe });
            return false;
        }
    }

    // "Trial run": compiles the text that is in the editor (saved or not) straight from stdin into a temporary
    // exe. A "#line" directive makes errors point at the real file, and the file on disk is never touched.
    public static async Task<BuildOutcome> BuildTrial(string originalPath, string text, string workDir, BuildOptions o, Action<Process> started)
    {
        var watch = Stopwatch.StartNew();
        var outcome = new BuildOutcome();
        string name = string.IsNullOrEmpty(originalPath) ? Path.Combine(workDir, "untitled.cpp") : originalPath;
        string temp = Path.Combine(Path.GetTempPath(), "GestirnTrial", Guid.NewGuid().ToString("N").Substring(0, 10));
        Directory.CreateDirectory(temp);
        string exe = Path.Combine(temp, "trial.exe");
        outcome.ExePath = exe;
        outcome.TempDir = temp;
        string args = "-x " + (IsC(name) ? "c" : "c++") + " " + CompileFlags(name, o, text) + "-I " + Quote(workDir) + " - -o " + Quote(exe) + (o.StaticLink ? " -static" : "");
        string source = "#line 1 \"" + name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"\n" + text;
        outcome.Log.AppendLine(Path.GetFileName(CompilerFor(name, o.Compiler)) + " " + args);
        var r = await RunTool(CompilerFor(name, o.Compiler), args, workDir, started, source);
        outcome.Log.Append(r.Output);
        string key = Path.GetFullPath(name);
        lock (TextOverrides) TextOverrides[key] = text;
        try { outcome.Diagnostics.AddRange(ParseDiagnostics(r.Output, workDir)); }
        finally { lock (TextOverrides) TextOverrides.Remove(key); }
        outcome.Success = r.ExitCode == 0 && File.Exists(exe);
        outcome.Elapsed = watch.Elapsed;
        return outcome;
    }

    public static void DeleteTrialDir(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !dir.Contains("GestirnTrial")) return;
        for (int i = 0; i < 5; i++)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); return; }
            catch (IOException) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) { Thread.Sleep(200); }
        }
    }

    // Removes leftovers of earlier sessions (a crash or a killed program can leave a folder behind).
    public static void CleanTrialTemp()
    {
        try
        {
            string root = Path.Combine(Path.GetTempPath(), "GestirnTrial");
            if (!Directory.Exists(root)) return;
            foreach (var d in Directory.GetDirectories(root))
                if (DateTime.UtcNow - Directory.GetCreationTimeUtc(d) > TimeSpan.FromHours(6)) DeleteTrialDir(d);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Builds a Dev-C++ project: units compile in parallel, unchanged objects are reused, then link.
    public static async Task<BuildOutcome> BuildProject(DevProject project, BuildOptions o, bool rebuild, Action<string> log, Action<Process> started)
    {
        var watch = Stopwatch.StartNew();
        var outcome = new BuildOutcome();
        string dir = project.Directory;
        string objDir = project.ObjectDir;
        Directory.CreateDirectory(objDir);
        string exe = project.OutputPath;
        outcome.ExePath = exe;
        if (project.UseCustomMakefile && !string.IsNullOrEmpty(project.CustomMakefile))
        {
            string make = Sibling(o.Compiler, "mingw32-make.exe") ?? "mingw32-make.exe";
            string mf = project.FullPath(project.CustomMakefile);
            if (rebuild) { log("mingw32-make clean"); await RunTool(make, "-f \"" + mf + "\" clean", dir, started); }
            log("mingw32-make -f " + Path.GetFileName(mf));
            var mr = await RunTool(make, "-f \"" + mf + "\"", dir, started);
            outcome.Log.Append(mr.Output);
            if (mr.Output.Length > 0) log(mr.Output.TrimEnd());
            outcome.Diagnostics.AddRange(ParseDiagnostics(mr.Output, dir));
            outcome.Success = mr.ExitCode == 0;
            outcome.Elapsed = watch.Elapsed;
            return outcome;
        }
        var sources = project.Units.Where(u => u.Compile && DevProject.IsSource(u.FileName)).ToList();
        if (sources.Count == 0)
        {
            outcome.Diagnostics.Add(new Diagnostic { Line = -1, Severity = Severity.Error, Message = L.T("The project has no source files.", "项目中没有源文件。") });
            return outcome;
        }
        bool anyCpp = sources.Any(u => !IsC(u.FileName)) || project.IsCpp;
        var headers = project.Units.Where(u => !DevProject.IsSource(u.FileName)).Select(u => project.FullPath(u.FileName)).Where(File.Exists).ToList();
        DateTime newestHeader = headers.Count > 0 ? headers.Max(h => File.GetLastWriteTimeUtc(h)) : DateTime.MinValue;
        string flagsStamp = string.Join("|", o.Standard, o.ExtraFlags, o.Optimization, o.Debug, o.Profile, project.CompilerFlags, project.CppFlags, project.Includes);
        string stampFile = Path.Combine(objDir, ".gestirn-flags");
        bool flagsChanged = !File.Exists(stampFile) || File.ReadAllText(stampFile) != flagsStamp;
        if (flagsChanged) rebuild = true;

        var objects = new List<string>();
        var jobs = new List<Func<Task<bool>>>();
        var logLock = new object();
        foreach (var unit in sources)
        {
            string src = project.FullPath(unit.FileName);
            string obj = Path.Combine(objDir, Path.GetFileNameWithoutExtension(unit.FileName) + (string.Equals(Path.GetExtension(src), ".rc", StringComparison.OrdinalIgnoreCase) ? "_res.o" : ".o"));
            objects.Add(obj);
            bool stale = rebuild || !File.Exists(obj) || File.GetLastWriteTimeUtc(src) > File.GetLastWriteTimeUtc(obj) || newestHeader > File.GetLastWriteTimeUtc(obj);
            if (!stale) continue;
            jobs.Add(async () =>
            {
                string tool, args;
                if (string.Equals(Path.GetExtension(src), ".rc", StringComparison.OrdinalIgnoreCase))
                {
                    tool = Sibling(o.Compiler, "windres.exe") ?? "windres.exe";
                    args = "-i " + Quote(src) + " --input-format=rc -o " + Quote(obj) + " -O coff";
                }
                else
                {
                    tool = CompilerFor(src, o.Compiler);
                    string extra = IsC(src) ? project.CompilerFlags : project.CppFlags;
                    args = "-c " + CompileFlags(src, o) + extra + " " + project.IncludeFlags + Quote(src) + " -o " + Quote(obj);
                }
                lock (logLock) { log(Path.GetFileName(tool) + " " + Path.GetFileName(src)); }
                var r = await RunTool(tool, args, dir, started);
                lock (logLock)
                {
                    outcome.Log.Append(r.Output);
                    outcome.Diagnostics.AddRange(ParseDiagnostics(r.Output, dir));
                    if (r.Output.Length > 0) log(r.Output.TrimEnd());
                }
                return r.ExitCode == 0;
            });
        }

        bool ok = true;
        if (jobs.Count == 0 && File.Exists(exe) && objects.All(File.Exists) && objects.Max(f => File.GetLastWriteTimeUtc(f)) <= File.GetLastWriteTimeUtc(exe))
        {
            outcome.Success = true;
            outcome.UpToDate = true;
            outcome.Elapsed = watch.Elapsed;
            return outcome;
        }
        using (var gate = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount)))
        {
            var tasks = jobs.Select(async job =>
            {
                await gate.WaitAsync();
                try { return await job(); }
                finally { gate.Release(); }
            }).ToList();
            var results = await Task.WhenAll(tasks);
            ok = results.All(x => x);
        }
        if (!ok) { outcome.Elapsed = watch.Elapsed; return outcome; }
        File.WriteAllText(stampFile, flagsStamp);

        if (!TryDelete(exe, outcome)) return outcome;
        string objList = string.Join(" ", objects.Select(Quote));
        ToolResult link;
        if (project.Type == DevProject.TypeStaticLib)
        {
            string ar = Sibling(o.Compiler, "ar.exe") ?? "ar.exe";
            log("ar " + Path.GetFileName(exe));
            link = await RunTool(ar, "rcs " + Quote(exe) + " " + objList, dir, started);
        }
        else
        {
            string linker = anyCpp ? o.Compiler : (Sibling(o.Compiler, "gcc.exe") ?? o.Compiler);
            var la = new StringBuilder();
            la.Append(objList).Append(" -o ").Append(Quote(exe)).Append(' ');
            if (project.Type == DevProject.TypeGui) la.Append("-mwindows ");
            if (project.Type == DevProject.TypeDll) la.Append("-shared ");
            if (o.Debug) la.Append("-g ");
            if (o.Profile) la.Append("-pg ");
            if (o.StaticLink) la.Append("-static ");
            la.Append(project.LinkerFlags).Append(' ').Append(project.Libs);
            log(Path.GetFileName(linker) + " -o " + Path.GetFileName(exe));
            link = await RunTool(linker, la.ToString(), dir, started);
        }
        outcome.Log.Append(link.Output);
        outcome.Diagnostics.AddRange(ParseDiagnostics(link.Output, dir));
        if (link.Output.Length > 0) log(link.Output.TrimEnd());
        outcome.Success = link.ExitCode == 0 && File.Exists(exe);
        outcome.Elapsed = watch.Elapsed;
        return outcome;
    }

    // Compiles without producing output: fast feedback on errors (Dev-C++ "Syntax check").
    public static async Task<List<Diagnostic>> SyntaxCheck(string source, BuildOptions o)
    {
        string dir = Path.GetDirectoryName(source);
        var r = await RunTool(CompilerFor(source, o.Compiler), "-fsyntax-only " + CompileFlags(source, o) + Quote(Path.GetFileName(source)), dir, null);
        return ParseDiagnostics(r.Output, dir);
    }

    // ------------------------------------------------------------------ run

    // Runs the program in its own console through GestirnRun.exe, which prints the exit code and
    // elapsed time and waits for a key (like Dev-C++'s console pauser).
    public static Process RunProgram(string exe, string args, string workingDir, string note = null)
    {
        string runner = Path.Combine(AppDir, "GestirnRun.exe");
        ProcessStartInfo psi;
        if (File.Exists(runner))
            psi = new ProcessStartInfo(runner, Quote(exe) + (string.IsNullOrEmpty(args) ? "" : " " + args));
        else
            psi = new ProcessStartInfo("cmd.exe", "/c \"" + Quote(exe) + " " + (args ?? "") + " & echo. & pause\"");
        psi.UseShellExecute = false;
        psi.WorkingDirectory = string.IsNullOrEmpty(workingDir) ? Path.GetDirectoryName(exe) : workingDir;
        if (!string.IsNullOrEmpty(note)) psi.EnvironmentVariables["GESTIRN_NOTE"] = note;
        return Process.Start(psi);
    }

    // ------------------------------------------------------------------ diagnostics

    private static readonly Regex DiagLine = new Regex(
        @"^(?<file>[^:\r\n]+(?::[\\/][^:\r\n]*)?):(?<line>\d+):(?:(?<col>\d+):)?\s*(?<kind>fatal error|error|warning|note):\s*(?<msg>.*)$",
        RegexOptions.Compiled);
    // Linker messages: "C:\Temp\cc1.o:main.cpp:(.text+0x13): undefined reference to `f(int)'" (the object file name carries a colon).
    private static readonly Regex LinkLine = new Regex(@"^(?<pre>.+?):\((?<sect>\.[^)]*)\):\s*(?<msg>.+)$", RegexOptions.Compiled);
    // With debug information the linker gives a line number: "main.cpp:12: undefined reference to `f(int)'".
    private static readonly Regex LinkLineNo = new Regex(@"^(?<file>[^:\r\n]+(?::[\\/][^:\r\n]*)?):(?<line>\d+):\s*(?<msg>(?:undefined reference|multiple definition|relocation truncated).*)$", RegexOptions.Compiled);
    // "ld.exe: cannot find -lfoo", "g++.exe: error: x.cpp: No such file or directory", "cc1plus.exe: warning: ..."
    private static readonly Regex Generic = new Regex(@"^(?:.*[\\/])?(?<tool>collect2|ld|g\+\+|gcc|cc1plus|cc1|windres|ar|as|dlltool|cpp)(?:\.exe)?:\s*(?:(?<kind>fatal error|error|warning):\s*)?(?<msg>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StrayByte = new Regex(@"^stray '\\\d+' in program", RegexOptions.Compiled);

    public static List<Diagnostic> ParseDiagnostics(string output, string baseDir)
    {
        var list = new List<Diagnostic>();
        foreach (var raw in output.Replace("\r", "").Split('\n'))
        {
            var m = DiagLine.Match(raw);
            if (m.Success)
            {
                string kind = m.Groups["kind"].Value;
                list.Add(new Diagnostic
                {
                    File = Resolve(m.Groups["file"].Value, baseDir),
                    Line = int.Parse(m.Groups["line"].Value) - 1,
                    Col = m.Groups["col"].Success ? int.Parse(m.Groups["col"].Value) : 0,
                    Severity = kind == "warning" ? Severity.Warning : kind == "note" ? Severity.Note : Severity.Error,
                    Message = m.Groups["msg"].Value.Trim(),
                });
                continue;
            }
            m = LinkLineNo.Match(raw);
            if (m.Success)
            {
                list.Add(new Diagnostic { File = Resolve(m.Groups["file"].Value, baseDir), Line = int.Parse(m.Groups["line"].Value) - 1, Severity = Severity.Error, Message = m.Groups["msg"].Value.Trim() });
                continue;
            }
            m = LinkLine.Match(raw);
            if (m.Success)
            {
                string msg = m.Groups["msg"].Value.Trim();
                string pre = m.Groups["pre"].Value;
                string file = null;
                int colon = pre.LastIndexOf(':');
                if (colon > 1)
                {
                    string cand = pre.Substring(colon + 1);
                    if (cand.Length > 0 && !cand.EndsWith(".o", StringComparison.OrdinalIgnoreCase) && !cand.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)) file = Resolve(cand, baseDir);
                }
                bool isNote = msg.StartsWith("first defined here", StringComparison.OrdinalIgnoreCase) || msg.StartsWith("in function", StringComparison.OrdinalIgnoreCase);
                list.Add(new Diagnostic { File = file, Line = -1, Severity = isNote ? Severity.Note : Severity.Error, Message = msg });
                continue;
            }
            m = Generic.Match(raw);
            if (m.Success)
            {
                string msg = m.Groups["msg"].Value.Trim();
                if (msg.Contains("ld returned") && msg.Contains("exit status")) continue;
                string kind = m.Groups["kind"].Value.ToLowerInvariant();
                // "ld.exe: warning: ..." and friends keep their own severity; a bare "ld.exe: cannot find ..." is an error
                if (m.Groups["tool"].Value.Equals("ld", StringComparison.OrdinalIgnoreCase) && kind.Length == 0 && msg.StartsWith("warning:", StringComparison.OrdinalIgnoreCase)) { kind = "warning"; msg = msg.Substring(8).Trim(); }
                if (msg.EndsWith(":") || msg.StartsWith("In function", StringComparison.Ordinal)) continue;
                list.Add(new Diagnostic { Line = -1, Severity = kind == "warning" ? Severity.Warning : Severity.Error, Message = msg });
            }
        }
        return Finish(list);
    }

    // The text of a source line and the one before it, for translating errors with the code around them.
    // Unsaved text (a trial run) is registered in TextOverrides; everything else is read from disk.
    public static readonly Dictionary<string, string> TextOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, KeyValuePair<DateTime, string[]>> lineCache = new Dictionary<string, KeyValuePair<DateTime, string[]>>(StringComparer.OrdinalIgnoreCase);

    public static string[] SourceLines(string file, int line)
    {
        string[] all;
        lock (TextOverrides)
        {
            string over;
            if (TextOverrides.TryGetValue(file, out over)) all = over.Replace("\r", "").Split('\n');
            else
            {
                if (!File.Exists(file)) return null;
                DateTime t = File.GetLastWriteTimeUtc(file);
                KeyValuePair<DateTime, string[]> cached;
                if (lineCache.TryGetValue(file, out cached) && cached.Key == t) all = cached.Value;
                else
                {
                    byte[] bytes = File.ReadAllBytes(file);
                    string text;
                    try { text = new UTF8Encoding(false, true).GetString(bytes); }
                    catch (DecoderFallbackException) { text = ConsoleEncoding.GetString(bytes); }
                    all = text.Replace("\r", "").Split('\n');
                    if (lineCache.Count > 64) lineCache.Clear();
                    lineCache[file] = new KeyValuePair<DateTime, string[]>(t, all);
                }
            }
        }
        if (line < 0 || line >= all.Length) return null;
        return line > 0 ? new[] { all[line], all[line - 1] } : new[] { all[line] };
    }

    // Removes duplicates, folds the several "stray" errors a single Chinese character produces into one, marks the
    // errors that are probably knock-on effects, then translates everything with the code around it.
    private static List<Diagnostic> Finish(List<Diagnostic> list)
    {
        var result = new List<Diagnostic>();
        var seen = new HashSet<string>();
        var strayLines = new HashSet<string>();
        foreach (var d in list)
        {
            string key = d.File + "|" + d.Line + "|" + d.Col + "|" + d.Severity + "|" + d.Message;
            if (!seen.Add(key)) continue;
            if (d.Severity == Severity.Error && d.Line >= 0 && StrayByte.IsMatch(d.Message))
            {
                if (!strayLines.Add(d.File + "|" + d.Line)) continue;   // the second and third byte of the same character
            }
            result.Add(d);
        }
        // knock-on errors: anything else reported on a line that already has a stray character, or the line after a syntax error
        var rootLines = new Dictionary<string, int>();
        foreach (var d in result.Where(x => x.Severity == Severity.Error && x.Line >= 0))
        {
            string at = d.File + "|" + d.Line;
            bool isRoot = StrayByte.IsMatch(d.Message);
            if (isRoot) rootLines[at] = d.Line;
            else if (rootLines.ContainsKey(at)) d.Cascade = true;
        }
        foreach (var d in result)
        {
            Hints.Ctx ctx = null;
            if (d.Line >= 0 && d.File != null && SourceLines != null)
            {
                string[] lines = null;
                try { lines = SourceLines(d.File, d.Line); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (lines != null && lines.Length > 0)
                {
                    ctx = new Hints.Ctx { Line = lines[0], Prev = lines.Length > 1 ? lines[1] : null, Col = d.Col };
                    d.SourceLine = lines[0];
                }
            }
            Hints.Apply(d, ctx);
            if (d.Cascade && d.Plain != null)
                d.Plain = L.T("(Probably caused by the error above) ", "（多半是上面那个错误连带出来的）") + d.Plain;
        }
        return result;
    }

    private static string Resolve(string file, string baseDir)
    {
        try { return Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(baseDir, file)); }
        catch (ArgumentException) { return file; }
        catch (NotSupportedException) { return file; }
    }
}
}
