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
        SelectInput(currentLayer == "通常" ? key : currentLayer + "+" + key, false);
    }
    void OpenShortcutForVisualInput(string key)
    {
        if (MultiSelectToggle.IsChecked == true)
            return;
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
        var button = (System.Windows.Controls.Button)sender;
        var menu = CreateInputContextMenu(key, KeyboardPanel.Children.Contains(button) || InputButtons(MousePanel).Contains(button));
        menu.PlacementTarget = (System.Windows.Controls.Button)sender;
        menu.IsOpen = true;
    }
    internal ContextMenu CreateInputContextMenu(string key, bool includeAllLayers = true)
    {
        string input = currentLayer == "通常" ? key : currentLayer + "+" + key;
        var existing = CurrentProfile.Mappings.LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "この割り当てをコピー", IsEnabled = existing != null };
        copy.Click += (_, _) => { copiedMapping = existing == null ? null : CloneMapping(existing); ShowInlineNotice(input + " の割り当てをコピーしました"); };
        var paste = new MenuItem { Header = "コピーした割り当てを貼り付け", IsEnabled = copiedMapping != null };
        paste.Click += (_, _) =>
        {
            if (copiedMapping == null) return;
            var map = CloneMapping(copiedMapping);
            map.Input = input;
            map.Layer = currentLayer;
            if (map.Kind == ActionKind.Gesture && !ConfirmDirectMouseGestureConflict(input)) return;
            CurrentProfile.Mappings.RemoveAll(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
            CurrentProfile.Mappings.Add(map);
            UpdateLayerButtons();
            MarkDirty();
            ClearSelectedInput();
            ShowInlineNotice(DisplayInputName(input) + " の割り当てを貼り付けました");
        };
        var assignAllLayers = new MenuItem { Header = "全レイヤーに割り当てる", IsEnabled = existing != null };
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
        if (string.IsNullOrWhiteSpace(key) || DeckPanelLayout.IsInputName(key))
            return 0;
        var template = CloneMapping(source);
        int applied = 0;
        foreach (string layer in AllAssignmentLayerNames)
        {
            // A layer activation key cannot trigger itself while it is being held.
            if (layer.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;
            string targetInput = layer + "+" + key;
            mappings.RemoveAll(mapping => mapping.Input.Equals(targetInput, StringComparison.OrdinalIgnoreCase));
            var copy = CloneMapping(template);
            copy.Input = targetInput;
            copy.Layer = layer;
            mappings.Add(copy);
            applied++;
        }
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
        var targets = selectedKeys.Select((key, index) => (Input: MultiSelectionInput(key), Source: sources.Length == 1 ? sources[0] : index < sources.Length ? sources[index] : null)).ToList();
        if (targets.Count == 0)
            return;
        foreach (var (Input, Source) in targets)
            if (Source?.Kind == ActionKind.Gesture && !ConfirmDirectMouseGestureConflict(Input))
                return;
        foreach (var (Input, Source) in targets)
        {
            var mappings = deckManagementMode && selectedDeckLayout != null ? selectedDeckLayout.Mappings : CurrentProfile.Mappings;
            mappings.RemoveAll(x => x.Input.Equals(Input, StringComparison.OrdinalIgnoreCase));
            if (Source == null)
                continue;
            var mapping = CloneMapping(Source);
            mapping.Input = Input;
            mapping.Layer = deckManagementMode ? DeckPanelLayout.Layer : currentLayer;
            mappings.Add(mapping);
        }
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
        if (!IsLongPressSupportedFor(selected))
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
        if (!longPress && selected != null && DeckPanelLayout.IsInputName(selected.Input))
            selected.DeckMonitor = string.Empty;
        if (!longPress && selected != null
            && DeckPanelLayout.IsInputName(selected.Input)
            && string.IsNullOrWhiteSpace(selected.DeckIconPath)
            && (string.IsNullOrWhiteSpace(selected.DeckIcon) || selected.DeckIconAutoAssigned))
        {
            selected.DeckIcon = DeckIconCatalog.SuggestedPresetId(action);
            selected.DeckIconAutoAssigned = true;
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
        if (selected == null || action.Kind == ActionKind.Gesture && longPress)
            return;
        if (action.Kind == ActionKind.Gesture && !ConfirmDirectMouseGestureConflict(selected.Input))
            return;
        loading = true;
        if (longPress)
        {
            selected.LongPressKind = action.Kind;
            selected.LongPressValue = action.Value;
            SelectActionOptionForMapping(LongKindBox, action.Kind, action.Value);
            LongValueBox.Text = DisplayConfiguredActionValue(action.Kind, action.Value);
        }
        else
        {
            selected.Kind = action.Kind;
            selected.Value = action.Value;
            SelectActionOptionForMapping(KindBox, action.Kind, action.Value);
            ValueBox.Text = DisplayConfiguredActionValue(action.Kind, action.Value);
            if (action.Kind == ActionKind.Gesture)
            {
                selected.LongPressKind = ActionKind.None;
                selected.LongPressValue = "";
                LongKindBox.SelectedIndex = -1;
                LongValueBox.Clear();
                LongPressExpander.IsExpanded = false;
            }
        }
        loading = false;
        var mappings = MappingCollectionForInput(selected.Input);
        if (MappingHasConfiguredAction(selected) && !mappings.Contains(selected))
            mappings.Add(selected);
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
        if (selected == null)
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
        if (selected == null || !config.Profiles.Any(x => x.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase)))
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
        if (DeckPanelLayout.IsInputName(selected.Input)
            && (Kind != ActionKind.None || longAction.Kind != ActionKind.None || !string.IsNullOrWhiteSpace(Value) || !string.IsNullOrWhiteSpace(longAction.Value)))
            selected.DeckMonitor = string.Empty;
        if (Kind == ActionKind.Gesture)
        {
            selected.LongPressKind = ActionKind.None;
            selected.LongPressValue = "";
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
            var mappings = MappingCollectionForInput(selected.Input);
            if (!mappings.Contains(selected))
                mappings.Add(selected);
        }
        else if (deckManagementMode && HasDeckButtonContent(selected))
        {
            var mappings = MappingCollectionForInput(selected.Input);
            if (!mappings.Contains(selected))
                mappings.Add(selected);
        }
        else
            MappingCollectionForInput(selected.Input).Remove(selected);
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
        // A short-press gesture owns the complete press/move/release lifecycle, so it
        // cannot safely coexist with an independent long-press action.
        bool shortGestureSelected = selected?.Kind == ActionKind.Gesture;
        bool legacyLongGestureSelected = selected?.LongPressKind == ActionKind.Gesture;
        bool longPressSupported = IsLongPressSupportedFor(selected);
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
            : IsNormalLayerAlphabetKey(selected)
                ? "＋ 長押し（通常レイヤーの英字では設定できません）"
                : "＋ 長押しを追加（任意）";
        if (!longPressSupported)
            LongPressExpander.IsExpanded = false;
    }

    internal static bool IsLongPressSupportedFor(Mapping? mapping)
        => mapping?.Kind != ActionKind.Gesture && !IsNormalLayerAlphabetKey(mapping);

    static bool IsNormalLayerAlphabetKey(Mapping? mapping)
        => mapping is { Layer: "通常", Input.Length: 1 }
           && mapping.Input[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
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
        ProcessTaskbarClickReplays(taskbarClickReplayQueue.GetConsumingEnumerable(), InputEngine.SendMouseClickAtomic, InputEngine.ReleaseAllDefensively, FailOpenAfterTaskbarClickReplayFailure);
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
        bool protectedLeftClick = IsProtectedNormalLeftClick((string)b.Tag);
        bool reserved = protectedLeftClick || ((string)b.Tag == "Space" && currentLayer is "通常" or "Space") ||
                      ((string)b.Tag == "CapsLock" && !editingSelectedInput && destinationInputTarget == null);
        string input = currentLayer == "通常" ? (string)b.Tag : currentLayer + "+" + (string)b.Tag;
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
        b.IsEnabled = !protectedLeftClick;
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
        b.ToolTip = protectedLeftClick ? "通常レイヤーでは変更できません" : assigned != null ? CreateAssignmentToolTip(assigned) : keyboardButton ? null : DefaultMouseToolTip((string)b.Tag);
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
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimationUsingKeyFrames
            {
                KeyFrames = [new EasingDoubleKeyFrame(.965, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(55))), new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)))]
            });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimationUsingKeyFrames
            {
                KeyFrames = [new EasingDoubleKeyFrame(.965, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(55))), new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)))]
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
    void UpdateStatus()
    {
        EngineStatus.Text = engine.Enabled ? "● エンジン稼働中" : "■ エンジン停止中";
        EngineStatus.Foreground = ThemeService.Brush(engine.Enabled ? "AccentTextBrush" : "DangerBrush");
    }
    void SetupTray()
    {
        tray.Text = "RELYR v" + DisplayVersion;
        defaultTrayIcon = CreateDefaultTrayIcon();
        tray.Icon = defaultTrayIcon;
        tray.Visible = true;
        RebuildTrayMenu();
        UpdateTrayNumber();
        tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowFromExternalLaunch);
    }
    internal static bool NativeTrayRegistrationAllowed(bool requestedSuppression)
    {
#if PRODUCTION_PUBLISH
        return !requestedSuppression;
#else
        // A development/test executable can live at dozens of temporary paths.
        // Never let one of those paths claim or duplicate the product tray ID.
        return false;
#endif
    }
    void UpdateTrayNumber()
    {
        if (!config.ShowDesktopNumberInTray)
        {
            numberedTrayIcon?.Dispose();
            numberedTrayIcon = null;
            tray.Icon = defaultTrayIcon;
            tray.Text = "RELYR v" + DisplayVersion;
            return;
        }
        try
        {
            int number = VirtualDesktopAccessor.CurrentNumber + 1;
            var icon = CreateDesktopNumberIcon(number);
            numberedTrayIcon?.Dispose();
            numberedTrayIcon = icon;
            tray.Icon = icon;
            tray.Text = $"RELYR v{DisplayVersion} — デスクトップ {number}";
        }
        catch { tray.Icon = defaultTrayIcon; }
    }
    internal static System.Drawing.Icon CreateDefaultTrayIcon()
    {
        try
        {
            string? executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable) && System.Drawing.Icon.ExtractAssociatedIcon(executable) is { } icon)
                return icon;
        }
        catch { }
        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }
    internal static System.Drawing.Icon CreateDesktopNumberIcon(int number)
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(System.Drawing.Color.Transparent);
            float fontSize = number < 10 ? 36 : number < 100 ? 25 : 17;
            using var font = new System.Drawing.Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
            using var format = new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center, LineAlignment = System.Drawing.StringAlignment.Center, FormatFlags = System.Drawing.StringFormatFlags.NoClip };
            g.DrawString(number.ToString(), font, System.Drawing.Brushes.White, new System.Drawing.RectangleF(-4, -6, 40, 42), format);
        }
        IntPtr hIcon = bitmap.GetHicon();
        try
        {
            return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
        }
        finally { DestroyIcon(hIcon); }
    }
    void RebuildTrayMenu()
    {
        var old = tray.ContextMenuStrip;
        var menu = TrayMenuTheme.Create(ThemeService.UsesDark);
        menu.Items.Add("表示", null, (_, _) => Dispatcher.BeginInvoke(ShowFromExternalLaunch));
        menu.Items.Add("有効 / 一時停止", null, (_, _) => Dispatcher.BeginInvoke(() => EngineToggle.IsChecked = !EngineToggle.IsChecked));
        var profiles = new System.Windows.Forms.ToolStripMenuItem("プロファイル");
        foreach (var profile in appliedConfig.Profiles.Where(p => config.Profiles.Any(x => x.Name == p.Name)))
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(profile.Name) { Checked = profile.Name == appliedConfig.ActiveProfile };
            item.Click += (_, _) => Dispatcher.BeginInvoke(() => SwitchProfile(profile.Name, true));
            profiles.DropDownItems.Add(item);
        }
        menu.Items.Add(profiles);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("押下キーをすべて解除", null, (_, _) => InputEngine.ReleaseAllDefensively());
        menu.Items.Add("再起動", null, (_, _) => Dispatcher.BeginInvoke(RequestApplicationRestart));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => RequestApplicationExit("tray-exit"));
        TrayMenuTheme.Apply(menu, ThemeService.UsesDark);
        tray.ContextMenuStrip = menu;
        old?.Dispose();
    }

    void ProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading)
            return;
        if (ProfileBox.SelectedItem is ComboBoxItem { Tag: string tag } && tag == NewProfileMenuTag)
        {
            loading = true;
            ProfileBox.SelectedItem = config.ActiveProfile;
            loading = false;
            Dispatcher.BeginInvoke(new Action(() => NewProfile_Click(ProfileBox, new RoutedEventArgs())));
            return;
        }
        if (ProfileBox.SelectedItem is not string name)
            return;
        suppressAutomaticProfileSwitchUntil = DateTime.UtcNow.AddSeconds(2);
        SwitchProfile(name, false);
    }
    void ProfileDropDownOpened(object sender, EventArgs e) => profileDropDownOpen = true;
    void ProfileDropDownClosed(object sender, EventArgs e)
    {
        profileDropDownOpen = false;
        suppressAutomaticProfileSwitchUntil = DateTime.UtcNow.AddSeconds(2);
    }
    void KeyboardLayoutChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || config == null)
            return;
        config.KeyboardLayout = KeyboardLayoutBox.SelectedIndex == 1 ? "US" : "JIS";
        appliedConfig.KeyboardLayout = config.KeyboardLayout;
        engine.UseUsLayout = config.KeyboardLayout == "US";
        BuildKeyboard();
        ColorButtons();
        var persisted = store.Load();
        persisted.KeyboardLayout = config.KeyboardLayout;
        store.Save(persisted);
        ShowInlineNotice(config.KeyboardLayout + "配列へ切り替えました");
    }

    void LightThemeToggle_Click(object sender, RoutedEventArgs e) => ApplyToolbarTheme(AppThemeMode.Light);
    void DarkThemeToggle_Click(object sender, RoutedEventArgs e) => ApplyToolbarTheme(AppThemeMode.Dark);

    void ApplyToolbarTheme(AppThemeMode mode)
    {
        if (config == null)
            return;
        config.ThemeMode = mode;
        if (appliedConfig != null)
            appliedConfig.ThemeMode = mode;
        var persisted = store.Load();
        persisted.ThemeMode = mode;
        store.Save(persisted);
        ThemeService.Apply(mode);
        UpdateThemeToolbarControls();
    }

    void UpdateThemeToolbarControls()
    {
        if (LightThemeToggle == null || DarkThemeToggle == null)
            return;
        LightThemeToggle.IsChecked = ThemeService.CurrentMode == AppThemeMode.Light;
        DarkThemeToggle.IsChecked = ThemeService.CurrentMode == AppThemeMode.Dark;
    }
    void MainContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (WorkspaceGrid == null || AssignmentPane == null || LowerInputRow == null)
            return;
        double gap = e.NewSize.Width < 1000 ? 12 : 16;
        double shellWidth = ActualWidth > 0 ? ActualWidth : e.NewSize.Width + LayerNavigationPane.Width;
        // The navigation contains short fixed labels, so it can be materially
        // narrower than the inspector without wrapping. Return that space to
        // the keyboard and Deck workspaces at every breakpoint.
        double navigationPaneWidth = shellWidth < 1000 ? 208 : shellWidth < 1500 ? 216 : 224;
        double inspectorPaneWidth = shellWidth < 1000 ? 232 : shellWidth < 1500 ? 252 : 272;
        LayerNavigationPane.Width = navigationPaneWidth;
        LayerNavigationColumn.Width = new GridLength(0);
        HeaderBrandColumn.Width = new GridLength(0);
        AssignmentPaneColumn.Width = new GridLength(inspectorPaneWidth);
        double centerWidth = Math.Max(360, e.NewSize.Width - inspectorPaneWidth - gap * 2);
        double mouseWidth = Math.Clamp(centerWidth * .23, 96, 240);
        MouseColumn.Width = new GridLength(mouseWidth);
        // Keep the mouse on the same visual key scale as the main keyboard.
        // Its portrait layout may make the lower row taller than the keypad.
        double lowerHeight = Math.Clamp(e.NewSize.Height * .36, 220, 340);
        // Fit the main and lower controls as one vertical composition.  The
        // A-key remains the scale reference for every lower and mouse key,
        // even on a short display, so the mouse can never be clipped below.
        double availableHeight = Math.Max(360, e.NewSize.Height - gap * 2);
        double fixedVerticalSpace = KeyboardSurfaceCard.Padding.Top + KeyboardSurfaceCard.Padding.Bottom
            + LowerInputGrid.Margin.Top + MouseHost.Margin.Top + MouseHost.Margin.Bottom;
        double widthScale = Math.Max(.35, (centerWidth - KeyboardSurfaceCard.Padding.Left - KeyboardSurfaceCard.Padding.Right) / Math.Max(1, KeyboardPanel.Width));
        double heightScale = Math.Max(.35, (availableHeight - fixedVerticalSpace) / (KeyboardPanel.Height + MousePanel.Height));
        double maxCommonScale = Math.Clamp(Math.Min(widthScale, heightScale), .35, MaximumKeyboardWorkspaceScale);
        KeyboardViewbox.Width = KeyboardPanel.Width * maxCommonScale;
        KeyboardViewbox.Height = KeyboardPanel.Height * maxCommonScale;
        KeyboardViewbox.MaxWidth = KeyboardViewbox.Width;
        KeyboardViewbox.MaxHeight = KeyboardViewbox.Height;
        double maximumMouseWidth = shellWidth <= 1500 ? 170 : MousePanel.Width * MaximumKeyboardWorkspaceScale;
        double maximumMouseScale = maximumMouseWidth / Math.Max(1, MousePanel.Width);
        double mouseScale = Math.Clamp((mouseWidth - 16) / Math.Max(1, MousePanel.Width), .35, maximumMouseScale);
        double secondaryHeight = Math.Min(lowerHeight - 12, Math.Max(80, (centerWidth - mouseWidth - 14) / 654 * 312));
        SecondaryKeyboardViewbox.Height = secondaryHeight;
        MouseHost.Width = MousePanel.Width * mouseScale;
        MouseHost.Height = MousePanel.Height * mouseScale;
        double mouseTotalHeight = MouseHost.Height + MouseHost.Margin.Top + MouseHost.Margin.Bottom;
        LowerInputRow.Height = new GridLength(Math.Max(secondaryHeight, mouseTotalHeight) + LowerInputGrid.Margin.Top);
        WorkspaceGrid.Margin = new Thickness(gap);
        AssignmentPane.Padding = new Thickness(gap);
        UpdateLayerButtonWidths();
        ScheduleLowerKeyboardScaleSync();
        ScheduleToolbarKeyboardAlignment();
    }

    void TopToolbarPane_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ToolbarPanel == null || ProfileBox == null || KeyboardLayoutBox == null)
            return;
        bool compact = e.NewSize.Width < 1040;
        bool narrow = e.NewSize.Width < 920;
        ProfileBox.Width = narrow ? 132 : compact ? 145 : 170;
        KeyboardLayoutBox.Width = narrow ? 70 : 84;
        ToolbarPanel.Margin = new Thickness(compact ? 6 : 14, 9.5, 0, -9.5);
        double compactCommandWidth = compact ? 34 : 44;
        foreach (var control in new System.Windows.Controls.Control[] { MultiSelectToggle, MultiCopyButton, MultiPasteButton, MultiDeleteButton })
        {
            control.Width = compactCommandWidth;
            control.MinWidth = compactCommandWidth;
            control.Margin = new Thickness(compact ? 1 : 3);
            control.Padding = new Thickness(0);
        }
        double themeCommandWidth = compact ? 34 : 40;
        foreach (var control in new System.Windows.Controls.Control[] { LightThemeToggle, DarkThemeToggle })
        {
            control.Width = themeCommandWidth;
            control.MinWidth = themeCommandWidth;
            control.Margin = new Thickness(0);
            control.Padding = new Thickness(0);
        }
        ProfileToolbarIcon.Margin = new Thickness(0, 0, compact ? 5 : 8, 0);
        KeyboardLayoutToolbarIcon.Margin = new Thickness(compact ? 10 : 18, 0, compact ? 5 : 8, 0);
        MultiSelectActionsPanel.Margin = new Thickness(compact ? 10 : 18, 0, 0, 0);
        ToolbarSaveButton.Width = compact ? 78 : 96;
        ToolbarSaveButton.MinWidth = ToolbarSaveButton.Width;
        ToolbarSaveButton.Margin = new Thickness(compact ? 8 : 18, 3, compact ? 6 : 12, 3);
        ScheduleToolbarKeyboardAlignment();
    }
    void KeyboardViewbox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScheduleLowerKeyboardScaleSync();
        ScheduleToolbarKeyboardAlignment();
    }
    void ScheduleToolbarKeyboardAlignment()
    {
        int generation = ++toolbarKeyboardAlignmentGeneration;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            if (generation == toolbarKeyboardAlignmentGeneration)
                AlignToolbarProfileWithEscapeKey();
        }));
    }
    void AlignToolbarProfileWithEscapeKey()
    {
        if (!editorUiInitialized || KeyboardWorkspace.Visibility != Visibility.Visible || !KeyboardViewbox.IsVisible)
            return;
        var escapeKey = InputButtons(KeyboardPanel).FirstOrDefault(button => string.Equals(button.Tag?.ToString(), "Esc", StringComparison.OrdinalIgnoreCase));
        if (escapeKey == null || escapeKey.ActualWidth <= 0 || ProfileToolbarIcon.ActualWidth <= 0)
            return;
        try
        {
            double escapeLeft = escapeKey.TranslatePoint(new System.Windows.Point(), TopToolbarPane).X;
            double profileIconLeft = ProfileToolbarIcon.TranslatePoint(new System.Windows.Point(), TopToolbarPane).X;
            double alignedLeftMargin = Math.Max(0, ToolbarContextPanel.Margin.Left + escapeLeft - profileIconLeft);
            if (Math.Abs(alignedLeftMargin - ToolbarContextPanel.Margin.Left) <= 0.25)
                return;
            ToolbarContextPanel.Margin = new Thickness(alignedLeftMargin, 0, 0, 0);
        }
        catch (InvalidOperationException)
        {
            // A resize can briefly detach the scaled keyboard visual. The next
            // layout pass schedules alignment again without disturbing input.
        }
    }
    void ScheduleLowerKeyboardScaleSync()
    {
        int generation = ++lowerKeyboardScaleSyncGeneration;
        int attempts = 0;
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (generation != lowerKeyboardScaleSyncGeneration || ++attempts > 6 || MatchLowerKeyboardScale())
                LayoutUpdated -= handler;
        };
        LayoutUpdated += handler;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            if (generation == lowerKeyboardScaleSyncGeneration)
                MatchLowerKeyboardScale();
        }));
    }
    bool MatchLowerKeyboardScale()
    {
        var referenceKey = KeyboardPanel?.Children.OfType<System.Windows.Controls.Button>().FirstOrDefault(button => Equals(button.Tag, "A"));
        var lowerKey = SecondaryKeyboardPanel?.Children.OfType<System.Windows.Controls.Button>().FirstOrDefault(button => Equals(button.Tag, "Insert"));
        if (referenceKey == null || lowerKey == null || referenceKey.ActualHeight <= 0 || lowerKey.ActualHeight <= 0 || SecondaryKeyboardViewbox.ActualHeight <= 0)
            return false;
        double mainHeight = referenceKey.TransformToAncestor(this).TransformBounds(new Rect(0, 0, referenceKey.ActualWidth, referenceKey.ActualHeight)).Height;
        double lowerHeight = lowerKey.TransformToAncestor(this).TransformBounds(new Rect(0, 0, lowerKey.ActualWidth, lowerKey.ActualHeight)).Height;
        if (mainHeight <= 0 || lowerHeight <= 0)
            return false;
        double difference = Math.Abs(mainHeight - lowerHeight);
        double matchedHeight = SecondaryKeyboardViewbox.ActualHeight;
        if (difference >= .05)
        {
            double correction = mainHeight / lowerHeight;
            double matchedWidth = SecondaryKeyboardViewbox.ActualWidth * correction;
            matchedHeight = SecondaryKeyboardViewbox.ActualHeight * correction;
            SecondaryKeyboardViewbox.Width = matchedWidth;
            SecondaryKeyboardViewbox.Height = matchedHeight;
        }
        double maximumMouseWidth = ActualWidth <= 1500 ? 170 : MousePanel.Width * MaximumKeyboardWorkspaceScale;
        double maximumMouseScale = maximumMouseWidth / Math.Max(1, MousePanel.Width);
        double mouseScale = Math.Min(mainHeight / SecondaryKeyHeight, Math.Min(maximumMouseScale, Math.Max(.35, (MouseColumn.ActualWidth - 16) / Math.Max(1, MousePanel.Width))));
        MouseHost.Width = MousePanel.Width * mouseScale;
        MouseHost.Height = MousePanel.Height * mouseScale;
        double mouseTotalHeight = MouseHost.Height + MouseHost.Margin.Top + MouseHost.Margin.Bottom;
        LowerInputRow.Height = new GridLength(Math.Max(matchedHeight, mouseTotalHeight) + LowerInputGrid.Margin.Top);
        return difference < .05;
    }
    void LayerButtonsPanel_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayerButtonWidths();
    void UpdateLayerButtonWidths()
    {
        if (LayerButtonsPanel == null)
            return;
        bool compact = MainContentGrid.ActualHeight > 0 && MainContentGrid.ActualHeight < 620;
        bool narrow = LayerNavigationPane.Width < 215;
        foreach (var category in new[] { KeyboardLayerCategory, MouseLayerCategory, WindowsLayerCategory })
            category.Margin = compact ? new Thickness(8, 4, 8, 2) : new Thickness(8, 6, 8, 3);
        foreach (var divider in new[] { KeyboardLayerDivider, MouseLayerDivider })
            divider.Margin = compact ? new Thickness(8, 4, 8, 2) : new Thickness(8, 6, 8, 2);
        foreach (var button in LayerButtonsPanel.Children.OfType<System.Windows.Controls.Button>())
        {
            button.Width = double.NaN;
            button.Height = compact ? 48 : 52;
            button.MinHeight = 48;
            button.Padding = new Thickness(8, 4, 8, 4);
            button.FontSize = 15;
            button.Margin = new Thickness(0, 1, 0, 1);
            button.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
            StackPanel? content = button.Content switch
            {
                StackPanel direct => direct,
                Grid grid => grid.Children.OfType<StackPanel>().FirstOrDefault(),
                _ => null
            };
            if (content != null)
            {
                content.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                foreach (var title in content.Children.OfType<TextBlock>().Take(1))
                {
                    title.TextWrapping = TextWrapping.NoWrap;
                    title.FontSize = 15;
                }
                foreach (var description in content.Children.OfType<TextBlock>().Skip(1))
                {
                    description.Visibility = Visibility.Visible;
                    description.FontSize = narrow ? 9.5 : 10.5;
                    description.TextWrapping = TextWrapping.NoWrap;
                }
            }
        }
    }
    void SwitchProfile(string name, bool refresh, bool persist = true)
    {
        if (!config.Profiles.Any(x => x.Name == name))
            return;
        bool preserveDeckSelection = deckManagementMode && MultiSelectToggle.IsChecked == true;
        string[] preservedDeckInputs = preserveDeckSelection ? [.. multiSelectedInputs] : [];
        bool changed = !config.ActiveProfile.Equals(name, StringComparison.OrdinalIgnoreCase) || !appliedConfig.ActiveProfile.Equals(name, StringComparison.OrdinalIgnoreCase);
        suppressAutomaticProfileSwitchUntil = DateTime.UtcNow.AddSeconds(2);
        explicitProfileSwitchProcess = ConditionMatcher.ForegroundProcessName();
        automaticProfileReturnName = "";
        string? selectedDeckGroup = deckManagementMode && selectedDeckLayout?.ProfileSwitchEnabled == true ? selectedDeckLayout.ProfileGroupId : null;
        config.ActiveProfile = name;
        if (appliedConfig.Profiles.Any(x => x.Name == name))
        {
            appliedConfig.ActiveProfile = name;
            if (persist)
            {
                var persisted = store.Load();
                if (persisted.Profiles.Any(x => x.Name == name))
                {
                    persisted.ActiveProfile = name;
                    store.Save(persisted);
                }
            }
        }
        ClearSelectedInput();
        if (selectedDeckGroup != null)
        {
            var profile = DeckPanelLayout.ActiveProfile(config);
            var variant = config.DeckLayouts.FirstOrDefault(layout => layout.ProfileSwitchEnabled
                && layout.ProfileGroupId.Equals(selectedDeckGroup, StringComparison.OrdinalIgnoreCase)
                && profile != null && layout.ProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
            if (variant != null)
                EditDeckLayout(variant);
        }
        if (preserveDeckSelection)
        {
            int visibleSlots = selectedDeckLayout == null ? 0 : DeckPanelLayout.VisibleSlotCount(selectedDeckLayout);
            multiSelectedInputs.Clear();
            foreach (string input in preservedDeckInputs.Where(input => DeckPanelLayout.SlotNumber(input) is int slot && slot >= 1 && slot <= visibleSlots))
                multiSelectedInputs.Add(input);
            MultiSelectToggle.IsChecked = true;
            UpdateMultiSelectControls();
            ColorDeckManagementButtons();
        }
        if (IsMouseLayerBlockedByDirectGesture(config.Profiles, CurrentProfile.Name, currentLayer))
            currentLayer = "通常";
        if (refresh)
            RefreshProfiles();
        UpdateLayerButtons();
        UpdateStatus();
        RebuildTrayMenu();
        if (runtimeRole == RuntimeRole.UiHost && changed)
            IpcRuntime.RequestReload();
        if (changed)
        {
            OverlayService.RefreshDeckPanelForProfileChange();
            ShowProfileOverlay(name);
        }
    }
    AppConfig DeckOverlayConfig()
    {
        var snapshot = store.Clone(config);
        snapshot.ActiveProfile = appliedConfig.ActiveProfile;
        // The active profile is runtime state, but Deck editing must use one
        // shared model. Cloning the layouts here made the editor and the live
        // overlay modify different objects until the process was rebuilt.
        snapshot.DeckLayouts = config.DeckLayouts;
        snapshot.SharedDeckMappings = config.SharedDeckMappings;
        return snapshot;
    }
    void ShowProfileOverlay(string profileName)
    {
        if (!appliedConfig.ShowProfileSwitchOverlay)
            return;
        if (profileOverlay?.IsVisible == true && lastProfileOverlayName.Equals(profileName, StringComparison.OrdinalIgnoreCase))
            return;
        profileOverlay?.Close();
        lastProfileOverlayName = profileName;
        var overlay = new ProfileSwitchOverlay(profileName);
        profileOverlay = overlay;
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(profileOverlay, overlay))
                profileOverlay = null;
            if (lastProfileOverlayName.Equals(profileName, StringComparison.OrdinalIgnoreCase))
                lastProfileOverlayName = "";
        };
        overlay.Show();
    }
    void AutoSwitchProfile()
    {
        bool needsForegroundProcess = appliedConfig.InputDisabledApplications.Count > 0
            || appliedConfig.Profiles.Skip(1).Any(x => x.AutoSwitchEnabled);
        string process = needsForegroundProcess ? ConditionMatcher.ForegroundProcessName() : "";
        RefreshInputProcessingSuppression(process);
        if (profileDropDownOpen || DateTime.UtcNow < suppressAutomaticProfileSwitchUntil)
        {
            LogAutomaticProfileSwitch($"paused dropdown={profileDropDownOpen} suppressUntil={suppressAutomaticProfileSwitchUntil:O}");
            return;
        }
        if (!appliedConfig.Profiles.Skip(1).Any(x => x.AutoSwitchEnabled))
        {
            bool changed = TryApplyAutomaticProfile(config, appliedConfig, config.ActiveProfile, engine.TryPrepareForProfileChange);
            LogAutomaticProfileSwitch($"no-enabled-profiles editor={config.ActiveProfile} runtime={appliedConfig.ActiveProfile} changed={changed}");
            if (changed)
            {
                if (runtimeRole == RuntimeRole.UiHost)
                    IpcRuntime.RequestReload();
                RebuildTrayMenu();
                OverlayService.RefreshDeckPanelForProfileChange();
                ShowProfileOverlay(appliedConfig.ActiveProfile);
            }
            return;
        }
        string[] processes = string.IsNullOrWhiteSpace(process) ? [] : [process];
        var (Target, ReturnProfile) = ResolveAutomaticProfileTarget(appliedConfig.Profiles, appliedConfig.ActiveProfile, automaticProfileReturnName, processes, false);
        int requiredSamples = AutomaticProfileRequiredSamples(appliedConfig.Profiles, Target);
        bool stable = ObserveAutomaticProfileCandidate(Target, requiredSamples);
        LogAutomaticProfileSwitch($"observe foreground={process} candidate={Target} samples={automaticProfileCandidateSamples}/{requiredSamples} stable={stable} runtime={appliedConfig.ActiveProfile} return={automaticProfileReturnName}");
        if (!stable)
            return;
        ResetAutomaticProfileCandidate();
        if (ShouldKeepExplicitProfile(explicitProfileSwitchProcess, process, false))
        {
            LogAutomaticProfileSwitch($"manual-hold original={explicitProfileSwitchProcess} current={process}");
            return;
        }
        explicitProfileSwitchProcess = "";
        string before = appliedConfig.ActiveProfile;
        if (TryApplyAutomaticProfileForProcesses(processes, false, out string target))
        {
            LogAutomaticProfileSwitch($"applied before={before} target={target} runtime={appliedConfig.ActiveProfile} return={automaticProfileReturnName}");
        }
        else
            LogAutomaticProfileSwitch($"not-applied before={before} target={target} runtime={appliedConfig.ActiveProfile} captured={engine.HasCapturedPhysicalInput}");
    }

    void RefreshInputProcessingSuppression(string? foregroundProcess = null)
    {
        if (appliedConfig.InputDisabledApplications.Count == 0)
        {
            Volatile.Write(ref inputProcessingSuppressedForForeground, false);
            return;
        }
        foregroundProcess ??= ConditionMatcher.ForegroundProcessName();
        Volatile.Write(ref inputProcessingSuppressedForForeground,
            IsInputProcessingDisabledForApplication(appliedConfig.InputDisabledApplications, foregroundProcess));
    }

    internal static bool IsInputProcessingDisabledForApplication(IEnumerable<string> applications, string foregroundProcess)
        => !string.IsNullOrWhiteSpace(foregroundProcess)
            && applications.Any(application => ConditionMatcher.Matches(application, foregroundProcess));
    bool TryApplyAutomaticProfileForProcesses(IReadOnlyCollection<string> processes, bool cursorOverTaskbar, out string target)
    {
        if (!TryResolveAndApplyAutomaticProfile(config, appliedConfig, processes, cursorOverTaskbar, engine.TryPrepareForProfileChange, ref automaticProfileReturnName, out target))
            return false;
        RebuildTrayMenu();
        if (runtimeRole == RuntimeRole.UiHost)
            IpcRuntime.RequestReload();
        OverlayService.RefreshDeckPanelForProfileChange();
        ShowProfileOverlay(target);
        return true;
    }
    void LogAutomaticProfileSwitch(string message)
    {
        if (string.IsNullOrWhiteSpace(automaticProfileDiagnosticLog))
            return;
        try
        {
            string? directory = Path.GetDirectoryName(automaticProfileDiagnosticLog);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.AppendAllText(automaticProfileDiagnosticLog, $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }
    void ResetAutomaticProfileCandidate()
    {
        automaticProfileCandidateSignature = "";
        automaticProfileCandidateSamples = 0;
    }
    bool ObserveAutomaticProfileCandidate(string signature, int requiredSamples)
    {
        if (!automaticProfileCandidateSignature.Equals(signature, StringComparison.OrdinalIgnoreCase))
        {
            automaticProfileCandidateSignature = signature;
            automaticProfileCandidateSamples = 1;
            return requiredSamples <= 1;
        }
        automaticProfileCandidateSamples = Math.Min(requiredSamples, automaticProfileCandidateSamples + 1);
        return automaticProfileCandidateSamples >= requiredSamples;
    }
    internal static int AutomaticProfileRequiredSamples(IEnumerable<Profile> profiles, string target)
        => profiles.FirstOrDefault(profile => profile.Name.Equals(target, StringComparison.OrdinalIgnoreCase))?.AutoSwitchEnabled == true ? 1 : 2;
    internal static bool TryApplyAutomaticProfile(AppConfig editingConfig, AppConfig runtimeConfig, string targetName, Func<bool> prepare)
    {
        if (runtimeConfig.ActiveProfile == targetName || !runtimeConfig.Profiles.Any(x => x.Name == targetName))
            return false;
        if (!prepare())
            return false;
        return ApplyAutomaticProfile(editingConfig, runtimeConfig, targetName);
    }
    internal static bool TryResolveAndApplyAutomaticProfile(AppConfig editingConfig, AppConfig runtimeConfig, IReadOnlyCollection<string> processes, bool cursorOverTaskbar, Func<bool> prepare, ref string returnProfile, out string target)
    {
        var (Target, ReturnProfile) = ResolveAutomaticProfileTarget(runtimeConfig.Profiles, runtimeConfig.ActiveProfile, returnProfile, processes, cursorOverTaskbar);
        target = Target;
        if (runtimeConfig.ActiveProfile.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            returnProfile = ReturnProfile;
            return false;
        }
        if (!TryApplyAutomaticProfile(editingConfig, runtimeConfig, target, prepare))
            return false;
        returnProfile = ReturnProfile;
        return true;
    }
    internal static bool ApplyAutomaticProfile(AppConfig editingConfig, AppConfig runtimeConfig, string targetName)
    {
        if (runtimeConfig.ActiveProfile == targetName || !runtimeConfig.Profiles.Any(x => x.Name == targetName))
            return false;
        // Automatic switching is a runtime concern. Never move the profile that the
        // user is currently editing, even when the cursor or virtual desktop changes.
        runtimeConfig.ActiveProfile = targetName;
        return true;
    }
    internal static Profile SelectAutomaticProfile(IReadOnlyList<Profile> profiles, string process) => profiles.Skip(1).FirstOrDefault(x => x.AutoSwitchEnabled && x.AutoSwitchApplications.Any(app => ConditionMatcher.Matches(app, process))) ?? profiles[0];
    internal static string SelectAutomaticProfileNameForLocation(IReadOnlyList<Profile> profiles, string currentProfile, string process, bool cursorOverTaskbar) => cursorOverTaskbar && profiles.Any(x => x.Name == currentProfile) ? currentProfile : SelectAutomaticProfile(profiles, process).Name;
    internal static (string Target, string ReturnProfile) ResolveAutomaticProfileTarget(IReadOnlyList<Profile> profiles, string currentProfile, string returnProfile, string process, bool cursorOverTaskbar)
        => ResolveAutomaticProfileTarget(profiles, currentProfile, returnProfile, string.IsNullOrWhiteSpace(process) ? [] : [process], cursorOverTaskbar);
    internal static (string Target, string ReturnProfile) ResolveAutomaticProfileTarget(IReadOnlyList<Profile> profiles, string currentProfile, string returnProfile, IReadOnlyCollection<string> processes, bool cursorOverTaskbar)
    {
        if (cursorOverTaskbar)
            return (currentProfile, returnProfile);
        string defaultProfile = profiles[0].Name;
        var matched = profiles.Skip(1).FirstOrDefault(x => x.AutoSwitchEnabled && x.AutoSwitchApplications.Any(app => processes.Any(process => ConditionMatcher.Matches(app, process))));
        if (matched != null)
        {
            string returnTarget = ValidManualReturnProfile(profiles, returnProfile)
                ?? ValidManualReturnProfile(profiles, currentProfile)
                ?? defaultProfile;
            return (matched.Name, returnTarget);
        }
        if (!string.IsNullOrWhiteSpace(returnProfile) && profiles.Any(x => x.Name == returnProfile))
            return (returnProfile, "");
        // An automatically selected profile is not a safe fallback: it may
        // belong to an app on a different virtual desktop. Return to the
        // manually selected non-automatic profile, or the standard profile.
        return (ValidManualReturnProfile(profiles, currentProfile) ?? defaultProfile, "");
    }
    static string? ValidManualReturnProfile(IReadOnlyList<Profile> profiles, string profileName)
        => profiles.FirstOrDefault(x => x.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase) && !x.AutoSwitchEnabled)?.Name;
    internal static bool ShouldKeepExplicitProfile(string originalProcess, string currentProcess, bool cursorOverTaskbar) => cursorOverTaskbar || (!string.IsNullOrWhiteSpace(originalProcess) && ConditionMatcher.Matches(originalProcess, currentProcess));
    internal static bool IsOwnProcess(string process, string? executablePath = null)
        => !string.IsNullOrWhiteSpace(process)
            && ConditionMatcher.Matches(Path.GetFileNameWithoutExtension(executablePath ?? Environment.ProcessPath ?? "RELYR"), process);
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
    static GestureDefinition CloneGesture(GestureDefinition x) => new() { Name = x.Name, UpKind = x.UpKind, UpValue = x.UpValue, DownKind = x.DownKind, DownValue = x.DownValue, LeftKind = x.LeftKind, LeftValue = x.LeftValue, RightKind = x.RightKind, RightValue = x.RightValue, CenterKind = x.CenterKind, CenterValue = x.CenterValue };
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
        hasUnsavedChanges = false;
        UpdateUnsavedChangesIndicator();
        if (deckManagementMode && appliedConfig.Profiles.Any(profile => profile.Name.Equals(runtimeProfileBeforeSave, StringComparison.OrdinalIgnoreCase)))
            appliedConfig.ActiveProfile = runtimeProfileBeforeSave;
        engine.SpaceHoldRepeatEnabled = config.SpaceHoldRepeatEnabled;
        engine.SpaceHoldRepeatDelayMs = config.SpaceHoldRepeatDelayMs;
        engine.GestureThresholdPixels = config.GestureThresholdPixels;
        engine.LockCursorDuringGesture = config.LockCursorDuringGesture;
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
        ShowKeyboardWorkspace();
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

    internal void OpenSettingsFrom(Window owner, string? category = null)
    {
        if (settingsWindow is { IsVisible: true } existing)
        {
            if (category != null)
                existing.SelectCategory(category);
            existing.Activate();
            return;
        }

        var window = new SettingsWindow(config, lastUpdateCheck) { Owner = owner };
        settingsWindow = window;
        if (category != null)
            window.SelectCategory(category);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(settingsWindow, window))
                settingsWindow = null;
            if (window.Accepted)
                ApplySettingsWindowResult(window);
        };
        window.Show();
        window.Activate();
    }

    void ApplySettingsWindowResult(SettingsWindow window)
    {
        if (!window.Accepted)
            return;
        if (window.CapsRemapChanged)
        {
            LastInput.Text = "CapsLock設定を変更しました — Windows再起動後に反映されます";
            LastInput.Foreground = ThemeService.Brush("WarningBrush");
        }
        if (window.ResetConfig is { } reset)
        {
            ApplyCompleteConfig(reset, "すべての設定を初期状態へ戻しました");
            if (window.ResetNeedsRestart)
                SettingsWindow.PromptForWindowsRestart(this, false);
            return;
        }
        if (window.ImportedConfig is { } imported)
        {
            ApplyCompleteConfig(imported, "設定をインポートして反映しました");
            if (window.ImportedCapsLockNeedsRestart)
                SettingsWindow.PromptForWindowsRestart(this, window.ImportedCapsLockEnabled);
            return;
        }
        bool previousUpdateSetting = config.CheckForUpdates;
        try
        {
            ApplySettingsWindowValues(window);
            OverlayService.RefreshDeckPanel();
            CopyApplicationOptions(config, appliedConfig);
            var persisted = store.Load();
            CopyApplicationOptions(config, persisted);
            store.Save(persisted);
            if (runtimeRole == RuntimeRole.UiHost)
                IpcRuntime.RequestReload();

            ThemeService.Apply(config.ThemeMode);
            ApplyArchiveWatcherConfiguration();
            UpdateTrayNumber();
            ApplyUpdateCheckPreference(previousUpdateSetting);
            if (config.AutoSave)
                SaveAndApply("自動保存をオンにし、現在の変更を保存・反映しました");
            else
            {
                LastInput.Text = "アプリ設定を保存しました — 自動保存はオフです";
                LastInput.Foreground = ThemeService.Brush("AccentTextBrush");
            }
        }
        catch (Exception ex) { WpfMessageBox.Show("設定を保存できません: " + ex.Message); }
        finally { loading = true; AutoSaveToggle.IsChecked = config.AutoSave; UpdateAutoSaveToggleText(); loading = false; }
    }
    void ApplySettingsWindowValues(SettingsWindow window)
    {
        if (window.StartWithWindowsChanged)
            StartupService.SetEnabled(window.StartWithWindows);
        config.StartWithWindows = window.StartWithWindows;
        config.AutoExtractDesktopArchives = window.AutoExtract;
        config.ShowArchiveExtractionOverlay = window.ShowArchiveExtractionOverlay;
        config.ArchiveWatchFolder = window.ArchiveWatchFolder;
        config.ArchiveDestinationFolder = window.ArchiveDestinationFolder;
        config.DeleteArchiveAfterExtract = window.DeleteAfterExtract;
        config.ShowDesktopNumberInTray = window.ShowDesktopNumberInTray;
        config.CheckForUpdates = window.CheckForUpdates;
        config.ShowProfileSwitchOverlay = window.ShowProfileSwitchOverlay;
        config.WindowActionTarget = window.SelectedWindowActionTarget;
        config.ThemeMode = window.SelectedThemeMode;
        config.UiAnimationsEnabled = window.UiAnimationsEnabled;
        UiMotionService.Apply(config.UiAnimationsEnabled);
        config.AutoSave = window.AutoSave;
        config.SpaceHoldRepeatEnabled = window.SpaceHoldRepeat;
        config.InputDisabledApplications = [.. window.InputDisabledApplications];
        config.SpaceHoldRepeatDelayMs = window.SpaceHoldRepeatDelay;
        config.GestureThresholdPixels = window.GestureThreshold;
        config.LockCursorDuringGesture = window.LockCursorDuringGesture;
        config.ClockBackgroundMode = window.SelectedClockBackgroundMode;
        config.ClockDisplayMode = window.SelectedClockDisplayMode;
        config.ClockBackgroundImage = window.ClockBackgroundImage;
        config.ClockSolidColor = window.ClockSolidColor;
        config.ClockShowOnAllMonitors = window.ClockShowOnAllMonitors;
        config.InputPanelOpacityPercent = window.InputPanelOpacityPercent;
        config.DeckAfterActionBehavior = window.DeckAfterActionBehavior;
        config.DeckPointerLeaveBehavior = window.DeckPointerLeaveBehavior;
        engine.SpaceHoldRepeatEnabled = config.SpaceHoldRepeatEnabled;
        engine.SpaceHoldRepeatDelayMs = config.SpaceHoldRepeatDelayMs;
        engine.GestureThresholdPixels = config.GestureThresholdPixels;
        engine.LockCursorDuringGesture = config.LockCursorDuringGesture;
        RefreshInputProcessingSuppression();
        OverlayService.RefreshDeckPanel();
    }
    static void CopyApplicationOptions(AppConfig source, AppConfig destination)
    {
        destination.StartWithWindows = source.StartWithWindows;
        destination.AutoExtractDesktopArchives = source.AutoExtractDesktopArchives;
        destination.ShowArchiveExtractionOverlay = source.ShowArchiveExtractionOverlay;
        destination.ArchiveWatchFolder = source.ArchiveWatchFolder;
        destination.ArchiveDestinationFolder = source.ArchiveDestinationFolder;
        destination.DeleteArchiveAfterExtract = source.DeleteArchiveAfterExtract;
        destination.ShowDesktopNumberInTray = source.ShowDesktopNumberInTray;
        destination.CheckForUpdates = source.CheckForUpdates;
        destination.ShowProfileSwitchOverlay = source.ShowProfileSwitchOverlay;
        destination.DismissedUpdateVersion = source.DismissedUpdateVersion;
        destination.PendingUpdateNotesVersion = source.PendingUpdateNotesVersion;
        destination.PendingUpdateNotesBody = source.PendingUpdateNotesBody;
        destination.LastShownUpdateNotesVersion = source.LastShownUpdateNotesVersion;
        destination.WindowActionTarget = source.WindowActionTarget;
        destination.ThemeMode = source.ThemeMode;
        destination.UiAnimationsEnabled = source.UiAnimationsEnabled;
        destination.AutoSave = source.AutoSave;
        destination.SpaceHoldRepeatEnabled = source.SpaceHoldRepeatEnabled;
        destination.InputDisabledApplications = [.. source.InputDisabledApplications];
        destination.SpaceHoldRepeatDelayMs = source.SpaceHoldRepeatDelayMs;
        destination.GestureThresholdPixels = source.GestureThresholdPixels;
        destination.LockCursorDuringGesture = source.LockCursorDuringGesture;
        destination.ClockBackgroundMode = source.ClockBackgroundMode;
        destination.ClockDisplayMode = source.ClockDisplayMode;
        destination.ClockBackgroundImage = source.ClockBackgroundImage;
        destination.ClockSolidColor = source.ClockSolidColor;
        destination.ClockShowOnAllMonitors = source.ClockShowOnAllMonitors;
        destination.InputPanelOpacityPercent = source.InputPanelOpacityPercent;
        destination.DeckAfterActionBehavior = source.DeckAfterActionBehavior;
        destination.DeckPointerLeaveBehavior = source.DeckPointerLeaveBehavior;
    }
    void ApplyCompleteConfig(AppConfig value, string message)
    {
        ClearPendingActions();
        config = value;
        UiMotionService.Apply(config.UiAnimationsEnabled);
        store.Save(config);
        appliedConfig = store.Clone(config);
        bool pending = LegacyKeyRemapService.IsRestartStillPending(config);
        capsLockRemapped = pending ? config.CapsLockRemapEffectiveBeforeRestart : LegacyKeyRemapService.HasCapsLockToF13();
        engine.TreatF13AsCapsLock = capsLockRemapped;
        engine.UseUsLayout = config.KeyboardLayout == "US";
        engine.SpaceHoldRepeatEnabled = config.SpaceHoldRepeatEnabled;
        engine.SpaceHoldRepeatDelayMs = config.SpaceHoldRepeatDelayMs;
        engine.GestureThresholdPixels = config.GestureThresholdPixels;
        engine.LockCursorDuringGesture = config.LockCursorDuringGesture;
        RefreshInputProcessingSuppression();
        engine.Enabled = engineStarted && config.EngineEnabled;
        loading = true;
        KeyboardLayoutBox.SelectedIndex = config.KeyboardLayout == "US" ? 1 : 0;
        AutoSaveToggle.IsChecked = config.AutoSave;
        EngineToggle.IsChecked = engine.Enabled;
        loading = false;
        currentLayer = "通常";
        ClearSelectedInput();
        ThemeService.Apply(config.ThemeMode);
        BuildKeyboard();
        RefreshProfiles();
        UpdateLayerButtons();
        ColorButtons();
        UpdateAutoSaveToggleText();
        ApplyArchiveWatcherConfiguration();
        UpdateTrayNumber();
        UpdateStatus();
        RebuildTrayMenu();
        ApplyUpdateCheckPreference(false);
        LastInput.Text = message;
        LastInput.Foreground = ThemeService.Brush("AccentTextBrush");
    }
    public void ShowFirstRunSetup()
    {
        if (!NeedsFirstRunSetup)
            return;
        EnsureEditorUiInitialized();
        var setup = new SetupWindow { Owner = this };
        if (setup.ShowDialog() == true)
        {
            config.ActiveProfile = "標準";
            config.FirstRunCompleted = setup.DoNotShowAgain;
            store.Save(config);
            RefreshProfiles();
            RebuildTrayMenu();
        }
    }
    public void ShowFromExternalLaunch()
    {
        EnsureEditorUiInitialized();
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        ConstrainToCurrentWorkArea();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        EnsureUpdateCheckStarted();
    }

    void ApplyArchiveWatcherConfiguration()
    {
        ArchiveAutomationState.Set(config.AutoExtractDesktopArchives);
        if (!OwnsArchiveAutomation(runtimeRole))
        {
            // The medium UI process is the sole archive owner. Running the same
            // FileSystemWatcher in the elevated helper races two extractions of
            // one archive and can create a second "(2)" destination folder.
            archiveWatcher.Dispose();
            return;
        }
        archiveWatcher.Apply(config);
        if (!config.ShowArchiveExtractionOverlay || !config.AutoExtractDesktopArchives)
            archiveProgressOverlay?.HideImmediately();
    }

    internal static bool OwnsArchiveAutomation(RuntimeRole role)
        => role != RuntimeRole.ElevatedHelper;

    void HandleArchiveActivity(ArchiveActivity activity)
    {
        try
        {
            if (runtimeRole == RuntimeRole.ElevatedHelper || !config.ShowArchiveExtractionOverlay)
            {
                archiveProgressOverlay?.HideImmediately();
                return;
            }
            archiveProgressOverlay ??= new ArchiveProgressOverlay();
            archiveProgressOverlay.ShowActivity(activity);
        }
        catch (Exception error)
        {
            LifecycleDiagnostics.Write("archive-progress-overlay-failed", error.ToString());
            try { archiveProgressOverlay?.CloseForProcessExit(); } catch { }
            archiveProgressOverlay = null;
        }
    }

    internal void ToggleAutoExtractFromAction()
    {
        if (runtimeRole == RuntimeRole.ElevatedHelper)
        {
            _ = OverlayUiBridge.RequestShow(ActionCatalog.ToggleAutoExtractAction);
            return;
        }

        bool previous = config.AutoExtractDesktopArchives;
        bool enabled = !previous;
        try
        {
            config.AutoExtractDesktopArchives = enabled;
            appliedConfig.AutoExtractDesktopArchives = enabled;
            store.Save(config);
            ApplyArchiveWatcherConfiguration();
            if (runtimeRole == RuntimeRole.UiHost)
                IpcRuntime.RequestReload();
            LastInput.Text = enabled ? "自動解凍をオンにしました" : "自動解凍をオフにしました";
            LastInput.Foreground = ThemeService.Brush("AccentTextBrush");
        }
        catch (Exception error)
        {
            config.AutoExtractDesktopArchives = previous;
            appliedConfig.AutoExtractDesktopArchives = previous;
            try { ApplyArchiveWatcherConfiguration(); } catch { }
            LifecycleDiagnostics.Write("auto-extract-toggle-failed", error.ToString());
            ShowInlineError("自動解凍の設定を保存できませんでした");
        }
    }
    // アップデートの定期確認、通知表示、検証済みインストーラーの起動をまとめて管理する。
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
    void Window_Closing(object? s, CancelEventArgs e)
    {
        if (!allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        SystemEvents.UserPreferenceChanged -= WindowsThemeChanged;
        SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        ThemeService.ThemeChanged -= AppThemeChanged;
        MacroPlayer.PlaybackFinished -= MacroPlaybackFinished;
        updateCancellation.Cancel();
        profileOverlay?.Close();
        archiveProgressOverlay?.CloseForProcessExit();
        OverlayService.Shutdown();
        trayNumberTimer.Stop();
        profileSwitchTimer.Stop();
        autoSaveTimer.Stop();
        engine.Enabled = false;
        ClearPendingActions();
        actionQueue.CompleteAdding();
        dragActionQueue.CompleteAdding();
        taskbarClickReplayQueue.CompleteAdding();
        try
        {
            Task.WaitAll([actionWorker, dragActionWorker, taskbarClickReplayWorker], 2000);
        }
        catch { }
        InputEngine.ReleaseAllDefensively();
        engine.Dispose();
        RemoveTrayIconForImmediateExit();
        archiveWatcher.Dispose();
        updateCancellation.Dispose();
    }
    internal void RemoveTrayIconForImmediateExit()
    {
        if (Interlocked.Exchange(ref trayDisposed, 1) != 0)
            return;
        try
        {
            tray.Visible = false;
        }
        catch { }
        try
        {
            tray.Dispose();
        }
        catch { }
        try
        {
            numberedTrayIcon?.Dispose();
        }
        catch { }
        try
        {
            defaultTrayIcon?.Dispose();
        }
        catch { }
        numberedTrayIcon = null;
        defaultTrayIcon = null;
    }
    internal void PrepareVisualsForImmediateExit()
    {
        allowClose = true;
        try
        {
            profileOverlay?.HideImmediatelyForProcessExit();
        }
        catch { }
        try
        {
            archiveProgressOverlay?.HideImmediately();
        }
        catch { }
        try
        {
            Hide();
        }
        catch { }
        RemoveTrayIconForImmediateExit();
    }
    public void PrepareForSystemShutdown()
    {
        allowClose = true;
        engine.Enabled = false;
        ClearPendingActions();
        InputEngine.ReleaseAllDefensively();
        engine.Dispose();
        archiveProgressOverlay?.CloseForProcessExit();
        archiveWatcher.Dispose();
    }
    public void ResetInputStateForSessionTransition()
    {
        activeInputMappings.Clear();
        activeLayerMappings.Clear();
        ClearPendingActions();
        engine.ResetForSessionTransition();
        InputEngine.ReleaseAllDefensively();
    }
    public void RequestApplicationExit(string reason = "application-exit")
    {
        App.MarkShutdownInProgress(reason);
        if (Interlocked.Exchange(ref exitRequested, 1) != 0)
            return;
        allowClose = true;
        // WPF/WinFormsの後処理が停止しても、トレイ終了後にプロセスだけを残さない。
        App.ArmForcedProcessExit(TimeSpan.FromSeconds(3));
        try
        {
            Close();
            InputEngine.ReleaseAllDefensively();
            App.ExitImmediately(0);
        }
        catch
        {
            App.ExitImmediately(1);
        }
    }
    void UpdateUnsavedChangesIndicator()
    {
        if (UnsavedChangesIndicator == null)
            return;
        UnsavedChangesIndicator.Visibility = config != null && !config.AutoSave && hasUnsavedChanges
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
    void RequestApplicationRestart()
    {
        if (Interlocked.Exchange(ref restartRequested, 1) != 0)
            return;
        try
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("実行ファイルの場所を確認できません。");
            var start = new ProcessStartInfo(executable) { UseShellExecute = true };
            // Re-enter through the registered elevated launcher after this
            // process has fully released its hooks.  This is the same ownership
            // path used by a normal Windows/taskbar launch.
            foreach (string argument in App.RestartChildArguments(Environment.ProcessId))
                start.ArgumentList.Add(argument);
            if (Process.Start(start) == null)
                throw new InvalidOperationException("再起動プロセスを開始できません。");
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref restartRequested, 0);
            AppDialog.Show(this, "RELYRを再起動できませんでした。\n\n" + ex.Message, "再起動できません", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        RequestApplicationExit("restart");
    }
    string? PromptText(string title, string label, string initial)
    {
        var dialog = new Window { Title = title, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Width = 460, Height = 220, ResizeMode = ResizeMode.NoResize, Background = ThemeService.Brush("SurfaceBackground"), Foreground = ThemeService.Brush("PrimaryText"), ShowInTaskbar = false };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 9) });
        var box = new TextBox { Text = initial, FontSize = 15, Height = 40, Padding = new Thickness(12, 0, 12, 0), Background = ThemeService.Brush("InputBackground"), Foreground = ThemeService.Brush("PrimaryText"), BorderBrush = ThemeService.Brush("BorderBrush"), VerticalContentAlignment = VerticalAlignment.Center };
        panel.Children.Add(box);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new System.Windows.Controls.Button { Content = "キャンセル", Width = 112, Height = 40, Margin = new Thickness(6, 0, 0, 0), Style = (Style)System.Windows.Application.Current.FindResource("AppButtonStyle") };
        var ok = new System.Windows.Controls.Button { Content = "決定", Width = 112, Height = 40, Margin = new Thickness(6, 0, 0, 0), Style = (Style)System.Windows.Application.Current.FindResource("AccentAppButtonStyle"), IsDefault = true };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        ok.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        FollowWindowsTitleBarTheme(dialog);
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }
    Profile? SelectProfile(string title, bool allowNoCopy) => SelectProfile(title, allowNoCopy, out _);
    Profile? SelectProfile(string title, bool allowNoCopy, out bool cancelled)
    {
        bool noCopy = false;
        var dialog = new Window { Title = title, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Width = 460, Height = 400, Background = ThemeService.Brush("SurfaceBackground"), Foreground = ThemeService.Brush("PrimaryText"), ShowInTaskbar = false };
        var grid = new Grid { Margin = new Thickness(22) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = allowNoCopy ? "コピー元を選ぶか、空のプロファイルとして作成してください。" : "コピー元のプロファイルを選択してください。", Foreground = ThemeService.Brush("SecondaryText"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var list = new ListBox { ItemsSource = config.Profiles, DisplayMemberPath = "Name", Background = ThemeService.Brush("CardBackground"), Foreground = ThemeService.Brush("PrimaryText"), BorderBrush = ThemeService.Brush("BorderBrush"), Padding = new Thickness(6) };
        Grid.SetRow(list, 1);
        grid.Children.Add(list);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new System.Windows.Controls.Button { Content = allowNoCopy ? "コピーせず作成" : "キャンセル", Height = 40, Margin = new Thickness(6, 0, 0, 0), Style = (Style)System.Windows.Application.Current.FindResource("AppButtonStyle") };
        var ok = new System.Windows.Controls.Button { Content = "選択してコピー", Height = 40, Margin = new Thickness(6, 0, 0, 0), Style = (Style)System.Windows.Application.Current.FindResource("AccentAppButtonStyle") };
        cancel.Click += (_, _) => { noCopy = allowNoCopy; dialog.DialogResult = false; };
        ok.Click += (_, _) => { if (list.SelectedItem != null) dialog.DialogResult = true; };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);
        dialog.Content = grid;
        FollowWindowsTitleBarTheme(dialog);
        bool? result = dialog.ShowDialog();
        cancelled = result is null || (result == false && !noCopy);
        return result == true ? list.SelectedItem as Profile : null;
    }
    string? SelectRunningApplication() => SelectRunningApplication(this, "自動切替する起動中のアプリを選択");

    internal static string? SelectRunningApplication(Window owner, string title)
    {
        var apps = new List<InstalledApplicationInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(process.MainWindowTitle))
                    {
                        string executable = process.ProcessName + ".exe";
                        string? path = ApplicationIconService.TryGetProcessPath(process);
                        apps.Add(new(process.MainWindowTitle, path ?? executable, "起動中  ·  " + executable, executable));
                    }
                }
                catch { }
            }
        }
        var uniqueApps = apps.GroupBy(x => x.ExecutableName, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).OrderBy(x => x.Name).ToList();
        var dialog = new ApplicationPickerWindow(uniqueApps) { Title = title, Owner = owner };
        dialog.BrowseApplicationButton.Visibility = Visibility.Collapsed;
        if (dialog.ShowDialog() != true || dialog.SelectedApplication == null)
            return null;
        return dialog.SelectedApplication.ExecutableName
            ?? Path.GetFileName(ApplicationIconService.ResolveExecutablePath(dialog.SelectedApplication.LaunchPath) ?? dialog.SelectedApplication.LaunchPath);
    }
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int valueSize);
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
