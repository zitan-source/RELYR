using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RELYR;

internal sealed class DeckTimerNotificationWindow : Window
{
    const int GwlExStyle = -20;
    const long WsExTransparent = 0x00000020L;
    const long WsExToolWindow = 0x00000080L;
    const long WsExNoActivate = 0x08000000L;

    readonly Border surface;
    readonly Border accentBar;
    readonly TextBlock glyph;
    readonly TextBlock caption;
    readonly TextBlock detail;
    readonly TimeSpan visibleDuration;
    System.Threading.Timer? hideTimer;

    internal bool UsesCompactClickThroughSurfaceForTest
        => !ShowActivated && !IsHitTestVisible && AllowsTransparency && Width <= 360 && Height <= 100;
    internal string CaptionForTest => caption.Text;
    internal string DetailForTest => detail.Text;

    internal DeckTimerNotificationWindow(TimeSpan timerDuration, TimeSpan? duration = null)
    {
        Width = 340;
        Height = 86;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        Opacity = 0;
        visibleDuration = duration ?? TimeSpan.FromSeconds(6);

        accentBar = new Border { Width = 4, CornerRadius = new CornerRadius(2) };
        glyph = new TextBlock
        {
            Text = "\uE823",
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 24,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        caption = new TextBlock
        {
            Text = "タイマーが終了しました",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        detail = new TextBlock
        {
            Text = $"{DeckTimerService.DurationLabel(timerDuration)}のタイマー",
            FontSize = 11.5,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(caption);
        text.Children.Add(detail);
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(accentBar);
        Grid.SetColumn(glyph, 1);
        body.Children.Add(glyph);
        Grid.SetColumn(text, 2);
        body.Children.Add(text);

        surface = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 11, 18, 11),
            SnapsToDevicePixels = true,
            Child = body,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 18,
                ShadowDepth = 4,
                Opacity = .45
            }
        };
        Content = surface;
        ApplyCurrentTheme();

        ThemeService.ThemeChanged += ThemeChanged;
        SourceInitialized += (_, _) => MakeNativeWindowClickThrough();
        Loaded += OverlayLoaded;
        Closed += (_, _) =>
        {
            ThemeService.ThemeChanged -= ThemeChanged;
            hideTimer?.Dispose();
            hideTimer = null;
        };
    }

    void OverlayLoaded(object sender, RoutedEventArgs e)
    {
        PositionOnCurrentScreen();
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            if (!IsLoaded)
                return;
            ApplyCurrentTheme();
            Opacity = 1;
            StartHideTimer();
        }));
    }

    void StartHideTimer()
    {
        hideTimer?.Dispose();
        IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        hideTimer = new System.Threading.Timer(_ =>
        {
            if (handle != IntPtr.Zero)
                ShowWindow(handle, 0);
            try
            {
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (IsLoaded)
                        Close();
                }));
            }
            catch { }
        }, null, visibleDuration, Timeout.InfiniteTimeSpan);
    }

    void PositionOnCurrentScreen()
    {
        var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not { } target)
            return;
        var topLeft = target.TransformFromDevice.Transform(new System.Windows.Point(area.Left, area.Top));
        var bottomRight = target.TransformFromDevice.Transform(new System.Windows.Point(area.Right, area.Bottom));
        Left = Math.Max(topLeft.X + 16, bottomRight.X - Width - 24);
        Top = Math.Max(topLeft.Y + 16, bottomRight.Y - Height - 24);
    }

    void MakeNativeWindowClickThrough()
    {
        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        long style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExTransparent | WsExToolWindow | WsExNoActivate));
    }

    void ThemeChanged()
    {
        if (Dispatcher.CheckAccess())
            ApplyCurrentTheme();
        else
            _ = Dispatcher.BeginInvoke((Action)ApplyCurrentTheme);
    }

    void ApplyCurrentTheme()
    {
        surface.Background = ThemeService.Brush("CardBackground");
        surface.BorderBrush = ThemeService.Brush("AccentBrush");
        accentBar.Background = ThemeService.Brush("AccentBrush");
        glyph.Foreground = ThemeService.Brush("AccentBrush");
        caption.Foreground = ThemeService.Brush("PrimaryText");
        detail.Foreground = ThemeService.Brush("SecondaryText");
    }

    internal void HideImmediatelyForProcessExit()
    {
        hideTimer?.Dispose();
        hideTimer = null;
        IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            ShowWindow(handle, 0);
    }

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
