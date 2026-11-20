using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GestirnDev;

internal static class Cov
{
    private static int Main(string[] args)
    {
        Native.SetProcessDPIAware();
        Ui.Init();
        L.Zh = true;
        string dir = args[0];
        var perProgram = new Dictionary<string, StringBuilder>();
        foreach (var line in File.ReadAllLines(Path.Combine(dir, "raw.tsv"), Encoding.UTF8))
        {
            int tab = line.IndexOf('\t');
            if (tab < 0) continue;
            string name = line.Substring(0, tab);
            if (!perProgram.ContainsKey(name)) perProgram[name] = new StringBuilder();
            perProgram[name].AppendLine(line.Substring(tab + 1));
        }
        int total = 0, translated = 0, errTotal = 0, errTranslated = 0, warnTotal = 0, warnTranslated = 0;
        var missing = new Dictionary<string, List<string>>();
        var unparsed = new Dictionary<string, List<string>>();
        var programsWithErrors = 0; var programsFullyTranslated = 0; var programsHalf = 0;
        foreach (var kv in perProgram)
        {
            string output = kv.Value.ToString();
            var diags = Toolchain.ParseDiagnostics(output, dir);
            var real = diags.Where(d => d.Severity != Severity.Note).ToList();
            int errs = real.Count(d => d.Severity == Severity.Error);
            if (errs > 0)
            {
                programsWithErrors++;
                int okErr = real.Count(d => d.Severity == Severity.Error && d.Plain != null);
                if (okErr == errs) programsFullyTranslated++;
                if (okErr * 2 <= errs) programsHalf++;
            }
            foreach (var d in real)
            {
                total++;
                bool ok = d.Plain != null && !d.Approx;
                if (ok) translated++;
                if (d.Severity == Severity.Error) { errTotal++; if (ok) errTranslated++; } else { warnTotal++; if (ok) warnTranslated++; }
                if (!ok)
                {
                    string key = Mask(d.Message);
                    List<string> l;
                    if (!missing.TryGetValue(key, out l)) missing[key] = l = new List<string>();
                    l.Add(kv.Key);
                }
            }
            // lines that look like diagnostics but did not become one
            foreach (var raw in output.Replace("\r", "").Split('\n'))
            {
                string t = raw.Trim();
                if (t.Length == 0) continue;
                bool looks = Regex.IsMatch(t, @"\b(error|warning)\b:|undefined reference|cannot find -l|multiple definition|No such file|cannot open|fatal error", RegexOptions.IgnoreCase);
                if (!looks) continue;
                bool parsed = real.Any(d => t.Contains(d.Message)) || diags.Any(d => t.Contains(d.Message));
                if (!parsed && !t.Contains("ld returned 1 exit status") && !t.Contains("compilation terminated"))
                {
                    string key = Mask(t);
                    List<string> l;
                    if (!unparsed.TryGetValue(key, out l)) unparsed[key] = l = new List<string>();
                    l.Add(kv.Key);
                }
            }
        }
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("programs: {0}  with errors: {1}", perProgram.Count, programsWithErrors);
        Console.WriteLine("diagnostics shown in Problems: {0}   translated: {1} ({2:0}%)", total, translated, 100.0 * translated / Math.Max(1, total));
        Console.WriteLine("  errors:   {0}   translated: {1} ({2:0}%)", errTotal, errTranslated, 100.0 * errTranslated / Math.Max(1, errTotal));
        Console.WriteLine("  warnings: {0}   translated: {1} ({2:0}%)", warnTotal, warnTranslated, 100.0 * warnTranslated / Math.Max(1, warnTotal));
        Console.WriteLine("programs whose errors are ALL translated: {0}/{1}; programs with at most half translated: {2}", programsFullyTranslated, programsWithErrors, programsHalf);
        Console.WriteLine();
        Console.WriteLine("== UNTRANSLATED message shapes ({0}) ==", missing.Count);
        foreach (var kv in missing.OrderByDescending(k => k.Value.Count)) Console.WriteLine("{0,3}x  {1}   [{2}]", kv.Value.Count, kv.Key, string.Join(",", kv.Value.Distinct().Take(3)));
        Console.WriteLine();
        Console.WriteLine("== lines that look like errors but never reach the Problems list ({0}) ==", unparsed.Count);
        foreach (var kv in unparsed.OrderByDescending(k => k.Value.Count)) Console.WriteLine("{0,3}x  {1}   [{2}]", kv.Value.Count, kv.Key, string.Join(",", kv.Value.Distinct().Take(3)));
        // The gate: hand-written rules must read at least 95% of the errors; phrase fallback covers the rest.
        double rate = 100.0 * errTranslated / Math.Max(1, errTotal);
        Console.WriteLine();
        Console.WriteLine(rate >= 95.0 ? "GATE PASSED: {0:0.0}% of the errors have a hand-written translation (need 95%)" : "GATE FAILED: only {0:0.0}% of the errors have a hand-written translation (need 95%)", rate);
        return rate >= 95.0 ? 0 : 1;
    }

    // Replaces identifiers inside quotes so that equal message shapes group together.
    private static string Mask(string s)
    {
        s = s.Replace('‘', '\'').Replace('’', '\'').Replace('`', '\'');
        s = Regex.Replace(s, @"[A-Za-z]:[\\/][^ :]+", "<path>");
        s = Regex.Replace(s, @"'[^']*'", "'…'");
        s = Regex.Replace(s, @"\b\d+\b", "N");
        return s;
    }
}
