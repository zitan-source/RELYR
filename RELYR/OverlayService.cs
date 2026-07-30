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
    static int fullScreenActive;
    static int fullScreenClosing;
    static int fullScreenDismissArmed;
    static System.Drawing.Point fullScreenStartCursor;
    internal static Action<string>? ActionRequestedForTest;
    internal static bool FullScreenVisible=>Volatile.Read(ref fullScreenActive)!=0;

    internal static void Configure(Func<AppConfig>? provider,Func<bool>? inputDownProvider=null,Action<Mapping>? deckAction=null)
    {
        configProvider=provider;
        physicalInputDownProvider=inputDownProvider;
        deckActionRequested=deckAction;
    }
    internal static void Shutdown()
    {
        if(WpfApplication.Current?.Dispatcher.CheckAccess()==true)
        {
            inputPanel?.Close();inputPanel=null;deckPanel?.Close();deckPanel=null;CloseScreenOverlays();configProvider=null;physicalInputDownProvider=null;deckActionRequested=null;
        }
        else if(WpfApplication.Current is { } app)_=app.Dispatcher.BeginInvoke(Shutdown);
    }

    internal static bool IsOverlayAction(string? value)=>value is NumpadAction or ExtendedKeypadAction or DeckPanelAction or BlankAction or ClockAction;

    internal static bool TryShow(string? value)
    {
        if(!IsOverlayAction(value))return false;
        string action=value!;
        if(ActionRequestedForTest is { } test){test(action);return true;}
        var dispatcher=WpfApplication.Current?.Dispatcher;
        if(dispatcher==null)return false;
        _=dispatcher.BeginInvoke(()=>ShowOnUiThread(action));
        return true;
    }

    static void ShowOnUiThread(string action)
    {
        if(action==DeckPanelAction)
        {
            inputPanel?.Close();inputPanel=null;
            if(deckPanel is {IsVisible:true} existing){existing.Close();deckPanel=null;return;}
            AppConfig deckConfig=configProvider?.Invoke()??new AppConfig();
            deckPanel=new DeckPanelOverlayWindow(deckConfig,deckActionRequested,deckConfig.InputPanelOpacityPercent);
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
            inputPanel=new InputPanelOverlayWindow(extended,opacity,useUsLayout);
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
        var screens=action==BlankAction||config.ClockShowOnAllMonitors
            ?System.Windows.Forms.Screen.AllScreens
            :[System.Windows.Forms.Screen.PrimaryScreen??System.Windows.Forms.Screen.AllScreens[0]];
        bool clock=action==ClockAction;
        foreach(var screen in screens)
        {
            var overlay=new ScreenOverlayWindow(screen,clock,config);
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
    bool dragging;
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

    internal InputPanelOverlayWindow(bool extended,int opacityPercent=96,bool useUsLayout=false)
    {
        IsExtended=extended;
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
        Left=Math.Max(SystemParameters.WorkArea.Left,SystemParameters.WorkArea.Right-Width-24);
        Top=Math.Max(SystemParameters.WorkArea.Top,SystemParameters.WorkArea.Bottom-Height-24);

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
        Closed+=(_,_)=>dragging=false;
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
        AddUniformKey(grid,"PageUp","PageUp");
        AddUniformKey(grid,"Delete","Delete");
        AddUniformKey(grid,"End","End");
        AddUniformKey(grid,"PageDown","PageDown");
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
            Content=label,Tag=action,Margin=new Thickness(KeyGap/2),Padding=new Thickness(4,5,4,5),
            FontSize=14,FontWeight=FontWeights.Medium,
            HorizontalContentAlignment=System.Windows.HorizontalAlignment.Center,VerticalContentAlignment=System.Windows.VerticalAlignment.Center
        };
        if(!double.IsNaN(width))button.Width=width;
        if(!double.IsNaN(height))button.Height=height;
        if(WpfApplication.Current?.Resources["AppButtonStyle"] is Style style)button.Style=style;
        return button;
    }

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
        Left=windowStartLeft+delta.X;Top=windowStartTop+delta.Y;
        e.Handled=true;
    }

    internal static Point PhysicalDragDeltaToDip(Vector physicalDelta,DpiScale dpi)=>
        new(physicalDelta.X/Math.Max(.01,dpi.DpiScaleX),physicalDelta.Y/Math.Max(.01,dpi.DpiScaleY));

    void DragEnded(object sender,MouseButtonEventArgs e)
    {
        if(!dragging)return;
        dragging=false;dragArea.ReleaseMouseCapture();e.Handled=true;
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
    bool dragging;
    Point dragStart;
    double windowStartLeft,windowStartTop;
    DpiScale dragDpi;

    internal IReadOnlyList<Button> DeckButtons=>deckButtons;
    internal Button CloseButton{get;private set;}=null!;
    internal double PanelOpacity=>panelCard.Opacity;
    internal bool UsesNoActivateStyle{get;private set;}
    readonly List<Button> deckButtons=[];
    readonly Border panelCard;

    internal DeckPanelOverlayWindow(AppConfig config,Action<Mapping>? executeAction,int opacityPercent=96)
    {
        execute=executeAction;
        Title="RELYR Deck";
        Width=DeckPanelLayout.Columns*(DeckPanelLayout.KeyWidth+DeckPanelLayout.Gap)+24;
        Height=DeckPanelLayout.Rows*70+84;
        MinWidth=Width;MaxWidth=Width;MinHeight=Height;MaxHeight=Height;
        WindowStyle=WindowStyle.None;
        ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true;
        Background=WpfBrushes.Transparent;
        ShowInTaskbar=false;
        ShowActivated=false;
        Topmost=true;
        WindowStartupLocation=WindowStartupLocation.Manual;
        Left=Math.Max(SystemParameters.WorkArea.Left,SystemParameters.WorkArea.Right-Width-24);
        Top=Math.Max(SystemParameters.WorkArea.Top,SystemParameters.WorkArea.Bottom-Height-24);

        panelCard=new Border
        {
            CornerRadius=new CornerRadius(14),
            BorderThickness=new Thickness(1),
            Padding=new Thickness(12),
            Opacity=Math.Clamp(opacityPercent,40,100)/100d,
            Effect=new DropShadowEffect{BlurRadius=24,ShadowDepth=5,Opacity=.4,Color=Colors.Black}
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

        var deckGrid=new UniformGrid
        {
            Rows=DeckPanelLayout.Rows,
            Columns=DeckPanelLayout.Columns,
            Width=DeckPanelLayout.Columns*(DeckPanelLayout.KeyWidth+DeckPanelLayout.Gap),
            Height=DeckPanelLayout.Rows*70,
            Margin=new Thickness(0,12,0,0)
        };
        for(int slot=1;slot<=DeckPanelLayout.SlotCount;slot++)
        {
            var mapping=DeckPanelLayout.FindMapping(config,slot);
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
                IsEnabled=MainWindow.MappingInterceptsInput(mapping)&&mapping!.Kind!=ActionKind.Gesture,
                HorizontalContentAlignment=System.Windows.HorizontalAlignment.Stretch,
                VerticalContentAlignment=System.Windows.VerticalAlignment.Center
            };
            if(WpfApplication.Current?.Resources["AppButtonStyle"] is Style style)button.Style=style;
            if(MainWindow.MappingInterceptsInput(mapping))
            {
                button.Background=new SolidColorBrush(MainWindow.AssignmentColorFor(mapping!));
                button.Foreground=WpfBrushes.White;
            }
            button.Click+=DeckButtonClicked;
            deckButtons.Add(button);
            var cell=new StackPanel{Width=DeckPanelLayout.KeyWidth+DeckPanelLayout.Gap,Height=70};
            cell.Children.Add(button);
            cell.Children.Add(DeckPanelLayout.CreateNameLabel(mapping));
            deckGrid.Children.Add(cell);
        }
        Grid.SetRow(deckGrid,1);
        root.Children.Add(deckGrid);

        SourceInitialized+=WindowSourceInitialized;
        Closed+=(_,_)=>dragging=false;
    }

    Border BuildHeader()
    {
        var border=new Border{CornerRadius=new CornerRadius(8),Padding=new Thickness(8,4,8,4),Cursor=WpfCursors.SizeAll};
        border.SetResourceReference(Border.BackgroundProperty,"SurfaceBackground");
        var grid=new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var grip=new TextBlock{Text="⋮⋮",FontSize=18,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,9,0)};
        grip.SetResourceReference(TextBlock.ForegroundProperty,"AccentBrush");
        var title=new TextBlock{Text="Deck",FontSize=16,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};
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

    void DeckButtonClicked(object sender,RoutedEventArgs e)
    {
        if(sender is not Button{Tag:Mapping mapping}||mapping.Kind==ActionKind.Gesture)return;
        execute?.Invoke(mapping);
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
        Point delta=InputPanelOverlayWindow.PhysicalDragDeltaToDip(current-dragStart,dragDpi);
        Left=windowStartLeft+delta.X;Top=windowStartTop+delta.Y;e.Handled=true;
    }
    void DragEnded(object sender,MouseButtonEventArgs e)
    {
        if(!dragging)return;dragging=false;dragArea.ReleaseMouseCapture();e.Handled=true;
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
        if(clock)
        {
            AddClockBackground(root,screen,config);
            var shade=new Border{Background=new SolidColorBrush(WpfColor.FromArgb(92,0,0,0))};
            root.Children.Add(shade);
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
