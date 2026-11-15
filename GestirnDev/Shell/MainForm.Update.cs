using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GestirnDev
{
internal sealed partial class MainForm
{
    private UpdateCard updateCard;
    private CancellationTokenSource updateCancel;
    private string downloadedUpdate;
    private ReleaseInfo availableRelease;

    // A few seconds after start (so it never slows the launch), quietly asks GitHub for the latest release.
    private void ScheduleUpdateCheck()
    {
        if (!settings.CheckUpdates || Environment.GetEnvironmentVariable("GESTIRN_NO_UPDATE_CHECK") == "1") return;
        var t = new System.Windows.Forms.Timer { Interval = 2500 };
        t.Tick += async (_, _) =>
        {
            t.Stop();
            t.Dispose();
            await CheckForUpdatesCore(false);
        };
        t.Start();
    }

    private async void CheckForUpdates(bool manual) { await CheckForUpdatesCore(manual); }

    private async Task<string> CheckForUpdatesFromSettings()
    {
        var r = await Updater.FetchLatest();
        if (r == null) return L.T("Couldn't reach GitHub. Check your connection.", "无法连接 GitHub，请检查网络。");
        if (!Updater.IsNewer(r.Version, Version)) return L.T("You have the latest version (", "已是最新版本（") + Version + L.T(").", "）。");
        BeginInvoke(new Action(() => ShowUpdateCard(r)));
        return L.T("Version ", "发现新版本 ") + r.Version + L.T(" is available — see the card in the main window.", "，更新提示已显示在主窗口。");
    }

    private async Task CheckForUpdatesCore(bool manual)
    {
        if (manual) { status.Message = L.T("Checking for updates…", "正在检查更新…"); status.Invalidate(); }
        var r = await Updater.FetchLatest();
        if (IsDisposed || closing) return;
        if (r == null)
        {
            if (manual) toasts.Show(L.T("Can't check for updates", "无法检查更新"), L.T("GitHub could not be reached. Check your internet connection.", "无法连接到 GitHub，请检查网络连接。"), ToastKind.Warning, 4000);
            return;
        }
        if (!Updater.IsNewer(r.Version, Version))
        {
            if (manual)
            {
                toasts.Show(L.T("You're up to date", "已是最新版本"), "Gestirn dev " + Version, ToastKind.Success, 2600);
                status.Message = L.T("Up to date", "已是最新版本");
                status.Invalidate();
            }
            return;
        }
        if (!manual && string.Equals(settings.SkippedVersion, r.Version, StringComparison.OrdinalIgnoreCase)) return;
        ShowUpdateCard(r);
    }

    internal void ShowUpdateCard(ReleaseInfo r)
    {
        if (updateCard != null && !updateCard.IsDisposed)
        {
            if (updateCard.Release.Version == r.Version) return;
            RemoveUpdateCard(false);
        }
        availableRelease = r;
        var card = updateCard = new UpdateCard(r, Version);
        card.UpdateRequested += () => BeginUpdate(card);
        card.RestartRequested += () => RestartIntoUpdate(card);
        card.PageRequested += () => OpenUrl(r.PageUrl ?? Updater.ReleasesUrl);
        card.SkipRequested += () =>
        {
            settings.SkippedVersion = r.Version;
            settings.Save();
            RemoveUpdateCard(true);
            toasts.Show(L.T("Skipped ", "已跳过 ") + r.Version, L.T("You'll be told again when a newer version comes out. Help → Check for Updates any time.", "有更新的版本时会再提醒；随时可在 帮助 → 检查更新 中查看。"), ToastKind.Info, 3600);
        };
        card.CancelRequested += () => { if (updateCancel != null) updateCancel.Cancel(); };
        card.Dismissed += () => RemoveUpdateCard(true);
        Controls.Add(card);
        card.BringToFront();
        card.Appear.Changed += PlaceUpdateCard;
        Resize += OnResizeUpdateCard;
        PlaceUpdateCard();
        card.Appear.To(1f, 620, Ease.OutBack, 60);
        status.Set("update", L.T("Update ", "新版本 ") + r.Version, "update", Theme.Lime, true);
    }

    private void OnResizeUpdateCard(object sender, EventArgs e) { PlaceUpdateCard(); }

    private void PlaceUpdateCard()
    {
        var card = updateCard;
        if (card == null || card.IsDisposed) return;
        float v = card.Appear.Value;
        int x = ClientSize.Width - card.Width - Ui.D(18) + (int)((1f - v) * (card.Width + Ui.D(40)));
        int y = topBar.Bottom + Ui.D(14);
        card.Location = new Point(x, y);
    }

    private void RemoveUpdateCard(bool animate)
    {
        var card = updateCard;
        if (card == null || card.Closing) return;
        card.Closing = true;
        updateCard = null;
        Resize -= OnResizeUpdateCard;
        Action remove = () => { Controls.Remove(card); card.Dispose(); };
        if (animate && !Animator.Reduced)
        {
            card.Appear.Finished += remove;
            card.Appear.To(0f, 300, Ease.InOutCubic);
        }
        else remove();
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(url); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private async void BeginUpdate(UpdateCard card)
    {
        var r = card.Release;
        string url = Updater.PackageFor(r);
        if (url == null || !Updater.CanWriteAppDir())
        {
            // No package to install in place (or the folder is read-only): let the browser download it.
            OpenUrl(url ?? r.PageUrl ?? Updater.ReleasesUrl);
            return;
        }
        if (updateCancel != null) updateCancel.Dispose();
        var cancel = updateCancel = new CancellationTokenSource();
        card.SetStage(UpdateCard.Stage.Downloading);
        long total = url == r.FullZip ? r.FullSize : r.LiteSize;
        var progress = new Progress<double>(f => { if (!card.IsDisposed) card.SetProgress(f, (long)(f * total), total); });
        try
        {
            downloadedUpdate = await Updater.Download(url, progress, cancel.Token);
            if (card.IsDisposed) return;
            card.SetStage(UpdateCard.Stage.Ready);
            status.Flash(Theme.Lime);
        }
        catch (Exception ex) when (ex is WebException || ex is IOException || ex is OperationCanceledException || ex is UnauthorizedAccessException)
        {
            if (card.IsDisposed) return;
            bool cancelled = cancel.IsCancellationRequested;
            card.SetStage(UpdateCard.Stage.Failed, cancelled ? L.T("Download cancelled.", "已取消下载。") : L.T("Download failed: ", "下载失败：") + ex.Message);
        }
    }

    private void RestartIntoUpdate(UpdateCard card)
    {
        if (downloadedUpdate == null || !File.Exists(downloadedUpdate)) { card.SetStage(UpdateCard.Stage.Offer); return; }
        try
        {
            Updater.InstallAndRestart(downloadedUpdate);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
        {
            card.SetStage(UpdateCard.Stage.Failed, ex.Message);
            return;
        }
        Close();
    }
}
}
