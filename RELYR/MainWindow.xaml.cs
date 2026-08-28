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

public partial class MainWindow : Window
{
    const string NewProfileMenuTag = "NewProfile";
    const double DefaultMainWindowMinWidth = 880;
    const double DefaultMainWindowMinHeight = 640;
    const double MainWindowWorkAreaInset = 8;
    internal const double SelectionDimOpacity = 0.30;
    public static readonly DependencyProperty IsMultiSelectedProperty = DependencyProperty.RegisterAttached("IsMultiSelected", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public static bool GetIsMultiSelected(DependencyObject element) => (bool)element.GetValue(IsMultiSelectedProperty);
    public static void SetIsMultiSelected(DependencyObject element, bool value) => element.SetValue(IsMultiSelectedProperty, value);
    public static readonly DependencyProperty IsCurrentSelectedProperty = DependencyProperty.RegisterAttached("IsCurrentSelected", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public static bool GetIsCurrentSelected(DependencyObject element) => (bool)element.GetValue(IsCurrentSelectedProperty);
    public static void SetIsCurrentSelected(DependencyObject element, bool value) => element.SetValue(IsCurrentSelectedProperty, value);
    public static readonly DependencyProperty IsSelectionPulseActiveProperty = DependencyProperty.RegisterAttached("IsSelectionPulseActive", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public static bool GetIsSelectionPulseActive(DependencyObject element) => (bool)element.GetValue(IsSelectionPulseActiveProperty);
    public static void SetIsSelectionPulseActive(DependencyObject element, bool value) => element.SetValue(IsSelectionPulseActiveProperty, value);
    public static readonly DependencyProperty SelectionPulseBrushProperty = DependencyProperty.RegisterAttached("SelectionPulseBrush", typeof(System.Windows.Media.Brush), typeof(MainWindow), new PropertyMetadata(null));
    public static System.Windows.Media.Brush? GetSelectionPulseBrush(DependencyObject element) => (System.Windows.Media.Brush?)element.GetValue(SelectionPulseBrushProperty);
    public static void SetSelectionPulseBrush(DependencyObject element, System.Windows.Media.Brush value) => element.SetValue(SelectionPulseBrushProperty, value);
    public static readonly DependencyProperty IsAssignmentDropTargetProperty = DependencyProperty.RegisterAttached("IsAssignmentDropTarget", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public static bool GetIsAssignmentDropTarget(DependencyObject element) => (bool)element.GetValue(IsAssignmentDropTargetProperty);
    public static void SetIsAssignmentDropTarget(DependencyObject element, bool value) => element.SetValue(IsAssignmentDropTargetProperty, value);
    readonly ConfigService store = new();
    readonly InputEngine engine = new();
    readonly MappingExecutor executor;
    readonly MappingExecutor taskbarExecutor;
    readonly MappingExecutor deckExecutor;
    readonly ArchiveWatcher archiveWatcher = new();
    readonly BlockingCollection<(Mapping Map, string Input, bool ForceActiveWindow)> actionQueue = new(256);
    readonly Task actionWorker;
    readonly BlockingCollection<(Mapping? Map, string Input)> dragActionQueue = [];
    readonly Task dragActionWorker;
    readonly BlockingCollection<string> taskbarClickReplayQueue = [];
    readonly Task taskbarClickReplayWorker;
    int taskbarClickReplayFailed;
    readonly ConcurrentDictionary<string, InputMappingSnapshot> activeInputMappings = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, LayerMappingSnapshot> activeLayerMappings = new(StringComparer.OrdinalIgnoreCase);
    readonly System.Windows.Threading.DispatcherTimer trayNumberTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly System.Windows.Threading.DispatcherTimer profileSwitchTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    bool profileDropDownOpen;
    string explicitProfileSwitchProcess = "";
    string automaticProfileReturnName = "";
    string automaticProfileCandidateSignature = "";
    int automaticProfileCandidateSamples;
    bool inputProcessingSuppressedForForeground;
    DateTime suppressAutomaticProfileSwitchUntil = DateTime.MinValue;
    readonly string automaticProfileDiagnosticLog = Environment.GetEnvironmentVariable("RELYR_PROFILE_SWITCH_LOG") ?? "";
    readonly System.Windows.Threading.DispatcherTimer autoSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    readonly CancellationTokenSource updateCancellation = new();
    internal static readonly TimeSpan AutomaticUpdateCheckInterval = TimeSpan.FromDays(1);
    System.Drawing.Icon? numberedTrayIcon;
    System.Drawing.Icon? defaultTrayIcon;
    AppConfig config;
    AppConfig appliedConfig = null!;
    Mapping? selected;
    string selectedBaseInput = "";
    private bool loading;
    private bool hasUnsavedChanges;
    private bool detectMode;
    private bool allowClose;
    private readonly bool engineStarted;
    private bool editingSelectedInput;
    private bool selectionPulseSuppressed;
    int exitRequested;
    int restartRequested;
    int lowerKeyboardScaleSyncGeneration;
    int toolbarKeyboardAlignmentGeneration;
    string? pendingDetectedLayer;
    bool updateInProgress;
    DateTimeOffset lastAutomaticUpdateCheckAttempt;
    Task<UpdateCheckResult>? runningUpdateCheckTask;
    bool capsLockRemapped;
    string currentLayer = "通常";
    internal static IReadOnlyList<string> AllAssignmentLayerNames { get; } = ["Space", "CapsLock", "MouseRight", "MouseForward", "MouseBack", "Taskbar"];
    MacroWindow? macroWindow; bool engineBeforeMacroRecording, macroEmergencyStop, macroIsRecording;
    ProfileSwitchOverlay? profileOverlay;
    ArchiveProgressOverlay? archiveProgressOverlay;
    string lastProfileOverlayName = "";
    Mapping? copiedMapping;
    Mapping? copiedDeckMapping;
    Dictionary<string, Mapping?>? copiedMultiMappings;
    bool copiedMultiMappingsAreDeck;
    readonly HashSet<string> multiSelectedInputs = new(StringComparer.OrdinalIgnoreCase);
    int deckMultiSelectionAnchor;
    TextBox? destinationInputTarget;
    readonly List<System.Windows.Controls.Button> deckManagementButtons = [];
    readonly Dictionary<System.Windows.Controls.Button, TextBlock> deckManagementNameLabels = [];
    bool deckManagementMode;
    bool updatingDeckEditor;
    DeckLayoutDefinition? selectedDeckLayout;
    System.Windows.Controls.Button? deckReorderSource;
    System.Windows.Controls.Button? deckReorderTarget;
    System.Windows.Point deckReorderStart;
    int destinationFocusRequest;
    UpdateInfo? availableUpdate;
    UpdateCheckResult? lastUpdateCheck;
    SettingsWindow? settingsWindow;
    internal event Action<UpdateCheckResult>? UpdateCheckCompleted;
    readonly StableNotifyIcon tray = new();
    readonly bool suppressTray;
    readonly RuntimeRole runtimeRole;
    readonly bool inputHooksRequired;
    bool editorUiInitialized;
    int trayDisposed;
    Profile CurrentProfile => config.Profiles.First(x => x.Name == config.ActiveProfile);
    Profile AppliedProfile => appliedConfig.Profiles.FirstOrDefault(x => x.Name == appliedConfig.ActiveProfile) ?? appliedConfig.Profiles[0];
    List<Mapping> MappingCollectionForInput(string input) => DeckPanelLayout.IsInputName(input) && selectedDeckLayout != null ? selectedDeckLayout.Mappings : CurrentProfile.Mappings;
    public bool NeedsFirstRunSetup => !config.FirstRunCompleted;
    internal bool TitleBarUsesDarkMode
    {
        get; private set;
    }
    internal static string DisplayVersion
    {
        get
        {
            var v = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
        }
    }
    internal static Version RunningVersion
    {
        get
        {
            var v = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public MainWindow(bool skipSetup = false, bool suppressTray = false, AppConfig? startupConfig = null, RuntimeRole runtimeRole = RuntimeRole.Standard, bool startInputHooks = true, bool deferEditorUiUntilShown = false)
    {
        this.suppressTray = !NativeTrayRegistrationAllowed(suppressTray);
        this.runtimeRole = runtimeRole;
        inputHooksRequired = startInputHooks;
        loading = true;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            EnsureUpdateCheckStarted();
            QueuePendingUpdateNotes();
            Dispatcher.BeginInvoke((Action)ConstrainToCurrentWorkArea);
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) EnsureUpdateCheckStarted(); };
        StateChanged += (_, _) => { if (IsVisible && WindowState != WindowState.Minimized) EnsureUpdateCheckStarted(); };
        SourceInitialized += (_, _) =>
        {
            ApplyWindowsTitleBarTheme();
            ConstrainToCurrentWorkArea();
            if (runtimeRole == RuntimeRole.UiHost)
                OverlayUiBridge.Attach(this);
        };
        SystemEvents.UserPreferenceChanged += WindowsThemeChanged;
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke((Action)ConstrainToCurrentWorkArea);
        ArrangeInputWorkspace();
        VersionText.Text = "v" + DisplayVersion;
        Title = "RELYR v" + DisplayVersion;
        config = startupConfig ?? store.Load();
        DiagnosticLogStorage.Configure(config.DetailedDiagnosticsEnabled);
        ArchiveAutomationState.Set(config.AutoExtractDesktopArchives);
        ThemeService.Apply(config.ThemeMode);
        UiMotionService.Apply(config.UiAnimationsEnabled);
        ThemeService.ThemeChanged += AppThemeChanged;
        MacroPlayer.PlaybackFinished += MacroPlaybackFinished;
        bool configuredCapsLockRemap = LegacyKeyRemapService.HasCapsLockToF13();
        bool capsRestartPending = LegacyKeyRemapService.IsRestartStillPending(config);
        if (config.CapsLockRemapPendingRestart && !capsRestartPending)
        {
            config.CapsLockRemapPendingRestart = false;
            config.CapsLockRemapEffectiveBeforeRestart = false;
            config.CapsLockRemapChangedAtUtcTicks = 0;
            config.CapsLockLayerEnabled = configuredCapsLockRemap;
            store.Save(config);
        }
        else if (!capsRestartPending)
            config.CapsLockLayerEnabled = configuredCapsLockRemap;
        capsLockRemapped = capsRestartPending ? config.CapsLockRemapEffectiveBeforeRestart : configuredCapsLockRemap;
        engine.TreatF13AsCapsLock = capsLockRemapped;
        appliedConfig = store.Clone(config);
        UpdateThemeToolbarControls();
        OverlayService.Configure(
            DeckOverlayConfig,
            () => engine.HasCapturedPhysicalInput,
            mapping => { try { if (!actionQueue.IsAddingCompleted) actionQueue.TryAdd((mapping, mapping.Input, true)); } catch (InvalidOperationException) { } },
            PersistDeckPanelPosition,
            PersistInputPanelPosition,
            HandleOverlayDeckLayoutChanged,
            HandleOverlayDeckSlotsChanged,
            PersistDeckPanelSize,
            PersistDeckPanelPinned,
            PersistDeckPanelCollapsedPosition,
            HandleDeckOverlayPresentationChanged);
        Func<string, WindowActionTarget, bool>? ipcShortcut = runtimeRole == RuntimeRole.UiHost ? IpcRuntime.TrySendShortcut : null;
        Func<string, bool>? ipcText = runtimeRole == RuntimeRole.UiHost ? IpcRuntime.TrySendText : null;
        Func<string, bool>? ipcMouse = runtimeRole == RuntimeRole.UiHost ? IpcRuntime.TrySendMouse : null;
        Func<string, bool>? uiOverlayRequest = runtimeRole == RuntimeRole.ElevatedHelper ? OverlayUiBridge.RequestShow : null;
        // Physical input is already owned by the correctly privileged hook: the
        // medium UI hook for normal windows and the elevated hook for elevated
        // windows. Execute its mapping in that same process so a delayed or failed
        // helper connection can never disable keyboard or mouse layers.
        executor = new MappingExecutor(new SystemInputOutput(name => appliedConfig.Macros.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)), name => Dispatcher.BeginInvoke(() => SwitchProfile(name, true)), () => appliedConfig.KeyboardLayout == "US", () => appliedConfig, null, null, null, null, uiOverlayRequest, isElevatedInputHelper: runtimeRole == RuntimeRole.ElevatedHelper));
        taskbarExecutor = new MappingExecutor(new SystemInputOutput(name => appliedConfig.Macros.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)), name => Dispatcher.BeginInvoke(() => SwitchProfile(name, true)), () => appliedConfig.KeyboardLayout == "US", TaskbarExecutionConfig, null, null, null, null, uiOverlayRequest, isElevatedInputHelper: runtimeRole == RuntimeRole.ElevatedHelper));
        deckExecutor = new MappingExecutor(new SystemInputOutput(name => appliedConfig.Macros.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)), name => Dispatcher.BeginInvoke(() => SwitchProfile(name, true)), () => appliedConfig.KeyboardLayout == "US", DeckExecutionConfig, null, ipcShortcut, ipcText, ipcMouse, uiOverlayRequest, isDeckExecution: true, isElevatedInputHelper: runtimeRole == RuntimeRole.ElevatedHelper));
        actionWorker = Task.Run(ProcessActions);
        dragActionWorker = Task.Factory.StartNew(ProcessDragActions, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        taskbarClickReplayWorker = Task.Factory.StartNew(ProcessTaskbarClickReplays, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        AutoSaveToggle.IsChecked = config.AutoSave;
        UpdateAutoSaveToggleText();
        KeyboardLayoutBox.SelectedIndex = config.KeyboardLayout == "US" ? 1 : 0;
        if (!deferEditorUiUntilShown)
            EnsureEditorUiInitialized();
        else
            loading = false;
        engine.UseUsLayout = config.KeyboardLayout == "US";
        engine.SpaceHoldRepeatEnabled = config.SpaceHoldRepeatEnabled;
        engine.SpaceHoldRepeatDelayMs = config.SpaceHoldRepeatDelayMs;
        engine.LockCursorDuringGesture = config.LockCursorDuringGesture;
        engine.InputReceived = HandleInput;
        engine.InputStarted = CaptureInputMapping;
        engine.InputEnded = ReleaseInputMapping;
        engine.LayerStarted = CaptureLayerMappings;
        engine.LayerEnded = ReleaseLayerMappings;
        InputEngine.DesktopActionFailed = message => Dispatcher.BeginInvoke(() => { LastInput.Text = "仮想デスクトップ操作エラー: " + message; LastInput.Foreground = ThemeService.Brush("DangerBrush"); });
        engine.QualifyInput = QualifyInput;
        engine.HasMapping = runtimeRole == RuntimeRole.ElevatedHelper ? HasElevatedForegroundMapping : HasMapping;
        // Each physical input is owned by one hook. The UI skips an elevated
        // foreground window, while either hook retains an already captured
        // press until its physical release is received.
        engine.ShouldInterceptInput = runtimeRole switch
        {
            RuntimeRole.UiHost => () => engine.HasCapturedPhysicalInput || (!Volatile.Read(ref inputProcessingSuppressedForForeground) && !WindowMonitorService.IsForegroundWindowElevated()),
            RuntimeRole.ElevatedHelper => () => engine.HasCapturedPhysicalInput || (!Volatile.Read(ref inputProcessingSuppressedForForeground) && !ConditionMatcher.IsForegroundVirtualMachineConsole() && WindowMonitorService.IsForegroundWindowElevated()),
            _ => () => engine.HasCapturedPhysicalInput || !Volatile.Read(ref inputProcessingSuppressedForForeground)
        };
        engine.ShouldInterceptMouseInput = engine.ShouldInterceptInput;
        engine.IsNativeMouseDrag = input => FindCapturedInputMapping(input) is { Kind: ActionKind.Mouse } map && MappingExecutor.IsModifierDrag(map.Value);
        engine.HasLegacyMouseDrag = input => FindCapturedInputMapping(input) is { } map && (!string.IsNullOrWhiteSpace(map.DragValue) || !string.IsNullOrWhiteSpace(map.DragEndValue));
        engine.SuppressLayerTap = key => key.Equals("CapsLock", StringComparison.OrdinalIgnoreCase);
        engine.HasLongPress = input => HasConfiguredLongPress(FindCapturedInputMapping(input));
        engine.IsGesturePress = input => FindCapturedInputMapping(input)?.Kind == ActionKind.Gesture;
        engine.IsGestureLongPress = input => FindCapturedInputMapping(input)?.LongPressKind == ActionKind.Gesture;
        engine.LongPressDuration = input => FindCapturedInputMapping(input)?.LongPressMs ?? 500;
        engine.DragPixels = config.MouseDragPixels;
        engine.GestureThresholdPixels = config.GestureThresholdPixels;
        RefreshInputProcessingSuppression();
        engine.Detected += text => Dispatcher.BeginInvoke(() => HandleDetectedInput(text));
        engine.Enabled = false;
        // The UI hook remains medium integrity so Explorer can drop files onto
        // Deck. The elevated helper has a second, filtered hook which handles
        // configured key and shortcut mappings while an elevated window is foreground.
        if (!startInputHooks)
        {
            // UI integration tests use the exact engine callbacks and workers,
            // but must never register a second system-wide hook beside the
            // user's running RELYR. Treat the direct-event backend as ready.
            engineStarted = true;
            engine.Enabled = config.EngineEnabled;
        }
        else if (runtimeRole == RuntimeRole.ElevatedHelper)
        {
            try
            {
                engine.Start();
                engineStarted = true;
                DeckIpcDiagnostics.LogIpcStartup("Elevated helper input hook", "started; scoped to elevated foreground key and shortcut mappings");
            }
            catch (Exception ex)
            {
                DeckIpcDiagnostics.LogIpcStartup("Elevated helper input hook", "start failed: " + ex.GetType().Name);
            }
            engine.Enabled = engineStarted && config.EngineEnabled;
        }
        else
        {
            try
            {
                engine.Start();
                engineStarted = true;
            }
            catch (Exception ex) { config.EngineEnabled = false; appliedConfig.EngineEnabled = false; store.Save(config); WpfMessageBox.Show("入力フックを開始できません。エンジンを停止しました。\n\n" + ex.Message, "入力エンジンを開始できません", MessageBoxButton.OK, MessageBoxImage.Error); }
            engine.Enabled = engineStarted && config.EngineEnabled;
        }
        EngineToggle.IsChecked = engine.Enabled;
        EngineToggle.IsEnabled = engineStarted;
        archiveWatcher.Status += text => Dispatcher.BeginInvoke(() => LastInput.Text = text);
        archiveWatcher.ActivityChanged += activity => Dispatcher.BeginInvoke(() => HandleArchiveActivity(activity));
        ApplyArchiveWatcherConfiguration();
        if (!this.suppressTray)
        {
            SetupTray();
            trayNumberTimer.Tick += (_, _) => UpdateTrayNumber();
            trayNumberTimer.Start();
        }
#if !PRODUCTION_PUBLISH
        else
        {
            // Tests exercise the menu commands without ever registering an
            // icon in Explorer or polluting Windows notification settings.
            RebuildTrayMenu();
        }
#endif
        profileSwitchTimer.Tick += (_, _) => AutoSwitchProfile();
        profileSwitchTimer.Start();
        autoSaveTimer.Tick += (_, _) => { autoSaveTimer.Stop(); SaveAndApply("自動保存しました"); };
        UpdateStatus();
        if (capsRestartPending)
        {
            LastInput.Text = "CapsLock設定は再起動待ちです — Windowsを再起動するまで変更は有効になりません";
            LastInput.Foreground = ThemeService.Brush("WarningBrush");
        }
        else if (configuredCapsLockRemap)
        {
            LastInput.Text = "CapsLock→F13設定を検出しました。CapsLockレイヤーとして互換動作します";
            LastInput.Foreground = ThemeService.Brush("AccentTextBrush");
        }
        if (skipSetup && NeedsFirstRunSetup)
        {
            config.FirstRunCompleted = true;
            store.Save(config);
        }
    }

    // Runtime input must become ready during a tray launch without paying the
    // cost of constructing controls that are used only by the editor.  Keep
    // this boundary explicit: mappings, hooks, profile routing, Deck overlay
    // execution, and the tray do not depend on these visual controls.
    void EnsureEditorUiInitialized()
    {
        if (editorUiInitialized)
            return;

        editorUiInitialized = true;
        loading = true;
        try
        {
            KindBox.ItemsSource = ActionOptions(allowGesture: true);
            LongKindBox.ItemsSource = ActionOptions(allowGesture: false);
            KindBox.SelectedValuePath = nameof(ActionOption.SelectionKind);
            LongKindBox.SelectedValuePath = nameof(ActionOption.SelectionKind);
            BuildKeyboard();
            BuildDeckManagementPanel();
            RefreshProfiles();
            UpdateLayerButtons();
            RefreshActionPalette();
        }
        finally
        {
            loading = false;
        }
    }

    internal void ReloadRuntimeConfigForIpc()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ReloadRuntimeConfigForIpc);
            return;
        }
        var latest = store.Load();
        config = latest;
        appliedConfig = store.Clone(latest);
        DiagnosticLogStorage.Configure(config.DetailedDiagnosticsEnabled);
        engine.UseUsLayout = config.KeyboardLayout == "US";
        engine.SpaceHoldRepeatEnabled = config.SpaceHoldRepeatEnabled;
        engine.SpaceHoldRepeatDelayMs = config.SpaceHoldRepeatDelayMs;
        engine.GestureThresholdPixels = config.GestureThresholdPixels;
        engine.LockCursorDuringGesture = config.LockCursorDuringGesture;
        engine.DragPixels = config.MouseDragPixels;
        if (engineStarted)
            engine.Enabled = config.EngineEnabled;
    }

    internal void ExecuteShortcutForIpc(string value, WindowActionTarget target)
    {
        DeckIpcDiagnostics.LogHelperReceivedShortcut(value, target);
        using var diagnostic = DeckIpcDiagnostics.BeginHelperInput(value, target);
        try
        {
            InputEngine.SendShortcut(value, config.KeyboardLayout == "US", target);
            diagnostic.Complete();
        }
        catch (Exception error)
        {
            diagnostic.Fail(error);
            throw;
        }
    }
    internal void ExecuteTextForIpc(string value)
        => InputEngine.SendText(value, config.KeyboardLayout == "US");
    internal void ExecuteMouseForIpc(string value)
        => InputEngine.SendMouse(value);
    void ArrangeInputWorkspace()
    {
        if (!ReferenceEquals(LayerNavigationPane.Parent, ShellDock))
        {
            if (LayerNavigationPane.Parent is System.Windows.Controls.Panel navigationParent)
                navigationParent.Children.Remove(LayerNavigationPane);
            DockPanel.SetDock(LayerNavigationPane, Dock.Left);
            ShellDock.Children.Insert(0, LayerNavigationPane);
        }
        if (LayerButtonsPanel.Parent is System.Windows.Controls.Panel layerParent)
            layerParent.Children.Remove(LayerButtonsPanel);
        LayerButtonsPanel.Orientation = System.Windows.Controls.Orientation.Vertical;
        LayerButtonsPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        LayerNavigationHost.Children.Add(LayerButtonsPanel);
        EnsureLayerCardIndicators();

        if (MousePanel.Parent is System.Windows.Controls.Panel mouseParent)
        {
            mouseParent.Children.Remove(MousePanel);
        }
        MouseHost.Child = MousePanel;
        if (!ReferenceEquals(LeftBottomActions.Parent, LayerNavigationGrid))
        {
            if (LeftBottomActions.Parent is System.Windows.Controls.Panel actionsParent)
                actionsParent.Children.Remove(LeftBottomActions);
            LayerNavigationGrid.Children.Add(LeftBottomActions);
        }
        Grid.SetRow(LeftBottomActions, 2);
        UpdateLayerButtonWidths();
    }

    void EnsureLayerCardIndicators()
    {
        foreach (var button in LayerButtonsPanel.Children.OfType<System.Windows.Controls.Button>())
        {
            if (button.Content is not Grid grid || grid.Children.OfType<System.Windows.Shapes.Ellipse>().Any(x => Equals(x.Tag, "LayerActiveIndicator")))
                continue;
            var indicator = new System.Windows.Shapes.Ellipse { Tag = "LayerActiveIndicator", Style = (Style)FindResource("LayerActiveDot"), HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            Grid.SetColumn(indicator, 0);
            Grid.SetColumnSpan(indicator, grid.ColumnDefinitions.Count);
            grid.Children.Add(indicator);
        }
    }

    void WindowsThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Color or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
            Dispatcher.BeginInvoke((Action)(() => { ThemeService.RefreshSystemTheme(); ApplyWindowsTitleBarTheme(); }));
    }

    void DisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke((Action)ConstrainToCurrentWorkArea);

    void ConstrainToCurrentWorkArea()
    {
        if (WindowState != WindowState.Normal)
            return;
        IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
            return;
        var pixels = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var topLeft = target.TransformFromDevice.Transform(new System.Windows.Point(pixels.Left, pixels.Top));
        var bottomRight = target.TransformFromDevice.Transform(new System.Windows.Point(pixels.Right, pixels.Bottom));
        var workArea = new Rect(topLeft, bottomRight);
        double safeWidth = Math.Max(1, workArea.Width - MainWindowWorkAreaInset * 2);
        double safeHeight = Math.Max(1, workArea.Height - MainWindowWorkAreaInset * 2);
        MinWidth = Math.Min(DefaultMainWindowMinWidth, safeWidth);
        MinHeight = Math.Min(DefaultMainWindowMinHeight, safeHeight);
        double currentWidth = ActualWidth > 0 ? ActualWidth : Width;
        double currentHeight = ActualHeight > 0 ? ActualHeight : Height;
        Rect constrained = ConstrainWindowBoundsForTest(new Rect(Left, Top, currentWidth, currentHeight), workArea);
        Width = constrained.Width;
        Height = constrained.Height;
        Left = constrained.Left;
        Top = constrained.Top;
    }

    internal static Rect ConstrainWindowBoundsForTest(Rect requested, Rect workArea)
    {
        double insetX = workArea.Width > MainWindowWorkAreaInset * 2 ? MainWindowWorkAreaInset : 0;
        double insetY = workArea.Height > MainWindowWorkAreaInset * 2 ? MainWindowWorkAreaInset : 0;
        var safe = new Rect(workArea.Left + insetX, workArea.Top + insetY, Math.Max(1, workArea.Width - insetX * 2), Math.Max(1, workArea.Height - insetY * 2));
        double width = Math.Clamp(double.IsFinite(requested.Width) ? requested.Width : safe.Width, Math.Min(DefaultMainWindowMinWidth, safe.Width), safe.Width);
        double height = Math.Clamp(double.IsFinite(requested.Height) ? requested.Height : safe.Height, Math.Min(DefaultMainWindowMinHeight, safe.Height), safe.Height);
        double left = double.IsFinite(requested.Left) ? requested.Left : safe.Left + (safe.Width - width) / 2;
        double top = double.IsFinite(requested.Top) ? requested.Top : safe.Top + (safe.Height - height) / 2;
        left = Math.Clamp(left, safe.Left, safe.Right - width);
        top = Math.Clamp(top, safe.Top, safe.Bottom - height);
        return new Rect(left, top, width, height);
    }

    void AppThemeChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke((Action)AppThemeChanged);
            return;
        }
        ApplyWindowsTitleBarTheme();
        UpdateThemeToolbarControls();
        RebuildTrayMenu();
        if (editorUiInitialized && KeyboardPanel != null)
        {
            BuildKeyboard();
            ColorButtons();
            UpdateLayerButtons();
        }
        if (DeckLayoutCardsPanel != null && DeckLayoutListWorkspace.Visibility == Visibility.Visible)
            RefreshDeckLayoutCards();
        if (engine != null)
            UpdateStatus();
    }

    internal static bool IsWindowsAppDarkMode()
    {
        try
        {
            return ThemeService.SystemUsesDarkMode();
        }
        catch { return false; }
    }

    void ApplyWindowsTitleBarTheme()
    {
        TitleBarUsesDarkMode = ApplyWindowsTitleBarTheme(this);
    }
    internal static bool ApplyWindowsTitleBarTheme(Window window)
    {
        IntPtr handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return false;
        bool dark = ThemeService.UsesDark;
        int enabled = dark ? 1 : 0;
        int result = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        if (result != 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        return dark;
    }
    internal static void FollowWindowsTitleBarTheme(Window window, Action<bool>? applied = null)
    {
        void Apply()
        {
            if (!window.Dispatcher.HasShutdownStarted)
            {
                bool dark = ApplyWindowsTitleBarTheme(window);
                applied?.Invoke(dark);
            }
        }
        Action themeHandler = Apply;
        void handler(object _, UserPreferenceChangedEventArgs e)
        {
            if (e.Category is UserPreferenceCategory.Color or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
                window.Dispatcher.BeginInvoke((Action)Apply);
        }
        window.SourceInitialized += (_, _) => Apply();
        SystemEvents.UserPreferenceChanged += handler;
        ThemeService.ThemeChanged += themeHandler;
        window.Closed += (_, _) => { SystemEvents.UserPreferenceChanged -= handler; ThemeService.ThemeChanged -= themeHandler; };
    }

    void BuildKeyboard()
    {
        if (!editorUiInitialized)
            return;
        KeyboardPanel.Children.Clear();
        SecondaryKeyboardPanel.Children.Clear();
        KeyboardPanel.Width = config.KeyboardLayout == "US" ? 900 : 942;
        LayoutMousePanel();
        AddSecondaryGroupFrames();
        if (config.KeyboardLayout == "US")
        {
            BuildUsKeyboard();
            return;
        }
        AddTopFunctionRow(942);
        AddKey("PrintScreen", "Print", 970, 0, 72);
        AddKey("ScrollLock", "Scroll", 1046, 0, 72);
        AddKey("Pause", "Pause", 1122, 0, 72);

        AddRow(44, [new("半角/全角", "半角/全角", 88), new("1", "1", 54), new("2", "2", 54), new("3", "3", 54), new("4", "4", 54), new("5", "5", 54), new("6", "6", 54), new("7", "7", 54), new("8", "8", 54), new("9", "9", 54), new("0", "0", 54), new("-", "-", 54), new("^", "^", 54), new("¥", "¥", 54), new("Back", "Backspace", 96)]);
        AddKey("Insert", "Insert", 970, 70, 72);
        AddKey("Home", "Home", 1046, 70, 72);
        AddKey("PageUp", "Page\nUp", 1122, 70, 72);
        AddRow(100, [new("Tab", "Tab", 82), new("Q", "Q", 54), new("W", "W", 54), new("E", "E", 54), new("R", "R", 54), new("T", "T", 54), new("Y", "Y", 54), new("U", "U", 54), new("I", "I", 54), new("O", "O", 54), new("P", "P", 54), new("@", "@", 54), new("[", "[", 54)]);
        AddKey("Delete", "Delete", 970, 128, 72);
        AddKey("End", "End", 1046, 128, 72);
        AddKey("PageDown", "Page\nDown", 1122, 128, 72);
        AddRow(156, [new("CapsLock", "CapsLock\n(F13設定時)", 104), new("A", "A", 54), new("S", "S", 54), new("D", "D", 54), new("F", "F", 54), new("G", "G", 54), new("H", "H", 54), new("J", "J", 54), new("K", "K", 54), new("L", "L", 54), new(";", ";", 54), new(":", ":", 54), new("]", "]", 54)]);
        AddJisEnter();
        AddRow(212, [new("LeftShift", "Shift", 126), new("Z", "Z", 54), new("X", "X", 54), new("C", "C", 54), new("V", "V", 54), new("B", "B", 54), new("N", "N", 54), new("M", "M", 54), new(",", ",", 54), new(".", ".", 54), new("/", "/", 54), new("_", "＼  _", 54), new("RightShift", "Shift", 174)]);
        AddKey("Up", "↑", 1046, 244, 54);
        AddRow(268, [new("LeftCtrl", "Ctrl", 78), new("LWin", "Win", 64), new("LeftAlt", "Alt", 68), new("無変換", "無変換", 76), new("Space", "Space", 248), new("変換", "変換", 72), new("カタカナ", "カタカナ", 78), new("RightAlt", "Alt", 68), new("RWin", "Win", 64), new("RightCtrl", "Ctrl", 90)]);
        AddKey("Left", "←", 970, 302, 54);
        AddKey("Down", "↓", 1046, 302, 54);
        AddKey("Right", "→", 1122, 302, 54);

        AddKey("NumLock", "Num", 1210, 70, 62);
        AddKey("Divide", "÷", 1276, 70, 62);
        AddKey("Multiply", "×", 1342, 70, 62);
        AddKey("Subtract", "−", 1408, 70, 62);
        AddKey("NumPad7", "7", 1210, 128, 62);
        AddKey("NumPad8", "8", 1276, 128, 62);
        AddKey("NumPad9", "9", 1342, 128, 62);
        AddKey("Add", "＋", 1408, 128, 62, 108);
        AddKey("NumPad4", "4", 1210, 186, 62);
        AddKey("NumPad5", "5", 1276, 186, 62);
        AddKey("NumPad6", "6", 1342, 186, 62);
        AddKey("NumPad1", "1", 1210, 244, 62);
        AddKey("NumPad2", "2", 1276, 244, 62);
        AddKey("NumPad3", "3", 1342, 244, 62);
        AddKey("NumPadEnter", "Enter", 1408, 244, 62, 108);
        AddKey("NumPad0", "0", 1210, 302, 128);
        AddKey("Decimal", ".", 1342, 302, 62);

        AddFunctionExtension();
    }
    void BuildUsKeyboard()
    {
        AddTopFunctionRow(900);
        AddKey("PrintScreen", "Print", 970, 0, 72);
        AddKey("ScrollLock", "Scroll", 1046, 0, 72);
        AddKey("Pause", "Pause", 1122, 0, 72);
        AddRow(44, [new("`", "`", 56), new("1", "1", 56), new("2", "2", 56), new("3", "3", 56), new("4", "4", 56), new("5", "5", 56), new("6", "6", 56), new("7", "7", 56), new("8", "8", 56), new("9", "9", 56), new("0", "0", 56), new("-", "-", 56), new("=", "=", 56), new("Back", "Backspace", 120)]);
        AddKey("Insert", "Insert", 970, 70, 72);
        AddKey("Home", "Home", 1046, 70, 72);
        AddKey("PageUp", "Page\nUp", 1122, 70, 72);
        AddRow(100, [new("Tab", "Tab", 88), new("Q", "Q", 56), new("W", "W", 56), new("E", "E", 56), new("R", "R", 56), new("T", "T", 56), new("Y", "Y", 56), new("U", "U", 56), new("I", "I", 56), new("O", "O", 56), new("P", "P", 56), new("[", "[", 56), new("]", "]", 56), new("\\", "＼", 88)]);
        AddKey("Delete", "Delete", 970, 128, 72);
        AddKey("End", "End", 1046, 128, 72);
        AddKey("PageDown", "Page\nDown", 1122, 128, 72);
        AddRow(156, [new("CapsLock", "CapsLock\n(F13設定時)", 102), new("A", "A", 56), new("S", "S", 56), new("D", "D", 56), new("F", "F", 56), new("G", "G", 56), new("H", "H", 56), new("J", "J", 56), new("K", "K", 56), new("L", "L", 56), new(";", ";", 56), new("'", "'", 56), new("Enter", "Enter", 134)]);
        AddRow(212, [new("LeftShift", "Shift", 136), new("Z", "Z", 56), new("X", "X", 56), new("C", "C", 56), new("V", "V", 56), new("B", "B", 56), new("N", "N", 56), new("M", "M", 56), new(",", ",", 56), new(".", ".", 56), new("/", "/", 56), new("RightShift", "Shift", 160)]);
        AddKey("Up", "↑", 1046, 244, 56);
        AddRow(268, [new("LeftCtrl", "Ctrl", 72), new("LWin", "Win", 72), new("LeftAlt", "Alt", 72), new("Space", "Space", 368), new("RightAlt", "Alt", 72), new("RWin", "Win", 72), new("Menu", "Menu", 72), new("RightCtrl", "Ctrl", 72)]);
        AddKey("Left", "←", 970, 302, 56);
        AddKey("Down", "↓", 1046, 302, 56);
        AddKey("Right", "→", 1122, 302, 56);
        AddNumpad();
        AddFunctionExtension();
    }
    void AddTopFunctionRow(double rightEdge)
    {
        const int keyCount = 14;
        const double gap = 4;
        double keyWidth = (rightEdge - gap * (keyCount - 1)) / keyCount;
        double x = 0;
        AddMainKey("Esc", "Esc", x, 0, keyWidth, 26);
        x += keyWidth + gap;
        for (int i = 1; i <= 12; i++)
        {
            AddMainKey($"F{i}", $"F{i}", x, 0, keyWidth, 26);
            x += keyWidth + gap;
        }
        AddMainKey("Delete", "Delete", x, 0, keyWidth, 26);
    }
    void AddMainKey(string key, string label, double x, double y, double width, double height)
    {
        var b = MakeInputButton(key);
        b.Content = label;
        b.Width = width;
        b.Height = height;
        b.MinWidth = 0;
        b.Margin = new Thickness(0);
        Canvas.SetLeft(b, x);
        Canvas.SetTop(b, y);
        KeyboardPanel.Children.Add(b);
    }
    void AddNumpad()
    {
        AddKey("NumLock", "Num", 1210, 70, 62);
        AddKey("Divide", "÷", 1276, 70, 62);
        AddKey("Multiply", "×", 1342, 70, 62);
        AddKey("Subtract", "−", 1408, 70, 62);
        AddKey("NumPad7", "7", 1210, 128, 62);
        AddKey("NumPad8", "8", 1276, 128, 62);
        AddKey("NumPad9", "9", 1342, 128, 62);
        AddKey("Add", "＋", 1408, 128, 62, 108);
        AddKey("NumPad4", "4", 1210, 186, 62);
        AddKey("NumPad5", "5", 1276, 186, 62);
        AddKey("NumPad6", "6", 1342, 186, 62);
        AddKey("NumPad1", "1", 1210, 244, 62);
        AddKey("NumPad2", "2", 1276, 244, 62);
        AddKey("NumPad3", "3", 1342, 244, 62);
        AddKey("NumPadEnter", "Enter", 1408, 244, 62, 108);
        AddKey("NumPad0", "0", 1210, 302, 128);
        AddKey("Decimal", ".", 1342, 302, 62);
    }
    void AddFunctionExtension()
    {
        const double gap = 4;
        double rightEdge = config.KeyboardLayout == "US" ? 900 : 942;
        double width = (rightEdge - gap * 13) / 14;
        double x = 0;
        for (int i = 14; i <= 24; i++)
        {
            AddKey($"F{i}", $"F{i}", x, 328, width, 26);
            x += width + gap;
        }
    }
    void AddRow(double y, IEnumerable<KeySpec> keys)
    {
        double x = 0;
        foreach (var key in keys)
        {
            // The JIS home row must leave a real four-pixel gutter before Enter.
            // Keep the existing labels, but use 52px letter keys so the row ends at x=776.
            double width = key.Width;
            AddKey(key.Key, key.Label, x, y, width);
            x += width + 4;
        }
    }
    void AddKey(string key, string label, double x, double y, double width, double height = 52)
    {
        var panel = KeyboardPanel;
        if (TrySecondaryPosition(key, out double secondaryX, out double secondaryY))
        {
            panel = SecondaryKeyboardPanel;
            x = secondaryX;
            y = secondaryY;
            width = SecondaryKeyWidth;
            height = SecondaryKeyHeight;
            if (key == "NumPad0")
                width = SecondaryKeyWidth * 2 + SecondaryKeyGap;
            else if (key is "Add" or "NumPadEnter")
                height = SecondaryKeyHeight * 2 + SecondaryKeyGap;
        }
        var b = MakeInputButton(key);
        b.Content = KeyLabel(label);
        b.Width = width;
        b.Height = height;
        b.MinWidth = 0;
        b.Margin = new Thickness(0);
        Canvas.SetLeft(b, x);
        Canvas.SetTop(b, y);
        panel.Children.Add(b);
    }
    static object KeyLabel(string label) => label.Contains('\n')
        ? new TextBlock { Text = label, TextAlignment = TextAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        : label;
    const double SecondaryKeyHeight = 52, SecondaryKeyGap = 4, SecondaryFramePadding = 10, SecondaryKeyTop = 26, SecondaryGroupGap = 12;
    const double MaximumKeyboardWorkspaceScale = 1.15;
    double SecondaryKeyWidth => config.KeyboardLayout == "US" ? 56 : 54;
    void LayoutMousePanel()
    {
        const double gap = SecondaryKeyGap, keyHeight = SecondaryKeyHeight, padding = 10;
        const double top = 10, tiltLabelTop = 181, tiltTop = 200, forwardTop = 270, backTop = 326, panelHeight = 390;
        double unit = SecondaryKeyWidth;
        double doubleHeight = keyHeight * 3 + gap * 2;
        double centerX = padding + unit + gap;
        double rightX = centerX + unit + gap;
        double panelWidth = padding * 2 + unit * 3 + gap * 2;
        double tiltWidth = unit * 2 + gap;
        double tiltLeft = (panelWidth - tiltWidth) / 2;

        MousePanel.Width = MouseCanvas.Width = MouseBody.Width = panelWidth;
        MousePanel.Height = MouseCanvas.Height = MouseBody.Height = panelHeight;
        SetMouseBounds(MouseLeftVisual, padding, top, unit, doubleHeight);
        SetMouseBounds(MouseRightVisual, rightX, top, unit, doubleHeight);
        SetMouseBounds(WheelUpVisual, centerX, top, unit, keyHeight);
        SetMouseBounds(MouseMiddleVisual, centerX, top + keyHeight + gap, unit, keyHeight);
        SetMouseBounds(WheelDownVisual, centerX, top + (keyHeight + gap) * 2, unit, keyHeight);
        Canvas.SetLeft(TiltLabel, tiltLeft);
        Canvas.SetTop(TiltLabel, tiltLabelTop);
        TiltLabel.Width = tiltWidth;
        SetMouseBounds(TiltLeftVisual, tiltLeft, tiltTop, unit, keyHeight);
        SetMouseBounds(TiltRightVisual, tiltLeft + unit + gap, tiltTop, unit, keyHeight);
        SetMouseBounds(MouseForwardVisual, padding, forwardTop, unit, keyHeight);
        SetMouseBounds(MouseBackVisual, padding, backTop, unit, keyHeight);
        SetMouseBounds(MouseXVisual, rightX, backTop, unit, keyHeight);
    }
    static void SetMouseBounds(System.Windows.Controls.Button button, double x, double y, double width, double height)
    {
        Canvas.SetLeft(button, x);
        Canvas.SetTop(button, y);
        button.Width = width;
        button.Height = height;
        button.MinWidth = 0;
        button.MinHeight = 0;
    }
    void AddSecondaryGroupFrames()
    {
        double unit = SecondaryKeyWidth;
        double navigationWidth = SecondaryFramePadding * 2 + unit * 3 + SecondaryKeyGap * 2;
        double navigationHeight = SecondaryKeyTop + SecondaryKeyHeight * 3 + SecondaryKeyGap * 2 + SecondaryFramePadding;
        double numpadX = navigationWidth + SecondaryGroupGap;
        double numpadWidth = SecondaryFramePadding * 2 + unit * 4 + SecondaryKeyGap * 3;
        double numpadHeight = SecondaryKeyTop + SecondaryKeyHeight * 5 + SecondaryKeyGap * 4 + SecondaryFramePadding;
        double cursorX = numpadX + numpadWidth + SecondaryGroupGap;
        double cursorWidth = navigationWidth;
        double cursorHeight = SecondaryKeyTop + SecondaryKeyHeight * 2 + SecondaryKeyGap + SecondaryFramePadding;
        double commonHeight = Math.Max(navigationHeight, Math.Max(numpadHeight, cursorHeight));
        AddSecondaryGroupFrame("ナビゲーション", 0, 0, navigationWidth, commonHeight);
        AddSecondaryGroupFrame("テンキー", numpadX, 0, numpadWidth, numpadHeight);
        AddSecondaryGroupFrame("カーソルキー", cursorX, 0, cursorWidth, commonHeight);
        SecondaryKeyboardPanel.Width = cursorX + cursorWidth;
        SecondaryKeyboardPanel.Height = commonHeight;
    }
    void AddSecondaryGroupFrame(string title, double x, double y, double width, double height)
    {
        var frame = new Border
        {
            Width = width,
            Height = height,
            Tag = title,
            CornerRadius = new CornerRadius(0),
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Effect = null,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(frame, x);
        Canvas.SetTop(frame, y);
        SecondaryKeyboardPanel.Children.Add(frame);
        var heading = new TextBlock { Text = title, Foreground = ThemeService.Brush("MutedText"), FontSize = 11, FontWeight = FontWeights.SemiBold, IsHitTestVisible = false };
        Canvas.SetLeft(heading, x);
        Canvas.SetTop(heading, y + 7);
        SecondaryKeyboardPanel.Children.Add(heading);
    }
    bool TrySecondaryPosition(string key, out double x, out double y)
    {
        double unit = SecondaryKeyWidth, stepX = unit + SecondaryKeyGap, stepY = SecondaryKeyHeight + SecondaryKeyGap;
        double navigationWidth = SecondaryFramePadding * 2 + unit * 3 + SecondaryKeyGap * 2;
        double numpadX = navigationWidth + SecondaryGroupGap;
        double numpadWidth = SecondaryFramePadding * 2 + unit * 4 + SecondaryKeyGap * 3;
        double cursorX = numpadX + numpadWidth + SecondaryGroupGap;
        // The main keyboard starts at the shared 18-DIP workspace inset. The
        // secondary Viewbox owns that inset, so every lower group starts at
        // its local zero instead of adding a second invisible card padding.
        double navLeft = 0, numpadLeft = numpadX, cursorLeft = cursorX;
        (x, y) = key switch
        {
            "Insert" => (navLeft, SecondaryKeyTop),
            "Home" => (navLeft + stepX, SecondaryKeyTop),
            "PageUp" => (navLeft + stepX * 2, SecondaryKeyTop),
            "Delete" => (navLeft, SecondaryKeyTop + stepY),
            "End" => (navLeft + stepX, SecondaryKeyTop + stepY),
            "PageDown" => (navLeft + stepX * 2, SecondaryKeyTop + stepY),
            "PrintScreen" => (navLeft, SecondaryKeyTop + stepY * 2),
            "ScrollLock" => (navLeft + stepX, SecondaryKeyTop + stepY * 2),
            "Pause" => (navLeft + stepX * 2, SecondaryKeyTop + stepY * 2),
            "NumLock" => (numpadLeft, SecondaryKeyTop),
            "Divide" => (numpadLeft + stepX, SecondaryKeyTop),
            "Multiply" => (numpadLeft + stepX * 2, SecondaryKeyTop),
            "Subtract" => (numpadLeft + stepX * 3, SecondaryKeyTop),
            "NumPad7" => (numpadLeft, SecondaryKeyTop + stepY),
            "NumPad8" => (numpadLeft + stepX, SecondaryKeyTop + stepY),
            "NumPad9" => (numpadLeft + stepX * 2, SecondaryKeyTop + stepY),
            "Add" => (numpadLeft + stepX * 3, SecondaryKeyTop + stepY),
            "NumPad4" => (numpadLeft, SecondaryKeyTop + stepY * 2),
            "NumPad5" => (numpadLeft + stepX, SecondaryKeyTop + stepY * 2),
            "NumPad6" => (numpadLeft + stepX * 2, SecondaryKeyTop + stepY * 2),
            "NumPad1" => (numpadLeft, SecondaryKeyTop + stepY * 3),
            "NumPad2" => (numpadLeft + stepX, SecondaryKeyTop + stepY * 3),
            "NumPad3" => (numpadLeft + stepX * 2, SecondaryKeyTop + stepY * 3),
            "NumPadEnter" => (numpadLeft + stepX * 3, SecondaryKeyTop + stepY * 3),
            "NumPad0" => (numpadLeft, SecondaryKeyTop + stepY * 4),
            "Decimal" => (numpadLeft + stepX * 2, SecondaryKeyTop + stepY * 4),
            "Up" => (cursorLeft + stepX, SecondaryKeyTop),
            "Left" => (cursorLeft, SecondaryKeyTop + stepY),
            "Down" => (cursorLeft + stepX, SecondaryKeyTop + stepY),
            "Right" => (cursorLeft + stepX * 2, SecondaryKeyTop + stepY),
            _ => (double.NaN, double.NaN)
        };
        return !double.IsNaN(x);
    }
    void AddJisEnter()
    {
        // Keep the inner step centered in the row gutter. A straight inner edge
        // prevents the visual gap next to ] from narrowing at the transition.
        var roundedShape = Geometry.Parse("M8,0 H152 A8,8 0 0 1 160,8 V98.86 A8,8 0 0 1 152,106.86 H32 A8,8 0 0 1 24,98.86 V60 C24,55.582 20.418,52 16,52 H8 A8,8 0 0 1 0,44 V8 A8,8 0 0 1 8,0 Z");
        var button = MakeInputButton("Enter");
        button.Style = (Style)FindResource("JisEnterButton");
        button.Content = "Enter";
        button.Width = 160;
        button.Height = 108;
        button.MinWidth = 0;
        button.Margin = new Thickness(0);
        button.Clip = roundedShape;
        Canvas.SetLeft(button, 782);
        Canvas.SetTop(button, 100);
        KeyboardPanel.Children.Add(button);
    }
    readonly record struct KeySpec(string Key, string Label, double Width);
    System.Windows.Controls.Button MakeInputButton(string key)
    {
        var b = new System.Windows.Controls.Button { Content = key == "CapsLock" ? "CapsLock\n(F13設定時)" : key, Tag = key, Style = (Style)FindResource("KeyButton") };
        if (key == "Space")
            b.Width = 210;
        else if (key == "CapsLock")
        {
            b.MinWidth = 94;
            b.FontSize = 10;
        }
        else if (key is "LeftShift" or "RightShift" or "Enter" or "Back")
            b.MinWidth = 82;
        else if (key is "Tab" or "LeftCtrl" or "RightCtrl")
            b.MinWidth = 70;
        b.Click += (_, _) => SelectVisualInput(key);
        b.MouseDoubleClick += (_, _) => OpenShortcutForVisualInput(key);
        return b;
    }
    void SelectVisualInput(string key)
    {
        string input = InputForCurrentLayer(key);
        if (InputAssignmentPolicy.UnavailableInputReason(input) is { } unavailableReason)
        {
            ShowInlineNotice(unavailableReason);
            return;
        }
        if (IsProtectedNormalLeftClick(key))
        {
            ShowInlineError("通常レイヤーの左クリックは変更すると危険なため設定できません");
            return;
        }
        if (key == "Space" && currentLayer is "通常" or "Space")
        {
            ShowInlineNotice("SpaceキーはSpaceレイヤー専用のため、このレイヤーでは変更できません");
            return;
        }
        if (key == "CapsLock" && !editingSelectedInput && destinationInputTarget == null)
        {
            ShowInlineNotice("CapsLockは割り当て元にはできません。別のキーを選んだ後、割り当て先として使用できます");
            return;
        }
        if (MultiSelectToggle.IsChecked == true)
        {
            if (!multiSelectedInputs.Add(key))
                multiSelectedInputs.Remove(key);
            UpdateMultiSelectControls();
            ColorButtons();
            return;
        }
        ClearExecutionFocus();
        if (actionPaletteOpen)
            CloseActionPalette(animated: false);
        SelectInput(input, false);
        AnimateAssignmentEditorReveal();
    }

    void AnimateAssignmentEditorReveal()
        => UiMotionService.RunSafely("assignment-editor-reveal", AnimateAssignmentEditorRevealCore);

    void AnimateAssignmentEditorRevealCore()
    {
        if (!UiMotionService.Enabled)
        {
            SettleAssignmentEditorMotion();
            return;
        }
        var (scale, translate) = UiMotionService.MutableMotionTransform(AssignmentEditor);
        bool inFlight = AssignmentEditor.HasAnimatedProperties || scale.HasAnimatedProperties || translate.HasAnimatedProperties;
        if (!inFlight)
        {
            UiMotionService.StopAndSetDouble(AssignmentEditor, UIElement.OpacityProperty, .74);
            UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleXProperty, .992);
            UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleYProperty, .992);
            UiMotionService.StopAndSetDouble(translate, TranslateTransform.XProperty, 14);
        }
        UiMotionService.AnimateDouble("assignment-editor-opacity", AssignmentEditor, UIElement.OpacityProperty, 1, TimeSpan.FromMilliseconds(175));
        UiMotionService.AnimateDouble("assignment-editor-scale-x", scale, ScaleTransform.ScaleXProperty, 1, TimeSpan.FromMilliseconds(205));
        UiMotionService.AnimateDouble("assignment-editor-scale-y", scale, ScaleTransform.ScaleYProperty, 1, TimeSpan.FromMilliseconds(205));
        UiMotionService.AnimateDouble("assignment-editor-translate", translate, TranslateTransform.XProperty, 0, TimeSpan.FromMilliseconds(205));
    }

    void SettleAssignmentEditorMotion()
    {
        UiMotionService.StopAndSetDouble(AssignmentEditor, UIElement.OpacityProperty, 1);
        if (AssignmentEditor.RenderTransform is not TransformGroup group)
            return;
        foreach (var scale in group.Children.OfType<ScaleTransform>())
        {
            UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleXProperty, 1);
            UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleYProperty, 1);
        }
        foreach (var translate in group.Children.OfType<TranslateTransform>())
        {
            UiMotionService.StopAndSetDouble(translate, TranslateTransform.XProperty, 0);
            UiMotionService.StopAndSetDouble(translate, TranslateTransform.YProperty, 0);
        }
    }
    void OpenShortcutForVisualInput(string key)
    {
        if (MultiSelectToggle.IsChecked == true)
            return;
        if (InputAssignmentPolicy.UnavailableInputReason(InputForCurrentLayer(key)) is { } unavailableReason)
        {
            ShowInlineNotice(unavailableReason);
            return;
        }
        if (IsProtectedNormalLeftClick(key))
            return;
        if (key == "Space" && currentLayer is "通常" or "Space")
            return;
        if (key == "CapsLock" && !editingSelectedInput && destinationInputTarget == null)
            return;
        SelectInput(InputForCurrentLayer(key), false);
        OpenActionPicker(false);
    }
    void InputButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string key })
            SelectVisualInput(key);
    }
    void MouseCanvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsProtectedNormalLeftClick("MouseLeft"))
            return;
        var hit = MouseCanvas.InputHitTest(e.GetPosition(MouseCanvas)) as DependencyObject;
        if (hit == null || !IsDescendantOf(hit, MouseLeftVisual))
            return;
        e.Handled = true;
        ShowInlineError("通常レイヤーの左クリックは変更すると危険なため設定できません");
    }
    void InputButton_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string key })
        {
            e.Handled = true;
            OpenShortcutForVisualInput(key);
        }
    }
    void ExecutionValue_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox target || selected == null)
            return;
        destinationInputTarget = target;
        editingSelectedInput = true;
        UpdateExecutionEditButtons(target);
    }
    internal static string ShortcutTextForKey(Key key, ModifierKeys modifiers)
    {
        bool modifierKey = key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
        var parts = new List<string>();
        if ((modifiers & ModifierKeys.Control) != 0 || key is Key.LeftCtrl or Key.RightCtrl) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Shift) != 0 || key is Key.LeftShift or Key.RightShift) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Alt) != 0 || key is Key.LeftAlt or Key.RightAlt) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Windows) != 0 || key is Key.LWin or Key.RWin) parts.Add("Win");
        if (!modifierKey)
        {
            string token = ShortcutTokenForKey(key);
            if (token.Length > 0 && !parts.Contains(token, StringComparer.OrdinalIgnoreCase))
                parts.Add(token);
        }
        return string.Join("+", parts);
    }

    internal static string ShortcutTokenForKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return key.ToString();
        if (key is >= Key.D0 and <= Key.D9) return ((int)key - (int)Key.D0).ToString();
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return "NumPad" + ((int)key - (int)Key.NumPad0);
        if (key is >= Key.F1 and <= Key.F24) return key.ToString();
        return key switch
        {
            Key.CapsLock => "CapsLock", Key.Return => "Enter", Key.Escape => "Esc", Key.Back => "Backspace",
            Key.Delete => "Delete", Key.Space => "Space", Key.Tab => "Tab", Key.Insert => "Insert", Key.Home => "Home",
            Key.End => "End", Key.PageUp => "PageUp", Key.PageDown => "PageDown", Key.Up => "Up", Key.Down => "Down",
            Key.Left => "Left", Key.Right => "Right", Key.OemPlus => "+", Key.OemMinus => "-", Key.OemComma => ",",
            Key.OemPeriod => ".", Key.OemQuestion => "/", Key.OemSemicolon => ";", Key.OemQuotes => "'",
            Key.OemOpenBrackets => "[", Key.OemCloseBrackets => "]", Key.OemPipe => "\\", Key.OemTilde => "^",
            Key.Multiply => "Multiply", Key.Divide => "Divide", Key.Add => "Add", Key.Subtract => "Subtract",
            Key.Decimal => "Decimal", _ => key.ToString()
        };
    }

    void InputButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string key })
            return;
        e.Handled = true;
        if (InputAssignmentPolicy.UnavailableInputReason(InputForCurrentLayer(key)) is { } unavailableReason)
        {
            ShowInlineNotice(unavailableReason);
            return;
        }
        if (IsProtectedNormalLeftClick(key))
        {
            ShowInlineError("通常レイヤーの左クリックは変更すると危険なため設定できません");
            return;
        }
        if (MultiSelectToggle.IsChecked == true && multiSelectedInputs.Count > 0)
        {
            var multiMenu = CreateMultiSelectionContextMenu();
            multiMenu.PlacementTarget = (System.Windows.Controls.Button)sender;
            multiMenu.IsOpen = true;
            return;
        }
        if (DeckPanelLayout.IsInputName(key))
        {
            var deckMenu = CreateDeckInputContextMenu(key);
            deckMenu.PlacementTarget = (System.Windows.Controls.Button)sender;
            deckMenu.IsOpen = true;
            return;
        }
        if (key == "Space" && currentLayer is "通常" or "Space")
        {
            ShowInlineNotice("Spaceキーはレイヤー専用のため変更できません");
            return;
        }
        if (key == "CapsLock" && !editingSelectedInput && destinationInputTarget == null)
        {
            ShowInlineNotice("CapsLockは割り当て元にはできません");
            return;
        }
        var button = (System.Windows.Controls.Button)sender;
        var menu = CreateInputContextMenu(key, KeyboardPanel.Children.Contains(button) || InputButtons(MousePanel).Contains(button));
        menu.PlacementTarget = (System.Windows.Controls.Button)sender;
        menu.IsOpen = true;
    }
    internal ContextMenu CreateInputContextMenu(string key, bool includeAllLayers = true)
    {
        string input = currentLayer == "通常" ? key : currentLayer + "+" + key;
        string? unavailableReason = InputAssignmentPolicy.UnavailableInputReason(input);
        unavailableReason ??= IsProtectedNormalLeftClick(key) ? "通常レイヤーでは変更できません" : null;
        unavailableReason ??= key == "Space" && currentLayer is "通常" or "Space" ? "Spaceキーはレイヤー専用です" : null;
        unavailableReason ??= key == "CapsLock" && !editingSelectedInput && destinationInputTarget == null ? "CapsLockは割り当て元にはできません" : null;
        var existing = CurrentProfile.Mappings.LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "この割り当てをコピー", IsEnabled = existing != null };
        copy.Click += (_, _) => { copiedMapping = existing == null ? null : CloneMapping(existing); ShowInlineNotice(input + " の割り当てをコピーしました"); };
        var paste = new MenuItem { Header = "コピーした割り当てを貼り付け", IsEnabled = copiedMapping != null && unavailableReason == null, ToolTip = unavailableReason };
        paste.Click += (_, _) =>
        {
            if (copiedMapping == null || unavailableReason != null) return;
            var map = CloneMapping(copiedMapping);
            map.Input = input;
            map.Layer = currentLayer;
            ClearUnsupportedLongPress(map, CurrentProfile.Mappings);
            if (map.Kind == ActionKind.Gesture && !InputAssignmentPolicy.SupportsGesture(input))
            {
                ShowInlineNotice("ホイール／チルトではジェスチャーを設定できません");
                return;
            }
            if (map.Kind == ActionKind.Gesture && !ConfirmDirectMouseGestureConflict(input)) return;
            CurrentProfile.Mappings.RemoveAll(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
            CurrentProfile.Mappings.Add(map);
            InputAssignmentPolicy.SanitizeMappings(CurrentProfile.Mappings);
            UpdateLayerButtons();
            MarkDirty();
            ClearSelectedInput();
            ShowInlineNotice(DisplayInputName(input) + " の割り当てを貼り付けました");
        };
        var assignAllLayers = new MenuItem { Header = "全レイヤーに割り当てる", IsEnabled = existing != null && unavailableReason == null, ToolTip = unavailableReason };
        assignAllLayers.Click += (_, _) =>
        {
            if (existing == null)
                return;
            int applied = AssignMappingToAllLayers(CurrentProfile.Mappings, key, existing);
            if (applied == 0)
                return;
            ClearSelectedInput();
            MarkDirty();
            UpdateLayerButtons();
            ColorButtons();
            ShowInlineNotice($"{DisplayInputName(input)} の割り当てをデフォルト以外の{applied}レイヤーへ適用しました");
        };
        var delete = new MenuItem { Header = "この割り当てを削除", IsEnabled = existing != null, Foreground = ThemeService.Brush("DangerBrush") };
        delete.Click += (_, _) => { if (existing == null) return; CurrentProfile.Mappings.Remove(existing); MarkDirty(); UpdateLayerButtons(); ClearSelectedInput(); ShowInlineNotice(DisplayInputName(input) + " の割り当てを削除しました"); };
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        if (includeAllLayers)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(assignAllLayers);
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(delete);
        return menu;
    }
    internal static int AssignMappingToAllLayers(List<Mapping> mappings, string key, Mapping source)
    {
        if (string.IsNullOrWhiteSpace(key) || DeckPanelLayout.IsInputName(key)
            || key.Equals("MouseX", StringComparison.OrdinalIgnoreCase))
            return 0;
        var template = CloneMapping(source);
        int applied = 0;
        foreach (string layer in AllAssignmentLayerNames)
        {
            // A layer activation key cannot trigger itself while it is being held.
            if (layer.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;
            string targetInput = layer + "+" + key;
            if (InputAssignmentPolicy.IsUnreachableInput(targetInput))
                continue;
            mappings.RemoveAll(mapping => mapping.Input.Equals(targetInput, StringComparison.OrdinalIgnoreCase));
            var copy = CloneMapping(template);
            copy.Input = targetInput;
            copy.Layer = layer;
            ClearUnsupportedLongPress(copy);
            mappings.Add(copy);
            applied++;
        }
        InputAssignmentPolicy.SanitizeMappings(mappings);
        return applied;
    }
    internal ContextMenu CreateMultiSelectionContextMenu()
    {
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "選択した割り当てをコピー", IsEnabled = multiSelectedInputs.Count > 0 };
        copy.Click += (_, _) => CopyMultiSelection();
        var paste = new MenuItem { Header = "コピーした割り当てを貼り付け", IsEnabled = copiedMultiMappings is { Count: > 0 } && copiedMultiMappingsAreDeck == deckManagementMode };
        paste.Click += (_, _) => PasteMultiSelection();
        var delete = new MenuItem { Header = "選択した割り当てを削除", IsEnabled = multiSelectedInputs.Count > 0, Foreground = ThemeService.Brush("DangerBrush") };
        delete.Click += (_, _) => DeleteMultiSelection();
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(delete);
        return menu;
    }
    void MultiCopy_Click(object sender, RoutedEventArgs e) => CopyMultiSelection();
    void MultiPaste_Click(object sender, RoutedEventArgs e) => PasteMultiSelection();
    void MultiDelete_Click(object sender, RoutedEventArgs e)
    {
        if (MultiSelectToggle.IsChecked == true)
            DeleteMultiSelection();
        else
            Delete_Click(sender, e);
    }
    void CopyMultiSelection()
    {
        if (multiSelectedInputs.Count == 0)
            return;
        var mappings = deckManagementMode && selectedDeckLayout != null ? selectedDeckLayout.Mappings : CurrentProfile.Mappings;
        copiedMultiMappings = multiSelectedInputs.ToDictionary(key => key, key =>
        {
            string input = MultiSelectionInput(key);
            var mapping = mappings.LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
            return mapping == null ? null : CloneMapping(mapping);
        }, StringComparer.OrdinalIgnoreCase);
        copiedMultiMappingsAreDeck = deckManagementMode;
        UpdateMultiSelectControls();
        ShowInlineNotice($"{copiedMultiMappings.Count}入力の割り当てをコピーしました");
    }
    void PasteMultiSelection()
    {
        if (copiedMultiMappings is not { Count: > 0 } || copiedMultiMappingsAreDeck != deckManagementMode || multiSelectedInputs.Count == 0)
            return;
        var selectedKeys = multiSelectedInputs.OrderBy(MultiSelectionOrder).ThenBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();
        var sources = copiedMultiMappings.OrderBy(pair => MultiSelectionOrder(pair.Key)).ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => pair.Value).ToArray();
        var targets = selectedKeys.Select((key, index) => (Input: MultiSelectionInput(key), Source: sources.Length == 1 ? sources[0] : index < sources.Length ? sources[index] : null))
            .Where(target => !InputAssignmentPolicy.IsUnreachableInput(target.Input))
            .ToList();
        if (targets.Count == 0)
            return;
        foreach (var (Input, Source) in targets)
            if (Source?.Kind == ActionKind.Gesture && !ConfirmDirectMouseGestureConflict(Input))
                return;
        foreach (var (Input, Source) in targets)
            if (Source?.Kind == ActionKind.Gesture && !InputAssignmentPolicy.SupportsGesture(Input))
            {
                ShowInlineNotice("ホイール／チルトではジェスチャーを設定できません");
                return;
            }
        foreach (var (Input, Source) in targets)
        {
            var mappings = deckManagementMode && selectedDeckLayout != null ? selectedDeckLayout.Mappings : CurrentProfile.Mappings;
            mappings.RemoveAll(x => x.Input.Equals(Input, StringComparison.OrdinalIgnoreCase));
            if (Source == null)
                continue;
            var mapping = CloneMapping(Source);
            mapping.Input = Input;
            mapping.Layer = deckManagementMode ? DeckPanelLayout.Layer : currentLayer;
            ClearUnsupportedLongPress(mapping, mappings);
            mappings.Add(mapping);
        }
        if (!deckManagementMode)
            InputAssignmentPolicy.SanitizeMappings(CurrentProfile.Mappings);
        MarkDirty();
        UpdateLayerButtons();
        ColorButtons();
        MultiSelectToggle.IsChecked = false;
        ClearSelectedInput();
        ShowInlineNotice($"{targets.Count}入力へ割り当てを貼り付けました");
    }
    void DeleteMultiSelection()
    {
        int removed = 0;
        var mappings = deckManagementMode && selectedDeckLayout != null ? selectedDeckLayout.Mappings : CurrentProfile.Mappings;
        foreach (var key in multiSelectedInputs)
            removed += mappings.RemoveAll(x => x.Input.Equals(MultiSelectionInput(key), StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            ShowInlineNotice("削除する割り当てはありません");
            return;
        }
        MarkDirty();
        UpdateLayerButtons();
        MultiSelectToggle.IsChecked = false;
        ShowInlineNotice($"{removed}件の割り当てを削除しました");
    }
    string InputForCurrentLayer(string key) => currentLayer == "通常" ? key : currentLayer + "+" + key;
    string MultiSelectionInput(string key) => deckManagementMode ? key : InputForCurrentLayer(key);
    static int MultiSelectionOrder(string key) => DeckPanelLayout.IsInputName(key) ? DeckPanelLayout.SlotNumber(key) : int.MaxValue;
    void MultiSelectChanged(object sender, RoutedEventArgs e)
    {
        if (MultiSelectToggle.IsChecked == true)
        {
            if (destinationInputTarget != null || editingSelectedInput)
                CompleteDestinationInput(MultiSelectToggle);
            // Multi-select has one source of truth: explicitly selected keys
            // stay at full brightness while every other choice is dimmed.
            // Clear a stale single-key selection before applying that rule.
            if (selected != null)
                ClearSelectedInput();
            multiSelectedInputs.Clear();
            deckMultiSelectionAnchor = 0;
            ShowInlineNotice(deckManagementMode ? "複数選択: Deckボタンをクリックして選択します" : "複数選択: キーやマウスボタンをクリックして選択します");
        }
        else
        {
            multiSelectedInputs.Clear();
            deckMultiSelectionAnchor = 0;
            ShowInlineNotice("複数選択を終了しました");
        }
        UpdateMultiSelectControls();
        ColorButtons();
    }
    void UpdateMultiSelectControls()
    {
        if (MultiCopyButton == null || MultiPasteButton == null || MultiDeleteButton == null)
            return;
        bool active = MultiSelectToggle.IsChecked == true;
        MultiCopyButton.IsEnabled = active && multiSelectedInputs.Count > 0;
        MultiPasteButton.IsEnabled = active && multiSelectedInputs.Count > 0 && copiedMultiMappings is { Count: > 0 } && copiedMultiMappingsAreDeck == deckManagementMode;
        MultiDeleteButton.IsEnabled = active ? multiSelectedInputs.Count > 0 : HasDeletableSingleSelection();
        if (!active)
            return;
        LastInput.Text = multiSelectedInputs.Count == 0
            ? deckManagementMode ? "複数選択: Deckボタンをクリックして選択します" : "複数選択: キーやマウスボタンをクリックして選択します"
            : $"複数選択: {multiSelectedInputs.Count}入力を選択中";
        LastInput.Foreground = ThemeService.Brush("AccentTextBrush");
    }
    bool HasDeletableSingleSelection()
    {
        if (selected == null)
            return false;
        return MappingCollectionForInput(selected.Input).Any(mapping =>
            mapping.Input.Equals(selected.Input, StringComparison.OrdinalIgnoreCase) &&
            (MappingHasConfiguredAction(mapping) || HasDeckButtonContent(mapping)));
    }
    void LongPressOnly_Click(object sender, RoutedEventArgs e)
    {
        if (!IsLongPressSupportedFor(selected, CurrentProfile.Mappings))
            return;
        ValueBox.Clear();
        selected?.Kind = ActionKind.None;
        loading = true;
        KindBox.SelectedValue = ActionKind.None;
        loading = false;
        LongPressExpander.IsExpanded = true;
        FocusExecutionValue(LongValueBox, true);
        MarkDirty();
        LastInput.Text = "長押しのみ：短押しは元のキーを入力します";
    }
    void DestinationInputDone_Click(object sender, RoutedEventArgs e) => CompleteDestinationInput(sender as FrameworkElement);
    void ExecutionValueClear_Click(object sender, RoutedEventArgs e)
    {
        bool longPress = ReferenceEquals(sender, LongDestinationClearButton);
        var target = longPress ? LongValueBox : ValueBox;
        target.Clear();
        FocusExecutionValue(target, longPress);
    }
    void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source)
            return;
        bool deckCustomizeBlankClick = deckManagementMode
            && deckCustomizeOpen
            && DeckEditorWorkspace.Visibility == Visibility.Visible
            && !IsDescendantOf(source, DeckSettingsPanel)
            && !IsDescendantOf(source, DeckCustomizeToggleButton)
            && !IsInteractiveClick(source);
        if (deckCustomizeBlankClick)
            CloseDeckCustomization();
        bool actionPaletteBlankClick = actionPaletteOpen
            && !IsDescendantOf(source, ActionPalettePane)
            && !IsDescendantOf(source, KeyboardPanel)
            && !IsDescendantOf(source, SecondaryKeyboardPanel)
            && !IsDescendantOf(source, MousePanel)
            && !IsInteractiveClick(source);
        if (actionPaletteBlankClick)
        {
            CloseActionPalette(animated: true);
            e.Handled = true;
            return;
        }
        if (IsDescendantOf(source, KeyboardPanel) || IsDescendantOf(source, SecondaryKeyboardPanel) || IsDescendantOf(source, MousePanel) || IsInteractiveClick(source))
            return;
        if (MultiSelectToggle.IsChecked == true && multiSelectedInputs.Count > 0)
        {
            MultiSelectToggle.IsChecked = false;
            return;
        }
        if (destinationInputTarget != null || editingSelectedInput)
            CompleteDestinationInput();
        else if (selected != null)
            ClearSelectedInput();
    }
    void CompleteDestinationInput(FrameworkElement? fallback = null)
    {
        autoSaveTimer.Stop();
        if (selected != null)
        {
            NormalizeLongOnlyMapping(selected);
            if (config.AutoSave)
                SaveAndApply("確定 — 設定を保存して反映しました");
            else
            {
                LastInput.Text = "確定 — 未保存の変更があります。［保存して反映］で有効になります";
                LastInput.Foreground = ThemeService.Brush("WarningBrush");
            }
        }
        editingSelectedInput = false;
        selectionPulseSuppressed = true;
        ClearExecutionFocus(fallback);
        ColorButtons();
        CloseAssignmentPane();
    }
    void ClearSelectedInput(FrameworkElement? fallback = null)
    {
        selected = null;
        selectedBaseInput = "";
        editingSelectedInput = false;
        selectionPulseSuppressed = true;
        InputName.Clear();
        InputDisplayText.Text = "キーを選択してください";
        InspectorEmptyState.Visibility = Visibility.Visible;
        InspectorHintsPanel.Visibility = Visibility.Visible;
        InspectorEmptyTitleText.Text = "Actionを選択";
        InspectorEmptyDescriptionText.Text = deckManagementMode ? "一覧からDeckへドラッグ" : "一覧からキーへドラッグ";
        UpdateInspectorHintsForContext();
        SelectionHeader.Visibility = Visibility.Collapsed;
        AssignmentEditor.Visibility = Visibility.Collapsed;
        AssignmentEditor.IsEnabled = false;
        loading = true;
        KindBox.SelectedIndex = -1;
        ValueBox.Clear();
        LongKindBox.SelectedIndex = -1;
        LongValueBox.Clear();
        LongPressBox.Clear();
        LongPressExpander.IsExpanded = false;
        DeckNameBox.Clear();
        loading = false;
        UpdateDeckFileDropTarget();
        ClearExecutionFocus(fallback);
        UpdateMultiSelectControls();
        ColorButtons();
        CloseAssignmentPane(false);
        UpdateAssignmentPaneContentView();
    }
    static bool IsDescendantOf(DependencyObject source, DependencyObject ancestor)
    {
        for (DependencyObject? current = source; current != null; current = GetParent(current))
        if (ReferenceEquals(current, ancestor))
            return true;
        return false;
    }
    static bool IsInteractiveClick(DependencyObject source)
    {
        for (DependencyObject? current = source; current != null; current = GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.Primitives.Selector
                or System.Windows.Controls.ComboBoxItem
                or System.Windows.Controls.ListBoxItem
                or System.Windows.Controls.Primitives.RangeBase
                or System.Windows.Controls.Primitives.Thumb
                or MenuItem
                or PasswordBox)
                return true;
            if (current is Window)
                return false;
        }
        return false;
    }
    static DependencyObject? GetParent(DependencyObject current) => current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
    void FocusExecutionValue(TextBox target, bool bringIntoView = false)
    {
        int request = ++destinationFocusRequest;
        destinationInputTarget = target;
        editingSelectedInput = true;
        UpdateExecutionEditButtons(target);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            if (request != destinationFocusRequest || !ReferenceEquals(destinationInputTarget, target) || !target.IsVisible || !target.IsEnabled)
                return;
            if (bringIntoView)
                target.BringIntoView();
            target.Focus();
            Keyboard.Focus(target);
            target.CaretIndex = target.Text.Length;
            target.SelectionLength = 0;
        }));
    }
    void ClearExecutionFocus(FrameworkElement? fallback = null)
    {
        destinationFocusRequest++;
        destinationInputTarget = null;
        UpdateExecutionEditButtons(null);
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), null);
        if (fallback is { IsVisible: true, IsEnabled: true })
        {
            fallback.Focus();
            Keyboard.Focus(fallback);
        }
    }
    void UpdateExecutionEditButtons(TextBox? target)
    {
        var shortVisibility = ReferenceEquals(target, ValueBox) ? Visibility.Visible : Visibility.Collapsed;
        var longVisibility = ReferenceEquals(target, LongValueBox) ? Visibility.Visible : Visibility.Collapsed;
        DestinationClearButton.Visibility = shortVisibility;
        DestinationConfirmButton.Visibility = shortVisibility;
        LongDestinationClearButton.Visibility = longVisibility;
        LongDestinationConfirmButton.Visibility = longVisibility;
    }
    void ActionKind_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list || ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is not ListBoxItem item || item.DataContext is not ActionOption option || !option.IsEnabled)
            return;
        bool longPress = ReferenceEquals(list, LongKindBox);
        if (longPress && (selected == null || !IsLongPressSupportedFor(selected, MappingCollectionForInput(selected.Input))))
        {
            e.Handled = true;
            return;
        }
        if (!longPress && option.Kind == ActionKind.Gesture && selected != null && !InputAssignmentPolicy.SupportsGesture(selected.Input))
        {
            e.Handled = true;
            ShowInlineNotice("ホイール／チルトではジェスチャーを設定できません");
            return;
        }
        if (option.IsKeypad)
        {
            e.Handled = true;
            KeypadInput_Click(list, e);
            return;
        }
        if (option.IsDeckPanel)
        {
            e.Handled = true;
            OpenDeckPanelPicker(list, longPress);
            return;
        }
        if (option.IsDeckMonitor)
        {
            e.Handled = true;
            if (!longPress)
                OpenDeckMonitorPicker(list);
            return;
        }
        switch (option.Kind)
        {
            case ActionKind.Profile:
                e.Handled = true;
                OpenProfilePicker(longPress, list);
                break;
            case ActionKind.Shortcut:
                e.Handled = true;
                OpenActionPicker(longPress);
                break;
            case ActionKind.Launch:
                e.Handled = true;
                OpenApplicationPicker(longPress);
                break;
            case ActionKind.Macro:
                e.Handled = true;
                ShowMacroWindow(true, longPress);
                break;
            case ActionKind.Gesture:
                e.Handled = true;
                if (!longPress)
                    OpenGesturePicker(list);
                break;
            case ActionKind.Disabled:
                e.Handled = true;
                ApplyDisabledAction(longPress);
                break;
            default:
                Dispatcher.BeginInvoke(() => FocusExecutionValue(longPress ? LongValueBox : ValueBox, longPress));
                break;
        }
    }
    void LongPressExpander_Expanded(object sender, RoutedEventArgs e)
    {
        if (!loading && selected != null)
            FocusExecutionValue(LongValueBox, true);
    }
    void AssignmentPane_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer || e.Delta == 0 || viewer.ScrollableHeight <= 0)
            return;
        double distance = Math.Clamp(Math.Abs(e.Delta), 48d, 160d);
        viewer.ScrollToVerticalOffset(Math.Clamp(viewer.VerticalOffset - Math.Sign(e.Delta) * distance, 0, viewer.ScrollableHeight));
        e.Handled = true;
    }
    void ShowAssignmentPane()
    {
        AssignmentPane.Visibility = Visibility.Visible;
        AssignmentPaneTransform.BeginAnimation(TranslateTransform.XProperty, null);
        AssignmentPaneTransform.X = 0;
    }
    internal void DismissAssignmentPaneIfOutside(DependencyObject source)
    {
        ShowAssignmentPane();
    }
    void CloseAssignmentPane(bool animate = true)
    {
        ShowAssignmentPane();
    }
    void OpenActionPicker(bool longPress, string? initialMajorCategory = null)
    {
        if (longPress && (selected == null || !IsLongPressSupportedFor(selected, MappingCollectionForInput(selected.Input))))
            return;
#if !PRODUCTION_PUBLISH
        if (ActionPickerRequestedForTest != null)
        {
            if (ActionPickerRequestedForTest(longPress, initialMajorCategory) is { } testAction)
                ApplyCatalogAction(testAction, longPress);
            return;
        }
#endif
        var picker = new ActionPickerWindow(config.Profiles, config.KeyboardLayout, null, false, initialMajorCategory, config.DeckLayouts) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedAction is not { } action)
            return;
        ApplyCatalogAction(action, longPress);
    }
    void OpenGesturePicker(ListBox placementTarget)
    {
        var menu = CreateGesturePickerMenu(placementTarget);
        if (menu.Items.Count == 0)
        {
            ShowInlineNotice("先に左下の「ジェスチャー管理」でジェスチャーを作成してください");
            return;
        }
        menu.IsOpen = true;
    }
    internal ContextMenu CreateGesturePickerMenu(ListBox placementTarget)
    {
        var menu = new ContextMenu { PlacementTarget = placementTarget, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        foreach (var gesture in config.Gestures.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
        {
            string name = gesture.Name;
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => ApplyCatalogAction(new CatalogAction("ジェスチャー", name, "登録済みのジェスチャーを実行します", ActionKind.Gesture, name), false);
            menu.Items.Add(item);
        }
        return menu;
    }
    void OpenDeckPanelPicker(ListBox placementTarget, bool longPress)
    {
        var menu = CreateDeckPanelPickerMenu(placementTarget, longPress);
        if (menu.Items.Count == 0)
        {
            ShowInlineNotice("先に左下の「Deckパネル」でDeckを作成してください");
            return;
        }
        menu.IsOpen = true;
    }
    internal ContextMenu CreateDeckPanelPickerMenu(ListBox placementTarget, bool longPress)
    {
        var menu = new ContextMenu { PlacementTarget = placementTarget, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        foreach (var layout in config.DeckLayouts.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
        {
            string name = layout.Name;
            string actionValue = DeckPanelLayout.ActionValue(layout.Id);
            string description = $"{layout.Columns}×{layout.Rows}のDeckレイアウトを表示します";
            var item = new MenuItem { Header = name, ToolTip = $"{layout.Columns}×{layout.Rows}" };
            item.Click += (_, _) => ApplyCatalogAction(new CatalogAction("Deckパネル", name, description, ActionKind.Shortcut, actionValue), longPress);
            menu.Items.Add(item);
        }
        return menu;
    }
    void OpenDeckMonitorPicker(ListBox placementTarget)
    {
        var menu = CreateDeckMonitorPickerMenu(placementTarget);
        if (menu.Items.Count == 0)
            return;
        menu.IsOpen = true;
    }
    internal ContextMenu CreateDeckMonitorPickerMenu(ListBox placementTarget)
    {
        var menu = new ContextMenu { PlacementTarget = placementTarget, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        foreach (var monitor in DeckMonitorCatalog.Items)
        {
            var definition = monitor;
            var header = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            header.Children.Add(new TextBlock
            {
                Text = definition.Glyph,
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 15,
                Width = 24,
                Foreground = ThemeService.Brush("AccentTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });
            header.Children.Add(new TextBlock
            {
                Text = definition.Name,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            var item = new MenuItem { Header = header, ToolTip = definition.Description };
            item.Click += (_, _) =>
            {
                if (selected == null || !DeckPanelLayout.IsInputName(selected.Input) || !ApplyPaletteMonitorDrop(definition, selected.Input))
                    ShowInlineNotice("Deckボタンを選択してからモニターを配置してください");
            };
            menu.Items.Add(item);
        }
        return menu;
    }
    void ApplyCatalogAction(CatalogAction action, bool longPress)
    {
        var target = selected;
        if (target == null)
            return;
        if (!longPress && action.Kind == ActionKind.Gesture && !InputAssignmentPolicy.SupportsGesture(target.Input))
        {
            ShowInlineNotice("ホイール／チルトではジェスチャーを設定できません");
            return;
        }
        if (longPress && !IsLongPressSupportedFor(target, MappingCollectionForInput(target.Input)))
            return;
        if (!longPress && DeckPanelLayout.IsInputName(target.Input))
            target.DeckMonitor = string.Empty;
        if (!longPress
            && DeckPanelLayout.IsInputName(target.Input)
            && string.IsNullOrWhiteSpace(target.DeckIconPath)
            && (string.IsNullOrWhiteSpace(target.DeckIcon) || target.DeckIconAutoAssigned))
        {
            target.DeckIcon = DeckIconCatalog.SuggestedPresetId(action);
            target.DeckIconAutoAssigned = true;
        }
        if (action.Kind == ActionKind.Profile)
        {
            ApplyProfileAction(action.Value, longPress);
            return;
        }
        if (action.Kind == ActionKind.Disabled)
        {
            ApplyDisabledAction(longPress);
            return;
        }
        if (action.Kind == ActionKind.Gesture && longPress)
            return;
        if (action.Kind == ActionKind.Gesture && !ConfirmDirectMouseGestureConflict(target.Input))
            return;
        loading = true;
        if (longPress)
        {
            target.LongPressKind = action.Kind;
            target.LongPressValue = action.Value;
            SelectActionOptionForMapping(LongKindBox, action.Kind, action.Value);
            LongValueBox.Text = DisplayConfiguredActionValue(action.Kind, action.Value);
        }
        else
        {
            target.Kind = action.Kind;
            target.Value = action.Value;
            SelectActionOptionForMapping(KindBox, action.Kind, action.Value);
            ValueBox.Text = DisplayConfiguredActionValue(action.Kind, action.Value);
            if (!InputAssignmentPolicy.CanExecuteLongPress(target, MappingCollectionForInput(target.Input)))
            {
                ClearUnsupportedLongPress(target, MappingCollectionForInput(target.Input));
                ClearLongPressEditor();
            }
        }
        loading = false;
        var mappings = MappingCollectionForInput(target.Input);
        if (MappingHasConfiguredAction(target) && !mappings.Contains(target))
            mappings.Add(target);
        if (!DeckPanelLayout.IsInputName(target.Input))
            InputAssignmentPolicy.SanitizeMappings(CurrentProfile.Mappings);
        UpdateBrowseButtons();
        UpdateLayerButtons();
        MarkDirty();
        ColorButtons();
        CompleteDestinationInput();
    }
    void SelectActionOptionForMapping(ListBox list, ActionKind kind, string value)
    {
        if (kind == ActionKind.Shortcut && DeckPanelLayout.IsDeckAction(value))
        {
            list.SelectedItem = list.Items.Cast<ActionOption>().FirstOrDefault(option => option.IsDeckPanel);
            return;
        }
        list.SelectedValue = kind == ActionKind.Gesture ? ActionKind.Gesture : EditorActionKind(kind);
    }
    void UpdateInspectorHintsForContext()
    {
        if (InspectorHintOneTitle == null)
            return;
        InspectorHintOneIcon.Data = Geometry.Parse("M4,5 H20 M4,12 H20 M4,19 H20 M2,5 H2.1 M2,12 H2.1 M2,19 H2.1");
        InspectorHintOneTitle.Text = "Actionを開く";
        InspectorHintOneDescription.Text = "検索して選択";
        InspectorHintTwoIcon.Data = Geometry.Parse("M5,5 L19,19 M19,19 L14,19 M19,19 L19,14 M19,5 L5,19 M5,19 L10,19 M5,19 L5,14");
        InspectorHintTwoTitle.Text = "ドラッグ";
        InspectorHintTwoDescription.Text = deckManagementMode ? "Deckへ割り当て" : "キーへ割り当て";
        InspectorHintThreeIcon.Data = Geometry.Parse("M5,3 L5,20 L9.5,15.5 L13,22 L16,20.5 L12.5,14 L19,14 Z");
        InspectorHintThreeTitle.Text = deckManagementMode ? "Deckボタンをクリック" : "キーをクリック";
        InspectorHintThreeDescription.Text = "詳細を編集";
    }
    void OpenProfilePicker(bool longPress, ListBox placementTarget)
    {
        var menu = new ContextMenu { PlacementTarget = placementTarget, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        foreach (var profile in config.Profiles)
        {
            var item = new MenuItem { Header = profile.Name };
            string name = profile.Name;
            item.Click += (_, _) => ApplyProfileAction(name, longPress);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    void ApplyDisabledAction(bool longPress)
    {
        if (selected == null || longPress && !IsLongPressSupportedFor(selected, MappingCollectionForInput(selected.Input)))
            return;
        loading = true;
        try
        {
            if (longPress)
            {
                selected.LongPressKind = ActionKind.Disabled;
                selected.LongPressValue = "";
                LongKindBox.SelectedValue = ActionKind.Disabled;
                LongValueBox.Clear();
                LongPressExpander.IsExpanded = true;
            }
            else
            {
                selected.Kind = ActionKind.Disabled;
                selected.Value = "";
                KindBox.SelectedValue = ActionKind.Disabled;
                ValueBox.Clear();
            }
        }
        finally { loading = false; }
        var mappings = MappingCollectionForInput(selected.Input);
        if (!mappings.Contains(selected))
            mappings.Add(selected);
        if (!DeckPanelLayout.IsInputName(selected.Input))
            InputAssignmentPolicy.SanitizeMappings(CurrentProfile.Mappings);
        MarkDirty();
        ColorButtons();
        CompleteDestinationInput();
    }
    void OpenMacros_Click(object sender, RoutedEventArgs e) => ShowMacroWindow(false, false);
    void OpenProfileManager_Click(object sender, RoutedEventArgs e)
    {
        var window = new ProfileManagerWindow(config.Profiles, config.ActiveProfile) { Owner = this };
        if (window.ShowDialog() != true)
            return;
        ApplyProfileManagerResult(window.ResultProfiles, window.ResultActiveProfile);
    }
    void ApplyProfileManagerResult(IReadOnlyList<Profile> profiles, string activeProfile)
    {
        string editingProfile = config.ActiveProfile;
        config.Profiles = [.. profiles];
        SyncDeckProfileVariants();
        EnsureProfileDeckDefaults();
        config.ActiveProfile = config.Profiles.Any(profile => profile.Name.Equals(editingProfile, StringComparison.OrdinalIgnoreCase)) ? editingProfile : config.Profiles[0].Name;
        config.AutoSwitchProfilesByCursor = false;
        automaticProfileReturnName = "";
        explicitProfileSwitchProcess = "";
        ResetAutomaticProfileCandidate();
        ClearSelectedInput();
        RefreshProfiles();
        UpdateLayerButtons();
        // The dialog's primary action is Apply. Profile routing must become live
        // immediately even when assignment auto-save is disabled.
        SaveAndApply("プロファイル設定を保存し、反映しました");
    }
    void OpenGestureManager_Click(object sender, RoutedEventArgs e)
    {
        var window = new GestureManagerWindow(config.Gestures, config.Profiles, config.Macros, config.KeyboardLayout, config.DeckLayouts) { Owner = this };
        if (window.ShowDialog() != true)
            return;
        config.Gestures = [.. window.ResultGestures];
        config.Profiles = [.. window.ResultProfiles];
        ClearSelectedInput();
        RefreshProfiles();
        UpdateLayerButtons();
        MarkDirty();
    }
    void ApplyProfileAction(string profileName, bool longPress)
    {
        if (selected == null || longPress && !IsLongPressSupportedFor(selected, MappingCollectionForInput(selected.Input)) || !config.Profiles.Any(x => x.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase)))
            return;
        loading = true;
        try
        {
            if (longPress)
            {
                selected.LongPressKind = ActionKind.Profile;
                selected.LongPressValue = profileName;
                LongKindBox.SelectedValue = ActionKind.Profile;
                LongValueBox.Text = ProfileDisplayValue(profileName);
                LongPressExpander.IsExpanded = true;
            }
            else
            {
                selected.Kind = ActionKind.Profile;
                selected.Value = profileName;
                KindBox.SelectedValue = ActionKind.Profile;
                ValueBox.Text = ProfileDisplayValue(profileName);
            }
        }
        finally { loading = false; }
        var mappings = MappingCollectionForInput(selected.Input);
        if (!mappings.Contains(selected))
            mappings.Add(selected);
        UpdateBrowseButtons();
        MarkDirty();
        ColorButtons();
        CompleteDestinationInput();
    }
    void ShowMacroWindow(bool assign, bool longPress)
    {
        if (assign && longPress && (selected == null || !IsLongPressSupportedFor(selected, MappingCollectionForInput(selected.Input))))
            return;
        string target = assign ? $"{InputName.Text}（{(longPress ? "長押し" : "短押し")}）" : "";
        var window = new MacroWindow(config, SetMacroRecording, assign, target) { Owner = this };
        window.Saved += () => SaveAndApply("マクロを保存して反映しました");
        macroWindow = window;
        bool? result = window.ShowDialog();
        macroWindow = null;
        SetMacroRecording(false, false, false);
        if (!window.SaveRequested && window.Changed)
            MarkDirty();
        if (!assign || result != true || string.IsNullOrWhiteSpace(window.SelectedMacroName))
            return;
        if (longPress)
        {
            LongKindBox.SelectedValue = ActionKind.Macro;
            LongValueBox.Text = window.SelectedMacroName;
            LongPressExpander.IsExpanded = true;
            FocusExecutionValue(LongValueBox, true);
        }
        else
        {
            KindBox.SelectedValue = ActionKind.Macro;
            ValueBox.Text = window.SelectedMacroName;
        }
        CompleteDestinationInput();
    }
    void SetMacroRecording(bool recording, bool captureMouseMoves, bool useMappedActions)
    {
        if (recording)
        {
            if (macroIsRecording)
                return;
            macroIsRecording = true;
            macroEmergencyStop = false;
            engineBeforeMacroRecording = engine.Enabled;
            MacroPlayer.StopAll();
            engine.Enabled = useMappedActions && engineStarted;
            engine.CaptureMouseMoves = captureMouseMoves;
            EngineStatus.Text = useMappedActions ? "● マクロ記録中（割り当て後のアクション）" : captureMouseMoves ? "● マクロ記録中（マウス軌跡あり）" : "● マクロ記録中（物理キー）";
            EngineStatus.Foreground = ThemeService.Brush("WarningBrush");
        }
        else
        {
            if (!macroIsRecording)
                return;
            macroIsRecording = false;
            engine.CaptureMouseMoves = false;
            engine.Enabled = engineBeforeMacroRecording && config.EngineEnabled && !macroEmergencyStop;
            UpdateStatus();
        }
    }
    void OpenApplicationPicker(bool longPress)
    {
        var dialog = new ApplicationPickerWindow { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            ApplyApplicationSelection(longPress, dialog.SelectedPath);
    }
    void ApplyApplicationSelection(bool longPress, string path)
    {
        if (longPress && (selected == null || !IsLongPressSupportedFor(selected, MappingCollectionForInput(selected.Input))))
            return;
        if (longPress)
        {
            LongKindBox.SelectedValue = ActionKind.Launch;
            LongValueBox.Text = path;
            FocusExecutionValue(LongValueBox, true);
        }
        else
        {
            KindBox.SelectedValue = ActionKind.Launch;
            ValueBox.Text = path;
        }
        AssignAutomaticDeckApplicationIcon(path);
        MarkDirty();
        CompleteDestinationInput();
    }

    void AssignAutomaticDeckApplicationIcon(string path)
    {
        if (selected == null
            || !DeckPanelLayout.IsInputName(selected.Input)
            || !string.IsNullOrWhiteSpace(selected.DeckIconPath)
            || (!string.IsNullOrWhiteSpace(selected.DeckIcon) && !selected.DeckIconAutoAssigned))
            return;

        selected.DeckIcon = DeckIconCatalog.SuggestedPresetId(new CatalogAction("", "", "", ActionKind.Launch, path));
        selected.DeckIconAutoAssigned = true;
        RefreshSelectedInputVisual(selected.Input);
    }

    void SelectInput(string input, bool focusExecution = true)
    {
        if (selected != null && (destinationInputTarget != null || editingSelectedInput))
            CompleteDestinationInput();
        string layer = "通常";
        selectedBaseInput = input;
        int plus = input.IndexOf('+');
        if (plus > 0)
        {
            layer = input[..plus];
            selectedBaseInput = input[(plus + 1)..];
        }
        var mappings = MappingCollectionForInput(input);
        var visibleAssignment = DeckPanelLayout.IsInputName(input) ? mappings.LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase) && MappingInterceptsInput(x)) : FindProfileMapping(config.Profiles, CurrentProfile.Name, input, MappingInterceptsInput);
        detectMode = false;
        editingSelectedInput = focusExecution;
        selectionPulseSuppressed = false;
        selected = SelectEditorMapping(mappings, visibleAssignment, input);
        currentLayer = layer;
        loading = true;
        if (!DeckPanelLayout.IsInputName(input))
        {
            KindBox.ItemsSource = ActionOptions(allowGesture: InputAssignmentPolicy.SupportsGesture(input));
            KindBox.SelectedValuePath = nameof(ActionOption.SelectionKind);
        }
        InputName.Text = selected.Input;
        InputDisplayText.Text = DisplayInputName(selected.Input);
        SelectActionOptionForMapping(KindBox, selected.Kind, selected.Value);
        if (DeckMonitorCatalog.IsMonitor(selected.DeckMonitor))
            KindBox.SelectedItem = KindBox.Items.Cast<ActionOption>().FirstOrDefault(option => option.IsDeckMonitor);
        ValueBox.Text = DisplayConfiguredActionValue(selected.Kind, selected.Value);
        SelectActionOptionForMapping(LongKindBox, selected.LongPressKind, selected.LongPressValue);
        LongValueBox.Text = DisplayConfiguredActionValue(selected.LongPressKind, selected.LongPressValue);
        LongPressBox.Text = selected.LongPressMs.ToString();
        LongPressExpander.IsExpanded = HasConfiguredLongPress(selected);
        DeckNameBox.Text = selected.Description ?? "";
        loading = false;
        UpdateDeckFileDropTarget();
        UpdateDeckColorPicker();
        InspectorEmptyState.Visibility = Visibility.Collapsed;
        InspectorHintsPanel.Visibility = Visibility.Collapsed;
        SelectionHeader.Visibility = Visibility.Visible;
        AssignmentEditor.Visibility = Visibility.Visible;
        AssignmentEditor.IsEnabled = true;
        DeckNameEditorPanel.Visibility = deckManagementMode ? Visibility.Visible : Visibility.Collapsed;
        UpdateBrowseButtons();
        UpdateLayerButtons();
        UpdateMultiSelectControls();
        ColorButtons();
        ShowAssignmentPane();
        UpdateAssignmentPaneContentView();
        if (focusExecution && ShouldFocusExecutionForSelectedInput(visibleAssignment))
            FocusExecutionValue(ValueBox);
    }
    internal static Mapping SelectEditorMapping(IReadOnlyList<Mapping> currentMappings, Mapping? visibleAssignment, string input)
    {
        var direct = currentMappings.LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
        if (direct != null)
            return direct;
        if (visibleAssignment != null)
        {
            var inherited = CloneMapping(visibleAssignment);
            inherited.Input = input;
            return inherited;
        }
        return new Mapping { Input = input, Kind = ActionKind.None };
    }
    internal static bool ShouldFocusExecutionForSelectedInput(Mapping? visibleAssignment) => visibleAssignment == null;
    internal static string DisplayInputName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "キーを選択してください";
        return string.Join(" + ", input.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(DisplayInputPart));
    }
    static string DisplayInputPart(string value) => value switch
    {
        "通常" => "デフォルト",
        "Space" => "Space",
        "CapsLock" => "CapsLock",
        "MouseRight" => "右クリック",
        "MouseBack" => "戻る",
        "MouseForward" => "進む",
        "Taskbar" => "タスクバー",
        DeckPanelLayout.Layer => "Deck",
        "MouseLeft" => "左クリック",
        "MouseMiddle" => "ホイールクリック",
        "MouseX" => "追加ボタン",
        "WheelUp" => "ホイール上",
        "WheelDown" => "ホイール下",
        "TiltLeft" => "チルト左",
        "TiltRight" => "チルト右",
        _ => value
    };
    void EditorChanged(object sender, EventArgs e)
    {
        if (loading || selected == null)
            return;
        if (ReferenceEquals(sender, ValueBox) && !string.IsNullOrWhiteSpace(ValueBox.Text))
        {
            loading = true;
            KindBox.SelectedValue = ContainsJapaneseText(ValueBox.Text) ? ActionKind.Text : KindBox.SelectedValue is ActionKind ? KindBox.SelectedValue : ActionKind.Key;
            loading = false;
        }
        var longEditorKind = LongKindBox.SelectedValue is ActionKind lk ? lk : EditorActionKind(selected.LongPressKind);
        if (ReferenceEquals(sender, LongKindBox) && longEditorKind == ActionKind.None && !string.IsNullOrEmpty(LongValueBox.Text))
        {
            loading = true;
            LongValueBox.Clear();
            loading = false;
        }
        var (Kind, Value) = NormalizeEditorAction(KindBox.SelectedValue is ActionKind k ? k : EditorActionKind(selected.Kind), ValueBox.Text, selected.Kind, selected.Value);
        var longAction = NormalizeEditorAction(longEditorKind, LongValueBox.Text, selected.LongPressKind, selected.LongPressValue);
        selected.Kind = Kind;
        selected.Value = Value;
        var selectedMappings = MappingCollectionForInput(selected.Input);
        if (selected.Kind == ActionKind.Gesture && !InputAssignmentPolicy.SupportsGesture(selected.Input))
        {
            selected.Kind = ActionKind.None;
            selected.Value = "";
        }
        if (DeckPanelLayout.IsInputName(selected.Input)
            && (Kind != ActionKind.None || longAction.Kind != ActionKind.None || !string.IsNullOrWhiteSpace(Value) || !string.IsNullOrWhiteSpace(longAction.Value)))
            selected.DeckMonitor = string.Empty;
        if (!InputAssignmentPolicy.CanExecuteLongPress(selected, selectedMappings))
        {
            ClearUnsupportedLongPress(selected, selectedMappings);
            ClearLongPressEditor();
        }
        else
        {
            selected.LongPressKind = longAction.Kind;
            selected.LongPressValue = longAction.Value;
        }
        selected.Layer = currentLayer;
        if (int.TryParse(LongPressBox.Text, out var ms))
            selected.LongPressMs = ms;
        if (MappingHasConfiguredAction(selected))
        {
            if (!selectedMappings.Contains(selected))
                selectedMappings.Add(selected);
        }
        else if (deckManagementMode && HasDeckButtonContent(selected))
        {
            var mappings = MappingCollectionForInput(selected.Input);
            if (!mappings.Contains(selected))
                mappings.Add(selected);
        }
        else
            selectedMappings.Remove(selected);
        if (!DeckPanelLayout.IsInputName(selected.Input))
            InputAssignmentPolicy.SanitizeMappings(CurrentProfile.Mappings);
        bool continuousTextEdit = ReferenceEquals(sender, ValueBox)
            || ReferenceEquals(sender, LongValueBox)
            || ReferenceEquals(sender, LongPressBox);
        UpdateBrowseButtons();
        if (continuousTextEdit)
        {
            // An 18x18 Deck contains 324 controls. Rebuilding the Deck overlay,
            // recoloring the complete keyboard, and restarting an animation for
            // every typed character makes the editor lag behind the caret. Keep
            // the draft only; CompleteDestinationInput/SaveAndApply performs one
            // complete visual and runtime refresh after confirmation.
            MarkDirty(refreshDeckPanel: false);
            RefreshSelectedInputVisual(selected.Input);
        }
        else
        {
            UpdateLayerButtons();
            UpdateMultiSelectControls();
            MarkDirty();
            RefreshSelectedInputVisual(selected.Input);
            AnimateAssignmentCommit(selected.Input);
        }
        if (ReferenceEquals(sender, KindBox))
            FocusExecutionValue(ValueBox);
        else if (ReferenceEquals(sender, LongKindBox))
            FocusExecutionValue(LongValueBox, true);
    }
    static string ProfileDisplayValue(string profileName) => "プロファイル：" + profileName;
    internal static bool ContainsJapaneseText(string? value) => !string.IsNullOrEmpty(value) && value.Any(character => character is >= '\u3040' and <= '\u30FF' or >= '\u3400' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF');
    static string GestureDisplayValue(string gestureName) => "ジェスチャー：" + gestureName;
    internal static string DisplayActionValue(ActionKind kind, string value) => kind switch { ActionKind.Profile => ProfileDisplayValue(value), ActionKind.Gesture => GestureDisplayValue(value), ActionKind.Mouse => ActionCatalog.DisplayMouseAction(value), _ => value };
    string DisplayConfiguredActionValue(ActionKind kind, string value)
    {
        if (kind == ActionKind.Shortcut && DeckPanelLayout.IsDeckAction(value))
        {
            var layout = DeckPanelLayout.ResolveActionLayout(config, value);
            return "Deckパネル：" + (layout?.Name ?? "既定");
        }
        if (kind == ActionKind.Shortcut && value.Equals(ActionCatalog.ShowRelyrMainWindowAction, StringComparison.OrdinalIgnoreCase))
            return "RELYRを表示";
        return DisplayActionValue(kind, value);
    }
    static ActionKind EditorActionKind(ActionKind kind) => kind switch { ActionKind.Mouse => ActionKind.Shortcut, _ => kind };
    internal static (ActionKind Kind, string Value) NormalizeEditorAction(ActionKind editorKind, string? value, ActionKind existingKind = ActionKind.None, string? existingValue = null)
    {
        string original = value ?? "";
        if (editorKind == ActionKind.Profile)
        {
            const string prefix = "プロファイル：";
            string profileName = original.StartsWith(prefix, StringComparison.Ordinal) ? original[prefix.Length..].Trim() : original.Trim();
            return (ActionKind.Profile, profileName);
        }
        if (editorKind == ActionKind.Gesture || (editorKind == ActionKind.Shortcut && existingKind == ActionKind.Gesture && original.StartsWith("ジェスチャー：", StringComparison.Ordinal)))
        {
            string gestureName = original.StartsWith("ジェスチャー：", StringComparison.Ordinal) ? original["ジェスチャー：".Length..] : original;
            return string.IsNullOrWhiteSpace(gestureName) ? (ActionKind.None, "") : (ActionKind.Gesture, gestureName);
        }
        if (editorKind == ActionKind.Disabled)
            return (ActionKind.Disabled, "");
        if (editorKind != ActionKind.Shortcut)
            return (editorKind, original);
        if (DeckPanelLayout.IsDeckAction(existingValue) && original.StartsWith("Deckパネル：", StringComparison.Ordinal))
            return (ActionKind.Shortcut, existingValue!);
        if (existingValue?.Equals(ActionCatalog.ShowRelyrMainWindowAction, StringComparison.OrdinalIgnoreCase) == true && original.Equals("RELYRを表示", StringComparison.Ordinal))
            return (ActionKind.Shortcut, existingValue);
        return ActionCatalog.TryNormalizeMouseAction(original, out string mouseAction) ? (ActionKind.Mouse, mouseAction) : (editorKind, original.Trim());
    }
    void UpdateBrowseButtons()
    {
        bool shortGestureSelected = selected?.Kind == ActionKind.Gesture;
        bool shortModifierClickSelected = ShortActionPreventsLongPress(selected);
        bool legacyLongGestureSelected = selected?.LongPressKind == ActionKind.Gesture;
        IReadOnlyList<Mapping>? mappings = selected == null ? null : MappingCollectionForInput(selected.Input);
        bool impulseInput = InputAssignmentPolicy.IsImpulseInput(selected?.Input);
        bool layerSourceInUse = selected != null && InputAssignmentPolicy.HasConfiguredLayerMappings(mappings, selected.Input);
        bool longPressSupported = IsLongPressSupportedFor(selected, mappings);
        ValueBox.IsReadOnly = shortGestureSelected;
        ValueBox.IsTabStop = !shortGestureSelected;
        ValueBox.Opacity = shortGestureSelected ? .72 : 1;
        ValueBox.ToolTip = shortGestureSelected
            ? "ジェスチャー名は直接編集できません。変更する場合は「ショートカット」から別のジェスチャーを選んでください。"
            : "Ctrlを押しながらCなど、実際のキーボードでもショートカットを入力できます";
        LongValueBox.IsReadOnly = legacyLongGestureSelected;
        LongValueBox.IsTabStop = !legacyLongGestureSelected;
        LongValueBox.Opacity = legacyLongGestureSelected ? .72 : 1;
        LongPressOnlyButton.IsEnabled = longPressSupported;
        LongPressExpander.IsEnabled = longPressSupported;
        LongPressExpander.Opacity = longPressSupported ? 1 : .58;
        LongPressExpander.Header = shortGestureSelected
            ? "＋ 長押し（ジェスチャーでは設定できません）"
            : shortModifierClickSelected
                ? "＋ 長押し（短押しの修飾クリックとは併用できません）"
                : impulseInput
                    ? "＋ 長押し（ホイール／チルトでは設定できません）"
                    : layerSourceInUse
                        ? "＋ 長押し（レイヤー使用中は設定できません）"
                : IsNormalLayerAlphabetKey(selected)
                ? "＋ 長押し（通常レイヤーの英字では設定できません）"
                : "＋ 長押しを追加（任意）";
        if (!longPressSupported)
            LongPressExpander.IsExpanded = false;
    }

    internal static bool IsLongPressSupportedFor(Mapping? mapping, IReadOnlyList<Mapping>? mappings = null)
        => InputAssignmentPolicy.CanExecuteLongPress(mapping, mappings);

    internal static bool ShortActionBlocksLongPress(Mapping? mapping)
        => mapping?.Kind == ActionKind.Gesture || ShortActionPreventsLongPress(mapping);

    internal static bool ShortActionPreventsLongPress(Mapping? mapping)
        => mapping is { Kind: ActionKind.Mouse } && MappingExecutor.IsModifierDrag(mapping.Value);

    internal static bool ClearUnsupportedLongPress(Mapping? mapping, IReadOnlyList<Mapping>? mappings = null)
        => InputAssignmentPolicy.ClearImpossibleLongPress(mapping, mappings);

    void ClearLongPressEditor()
    {
        bool wasLoading = loading;
        loading = true;
        LongKindBox.SelectedIndex = -1;
        LongValueBox.Clear();
        LongPressExpander.IsExpanded = false;
        loading = wasLoading;
    }

    static bool IsNormalLayerAlphabetKey(Mapping? mapping)
        => mapping?.Layer == "通常" && InputAssignmentPolicy.IsNormalAlphabetInput(mapping.Input);
    bool HandleInput(string input)
    {
        const string gestureMarker = ":Gesture:";
        int gestureIndex = input.LastIndexOf(gestureMarker, StringComparison.OrdinalIgnoreCase);
        if (gestureIndex >= 0)
        {
            string baseGestureInput = input[..gestureIndex], direction = input[(gestureIndex + gestureMarker.Length)..];
            activeInputMappings.TryGetValue(baseGestureInput, out var captured);
            var source = captured?.Mapping ?? FindMapping(baseGestureInput);
            string? gestureName = source?.Kind == ActionKind.Gesture ? source.Value : source?.LongPressKind == ActionKind.Gesture ? source.LongPressValue : null;
            var definition = captured?.Gesture ?? (gestureName == null ? null : appliedConfig.Gestures.FirstOrDefault(x => x.Name.Equals(gestureName, StringComparison.OrdinalIgnoreCase)));
            if (source == null || definition == null)
                return false;
            var (Kind, Value) = GestureAction(definition, direction);
            if (Kind is ActionKind.None or ActionKind.Disabled || string.IsNullOrWhiteSpace(Value))
                return Kind == ActionKind.Disabled;
            var gestureMap = new Mapping { Input = source.Input, Layer = source.Layer, Kind = Kind, Value = Value };
            if (!actionQueue.TryAdd((gestureMap, input, false)))
                Dispatcher.BeginInvoke(() => { LastInput.Text = "連続入力が多すぎるため一部を安全に破棄しました"; LastInput.Foreground = ThemeService.Brush("DangerBrush"); });
            else
                RecordMappedAction(gestureMap, input);
            return true;
        }
        bool longPress = input.EndsWith(":Long", StringComparison.OrdinalIgnoreCase), dragStart = input.EndsWith(":DragStart", StringComparison.OrdinalIgnoreCase), dragEnd = input.EndsWith(":DragEnd", StringComparison.OrdinalIgnoreCase), pressStart = input.EndsWith(":PressStart", StringComparison.OrdinalIgnoreCase), pressEnd = input.EndsWith(":PressEnd", StringComparison.OrdinalIgnoreCase);
        string baseInput = longPress ? input[..^5] : dragStart ? input[..^10] : dragEnd ? input[..^8] : pressStart ? input[..^11] : pressEnd ? input[..^9] : input;
        var map = activeInputMappings.TryGetValue(baseInput, out var capturedInput) ? capturedInput.Mapping : FindMapping(baseInput);
        if (map == null)
        {
            if (dragEnd || pressEnd)
            {
                if (!QueueDragAction(null, input))
                    _ = Task.Run(InputEngine.EndModifierDrag);
                return true;
            }
            return false;
        }
        // A taskbar long-press-only mouse mapping must not consume the normal
        // short click. Replay that physical click only after ProcessPress has
        // confirmed it was not promoted to the long action.
        if (TaskbarShortClickReplay(map, baseInput, longPress, dragStart, dragEnd, pressStart, pressEnd) is { } replayClick)
        {
            // ProcessPress calls HandleInput while the low-level hook owns the
            // engine state lock. SendInput here can stall Explorer and make
            // Windows silently retire the hook. Preserve order on a dedicated
            // worker and let this physical Up callback return immediately.
            if (!taskbarClickReplayQueue.IsAddingCompleted)
                taskbarClickReplayQueue.TryAdd(replayClick);
            return true;
        }
        var snapshot = CloneMapping(map);
        if ((pressStart || pressEnd || dragStart || dragEnd) && MappingExecutor.IsModifierDrag(snapshot.Value))
        {
            bool queued = QueueDragAction(snapshot, input);
            if (queued)
                RecordMappedAction(snapshot, input);
            return queued;
        }
        if (pressStart || pressEnd)
            return false;
        if (!actionQueue.TryAdd((snapshot, input, false)))
            Dispatcher.BeginInvoke(() => { LastInput.Text = "連続入力が多すぎるため一部を安全に破棄しました"; LastInput.Foreground = ThemeService.Brush("DangerBrush"); });
        else
        {
            if (runtimeRole == RuntimeRole.ElevatedHelper)
                DeckIpcDiagnostics.LogElevatedHookAction(input, snapshot);
            else
                DeckIpcDiagnostics.LogMappedActionQueued(input, snapshot);
            RecordMappedAction(snapshot, input);
        }
        return true;
    }
    void CaptureInputMapping(string input)
    {
        var mapping = FindMapping(input);
        if (mapping == null)
            return;
        GestureDefinition? gesture = null;
        string? gestureName = mapping.Kind == ActionKind.Gesture ? mapping.Value : mapping.LongPressKind == ActionKind.Gesture ? mapping.LongPressValue : null;
        if (!string.IsNullOrWhiteSpace(gestureName) && appliedConfig.Gestures.FirstOrDefault(x => x.Name.Equals(gestureName, StringComparison.OrdinalIgnoreCase)) is { } definition)
            gesture = CloneGesture(definition);
        activeInputMappings[input] = new InputMappingSnapshot(CloneMapping(mapping), gesture);
    }
    void ReleaseInputMapping(string input) => activeInputMappings.TryRemove(input, out _);
    void CaptureLayerMappings(string layer)
    {
        var profile = appliedConfig.Profiles.FirstOrDefault(x => x.Name.Equals(appliedConfig.ActiveProfile, StringComparison.OrdinalIgnoreCase))
            ?? appliedConfig.Profiles.FirstOrDefault();
        if (profile == null)
            return;
        string foregroundProcess = profile.Mappings.Any(mapping => !string.IsNullOrWhiteSpace(mapping.Application))
            ? ConditionMatcher.ForegroundProcessName()
            : "";
        activeLayerMappings[layer] = new LayerMappingSnapshot(
            profile.Mappings
                .Where(mapping => MappingApplicationMatches(mapping, foregroundProcess))
                .Select(CloneMapping)
                .ToArray());
    }
    void ReleaseLayerMappings(string layer) => activeLayerMappings.TryRemove(layer, out _);
    internal static (ActionKind Kind, string Value) GestureAction(GestureDefinition gesture, string direction) => direction switch
    {
        "Up" => (gesture.UpKind, gesture.UpValue),
        "Down" => (gesture.DownKind, gesture.DownValue),
        "Left" => (gesture.LeftKind, gesture.LeftValue),
        "Right" => (gesture.RightKind, gesture.RightValue),
        _ => (gesture.CenterKind, gesture.CenterValue)
    };
    void RecordMappedAction(Mapping map, string input)
    {
        if (macroIsRecording && config.RecordMappedActionsInMacros)
            Dispatcher.BeginInvoke(() => macroWindow?.CaptureMappedAction(map, input));
    }
    AppConfig DeckExecutionConfig()
    {
        var snapshot = store.Clone(appliedConfig);
        snapshot.WindowActionTarget = WindowActionTarget.ActiveWindow;
        return snapshot;
    }
    AppConfig TaskbarExecutionConfig()
    {
        var snapshot = store.Clone(appliedConfig);
        // The taskbar is the invocation surface, never the target window. Its
        // physical Down is suppressed before this worker runs, so the existing
        // foreground window remains the correct recipient. Do not weaken the
        // Explorer shell-surface guard to make taskbar shortcuts work.
        snapshot.WindowActionTarget = WindowActionTarget.ActiveWindow;
        return snapshot;
    }
    internal static bool IsTaskbarMappedInput(string input)
        => input.StartsWith("Taskbar+", StringComparison.OrdinalIgnoreCase);
    void ProcessActions()
    {
        foreach (var (Map, Input, ForceActiveWindow) in actionQueue.GetConsumingEnumerable())
            try
            {
                MappingExecutor selectedExecutor = ForceActiveWindow
                    ? deckExecutor
                    : IsTaskbarMappedInput(Input) ? taskbarExecutor : executor;
                bool result = selectedExecutor.Execute(Map, Input, out var value);
                if (result)
                    Dispatcher.BeginInvoke(() => { LastInput.Text = $"実行: {Map.Input} → {value}"; LastInput.Foreground = value.StartsWith("エラー:", StringComparison.Ordinal) ? ThemeService.Brush("DangerBrush") : ThemeService.Brush("AccentTextBrush"); });
            }
            catch (Exception ex) { InputEngine.ReleaseAll(); Dispatcher.BeginInvoke(() => { LastInput.Text = "実行エラー: " + ex.Message; LastInput.Foreground = ThemeService.Brush("DangerBrush"); }); }
    }
    void ProcessDragActions()
    {
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        foreach (var (Map, Input) in dragActionQueue.GetConsumingEnumerable())
        {
            try
            {
                if (Map == null)
                {
                    InputEngine.EndModifierDrag();
                    continue;
                }
                // A fast physical click can be released before this dedicated
                // output worker wakes. Keep the queued Start/End pair intact so
                // Ctrl/Shift click is never silently discarded.
                bool result = executor.Execute(Map, Input, out var value);
                if (result
                    && !value.StartsWith("エラー:", StringComparison.Ordinal)
                    && Input.EndsWith(":PressStart", StringComparison.OrdinalIgnoreCase))
                    engine.NotifyNativeMouseDragStarted(Input);
                if (result)
                    Dispatcher.BeginInvoke(() => { LastInput.Text = $"実行: {Map.Input} → {value}"; LastInput.Foreground = value.StartsWith("エラー:", StringComparison.Ordinal) ? ThemeService.Brush("DangerBrush") : ThemeService.Brush("AccentTextBrush"); });
            }
            catch (Exception ex) { InputEngine.ReleaseAll(); Dispatcher.BeginInvoke(() => { LastInput.Text = "ドラッグ実行エラー: " + ex.Message; LastInput.Foreground = ThemeService.Brush("DangerBrush"); }); }
        }
    }
    void ProcessTaskbarClickReplays()
    {
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        // The physical taskbar click has already been suppressed by the hook.
        // Replay Down+Up as one SendInput batch so another producer cannot
        // interleave between them and a partial Down cannot strand Explorer.
        ProcessTaskbarClickReplays(taskbarClickReplayQueue.GetConsumingEnumerable(), InputEngine.SendMouseClickAtomic, InputEngine.ReleaseForProcessLifecycle, FailOpenAfterTaskbarClickReplayFailure);
    }
    internal static void ProcessTaskbarClickReplays(IEnumerable<string> clicks, Func<string, bool> sendClick, Action releaseInputs, Action replayFailed)
    {
        bool failureReported = false;
        foreach (string click in clicks)
        {
            bool sent = false;
            try { sent = sendClick(click); }
            catch { }
            if (sent)
                continue;
            try { releaseInputs(); }
            catch { }
            if (!failureReported)
            {
                failureReported = true;
                try { replayFailed(); }
                catch { }
            }
        }
    }
    void FailOpenAfterTaskbarClickReplayFailure()
    {
        if (Interlocked.Exchange(ref taskbarClickReplayFailed, 1) != 0)
            return;
        // Output failure must degrade RELYR, never Windows. Disabling the
        // engine clears captured state and makes every later physical key and
        // mouse event pass through unchanged for the rest of this process.
        engine.Enabled = false;
        Dispatcher.BeginInvoke(() =>
        {
            LastInput.Text = "安全停止: タスクバーのクリックをWindowsへ返せなかったため、入力機能を停止しました";
            LastInput.Foreground = ThemeService.Brush("DangerBrush");
            UpdateStatus();
        });
    }
    internal static string? TaskbarShortClickReplay(Mapping? map, string baseInput, bool longPress = false, bool dragStart = false, bool dragEnd = false, bool pressStart = false, bool pressEnd = false)
    {
        if (longPress || dragStart || dragEnd || pressStart || pressEnd
            || map is not { Kind: ActionKind.None }
            || map.LongPressKind == ActionKind.None
            || !baseInput.StartsWith("Taskbar+", StringComparison.OrdinalIgnoreCase))
            return null;
        if (baseInput.EndsWith("MouseLeft", StringComparison.OrdinalIgnoreCase))
            return "MouseLeft";
        return baseInput.EndsWith("MouseRight", StringComparison.OrdinalIgnoreCase) ? "MouseRight" : null;
    }
    bool QueueDragAction(Mapping? map, string input)
    {
        try
        {
            if (dragActionQueue.IsAddingCompleted)
                return false;
            // Do not wait here: HandleInput runs while the low-level hook owns
            // the engine state lock. Waiting for SendInput would make the
            // generated hook notification wait on that same lock and cancel the
            // drag on timeout. The dedicated worker preserves start/end order;
            // physical left-button Up remains the authoritative safety release.
            return dragActionQueue.TryAdd((map, input));
        }
        catch (InvalidOperationException) { return false; }
    }
    string QualifyInput(string input)
    {
        if (input.StartsWith("Taskbar+", StringComparison.OrdinalIgnoreCase))
            return input;
        string taskbarInput = "Taskbar+" + input;
        // Cursor inspection crosses into user32. Only pay that cost when the
        // active profile actually has an applicable taskbar mapping.
        return FindApplicableProfileMapping(taskbarInput, MappingInterceptsInput) != null
            && ConditionMatcher.IsCursorOverTaskbar() ? taskbarInput : input;
    }
    Mapping? FindMapping(string input)
    {
        if (TryGetLayerMappingSnapshot(input, out var layerSnapshot))
        {
            if (!input.StartsWith("Taskbar+", StringComparison.OrdinalIgnoreCase))
            {
                string taskbarInput = "Taskbar+" + input;
                var taskbarMapping = layerSnapshot.Mappings.LastOrDefault(x => x.Input.Equals(taskbarInput, StringComparison.OrdinalIgnoreCase));
                if (taskbarMapping != null && ConditionMatcher.IsCursorOverTaskbar())
                    return taskbarMapping;
            }
            return layerSnapshot.Mappings.LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
        }
        if (input.StartsWith("Taskbar+", StringComparison.OrdinalIgnoreCase))
            return FindApplicableProfileMapping(input, MappingInterceptsInput);
        string qualified = QualifyInput(input);
        if (!qualified.Equals(input, StringComparison.OrdinalIgnoreCase))
            return FindApplicableProfileMapping(qualified, MappingInterceptsInput);
        return FindApplicableProfileMapping(input, MappingInterceptsInput);
    }
    Mapping? FindCapturedInputMapping(string input)
        => activeInputMappings.TryGetValue(input, out var captured) ? captured.Mapping : FindMapping(input);
    bool HasMapping(string input)
    {
        // A profile created by an older build may still contain this mapping.
        // Never intercept the system's primary click outside explicit layers
        // such as Space+MouseLeft or Taskbar+MouseLeft.
        if (input.Equals("MouseLeft", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("MouseLeft+", StringComparison.OrdinalIgnoreCase))
            return false;
        if (input.EndsWith("+*", StringComparison.Ordinal))
        {
            string prefix = input[..^1];
            if (TryGetLayerMappingSnapshot(input, out var layerSnapshot))
                return layerSnapshot.Mappings.Any(x => MappingInterceptsInput(x) && x.Input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            return FindApplicableProfileMapping(null, x => MappingInterceptsInput(x) && x.Input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) != null;
        }
        return MappingInterceptsInput(FindMapping(input));
    }
    bool HasElevatedForegroundMapping(string input)
    {
        if (!WindowMonitorService.IsForegroundWindowElevated())
            return false;
        if (input.Equals("MouseLeft", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("MouseLeft+", StringComparison.OrdinalIgnoreCase))
            return false;
        if (input.EndsWith("+*", StringComparison.Ordinal))
        {
            string prefix = input[..^1];
            if (TryGetLayerMappingSnapshot(input, out var layerSnapshot))
                return layerSnapshot.Mappings.Any(x => x.Input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && IsElevatedInputMapping(x));
            return FindApplicableProfileMapping(null, x => x.Input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && IsElevatedInputMapping(x)) != null;
        }
        return IsElevatedInputMapping(FindMapping(input));
    }
    static bool IsElevatedInputMapping(Mapping? map)
        => IsElevatedInputMappingForTest(map);
    internal static bool IsElevatedInputMappingForTest(Mapping? map)
        => MappingInterceptsInput(map);
    bool TryGetLayerMappingSnapshot(string input, out LayerMappingSnapshot snapshot)
    {
        string candidate = input.StartsWith("Taskbar+", StringComparison.OrdinalIgnoreCase) ? input["Taskbar+".Length..] : input;
        foreach (var pair in activeLayerMappings)
            if (candidate.Equals(pair.Key, StringComparison.OrdinalIgnoreCase) || candidate.StartsWith(pair.Key + "+", StringComparison.OrdinalIgnoreCase))
            {
                snapshot = pair.Value;
                return true;
            }
        snapshot = null!;
        return false;
    }
    internal static bool MappingHasConfiguredAction(Mapping? map) => HasConfiguredShortAction(map) || HasConfiguredLongPress(map);
    internal static bool HasConfiguredShortAction(Mapping? map) => map != null && map.Kind != ActionKind.None && (map.Kind == ActionKind.Disabled || !string.IsNullOrWhiteSpace(map.Value));
    internal static bool MappingInterceptsInput(Mapping? map) => MappingHasConfiguredAction(map);
    internal static bool HasConfiguredLongPress(Mapping? map) => map != null && map.LongPressKind != ActionKind.None && (map.LongPressKind == ActionKind.Disabled || !string.IsNullOrWhiteSpace(map.LongPressValue));
    internal static void NormalizeLongOnlyMapping(Mapping map)
    {
        if (!HasConfiguredShortAction(map) && HasConfiguredLongPress(map))
            map.Kind = ActionKind.None;
    }
    internal static Mapping? FindProfileMapping(IReadOnlyList<Profile> profiles, string activeName, string? exactInput, Func<Mapping, bool>? predicate = null)
    {
        if (profiles.Count == 0)
            return null;
        var active = profiles.FirstOrDefault(x => x.Name == activeName) ?? profiles[0];
        return active.Mappings.LastOrDefault(x => (exactInput == null || x.Input.Equals(exactInput, StringComparison.OrdinalIgnoreCase)) && (predicate?.Invoke(x) ?? true));
    }
    Mapping? FindApplicableProfileMapping(string? exactInput, Func<Mapping, bool>? predicate = null)
    {
        string? foregroundProcess = null;
        var mappings = AppliedProfile.Mappings;
        for (int index = mappings.Count - 1; index >= 0; index--)
        {
            var mapping = mappings[index];
            if ((exactInput != null && !mapping.Input.Equals(exactInput, StringComparison.OrdinalIgnoreCase))
                || !(predicate?.Invoke(mapping) ?? true))
                continue;
            if (string.IsNullOrWhiteSpace(mapping.Application))
                return mapping;
            if (foregroundProcess == null)
            {
                foregroundProcess = ConditionMatcher.ForegroundProcessName();
            }
            if (MappingApplicationMatches(mapping, foregroundProcess))
                return mapping;
        }
        return null;
    }
    internal static bool MappingApplicationMatches(Mapping mapping, string foregroundProcess)
        => IsOwnProcess(foregroundProcess)
            || string.IsNullOrWhiteSpace(mapping.Application)
            || ConditionMatcher.Matches(mapping.Application, foregroundProcess);
    void RefreshProfiles()
    {
        if (!editorUiInitialized)
            return;
        loading = true;
        ProfileBox.ItemsSource = null;
        var profileItems = config.Profiles.Select(x => (object)x.Name).ToList();
        var createProfileItem = new ComboBoxItem
        {
            Tag = NewProfileMenuTag,
            Padding = new Thickness(0),
            Content = new Border
            {
                MinHeight = 40,
                Margin = new Thickness(0, 4, 0, 4),
                Padding = new Thickness(8, 6, 8, 6),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = new TextBlock
                {
                    Text = "＋  新しいプロファイル",
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
        if (createProfileItem.Content is Border createProfileBorder)
        {
            createProfileBorder.SetResourceReference(Border.BorderBrushProperty, "SubtleBorderBrush");
            if (createProfileBorder.Child is TextBlock createProfileLabel)
                createProfileLabel.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
        }
        System.Windows.Automation.AutomationProperties.SetName(createProfileItem, "新しいプロファイルを作成");
        profileItems.Add(createProfileItem);
        ProfileBox.ItemsSource = profileItems;
        ProfileBox.SelectedItem = config.ActiveProfile;
        loading = false;
        ColorButtons();
    }
    void ColorButtons()
    {
        if (!editorUiInitialized)
            return;
        var keyboardButtons = InputButtons(KeyboardPanel).Concat(InputButtons(SecondaryKeyboardPanel)).ToHashSet();
        foreach (var b in VisualInputButtons())
            UpdateInputButtonVisual(b, keyboardButtons.Contains(b));
        ColorDeckManagementButtons();
    }
    void UpdateInputButtonVisual(System.Windows.Controls.Button b, bool keyboardButton)
    {
        string key = (string)b.Tag;
        string input = InputForCurrentLayer(key);
        bool protectedLeftClick = IsProtectedNormalLeftClick(key);
        string? unavailableReason = InputAssignmentPolicy.UnavailableInputReason(input);
        bool unavailable = unavailableReason != null;
        bool reserved = protectedLeftClick || unavailable || (key == "Space" && currentLayer is "通常" or "Space") ||
                      (key == "CapsLock" && !editingSelectedInput && destinationInputTarget == null);
        var assigned = protectedLeftClick ? null : FindProfileMapping(config.Profiles, CurrentProfile.Name, input, MappingInterceptsInput);
        bool currentSelected = selected?.Input.Equals(input, StringComparison.OrdinalIgnoreCase) == true;
        bool multiSelectActive = MultiSelectToggle.IsChecked == true;
        bool multiSelected = multiSelectActive && multiSelectedInputs.Contains((string)b.Tag);
        bool selectionActive = multiSelectActive || selected != null;
        bool highlighted = multiSelectActive ? multiSelected : currentSelected;
        bool selectionOutlined = currentSelected || multiSelected;
        bool pulsing = multiSelected || (currentSelected && !selectionPulseSuppressed);
        System.Windows.Media.Brush pulseBrush = ThemeService.Brush("AccentBrush");
        b.Background = reserved && !selectionActive ? ThemeService.Brush("ReservedKeyBackground") : assigned != null ? new SolidColorBrush(AssignmentColorFor(assigned)) : ThemeService.Brush("KeyBackground");
        b.BorderBrush = ThemeService.Brush(selectionOutlined ? "AccentBrush" : "SubtleBorderBrush");
        b.BorderThickness = new Thickness(selectionOutlined ? 2 : 1);
        b.Foreground = assigned == null ? ThemeService.Brush("PrimaryText") : new SolidColorBrush(AssignmentTextColorFor(assigned));
        b.Opacity = selectionActive && !highlighted ? SelectionDimOpacity : reserved ? 0.48 : 1;
        b.IsEnabled = !protectedLeftClick && !unavailable;
        bool currentSelectionChanged = GetIsCurrentSelected(b) != currentSelected;
        bool pulseStateChanged = GetIsSelectionPulseActive(b) != pulsing;
        bool multiSelectionChanged = GetIsMultiSelected(b) != multiSelected;
        SetIsMultiSelected(b, multiSelected);
        SetIsCurrentSelected(b, currentSelected);
        if (currentSelectionChanged || multiSelectionChanged)
            SetSelectionStateVisual(b, currentSelected, multiSelected);
        SetIsSelectionPulseActive(b, pulsing);
        SetSelectionPulseBrush(b, pulseBrush);
        if (pulseStateChanged)
            SetSelectionPulseVisual(b, pulsing, pulseBrush);
        b.ToolTip = protectedLeftClick ? "通常レイヤーでは変更できません"
            : unavailableReason != null ? unavailableReason
            : assigned != null ? CreateAssignmentToolTip(assigned)
            : keyboardButton ? null
            : DefaultMouseToolTip(key);
        ToolTipService.SetShowOnDisabled(b, true);
        ToolTipService.SetInitialShowDelay(b, 250);
        ToolTipService.SetBetweenShowDelay(b, 80);
        ToolTipService.SetShowDuration(b, 20000);
    }
    void AnimateAssignmentCommit(string input)
    {
        var button = deckManagementMode && DeckPanelLayout.IsInputName(input)
            ? deckManagementButtons.FirstOrDefault(x => string.Equals(x.Tag?.ToString(), input, StringComparison.OrdinalIgnoreCase))
            : VisualInputButtons().FirstOrDefault(x => string.Equals(x.Tag?.ToString(), selectedBaseInput, StringComparison.OrdinalIgnoreCase));
        if (button == null)
            return;
        UiMotionService.RunSafely("assignment-commit", () =>
        {
            var scale = InputScaleTransform(button);
            if (!UiMotionService.Enabled)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = 1;
                scale.ScaleY = 1;
                return;
            }
            double rest = button.IsMouseOver ? 1.05 : 1;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimationUsingKeyFrames
            {
                FillBehavior = FillBehavior.Stop,
                KeyFrames =
                [
                    new EasingDoubleKeyFrame(.94, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(65))) { EasingFunction = UiMotionService.ResponsiveEaseOut() },
                    new EasingDoubleKeyFrame(rest + .025, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(145))) { EasingFunction = UiMotionService.ResponsiveEaseOut() },
                    new EasingDoubleKeyFrame(rest, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(235))) { EasingFunction = UiMotionService.GentleSettleEase() }
                ]
            });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimationUsingKeyFrames
            {
                FillBehavior = FillBehavior.Stop,
                KeyFrames =
                [
                    new EasingDoubleKeyFrame(.94, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(65))) { EasingFunction = UiMotionService.ResponsiveEaseOut() },
                    new EasingDoubleKeyFrame(rest + .025, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(145))) { EasingFunction = UiMotionService.ResponsiveEaseOut() },
                    new EasingDoubleKeyFrame(rest, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(235))) { EasingFunction = UiMotionService.GentleSettleEase() }
                ]
            });
        });
    }
    static void SetSelectionPulseVisual(System.Windows.Controls.Button button, bool active, System.Windows.Media.Brush pulseBrush)
    {
        button.ApplyTemplate();
        if (button.Template.FindName("SelectionPulse", button) is not UIElement pulse)
            return;
        // The pulse layer stays disabled: selection uses only relative
        // brightness plus the button's shared accent outline, never a tint,
        // underline, or badge over the key's own appearance.
        pulse.BeginAnimation(UIElement.OpacityProperty, null);
        pulse.Opacity = 0;
    }
    static void SetCurrentSelectionVisual(System.Windows.Controls.Button button, bool active)
        => SetSelectionStateVisual(button, active, GetIsMultiSelected(button));
    static void SetSelectionStateVisual(System.Windows.Controls.Button button, bool currentSelected, bool multiSelected)
    {
        button.ApplyTemplate();
        UIElement? tint = button.Template.FindName("SelectionTint", button) as UIElement
            ?? button.Template.FindName("EnterSelectionTint", button) as UIElement;
        if (tint != null)
            tint.Opacity = 0;
        if (button.Template.FindName("MultiSelectBadge", button) is UIElement badge)
            badge.Opacity = 0;
    }
    void ColorDeckManagementButtons()
    {
        foreach (var button in deckManagementButtons)
            UpdateDeckManagementButtonVisual(button);
    }
    void RefreshSelectedInputVisual(string input)
    {
        if (deckManagementMode && DeckPanelLayout.IsInputName(input))
        {
            foreach (var button in deckManagementButtons.Where(x => x.Tag is string tag && tag.Equals(input, StringComparison.OrdinalIgnoreCase)))
                UpdateDeckManagementButtonVisual(button);
            return;
        }
        var selectedButton = VisualInputButtons().FirstOrDefault(x => string.Equals(x.Tag?.ToString(), selectedBaseInput, StringComparison.OrdinalIgnoreCase));
        if (selectedButton != null)
            UpdateInputButtonVisual(selectedButton, IsDescendantOf(selectedButton, KeyboardPanel) || IsDescendantOf(selectedButton, SecondaryKeyboardPanel));
    }
    void UpdateDeckManagementButtonVisual(System.Windows.Controls.Button button)
    {
        if (button.Tag is not string input)
            return;
        DeckVisualUpdateCountForTest++;
        var mapping = DeckPanelLayout.FindMapping(selectedDeckLayout ?? DeckPanelLayout.DefaultLayout(config), DeckPanelLayout.SlotNumber(input));
        var assigned = MappingInterceptsInput(mapping) ? mapping : null;
        bool hasCustomColor = DeckPanelLayout.TryGetButtonColor(mapping, out var customColor);
        bool currentSelected = selected?.Input.Equals(input, StringComparison.OrdinalIgnoreCase) == true;
        bool multiSelectActive = MultiSelectToggle.IsChecked == true;
        bool multiSelected = multiSelectActive && multiSelectedInputs.Contains(input);
        bool selectionActive = multiSelectActive || selected != null;
        bool highlighted = multiSelectActive ? multiSelected : currentSelected;
        bool selectionOutlined = currentSelected || multiSelected;
        System.Windows.Media.Brush pulseBrush = ThemeService.Brush("AccentBrush");
        button.Background = hasCustomColor ? new SolidColorBrush(customColor) : assigned != null ? new SolidColorBrush(AssignmentColorFor(assigned)) : ThemeService.Brush("KeyBackground");
        button.BorderBrush = ThemeService.Brush(selectionOutlined ? "AccentBrush" : "SubtleBorderBrush");
        button.BorderThickness = new Thickness(selectionOutlined ? 2 : 1);
        button.Foreground = hasCustomColor ? new SolidColorBrush(DeckPanelLayout.TextColorFor(customColor)) : assigned == null ? ThemeService.Brush("PrimaryText") : new SolidColorBrush(AssignmentTextColorFor(assigned));
        button.Opacity = selectionActive && !highlighted ? SelectionDimOpacity : 1;
        bool pulsing = multiSelected || currentSelected && !selectionPulseSuppressed;
        bool currentSelectionChanged = GetIsCurrentSelected(button) != currentSelected;
        bool pulseStateChanged = GetIsSelectionPulseActive(button) != pulsing;
        bool multiSelectionChanged = GetIsMultiSelected(button) != multiSelected;
        SetIsCurrentSelected(button, currentSelected);
        SetIsMultiSelected(button, multiSelected);
        if (currentSelectionChanged || multiSelectionChanged)
            SetSelectionStateVisual(button, currentSelected, multiSelected);
        SetIsSelectionPulseActive(button, pulsing);
        SetSelectionPulseBrush(button, pulseBrush);
        if (pulseStateChanged)
            SetSelectionPulseVisual(button, pulsing, pulseBrush);
        bool monitorContentMatches = DeckMonitorCatalog.TryGet(mapping?.DeckMonitor, out var monitor)
            && button.Content is DeckMonitorView monitorView
            && monitorView.MonitorId.Equals(monitor.Id, StringComparison.OrdinalIgnoreCase);
        if ((DeckMonitorCatalog.IsMonitor(mapping?.DeckMonitor) && !monitorContentMatches)
            || button.Content is DeckMonitorView && !monitorContentMatches
            || DeckPanelLayout.HasRegisteredFile(mapping)
            || DeckIconCatalog.HasIcon(mapping)
            || button.Content is not TextBlock and not DeckMonitorView
            || button.Content is TextBlock contentText && Equals(contentText.Tag, DeckIconCatalog.VisualTag))
            button.Content = DeckPanelLayout.CreateButtonContent(input, mapping);
        else if (button.Content is TextBlock textContent)
            textContent.Text = DeckPanelLayout.ActionLabel(input, mapping);
        if (deckManagementNameLabels.TryGetValue(button, out var nameLabel))
        {
            nameLabel.Text = mapping?.Description ?? "";
            if (hasCustomColor || assigned != null)
                nameLabel.Foreground = button.Foreground;
            else
                nameLabel.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        }
        if (deckListActionLabels.TryGetValue(button, out var labels))
        {
            var summary = DeckListActionSummary(mapping);
            labels.Type.Text = summary.Type;
            labels.Value.Text = summary.Value;
        }
        bool missingFile = DeckPanelLayout.HasRegisteredFile(mapping) && !DeckPanelLayout.IsAvailableFile(mapping);
        button.Resources["DeckFileAvailable"] = DeckPanelLayout.IsAvailableFile(mapping);
        button.ToolTip = missingFile ? DeckPanelLayout.CreateMissingFileToolTip() : assigned != null ? CreateAssignmentToolTip(assigned) : null;
        ToolTipService.SetInitialShowDelay(button, 250);
        ToolTipService.SetShowDuration(button, 20000);
    }
    internal static WpfColor AssignmentColorFor(Mapping mapping)
    {
        var kind = AssignmentDisplayKind(mapping);
        return kind switch
        {
            ActionKind.Key => WpfColor.FromRgb(217, 130, 43),
            ActionKind.Disabled => WpfColor.FromRgb(98, 107, 120),
            ActionKind.Text => WpfColor.FromRgb(205, 160, 45),
            ActionKind.Macro => WpfColor.FromRgb(194, 67, 77),
            ActionKind.Launch => WpfColor.FromRgb(139, 91, 190),
            ActionKind.Profile => WpfColor.FromRgb(50, 112, 196),
            ActionKind.Gesture => WpfColor.FromRgb(0, 151, 167),
            _ => WpfColor.FromRgb(23, 141, 121)
        };
    }
    static ActionKind AssignmentDisplayKind(Mapping mapping) => !HasConfiguredShortAction(mapping) && HasConfiguredLongPress(mapping) ? mapping.LongPressKind : mapping.Kind == ActionKind.None ? mapping.LongPressKind : mapping.Kind;
    static WpfColor AssignmentTextColorFor(Mapping mapping) => DeckPanelLayout.TextColorFor(AssignmentColorFor(mapping));
    static string AssignmentTypeLabel(Mapping mapping) => AssignmentDisplayKind(mapping) switch { ActionKind.Key => "別のキー", ActionKind.Disabled => "無効化", ActionKind.Text => "文字列", ActionKind.Macro => "マクロ", ActionKind.Launch => "アプリ・パス", ActionKind.Profile => "プロファイル", ActionKind.Gesture => "ジェスチャー", _ => "ショートカット" };
    internal static string? AssignmentToolTipText(Mapping? mapping)
    {
        if (!MappingInterceptsInput(mapping))
            return null;
        var lines = new List<string>();
        if (HasConfiguredShortAction(mapping))
        {
            lines.Add("短押し");
            lines.Add("アクション：" + ActionKindDisplayName(mapping!.Kind));
            lines.Add("実行内容：" + FriendlyActionValue(mapping.Kind, mapping.Value));
        }
        if (HasConfiguredLongPress(mapping))
        {
            if (lines.Count > 0)
                lines.Add("");
            lines.Add($"長押し（{mapping!.LongPressMs} ms）");
            lines.Add("アクション：" + ActionKindDisplayName(mapping.LongPressKind));
            lines.Add("実行内容：" + FriendlyActionValue(mapping.LongPressKind, mapping.LongPressValue));
        }
        return string.Join(Environment.NewLine, lines);
    }
    internal static string ActionKindDisplayName(ActionKind kind) => kind switch
    {
        ActionKind.Disabled => "無効化",
        ActionKind.Key => "別のキー",
        ActionKind.Shortcut => "ショートカット",
        ActionKind.Text => "文字列入力",
        ActionKind.Launch => "アプリ・ファイル・URL",
        ActionKind.Mouse => "マウス操作",
        ActionKind.Macro => "マクロ",
        ActionKind.Profile => "プロファイル切替",
        ActionKind.Gesture => "ジェスチャー",
        _ => "未設定"
    };
    internal static string FriendlyActionValue(ActionKind kind, string value)
    {
        if (kind == ActionKind.Disabled)
            return "入力しない";
        if (kind == ActionKind.Profile)
            return ProfileDisplayValue(value);
        if (kind == ActionKind.Gesture)
            return GestureDisplayValue(value);
        if (kind == ActionKind.Mouse)
            return ActionCatalog.DisplayMouseAction(value);
        if (kind == ActionKind.Shortcut && DeckPanelLayout.IsDeckAction(value))
            return "Deckパネル";
        if (kind == ActionKind.Shortcut && value.Equals(ActionCatalog.ShowRelyrMainWindowAction, StringComparison.OrdinalIgnoreCase))
            return "RELYRを表示";
        var catalog = ActionCatalog.Items.FirstOrDefault(x => x.Kind == kind && x.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        string display = catalog?.Name ?? (kind is ActionKind.Key or ActionKind.Shortcut or ActionKind.Mouse ? DisplayInputName(value) : value);
        if (string.IsNullOrWhiteSpace(display))
            display = "未設定";
        return display.Length <= 180 ? display : display[..180] + "…";
    }
    static System.Windows.Controls.ToolTip CreateAssignmentToolTip(Mapping mapping) => new()
    {
        Content = new TextBlock { Text = AssignmentToolTipText(mapping), Foreground = ThemeService.Brush("PrimaryText"), TextWrapping = TextWrapping.Wrap, LineHeight = 20, MaxWidth = 340 },
        Background = ThemeService.Brush("CardBackground"),
        BorderBrush = ThemeService.Brush("AccentBrush"),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(12, 9, 12, 9),
        Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse
    };
    static string? DefaultMouseToolTip(string key) => key switch
    {
        "MouseLeft" => "左クリック",
        "MouseRight" => "右クリック／右クリックレイヤー",
        "WheelUp" => "ホイール上回転",
        "MouseMiddle" => "ホイールクリック（中央ボタン）",
        "WheelDown" => "ホイール下回転",
        "TiltLeft" => "チルトホイール左",
        "TiltRight" => "チルトホイール右",
        "MouseBack" => "戻るボタン／戻るレイヤー",
        "MouseForward" => "進むボタン／進むレイヤー",
        "MouseX" => "追加マウスボタン",
        _ => null
    };
    IEnumerable<System.Windows.Controls.Button> VisualInputButtons() => InputButtons(KeyboardPanel).Concat(InputButtons(SecondaryKeyboardPanel)).Concat(InputButtons(MousePanel));
    bool IsProtectedNormalLeftClick(string key)
        => currentLayer == "通常" && key.Equals("MouseLeft", StringComparison.OrdinalIgnoreCase);
    static IEnumerable<System.Windows.Controls.Button> InputButtons(System.Windows.Controls.Panel panel)
    {
        foreach (UIElement child in panel.Children)
        {
            if (child is System.Windows.Controls.Button b && b.Tag is string)
                yield return b;
            if (child is System.Windows.Controls.Panel nested)
            foreach (var b2 in InputButtons(nested))
                yield return b2;
        }
    }
    static string LayerDisplayName(string layer) => layer switch { "通常" => "デフォルト", "Space" => "Space", "CapsLock" => "CapsLock", "MouseRight" => "右クリック", "MouseForward" => "進む", "MouseBack" => "戻る", "Taskbar" => "タスクバー", DeckPanelLayout.Layer => "Deckパネル", _ => layer };
    void MarkDirty(bool refreshDeckPanel = true)
    {
        if (config == null)
            return;
        hasUnsavedChanges = true;
        UpdateUnsavedChangesIndicator();
        deckOverlayVisualSynchronized = false;
        if (deckManagementMode && DeckEditorWorkspace?.Visibility == Visibility.Visible)
            UpdateDeckSaveStatus(saved: false);
        if (refreshDeckPanel)
        {
            OverlayService.RefreshDeckPanel();
            deckOverlayVisualSynchronized = true;
        }
        if (config.AutoSave)
        {
            // Execution text is a draft until the user confirms it or clicks away.
            // This prevents a half-typed shortcut/path from reaching the live engine.
            if (destinationInputTarget != null || editingSelectedInput)
            {
                autoSaveTimer.Stop();
                LastInput.Text = "編集中 — 確定または欄外クリックで反映します";
                LastInput.Foreground = ThemeService.Brush("WarningBrush");
                return;
            }
            LastInput.Text = "変更を自動保存しています…";
            LastInput.Foreground = ThemeService.Brush("AccentTextBrush");
            autoSaveTimer.Stop();
            autoSaveTimer.Start();
        }
        else
        {
            LastInput.Text = "未保存の変更があります — 保存すると反映されます";
            LastInput.Foreground = ThemeService.Brush("WarningBrush");
        }
    }
    void ShowInlineNotice(string message)
    {
        LastInput.Text = "ⓘ " + message;
        LastInput.Foreground = ThemeService.Brush("WarningBrush");
    }
    void ShowInlineError(string message)
    {
        LastInput.Text = "⚠ " + message;
        LastInput.Foreground = ThemeService.Brush("DangerBrush");
    }
    void UpdateLayerButtons()
    {
        if (!editorUiInitialized)
            return;
        if (NormalLayerButton == null)
            return;
        foreach (var b in new[] { NormalLayerButton, SpaceLayerButton, CapsLockLayerButton, TaskbarLayerButton, RightMouseLayerButton, BackMouseLayerButton, ForwardMouseLayerButton })
        {
            bool blocked = IsMouseLayerBlockedByDirectGesture(config.Profiles, CurrentProfile.Name, b.Tag?.ToString() ?? "");
            bool active = Equals(b.Tag, currentLayer);
            b.IsEnabled = !blocked;
            b.Background = active ? ThemeService.Brush("LayerActiveBackground") : System.Windows.Media.Brushes.Transparent;
            b.BorderBrush = active ? ThemeService.Brush("LayerActiveBackground") : System.Windows.Media.Brushes.Transparent;
            b.Foreground = ThemeService.Brush("PrimaryText");
            if (b.Content is Grid layerGrid)
            {
                var indicator = layerGrid.Children.OfType<System.Windows.Shapes.Ellipse>().FirstOrDefault(x => Equals(x.Tag, "LayerActiveIndicator"));
                if (indicator != null)
                    indicator.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                if (layerGrid.Children.OfType<Border>().FirstOrDefault() is { } iconFrame)
                {
                    iconFrame.Background = active ? ThemeService.Brush("AccentSoftBrush") : ThemeService.Brush("SurfaceBackground");
                    if (iconFrame.Child is Viewbox { Child: Canvas iconCanvas })
                        foreach (var path in iconCanvas.Children.OfType<System.Windows.Shapes.Path>())
                            path.Stroke = active ? ThemeService.Brush("AccentBrush") : ThemeService.Brush("PrimaryText");
                }
            }
            b.ToolTip = blocked ? $"通常レイヤーの{MouseLayerLabel(b.Tag?.ToString() ?? "")}にジェスチャーが割り当てられているため使用できません。ジェスチャーを削除すると再び使用できます。" : null;
        }
        bool deckActive = deckManagementMode;
        DeckPanelManagerButton.Background = deckActive ? ThemeService.Brush("LayerActiveBackground") : System.Windows.Media.Brushes.Transparent;
        DeckPanelManagerButton.BorderBrush = System.Windows.Media.Brushes.Transparent;
        DeckPanelManagerButton.Foreground = ThemeService.Brush("PrimaryText");
        DeckNavigationActiveIndicator.Visibility = deckActive ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceHeader.Visibility = Visibility.Collapsed;
        if (!deckActive)
            WorkspaceSubtitle.Text = LayerDisplayName(currentLayer);
        InputDisplayText?.Text = selected == null ? "キーを選択してください" : DisplayInputName(selected.Input);
    }
    internal static bool IsMouseLayerBlockedByDirectGesture(IReadOnlyList<Profile> profiles, string profileName, string layer)
    {
        if (layer is not ("MouseRight" or "MouseBack" or "MouseForward"))
            return false;
        return FindProfileMapping(profiles, profileName, layer, MappingInterceptsInput)?.Kind == ActionKind.Gesture;
    }
    static string MouseLayerLabel(string layer) => layer switch { "MouseRight" => "右クリック", "MouseBack" => "戻るボタン", "MouseForward" => "進むボタン", _ => layer };
    bool ConfirmDirectMouseGestureConflict(string input)
    {
        if (input is not ("MouseRight" or "MouseBack" or "MouseForward"))
            return true;
        string label = MouseLayerLabel(input);
        string message = $"通常レイヤーの「{label}」にジェスチャーを割り当てると、「{label}レイヤー」はジェスチャーを削除するまで使用できません。\n\nレイヤー内の既存の割り当ては削除されません。続行しますか？";
        return WpfMessageBox.Show(this, message, "レイヤーとの競合", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
    }
    void Delete_Click(object s, RoutedEventArgs e)
    {
        if (selected == null)
            return;
        string input = selected.Input;
        var mappings = MappingCollectionForInput(input);
        if (DeckPanelLayout.IsInputName(input))
        {
            var mapping = mappings.LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
            if (mapping != null)
            {
                mapping.Kind = ActionKind.None;
                mapping.Value = "";
                mapping.LongPressKind = ActionKind.None;
                mapping.LongPressValue = "";
                mapping.DragValue = "";
                mapping.DragEndValue = "";
                mapping.Application = "";
                if (mapping.DeckIconAutoAssigned)
                {
                    mapping.DeckIcon = "";
                    mapping.DeckIconAutoAssigned = false;
                }
                if (!HasDeckButtonContent(mapping))
                    mappings.Remove(mapping);
            }
        }
        else
            mappings.Remove(selected);
        UpdateLayerButtons();
        ClearSelectedInput(s as FrameworkElement);
        MarkDirty();
        LastInput.Text = DisplayInputName(input) + " の割り当てを削除しました";
        LastInput.Foreground = ThemeService.Brush("DangerBrush");
    }
    static ActionOption[] ActionOptions(bool allowGesture)
    {
        var options = new List<ActionOption>
        {
            new(ActionKind.Key, "⌨", "別のキー"),
            new(ActionKind.Profile, "⇄", "プロファイル"),
            new(ActionKind.Shortcut, "↗", "ショートカット"),
            new(ActionKind.Text, "T", "文字列"),
            new(ActionKind.Launch, "▱", "アプリ・パス"),
            new(ActionKind.Macro, "⌘", "マクロ"),
            new(ActionKind.Gesture, "✣", "ジェスチャー", allowGesture),
            new(ActionKind.Shortcut, "▦", "Deckパネル", true, false, true),
            new(ActionKind.None, "⌨", "キーパッドから入力", true, true)
        };
        return [.. options];
    }
    static ActionOption[] DeckActionOptions()
    {
        var options = ActionOptions(false).Where(option => option.Kind != ActionKind.Gesture).ToList();
        int keypadIndex = options.FindIndex(option => option.IsKeypad);
        options.Insert(keypadIndex < 0 ? options.Count : keypadIndex,
            new ActionOption(ActionKind.None, "\uE9D9", "モニター", IsDeckMonitor: true));
        return [.. options];
    }
    internal FrameworkElement DeckKeypadInputButton => KindBox;
    sealed record ActionOption(ActionKind Kind, string Icon, string Label, bool IsEnabled = true, bool IsKeypad = false, bool IsDeckPanel = false, bool IsDeckMonitor = false)
    {
        public ActionKind? SelectionKind => IsKeypad || IsDeckPanel || IsDeckMonitor ? null : Kind;
        public string DisplayLabel => Label;
        public double LabelFontSize => IsKeypad ? 10 : 11;
    }
    sealed record InputMappingSnapshot(Mapping Mapping, GestureDefinition? Gesture);
    sealed record LayerMappingSnapshot(IReadOnlyList<Mapping> Mappings);
}
