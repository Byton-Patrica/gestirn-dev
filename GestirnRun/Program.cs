using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

// Gestirn dev console runner: starts the built program in this console window, then reports
// how long it ran and its return value and waits for a key, like Dev-C++'s ConsolePauser.
internal static class GestirnRun
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("usage: GestirnRun <program.exe> [arguments]");
            return 2;
        }
        bool zh = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
        string exe = args[0];
        string rest = string.Join(" ", args.Skip(1).Select(Quote));
        try { Console.Title = Path.GetFileName(exe); }
        catch (IOException) { }

        int code;
        var watch = Stopwatch.StartNew();
        try
        {
            var psi = new ProcessStartInfo(exe, rest) { UseShellExecute = false, WorkingDirectory = Directory.GetCurrentDirectory() };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit();
                code = p.ExitCode;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine((zh ? "无法启动程序：" : "Could not start the program: ") + ex.Message);
            code = -1;
        }
        watch.Stop();

        Console.ResetColor();
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("--------------------------------");
        Console.ForegroundColor = code == 0 ? ConsoleColor.Green : ConsoleColor.Yellow;
        string seconds = watch.Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture);
        Console.WriteLine(zh ? string.Format("程序已结束，用时 {0} 秒，返回值 {1}", seconds, code)
                             : string.Format("Process exited after {0} seconds with return value {1}", seconds, code));
        if (code == -1073741819) Console.WriteLine(zh ? "（0xC0000005：访问了无效内存，例如数组越界或空指针）" : "(0xC0000005: invalid memory access, e.g. out-of-range index or null pointer)");
        if (code == -1073741571) Console.WriteLine(zh ? "（0xC00000FD：栈溢出，常见于无限递归）" : "(0xC00000FD: stack overflow, often infinite recursion)");
        if (code == -1073741676) Console.WriteLine(zh ? "（0xC0000094：整数除以零）" : "(0xC0000094: integer division by zero)");
        Console.ResetColor();
        Console.Write(zh ? "请按任意键继续. . ." : "Press any key to continue . . .");
        try { Console.ReadKey(true); }
        catch (InvalidOperationException) { Console.Read(); }
        return code;
    }

    private static string Quote(string s) { return s.Contains(" ") ? "\"" + s + "\"" : s; }
}
