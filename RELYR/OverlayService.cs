using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Button=System.Windows.Controls.Button;
using MouseEventArgs=System.Windows.Input.MouseEventArgs;
using Panel=System.Windows.Controls.Panel;
using Point=System.Windows.Point;
using WpfApplication=System.Windows.Application;
using WpfBrushes=System.Windows.Media.Brushes;
using WpfColor=System.Windows.Media.Color;
using WpfCursors=System.Windows.Input.Cursors;
using WpfImage=System.Windows.Controls.Image;

namespace RELYR;

/// <summary>割り当てから呼び出せる画面オーバーレイを一元管理します。</summary>
internal static class OverlayService
{
    internal const string NumpadAction="ShowNumpadOverlay";
    internal const string ExtendedKeypadAction="ShowExtendedKeypadOverlay";
    internal const string DeckPanelAction="ShowDeckPanelOverlay";
    internal const string BlankAction="ShowBlankOverlay";
    internal const string ClockAction="ShowClockOverlay";

    static InputPanelOverlayWindow? inputPanel;
    static DeckPanelOverlayWindow? deckPanel;
    static readonly List<ScreenOverlayWindow> screenOverlays=[];
    static Func<AppConfig>? configProvider;
    static Func<bool>? physicalInputDownProvider;
    static Action<Mapping>? deckActionRequested;
    static Action<double,double>? deckPositionChanged;
    static Action<bool,double,double>? inputPanelPositionChanged;
    static int fullScreenActive;
    static int fullScreenClosing;
    static int fullScreenDismissArmed;
    static System.Drawing.Point fullScreenStartCursor;
#if !PRODUCTION_PUBLISH
    internal static Action<string>? ActionRequestedForTest;
#endif
    internal static bool FullScreenVisible=>Volatile.Read(ref fullScreenActive)!=0;

    internal static void Configure(Func<AppConfig>? provider,Func<bool>? inputDownProvider=null,Action<Mapping>? deckAction=null,Action<double,double>? positionChanged=null,Action<bool,double,double>? inputPositionChanged=null)
    {
        configProvider=provider;
        physicalInputDownProvider=inputDownProvider;
        deckActionRequested=deckAction;
        deckPositionChanged=positionChanged;
        inputPanelPositionChanged=inputPositionChanged;
    }
    internal static void Shutdown()
    {
        if(WpfApplication.Current?.Dispatcher.CheckAccess()==true)
        {
            inputPanel?.Close();inputPanel=null;deckPanel?.Close();deckPanel=null;CloseScreenOverlays();configProvider=null;physicalInputDownProvider=null;deckActionRequested=null;deckPositionChanged=null;inputPanelPositionChanged=null;
        }
        else if(WpfApplication.Current is { } app)_=app.Dispatcher.BeginInvoke(Shutdown);
    }

    internal static bool IsOverlayAction(string? value)=>value is NumpadAction or ExtendedKeypadAction or BlankAction or ClockAction||DeckPanelLayout.IsDeckAction(value);

    internal static bool TryShow(string? value)
    {
        if(!IsOverlayAction(value))return false;
        string action=value!;
#if !PRODUCTION_PUBLISH
        if(ActionRequestedForTest is { } test){test(action);return true;}
#endif
        var dispatcher=WpfApplication.Current?.Dispatcher;
        if(dispatcher==null)return false;
        _=dispatcher.BeginInvoke(()=>ShowOnUiThread(action));
        return true;
    }

    static void ShowOnUiThread(string action)
    {
        if(DeckPanelLayout.IsDeckAction(action))
        {
            inputPanel?.Close();inputPanel=null;
            AppConfig deckConfig=configProvider?.Invoke()??new AppConfig();
            var layout=DeckPanelLayout.ResolveActionLayout(deckConfig,action);
            if(deckPanel is {IsVisible:true} existing)
            {
                bool same=layout?.Id.Equals(existing.LayoutId,StringComparison.OrdinalIgnoreCase)==true;
                existing.Close();deckPanel=null;
                if(same)return;
            }
            if(layout==null)return;
            deckPanel=new DeckPanelOverlayWindow(deckConfig,deckActionRequested,deckConfig.InputPanelOpacityPercent,deckPositionChanged,layout);
            deckPanel.Closed+=(_,_)=>deckPanel=null;
            deckPanel.Show();
            return;
        }
        if(action is NumpadAction or ExtendedKeypadAction)
        {
            deckPanel?.Close();deckPanel=null;
            bool extended=action==ExtendedKeypadAction;
            if(inputPanel is {IsVisible:true} existing&&existing.IsExtended==extended){existing.Close();inputPanel=null;return;}
            inputPanel?.Close();
            AppConfig? panelConfig=configProvider?.Invoke();
            int opacity=panelConfig?.InputPanelOpacityPercent??96;
            bool useUsLayout=panelConfig?.KeyboardLayout=="US";
            inputPanel=new InputPanelOverlayWindow(extended,opacity,useUsLayout,panelConfig,inputPanelPositionChanged);
            inputPanel.Closed+=(_,_)=>inputPanel=null;
            inputPanel.Show();
            return;
        }

        inputPanel?.Close();
        inputPanel=null;
        deckPanel?.Close();
        deckPanel=null;
        if(FullScreenVisible){CloseScreenOverlays();return;}
        AppConfig config=configProvider?.Invoke()??new AppConfig();
        bool clockAction=action==ClockAction;
        foreach(var screen in System.Windows.Forms.Screen.AllScreens)
        {
            bool showClock=clockAction&&(config.ClockShowOnAllMonitors||screen.Primary);
            var overlay=new ScreenOverlayWindow(screen,showClock,config,clockAction);
            screenOverlays.Add(overlay);
        }
        fullScreenStartCursor=System.Windows.Forms.Cursor.Position;
        Interlocked.Exchange(ref fullScreenActive,1);
        Interlocked.Exchange(ref fullScreenClosing,0);
        // 物理キーから起動した場合は、その起動キーのUpを受け取るまで解除を
        // 許可しない。マクロ等からの起動は、最初の新しい入力ですぐ解除できる。
        Interlocked.Exchange(ref fullScreenDismissArmed,physicalInputDownProvider?.Invoke()==true?0:1);
        foreach(var overlay in screenOverlays)overlay.Show();
    }

    /// <summary>起動に使ったキーを離すまでは待ち、次の新しいキー操作で全画面表示を閉じます。</summary>
    internal static bool TryDismissFullScreenKeyboard(bool down)
    {
        if(!FullScreenVisible)return false;
        bool armed=Volatile.Read(ref fullScreenDismissArmed)!=0;
        if(!armed&&!down)
        {
            ArmFullScreenDismiss();
            return true;
        }
        if(ShouldDismissFullScreenKeyboard(armed,down))
            RequestCloseScreenOverlays();
        return true;
    }

    /// <summary>起動操作を離した後のマウス移動・押下・ホイールで全画面表示を閉じます。</summary>
    internal static bool TryDismissFullScreenMouse(int message,int x,int y)
    {
        if(!FullScreenVisible)return false;
        bool armed=Volatile.Read(ref fullScreenDismissArmed)!=0;
        if(!armed&&message is 0x202 or 0x205 or 0x208 or 0x20C)
        {
            ArmFullScreenDismiss();
            return true;
        }
        bool moved=message==0x200&&Math.Abs(x-fullScreenStartCursor.X)+Math.Abs(y-fullScreenStartCursor.Y)>=3;
        if(ShouldDismissFullScreenMouse(armed,message,moved))
            RequestCloseScreenOverlays();
        return true;
    }

    internal static bool ShouldDismissFullScreenKeyboard(bool armed,bool down)=>armed&&down;
    internal static bool ShouldDismissFullScreenMouse(bool armed,int message,bool moved)
        =>armed&&(moved||message is 0x201 or 0x204 or 0x207 or 0x20B or 0x20A or 0x20E);
    internal static void ArmFullScreenDismissForTest()=>ArmFullScreenDismiss();
    static void ArmFullScreenDismiss()
    {
        fullScreenStartCursor=System.Windows.Forms.Cursor.Position;
        Interlocked.Exchange(ref fullScreenDismissArmed,1);
    }

    static void RequestCloseScreenOverlays()
    {
        if(Interlocked.Exchange(ref fullScreenClosing,1)!=0)return;
        _=WpfApplication.Current.Dispatcher.BeginInvoke(CloseScreenOverlays);
    }

    static void CloseScreenOverlays()
    {
        foreach(var window in screenOverlays.ToArray())window.Close();
        screenOverlays.Clear();
        Interlocked.Exchange(ref fullScreenActive,0);
        Interlocked.Exchange(ref fullScreenClosing,0);
        Interlocked.Exchange(ref fullScreenDismissArmed,0);
    }

}

/// <summary>フォーカスを奪わず、クリックしたキーを直前の入力先へ送る半透明パネルです。</summary>
internal sealed class InputPanelOverlayWindow:Window
{
    const double KeyHeight=52;
    const double KeyGap=4;
    const int GwlExStyle=-20;
    const long WsExToolWindow=0x00000080L;
    const long WsExNoActivate=0x08000000L;
    const int WmMouseActivate=0x0021;
    const int MaNoActivate=3;

    readonly Border dragArea;
    readonly Action<double,double>? savePosition;
    bool dragging;
    bool positionDirty;
    Point dragStart;
    double windowStartLeft,windowStartTop;
    DpiScale dragDpi;
    readonly double keyWidth;
    double KeyCellWidth=>keyWidth+KeyGap;
    double KeyCellHeight=>KeyHeight+KeyGap;

    internal bool IsExtended{get;}
    internal IReadOnlyList<Button> InputButtons=>inputButtons;
    internal Button CloseButton{get;private set;}=null!;
    internal double PanelOpacity=>panelCard.Opacity;
    readonly List<Button> inputButtons=[];
    readonly Border panelCard;

    internal InputPanelOverlayWindow(bool extended,int opacityPercent=96,bool useUsLayout=false,AppConfig? config=null,Action<bool,double,double>? positionChanged=null)
    {
        IsExtended=extended;
        savePosition=positionChanged==null?null:(left,top)=>positionChanged(extended,left,top);
        keyWidth=useUsLayout?56:54;
        Title=extended?"ナビゲーション・テンキー":"テンキー";
        Width=extended?KeyCellWidth*7+16+24:KeyCellWidth*4+24;
        Height=extended?440:400;
        MinWidth=Width;
        MinHeight=Height;
        MaxWidth=Width;
        MaxHeight=Height;
        WindowStyle=WindowStyle.None;
        ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true;
        Background=WpfBrushes.Transparent;
        ShowInTaskbar=false;
        ShowActivated=false;
        Topmost=true;
        SizeToContent=SizeToContent.Manual;
        WindowStartupLocation=WindowStartupLocation.Manual;
        Point initial=InitialPosition(config,extended,Width,Height);
        Left=initial.X;
        Top=initial.Y;

        panelCard=new Border
        {
            CornerRadius=new CornerRadius(14),
            BorderThickness=new Thickness(1),
            Padding=new Thickness(12),
            Opacity=Math.Clamp(opacityPercent,40,100)/100d,
            Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=24,ShadowDepth=5,Opacity=.4,Color=Colors.Black}
        };
        panelCard.SetResourceReference(Border.BackgroundProperty,"CardBackground");
        panelCard.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        Content=panelCard;

        var root=new Grid();
        root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(48)});
        root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        panelCard.Child=root;

        dragArea=BuildHeader();
        root.Children.Add(dragArea);
        var body=extended?BuildExtendedBody():BuildNumpad();
        Grid.SetRow(body,1);
        root.Children.Add(body);

        SourceInitialized+=WindowSourceInitialized;
        Closed+=(_,_)=>{dragging=false;PersistPosition();};
    }

    Border BuildHeader()
    {
        var border=new Border{CornerRadius=new CornerRadius(8),Padding=new Thickness(8,4,8,4),Cursor=WpfCursors.SizeAll};
        border.SetResourceReference(Border.BackgroundProperty,"SurfaceBackground");
        var grid=new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var grip=new TextBlock{Text="⠿",FontSize=22,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,9,0)};
        grip.SetResourceReference(TextBlock.ForegroundProperty,"AccentBrush");
        var title=new TextBlock{Text=Title,FontSize=16,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};
        title.SetResourceReference(TextBlock.ForegroundProperty,"PrimaryText");
        var close=new Button
        {
            Width=34,Height=34,MinWidth=34,MaxWidth=34,MinHeight=34,MaxHeight=34,
            Margin=new Thickness(0),Padding=new Thickness(0),Focusable=false,
            HorizontalContentAlignment=System.Windows.HorizontalAlignment.Center,
            VerticalContentAlignment=System.Windows.VerticalAlignment.Center,
            Content=new System.Windows.Shapes.Path
            {
                Data=Geometry.Parse("M 1,1 L 11,11 M 11,1 L 1,11"),
                Width=12,Height=12,Stretch=Stretch.Uniform,
                HorizontalAlignment=System.Windows.HorizontalAlignment.Center,
                VerticalAlignment=System.Windows.VerticalAlignment.Center,
                StrokeThickness=1.6,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round
            }
        };
        ((System.Windows.Shapes.Path)close.Content).SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"PrimaryText");
        if(WpfApplication.Current?.Resources["AppButtonStyle"] is Style closeStyle)close.Style=closeStyle;
        close.Click+=(_,_)=>Close();
        CloseButton=close;
        grid.Children.Add(grip);
        Grid.SetColumn(title,1);grid.Children.Add(title);
        Grid.SetColumn(close,2);grid.Children.Add(close);
        border.Child=grid;
        border.PreviewMouseLeftButtonDown+=DragStarted;
        border.PreviewMouseMove+=DragMoved;
        border.PreviewMouseLeftButtonUp+=DragEnded;
        return border;
    }

    FrameworkElement BuildExtendedBody()
    {
        var grid=new Grid{Margin=new Thickness(0,12,0,0)};
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(KeyCellWidth*3)});
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(16)});
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(KeyCellWidth*4)});

        var left=new StackPanel();
        left.Children.Add(SectionLabel("ナビゲーション"));
        left.Children.Add(BuildNavigation());
        left.Children.Add(SectionLabel("カーソルキー",new Thickness(0,14,0,6)));
        left.Children.Add(BuildCursorKeys());
        grid.Children.Add(left);

        var numpadHost=new StackPanel();
        numpadHost.Children.Add(SectionLabel("テンキー"));
        numpadHost.Children.Add(BuildNumpad());
        Grid.SetColumn(numpadHost,2);
        grid.Children.Add(numpadHost);
        return grid;
    }

    FrameworkElement BuildNavigation()
    {
        var grid=CreateUniformGrid(3,3);
        grid.Width=KeyCellWidth*3;grid.Height=KeyCellHeight*3;
        AddUniformKey(grid,"Insert","Insert");
        AddUniformKey(grid,"Home","Home");
        AddUniformKey(grid,"Page\nUp","PageUp");
        AddUniformKey(grid,"Delete","Delete");
        AddUniformKey(grid,"End","End");
        AddUniformKey(grid,"Page\nDown","PageDown");
        AddUniformKey(grid,"Print","PrintScreen");
        AddUniformKey(grid,"Scroll","ScrollLock");
        AddUniformKey(grid,"Pause","Pause");
        return grid;
    }

    FrameworkElement BuildCursorKeys()
    {
        var grid=CreateUniformGrid(3,2);
        grid.Width=KeyCellWidth*3;grid.Height=KeyCellHeight*2;
        grid.Children.Add(new Border());
        AddUniformKey(grid,"↑","Up");
        grid.Children.Add(new Border());
        AddUniformKey(grid,"←","Left");
        AddUniformKey(grid,"↓","Down");
        AddUniformKey(grid,"→","Right");
        return grid;
    }

    FrameworkElement BuildNumpad()
    {
        var outer=new Grid
        {
            Width=KeyCellWidth*4,Height=KeyCellHeight*6,
            HorizontalAlignment=System.Windows.HorizontalAlignment.Center,
            Margin=IsExtended?new Thickness(0):new Thickness(0,12,0,0)
        };
        for(int row=0;row<6;row++)outer.RowDefinitions.Add(new RowDefinition{Height=new GridLength(KeyCellHeight)});
        for(int col=0;col<4;col++)outer.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(KeyCellWidth)});

        AddGridKey(outer,"⌫  Backspace","Back",0,0,1,4);
        AddGridKey(outer,"Num","NumLock",1,0);AddGridKey(outer,"÷","Divide",1,1);AddGridKey(outer,"×","Multiply",1,2);AddGridKey(outer,"−","Subtract",1,3);
        AddGridKey(outer,"7","NumPad7",2,0);AddGridKey(outer,"8","NumPad8",2,1);AddGridKey(outer,"9","NumPad9",2,2);AddGridKey(outer,"＋","Add",2,3,2,1);
        AddGridKey(outer,"4","NumPad4",3,0);AddGridKey(outer,"5","NumPad5",3,1);AddGridKey(outer,"6","NumPad6",3,2);
        AddGridKey(outer,"1","NumPad1",4,0);AddGridKey(outer,"2","NumPad2",4,1);AddGridKey(outer,"3","NumPad3",4,2);AddGridKey(outer,"Enter","NumPadEnter",4,3,2,1);
        AddGridKey(outer,"0","NumPad0",5,0,1,2);AddGridKey(outer,".","Decimal",5,2);
        return outer;
    }

    static TextBlock SectionLabel(string text,Thickness? margin=null)
    {
        var label=new TextBlock{Text=text,FontWeight=FontWeights.SemiBold,FontSize=14,Margin=margin??new Thickness(0,0,0,6)};
        label.SetResourceReference(TextBlock.ForegroundProperty,"SecondaryText");
        return label;
    }

    static UniformGrid CreateUniformGrid(int columns,int rows)=>new(){Columns=columns,Rows=rows};

    void AddUniformKey(Panel panel,string label,string action)=>panel.Children.Add(CreateInputButton(label,action));

    void AddGridKey(Grid grid,string label,string action,int row,int column,int rowSpan=1,int columnSpan=1)
    {
        var button=CreateInputButton(label,action);
        Grid.SetRow(button,row);Grid.SetColumn(button,column);Grid.SetRowSpan(button,rowSpan);Grid.SetColumnSpan(button,columnSpan);
        grid.Children.Add(button);
    }

    Button CreateInputButton(string label,string action)
    {
        var button=CreateKeyButton(label,action,double.NaN,double.NaN);
        button.Click+=InputButtonClicked;
        inputButtons.Add(button);
        return button;
    }

    static Button CreateKeyButton(string label,string action,double width,double height)
    {
        var button=new Button
        {
            Content=KeyLabel(label),Tag=action,Margin=new Thickness(KeyGap/2),Padding=new Thickness(4,5,4,5),
            FontSize=14,FontWeight=FontWeights.Medium,
            HorizontalContentAlignment=System.Windows.HorizontalAlignment.Center,VerticalContentAlignment=System.Windows.VerticalAlignment.Center
        };
        if(!double.IsNaN(width))button.Width=width;
        if(!double.IsNaN(height))button.Height=height;
        if(WpfApplication.Current?.Resources["AppButtonStyle"] is Style style)button.Style=style;
        return button;
    }
    static object KeyLabel(string label)=>label.Contains('\n')
        ?new TextBlock{Text=label,TextAlignment=TextAlignment.Center,HorizontalAlignment=System.Windows.HorizontalAlignment.Center,VerticalAlignment=System.Windows.VerticalAlignment.Center}
        :label;

    void InputButtonClicked(object sender,RoutedEventArgs e)
    {
        if(sender is not Button{Tag:string action}||action.Length==0)return;
        try{InputEngine.SendShortcut(action);}
        catch(Exception ex){System.Diagnostics.Debug.WriteLine($"Overlay key '{action}' failed: {ex.Message}");}
    }

    void DragStarted(object sender,MouseButtonEventArgs e)
    {
        if(IsInsideButton(e.OriginalSource as DependencyObject))return;
        dragging=true;dragStart=PointToScreen(e.GetPosition(this));dragDpi=VisualTreeHelper.GetDpi(this);windowStartLeft=Left;windowStartTop=Top;
        dragArea.CaptureMouse();e.Handled=true;
    }

    static bool IsInsideButton(DependencyObject? source)
    {
        for(var current=source;current!=null;)
        {
            if(current is System.Windows.Controls.Primitives.ButtonBase)return true;
            current=current is Visual?VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    void DragMoved(object sender,MouseEventArgs e)
    {
        if(!dragging||e.LeftButton!=MouseButtonState.Pressed)return;
        Point current=PointToScreen(e.GetPosition(this));
        Point delta=PhysicalDragDeltaToDip(current-dragStart,dragDpi);
        Left=windowStartLeft+delta.X;Top=windowStartTop+delta.Y;positionDirty=true;
        e.Handled=true;
    }

    internal static Point PhysicalDragDeltaToDip(Vector physicalDelta,DpiScale dpi)=>
        new(physicalDelta.X/Math.Max(.01,dpi.DpiScaleX),physicalDelta.Y/Math.Max(.01,dpi.DpiScaleY));

    void DragEnded(object sender,MouseButtonEventArgs e)
    {
        if(!dragging)return;
        dragging=false;dragArea.ReleaseMouseCapture();PersistPosition();e.Handled=true;
    }

    void PersistPosition(){if(!positionDirty)return;positionDirty=false;savePosition?.Invoke(Left,Top);}
    internal void MoveAndPersistForTest(double left,double top){Left=left;Top=top;positionDirty=false;savePosition?.Invoke(left,top);}
    internal static Point InitialPosition(AppConfig? config,bool extended,double width,double height)
    {
        double defaultLeft=Math.Max(SystemParameters.WorkArea.Left,SystemParameters.WorkArea.Right-width-24);
        double defaultTop=Math.Max(SystemParameters.WorkArea.Top,SystemParameters.WorkArea.Bottom-height-24);
        if(config==null)return new Point(defaultLeft,defaultTop);
        double? configuredLeft=extended?config.ExtendedKeypadPanelLeft:config.NumpadPanelLeft;
        double? configuredTop=extended?config.ExtendedKeypadPanelTop:config.NumpadPanelTop;
        if(configuredLeft is not double savedLeft||configuredTop is not double savedTop||!double.IsFinite(savedLeft)||!double.IsFinite(savedTop))return new Point(defaultLeft,defaultTop);
        const double visibleEdge=48;
        double minLeft=SystemParameters.VirtualScreenLeft-width+visibleEdge,maxLeft=SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-visibleEdge;
        double minTop=SystemParameters.VirtualScreenTop-height+visibleEdge,maxTop=SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-visibleEdge;
        return new Point(Math.Clamp(savedLeft,minLeft,maxLeft),Math.Clamp(savedTop,minTop,maxTop));
    }

    void WindowSourceInitialized(object? sender,EventArgs e)
    {
        var helper=new WindowInteropHelper(this);
        long style=GetWindowLongPtr(helper.Handle,GwlExStyle).ToInt64();
        SetWindowLongPtr(helper.Handle,GwlExStyle,new IntPtr(style|WsExToolWindow|WsExNoActivate));
        HwndSource.FromHwnd(helper.Handle)?.AddHook(WindowMessageHook);
    }

    static IntPtr WindowMessageHook(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(msg==WmMouseActivate){handled=true;return new IntPtr(MaNoActivate);}
        return IntPtr.Zero;
    }

    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr64(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong32(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern IntPtr SetWindowLongPtr64(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")]static extern int SetWindowLong32(IntPtr hwnd,int index,int value);
    static IntPtr GetWindowLongPtr(IntPtr hwnd,int index)=>IntPtr.Size==8?GetWindowLongPtr64(hwnd,index):new IntPtr(GetWindowLong32(hwnd,index));
    static IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value)=>IntPtr.Size==8?SetWindowLongPtr64(hwnd,index,value):new IntPtr(SetWindowLong32(hwnd,index,value.ToInt32()));
}

internal sealed class DeckPanelOverlayWindow:Window
{
    const int GwlExStyle=-20;
    const long WsExToolWindow=0x00000080L;
    const long WsExNoActivate=0x08000000L;
    const int WmMouseActivate=0x0021;
    const int MaNoActivate=3;

    readonly Border dragArea;
    readonly Action<Mapping>? execute;
    readonly Action<double,double>? savePosition;
    readonly bool hoverPreviewsEnabled;
    bool dragging;
    bool positionDirty;
    Button? fileDragButton;
    Point fileDragStart;
    MediaPlayer? hoverAudioPlayer;
    Point dragStart;
    double windowStartLeft,windowStartTop;
    DpiScale dragDpi;

    internal IReadOnlyList<Button> DeckButtons=>deckButtons;
    internal Button CloseButton{get;private set;}=null!;
    internal double PanelOpacity=>panelCard.Opacity;
    internal string LayoutId=>layout.Id;
    internal bool UsesNoActivateStyle{get;private set;}
    readonly List<Button> deckButtons=[];
    readonly Border panelCard;
    readonly DeckLayoutDefinition layout;

    internal DeckPanelOverlayWindow(AppConfig config,Action<Mapping>? executeAction,int opacityPercent=96,Action<double,double>? positionChanged=null,DeckLayoutDefinition? selectedLayout=null)
    {
        execute=executeAction;
        savePosition=positionChanged;
        hoverPreviewsEnabled=config.DeckHoverPreviewsEnabled;
        layout=selectedLayout??DeckPanelLayout.DefaultLayout(config)??new DeckLayoutDefinition();
        Title="RELYR Deck - "+layout.Name;
        double naturalGridWidth=layout.Columns*(DeckPanelLayout.KeyWidth+DeckPanelLayout.Gap);
        double naturalGridHeight=layout.Rows*DeckPanelLayout.CellHeight;
        double scale=Math.Min(1,Math.Min((SystemParameters.WorkArea.Width-48)/Math.Max(1,naturalGridWidth),(SystemParameters.WorkArea.Height-76)/Math.Max(1,naturalGridHeight)));
        Width=naturalGridWidth*scale+24;
        Height=naturalGridHeight*scale+52;
        MinWidth=Width;MaxWidth=Width;MinHeight=Height;MaxHeight=Height;
        WindowStyle=WindowStyle.None;
        ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true;
        Background=WpfBrushes.Transparent;
        ShowInTaskbar=false;
        ShowActivated=false;
        Topmost=true;
        WindowStartupLocation=WindowStartupLocation.Manual;
        Point initial=InitialPosition(config,Width,Height);Left=initial.X;Top=initial.Y;

        panelCard=new Border
        {
            CornerRadius=new CornerRadius(14),
            BorderThickness=new Thickness(1),
            Padding=new Thickness(12,8,12,8),
            Opacity=Math.Clamp(opacityPercent,40,100)/100d,
            Effect=new DropShadowEffect{BlurRadius=24,ShadowDepth=5,Opacity=.4,Color=Colors.Black}
        };
        panelCard.SetResourceReference(Border.BackgroundProperty,"CardBackground");
        panelCard.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        Content=panelCard;

        var root=new Grid();
        root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(30)});
        root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        panelCard.Child=root;
        dragArea=BuildHeader();
        root.Children.Add(dragArea);

        var deckGrid=new UniformGrid
        {
            Rows=layout.Rows,
            Columns=layout.Columns,
            Width=naturalGridWidth,
            Height=naturalGridHeight
        };
        for(int slot=1;slot<=DeckPanelLayout.VisibleSlotCount(layout);slot++)
        {
            var mapping=DeckPanelLayout.FindMapping(layout,slot);
            var button=new Button
            {
                Tag=mapping,
                Content=DeckPanelLayout.CreateButtonContent(DeckPanelLayout.InputName(slot),mapping),
                Width=DeckPanelLayout.KeyWidth,
                Height=DeckPanelLayout.KeyHeight,
                MinWidth=0,
                MinHeight=0,
                Margin=new Thickness(DeckPanelLayout.Gap/2,0,DeckPanelLayout.Gap/2,0),
                Padding=new Thickness(3),
                Focusable=false,
                IsEnabled=(MainWindow.MappingInterceptsInput(mapping)&&mapping!.Kind!=ActionKind.Gesture)||DeckPanelLayout.HasRegisteredFile(mapping),
                HorizontalContentAlignment=System.Windows.HorizontalAlignment.Stretch,
                VerticalContentAlignment=System.Windows.VerticalAlignment.Center
            };
            if(WpfApplication.Current?.Resources["AppButtonStyle"] is Style style)button.Style=style;
            if(DeckPanelLayout.TryGetButtonColor(mapping,out var customColor))
            {
                button.Background=new SolidColorBrush(customColor);
                button.Foreground=new SolidColorBrush(DeckPanelLayout.TextColorFor(customColor));
            }
            else if(MainWindow.MappingInterceptsInput(mapping))
            {
                button.Background=new SolidColorBrush(MainWindow.AssignmentColorFor(mapping!));
                button.Foreground=WpfBrushes.White;
            }
            button.Click+=DeckButtonClicked;
            if(DeckPanelLayout.HasRegisteredFile(mapping))
            {
                button.PreviewMouseLeftButtonDown+=DeckFileDragStarted;
                button.PreviewMouseMove+=DeckFileDragMoved;
                button.PreviewMouseLeftButtonUp+=DeckFileDragEnded;
            }
            if(hoverPreviewsEnabled)ConfigureHoverPreview(button,mapping);
            deckButtons.Add(button);
            var cell=new StackPanel{Width=DeckPanelLayout.KeyWidth+DeckPanelLayout.Gap,Height=DeckPanelLayout.CellHeight};
            cell.Children.Add(button);
            cell.Children.Add(DeckPanelLayout.CreateNameLabel(mapping));
            deckGrid.Children.Add(cell);
        }
        var deckView=new Viewbox{Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=System.Windows.HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,6,0,0),Child=deckGrid};
        Grid.SetRow(deckView,1);
        root.Children.Add(deckView);

        SourceInitialized+=WindowSourceInitialized;
        Closed+=(_,_)=>{dragging=false;fileDragButton=null;StopHoverAudio();PersistPosition();};
    }

    Border BuildHeader()
    {
        var border=new Border{CornerRadius=new CornerRadius(6),Padding=new Thickness(6,2,4,2),Cursor=WpfCursors.SizeAll};
        border.SetResourceReference(Border.BackgroundProperty,"SurfaceBackground");
        var grid=new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var grip=new TextBlock{Text="⋮⋮",FontSize=14,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,7,0)};
        grip.SetResourceReference(TextBlock.ForegroundProperty,"AccentBrush");
        var title=new TextBlock{Text=layout.Name,FontSize=14,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
        title.SetResourceReference(TextBlock.ForegroundProperty,"PrimaryText");
        var close=new Button
        {
            Width=26,Height=26,MinWidth=26,MaxWidth=26,MinHeight=26,MaxHeight=26,
            Margin=new Thickness(0),Padding=new Thickness(0),Focusable=false,
            HorizontalContentAlignment=System.Windows.HorizontalAlignment.Center,
            VerticalContentAlignment=System.Windows.VerticalAlignment.Center,
            Content=new System.Windows.Shapes.Path
            {
                Data=Geometry.Parse("M 1,1 L 9,9 M 9,1 L 1,9"),
                Width=10,Height=10,Stretch=Stretch.Uniform,
                StrokeThickness=1.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round
            }
        };
        ((System.Windows.Shapes.Path)close.Content).SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"PrimaryText");
        if(WpfApplication.Current?.Resources["AppButtonStyle"] is Style closeStyle)close.Style=closeStyle;
        close.Click+=(_,_)=>Close();
        CloseButton=close;
        grid.Children.Add(grip);
        Grid.SetColumn(title,1);grid.Children.Add(title);
        Grid.SetColumn(close,2);grid.Children.Add(close);
        border.Child=grid;
        border.PreviewMouseLeftButtonDown+=DragStarted;
        border.PreviewMouseMove+=DragMoved;
        border.PreviewMouseLeftButtonUp+=DragEnded;
        return border;
    }

    void DeckButtonClicked(object sender,RoutedEventArgs e)
    {
        if(sender is not Button{Tag:Mapping mapping}||mapping.Kind==ActionKind.Gesture)return;
        if(MainWindow.MappingInterceptsInput(mapping))execute?.Invoke(mapping);
        else if(DeckPanelLayout.IsAudioFile(mapping.DeckFilePath))PlayHoverAudio(mapping.DeckFilePath);
    }
    void ConfigureHoverPreview(Button button,Mapping? mapping)
    {
        if(mapping==null)return;
        object? content=CreateHoverContent(mapping);
        if(content!=null)
        {
            button.ToolTip=content;
            ToolTipService.SetInitialShowDelay(button,220);
            ToolTipService.SetShowDuration(button,20000);
        }
        if(DeckPanelLayout.IsAudioFile(mapping.DeckFilePath))button.MouseEnter+=(_,_)=>PlayHoverAudio(mapping.DeckFilePath);
    }
    object? CreateHoverContent(Mapping mapping)
    {
        if(DeckPanelLayout.IsImageFile(mapping.DeckFilePath))
        {
            var image=DeckPanelLayout.LoadImageThumbnail(mapping.DeckFilePath,360);
            if(image!=null)
            {
                var preview=new WpfImage{Source=image,MaxWidth=240,MaxHeight=180,Stretch=Stretch.Uniform};
                var border=new Border{Padding=new Thickness(6),CornerRadius=new CornerRadius(5),Child=preview};
                border.SetResourceReference(Border.BackgroundProperty,"CardBackground");
                border.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");border.BorderThickness=new Thickness(1);
                return border;
            }
        }
        if(DeckPanelLayout.HasRegisteredFile(mapping))return DeckPanelLayout.FileDisplayName(mapping)+(File.Exists(mapping.DeckFilePath)?"":"\nファイルが見つかりません");
        return MainWindow.AssignmentToolTipText(mapping);
    }
    void PlayHoverAudio(string path)
    {
        if(!hoverPreviewsEnabled||!DeckPanelLayout.IsAudioFile(path)||!File.Exists(path))return;
        try
        {
            StopHoverAudio();
            var player=new MediaPlayer();hoverAudioPlayer=player;
            player.MediaEnded+=(_,_)=>{if(ReferenceEquals(hoverAudioPlayer,player))StopHoverAudio();};
            player.MediaFailed+=(_,_)=>{if(ReferenceEquals(hoverAudioPlayer,player))StopHoverAudio();};
            player.Open(new Uri(path,UriKind.Absolute));player.Volume=.8;player.Play();
        }
        catch{StopHoverAudio();}
    }
    void StopHoverAudio()
    {
        var player=hoverAudioPlayer;hoverAudioPlayer=null;
        if(player==null)return;
        try{player.Stop();player.Close();}catch{}
    }
    void DeckFileDragStarted(object sender,MouseButtonEventArgs e)
    {
        if(sender is not Button{Tag:Mapping mapping}||!DeckPanelLayout.IsAvailableFile(mapping))return;
        fileDragButton=(Button)sender;fileDragStart=e.GetPosition(fileDragButton);
    }
    void DeckFileDragMoved(object sender,MouseEventArgs e)
    {
        if(sender is not Button button||!ReferenceEquals(fileDragButton,button)||e.LeftButton!=MouseButtonState.Pressed||button.Tag is not Mapping mapping||!DeckPanelLayout.IsAvailableFile(mapping))return;
        Point current=e.GetPosition(button);
        if(Math.Abs(current.X-fileDragStart.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(current.Y-fileDragStart.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        fileDragButton=null;
        try
        {
            var data=new System.Windows.DataObject();data.SetData(System.Windows.DataFormats.FileDrop,new[]{mapping.DeckFilePath});
            System.Windows.DragDrop.DoDragDrop(button,data,System.Windows.DragDropEffects.Copy);
        }
        catch(COMException){}
        e.Handled=true;
    }
    void DeckFileDragEnded(object sender,MouseButtonEventArgs e)=>fileDragButton=null;

    void DragStarted(object sender,MouseButtonEventArgs e)
    {
        if(IsInsideButton(e.OriginalSource as DependencyObject))return;
        dragging=true;dragStart=PointToScreen(e.GetPosition(this));dragDpi=VisualTreeHelper.GetDpi(this);windowStartLeft=Left;windowStartTop=Top;
        dragArea.CaptureMouse();e.Handled=true;
    }
    static bool IsInsideButton(DependencyObject? source)
    {
        for(var current=source;current!=null;)
        {
            if(current is System.Windows.Controls.Primitives.ButtonBase)return true;
            current=current is Visual?VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current);
        }
        return false;
    }
    void DragMoved(object sender,MouseEventArgs e)
    {
        if(!dragging||e.LeftButton!=MouseButtonState.Pressed)return;
        Point current=PointToScreen(e.GetPosition(this));
        Point delta=InputPanelOverlayWindow.PhysicalDragDeltaToDip(current-dragStart,dragDpi);
        Left=windowStartLeft+delta.X;Top=windowStartTop+delta.Y;positionDirty=true;e.Handled=true;
    }
    void DragEnded(object sender,MouseButtonEventArgs e)
    {
        if(!dragging)return;dragging=false;dragArea.ReleaseMouseCapture();PersistPosition();e.Handled=true;
    }
    void PersistPosition(){if(!positionDirty)return;positionDirty=false;savePosition?.Invoke(Left,Top);}
    internal void MoveAndPersistForTest(double left,double top){Left=left;Top=top;positionDirty=false;savePosition?.Invoke(left,top);}
    internal static Point InitialPosition(AppConfig config,double width,double height)
    {
        double defaultLeft=Math.Max(SystemParameters.WorkArea.Left,SystemParameters.WorkArea.Right-width-24);
        double defaultTop=Math.Max(SystemParameters.WorkArea.Top,SystemParameters.WorkArea.Bottom-height-24);
        if(config.DeckPanelLeft is not double savedLeft||config.DeckPanelTop is not double savedTop||!double.IsFinite(savedLeft)||!double.IsFinite(savedTop))return new Point(defaultLeft,defaultTop);
        const double visibleEdge=48;
        double minLeft=SystemParameters.VirtualScreenLeft-width+visibleEdge,maxLeft=SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth-visibleEdge;
        double minTop=SystemParameters.VirtualScreenTop-height+visibleEdge,maxTop=SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight-visibleEdge;
        return new Point(Math.Clamp(savedLeft,minLeft,maxLeft),Math.Clamp(savedTop,minTop,maxTop));
    }
    void WindowSourceInitialized(object? sender,EventArgs e)
    {
        var helper=new WindowInteropHelper(this);
        long style=GetWindowLongPtr(helper.Handle,GwlExStyle).ToInt64();
        long updated=style|WsExToolWindow|WsExNoActivate;
        SetWindowLongPtr(helper.Handle,GwlExStyle,new IntPtr(updated));
        UsesNoActivateStyle=(GetWindowLongPtr(helper.Handle,GwlExStyle).ToInt64()&WsExNoActivate)!=0;
        HwndSource.FromHwnd(helper.Handle)?.AddHook(WindowMessageHook);
    }
    static IntPtr WindowMessageHook(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(msg==WmMouseActivate){handled=true;return new IntPtr(MaNoActivate);}
        return IntPtr.Zero;
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr64(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong32(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern IntPtr SetWindowLongPtr64(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")]static extern int SetWindowLong32(IntPtr hwnd,int index,int value);
    static IntPtr GetWindowLongPtr(IntPtr hwnd,int index)=>IntPtr.Size==8?GetWindowLongPtr64(hwnd,index):new IntPtr(GetWindowLong32(hwnd,index));
    static IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value)=>IntPtr.Size==8?SetWindowLongPtr64(hwnd,index,value):new IntPtr(SetWindowLong32(hwnd,index,value.ToInt32()));
}

/// <summary>ブランク画面またはクロックを、指定モニター全体へ表示します。</summary>
internal sealed class ScreenOverlayWindow:Window
{
    readonly DispatcherTimer clockTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    readonly TextBlock? timeText,dateText;
    readonly System.Drawing.Rectangle screenBounds;
    readonly ClockDisplayMode displayMode;
    internal bool IsClock=>timeText!=null;

    internal ScreenOverlayWindow(System.Windows.Forms.Screen screen,bool clock,AppConfig config)
        :this(screen,clock,config,clock)
    {
    }

    internal ScreenOverlayWindow(System.Windows.Forms.Screen screen,bool clock,AppConfig config,bool useConfiguredBackground)
    {
        screenBounds=screen.Bounds;
        displayMode=config.ClockDisplayMode;
        Title=clock?"RELYR クロック":"RELYR ブランク";
        WindowStyle=WindowStyle.None;
        ResizeMode=ResizeMode.NoResize;
        ShowInTaskbar=false;
        ShowActivated=false;
        Topmost=true;
        Background=WpfBrushes.Black;
        Cursor=WpfCursors.None;
        ForceCursor=true;
        Left=screen.Bounds.Left;
        Top=screen.Bounds.Top;
        Width=screen.Bounds.Width;
        Height=screen.Bounds.Height;

        var root=new Grid{ClipToBounds=true,Background=WpfBrushes.Black,Cursor=WpfCursors.None,ForceCursor=true};
        Content=root;
        if(useConfiguredBackground)
        {
            AddClockBackground(root,screen,config);
            root.Children.Add(new Border{Background=new SolidColorBrush(WpfColor.FromArgb(92,0,0,0))});
        }
        if(clock)
        {
            var clockPanel=new StackPanel{HorizontalAlignment=System.Windows.HorizontalAlignment.Center,VerticalAlignment=System.Windows.VerticalAlignment.Center};
            timeText=new TextBlock
            {
                FontSize=Math.Clamp(screen.Bounds.Height*.19,96,230),FontWeight=FontWeights.Light,
                FontFamily=new System.Windows.Media.FontFamily("Segoe UI Variable Display"),FontStretch=FontStretches.Condensed,
                Foreground=WpfBrushes.White,TextAlignment=TextAlignment.Center,
                Effect=new DropShadowEffect{BlurRadius=18,ShadowDepth=2,Opacity=.62,Color=Colors.Black}
            };
            dateText=new TextBlock
            {
                FontSize=Math.Clamp(screen.Bounds.Height*.035,24,48),FontWeight=FontWeights.Light,
                FontFamily=new System.Windows.Media.FontFamily("Segoe UI Variable Text"),
                Foreground=new SolidColorBrush(WpfColor.FromArgb(225,255,255,255)),TextAlignment=TextAlignment.Center,
                Margin=new Thickness(0,8,0,0),Effect=new DropShadowEffect{BlurRadius=10,ShadowDepth=1,Opacity=.6,Color=Colors.Black}
            };
            clockPanel.Children.Add(timeText);clockPanel.Children.Add(dateText);
            root.Children.Add(clockPanel);
            UpdateClock();
            clockTimer.Tick+=(_,_)=>UpdateClock();
            clockTimer.Start();
        }

        SourceInitialized+=(_,_)=>SetWindowPos(new WindowInteropHelper(this).Handle,IntPtr.Zero,screenBounds.Left,screenBounds.Top,screenBounds.Width,screenBounds.Height,0x0010|0x0040);
        Closed+=(_,_)=>clockTimer.Stop();
    }

    void AddClockBackground(Grid root,System.Windows.Forms.Screen screen,AppConfig config)
    {
        if(config.ClockBackgroundMode==ClockBackgroundMode.Solid)
        {
            root.Background=new SolidColorBrush(ParseClockColor(config.ClockSolidColor));
            return;
        }
        ImageSource? source=config.ClockBackgroundMode switch
        {
            ClockBackgroundMode.Image=>LoadImage(config.ClockBackgroundImage),
            ClockBackgroundMode.FrostedScreen=>CaptureScreen(screen.Bounds),
            _=>null
        };
        if(source==null)
        {
            root.Background=new LinearGradientBrush(
                WpfColor.FromRgb(20,31,46),WpfColor.FromRgb(8,13,21),new Point(0,0),new Point(1,1));
            return;
        }
        var image=new WpfImage{Source=source,Stretch=Stretch.UniformToFill};
        if(config.ClockBackgroundMode==ClockBackgroundMode.FrostedScreen)image.Effect=new BlurEffect{Radius=28,RenderingBias=RenderingBias.Quality};
        root.Children.Add(image);
    }

    internal static WpfColor ParseClockColor(string? value)
    {
        try
        {
            if(System.Windows.Media.ColorConverter.ConvertFromString(value) is WpfColor color)return color;
        }
        catch(FormatException){}
        return WpfColor.FromRgb(16,31,46);
    }

    void UpdateClock()
    {
        if(timeText==null||dateText==null)return;
        DateTime now=DateTime.Now;
        bool seconds=displayMode is ClockDisplayMode.TimeWithSeconds or ClockDisplayMode.FullDateAndTime;
        timeText.Text=now.ToString(seconds?"H:mm:ss":"H:mm");
        dateText.Text=displayMode switch
        {
            ClockDisplayMode.Time or ClockDisplayMode.TimeWithSeconds=>"",
            ClockDisplayMode.DateAndTime=>now.ToString("M月d日（ddd）"),
            _=>now.ToString("yyyy年M月d日（ddd）")
        };
        dateText.Visibility=dateText.Text.Length==0?Visibility.Collapsed:Visibility.Visible;
    }

    static ImageSource? LoadImage(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))return null;
        try
        {
            var bitmap=new BitmapImage();
            bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(path,UriKind.Absolute);bitmap.EndInit();bitmap.Freeze();
            return bitmap;
        }
        catch{return null;}
    }

    static ImageSource? CaptureScreen(System.Drawing.Rectangle bounds)
    {
        try
        {
            using var bitmap=new System.Drawing.Bitmap(bounds.Width,bounds.Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using(var graphics=System.Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(bounds.Left,bounds.Top,0,0,bounds.Size,System.Drawing.CopyPixelOperation.SourceCopy);
            IntPtr handle=bitmap.GetHbitmap();
            try
            {
                var source=Imaging.CreateBitmapSourceFromHBitmap(handle,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();return source;
            }
            finally{DeleteObject(handle);}
        }
        catch{return null;}
    }

    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr insertAfter,int x,int y,int width,int height,uint flags);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr value);
}
