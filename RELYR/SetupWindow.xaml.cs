using System.Windows;
using System.Windows.Media;
using WpfBrush=System.Windows.Media.Brush;
using WpfColor=System.Windows.Media.Color;
using WpfColorConverter=System.Windows.Media.ColorConverter;

namespace RELYR;

public partial class SetupWindow:Window
{
    readonly bool openedFromSettings;
    int pageIndex;

    public bool DoNotShowAgain=>DoNotShowAgainBox.IsChecked==true;
    internal bool TitleBarUsesDarkMode{get;private set;}
    internal bool UsesDarkPalette{get;private set;}
    internal int CurrentPage=>pageIndex;

    public SetupWindow(bool openedFromSettings=false)
    {
        this.openedFromSettings=openedFromSettings;
        InitializeComponent();
        if(openedFromSettings)
        {
            DoNotShowAgainBox.Visibility=Visibility.Collapsed;
            SkipButton.Visibility=Visibility.Collapsed;
        }
        ApplyTheme(MainWindow.IsWindowsAppDarkMode());
        MainWindow.FollowWindowsTitleBarTheme(this,ApplyTheme);
        ShowPage(0);
    }

    void ApplyTheme(bool dark)
    {
        TitleBarUsesDarkMode=dark;UsesDarkPalette=dark;
        SetBrush("TutorialBackground",dark?"#0F141C":"#F5F7FB");
        SetBrush("TutorialSurface",dark?"#141B25":"#FFFFFF");
        SetBrush("TutorialCard",dark?"#1A2330":"#FFFFFF");
        SetBrush("TutorialBorder",dark?"#334154":"#D5DEE9");
        SetBrush("PrimaryText",dark?"#F2F6FC":"#172231");
        SetBrush("SecondaryText",dark?"#AAB7C9":"#526174");
        SetBrush("TutorialAccent",dark?"#55A6FF":"#126FE5");
        SetBrush("TutorialAccentSoft",dark?"#172C44":"#EAF3FF");
        SetBrush("TutorialGreen",dark?"#6CD48A":"#258A43");
        SetBrush("TutorialOrange",dark?"#F5B24B":"#C97800");
        SetBrush("TutorialRed",dark?"#FF7580":"#D64854");
        SetBrush("TutorialKey",dark?"#253142":"#F4F7FB");
        UpdateDots();
    }

    void SetBrush(string key,string value)=>Resources[key]=new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(value));

    void ShowPage(int index)
    {
        pageIndex=Math.Clamp(index,0,2);
        PageOne.Visibility=pageIndex==0?Visibility.Visible:Visibility.Collapsed;
        PageTwo.Visibility=pageIndex==1?Visibility.Visible:Visibility.Collapsed;
        PageThree.Visibility=pageIndex==2?Visibility.Visible:Visibility.Collapsed;
        BackButton.Visibility=pageIndex==0?Visibility.Collapsed:Visibility.Visible;
        NextButton.Content=pageIndex==2?(openedFromSettings?"閉じる":"RELYRを使い始める"):"次へ";
        UpdateDots();
    }

    void UpdateDots()
    {
        if(Dot1==null)return;
        var active=(WpfBrush)Resources["TutorialAccent"];
        var inactive=new SolidColorBrush(UsesDarkPalette?WpfColor.FromRgb(64,76,94):WpfColor.FromRgb(207,214,224));
        Dot1.Fill=pageIndex==0?active:inactive;Dot2.Fill=pageIndex==1?active:inactive;Dot3.Fill=pageIndex==2?active:inactive;
    }

    void Next_Click(object sender,RoutedEventArgs e){if(pageIndex<2)ShowPage(pageIndex+1);else Complete();}
    void Back_Click(object sender,RoutedEventArgs e)=>ShowPage(pageIndex-1);
    void Skip_Click(object sender,RoutedEventArgs e)=>Complete();
    void Complete(){DialogResult=true;Close();}

    internal void ShowPageForTest(int index)=>ShowPage(index);
}
