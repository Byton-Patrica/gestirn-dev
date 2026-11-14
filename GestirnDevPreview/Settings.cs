using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GestirnDevPreview
{
// Small key=value settings file kept per user in %APPDATA%\Gestirn dev\settings.ini.
internal sealed class AppSettings
{
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gestirn dev");
    public static readonly string FilePath = Path.Combine(Folder, "settings.ini");

    public string CompilerPath = "";
    public string Standard = "c++14";
    public string ExtraFlags = "-Wall";
    public string Workspace = "";

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (!File.Exists(FilePath)) return settings;
            foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "CompilerPath": settings.CompilerPath = value; break;
                    case "Standard": settings.Standard = value; break;
                    case "ExtraFlags": settings.ExtraFlags = value; break;
                    case "Workspace": settings.Workspace = value; break;
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(FilePath, new[]
            {
                "CompilerPath=" + CompilerPath,
                "Standard=" + Standard,
                "ExtraFlags=" + ExtraFlags,
                "Workspace=" + Workspace,
            }, Encoding.UTF8);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

// Finds a g++ the user can compile with: saved choice, PATH, then common installs (Dev-C++ and MSYS2).
internal static class CompilerLocator
{
    public static string Find(string saved)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(saved)) candidates.Add(saved);

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            var trimmed = dir.Trim().Trim('"');
            if (trimmed.Length > 0) candidates.Add(Path.Combine(trimmed, "g++.exe"));
        }

        foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles") })
        {
            if (!string.IsNullOrEmpty(root)) candidates.Add(Path.Combine(root, "Dev-Cpp", "MinGW64", "bin", "g++.exe"));
        }
        candidates.Add(@"C:\msys64\ucrt64\bin\g++.exe");
        candidates.Add(@"C:\msys64\mingw64\bin\g++.exe");

        foreach (var candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
        }
        return null;
    }
}
}
