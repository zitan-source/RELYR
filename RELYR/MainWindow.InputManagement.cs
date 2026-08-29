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
    void Detect_Click(object s, RoutedEventArgs e)
    {
        detectMode = true;
        pendingDetectedLayer = null;
        ClearExecutionFocus(ActionPaletteButton);
        LastInput.Text = "入力を待っています… レイヤーボタンは押したまま次のキーを押してください";
        LastInput.Foreground = ThemeService.Brush("WarningBrush");
    }
    void HandleDetectedInput(string text)
    {
        if (text == "緊急停止")
        {
            macroEmergencyStop = true;
            ClearPendingActions();
            EngineToggle.IsChecked = false;
        }
        macroWindow?.Capture(text);
        LastInput.Text = "入力: " + text;
        if (!detectMode || text == "緊急停止")
            return;
        string[] parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;
        string input = parts[0], state = parts.Length > 1 ? parts[1] : "";
        if (state.Equals("Layer Down", StringComparison.OrdinalIgnoreCase))
        {
            pendingDetectedLayer = input;
            ShowDetectionLayerWaiting(input);
            return;
        }
        if (state.Equals("Layer Up", StringComparison.OrdinalIgnoreCase))
        {
            if (pendingDetectedLayer?.Equals(input, StringComparison.OrdinalIgnoreCase) == true)
                CompleteDetectedInput(input);
            return;
        }
        bool down = state.Equals("Down", StringComparison.OrdinalIgnoreCase), up = state.Equals("Up", StringComparison.OrdinalIgnoreCase);
        if (pendingDetectedLayer != null)
        {
            if (input.Equals(pendingDetectedLayer, StringComparison.OrdinalIgnoreCase))
            {
                if (up)
                    CompleteDetectedInput(input);
                return;
            }
            if (down)
            {
                CompleteDetectedInput(input.Contains('+') ? input : pendingDetectedLayer + "+" + input);
                return;
            }
        }
        if (down && IsDetectableLayer(input))
        {
            pendingDetectedLayer = input;
            ShowDetectionLayerWaiting(input);
            return;
        }
        if (down || (!down && !up && !state.Contains("Drag", StringComparison.OrdinalIgnoreCase)))
            CompleteDetectedInput(input);
    }
    void MacroPlaybackFinished(MacroPlaybackResult result)
    {
        if (result.Cancelled)
            return;
        Dispatcher.BeginInvoke(() => { LastInput.Text = result.Succeeded ? result.Message : "マクロ実行エラー: " + result.Message; LastInput.Foreground = ThemeService.Brush(result.Succeeded ? "AccentTextBrush" : "DangerBrush"); });
    }
    static bool IsDetectableLayer(string input) => input is "Space" or "CapsLock" or "MouseRight" or "MouseBack" or "MouseForward";
    void ShowDetectionLayerWaiting(string layer)
    {
        LastInput.Text = $"待機中: {DisplayInputName(layer)} を押したまま、組み合わせるキーを押してください";
        LastInput.Foreground = ThemeService.Brush("WarningBrush");
    }
    void CompleteDetectedInput(string input)
    {
        detectMode = false;
        pendingDetectedLayer = null;
        string? unavailableReason = InputAssignmentPolicy.UnavailableInputReason(input);
        if (unavailableReason != null || input is "Space" or "CapsLock")
        {
            ShowInlineNotice(unavailableReason ?? "レイヤーボタン単体には設定できません");
            return;
        }
        SelectInput(input, false);
        editingSelectedInput = true;
        ColorButtons();
        if (string.IsNullOrWhiteSpace(ValueBox.Text))
            FocusExecutionValue(ValueBox);
        LastInput.Text = "検出: " + DisplayInputName(input);
        LastInput.Foreground = ThemeService.Brush("AccentTextBrush");
    }
    void LayerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string layer } button)
            return;
        bool leavingDeckWorkspace = deckManagementMode;
        ShowKeyboardWorkspace();
        if (leavingDeckWorkspace && actionPaletteOpen)
            CloseActionPalette(animated: false);
        ClearExecutionFocus(button);
        if (IsMouseLayerBlockedByDirectGesture(config.Profiles, CurrentProfile.Name, layer))
        {
            ShowInlineNotice($"{MouseLayerLabel(layer)}レイヤーは通常レイヤーのジェスチャーと競合しているため使用できません");
            return;
        }
        if (layer == "CapsLock" && !ConfirmCapsLockLayer())
            return;
        currentLayer = layer;
        ClearSelectedInput(button);
        UpdateLayerButtons();
    }

    void SettleLayerEditorMotion()
    {
        SettleAssignmentEditorMotion();
        foreach (var button in VisualInputButtons().Concat(deckManagementButtons).Distinct())
        {
            button.ApplyTemplate();
            if (button.Template.FindName("DropTargetTint", button) is FrameworkElement wave)
                ResetActionDropSuccessVisual(button, wave);
            else if (button.RenderTransform is ScaleTransform inputScale)
            {
                UiMotionService.StopAndSetDouble(inputScale, ScaleTransform.ScaleXProperty, 1);
                UiMotionService.StopAndSetDouble(inputScale, ScaleTransform.ScaleYProperty, 1);
            }
        }
        UiMotionService.StopAndSetDouble(KeyboardWorkspace, UIElement.OpacityProperty, 1);
        if (KeyboardWorkspace.RenderTransform is TransformGroup workspaceGroup)
        {
            foreach (var scale in workspaceGroup.Children.OfType<ScaleTransform>())
            {
                UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleXProperty, 1);
                UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleYProperty, 1);
            }
            foreach (var translate in workspaceGroup.Children.OfType<TranslateTransform>())
            {
                UiMotionService.StopAndSetDouble(translate, TranslateTransform.XProperty, 0);
                UiMotionService.StopAndSetDouble(translate, TranslateTransform.YProperty, 0);
            }
        }
        foreach (var layerButton in new[] { NormalLayerButton, SpaceLayerButton, CapsLockLayerButton, RightMouseLayerButton, ForwardMouseLayerButton, BackMouseLayerButton, TaskbarLayerButton })
        {
            if (layerButton.Content is not Grid layerGrid || layerGrid.Children.OfType<Border>().FirstOrDefault() is not Border iconFrame || iconFrame.RenderTransform is not ScaleTransform scale)
                continue;
            UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleXProperty, 1);
            UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleYProperty, 1);
        }
    }
    bool ConfirmCapsLockLayer()
    {
        if (capsLockRemapped)
            return true;
        ShowInlineNotice("CapsLockレイヤーにはF13リマップ設定とWindows再起動が必要です");
        WpfMessageBox.Show("CapsLockレイヤーは安全性のため、CapsLock→F13設定を行った場合だけ動作します。\n\n［設定］→［レイヤー］で設定し、Windowsを再起動してください。", "CapsLockレイヤーは無効です", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }
    void EngineChanged(object s, RoutedEventArgs e)
    {
        if (loading || config == null)
            return;
        if (!engineStarted)
        {
            loading = true;
            EngineToggle.IsChecked = false;
            loading = false;
            return;
        }
        engine.Enabled = EngineToggle.IsChecked == true;
        if (!engine.Enabled)
            ClearPendingActions();
        config.EngineEnabled = engine.Enabled;
        appliedConfig.EngineEnabled = engine.Enabled;
        var persisted = store.Load();
        persisted.EngineEnabled = engine.Enabled;
        store.Save(persisted);
        SynchronizeEditorHistoryCheckpoint();
        UpdateStatus();
    }
    void AutoSaveChanged(object s, RoutedEventArgs e)
    {
        if (loading || config == null)
            return;
        config.AutoSave = AutoSaveToggle.IsChecked == true;
        UpdateAutoSaveToggleText();
        if (config.AutoSave)
            SaveAndApply("自動保存をオンにし、現在の変更を保存・反映しました");
        else
        {
            appliedConfig.AutoSave = false;
            var persisted = store.Load();
            persisted.AutoSave = false;
            store.Save(persisted);
            SynchronizeEditorHistoryCheckpoint();
            LastInput.Text = "自動保存をオフにしました";
            LastInput.Foreground = ThemeService.Brush("AccentTextBrush");
            UpdateUnsavedChangesIndicator();
        }
    }
    void UpdateAutoSaveToggleText()
    {
        if (AutoSaveStatus != null)
        {
            AutoSaveStatus.Text = AutoSaveToggle.IsChecked == true ? "● 自動保存 オン" : "○ 自動保存 オフ";
            AutoSaveStatus.Foreground = ThemeService.Brush(AutoSaveToggle.IsChecked == true ? "AccentTextBrush" : "SecondaryText");
        }
        UpdateUnsavedChangesIndicator();
    }
    void ClearPendingActions()
    {
        while (actionQueue.TryTake(out _))
        {
        } while (dragActionQueue.TryTake(out _))
        {
        }
        // Never discard taskbar click replays. Their physical Down/Up pair was
        // already consumed, so removing a queued replay makes Windows appear
        // globally unclickable on the taskbar until RELYR is stopped.
        InputEngine.EndModifierDrag();
        MacroPlayer.StopAll();
    }
    void OpenSettings_Click(object sender, RoutedEventArgs e)
        => OpenSettingsFrom(this);

}
