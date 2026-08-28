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
using Point = System.Windows.Point;

namespace RELYR;

internal enum AssignmentTransferResult
{
    None,
    Moved,
    Swapped
}

public partial class MainWindow
{
    const string AssignmentDragFormat = "RELYR.AssignmentInput.v1";
    Button? assignmentDragSource;
    Button? assignmentDropTarget;
    Point assignmentDragStart;

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
        if (sender is Button { Tag: string paletteTargetKey } paletteTarget
            && TryGetPaletteAction(e.Data, out CatalogAction paletteAction))
        {
            string paletteTargetInput = InputForCurrentLayer(paletteTargetKey);
            bool paletteValid = CanAssignPaletteAction(paletteTargetInput, paletteAction);
            SetAssignmentDropTarget(paletteValid ? paletteTarget : null);
            e.Effects = paletteValid ? DragDropEffects.Copy : DragDropEffects.None;
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
        if (sender is Button { Tag: string paletteTargetKey }
            && TryGetPaletteAction(e.Data, out CatalogAction paletteAction))
        {
            string paletteTargetInput = InputForCurrentLayer(paletteTargetKey);
            ClearAssignmentDropTarget();
            bool applied = CanAssignPaletteAction(paletteTargetInput, paletteAction)
                && ApplyPaletteActionDrop(paletteAction, paletteTargetInput, paletteTargetKey);
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

    void SetAssignmentDropTarget(Button? target)
    {
        if (ReferenceEquals(target, assignmentDropTarget))
            return;
        ClearAssignmentDropTarget();
        if (target == null)
            return;
        assignmentDropTarget = target;
        SetAssignmentDropTargetVisual(target, true);
    }

    void ClearAssignmentDropTarget()
    {
        if (assignmentDropTarget == null)
            return;
        var target = assignmentDropTarget;
        assignmentDropTarget = null;
        SetAssignmentDropTargetVisual(target, false);
        UpdateInputButtonVisual(target, IsDescendantOf(target, KeyboardPanel) || IsDescendantOf(target, SecondaryKeyboardPanel));
    }

    internal static void SetAssignmentDropTargetVisual(Button button, bool active)
    {
        SetIsAssignmentDropTarget(button, active);
        button.ApplyTemplate();
        if (button.Template.FindName("DropTargetTint", button) is UIElement tint)
            tint.Opacity = 0;
        if (button.Template.FindName("DropTargetBadge", button) is UIElement badge)
            badge.Opacity = active ? 1 : 0;
        if (active)
        {
            button.BorderBrush = ThemeService.Brush("AccentBrush");
            button.BorderThickness = new Thickness(3);
        }
        SetInputVisualZIndex(button, active ? 50 : 0);
        SetInputScaleImmediately(button, 1);
    }

    static void SetInputVisualZIndex(Button button, int value)
    {
        System.Windows.Controls.Panel.SetZIndex(button, value);
        if (button.Parent is UIElement parent && parent is not System.Windows.Controls.Canvas)
            System.Windows.Controls.Panel.SetZIndex(parent, value);
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
