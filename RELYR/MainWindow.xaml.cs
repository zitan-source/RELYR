using Microsoft.Win32;
using System.ComponentModel;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfMessageBox = RELYR.AppDialog;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfBrushes = System.Windows.Media.Brushes;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using System.Windows.Input;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace RELYR;

public partial class MainWindow : Window
{
    readonly ConfigService store = new();
    readonly InputEngine engine = new();
    readonly MappingExecutor executor;
    readonly MappingExecutor deckExecutor;
    readonly ArchiveWatcher archiveWatcher = new();
    readonly BlockingCollection<(Mapping Map,string Input,bool ForceActiveWindow)> actionQueue=new(256);
    readonly Task actionWorker;
    readonly BlockingCollection<(Mapping? Map,string Input)> dragActionQueue=new();
    readonly Task dragActionWorker;
    readonly ConcurrentDictionary<string,InputMappingSnapshot> activeInputMappings=new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string,LayerMappingSnapshot> activeLayerMappings=new(StringComparer.OrdinalIgnoreCase);
    readonly System.Windows.Threading.DispatcherTimer trayNumberTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    readonly System.Windows.Threading.DispatcherTimer profileSwitchTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    bool profileDropDownOpen;
    string explicitProfileSwitchProcess="";
    string automaticProfileReturnName="";
    string automaticProfileCandidateSignature="";
    int automaticProfileCandidateSamples;
    int automaticProfileCheckQueued;
    IntPtr automaticProfilePointerWindow;
    DateTime suppressAutomaticProfileSwitchUntil=DateTime.MinValue;
    readonly string automaticProfileDiagnosticLog=Environment.GetEnvironmentVariable("RELYR_PROFILE_SWITCH_LOG")??"";
    readonly System.Windows.Threading.DispatcherTimer autoSaveTimer=new(){Interval=TimeSpan.FromMilliseconds(450)};
    readonly CancellationTokenSource updateCancellation=new();
    internal static readonly TimeSpan AutomaticUpdateCheckInterval=TimeSpan.FromDays(1);
    System.Drawing.Icon? numberedTrayIcon;
    System.Drawing.Icon? defaultTrayIcon;
    AppConfig config;
    AppConfig appliedConfig=null!;
    Mapping? selected;
    string selectedBaseInput="";
    bool loading, detectMode, allowClose, engineStarted, editingSelectedInput;
    int exitRequested;
    string? pendingDetectedLayer;
    bool updateInProgress;
    DateTimeOffset lastAutomaticUpdateCheckAttempt;
    Task<UpdateCheckResult>? runningUpdateCheckTask;
    bool capsLockRemapped;
    string currentLayer="通常";
    MacroWindow? macroWindow;bool engineBeforeMacroRecording,macroEmergencyStop,macroIsRecording;
    ProfileSwitchOverlay? profileOverlay;
    string lastProfileOverlayName="";
    Mapping? copiedMapping;
    TextBox? destinationInputTarget;
    readonly List<System.Windows.Controls.Button> deckManagementButtons=[];
    readonly Dictionary<System.Windows.Controls.Button,TextBlock> deckManagementNameLabels=[];
    bool deckManagementMode;
    int destinationFocusRequest;
    UpdateInfo? availableUpdate;
    UpdateCheckResult? lastUpdateCheck;
    internal event Action<UpdateCheckResult>? UpdateCheckCompleted;
    readonly System.Windows.Forms.NotifyIcon tray = new();
    readonly bool suppressTray;
    int trayDisposed;
    Profile CurrentProfile => config.Profiles.First(x => x.Name == config.ActiveProfile);
    Profile AppliedProfile => appliedConfig.Profiles.FirstOrDefault(x=>x.Name==appliedConfig.ActiveProfile)??appliedConfig.Profiles[0];
    List<Mapping> MappingCollectionForInput(string input)=>DeckPanelLayout.IsInputName(input)&&config.UseSharedDeckPanel?config.SharedDeckMappings:CurrentProfile.Mappings;
    public bool NeedsFirstRunSetup=>!config.FirstRunCompleted;
    internal bool IsInputHookDisposedForTest=>engine.IsDisposedForTest;
    internal bool IsInputEngineReadyForTest=>engineStarted&&engine.Enabled;
    internal bool HasDestinationInputTargetForTest=>destinationInputTarget!=null;
    internal bool IsEditingSelectedInputForTest=>editingSelectedInput;
    internal Profile CurrentProfileForTest=>CurrentProfile;
    internal AppConfig ConfigForTest=>config;
    internal IList<Profile> ProfilesForTest=>config.Profiles;
    internal string AppliedProfileNameForTest=>AppliedProfile.Name;
    internal Mapping? AppliedMappingForTest(string input)=>FindProfileMapping(appliedConfig.Profiles,AppliedProfile.Name,input,MappingInterceptsInput);
    internal Mapping? RuntimeMappingForTest(string input)=>FindMapping(input);
    internal void BeginLayerMappingScopeForTest(string layer)=>CaptureLayerMappings(layer);
    internal void EndLayerMappingScopeForTest(string layer)=>ReleaseLayerMappings(layer);
    internal bool ExecuteMappingForTest(Mapping mapping,string input)=>executor.Execute(mapping,input,out _);
    internal void SwitchProfileForTest(string name)=>SwitchProfile(name,true,false);
    internal void ApplyProfileManagerResultForTest(IReadOnlyList<Profile> profiles,string activeProfile,bool autoSwitch)
        =>ApplyProfileManagerResult(profiles,activeProfile,autoSwitch);
    internal bool ApplyAutomaticProfileForTest(IReadOnlyCollection<string> processes)
        =>TryApplyAutomaticProfileForProcesses(processes,false,out _);
    internal bool IsProfileOverlayVisibleForTest=>profileOverlay?.IsVisible==true;
    internal ProfileSwitchOverlay? ProfileOverlayForTest=>profileOverlay;
    internal void ShowProfileOverlayForTest(string name)=>ShowProfileOverlay(name);
    internal IReadOnlyList<System.Windows.Controls.Button> VisualInputButtonsForTest=>VisualInputButtons().ToList();
    internal IReadOnlyList<System.Windows.Controls.Button> DeckManagementButtonsForTest=>deckManagementButtons;
    internal WindowActionTarget DeckWindowActionTargetForTest=>DeckExecutionConfig().WindowActionTarget;
    internal bool TitleBarUsesDarkMode { get; private set; }
    internal static string DisplayVersion
    {
        get{var v=typeof(MainWindow).Assembly.GetName().Version??new Version(0,0,0);return $"{v.Major}.{v.Minor}.{Math.Max(0,v.Build)}";}
    }
    internal static Version RunningVersion
    {
        get{var v=typeof(MainWindow).Assembly.GetName().Version??new Version(0,0,0);return new Version(v.Major,v.Minor,Math.Max(0,v.Build));}
    }

    public MainWindow(bool skipSetup=false,bool suppressTray=false,AppConfig? startupConfig=null)
    {
        this.suppressTray=suppressTray;
        loading=true;
        InitializeComponent();
        Loaded+=(_,_)=>EnsureUpdateCheckStarted();
        IsVisibleChanged+=(_,_)=>{if(IsVisible)EnsureUpdateCheckStarted();};
        StateChanged+=(_,_)=>{if(IsVisible&&WindowState!=WindowState.Minimized)EnsureUpdateCheckStarted();};
        SourceInitialized+=(_,_)=>ApplyWindowsTitleBarTheme();
        SystemEvents.UserPreferenceChanged+=WindowsThemeChanged;
        ArrangeInputWorkspace();
        VersionText.Text="v"+DisplayVersion;
        Title="RELYR v"+DisplayVersion;
        config = startupConfig??store.Load();
        ThemeService.Apply(config.ThemeMode);
        ThemeService.ThemeChanged+=AppThemeChanged;
        MacroPlayer.PlaybackFinished+=MacroPlaybackFinished;
        bool configuredCapsLockRemap=LegacyKeyRemapService.HasCapsLockToF13();bool capsRestartPending=LegacyKeyRemapService.IsRestartStillPending(config);
        if(config.CapsLockRemapPendingRestart&&!capsRestartPending){config.CapsLockRemapPendingRestart=false;config.CapsLockRemapEffectiveBeforeRestart=false;config.CapsLockRemapChangedAtUtcTicks=0;config.CapsLockLayerEnabled=configuredCapsLockRemap;store.Save(config);}
        else if(!capsRestartPending)config.CapsLockLayerEnabled=configuredCapsLockRemap;
        capsLockRemapped=capsRestartPending?config.CapsLockRemapEffectiveBeforeRestart:configuredCapsLockRemap;
        engine.TreatF13AsCapsLock=capsLockRemapped;
        appliedConfig=store.Clone(config);
        OverlayService.Configure(
            ()=>appliedConfig,
            ()=>engine.HasCapturedPhysicalInput,
            mapping=>{try{if(!actionQueue.IsAddingCompleted)actionQueue.TryAdd((mapping,mapping.Input,true));}catch(InvalidOperationException){}},
            PersistDeckPanelPosition);
        executor=new MappingExecutor(new SystemInputOutput(name=>appliedConfig.Macros.FirstOrDefault(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)),name=>Dispatcher.BeginInvoke(()=>SwitchProfile(name,true)),()=>appliedConfig.KeyboardLayout=="US",()=>appliedConfig));
        deckExecutor=new MappingExecutor(new SystemInputOutput(name=>appliedConfig.Macros.FirstOrDefault(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)),name=>Dispatcher.BeginInvoke(()=>SwitchProfile(name,true)),()=>appliedConfig.KeyboardLayout=="US",DeckExecutionConfig));
        actionWorker=Task.Run(ProcessActions);dragActionWorker=Task.Factory.StartNew(ProcessDragActions,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
        AutoSaveToggle.IsChecked=config.AutoSave;UpdateAutoSaveToggleText();
        KeyboardLayoutBox.SelectedIndex=config.KeyboardLayout=="US"?1:0;
        loading=false;
        var shortActionOptions=ActionOptions(allowGesture:true);
        var longActionOptions=ActionOptions(allowGesture:false);
        KindBox.ItemsSource=shortActionOptions;LongKindBox.ItemsSource=longActionOptions;
        KindBox.SelectedValuePath=nameof(ActionOption.Kind);
        LongKindBox.SelectedValuePath=nameof(ActionOption.Kind);
        BuildKeyboard();BuildDeckManagementPanel();engine.UseUsLayout=config.KeyboardLayout=="US";engine.SpaceHoldRepeatEnabled=config.SpaceHoldRepeatEnabled;engine.SpaceHoldRepeatDelayMs=config.SpaceHoldRepeatDelayMs;engine.LockCursorDuringGesture=config.LockCursorDuringGesture;RefreshProfiles();UpdateLayerButtons();
        engine.InputReceived = HandleInput;
        engine.InputStarted = CaptureInputMapping;
        engine.InputEnded = ReleaseInputMapping;
        engine.LayerStarted = CaptureLayerMappings;
        engine.LayerEnded = ReleaseLayerMappings;
        InputEngine.DesktopActionFailed=message=>Dispatcher.BeginInvoke(()=>{LastInput.Text="仮想デスクトップ操作エラー: "+message;LastInput.Foreground=ThemeService.Brush("DangerBrush");});
        engine.QualifyInput = QualifyInput;
        engine.HasMapping = HasMapping;
        engine.IsNativeMouseDrag=input=>FindMapping(input) is {Kind:ActionKind.Mouse} map&&MappingExecutor.IsModifierDrag(map.Value);
        engine.HasLegacyMouseDrag=input=>FindMapping(input) is { } map&&(!string.IsNullOrWhiteSpace(map.DragValue)||!string.IsNullOrWhiteSpace(map.DragEndValue));
        engine.SuppressLayerTap = key=>key.Equals("CapsLock",StringComparison.OrdinalIgnoreCase);
        engine.HasLongPress = input => HasConfiguredLongPress(FindMapping(input));
        engine.IsGesturePress = input => FindMapping(input)?.Kind==ActionKind.Gesture;
        engine.IsGestureLongPress = input => FindMapping(input)?.LongPressKind==ActionKind.Gesture;
        engine.LongPressDuration = input => FindMapping(input)?.LongPressMs ?? 500;
        engine.DragPixels = config.MouseDragPixels;
        engine.GestureThresholdPixels=config.GestureThresholdPixels;
        engine.Detected += text => Dispatcher.BeginInvoke(() => HandleDetectedInput(text));
        engine.PointerMoved += QueueAutomaticProfileCheck;
        engine.Enabled=false;
        try { engine.Start();engineStarted=true; } catch (Exception ex) { config.EngineEnabled=false;appliedConfig.EngineEnabled=false;store.Save(config);WpfMessageBox.Show("入力フックを開始できません。エンジンを停止しました。\n\n" + ex.Message,"入力エンジンを開始できません",MessageBoxButton.OK,MessageBoxImage.Error); }
        engine.Enabled = engineStarted&&config.EngineEnabled;
        EngineToggle.IsChecked = engine.Enabled;EngineToggle.IsEnabled=engineStarted;
        AdminStatus.Text = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator) ? "管理者モード" : "一般権限";
        archiveWatcher.Status+=text=>Dispatcher.BeginInvoke(()=>LastInput.Text=text);
        archiveWatcher.Apply(config);
        if(!suppressTray){SetupTray();trayNumberTimer.Tick+=(_,_)=>UpdateTrayNumber();trayNumberTimer.Start();}
        profileSwitchTimer.Tick+=(_,_)=>AutoSwitchProfile();profileSwitchTimer.Start();autoSaveTimer.Tick+=(_,_)=>{autoSaveTimer.Stop();SaveAndApply("自動保存しました");};UpdateStatus();
        if(capsRestartPending){LastInput.Text="CapsLock設定は再起動待ちです — Windowsを再起動するまで変更は有効になりません";LastInput.Foreground=ThemeService.Brush("WarningBrush");}
        else if(configuredCapsLockRemap){LastInput.Text="CapsLock→F13設定を検出しました。CapsLockレイヤーとして互換動作します";LastInput.Foreground=ThemeService.Brush("AccentBrush");}
        if(skipSetup&&NeedsFirstRunSetup){config.FirstRunCompleted=true;store.Save(config);}
    }

    void QueueAutomaticProfileCheck()
    {
        if(Interlocked.Exchange(ref automaticProfileCheckQueued,1)!=0)return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,new Action(()=>
        {
            Interlocked.Exchange(ref automaticProfileCheckQueued,0);
            IntPtr pointerWindow=ConditionMatcher.RootWindowUnderCursor();
            if(pointerWindow==automaticProfilePointerWindow)return;
            automaticProfilePointerWindow=pointerWindow;
            AutoSwitchProfile();
        }));
    }

    void ArrangeInputWorkspace()
    {
        if(LayerButtonsPanel.Parent is System.Windows.Controls.Panel layerParent)layerParent.Children.Remove(LayerButtonsPanel);
        LayerButtonsPanel.Orientation=System.Windows.Controls.Orientation.Vertical;
        LayerButtonsPanel.HorizontalAlignment=System.Windows.HorizontalAlignment.Stretch;
        LayerNavigationHost.Children.Add(LayerButtonsPanel);

        if(MousePanel.Parent is System.Windows.Controls.Panel mouseParent)
        {
            mouseParent.Children.Remove(MousePanel);
        }
        MouseHost.Child=MousePanel;
        UpdateLayerButtonWidths();
    }

    void WindowsThemeChanged(object sender,UserPreferenceChangedEventArgs e)
    {
        if(e.Category is UserPreferenceCategory.Color or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
            Dispatcher.BeginInvoke((Action)(()=>{ThemeService.RefreshSystemTheme();ApplyWindowsTitleBarTheme();}));
    }

    void AppThemeChanged()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke((Action)AppThemeChanged);return;}
        ApplyWindowsTitleBarTheme();
        if(KeyboardPanel!=null){BuildKeyboard();ColorButtons();UpdateLayerButtons();}
        if(engine!=null)UpdateStatus();
    }

    internal static bool IsWindowsAppDarkMode()
    {
        try
        {
            return ThemeService.SystemUsesDarkMode();
        }
        catch{return false;}
    }

    void ApplyWindowsTitleBarTheme()
    {
        TitleBarUsesDarkMode=ApplyWindowsTitleBarTheme(this);
    }
    internal static bool ApplyWindowsTitleBarTheme(Window window)
    {
        IntPtr handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if(handle==IntPtr.Zero)return false;
        bool dark=ThemeService.UsesDark;int enabled=dark?1:0;
        int result=DwmSetWindowAttribute(handle,20,ref enabled,sizeof(int));
        if(result!=0)DwmSetWindowAttribute(handle,19,ref enabled,sizeof(int));
        return dark;
    }
    internal static void FollowWindowsTitleBarTheme(Window window,Action<bool>? applied=null)
    {
        void Apply(){if(!window.Dispatcher.HasShutdownStarted){bool dark=ApplyWindowsTitleBarTheme(window);applied?.Invoke(dark);}}
        Action themeHandler=Apply;
        UserPreferenceChangedEventHandler handler=(_,e)=>
        {
            if(e.Category is UserPreferenceCategory.Color or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)window.Dispatcher.BeginInvoke((Action)Apply);
        };
        window.SourceInitialized+=(_,_)=>Apply();
        SystemEvents.UserPreferenceChanged+=handler;
        ThemeService.ThemeChanged+=themeHandler;
        window.Closed+=(_,_)=>{SystemEvents.UserPreferenceChanged-=handler;ThemeService.ThemeChanged-=themeHandler;};
    }

    void BuildKeyboard()
    {
        KeyboardPanel.Children.Clear();SecondaryKeyboardPanel.Children.Clear();KeyboardPanel.Width=config.KeyboardLayout=="US"?900:942;AddSecondaryGroupFrames();if(config.KeyboardLayout=="US"){BuildUsKeyboard();return;}
        AddTopFunctionRow(942);
        AddKey("PrintScreen","Print",970,0,72);AddKey("ScrollLock","Scroll",1046,0,72);AddKey("Pause","Pause",1122,0,72);

        AddRow(44,[new("半角/全角","半角/全角",88),new("1","1",54),new("2","2",54),new("3","3",54),new("4","4",54),new("5","5",54),new("6","6",54),new("7","7",54),new("8","8",54),new("9","9",54),new("0","0",54),new("-","-",54),new("^","^",54),new("¥","¥",54),new("Back","Backspace",96)]);
        AddKey("Insert","Insert",970,70,72);AddKey("Home","Home",1046,70,72);AddKey("PageUp","PageUp",1122,70,72);
        AddRow(100,[new("Tab","Tab",82),new("Q","Q",54),new("W","W",54),new("E","E",54),new("R","R",54),new("T","T",54),new("Y","Y",54),new("U","U",54),new("I","I",54),new("O","O",54),new("P","P",54),new("@","@",54),new("[","[",54)]);
        AddKey("Delete","Delete",970,128,72);AddKey("End","End",1046,128,72);AddKey("PageDown","PageDown",1122,128,72);
        AddRow(156,[new("CapsLock","CapsLock\n(F13設定時)",104),new("A","A",54),new("S","S",54),new("D","D",54),new("F","F",54),new("G","G",54),new("H","H",54),new("J","J",54),new("K","K",54),new("L","L",54),new(";",";",54),new(":",":",54),new("]","]",54)]);
        AddJisEnter();
        AddRow(212,[new("LeftShift","Shift",126),new("Z","Z",54),new("X","X",54),new("C","C",54),new("V","V",54),new("B","B",54),new("N","N",54),new("M","M",54),new(",",",",54),new(".",".",54),new("/","/",54),new("_","＼  _",54),new("RightShift","Shift",174)]);
        AddKey("Up","↑",1046,244,54);
        AddRow(268,[new("LeftCtrl","Ctrl",78),new("LWin","Win",64),new("LeftAlt","Alt",68),new("無変換","無変換",76),new("Space","Space",248),new("変換","変換",72),new("カタカナ","カタカナ",78),new("RightAlt","Alt",68),new("RWin","Win",64),new("RightCtrl","Ctrl",90)]);
        AddKey("Left","←",970,302,54);AddKey("Down","↓",1046,302,54);AddKey("Right","→",1122,302,54);

        AddKey("NumLock","Num",1210,70,62);AddKey("Divide","÷",1276,70,62);AddKey("Multiply","×",1342,70,62);AddKey("Subtract","−",1408,70,62);
        AddKey("NumPad7","7",1210,128,62);AddKey("NumPad8","8",1276,128,62);AddKey("NumPad9","9",1342,128,62);AddKey("Add","＋",1408,128,62,108);
        AddKey("NumPad4","4",1210,186,62);AddKey("NumPad5","5",1276,186,62);AddKey("NumPad6","6",1342,186,62);
        AddKey("NumPad1","1",1210,244,62);AddKey("NumPad2","2",1276,244,62);AddKey("NumPad3","3",1342,244,62);AddKey("NumPadEnter","Enter",1408,244,62,108);
        AddKey("NumPad0","0",1210,302,128);AddKey("Decimal",".",1342,302,62);

        AddFunctionExtension();
    }
    void BuildUsKeyboard()
    {
        AddTopFunctionRow(900);AddKey("PrintScreen","Print",970,0,72);AddKey("ScrollLock","Scroll",1046,0,72);AddKey("Pause","Pause",1122,0,72);
        AddRow(44,[new("`","`",56),new("1","1",56),new("2","2",56),new("3","3",56),new("4","4",56),new("5","5",56),new("6","6",56),new("7","7",56),new("8","8",56),new("9","9",56),new("0","0",56),new("-","-",56),new("=","=",56),new("Back","Backspace",120)]);AddKey("Insert","Insert",970,70,72);AddKey("Home","Home",1046,70,72);AddKey("PageUp","PageUp",1122,70,72);
        AddRow(100,[new("Tab","Tab",88),new("Q","Q",56),new("W","W",56),new("E","E",56),new("R","R",56),new("T","T",56),new("Y","Y",56),new("U","U",56),new("I","I",56),new("O","O",56),new("P","P",56),new("[","[",56),new("]","]",56),new("\\","＼",88)]);AddKey("Delete","Delete",970,128,72);AddKey("End","End",1046,128,72);AddKey("PageDown","PageDown",1122,128,72);
        AddRow(156,[new("CapsLock","CapsLock\n(F13設定時)",102),new("A","A",56),new("S","S",56),new("D","D",56),new("F","F",56),new("G","G",56),new("H","H",56),new("J","J",56),new("K","K",56),new("L","L",56),new(";",";",56),new("'","'",56),new("Enter","Enter",134)]);
        AddRow(212,[new("LeftShift","Shift",136),new("Z","Z",56),new("X","X",56),new("C","C",56),new("V","V",56),new("B","B",56),new("N","N",56),new("M","M",56),new(",",",",56),new(".",".",56),new("/","/",56),new("RightShift","Shift",160)]);AddKey("Up","↑",1046,244,56);
        AddRow(268,[new("LeftCtrl","Ctrl",72),new("LWin","Win",72),new("LeftAlt","Alt",72),new("Space","Space",368),new("RightAlt","Alt",72),new("RWin","Win",72),new("Menu","Menu",72),new("RightCtrl","Ctrl",72)]);AddKey("Left","←",970,302,56);AddKey("Down","↓",1046,302,56);AddKey("Right","→",1122,302,56);AddNumpad();AddFunctionExtension();
    }
    void AddTopFunctionRow(double rightEdge){const int keyCount=14;const double gap=4;double keyWidth=(rightEdge-gap*(keyCount-1))/keyCount;double x=0;AddMainKey("Esc","Esc",x,0,keyWidth,26);x+=keyWidth+gap;for(int i=1;i<=12;i++){AddMainKey($"F{i}",$"F{i}",x,0,keyWidth,26);x+=keyWidth+gap;}AddMainKey("Delete","Delete",x,0,keyWidth,26);}
    void AddMainKey(string key,string label,double x,double y,double width,double height){var b=MakeInputButton(key);b.Content=label;b.Width=width;b.Height=height;b.MinWidth=0;b.Margin=new Thickness(0);Canvas.SetLeft(b,x);Canvas.SetTop(b,y);KeyboardPanel.Children.Add(b);}
    void AddNumpad(){AddKey("NumLock","Num",1210,70,62);AddKey("Divide","÷",1276,70,62);AddKey("Multiply","×",1342,70,62);AddKey("Subtract","−",1408,70,62);AddKey("NumPad7","7",1210,128,62);AddKey("NumPad8","8",1276,128,62);AddKey("NumPad9","9",1342,128,62);AddKey("Add","＋",1408,128,62,108);AddKey("NumPad4","4",1210,186,62);AddKey("NumPad5","5",1276,186,62);AddKey("NumPad6","6",1342,186,62);AddKey("NumPad1","1",1210,244,62);AddKey("NumPad2","2",1276,244,62);AddKey("NumPad3","3",1342,244,62);AddKey("NumPadEnter","Enter",1408,244,62,108);AddKey("NumPad0","0",1210,302,128);AddKey("Decimal",".",1342,302,62);}
    void AddFunctionExtension(){const double gap=4;double rightEdge=config.KeyboardLayout=="US"?900:942;double width=(rightEdge-gap*13)/14;double x=0;for(int i=14;i<=24;i++){AddKey($"F{i}",$"F{i}",x,338,width,26);x+=width+gap;}}
    void AddRow(double y,IEnumerable<KeySpec> keys){double x=0;foreach(var key in keys){AddKey(key.Key,key.Label,x,y,key.Width);x+=key.Width+4;}}
    void AddKey(string key,string label,double x,double y,double width,double height=52)
    {
        var panel=KeyboardPanel;
        if(TrySecondaryPosition(key,out double secondaryX,out double secondaryY))
        {
            panel=SecondaryKeyboardPanel;x=secondaryX;y=secondaryY;width=SecondaryKeyWidth;height=SecondaryKeyHeight;
            if(key=="NumPad0")width=SecondaryKeyWidth*2+SecondaryKeyGap;
            else if(key is "Add" or "NumPadEnter")height=SecondaryKeyHeight*2+SecondaryKeyGap;
        }
        var b=MakeInputButton(key);b.Content=label;b.Width=width;b.Height=height;b.MinWidth=0;b.Margin=new Thickness(0);Canvas.SetLeft(b,x);Canvas.SetTop(b,y);panel.Children.Add(b);
    }
    const double SecondaryKeyHeight=52,SecondaryKeyGap=4,SecondaryFramePadding=10,SecondaryKeyTop=26,SecondaryGroupGap=12;
    double SecondaryKeyWidth=>config.KeyboardLayout=="US"?56:54;
    void AddSecondaryGroupFrames()
    {
        double unit=SecondaryKeyWidth;
        double navigationWidth=SecondaryFramePadding*2+unit*3+SecondaryKeyGap*2;
        double navigationHeight=SecondaryKeyTop+SecondaryKeyHeight*3+SecondaryKeyGap*2+SecondaryFramePadding;
        double numpadX=navigationWidth+SecondaryGroupGap;
        double numpadWidth=SecondaryFramePadding*2+unit*4+SecondaryKeyGap*3;
        double numpadHeight=SecondaryKeyTop+SecondaryKeyHeight*5+SecondaryKeyGap*4+SecondaryFramePadding;
        double cursorX=numpadX+numpadWidth+SecondaryGroupGap;
        double cursorWidth=navigationWidth;
        double cursorHeight=SecondaryKeyTop+SecondaryKeyHeight*2+SecondaryKeyGap+SecondaryFramePadding;
        AddSecondaryGroupFrame("ナビゲーション",0,0,navigationWidth,navigationHeight);
        AddSecondaryGroupFrame("テンキー",numpadX,0,numpadWidth,numpadHeight);
        AddSecondaryGroupFrame("カーソルキー",cursorX,0,cursorWidth,cursorHeight);
        SecondaryKeyboardPanel.Width=cursorX+cursorWidth;
        SecondaryKeyboardPanel.Height=numpadHeight;
    }
    void AddSecondaryGroupFrame(string title,double x,double y,double width,double height)
    {
        var frame=new Border
        {
            Width=width,Height=height,Tag=title,CornerRadius=new CornerRadius(7),BorderThickness=new Thickness(1),
            BorderBrush=ThemeService.Brush("SubtleBorderBrush"),
            Background=WpfBrushes.Transparent,IsHitTestVisible=false
        };
        Canvas.SetLeft(frame,x);Canvas.SetTop(frame,y);SecondaryKeyboardPanel.Children.Add(frame);
        var heading=new TextBlock{Text=title,Foreground=ThemeService.Brush("MutedText"),FontSize=11,FontWeight=FontWeights.SemiBold,IsHitTestVisible=false};
        Canvas.SetLeft(heading,x+10);Canvas.SetTop(heading,y+3);SecondaryKeyboardPanel.Children.Add(heading);
    }
    bool TrySecondaryPosition(string key,out double x,out double y)
    {
        double unit=SecondaryKeyWidth,stepX=unit+SecondaryKeyGap,stepY=SecondaryKeyHeight+SecondaryKeyGap;
        double navigationWidth=SecondaryFramePadding*2+unit*3+SecondaryKeyGap*2;
        double numpadX=navigationWidth+SecondaryGroupGap;
        double numpadWidth=SecondaryFramePadding*2+unit*4+SecondaryKeyGap*3;
        double cursorX=numpadX+numpadWidth+SecondaryGroupGap;
        double navLeft=SecondaryFramePadding,numpadLeft=numpadX+SecondaryFramePadding,cursorLeft=cursorX+SecondaryFramePadding;
        (x,y)=key switch
        {
            "Insert"=>(navLeft,SecondaryKeyTop),"Home"=>(navLeft+stepX,SecondaryKeyTop),"PageUp"=>(navLeft+stepX*2,SecondaryKeyTop),
            "Delete"=>(navLeft,SecondaryKeyTop+stepY),"End"=>(navLeft+stepX,SecondaryKeyTop+stepY),"PageDown"=>(navLeft+stepX*2,SecondaryKeyTop+stepY),
            "PrintScreen"=>(navLeft,SecondaryKeyTop+stepY*2),"ScrollLock"=>(navLeft+stepX,SecondaryKeyTop+stepY*2),"Pause"=>(navLeft+stepX*2,SecondaryKeyTop+stepY*2),
            "NumLock"=>(numpadLeft,SecondaryKeyTop),"Divide"=>(numpadLeft+stepX,SecondaryKeyTop),"Multiply"=>(numpadLeft+stepX*2,SecondaryKeyTop),"Subtract"=>(numpadLeft+stepX*3,SecondaryKeyTop),
            "NumPad7"=>(numpadLeft,SecondaryKeyTop+stepY),"NumPad8"=>(numpadLeft+stepX,SecondaryKeyTop+stepY),"NumPad9"=>(numpadLeft+stepX*2,SecondaryKeyTop+stepY),"Add"=>(numpadLeft+stepX*3,SecondaryKeyTop+stepY),
            "NumPad4"=>(numpadLeft,SecondaryKeyTop+stepY*2),"NumPad5"=>(numpadLeft+stepX,SecondaryKeyTop+stepY*2),"NumPad6"=>(numpadLeft+stepX*2,SecondaryKeyTop+stepY*2),
            "NumPad1"=>(numpadLeft,SecondaryKeyTop+stepY*3),"NumPad2"=>(numpadLeft+stepX,SecondaryKeyTop+stepY*3),"NumPad3"=>(numpadLeft+stepX*2,SecondaryKeyTop+stepY*3),"NumPadEnter"=>(numpadLeft+stepX*3,SecondaryKeyTop+stepY*3),
            "NumPad0"=>(numpadLeft,SecondaryKeyTop+stepY*4),"Decimal"=>(numpadLeft+stepX*2,SecondaryKeyTop+stepY*4),
            "Up"=>(cursorLeft+stepX,SecondaryKeyTop),"Left"=>(cursorLeft,SecondaryKeyTop+stepY),"Down"=>(cursorLeft+stepX,SecondaryKeyTop+stepY),"Right"=>(cursorLeft+stepX*2,SecondaryKeyTop+stepY),
            _=>(double.NaN,double.NaN)
        };
        return !double.IsNaN(x);
    }
    void AddJisEnter(){var shape=Geometry.Parse("M 0,0 L 160,0 L 160,108 L 22,108 L 22,56 L 0,56 Z");var b=MakeInputButton("Enter");b.Content="Enter";b.Width=160;b.Height=108;b.MinWidth=0;b.Margin=new Thickness(0);b.Clip=shape;Canvas.SetLeft(b,782);Canvas.SetTop(b,100);KeyboardPanel.Children.Add(b);var outline=new System.Windows.Shapes.Path{Data=shape,Stroke=ThemeService.Brush("BorderBrush"),StrokeThickness=1,Fill=WpfBrushes.Transparent,IsHitTestVisible=false};Canvas.SetLeft(outline,782);Canvas.SetTop(outline,100);KeyboardPanel.Children.Add(outline);}
    readonly record struct KeySpec(string Key,string Label,double Width);
    System.Windows.Controls.Button MakeInputButton(string key) { var b = new System.Windows.Controls.Button { Content = key=="CapsLock"?"CapsLock\n(F13設定時)":key, Tag = key, Style = (Style)FindResource("KeyButton") };if(key=="Space")b.Width=210;else if(key=="CapsLock"){b.MinWidth=94;b.FontSize=10;}else if(key is "LeftShift" or "RightShift" or "Enter" or "Back")b.MinWidth=82;else if(key is "Tab" or "LeftCtrl" or "RightCtrl")b.MinWidth=70;b.Click += (_, _) => SelectVisualInput(key); return b; }
    void SelectVisualInput(string key)
    {
        if(key=="Space"&&currentLayer is "通常" or "Space"){ShowInlineNotice("SpaceキーはSpaceレイヤー専用のため、このレイヤーでは変更できません");return;}
        if(key=="CapsLock"&&!editingSelectedInput&&destinationInputTarget==null){ShowInlineNotice("CapsLockは割り当て元にはできません。別のキーを選んだ後、割り当て先として使用できます");return;}
        SelectInput(currentLayer=="通常"?key:currentLayer+"+"+key);
    }
    void InputButton_Click(object sender,RoutedEventArgs e){if(sender is System.Windows.Controls.Button{Tag:string key})SelectVisualInput(key);}
    void DestinationButton_PreviewMouseDown(object sender,MouseButtonEventArgs e)
    {
        var target=ValueBox.IsKeyboardFocusWithin?ValueBox:LongValueBox.IsKeyboardFocusWithin?LongValueBox:destinationInputTarget;
        if(target==null&&editingSelectedInput&&sender is System.Windows.Controls.Button{Tag:"CapsLock"})target=ValueBox;
        if(target==null||!target.IsVisible||!target.IsEnabled||sender is not System.Windows.Controls.Button{Tag:string key})return;
        string token=key;var parts=target.Text.Split('+',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToList();
        parts.Add(token);target.Text=string.Join("+",parts);target.CaretIndex=target.Text.Length;
        var kindBox=target==LongValueBox?LongKindBox:KindBox;
        if(parts.Count>1)kindBox.SelectedValue=ActionKind.Shortcut;
        else if(key.StartsWith("Mouse",StringComparison.OrdinalIgnoreCase)||key.StartsWith("Wheel",StringComparison.OrdinalIgnoreCase)||key.StartsWith("Tilt",StringComparison.OrdinalIgnoreCase))kindBox.SelectedValue=ActionKind.Shortcut;
        else kindBox.SelectedValue=ActionKind.Key;
        e.Handled=true;FocusExecutionValue(target);
    }
    void ExecutionValue_PreviewKeyDown(object sender,System.Windows.Input.KeyEventArgs e)
    {
        if(sender is not TextBox target)return;
        Key key=e.Key==Key.System?e.SystemKey:e.Key;
        bool modifierKey=key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
        ModifierKeys modifiers=Keyboard.Modifiers;
        if(!modifierKey&&modifiers==ModifierKeys.None)return;
        target.Text=ShortcutTextForKey(key,modifiers);
        target.CaretIndex=target.Text.Length;
        (ReferenceEquals(target,LongValueBox)?LongKindBox:KindBox).SelectedValue=ActionKind.Shortcut;
        e.Handled=true;
    }
    internal static string ShortcutTextForKey(Key key,ModifierKeys modifiers)
    {
        bool modifierKey=key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
        var parts=new List<string>();
        if((modifiers&ModifierKeys.Control)!=0||key is Key.LeftCtrl or Key.RightCtrl)parts.Add("Ctrl");
        if((modifiers&ModifierKeys.Shift)!=0||key is Key.LeftShift or Key.RightShift)parts.Add("Shift");
        if((modifiers&ModifierKeys.Alt)!=0||key is Key.LeftAlt or Key.RightAlt)parts.Add("Alt");
        if((modifiers&ModifierKeys.Windows)!=0||key is Key.LWin or Key.RWin)parts.Add("Win");
        if(!modifierKey)
        {
            string token=ShortcutTokenForKey(key);
            if(token.Length>0&&!parts.Contains(token,StringComparer.OrdinalIgnoreCase))parts.Add(token);
        }
        return string.Join("+",parts);
    }
    internal static string ShortcutTokenForKey(Key key)
    {
        if(key is >=Key.A and <=Key.Z)return key.ToString();
        if(key is >=Key.D0 and <=Key.D9)return ((int)key-(int)Key.D0).ToString();
        if(key is >=Key.NumPad0 and <=Key.NumPad9)return "NumPad"+((int)key-(int)Key.NumPad0);
        if(key is >=Key.F1 and <=Key.F24)return key.ToString();
        return key switch
        {
            Key.Return=>"Enter",Key.Escape=>"Esc",Key.Back=>"Backspace",Key.Delete=>"Delete",
            Key.Space=>"Space",Key.Tab=>"Tab",Key.Insert=>"Insert",Key.Home=>"Home",Key.End=>"End",
            Key.PageUp=>"PageUp",Key.PageDown=>"PageDown",Key.Up=>"Up",Key.Down=>"Down",Key.Left=>"Left",Key.Right=>"Right",
            Key.OemPlus=>"+",Key.OemMinus=>"-",Key.OemComma=>",",Key.OemPeriod=>".",Key.OemQuestion=>"/",
            Key.OemSemicolon=>";",Key.OemQuotes=>"'",Key.OemOpenBrackets=>"[",Key.OemCloseBrackets=>"]",
            Key.OemPipe=>"\\",Key.OemTilde=>"^",Key.Multiply=>"Multiply",Key.Divide=>"Divide",
            Key.Add=>"Add",Key.Subtract=>"Subtract",Key.Decimal=>"Decimal",_=>key.ToString()
        };
    }
    void InputButton_RightClick(object sender,MouseButtonEventArgs e)
    {
        if(sender is not System.Windows.Controls.Button{Tag:string key})return;
        e.Handled=true;
        if(DeckPanelLayout.IsInputName(key))
        {
            RenameDeckButton(key);
            return;
        }
        if(key=="Space"&&currentLayer is "通常" or "Space"){ShowInlineNotice("Spaceキーはレイヤー専用のため変更できません");return;}
        var menu=CreateInputContextMenu(key);menu.PlacementTarget=(System.Windows.Controls.Button)sender;menu.IsOpen=true;
    }
    void RenameDeckButton(string input)
    {
        var mappings=MappingCollectionForInput(input);
        var mapping=mappings.LastOrDefault(x=>x.Input.Equals(input,StringComparison.OrdinalIgnoreCase));
        string? name=PromptText("Deckボタン名","ボタンの下に表示する名前",mapping?.Description??"");
        if(name==null)return;
        SetDeckButtonName(input,name);
    }
    void SetDeckButtonName(string input,string name)
    {
        var mappings=MappingCollectionForInput(input);
        var mapping=mappings.LastOrDefault(x=>x.Input.Equals(input,StringComparison.OrdinalIgnoreCase));
        if(mapping==null)
        {
            mapping=new Mapping{Input=input,Layer=DeckPanelLayout.Layer};
            mappings.Add(mapping);
        }
        mapping.Description=name;
        if(selected?.Input.Equals(input,StringComparison.OrdinalIgnoreCase)==true)
        {
            selected.Description=name;
            if(DeckNameBox!=null&&!string.Equals(DeckNameBox.Text,name,StringComparison.Ordinal))
            {
                loading=true;DeckNameBox.Text=name;loading=false;
            }
        }
        if(!MappingHasConfiguredAction(mapping)&&string.IsNullOrWhiteSpace(mapping.Description))
            mappings.Remove(mapping);
        MarkDirty();
        ColorButtons();
    }
    void DeckNameBox_TextChanged(object sender,TextChangedEventArgs e)
    {
        if(loading||selected==null||!DeckPanelLayout.IsInputName(selected.Input))return;
        selected.Description=DeckNameBox.Text.Trim();
        if(MappingHasConfiguredAction(selected)||!string.IsNullOrWhiteSpace(selected.Description))
        {
            var mappings=MappingCollectionForInput(selected.Input);
            if(!mappings.Contains(selected))mappings.Add(selected);
        }
        else MappingCollectionForInput(selected.Input).Remove(selected);
        MarkDirty();
        ColorButtons();
    }
    internal void SetDeckButtonNameForTest(string input,string name)=>SetDeckButtonName(input,name);
    internal ContextMenu CreateInputContextMenu(string key)
    {
        string input=currentLayer=="通常"?key:currentLayer+"+"+key;
        var existing=CurrentProfile.Mappings.LastOrDefault(x=>x.Input.Equals(input,StringComparison.OrdinalIgnoreCase));
        var menu=new ContextMenu();
        var copy=new MenuItem{Header="この割り当てをコピー",IsEnabled=existing!=null};
        copy.Click+=(_,_)=>{copiedMapping=existing==null?null:CloneMapping(existing);ShowInlineNotice(input+" の割り当てをコピーしました");};
        var paste=new MenuItem{Header="コピーした割り当てを貼り付け",IsEnabled=copiedMapping!=null};
        paste.Click+=(_,_)=>{if(copiedMapping==null)return;var map=CloneMapping(copiedMapping);map.Input=input;map.Layer=currentLayer;if(map.Kind==ActionKind.Gesture&&!ConfirmDirectMouseGestureConflict(input))return;CurrentProfile.Mappings.RemoveAll(x=>x.Input.Equals(input,StringComparison.OrdinalIgnoreCase));CurrentProfile.Mappings.Add(map);SelectInput(input);UpdateLayerButtons();MarkDirty();ColorButtons();};
        var delete=new MenuItem{Header="この割り当てを削除",IsEnabled=existing!=null,Foreground=ThemeService.Brush("DangerBrush")};
        delete.Click+=(_,_)=>{if(existing==null)return;CurrentProfile.Mappings.Remove(existing);if(selected?.Input.Equals(input,StringComparison.OrdinalIgnoreCase)==true)selected=null;MarkDirty();SelectInput(input,false);UpdateLayerButtons();ClearExecutionFocus();ShowInlineNotice(DisplayInputName(input)+" の割り当てを削除しました");};
        menu.Items.Add(copy);menu.Items.Add(paste);menu.Items.Add(new Separator());menu.Items.Add(delete);
        return menu;
    }
    void LongPressOnly_Click(object sender,RoutedEventArgs e){ValueBox.Clear();if(selected!=null)selected.Kind=ActionKind.None;loading=true;KindBox.SelectedValue=ActionKind.None;loading=false;LongPressExpander.IsExpanded=true;FocusExecutionValue(LongValueBox,true);MarkDirty();LastInput.Text="長押しのみ：短押しは元のキーを入力します";}
    void DestinationInputDone_Click(object sender,RoutedEventArgs e)=>CompleteDestinationInput(sender as FrameworkElement);
    void MainWindow_PreviewMouseDown(object sender,MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=MouseButton.Left||e.OriginalSource is not DependencyObject source)return;
        if(IsDescendantOf(source,KeyboardPanel)||IsDescendantOf(source,SecondaryKeyboardPanel)||IsDescendantOf(source,MousePanel)||IsInteractiveClick(source))return;
        if(destinationInputTarget!=null||editingSelectedInput)CompleteDestinationInput();
        else if(selected!=null)ClearSelectedInput();
    }
    void CompleteDestinationInput(FrameworkElement? fallback=null)
    {
        autoSaveTimer.Stop();
        if(selected!=null)
        {
            NormalizeLongOnlyMapping(selected);
            if(config.AutoSave)SaveAndApply("入力完了 — 設定を保存して反映しました");
            else
            {
                LastInput.Text="入力完了 — 未保存の変更があります。［保存して反映］で有効になります";
                LastInput.Foreground=ThemeService.Brush("WarningBrush");
            }
        }
        editingSelectedInput=false;ClearExecutionFocus(fallback);ColorButtons();CloseAssignmentPane();
    }
    void ClearSelectedInput(FrameworkElement? fallback=null)
    {
        selected=null;selectedBaseInput="";editingSelectedInput=false;InputName.Clear();InputDisplayText.Text="キーを選択してください";
        InspectorEmptyState.Visibility=Visibility.Visible;SelectionHeader.Visibility=Visibility.Collapsed;AssignmentEditor.Visibility=Visibility.Collapsed;AssignmentEditor.IsEnabled=false;
        loading=true;DeckNameBox.Clear();loading=false;
        ClearExecutionFocus(fallback);ColorButtons();CloseAssignmentPane(false);
    }
    static bool IsDescendantOf(DependencyObject source,DependencyObject ancestor)
    {
        for(DependencyObject? current=source;current!=null;current=GetParent(current))if(ReferenceEquals(current,ancestor))return true;
        return false;
    }
    static bool IsInteractiveClick(DependencyObject source)
    {
        for(DependencyObject? current=source;current!=null;current=GetParent(current))
        {
            if(current is System.Windows.Controls.Primitives.ButtonBase||current is System.Windows.Controls.Primitives.TextBoxBase||current is System.Windows.Controls.Primitives.Selector||current is System.Windows.Controls.Primitives.RangeBase||current is System.Windows.Controls.Primitives.Thumb||current is PasswordBox)return true;
            if(current is Window)return false;
        }
        return false;
    }
    static DependencyObject? GetParent(DependencyObject current)=>current is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current);
    void FocusExecutionValue(TextBox target,bool bringIntoView=false)
    {
        int request=++destinationFocusRequest;
        destinationInputTarget=target;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(() =>
        {
            if(request!=destinationFocusRequest||!ReferenceEquals(destinationInputTarget,target)||!target.IsVisible||!target.IsEnabled)return;
            if(bringIntoView)target.BringIntoView();
            target.Focus();Keyboard.Focus(target);target.CaretIndex=target.Text.Length;target.SelectionLength=0;
        }));
    }
    void ClearExecutionFocus(FrameworkElement? fallback=null)
    {
        destinationFocusRequest++;
        destinationInputTarget=null;
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this),null);
        if(fallback is {IsVisible:true,IsEnabled:true}){fallback.Focus();Keyboard.Focus(fallback);}
    }
    void ActionKind_PreviewMouseLeftButtonDown(object sender,MouseButtonEventArgs e)
    {
        if(sender is not ListBox list||ItemsControl.ContainerFromElement(list,e.OriginalSource as DependencyObject) is not ListBoxItem item||item.DataContext is not ActionOption option||!option.IsEnabled)return;
        bool longPress=ReferenceEquals(list,LongKindBox);
        switch(option.Kind)
        {
            case ActionKind.Profile:
                e.Handled=true;
                OpenProfilePicker(longPress,list);
                break;
            case ActionKind.Shortcut:
                e.Handled=true;
                OpenActionPicker(longPress);
                break;
            case ActionKind.Launch:
                e.Handled=true;
                OpenApplicationPicker(longPress);
                break;
            case ActionKind.Macro:
                e.Handled=true;
                ShowMacroWindow(true,longPress);
                break;
            case ActionKind.Gesture:
                e.Handled=true;
                if(!longPress)OpenGesturePicker(list);
                break;
            case ActionKind.Disabled:
                e.Handled=true;
                ApplyDisabledAction(longPress);
                break;
            default:
                Dispatcher.BeginInvoke(()=>FocusExecutionValue(longPress?LongValueBox:ValueBox,longPress));
                break;
        }
    }
    void LongPressExpander_Expanded(object sender,RoutedEventArgs e){if(!loading&&selected!=null)FocusExecutionValue(LongValueBox,true);}
    void AssignmentPane_PreviewMouseWheel(object sender,MouseWheelEventArgs e)
    {
        if(sender is not ScrollViewer viewer||e.Delta==0||viewer.ScrollableHeight<=0)return;
        double distance=Math.Clamp(Math.Abs(e.Delta),48d,160d);
        viewer.ScrollToVerticalOffset(Math.Clamp(viewer.VerticalOffset-Math.Sign(e.Delta)*distance,0,viewer.ScrollableHeight));
        e.Handled=true;
    }
    void ShowAssignmentPane()
    {
        AssignmentPane.Visibility=Visibility.Visible;
        AssignmentPaneTransform.BeginAnimation(TranslateTransform.XProperty,null);
        AssignmentPaneTransform.X=0;
    }
    internal void DismissAssignmentPaneIfOutside(DependencyObject source)
    {
        ShowAssignmentPane();
    }
    void CloseAssignmentPane(bool animate=true)
    {
        ShowAssignmentPane();
    }
    void OpenActionPicker(bool longPress,string? initialMajorCategory=null)
    {
        var picker=new ActionPickerWindow(config.Profiles,config.KeyboardLayout,null,false,initialMajorCategory){Owner=this};
        if(picker.ShowDialog()!=true||picker.SelectedAction is not { } action)return;
        ApplyCatalogAction(action,longPress);
    }
    void OpenGesturePicker(ListBox placementTarget)
    {
        var menu=CreateGesturePickerMenu(placementTarget);
        if(menu.Items.Count==0){ShowInlineNotice("先に左下の「ジェスチャー管理」でジェスチャーを作成してください");return;}
        menu.IsOpen=true;
    }
    internal ContextMenu CreateGesturePickerMenu(ListBox placementTarget)
    {
        var menu=new ContextMenu{PlacementTarget=placementTarget,Placement=System.Windows.Controls.Primitives.PlacementMode.MousePoint};
        foreach(var gesture in config.Gestures.Where(x=>!string.IsNullOrWhiteSpace(x.Name)))
        {
            string name=gesture.Name;
            var item=new MenuItem{Header=name};
            item.Click+=(_,_)=>ApplyCatalogAction(new CatalogAction("ジェスチャー",name,"登録済みのジェスチャーを実行します",ActionKind.Gesture,name),false);
            menu.Items.Add(item);
        }
        return menu;
    }
    void ApplyCatalogAction(CatalogAction action,bool longPress)
    {
        if(action.Kind==ActionKind.Profile){ApplyProfileAction(action.Value,longPress);return;}
        if(action.Kind==ActionKind.Disabled){ApplyDisabledAction(longPress);return;}
        if(selected==null||action.Kind==ActionKind.Gesture&&longPress)return;
        if(action.Kind==ActionKind.Gesture&&!ConfirmDirectMouseGestureConflict(selected.Input))return;
        var editorKind=EditorActionKind(action.Kind);
        loading=true;
        if(longPress)
        {
            selected.LongPressKind=action.Kind;selected.LongPressValue=action.Value;
            LongKindBox.SelectedValue=editorKind;LongValueBox.Text=DisplayActionValue(action.Kind,action.Value);
        }
        else
        {
            selected.Kind=action.Kind;selected.Value=action.Value;
            KindBox.SelectedValue=action.Kind==ActionKind.Gesture?ActionKind.Gesture:editorKind;ValueBox.Text=DisplayActionValue(action.Kind,action.Value);
            if(action.Kind==ActionKind.Gesture)
            {
                selected.LongPressKind=ActionKind.None;selected.LongPressValue="";
                LongKindBox.SelectedIndex=-1;LongValueBox.Clear();LongPressExpander.IsExpanded=false;
            }
        }
        loading=false;
        var mappings=MappingCollectionForInput(selected.Input);
        if(MappingHasConfiguredAction(selected)&&!mappings.Contains(selected))mappings.Add(selected);
        UpdateBrowseButtons();UpdateLayerButtons();MarkDirty();ColorButtons();
        if(action.Kind==ActionKind.Gesture)ClearExecutionFocus(KindBox);
        else FocusExecutionValue(longPress?LongValueBox:ValueBox,longPress);
    }
    internal void ApplyCatalogActionForTest(CatalogAction action,bool longPress=false)=>ApplyCatalogAction(action,longPress);
    void OpenProfilePicker(bool longPress,ListBox placementTarget)
    {
        var menu=new ContextMenu{PlacementTarget=placementTarget,Placement=System.Windows.Controls.Primitives.PlacementMode.MousePoint};
        foreach(var profile in config.Profiles)
        {
            var item=new MenuItem{Header=profile.Name};
            string name=profile.Name;
            item.Click+=(_,_)=>ApplyProfileAction(name,longPress);
            menu.Items.Add(item);
        }
        menu.IsOpen=true;
    }
    void ApplyDisabledAction(bool longPress)
    {
        if(selected==null)return;
        loading=true;
        try
        {
            if(longPress)
            {
                selected.LongPressKind=ActionKind.Disabled;selected.LongPressValue="";
                LongKindBox.SelectedValue=ActionKind.Disabled;LongValueBox.Clear();LongPressExpander.IsExpanded=true;
            }
            else
            {
                selected.Kind=ActionKind.Disabled;selected.Value="";
                KindBox.SelectedValue=ActionKind.Disabled;ValueBox.Clear();
            }
        }
        finally{loading=false;}
        var mappings=MappingCollectionForInput(selected.Input);if(!mappings.Contains(selected))mappings.Add(selected);
        MarkDirty();ColorButtons();
    }
    void OpenMacros_Click(object sender,RoutedEventArgs e)=>ShowMacroWindow(false,false);
    void OpenProfileManager_Click(object sender,RoutedEventArgs e)
    {
        var window=new ProfileManagerWindow(config.Profiles,config.ActiveProfile,config.AutoSwitchProfilesByCursor){Owner=this};if(window.ShowDialog()!=true)return;
        ApplyProfileManagerResult(window.ResultProfiles,window.ResultActiveProfile,window.ResultAutoSwitchProfilesByCursor);
    }
    void ApplyProfileManagerResult(IReadOnlyList<Profile> profiles,string activeProfile,bool autoSwitch)
    {
        config.Profiles=profiles.ToList();
        config.ActiveProfile=activeProfile;
        config.AutoSwitchProfilesByCursor=autoSwitch;
        automaticProfileReturnName="";
        explicitProfileSwitchProcess="";
        ResetAutomaticProfileCandidate();
        ClearSelectedInput();RefreshProfiles();UpdateLayerButtons();
        // The dialog's primary action is Apply. Profile routing must become live
        // immediately even when assignment auto-save is disabled.
        SaveAndApply("プロファイル設定を保存し、反映しました");
    }
    void OpenGestureManager_Click(object sender,RoutedEventArgs e)
    {
        var window=new GestureManagerWindow(config.Gestures,config.Profiles,config.Macros,config.KeyboardLayout){Owner=this};if(window.ShowDialog()!=true)return;
        config.Gestures=window.ResultGestures.ToList();
        config.Profiles=window.ResultProfiles.ToList();
        ClearSelectedInput();RefreshProfiles();UpdateLayerButtons();MarkDirty();
    }
    void BuildDeckManagementPanel()
    {
        DeckManagementGrid.Children.Clear();
        deckManagementButtons.Clear();
        deckManagementNameLabels.Clear();
        for(int slot=1;slot<=DeckPanelLayout.SlotCount;slot++)
        {
            int capturedSlot=slot;
            var button=new System.Windows.Controls.Button
            {
                Tag=DeckPanelLayout.InputName(slot),
                Margin=new Thickness(2),
                MinWidth=0,
                MinHeight=0,
                Padding=new Thickness(3),
                FontSize=11,
                HorizontalContentAlignment=System.Windows.HorizontalAlignment.Center,
                VerticalContentAlignment=System.Windows.VerticalAlignment.Center,
                Style=(Style)FindResource("DeckButtonStyle")
            };
            button.Click+=(_,_)=>SelectInput(DeckPanelLayout.InputName(capturedSlot));
            button.PreviewMouseRightButtonDown+=InputButton_RightClick;
            button.Width=DeckPanelLayout.KeyWidth;
            button.Height=DeckPanelLayout.KeyHeight;
            button.Margin=new Thickness(2,0,2,0);
            var nameLabel=DeckPanelLayout.CreateNameLabel(null);
            var cell=new StackPanel{Width=DeckPanelLayout.KeyWidth+DeckPanelLayout.Gap,Height=70};
            cell.Children.Add(button);
            cell.Children.Add(nameLabel);
            DeckManagementGrid.Children.Add(cell);
            deckManagementButtons.Add(button);
            deckManagementNameLabels[button]=nameLabel;
        }
        ColorDeckManagementButtons();
    }
    void OpenDeckPanelManager_Click(object sender,RoutedEventArgs e)
    {
        deckManagementMode=true;
        currentLayer=DeckPanelLayout.Layer;
        KeyboardWorkspace.Visibility=Visibility.Collapsed;
        DeckWorkspace.Visibility=Visibility.Visible;
        DetectInputButton.Visibility=Visibility.Collapsed;
        DeckKeypadInputButton.Visibility=Visibility.Visible;
        LongPressExpander.Visibility=Visibility.Collapsed;
        LongPressOnlyButton.Visibility=Visibility.Collapsed;
        KindBox.ItemsSource=ActionOptions(allowGesture:false).Where(x=>x.Kind!=ActionKind.Gesture).ToArray();
        KindBox.SelectedValuePath=nameof(ActionOption.Kind);
        UpdateDeckScopeUi();
        ClearSelectedInput();
        UpdateLayerButtons();
        ColorButtons();
    }
    void ShowKeyboardWorkspace()
    {
        if(!deckManagementMode)return;
        deckManagementMode=false;
        KeyboardWorkspace.Visibility=Visibility.Visible;
        DeckWorkspace.Visibility=Visibility.Collapsed;
        DetectInputButton.Visibility=Visibility.Visible;
        DeckKeypadInputButton.Visibility=Visibility.Collapsed;
        LongPressExpander.Visibility=Visibility.Visible;
        LongPressOnlyButton.Visibility=Visibility.Visible;
        KindBox.ItemsSource=ActionOptions(allowGesture:true);
        KindBox.SelectedValuePath=nameof(ActionOption.Kind);
        DeckNameEditorPanel.Visibility=Visibility.Collapsed;
        UpdateDeckScopeUi();
    }
    void SharedDeckPanelChanged(object sender,RoutedEventArgs e)
    {
        if(loading||config==null)return;
        bool enable=SharedDeckPanelBox.IsChecked==true;
        if(enable)
        {
            var candidates=DeckPanelLayout.ProfilesWithDeckMappings(config.Profiles);
            Profile? source=candidates.FirstOrDefault(x=>x.Name.Equals(config.ActiveProfile,StringComparison.OrdinalIgnoreCase))??candidates.FirstOrDefault();
            if(candidates.Count>1&&DeckPanelLayout.DistinctDeckCount(candidates)>1)
            {
                source=SelectSharedDeckSource(candidates);
                if(source==null)
                {
                    loading=true;SharedDeckPanelBox.IsChecked=false;loading=false;
                    UpdateDeckScopeUi();
                    return;
                }
            }
            EnableSharedDeck(source);
        }
        else config.UseSharedDeckPanel=false;
        ClearSelectedInput(SharedDeckPanelBox);UpdateDeckScopeUi();UpdateLayerButtons();ColorButtons();MarkDirty();
    }
    void EnableSharedDeck(Profile? source)
    {
        config.SharedDeckMappings=source?.Mappings.Where(x=>DeckPanelLayout.IsInputName(x.Input)).Select(CloneMapping).ToList()??[];
        config.UseSharedDeckPanel=true;
    }
    internal void EnableSharedDeckForTest(string? profileName)
        =>EnableSharedDeck(config.Profiles.FirstOrDefault(x=>x.Name.Equals(profileName??"",StringComparison.OrdinalIgnoreCase)));
    Profile? SelectSharedDeckSource(IReadOnlyList<Profile> candidates)
    {
        var dialog=new Window{Title="共通Deckの選択",Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Width=460,Height=390,ResizeMode=ResizeMode.NoResize,Background=ThemeService.Brush("SurfaceBackground"),Foreground=ThemeService.Brush("PrimaryText"),ShowInTaskbar=false};
        var grid=new Grid{Margin=new Thickness(22)};grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        grid.Children.Add(new TextBlock{Text="共通Deckの元にするプロファイルを選択してください。各プロファイルの元のDeckは残ります。",Foreground=ThemeService.Brush("SecondaryText"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        var list=new ListBox{ItemsSource=candidates,DisplayMemberPath="Name",SelectedItem=candidates.FirstOrDefault(x=>x.Name.Equals(config.ActiveProfile,StringComparison.OrdinalIgnoreCase))??candidates[0],Background=ThemeService.Brush("CardBackground"),Foreground=ThemeService.Brush("PrimaryText"),BorderBrush=ThemeService.Brush("BorderBrush"),Padding=new Thickness(6)};Grid.SetRow(list,1);grid.Children.Add(list);
        var buttons=new StackPanel{Orientation=System.Windows.Controls.Orientation.Horizontal,HorizontalAlignment=System.Windows.HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};var cancel=new System.Windows.Controls.Button{Content="キャンセル",Height=40,Margin=new Thickness(6,0,0,0),Style=(Style)System.Windows.Application.Current.FindResource("AppButtonStyle"),IsCancel=true};var ok=new System.Windows.Controls.Button{Content="このDeckを使用",Height=40,Margin=new Thickness(6,0,0,0),Style=(Style)System.Windows.Application.Current.FindResource("AccentAppButtonStyle"),IsDefault=true};cancel.Click+=(_,_)=>dialog.DialogResult=false;ok.Click+=(_,_)=>{if(list.SelectedItem!=null)dialog.DialogResult=true;};buttons.Children.Add(cancel);buttons.Children.Add(ok);Grid.SetRow(buttons,2);grid.Children.Add(buttons);dialog.Content=grid;FollowWindowsTitleBarTheme(dialog);
        return dialog.ShowDialog()==true?list.SelectedItem as Profile:null;
    }
    void UpdateDeckScopeUi()
    {
        if(SharedDeckPanelBox==null||ProfileBox==null)return;
        bool shared=config?.UseSharedDeckPanel==true;
        loading=true;SharedDeckPanelBox.IsChecked=shared;loading=false;
        SharedDeckPanelBox.Visibility=deckManagementMode?Visibility.Visible:Visibility.Collapsed;
        ProfileBox.IsEnabled=!(deckManagementMode&&shared);
        ProfileBox.Opacity=ProfileBox.IsEnabled?1:.45;
    }
    void PersistDeckPanelPosition(double left,double top)
    {
        if(!double.IsFinite(left)||!double.IsFinite(top))return;
        config.DeckPanelLeft=left;config.DeckPanelTop=top;appliedConfig.DeckPanelLeft=left;appliedConfig.DeckPanelTop=top;
        try{var persisted=store.Load();persisted.DeckPanelLeft=left;persisted.DeckPanelTop=top;store.Save(persisted);}catch{}
    }
    void DeckKeypadInput_Click(object sender,RoutedEventArgs e)
    {
        if(!deckManagementMode||selected==null)
        {
            ShowInlineNotice("先にDeckパネルのボタンを選択してください");
            return;
        }
        var picker=new MacroInputPickerWindow(config.KeyboardLayout){Owner=this};
        bool shortcut=(KindBox.SelectedValue is ActionKind kind?kind:EditorActionKind(selected.Kind))==ActionKind.Shortcut;
        if(shortcut)
        {
            picker.ConfigureShortcutEditing(ValueBox.Text);
            picker.ShortcutChanged+=value=>
            {
                KindBox.SelectedValue=ActionKind.Shortcut;
                ValueBox.Text=value;
            };
        }
        else
        {
            picker.InputChosen+=input=>
            {
                KindBox.SelectedValue=ActionKind.Key;
                ValueBox.Text=input;
            };
        }
        picker.ShowDialog();
    }
    void ApplyProfileAction(string profileName,bool longPress)
    {
        if(selected==null||!config.Profiles.Any(x=>x.Name.Equals(profileName,StringComparison.OrdinalIgnoreCase)))return;
        loading=true;
        try
        {
            if(longPress)
            {
                selected.LongPressKind=ActionKind.Profile;selected.LongPressValue=profileName;
                LongKindBox.SelectedValue=ActionKind.Profile;LongValueBox.Text=ProfileDisplayValue(profileName);LongPressExpander.IsExpanded=true;
            }
            else
            {
                selected.Kind=ActionKind.Profile;selected.Value=profileName;
                KindBox.SelectedValue=ActionKind.Profile;ValueBox.Text=ProfileDisplayValue(profileName);
            }
        }
        finally{loading=false;}
        var mappings=MappingCollectionForInput(selected.Input);if(!mappings.Contains(selected))mappings.Add(selected);
        UpdateBrowseButtons();MarkDirty();ColorButtons();
        ClearExecutionFocus(longPress?LongKindBox:KindBox);
    }
    internal void ApplyProfileActionForTest(string profileName,bool longPress)=>ApplyProfileAction(profileName,longPress);
    void ShowMacroWindow(bool assign,bool longPress)
    {
        string target=assign?$"{InputName.Text}（{(longPress?"長押し":"短押し")}）":"";var window=new MacroWindow(config,SetMacroRecording,assign,target){Owner=this};window.Saved+=()=>SaveAndApply("マクロを保存して反映しました");macroWindow=window;bool? result=window.ShowDialog();macroWindow=null;SetMacroRecording(false,false,false);
        if(!window.SaveRequested&&window.Changed)MarkDirty();if(!assign||result!=true||string.IsNullOrWhiteSpace(window.SelectedMacroName))return;
        if(longPress){LongKindBox.SelectedValue=ActionKind.Macro;LongValueBox.Text=window.SelectedMacroName;LongPressExpander.IsExpanded=true;FocusExecutionValue(LongValueBox,true);}else{KindBox.SelectedValue=ActionKind.Macro;ValueBox.Text=window.SelectedMacroName;FocusExecutionValue(ValueBox);}
    }
    void SetMacroRecording(bool recording,bool captureMouseMoves,bool useMappedActions)
    {
        if(recording){if(macroIsRecording)return;macroIsRecording=true;macroEmergencyStop=false;engineBeforeMacroRecording=engine.Enabled;MacroPlayer.StopAll();engine.Enabled=useMappedActions&&engineStarted;engine.CaptureMouseMoves=captureMouseMoves;EngineStatus.Text=useMappedActions?"● マクロ記録中（割り当て後のアクション）":captureMouseMoves?"● マクロ記録中（マウス軌跡あり）":"● マクロ記録中（物理キー）";EngineStatus.Foreground=ThemeService.Brush("WarningBrush");}
        else{if(!macroIsRecording)return;macroIsRecording=false;engine.CaptureMouseMoves=false;engine.Enabled=engineBeforeMacroRecording&&config.EngineEnabled&&!macroEmergencyStop;UpdateStatus();}
    }
    void OpenApplicationPicker(bool longPress)
    {
        var dialog=new ApplicationPickerWindow{Owner=this};
        if(dialog.ShowDialog()==true&&!string.IsNullOrWhiteSpace(dialog.SelectedPath))ApplyApplicationSelection(longPress,dialog.SelectedPath);
    }
    void ApplyApplicationSelection(bool longPress,string path)
    {
        if(longPress){LongKindBox.SelectedValue=ActionKind.Launch;LongValueBox.Text=path;FocusExecutionValue(LongValueBox,true);}
        else{KindBox.SelectedValue=ActionKind.Launch;ValueBox.Text=path;FocusExecutionValue(ValueBox);}
        MarkDirty();
    }

    void SelectInput(string input,bool focusExecution=true)
    {
        string layer="通常";selectedBaseInput=input;
        int plus=input.IndexOf('+');if(plus>0){layer=input[..plus];selectedBaseInput=input[(plus+1)..];}
        var mappings=MappingCollectionForInput(input);
        var visibleAssignment=DeckPanelLayout.IsInputName(input)?mappings.LastOrDefault(x=>x.Input.Equals(input,StringComparison.OrdinalIgnoreCase)&&MappingInterceptsInput(x)):FindProfileMapping(config.Profiles,CurrentProfile.Name,input,MappingInterceptsInput);
        detectMode=false;editingSelectedInput=focusExecution;selected=SelectEditorMapping(mappings,visibleAssignment,input);
        currentLayer=layer;loading = true; InputName.Text = selected.Input;InputDisplayText.Text=DisplayInputName(selected.Input);KindBox.SelectedValue = selected.Kind==ActionKind.Gesture?ActionKind.Gesture:EditorActionKind(selected.Kind); ValueBox.Text = DisplayActionValue(selected.Kind,selected.Value); LongKindBox.SelectedValue=EditorActionKind(selected.LongPressKind); LongValueBox.Text=DisplayActionValue(selected.LongPressKind,selected.LongPressValue); LongPressBox.Text = selected.LongPressMs.ToString(); LongPressExpander.IsExpanded=HasConfiguredLongPress(selected);DeckNameBox.Text=selected.Description??"";loading = false;
        InspectorEmptyState.Visibility=Visibility.Collapsed;SelectionHeader.Visibility=Visibility.Visible;AssignmentEditor.Visibility=Visibility.Visible;AssignmentEditor.IsEnabled=true;DeckNameEditorPanel.Visibility=deckManagementMode?Visibility.Visible:Visibility.Collapsed;
        UpdateBrowseButtons();UpdateLayerButtons();ColorButtons();ShowAssignmentPane();if(focusExecution&&ShouldFocusExecutionForSelectedInput(visibleAssignment))FocusExecutionValue(ValueBox);
    }
    internal static Mapping SelectEditorMapping(IReadOnlyList<Mapping> currentMappings,Mapping? visibleAssignment,string input)
    {
        var direct=currentMappings.LastOrDefault(x=>x.Input.Equals(input,StringComparison.OrdinalIgnoreCase));
        if(direct!=null)return direct;
        if(visibleAssignment!=null){var inherited=CloneMapping(visibleAssignment);inherited.Input=input;return inherited;}
        return new Mapping{Input=input,Kind=ActionKind.None};
    }
    internal static bool ShouldFocusExecutionForSelectedInput(Mapping? visibleAssignment)=>visibleAssignment==null;
    internal static string DisplayInputName(string input)
    {
        if(string.IsNullOrWhiteSpace(input))return "キーを選択してください";
        return string.Join(" + ",input.Split('+',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(DisplayInputPart));
    }
    static string DisplayInputPart(string value)=>value switch
    {
        "通常"=>"通常","Space"=>"Space","CapsLock"=>"CapsLock","MouseRight"=>"右クリック","MouseBack"=>"戻る","MouseForward"=>"進む","Taskbar"=>"タスクバー上",DeckPanelLayout.Layer=>"Deck",
        "MouseLeft"=>"左クリック","MouseMiddle"=>"ホイールクリック","MouseX"=>"追加ボタン","WheelUp"=>"ホイール上","WheelDown"=>"ホイール下","TiltLeft"=>"チルト左","TiltRight"=>"チルト右",_=>value
    };
    void EditorChanged(object sender, EventArgs e)
    {
        if (loading || selected == null) return;
        if(ReferenceEquals(sender,ValueBox)&&KindBox.SelectedValue is not ActionKind&&!string.IsNullOrWhiteSpace(ValueBox.Text)){loading=true;KindBox.SelectedValue=ActionKind.Key;loading=false;}
        var longEditorKind=LongKindBox.SelectedValue is ActionKind lk?lk:EditorActionKind(selected.LongPressKind);if(ReferenceEquals(sender,LongKindBox)&&longEditorKind==ActionKind.None&&!string.IsNullOrEmpty(LongValueBox.Text)){loading=true;LongValueBox.Clear();loading=false;}
        var shortAction=NormalizeEditorAction(KindBox.SelectedValue is ActionKind k?k:EditorActionKind(selected.Kind),ValueBox.Text,selected.Kind,selected.Value);
        var longAction=NormalizeEditorAction(longEditorKind,LongValueBox.Text,selected.LongPressKind,selected.LongPressValue);
        selected.Kind=shortAction.Kind;selected.Value=shortAction.Value;
        if(shortAction.Kind==ActionKind.Gesture){selected.LongPressKind=ActionKind.None;selected.LongPressValue="";}
        else{selected.LongPressKind=longAction.Kind;selected.LongPressValue=longAction.Value;}
        selected.Layer=currentLayer;if (int.TryParse(LongPressBox.Text, out var ms)) selected.LongPressMs = ms;
        if(MappingHasConfiguredAction(selected))
        {
            var mappings=MappingCollectionForInput(selected.Input);if(!mappings.Contains(selected))mappings.Add(selected);
        }
        else if(deckManagementMode&&!string.IsNullOrWhiteSpace(selected.Description))
        {
            var mappings=MappingCollectionForInput(selected.Input);if(!mappings.Contains(selected))mappings.Add(selected);
        }
        else MappingCollectionForInput(selected.Input).Remove(selected);
        UpdateBrowseButtons();
        UpdateLayerButtons();MarkDirty();ColorButtons();
        if(ReferenceEquals(sender,KindBox))FocusExecutionValue(ValueBox);
        else if(ReferenceEquals(sender,LongKindBox))FocusExecutionValue(LongValueBox,true);
    }
    static string ProfileDisplayValue(string profileName)=>"プロファイル："+profileName;
    static string GestureDisplayValue(string gestureName)=>"ジェスチャー："+gestureName;
    internal static string DisplayActionValue(ActionKind kind,string value)=>kind switch{ActionKind.Profile=>ProfileDisplayValue(value),ActionKind.Gesture=>GestureDisplayValue(value),ActionKind.Mouse=>ActionCatalog.DisplayMouseAction(value),_=>value};
    static ActionKind EditorActionKind(ActionKind kind)=>kind switch{ActionKind.Mouse=>ActionKind.Shortcut,_=>kind};
    internal static (ActionKind Kind,string Value) NormalizeEditorAction(ActionKind editorKind,string? value,ActionKind existingKind=ActionKind.None,string? existingValue=null)
    {
        string original=value??"";
        if(editorKind==ActionKind.Profile)
        {
            const string prefix="プロファイル：";
            string profileName=original.StartsWith(prefix,StringComparison.Ordinal)?original[prefix.Length..].Trim():original.Trim();
            return (ActionKind.Profile,profileName);
        }
        if(editorKind==ActionKind.Gesture||(editorKind==ActionKind.Shortcut&&existingKind==ActionKind.Gesture&&original.StartsWith("ジェスチャー：",StringComparison.Ordinal)))
        {
            string gestureName=original.StartsWith("ジェスチャー：",StringComparison.Ordinal)?original["ジェスチャー：".Length..]:original;
            return string.IsNullOrWhiteSpace(gestureName)?(ActionKind.None,""):(ActionKind.Gesture,gestureName);
        }
        if(editorKind==ActionKind.Disabled)return (ActionKind.Disabled,"");
        if(editorKind!=ActionKind.Shortcut)return (editorKind,original);
        return ActionCatalog.TryNormalizeMouseAction(original,out string mouseAction)?(ActionKind.Mouse,mouseAction):(editorKind,original.Trim());
    }
    void UpdateBrowseButtons()
    {
        // A short-press gesture owns the complete press/move/release lifecycle, so it
        // cannot safely coexist with an independent long-press action.
        bool shortGestureSelected=selected?.Kind==ActionKind.Gesture;
        bool legacyLongGestureSelected=selected?.LongPressKind==ActionKind.Gesture;
        ValueBox.IsReadOnly=shortGestureSelected;
        ValueBox.IsTabStop=!shortGestureSelected;
        ValueBox.Opacity=shortGestureSelected ? .72 : 1;
        ValueBox.ToolTip=shortGestureSelected
            ?"ジェスチャー名は直接編集できません。変更する場合は「ショートカット」から別のジェスチャーを選んでください。"
            :"Ctrlを押しながらCなど、実際のキーボードでもショートカットを入力できます";
        LongValueBox.IsReadOnly=legacyLongGestureSelected;
        LongValueBox.IsTabStop=!legacyLongGestureSelected;
        LongValueBox.Opacity=legacyLongGestureSelected ? .72 : 1;
        LongPressExpander.IsEnabled=!shortGestureSelected;
        LongPressExpander.Opacity=shortGestureSelected ? .58 : 1;
        LongPressExpander.Header=shortGestureSelected?"＋ 長押し（ジェスチャーでは設定できません）":"＋ 長押しを追加（任意）";
        if(shortGestureSelected)LongPressExpander.IsExpanded=false;
    }
    bool HandleInput(string input)
    {
        const string gestureMarker=":Gesture:";
        int gestureIndex=input.LastIndexOf(gestureMarker,StringComparison.OrdinalIgnoreCase);
        if(gestureIndex>=0)
        {
            string baseGestureInput=input[..gestureIndex],direction=input[(gestureIndex+gestureMarker.Length)..];
            activeInputMappings.TryGetValue(baseGestureInput,out var captured);
            var source=captured?.Mapping??FindMapping(baseGestureInput);
            string? gestureName=source?.Kind==ActionKind.Gesture?source.Value:source?.LongPressKind==ActionKind.Gesture?source.LongPressValue:null;
            var definition=captured?.Gesture??(gestureName==null?null:appliedConfig.Gestures.FirstOrDefault(x=>x.Name.Equals(gestureName,StringComparison.OrdinalIgnoreCase)));
            if(source==null||definition==null)return false;
            var action=GestureAction(definition,direction);
            if(action.Kind is ActionKind.None or ActionKind.Disabled||string.IsNullOrWhiteSpace(action.Value))return action.Kind==ActionKind.Disabled;
            var gestureMap=new Mapping{Input=source.Input,Layer=source.Layer,Kind=action.Kind,Value=action.Value};
            if(!actionQueue.TryAdd((gestureMap,input,false)))Dispatcher.BeginInvoke(()=>{LastInput.Text="連続入力が多すぎるため一部を安全に破棄しました";LastInput.Foreground=ThemeService.Brush("DangerBrush");});else RecordMappedAction(gestureMap,input);
            return true;
        }
        bool longPress=input.EndsWith(":Long",StringComparison.OrdinalIgnoreCase),dragStart=input.EndsWith(":DragStart",StringComparison.OrdinalIgnoreCase),dragEnd=input.EndsWith(":DragEnd",StringComparison.OrdinalIgnoreCase),pressStart=input.EndsWith(":PressStart",StringComparison.OrdinalIgnoreCase),pressEnd=input.EndsWith(":PressEnd",StringComparison.OrdinalIgnoreCase);
        string baseInput=longPress?input[..^5]:dragStart?input[..^10]:dragEnd?input[..^8]:pressStart?input[..^11]:pressEnd?input[..^9]:input;
        var map=activeInputMappings.TryGetValue(baseInput,out var capturedInput)?capturedInput.Mapping:FindMapping(baseInput);
        if(map==null){if(dragEnd||pressEnd){if(!QueueDragAction(null,input))_=Task.Run(InputEngine.EndModifierDrag);return true;}return false;}
        var snapshot=CloneMapping(map);
        if((pressStart||pressEnd||dragStart||dragEnd)&&MappingExecutor.IsModifierDrag(snapshot.Value)){bool queued=QueueDragAction(snapshot,input);if(queued)RecordMappedAction(snapshot,input);return queued;}
        if(pressStart||pressEnd)return false;
        if(!actionQueue.TryAdd((snapshot,input,false)))Dispatcher.BeginInvoke(()=>{LastInput.Text="連続入力が多すぎるため一部を安全に破棄しました";LastInput.Foreground=ThemeService.Brush("DangerBrush");});else RecordMappedAction(snapshot,input);return true;
    }
    void CaptureInputMapping(string input)
    {
        var mapping=FindMapping(input);
        if(mapping==null)return;
        GestureDefinition? gesture=null;
        string? gestureName=mapping.Kind==ActionKind.Gesture?mapping.Value:mapping.LongPressKind==ActionKind.Gesture?mapping.LongPressValue:null;
        if(!string.IsNullOrWhiteSpace(gestureName)&&appliedConfig.Gestures.FirstOrDefault(x=>x.Name.Equals(gestureName,StringComparison.OrdinalIgnoreCase)) is { } definition)
            gesture=CloneGesture(definition);
        activeInputMappings[input]=new InputMappingSnapshot(CloneMapping(mapping),gesture);
    }
    void ReleaseInputMapping(string input)=>activeInputMappings.TryRemove(input,out _);
    void CaptureLayerMappings(string layer)
    {
        var profile=appliedConfig.Profiles.FirstOrDefault(x=>x.Name.Equals(appliedConfig.ActiveProfile,StringComparison.OrdinalIgnoreCase))
            ??appliedConfig.Profiles.FirstOrDefault();
        if(profile==null)return;
        activeLayerMappings[layer]=new LayerMappingSnapshot(
            profile.Mappings.Where(x=>AppMatches(x.Application)).Select(CloneMapping).ToArray());
    }
    void ReleaseLayerMappings(string layer)=>activeLayerMappings.TryRemove(layer,out _);
    internal static (ActionKind Kind,string Value) GestureAction(GestureDefinition gesture,string direction)=>direction switch
    {
        "Up"=>(gesture.UpKind,gesture.UpValue),"Down"=>(gesture.DownKind,gesture.DownValue),
        "Left"=>(gesture.LeftKind,gesture.LeftValue),"Right"=>(gesture.RightKind,gesture.RightValue),
        _=>(gesture.CenterKind,gesture.CenterValue)
    };
    void RecordMappedAction(Mapping map,string input){if(macroIsRecording&&config.RecordMappedActionsInMacros)Dispatcher.BeginInvoke(()=>macroWindow?.CaptureMappedAction(map,input));}
    AppConfig DeckExecutionConfig()
    {
        var snapshot=store.Clone(appliedConfig);
        snapshot.WindowActionTarget=WindowActionTarget.ActiveWindow;
        return snapshot;
    }
    void ProcessActions()
    {
        foreach(var item in actionQueue.GetConsumingEnumerable())try{bool result=(item.ForceActiveWindow?deckExecutor:executor).Execute(item.Map,item.Input,out var value);if(result)Dispatcher.BeginInvoke(()=>{LastInput.Text=$"実行: {item.Map.Input} → {value}";LastInput.Foreground=value.StartsWith("エラー:",StringComparison.Ordinal)?ThemeService.Brush("DangerBrush"):ThemeService.Brush("AccentBrush");});}catch(Exception ex){InputEngine.ReleaseAll();Dispatcher.BeginInvoke(()=>{LastInput.Text="実行エラー: "+ex.Message;LastInput.Foreground=ThemeService.Brush("DangerBrush");});}
    }
    void ProcessDragActions()
    {
        foreach(var item in dragActionQueue.GetConsumingEnumerable())try{if(item.Map==null){InputEngine.EndModifierDrag();continue;}bool result=executor.Execute(item.Map,item.Input,out var value);if(result)Dispatcher.BeginInvoke(()=>{LastInput.Text=$"実行: {item.Map.Input} → {value}";LastInput.Foreground=value.StartsWith("エラー:",StringComparison.Ordinal)?ThemeService.Brush("DangerBrush"):ThemeService.Brush("AccentBrush");});}catch(Exception ex){InputEngine.ReleaseAll();Dispatcher.BeginInvoke(()=>{LastInput.Text="ドラッグ実行エラー: "+ex.Message;LastInput.Foreground=ThemeService.Brush("DangerBrush");});}
    }
    bool QueueDragAction(Mapping? map,string input){try{return !dragActionQueue.IsAddingCompleted&&dragActionQueue.TryAdd((map,input));}catch(InvalidOperationException){return false;}}
    string QualifyInput(string input)
    {
        if(input.StartsWith("Taskbar+",StringComparison.OrdinalIgnoreCase)||!ConditionMatcher.IsCursorOverTaskbar())return input;
        string taskbarInput="Taskbar+"+input;
        return FindProfileMapping(appliedConfig.Profiles,AppliedProfile.Name,taskbarInput,x=>MappingInterceptsInput(x)&&AppMatches(x.Application))!=null?taskbarInput:input;
    }
    Mapping? FindMapping(string input)
    {
        if(TryGetLayerMappingSnapshot(input,out var layerSnapshot))
        {
            if(!input.StartsWith("Taskbar+",StringComparison.OrdinalIgnoreCase)&&ConditionMatcher.IsCursorOverTaskbar())
            {
                string taskbarInput="Taskbar+"+input;
                if(layerSnapshot.Mappings.LastOrDefault(x=>x.Input.Equals(taskbarInput,StringComparison.OrdinalIgnoreCase)) is { } taskbarMapping)
                    return taskbarMapping;
            }
            return layerSnapshot.Mappings.LastOrDefault(x=>x.Input.Equals(input,StringComparison.OrdinalIgnoreCase));
        }
        if(input.StartsWith("Taskbar+",StringComparison.OrdinalIgnoreCase))return FindProfileMapping(appliedConfig.Profiles,AppliedProfile.Name,input,x=>MappingInterceptsInput(x)&&AppMatches(x.Application));
        string qualified=QualifyInput(input);if(!qualified.Equals(input,StringComparison.OrdinalIgnoreCase))return FindProfileMapping(appliedConfig.Profiles,AppliedProfile.Name,qualified,x=>MappingInterceptsInput(x)&&AppMatches(x.Application));
        return FindProfileMapping(appliedConfig.Profiles,AppliedProfile.Name,input,x=>MappingInterceptsInput(x)&&AppMatches(x.Application));
    }
    bool HasMapping(string input)
    {
        if(input.EndsWith("+*",StringComparison.Ordinal))
        {
            string prefix=input[..^1];
            if(TryGetLayerMappingSnapshot(input,out var layerSnapshot))
                return layerSnapshot.Mappings.Any(x=>MappingInterceptsInput(x)&&x.Input.StartsWith(prefix,StringComparison.OrdinalIgnoreCase));
            return FindProfileMapping(appliedConfig.Profiles,AppliedProfile.Name,null,x=>MappingInterceptsInput(x)&&x.Input.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&AppMatches(x.Application))!=null;
        }
        return MappingInterceptsInput(FindMapping(input));
    }
    bool TryGetLayerMappingSnapshot(string input,out LayerMappingSnapshot snapshot)
    {
        string candidate=input.StartsWith("Taskbar+",StringComparison.OrdinalIgnoreCase)?input["Taskbar+".Length..]:input;
        foreach(var pair in activeLayerMappings)
            if(candidate.Equals(pair.Key,StringComparison.OrdinalIgnoreCase)||candidate.StartsWith(pair.Key+"+",StringComparison.OrdinalIgnoreCase))
            {
                snapshot=pair.Value;
                return true;
            }
        snapshot=null!;
        return false;
    }
    internal static bool MappingHasConfiguredAction(Mapping? map)=>HasConfiguredShortAction(map)||HasConfiguredLongPress(map);
    internal static bool HasConfiguredShortAction(Mapping? map)=>map!=null&&map.Kind!=ActionKind.None&&(map.Kind==ActionKind.Disabled||!string.IsNullOrWhiteSpace(map.Value));
    internal static bool MappingInterceptsInput(Mapping? map)=>MappingHasConfiguredAction(map);
    internal static bool HasConfiguredLongPress(Mapping? map)=>map!=null&&map.LongPressKind!=ActionKind.None&&(map.LongPressKind==ActionKind.Disabled||!string.IsNullOrWhiteSpace(map.LongPressValue));
    internal static void NormalizeLongOnlyMapping(Mapping map){if(!HasConfiguredShortAction(map)&&HasConfiguredLongPress(map))map.Kind=ActionKind.None;}
    internal static Mapping? FindProfileMapping(IReadOnlyList<Profile> profiles,string activeName,string? exactInput,Func<Mapping,bool>? predicate=null)
    {
        if(profiles.Count==0)return null;var active=profiles.FirstOrDefault(x=>x.Name==activeName)??profiles[0];
        return active.Mappings.LastOrDefault(x=>(exactInput==null||x.Input.Equals(exactInput,StringComparison.OrdinalIgnoreCase))&&(predicate?.Invoke(x)??true));
    }
    static bool AppMatches(string app)=>ConditionMatcher.ForegroundProcessMatches(app);

    void RefreshProfiles() { loading = true; ProfileBox.ItemsSource = null; ProfileBox.ItemsSource = config.Profiles.Select(x => x.Name).ToList(); ProfileBox.SelectedItem = config.ActiveProfile; loading = false; ColorButtons(); }
    void ColorButtons()
    {
        var keyboardButtons=InputButtons(KeyboardPanel).Concat(InputButtons(SecondaryKeyboardPanel)).ToHashSet();
        foreach(var b in VisualInputButtons())
        {
            bool reserved=((string)b.Tag=="Space"&&currentLayer is "通常" or "Space")||
                          ((string)b.Tag=="CapsLock"&&!editingSelectedInput&&destinationInputTarget==null);
            string input=currentLayer=="通常"?(string)b.Tag:currentLayer+"+"+(string)b.Tag;
            var assigned=FindProfileMapping(config.Profiles,CurrentProfile.Name,input,MappingInterceptsInput);
            bool editing=editingSelectedInput&&selected?.Input.Equals(input,StringComparison.OrdinalIgnoreCase)==true;
            b.Background=editing?ThemeService.Brush("EditingKeyBackground"):reserved?ThemeService.Brush("ReservedKeyBackground"):assigned!=null?new SolidColorBrush(AssignmentColorFor(assigned)):ThemeService.Brush("KeyBackground");
            b.BorderBrush=editing?ThemeService.Brush("EditingKeyBorderBrush"):ThemeService.Brush("SubtleBorderBrush");
            b.Foreground=editing?WpfBrushes.White:assigned==null?ThemeService.Brush("PrimaryText"):new SolidColorBrush(AssignmentTextColorFor(assigned));
            b.Opacity=reserved ? 0.48 : 1;
            b.ToolTip=assigned!=null?CreateAssignmentToolTip(assigned):keyboardButtons.Contains(b)?null:DefaultMouseToolTip((string)b.Tag);
            ToolTipService.SetInitialShowDelay(b,250);ToolTipService.SetBetweenShowDelay(b,80);ToolTipService.SetShowDuration(b,20000);
        }
        ColorDeckManagementButtons();
    }
    void ColorDeckManagementButtons()
    {
        foreach(var button in deckManagementButtons)
        {
            if(button.Tag is not string input)continue;
            var mapping=DeckPanelLayout.FindMapping(config,DeckPanelLayout.SlotNumber(input));
            var assigned=MappingInterceptsInput(mapping)?mapping:null;
            bool editing=editingSelectedInput&&selected?.Input.Equals(input,StringComparison.OrdinalIgnoreCase)==true;
            button.Background=editing?ThemeService.Brush("EditingKeyBackground"):assigned!=null?new SolidColorBrush(AssignmentColorFor(assigned)):ThemeService.Brush("KeyBackground");
            button.BorderBrush=editing?ThemeService.Brush("EditingKeyBorderBrush"):ThemeService.Brush("SubtleBorderBrush");
            button.Foreground=editing?WpfBrushes.White:assigned==null?ThemeService.Brush("PrimaryText"):new SolidColorBrush(AssignmentTextColorFor(assigned));
            button.Content=DeckPanelLayout.CreateButtonContent(input,mapping);
            if(deckManagementNameLabels.TryGetValue(button,out var nameLabel))nameLabel.Text=mapping?.Description??"";
            button.ToolTip=assigned!=null?CreateAssignmentToolTip(assigned):null;
            ToolTipService.SetInitialShowDelay(button,250);
            ToolTipService.SetShowDuration(button,20000);
        }
    }
    internal static WpfColor AssignmentColorFor(Mapping mapping)
    {
        var kind=AssignmentDisplayKind(mapping);
        return kind switch
        {
            ActionKind.Key=>WpfColor.FromRgb(217,130,43),
            ActionKind.Disabled=>WpfColor.FromRgb(98,107,120),
            ActionKind.Text=>WpfColor.FromRgb(205,160,45),
            ActionKind.Macro=>WpfColor.FromRgb(194,67,77),
            ActionKind.Launch=>WpfColor.FromRgb(139,91,190),
            ActionKind.Profile=>WpfColor.FromRgb(50,112,196),
            ActionKind.Gesture=>WpfColor.FromRgb(0,151,167),
            _=>WpfColor.FromRgb(23,141,121)
        };
    }
    static ActionKind AssignmentDisplayKind(Mapping mapping)=>!HasConfiguredShortAction(mapping)&&HasConfiguredLongPress(mapping)?mapping.LongPressKind:mapping.Kind==ActionKind.None?mapping.LongPressKind:mapping.Kind;
    static WpfColor AssignmentTextColorFor(Mapping mapping)=>AssignmentDisplayKind(mapping) is ActionKind.Text or ActionKind.Key?WpfColors.Black:WpfColors.White;
    static string AssignmentTypeLabel(Mapping mapping)=>AssignmentDisplayKind(mapping) switch{ActionKind.Key=>"別のキー",ActionKind.Disabled=>"無効化",ActionKind.Text=>"文字列",ActionKind.Macro=>"マクロ",ActionKind.Launch=>"アプリ・パス",ActionKind.Profile=>"プロファイル",ActionKind.Gesture=>"ジェスチャー",_=>"ショートカット"};
    internal static string? AssignmentToolTipText(Mapping? mapping)
    {
        if(!MappingInterceptsInput(mapping))return null;var lines=new List<string>();
        if(HasConfiguredShortAction(mapping)){lines.Add("短押し");lines.Add("アクション："+ActionKindDisplayName(mapping!.Kind));lines.Add("実行内容："+FriendlyActionValue(mapping.Kind,mapping.Value));}
        if(HasConfiguredLongPress(mapping)){if(lines.Count>0)lines.Add("");lines.Add($"長押し（{mapping!.LongPressMs} ms）");lines.Add("アクション："+ActionKindDisplayName(mapping.LongPressKind));lines.Add("実行内容："+FriendlyActionValue(mapping.LongPressKind,mapping.LongPressValue));}
        return string.Join(Environment.NewLine,lines);
    }
    internal static string ActionKindDisplayName(ActionKind kind)=>kind switch
    {
        ActionKind.Disabled=>"無効化",ActionKind.Key=>"別のキー",ActionKind.Shortcut=>"ショートカット",ActionKind.Text=>"文字列入力",
        ActionKind.Launch=>"アプリ・ファイル・URL",ActionKind.Mouse=>"マウス操作",ActionKind.Macro=>"マクロ",ActionKind.Profile=>"プロファイル切替",ActionKind.Gesture=>"ジェスチャー",_=>"未設定"
    };
    internal static string FriendlyActionValue(ActionKind kind,string value)
    {
        if(kind==ActionKind.Disabled)return "入力しない";
        if(kind==ActionKind.Profile)return ProfileDisplayValue(value);
        if(kind==ActionKind.Gesture)return GestureDisplayValue(value);
        if(kind==ActionKind.Mouse)return ActionCatalog.DisplayMouseAction(value);
        var catalog=ActionCatalog.Items.FirstOrDefault(x=>x.Kind==kind&&x.Value.Equals(value,StringComparison.OrdinalIgnoreCase));string display=catalog?.Name??(kind is ActionKind.Key or ActionKind.Shortcut or ActionKind.Mouse?DisplayInputName(value):value);
        if(string.IsNullOrWhiteSpace(display))display="未設定";return display.Length<=180?display:display[..180]+"…";
    }
    static System.Windows.Controls.ToolTip CreateAssignmentToolTip(Mapping mapping)=>new()
    {
        Content=new TextBlock{Text=AssignmentToolTipText(mapping),Foreground=ThemeService.Brush("PrimaryText"),TextWrapping=TextWrapping.Wrap,LineHeight=20,MaxWidth=340},
        Background=ThemeService.Brush("CardBackground"),BorderBrush=ThemeService.Brush("AccentBrush"),BorderThickness=new Thickness(1),Padding=new Thickness(12,9,12,9),Placement=System.Windows.Controls.Primitives.PlacementMode.Mouse
    };
    static string? DefaultMouseToolTip(string key)=>key switch
    {
        "MouseLeft"=>"左クリック","MouseRight"=>"右クリック／右クリックレイヤー","WheelUp"=>"ホイール上回転","MouseMiddle"=>"ホイールクリック（中央ボタン）","WheelDown"=>"ホイール下回転",
        "TiltLeft"=>"チルトホイール左","TiltRight"=>"チルトホイール右","MouseBack"=>"戻るボタン／戻るレイヤー","MouseForward"=>"進むボタン／進むレイヤー","MouseX"=>"追加マウスボタン",_=>null
    };
    IEnumerable<System.Windows.Controls.Button> VisualInputButtons()=>InputButtons(KeyboardPanel).Concat(InputButtons(SecondaryKeyboardPanel)).Concat(InputButtons(MousePanel));
    static IEnumerable<System.Windows.Controls.Button> InputButtons(System.Windows.Controls.Panel panel){foreach(UIElement child in panel.Children){if(child is System.Windows.Controls.Button b&&b.Tag is string)yield return b;if(child is System.Windows.Controls.Panel nested)foreach(var b2 in InputButtons(nested))yield return b2;}}
    static string LayerDisplayName(string layer)=>layer switch{"通常"=>"通常レイヤー","Taskbar"=>"タスクバー上で有効",DeckPanelLayout.Layer=>"Deckパネル",_=>layer+" レイヤー"};
    void MarkDirty()
    {
        if(config.AutoSave)
        {
            // Execution text is a draft until the user confirms it or clicks away.
            // This prevents a half-typed shortcut/path from reaching the live engine.
            if(destinationInputTarget!=null||editingSelectedInput)
            {
                autoSaveTimer.Stop();
                LastInput.Text="編集中 — 入力完了または欄外クリックで反映します";
                LastInput.Foreground=ThemeService.Brush("WarningBrush");
                return;
            }
            LastInput.Text="変更を自動保存しています…";LastInput.Foreground=ThemeService.Brush("AccentBrush");autoSaveTimer.Stop();autoSaveTimer.Start();
        }
        else{LastInput.Text="未保存の変更があります — 保存すると反映されます";LastInput.Foreground=ThemeService.Brush("WarningBrush");}
    }
    void ShowInlineNotice(string message)
    {
        LastInput.Text="ⓘ "+message;LastInput.Foreground=ThemeService.Brush("WarningBrush");
    }
    void UpdateLayerButtons()
    {
        if(NormalLayerButton==null)return;
        foreach(var b in new[]{NormalLayerButton,SpaceLayerButton,CapsLockLayerButton,TaskbarLayerButton,RightMouseLayerButton,BackMouseLayerButton,ForwardMouseLayerButton})
        {
            bool blocked=IsMouseLayerBlockedByDirectGesture(config.Profiles,CurrentProfile.Name,b.Tag?.ToString()??"");
            bool active=Equals(b.Tag,currentLayer);
            b.IsEnabled=!blocked;
            b.Background=active?ThemeService.Brush("LayerActiveBackground"):System.Windows.Media.Brushes.Transparent;
            b.BorderBrush=System.Windows.Media.Brushes.Transparent;
            b.Foreground=ThemeService.Brush("PrimaryText");
            b.ToolTip=blocked?$"通常レイヤーの{MouseLayerLabel(b.Tag?.ToString()??"")}にジェスチャーが割り当てられているため使用できません。ジェスチャーを削除すると再び使用できます。":null;
        }
        bool deckActive=deckManagementMode;
        DeckPanelManagerButton.Background=deckActive?ThemeService.Brush("LayerActiveBackground"):System.Windows.Media.Brushes.Transparent;
        DeckPanelManagerButton.BorderBrush=System.Windows.Media.Brushes.Transparent;
        DeckPanelManagerButton.Foreground=ThemeService.Brush("PrimaryText");
        WorkspaceTitle.Text=deckActive?"Deckパネル":"キーボード";
        WorkspaceSubtitle.Text=deckActive?(config.UseSharedDeckPanel?"すべてのプロファイルで共通":"プロファイルごとに45個のボタン"):LayerDisplayName(currentLayer);
        if(InputDisplayText!=null)InputDisplayText.Text=selected==null?"キーを選択してください":DisplayInputName(selected.Input);
    }
    internal static bool IsMouseLayerBlockedByDirectGesture(IReadOnlyList<Profile> profiles,string profileName,string layer)
    {
        if(layer is not ("MouseRight" or "MouseBack" or "MouseForward"))return false;
        return FindProfileMapping(profiles,profileName,layer,MappingInterceptsInput)?.Kind==ActionKind.Gesture;
    }
    static string MouseLayerLabel(string layer)=>layer switch{"MouseRight"=>"右クリック","MouseBack"=>"戻るボタン","MouseForward"=>"進むボタン",_=>layer};
    bool ConfirmDirectMouseGestureConflict(string input)
    {
        if(input is not ("MouseRight" or "MouseBack" or "MouseForward"))return true;
        string label=MouseLayerLabel(input);
        string message=$"通常レイヤーの「{label}」にジェスチャーを割り当てると、「{label}レイヤー」はジェスチャーを削除するまで使用できません。\n\nレイヤー内の既存の割り当ては削除されません。続行しますか？";
        return WpfMessageBox.Show(this,message,"レイヤーとの競合",MessageBoxButton.OKCancel,MessageBoxImage.Warning)==MessageBoxResult.OK;
    }
    void UpdateStatus() { EngineStatus.Text = engine.Enabled ? "● エンジン稼働中" : "■ エンジン停止中"; EngineStatus.Foreground = ThemeService.Brush(engine.Enabled?"AccentBrush":"DangerBrush"); }
    void SetupTray() { tray.Text = "RELYR v"+DisplayVersion; defaultTrayIcon=CreateDefaultTrayIcon();tray.Icon=defaultTrayIcon; tray.Visible = true; RebuildTrayMenu();UpdateTrayNumber(); tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowFromExternalLaunch); }
    void UpdateTrayNumber()
    {
        if(!config.ShowDesktopNumberInTray){numberedTrayIcon?.Dispose();numberedTrayIcon=null;tray.Icon=defaultTrayIcon;tray.Text="RELYR v"+DisplayVersion;return;}
        try
        {
            int number=VirtualDesktopAccessor.CurrentNumber+1;var icon=CreateDesktopNumberIcon(number);numberedTrayIcon?.Dispose();numberedTrayIcon=icon;tray.Icon=icon;tray.Text=$"RELYR v{DisplayVersion} — デスクトップ {number}";
        }
        catch{tray.Icon=defaultTrayIcon;}
    }
    internal static System.Drawing.Icon CreateDefaultTrayIcon()
    {
        try
        {
            string? executable=Environment.ProcessPath;
            if(!string.IsNullOrWhiteSpace(executable)&&System.Drawing.Icon.ExtractAssociatedIcon(executable) is { } icon)return icon;
        }
        catch{}
        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }
    internal static System.Drawing.Icon CreateDesktopNumberIcon(int number)
    {
        using var bitmap=new System.Drawing.Bitmap(32,32);using(var g=System.Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;g.Clear(System.Drawing.Color.Transparent);
            float fontSize=number<10?36:number<100?25:17;using var font=new System.Drawing.Font("Segoe UI",fontSize,System.Drawing.FontStyle.Bold,System.Drawing.GraphicsUnit.Pixel);
            using var format=new System.Drawing.StringFormat{Alignment=System.Drawing.StringAlignment.Center,LineAlignment=System.Drawing.StringAlignment.Center,FormatFlags=System.Drawing.StringFormatFlags.NoClip};
            g.DrawString(number.ToString(),font,System.Drawing.Brushes.White,new System.Drawing.RectangleF(-4,-6,40,42),format);
        }
        IntPtr hIcon=bitmap.GetHicon();try{return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();}finally{DestroyIcon(hIcon);}
    }
    void RebuildTrayMenu()
    {
        var old=tray.ContextMenuStrip;var menu=TrayMenuTheme.Create(ThemeService.UsesDark);
        menu.Items.Add("表示",null,(_,_)=>Dispatcher.BeginInvoke(ShowFromExternalLaunch));
        menu.Items.Add("有効 / 一時停止",null,(_,_)=>Dispatcher.BeginInvoke(()=>EngineToggle.IsChecked=!EngineToggle.IsChecked));
        var profiles=new System.Windows.Forms.ToolStripMenuItem("プロファイル");
        foreach(var profile in appliedConfig.Profiles.Where(p=>config.Profiles.Any(x=>x.Name==p.Name))){var item=new System.Windows.Forms.ToolStripMenuItem(profile.Name){Checked=profile.Name==appliedConfig.ActiveProfile};item.Click+=(_,_)=>Dispatcher.BeginInvoke(()=>SwitchProfile(profile.Name,true));profiles.DropDownItems.Add(item);}menu.Items.Add(profiles);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("押下キーをすべて解除",null,(_,_)=>InputEngine.ReleaseAll());
        menu.Items.Add("セーフモード",null,(_,_)=>Dispatcher.BeginInvoke(()=>{EngineToggle.IsChecked=false;InputEngine.ReleaseAll();}));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("終了",null,(_,_)=>RequestApplicationExitFromTray());
        TrayMenuTheme.Apply(menu,ThemeService.UsesDark);
        tray.ContextMenuStrip=menu;old?.Dispose();
    }

    void ProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if(loading||ProfileBox.SelectedItem is not string name)return;
        suppressAutomaticProfileSwitchUntil=DateTime.UtcNow.AddSeconds(2);
        SwitchProfile(name,false);
    }
    void ProfileDropDownOpened(object sender,EventArgs e)=>profileDropDownOpen=true;
    void ProfileDropDownClosed(object sender,EventArgs e)
    {
        profileDropDownOpen=false;
        suppressAutomaticProfileSwitchUntil=DateTime.UtcNow.AddSeconds(2);
    }
    void KeyboardLayoutChanged(object sender,SelectionChangedEventArgs e){if(loading||config==null)return;config.KeyboardLayout=KeyboardLayoutBox.SelectedIndex==1?"US":"JIS";appliedConfig.KeyboardLayout=config.KeyboardLayout;engine.UseUsLayout=config.KeyboardLayout=="US";BuildKeyboard();ColorButtons();var persisted=store.Load();persisted.KeyboardLayout=config.KeyboardLayout;store.Save(persisted);ShowInlineNotice(config.KeyboardLayout+"配列へ切り替えました");}
    void MainContent_SizeChanged(object sender,SizeChangedEventArgs e)
    {
        if(WorkspaceGrid==null||AssignmentPane==null||LowerInputRow==null)return;
        double gap=e.NewSize.Width<1000?12:e.NewSize.Width<1450?16:20;
        double navigationWidth=e.NewSize.Width<1000?176:e.NewSize.Width<1500?196:208;
        double inspectorWidth=e.NewSize.Width<1000?288:e.NewSize.Width<1500?320:340;
        LayerNavigationColumn.Width=new GridLength(navigationWidth);HeaderBrandColumn.Width=new GridLength(navigationWidth);
        AssignmentPaneColumn.Width=new GridLength(inspectorWidth);
        double centerWidth=Math.Max(360,e.NewSize.Width-navigationWidth-inspectorWidth-gap*2);
        double mouseWidth=Math.Clamp(centerWidth*.23,150,240);
        MouseColumn.Width=new GridLength(mouseWidth);
        // Every lower input group follows the same available-space ratio. The
        // mouse diagram is capped below the adjacent keypad/navigation surface.
        double lowerHeight=Math.Clamp(e.NewSize.Height*.36,220,340);
        double mouseScale=Math.Clamp(Math.Min(mouseWidth/220,lowerHeight/300),.62,.9);
        double secondaryHeight=Math.Min(lowerHeight,Math.Max(80,(centerWidth-mouseWidth-12)/654*312));
        MouseHost.Width=168*mouseScale;MouseHost.Height=Math.Max(48,secondaryHeight-30);
        LowerInputRow.Height=new GridLength(lowerHeight);
        KeyboardViewbox.MaxWidth=e.NewSize.Width<1100?double.PositiveInfinity:e.NewSize.Width<1500?1080:1180;
        WorkspaceGrid.Margin=new Thickness(gap);AssignmentPane.Padding=new Thickness(gap);UpdateLayerButtonWidths();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render,new Action(()=>
        {
            if(SecondaryKeyboardViewbox.ActualHeight>0)MouseHost.Height=Math.Max(48,SecondaryKeyboardViewbox.ActualHeight-30);
        }));
    }
    void LayerButtonsPanel_SizeChanged(object sender,SizeChangedEventArgs e)=>UpdateLayerButtonWidths();
    void UpdateLayerButtonWidths()
    {
        if(LayerButtonsPanel==null)return;
        bool compact=MainContentGrid.ActualHeight>0&&MainContentGrid.ActualHeight<720;
        bool narrow=LayerNavigationColumn.Width.IsAbsolute&&LayerNavigationColumn.Width.Value<155;
        foreach(var category in new[]{KeyboardLayerCategory,MouseLayerCategory,WindowsLayerCategory})category.Margin=compact?new Thickness(8,2,8,1):new Thickness(8,8,8,3);
        foreach(var button in LayerButtonsPanel.Children.OfType<System.Windows.Controls.Button>())
        {
            button.Width=double.NaN;button.Height=compact?36:44;button.MinHeight=36;button.Padding=compact?new Thickness(8,0,8,0):new Thickness(10,4,10,4);button.FontSize=compact?12:13;button.Margin=compact?new Thickness(0,1,0,1):new Thickness(0,2,0,2);button.HorizontalAlignment=System.Windows.HorizontalAlignment.Stretch;button.HorizontalContentAlignment=System.Windows.HorizontalAlignment.Left;
            if(button.Content is StackPanel content)
            {
                content.HorizontalAlignment=System.Windows.HorizontalAlignment.Left;
                foreach(var title in content.Children.OfType<TextBlock>().Take(1)){title.TextWrapping=TextWrapping.NoWrap;title.FontSize=button.FontSize;}
                foreach(var description in content.Children.OfType<TextBlock>().Skip(1)){description.Visibility=compact?Visibility.Collapsed:Visibility.Visible;description.FontSize=narrow?9:10;description.TextWrapping=TextWrapping.NoWrap;}
            }
        }
    }
    void SwitchProfile(string name,bool refresh,bool persist=true)
    {
        if(!config.Profiles.Any(x=>x.Name==name))return;
        bool changed=!config.ActiveProfile.Equals(name,StringComparison.OrdinalIgnoreCase)||!appliedConfig.ActiveProfile.Equals(name,StringComparison.OrdinalIgnoreCase);
        suppressAutomaticProfileSwitchUntil=DateTime.UtcNow.AddSeconds(2);
        explicitProfileSwitchProcess=ConditionMatcher.ProcessUnderCursor();
        automaticProfileReturnName="";
        config.ActiveProfile=name;
        if(appliedConfig.Profiles.Any(x=>x.Name==name)){appliedConfig.ActiveProfile=name;if(persist){var persisted=store.Load();if(persisted.Profiles.Any(x=>x.Name==name)){persisted.ActiveProfile=name;store.Save(persisted);}}}
        ClearSelectedInput();if(IsMouseLayerBlockedByDirectGesture(config.Profiles,CurrentProfile.Name,currentLayer))currentLayer="通常";if(refresh)RefreshProfiles();UpdateLayerButtons();UpdateStatus();RebuildTrayMenu();
        if(changed)ShowProfileOverlay(name);
    }
    void ShowProfileOverlay(string profileName)
    {
        if(!appliedConfig.ShowProfileSwitchOverlay)return;
        if(profileOverlay?.IsVisible==true&&lastProfileOverlayName.Equals(profileName,StringComparison.OrdinalIgnoreCase))return;
        profileOverlay?.Close();
        lastProfileOverlayName=profileName;
        var overlay=new ProfileSwitchOverlay(profileName);profileOverlay=overlay;
        overlay.Closed+=(_,_)=>
        {
            if(ReferenceEquals(profileOverlay,overlay))profileOverlay=null;
            if(lastProfileOverlayName.Equals(profileName,StringComparison.OrdinalIgnoreCase))lastProfileOverlayName="";
        };
        overlay.Show();
    }
    void AutoSwitchProfile()
    {
        if(profileDropDownOpen||DateTime.UtcNow<suppressAutomaticProfileSwitchUntil)
        {
            LogAutomaticProfileSwitch($"paused dropdown={profileDropDownOpen} suppressUntil={suppressAutomaticProfileSwitchUntil:O}");
            return;
        }
        if(!appliedConfig.AutoSwitchProfilesByCursor)
        {
            bool changed=TryApplyAutomaticProfile(config,appliedConfig,config.ActiveProfile,engine.TryPrepareForProfileChange);
            LogAutomaticProfileSwitch($"disabled editor={config.ActiveProfile} runtime={appliedConfig.ActiveProfile} changed={changed}");
            if(changed){RebuildTrayMenu();ShowProfileOverlay(appliedConfig.ActiveProfile);}
            return;
        }
        if(!appliedConfig.Profiles.Skip(1).Any(x=>x.AutoSwitchEnabled))
        {
            LogAutomaticProfileSwitch("no-enabled-profiles");
            return;
        }
        // The taskbar is an input location, not an application profile target.
        // Keep the profile of the application that the pointer just left so its
        // Taskbar+... mappings remain available.
        bool cursorOverTaskbar=ConditionMatcher.IsCursorOverTaskbar();
        if(cursorOverTaskbar){ResetAutomaticProfileCandidate();LogAutomaticProfileSwitch($"taskbar runtime={appliedConfig.ActiveProfile}");return;}
        var processesAtCursor=ConditionMatcher.ProcessesUnderCursor()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        // RELYR windows (the editor, profile notification, dialogs, etc.) are
        // neutral UI surfaces. Treating them as "no matching application"
        // feeds back into the auto switcher and can repeatedly return to the
        // default profile while the notification itself is under the pointer.
        if(processesAtCursor.Any(process=>IsOwnProcess(process))){ResetAutomaticProfileCandidate();LogAutomaticProfileSwitch($"own-window processes={string.Join(",",processesAtCursor)} runtime={appliedConfig.ActiveProfile}");return;}
        var processes=processesAtCursor.Where(process=>!IsOwnProcess(process)).ToArray();
        // Child/owner windows can change while the pointer remains inside one
        // Chromium or Qt application. Stabilize the resolved profile rather
        // than requiring the raw process list to be byte-for-byte identical.
        var candidate=ResolveAutomaticProfileTarget(appliedConfig.Profiles,appliedConfig.ActiveProfile,automaticProfileReturnName,processes,cursorOverTaskbar);
        bool stable=ObserveAutomaticProfileCandidate(candidate.Target,1);
        LogAutomaticProfileSwitch($"observe processes={string.Join(",",processes)} candidate={candidate.Target} samples={automaticProfileCandidateSamples}/1 stable={stable} runtime={appliedConfig.ActiveProfile} return={automaticProfileReturnName}");
        if(!stable)return;
        string process=processes.FirstOrDefault()??"";
        // RELYR itself is never an automatic-switch target. Treat it like an
        // unmatched application so the runtime profile cannot remain stuck on
        // the application from another virtual desktop.
        if(ShouldKeepExplicitProfile(explicitProfileSwitchProcess,process,cursorOverTaskbar)){LogAutomaticProfileSwitch($"manual-hold original={explicitProfileSwitchProcess} current={process}");return;}
        explicitProfileSwitchProcess="";
        string before=appliedConfig.ActiveProfile;
        if(TryApplyAutomaticProfileForProcesses(processes,cursorOverTaskbar,out string target))
        {
            LogAutomaticProfileSwitch($"applied before={before} target={target} runtime={appliedConfig.ActiveProfile} return={automaticProfileReturnName}");
        }
        else LogAutomaticProfileSwitch($"not-applied before={before} target={target} runtime={appliedConfig.ActiveProfile} captured={engine.HasCapturedPhysicalInput}");
    }
    bool TryApplyAutomaticProfileForProcesses(IReadOnlyCollection<string> processes,bool cursorOverTaskbar,out string target)
    {
        if(!TryResolveAndApplyAutomaticProfile(config,appliedConfig,processes,cursorOverTaskbar,engine.TryPrepareForProfileChange,ref automaticProfileReturnName,out target))
            return false;
        RebuildTrayMenu();
        ShowProfileOverlay(target);
        return true;
    }
    void LogAutomaticProfileSwitch(string message)
    {
        if(string.IsNullOrWhiteSpace(automaticProfileDiagnosticLog))return;
        try
        {
            string? directory=Path.GetDirectoryName(automaticProfileDiagnosticLog);
            if(!string.IsNullOrWhiteSpace(directory))Directory.CreateDirectory(directory);
            File.AppendAllText(automaticProfileDiagnosticLog,$"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch{}
    }
    void ResetAutomaticProfileCandidate(){automaticProfileCandidateSignature="";automaticProfileCandidateSamples=0;}
    bool ObserveAutomaticProfileCandidate(string signature,int requiredSamples)
    {
        if(!automaticProfileCandidateSignature.Equals(signature,StringComparison.OrdinalIgnoreCase))
        {
            automaticProfileCandidateSignature=signature;
            automaticProfileCandidateSamples=1;
            return requiredSamples<=1;
        }
        automaticProfileCandidateSamples=Math.Min(requiredSamples,automaticProfileCandidateSamples+1);
        return automaticProfileCandidateSamples>=requiredSamples;
    }
    internal static bool TryApplyAutomaticProfile(AppConfig editingConfig,AppConfig runtimeConfig,string targetName,Func<bool> prepare)
    {
        if(runtimeConfig.ActiveProfile==targetName||!runtimeConfig.Profiles.Any(x=>x.Name==targetName))return false;
        if(!prepare())return false;
        return ApplyAutomaticProfile(editingConfig,runtimeConfig,targetName);
    }
    internal static bool TryResolveAndApplyAutomaticProfile(AppConfig editingConfig,AppConfig runtimeConfig,IReadOnlyCollection<string> processes,bool cursorOverTaskbar,Func<bool> prepare,ref string returnProfile,out string target)
    {
        var resolution=ResolveAutomaticProfileTarget(runtimeConfig.Profiles,runtimeConfig.ActiveProfile,returnProfile,processes,cursorOverTaskbar);
        target=resolution.Target;
        if(runtimeConfig.ActiveProfile.Equals(target,StringComparison.OrdinalIgnoreCase))
        {
            returnProfile=resolution.ReturnProfile;
            return false;
        }
        if(!TryApplyAutomaticProfile(editingConfig,runtimeConfig,target,prepare))return false;
        returnProfile=resolution.ReturnProfile;
        return true;
    }
    internal static bool ApplyAutomaticProfile(AppConfig editingConfig,AppConfig runtimeConfig,string targetName)
    {
        if(runtimeConfig.ActiveProfile==targetName||!runtimeConfig.Profiles.Any(x=>x.Name==targetName))return false;
        // Automatic switching is a runtime concern. Never move the profile that the
        // user is currently editing, even when the cursor or virtual desktop changes.
        runtimeConfig.ActiveProfile=targetName;
        return true;
    }
    internal static Profile SelectAutomaticProfile(IReadOnlyList<Profile> profiles,string process)=>profiles.Skip(1).FirstOrDefault(x=>x.AutoSwitchEnabled&&x.AutoSwitchApplications.Any(app=>ConditionMatcher.Matches(app,process)))??profiles[0];
    internal static string SelectAutomaticProfileNameForLocation(IReadOnlyList<Profile> profiles,string currentProfile,string process,bool cursorOverTaskbar)=>cursorOverTaskbar&&profiles.Any(x=>x.Name==currentProfile)?currentProfile:SelectAutomaticProfile(profiles,process).Name;
    internal static (string Target,string ReturnProfile) ResolveAutomaticProfileTarget(IReadOnlyList<Profile> profiles,string currentProfile,string returnProfile,string process,bool cursorOverTaskbar)
        =>ResolveAutomaticProfileTarget(profiles,currentProfile,returnProfile,string.IsNullOrWhiteSpace(process)?[]:[process],cursorOverTaskbar);
    internal static (string Target,string ReturnProfile) ResolveAutomaticProfileTarget(IReadOnlyList<Profile> profiles,string currentProfile,string returnProfile,IReadOnlyCollection<string> processes,bool cursorOverTaskbar)
    {
        if(cursorOverTaskbar)return(currentProfile,returnProfile);
        string defaultProfile=profiles[0].Name;
        var matched=profiles.Skip(1).FirstOrDefault(x=>x.AutoSwitchEnabled&&x.AutoSwitchApplications.Any(app=>processes.Any(process=>ConditionMatcher.Matches(app,process))));
        if(matched!=null)
        {
            string returnTarget=ValidManualReturnProfile(profiles,returnProfile)
                ?? ValidManualReturnProfile(profiles,currentProfile)
                ?? defaultProfile;
            return(matched.Name,returnTarget);
        }
        if(!string.IsNullOrWhiteSpace(returnProfile)&&profiles.Any(x=>x.Name==returnProfile))return(returnProfile,"");
        // An automatically selected profile is not a safe fallback: it may
        // belong to an app on a different virtual desktop. Return to the
        // manually selected non-automatic profile, or the standard profile.
        return(ValidManualReturnProfile(profiles,currentProfile)??defaultProfile,"");
    }
    static string? ValidManualReturnProfile(IReadOnlyList<Profile> profiles,string profileName)
        =>profiles.FirstOrDefault(x=>x.Name.Equals(profileName,StringComparison.OrdinalIgnoreCase)&&!x.AutoSwitchEnabled)?.Name;
    internal static bool ShouldKeepExplicitProfile(string originalProcess,string currentProcess,bool cursorOverTaskbar)=>cursorOverTaskbar||(!string.IsNullOrWhiteSpace(originalProcess)&&ConditionMatcher.Matches(originalProcess,currentProcess));
    internal static bool IsOwnProcess(string process,string? executablePath=null)=>!string.IsNullOrWhiteSpace(process)&&ConditionMatcher.Matches(System.IO.Path.GetFileNameWithoutExtension(executablePath??Environment.ProcessPath??"RELYR"),process);
    void NewProfile_Click(object s, RoutedEventArgs e) { var name=PromptText("新しいプロファイル","新しいプロファイル名",$"プロファイル {config.Profiles.Count+1}");if(string.IsNullOrWhiteSpace(name)||config.Profiles.Any(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase))){if(!string.IsNullOrWhiteSpace(name))ShowInlineNotice("同じ名前のプロファイルがあります");return;}var source=SelectProfile("割り当てのコピー元",true,out bool cancelled);if(cancelled)return;config.Profiles.Add(new Profile{Name=name,Mappings=source?.Mappings.Select(CloneMapping).ToList()??[]});config.ActiveProfile=name;RefreshProfiles();MarkDirty();UpdateStatus(); }
    void DuplicateProfile_Click(object s,RoutedEventArgs e){var source=CurrentProfile;var name=source.Name+" のコピー";int i=2;while(config.Profiles.Any(x=>x.Name==name))name=source.Name+$" のコピー {i++}";var copy=new Profile{Name=name,Mappings=source.Mappings.Select(CloneMapping).ToList()};config.Profiles.Add(copy);config.ActiveProfile=name;RefreshProfiles();MarkDirty();UpdateStatus();}
    void RenameProfile_Click(object s,RoutedEventArgs e){if(CurrentProfile==config.Profiles[0]){ShowInlineNotice("標準プロファイルの名前は変更できません");return;}var old=CurrentProfile.Name;var name=PromptText("プロファイル名を変更","新しい名前",old);if(string.IsNullOrWhiteSpace(name)||config.Profiles.Any(x=>x!=CurrentProfile&&x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))return;CurrentProfile.Name=name;if(config.ActiveProfile==old)config.ActiveProfile=name;foreach(var map in config.Profiles.SelectMany(x=>x.Mappings)){if(map.Kind==ActionKind.Profile&&map.Value==old)map.Value=name;if(map.LongPressKind==ActionKind.Profile&&map.LongPressValue==old)map.LongPressValue=name;}RefreshProfiles();MarkDirty();}
    void CopyProfile_Click(object s,RoutedEventArgs e){var source=SelectProfile("割り当てのコピー元を選択",false);if(source==null||source==CurrentProfile)return;if(WpfMessageBox.Show($"「{source.Name}」の割り当てで「{CurrentProfile.Name}」を置き換えますか？","割り当てコピー",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;CurrentProfile.Mappings=source.Mappings.Select(CloneMapping).ToList();MarkDirty();ColorButtons();}
    void ConfigureProfileAutoSwitch_Click(object s,RoutedEventArgs e){if(CurrentProfile==config.Profiles[0]){ShowInlineNotice("標準プロファイルは自動切替の戻り先です");return;}if(CurrentProfile.AutoSwitchEnabled){var choice=WpfMessageBox.Show($"「{CurrentProfile.Name}」の自動切替はオンです。\n\nはい：対象アプリを追加\nいいえ：自動切替をオフ\nキャンセル：変更しない","プロファイル自動切替",MessageBoxButton.YesNoCancel);if(choice==MessageBoxResult.Cancel)return;if(choice==MessageBoxResult.No){CurrentProfile.AutoSwitchEnabled=false;MarkDirty();ShowInlineNotice("自動切替をオフにしました");return;}}var app=SelectRunningApplication();if(string.IsNullOrWhiteSpace(app))return;CurrentProfile.AutoSwitchEnabled=true;if(!CurrentProfile.AutoSwitchApplications.Contains(app,StringComparer.OrdinalIgnoreCase))CurrentProfile.AutoSwitchApplications.Add(app);MarkDirty();ShowInlineNotice($"カーソルが {app} 上にある時、自動的に「{CurrentProfile.Name}」へ切り替えます");}
    void DeleteProfile_Click(object s,RoutedEventArgs e){if(CurrentProfile==config.Profiles[0]){ShowInlineNotice("標準プロファイルは削除できません");return;}if(WpfMessageBox.Show($"「{CurrentProfile.Name}」を削除しますか？","確認",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;config.Profiles.Remove(CurrentProfile);config.ActiveProfile=config.Profiles[0].Name;RefreshProfiles();MarkDirty();UpdateStatus();RebuildTrayMenu();}
    static Mapping CloneMapping(Mapping x)=>new(){Input=x.Input,Kind=x.Kind,Value=x.Value,LongPressKind=x.LongPressKind,LongPressValue=x.LongPressValue,DragValue=x.DragValue,DragEndValue=x.DragEndValue,LongPressMs=x.LongPressMs,Application=x.Application,Layer=x.Layer,Description=x.Description};
    static GestureDefinition CloneGesture(GestureDefinition x)=>new(){Name=x.Name,UpKind=x.UpKind,UpValue=x.UpValue,DownKind=x.DownKind,DownValue=x.DownValue,LeftKind=x.LeftKind,LeftValue=x.LeftValue,RightKind=x.RightKind,RightValue=x.RightValue,CenterKind=x.CenterKind,CenterValue=x.CenterValue};
    void Save_Click(object s, RoutedEventArgs e)=>SaveAndApply("設定を保存し、エンジンへ反映しました");
    void SaveAndApply(string message){var errors=ConfigValidator.Validate(config);if(errors.Count>0){WpfMessageBox.Show(string.Join("\n",errors),"設定の確認",MessageBoxButton.OK,MessageBoxImage.Warning);return;}store.Save(config);appliedConfig=store.Clone(config);engine.SpaceHoldRepeatEnabled=config.SpaceHoldRepeatEnabled;engine.SpaceHoldRepeatDelayMs=config.SpaceHoldRepeatDelayMs;engine.GestureThresholdPixels=config.GestureThresholdPixels;engine.LockCursorDuringGesture=config.LockCursorDuringGesture;UpdateStatus();RebuildTrayMenu();LastInput.Text=message;LastInput.Foreground=ThemeService.Brush("AccentBrush");}
    void Detect_Click(object s, RoutedEventArgs e) { detectMode = true;pendingDetectedLayer=null;ClearExecutionFocus(DetectInputButton);LastInput.Text = "入力を待っています… レイヤーボタンは押したまま次のキーを押してください";LastInput.Foreground=ThemeService.Brush("WarningBrush"); }
    void HandleDetectedInput(string text)
    {
        if(text=="緊急停止"){macroEmergencyStop=true;ClearPendingActions();EngineToggle.IsChecked=false;}
        macroWindow?.Capture(text);LastInput.Text="入力: "+text;
        if(!detectMode||text=="緊急停止")return;
        string[] parts=text.Split(' ',2,StringSplitOptions.RemoveEmptyEntries);if(parts.Length==0)return;
        string input=parts[0],state=parts.Length>1?parts[1]:"";
        if(state.Equals("Layer Down",StringComparison.OrdinalIgnoreCase)){pendingDetectedLayer=input;ShowDetectionLayerWaiting(input);return;}
        if(state.Equals("Layer Up",StringComparison.OrdinalIgnoreCase)){if(pendingDetectedLayer?.Equals(input,StringComparison.OrdinalIgnoreCase)==true)CompleteDetectedInput(input);return;}
        bool down=state.Equals("Down",StringComparison.OrdinalIgnoreCase),up=state.Equals("Up",StringComparison.OrdinalIgnoreCase);
        if(pendingDetectedLayer!=null)
        {
            if(input.Equals(pendingDetectedLayer,StringComparison.OrdinalIgnoreCase)){if(up)CompleteDetectedInput(input);return;}
            if(down){CompleteDetectedInput(input.Contains('+')?input:pendingDetectedLayer+"+"+input);return;}
        }
        if(down&&IsDetectableLayer(input)){pendingDetectedLayer=input;ShowDetectionLayerWaiting(input);return;}
        if(down||(!down&&!up&&!state.Contains("Drag",StringComparison.OrdinalIgnoreCase)))CompleteDetectedInput(input);
    }
    void MacroPlaybackFinished(MacroPlaybackResult result)
    {
        if(result.Cancelled)return;Dispatcher.BeginInvoke(()=>{LastInput.Text=result.Succeeded?result.Message:"マクロ実行エラー: "+result.Message;LastInput.Foreground=ThemeService.Brush(result.Succeeded?"AccentBrush":"DangerBrush");});
    }
    static bool IsDetectableLayer(string input)=>input is "Space" or "CapsLock" or "MouseRight" or "MouseBack" or "MouseForward";
    void ShowDetectionLayerWaiting(string layer){LastInput.Text=$"待機中: {DisplayInputName(layer)} を押したまま、組み合わせるキーを押してください";LastInput.Foreground=ThemeService.Brush("WarningBrush");}
    void CompleteDetectedInput(string input){detectMode=false;pendingDetectedLayer=null;SelectInput(input,false);editingSelectedInput=true;ColorButtons();if(string.IsNullOrWhiteSpace(ValueBox.Text))FocusExecutionValue(ValueBox);LastInput.Text="検出: "+DisplayInputName(input);LastInput.Foreground=ThemeService.Brush("AccentBrush");}
    internal void BeginInputDetectionForTest()=>Detect_Click(DetectInputButton,new RoutedEventArgs());
    internal void FeedDetectedInputForTest(string text)=>HandleDetectedInput(text);
    internal void CompleteDestinationInputForTest()=>CompleteDestinationInput();
    internal void RefreshLayerButtonsForTest()=>UpdateLayerButtons();
    internal void SaveAndApplyForTest()=>SaveAndApply("テスト：設定を保存し、エンジンへ反映しました");
    internal void ChooseDestinationForTest(string key)
    {
        var button=VisualInputButtons().First(x=>string.Equals(x.Tag?.ToString(),key,StringComparison.OrdinalIgnoreCase));
        var args=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent};
        DestinationButton_PreviewMouseDown(button,args);
    }
    void LayerButton_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not System.Windows.Controls.Button{Tag:string layer} button)return;
        ShowKeyboardWorkspace();
        ClearExecutionFocus(button);
        if(IsMouseLayerBlockedByDirectGesture(config.Profiles,CurrentProfile.Name,layer)){ShowInlineNotice($"{MouseLayerLabel(layer)}レイヤーは通常レイヤーのジェスチャーと競合しているため使用できません");return;}
        if(layer=="CapsLock"&&!ConfirmCapsLockLayer())return;
        currentLayer=layer;ClearSelectedInput(button);UpdateLayerButtons();
    }
    bool ConfirmCapsLockLayer()
    {
        if(capsLockRemapped)return true;ShowInlineNotice("CapsLockレイヤーにはF13リマップ設定とWindows再起動が必要です");WpfMessageBox.Show("CapsLockレイヤーは安全性のため、CapsLock→F13設定を行った場合だけ動作します。\n\n［設定］→［レイヤー］で設定し、Windowsを再起動してください。","CapsLockレイヤーは無効です",MessageBoxButton.OK,MessageBoxImage.Information);return false;
    }
    internal void SetCapsLockRemapForTest(bool enabled){capsLockRemapped=enabled;engine.TreatF13AsCapsLock=enabled;}
    void EngineChanged(object s, RoutedEventArgs e) { if (loading || config == null) return;if(!engineStarted){loading=true;EngineToggle.IsChecked=false;loading=false;return;}engine.Enabled = EngineToggle.IsChecked == true; if(!engine.Enabled)ClearPendingActions();config.EngineEnabled = engine.Enabled;appliedConfig.EngineEnabled=engine.Enabled;var persisted=store.Load();persisted.EngineEnabled=engine.Enabled;store.Save(persisted);UpdateStatus(); }
    void AutoSaveChanged(object s,RoutedEventArgs e){if(loading||config==null)return;config.AutoSave=AutoSaveToggle.IsChecked==true;UpdateAutoSaveToggleText();if(config.AutoSave)SaveAndApply("自動保存をオンにし、現在の変更を保存・反映しました");else{appliedConfig.AutoSave=false;var persisted=store.Load();persisted.AutoSave=false;store.Save(persisted);LastInput.Text="自動保存をオフにしました";LastInput.Foreground=ThemeService.Brush("AccentBrush");}}
    void UpdateAutoSaveToggleText(){if(AutoSaveStatus!=null){AutoSaveStatus.Text=AutoSaveToggle.IsChecked==true?"● 自動保存 オン":"○ 自動保存 オフ";AutoSaveStatus.Foreground=ThemeService.Brush(AutoSaveToggle.IsChecked==true?"AccentBrush":"SecondaryText");}}
    void ClearPendingActions(){while(actionQueue.TryTake(out _)){}while(dragActionQueue.TryTake(out _)){}InputEngine.EndModifierDrag();MacroPlayer.StopAll();}
    void OpenSettings_Click(object sender,RoutedEventArgs e)
        =>OpenSettingsFrom(this);

    internal void OpenSettingsFrom(Window owner,string? category=null)
    {
        var window=new SettingsWindow(config,lastUpdateCheck){Owner=owner};
        if(category!=null)window.SelectCategory(category);
        if(window.ShowDialog()!=true)return;
        if(window.CapsRemapChanged){LastInput.Text="CapsLock設定を変更しました — Windows再起動後に反映されます";LastInput.Foreground=ThemeService.Brush("WarningBrush");}
        if(window.ResetConfig is { } reset){ApplyCompleteConfig(reset,"すべての設定を初期状態へ戻しました");if(window.ResetNeedsRestart)SettingsWindow.PromptForWindowsRestart(this,false);return;}
        if(window.ImportedConfig is { } imported){ApplyCompleteConfig(imported,"設定をインポートして反映しました");if(window.ImportedCapsLockNeedsRestart)SettingsWindow.PromptForWindowsRestart(this,window.ImportedCapsLockEnabled);return;}
        bool previousUpdateSetting=config.CheckForUpdates;
        try
        {
            ApplySettingsWindowValues(window);
            CopyApplicationOptions(config,appliedConfig);
            var persisted=store.Load();
            CopyApplicationOptions(config,persisted);
            store.Save(persisted);

            ThemeService.Apply(config.ThemeMode);
            archiveWatcher.Apply(config);
            UpdateTrayNumber();
            ApplyUpdateCheckPreference(previousUpdateSetting);
            if(config.AutoSave)SaveAndApply("自動保存をオンにし、現在の変更を保存・反映しました");
            else
            {
                LastInput.Text="アプリ設定を保存しました — 自動保存はオフです";
                LastInput.Foreground=ThemeService.Brush("AccentBrush");
            }
        }
        catch(Exception ex){WpfMessageBox.Show("設定を保存できません: "+ex.Message);}
        finally{loading=true;AutoSaveToggle.IsChecked=config.AutoSave;UpdateAutoSaveToggleText();loading=false;}
    }
    void ApplySettingsWindowValues(SettingsWindow window)
    {
        if(window.StartWithWindowsChanged)StartupService.SetEnabled(window.StartWithWindows);
        config.StartWithWindows=window.StartWithWindows;
        config.AutoExtractDesktopArchives=window.AutoExtract;
        config.ArchiveWatchFolder=window.ArchiveWatchFolder;
        config.ArchiveDestinationFolder=window.ArchiveDestinationFolder;
        config.DeleteArchiveAfterExtract=window.DeleteAfterExtract;
        config.ShowDesktopNumberInTray=window.ShowDesktopNumberInTray;
        config.CheckForUpdates=window.CheckForUpdates;
        config.ShowProfileSwitchOverlay=window.ShowProfileSwitchOverlay;
        config.WindowActionTarget=window.SelectedWindowActionTarget;
        config.ThemeMode=window.SelectedThemeMode;
        config.AutoSave=window.AutoSave;
        config.SpaceHoldRepeatEnabled=window.SpaceHoldRepeat;
        config.SpaceHoldRepeatDelayMs=window.SpaceHoldRepeatDelay;
        config.GestureThresholdPixels=window.GestureThreshold;
        config.LockCursorDuringGesture=window.LockCursorDuringGesture;
        config.ClockBackgroundMode=window.SelectedClockBackgroundMode;
        config.ClockDisplayMode=window.SelectedClockDisplayMode;
        config.ClockBackgroundImage=window.ClockBackgroundImage;
        config.ClockSolidColor=window.ClockSolidColor;
        config.ClockShowOnAllMonitors=window.ClockShowOnAllMonitors;
        config.InputPanelOpacityPercent=window.InputPanelOpacityPercent;
        engine.SpaceHoldRepeatEnabled=config.SpaceHoldRepeatEnabled;
        engine.SpaceHoldRepeatDelayMs=config.SpaceHoldRepeatDelayMs;
        engine.GestureThresholdPixels=config.GestureThresholdPixels;
        engine.LockCursorDuringGesture=config.LockCursorDuringGesture;
    }
    static void CopyApplicationOptions(AppConfig source,AppConfig destination)
    {
        destination.StartWithWindows=source.StartWithWindows;
        destination.AutoExtractDesktopArchives=source.AutoExtractDesktopArchives;
        destination.ArchiveWatchFolder=source.ArchiveWatchFolder;
        destination.ArchiveDestinationFolder=source.ArchiveDestinationFolder;
        destination.DeleteArchiveAfterExtract=source.DeleteArchiveAfterExtract;
        destination.ShowDesktopNumberInTray=source.ShowDesktopNumberInTray;
        destination.CheckForUpdates=source.CheckForUpdates;
        destination.ShowProfileSwitchOverlay=source.ShowProfileSwitchOverlay;
        destination.DismissedUpdateVersion=source.DismissedUpdateVersion;
        destination.WindowActionTarget=source.WindowActionTarget;
        destination.ThemeMode=source.ThemeMode;
        destination.AutoSave=source.AutoSave;
        destination.SpaceHoldRepeatEnabled=source.SpaceHoldRepeatEnabled;
        destination.SpaceHoldRepeatDelayMs=source.SpaceHoldRepeatDelayMs;
        destination.GestureThresholdPixels=source.GestureThresholdPixels;
        destination.LockCursorDuringGesture=source.LockCursorDuringGesture;
        destination.ClockBackgroundMode=source.ClockBackgroundMode;
        destination.ClockDisplayMode=source.ClockDisplayMode;
        destination.ClockBackgroundImage=source.ClockBackgroundImage;
        destination.ClockSolidColor=source.ClockSolidColor;
        destination.ClockShowOnAllMonitors=source.ClockShowOnAllMonitors;
        destination.InputPanelOpacityPercent=source.InputPanelOpacityPercent;
    }
    void ApplyCompleteConfig(AppConfig value,string message)
    {
        ClearPendingActions();config=value;store.Save(config);appliedConfig=store.Clone(config);
        bool pending=LegacyKeyRemapService.IsRestartStillPending(config);capsLockRemapped=pending?config.CapsLockRemapEffectiveBeforeRestart:LegacyKeyRemapService.HasCapsLockToF13();engine.TreatF13AsCapsLock=capsLockRemapped;
        engine.UseUsLayout=config.KeyboardLayout=="US";engine.SpaceHoldRepeatEnabled=config.SpaceHoldRepeatEnabled;engine.SpaceHoldRepeatDelayMs=config.SpaceHoldRepeatDelayMs;engine.GestureThresholdPixels=config.GestureThresholdPixels;engine.LockCursorDuringGesture=config.LockCursorDuringGesture;engine.Enabled=engineStarted&&config.EngineEnabled;
        loading=true;KeyboardLayoutBox.SelectedIndex=config.KeyboardLayout=="US"?1:0;AutoSaveToggle.IsChecked=config.AutoSave;EngineToggle.IsChecked=engine.Enabled;loading=false;
        currentLayer="通常";ClearSelectedInput();
        ThemeService.Apply(config.ThemeMode);BuildKeyboard();RefreshProfiles();UpdateLayerButtons();ColorButtons();UpdateAutoSaveToggleText();archiveWatcher.Apply(config);UpdateTrayNumber();UpdateStatus();RebuildTrayMenu();ApplyUpdateCheckPreference(false);LastInput.Text=message;LastInput.Foreground=ThemeService.Brush("AccentBrush");
    }
    public void ShowFirstRunSetup(){if(!NeedsFirstRunSetup)return;var setup=new SetupWindow{Owner=this};if(setup.ShowDialog()==true){config.ActiveProfile="標準";config.FirstRunCompleted=setup.DoNotShowAgain;store.Save(config);RefreshProfiles();RebuildTrayMenu();}}
    public void ShowFromExternalLaunch(){Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();Topmost=true;Topmost=false;Focus();EnsureUpdateCheckStarted();}
    // アップデートの定期確認、通知表示、検証済みインストーラーの起動をまとめて管理する。
    void ApplyUpdateCheckPreference(bool previousSetting)
    {
        if(!config.CheckForUpdates)
        {
            availableUpdate=null;UpdateBanner.Visibility=Visibility.Collapsed;return;
        }
        EnsureUpdateCheckStarted(!previousSetting);
    }
    void EnsureUpdateCheckStarted(bool force=false)
    {
        if(!config.CheckForUpdates||!IsLoaded||!IsVisible)return;
        var now=DateTimeOffset.UtcNow;
        if(!force&&!IsAutomaticUpdateCheckDue(now,lastAutomaticUpdateCheckAttempt,config.LastUpdateCheckUtcTicks))return;
        lastAutomaticUpdateCheckAttempt=now;_ = CheckForUpdatesAsync();
    }
    internal static bool IsAutomaticUpdateCheckDue(DateTimeOffset now,DateTimeOffset lastAttempt,long lastSuccessfulUtcTicks)
    {
        DateTimeOffset last=lastAttempt;
        if(lastSuccessfulUtcTicks>0)
            try{var successful=new DateTimeOffset(lastSuccessfulUtcTicks,TimeSpan.Zero);if(successful>last)last=successful;}catch(ArgumentOutOfRangeException){}
        if(last==default)return true;
        TimeSpan elapsed=now-last;
        return elapsed<TimeSpan.Zero||elapsed>=AutomaticUpdateCheckInterval;
    }
    async Task CheckForUpdatesAsync()
    {
        try
        {
            await CheckForUpdatesNowAsync();
        }
        catch(OperationCanceledException){}
        catch(Exception){}
    }
    internal Task<UpdateCheckResult> CheckForUpdatesNowAsync()
    {
        if(runningUpdateCheckTask is {IsCompleted:false})return runningUpdateCheckTask;
        runningUpdateCheckTask=RunUpdateCheckAsync();return runningUpdateCheckTask;
    }
    async Task<UpdateCheckResult> RunUpdateCheckAsync()
    {
        var result=await UpdateService.CheckLatestAsync(RunningVersion,updateCancellation.Token);ApplyUpdateCheckResult(result);return result;
    }
    void ApplyUpdateCheckResult(UpdateCheckResult result)
    {
        lastUpdateCheck=result;
        availableUpdate=result.AvailableUpdate;
        if(availableUpdate==null)UpdateBanner.Visibility=Visibility.Collapsed;
        else ShowUpdateAvailable(availableUpdate);
        config.LastUpdateCheckUtcTicks=result.CheckedAt.UtcTicks;
        appliedConfig.LastUpdateCheckUtcTicks=config.LastUpdateCheckUtcTicks;
        try
        {
            var persisted=store.Load();
            persisted.LastUpdateCheckUtcTicks=config.LastUpdateCheckUtcTicks;
            store.Save(persisted);
        }
        catch(IOException){}
        catch(UnauthorizedAccessException){}
        UpdateCheckCompleted?.Invoke(result);
    }
    internal void SetAvailableUpdate(UpdateInfo? update)
    {
        availableUpdate=update;
        UpdateBannerProgress.Visibility=Visibility.Collapsed;
        UpdateBannerProgress.Value=0;
        if(update==null)
        {
            UpdateBanner.Visibility=Visibility.Collapsed;
            return;
        }
        UpdateBannerText.Text=$"新しいバージョンが利用可能です（v{update.VersionText}）";
        UpdateAvailableButton.Content="今すぐ更新";
        UpdateAvailableButton.IsEnabled=true;
        UpdateDismissButton.IsEnabled=true;
        UpdateBanner.Visibility=string.Equals(config.DismissedUpdateVersion,update.VersionText,StringComparison.OrdinalIgnoreCase)
            ?Visibility.Collapsed
            :Visibility.Visible;
    }
    void ShowUpdateAvailable(UpdateInfo update)=>SetAvailableUpdate(update);
    internal void ShowUpdateAvailableForTest(UpdateInfo update)=>ShowUpdateAvailable(update);
    internal void DismissAvailableUpdateForTest()=>DismissCurrentUpdate();
    void UpdateDismiss_Click(object sender,RoutedEventArgs e)=>DismissCurrentUpdate();
    void DismissCurrentUpdate()
    {
        // 未保存のキー割り当てには触れず、閉じたリリース番号だけを直ちに永続化する。
        if(updateInProgress||availableUpdate is not { } update)return;
        config.DismissedUpdateVersion=update.VersionText;
        appliedConfig.DismissedUpdateVersion=update.VersionText;
        UpdateBanner.Visibility=Visibility.Collapsed;
        try
        {
            var persisted=store.Load();
            persisted.DismissedUpdateVersion=update.VersionText;
            store.Save(persisted);
        }
        catch(IOException){}
        catch(UnauthorizedAccessException){}
    }
    async void UpdateAvailable_Click(object sender,RoutedEventArgs e)
    {
        if(updateInProgress||availableUpdate is not { } update)return;
        if(WpfMessageBox.Show(this,$"RELYR v{update.VersionText} をダウンロードして更新します。\n\n更新ファイルはSHA-256で検証してから実行します。続行しますか？","RELYRをアップデート",MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return;
        await InstallUpdateAsync(this,update);
    }
    internal async Task<bool> InstallUpdateAsync(Window owner,UpdateInfo update,Action<string>? reportProgress=null,IProgress<UpdateDownloadProgress>? downloadProgress=null)
    {
        if(updateInProgress)return false;
        updateInProgress=true;UpdateAvailableButton.IsEnabled=false;UpdateDismissButton.IsEnabled=false;UpdateAvailableButton.Content="ダウンロード中…";UpdateBannerProgress.Value=0;UpdateBannerProgress.Visibility=Visibility.Visible;
        reportProgress?.Invoke("アップデートをダウンロードしています…");
        try
        {
            var footerProgress=new Progress<UpdateDownloadProgress>(value=>
            {
                if(value.Percentage is { } percentage){UpdateAvailableButton.Content=$"ダウンロード中… {percentage:0}%";UpdateBannerProgress.Value=percentage;}
                downloadProgress?.Report(value);
            });
            string installer=await UpdateService.DownloadAndVerifyAsync(update,updateCancellation.Token,footerProgress);
            UpdateBannerProgress.Value=100;
            UpdateAvailableButton.Content="更新準備完了";
            reportProgress?.Invoke("ダウンロードと安全性の検証が完了しました。");
            var confirm=AppDialog.Show(owner,
                $"RELYR v{update.VersionText} の準備ができました。\n\nRELYRを終了してアップデートし、完了後にメイン画面を開きます。今すぐ再起動しますか？",
                "アップデートの準備完了",MessageBoxButton.YesNo,MessageBoxImage.Question);
            if(confirm!=MessageBoxResult.Yes)
            {
                RestoreUpdateButton(update);
                reportProgress?.Invoke("更新は保留されています。設定の［アップデート］からいつでも実行できます。");
                return false;
            }
            UpdateAvailableButton.Content="更新しています…";
            reportProgress?.Invoke("RELYRを再起動してアップデートします…");
            const string silentArguments="/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELYRUPDATE=1";
            using var process=Process.Start(new ProcessStartInfo(installer,silentArguments){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Hidden});
            if(process==null)throw new InvalidOperationException("更新用インストーラーを起動できませんでした。");
            RequestApplicationExit();
            return true;
        }
        catch(OperationCanceledException)
        {
            RestoreUpdateButton(update);
            return false;
        }
        catch(Exception ex)
        {
            RestoreUpdateButton(update);
            WpfMessageBox.Show(owner,UpdateService.FriendlyError(ex),"アップデートできません",MessageBoxButton.OK,MessageBoxImage.Error);
            return false;
        }
    }
    void RestoreUpdateButton(UpdateInfo update)
    {
        updateInProgress=false;
        UpdateAvailableButton.IsEnabled=true;
        UpdateDismissButton.IsEnabled=true;
        UpdateAvailableButton.Content="今すぐ更新";
        UpdateBannerProgress.Visibility=Visibility.Collapsed;
        UpdateBannerProgress.Value=0;
    }
    void Delete_Click(object s, RoutedEventArgs e) { if (selected == null) return; var input=selected.Input;MappingCollectionForInput(input).Remove(selected);selected=null;SelectInput(input,false);UpdateLayerButtons();ClearExecutionFocus(s as FrameworkElement);MarkDirty();ColorButtons();LastInput.Text=DisplayInputName(input)+" の割り当てを削除しました";LastInput.Foreground=ThemeService.Brush("DangerBrush"); }
    void Window_Closing(object? s, CancelEventArgs e) { if(!allowClose){e.Cancel=true;Hide();return;}SystemEvents.UserPreferenceChanged-=WindowsThemeChanged;ThemeService.ThemeChanged-=AppThemeChanged;MacroPlayer.PlaybackFinished-=MacroPlaybackFinished;engine.PointerMoved-=QueueAutomaticProfileCheck;updateCancellation.Cancel();profileOverlay?.Close();OverlayService.Shutdown();trayNumberTimer.Stop();profileSwitchTimer.Stop();autoSaveTimer.Stop();engine.Enabled=false;ClearPendingActions();actionQueue.CompleteAdding();dragActionQueue.CompleteAdding();try{Task.WaitAll([actionWorker,dragActionWorker],2000);}catch{}InputEngine.ReleaseAll();engine.Dispose();RemoveTrayIconForImmediateExit();archiveWatcher.Dispose();updateCancellation.Dispose(); }
    internal void RemoveTrayIconForImmediateExit()
    {
        if(suppressTray||Interlocked.Exchange(ref trayDisposed,1)!=0)return;
        try{tray.Visible=false;}catch{}
        try{tray.Dispose();}catch{}
        try{numberedTrayIcon?.Dispose();}catch{}
        try{defaultTrayIcon?.Dispose();}catch{}
        numberedTrayIcon=null;
        defaultTrayIcon=null;
    }
    internal void PrepareVisualsForImmediateExit()
    {
        try{profileOverlay?.HideImmediatelyForProcessExit();}catch{}
        try{Hide();}catch{}
        RemoveTrayIconForImmediateExit();
    }
    public void PrepareForSystemShutdown()
    {
        allowClose=true;
        engine.Enabled=false;
        ClearPendingActions();
        InputEngine.ReleaseAll();
        engine.Dispose();
        archiveWatcher.Dispose();
    }
    public void ResetInputStateForSessionTransition()
    {
        activeInputMappings.Clear();activeLayerMappings.Clear();ClearPendingActions();engine.ResetForSessionTransition();
    }
    public void RequestApplicationExit()
    {
        if(Interlocked.Exchange(ref exitRequested,1)!=0)return;
        allowClose=true;
        // WPF/WinFormsの後処理が停止しても、トレイ終了後にプロセスだけを残さない。
        App.ArmForcedProcessExit(TimeSpan.FromSeconds(3));
        try
        {
            Close();
            InputEngine.ReleaseAll();
            App.ExitImmediately(0);
        }
        catch
        {
            App.ExitImmediately(1);
        }
    }
    void RequestApplicationExitFromTray()
    {
        if(Interlocked.Exchange(ref exitRequested,1)!=0)return;
        App.ArmForcedProcessExit(TimeSpan.FromSeconds(3));
        RemoveTrayIconForImmediateExit();
        App.ExitImmediately(0);
    }
    string? PromptText(string title,string label,string initial)
    {
        var dialog=new Window{Title=title,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Width=460,Height=220,ResizeMode=ResizeMode.NoResize,Background=ThemeService.Brush("SurfaceBackground"),Foreground=ThemeService.Brush("PrimaryText"),ShowInTaskbar=false};var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(new TextBlock{Text=label,FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,9)});var box=new TextBox{Text=initial,FontSize=15,Height=42,Padding=new Thickness(10,8,10,8),Background=ThemeService.Brush("InputBackground"),Foreground=ThemeService.Brush("PrimaryText"),BorderBrush=ThemeService.Brush("BorderBrush")};panel.Children.Add(box);var buttons=new StackPanel{Orientation=System.Windows.Controls.Orientation.Horizontal,HorizontalAlignment=System.Windows.HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};var cancel=new System.Windows.Controls.Button{Content="キャンセル",Width=112,Height=40,Margin=new Thickness(6,0,0,0),Style=(Style)System.Windows.Application.Current.FindResource("AppButtonStyle")};var ok=new System.Windows.Controls.Button{Content="決定",Width=112,Height=40,Margin=new Thickness(6,0,0,0),Style=(Style)System.Windows.Application.Current.FindResource("AccentAppButtonStyle"),IsDefault=true};cancel.Click+=(_,_)=>dialog.DialogResult=false;ok.Click+=(_,_)=>dialog.DialogResult=true;buttons.Children.Add(cancel);buttons.Children.Add(ok);panel.Children.Add(buttons);dialog.Content=panel;FollowWindowsTitleBarTheme(dialog);dialog.Loaded+=(_,_)=>{box.Focus();box.SelectAll();};return dialog.ShowDialog()==true?box.Text.Trim():null;
    }
    Profile? SelectProfile(string title,bool allowNoCopy)=>SelectProfile(title,allowNoCopy,out _);
    Profile? SelectProfile(string title,bool allowNoCopy,out bool cancelled)
    {
        bool noCopy=false;var dialog=new Window{Title=title,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Width=460,Height=400,Background=ThemeService.Brush("SurfaceBackground"),Foreground=ThemeService.Brush("PrimaryText"),ShowInTaskbar=false};var grid=new Grid{Margin=new Thickness(22)};grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.Children.Add(new TextBlock{Text=allowNoCopy?"コピー元を選ぶか、空のプロファイルとして作成してください。":"コピー元のプロファイルを選択してください。",Foreground=ThemeService.Brush("SecondaryText"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});var list=new ListBox{ItemsSource=config.Profiles,DisplayMemberPath="Name",Background=ThemeService.Brush("CardBackground"),Foreground=ThemeService.Brush("PrimaryText"),BorderBrush=ThemeService.Brush("BorderBrush"),Padding=new Thickness(6)};Grid.SetRow(list,1);grid.Children.Add(list);var buttons=new StackPanel{Orientation=System.Windows.Controls.Orientation.Horizontal,HorizontalAlignment=System.Windows.HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};var cancel=new System.Windows.Controls.Button{Content=allowNoCopy?"コピーせず作成":"キャンセル",Height=40,Margin=new Thickness(6,0,0,0),Style=(Style)System.Windows.Application.Current.FindResource("AppButtonStyle")};var ok=new System.Windows.Controls.Button{Content="選択してコピー",Height=40,Margin=new Thickness(6,0,0,0),Style=(Style)System.Windows.Application.Current.FindResource("AccentAppButtonStyle")};cancel.Click+=(_,_)=>{noCopy=allowNoCopy;dialog.DialogResult=false;};ok.Click+=(_,_)=>{if(list.SelectedItem!=null)dialog.DialogResult=true;};buttons.Children.Add(cancel);buttons.Children.Add(ok);Grid.SetRow(buttons,2);grid.Children.Add(buttons);dialog.Content=grid;FollowWindowsTitleBarTheme(dialog);bool? result=dialog.ShowDialog();cancelled=result is null||(result==false&&!noCopy);return result==true?list.SelectedItem as Profile:null;
    }
    string? SelectRunningApplication()
    {
        var apps=new List<RunningApplicationOption>();
        foreach(var process in Process.GetProcesses())
        {
            using(process)
            {
                try
                {
                    if(process.MainWindowHandle!=IntPtr.Zero&&!string.IsNullOrWhiteSpace(process.MainWindowTitle))
                        apps.Add(new($"{process.MainWindowTitle}  —  {process.ProcessName}.exe",process.ProcessName+".exe"));
                }
                catch{}
            }
        }
        var uniqueApps=apps.GroupBy(x=>x.Value,StringComparer.OrdinalIgnoreCase).Select(x=>x.First()).OrderBy(x=>x.Label).ToList();
        var dialog=new Window{Title="自動切替する起動中のアプリを選択",Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Width=620,Height=480,Background=ThemeService.Brush("SurfaceBackground"),Foreground=ThemeService.Brush("PrimaryText"),ShowInTaskbar=false};
        var grid=new Grid{Margin=new Thickness(18)};
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var list=new ListBox{ItemsSource=uniqueApps,DisplayMemberPath="Label",Background=ThemeService.Brush("CardBackground"),Foreground=ThemeService.Brush("PrimaryText"),BorderBrush=ThemeService.Brush("BorderBrush")};
        grid.Children.Add(list);
        var ok=new System.Windows.Controls.Button{Content="このアプリを使用",Width=160,Height=40,Margin=new Thickness(3,14,3,3),HorizontalAlignment=System.Windows.HorizontalAlignment.Right,Style=(Style)System.Windows.Application.Current.FindResource("AccentAppButtonStyle"),IsDefault=true};
        ok.Click+=(_,_)=>{if(list.SelectedItem!=null)dialog.DialogResult=true;};
        Grid.SetRow(ok,1);grid.Children.Add(ok);dialog.Content=grid;FollowWindowsTitleBarTheme(dialog);
        return dialog.ShowDialog()==true?(list.SelectedItem as RunningApplicationOption)?.Value:null;
    }
    [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr handle);
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr handle,int attribute,ref int value,int valueSize);
    static ActionOption[] ActionOptions(bool allowGesture)
    {
        var options=new List<ActionOption>
        {
            new(ActionKind.Key,"⌨","別のキー"),
            new(ActionKind.Profile,"⇄","プロファイル"),
            new(ActionKind.Shortcut,"↗","ショートカット"),
            new(ActionKind.Text,"T","文字列"),
            new(ActionKind.Launch,"▱","アプリ・パス"),
            new(ActionKind.Macro,"⌘","マクロ")
        };
        options.Add(new(ActionKind.Gesture,"✣","ジェスチャー",allowGesture));
        options.Add(new(ActionKind.Disabled,"⊘","無効化"));
        return [.. options];
    }
    sealed record ActionOption(ActionKind Kind,string Icon,string Label,bool IsEnabled=true);
    sealed record InputMappingSnapshot(Mapping Mapping,GestureDefinition? Gesture);
    sealed record LayerMappingSnapshot(IReadOnlyList<Mapping> Mappings);
    sealed record RunningApplicationOption(string Label,string Value);
}
