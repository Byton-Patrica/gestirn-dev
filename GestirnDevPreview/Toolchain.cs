using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GestirnDevPreview
{
internal sealed class BuildResult
{
    public int ExitCode;
    public string Output = "";
    public string ExePath = "";
}

// Compiles and runs C/C++ with a GCC toolchain (MinGW g++/gcc).
internal static class Toolchain
{
    // g++ diagnostics look like: main.cpp:12:5: error: ...
    public static readonly Regex DiagnosticPattern = new Regex(
        @"^(?<file>.+?):(?<line>\d+):(?:(?<col>\d+):)?\s*(?<kind>fatal error|error|warning|note):",
        RegexOptions.Compiled);

    // Console output of the compiler and shell follows the system ANSI code page (e.g. GBK on Chinese Windows).
    public static Encoding ConsoleEncoding
    {
        get { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage); }
    }

    public static async Task<BuildResult> CompileAsync(string compiler, string sourcePath, string standard, string extraFlags, Action<Process> started)
    {
        string dir = Path.GetDirectoryName(sourcePath);
        string fileName = Path.GetFileName(sourcePath);
        string exeName = Path.GetFileNameWithoutExtension(sourcePath) + ".exe";
        string exePath = Path.Combine(dir, exeName);
        bool isC = string.Equals(Path.GetExtension(sourcePath), ".c", StringComparison.OrdinalIgnoreCase);

        string tool = compiler;
        string args;
        if (isC)
        {
            string gcc = Path.Combine(Path.GetDirectoryName(compiler), "gcc.exe");
            bool hasGcc = File.Exists(gcc);
            if (hasGcc) tool = gcc;
            args = (hasGcc ? "" : "-x c ") + "-std=c11 -Wall -g \"" + fileName + "\" -o \"" + exeName + "\"";
        }
        else
        {
            args = "-std=" + standard + " " + extraFlags + " -g \"" + fileName + "\" -o \"" + exeName + "\"";
        }

        // Remove the old binary first, so a failed build cannot leave a stale program behind.
        try
        {
            if (File.Exists(exePath)) File.Delete(exePath);
        }
        catch (IOException)
        {
            throw new IOException(exeName + " is still in use. Close the running program and build again.");
        }

        var startInfo = new ProcessStartInfo(tool, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = dir,
            StandardOutputEncoding = ConsoleEncoding,
            StandardErrorEncoding = ConsoleEncoding,
        };
        startInfo.EnvironmentVariables["PATH"] = Path.GetDirectoryName(tool) + ";" + Environment.GetEnvironmentVariable("PATH");

        using (var process = new Process { StartInfo = startInfo })
        {
            process.Start();
            started(process);
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            await Task.Run(() => process.WaitForExit());
            string outText = await stdout;
            string errText = await stderr;
            return new BuildResult
            {
                ExitCode = process.ExitCode,
                Output = outText + errText,
                ExePath = exePath,
            };
        }
    }

    // Starts the built program in its own console window, so interactive input and output work.
    public static Process RunProgram(string exePath)
    {
        var startInfo = new ProcessStartInfo("cmd.exe", "/c \"\"" + exePath + "\" & echo. & pause\"")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath),
        };
        return Process.Start(startInfo);
    }

    public static void KillTree(Process process)
    {
        try
        {
            if (process == null || process.HasExited) return;
            var killer = Process.Start(new ProcessStartInfo("taskkill", "/PID " + process.Id + " /T /F")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            killer.WaitForExit(3000);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
}
