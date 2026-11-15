using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GestirnDev
{
internal sealed partial class MainForm
{
    private Timer profileTimer;

    // Runs a -pg build through the console runner, waits for gmon.out, then shows gprof's flat profile.
    private void StartProfiling(string exe, string compiler)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => StartProfiling(exe, compiler))); return; }
        string gprof = Profiler.FindGprof(compiler);
        if (gprof == null)
        {
            toasts.Show(L.T("gprof not found", "找不到 gprof"), L.T("Performance analysis needs gprof.exe next to g++.exe.", "性能分析需要 g++.exe 同目录下的 gprof.exe。"), ToastKind.Error, 5000);
            return;
        }
        string dir = Path.GetDirectoryName(exe);
        string gmon = Path.Combine(dir, "gmon.out");
        try { if (File.Exists(gmon)) File.Delete(gmon); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        Process proc;
        try { proc = Toolchain.RunProgram(exe, settings.ProgramArgs, dir); }
        catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
        {
            toasts.Show(L.T("Can't start the program", "无法启动程序"), ex.Message, ToastKind.Error);
            return;
        }
        programs.Add(proc);
        output.Append(L.T("Profiling ", "性能分析：运行 ") + Path.GetFileName(exe) + L.T(" — results appear when the program exits.", "，程序结束后显示结果。"), Theme.TextDim);
        profileView.Waiting = true;
        profileView.WaitingText = L.T("Profiling ", "正在分析 ") + Path.GetFileName(exe) + "…";
        profileView.Invalidate();
        ShowPanel("profile");
        status.Message = L.T("Profiling ", "性能分析中 ") + Path.GetFileName(exe);
        status.Busy = true;
        status.Invalidate();

        if (profileTimer != null) profileTimer.Dispose();
        var timer = profileTimer = new Timer { Interval = 300 };
        long lastLength = -1;
        DateTime exitedAt = DateTime.MaxValue;
        timer.Tick += async (_, _) =>
        {
            bool exited;
            try { exited = proc.HasExited; }
            catch (InvalidOperationException) { exited = true; }
            if (exited && exitedAt == DateTime.MaxValue) exitedAt = DateTime.Now;
            long length = -1;
            try { if (File.Exists(gmon)) length = new FileInfo(gmon).Length; }
            catch (IOException) { }
            bool stable = length > 0 && length == lastLength;
            lastLength = length;
            if (stable)
            {
                timer.Stop();
                timer.Dispose();
                if (profileTimer == timer) profileTimer = null;
                await FinishProfiling(gprof, exe, gmon);
                return;
            }
            if (exited && (DateTime.Now - exitedAt).TotalSeconds > 2)
            {
                timer.Stop();
                timer.Dispose();
                if (profileTimer == timer) profileTimer = null;
                profileView.Waiting = false;
                profileView.Invalidate();
                status.Busy = false;
                status.Message = L.T("Profiling stopped", "性能分析已中止");
                status.Invalidate();
                toasts.Show(L.T("No profile data", "没有性能数据"),
                    L.T("The program was stopped before it finished, so gmon.out was not written.", "程序在正常结束前被关闭，没有生成 gmon.out。"), ToastKind.Warning, 5000);
            }
        };
        timer.Start();
    }

    private async System.Threading.Tasks.Task FinishProfiling(string gprof, string exe, string gmon)
    {
        try
        {
            var report = await Profiler.Analyze(gprof, exe, gmon);
            if (InvokeRequired) { BeginInvoke(new Action(() => ShowProfile(report))); return; }
            ShowProfile(report);
        }
        catch (Exception ex) when (ex is IOException || ex is Win32Exception || ex is InvalidOperationException)
        {
            profileView.Waiting = false;
            toasts.Show(L.T("gprof failed", "gprof 运行失败"), ex.Message, ToastKind.Error);
            status.Busy = building;
            status.Invalidate();
        }
    }

    private void ShowProfile(ProfileReport report)
    {
        try
        {
            profileView.Set(report);
            ShowPanel("profile");
            output.Append("▸ gprof " + Path.GetFileName(report.Exe), Theme.Orange);
            output.AppendLog(report.Raw.TrimEnd());
            var top = profileView.Count > 0 ? report.Entries.FirstOrDefault(e => report.HasSamples ? e.Self > 0 : e.Calls > 0) : null;
            status.Message = L.T("Profile ready", "性能分析完成") + (top != null ? "  ·  " + top.Name + (report.HasSamples ? " " + top.Percent.ToString("0") + "%" : "") : "");
            status.Flash(Theme.Gold);
            if (top != null)
                toasts.Show(L.T("Performance analysis done", "性能分析完成"),
                    report.HasSamples
                        ? string.Format(L.T("Hottest: {0} ({1}% of the time)", "最耗时：{0}（占 {1}% 时间）"), top.Name, top.Percent.ToString("0"))
                        : L.T("The program finished too fast to measure time; call counts are shown.", "程序运行太快，未测到耗时，已显示调用次数。"),
                    ToastKind.Success, 4200);
        }
        finally
        {
            status.Busy = building;
            status.Invalidate();
        }
    }
}
}
