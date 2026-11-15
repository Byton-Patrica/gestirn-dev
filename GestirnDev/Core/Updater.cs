using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GestirnDev
{
internal sealed class ReleaseInfo
{
    public string Tag, Version, Name, Notes, PageUrl, FullZip, LiteZip;
    public long FullSize, LiteSize;
}

// Checks the project's GitHub repository for a newer release and installs it in place.
internal static class Updater
{
    public const string Repo = "Byton-Patrica/gestirn-dev";
    public const string RepoUrl = "https://github.com/" + Repo;
    public const string ReleasesUrl = RepoUrl + "/releases";
    public const string LatestApi = "https://api.github.com/repos/" + Repo + "/releases/latest";

    // ------------------------------------------------------------------ versions

    public static string Normalize(string tag)
    {
        tag = (tag ?? "").Trim();
        if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)) tag = tag.Substring(1);
        int dash = tag.IndexOfAny(new[] { '-', '+', ' ' });
        return dash > 0 ? tag.Substring(0, dash) : tag;
    }

    // Gestirn versions read as decimals after the major number: 0.5 < 0.51 < 0.52 < 0.6.
    // Further components (0.52.1) compare as whole numbers.
    public static int Compare(string a, string b)
    {
        var x = Normalize(a).Split('.');
        var y = Normalize(b).Split('.');
        int c = Num(x, 0).CompareTo(Num(y, 0));
        if (c != 0) return c;
        c = Frac(x).CompareTo(Frac(y));
        if (c != 0) return c;
        for (int i = 2; i < Math.Max(x.Length, y.Length); i++)
        {
            c = Num(x, i).CompareTo(Num(y, i));
            if (c != 0) return c;
        }
        return 0;
    }

    public static bool IsNewer(string candidate, string current) { return Compare(candidate, current) > 0; }

    private static long Num(string[] parts, int i)
    {
        long v;
        return i < parts.Length && long.TryParse(parts[i], out v) ? v : 0;
    }

    private static decimal Frac(string[] parts)
    {
        if (parts.Length < 2) return 0m;
        string digits = new string(parts[1].TakeWhile(char.IsDigit).ToArray());
        decimal v;
        return digits.Length > 0 && decimal.TryParse("0." + digits, NumberStyles.Number, CultureInfo.InvariantCulture, out v) ? v : 0m;
    }

    // ------------------------------------------------------------------ GitHub

    private static readonly Regex Field = new Regex("\"(?<k>tag_name|name|body|html_url)\"\\s*:\\s*(?:\"(?<v>(?:[^\"\\\\]|\\\\.)*)\"|null)", RegexOptions.Compiled);
    private static readonly Regex Asset = new Regex("\"size\"\\s*:\\s*(?<size>\\d+)[^{}]*?\"browser_download_url\"\\s*:\\s*\"(?<url>[^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex AssetUrl = new Regex("\"browser_download_url\"\\s*:\\s*\"(?<url>[^\"]+)\"", RegexOptions.Compiled);

    public static ReleaseInfo ParseRelease(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var info = new ReleaseInfo();
        // The release's own fields come before "assets" and "author" in GitHub's response.
        int assetsAt = json.IndexOf("\"assets\"", StringComparison.Ordinal);
        string head = assetsAt > 0 ? json.Substring(0, assetsAt) : json;
        foreach (Match m in Field.Matches(head))
        {
            string k = m.Groups["k"].Value, v = Unescape(m.Groups["v"].Value);
            if (k == "tag_name" && info.Tag == null) info.Tag = v;
            else if (k == "name" && info.Name == null) info.Name = v;
            else if (k == "html_url" && info.PageUrl == null && v.Contains("/releases/")) info.PageUrl = v;
        }
        var body = Field.Matches(json).Cast<Match>().FirstOrDefault(m => m.Groups["k"].Value == "body");
        if (body != null) info.Notes = Unescape(body.Groups["v"].Value);
        if (string.IsNullOrEmpty(info.Tag)) return null;
        info.Version = Normalize(info.Tag);
        if (info.PageUrl == null) info.PageUrl = ReleasesUrl + "/tag/" + info.Tag;
        foreach (Match m in AssetUrl.Matches(json))
        {
            string url = m.Groups["url"].Value;
            if (url.EndsWith("-win64.zip", StringComparison.OrdinalIgnoreCase)) info.FullZip = url;
            else if (url.EndsWith("-lite.zip", StringComparison.OrdinalIgnoreCase)) info.LiteZip = url;
        }
        foreach (Match m in Asset.Matches(json))
        {
            long size;
            long.TryParse(m.Groups["size"].Value, out size);
            if (m.Groups["url"].Value == info.FullZip) info.FullSize = size;
            else if (m.Groups["url"].Value == info.LiteZip) info.LiteSize = size;
        }
        return info;
    }

    private static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
            char n = s[++i];
            switch (n)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': break;
                case 't': sb.Append('\t'); break;
                case 'b': case 'f': break;
                case 'u':
                    int code;
                    if (i + 4 < s.Length && int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) { sb.Append((char)code); i += 4; }
                    break;
                default: sb.Append(n); break;
            }
        }
        return sb.ToString();
    }

    private static HttpWebRequest Request(string url, int timeoutMs)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var req = (HttpWebRequest)WebRequest.Create(url);
        // A generic client name only; nothing about the user is sent.
        req.UserAgent = "GestirnDev/" + MainForm.Version;
        req.Accept = "application/vnd.github+json";
        req.Timeout = timeoutMs;
        req.ReadWriteTimeout = timeoutMs;
        req.AllowAutoRedirect = true;
        return req;
    }

    // Returns null when offline or when GitHub can't be reached; never throws.
    public static Task<ReleaseInfo> FetchLatest(int timeoutMs = 8000)
    {
        return Task.Run(() =>
        {
            try
            {
                var req = Request(LatestApi, timeoutMs);
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return ParseRelease(reader.ReadToEnd());
            }
            catch (WebException) { return null; }
            catch (IOException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (UriFormatException) { return null; }
            catch (System.Security.SecurityException) { return null; }
        });
    }

    // ------------------------------------------------------------------ install

    public static bool BundlesCompiler { get { return File.Exists(Path.Combine(Toolchain.AppDir, "MinGW64", "bin", "g++.exe")); } }

    public static string PackageFor(ReleaseInfo r)
    {
        if (r == null) return null;
        return BundlesCompiler ? (r.FullZip ?? r.LiteZip) : (r.LiteZip ?? r.FullZip);
    }

    public static bool CanWriteAppDir()
    {
        try
        {
            string probe = Path.Combine(Toolchain.AppDir, ".gestirn-write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static Task<string> Download(string url, IProgress<double> progress, CancellationToken cancel)
    {
        return Task.Run(() =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "gestirn-update");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, Path.GetFileName(new Uri(url).AbsolutePath));
            var req = Request(url, 30000);
            req.Accept = "application/octet-stream";
            using (cancel.Register(req.Abort))
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var input = resp.GetResponseStream())
            using (var outFile = File.Create(file))
            {
                long total = resp.ContentLength, done = 0;
                var buffer = new byte[81920];
                int n;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    outFile.Write(buffer, 0, n);
                    done += n;
                    if (total > 0 && progress != null) progress.Report((double)done / total);
                }
            }
            if (progress != null) progress.Report(1.0);
            return file;
        }, cancel);
    }

    // Waits for this process to exit, copies the new files over the program folder, then starts it again.
    public static void InstallAndRestart(string zip)
    {
        string app = Toolchain.AppDir.TrimEnd('\\');
        string exe = Path.Combine(app, Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName));
        string work = Path.Combine(Path.GetTempPath(), "gestirn-update", "unpacked");
        string script = Path.Combine(Path.GetTempPath(), "gestirn-update", "install.ps1");
        string log = Path.Combine(Path.GetTempPath(), "gestirn-update", "install.log");
        Func<string, string> q = s => "'" + s.Replace("'", "''") + "'";
        var ps = new StringBuilder();
        ps.AppendLine("$ErrorActionPreference = 'Stop'");
        ps.AppendLine("try {");
        ps.AppendLine("  Wait-Process -Id " + Process.GetCurrentProcess().Id + " -Timeout 60 -ErrorAction SilentlyContinue");
        ps.AppendLine("  Start-Sleep -Milliseconds 400");
        ps.AppendLine("  if (Test-Path " + q(work) + ") { Remove-Item " + q(work) + " -Recurse -Force }");
        ps.AppendLine("  Add-Type -AssemblyName System.IO.Compression.FileSystem");
        ps.AppendLine("  [IO.Compression.ZipFile]::ExtractToDirectory(" + q(zip) + ", " + q(work) + ")");
        ps.AppendLine("  $src = Get-ChildItem " + q(work) + " -Directory | Select-Object -First 1");
        ps.AppendLine("  if (-not $src -or -not (Test-Path (Join-Path $src.FullName 'Gestirn-dev.exe'))) { $src = Get-Item " + q(work) + " }");
        ps.AppendLine("  robocopy $src.FullName " + q(app) + " /E /R:3 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null");
        ps.AppendLine("  if ($LASTEXITCODE -ge 8) { throw \"robocopy failed: $LASTEXITCODE\" }");
        ps.AppendLine("  Remove-Item " + q(work) + " -Recurse -Force -ErrorAction SilentlyContinue");
        ps.AppendLine("  Remove-Item " + q(zip) + " -Force -ErrorAction SilentlyContinue");
        ps.AppendLine("} catch { $_ | Out-File " + q(log) + " }");
        ps.AppendLine("Start-Process -FilePath " + q(exe));
        File.WriteAllText(script, ps.ToString(), new UTF8Encoding(true));
        var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"")
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath(),
        };
        Process.Start(psi);
    }
}
}
