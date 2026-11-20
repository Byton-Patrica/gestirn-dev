using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Gestirn dev")]
[assembly: AssemblyProduct("Gestirn dev")]
[assembly: AssemblyDescription("A modern, lightweight C/C++ IDE built from the Dev-C++ source line")]
[assembly: AssemblyCopyright("GPL-2.0; based on Bloodshed/Orwell Dev-C++")]
[assembly: AssemblyVersion("0.61.0.0")]
[assembly: AssemblyFileVersion("0.61.0.0")]
[assembly: AssemblyInformationalVersion("0.61")]

namespace GestirnDev
{
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try { Native.SetProcessDPIAware(); }
        catch (EntryPointNotFoundException) { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Ui.Init();
        var settings = AppSettings.Load();
        L.Zh = settings.UseChinese;
        Theme.Apply(MainForm.ResolveLight(settings.Theme));
        Animator.Reduced = !settings.Animations;
        Hints.Mode = Hints.ModeFrom(settings.ErrorTranslation);
        Theme.InitFonts(settings.FontName, settings.FontSize);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Report(e.Exception, false);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception, true);

        SplashForm splash = null;
        if (settings.Animations && Environment.GetEnvironmentVariable("GESTIRN_NO_SPLASH") != "1")
        {
            splash = new SplashForm(MainForm.Version);
            splash.Show();
            while (!splash.Assembled && !splash.IsDisposed)
            {
                Application.DoEvents();
                Thread.Sleep(8);
            }
        }
        var form = new MainForm(settings, args);
        if (splash != null)
            form.Shown += (_, _) => { if (!splash.IsDisposed) splash.FadeOut(); };
        Application.Run(form);
    }

    // Unexpected errors never take unsaved work with them: buffers are backed up first.
    private static void Report(Exception ex, bool fatal)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.AppendAllText(Path.Combine(AppSettings.Folder, "crash.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + Environment.NewLine + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (MainForm.Instance != null) MainForm.Instance.EmergencyBackup();
        MessageBox.Show(
            L.T("Something went wrong, but your work has been backed up.\n\n", "出现了一个错误，但你的工作已备份。\n\n") + (ex != null ? ex.Message : "") +
            L.T("\n\nDetails were written to crash.log in the settings folder.", "\n\n详细信息已写入设置文件夹中的 crash.log。"),
            "Gestirn dev", MessageBoxButtons.OK, fatal ? MessageBoxIcon.Error : MessageBoxIcon.Warning);
    }
}
}
