using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace RELYR;

public enum MacroStepVisualKind
{
    Keyboard,
    Mouse,
    Wait,
    Action,
    Macro,
    Text
}

public partial class MacroWindow : Window
{
    public sealed record StepView(MacroStep Step, int Number, string Title, string Detail, string DelayLabel, MacroStepVisualKind VisualKind);

    readonly AppConfig config;
    readonly Action<bool, bool, bool> setRecording;
    readonly bool allowAssignment;
    readonly HashSet<Key> manualHeld = [];
    readonly HashSet<MacroStep> recordedMovesInsideWindow = [];
    readonly HashSet<string> suppressedMappedInputs = new(StringComparer.OrdinalIgnoreCase);
    readonly Stack<List<MacroStep>> undoSteps = [];
    readonly List<MacroStep> copiedSteps = [];
    List<MacroDefinition> savedMacros = [];
    Dictionary<Mapping, (ActionKind Kind, string Value, ActionKind LongKind, string LongValue)> savedMappingActions = [];
    MacroDefinition? current;
    (int X, int Y)? lastRecordedMousePosition;
    Stopwatch? sinceLast, manualSinceLast;
    MacroStep? dragStep;
    ListBoxItem? dragTargetContainer;
    System.Windows.Media.TranslateTransform? dragTargetTransform;
    bool dragTargetAfter;
    Action<int, int>? coordinateCaptureCallback;
    int dragInsertionIndex = -1;
    System.Windows.Point dragStart;
    private bool recording;
    private bool manualCaptureActive;
    private bool coordinateCaptureActive;
    private bool loading;
    private bool refreshingList;
    private readonly bool loadingOption;
    private bool editingName;
    private bool accepted;
    private bool dirty;
    private bool closingConfirmed;
    private bool testRunning;
    private bool ignoreInitialMouseRelease;
    int recordingStartIndex;
    string nameBeforeEdit = "";
    readonly MacroStopShortcut stopShortcut = new();

    public bool Changed
    {
        get; private set;
    }
    public bool SaveRequested
    {
        get; private set;
    }
    public string? SelectedMacroName => current?.Name;
    public string? ShortcutCreatedPath
    {
        get; private set;
    }
    internal bool TitleBarUsesDarkMode
    {
        get; private set;
    }
    internal bool SuppressUnsavedPromptForTest
    {
        get; set;
    }
    public event Action? Saved;

    public MacroWindow(AppConfig config, Action<bool, bool, bool> setRecording, bool allowAssignment = false, string assignmentTarget = "")
    {
        InitializeComponent();
        this.config = config;
        this.setRecording = setRecording;
        this.allowAssignment = allowAssignment;
        CaptureSavedState();
        MainWindow.FollowWindowsTitleBarTheme(this, value => TitleBarUsesDarkMode = value);
        loadingOption = true;
        RecordKeyboardBox.IsChecked = config.RecordKeyboardInputInMacros;
        RecordMappedActionsBox.IsChecked = config.RecordMappedActionsInMacros;
        RecordPhysicalInputBox.IsChecked = !config.RecordMappedActionsInMacros;
        RecordMouseMovesBox.IsChecked = config.RecordMouseMovementInMacros;
        RelativeMouseMovementBox.IsChecked = config.RecordMouseMovementRelativeInMacros;
        FixedMousePositionBox.IsChecked = !config.RecordMouseMovementRelativeInMacros;
        loadingOption = false;
        UpdateMouseMovementModeState();
        UseButton.Visibility = allowAssignment ? Visibility.Visible : Visibility.Collapsed;
        AssignmentTargetText.Text = allowAssignment && !string.IsNullOrWhiteSpace(assignmentTarget) ? "割り当て先: " + assignmentTarget : "保存後も、この画面を開いたまま編集できます。";
        RefreshMacros();
        if (config.Macros.Count > 0)
            MacroList.SelectedItem = config.Macros[0];
        else
            SetEditorState();
        UpdateEditorModeButtons();
    }

    void MarkChanged(string? status = null)
    {
        Changed = true;
        dirty = true;
        UnsavedStatus.Text = "● 未保存の変更があります";
        if (status != null)
            FooterStatus.Text = status;
    }
    static MacroStep CloneStep(MacroStep step) => new() { Event = step.Event, DelayMs = step.DelayMs, RecordedActionKind = step.RecordedActionKind, RecordedActionValue = step.RecordedActionValue };
    static List<MacroStep> CloneSteps(IEnumerable<MacroStep> steps) => [.. steps.Select(CloneStep)];
    static MacroDefinition CloneMacro(MacroDefinition macro) => new() { Id = macro.Id, Name = macro.Name, Steps = CloneSteps(macro.Steps) };
    string UniqueMacroName(string basis)
    {
        string name = basis;
        int suffix = 2;
        while (config.Macros.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            name = $"{basis} {suffix++}";
        return name;
    }

    void RefreshMacros()
    {
        string query = MacroSearchBox?.Text.Trim() ?? "";
        var selected = current;
        var items = config.Macros.Where(x => query.Length == 0 || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        refreshingList = true;
        MacroList.ItemsSource = items;
        MacroList.SelectedItem = selected != null && items.Contains(selected) ? selected : null;
        refreshingList = false;
    }
    void MacroSearchChanged(object sender, TextChangedEventArgs e) => RefreshMacros();
    void SetEditorState()
    {
        bool available = current != null, hasSteps = available && current!.Steps.Count > 0, hasSelection = StepList?.SelectedItems.Count > 0;
        EditorPanel.IsEnabled = available;
        EditorPanel.Opacity = available ? 1 : 0;
        EditorPanel.Visibility = available ? Visibility.Visible : Visibility.Hidden;
        EmptyHint.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        EditMacroButton.IsEnabled = available;
        DeleteMacroButton.IsEnabled = available;
        DuplicateMacroButton.IsEnabled = available;
        SaveButton.IsEnabled = available && !testRunning;
        ShortcutButton.IsEnabled = hasSteps && !testRunning;
        UseButton.IsEnabled = allowAssignment && hasSteps && !testRunning;
        TestButton.IsEnabled = hasSteps && !testRunning;
        StopTestButton.IsEnabled = testRunning;
        DeleteStepButton.IsEnabled = hasSelection;
        MoveUpButton.IsEnabled = hasSelection;
        MoveDownButton.IsEnabled = hasSelection;
        ClearStepsButton.IsEnabled = hasSteps;
        UndoButton.IsEnabled = undoSteps.Count > 0;
        UpdateSelectedStepEditor();
    }
    void CreateMacro()
    {
        var macro = new MacroDefinition { Name = UniqueMacroName("マクロ 1") };
        config.Macros.Add(macro);
        current = macro;
        undoSteps.Clear();
        MarkChanged();
        RefreshMacros();
        MacroList.SelectedItem = macro;
        SetEditorState();
        BeginNameEdit();
    }
    void New_Click(object sender, RoutedEventArgs e)
    {
        StopRecording();
        StopManualCapture();
        CommitNameEdit(false);
        CreateMacro();
    }
    void DuplicateMacro_Click(object sender, RoutedEventArgs e)
    {
        if (current == null)
            return;
        StopRecording();
        StopManualCapture();
        var copy = CloneMacro(current);
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = UniqueMacroName(current.Name + " のコピー");
        config.Macros.Add(copy);
        current = copy;
        undoSteps.Clear();
        MarkChanged("マクロを複製しました。");
        RefreshMacros();
        MacroList.SelectedItem = copy;
        RefreshSteps();
        SetEditorState();
    }
    void EditMacro_Click(object sender, RoutedEventArgs e)
    {
        if (current != null)
            BeginNameEdit();
    }
    void BeginNameEdit()
    {
        if (current == null)
            return;
        nameBeforeEdit = current.Name;
        editingName = true;
        NameBox.IsReadOnly = false;
        ConfirmNameButton.Visibility = Visibility.Visible;
        NameBox.Text = current.Name;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { NameBox.Focus(); NameBox.SelectAll(); }));
    }
    bool CommitNameEdit(bool showError)
    {
        if (!editingName || current == null)
            return true;
        string name = NameBox.Text.Trim();
        if (name.Length == 0 || config.Macros.Any(x => x != current && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            if (showError)
                AppDialog.Show(this, name.Length == 0 ? "マクロ名を入力してください。" : "同じ名前のマクロがあります。", "マクロ名", MessageBoxButton.OK, MessageBoxImage.Warning);
            loading = true;
            NameBox.Text = nameBeforeEdit;
            loading = false;
            current.Name = nameBeforeEdit;
            NameBox.IsReadOnly = true;
            ConfirmNameButton.Visibility = Visibility.Collapsed;
            editingName = false;
            RefreshMacros();
            return false;
        }
        string old = current.Name;
        current.Name = name;
        if (!old.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var map in config.Profiles.SelectMany(x => x.Mappings))
            {
                if (map.Kind == ActionKind.Macro && map.Value.Equals(old, StringComparison.OrdinalIgnoreCase))
                    map.Value = name;
                if (map.LongPressKind == ActionKind.Macro && map.LongPressValue.Equals(old, StringComparison.OrdinalIgnoreCase))
                    map.LongPressValue = name;
            }
            MarkChanged();
        }
        loading = true;
        NameBox.Text = current.Name;
        loading = false;
        NameBox.IsReadOnly = true;
        ConfirmNameButton.Visibility = Visibility.Collapsed;
        editingName = false;
        RefreshMacros();
        FooterStatus.Text = $"マクロ名を「{current.Name}」に確定しました。";
        return true;
    }
    void MacroChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshingList)
            return;
        StopCoordinateCapture();
        StopRecording();
        StopManualCapture();
        CommitNameEdit(false);
        current = MacroList.SelectedItem as MacroDefinition;
        undoSteps.Clear();
        loading = true;
        NameBox.Text = current?.Name ?? "";
        loading = false;
        RefreshSteps();
        SetEditorState();
        FooterStatus.Text = "";
    }
    void NameChanged(object sender, TextChangedEventArgs e)
    {
        if (!loading && editingName)
            FooterStatus.Text = "［名前を確定］を押すか Enter キーで確定してください。";
    }
    void ConfirmName_Click(object sender, RoutedEventArgs e)
    {
        if (CommitNameEdit(true))
            StepList.Focus();
    }
    void NameBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (CommitNameEdit(true))
                StepList.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            loading = true;
            NameBox.Text = nameBeforeEdit;
            loading = false;
            CommitNameEdit(false);
            e.Handled = true;
        }
    }
    void DeleteMacro_Click(object sender, RoutedEventArgs e)
    {
        if (current == null)
            return;
        int references = config.Profiles.SelectMany(x => x.Mappings).Count(x => (x.Kind == ActionKind.Macro && x.Value.Equals(current.Name, StringComparison.OrdinalIgnoreCase)) || (x.LongPressKind == ActionKind.Macro && x.LongPressValue.Equals(current.Name, StringComparison.OrdinalIgnoreCase)));
        string note = references > 0 ? $"\nこのマクロを使う割り当て {references} 件も未設定に戻します。" : "";
        if (AppDialog.Show(this, $"「{current.Name}」を削除しますか？{note}", "マクロを削除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        StopRecording();
        StopManualCapture();
        foreach (var map in config.Profiles.SelectMany(x => x.Mappings))
        {
            if (map.Kind == ActionKind.Macro && map.Value.Equals(current.Name, StringComparison.OrdinalIgnoreCase))
            {
                map.Kind = ActionKind.None;
                map.Value = "";
            }
            if (map.LongPressKind == ActionKind.Macro && map.LongPressValue.Equals(current.Name, StringComparison.OrdinalIgnoreCase))
            {
                map.LongPressKind = ActionKind.None;
                map.LongPressValue = "";
            }
        }
        config.Macros.Remove(current);
        current = null;
        undoSteps.Clear();
        MarkChanged();
        RefreshMacros();
        if (config.Macros.Count > 0)
            MacroList.SelectedItem = config.Macros[0];
        else
        {
            loading = true;
            NameBox.Clear();
            loading = false;
            RefreshSteps();
            SetEditorState();
        }
    }

    void ManualCapture_Click(object sender, RoutedEventArgs e)
    {
        if (manualCaptureActive)
        {
            StopManualCapture();
            return;
        }
        if (current == null)
            return;
        StopCoordinateCapture();
        StopRecording();
        manualCaptureActive = true;
        manualHeld.Clear();
        manualSinceLast = Stopwatch.StartNew();
        setRecording(true, false, false);
        ManualCaptureLabel.Text = "手動追加を完了";
        ManualStatus.Text = "入力中です。キーを押してください。終わったら［手動追加を完了］を押します。";
        ManualCaptureButton.Focus();
    }
    void ManualCapture_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (manualCaptureActive && e.NewFocus is not null && e.NewFocus != ManualCaptureButton)
            StopManualCapture();
    }
    void StopManualCapture()
    {
        if (!manualCaptureActive)
            return;
        foreach (var key in manualHeld.ToArray())
            AddManualEvent(key, false);
        manualHeld.Clear();
        manualCaptureActive = false;
        manualSinceLast = null;
        setRecording(false, false, false);
        ManualCaptureLabel.Text = "手動追加を開始";
        ManualStatus.Text = "停止中";
    }
    static Key EventKey(WpfKeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
    void ManualCapture_KeyDown(object sender, WpfKeyEventArgs e)
    {
        var key = EventKey(e);
        e.Handled = true;
        if (current == null || key == Key.None || e.IsRepeat || !manualHeld.Add(key))
            return;
        PushUndo();
        AddManualEvent(key, true);
    }
    void ManualCapture_KeyUp(object sender, WpfKeyEventArgs e)
    {
        var key = EventKey(e);
        e.Handled = true;
        if (current == null || key == Key.None || !manualHeld.Remove(key))
            return;
        AddManualEvent(key, false);
    }
    void AddManualEvent(Key key, bool down)
    {
        if (current == null)
            return;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        string name = InputEngine.KeyName(vk);
        string value = $"{name} {(down ? "Down" : "Up")}";
        if (vk == 0 || !InputEngine.IsValidRecordedEvent(value))
            return;
        int delay = (int)Math.Clamp(manualSinceLast?.ElapsedMilliseconds ?? 0, 0, 600000);
        manualSinceLast?.Restart();
        var step = new MacroStep { Event = value, DelayMs = delay };
        InsertStep(step, false);
        MarkChanged();
    }
    internal void AddManualKeyForTest(Key key)
    {
        PushUndo();
        var old = manualSinceLast;
        manualSinceLast = null;
        AddManualEvent(key, true);
        AddManualEvent(key, false);
        manualSinceLast = old;
    }
    void KeypadInput_Click(object sender, RoutedEventArgs e)
    {
        if (current == null)
            return;
        StopCoordinateCapture();
        StopRecording();
        StopManualCapture();
        var picker = new MacroInputPickerWindow(config.KeyboardLayout) { Owner = this };
        picker.InputChosen += AddInputFromKeypad;
        picker.ShowDialog();
    }
    void AddInputFromKeypad(string input)
    {
        if (current == null)
            return;
        var steps = new[] { new MacroStep { Event = input + " Down" }, new MacroStep { Event = input + " Up" } };
        if (steps.Any(step => !InputEngine.IsValidRecordedEvent(step.Event)))
            return;
        PushUndo();
        int index = Math.Clamp(InsertionIndex(), 0, current.Steps.Count);
        current.Steps.InsertRange(index, steps);
        MarkChanged($"「{MainWindow.DisplayInputName(input)}」を追加しました。");
        RefreshSteps(steps);
        SetEditorState();
    }
    internal void AddInputFromKeypadForTest(string input) => AddInputFromKeypad(input);

    void Record_Click(object sender, RoutedEventArgs e)
    {
        if (recording)
        {
            StopRecording();
            return;
        }
        if (current == null)
            return;
        StopCoordinateCapture();
        StopManualCapture();
        PushUndo();
        recordingStartIndex = current.Steps.Count;
        stopShortcut.Reset();
        recording = true;
        ignoreInitialMouseRelease = true;
        sinceLast = Stopwatch.StartNew();
        lastRecordedMousePosition = null;
        recordedMovesInsideWindow.Clear();
        suppressedMappedInputs.Clear();
        RecordKeyboardBox.IsEnabled = false;
        RecordMappedActionsBox.IsEnabled = false;
        RecordPhysicalInputBox.IsEnabled = false;
        RecordMouseMovesBox.IsEnabled = false;
        RelativeMouseMovementBox.IsEnabled = false;
        FixedMousePositionBox.IsEnabled = false;
        setRecording(true, RecordMouseMovesBox.IsChecked == true, config.RecordMappedActionsInMacros);
        RecordButtonLabel.Text = "記録を停止";
        RecordButton.Background = ThemeService.Brush("AccentStrongBrush");
        RecordButton.Foreground = ThemeService.Brush("AccentButtonText");
        RecordStatus.Text = $"記録中（{(config.RecordMappedActionsInMacros ? "割り当て後のアクション" : "物理キー")}）— Ctrl + Shift + F12 で終了";
        RecordStatus.Foreground = ThemeService.Brush("AccentTextBrush");
    }
    public void Capture(string text)
    {
        if (!recording || current == null)
            return;
        if (ignoreInitialMouseRelease)
        {
            if (text.Equals("MouseLeft Up", StringComparison.OrdinalIgnoreCase) && sinceLast?.ElapsedMilliseconds < 1000)
            {
                ignoreInitialMouseRelease = false;
                sinceLast.Restart();
                return;
            }
            ignoreInitialMouseRelease = false;
        }
        bool moveInsideWindow = false;
        if (text.StartsWith("MouseMove:", StringComparison.OrdinalIgnoreCase) && TryParseMousePoint(text, out int mouseX, out int mouseY))
        {
            moveInsideWindow = IsScreenPointInsideWindow(mouseX, mouseY);
            if (config.RecordMouseMovementRelativeInMacros)
            {
                if (lastRecordedMousePosition is not { } previous)
                {
                    lastRecordedMousePosition = (mouseX, mouseY);
                    return;
                }
                text = $"MouseMoveRelative:{mouseX - previous.X},{mouseY - previous.Y}";
                lastRecordedMousePosition = (mouseX, mouseY);
            }
        }
        bool down = text.EndsWith(" Down", StringComparison.OrdinalIgnoreCase), up = text.EndsWith(" Up", StringComparison.OrdinalIgnoreCase);
        if (stopShortcut.Process(text))
        {
            while (current.Steps.Count > recordingStartIndex && current.Steps.LastOrDefault() is { } last && (last.Event.StartsWith("LeftCtrl ") || last.Event.StartsWith("RightCtrl ") || last.Event.StartsWith("LeftShift ") || last.Event.StartsWith("RightShift ")))
                current.Steps.RemoveAt(current.Steps.Count - 1);
            StopRecording();
            return;
        }
        if (config.RecordMappedActionsInMacros && TryPhysicalEventName(text, out string physicalName, out bool physicalUp) && suppressedMappedInputs.Contains(physicalName))
        {
            if (physicalUp)
                suppressedMappedInputs.Remove(physicalName);
            return;
        }
        if (!ShouldRecordEvent(text, RecordKeyboardBox.IsChecked == true))
            return;
        bool supported = text.StartsWith("MouseMove:", StringComparison.OrdinalIgnoreCase) || text.StartsWith("MouseMoveRelative:", StringComparison.OrdinalIgnoreCase) || down || up;
        if (!supported)
            return;
        int delay = (int)Math.Clamp(sinceLast?.ElapsedMilliseconds ?? 0, 0, 600000);
        sinceLast?.Restart();
        var step = new MacroStep { Event = text, DelayMs = delay };
        current.Steps.Add(step);
        if (moveInsideWindow)
            recordedMovesInsideWindow.Add(step);
        MarkChanged();
        RefreshSteps([step]);
        SetEditorState();
    }
    internal void CaptureMappedAction(Mapping map, string eventName)
    {
        if (!recording || current == null || !config.RecordMappedActionsInMacros || !MappingExecutor.TryGetRecordedAction(map, eventName, out var kind, out string value))
            return;
        string baseInput = eventName.Split(':', 2)[0];
        var physicalNames = baseInput.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => !x.Equals("Taskbar", StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int delay = (int)Math.Clamp(sinceLast?.ElapsedMilliseconds ?? 0, 0, 600000);
        while (current.Steps.Count > recordingStartIndex && current.Steps[^1].RecordedActionKind == null && TryPhysicalEventName(current.Steps[^1].Event, out string name, out _) && physicalNames.Contains(name))
        {
            delay = Math.Min(600000, delay + current.Steps[^1].DelayMs);
            current.Steps.RemoveAt(current.Steps.Count - 1);
        }
        sinceLast?.Restart();
        bool layerFiresBeforeRelease = baseInput.StartsWith("Space+", StringComparison.OrdinalIgnoreCase) || baseInput.StartsWith("CapsLock+", StringComparison.OrdinalIgnoreCase);
        if (layerFiresBeforeRelease)
        foreach (string name in physicalNames)
            suppressedMappedInputs.Add(name);
        var step = new MacroStep { Event = $"割り当て: {value}", DelayMs = delay, RecordedActionKind = kind, RecordedActionValue = value };
        current.Steps.Add(step);
        MarkChanged();
        RefreshSteps([step]);
        SetEditorState();
    }
    static bool TryPhysicalEventName(string text, out string name, out bool up)
    {
        up = text.EndsWith(" Up", StringComparison.OrdinalIgnoreCase);
        bool down = text.EndsWith(" Down", StringComparison.OrdinalIgnoreCase);
        if (!up && !down)
        {
            name = "";
            return false;
        }
        name = text[..^(up ? 3 : 5)].TrimEnd();
        return name.Length > 0;
    }
    internal static bool ShouldRecordEvent(string text, bool recordKeyboard)
    {
        bool keyEvent = (text.EndsWith(" Down", StringComparison.OrdinalIgnoreCase) || text.EndsWith(" Up", StringComparison.OrdinalIgnoreCase)) && !text.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("Tilt", StringComparison.OrdinalIgnoreCase);
        return recordKeyboard || !keyEvent;
    }
    void StopRecording()
    {
        if (!recording)
            return;
        recording = false;
        ignoreInitialMouseRelease = false;
        stopShortcut.Reset();
        while (current != null && current.Steps.Count > recordingStartIndex && current.Steps.LastOrDefault() is { } last && (recordedMovesInsideWindow.Contains(last) || IsMoveInsideWindow(last.Event) || (last.Event == "MouseLeft Down" && last.DelayMs < 1000)))
            current.Steps.RemoveAt(current.Steps.Count - 1);
        sinceLast = null;
        lastRecordedMousePosition = null;
        recordedMovesInsideWindow.Clear();
        suppressedMappedInputs.Clear();
        setRecording(false, false, false);
        RecordKeyboardBox.IsEnabled = true;
        RecordMappedActionsBox.IsEnabled = true;
        RecordPhysicalInputBox.IsEnabled = true;
        RecordMouseMovesBox.IsEnabled = true;
        UpdateMouseMovementModeState();
        RecordButtonLabel.Text = "記録を開始（末尾へ追記）";
        RecordButton.Background = ThemeService.Brush("DangerBackground");
        RecordButton.Foreground = ThemeService.Brush("DangerForeground");
        RecordStatus.Text = "停止中";
        RecordStatus.Foreground = ThemeService.Brush("SecondaryText");
        RefreshSteps();
        SetEditorState();
    }
    void RecordKeyboardChanged(object sender, RoutedEventArgs e)
    {
        if (loadingOption)
            return;
        config.RecordKeyboardInputInMacros = RecordKeyboardBox.IsChecked == true;
        MarkChanged();
    }
    void KeyRecordingModeChanged(object sender, RoutedEventArgs e)
    {
        if (loadingOption)
            return;
        config.RecordMappedActionsInMacros = RecordMappedActionsBox.IsChecked == true;
        MarkChanged();
    }
    void RecordMouseMovesChanged(object sender, RoutedEventArgs e)
    {
        if (loadingOption)
            return;
        config.RecordMouseMovementInMacros = RecordMouseMovesBox.IsChecked == true;
        UpdateMouseMovementModeState();
        MarkChanged();
    }
    void MouseMovementModeChanged(object sender, RoutedEventArgs e)
    {
        if (loadingOption)
            return;
        config.RecordMouseMovementRelativeInMacros = RelativeMouseMovementBox.IsChecked == true;
        MarkChanged();
    }
    void UpdateMouseMovementModeState()
    {
        MouseMovementModePanel.Opacity = RecordMouseMovesBox.IsChecked == true ? 1 : .45;
        RelativeMouseMovementBox.IsEnabled = !recording && RecordMouseMovesBox.IsChecked == true;
        FixedMousePositionBox.IsEnabled = !recording && RecordMouseMovesBox.IsChecked == true;
    }
    static bool TryParseMousePoint(string value, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (!value.StartsWith("MouseMove:", StringComparison.OrdinalIgnoreCase))
            return false;
        var p = value[10..].Split(',');
        return p.Length == 2 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y);
    }
    bool IsScreenPointInsideWindow(int x, int y)
    {
        var point = PointFromScreen(new System.Windows.Point(x, y));
        return point.X >= 0 && point.Y >= 0 && point.X <= ActualWidth && point.Y <= ActualHeight;
    }
    bool IsMoveInsideWindow(string value)
    {
        if (!value.StartsWith("MouseMove:", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            var p = value[10..].Split(',');
            var point = PointFromScreen(new System.Windows.Point(double.Parse(p[0]), double.Parse(p[1])));
            return point.X >= 0 && point.Y >= 0 && point.X <= ActualWidth && point.Y <= ActualHeight;
        }
        catch { return false; }
    }

    int InsertionIndex()
    {
        if (current == null)
            return 0;
        var selected = SelectedSteps();
        if (selected.Count == 0)
            return current.Steps.Count;
        return selected.Max(x => current.Steps.IndexOf(x)) + 1;
    }
    void InsertStep(MacroStep step, bool pushUndo = true)
    {
        if (current == null)
            return;
        if (pushUndo)
            PushUndo();
        int index = Math.Clamp(InsertionIndex(), 0, current.Steps.Count);
        current.Steps.Insert(index, step);
        RefreshSteps([step]);
        SetEditorState();
    }
    void AddWait_Click(object sender, RoutedEventArgs e)
    {
        if (current == null)
            return;
        if (!int.TryParse(WaitBox.Text, out int ms) || ms < 1 || ms > 600000)
        {
            AppDialog.Show(this, "待機時間は1～600000ミリ秒で入力してください。", "待機時間", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        InsertStep(new MacroStep { Event = "Wait", DelayMs = ms });
        MarkChanged("待機時間を追加しました。");
    }
    void CoordinateCapture_Click(object sender, RoutedEventArgs e)
    {
        if (coordinateCaptureActive)
        {
            StopCoordinateCapture("座標の記録をキャンセルしました。");
            return;
        }
        if (current == null)
            return;
        StopRecording();
        StopManualCapture();
        coordinateCaptureCallback = CoordinateCaptured;
        if (!InputEngine.BeginCoordinateCapture(coordinateCaptureCallback))
        {
            coordinateCaptureCallback = null;
            FooterStatus.Text = "別の座標記録が完了するまでお待ちください。";
            return;
        }
        coordinateCaptureActive = true;
        CoordinateCaptureLabel.Text = "クリックして座標を記録…（Escでキャンセル）";
        CoordinateCaptureButton.Background = ThemeService.Brush("AccentSoftBrush");
        FooterStatus.Text = "画面上の記録したい位置を左クリックしてください。このクリック自体は実行されません。";
        CoordinateCaptureButton.Focus();
    }
    void CoordinateCaptured(int x, int y)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!coordinateCaptureActive || current == null)
                return;
            PushUndo();
            var steps = new[]
            {
                new MacroStep{Event=$"MouseMove:{x},{y}"},
                new MacroStep{Event="MouseLeft Down"},
                new MacroStep{Event="MouseLeft Up"}
            };
            current.Steps.AddRange(steps);
            StopCoordinateCapture();
            MarkChanged($"座標（{x}, {y}）への移動とクリックを末尾へ追加しました。");
            RefreshSteps(steps);
            SetEditorState();
        });
    }
    void StopCoordinateCapture(string? status = null)
    {
        if (coordinateCaptureCallback != null)
            InputEngine.CancelCoordinateCapture(coordinateCaptureCallback);
        coordinateCaptureCallback = null;
        coordinateCaptureActive = false;
        CoordinateCaptureLabel.Text = "座標を記録";
        CoordinateCaptureButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        if (status != null)
            FooterStatus.Text = status;
    }
    void Window_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (coordinateCaptureActive && EventKey(e) == Key.Escape)
        {
            StopCoordinateCapture("座標の記録をキャンセルしました。");
            e.Handled = true;
            return;
        }
        if (!StepList.IsKeyboardFocusWithin)
            return;
        var key = EventKey(e);
        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (control && key == Key.C)
        {
            CopySteps_Click(sender, e);
            e.Handled = true;
        }
        else if (control && key == Key.V)
        {
            PasteSteps_Click(sender, e);
            e.Handled = true;
        }
        else if (key == Key.Delete)
        {
            DeleteStep_Click(sender, e);
            e.Handled = true;
        }
    }
    internal bool CoordinateCaptureActiveForTest => coordinateCaptureActive;
    void AddCatalogAction_Click(object sender, RoutedEventArgs e)
    {
        if (current == null)
            return;
        var picker = new ActionPickerWindow(deckLayouts: config.DeckLayouts) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedAction is not { } action)
            return;
        InsertStep(new MacroStep { Event = $"割り当て: {action.Value}", RecordedActionKind = action.Kind, RecordedActionValue = action.Value });
        MarkChanged($"「{action.Name}」を追加しました。");
    }
    void AddApplicationAction_Click(object sender, RoutedEventArgs e)
    {
        if (current == null)
            return;
        var picker = new ApplicationPickerWindow { Owner = this };
        if (picker.ShowDialog() != true || string.IsNullOrWhiteSpace(picker.SelectedPath))
            return;
        InsertStep(new MacroStep { Event = $"割り当て: {picker.SelectedPath}", RecordedActionKind = ActionKind.Launch, RecordedActionValue = picker.SelectedPath });
        MarkChanged("アプリ・ファイル・URLを追加しました。");
    }
    void AddTextAction_Click(object sender, RoutedEventArgs e)
    {
        if (current == null || string.IsNullOrEmpty(ManualTextBox.Text))
        {
            FooterStatus.Text = "追加する文字列を入力してください。";
            return;
        }
        string value = ManualTextBox.Text;
        InsertStep(new MacroStep { Event = "割り当て: 文字列入力", RecordedActionKind = ActionKind.Text, RecordedActionValue = value });
        ManualTextBox.Clear();
        MarkChanged("文字列入力を追加しました。");
    }
    void PushUndo()
    {
        if (current == null)
            return;
        undoSteps.Push(CloneSteps(current.Steps));
        if (undoSteps.Count > 30)
        {
            var keep = undoSteps.Take(30).Reverse().ToArray();
            undoSteps.Clear();
            foreach (var item in keep)
                undoSteps.Push(item);
        }
        UndoButton.IsEnabled = true;
    }
    void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (current == null || undoSteps.Count == 0)
            return;
        current.Steps = CloneSteps(undoSteps.Pop());
        MarkChanged("直前の手順編集を元に戻しました。");
        RefreshSteps();
        SetEditorState();
    }
    List<MacroStep> SelectedSteps() => [.. StepList.SelectedItems.Cast<StepView>().Select(x => x.Step).Where(x => current?.Steps.Contains(x) == true)];
    void StepContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        bool selected = SelectedSteps().Count > 0;
        CopyStepsMenuItem.IsEnabled = selected;
        PasteStepsMenuItem.IsEnabled = current != null && copiedSteps.Count > 0;
        DeleteStepsMenuItem.IsEnabled = selected;
    }
    void CopySteps_Click(object sender, RoutedEventArgs e)
    {
        if (current == null)
            return;
        var selected = SelectedSteps().ToHashSet();
        if (selected.Count == 0)
            return;
        copiedSteps.Clear();
        copiedSteps.AddRange(current.Steps.Where(selected.Contains).Select(CloneStep));
        FooterStatus.Text = $"{copiedSteps.Count}件の手順をコピーしました。";
    }
    void PasteSteps_Click(object sender, RoutedEventArgs e)
    {
        if (current == null || copiedSteps.Count == 0)
            return;
        var selected = SelectedSteps();
        int index = selected.Count == 0 ? current.Steps.Count : selected.Max(step => current.Steps.IndexOf(step)) + 1;
        var pasted = copiedSteps.Select(CloneStep).ToList();
        PushUndo();
        current.Steps.InsertRange(index, pasted);
        MarkChanged($"{pasted.Count}件の手順を貼り付けました。");
        RefreshSteps(pasted);
        SetEditorState();
    }
    void DeleteStep_Click(object sender, RoutedEventArgs e)
    {
        if (current == null)
            return;
        var selected = SelectedSteps();
        if (selected.Count == 0)
            return;
        PushUndo();
        int next = selected.Min(x => current.Steps.IndexOf(x));
        foreach (var step in selected)
            current.Steps.Remove(step);
        MarkChanged();
        RefreshSteps();
        if (current.Steps.Count > 0)
            SelectSteps([current.Steps[Math.Min(next, current.Steps.Count - 1)]]);
        SetEditorState();
    }
    void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (current == null || current.Steps.Count == 0)
            return;
        if (AppDialog.Show(this, "登録された手順をすべて消去しますか？\n［元に戻す］で取り消すこともできます。", "手順をすべて消去", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        PushUndo();
        current.Steps.Clear();
        MarkChanged();
        RefreshSteps();
        SetEditorState();
    }
    void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1); void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);
    void MoveSelected(int offset)
    {
        if (current == null)
            return;
        var selected = SelectedSteps();
        if (selected.Count == 0)
            return;
        var indexes = selected.Select(step => current.Steps.IndexOf(step)).Order().ToArray();
        if (offset < 0 && indexes[0] == 0 || offset > 0 && indexes[^1] == current.Steps.Count - 1)
            return;
        PushUndo();
        var selectedSet = selected.ToHashSet();
        if (offset < 0)
        {
            foreach (int index in indexes)
            if (index > 0 && !selectedSet.Contains(current.Steps[index - 1]))
                (current.Steps[index - 1], current.Steps[index]) = (current.Steps[index], current.Steps[index - 1]);
        }
        else
        {
            foreach (int index in indexes.Reverse())
            if (index < current.Steps.Count - 1 && !selectedSet.Contains(current.Steps[index + 1]))
                (current.Steps[index], current.Steps[index + 1]) = (current.Steps[index + 1], current.Steps[index]);
        }
        MarkChanged();
        RefreshSteps(selected);
        SetEditorState();
    }
    void StepList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(StepList);
        dragStep = IsStepDragHandle(e.OriginalSource as DependencyObject) ? StepFromElement(e.OriginalSource as DependencyObject) : null;
        dragInsertionIndex = -1;
    }
    void StepList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = StepContainerFromElement(e.OriginalSource as DependencyObject);
        if (item == null)
            return;
        if (!item.IsSelected)
        {
            StepList.SelectedItems.Clear();
            item.IsSelected = true;
        }
        item.Focus();
    }
    void StepList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || dragStep == null)
            return;
        var p = e.GetPosition(StepList);
        if (Math.Abs(p.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        try
        {
            ShowStepDragPreview(p);
            DragDrop.DoDragDrop(StepList, dragStep, System.Windows.DragDropEffects.Move);
        }
        finally { HideDropIndicator(); StepDragPreviewPopup.IsOpen = false; dragStep = null; }
    }
    void StepList_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (current == null || dragStep == null || !e.Data.GetDataPresent(typeof(MacroStep)))
        {
            HideDropIndicator();
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }
        var container = StepContainerFromElement(e.OriginalSource as DependencyObject);
        double lineY;
        if (container?.DataContext is StepView view)
        {
            int target = current.Steps.IndexOf(view.Step);
            bool after = e.GetPosition(container).Y >= container.ActualHeight / 2;
            dragInsertionIndex = Math.Clamp(target + (after ? 1 : 0), 0, current.Steps.Count);
            lineY = container.TranslatePoint(new System.Windows.Point(0, after ? container.ActualHeight : 0), StepList).Y;
            AnimateDragTarget(container, after);
        }
        else
        {
            dragInsertionIndex = current.Steps.Count;
            lineY = StepList.ItemContainerGenerator.ContainerFromIndex(StepList.Items.Count - 1) is not ListBoxItem last ? 2 : last.TranslatePoint(new System.Windows.Point(0, last.ActualHeight), StepList).Y;
            AnimateDragTarget(null, false);
        }
        var pointer = e.GetPosition(StepList);
        StepDragPreviewPopup.HorizontalOffset = pointer.X + 16;
        StepDragPreviewPopup.VerticalOffset = pointer.Y + 14;
        DropIndicator.Margin = new Thickness(5, Math.Max(0, lineY - 1.5), 5, 0);
        if (DropIndicator.Visibility != Visibility.Visible)
        {
            DropIndicator.Opacity = UiMotionService.Enabled ? 0 : 1;
            DropIndicator.Visibility = Visibility.Visible;
            if (UiMotionService.Enabled)
                UiMotionService.RunSafely("macro-drop-indicator", () =>
                    DropIndicator.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(110))));
        }
        e.Effects = System.Windows.DragDropEffects.Move;
        e.Handled = true;
    }
    void StepList_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        var p = e.GetPosition(StepList);
        if (p.X < 0 || p.Y < 0 || p.X > StepList.ActualWidth || p.Y > StepList.ActualHeight)
            HideDropIndicator();
    }
    void HideDropIndicator()
    {
        dragInsertionIndex = -1;
        DropIndicator?.Visibility = Visibility.Collapsed;
        AnimateDragTarget(null, false);
    }
    void StepList_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (current == null || e.Data.GetData(typeof(MacroStep)) is not MacroStep source)
        {
            HideDropIndicator();
            return;
        }
        int from = current.Steps.IndexOf(source), to = dragInsertionIndex;
        if (from < 0 || to < 0)
        {
            HideDropIndicator();
            return;
        }
        to = DropIndexAfterRemoval(from, to, current.Steps.Count);
        HideDropIndicator();
        if (to == from)
            return;
        PushUndo();
        current.Steps.RemoveAt(from);
        current.Steps.Insert(to, source);
        MarkChanged();
        RefreshSteps([source]);
        SetEditorState();
        e.Handled = true;
    }
    internal static int DropIndexAfterRemoval(int sourceIndex, int insertionIndex, int count)
    {
        if (count <= 1)
            return 0;
        int target = Math.Clamp(insertionIndex, 0, count);
        if (sourceIndex < target)
            target--;
        return Math.Clamp(target, 0, count - 1);
    }
    ListBoxItem? StepContainerFromElement(DependencyObject? element)
    {
        while (element != null && element != StepList)
        {
            if (element is ListBoxItem item)
                return item;
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        return null;
    }
    MacroStep? StepFromElement(DependencyObject? element) => StepContainerFromElement(element)?.DataContext is StepView view ? view.Step : null;

    bool IsStepDragHandle(DependencyObject? element)
    {
        while (element != null && element != StepList)
        {
            if (element is FrameworkElement { Tag: "MacroStepDragHandle" })
                return true;
            if (element is ListBoxItem)
                return false;
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    void ShowStepDragPreview(System.Windows.Point pointer)
    {
        if (dragStep == null || current == null)
            return;
        int index = current.Steps.IndexOf(dragStep);
        StepDragPreviewNumber.Text = (index + 1).ToString();
        StepDragPreviewTitle.Text = HumanTitle(dragStep);
        StepDragPreviewDetail.Text = HumanDetail(dragStep);
        StepDragPreviewPopup.HorizontalOffset = pointer.X + 16;
        StepDragPreviewPopup.VerticalOffset = pointer.Y + 14;
        StepDragPreviewPopup.IsOpen = true;
    }

    void StepDragPreviewPopup_Opened(object? sender, EventArgs e) => NonInteractivePopupSafety.Apply(StepDragPreviewPopup);

#if !PRODUCTION_PUBLISH
    internal bool OpenSafeStepDragPreviewForTest()
    {
        StepDragPreviewPopup.IsOpen = true;
        NonInteractivePopupSafety.Apply(StepDragPreviewPopup);
        bool safe = NonInteractivePopupSafety.HasRequiredStylesForTest(StepDragPreviewPopup);
        StepDragPreviewPopup.IsOpen = false;
        return safe;
    }
#endif

    void AnimateDragTarget(ListBoxItem? target, bool insertAfter)
        => UiMotionService.RunSafely("macro-drag-target", () => AnimateDragTargetCore(target, insertAfter));

    void AnimateDragTargetCore(ListBoxItem? target, bool insertAfter)
    {
        if (ReferenceEquals(target, dragTargetContainer) && (target == null || insertAfter == dragTargetAfter))
            return;
        if (!UiMotionService.Enabled)
        {
            if (dragTargetContainer != null)
                dragTargetContainer.RenderTransform = System.Windows.Media.Transform.Identity;
            dragTargetContainer = target;
            dragTargetTransform = null;
            dragTargetAfter = insertAfter;
            if (target != null)
                target.RenderTransform = System.Windows.Media.Transform.Identity;
            return;
        }
        if (dragTargetContainer != null && dragTargetTransform != null)
        {
            var oldTarget = dragTargetContainer;
            var oldTransform = dragTargetTransform;
            var settle = new System.Windows.Media.Animation.DoubleAnimation(oldTransform.Y, 0, TimeSpan.FromMilliseconds(90))
            {
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            settle.Completed += (_, _) =>
            {
                if (ReferenceEquals(oldTarget.RenderTransform, oldTransform))
                    oldTarget.RenderTransform = System.Windows.Media.Transform.Identity;
            };
            oldTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, settle);
        }
        dragTargetContainer = target;
        dragTargetTransform = null;
        dragTargetAfter = insertAfter;
        if (target == null)
            return;
        var transform = new System.Windows.Media.TranslateTransform();
        target.RenderTransform = transform;
        dragTargetTransform = transform;
        double offset = insertAfter ? -5 : 5;
        transform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, offset, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            });
    }
    void StepList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (StepList.SelectedItems.Count == 1)
            EditorTabs.SelectedIndex = 2;
    }
    void EditorModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string value } && int.TryParse(value, out int index) && index >= 0 && index < EditorTabs.Items.Count)
            EditorTabs.SelectedIndex = index;
    }
    void EditorTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, EditorTabs))
            UpdateEditorModeButtons();
    }
    void UpdateEditorModeButtons()
    {
        if (ManualModeButton == null)
            return;
        var buttons = new[] { ManualModeButton, RecordModeButton, StepEditModeButton };
        for (int index = 0; index < buttons.Length; index++)
        {
            bool active = EditorTabs.SelectedIndex == index;
            buttons[index].Background = ThemeService.Brush(active ? "AccentSoftBrush" : "ControlBackground");
            buttons[index].BorderBrush = ThemeService.Brush(active ? "AccentBrush" : "BorderBrush");
            buttons[index].Foreground = ThemeService.Brush(active ? "AccentTextBrush" : "PrimaryText");
        }
    }
    void StepSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SetEditorState();
    }
    void UpdateSelectedStepEditor()
    {
        if (SelectedStepTitle == null)
            return;
        var selected = SelectedSteps();
        bool one = selected.Count == 1;
        SelectedStepDelayBox.IsEnabled = one;
        ApplyStepEditButton.IsEnabled = one;
        ReplaceStepActionButton.IsEnabled = one && selected[0].RecordedActionKind != null;
        if (!one)
        {
            SelectedStepTitle.Text = selected.Count > 1 ? $"{selected.Count}件の手順を選択中" : "中央から手順を選択してください。";
            SelectedStepDelayBox.Text = "0";
            return;
        }
        var step = selected[0];
        SelectedStepTitle.Text = HumanTitle(step);
        SelectedStepDelayBox.Text = step.DelayMs.ToString();
    }
    void ApplyStepEdit_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedSteps();
        if (selected.Count != 1)
            return;
        if (!int.TryParse(SelectedStepDelayBox.Text, out int ms) || ms < 0 || ms > 600000)
        {
            AppDialog.Show(this, "待機時間は0～600000ミリ秒で入力してください。", "手順編集", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        PushUndo();
        selected[0].DelayMs = ms;
        MarkChanged("手順を変更しました。");
        RefreshSteps(selected);
        SetEditorState();
    }
    void ReplaceStepAction_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedSteps();
        if (selected.Count != 1)
            return;
        var picker = new ActionPickerWindow(deckLayouts: config.DeckLayouts) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedAction is not { } action)
            return;
        PushUndo();
        var step = selected[0];
        step.Event = $"割り当て: {action.Value}";
        step.RecordedActionKind = action.Kind;
        step.RecordedActionValue = action.Value;
        MarkChanged("アクションを変更しました。");
        RefreshSteps([step]);
        SetEditorState();
    }
    void RefreshSteps(IEnumerable<MacroStep>? selected = null)
    {
        var selectedSteps = selected?.ToList() ?? [];
        var views = current?.Steps.Select((step, index) => new StepView(step, index + 1, HumanTitle(step), HumanDetail(step), step.DelayMs > 0 ? $"{step.DelayMs} ms" : "", VisualKindFor(step))).ToList() ?? [];
        StepList.ItemsSource = views;
        StepSummary.Text = current == null ? "" : $"{current.Steps.Count} 手順・待機合計 {current.Steps.Sum(x => x.DelayMs)} ms";
        if (selectedSteps.Count > 0)
            SelectSteps(selectedSteps);
    }
    void SelectSteps(IEnumerable<MacroStep> steps)
    {
        var set = steps.ToHashSet();
        StepList.SelectedItems.Clear();
        foreach (var view in StepList.Items.Cast<StepView>().Where(x => set.Contains(x.Step)))
            StepList.SelectedItems.Add(view);
        if (StepList.SelectedItem != null)
            StepList.ScrollIntoView(StepList.SelectedItem);
    }
    static string HumanTitle(MacroStep step)
    {
        if (step.Event.Equals("Wait", StringComparison.OrdinalIgnoreCase))
            return $"待機 {step.DelayMs} ms";
        if (step.RecordedActionKind is { } kind)
        {
            var catalog = ActionCatalog.Items.FirstOrDefault(x => x.Kind == kind && x.Value.Equals(step.RecordedActionValue, StringComparison.OrdinalIgnoreCase));
            return kind == ActionKind.Mouse ? MainWindow.DisplayActionValue(kind, step.RecordedActionValue) : catalog?.Name ?? kind switch
            {
                ActionKind.Text => $"文字列を入力「{Shorten(step.RecordedActionValue, 24)}」",
                ActionKind.Launch => $"開く: {Shorten(step.RecordedActionValue, 30)}",
                ActionKind.Macro => $"マクロを実行: {step.RecordedActionValue}",
                ActionKind.Profile => $"プロファイル切替: {step.RecordedActionValue}",
                _ => step.RecordedActionValue
            };
        }
        if (step.Event.StartsWith("MouseMoveRelative:", StringComparison.OrdinalIgnoreCase))
            return "マウスを相対移動";
        if (step.Event.StartsWith("MouseMove:", StringComparison.OrdinalIgnoreCase))
            return "マウスを指定位置へ移動";
        if (step.Event.EndsWith(" Down", StringComparison.OrdinalIgnoreCase))
            return MainWindow.DisplayInputName(step.Event[..^5]) + " を押す";
        if (step.Event.EndsWith(" Up", StringComparison.OrdinalIgnoreCase))
            return MainWindow.DisplayInputName(step.Event[..^3]) + " を離す";
        return step.Event;
    }
    static string HumanDetail(MacroStep step)
    {
        if (step.Event == "Wait")
            return "この時間だけ次の操作を待ちます";
        if (step.RecordedActionKind is { } kind)
            return ActionKindLabel(kind);
        return step.Event;
    }
    internal static MacroStepVisualKind VisualKindFor(MacroStep step)
    {
        if (step.Event.Equals("Wait", StringComparison.OrdinalIgnoreCase))
            return MacroStepVisualKind.Wait;
        if (step.RecordedActionKind == ActionKind.Macro)
            return MacroStepVisualKind.Macro;
        if (step.RecordedActionKind == ActionKind.Text)
            return MacroStepVisualKind.Text;
        if (step.RecordedActionKind == ActionKind.Mouse)
            return MacroStepVisualKind.Mouse;
        if (step.RecordedActionKind != null)
            return MacroStepVisualKind.Action;
        if (step.Event.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase)
           || step.Event.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase)
           || step.Event.StartsWith("Tilt", StringComparison.OrdinalIgnoreCase))
            return MacroStepVisualKind.Mouse;
        return MacroStepVisualKind.Keyboard;
    }
    static string ActionKindLabel(ActionKind kind) => kind switch { ActionKind.Key or ActionKind.Shortcut => "キー・ショートカット", ActionKind.Text => "文字列入力", ActionKind.Launch => "アプリ・ファイル・URL", ActionKind.Mouse => "マウス操作", ActionKind.Macro => "マクロ", ActionKind.Profile => "プロファイル", _ => "アクション" };
    static string Shorten(string value, int length) => value.Length <= length ? value : value[..length] + "…";

    async void TestMacro_Click(object sender, RoutedEventArgs e)
    {
        if (testRunning || !ValidateCurrent(true) || current == null)
            return;
        StopRecording();
        StopManualCapture();
        testRunning = true;
        FooterStatus.Text = "テスト実行中です。［停止］または緊急停止キーで中断できます。";
        SetEditorState();
        var result = await MacroPlayer.PlayAsync(CloneMacro(current), config);
        if (!IsLoaded)
            return;
        testRunning = false;
        FooterStatus.Text = result.Cancelled ? "テストを停止しました。" : result.Succeeded ? "テストは正常に完了しました。" : "テスト失敗: " + result.Message;
        FooterStatus.Foreground = ThemeService.Brush(result.Succeeded ? "AccentTextBrush" : result.Cancelled ? "SecondaryText" : "DangerBrush");
        SetEditorState();
    }
    void StopTest_Click(object sender, RoutedEventArgs e)
    {
        MacroPlayer.StopAll();
        FooterStatus.Text = "停止しています…";
    }
    bool ValidateCurrent(bool requireSteps)
    {
        if (!CommitNameEdit(true) || current == null)
        {
            AppDialog.Show(this, "先に［＋ 新規］からマクロを作成してください。", "マクロ", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (requireSteps && current.Steps.Count == 0)
        {
            AppDialog.Show(this, "先にキー操作、アクション、または待機時間を追加してください。", "マクロ", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        return true;
    }
    bool SaveChanges()
    {
        if (!ValidateCurrent(false))
            return false;
        StopRecording();
        StopManualCapture();
        var errors = ConfigValidator.Validate(config);
        if (errors.Count > 0)
        {
            AppDialog.Show(this, string.Join("\n", errors), "保存できません", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        new ConfigService().Save(config);
        UpdateRenamedShortcuts();
        CaptureSavedState();
        SaveRequested = true;
        Changed = true;
        dirty = false;
        UnsavedStatus.Text = "";
        Saved?.Invoke();
        FooterStatus.Foreground = ThemeService.Brush("AccentTextBrush");
        FooterStatus.Text = "保存して反映しました。この画面を開いたまま編集を続けられます。";
        return true;
    }
    void Save_Click(object sender, RoutedEventArgs e) => SaveChanges();
    void CreateShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateCurrent(true) || current == null)
            return;
        StopRecording();
        StopManualCapture();
        try
        {
            if (!SaveChanges())
                return;
            ShortcutCreatedPath = ShortcutService.CreateMacroShortcut(current);
            FooterStatus.Text = $"デスクトップに「{System.IO.Path.GetFileName(ShortcutCreatedPath)}」を作成しました。";
        }
        catch (Exception ex) { AppDialog.Show(this, "実行アイコンを作成できませんでした。\n\n" + ex.Message, "デスクトップに作成", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    void Use_Click(object sender, RoutedEventArgs e)
    {
        if (!allowAssignment || !ValidateCurrent(true))
            return;
        StopRecording();
        StopManualCapture();
        accepted = true;
        closingConfirmed = true;
        DialogResult = true;
    }
    void Close_Click(object sender, RoutedEventArgs e) => Close();
    void CaptureSavedState()
    {
        var snapshot = new ConfigService().Clone(config);
        savedMacros = snapshot.Macros;
        savedRecordKeyboard = config.RecordKeyboardInputInMacros;
        savedRecordMappedActions = config.RecordMappedActionsInMacros;
        savedRecordMouseMoves = config.RecordMouseMovementInMacros;
        savedRelativeMouseMoves = config.RecordMouseMovementRelativeInMacros;
        savedMappingActions = config.Profiles.SelectMany(x => x.Mappings).ToDictionary(x => x, x => (x.Kind, x.Value, x.LongPressKind, x.LongPressValue));
    }
    bool savedRecordKeyboard, savedRecordMappedActions, savedRecordMouseMoves, savedRelativeMouseMoves;
    void UpdateRenamedShortcuts()
    {
        foreach (var macro in config.Macros)
        {
            var previous = savedMacros.FirstOrDefault(x => x.Id.Equals(macro.Id, StringComparison.OrdinalIgnoreCase));
            if (previous == null || previous.Name.Equals(macro.Name, StringComparison.Ordinal))
                continue;
            try
            {
                ShortcutService.MigrateRenamedMacroShortcut(previous.Name, macro);
            }
            catch (Exception ex) { FooterStatus.Text = "マクロは保存しましたが、既存の実行アイコン名を変更できませんでした: " + ex.Message; }
        }
        foreach (var macro in config.Macros)
            try
            {
                ShortcutService.UpgradeExistingMacroShortcut(macro);
            }
            catch (Exception ex) { FooterStatus.Text = "マクロは保存しましたが、既存の実行アイコンを更新できませんでした: " + ex.Message; }
    }
    void RestoreUncommittedChanges()
    {
        config.Macros = new ConfigService().Clone(new AppConfig { Macros = savedMacros, Profiles = [new Profile()] }).Macros;
        config.RecordKeyboardInputInMacros = savedRecordKeyboard;
        config.RecordMappedActionsInMacros = savedRecordMappedActions;
        config.RecordMouseMovementInMacros = savedRecordMouseMoves;
        config.RecordMouseMovementRelativeInMacros = savedRelativeMouseMoves;
        foreach (var pair in savedMappingActions)
        {
            pair.Key.Kind = pair.Value.Kind;
            pair.Key.Value = pair.Value.Value;
            pair.Key.LongPressKind = pair.Value.LongKind;
            pair.Key.LongPressValue = pair.Value.LongValue;
        }
        Changed = false;
        dirty = false;
    }
    void Window_Closing(object? sender, CancelEventArgs e)
    {
        StopCoordinateCapture();
        StopRecording();
        StopManualCapture();
        MacroPlayer.StopAll();
        CommitNameEdit(false);
        if (accepted)
            return;
        if (dirty && !closingConfirmed && !SuppressUnsavedPromptForTest)
        {
            var answer = AppDialog.Show(this, "保存していない変更があります。\n\n保存してから閉じますか？", "マクロの変更", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (answer == MessageBoxResult.Yes && !SaveChanges())
            {
                e.Cancel = true;
                return;
            }
            closingConfirmed = true;
        }
        if (!accepted && dirty)
            RestoreUncommittedChanges();
    }
}
