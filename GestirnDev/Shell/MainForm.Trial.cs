using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GestirnDev
{
// The advice shown before a trial run while auto save is on.
internal sealed class TrialNoticeDialog : DarkDialog
{
    public enum Choice { Cancel, Run, DisableAutoSaveAndRun }

    public Choice Result { get; private set; }
    public bool DontShowAgain { get { return dontShow.Checked; } }
    private readonly ToggleSwitch dontShow = new ToggleSwitch();

    public TrialNoticeDialog()
    {
        Text = L.T("Trial run", "试运行");
        ClientSize = Ui.D(540, 300);
        int pad = Ui.D(26);
        AddLabel(L.T("Run the code you see, without saving it", "运行你眼前的代码，不用保存文件"), pad, Ui.D(22), ClientSize.Width - pad * 2, Theme.UiTitle, Theme.Text).Height = Ui.D(30);
        var body = new Label
        {
            Bounds = new Rectangle(pad, Ui.D(64), ClientSize.Width - pad * 2, Ui.D(112)), ForeColor = Theme.TextDim, BackColor = BackColor, Font = Theme.Ui10,
            Text = L.T("A trial run compiles what is in the editor right now and runs it. Nothing is written to your file.\n\nIt works best with Auto save turned off: while Auto save is on, your changes are saved a moment after you stop typing, and then a trial run is no different from a normal run.",
                       "试运行会把编辑器里现在的代码编译并运行，不会写入你的文件。\n\n推荐在关闭“自动保存”后使用：自动保存开着时，你停下来片刻改动就会被存进文件，试运行也就和普通运行没有区别了。"),
        };
        Controls.Add(body);
        dontShow.Location = new Point(pad, Ui.D(190));
        Controls.Add(dontShow);
        AddLabel(L.T("Don't show this again", "不再显示"), pad + Ui.D(52), Ui.D(190), Ui.D(220), Theme.Ui10, Theme.Text).Height = Ui.D(24);

        var off = AddButton(L.T("Turn off auto save & run", "关闭自动保存并试运行"), ButtonKind.Primary, pad, Ui.D(238), Ui.D(200));
        var run = AddButton(L.T("Run anyway", "直接试运行"), ButtonKind.Secondary, pad + Ui.D(210), Ui.D(238), Ui.D(140));
        var cancel = AddButton(L.T("Cancel", "取消"), ButtonKind.Ghost, pad + Ui.D(360), Ui.D(238), Ui.D(100));
        off.Click += (_, _) => { Result = Choice.DisableAutoSaveAndRun; DialogResult = DialogResult.OK; };
        run.Click += (_, _) => { Result = Choice.Run; DialogResult = DialogResult.OK; };
        cancel.Click += (_, _) => { Result = Choice.Cancel; DialogResult = DialogResult.Cancel; };
    }
}

internal sealed partial class MainForm
{
    private bool trialBusy;

    // Compiles the text in the editor (saved or not) and runs it; the file on disk is never written.
    private async void TrialRun()
    {
        if (building || trialBusy) { status.Message = L.T("A build is already running", "正在编译中"); status.Invalidate(); return; }
        if (active == null || !active.IsSource)
        {
            toasts.Show(L.T("Open a source file first", "请先打开源文件"), L.T("Trial run needs a .c or .cpp file in the editor.", "试运行需要编辑器里有一个 .c 或 .cpp 文件。"), ToastKind.Info);
            return;
        }
        if (project != null && active.Path != null && project.Contains(active.Path) && project.Units.Count(u => u.Compile && DevProject.IsSource(u.FileName)) > 1)
        {
            toasts.Show(L.T("Not for multi-file projects (yet)", "多文件项目暂不支持试运行"),
                L.T("Trial run runs one file on its own. Use Run (F11) for this project.", "试运行只运行单个文件。这个项目请用“运行”(F11)。"), ToastKind.Info, 5000);
            return;
        }
        string compiler = Toolchain.FindCompiler(settings.CompilerPath);
        if (compiler == null) { CheckCompiler(); OpenSettings(); return; }

        if (settings.AutoSave && !settings.TrialNoticeHidden)
        {
            using (var dialog = new TrialNoticeDialog())
            {
                dialog.ShowDialog(this);
                if (dialog.Result == TrialNoticeDialog.Choice.Cancel) return;
                if (dialog.DontShowAgain) settings.TrialNoticeHidden = true;
                if (dialog.Result == TrialNoticeDialog.Choice.DisableAutoSaveAndRun)
                {
                    settings.AutoSave = false;
                    autosaveTimer.Stop();
                    toasts.Show(L.T("Auto save is off", "自动保存已关闭"), L.T("You can turn it back on in Settings > Files.", "可在 设置 > 文件 中重新打开。"), ToastKind.Info, 3000);
                }
                settings.Save();
            }
        }

        var doc = active;
        string text = doc.Editor.Text;
        string workDir = doc.Path != null ? Path.GetDirectoryName(doc.Path) : Path.Combine(DefaultDocumentsFolder(), "scratch");
        Directory.CreateDirectory(workDir);
        if (gdb != null && gdb.Alive) gdb.Stop();

        trialBusy = building = true;
        stopRequested = false;
        trialBtn.Busy = true;
        status.Busy = true;
        topBar.Busy = true;
        string name = doc.Path != null ? Path.GetFileName(doc.Path) : doc.Title;
        status.Message = L.T("Trial-compiling ", "试运行编译 ") + name + "…";
        status.Invalidate();
        output.Clear();
        var opts = Options(compiler, false);
        output.Append("▸ " + L.T("Trial run of ", "试运行 ") + name + L.T(" (unsaved code, the file is not touched)", "（使用编辑器里的代码，不写入文件）") + "   [" + opts.Standard + ", GCC " + Toolchain.Version(compiler) + "]", Theme.Orange);
        string keepDir = null;
        try
        {
            var result = await Toolchain.BuildTrial(doc.Path, text, workDir, opts, p => { lock (buildProcs) buildProcs.Add(p); });
            keepDir = result.TempDir;
            output.AppendLog(result.Log.ToString());
            ApplyDiagnostics(result.Diagnostics);
            int errors = result.Diagnostics.Count(d => d.Severity == Severity.Error);
            int warnings = result.Diagnostics.Count(d => d.Severity == Severity.Warning);
            if (stopRequested)
            {
                output.Append(L.T("Build stopped.", "编译已停止。"), Theme.Muted);
                status.Message = L.T("Build stopped", "编译已停止");
            }
            else if (result.Success)
            {
                string secs = result.Elapsed.TotalSeconds.ToString("0.00");
                output.Append(L.T("Trial build succeeded", "试运行编译成功") + "  (" + secs + "s)", Theme.Lime);
                status.Flash(Theme.Lime);
                topBar.Burst(new Point(trialBtn.Left + trialBtn.Width / 2, trialBtn.Top + trialBtn.Height / 2));
                bool triumph = RewardBuildSucceeded();
                string tempDir = result.TempDir;
                string note = L.T("[Trial run] This is the code in the editor, which may not be saved yet.", "[试运行] 运行的是编辑器里的代码，它可能还没有保存。");
                Launch(result.ExePath, !triumph, workDir, note, () => Toolchain.DeleteTrialDir(tempDir));
                keepDir = null;
                if (warnings > 0) status.Message = L.T("Trial run", "试运行中") + "  ·  " + warnings + L.T(" warnings", " 个警告");
            }
            else
            {
                RewardBuildFailed();
                output.Append(L.T("Build failed.", "编译失败。"), Theme.Red);
                status.Message = string.Format(L.T("Build failed · {0} errors", "编译失败 · {0} 个错误"), Math.Max(errors, 1));
                status.Flash(Theme.Red);
                status.Shake();
                var first = result.Diagnostics.FirstOrDefault(d => d.Severity == Severity.Error);
                toasts.Show(string.Format(L.T("Build failed · {0} errors", "编译失败 · {0} 个错误"), Math.Max(errors, 1)),
                    first != null ? (first.Headline + (first.Hint != null ? "\n" + first.Hint : "")) : L.T("See the Output panel for details.", "详情见“输出”面板。"), ToastKind.Error, 5000);
                ShowPanel(result.Diagnostics.Count > 0 ? "problems" : "output");
                if (first != null && first.File != null && first.Line >= 0 && File.Exists(first.File)) GoToDiagnostic(first);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is Win32Exception || ex is UnauthorizedAccessException)
        {
            output.Append("Gestirn dev: " + ex.Message, Theme.Red);
            toasts.Show(L.T("Build error", "编译出错"), ex.Message, ToastKind.Error);
        }
        finally
        {
            Toolchain.DeleteTrialDir(keepDir);
            lock (buildProcs) buildProcs.Clear();
            trialBusy = building = false;
            trialBtn.Busy = false;
            status.Busy = false;
            topBar.Busy = false;
            status.Invalidate();
        }
    }
}
}
