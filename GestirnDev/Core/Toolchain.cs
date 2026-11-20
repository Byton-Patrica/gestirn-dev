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
    public string ExePath;
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

    private static string CompileFlags(string source, BuildOptions o)
    {
        var sb = new StringBuilder();
        sb.Append(IsC(source) ? "-std=c11 " : "-std=" + o.Standard + " ");
        sb.Append(CharsetFlags(source, o.ExecCharset));
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

    public static async Task<ToolResult> RunTool(string exe, string args, string cwd, Action<Process> started)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
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
    public static Process RunProgram(string exe, string args, string workingDir)
    {
        string runner = Path.Combine(AppDir, "GestirnRun.exe");
        ProcessStartInfo psi;
        if (File.Exists(runner))
            psi = new ProcessStartInfo(runner, Quote(exe) + (string.IsNullOrEmpty(args) ? "" : " " + args));
        else
            psi = new ProcessStartInfo("cmd.exe", "/c \"" + Quote(exe) + " " + (args ?? "") + " & echo. & pause\"");
        psi.UseShellExecute = false;
        psi.WorkingDirectory = string.IsNullOrEmpty(workingDir) ? Path.GetDirectoryName(exe) : workingDir;
        return Process.Start(psi);
    }

    // ------------------------------------------------------------------ diagnostics

    private static readonly Regex DiagLine = new Regex(
        @"^(?<file>[^:\r\n]+(?::[\\/][^:\r\n]*)?):(?<line>\d+):(?:(?<col>\d+):)?\s*(?<kind>fatal error|error|warning|note):\s*(?<msg>.*)$",
        RegexOptions.Compiled);
    private static readonly Regex LinkLine = new Regex(@"^(?<file>[^:\r\n]+(?::[\\/][^:\r\n]*)?):\(\.\w+[^)]*\):\s*(?<msg>.*)$", RegexOptions.Compiled);
    private static readonly Regex Generic = new Regex(@"^(?<tool>collect2(?:\.exe)?|ld(?:\.exe)?|g\+\+(?:\.exe)?|gcc(?:\.exe)?|cc1plus(?:\.exe)?):\s*(?:fatal )?error:\s*(?<msg>.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<Diagnostic> ParseDiagnostics(string output, string baseDir)
    {
        var list = new List<Diagnostic>();
        foreach (var raw in output.Replace("\r", "").Split('\n'))
        {
            var m = DiagLine.Match(raw);
            if (m.Success)
            {
                string kind = m.Groups["kind"].Value;
                var d = new Diagnostic
                {
                    File = Resolve(m.Groups["file"].Value, baseDir),
                    Line = int.Parse(m.Groups["line"].Value) - 1,
                    Col = m.Groups["col"].Success ? int.Parse(m.Groups["col"].Value) : 0,
                    Severity = kind == "warning" ? Severity.Warning : kind == "note" ? Severity.Note : Severity.Error,
                    Message = m.Groups["msg"].Value.Trim(),
                };
                Hints.Apply(d);
                list.Add(d);
                continue;
            }
            m = LinkLine.Match(raw);
            if (m.Success)
            {
                var d = new Diagnostic { File = Resolve(m.Groups["file"].Value, baseDir), Line = -1, Severity = Severity.Error, Message = m.Groups["msg"].Value.Trim() };
                Hints.Apply(d);
                list.Add(d);
                continue;
            }
            m = Generic.Match(raw);
            if (m.Success && !raw.Contains("ld returned 1 exit status"))
            {
                var d = new Diagnostic { Line = -1, Severity = Severity.Error, Message = m.Groups["msg"].Value.Trim() };
                Hints.Apply(d);
                list.Add(d);
            }
        }
        return list;
    }

    private static string Resolve(string file, string baseDir)
    {
        try { return Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(baseDir, file)); }
        catch (ArgumentException) { return file; }
        catch (NotSupportedException) { return file; }
    }
}
}
