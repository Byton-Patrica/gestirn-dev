using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GestirnDev
{
// Learning aids: the reward mode and the experimental AI fix.
internal sealed partial class MainForm
{
    // ------------------------------------------------------------------ reward mode

    // Shown after each run, in turn.
    internal static readonly string[] Cheers =
    {
        "Nice! Your code is running. That's real progress.",
        "Every run teaches you something. Keep going!",
        "Look at you, building things. Proud of this one.",
        "You're getting sharper with every line you write.",
        "Great job hitting run. Curiosity wins again.",
        "Small steps, big skills. This is how experts start.",
        "Another run, another lesson learned. Well done!",
        "Your future self is already thanking you.",
        "Coding is a marathon, and you are right on pace.",
        "Look at that: an idea of yours is alive on the screen!",
    };

    // Shown when a build finally succeeds after it failed, in turn.
    internal static readonly string[] Triumphs =
    {
        "YES!!! You broke it, you fixed it, and now it RUNS! That's exactly how real programmers win!",
        "INCREDIBLE comeback! The errors tried to stop you, and you crushed them!",
        "BOOM! Bug defeated! Persistence is your superpower, and everyone can see it!",
        "UNSTOPPABLE! You turned a wall of red into a working program. Legendary!",
        "THAT is the moment every great coder lives for: it works, and you earned every bit of it!",
    };

    private int cheerIndex = Environment.TickCount % Cheers.Length;
    private int triumphIndex = Environment.TickCount % Triumphs.Length;
    private bool failedSinceSuccess;
    private Timer rewardTimer;

    public string NextCheer() { return Cheers[cheerIndex++ % Cheers.Length]; }
    public string NextTriumph() { return Triumphs[triumphIndex++ % Triumphs.Length]; }

    private void RewardBuildFailed() { failedSinceSuccess = true; }

    // Called when a build succeeds. Returns true when a triumphant message was shown (it replaces the normal one).
    private bool RewardBuildSucceeded()
    {
        bool comeback = failedSinceSuccess;
        failedSinceSuccess = false;
        if (!settings.RewardMode || !comeback) return false;
        ShowReward(NextTriumph(), true);
        return true;
    }

    private void RewardOnRun()
    {
        if (settings.RewardMode) ShowReward(NextCheer(), false);
    }

    // The line goes on the status bar (it stays for a while) and into the Output panel.
    internal void ShowReward(string text, bool big)
    {
        status.Set("reward", text, "spark", big ? Theme.Orange : Theme.Gold, false);
        status.Flash(big ? Theme.Orange : Theme.Gold);
        output.Append((big ? "★★★ " : "★ ") + text, big ? Theme.Orange : Theme.Gold);
        if (big)
        {
            if (runBtn != null && runBtn.Visible) topBar.Burst(new Point(runBtn.Left + runBtn.Width / 2, runBtn.Top + runBtn.Height / 2));
            toasts.Show(L.T("You did it!", "你做到了！"), text, ToastKind.Success, 6000);
        }
        if (rewardTimer != null) rewardTimer.Dispose();
        rewardTimer = new Timer { Interval = big ? 16000 : 9000 };
        rewardTimer.Tick += (_, _) => { rewardTimer.Stop(); status.Remove("reward"); };
        rewardTimer.Start();
    }

    // ------------------------------------------------------------------ AI fix (experimental)

    private bool aiBusy;

    private void RefreshAiButton(int errors)
    {
        if (errors > 0 && AiFix.Configured(settings)) status.Set("aifix", L.T("AI fix", "AI 纠错"), "spark", Theme.Orange, false);
        else status.Remove("aifix");
    }

    private async void AiFixNow()
    {
        if (aiBusy) return;
        if (!AiFix.Configured(settings))
        {
            toasts.Show(L.T("AI fix isn't set up", "AI 纠错还没有设置"), L.T("Turn it on and enter an API key in Settings > Easy help > AI fix.", "请在 设置 > 简易辅助 > AI 纠错 中开启并填写 API Key。"), ToastKind.Info, 4000);
            OpenSettings("assist");
            return;
        }
        if (active == null || !active.IsSource)
        {
            toasts.Show(L.T("Open a source file first", "请先打开源文件"), null, ToastKind.Info);
            return;
        }
        var problemsHere = lastDiagnostics.Where(d => d.Severity == Severity.Error).ToList();
        if (problemsHere.Count == 0)
        {
            toasts.Show(L.T("No errors to fix", "当前没有报错"), L.T("Compile first (F9); the button appears when there are errors.", "先编译（F9）；有报错时才需要 AI 纠错。"), ToastKind.Info);
            return;
        }
        string messages = string.Join("\n", lastDiagnostics.Where(d => d.Severity != Severity.Note).Take(30).Select(d =>
            (d.File != null ? Path.GetFileName(d.File) : "") + (d.Line >= 0 ? ":" + (d.Line + 1) : "") + ": " + (d.Severity == Severity.Error ? "error: " : "warning: ") + d.Message));
        var doc = active;
        string original = doc.Editor.Text;
        aiBusy = true;
        status.Busy = true;
        status.Message = L.T("AI is looking at your code…", "AI 正在分析你的代码…");
        status.Invalidate();
        toasts.Show(L.T("Asking the AI…", "正在请求 AI…"), L.T("Sending this file and the compiler messages to ", "正在把当前文件和报错发送到 ") + AiFix.HostOf(settings.AiBaseUrl), ToastKind.Info, 3000);
        AiFix.Result r;
        try { r = await AiFix.Ask(settings, doc.Path != null ? Path.GetFileName(doc.Path) : doc.Title, original, messages); }
        finally
        {
            aiBusy = false;
            status.Busy = building;
            status.Message = "";
            status.Invalidate();
        }
        if (IsDisposed || closing) return;
        if (!r.Ok)
        {
            toasts.Show(L.T("AI fix failed", "AI 纠错失败"), r.Error, ToastKind.Error, 7000);
            return;
        }
        using (var dialog = new AiFixDialog(r, original, AiFix.HostOf(settings.AiBaseUrl)))
        {
            dialog.ShowDialog(this);
            if (dialog.Applied && r.Code != null && docs.Contains(doc))
            {
                doc.Editor.ReplaceAllText(r.Code);
                Activate(doc);
                toasts.Show(L.T("Fix applied", "已应用修复"), L.T("Press F9 to compile again. Ctrl+Z undoes it.", "按 F9 重新编译；Ctrl+Z 可撤销。"), ToastKind.Success, 4500);
            }
        }
    }

    // Used by the Settings page to test the connection.
    internal async System.Threading.Tasks.Task<string> TestAiConnection(AppSettings candidate)
    {
        var r = await AiFix.Ping(candidate);
        if (r.Ok) return L.T("Connected. The model answered: ", "连接成功，模型回复：") + (r.Explanation ?? "").Trim();
        return r.Error;
    }
}
}
