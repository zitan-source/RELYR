using System.Diagnostics;
using System.IO;
using System.Windows;

namespace RELYR;

public partial class SettingsWindow : Window
{
    internal const string ExportFileName = "relyr-settings.relyr";
    internal const string ExportFileFilter = "RELYR 設定ファイル (*.relyr)|*.relyr";
    internal const string ImportFileFilter = "RELYR 設定ファイル (*.relyr)|*.relyr|以前のRELYR設定 (*.json)|*.json";
    internal const string SupportPageUrl = "https://ko-fi.com/relyr";
    readonly AppConfig config;
    readonly bool initialStartWithWindows;
    readonly AppThemeMode originalThemeMode;
    readonly CancellationTokenSource updateCancellation = new();
    UpdateInfo? availableUpdate;
    UpdateCheckResult? lastUpdateCheck;
    bool updateCheckInProgress;
    private readonly bool themeSelectionLoading = true;
    private bool themeAccepted;
    private bool ownerUpdateSubscribed;
    readonly List<string> inputDisabledApplications;

    internal bool Accepted { get; private set; }

    public AppConfig? ImportedConfig
    {
        get; private set;
    }
    public bool ImportedCapsLockNeedsRestart
    {
        get; private set;
    }
    public bool ImportedCapsLockEnabled
    {
        get; private set;
    }
    public bool StartWithWindows => StartupBox.IsChecked == true;
    internal bool StartWithWindowsChanged => StartWithWindows != initialStartWithWindows;
    public bool AutoExtract => ExtractBox.IsChecked == true;
    public bool DeleteAfterExtract => DeleteBox.IsChecked == true;
    public string ArchiveWatchFolder => ArchiveWatchFolderBox.Text.Trim();
    public string ArchiveDestinationFolder => ArchiveDestinationFolderBox.Text.Trim();
    public bool ShowDesktopNumberInTray => DesktopNumberTrayBox.IsChecked == true;
    public bool CheckForUpdates => CheckForUpdatesBox.IsChecked == true;
    public bool ShowProfileSwitchOverlay => ProfileOverlayBox.IsChecked == true;
    public WindowActionTarget SelectedWindowActionTarget => CursorWindowTargetBox.IsChecked == true ? WindowActionTarget.WindowUnderCursor : WindowActionTarget.ActiveWindow;
    public AppThemeMode SelectedThemeMode => LightThemeBox.IsChecked == true ? AppThemeMode.Light : DarkThemeBox.IsChecked == true ? AppThemeMode.Dark : AppThemeMode.System;
    public bool AutoSave => AutoSaveBox.IsChecked == true;
    public bool SpaceHoldRepeat => SpaceRepeatBox.IsChecked == true;
    public IReadOnlyList<string> InputDisabledApplications => inputDisabledApplications;
    public int SpaceHoldRepeatDelay => int.TryParse(SpaceRepeatDelayBox.Text, out var value) ? Math.Clamp(value, 100, 2000) : 400;
    public int GestureThreshold => int.TryParse(GestureThresholdBox.Text, out var value) ? Math.Clamp(value, 3, 100) : 12;
    public bool LockCursorDuringGesture => LockGestureCursorBox.IsChecked == true;
    public ClockBackgroundMode SelectedClockBackgroundMode => ClockImageBackgroundBox.IsChecked == true ? ClockBackgroundMode.Image : ClockSolidBackgroundBox.IsChecked == true ? ClockBackgroundMode.Solid : ClockBackgroundMode.FrostedScreen;
    public ClockDisplayMode SelectedClockDisplayMode => (ClockDisplayMode)Math.Clamp(ClockDisplayModeBox.SelectedIndex, 0, 3);
    public string ClockBackgroundImage => ClockBackgroundImageBox.Text.Trim();
    public string ClockSolidColor => NormalizeClockColor(ClockSolidColorBox.Text);
    public bool ClockShowOnAllMonitors => ClockAllMonitorsBox.IsChecked == true;
    public int InputPanelOpacityPercent => (int)Math.Round(InputPanelOpacitySlider.Value);
    // Deck auto-hide is edited in the Deck workspace. Keeping these values in
    // the settings result prevents the general settings dialog from changing
    // an unrelated Deck preference when it is saved.
    public bool DeckAutoHideAfterAction => config.DeckAutoHideAfterAction;
    public bool DeckAutoHideOnPointerLeave => config.DeckAutoHideOnPointerLeave;
    public bool CapsRemapChanged
    {
        get; private set;
    }
    public AppConfig? ResetConfig
    {
        get; private set;
    }
    public bool ResetNeedsRestart
    {
        get; private set;
    }
    internal bool TitleBarUsesDarkMode
    {
        get; private set;
    }

    public SettingsWindow(AppConfig config) : this(config, (UpdateCheckResult?)null) { }

    internal SettingsWindow(AppConfig config, UpdateInfo knownUpdate) : this(config, new UpdateCheckResult(knownUpdate.Version, knownUpdate.VersionText, knownUpdate, DateTimeOffset.Now)) { }

    internal SettingsWindow(AppConfig config, UpdateCheckResult? knownUpdate)
    {
        this.config = config;
        originalThemeMode = config.ThemeMode;
        InitializeComponent();
        inputDisabledApplications = [.. config.InputDisabledApplications];
        RefreshInputDisabledApplications();
        MainWindow.FollowWindowsTitleBarTheme(this, value => TitleBarUsesDarkMode = value);
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 40);
        Height = Math.Min(Height, MaxHeight);
        initialStartWithWindows = StartupService.IsEnabled();
        StartupBox.IsChecked = initialStartWithWindows;
        DesktopNumberTrayBox.IsChecked = config.ShowDesktopNumberInTray;
        CheckForUpdatesBox.IsChecked = config.CheckForUpdates;
        ProfileOverlayBox.IsChecked = config.ShowProfileSwitchOverlay;
        ActiveWindowTargetBox.IsChecked = config.WindowActionTarget == WindowActionTarget.ActiveWindow;
        CursorWindowTargetBox.IsChecked = config.WindowActionTarget == WindowActionTarget.WindowUnderCursor;
        SystemThemeBox.IsChecked = config.ThemeMode == AppThemeMode.System;
        LightThemeBox.IsChecked = config.ThemeMode == AppThemeMode.Light;
        DarkThemeBox.IsChecked = config.ThemeMode == AppThemeMode.Dark;
        themeSelectionLoading = false;
        AutoSaveBox.IsChecked = config.AutoSave;
        SpaceRepeatBox.IsChecked = config.SpaceHoldRepeatEnabled;
        SpaceRepeatDelayBox.Text = config.SpaceHoldRepeatDelayMs.ToString();
        GestureThresholdBox.Text = config.GestureThresholdPixels.ToString();
        LockGestureCursorBox.IsChecked = config.LockCursorDuringGesture;
        ClockFrostedBackgroundBox.IsChecked = config.ClockBackgroundMode == ClockBackgroundMode.FrostedScreen;
        ClockImageBackgroundBox.IsChecked = config.ClockBackgroundMode == ClockBackgroundMode.Image;
        ClockSolidBackgroundBox.IsChecked = config.ClockBackgroundMode == ClockBackgroundMode.Solid;
        ClockDisplayModeBox.ItemsSource = new[] { "時・分", "時・分・秒", "月日・曜日・時刻", "年月日・曜日・秒まで" };
        ClockDisplayModeBox.SelectedIndex = (int)config.ClockDisplayMode;
        ClockBackgroundImageBox.Text = config.ClockBackgroundImage;
        ClockSolidColorBox.Text = NormalizeClockColor(config.ClockSolidColor);
        ClockAllMonitorsBox.IsChecked = config.ClockShowOnAllMonitors;
        InputPanelOpacitySlider.Value = Math.Clamp(config.InputPanelOpacityPercent, 40, 100);
        UpdateInputPanelOpacityText();
        UpdateClockBackgroundControls();
        ArchiveWatchFolderBox.Text = string.IsNullOrWhiteSpace(config.ArchiveWatchFolder) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : config.ArchiveWatchFolder;
        ArchiveDestinationFolderBox.Text = config.ArchiveDestinationFolder;
        ExtractBox.IsChecked = config.AutoExtractDesktopArchives;
        DeleteBox.IsChecked = config.DeleteArchiveAfterExtract;
        ExtractBox.Checked += (_, _) => DeleteBox.IsEnabled = true;
        ExtractBox.Unchecked += (_, _) => DeleteBox.IsEnabled = false;
        DeleteBox.IsEnabled = ExtractBox.IsChecked == true;
        Loaded += SettingsWindow_Loaded;
        Closed += SettingsWindow_Closed;
        CurrentVersionText.Text = "v" + MainWindow.DisplayVersion;
        ApplyUpdateResult(knownUpdate, false);
        RefreshCapsRemapStatus();
    }

    void RefreshInputDisabledApplications()
    {
        InputDisabledApplicationList.ItemsSource = null;
        InputDisabledApplicationList.ItemsSource = inputDisabledApplications.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    void AddRunningInputDisabledApplication_Click(object sender, RoutedEventArgs e)
    {
        string? executable = MainWindow.SelectRunningApplication(this, "RELYRの入力処理を無効にするアプリを選択");
        if (!string.IsNullOrWhiteSpace(executable))
            AddInputDisabledApplication(executable);
    }

    void AddInstalledInputDisabledApplication_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ApplicationPickerWindow(true) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedApplication == null)
            return;
        string? executable = ProfileManagerWindow.ExecutableNameForAutoSwitch(picker.SelectedApplication);
        if (!string.IsNullOrWhiteSpace(executable))
            AddInputDisabledApplication(executable);
    }

    void RemoveInputDisabledApplication_Click(object sender, RoutedEventArgs e)
    {
        if (InputDisabledApplicationList.SelectedItem is not string executable)
            return;
        inputDisabledApplications.RemoveAll(x => x.Equals(executable, StringComparison.OrdinalIgnoreCase));
        RefreshInputDisabledApplications();
    }

    void AddInputDisabledApplication(string executable)
    {
        executable = Path.GetFileName(executable.Trim());
        if (string.IsNullOrWhiteSpace(executable) || inputDisabledApplications.Contains(executable, StringComparer.OrdinalIgnoreCase))
            return;
        inputDisabledApplications.Add(executable);
        RefreshInputDisabledApplications();
        InputDisabledApplicationList.SelectedItem = executable;
        InputDisabledApplicationList.ScrollIntoView(executable);
    }

    internal void AddInputDisabledApplicationForTest(string executable) => AddInputDisabledApplication(executable);
    void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (Owner is not MainWindow main || ownerUpdateSubscribed)
            return;
        main.UpdateCheckCompleted += MainUpdateCheckCompleted;
        ownerUpdateSubscribed = true;
    }
    void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        updateCancellation.Cancel();
        if (ownerUpdateSubscribed && Owner is MainWindow main)
            main.UpdateCheckCompleted -= MainUpdateCheckCompleted;
        if (!themeAccepted)
            ThemeService.Apply(originalThemeMode);
    }
    void MainUpdateCheckCompleted(UpdateCheckResult result) => ApplyUpdateResult(result, true);
    void Category_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (GeneralPanel == null)
            return;
        string selected = (CategoryList.SelectedItem as System.Windows.Controls.ListBoxItem)?.Tag?.ToString() ?? "General";
        GeneralPanel.Visibility = selected == "General" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePanel.Visibility = selected == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        UpdatePanel.Visibility = selected == "Update" ? Visibility.Visible : Visibility.Collapsed;
        DisabledPanel.Visibility = selected == "Disabled" ? Visibility.Visible : Visibility.Collapsed;
        SupportPanel.Visibility = selected == "Support" ? Visibility.Visible : Visibility.Collapsed;
        LayersScrollPanel.Visibility = selected == "Layers" ? Visibility.Visible : Visibility.Collapsed;
        OverlayPanel.Visibility = selected == "Overlay" ? Visibility.Visible : Visibility.Collapsed;
        ArchivePanel.Visibility = selected == "Archive" ? Visibility.Visible : Visibility.Collapsed;
        DataPanel.Visibility = selected == "Data" ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void SelectCategory(string category)
    {
        var item = CategoryList.Items.Cast<System.Windows.Controls.ListBoxItem>()
            .FirstOrDefault(candidate => string.Equals(candidate.Tag?.ToString(), category, StringComparison.Ordinal));
        if (item != null)
            CategoryList.SelectedItem = item;
    }

    void ThemeMode_Changed(object sender, RoutedEventArgs e)
    {
        if (themeSelectionLoading)
            return;
        ThemeService.Apply(SelectedThemeMode);
        RefreshCapsRemapStatus();
    }

    void ClockBackground_Changed(object sender, RoutedEventArgs e) => UpdateClockBackgroundControls();
    void ClockSolidColor_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (ClockSolidColorSample == null)
            return;
        bool valid = TryClockColor(ClockSolidColorBox.Text, out var color);
        ClockSolidColorSample.Background = new System.Windows.Media.SolidColorBrush(color);
        ClockSolidColorBox.BorderBrush = valid ? ThemeService.Brush("BorderBrush") : ThemeService.Brush("DangerBrush");
    }
    void ClockColorPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string color })
            ClockSolidColorBox.Text = color;
    }
    internal static bool TryClockColor(string? value, out System.Windows.Media.Color color)
    {
        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(value) is System.Windows.Media.Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException) { }
        color = System.Windows.Media.Color.FromRgb(16, 31, 46);
        return false;
    }
    internal static string NormalizeClockColor(string? value)
        => TryClockColor(value, out var color) ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : "#101F2E";
    void InputPanelOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateInputPanelOpacityText();
    void UpdateInputPanelOpacityText()
    {
        InputPanelOpacityValueText?.Text = $"{Math.Round(InputPanelOpacitySlider.Value):0}%";
    }
    void UpdateClockBackgroundControls()
    {
        if (ClockImagePicker == null)
            return;
        ClockImagePicker.IsEnabled = ClockImageBackgroundBox.IsChecked == true;
        ClockImagePicker.Opacity = ClockImagePicker.IsEnabled ? 1 : .5;
        ClockSolidPicker.IsEnabled = ClockSolidBackgroundBox.IsChecked == true;
        ClockSolidPicker.Opacity = ClockSolidPicker.IsEnabled ? 1 : .5;
    }

    void BrowseClockBackground_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "クロックの背景画像を選択",
            Filter = "画像ファイル|*.jpg;*.jpeg;*.png;*.bmp;*.webp|すべてのファイル|*.*"
        };
        if (dialog.ShowDialog(this) == true)
            ClockBackgroundImageBox.Text = dialog.FileName;
    }

    async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (updateCheckInProgress)
            return;
        updateCheckInProgress = true;
        CheckForUpdatesButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        UpdateStatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "SecondaryText");
        UpdateStatusText.Text = "アップデートを確認しています…";
        try
        {
            var result = Owner is MainWindow main
                ? await main.CheckForUpdatesNowAsync()
                : await UpdateService.CheckLatestAsync(MainWindow.RunningVersion, updateCancellation.Token);
            ApplyUpdateResult(result, true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            UpdateStatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "WarningBrush");
            UpdateStatusText.Text = UpdateService.FriendlyError(ex);
        }
        finally
        {
            updateCheckInProgress = false;
            CheckForUpdatesButton.IsEnabled = true;
            InstallUpdateButton.IsEnabled = availableUpdate != null;
        }
    }

    async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (updateCheckInProgress || availableUpdate is not { } update || Owner is not MainWindow main)
            return;
        if (AppDialog.Show(this, $"RELYR v{update.VersionText} をダウンロードして更新します。\n\nSHA-256で検証後、RELYRを終了して更新し、自動で再起動します。続行しますか？", "RELYRをアップデート", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
            return;
        CheckForUpdatesButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        ShowDownloadProgress();
        var progress = new Progress<UpdateDownloadProgress>(UpdateDownloadProgressDisplay);
        bool started = await main.InstallUpdateAsync(this, update, status => UpdateStatusText.Text = status, progress);
        if (started || !IsLoaded)
            return;
        CheckForUpdatesButton.IsEnabled = true;
        InstallUpdateButton.IsEnabled = true;
        HideDownloadProgress();
        ApplyUpdateResult(lastUpdateCheck, false);
    }

    internal void ApplyUpdateResult(UpdateCheckResult? result, bool checkedNow)
    {
        lastUpdateCheck = result;
        availableUpdate = result?.AvailableUpdate;
        LatestVersionText.Text = result == null ? "—" : "v" + result.LatestVersionText;
        DateTimeOffset? checkedAt = result?.CheckedAt;
        if (checkedAt == null && config.LastUpdateCheckUtcTicks > 0)
            try
            {
                checkedAt = new DateTimeOffset(config.LastUpdateCheckUtcTicks, TimeSpan.Zero).ToLocalTime();
            }
            catch (ArgumentOutOfRangeException) { }
        LastCheckedText.Text = checkedAt is null ? "未確認" : checkedAt.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
        InstallUpdateButton.Visibility = availableUpdate == null ? Visibility.Collapsed : Visibility.Visible;
        InstallUpdateButton.IsEnabled = availableUpdate != null && !updateCheckInProgress;
        if (availableUpdate is { } update)
        {
            UpdateStatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "WarningBrush");
            UpdateStatusText.Text = $"新しいバージョン v{update.VersionText} を利用できます";
        }
        else
        {
            UpdateStatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, checkedNow ? "AccentBrush" : "SecondaryText");
            UpdateStatusText.Text = checkedNow ? $"最新バージョンです（v{MainWindow.DisplayVersion}）" : $"現在のバージョンは v{MainWindow.DisplayVersion} です。［アップデートを確認］から手動で確認できます。";
        }
    }
    void ShowDownloadProgress()
    {
        UpdateProgressBar.Value = 0;
        UpdateProgressBar.IsIndeterminate = true;
        UpdateProgressBar.Visibility = Visibility.Visible;
        UpdateProgressText.Text = "ダウンロードを準備しています…";
        UpdateProgressText.Visibility = Visibility.Visible;
    }
    void HideDownloadProgress()
    {
        UpdateProgressBar.Visibility = Visibility.Collapsed;
        UpdateProgressText.Visibility = Visibility.Collapsed;
    }
    void UpdateDownloadProgressDisplay(UpdateDownloadProgress value)
    {
        UpdateProgressBar.IsIndeterminate = value.Percentage == null;
        if (value.Percentage is { } percentage)
            UpdateProgressBar.Value = Math.Clamp(percentage, 0, 100);
        string received = FormatBytes(value.BytesReceived);
        UpdateProgressText.Text = value.TotalBytes is { } total
            ? $"{value.Percentage ?? 0:0}%（{received} / {FormatBytes(total)}）"
            : $"{received} をダウンロード済み";
    }
    static string FormatBytes(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.0} MB" : bytes >= 1024 ? $"{bytes / 1024d:0.0} KB" : $"{bytes} B";
    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedClockBackgroundMode == ClockBackgroundMode.Image && !File.Exists(ClockBackgroundImage))
        {
            AppDialog.Show(this, "クロックに使用する背景画像を［参照…］から選択してください。", "クロックの背景", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (AutoExtract)
        {
            if (!Directory.Exists(ArchiveWatchFolder))
            {
                AppDialog.Show(this, "監視するフォルダーが見つかりません。［参照］から実在するフォルダーを選択してください。", "自動解凍の設定", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!string.IsNullOrWhiteSpace(ArchiveDestinationFolder) && !Directory.Exists(ArchiveDestinationFolder))
            {
                AppDialog.Show(this, "解凍後の保存先が見つかりません。［参照］から実在するフォルダーを選択してください。", "自動解凍の設定", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        themeAccepted = true;
        Accepted = true;
        Close();
    }
    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ThemeService.Apply(originalThemeMode);
        Accepted = false;
        Close();
    }
    void ShowTutorial_Click(object sender, RoutedEventArgs e) => new SetupWindow(true) { Owner = this }.ShowDialog();
    void OpenSupportPage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(SupportPageUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, "支援ページを開けませんでした。\n\n" + ex.Message, "支援ページを開けません", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = ExportFileFilter, FileName = ExportFileName, DefaultExt = ".relyr", AddExtension = true };
        if (dialog.ShowDialog() == true)
            new ConfigService().Export(config, dialog.FileName);
    }
    async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = ImportFileFilter };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            var imported = new ConfigService().Import(dialog.FileName);
            bool current = LegacyKeyRemapService.HasCapsLockToF13();
            bool desired = imported.CapsLockLayerEnabled;
            if (desired)
            {
                string warning = "この設定ではCapsLockレイヤーがオンです。\n\nインポートするとCapsLockはF13へ割り当てられ、元のCapsLock機能は使用できなくなります。変更はWindowsを再起動するまで有効にならず、それまではCapsLockレイヤーも使用できません。\n\nこの設定をインポートしますか？";
                if (AppDialog.Show(this, warning, "CapsLockレイヤーを含む設定", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                    return;
            }
            if (desired != current)
            {
                if (!await ChangeCapsRemapAsync(desired, imported, false))
                    return;
                ImportedCapsLockNeedsRestart = true;
                ImportedCapsLockEnabled = desired;
            }
            else if (LegacyKeyRemapService.IsRestartStillPending(config))
            {
                imported.CapsLockRemapPendingRestart = true;
                imported.CapsLockRemapEffectiveBeforeRestart = config.CapsLockRemapEffectiveBeforeRestart;
                imported.CapsLockRemapChangedAtUtcTicks = config.CapsLockRemapChangedAtUtcTicks;
                new ConfigService().Save(imported);
                ImportedCapsLockNeedsRestart = true;
                ImportedCapsLockEnabled = desired;
            }
            ImportedConfig = imported;
            Accepted = true;
            Close();
        }
        catch (Exception ex) { AppDialog.Show(this, ex.Message, "インポートできません", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    void RefreshCapsRemapStatus()
    {
        bool enabled = LegacyKeyRemapService.HasCapsLockToF13();
        bool pending = LegacyKeyRemapService.IsRestartStillPending(config);
        CapsRemapStatus.Text = pending ? (enabled ? "設定済み（再起動待ち）— 再起動するまでCapsLockレイヤーは機能しません。" : "復元済み（再起動待ち）— 再起動するまではF13レイヤーの状態が続きます。") : (enabled ? "設定済み — CapsLockをF13レイヤーキーとして使用します。" : "未設定 — CapsLockレイヤーは動作せず、通常のCapsLockとしてWindowsへ渡します。");
        CapsRemapStatus.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, enabled && !pending ? "AccentBrush" : "WarningBrush");
        EnableCapsRemapButton.IsEnabled = !enabled;
        DisableCapsRemapButton.IsEnabled = enabled;
    }
    async void EnableCapsRemap_Click(object sender, RoutedEventArgs e)
    {
        if (AppDialog.Show(this, "CapsLock本来の機能を無効にし、CapsLockレイヤー専用キーへ変更します。CapsLockはF13へ割り当てられ、元のCapsLock機能は使用できなくなります。\n\n設定しますか？", "CapsLockレイヤーを有効化", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        if (await ChangeCapsRemapAsync(true, config, true))
            PromptForWindowsRestart(this, true);
    }
    async void DisableCapsRemap_Click(object sender, RoutedEventArgs e)
    {
        if (AppDialog.Show(this, "CapsLockレイヤーを無効にし、元のCapsLockへ戻します。\n\n設定しますか？", "CapsLockへ戻す", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        if (await ChangeCapsRemapAsync(false, config, true))
            PromptForWindowsRestart(this, false);
    }
    async Task<bool> ChangeCapsRemapAsync(bool enabled, AppConfig target, bool refresh)
    {
        try
        {
            bool effectiveBefore = LegacyKeyRemapService.HasCapsLockToF13();
            if (StartupService.IsProcessElevated())
                LegacyKeyRemapService.SetCapsLockToF13(enabled);
            else if (!await IpcRuntime.TrySetCapsLockRemapAsync(enabled))
                throw new InvalidOperationException("管理者ヘルパーに接続できません。RELYRを再起動してから、もう一度実行してください。");
            target.CapsLockLayerEnabled = enabled;
            target.CapsLockRemapPendingRestart = true;
            target.CapsLockRemapEffectiveBeforeRestart = effectiveBefore;
            target.CapsLockRemapChangedAtUtcTicks = DateTime.UtcNow.Ticks;
            new ConfigService().Save(target);
            CapsRemapChanged = true;
            if (refresh)
                RefreshCapsRemapStatus();
            return true;
        }
        catch (Exception ex) { AppDialog.Show(this, ex.Message, "設定できません", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
    }

    void BrowseArchiveWatchFolder_Click(object sender, RoutedEventArgs e) => BrowseFolder(ArchiveWatchFolderBox, "圧縮ファイルを監視するフォルダーを選択");
    void BrowseArchiveDestinationFolder_Click(object sender, RoutedEventArgs e) => BrowseFolder(ArchiveDestinationFolderBox, "解凍後のファイルを保存するフォルダーを選択");
    void BrowseFolder(System.Windows.Controls.TextBox target, string description)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = description, UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (Directory.Exists(target.Text))
            dialog.InitialDirectory = target.Text;
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            target.Text = dialog.SelectedPath;
    }

    void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        string message = "全プロファイル、全レイヤーの割り当て、マクロ、アプリ設定を初期状態へ戻します。\n\nこの操作は元に戻せません。続行しますか？";
        if (AppDialog.Show(this, message, "すべての設定をリセット", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;
        try
        {
            if (!StartupService.IsProcessElevated())
                throw new UnauthorizedAccessException("管理者モードで起動されていません。RELYRを再インストールしてください。");
            bool pending = LegacyKeyRemapService.IsRestartStillPending(config);
            bool remapRegistered = LegacyKeyRemapService.HasCapsLockToF13();
            bool effectiveBefore = pending ? config.CapsLockRemapEffectiveBeforeRestart : remapRegistered;
            ResetNeedsRestart = pending || remapRegistered || config.CapsLockLayerEnabled;
            LegacyKeyRemapService.SetCapsLockToF13(false);
            StartupService.SetEnabled(false);
            var reset = ConfigService.CreateDefault();
            reset.FirstRunCompleted = true;
            if (ResetNeedsRestart)
            {
                reset.CapsLockRemapPendingRestart = true;
                reset.CapsLockRemapEffectiveBeforeRestart = effectiveBefore;
                reset.CapsLockRemapChangedAtUtcTicks = DateTime.UtcNow.Ticks;
            }
            new ConfigService().Save(reset);
            ResetConfig = reset;
            CapsRemapChanged = ResetNeedsRestart;
            Accepted = true;
            Close();
        }
        catch (Exception ex) { AppDialog.Show(this, "設定をリセットできませんでした。\n\n" + ex.Message, "リセットできません", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    internal static void PromptForWindowsRestart(Window owner, bool enabled)
    {
        string pending = enabled ? "［キャンセル］を押した場合もCapsLockレイヤーはオンのままですが、再起動するまでは機能しません。" : "［キャンセル］を押した場合も復元設定は保存されますが、再起動するまではF13レイヤーの状態が続きます。";
        string message = $"CapsLockの設定を変更しました。再起動しないと有効になりません。\n\n［OK］を押すと今すぐWindowsを再起動します。\n{pending}\n\n今すぐ再起動しますか？";
        if (AppDialog.Show(owner, message, "Windowsの再起動が必要です", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
            return;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = true });
        }
        catch (Exception ex) { AppDialog.Show(owner, "Windowsを再起動できませんでした。手動で再起動してください。\n\n" + ex.Message, "再起動できません", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
