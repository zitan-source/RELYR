using System.Windows;
using System.Windows.Input;

namespace RELYR;

public partial class MainWindow
{
    const int EditorHistoryLimit = 50;
    readonly List<AppConfig> editorUndoHistory = [];
    readonly List<AppConfig> editorRedoHistory = [];
    AppConfig? editorHistoryCheckpoint;
    bool restoringEditorHistory;
    bool editorHistoryTransactionActive;
    bool editorHistoryTransactionRecorded;

    void InitializeEditorHistory()
    {
        editorHistoryCheckpoint = store.Clone(config);
        UpdateEditorHistoryControls();
    }

    void ResetEditorHistory()
    {
        editorUndoHistory.Clear();
        editorRedoHistory.Clear();
        editorHistoryTransactionActive = false;
        editorHistoryTransactionRecorded = false;
        editorHistoryCheckpoint = store.Clone(config);
        UpdateEditorHistoryControls();
    }

    void SynchronizeEditorHistoryCheckpoint()
    {
        editorHistoryTransactionActive = false;
        editorHistoryTransactionRecorded = false;
        if (!restoringEditorHistory)
            editorHistoryCheckpoint = store.Clone(config);
        UpdateEditorHistoryControls();
    }

    void RecordEditorHistoryChange()
    {
        if (restoringEditorHistory || loading || editorHistoryCheckpoint == null)
            return;
        if (editorHistoryTransactionActive && editorHistoryTransactionRecorded)
            return;
        editorUndoHistory.Add(editorHistoryCheckpoint);
        if (editorUndoHistory.Count > EditorHistoryLimit)
            editorUndoHistory.RemoveAt(0);
        editorRedoHistory.Clear();
        if (editorHistoryTransactionActive)
            editorHistoryTransactionRecorded = true;
        else
            editorHistoryCheckpoint = store.Clone(config);
        UpdateEditorHistoryControls();
    }

    void BeginEditorHistoryTransaction()
    {
        if (restoringEditorHistory || editorHistoryTransactionActive)
            return;
        editorHistoryTransactionActive = true;
        editorHistoryTransactionRecorded = false;
    }

    void CompleteEditorHistoryTransaction()
    {
        if (!editorHistoryTransactionActive)
            return;
        if (editorHistoryTransactionRecorded)
            editorHistoryCheckpoint = store.Clone(config);
        editorHistoryTransactionActive = false;
        editorHistoryTransactionRecorded = false;
        UpdateEditorHistoryControls();
    }

    void EditorUndo_Click(object sender, RoutedEventArgs e)
    {
        if (editorUndoHistory.Count == 0)
            return;
        CompleteEditorHistoryTransaction();
        var current = store.Clone(config);
        var target = editorUndoHistory[^1];
        editorUndoHistory.RemoveAt(editorUndoHistory.Count - 1);
        editorRedoHistory.Add(current);
        RestoreEditorHistory(target, "1つ戻しました");
    }

    void EditorRedo_Click(object sender, RoutedEventArgs e)
    {
        if (editorRedoHistory.Count == 0)
            return;
        CompleteEditorHistoryTransaction();
        var current = store.Clone(config);
        var target = editorRedoHistory[^1];
        editorRedoHistory.RemoveAt(editorRedoHistory.Count - 1);
        editorUndoHistory.Add(current);
        RestoreEditorHistory(target, "1つ進めました");
    }

    void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (TryHandleEditorHistoryShortcut(e.Key, Keyboard.Modifiers, Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase))
            e.Handled = true;
    }

    bool TryHandleEditorHistoryShortcut(Key key, ModifierKeys modifiers, bool textEditing)
    {
        if (textEditing || modifiers != ModifierKeys.Control)
            return false;
        if (key == Key.Z && editorUndoHistory.Count > 0)
        {
            EditorUndo_Click(this, new RoutedEventArgs());
            return true;
        }
        if (key == Key.Y && editorRedoHistory.Count > 0)
        {
            EditorRedo_Click(this, new RoutedEventArgs());
            return true;
        }
        return false;
    }

    void RestoreEditorHistory(AppConfig snapshot, string message)
    {
        string? selectedDeckId = selectedDeckLayout?.Id;
        bool restoreDeckEditor = deckManagementMode && DeckEditorWorkspace?.Visibility == Visibility.Visible;
        autoSaveTimer.Stop();
        restoringEditorHistory = true;
        try
        {
            ClearSelectedInput();
            config = store.Clone(snapshot);
            if (config.Profiles.Count == 0)
                return;
            if (!config.Profiles.Any(profile => profile.Name.Equals(config.ActiveProfile, StringComparison.OrdinalIgnoreCase)))
                config.ActiveProfile = config.Profiles[0].Name;
            selectedDeckLayout = selectedDeckId == null
                ? null
                : config.DeckLayouts.FirstOrDefault(layout => layout.Id.Equals(selectedDeckId, StringComparison.OrdinalIgnoreCase));
            if (IsMouseLayerBlockedByDirectGesture(config.Profiles, CurrentProfile.Name, currentLayer))
                currentLayer = "通常";
            RefreshProfiles();
            UpdateLayerButtons();
            RefreshActionPalette();
            if (deckManagementMode)
            {
                if (restoreDeckEditor && selectedDeckLayout != null)
                    EditDeckLayout(selectedDeckLayout);
                else
                    ShowDeckLayoutList();
            }
            else
                ColorButtons();
            editorHistoryCheckpoint = store.Clone(config);
            MarkDirty();
            LastInput.Text = message;
            LastInput.Foreground = ThemeService.Brush("AccentTextBrush");
        }
        finally
        {
            restoringEditorHistory = false;
            UpdateEditorHistoryControls();
        }
    }

    void UpdateEditorHistoryControls()
    {
        if (EditorUndoButton == null || EditorRedoButton == null)
            return;
        EditorUndoButton.IsEnabled = editorUndoHistory.Count > 0;
        EditorRedoButton.IsEnabled = editorRedoHistory.Count > 0;
    }
}
