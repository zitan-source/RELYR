using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using ContextMenu = System.Windows.Controls.ContextMenu;
using ListBox = System.Windows.Controls.ListBox;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBox = System.Windows.Controls.TextBox;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfMessageBox = RELYR.AppDialog;

namespace RELYR;

/// <summary>
/// Update-check, download, verification, and installer-launch UI for <see cref="MainWindow" />.
/// </summary>
public partial class MainWindow
{
    void ApplyUpdateCheckPreference(bool previousSetting)
    {
        if (!config.CheckForUpdates)
        {
            availableUpdate = null;
            UpdateBanner.Visibility = Visibility.Collapsed;
            return;
        }
        EnsureUpdateCheckStarted(!previousSetting);
    }
    void EnsureUpdateCheckStarted(bool force = false)
    {
        if (!config.CheckForUpdates || !IsLoaded || !IsVisible)
            return;
        var now = DateTimeOffset.UtcNow;
        if (!force && !IsAutomaticUpdateCheckDue(now, lastAutomaticUpdateCheckAttempt, config.LastUpdateCheckUtcTicks))
            return;
        lastAutomaticUpdateCheckAttempt = now;
        _ = CheckForUpdatesAsync();
    }
    internal static bool IsAutomaticUpdateCheckDue(DateTimeOffset now, DateTimeOffset lastAttempt, long lastSuccessfulUtcTicks)
    {
        DateTimeOffset last = lastAttempt;
        if (lastSuccessfulUtcTicks > 0)
            try
            {
                var successful = new DateTimeOffset(lastSuccessfulUtcTicks, TimeSpan.Zero);
                if (successful > last)
                    last = successful;
            }
            catch (ArgumentOutOfRangeException) { }
        if (last == default)
            return true;
        TimeSpan elapsed = now - last;
        return elapsed < TimeSpan.Zero || elapsed >= AutomaticUpdateCheckInterval;
    }
    async Task CheckForUpdatesAsync()
    {
        try
        {
            await CheckForUpdatesNowAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }
    internal Task<UpdateCheckResult> CheckForUpdatesNowAsync()
    {
        if (runningUpdateCheckTask is { IsCompleted: false })
            return runningUpdateCheckTask;
        runningUpdateCheckTask = RunUpdateCheckAsync();
        return runningUpdateCheckTask;
    }
    async Task<UpdateCheckResult> RunUpdateCheckAsync()
    {
        var result = await UpdateService.CheckLatestAsync(RunningVersion, updateCancellation.Token);
        ApplyUpdateCheckResult(result);
        return result;
    }
    void ApplyUpdateCheckResult(UpdateCheckResult result)
    {
        lastUpdateCheck = result;
        availableUpdate = result.AvailableUpdate;
        if (availableUpdate == null)
            UpdateBanner.Visibility = Visibility.Collapsed;
        else
            ShowUpdateAvailable(availableUpdate);
        config.LastUpdateCheckUtcTicks = result.CheckedAt.UtcTicks;
        appliedConfig.LastUpdateCheckUtcTicks = config.LastUpdateCheckUtcTicks;
        try
        {
            var persisted = store.Load();
            persisted.LastUpdateCheckUtcTicks = config.LastUpdateCheckUtcTicks;
            store.Save(persisted);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        UpdateCheckCompleted?.Invoke(result);
    }
    internal void SetAvailableUpdate(UpdateInfo? update)
    {
        availableUpdate = update;
        UpdateBannerProgress.Visibility = Visibility.Collapsed;
        UpdateBannerProgress.Value = 0;
        if (update == null)
        {
            UpdateBanner.Visibility = Visibility.Collapsed;
            return;
        }
        UpdateBannerText.Text = $"新しいバージョンが利用可能です（v{update.VersionText}）";
        UpdateAvailableButton.Content = "今すぐ更新";
        UpdateAvailableButton.IsEnabled = true;
        UpdateDismissButton.IsEnabled = true;
        UpdateBanner.Visibility = string.Equals(config.DismissedUpdateVersion, update.VersionText, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
    void ShowUpdateAvailable(UpdateInfo update) => SetAvailableUpdate(update);
    internal void ShowUpdateAvailableForTest(UpdateInfo update) => ShowUpdateAvailable(update);
    internal void DismissAvailableUpdateForTest() => DismissCurrentUpdate();
    void UpdateDismiss_Click(object sender, RoutedEventArgs e) => DismissCurrentUpdate();
    void DismissCurrentUpdate()
    {
        // 未保存のキー割り当てには触れず、閉じたリリース番号だけを直ちに永続化する。
        if (updateInProgress || availableUpdate is not { } update)
            return;
        config.DismissedUpdateVersion = update.VersionText;
        appliedConfig.DismissedUpdateVersion = update.VersionText;
        UpdateBanner.Visibility = Visibility.Collapsed;
        try
        {
            var persisted = store.Load();
            persisted.DismissedUpdateVersion = update.VersionText;
            store.Save(persisted);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    async void UpdateAvailable_Click(object sender, RoutedEventArgs e)
    {
        if (updateInProgress || availableUpdate is not { } update)
            return;
        if (WpfMessageBox.Show(this, $"RELYR v{update.VersionText} をダウンロードして更新します。\n\n更新ファイルはSHA-256で検証してから実行します。続行しますか？", "RELYRをアップデート", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
            return;
        await InstallUpdateAsync(this, update);
    }
    internal async Task<bool> InstallUpdateAsync(Window owner, UpdateInfo update, Action<string>? reportProgress = null, IProgress<UpdateDownloadProgress>? downloadProgress = null)
    {
        if (updateInProgress)
            return false;
        updateInProgress = true;
        UpdateAvailableButton.IsEnabled = false;
        UpdateDismissButton.IsEnabled = false;
        UpdateAvailableButton.Content = "ダウンロード中…";
        UpdateBannerProgress.Value = 0;
        UpdateBannerProgress.Visibility = Visibility.Visible;
        reportProgress?.Invoke("アップデートをダウンロードしています…");
        try
        {
            var footerProgress = new Progress<UpdateDownloadProgress>(value =>
            {
                if (value.Percentage is { } percentage)
                {
                    UpdateAvailableButton.Content = $"ダウンロード中… {percentage:0}%";
                    UpdateBannerProgress.Value = percentage;
                }
                downloadProgress?.Report(value);
            });
            string installer = await UpdateService.DownloadAndVerifyAsync(update, updateCancellation.Token, footerProgress);
            UpdateBannerProgress.Value = 100;
            UpdateAvailableButton.Content = "更新準備完了";
            reportProgress?.Invoke("ダウンロードと安全性の検証が完了しました。");
            var confirm = AppDialog.Show(owner,
                $"RELYR v{update.VersionText} の準備ができました。\n\nRELYRを終了してアップデートし、完了後にメイン画面を開きます。今すぐ再起動しますか？",
                "アップデートの準備完了", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                RestoreUpdateButton(update);
                reportProgress?.Invoke("更新は保留されています。設定の［アップデート］からいつでも実行できます。");
                return false;
            }
            UpdateAvailableButton.Content = "更新しています…";
            reportProgress?.Invoke("RELYRを再起動してアップデートします…");
            const string silentArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELYRUPDATE=1";
            using var process = Process.Start(new ProcessStartInfo(installer, silentArguments) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden }) ?? throw new InvalidOperationException("更新用インストーラーを起動できませんでした。");
            RequestApplicationExit();
            return true;
        }
        catch (OperationCanceledException)
        {
            RestoreUpdateButton(update);
            return false;
        }
        catch (Exception ex)
        {
            RestoreUpdateButton(update);
            WpfMessageBox.Show(owner, UpdateService.FriendlyError(ex), "アップデートできません", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
    void RestoreUpdateButton(UpdateInfo update)
    {
        updateInProgress = false;
        UpdateAvailableButton.IsEnabled = true;
        UpdateDismissButton.IsEnabled = true;
        UpdateAvailableButton.Content = "今すぐ更新";
        UpdateBannerProgress.Visibility = Visibility.Collapsed;
        UpdateBannerProgress.Value = 0;
    }
}
