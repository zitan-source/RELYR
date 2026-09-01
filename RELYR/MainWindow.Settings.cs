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
    void RefreshLocalizedUi()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RefreshLocalizedUi);
            return;
        }
        if (editorUiInitialized)
        {
            KindBox.Items.Refresh();
            LongKindBox.Items.Refresh();
            RefreshActionPalette();
            UpdateAutoSaveToggleText();
            UpdateStatus();
        }
        if (appliedConfig != null)
        {
            RebuildTrayMenu();
            UpdateTrayNumber();
        }
    }

    internal void OpenSettingsFrom(Window owner, string? category = null)
    {
        if (settingsWindow is { IsVisible: true } existing)
        {
            if (category != null)
                existing.SelectCategory(category);
            existing.Activate();
            return;
        }

        var window = new SettingsWindow(config, lastUpdateCheck) { Owner = owner };
        settingsWindow = window;
        if (category != null)
            window.SelectCategory(category);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(settingsWindow, window))
                settingsWindow = null;
            if (window.Accepted)
                ApplySettingsWindowResult(window);
        };
        window.Show();
        window.Activate();
    }

    void ApplySettingsWindowResult(SettingsWindow window)
    {
        if (!window.Accepted)
            return;
        if (window.CapsRemapChanged)
        {
            LastInput.Text = "CapsLock設定を変更しました — Windows再起動後に反映されます";
            LastInput.Foreground = ThemeService.Brush("WarningBrush");
        }
        if (window.ResetConfig is { } reset)
        {
            ApplyCompleteConfig(reset, "すべての設定を初期状態へ戻しました");
            if (window.ResetNeedsRestart)
                SettingsWindow.PromptForWindowsRestart(this, false);
            return;
        }
        if (window.ImportedConfig is { } imported)
        {
            ApplyCompleteConfig(imported, "設定をインポートして反映しました");
            if (window.ImportedCapsLockNeedsRestart)
                SettingsWindow.PromptForWindowsRestart(this, window.ImportedCapsLockEnabled);
            return;
        }
        bool previousUpdateSetting = config.CheckForUpdates;
        try
        {
            ApplySettingsWindowValues(window);
            OverlayService.RefreshDeckPanel();
            CopyApplicationOptions(config, appliedConfig);
            var persisted = store.Load();
            CopyApplicationOptions(config, persisted);
            store.Save(persisted);
            if (runtimeRole == RuntimeRole.UiHost)
                IpcRuntime.RequestReload();

            ThemeService.Apply(config.ThemeMode);
            ApplyArchiveWatcherConfiguration();
            UpdateTrayNumber();
            ApplyUpdateCheckPreference(previousUpdateSetting);
            if (config.AutoSave)
                SaveAndApply("自動保存をオンにし、現在の変更を保存・反映しました");
            else
            {
                LastInput.Text = "アプリ設定を保存しました — 自動保存はオフです";
                LastInput.Foreground = ThemeService.Brush("AccentTextBrush");
            }
        }
        catch (Exception ex) { WpfMessageBox.Show("設定を保存できません: " + ex.Message); }
        finally { loading = true; AutoSaveToggle.IsChecked = config.AutoSave; UpdateAutoSaveToggleText(); loading = false; }
    }
    void ApplySettingsWindowValues(SettingsWindow window)
    {
        if (window.StartWithWindowsChanged)
            StartupService.SetEnabled(window.StartWithWindows);
        config.StartWithWindows = window.StartWithWindows;
        config.AutoExtractDesktopArchives = window.AutoExtract;
        config.ShowArchiveExtractionOverlay = window.ShowArchiveExtractionOverlay;
        config.ArchiveWatchFolder = window.ArchiveWatchFolder;
        config.ArchiveDestinationFolder = window.ArchiveDestinationFolder;
        config.DeleteArchiveAfterExtract = window.DeleteAfterExtract;
        config.ShowDesktopNumberInTray = window.ShowDesktopNumberInTray;
        config.CheckForUpdates = window.CheckForUpdates;
        config.ShowProfileSwitchOverlay = window.ShowProfileSwitchOverlay;
        config.WindowActionTarget = window.SelectedWindowActionTarget;
        config.ThemeMode = window.SelectedThemeMode;
        config.UiLanguage = window.SelectedUiLanguage;
        LocalizationService.Apply(config.UiLanguage);
        if (editorUiInitialized)
            RefreshActionPalette();
        config.UiAnimationsEnabled = window.UiAnimationsEnabled;
        config.DetailedDiagnosticsEnabled = window.DetailedDiagnosticsEnabled;
        DiagnosticLogStorage.Configure(config.DetailedDiagnosticsEnabled);
        UiMotionService.Apply(config.UiAnimationsEnabled);
        if (!config.UiAnimationsEnabled)
            SettleLayerEditorMotion();
        config.AutoSave = window.AutoSave;
        config.SpaceHoldRepeatEnabled = window.SpaceHoldRepeat;
        config.InputDisabledApplications = [.. window.InputDisabledApplications];
        config.SpaceHoldRepeatDelayMs = window.SpaceHoldRepeatDelay;
        config.ClockBackgroundMode = window.SelectedClockBackgroundMode;
        config.ClockDisplayMode = window.SelectedClockDisplayMode;
        config.ClockBackgroundImage = window.ClockBackgroundImage;
        config.ClockSolidColor = window.ClockSolidColor;
        config.ClockShowOnAllMonitors = window.ClockShowOnAllMonitors;
        config.InputPanelOpacityPercent = window.InputPanelOpacityPercent;
        config.DeckAfterActionBehavior = window.DeckAfterActionBehavior;
        config.DeckPointerLeaveBehavior = window.DeckPointerLeaveBehavior;
        engine.SpaceHoldRepeatEnabled = config.SpaceHoldRepeatEnabled;
        engine.SpaceHoldRepeatDelayMs = config.SpaceHoldRepeatDelayMs;
        RefreshInputProcessingSuppression();
        OverlayService.RefreshDeckPanel();
    }
    static void CopyApplicationOptions(AppConfig source, AppConfig destination)
    {
        destination.StartWithWindows = source.StartWithWindows;
        destination.AutoExtractDesktopArchives = source.AutoExtractDesktopArchives;
        destination.ShowArchiveExtractionOverlay = source.ShowArchiveExtractionOverlay;
        destination.ArchiveWatchFolder = source.ArchiveWatchFolder;
        destination.ArchiveDestinationFolder = source.ArchiveDestinationFolder;
        destination.DeleteArchiveAfterExtract = source.DeleteArchiveAfterExtract;
        destination.ShowDesktopNumberInTray = source.ShowDesktopNumberInTray;
        destination.CheckForUpdates = source.CheckForUpdates;
        destination.ShowProfileSwitchOverlay = source.ShowProfileSwitchOverlay;
        destination.DismissedUpdateVersion = source.DismissedUpdateVersion;
        destination.PendingUpdateNotesVersion = source.PendingUpdateNotesVersion;
        destination.PendingUpdateNotesBody = source.PendingUpdateNotesBody;
        destination.LastShownUpdateNotesVersion = source.LastShownUpdateNotesVersion;
        destination.WindowActionTarget = source.WindowActionTarget;
        destination.ThemeMode = source.ThemeMode;
        destination.UiLanguage = source.UiLanguage;
        destination.UiAnimationsEnabled = source.UiAnimationsEnabled;
        destination.DetailedDiagnosticsEnabled = source.DetailedDiagnosticsEnabled;
        destination.AutoSave = source.AutoSave;
        destination.SpaceHoldRepeatEnabled = source.SpaceHoldRepeatEnabled;
        destination.InputDisabledApplications = [.. source.InputDisabledApplications];
        destination.SpaceHoldRepeatDelayMs = source.SpaceHoldRepeatDelayMs;
        destination.GestureThresholdPixels = source.GestureThresholdPixels;
        destination.ClockBackgroundMode = source.ClockBackgroundMode;
        destination.ClockDisplayMode = source.ClockDisplayMode;
        destination.ClockBackgroundImage = source.ClockBackgroundImage;
        destination.ClockSolidColor = source.ClockSolidColor;
        destination.ClockShowOnAllMonitors = source.ClockShowOnAllMonitors;
        destination.InputPanelOpacityPercent = source.InputPanelOpacityPercent;
        destination.DeckAfterActionBehavior = source.DeckAfterActionBehavior;
        destination.DeckPointerLeaveBehavior = source.DeckPointerLeaveBehavior;
    }
    void ApplyCompleteConfig(AppConfig value, string message)
    {
        ClearPendingActions();
        config = value;
        ResetEditorHistory();
        DiagnosticLogStorage.Configure(config.DetailedDiagnosticsEnabled);
        LocalizationService.Apply(config.UiLanguage);
        UiMotionService.Apply(config.UiAnimationsEnabled);
        if (!config.UiAnimationsEnabled && editorUiInitialized)
            SettleLayerEditorMotion();
        store.Save(config);
        appliedConfig = store.Clone(config);
        bool pending = LegacyKeyRemapService.IsRestartStillPending(config);
        capsLockRemapped = pending ? config.CapsLockRemapEffectiveBeforeRestart : LegacyKeyRemapService.HasCapsLockToF13();
        engine.TreatF13AsCapsLock = capsLockRemapped;
        engine.UseUsLayout = config.KeyboardLayout == "US";
        engine.SpaceHoldRepeatEnabled = config.SpaceHoldRepeatEnabled;
        engine.SpaceHoldRepeatDelayMs = config.SpaceHoldRepeatDelayMs;
        engine.GestureThresholdPixels = config.GestureThresholdPixels;
        RefreshInputProcessingSuppression();
        engine.Enabled = engineStarted && config.EngineEnabled;
        loading = true;
        KeyboardLayoutBox.SelectedIndex = config.KeyboardLayout == "US" ? 1 : 0;
        AutoSaveToggle.IsChecked = config.AutoSave;
        EngineToggle.IsChecked = engine.Enabled;
        loading = false;
        currentLayer = "通常";
        ClearSelectedInput();
        ThemeService.Apply(config.ThemeMode);
        BuildKeyboard();
        RefreshProfiles();
        UpdateLayerButtons();
        ColorButtons();
        UpdateAutoSaveToggleText();
        ApplyArchiveWatcherConfiguration();
        UpdateTrayNumber();
        UpdateStatus();
        RebuildTrayMenu();
        ApplyUpdateCheckPreference(false);
        LastInput.Text = message;
        LastInput.Foreground = ThemeService.Brush("AccentTextBrush");
    }
    public void ShowFirstRunSetup()
    {
        if (!NeedsFirstRunSetup)
            return;
        EnsureEditorUiInitialized();
        var setup = new SetupWindow { Owner = this };
        if (setup.ShowDialog() == true)
        {
            config.ActiveProfile = "標準";
            config.FirstRunCompleted = setup.DoNotShowAgain;
            store.Save(config);
            RefreshProfiles();
            RebuildTrayMenu();
        }
    }
    public void ShowFromExternalLaunch()
    {
        EnsureEditorUiInitialized();
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        ConstrainToCurrentWorkArea();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        EnsureUpdateCheckStarted();
    }

}
