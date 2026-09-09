using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Button = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;

namespace RELYR;

public partial class MainWindow
{
    CatalogAction? assignmentTapSummaryAction;
    CatalogAction? assignmentHoldSummaryAction;

    void UpdateAssignmentSummary()
    {
        if (AssignmentSummaryPanel == null || ActionPaletteContextText == null)
            return;

        UpdateActionPaletteContext();
        if (selected == null)
            return;

        bool deckInput = DeckPanelLayout.IsInputName(selected.Input);
        bool nativeShortPress = InputAssignmentPolicy.PreservesNativeShortPress(selected.Input);
        string nativeShortPressName = InputAssignmentPolicy.NativeShortPressDisplayName(selected.Input) ?? "元の入力";
        AssignmentActionSectionLabel.Text = deckInput ? "Action" : LocalizationService.Text("割り当て");
        AssignmentTapSlotText.Text = deckInput ? "ACTION" : "TAP";
        AssignmentHoldCard.Visibility = deckInput ? Visibility.Collapsed : Visibility.Visible;
        AssignmentReplaceHintText.Text = LocalizationService.Text(deckInput
            ? "変更はActionをDeckボタンへドラッグ"
            : nativeShortPress ? "ActionはHOLDへドラッグ" : "変更はActionをキーのTAP / HOLDへドラッグ");

        if (deckInput)
        {
            assignmentTapSummaryAction = null;
            assignmentHoldSummaryAction = null;
            UpdateAssignmentSummaryInteraction(AssignmentTapCard, AssignmentTapFavoriteButton, AssignmentTapDeleteButton, null);
            UpdateAssignmentSummaryInteraction(AssignmentHoldCard, AssignmentHoldFavoriteButton, AssignmentHoldDeleteButton, null);
            UpdateDeckAssignmentSummary(selected);
            AssignmentHoldTimingPanel.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateAssignmentCard(
            AssignmentTapCard,
            AssignmentTapNameText,
            AssignmentTapDetailText,
            !nativeShortPress && HasConfiguredShortAction(selected)
                ? CreateAssignmentToolTipRow("TAP", selected.Kind, selected.Value, config)
                : null,
            emptyName: nativeShortPress ? nativeShortPressName : "元の入力",
            emptyDetail: nativeShortPress ? "TAPは変更できません" : "TAPは未設定");
        assignmentTapSummaryAction = !nativeShortPress && HasConfiguredShortAction(selected)
            ? CatalogActionForAssignment(selected.Kind, selected.Value)
            : null;
        UpdateAssignmentSummaryInteraction(AssignmentTapCard, AssignmentTapFavoriteButton, AssignmentTapDeleteButton, assignmentTapSummaryAction);

        IReadOnlyList<Mapping> mappings = MappingCollectionForInput(selected.Input);
        bool longPressSupported = IsLongPressSupportedFor(selected, mappings);
        AssignmentToolTipRow? holdRow = HasConfiguredLongPress(selected)
            ? CreateAssignmentToolTipRow("HOLD", selected.LongPressKind, selected.LongPressValue, config)
            : null;
        if (!longPressSupported)
        {
            string reason = InputAssignmentPolicy.LongPressUnavailableReason(selected, mappings) ?? "この入力ではHOLDを使えません";
            UpdateAssignmentCard(AssignmentHoldCard, AssignmentHoldNameText, AssignmentHoldDetailText, null, "設定不可", reason);
            AssignmentHoldCard.Opacity = .7;
        }
        else
        {
            UpdateAssignmentCard(AssignmentHoldCard, AssignmentHoldNameText, AssignmentHoldDetailText, holdRow, "未設定", "ActionをHOLDへドラッグ");
            AssignmentHoldCard.Opacity = 1;
        }
        assignmentHoldSummaryAction = holdRow != null && longPressSupported
            ? CatalogActionForAssignment(selected.LongPressKind, selected.LongPressValue)
            : null;
        UpdateAssignmentSummaryInteraction(AssignmentHoldCard, AssignmentHoldFavoriteButton, AssignmentHoldDeleteButton, assignmentHoldSummaryAction);

        AssignmentHoldTimingPanel.Visibility = holdRow != null && longPressSupported
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (holdRow != null && longPressSupported)
        {
            int milliseconds = Math.Clamp(selected.LongPressMs, 250, 1000);
            bool wasLoading = loading;
            loading = true;
            LongPressDurationSlider.Value = milliseconds;
            loading = wasLoading;
            AssignmentHoldDurationText.Text = FormatLongPressSeconds(milliseconds);
        }
    }

    CatalogAction CatalogActionForAssignment(ActionKind kind, string value)
    {
        string signature = ActionPaletteSignature(kind, value);
        CatalogAction? known = actionPaletteItems.FirstOrDefault(item =>
            ActionPaletteSignature(item.Action.Kind, item.Action.Value).Equals(signature, StringComparison.OrdinalIgnoreCase))?.Action
            ?? ActionCatalog.Items.FirstOrDefault(action =>
                ActionPaletteSignature(action.Kind, action.Value).Equals(signature, StringComparison.OrdinalIgnoreCase));
        return known ?? new CatalogAction(
            "使用中のAction",
            FriendlyActionValue(kind, value),
            "割り当て済みのAction",
            kind,
            value);
    }

    void UpdateAssignmentSummaryInteraction(Border card, Button favoriteButton, Button deleteButton, CatalogAction? action)
    {
        card.Cursor = action == null ? WpfCursors.Arrow : WpfCursors.Hand;
        favoriteButton.Visibility = action == null ? Visibility.Collapsed : Visibility.Visible;
        deleteButton.Visibility = action == null ? Visibility.Collapsed : Visibility.Visible;
        if (action == null)
            return;
        if (card.ToolTip is string existingToolTip && !existingToolTip.Contains("Ctrl+ドラッグ", StringComparison.Ordinal))
            card.ToolTip = existingToolTip + "\nドラッグで移動 / Ctrl+ドラッグでコピー";
        bool favorite = config.ActionPaletteFavorites.Contains(ActionPaletteSignature(action.Kind, action.Value), StringComparer.OrdinalIgnoreCase);
        favoriteButton.Content = new TextBlock { Text = favorite ? "★" : "☆", FontSize = 15 };
        favoriteButton.Foreground = ThemeService.Brush(favorite ? "ActionTextIconBrush" : "MutedText");
        favoriteButton.ToolTip = favorite ? "お気に入りから外す" : "お気に入りに追加";
    }

    void AssignmentActionFavorite_Click(object sender, RoutedEventArgs e)
    {
        CatalogAction? action = ReferenceEquals(sender, AssignmentTapFavoriteButton)
            ? assignmentTapSummaryAction
            : ReferenceEquals(sender, AssignmentHoldFavoriteButton) ? assignmentHoldSummaryAction : null;
        if (action == null)
            return;
        ToggleActionPaletteFavorite(action);
        UpdateAssignmentSummary();
        e.Handled = true;
    }

    void AssignmentActionDelete_Click(object sender, RoutedEventArgs e)
    {
        AssignmentDropSlot slot = ReferenceEquals(sender, AssignmentHoldDeleteButton)
            ? AssignmentDropSlot.LongPress
            : AssignmentDropSlot.ShortPress;
        DeleteAssignmentAction(slot);
        e.Handled = true;
    }

    internal bool DeleteAssignmentAction(AssignmentDropSlot slot)
    {
        if (selected == null || DeckPanelLayout.IsInputName(selected.Input))
            return false;
        string input = selected.Input;
        List<Mapping> mappings = MappingCollectionForInput(input);
        Mapping? mapping = mappings.Contains(selected)
            ? selected
            : mappings.LastOrDefault(candidate => candidate.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
        bool configured = slot == AssignmentDropSlot.LongPress
            ? HasConfiguredLongPress(mapping)
            : HasConfiguredShortAction(mapping) && !InputAssignmentPolicy.PreservesNativeShortPress(input);
        if (mapping == null || !configured)
            return false;

        var snapshot = CapturePaletteAssignment(input);
        if (slot == AssignmentDropSlot.LongPress)
        {
            mapping.LongPressKind = ActionKind.None;
            mapping.LongPressValue = string.Empty;
        }
        else
        {
            mapping.Kind = ActionKind.None;
            mapping.Value = string.Empty;
        }
        if (!MappingHasConfiguredAction(mapping))
            mappings.Remove(mapping);
        else
            NormalizeLongOnlyMapping(mapping);
        InputAssignmentPolicy.SanitizeMappings(CurrentProfile.Mappings);

        string slotLabel = slot == AssignmentDropSlot.LongPress ? "HOLD" : "TAP";
        string message = $"{DisplayInputName(input)} の {slotLabel} Actionを削除しました";
        actionPaletteUndoState = new ActionPaletteUndoState([snapshot], message);
        ShowActionPaletteUndo(message);
        CommitPaletteAssignment(message, [input]);
        SelectInput(input, false);
        RefreshActionPalette();
        ColorButtons();
        return true;
    }

    void UpdateDeckAssignmentSummary(Mapping mapping)
    {
        AssignmentToolTipRow? row = HasConfiguredShortAction(mapping)
            ? CreateAssignmentToolTipRow("ACTION", mapping.Kind, mapping.Value, config)
            : null;
        if (row != null)
        {
            UpdateAssignmentCard(AssignmentTapCard, AssignmentTapNameText, AssignmentTapDetailText, row, "未設定", "Actionをドラッグ");
            return;
        }

        if (DeckMonitorCatalog.TryGet(mapping.DeckMonitor, out var monitor))
        {
            UpdateAssignmentCard(
                AssignmentTapCard,
                AssignmentTapNameText,
                AssignmentTapDetailText,
                new AssignmentToolTipRow("ACTION", monitor.Name, "モニター", []),
                "未設定",
                "Actionをドラッグ");
            return;
        }

        if (DeckPanelLayout.HasRegisteredFile(mapping))
        {
            UpdateAssignmentCard(
                AssignmentTapCard,
                AssignmentTapNameText,
                AssignmentTapDetailText,
                new AssignmentToolTipRow("ACTION", DeckPanelLayout.FileDisplayName(mapping), "ファイル", []),
                "未設定",
                "Actionをドラッグ");
            return;
        }

        UpdateAssignmentCard(AssignmentTapCard, AssignmentTapNameText, AssignmentTapDetailText, null, "未設定", "ActionをDeckボタンへドラッグ");
    }

    static void UpdateAssignmentCard(
        System.Windows.Controls.Border card,
        System.Windows.Controls.TextBlock nameText,
        System.Windows.Controls.TextBlock detailText,
        AssignmentToolTipRow? row,
        string emptyName,
        string emptyDetail)
    {
        nameText.Text = LocalizationService.Text(row?.Name ?? emptyName);
        string detail = row == null
            ? emptyDetail
            : row.Keycaps.Count > 0
                ? string.Join(" + ", row.Keycaps)
                : row.Detail;
        detailText.Text = LocalizationService.Text(string.IsNullOrWhiteSpace(detail) ? ActionSummaryFallbackDetail(row) : detail);
        card.ToolTip = row == null
            ? null
            : string.IsNullOrWhiteSpace(detail)
                ? row.Name
                : $"{row.Name}\n{detail}";
    }

    static string ActionSummaryFallbackDetail(AssignmentToolTipRow? row)
        => row?.Name switch
        {
            "入力しない" => "無効化",
            _ => "Action"
        };

    void UpdateActionPaletteContext()
    {
        if (ActionPaletteContextText == null)
            return;
        if (MultiSelectToggle?.IsChecked == true && multiSelectedInputs.Count > 0)
        {
            ActionPaletteContextText.Text = LocalizationService.Text($"{multiSelectedInputs.Count}個の入力へドラッグ");
            return;
        }
        if (selected == null)
        {
            ActionPaletteContextText.Text = LocalizationService.Text(deckManagementMode ? "Deckボタンへドラッグ" : "キーへドラッグ");
            return;
        }
        ActionPaletteContextText.Text = LocalizationService.Text(DeckPanelLayout.IsInputName(selected.Input)
            ? $"{DisplayInputName(selected.Input)} へドラッグ"
            : $"{DisplayInputName(selected.Input)} の TAP / HOLDへドラッグ");
    }

    void LongPressDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (loading || selected == null || DeckPanelLayout.IsInputName(selected.Input) || !HasConfiguredLongPress(selected))
            return;

        int milliseconds = Math.Clamp((int)Math.Round(e.NewValue / 50d) * 50, 250, 1000);
        if (selected.LongPressMs == milliseconds)
            return;
        selected.LongPressMs = milliseconds;
        AssignmentHoldDurationText.Text = FormatLongPressSeconds(milliseconds);

        bool wasLoading = loading;
        loading = true;
        LongPressBox.Text = milliseconds.ToString(CultureInfo.InvariantCulture);
        loading = wasLoading;

        var mappings = MappingCollectionForInput(selected.Input);
        if (MappingHasConfiguredAction(selected) && !mappings.Contains(selected))
            mappings.Add(selected);
        selected.Layer = currentLayer;
        MarkDirty(refreshDeckPanel: false);
        LastInput.Text = $"HOLDの判定を{FormatLongPressSeconds(milliseconds)}に変更しました";
        LastInput.Foreground = ThemeService.Brush("AccentTextBrush");
    }

    static string FormatLongPressSeconds(int milliseconds)
        => (milliseconds / 1000d).ToString("0.##", CultureInfo.CurrentCulture) + "秒";
}
