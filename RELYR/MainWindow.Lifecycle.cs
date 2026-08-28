using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
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

public partial class MainWindow
{
    void Window_Closing(object? s, CancelEventArgs e)
    {
        if (!allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        SystemEvents.UserPreferenceChanged -= WindowsThemeChanged;
        SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        ThemeService.ThemeChanged -= AppThemeChanged;
        MacroPlayer.PlaybackFinished -= MacroPlaybackFinished;
        updateCancellation.Cancel();
        profileOverlay?.Close();
        archiveProgressOverlay?.CloseForProcessExit();
        OverlayService.Shutdown();
        trayNumberTimer.Stop();
        profileSwitchTimer.Stop();
        autoSaveTimer.Stop();
        engine.Enabled = false;
        ClearPendingActions();
        actionQueue.CompleteAdding();
        dragActionQueue.CompleteAdding();
        taskbarClickReplayQueue.CompleteAdding();
        try
        {
            Task.WaitAll([actionWorker, dragActionWorker, taskbarClickReplayWorker], 2000);
        }
        catch { }
        InputEngine.ReleaseForProcessLifecycle();
        engine.Dispose();
        RemoveTrayIconForImmediateExit();
        archiveWatcher.Dispose();
        updateCancellation.Dispose();
    }
    internal void RemoveTrayIconForImmediateExit()
    {
        if (Interlocked.Exchange(ref trayDisposed, 1) != 0)
            return;
        try
        {
            tray.Visible = false;
        }
        catch { }
        try
        {
            tray.Dispose();
        }
        catch { }
        try
        {
            numberedTrayIcon?.Dispose();
        }
        catch { }
        try
        {
            defaultTrayIcon?.Dispose();
        }
        catch { }
        numberedTrayIcon = null;
        defaultTrayIcon = null;
    }
    internal void PrepareVisualsForImmediateExit()
    {
        allowClose = true;
        try
        {
            profileOverlay?.HideImmediatelyForProcessExit();
        }
        catch { }
        try
        {
            archiveProgressOverlay?.HideImmediately();
        }
        catch { }
        try
        {
            Hide();
        }
        catch { }
        RemoveTrayIconForImmediateExit();
    }
    public void PrepareForSystemShutdown()
    {
        allowClose = true;
        engine.Enabled = false;
        ClearPendingActions();
        InputEngine.ReleaseForProcessLifecycle();
        engine.Dispose();
        archiveProgressOverlay?.CloseForProcessExit();
        archiveWatcher.Dispose();
    }
    public void ResetInputStateForSessionTransition()
    {
        activeInputMappings.Clear();
        activeLayerMappings.Clear();
        ClearPendingActions();
        engine.ResetForSessionTransition();
        InputEngine.ReleaseForProcessLifecycle();
    }
    public void RequestApplicationExit(string reason = "application-exit")
    {
        App.MarkShutdownInProgress(reason);
        if (Interlocked.Exchange(ref exitRequested, 1) != 0)
            return;
        allowClose = true;
        // WPF/WinFormsの後処理が停止しても、トレイ終了後にプロセスだけを残さない。
        App.ArmForcedProcessExit(TimeSpan.FromSeconds(3));
        try
        {
            Close();
            InputEngine.ReleaseForProcessLifecycle();
            App.ExitImmediately(0);
        }
        catch
        {
            App.ExitImmediately(1);
        }
    }
    void UpdateUnsavedChangesIndicator()
    {
        if (UnsavedChangesIndicator == null)
            return;
        UnsavedChangesIndicator.Visibility = config != null && !config.AutoSave && hasUnsavedChanges
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
    void RequestApplicationRestart()
    {
        if (Interlocked.Exchange(ref restartRequested, 1) != 0)
            return;
        try
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("実行ファイルの場所を確認できません。");
            var start = new ProcessStartInfo(executable) { UseShellExecute = true };
            // Re-enter through the registered elevated launcher after this
            // process has fully released its hooks.  This is the same ownership
            // path used by a normal Windows/taskbar launch.
            foreach (string argument in App.RestartChildArguments(Environment.ProcessId))
                start.ArgumentList.Add(argument);
            if (Process.Start(start) == null)
                throw new InvalidOperationException("再起動プロセスを開始できません。");
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref restartRequested, 0);
            AppDialog.Show(this, "RELYRを再起動できませんでした。\n\n" + ex.Message, "再起動できません", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        RequestApplicationExit("restart");
    }
}
