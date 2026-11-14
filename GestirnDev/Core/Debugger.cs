using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace GestirnDev
{
internal sealed class GdbFrame
{
    public int Level;
    public string Func = "", File = "", FullName = "";
    public int Line;
}

internal sealed class GdbVar
{
    public string Name = "", Type = "", Value = "";
}

// gdb machine-interface (MI2) session. Output is parsed off-thread and handed to the UI thread.
internal sealed class GdbSession : IDisposable
{
    private Process gdb;
    private readonly Control ui;
    private int nextToken = 1;
    private readonly Dictionary<int, Action<string, Dictionary<string, object>>> pending = new Dictionary<int, Action<string, Dictionary<string, object>>>();
    private readonly Dictionary<string, string> breakpointIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private bool exited;

    public event Action<string> Output;
    public event Action<GdbFrame, string> Stopped;
    public event Action Resumed;
    public event Action<string> Exited;

    public GdbSession(Control ui) { this.ui = ui; }

    public bool Alive { get { return gdb != null && !gdb.HasExited && !exited; } }
    public bool Paused { get; private set; }

    public void Start(string gdbPath, string exe, string workingDir, IEnumerable<KeyValuePair<string, int>> breakpoints)
    {
        var psi = new ProcessStartInfo(gdbPath, "--interpreter=mi2 --nx --quiet \"" + exe + "\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = workingDir,
            StandardOutputEncoding = Toolchain.ConsoleEncoding, StandardErrorEncoding = Toolchain.ConsoleEncoding,
        };
        psi.EnvironmentVariables["PATH"] = Path.GetDirectoryName(gdbPath) + ";" + Environment.GetEnvironmentVariable("PATH");
        gdb = new Process { StartInfo = psi, EnableRaisingEvents = true };
        gdb.OutputDataReceived += (_, e) => { if (e.Data != null) Post(() => Handle(e.Data)); };
        gdb.ErrorDataReceived += (_, e) => { if (e.Data != null) Post(() => Emit(e.Data)); };
        gdb.Exited += (_, _) => Post(() => Finish(null));
        gdb.Start();
        gdb.BeginOutputReadLine();
        gdb.BeginErrorReadLine();
        Send("-gdb-set new-console on");
        Send("-gdb-set print pretty on");
        Send("-gdb-set charset UTF-8");
        foreach (var bp in breakpoints) AddBreakpoint(bp.Key, bp.Value);
        Send("-exec-run", (cls, r) => { if (cls == "error") Emit(L.T("Could not start: ", "无法启动：") + Get(r, "msg")); });
    }

    private void Post(Action action)
    {
        if (ui.IsDisposed) return;
        try { ui.BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    private void Emit(string text) { if (Output != null) Output(text); }

    public void Send(string command, Action<string, Dictionary<string, object>> callback = null)
    {
        if (gdb == null || gdb.HasExited) return;
        int token = nextToken++;
        if (callback != null) pending[token] = callback;
        try
        {
            gdb.StandardInput.WriteLine(token.ToString(CultureInfo.InvariantCulture) + command);
            gdb.StandardInput.Flush();
        }
        catch (IOException) { }
        catch (InvalidOperationException) { }
    }

    public void Continue() { if (Paused) Send("-exec-continue"); }
    public void StepOver() { if (Paused) Send("-exec-next"); }
    public void StepInto() { if (Paused) Send("-exec-step"); }
    public void StepOut() { if (Paused) Send("-exec-finish"); }

    public void AddBreakpoint(string file, int line)
    {
        string key = file + ":" + line;
        Send("-break-insert \"" + Escape(Path.GetFileName(file)) + ":" + (line + 1) + "\"", (cls, r) =>
        {
            var bkpt = Get(r, "bkpt") as Dictionary<string, object>;
            if (cls == "done" && bkpt != null) breakpointIds[key] = Get(bkpt, "number") as string;
        });
    }

    public void RemoveBreakpoint(string file, int line)
    {
        string key = file + ":" + line, id;
        if (breakpointIds.TryGetValue(key, out id) && id != null)
        {
            Send("-break-delete " + id);
            breakpointIds.Remove(key);
        }
    }

    public void Locals(Action<List<GdbVar>> done)
    {
        Send("-stack-list-variables --simple-values", (cls, r) =>
        {
            var list = new List<GdbVar>();
            var vars = Get(r, "variables") as List<object>;
            if (vars != null)
                foreach (var v in vars.OfType<Dictionary<string, object>>())
                    list.Add(new GdbVar { Name = Get(v, "name") as string ?? "", Type = Get(v, "type") as string ?? "", Value = Get(v, "value") as string ?? "{...}" });
            done(list);
        });
    }

    public void Frames(Action<List<GdbFrame>> done)
    {
        Send("-stack-list-frames", (cls, r) =>
        {
            var list = new List<GdbFrame>();
            var stack = Get(r, "stack") as List<object>;
            if (stack != null)
                foreach (var item in stack)
                {
                    var f = item as Dictionary<string, object>;
                    if (f != null && f.ContainsKey("frame")) f = f["frame"] as Dictionary<string, object>;
                    if (f != null) list.Add(ToFrame(f));
                }
            done(list);
        });
    }

    public void Evaluate(string expression, Action<string> done)
    {
        Send("-data-evaluate-expression \"" + Escape(expression) + "\"", (cls, r) =>
            done(cls == "done" ? (Get(r, "value") as string ?? "") : "<" + (Get(r, "msg") as string ?? "error") + ">"));
    }

    public void Stop()
    {
        if (gdb == null) return;
        Toolchain.KillTree(gdb);
        Finish(null);
    }

    public void Dispose() { Stop(); }

    private void Finish(string exitCode)
    {
        if (exited) return;
        exited = true;
        Paused = false;
        if (Exited != null) Exited(exitCode);
    }

    private static string Escape(string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\""); }

    private static object Get(Dictionary<string, object> d, string key)
    {
        object v;
        return d != null && d.TryGetValue(key, out v) ? v : null;
    }

    private static GdbFrame ToFrame(Dictionary<string, object> f)
    {
        int line, level;
        int.TryParse(Get(f, "line") as string, out line);
        int.TryParse(Get(f, "level") as string, out level);
        return new GdbFrame
        {
            Level = level,
            Func = Get(f, "func") as string ?? "??",
            File = Get(f, "file") as string ?? "",
            FullName = (Get(f, "fullname") as string ?? "").Replace('/', '\\'),
            Line = Math.Max(0, line - 1),
        };
    }

    // ------------------------------------------------------------------ MI output

    private void Handle(string line)
    {
        if (line.Length == 0 || line.StartsWith("(gdb)")) return;
        int i = 0;
        while (i < line.Length && char.IsDigit(line[i])) i++;
        int token = 0;
        if (i > 0) int.TryParse(line.Substring(0, i), out token);
        if (i >= line.Length) return;
        char kind = line[i];
        string rest = line.Substring(i + 1);
        switch (kind)
        {
            case '^':
            {
                string cls;
                var results = ParseRecord(rest, out cls);
                Action<string, Dictionary<string, object>> cb;
                if (token > 0 && pending.TryGetValue(token, out cb)) { pending.Remove(token); cb(cls, results); }
                else if (cls == "error") Emit("gdb: " + (Get(results, "msg") as string ?? rest));
                if (cls == "running") { Paused = false; if (Resumed != null) Resumed(); }
                break;
            }
            case '*':
            {
                string cls;
                var results = ParseRecord(rest, out cls);
                if (cls == "running") { Paused = false; if (Resumed != null) Resumed(); }
                else if (cls == "stopped")
                {
                    string reason = Get(results, "reason") as string ?? "";
                    if (reason.StartsWith("exited"))
                    {
                        string code = Get(results, "exit-code") as string ?? (reason == "exited-normally" ? "0" : null);
                        Send("-gdb-exit");
                        Finish(code);
                        return;
                    }
                    Paused = true;
                    var frame = Get(results, "frame") as Dictionary<string, object>;
                    if (Stopped != null) Stopped(frame != null ? ToFrame(frame) : null, reason);
                }
                break;
            }
            case '~':
            case '@':
                Emit(Unquote(rest).TrimEnd('\n'));
                break;
            case '=':
            {
                string cls;
                var results = ParseRecord(rest, out cls);
                if (cls == "thread-group-exited" && Get(results, "exit-code") != null) { Send("-gdb-exit"); Finish(Get(results, "exit-code") as string); }
                break;
            }
        }
    }

    private static Dictionary<string, object> ParseRecord(string s, out string cls)
    {
        int comma = s.IndexOf(',');
        cls = comma < 0 ? s : s.Substring(0, comma);
        var results = new Dictionary<string, object>();
        if (comma < 0) return results;
        int i = comma + 1;
        ParseResults(s, ref i, results, '\0');
        return results;
    }

    private static void ParseResults(string s, ref int i, Dictionary<string, object> into, char end)
    {
        while (i < s.Length && s[i] != end)
        {
            int eq = s.IndexOf('=', i);
            if (eq < 0) break;
            string name = s.Substring(i, eq - i);
            i = eq + 1;
            object value = ParseValue(s, ref i);
            if (into.ContainsKey(name))
            {
                var existing = into[name] as List<object>;
                if (existing == null) into[name] = existing = new List<object> { into[name] };
                existing.Add(value);
            }
            else into[name] = value;
            if (i < s.Length && s[i] == ',') i++;
        }
    }

    private static object ParseValue(string s, ref int i)
    {
        if (i >= s.Length) return "";
        char c = s[i];
        if (c == '"')
        {
            var bytes = new List<byte>();
            i++;
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if (n >= '0' && n <= '7')
                    {
                        int len = 0, val = 0;
                        while (len < 3 && i + 1 + len < s.Length && s[i + 1 + len] >= '0' && s[i + 1 + len] <= '7') { val = val * 8 + (s[i + 1 + len] - '0'); len++; }
                        bytes.Add((byte)val);
                        i += 1 + len;
                        continue;
                    }
                    char mapped = n == 'n' ? '\n' : n == 't' ? '\t' : n == 'r' ? '\r' : n;
                    bytes.AddRange(Encoding.UTF8.GetBytes(mapped.ToString()));
                    i += 2;
                    continue;
                }
                bytes.AddRange(Encoding.UTF8.GetBytes(s[i].ToString()));
                i++;
            }
            i++;
            var arr = bytes.ToArray();
            try { return new UTF8Encoding(false, true).GetString(arr); }
            catch (DecoderFallbackException) { return Toolchain.ConsoleEncoding.GetString(arr); }
        }
        if (c == '{')
        {
            i++;
            var tuple = new Dictionary<string, object>();
            ParseResults(s, ref i, tuple, '}');
            i++;
            return tuple;
        }
        if (c == '[')
        {
            i++;
            var list = new List<object>();
            while (i < s.Length && s[i] != ']')
            {
                if (s[i] == '"' || s[i] == '{' || s[i] == '[') list.Add(ParseValue(s, ref i));
                else
                {
                    var one = new Dictionary<string, object>();
                    int eq = s.IndexOf('=', i);
                    if (eq < 0) break;
                    string name = s.Substring(i, eq - i);
                    i = eq + 1;
                    one[name] = ParseValue(s, ref i);
                    list.Add(one);
                }
                if (i < s.Length && s[i] == ',') i++;
            }
            i++;
            return list;
        }
        int start = i;
        while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']') i++;
        return s.Substring(start, i - start);
    }

    private static string Unquote(string s)
    {
        int i = 0;
        return s.StartsWith("\"") ? ParseValue(s, ref i) as string ?? s : s;
    }
}
}
