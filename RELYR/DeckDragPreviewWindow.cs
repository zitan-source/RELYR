using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;
using WpfApplication = System.Windows.Application;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;
using WpfImage = System.Windows.Controls.Image;

namespace RELYR;

sealed class DeckDragPreviewWindow : Window
{
    const double WidthDip = 72, HeightDip = 66;
    const int GwlExStyle = -20;
    const long WsExTransparent = 0x00000020L;
    const long WsExToolWindow = 0x00000080L;
    const long WsExNoActivate = 0x08000000L;
    const uint SwpNoActivate = 0x0010;
    const uint SwpNoOwnerZOrder = 0x0200;
    const uint SwpShowWindow = 0x0040;
    internal DeckDragPreviewWindow(FrameworkElement preview)
    {
        Width = WidthDip;
        Height = HeightDip;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        Content = new Border
        {
            Width = WidthDip,
            Height = HeightDip,
            Padding = new Thickness(5),
            CornerRadius = new CornerRadius(9),
            Opacity = .78,
            Background = new SolidColorBrush(WpfColor.FromArgb(214, 24, 28, 31)),
            BorderBrush = new SolidColorBrush(WpfColor.FromArgb(210, 120, 225, 210)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = .55, Color = Colors.Black },
            Child = preview
        };
        SourceInitialized += (_, _) => ConfigureHandle();
    }
    void ConfigureHandle()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            long style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExTransparent | WsExToolWindow | WsExNoActivate));
        }
        catch { }
    }
    internal void MoveToPhysical(int x, int y)
    {
        if (!IsVisible)
            return;
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            uint dpi = GetDpiForWindow(hwnd);
            if (dpi == 0)
                dpi = 96;
            double scale = dpi / 96d;
            int width = (int)Math.Ceiling(WidthDip * scale), height = (int)Math.Ceiling(HeightDip * scale);
            SetWindowPos(hwnd, new IntPtr(-1), x - width / 2, y - height / 2, width, height, SwpNoActivate | SwpNoOwnerZOrder | SwpShowWindow);
        }
        catch { }
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) => GetWindowLongPtr64(hwnd, index);
    static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value) => SetWindowLongPtr64(hwnd, index, value);
}
