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
            || sourceInput.Equals(targetInput, StringComparison.OrdinalIgnoreCase))
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
        if (sender is not Button { Tag: string targetKey } target
            || !e.Data.GetDataPresent(AssignmentDragFormat)
            || e.Data.GetData(AssignmentDragFormat) is not string sourceInput)
            return;
        string targetInput = InputForCurrentLayer(targetKey);
        bool valid = !sourceInput.Equals(targetInput, StringComparison.OrdinalIgnoreCase)
            && CanUseAssignmentDragKey(targetKey, source: false);
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
        if (IsProtectedNormalLeftClick(key)
            || key.Equals("CapsLock", StringComparison.OrdinalIgnoreCase)
            || key.Equals("Space", StringComparison.OrdinalIgnoreCase) && currentLayer is "通常" or "Space")
            return false;
        if (!source)
            return true;
        string input = InputForCurrentLayer(key);
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
        SetInputVisualZIndex(button, active ? 50 : button.IsMouseOver ? 20 : 0);
        AnimateInputScale(button, active ? 1.06 : button.IsMouseOver ? 1.05 : 1, 120);
    }

    void InputButtonHoverEntered(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Button button && !GetIsAssignmentDropTarget(button))
        {
            SetInputVisualZIndex(button, 20);
            AnimateInputScale(button, 1.05, 120);
        }
    }

    void InputButtonHoverExited(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Button button && !GetIsAssignmentDropTarget(button))
        {
            SetInputVisualZIndex(button, 0);
            AnimateInputScale(button, 1, 150);
        }
    }

    static void SetInputVisualZIndex(Button button, int value)
    {
        System.Windows.Controls.Panel.SetZIndex(button, value);
        if (button.Parent is UIElement parent && parent is not System.Windows.Controls.Canvas)
            System.Windows.Controls.Panel.SetZIndex(parent, value);
    }

    static ScaleTransform InputScaleTransform(Button button)
    {
        button.RenderTransformOrigin = new Point(.5, .5);
        if (button.RenderTransform is ScaleTransform scale)
            return scale;
        scale = new ScaleTransform(1, 1);
        button.RenderTransform = scale;
        return scale;
    }

    static void AnimateInputScale(Button button, double target, int durationMs)
    {
        var scale = InputScaleTransform(button);
        var duration = TimeSpan.FromMilliseconds(durationMs);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } }, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } }, HandoffBehavior.SnapshotAndReplace);
    }
}
