using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GestirnDev
{
// One function in a gprof flat profile.
internal sealed class ProfileEntry
{
    public string Name;
    public double Percent, Cumulative, Self;
    public long Calls = -1;
    public bool Recursive;
    public double SelfMsPerCall = -1, TotalMsPerCall = -1;

    // "loop(long)" -> "loop", "ns::Cls::run(int) const" -> "run"
    public string ShortName
    {
        get
        {
            string n = Name;
            int p = n.IndexOf('(');
            if (p > 0) n = n.Substring(0, p);
            int c = n.LastIndexOf("::", StringComparison.Ordinal);
            if (c >= 0) n = n.Substring(c + 2);
            return n.Trim();
        }
    }
}

internal sealed class ProfileReport
{
    public readonly List<ProfileEntry> Entries = new List<ProfileEntry>();
    public string Exe, Raw;
    public double SampleSeconds = 0.01;
    public double TotalSeconds { get { return Entries.Sum(e => e.Self); } }
    public bool HasSamples { get { return Entries.Any(e => e.Self > 0); } }
    public DateTime When = DateTime.Now;
}

// Dev-C++ style profiling: build with -pg, run, then read gmon.out with gprof.
internal static class Profiler
{
    // Runtime helpers that gprof lists but the user did not write.
    private static readonly HashSet<string> Internal = new HashSet<string>(StringComparer.Ordinal)
    {
        "_mcount_private", "__fentry__", "mcount", "_mcount", "__mcount_internal", "__gmon_start__", "_monstartup", "monstartup",
    };

    private static readonly Regex Row = new Regex(
        @"^\s*(?<pct>[\d.]+)\s+(?<cum>[\d.]+)\s+(?<self>[\d.]+)\s+(?:(?<calls>\d+)\s+(?<sms>[\d.]+)\s+(?<tms>[\d.]+)\s+)?(?<name>\S.*)$",
        RegexOptions.Compiled);
    // Call-graph primary line: "[320]  0.0  0.00  0.00  1+2692536  fib(int) [320]" (recursive calls after '+').
    private static readonly Regex Primary = new Regex(@"^\[\d+\]\s+[\d.]+\s+[\d.]+\s+[\d.]+\s+(?<calls>\d+)\+(?<rec>\d+)\s+(?<name>.+?)\s+\[\d+\]\s*$", RegexOptions.Compiled);
    private static readonly Regex Sample = new Regex(@"Each sample counts as ([\d.]+) seconds", RegexOptions.Compiled);

    public static ProfileReport Parse(string text)
    {
        var report = new ProfileReport { Raw = text ?? "" };
        bool inFlat = false;
        var recursive = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var raw in report.Raw.Replace("\r", "").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.StartsWith("Flat profile", StringComparison.Ordinal)) { inFlat = true; continue; }
            // The flat profile ends at a form feed (which TrimEnd removes, so test the raw line) or at "Call graph".
            if (raw.StartsWith("\f", StringComparison.Ordinal) || line.Trim().StartsWith("Call graph", StringComparison.Ordinal)) { inFlat = false; continue; }
            var s = Sample.Match(line);
            if (s.Success) { report.SampleSeconds = D(s.Groups[1].Value); continue; }
            if (!inFlat)
            {
                var pm = Primary.Match(line);
                if (pm.Success)
                {
                    long a, b;
                    long.TryParse(pm.Groups["calls"].Value, out a);
                    long.TryParse(pm.Groups["rec"].Value, out b);
                    recursive[pm.Groups["name"].Value.Trim()] = a + b;
                }
                continue;
            }
            var m = Row.Match(line);
            if (!m.Success) continue;
            var e = new ProfileEntry
            {
                Name = m.Groups["name"].Value.Trim(),
                Percent = D(m.Groups["pct"].Value),
                Cumulative = D(m.Groups["cum"].Value),
                Self = D(m.Groups["self"].Value),
            };
            if (m.Groups["calls"].Success)
            {
                long calls;
                if (long.TryParse(m.Groups["calls"].Value, out calls)) e.Calls = calls;
                e.SelfMsPerCall = D(m.Groups["sms"].Value);
                e.TotalMsPerCall = D(m.Groups["tms"].Value);
            }
            if (Internal.Contains(e.Name)) continue;
            report.Entries.Add(e);
        }
        foreach (var e in report.Entries)
        {
            long n;
            if (recursive.TryGetValue(e.Name, out n)) { e.Calls = n; e.Recursive = true; }
        }
        // Percentages without gprof's own helpers, so the visible rows add up to 100%.
        double total = report.TotalSeconds;
        if (total > 0) foreach (var e in report.Entries) e.Percent = e.Self / total * 100.0;
        report.Entries.Sort((a, b) =>
        {
            int c = b.Self.CompareTo(a.Self);
            return c != 0 ? c : b.Calls.CompareTo(a.Calls);
        });
        return report;
    }

    private static double D(string s)
    {
        double v;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
    }

    public static string FindGprof(string compiler) { return Toolchain.Sibling(compiler, "gprof.exe"); }

    public static async Task<ProfileReport> Analyze(string gprof, string exe, string gmon)
    {
        var r = await Toolchain.RunTool(gprof, "-b \"" + exe + "\" \"" + gmon + "\"", Path.GetDirectoryName(exe), null);
        var report = Parse(r.Output);
        report.Exe = exe;
        return report;
    }
}
}
