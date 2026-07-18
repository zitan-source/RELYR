using Microsoft.Win32;
using System.ComponentModel;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfMessageBox = System.Windows.MessageBox;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using System.Windows.Input;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace RELYR;

public partial class MainWindow : Window
{
    readonly ConfigService store = new();
    readonly InputEngine engine = new();
    readonly MappingExecutor executor;
    readonly ArchiveWatcher archiveWatcher = new();
    readonly BlockingCollection<(Mapping Map,string Input)> actionQueue=new(256);
    readonly Task actionWorker;
    readonly BlockingCollection<(Mapping? Map,string Input)> dragActionQueue=new();
    readonly Task dragActionWorker;
    readonly System.Windows.Threading.DispatcherTimer trayNumberTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    readonly System.Windows.Threading.DispatcherTimer profileSwitchTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    readonly System.Windows.Threading.DispatcherTimer autoSaveTimer=new(){Interval=TimeSpan.FromMilliseconds(450)};
    System.Drawing.Icon? numberedTrayIcon;
    System.Drawing.Icon? defaultTrayIcon;
    AppConfig config;
    AppConfig appliedConfig=null!;
    Mapping? selected;
    string selectedBaseInput="";
    bool loading, detectMode, allowClose, engineStarted;
    bool capsLockRemapped;
    string currentLayer="通常";
    MacroWindow? macroWindow;bool engineBeforeMacroRecording,macroEmergencyStop,macroIsRecording;
    Mapping? copiedMapping;
    TextBox? destinationInputTarget;
    int destinationFocusRequest;
    readonly System.Windows.Forms.NotifyIcon tray = new();
    Profile CurrentProfile => config.Profiles.First(x => x.Name == config.ActiveProfile);
    Profile AppliedProfile => appliedConfig.Profiles.FirstOrDefault(x=>x.Name==appliedConfig.ActiveProfile)??appliedConfig.Profiles[0];
    public bool NeedsFirstRunSetup=>!config.FirstRunCompleted;
    internal bool IsInputHookDisposedForTest=>engine.IsDisposedForTest;
    internal bool HasDestinationInputTargetForTest=>destinationInputTarget!=null;
    internal Profile CurrentProfileForTest=>CurrentProfile;
    internal IReadOnlyList<System.Windows.Controls.Button> VisualInputButtonsForTest=>VisualInputButtons().ToList();
    internal bool TitleBarUsesDarkMode { get; private set; }
    internal static string DisplayVersion
    {
        get{var v=typeof(MainWindow).Assembly.GetName().Version??new Version(0,0,0);return $"{v.Major}.{v.Minor}.{Math.Max(0,v.Build)}";}
    }

    public MainWindow(bool skipSetup=false)
    {
        loading=true;
        InitializeComponent();
        SourceInitialized+=(_,_)=>ApplyWindowsTitleBarTheme();
        SystemEvents.UserPreferenceChanged+=WindowsThemeChanged;
        ArrangeInputWorkspace();
        VersionText.Text="v"+DisplayVersion;
        Title="RELYR v"+DisplayVersion;
        config = store.Load();
        bool configuredCapsLockRemap=LegacyKeyRemapService.HasCapsLockToF13();bool capsRestartPending=LegacyKeyRemapService.IsRestartStillPending(config);
        if(config.CapsLockRemapPendingRestart&&!capsRestartPending){config.CapsLockRemapPendingRestart=false;config.CapsLockRemapEffectiveBeforeRestart=false;config.CapsLockRemapChangedAtUtcTicks=0;config.CapsLockLayerEnabled=configuredCapsLockRemap;store.Save(config);}
        else if(!capsRestartPending)config.CapsLockLayerEnabled=configuredCapsLockRemap;
        capsLockRemapped=capsRestartPending?config.CapsLockRemapEffectiveBeforeRestart:configuredCapsLockRemap;
        engine.TreatF13AsCapsLock=capsLockRemapped;
        appliedConfig=store.Clone(config);
        executor=new MappingExecutor(new SystemInputOutput(name=>appliedConfig.Macros.FirstOrDefault(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)),name=>Dispatcher.BeginInvoke(()=>SwitchProfile(name,true)),()=>appliedConfig.KeyboardLayout=="US",()=>appliedConfig));
        actionWorker=Task.Run(ProcessActions);dragActionWorker=Task.Factory.StartNew(ProcessDragActions,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
        AutoSaveToggle.IsChecked=config.AutoSave;UpdateAutoSaveToggleText();
        KeyboardLayoutBox.SelectedIndex=config.KeyboardLayout=="US"?1:0;
        loading=false;
        var actionOptions=new[]{new ActionOption(ActionKind.None,"未設定"),new ActionOption(ActionKind.Disabled,"無効化"),new ActionOption(ActionKind.Key,"別のキー"),new ActionOption(ActionKind.Shortcut,"ショートカット"),new ActionOption(ActionKind.Text,"文字列入力"),new ActionOption(ActionKind.Launch,"アプリ・ファイル・URL"),new ActionOption(ActionKind.Mouse,"マウス操作"),new ActionOption(ActionKind.Macro,"マクロ")};
        KindBox.ItemsSource=actionOptions;LongKindBox.ItemsSource=actionOptions.Select(x=>x.Kind==ActionKind.None?new ActionOption(x.Kind,"短押しと同じ（キーは連打）"):x).ToArray();
        KindBox.SelectedValuePath=nameof(ActionOption.Kind);
        LongKindBox.SelectedValuePath=nameof(ActionOption.Kind);
        BuildKeyboard();engine.UseUsLayout=config.KeyboardLayout=="US";engine.SpaceHoldRepeatEnabled=config.SpaceHoldRepeatEnabled;engine.SpaceHoldRepeatDelayMs=config.SpaceHoldRepeatDelayMs;RefreshProfiles();UpdateLayerButtons();
        engine.InputReceived = HandleInput;
        InputEngine.DesktopActionFailed=message=>Dispatcher.BeginInvoke(()=>{LastInput.Text="仮想デスクトップ操作エラー: "+message;LastInput.Foreground=WpfBrushes.OrangeRed;});
        engine.HasMapping = HasMapping;
        engine.IsNativeMouseDrag=input=>FindMapping(input) is {Kind:ActionKind.Mouse} map&&MappingExecutor.IsModifierDrag(map.Value);
        engine.SuppressLayerTap = key=>key.Equals("CapsLock",StringComparison.OrdinalIgnoreCase);
        engine.HasLongPress = input => HasConfiguredLongPress(FindMapping(input));
        engine.LongPressDuration = input => FindMapping(input)?.LongPressMs ?? 500;
        engine.DragPixels = config.MouseDragPixels;
        engine.Detected += text => Dispatcher.BeginInvoke(() => { if(text=="緊急停止"){macroEmergencyStop=true;ClearPendingActions();EngineToggle.IsChecked=false;}macroWindow?.Capture(text);LastInput.Text = "入力: " + text; if (detectMode) SelectInput(text.Split(' ')[0]); });
        engine.Enabled=false;
        try { engine.Start();engineStarted=true; } catch (Exception ex) { config.EngineEnabled=false;appliedConfig.EngineEnabled=false;store.Save(config);WpfMessageBox.Show("入力フックを開始できません。エンジンを停止しました。\n\n" + ex.Message,"入力エンジンを開始できません",MessageBoxButton.OK,MessageBoxImage.Error); }
        engine.Enabled = engineStarted&&config.EngineEnabled;
        EngineToggle.IsChecked = engine.Enabled;EngineToggle.IsEnabled=engineStarted;
        AdminStatus.Text = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator) ? "管理者モード" : "一般権限";
        archiveWatcher.Status+=text=>Dispatcher.BeginInvoke(()=>LastInput.Text=text);
        archiveWatcher.Apply(config);
        SetupTray();trayNumberTimer.Tick+=(_,_)=>UpdateTrayNumber();trayNumberTimer.Start();profileSwitchTimer.Tick+=(_,_)=>AutoSwitchProfile();profileSwitchTimer.Start();autoSaveTimer.Tick+=(_,_)=>{autoSaveTimer.Stop();SaveAndApply("自動保存しました");};UpdateStatus();
        if(capsRestartPending){LastInput.Text="CapsLock設定は再起動待ちです — Windowsを再起動するまで変更は有効になりません";LastInput.Foreground=WpfBrushes.Orange;}
        else if(configuredCapsLockRemap){LastInput.Text="CapsLock→F13設定を検出しました。CapsLockレイヤーとして互換動作します";LastInput.Foreground=WpfBrushes.LightGreen;}
        if(skipSetup&&NeedsFirstRunSetup){config.FirstRunCompleted=true;store.Save(config);}
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
            Dispatcher.BeginInvoke((Action)ApplyWindowsTitleBarTheme);
    }

    internal static bool IsWindowsAppDarkMode()
    {
        try
        {
            object? value=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1);
            return Convert.ToInt32(value,System.Globalization.CultureInfo.InvariantCulture)==0;
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
        bool dark=IsWindowsAppDarkMode();int enabled=dark?1:0;
        int result=DwmSetWindowAttribute(handle,20,ref enabled,sizeof(int));
        if(result!=0)DwmSetWindowAttribute(handle,19,ref enabled,sizeof(int));
        return dark;
    }
    internal static void FollowWindowsTitleBarTheme(Window window,Action<bool> applied)
    {
        void Apply(){if(!window.Dispatcher.HasShutdownStarted)applied(ApplyWindowsTitleBarTheme(window));}
        UserPreferenceChangedEventHandler handler=(_,e)=>
        {
            if(e.Category is UserPreferenceCategory.Color or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)window.Dispatcher.BeginInvoke((Action)Apply);
        };
        window.SourceInitialized+=(_,_)=>Apply();
        SystemEvents.UserPreferenceChanged+=handler;
        window.Closed+=(_,_)=>SystemEvents.UserPreferenceChanged-=handler;
    }

    void BuildKeyboard()
    {
        KeyboardPanel.Children.Clear();SecondaryKeyboardPanel.Children.Clear();AddSecondaryGroupFrames();if(config.KeyboardLayout=="US"){BuildUsKeyboard();return;}
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
            BorderBrush=new SolidColorBrush(WpfColor.FromArgb(0x75,0x33,0x46,0x5A)),
            Background=new SolidColorBrush(WpfColor.FromArgb(0x24,0x12,0x1B,0x25)),IsHitTestVisible=false
        };
        Canvas.SetLeft(frame,x);Canvas.SetTop(frame,y);SecondaryKeyboardPanel.Children.Add(frame);
        var heading=new TextBlock{Text=title,Foreground=new SolidColorBrush(WpfColor.FromRgb(0x8F,0xA0,0xB2)),FontSize=11,FontWeight=FontWeights.SemiBold,IsHitTestVisible=false};
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
    void AddJisEnter(){var shape=Geometry.Parse("M 0,0 L 160,0 L 160,108 L 22,108 L 22,56 L 0,56 Z");var b=MakeInputButton("Enter");b.Content="Enter";b.Width=160;b.Height=108;b.MinWidth=0;b.Margin=new Thickness(0);b.Clip=shape;Canvas.SetLeft(b,782);Canvas.SetTop(b,100);KeyboardPanel.Children.Add(b);var outline=new System.Windows.Shapes.Path{Data=shape,Stroke=new SolidColorBrush(WpfColor.FromRgb(70,81,104)),StrokeThickness=1,Fill=WpfBrushes.Transparent,IsHitTestVisible=false};Canvas.SetLeft(outline,782);Canvas.SetTop(outline,100);KeyboardPanel.Children.Add(outline);}
    readonly record struct KeySpec(string Key,string Label,double Width);
    System.Windows.Controls.Button MakeInputButton(string key) { var b = new System.Windows.Controls.Button { Content = key=="CapsLock"?"CapsLock\n(F13設定時)":key, Tag = key, Style = (Style)FindResource("KeyButton") };if(key=="Space")b.Width=210;else if(key=="CapsLock"){b.MinWidth=94;b.FontSize=10;}else if(key is "LeftShift" or "RightShift" or "Enter" or "Back")b.MinWidth=82;else if(key is "Tab" or "LeftCtrl" or "RightCtrl")b.MinWidth=70;b.Click += (_, _) => SelectVisualInput(key); return b; }
    void SelectVisualInput(string key)
    {
        if(key=="Space"&&currentLayer is "通常" or "Space"){ShowInlineNotice("SpaceキーはSpaceレイヤー専用のため、このレイヤーでは変更できません");return;}
        SelectInput(currentLayer=="通常"?key:currentLayer+"+"+key);
    }
    void InputButton_Click(object sender,RoutedEventArgs e){if(sender is System.Windows.Controls.Button{Tag:string key})SelectVisualInput(key);}
    void DestinationButton_PreviewMouseDown(object sender,MouseButtonEventArgs e)
    {
        var target=ValueBox.IsKeyboardFocusWithin?ValueBox:LongValueBox.IsKeyboardFocusWithin?LongValueBox:destinationInputTarget;
        if(target==null||!target.IsVisible||!target.IsEnabled||sender is not System.Windows.Controls.Button{Tag:string key})return;
        string token=key;var parts=target.Text.Split('+',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToList();
        parts.Add(token);target.Text=string.Join("+",parts);target.CaretIndex=target.Text.Length;
        var kindBox=target==LongValueBox?LongKindBox:KindBox;
        if(parts.Count>1)kindBox.SelectedValue=ActionKind.Shortcut;
        else if(key.StartsWith("Mouse",StringComparison.OrdinalIgnoreCase)||key.StartsWith("Wheel",StringComparison.OrdinalIgnoreCase)||key.StartsWith("Tilt",StringComparison.OrdinalIgnoreCase))kindBox.SelectedValue=ActionKind.Mouse;
        else if(kindBox.SelectedValue is not ActionKind.Key and not ActionKind.Shortcut)kindBox.SelectedValue=ActionKind.Key;
        e.Handled=true;FocusExecutionValue(target);
    }
    void InputButton_RightClick(object sender,MouseButtonEventArgs e)
    {
        if(sender is not System.Windows.Controls.Button{Tag:string key})return;
        e.Handled=true;
        if(key=="Space"&&currentLayer is "通常" or "Space"){ShowInlineNotice("Spaceキーはレイヤー専用のため変更できません");return;}
        var menu=CreateInputContextMenu(key);menu.PlacementTarget=(System.Windows.Controls.Button)sender;menu.IsOpen=true;
    }
    internal ContextMenu CreateInputContextMenu(string key)
    {
        string input=currentLayer=="通常"?key:currentLayer+"+"+key;
        var existing=CurrentProfile.Mappings.LastOrDefault(x=>x.Input.Equals(input,StringComparison.OrdinalIgnoreCase));
        var menu=new ContextMenu();
        var copy=new MenuItem{Header="この割り当てをコピー",IsEnabled=existing!=null};
        copy.Click+=(_,_)=>{copiedMapping=existing==null?null:CloneMapping(existing);ShowInlineNotice(input+" の割り当てをコピーしました");};
        var paste=new MenuItem{Header="コピーした割り当てを貼り付け",IsEnabled=copiedMapping!=null};
        paste.Click+=(_,_)=>{if(copiedMapping==null)return;CurrentProfile.Mappings.RemoveAll(x=>x.Input.Equals(input,StringComparison.OrdinalIgnoreCase));var map=CloneMapping(copiedMapping);map.Input=input;map.Layer=currentLayer;CurrentProfile.Mappings.Add(map);SelectInput(input);MarkDirty();ColorButtons();};
        var delete=new MenuItem{Header="この割り当てを削除",IsEnabled=existing!=null,Foreground=WpfBrushes.OrangeRed};
        delete.Click+=(_,_)=>{if(existing==null)return;CurrentProfile.Mappings.Remove(existing);if(selected?.Input.Equals(input,StringComparison.OrdinalIgnoreCase)==true)selected=null;MarkDirty();SelectInput(input);ShowInlineNotice(input+" の割り当てを削除しました");};
        menu.Items.Add(copy);menu.Items.Add(paste);menu.Items.Add(new Separator());menu.Items.Add(delete);
        return menu;
    }
    void LongPressOnly_Click(object sender,RoutedEventArgs e){ValueBox.Clear();LongPressExpander.IsExpanded=true;FocusExecutionValue(LongValueBox,true);MarkDirty();LastInput.Text="長押しのみ：短押しは元のキーを入力します";}
    void DestinationInputDone_Click(object sender,RoutedEventArgs e)=>CompleteDestinationInput(sender as FrameworkElement);
    void MainWindow_PreviewMouseDown(object sender,MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=MouseButton.Left||destinationInputTarget==null||e.OriginalSource is not DependencyObject source)return;
        if(IsDescendantOf(source,KeyboardPanel)||IsDescendantOf(source,SecondaryKeyboardPanel)||IsDescendantOf(source,MousePanel)||IsInteractiveClick(source))return;
        CompleteDestinationInput();
    }
    void CompleteDestinationInput(FrameworkElement? fallback=null)
    {
        ClearExecutionFocus(fallback);CloseAssignmentPane();LastInput.Text="入力完了 — 次の設定対象を画面から選択できます";LastInput.Foreground=WpfBrushes.LightGreen;
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
    void ActionKind_PreviewMouseLeftButtonUp(object sender,MouseButtonEventArgs e)=>FocusExecutionValue(ReferenceEquals(sender,LongKindBox)?LongValueBox:ValueBox,ReferenceEquals(sender,LongKindBox));
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
    void ChooseCatalogAction_Click(object sender,RoutedEventArgs e)
    {
        var picker=new ActionPickerWindow{Owner=this};
        if(picker.ShowDialog()!=true||picker.SelectedAction is not { } action)return;
        if(sender is System.Windows.Controls.Button{Tag:string tag}&&tag=="Long"){LongKindBox.SelectedValue=action.Kind;LongValueBox.Text=action.Value;FocusExecutionValue(LongValueBox,true);}
        else {KindBox.SelectedValue=action.Kind;ValueBox.Text=action.Value;FocusExecutionValue(ValueBox);}
        MarkDirty();
    }
    void OpenMacros_Click(object sender,RoutedEventArgs e)=>ShowMacroWindow(false,false);
    void ChooseMacro_Click(object sender,RoutedEventArgs e)=>ShowMacroWindow(true,sender is System.Windows.Controls.Button{Tag:string tag}&&tag=="Long");
    void ChooseProfileAction_Click(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();foreach(var profile in config.Profiles){var item=new MenuItem{Header=profile.Name};item.Click+=(_,_)=>{if(sender is System.Windows.Controls.Button{Tag:string tag}&&tag=="Long"){LongKindBox.SelectedValue=ActionKind.Profile;LongValueBox.Text=profile.Name;FocusExecutionValue(LongValueBox,true);}else{KindBox.SelectedValue=ActionKind.Profile;ValueBox.Text=profile.Name;FocusExecutionValue(ValueBox);}MarkDirty();};menu.Items.Add(item);}menu.IsOpen=true;
    }
    void ShowMacroWindow(bool assign,bool longPress)
    {
        string target=assign?$"{InputName.Text}（{(longPress?"長押し":"短押し")}）":"";var window=new MacroWindow(config,SetMacroRecording,assign,target){Owner=this};window.Saved+=()=>SaveAndApply("マクロを保存して反映しました");macroWindow=window;bool? result=window.ShowDialog();macroWindow=null;SetMacroRecording(false,false,false);
        if(!window.SaveRequested&&window.Changed)MarkDirty();if(!assign||result!=true||string.IsNullOrWhiteSpace(window.SelectedMacroName))return;
        if(longPress){LongKindBox.SelectedValue=ActionKind.Macro;LongValueBox.Text=window.SelectedMacroName;LongPressExpander.IsExpanded=true;FocusExecutionValue(LongValueBox,true);}else{KindBox.SelectedValue=ActionKind.Macro;ValueBox.Text=window.SelectedMacroName;FocusExecutionValue(ValueBox);}
    }
    void SetMacroRecording(bool recording,bool captureMouseMoves,bool useMappedActions)
    {
        if(recording){if(macroIsRecording)return;macroIsRecording=true;macroEmergencyStop=false;engineBeforeMacroRecording=engine.Enabled;MacroPlayer.StopAll();engine.Enabled=useMappedActions&&engineStarted;engine.CaptureMouseMoves=captureMouseMoves;EngineStatus.Text=useMappedActions?"● マクロ記録中（割り当て後のアクション）":captureMouseMoves?"● マクロ記録中（マウス軌跡あり）":"● マクロ記録中（物理キー）";EngineStatus.Foreground=WpfBrushes.Orange;}
        else{if(!macroIsRecording)return;macroIsRecording=false;engine.CaptureMouseMoves=false;engine.Enabled=engineBeforeMacroRecording&&config.EngineEnabled&&!macroEmergencyStop;UpdateStatus();}
    }
    void BrowseApplication_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new ApplicationPickerWindow{Owner=this};
        if(dialog.ShowDialog()==true&&!string.IsNullOrWhiteSpace(dialog.SelectedPath))ApplyApplicationSelection(sender,dialog.SelectedPath);
    }
    void ApplyApplicationSelection(object sender,string path)
    {
        if(sender is System.Windows.Controls.Button{Tag:string tag}&&tag=="Long"){LongKindBox.SelectedValue=ActionKind.Launch;LongValueBox.Text=path;FocusExecutionValue(LongValueBox,true);}
        else{KindBox.SelectedValue=ActionKind.Launch;ValueBox.Text=path;FocusExecutionValue(ValueBox);}
        MarkDirty();
    }

    void SelectInput(string input,bool focusExecution=true)
    {
        string layer="通常";selectedBaseInput=input;
        int plus=input.IndexOf('+');if(plus>0){layer=input[..plus];selectedBaseInput=input[(plus+1)..];}
        detectMode = false; selected = CurrentProfile.Mappings.FirstOrDefault(x => x.Input == input) ?? new Mapping { Input = input };
        currentLayer=layer;loading = true; InputName.Text = selected.Input;CurrentLayerText.Text=LayerDisplayName(layer); KindBox.SelectedValue = selected.Kind; ValueBox.Text = selected.Value; LongKindBox.SelectedValue=selected.LongPressKind; LongValueBox.Text=selected.LongPressValue; LongPressBox.Text = selected.LongPressMs.ToString(); EnabledBox.IsChecked = selected.Enabled; LongPressExpander.IsExpanded=HasConfiguredLongPress(selected);AssignmentEditor.IsEnabled=true;AssignmentEditor.Opacity=1;SelectInputHint.Visibility=Visibility.Collapsed;loading = false;UpdateBrowseButtons();UpdateLayerButtons();ColorButtons();ShowAssignmentPane();if(focusExecution)FocusExecutionValue(ValueBox);
    }
    void EditorChanged(object sender, EventArgs e)
    {
        if (loading || selected == null) return;
        var longKind=LongKindBox.SelectedValue is ActionKind lk?lk:ActionKind.None;if(ReferenceEquals(sender,LongKindBox)&&longKind==ActionKind.None&&!string.IsNullOrEmpty(LongValueBox.Text)){loading=true;LongValueBox.Clear();loading=false;}selected.Kind = KindBox.SelectedValue is ActionKind k ? k : ActionKind.None; selected.Value = ValueBox.Text; selected.LongPressKind=longKind; selected.LongPressValue = LongValueBox.Text;selected.Layer=currentLayer; selected.Enabled = EnabledBox.IsChecked == true; if (int.TryParse(LongPressBox.Text, out var ms)) selected.LongPressMs = ms;
        if (!CurrentProfile.Mappings.Contains(selected)) CurrentProfile.Mappings.Add(selected);
        UpdateBrowseButtons();
        MarkDirty();ColorButtons();
        if(ReferenceEquals(sender,KindBox))FocusExecutionValue(ValueBox);
        else if(ReferenceEquals(sender,LongKindBox))FocusExecutionValue(LongValueBox,true);
    }
    void UpdateBrowseButtons()
    {
        if(BrowseApplicationButton!=null)BrowseApplicationButton.Visibility=Visibility.Visible;
        if(LongBrowseApplicationButton!=null)LongBrowseApplicationButton.Visibility=Visibility.Visible;
    }
    bool HandleInput(string input)
    {
        bool longPress=input.EndsWith(":Long",StringComparison.OrdinalIgnoreCase),dragStart=input.EndsWith(":DragStart",StringComparison.OrdinalIgnoreCase),dragEnd=input.EndsWith(":DragEnd",StringComparison.OrdinalIgnoreCase),pressStart=input.EndsWith(":PressStart",StringComparison.OrdinalIgnoreCase),pressEnd=input.EndsWith(":PressEnd",StringComparison.OrdinalIgnoreCase);
        string baseInput=longPress?input[..^5]:dragStart?input[..^10]:dragEnd?input[..^8]:pressStart?input[..^11]:pressEnd?input[..^9]:input;
        var map = FindMapping(baseInput);
        if(map==null){if(dragEnd||pressEnd){if(!QueueDragAction(null,input))_=Task.Run(InputEngine.EndModifierDrag);return true;}return false;}
        var snapshot=CloneMapping(map);
        if((pressStart||pressEnd||dragStart||dragEnd)&&MappingExecutor.IsModifierDrag(snapshot.Value)){bool queued=QueueDragAction(snapshot,input);if(queued)RecordMappedAction(snapshot,input);return queued;}
        if(pressStart||pressEnd)return false;
        if(!actionQueue.TryAdd((snapshot,input)))Dispatcher.BeginInvoke(()=>{LastInput.Text="連続入力が多すぎるため一部を安全に破棄しました";LastInput.Foreground=WpfBrushes.OrangeRed;});else RecordMappedAction(snapshot,input);return true;
    }
    void RecordMappedAction(Mapping map,string input){if(macroIsRecording&&config.RecordMappedActionsInMacros)Dispatcher.BeginInvoke(()=>macroWindow?.CaptureMappedAction(map,input));}
    void ProcessActions()
    {
        foreach(var item in actionQueue.GetConsumingEnumerable())try{bool result=executor.Execute(item.Map,item.Input,out var value);if(result)Dispatcher.BeginInvoke(()=>{LastInput.Text=$"実行: {item.Map.Input} → {value}";LastInput.Foreground=value.StartsWith("エラー:",StringComparison.Ordinal)?WpfBrushes.OrangeRed:WpfBrushes.LightGreen;});}catch(Exception ex){InputEngine.ReleaseAll();Dispatcher.BeginInvoke(()=>{LastInput.Text="実行エラー: "+ex.Message;LastInput.Foreground=WpfBrushes.OrangeRed;});}
    }
    void ProcessDragActions()
    {
        foreach(var item in dragActionQueue.GetConsumingEnumerable())try{if(item.Map==null){InputEngine.EndModifierDrag();continue;}bool result=executor.Execute(item.Map,item.Input,out var value);if(result)Dispatcher.BeginInvoke(()=>{LastInput.Text=$"実行: {item.Map.Input} → {value}";LastInput.Foreground=value.StartsWith("エラー:",StringComparison.Ordinal)?WpfBrushes.OrangeRed:WpfBrushes.LightGreen;});}catch(Exception ex){InputEngine.ReleaseAll();Dispatcher.BeginInvoke(()=>{LastInput.Text="ドラッグ実行エラー: "+ex.Message;LastInput.Foreground=WpfBrushes.OrangeRed;});}
    }
    bool QueueDragAction(Mapping? map,string input){try{return !dragActionQueue.IsAddingCompleted&&dragActionQueue.TryAdd((map,input));}catch(InvalidOperationException){return false;}}
    Mapping? FindMapping(string input)
    {
        if(ConditionMatcher.IsCursorOverTaskbar()){var taskbar=FindProfileMapping(appliedConfig.Profiles,AppliedProfile.Name,"Taskbar+"+input,x=>x.Enabled&&AppMatches(x.Application));if(taskbar!=null)return taskbar;}
        return FindProfileMapping(appliedConfig.Profiles,AppliedProfile.Name,input,x=>x.Enabled&&AppMatches(x.Application));
    }
    bool HasMapping(string input){if(input.EndsWith("+*",StringComparison.Ordinal)){string prefix=input[..^1];return FindProfileMapping(appliedConfig.Profiles,AppliedProfile.Name,null,x=>MappingInterceptsInput(x)&&x.Input.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&AppMatches(x.Application))!=null;}return MappingInterceptsInput(FindMapping(input));}
    internal static bool MappingInterceptsInput(Mapping? map)=>map is {Enabled:true}&&(map.Kind!=ActionKind.None||HasConfiguredLongPress(map));
    internal static bool HasConfiguredLongPress(Mapping? map)=>map!=null&&map.LongPressKind!=ActionKind.None&&(map.LongPressKind==ActionKind.Disabled||!string.IsNullOrWhiteSpace(map.LongPressValue));
    internal static Mapping? FindProfileMapping(IReadOnlyList<Profile> profiles,string activeName,string? exactInput,Func<Mapping,bool>? predicate=null)
    {
        if(profiles.Count==0)return null;var active=profiles.FirstOrDefault(x=>x.Name==activeName)??profiles[0];
        foreach(var profile in ReferenceEquals(active,profiles[0])?[active]:new[]{active,profiles[0]})
        {var mapping=profile.Mappings.LastOrDefault(x=>(exactInput==null||x.Input.Equals(exactInput,StringComparison.OrdinalIgnoreCase))&&(predicate?.Invoke(x)??true));if(mapping!=null)return mapping;}
        return null;
    }
    static bool AppMatches(string app)=>ConditionMatcher.ForegroundProcessMatches(app);

    void RefreshProfiles() { loading = true; ProfileBox.ItemsSource = null; ProfileBox.ItemsSource = config.Profiles.Select(x => x.Name).ToList(); ProfileBox.SelectedItem = config.ActiveProfile; loading = false; ColorButtons(); }
    void ColorButtons()
    {
        foreach(var b in VisualInputButtons())
        {
            bool reserved=(string)b.Tag=="Space"&&currentLayer is "通常" or "Space";string input=currentLayer=="通常"?(string)b.Tag:currentLayer+"+"+(string)b.Tag;
            bool assigned=FindProfileMapping(config.Profiles,CurrentProfile.Name,input,x=>x.Enabled)!=null;
            b.Background=reserved?new SolidColorBrush(WpfColor.FromRgb(52,59,68)):assigned?new SolidColorBrush(WpfColor.FromRgb(23,141,121)):new SolidColorBrush(WpfColor.FromRgb(27,39,51));
            b.Opacity=reserved ? 0.48 : 1;b.ToolTip=reserved?"Spaceレイヤー専用キーのため変更できません":null;
        }
    }
    IEnumerable<System.Windows.Controls.Button> VisualInputButtons()=>InputButtons(KeyboardPanel).Concat(InputButtons(SecondaryKeyboardPanel)).Concat(InputButtons(MousePanel));
    static IEnumerable<System.Windows.Controls.Button> InputButtons(System.Windows.Controls.Panel panel){foreach(UIElement child in panel.Children){if(child is System.Windows.Controls.Button b&&b.Tag is string)yield return b;if(child is System.Windows.Controls.Panel nested)foreach(var b2 in InputButtons(nested))yield return b2;}}
    static string LayerDisplayName(string layer)=>layer switch{"通常"=>"通常レイヤー","Taskbar"=>"タスクバー上で有効",_=>layer+" レイヤー"};
    void MarkDirty(){if(config.AutoSave){LastInput.Text="変更を自動保存しています…";LastInput.Foreground=WpfBrushes.LightGreen;autoSaveTimer.Stop();autoSaveTimer.Start();}else{LastInput.Text="未保存の変更があります — 保存すると反映されます";LastInput.Foreground=WpfBrushes.Orange;}}
    void ShowInlineNotice(string message)
    {
        LastInput.Text="ⓘ "+message;LastInput.Foreground=new SolidColorBrush(WpfColor.FromRgb(246,198,106));
    }
    void UpdateLayerButtons(){if(NormalLayerButton==null)return;foreach(var b in new[]{NormalLayerButton,SpaceLayerButton,CapsLockLayerButton,TaskbarLayerButton,RightMouseLayerButton,BackMouseLayerButton,ForwardMouseLayerButton}){bool active=Equals(b.Tag,currentLayer);b.Background=new SolidColorBrush(active?WpfColor.FromRgb(20,107,92):WpfColor.FromRgb(27,39,51));b.BorderBrush=new SolidColorBrush(active?WpfColor.FromRgb(114,224,193):WpfColor.FromRgb(51,70,90));}CurrentLayerText.Text=LayerDisplayName(currentLayer);}
    void UpdateStatus() { EngineStatus.Text = engine.Enabled ? "● エンジン稼働中" : "■ 停止中"; EngineStatus.Foreground = engine.Enabled ? WpfBrushes.LightGreen : WpfBrushes.OrangeRed; ProfileStatus.Text = config.ActiveProfile==appliedConfig.ActiveProfile?"プロファイル: "+config.ActiveProfile:"編集中: "+config.ActiveProfile+"（保存後に切替）"; }
    void SetupTray() { tray.Text = "RELYR v"+DisplayVersion; defaultTrayIcon=CreateDefaultTrayIcon();tray.Icon=defaultTrayIcon; tray.Visible = true; RebuildTrayMenu();UpdateTrayNumber(); tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => {Show();WindowState=WindowState.Normal;Activate();}); }
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
        var old=tray.ContextMenuStrip;var menu=new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("表示",null,(_,_)=>Dispatcher.Invoke(()=>{Show();WindowState=WindowState.Normal;Activate();}));
        menu.Items.Add("有効 / 一時停止",null,(_,_)=>Dispatcher.Invoke(()=>EngineToggle.IsChecked=!EngineToggle.IsChecked));
        var profiles=new System.Windows.Forms.ToolStripMenuItem("プロファイル");
        foreach(var profile in appliedConfig.Profiles.Where(p=>config.Profiles.Any(x=>x.Name==p.Name))){var item=new System.Windows.Forms.ToolStripMenuItem(profile.Name){Checked=profile.Name==appliedConfig.ActiveProfile};item.Click+=(_,_)=>Dispatcher.Invoke(()=>SwitchProfile(profile.Name,true));profiles.DropDownItems.Add(item);}menu.Items.Add(profiles);
        menu.Items.Add("押下キーをすべて解除",null,(_,_)=>InputEngine.ReleaseAll());
        menu.Items.Add("セーフモード",null,(_,_)=>Dispatcher.Invoke(()=>{EngineToggle.IsChecked=false;InputEngine.ReleaseAll();}));
        menu.Items.Add("終了",null,(_,_)=>Dispatcher.Invoke(()=>{allowClose=true;Close();}));
        tray.ContextMenuStrip=menu;old?.Dispose();
    }

    void ProfileChanged(object sender, SelectionChangedEventArgs e) { if (loading || ProfileBox.SelectedItem is not string name) return;SwitchProfile(name,false); }
    void KeyboardLayoutChanged(object sender,SelectionChangedEventArgs e){if(loading||config==null)return;config.KeyboardLayout=KeyboardLayoutBox.SelectedIndex==1?"US":"JIS";appliedConfig.KeyboardLayout=config.KeyboardLayout;engine.UseUsLayout=config.KeyboardLayout=="US";BuildKeyboard();ColorButtons();var persisted=store.Load();persisted.KeyboardLayout=config.KeyboardLayout;store.Save(persisted);ShowInlineNotice(config.KeyboardLayout+"配列へ切り替えました");}
    void MainContent_SizeChanged(object sender,SizeChangedEventArgs e)
    {
        if(WorkspaceGrid==null||AssignmentPane==null||LowerInputRow==null)return;
        double gap=e.NewSize.Width<1000?10:e.NewSize.Width<1450?14:18;
        LayerNavigationColumn.Width=new GridLength(e.NewSize.Width<950?140:e.NewSize.Width<1500?150:165);
        AssignmentPaneColumn.Width=new GridLength(e.NewSize.Width<950?280:e.NewSize.Width<1500?300:320);
        MouseColumn.Width=new GridLength(e.NewSize.Width<1000?190:e.NewSize.Width<1250?230:e.NewSize.Width<1550?270:300);
        LowerInputRow.Height=new GridLength(Math.Clamp(e.NewSize.Height*.38,240,370));
        KeyboardViewbox.MaxWidth=e.NewSize.Width<1100?double.PositiveInfinity:e.NewSize.Width<1500?1120:1220;
        WorkspaceGrid.Margin=new Thickness(gap);AssignmentPane.Padding=new Thickness(gap);UpdateLayerButtonWidths();
    }
    void LayerButtonsPanel_SizeChanged(object sender,SizeChangedEventArgs e)=>UpdateLayerButtonWidths();
    void UpdateLayerButtonWidths()
    {
        if(LayerButtonsPanel==null)return;
        bool compact=MainContentGrid.ActualHeight>0&&MainContentGrid.ActualHeight<620;
        bool narrow=LayerNavigationColumn.Width.IsAbsolute&&LayerNavigationColumn.Width.Value<155;
        LayerNavigationHint.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
        DetectInputButton.Margin=compact?new Thickness(3,1,3,3):new Thickness(3);
        foreach(var category in new[]{KeyboardLayerCategory,MouseLayerCategory,WindowsLayerCategory})category.Margin=compact?new Thickness(8,2,8,1):new Thickness(8,8,8,3);
        foreach(var button in LayerButtonsPanel.Children.OfType<System.Windows.Controls.Button>())
        {
            button.Width=double.NaN;button.Height=compact?36:50;button.MinHeight=36;button.Padding=compact?new Thickness(6,0,4,0):narrow?new Thickness(7,4,5,4):new Thickness(10,5,10,5);button.FontSize=compact?12:narrow?12:14;button.Margin=compact?new Thickness(3,1,3,1):new Thickness(3);button.HorizontalAlignment=System.Windows.HorizontalAlignment.Stretch;button.HorizontalContentAlignment=System.Windows.HorizontalAlignment.Left;
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
        config.ActiveProfile=name;
        if(appliedConfig.Profiles.Any(x=>x.Name==name)){appliedConfig.ActiveProfile=name;if(persist){var persisted=store.Load();if(persisted.Profiles.Any(x=>x.Name==name)){persisted.ActiveProfile=name;store.Save(persisted);}}}
        selected=null;InputName.Clear();AssignmentEditor.IsEnabled=false;AssignmentEditor.Opacity=.55;SelectInputHint.Visibility=Visibility.Visible;CloseAssignmentPane(false);if(refresh)RefreshProfiles();ColorButtons();UpdateStatus();RebuildTrayMenu();
    }
    void AutoSwitchProfile()
    {
        if(!appliedConfig.Profiles.Skip(1).Any(x=>x.AutoSwitchEnabled))return;
        string process=ConditionMatcher.ProcessUnderCursor();
        if(IsOwnProcess(process))return;
        var target=SelectAutomaticProfile(appliedConfig.Profiles,process);if(target.Name!=appliedConfig.ActiveProfile)SwitchProfile(target.Name,true,false);
    }
    internal static Profile SelectAutomaticProfile(IReadOnlyList<Profile> profiles,string process)=>profiles.Skip(1).FirstOrDefault(x=>x.AutoSwitchEnabled&&x.AutoSwitchApplications.Any(app=>ConditionMatcher.Matches(app,process)))??profiles[0];
    internal static bool IsOwnProcess(string process,string? executablePath=null)=>!string.IsNullOrWhiteSpace(process)&&ConditionMatcher.Matches(System.IO.Path.GetFileNameWithoutExtension(executablePath??Environment.ProcessPath??"RELYR"),process);
    void NewProfile_Click(object s, RoutedEventArgs e) { var name=PromptText("新しいプロファイル","プロファイル名",$"プロファイル {config.Profiles.Count+1}");if(string.IsNullOrWhiteSpace(name)||config.Profiles.Any(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase))){if(!string.IsNullOrWhiteSpace(name))ShowInlineNotice("同じ名前のプロファイルがあります");return;}var source=SelectProfile("割り当てのコピー元（コピーしない場合はキャンセル）",true);config.Profiles.Add(new Profile{Name=name,Mappings=source?.Mappings.Select(CloneMapping).ToList()??[]});config.ActiveProfile=name;RefreshProfiles();MarkDirty();UpdateStatus(); }
    void DuplicateProfile_Click(object s,RoutedEventArgs e){var source=CurrentProfile;var name=source.Name+" のコピー";int i=2;while(config.Profiles.Any(x=>x.Name==name))name=source.Name+$" のコピー {i++}";var copy=new Profile{Name=name,Mappings=source.Mappings.Select(CloneMapping).ToList()};config.Profiles.Add(copy);config.ActiveProfile=name;RefreshProfiles();MarkDirty();UpdateStatus();}
    void RenameProfile_Click(object s,RoutedEventArgs e){if(CurrentProfile==config.Profiles[0]){ShowInlineNotice("標準プロファイルの名前は変更できません");return;}var old=CurrentProfile.Name;var name=PromptText("プロファイル名を変更","新しい名前",old);if(string.IsNullOrWhiteSpace(name)||config.Profiles.Any(x=>x!=CurrentProfile&&x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))return;CurrentProfile.Name=name;if(config.ActiveProfile==old)config.ActiveProfile=name;foreach(var map in config.Profiles.SelectMany(x=>x.Mappings)){if(map.Kind==ActionKind.Profile&&map.Value==old)map.Value=name;if(map.LongPressKind==ActionKind.Profile&&map.LongPressValue==old)map.LongPressValue=name;}RefreshProfiles();MarkDirty();}
    void CopyProfile_Click(object s,RoutedEventArgs e){var source=SelectProfile("割り当てのコピー元を選択",false);if(source==null||source==CurrentProfile)return;if(WpfMessageBox.Show($"「{source.Name}」の割り当てで「{CurrentProfile.Name}」を置き換えますか？","割り当てコピー",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;CurrentProfile.Mappings=source.Mappings.Select(CloneMapping).ToList();MarkDirty();ColorButtons();}
    void ConfigureProfileAutoSwitch_Click(object s,RoutedEventArgs e){if(CurrentProfile==config.Profiles[0]){ShowInlineNotice("標準プロファイルは自動切替の戻り先です");return;}if(CurrentProfile.AutoSwitchEnabled){var choice=WpfMessageBox.Show($"「{CurrentProfile.Name}」の自動切替はオンです。\n\nはい：対象アプリを追加\nいいえ：自動切替をオフ\nキャンセル：変更しない","プロファイル自動切替",MessageBoxButton.YesNoCancel);if(choice==MessageBoxResult.Cancel)return;if(choice==MessageBoxResult.No){CurrentProfile.AutoSwitchEnabled=false;MarkDirty();ShowInlineNotice("自動切替をオフにしました");return;}}var app=SelectRunningApplication();if(string.IsNullOrWhiteSpace(app))return;CurrentProfile.AutoSwitchEnabled=true;if(!CurrentProfile.AutoSwitchApplications.Contains(app,StringComparer.OrdinalIgnoreCase))CurrentProfile.AutoSwitchApplications.Add(app);MarkDirty();ShowInlineNotice($"カーソルが {app} 上にある時、自動的に「{CurrentProfile.Name}」へ切り替えます");}
    void DeleteProfile_Click(object s,RoutedEventArgs e){if(CurrentProfile==config.Profiles[0]){ShowInlineNotice("標準プロファイルは削除できません");return;}if(WpfMessageBox.Show($"「{CurrentProfile.Name}」を削除しますか？","確認",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;config.Profiles.Remove(CurrentProfile);config.ActiveProfile=config.Profiles[0].Name;RefreshProfiles();MarkDirty();UpdateStatus();RebuildTrayMenu();}
    static Mapping CloneMapping(Mapping x)=>new(){Input=x.Input,Kind=x.Kind,Value=x.Value,LongPressKind=x.LongPressKind,LongPressValue=x.LongPressValue,DragValue=x.DragValue,DragEndValue=x.DragEndValue,Enabled=x.Enabled,LongPressMs=x.LongPressMs,Application=x.Application,Layer=x.Layer,Description=x.Description};
    void Save_Click(object s, RoutedEventArgs e)=>SaveAndApply("設定を保存し、エンジンへ反映しました");
    void SaveAndApply(string message){var errors=ConfigValidator.Validate(config);if(errors.Count>0){WpfMessageBox.Show(string.Join("\n",errors),"設定の確認",MessageBoxButton.OK,MessageBoxImage.Warning);return;}store.Save(config);appliedConfig=store.Clone(config);engine.SpaceHoldRepeatEnabled=config.SpaceHoldRepeatEnabled;engine.SpaceHoldRepeatDelayMs=config.SpaceHoldRepeatDelayMs;UpdateStatus();RebuildTrayMenu();LastInput.Text=message;LastInput.Foreground=WpfBrushes.LightGreen;}
    void Detect_Click(object s, RoutedEventArgs e) { detectMode = true; LastInput.Text = "入力を待っています…"; }
    void LayerButton_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not System.Windows.Controls.Button{Tag:string layer} button)return;
        ClearExecutionFocus(button);
        if(layer=="CapsLock"&&!ConfirmCapsLockLayer())return;
        currentLayer=layer;UpdateLayerButtons();ColorButtons();
        if(!string.IsNullOrEmpty(selectedBaseInput))SelectInput(layer=="通常"?selectedBaseInput:layer+"+"+selectedBaseInput,false);
        ClearExecutionFocus(button);
    }
    bool ConfirmCapsLockLayer()
    {
        if(capsLockRemapped)return true;ShowInlineNotice("CapsLockレイヤーにはF13リマップ設定とWindows再起動が必要です");WpfMessageBox.Show("CapsLockレイヤーは安全性のため、CapsLock→F13設定を行った場合だけ動作します。\n\n［アプリ設定］→［レイヤー］で設定し、Windowsを再起動してください。","CapsLockレイヤーは無効です",MessageBoxButton.OK,MessageBoxImage.Information);return false;
    }
    internal void SetCapsLockRemapForTest(bool enabled){capsLockRemapped=enabled;engine.TreatF13AsCapsLock=enabled;}
    void EngineChanged(object s, RoutedEventArgs e) { if (loading || config == null) return;if(!engineStarted){loading=true;EngineToggle.IsChecked=false;loading=false;return;}engine.Enabled = EngineToggle.IsChecked == true; if(!engine.Enabled)ClearPendingActions();config.EngineEnabled = engine.Enabled;appliedConfig.EngineEnabled=engine.Enabled;var persisted=store.Load();persisted.EngineEnabled=engine.Enabled;store.Save(persisted);UpdateStatus(); }
    void AutoSaveChanged(object s,RoutedEventArgs e){if(loading||config==null)return;config.AutoSave=AutoSaveToggle.IsChecked==true;UpdateAutoSaveToggleText();if(config.AutoSave)SaveAndApply("自動保存をオンにし、現在の変更を保存・反映しました");else{appliedConfig.AutoSave=false;var persisted=store.Load();persisted.AutoSave=false;store.Save(persisted);LastInput.Text="自動保存をオフにしました";LastInput.Foreground=WpfBrushes.LightGreen;}}
    void UpdateAutoSaveToggleText()=>AutoSaveToggle.Content=AutoSaveToggle.IsChecked==true?"自動保存 オン":"自動保存 オフ";
    void ClearPendingActions(){while(actionQueue.TryTake(out _)){}while(dragActionQueue.TryTake(out _)){}InputEngine.EndModifierDrag();MacroPlayer.StopAll();}
    void OpenSettings_Click(object sender,RoutedEventArgs e)
    {
        var window=new SettingsWindow(config){Owner=this};if(window.ShowDialog()!=true)return;
        if(window.CapsRemapChanged){LastInput.Text="CapsLock設定を変更しました — Windows再起動後に反映されます";LastInput.Foreground=WpfBrushes.Orange;}
        if(window.ResetConfig is { } reset){ApplyCompleteConfig(reset,"すべての設定を初期状態へ戻しました");if(window.ResetNeedsRestart)SettingsWindow.PromptForWindowsRestart(this,false);return;}
        if(window.ImportedConfig is { } imported){ApplyCompleteConfig(imported,"設定をインポートして反映しました");if(window.ImportedCapsLockNeedsRestart)SettingsWindow.PromptForWindowsRestart(this,window.ImportedCapsLockEnabled);return;}
        config.SpaceHoldRepeatEnabled=window.SpaceHoldRepeat;config.SpaceHoldRepeatDelayMs=window.SpaceHoldRepeatDelay;appliedConfig.SpaceHoldRepeatEnabled=config.SpaceHoldRepeatEnabled;appliedConfig.SpaceHoldRepeatDelayMs=config.SpaceHoldRepeatDelayMs;engine.SpaceHoldRepeatEnabled=config.SpaceHoldRepeatEnabled;engine.SpaceHoldRepeatDelayMs=config.SpaceHoldRepeatDelayMs;var repeatSettings=store.Load();repeatSettings.SpaceHoldRepeatEnabled=config.SpaceHoldRepeatEnabled;repeatSettings.SpaceHoldRepeatDelayMs=config.SpaceHoldRepeatDelayMs;store.Save(repeatSettings);
        try{if(window.StartWithWindowsChanged)StartupService.SetEnabled(window.StartWithWindows);config.StartWithWindows=window.StartWithWindows;config.AutoExtractDesktopArchives=window.AutoExtract;config.ArchiveWatchFolder=window.ArchiveWatchFolder;config.ArchiveDestinationFolder=window.ArchiveDestinationFolder;config.DeleteArchiveAfterExtract=window.DeleteAfterExtract;config.ShowDesktopNumberInTray=window.ShowDesktopNumberInTray;config.CloseWindowUnderCursor=window.CloseWindowUnderCursor;config.AutoSave=window.AutoSave;appliedConfig.StartWithWindows=config.StartWithWindows;appliedConfig.AutoExtractDesktopArchives=config.AutoExtractDesktopArchives;appliedConfig.ArchiveWatchFolder=config.ArchiveWatchFolder;appliedConfig.ArchiveDestinationFolder=config.ArchiveDestinationFolder;appliedConfig.DeleteArchiveAfterExtract=config.DeleteArchiveAfterExtract;appliedConfig.ShowDesktopNumberInTray=config.ShowDesktopNumberInTray;appliedConfig.CloseWindowUnderCursor=config.CloseWindowUnderCursor;appliedConfig.AutoSave=config.AutoSave;var persisted=store.Load();persisted.StartWithWindows=config.StartWithWindows;persisted.AutoExtractDesktopArchives=config.AutoExtractDesktopArchives;persisted.ArchiveWatchFolder=config.ArchiveWatchFolder;persisted.ArchiveDestinationFolder=config.ArchiveDestinationFolder;persisted.DeleteArchiveAfterExtract=config.DeleteArchiveAfterExtract;persisted.ShowDesktopNumberInTray=config.ShowDesktopNumberInTray;persisted.CloseWindowUnderCursor=config.CloseWindowUnderCursor;persisted.AutoSave=config.AutoSave;store.Save(persisted);archiveWatcher.Apply(config);UpdateTrayNumber();if(config.AutoSave)SaveAndApply("自動保存をオンにし、現在の変更を保存・反映しました");else{LastInput.Text="アプリ設定を保存しました — 自動保存はオフです";LastInput.Foreground=WpfBrushes.LightGreen;}}
        catch(Exception ex){WpfMessageBox.Show("設定を保存できません: "+ex.Message);}
        finally{loading=true;AutoSaveToggle.IsChecked=config.AutoSave;UpdateAutoSaveToggleText();loading=false;}
    }
    void ApplyCompleteConfig(AppConfig value,string message)
    {
        ClearPendingActions();config=value;store.Save(config);appliedConfig=store.Clone(config);
        bool pending=LegacyKeyRemapService.IsRestartStillPending(config);capsLockRemapped=pending?config.CapsLockRemapEffectiveBeforeRestart:LegacyKeyRemapService.HasCapsLockToF13();engine.TreatF13AsCapsLock=capsLockRemapped;
        engine.UseUsLayout=config.KeyboardLayout=="US";engine.SpaceHoldRepeatEnabled=config.SpaceHoldRepeatEnabled;engine.SpaceHoldRepeatDelayMs=config.SpaceHoldRepeatDelayMs;engine.Enabled=engineStarted&&config.EngineEnabled;
        loading=true;KeyboardLayoutBox.SelectedIndex=config.KeyboardLayout=="US"?1:0;AutoSaveToggle.IsChecked=config.AutoSave;EngineToggle.IsChecked=engine.Enabled;loading=false;
        selected=null;selectedBaseInput="";currentLayer="通常";InputName.Clear();AssignmentEditor.IsEnabled=false;AssignmentEditor.Opacity=.55;SelectInputHint.Visibility=Visibility.Visible;
        BuildKeyboard();RefreshProfiles();UpdateLayerButtons();ColorButtons();UpdateAutoSaveToggleText();archiveWatcher.Apply(config);UpdateTrayNumber();UpdateStatus();RebuildTrayMenu();LastInput.Text=message;LastInput.Foreground=WpfBrushes.LightGreen;
    }
    public void ShowFirstRunSetup(){if(!NeedsFirstRunSetup)return;var setup=new SetupWindow{Owner=this};if(setup.ShowDialog()==true){config.ActiveProfile="標準";config.FirstRunCompleted=setup.DoNotShowAgain;store.Save(config);RefreshProfiles();RebuildTrayMenu();}}
    public void ShowFromExternalLaunch(){Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();Topmost=true;Topmost=false;Focus();}
    void Delete_Click(object s, RoutedEventArgs e) { if (selected == null) return; CurrentProfile.Mappings.Remove(selected); var input = selected.Input; selected = null; SelectInput(input);MarkDirty();ColorButtons(); }
    void Window_Closing(object? s, CancelEventArgs e) { if(!allowClose){e.Cancel=true;Hide();return;}SystemEvents.UserPreferenceChanged-=WindowsThemeChanged;trayNumberTimer.Stop();profileSwitchTimer.Stop();autoSaveTimer.Stop();engine.Enabled=false;ClearPendingActions();actionQueue.CompleteAdding();dragActionQueue.CompleteAdding();try{Task.WaitAll([actionWorker,dragActionWorker],2000);}catch{}InputEngine.ReleaseAll();engine.Dispose();tray.Visible=false;tray.Dispose();numberedTrayIcon?.Dispose();defaultTrayIcon?.Dispose();archiveWatcher.Dispose(); }
    public void PrepareForSystemShutdown()
    {
        allowClose=true;
        engine.Enabled=false;
        ClearPendingActions();
        InputEngine.ReleaseAll();
        engine.Dispose();
        archiveWatcher.Dispose();
    }
    public void RequestApplicationExit(){allowClose=true;Close();}
    string? PromptText(string title,string label,string initial)
    {
        var dialog=new Window{Title=title,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Width=430,Height=190,ResizeMode=ResizeMode.NoResize,Background=new SolidColorBrush(WpfColor.FromRgb(23,29,39)),Foreground=WpfBrushes.White,ShowInTaskbar=false};var panel=new StackPanel{Margin=new Thickness(20)};panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,0,0,7)});var box=new TextBox{Text=initial,Padding=new Thickness(8)};panel.Children.Add(box);var buttons=new StackPanel{Orientation=System.Windows.Controls.Orientation.Horizontal,HorizontalAlignment=System.Windows.HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};var cancel=new System.Windows.Controls.Button{Content="キャンセル",Padding=new Thickness(15,7,15,7),Margin=new Thickness(3)};var ok=new System.Windows.Controls.Button{Content="決定",Padding=new Thickness(18,7,18,7),Margin=new Thickness(3),Background=new SolidColorBrush(WpfColor.FromRgb(31,143,123)),Foreground=WpfBrushes.White,IsDefault=true};cancel.Click+=(_,_)=>dialog.DialogResult=false;ok.Click+=(_,_)=>dialog.DialogResult=true;buttons.Children.Add(cancel);buttons.Children.Add(ok);panel.Children.Add(buttons);dialog.Content=panel;dialog.Loaded+=(_,_)=>{box.Focus();box.SelectAll();};return dialog.ShowDialog()==true?box.Text.Trim():null;
    }
    Profile? SelectProfile(string title,bool allowNoCopy)
    {
        var dialog=new Window{Title=title,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Width=420,Height=360,Background=new SolidColorBrush(WpfColor.FromRgb(23,29,39)),Foreground=WpfBrushes.White,ShowInTaskbar=false};var grid=new Grid{Margin=new Thickness(18)};grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});var list=new ListBox{ItemsSource=config.Profiles,DisplayMemberPath="Name",Background=new SolidColorBrush(WpfColor.FromRgb(29,35,48)),Foreground=WpfBrushes.White};grid.Children.Add(list);var buttons=new StackPanel{Orientation=System.Windows.Controls.Orientation.Horizontal,HorizontalAlignment=System.Windows.HorizontalAlignment.Right};var cancel=new System.Windows.Controls.Button{Content=allowNoCopy?"コピーせず作成":"キャンセル",Margin=new Thickness(3),Padding=new Thickness(14,7,14,7)};var ok=new System.Windows.Controls.Button{Content="選択",Margin=new Thickness(3),Padding=new Thickness(18,7,18,7),Background=new SolidColorBrush(WpfColor.FromRgb(31,143,123)),Foreground=WpfBrushes.White};cancel.Click+=(_,_)=>dialog.DialogResult=false;ok.Click+=(_,_)=>{if(list.SelectedItem!=null)dialog.DialogResult=true;};buttons.Children.Add(cancel);buttons.Children.Add(ok);Grid.SetRow(buttons,1);grid.Children.Add(buttons);dialog.Content=grid;return dialog.ShowDialog()==true?list.SelectedItem as Profile:null;
    }
    string? SelectRunningApplication()
    {
        var apps=System.Diagnostics.Process.GetProcesses().Where(p=>p.MainWindowHandle!=IntPtr.Zero&&!string.IsNullOrWhiteSpace(p.MainWindowTitle)).Select(p=>new{Label=$"{p.MainWindowTitle}  —  {p.ProcessName}.exe",Value=p.ProcessName+".exe"}).GroupBy(x=>x.Value,StringComparer.OrdinalIgnoreCase).Select(x=>x.First()).OrderBy(x=>x.Label).ToList();var dialog=new Window{Title="自動切替する起動中のアプリを選択",Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Width=620,Height=480,Background=new SolidColorBrush(WpfColor.FromRgb(23,29,39)),Foreground=WpfBrushes.White,ShowInTaskbar=false};var grid=new Grid{Margin=new Thickness(18)};grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});var list=new ListBox{ItemsSource=apps,DisplayMemberPath="Label",Background=new SolidColorBrush(WpfColor.FromRgb(29,35,48)),Foreground=WpfBrushes.White};grid.Children.Add(list);var ok=new System.Windows.Controls.Button{Content="このアプリを使用",Padding=new Thickness(18,8,18,8),Margin=new Thickness(3),HorizontalAlignment=System.Windows.HorizontalAlignment.Right,Background=new SolidColorBrush(WpfColor.FromRgb(31,143,123)),Foreground=WpfBrushes.White};ok.Click+=(_,_)=>{if(list.SelectedItem!=null)dialog.DialogResult=true;};Grid.SetRow(ok,1);grid.Children.Add(ok);dialog.Content=grid;return dialog.ShowDialog()==true?(string?)list.SelectedItem?.GetType().GetProperty("Value")?.GetValue(list.SelectedItem):null;
    }
    [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr handle);
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr handle,int attribute,ref int value,int valueSize);
    sealed record ActionOption(ActionKind Kind,string Label);
}
