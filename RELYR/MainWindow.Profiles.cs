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
    void NewProfile_Click(object s, RoutedEventArgs e)
    {
        var name = PromptText("新しいプロファイル", "新しいプロファイル名", $"プロファイル {config.Profiles.Count + 1}");
        if (string.IsNullOrWhiteSpace(name) || config.Profiles.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.IsNullOrWhiteSpace(name))
                ShowInlineNotice("同じ名前のプロファイルがあります");
            return;
        }
        var source = SelectProfile("割り当てのコピー元", true, out bool cancelled);
        if (cancelled)
            return;
        config.Profiles.Add(new Profile { Name = name, Mappings = source?.Mappings.Select(CloneMapping).ToList() ?? [], DefaultDeckLayoutId = source?.DefaultDeckLayoutId ?? DeckPanelLayout.DefaultLayout(config)?.Id ?? config.DeckLayouts[0].Id });
        SyncDeckProfileVariants();
        EnsureProfileDeckDefaults();
        RefreshProfiles();
        MarkDirty();
        UpdateStatus();
    }
    void DuplicateProfile_Click(object s, RoutedEventArgs e)
    {
        var source = CurrentProfile;
        var name = source.Name + " のコピー";
        int i = 2;
        while (config.Profiles.Any(x => x.Name == name))
            name = source.Name + $" のコピー {i++}";
        var copy = new Profile { Name = name, Mappings = [.. source.Mappings.Select(CloneMapping)], DefaultDeckLayoutId = source.DefaultDeckLayoutId };
        config.Profiles.Add(copy);
        SyncDeckProfileVariants();
        EnsureProfileDeckDefaults();
        RefreshProfiles();
        MarkDirty();
        UpdateStatus();
    }
    void RenameProfile_Click(object s, RoutedEventArgs e)
    {
        if (CurrentProfile == config.Profiles[0])
        {
            ShowInlineNotice("標準プロファイルの名前は変更できません");
            return;
        }
        var old = CurrentProfile.Name;
        var name = PromptText("プロファイル名を変更", "新しい名前", old);
        if (string.IsNullOrWhiteSpace(name) || config.Profiles.Any(x => x != CurrentProfile && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return;
        CurrentProfile.Name = name;
        if (config.ActiveProfile == old)
            config.ActiveProfile = name;
        foreach (var map in config.Profiles.SelectMany(x => x.Mappings))
        {
            if (map.Kind == ActionKind.Profile && map.Value == old)
                map.Value = name;
            if (map.LongPressKind == ActionKind.Profile && map.LongPressValue == old)
                map.LongPressValue = name;
        }
        RefreshProfiles();
        MarkDirty();
    }
    void CopyProfile_Click(object s, RoutedEventArgs e)
    {
        var source = SelectProfile("割り当てのコピー元を選択", false);
        if (source == null || source == CurrentProfile)
            return;
        if (WpfMessageBox.Show($"「{source.Name}」の割り当てで「{CurrentProfile.Name}」を置き換えますか？", "割り当てコピー", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        CurrentProfile.Mappings = [.. source.Mappings.Select(CloneMapping)];
        MarkDirty();
        ColorButtons();
    }
    void ConfigureProfileAutoSwitch_Click(object s, RoutedEventArgs e)
    {
        if (CurrentProfile == config.Profiles[0])
        {
            ShowInlineNotice("標準プロファイルは自動切替の戻り先です");
            return;
        }
        if (CurrentProfile.AutoSwitchEnabled)
        {
            var choice = WpfMessageBox.Show($"「{CurrentProfile.Name}」の自動切替はオンです。\n\nはい：対象アプリを追加\nいいえ：自動切替をオフ\nキャンセル：変更しない", "プロファイル自動切替", MessageBoxButton.YesNoCancel);
            if (choice == MessageBoxResult.Cancel)
                return;
            if (choice == MessageBoxResult.No)
            {
                CurrentProfile.AutoSwitchEnabled = false;
                MarkDirty();
                ShowInlineNotice("自動切替をオフにしました");
                return;
            }
        }
        var app = SelectRunningApplication();
        if (string.IsNullOrWhiteSpace(app))
            return;
        CurrentProfile.AutoSwitchEnabled = true;
        if (!CurrentProfile.AutoSwitchApplications.Contains(app, StringComparer.OrdinalIgnoreCase))
            CurrentProfile.AutoSwitchApplications.Add(app);
        MarkDirty();
        ShowInlineNotice($"{app} がアクティブな時、自動的に「{CurrentProfile.Name}」へ切り替えます");
    }
    void DeleteProfile_Click(object s, RoutedEventArgs e)
    {
        if (CurrentProfile == config.Profiles[0])
        {
            ShowInlineNotice("標準プロファイルは削除できません");
            return;
        }
        if (WpfMessageBox.Show($"「{CurrentProfile.Name}」を削除しますか？", "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        config.Profiles.Remove(CurrentProfile);
        SyncDeckProfileVariants();
        EnsureProfileDeckDefaults();
        config.ActiveProfile = config.Profiles[0].Name;
        RefreshProfiles();
        MarkDirty();
        UpdateStatus();
        RebuildTrayMenu();
    }
    static Mapping CloneMapping(Mapping mapping) => mapping.Copy();
    static GestureDefinition CloneGesture(GestureDefinition x) => new() { Name = x.Name, GestureThresholdPixels = x.GestureThresholdPixels, LockCursorDuringGesture = x.LockCursorDuringGesture, UpKind = x.UpKind, UpValue = x.UpValue, DownKind = x.DownKind, DownValue = x.DownValue, LeftKind = x.LeftKind, LeftValue = x.LeftValue, RightKind = x.RightKind, RightValue = x.RightValue, CenterKind = x.CenterKind, CenterValue = x.CenterValue };
    void Save_Click(object s, RoutedEventArgs e) => SaveAndApply("設定を保存し、エンジンへ反映しました");
    void SaveAndApply(string message)
    {
        string runtimeProfileBeforeSave = appliedConfig.ActiveProfile;
        EnsureProfileDeckDefaults();
        // Upgrade old, valid user intent before strict validation. In particular,
        // old releases could store literal text and executable paths as Key or
        // Shortcut actions, which must not block every unrelated layer change.
        config = ConfigService.NormalizeForSave(config);
        var errors = ConfigValidator.Validate(config);
        if (errors.Count > 0)
        {
            WpfMessageBox.Show(string.Join("\n", errors), "設定の確認", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        store.Save(config);
        appliedConfig = store.Clone(config);
        SynchronizeEditorHistoryCheckpoint();
        hasUnsavedChanges = false;
        UpdateUnsavedChangesIndicator();
        if (deckManagementMode && appliedConfig.Profiles.Any(profile => profile.Name.Equals(runtimeProfileBeforeSave, StringComparison.OrdinalIgnoreCase)))
            appliedConfig.ActiveProfile = runtimeProfileBeforeSave;
        engine.SpaceHoldRepeatEnabled = config.SpaceHoldRepeatEnabled;
        engine.SpaceHoldRepeatDelayMs = config.SpaceHoldRepeatDelayMs;
        engine.GestureThresholdPixels = config.GestureThresholdPixels;
        UpdateStatus();
        RebuildTrayMenu();
        if (runtimeRole == RuntimeRole.UiHost)
            IpcRuntime.RequestReload();
        // Text and shortcut editors deliberately defer Deck refreshes while the
        // user is typing.  Apply the completed mapping to an already visible
        // overlay once saving has committed the full value.
        if (!deckOverlayVisualSynchronized)
            OverlayService.RefreshDeckPanel();
        deckOverlayVisualSynchronized = true;
        LastInput.Text = message;
        LastInput.Foreground = ThemeService.Brush("AccentTextBrush");
        if (deckManagementMode && DeckEditorWorkspace?.Visibility == Visibility.Visible)
            UpdateDeckSaveStatus(saved: true);
    }
    void EnsureProfileDeckDefaults()
    {
        if (config.DeckLayouts.Count == 0)
            config.DeckLayouts.Add(new DeckLayoutDefinition());
        string fallback = config.DeckLayouts.FirstOrDefault(layout => !layout.ProfileSwitchEnabled)?.Id
            ?? DeckPanelLayout.DefaultLayout(config)?.Id ?? config.DeckLayouts[0].Id;
        config.DefaultDeckLayoutId = fallback;
        foreach (var profile in config.Profiles)
        {
            var current = config.DeckLayouts.FirstOrDefault(layout => layout.Id.Equals(profile.DefaultDeckLayoutId, StringComparison.OrdinalIgnoreCase));
            if (current == null || current.ProfileSwitchEnabled && !current.ProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))
                profile.DefaultDeckLayoutId = config.DeckLayouts.FirstOrDefault(layout => layout.ProfileSwitchEnabled && layout.ProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))?.Id ?? fallback;
        }
        config.SharedDefaultDeckLayoutId = fallback;
        config.UseSharedDeckPanel = false;
    }
}
