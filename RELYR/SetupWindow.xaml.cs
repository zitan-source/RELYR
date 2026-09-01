using System.Windows;

namespace RELYR;

public partial class SetupWindow : Window
{
    readonly bool openedFromSettings;
    int pageIndex;

    public bool DoNotShowAgain => DoNotShowAgainBox.IsChecked == true;
    internal bool TitleBarUsesDarkMode
    {
        get; private set;
    }
    internal bool UsesDarkPalette
    {
        get; private set;
    }
    internal int CurrentPage => pageIndex;

    public SetupWindow(bool openedFromSettings = false)
    {
        this.openedFromSettings = openedFromSettings;
        InitializeComponent();
        if (openedFromSettings)
        {
            DoNotShowAgainBox.Visibility = Visibility.Collapsed;
            SkipButton.Visibility = Visibility.Collapsed;
        }
        ApplyTheme(ThemeService.UsesDark);
        MainWindow.FollowWindowsTitleBarTheme(this, ApplyTheme);
        ShowPage(0);
    }

    void ApplyTheme(bool dark)
    {
        TitleBarUsesDarkMode = dark;
        UsesDarkPalette = dark;
        UpdateDots();
    }

    void ShowPage(int index)
    {
        pageIndex = Math.Clamp(index, 0, 4);
        PageOne.Visibility = pageIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageTwo.Visibility = pageIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        PageThree.Visibility = pageIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        PageFour.Visibility = pageIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        PageFive.Visibility = pageIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = pageIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = LocalizationService.Text(pageIndex == 4 ? (openedFromSettings ? "閉じる" : "RELYRを使い始める") : "次へ");
        PageCounterText.Text = $"{pageIndex + 1} / 5";
        UpdateDots();
    }

    void UpdateDots()
    {
        if (Dot1 == null)
            return;
        var active = (System.Windows.Media.Brush)FindResource("AccentBrush");
        var inactive = (System.Windows.Media.Brush)FindResource("BorderBrush");
        var dots = new[] { Dot1, Dot2, Dot3, Dot4, Dot5 };
        for (int i = 0; i < dots.Length; i++)
            dots[i].Fill = pageIndex == i ? active : inactive;
    }

    void Next_Click(object sender, RoutedEventArgs e)
    {
        if (pageIndex < 4)
            ShowPage(pageIndex + 1);
        else
            Complete();
    }
    void Back_Click(object sender, RoutedEventArgs e) => ShowPage(pageIndex - 1);
    void Skip_Click(object sender, RoutedEventArgs e) => Complete();
    void Complete()
    {
        DialogResult = true;
        Close();
    }

    internal void ShowPageForTest(int index) => ShowPage(index);
}
