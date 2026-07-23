using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Button=System.Windows.Controls.Button;
using WpfBrushes=System.Windows.Media.Brushes;

namespace RELYR;

public partial class MacroInputPickerWindow:Window
{
    const double Gap=4;
    readonly List<Button> inputButtons=[];
    readonly string layout;
    public event Action<string>? InputChosen;
    internal bool TitleBarUsesDarkMode{get;private set;}
    internal IReadOnlyList<Button> InputButtonsForTest=>inputButtons;

    public MacroInputPickerWindow(string keyboardLayout)
    {
        InitializeComponent();
        layout=keyboardLayout.Equals("US",StringComparison.OrdinalIgnoreCase)?"US":"JIS";
        KeyboardHeading.Text=$"キーボード（{layout}配列）とマウス";
        BuildInputSurface();
        MainWindow.FollowWindowsTitleBarTheme(this,value=>TitleBarUsesDarkMode=value);
    }

    void BuildInputSurface()
    {
        InputCanvas.Children.Clear();inputButtons.Clear();
        if(layout=="US")BuildUsKeyboard();else BuildJisKeyboard();
        AddExtendedFunctionKeys();
        BuildLowerGroups();
    }

    void BuildJisKeyboard()
    {
        AddTopFunctionRow(942);
        AddRow(44,[new("半角/全角","半角/全角",88),new("1","1",54),new("2","2",54),new("3","3",54),new("4","4",54),new("5","5",54),new("6","6",54),new("7","7",54),new("8","8",54),new("9","9",54),new("0","0",54),new("-","-",54),new("^","^",54),new("¥","¥",54),new("Back","Backspace",96)]);
        AddRow(100,[new("Tab","Tab",82),new("Q","Q",54),new("W","W",54),new("E","E",54),new("R","R",54),new("T","T",54),new("Y","Y",54),new("U","U",54),new("I","I",54),new("O","O",54),new("P","P",54),new("@","@",54),new("[","[",54)]);
        AddRow(156,[new("CapsLock","CapsLock",104),new("A","A",54),new("S","S",54),new("D","D",54),new("F","F",54),new("G","G",54),new("H","H",54),new("J","J",54),new("K","K",54),new("L","L",54),new(";",";",54),new(":",":",54),new("]","]",54)]);
        AddJisEnter();
        AddRow(212,[new("LeftShift","Shift",126),new("Z","Z",54),new("X","X",54),new("C","C",54),new("V","V",54),new("B","B",54),new("N","N",54),new("M","M",54),new(",",",",54),new(".",".",54),new("/","/",54),new("_","＼  _",54),new("RightShift","Shift",174)]);
        AddRow(268,[new("LeftCtrl","Ctrl",78),new("LWin","Win",64),new("LeftAlt","Alt",68),new("無変換","無変換",76),new("Space","Space",248),new("変換","変換",72),new("カタカナ","カタカナ",78),new("RightAlt","Alt",68),new("RWin","Win",64),new("RightCtrl","Ctrl",90)]);
    }

    void BuildUsKeyboard()
    {
        AddTopFunctionRow(900);
        AddRow(44,[new("`","`",56),new("1","1",56),new("2","2",56),new("3","3",56),new("4","4",56),new("5","5",56),new("6","6",56),new("7","7",56),new("8","8",56),new("9","9",56),new("0","0",56),new("-","-",56),new("=","=",56),new("Back","Backspace",120)]);
        AddRow(100,[new("Tab","Tab",88),new("Q","Q",56),new("W","W",56),new("E","E",56),new("R","R",56),new("T","T",56),new("Y","Y",56),new("U","U",56),new("I","I",56),new("O","O",56),new("P","P",56),new("[","[",56),new("]","]",56),new("\\","＼",88)]);
        AddRow(156,[new("CapsLock","CapsLock",102),new("A","A",56),new("S","S",56),new("D","D",56),new("F","F",56),new("G","G",56),new("H","H",56),new("J","J",56),new("K","K",56),new("L","L",56),new(";",";",56),new("'","'",56),new("Enter","Enter",134)]);
        AddRow(212,[new("LeftShift","Shift",136),new("Z","Z",56),new("X","X",56),new("C","C",56),new("V","V",56),new("B","B",56),new("N","N",56),new("M","M",56),new(",",",",56),new(".",".",56),new("/","/",56),new("RightShift","Shift",160)]);
        AddRow(268,[new("LeftCtrl","Ctrl",72),new("LWin","Win",72),new("LeftAlt","Alt",72),new("Space","Space",368),new("RightAlt","Alt",72),new("RWin","Win",72),new("Menu","Menu",72),new("RightCtrl","Ctrl",72)]);
    }

    void AddTopFunctionRow(double rightEdge)
    {
        const int count=14;double width=(rightEdge-Gap*(count-1))/count,x=0;
        AddButton("Esc","Esc",x,0,width,26);x+=width+Gap;
        for(int i=1;i<=12;i++){AddButton($"F{i}",$"F{i}",x,0,width,26);x+=width+Gap;}
        AddButton("Delete","Delete",x,0,width,26);
    }

    void AddExtendedFunctionKeys()
    {
        double rightEdge=layout=="US"?900:942,width=(rightEdge-Gap*11)/12,x=0;
        for(int i=13;i<=24;i++){AddButton($"F{i}",$"F{i}",x,338,width,26);x+=width+Gap;}
    }

    void BuildLowerGroups()
    {
        const double top=390,unit=54,keyHeight=52,padding=10,titleHeight=26,groupGap=12;
        double navigationWidth=padding*2+unit*3+Gap*2;
        double navigationHeight=titleHeight+keyHeight*3+Gap*2+padding;
        double numpadX=navigationWidth+groupGap;
        double numpadWidth=padding*2+unit*4+Gap*3;
        double numpadHeight=titleHeight+keyHeight*5+Gap*4+padding;
        double cursorX=numpadX+numpadWidth+groupGap;
        double cursorWidth=navigationWidth;
        double cursorHeight=titleHeight+keyHeight*2+Gap+padding;
        double mouseX=cursorX+cursorWidth+groupGap;
        double mouseWidth=1200-mouseX;

        AddFrame("ナビゲーション",0,top,navigationWidth,navigationHeight);
        AddFrame("テンキー",numpadX,top,numpadWidth,numpadHeight);
        AddFrame("カーソルキー",cursorX,top,cursorWidth,cursorHeight);
        AddFrame("マウス",mouseX,top,mouseWidth,numpadHeight);

        double navLeft=padding,firstY=top+titleHeight,step=unit+Gap;
        AddButton("Insert","Insert",navLeft,firstY,unit,keyHeight);AddButton("Home","Home",navLeft+step,firstY,unit,keyHeight);AddButton("PageUp","PageUp",navLeft+step*2,firstY,unit,keyHeight);
        AddButton("Delete","Delete",navLeft,firstY+56,unit,keyHeight);AddButton("End","End",navLeft+step,firstY+56,unit,keyHeight);AddButton("PageDown","PageDown",navLeft+step*2,firstY+56,unit,keyHeight);
        AddButton("PrintScreen","Print",navLeft,firstY+112,unit,keyHeight);AddButton("ScrollLock","Scroll",navLeft+step,firstY+112,unit,keyHeight);AddButton("Pause","Pause",navLeft+step*2,firstY+112,unit,keyHeight);

        double numLeft=numpadX+padding;
        AddButton("NumLock","Num",numLeft,firstY,unit,keyHeight);AddButton("Divide","÷",numLeft+step,firstY,unit,keyHeight);AddButton("Multiply","×",numLeft+step*2,firstY,unit,keyHeight);AddButton("Subtract","−",numLeft+step*3,firstY,unit,keyHeight);
        AddButton("NumPad7","7",numLeft,firstY+56,unit,keyHeight);AddButton("NumPad8","8",numLeft+step,firstY+56,unit,keyHeight);AddButton("NumPad9","9",numLeft+step*2,firstY+56,unit,keyHeight);AddButton("Add","＋",numLeft+step*3,firstY+56,unit,108);
        AddButton("NumPad4","4",numLeft,firstY+112,unit,keyHeight);AddButton("NumPad5","5",numLeft+step,firstY+112,unit,keyHeight);AddButton("NumPad6","6",numLeft+step*2,firstY+112,unit,keyHeight);
        AddButton("NumPad1","1",numLeft,firstY+168,unit,keyHeight);AddButton("NumPad2","2",numLeft+step,firstY+168,unit,keyHeight);AddButton("NumPad3","3",numLeft+step*2,firstY+168,unit,keyHeight);AddButton("NumPadEnter","Enter",numLeft+step*3,firstY+168,unit,108);
        AddButton("NumPad0","0",numLeft,firstY+224,unit*2+Gap,keyHeight);AddButton("Decimal",".",numLeft+step*2,firstY+224,unit,keyHeight);

        double cursorLeft=cursorX+padding;
        AddButton("Up","↑",cursorLeft+step,firstY,unit,keyHeight);
        AddButton("Left","←",cursorLeft,firstY+56,unit,keyHeight);AddButton("Down","↓",cursorLeft+step,firstY+56,unit,keyHeight);AddButton("Right","→",cursorLeft+step*2,firstY+56,unit,keyHeight);

        BuildMouse(mouseX,top,mouseWidth,numpadHeight);
    }

    void BuildMouse(double x,double y,double width,double height)
    {
        double bodyWidth=270,bodyHeight=248,bodyX=x+(width-bodyWidth)/2,bodyY=y+42;
        var body=new Border{Width=bodyWidth,Height=bodyHeight,CornerRadius=new CornerRadius(120),BorderThickness=new Thickness(2),BorderBrush=ThemeService.Brush("BorderBrush"),Background=ThemeService.Brush("CardBackground"),IsHitTestVisible=false};
        Canvas.SetLeft(body,bodyX);Canvas.SetTop(body,bodyY);InputCanvas.Children.Add(body);
        var split=new Line{X1=bodyX,X2=bodyX+bodyWidth,Y1=bodyY+78,Y2=bodyY+78,Stroke=ThemeService.Brush("BorderBrush"),StrokeThickness=2,IsHitTestVisible=false};
        InputCanvas.Children.Add(split);

        AddButton("MouseLeft","左クリック",bodyX+8,bodyY+8,102,62);
        AddButton("MouseRight","右クリック",bodyX+160,bodyY+8,102,62);
        AddButton("WheelUp","▲",bodyX+116,bodyY+8,38,27);
        AddButton("MouseMiddle","●",bodyX+116,bodyY+38,38,27);
        AddButton("WheelDown","▼",bodyX+116,bodyY+68,38,27);
        AddButton("TiltLeft","◀",bodyX+75,bodyY+104,58,38);
        AddButton("TiltRight","▶",bodyX+137,bodyY+104,58,38);
        AddButton("MouseBack","戻る",bodyX+28,bodyY+160,78,42);
        AddButton("MouseForward","進む",bodyX+28,bodyY+206,78,34);
        AddButton("MouseX","追加",bodyX+178,bodyY+160,64,80);
    }

    void AddFrame(string title,double x,double y,double width,double height)
    {
        var frame=new Border{Tag=title,Width=width,Height=height,CornerRadius=new CornerRadius(7),BorderThickness=new Thickness(1),BorderBrush=ThemeService.Brush("SubtleBorderBrush"),Background=ThemeService.Brush("CardBackground"),IsHitTestVisible=false};
        Canvas.SetLeft(frame,x);Canvas.SetTop(frame,y);InputCanvas.Children.Add(frame);
        var heading=new TextBlock{Text=title,Foreground=ThemeService.Brush("MutedText"),FontSize=11,FontWeight=FontWeights.SemiBold,IsHitTestVisible=false};
        Canvas.SetLeft(heading,x+10);Canvas.SetTop(heading,y+4);InputCanvas.Children.Add(heading);
    }

    void AddRow(double y,IEnumerable<KeySpec> keys)
    {
        double x=0;foreach(var key in keys){AddButton(key.Key,key.Label,x,y,key.Width,52);x+=key.Width+Gap;}
    }

    void AddJisEnter()
    {
        var geometry=Geometry.Parse("M 0,0 L 160,0 L 160,108 L 22,108 L 22,56 L 0,56 Z");
        var button=CreateButton("Enter","Enter",160,108);button.Clip=geometry;
        Canvas.SetLeft(button,782);Canvas.SetTop(button,100);InputCanvas.Children.Add(button);
        var outline=new Path{Data=geometry,Stroke=ThemeService.Brush("BorderBrush"),StrokeThickness=1,Fill=WpfBrushes.Transparent,IsHitTestVisible=false};
        Canvas.SetLeft(outline,782);Canvas.SetTop(outline,100);InputCanvas.Children.Add(outline);
    }

    void AddButton(string key,string label,double x,double y,double width,double height)
    {
        var button=CreateButton(key,label,width,height);
        Canvas.SetLeft(button,x);Canvas.SetTop(button,y);InputCanvas.Children.Add(button);
    }

    Button CreateButton(string key,string label,double width,double height)
    {
        var button=new Button{Tag=key,Content=label,Width=width,Height=height,MinWidth=0,MinHeight=0,ToolTip=MainWindow.DisplayInputName(key)};
        button.Click+=Input_Click;inputButtons.Add(button);return button;
    }

    void Input_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not Button{Tag:string input})return;
        InputChosen?.Invoke(input);
        StatusText.Text=$"「{MainWindow.DisplayInputName(input)}」を追加しました。続けて追加できます。";
    }

    void Close_Click(object sender,RoutedEventArgs e)=>Close();
    readonly record struct KeySpec(string Key,string Label,double Width);
}
