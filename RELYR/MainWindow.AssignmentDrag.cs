using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Button = System.Windows.Controls.Button;
using DataObject = System.Windows.DataObject;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using GiveFeedbackEventHandler = System.Windows.GiveFeedbackEventHandler;
using IDataObject = System.Windows.IDataObject;
using Point = System.Windows.Point;

namespace RELYR;

internal enum AssignmentTransferResult
{
    None,
    Moved,
    Swapped
}

internal enum AssignmentDropSlot
{
    ShortPress,
    LongPress
}

public partial class MainWindow
{
    const string AssignmentDragFormat = "RELYR.AssignmentInput.v1";
    const string AssignmentActionMoveFormat = "RELYR.AssignmentActionMove.v1";
    internal const double AssignmentDropTargetScale = 1.18;
    Button? assignmentDragSource;
    Button? assignmentDropTarget;
    CatalogAction? assignmentPaletteDropAction;
    AssignmentDropSlot assignmentDropSlot;
    string assignmentDropUnavailableReason = string.Empty;
    Point assignmentDragStart;
    Border? assignmentActionDragSource;
    Point assignmentActionDragStart;

    sealed record AssignmentActionMovePayload(string SourceInput, AssignmentDropSlot SourceSlot, CatalogAction Action);

    void AssignmentActionCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (selected == null || DeckPanelLayout.IsInputName(selected.Input) || MultiSelectToggle.IsChecked == true
            || sender is not Border card || IsAssignmentFavoriteSource(e.OriginalSource as DependencyObject))
            return;
        AssignmentDropSlot slot = ReferenceEquals(card, AssignmentHoldCard)
            ? AssignmentDropSlot.LongPress
            : AssignmentDropSlot.ShortPress;
        CatalogAction? action = slot == AssignmentDropSlot.LongPress ? assignmentHoldSummaryAction : assignmentTapSummaryAction;
        if (action == null || CurrentProfile.Mappings.All(mapping => !ReferenceEquals(mapping, selected)))
            return;
        assignmentActionDragSource = card;
        assignmentActionDragStart = e.GetPosition(card);
    }

    static bool IsAssignmentFavoriteSource(DependencyObject? source)
    {
        for (DependencyObject? current = source; current != null; current = VisualTreeHelper.GetParent(current))
            if (current is Button button && (button.Name == "AssignmentTapFavoriteButton" || button.Name == "AssignmentHoldFavoriteButton"))
                return true;
        return false;
    }

    void AssignmentActionCard_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (selected == null || sender is not Border card || !ReferenceEquals(card, assignmentActionDragSource)
            || e.LeftButton != MouseButtonState.Pressed)
            return;
        Point current = e.GetPosition(card);
        if (Math.Abs(current.X - assignmentActionDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - assignmentActionDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        AssignmentDropSlot slot = ReferenceEquals(card, AssignmentHoldCard)
            ? AssignmentDropSlot.LongPress
            : AssignmentDropSlot.ShortPress;
        CatalogAction? action = slot == AssignmentDropSlot.LongPress ? assignmentHoldSummaryAction : assignmentTapSummaryAction;
        assignmentActionDragSource = null;
        if (action == null)
            return;
        var data = new DataObject();
        data.SetData(AssignmentActionMoveFormat, new AssignmentActionMovePayload(selected.Input, slot, action));
        data.SetData(ActionPaletteDragFormat, action);
        RunActionPaletteDrag(card, AssignmentPaletteItemFor(action), data, DragDropEffects.Move);
        e.Handled = true;
    }

    void AssignmentActionCard_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => assignmentActionDragSource = null;

    ActionPaletteItem AssignmentPaletteItemFor(CatalogAction action)
        => actionPaletteItems.FirstOrDefault(item => item.Action.Kind == action.Kind
            && item.Action.Value.Equals(action.Value, StringComparison.OrdinalIgnoreCase))
            ?? new ActionPaletteItem(
                action,
                action.Name,
                ActionPaletteGroup(action),
                ActionPaletteItemDetail(action, ActionPaletteGroup(action)),
                ActionPaletteGlyph(action),
                0,
                config.ActionPaletteFavorites.Contains(ActionPaletteSignature(action.Kind, action.Value), StringComparer.OrdinalIgnoreCase));

    internal static AssignmentTransferResult TransferAssignments(List<Mapping> mappings, string sourceInput, string targetInput)
    {
        if (string.IsNullOrWhiteSpace(sourceInput) || string.IsNullOrWhiteSpace(targetInput)
            || sourceInput.Equals(targetInput, StringComparison.OrdinalIgnoreCase)
            || !CanTransferAssignments(mappings, sourceInput, targetInput))
            return AssignmentTransferResult.None;

        var sourceMappings = mappings.Where(mapping => mapping.Input.Equals(sourceInput, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (sourceMappings.Length == 0)
            return AssignmentTransferResult.None;
        var targetMappings = mappings.Where(mapping => mapping.Input.Equals(targetInput, StringComparison.OrdinalIgnoreCase)).ToArray();
        string sourceLayer = AssignmentLayerName(sourceInput);
        string targetLayer = AssignmentLayerName(targetInput);

        foreach (var mapping in sourceMappings)
        {
            mapping.Input = targetInput;
            mapping.Layer = targetLayer;
        }
        foreach (var mapping in targetMappings)
        {
            mapping.Input = sourceInput;
            mapping.Layer = sourceLayer;
        }
        return targetMappings.Length == 0 ? AssignmentTransferResult.Moved : AssignmentTransferResult.Swapped;
    }

    internal static bool CanTransferAssignments(IReadOnlyList<Mapping> mappings, string sourceInput, string targetInput)
    {
        if (string.IsNullOrWhiteSpace(sourceInput) || string.IsNullOrWhiteSpace(targetInput)
            || sourceInput.Equals(targetInput, StringComparison.OrdinalIgnoreCase))
            return false;
        var projected = mappings.Select(mapping => mapping.Copy()).ToList();
        var sourceMappings = projected.Where(mapping => mapping.Input.Equals(sourceInput, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (sourceMappings.Length == 0)
            return false;
        var targetMappings = projected.Where(mapping => mapping.Input.Equals(targetInput, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var mapping in sourceMappings)
        {
            mapping.Input = targetInput;
            mapping.Layer = AssignmentLayerName(targetInput);
        }
        foreach (var mapping in targetMappings)
        {
            mapping.Input = sourceInput;
            mapping.Layer = AssignmentLayerName(sourceInput);
        }
        return sourceMappings.Concat(targetMappings).All(mapping =>
            !InputAssignmentPolicy.IsUnreachableInput(mapping.Input)
            && (mapping.Kind != ActionKind.Gesture || InputAssignmentPolicy.SupportsGesture(mapping.Input))
            && (!InputAssignmentPolicy.HasConfiguredLongPress(mapping)
                || InputAssignmentPolicy.CanExecuteLongPress(mapping, projected)));
    }

    static string AssignmentLayerName(string input)
    {
        int separator = input.IndexOf('+');
        return separator > 0 ? input[..separator] : "通常";
    }

    void InputAssignmentDragStarted(object sender, MouseButtonEventArgs e)
    {
        if (MultiSelectToggle.IsChecked == true || deckManagementMode
            || sender is not Button { Tag: string key } button
            || !CanUseAssignmentDragKey(key, source: true))
            return;
        assignmentDragSource = button;
        assignmentDragStart = e.GetPosition(button);
    }

    void InputAssignmentDragMoved(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Button button || !ReferenceEquals(button, assignmentDragSource)
            || e.LeftButton != MouseButtonState.Pressed || button.Tag is not string key)
            return;
        Point point = e.GetPosition(button);
        if (Math.Abs(point.X - assignmentDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(point.Y - assignmentDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        string input = InputForCurrentLayer(key);
        assignmentDragSource = null;
        var data = new DataObject();
        data.SetData(AssignmentDragFormat, input);
        try
        {
            RunAssignmentEditorDrag(button, input, data);
        }
        finally
        {
            ClearAssignmentDropTarget();
        }
        e.Handled = true;
    }

    void InputAssignmentDragEnded(object sender, MouseButtonEventArgs e)
    {
        assignmentDragSource = null;
        ClearAssignmentDropTarget();
    }

    void InputAssignmentDragOver(object sender, DragEventArgs e)
    {
        if (sender is Button { Tag: string moveTargetKey } moveTarget
            && TryGetAssignmentActionMove(e.Data, out AssignmentActionMovePayload move))
        {
            string moveTargetInput = InputForCurrentLayer(moveTargetKey);
            AssignmentDropSlot slot = DropSlotAt(moveTargetInput, moveTarget, e.GetPosition(moveTarget));
            bool moveValid = CanMoveAssignmentAction(move, moveTargetInput, moveTargetKey, slot);
            SetAssignmentDropTarget(moveValid ? moveTarget : null, move.Action, slot);
            e.Effects = moveValid ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (sender is Button { Tag: string paletteTargetKey } paletteTarget
            && TryGetPaletteAction(e.Data, out CatalogAction paletteAction))
        {
            string paletteTargetInput = InputForCurrentLayer(paletteTargetKey);
            bool shortValid = CanAssignPaletteAction(paletteTargetInput, paletteAction);
            if (!shortValid)
            {
                SetAssignmentDropTarget(null);
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }
            AssignmentDropSlot slot = DropSlotAt(paletteTargetInput, paletteTarget, e.GetPosition(paletteTarget));
            bool slotValid = CanAssignPaletteDropToSlot(paletteAction, paletteTargetInput, paletteTargetKey, slot);
            SetAssignmentDropTarget(paletteTarget, paletteAction, slot);
            if (slot == AssignmentDropSlot.LongPress && !slotValid)
            {
                string reason = PaletteLongPressDropUnavailableReason(paletteAction, paletteTargetInput, paletteTargetKey);
                if (!reason.Equals(assignmentDropUnavailableReason, StringComparison.Ordinal))
                {
                    assignmentDropUnavailableReason = reason;
                    ShowInlineNotice(reason);
                }
            }
            else
                assignmentDropUnavailableReason = string.Empty;
            e.Effects = slotValid ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (sender is not Button { Tag: string targetKey } target
            || !e.Data.GetDataPresent(AssignmentDragFormat)
            || e.Data.GetData(AssignmentDragFormat) is not string sourceInput)
            return;
        string targetInput = InputForCurrentLayer(targetKey);
        bool valid = !sourceInput.Equals(targetInput, StringComparison.OrdinalIgnoreCase)
            && CanUseAssignmentDragKey(targetKey, source: false)
            && CanTransferAssignments(CurrentProfile.Mappings, sourceInput, targetInput);
        SetAssignmentDropTarget(valid ? target : null);
        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    void InputAssignmentDragLeave(object sender, DragEventArgs e)
    {
        if (ReferenceEquals(sender, assignmentDropTarget))
            ClearAssignmentDropTarget();
    }

    void InputAssignmentDropped(object sender, DragEventArgs e)
    {
        if (sender is Button { Tag: string moveTargetKey } moveTarget
            && TryGetAssignmentActionMove(e.Data, out AssignmentActionMovePayload move))
        {
            string moveTargetInput = InputForCurrentLayer(moveTargetKey);
            AssignmentDropSlot slot = DropSlotAt(moveTargetInput, moveTarget, e.GetPosition(moveTarget));
            ClearAssignmentDropTarget();
            bool moved = ApplyAssignmentActionMove(move, moveTargetInput, moveTargetKey, slot);
            e.Effects = moved ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (sender is Button { Tag: string paletteTargetKey }
            && TryGetPaletteAction(e.Data, out CatalogAction paletteAction))
        {
            string paletteTargetInput = InputForCurrentLayer(paletteTargetKey);
            AssignmentDropSlot slot = DropSlotAt(paletteTargetInput, (Button)sender, e.GetPosition((Button)sender));
            bool valid = CanAssignPaletteDropToSlot(paletteAction, paletteTargetInput, paletteTargetKey, slot);
            ClearAssignmentDropTarget();
            bool applied = valid
                && ApplyPaletteActionDrop(paletteAction, paletteTargetInput, paletteTargetKey, slot);
            e.Effects = applied ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (sender is not Button { Tag: string targetKey }
            || !e.Data.GetDataPresent(AssignmentDragFormat)
            || e.Data.GetData(AssignmentDragFormat) is not string sourceInput
            || !CanUseAssignmentDragKey(targetKey, source: false))
            return;

        string targetInput = InputForCurrentLayer(targetKey);
        ClearAssignmentDropTarget();
        AssignmentTransferResult result = TransferAssignments(CurrentProfile.Mappings, sourceInput, targetInput);
        if (result == AssignmentTransferResult.None)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        SelectInput(targetInput, false);
        UpdateLayerButtons();
        ColorButtons();
        MarkDirty();
        ShowInlineNotice(result == AssignmentTransferResult.Swapped
            ? $"{DisplayInputName(sourceInput)} と {DisplayInputName(targetInput)} のActionを入れ替えました"
            : $"{DisplayInputName(sourceInput)} のActionを {DisplayInputName(targetInput)} へ移動しました");
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    static bool TryGetAssignmentActionMove(IDataObject data, out AssignmentActionMovePayload payload)
    {
        payload = null!;
        if (!data.GetDataPresent(AssignmentActionMoveFormat)
            || data.GetData(AssignmentActionMoveFormat) is not AssignmentActionMovePayload value)
            return false;
        payload = value;
        return true;
    }

    bool CanMoveAssignmentAction(AssignmentActionMovePayload payload, string targetInput, string targetKey, AssignmentDropSlot targetSlot)
    {
        if (MultiSelectToggle.IsChecked == true
            || payload.SourceInput.Equals(targetInput, StringComparison.OrdinalIgnoreCase) && payload.SourceSlot == targetSlot)
            return false;
        Mapping? source = CurrentProfile.Mappings.LastOrDefault(mapping =>
            mapping.Input.Equals(payload.SourceInput, StringComparison.OrdinalIgnoreCase));
        if (source == null)
            return false;
        bool sourceStillMatches = payload.SourceSlot == AssignmentDropSlot.LongPress
            ? HasConfiguredLongPress(source)
                && source.LongPressKind == payload.Action.Kind
                && source.LongPressValue.Equals(payload.Action.Value, StringComparison.OrdinalIgnoreCase)
            : HasConfiguredShortAction(source)
                && source.Kind == payload.Action.Kind
                && source.Value.Equals(payload.Action.Value, StringComparison.OrdinalIgnoreCase);
        return sourceStillMatches
            && CanAssignPaletteDropToSlot(payload.Action, targetInput, targetKey, targetSlot);
    }

    bool ApplyAssignmentActionMove(AssignmentActionMovePayload payload, string targetInput, string targetKey, AssignmentDropSlot targetSlot)
    {
        if (!CanMoveAssignmentAction(payload, targetInput, targetKey, targetSlot))
            return false;
        string[] affectedInputs = [.. new[] { payload.SourceInput, targetInput }.Distinct(StringComparer.OrdinalIgnoreCase)];
        var snapshots = affectedInputs.Select(CapturePaletteAssignment).ToArray();
        ApplyPaletteActionToInput(payload.Action, targetInput, targetSlot);

        Mapping? source = CurrentProfile.Mappings.LastOrDefault(mapping =>
            mapping.Input.Equals(payload.SourceInput, StringComparison.OrdinalIgnoreCase));
        if (source == null)
            return false;
        if (payload.SourceSlot == AssignmentDropSlot.LongPress)
        {
            source.LongPressKind = ActionKind.None;
            source.LongPressValue = string.Empty;
        }
        else
        {
            source.Kind = ActionKind.None;
            source.Value = string.Empty;
        }
        if (!MappingHasConfiguredAction(source))
            CurrentProfile.Mappings.Remove(source);
        else
            NormalizeLongOnlyMapping(source);
        InputAssignmentPolicy.SanitizeMappings(CurrentProfile.Mappings);
        RememberRecentPaletteAction(payload.Action);

        string sourceSlot = payload.SourceSlot == AssignmentDropSlot.LongPress ? "HOLD" : "TAP";
        string targetSlotLabel = targetSlot == AssignmentDropSlot.LongPress ? "HOLD" : "TAP";
        string message = $"{DisplayInputName(payload.SourceInput)} の {sourceSlot} を {DisplayInputName(targetInput)} の {targetSlotLabel} へ移動しました";
        actionPaletteUndoState = new ActionPaletteUndoState(snapshots, message);
        ShowActionPaletteUndo(message);
        CommitPaletteAssignment(message, affectedInputs);
        if (!config.AutoSave)
            PersistActionPaletteLibraryPreferences();
        SelectInput(targetInput, false);
        PlayPaletteDropSuccess([targetInput]);
        RefreshActionPalette();
        ColorButtons();
        return true;
    }

    bool CanUseAssignmentDragKey(string key, bool source)
    {
        string input = InputForCurrentLayer(key);
        if (InputAssignmentPolicy.IsUnreachableInput(input)
            || IsProtectedNormalLeftClick(key)
            || key.Equals("CapsLock", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Space", StringComparison.OrdinalIgnoreCase) && currentLayer is "通常" or "Space")
            return false;
        if (!source)
            return true;
        return CurrentProfile.Mappings.Any(mapping => mapping.Input.Equals(input, StringComparison.OrdinalIgnoreCase) && MappingInterceptsInput(mapping));
    }

    static void RunAssignmentEditorDrag(Button button, string input, DataObject data)
    {
        DeckDragPreviewWindow? preview = null;
        GiveFeedbackEventHandler? feedback = null;
        try
        {
            string label = button.Content is string text ? text : input[(input.LastIndexOf('+') + 1)..];
            var face = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(2),
                Background = button.Background,
                BorderBrush = button.BorderBrush,
                BorderThickness = new Thickness(1),
                Child = new Viewbox
                {
                    Stretch = Stretch.Uniform,
                    Child = new TextBlock { Text = label, Foreground = button.Foreground, FontWeight = FontWeights.SemiBold }
                }
            };
            preview = new DeckDragPreviewWindow(face, compact: true);
            feedback = (_, args) =>
            {
                var cursor = System.Windows.Forms.Cursor.Position;
                preview.MoveToPhysical(cursor.X, cursor.Y);
                args.UseDefaultCursors = false;
                args.Handled = true;
            };
            button.GiveFeedback += feedback;
            preview.Show();
            var initialCursor = System.Windows.Forms.Cursor.Position;
            preview.MoveToPhysical(initialCursor.X, initialCursor.Y);
            DragDrop.DoDragDrop(button, data, DragDropEffects.Move);
        }
        finally
        {
            if (feedback != null)
                button.GiveFeedback -= feedback;
            preview?.Close();
        }
    }

    static AssignmentDropSlot DropSlotAt(string input, Button target, Point position)
        => !DeckPanelLayout.IsInputName(input) && target.ActualHeight > 0 && position.Y >= target.ActualHeight / 2
            ? AssignmentDropSlot.LongPress
            : AssignmentDropSlot.ShortPress;

    void SetAssignmentDropTarget(Button? target, CatalogAction? paletteAction = null, AssignmentDropSlot slot = AssignmentDropSlot.ShortPress)
    {
        bool palette = paletteAction != null
            && target?.Tag is string targetKey
            && !DeckPanelLayout.IsInputName(InputForCurrentLayer(targetKey));
        bool longPressAvailable = target?.Tag is string key && paletteAction != null
            && CanAssignPaletteDropToSlot(paletteAction, InputForCurrentLayer(key), key, AssignmentDropSlot.LongPress);
        if (ReferenceEquals(target, assignmentDropTarget)
            && Equals(paletteAction, assignmentPaletteDropAction)
            && slot == assignmentDropSlot)
        {
            if (target != null)
            {
                SetAssignmentDropTargetVisual(target, true, palette, slot, longPressAvailable);
                RepositionActionPaletteDragPreview();
            }
            return;
        }
        ClearAssignmentDropTarget();
        if (target == null)
            return;
        assignmentDropTarget = target;
        assignmentPaletteDropAction = paletteAction;
        assignmentDropSlot = slot;
        SetAssignmentDropTargetVisual(target, true, palette, slot, longPressAvailable);
        RepositionActionPaletteDragPreview();
    }

    void ClearAssignmentDropTarget()
    {
        if (assignmentDropTarget == null)
            return;
        var target = assignmentDropTarget;
        assignmentDropTarget = null;
        assignmentPaletteDropAction = null;
        assignmentDropSlot = AssignmentDropSlot.ShortPress;
        assignmentDropUnavailableReason = string.Empty;
        SetAssignmentDropTargetVisual(target, false);
        UpdateInputButtonVisual(target, IsDescendantOf(target, KeyboardPanel) || IsDescendantOf(target, SecondaryKeyboardPanel));
    }

    internal static void SetAssignmentDropTargetVisual(
        Button button,
        bool active,
        bool palette = false,
        AssignmentDropSlot slot = AssignmentDropSlot.ShortPress,
        bool longPressAvailable = true)
    {
        SetIsAssignmentDropTarget(button, active);
        bool showSlots = active && palette;
        SetIsPaletteAssignmentDropTarget(button, showSlots);
        SetIsLongPressAssignmentDropSlot(button, showSlots && slot == AssignmentDropSlot.LongPress);
        SetIsLongPressAssignmentDropAvailable(button, longPressAvailable);
        button.ApplyTemplate();
        if (button.Template.FindName("DropTargetTint", button) is UIElement tint)
            tint.Opacity = 0;
        if (button.Template.FindName("DropTargetBadge", button) is UIElement badge)
            badge.Opacity = active && !palette ? 1 : 0;
        if (button.Template.FindName("AssignmentSlotOverlay", button) is UIElement slotOverlay)
            slotOverlay.Opacity = showSlots ? 1 : 0;
        if (button.Template.FindName("LongPressDropUnavailableMark", button) is UIElement unavailableMark)
            unavailableMark.Opacity = showSlots && !longPressAvailable ? 1 : 0;
        if (active)
        {
            button.BorderBrush = ThemeService.Brush("AccentBrush");
            button.BorderThickness = new Thickness(3);
            button.Opacity = 1;
        }
        SetInputVisualZIndex(button, active ? 50 : 0);
        SetInputScaleImmediately(button, showSlots ? AssignmentDropTargetScale : 1);
    }

    static void SetInputVisualZIndex(Button button, int value)
    {
        System.Windows.Controls.Panel.SetZIndex(button, value);
        if (button.Parent is UIElement parent && parent is not System.Windows.Controls.Canvas)
            System.Windows.Controls.Panel.SetZIndex(parent, value);
        for (DependencyObject? ancestor = button; ancestor != null; ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            if (ancestor is Viewbox { Name: "MouseHost" } mouseHost)
            {
                System.Windows.Controls.Panel.SetZIndex(mouseHost, value > 0 ? value : 1);
                break;
            }
        }
    }

    static ScaleTransform InputScaleTransform(Button button)
        => UiMotionService.MutableScale(button);

    static void SetInputScaleImmediately(Button button, double value)
    {
        UiMotionService.RunSafely("input-key-scale-settle", () =>
        {
            var scale = InputScaleTransform(button);
            UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleXProperty, value);
            UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleYProperty, value);
        });
    }

    static void AnimateInputScale(Button button, double target, int durationMs, IEasingFunction? easing = null)
    {
        UiMotionService.RunSafely("input-key-scale", () =>
        {
            var scale = InputScaleTransform(button);
            if (!UiMotionService.Enabled)
            {
                button.ApplyTemplate();
                if (button.Template.FindName("DropTargetTint", button) is FrameworkElement wave)
                    ResetActionDropSuccessVisual(button, wave);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = target;
                scale.ScaleY = target;
                return;
            }
            var duration = TimeSpan.FromMilliseconds(durationMs);
            var motionEase = easing ?? UiMotionService.ResponsiveEaseOut();
            UiMotionService.AnimateDouble("input-key-scale-x", scale, ScaleTransform.ScaleXProperty, target, duration, motionEase);
            UiMotionService.AnimateDouble("input-key-scale-y", scale, ScaleTransform.ScaleYProperty, target, duration, motionEase);
        });
    }
}
