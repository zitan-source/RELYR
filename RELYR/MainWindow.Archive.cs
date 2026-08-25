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
    void ApplyArchiveWatcherConfiguration()
    {
        ArchiveAutomationState.Set(config.AutoExtractDesktopArchives);
        if (!OwnsArchiveAutomation(runtimeRole))
        {
            // The medium UI process is the sole archive owner. Running the same
            // FileSystemWatcher in the elevated helper races two extractions of
            // one archive and can create a second "(2)" destination folder.
            archiveWatcher.Dispose();
            return;
        }
        archiveWatcher.Apply(config);
        if (!config.ShowArchiveExtractionOverlay || !config.AutoExtractDesktopArchives)
            archiveProgressOverlay?.HideImmediately();
    }

    internal static bool OwnsArchiveAutomation(RuntimeRole role)
        => role != RuntimeRole.ElevatedHelper;

    void HandleArchiveActivity(ArchiveActivity activity)
    {
        try
        {
            if (runtimeRole == RuntimeRole.ElevatedHelper || !config.ShowArchiveExtractionOverlay)
            {
                archiveProgressOverlay?.HideImmediately();
                return;
            }
            archiveProgressOverlay ??= new ArchiveProgressOverlay();
            archiveProgressOverlay.ShowActivity(activity);
        }
        catch (Exception error)
        {
            LifecycleDiagnostics.Write("archive-progress-overlay-failed", error.ToString());
            try { archiveProgressOverlay?.CloseForProcessExit(); } catch { }
            archiveProgressOverlay = null;
        }
    }

    internal void ToggleAutoExtractFromAction()
    {
        if (runtimeRole == RuntimeRole.ElevatedHelper)
        {
            _ = OverlayUiBridge.RequestShow(ActionCatalog.ToggleAutoExtractAction);
            return;
        }

        bool previous = config.AutoExtractDesktopArchives;
        bool enabled = !previous;
        try
        {
            config.AutoExtractDesktopArchives = enabled;
            appliedConfig.AutoExtractDesktopArchives = enabled;
            store.Save(config);
            ApplyArchiveWatcherConfiguration();
            if (runtimeRole == RuntimeRole.UiHost)
                IpcRuntime.RequestReload();
            LastInput.Text = enabled ? "自動解凍をオンにしました" : "自動解凍をオフにしました";
            LastInput.Foreground = ThemeService.Brush("AccentTextBrush");
        }
        catch (Exception error)
        {
            config.AutoExtractDesktopArchives = previous;
            appliedConfig.AutoExtractDesktopArchives = previous;
            try { ApplyArchiveWatcherConfiguration(); } catch { }
            LifecycleDiagnostics.Write("auto-extract-toggle-failed", error.ToString());
            ShowInlineError("自動解凍の設定を保存できませんでした");
        }
    }
}
