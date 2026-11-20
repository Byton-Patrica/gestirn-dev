using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GestirnDev
{
// API keys are stored encrypted for the current Windows user (DPAPI), never as plain text.
internal static class Secret
{
    private const string Prefix = "dpapi:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("gestirn-dev");

    public static string Conceal(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        try
        {
            var bytes = System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(bytes);
        }
        catch (System.Security.Cryptography.CryptographicException) { return ""; }
    }

    public static string Reveal(string stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal)) return "";
        try
        {
            var bytes = System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(Prefix.Length)), Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (System.Security.Cryptography.CryptographicException) { return ""; }
        catch (FormatException) { return ""; }
    }
}

internal sealed class SessionFile
{
    public string Path;
    public int Line, Col;
    public List<int> Breakpoints = new List<int>();
}

// Per-user settings and session in %APPDATA%\Gestirn dev\settings.ini (plain key=value lines).
internal sealed class AppSettings
{
    // GESTIRN_HOME overrides the location; a "data" folder next to the exe makes the install portable.
    private static string folder;
    public static string Folder { get { return folder ?? (folder = ResolveFolder()); } }

    private static string ResolveFolder()
    {
        string env = Environment.GetEnvironmentVariable("GESTIRN_HOME");
        if (!string.IsNullOrEmpty(env)) return env;
        string portable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        if (Directory.Exists(portable)) return portable;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gestirn dev");
    }

    public static string DocumentsFolder
    {
        get
        {
            string env = Environment.GetEnvironmentVariable("GESTIRN_HOME");
            string dir = !string.IsNullOrEmpty(env) ? Path.Combine(env, "Documents") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Gestirn dev");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
    public static string FilePath { get { return Path.Combine(Folder, "settings.ini"); } }
    public static string BackupFolder { get { return Path.Combine(Folder, "backup"); } }

    public string Language = "auto";
    public string CompilerPath = "";
    public string Standard = "auto";
    public string ExtraFlags = "-Wall";
    public string Optimization = "";
    public bool StaticLink;
    public string ExecCharset = "auto";

    public string FontName = "";
    public float FontSize = 11f;
    public int TabSize = 4;
    public bool InsertSpaces = true;
    public bool AutoSave = true;
    public int AutoSaveDelay = 1000;
    public bool AutoComplete = true;
    public bool AutoPairs = true;
    public bool RestoreSession = true;
    public bool FormatOnSave;
    public string Theme = "light";
    public bool Animations = true;
    public string TerminalShell = "auto";
    public bool TerminalInSidebar;
    public string ProgramArgs = "";
    public bool CheckUpdates = true;
    public bool RewardMode;                  // encouraging lines after each run
    public bool SimpleErrors = true;         // compiler errors in plain language
    public bool AiFixEnabled;                // experimental: AI fixes for compile errors
    public string AiBaseUrl = "https://api.openai.com/v1";
    public string AiModel = "gpt-4o-mini";
    public string AiKey = "";
    public int Version;                      // settings layout version, for one-off migrations
    public string SkippedVersion = "";

    public string Workspace = "";
    public string Project = "";
    public List<string> RecentFiles = new List<string>();
    public List<string> RecentFolders = new List<string>();

    public int SidebarWidth = 270, PanelHeight = 230;
    public bool SidebarVisible = true, PanelVisible;
    public string SidebarView = "explorer";
    public Rectangle WindowBounds = Rectangle.Empty;
    public bool Maximized;

    public List<SessionFile> Session = new List<SessionFile>();
    public string ActiveFile = "";
    public List<string> UntitledBackups = new List<string>();

    public bool UseChinese
    {
        get
        {
            if (Language == "zh") return true;
            if (Language == "en") return false;
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
        }
    }

    public static AppSettings Load()
    {
        var s = new AppSettings();
        try
        {
            if (!File.Exists(FilePath)) return s;
            foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                string key = raw.Substring(0, eq).Trim(), v = raw.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "Language": s.Language = v; break;
                    case "CompilerPath": s.CompilerPath = v; break;
                    case "Standard": s.Standard = v.Length > 0 ? v : "auto"; break;
                    case "ExtraFlags": s.ExtraFlags = v; break;
                    case "Optimization": s.Optimization = v; break;
                    case "StaticLink": s.StaticLink = v == "1"; break;
                    case "ExecCharset": s.ExecCharset = v; break;
                    case "FontName": s.FontName = v; break;
                    case "FontSize": float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out s.FontSize); break;
                    case "TabSize": int.TryParse(v, out s.TabSize); break;
                    case "InsertSpaces": s.InsertSpaces = v != "0"; break;
                    case "AutoSave": s.AutoSave = v != "0"; break;
                    case "AutoSaveDelay": int.TryParse(v, out s.AutoSaveDelay); break;
                    case "AutoComplete": s.AutoComplete = v != "0"; break;
                    case "AutoPairs": s.AutoPairs = v != "0"; break;
                    case "RestoreSession": s.RestoreSession = v != "0"; break;
                    case "FormatOnSave": s.FormatOnSave = v == "1"; break;
                    case "Theme": s.Theme = v.Length > 0 ? v : "auto"; break;
                    case "Animations": s.Animations = v != "0"; break;
                    case "TerminalShell": s.TerminalShell = v.Length > 0 ? v : "auto"; break;
                    case "TerminalInSidebar": s.TerminalInSidebar = v == "1"; break;
                    case "ProgramArgs": s.ProgramArgs = v; break;
                    case "CheckUpdates": s.CheckUpdates = v != "0"; break;
                    case "RewardMode": s.RewardMode = v == "1"; break;
                    case "SimpleErrors": s.SimpleErrors = v != "0"; break;
                    case "AiFixEnabled": s.AiFixEnabled = v == "1"; break;
                    case "AiBaseUrl": if (v.Length > 0) s.AiBaseUrl = v; break;
                    case "AiModel": if (v.Length > 0) s.AiModel = v; break;
                    case "AiKey": s.AiKey = Secret.Reveal(v); break;
                    case "SettingsVersion": int.TryParse(v, out s.Version); break;
                    case "SkippedVersion": s.SkippedVersion = v; break;
                    case "Workspace": s.Workspace = v; break;
                    case "Project": s.Project = v; break;
                    case "Recent": if (v.Length > 0) s.RecentFiles.Add(v); break;
                    case "RecentFolder": if (v.Length > 0) s.RecentFolders.Add(v); break;
                    case "SidebarWidth": int.TryParse(v, out s.SidebarWidth); break;
                    case "PanelHeight": int.TryParse(v, out s.PanelHeight); break;
                    case "SidebarVisible": s.SidebarVisible = v != "0"; break;
                    case "PanelVisible": s.PanelVisible = v != "0"; break;
                    case "SidebarView": s.SidebarView = v; break;
                    case "Window":
                        var parts = v.Split(',').Select(p => { int n; int.TryParse(p, out n); return n; }).ToArray();
                        if (parts.Length == 4) s.WindowBounds = new Rectangle(parts[0], parts[1], parts[2], parts[3]);
                        break;
                    case "Maximized": s.Maximized = v == "1"; break;
                    case "Open":
                        var f = v.Split('|');
                        var sf = new SessionFile { Path = f[0] };
                        if (f.Length > 1) int.TryParse(f[1], out sf.Line);
                        if (f.Length > 2) int.TryParse(f[2], out sf.Col);
                        if (f.Length > 3)
                            foreach (var b in f[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            { int n; if (int.TryParse(b, out n)) sf.Breakpoints.Add(n); }
                        s.Session.Add(sf);
                        break;
                    case "Active": s.ActiveFile = v; break;
                    case "Untitled": if (v.Length > 0) s.UntitledBackups.Add(v); break;
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        // 0.6 introduced the 70s light theme as the default look; people who had left the theme on "auto" get it.
        if (File.Exists(FilePath) && s.Version < 6)
        {
            if (s.Theme == "auto") s.Theme = "light";
            s.Version = 6;
        }
        if (s.FontSize < 7 || s.FontSize > 36) s.FontSize = 11f;
        if (s.TabSize < 1 || s.TabSize > 16) s.TabSize = 4;
        if (s.AutoSaveDelay < 200) s.AutoSaveDelay = 1000;
        return s;
    }

    public void Save()
    {
        var lines = new List<string>
        {
            "Language=" + Language, "CompilerPath=" + CompilerPath, "Standard=" + Standard, "ExtraFlags=" + ExtraFlags,
            "Optimization=" + Optimization, "StaticLink=" + (StaticLink ? "1" : "0"), "ExecCharset=" + ExecCharset,
            "FontName=" + FontName, "FontSize=" + FontSize.ToString(CultureInfo.InvariantCulture), "TabSize=" + TabSize,
            "InsertSpaces=" + (InsertSpaces ? "1" : "0"), "AutoSave=" + (AutoSave ? "1" : "0"), "AutoSaveDelay=" + AutoSaveDelay,
            "AutoComplete=" + (AutoComplete ? "1" : "0"), "AutoPairs=" + (AutoPairs ? "1" : "0"),
            "RestoreSession=" + (RestoreSession ? "1" : "0"), "FormatOnSave=" + (FormatOnSave ? "1" : "0"),
            "Theme=" + Theme, "Animations=" + (Animations ? "1" : "0"), "TerminalShell=" + TerminalShell,
            "TerminalInSidebar=" + (TerminalInSidebar ? "1" : "0"), "ProgramArgs=" + ProgramArgs,
            "CheckUpdates=" + (CheckUpdates ? "1" : "0"), "SkippedVersion=" + SkippedVersion,
            "RewardMode=" + (RewardMode ? "1" : "0"), "SimpleErrors=" + (SimpleErrors ? "1" : "0"),
            "AiFixEnabled=" + (AiFixEnabled ? "1" : "0"), "AiBaseUrl=" + AiBaseUrl, "AiModel=" + AiModel, "AiKey=" + Secret.Conceal(AiKey),
            "SettingsVersion=6",
            "Workspace=" + Workspace, "Project=" + Project,
            "SidebarWidth=" + SidebarWidth, "PanelHeight=" + PanelHeight, "SidebarVisible=" + (SidebarVisible ? "1" : "0"),
            "PanelVisible=" + (PanelVisible ? "1" : "0"), "SidebarView=" + SidebarView,
            "Window=" + WindowBounds.X + "," + WindowBounds.Y + "," + WindowBounds.Width + "," + WindowBounds.Height,
            "Maximized=" + (Maximized ? "1" : "0"), "Active=" + ActiveFile,
        };
        lines.AddRange(RecentFiles.Take(15).Select(r => "Recent=" + r));
        lines.AddRange(RecentFolders.Take(10).Select(r => "RecentFolder=" + r));
        lines.AddRange(Session.Select(f => "Open=" + f.Path + "|" + f.Line + "|" + f.Col + "|" + string.Join(",", f.Breakpoints)));
        lines.AddRange(UntitledBackups.Select(u => "Untitled=" + u));
        try
        {
            Directory.CreateDirectory(Folder);
            string temp = FilePath + ".tmp";
            File.WriteAllLines(temp, lines, Encoding.UTF8);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void AddRecent(List<string> list, string path, int max)
    {
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
    }
}
}
