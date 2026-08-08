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

internal sealed class ScreenOverlayWindow : Window
{
    readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly TextBlock? timeText, dateText;
    readonly System.Drawing.Rectangle screenBounds;
    readonly ClockDisplayMode displayMode;
    internal bool IsClock => timeText != null;

    internal ScreenOverlayWindow(System.Windows.Forms.Screen screen, bool clock, AppConfig config)
        : this(screen, clock, config, clock)
    {
    }

    internal ScreenOverlayWindow(System.Windows.Forms.Screen screen, bool clock, AppConfig config, bool useConfiguredBackground)
    {
        screenBounds = screen.Bounds;
        displayMode = config.ClockDisplayMode;
        Title = clock ? "RELYR クロック" : "RELYR ブランク";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = WpfBrushes.Black;
        Cursor = WpfCursors.None;
        ForceCursor = true;
        Left = screen.Bounds.Left;
        Top = screen.Bounds.Top;
        Width = screen.Bounds.Width;
        Height = screen.Bounds.Height;

        var root = new Grid { ClipToBounds = true, Background = WpfBrushes.Black, Cursor = WpfCursors.None, ForceCursor = true };
        Content = root;
        if (useConfiguredBackground)
        {
            AddClockBackground(root, screen, config);
            root.Children.Add(new Border { Background = new SolidColorBrush(WpfColor.FromArgb(92, 0, 0, 0)) });
        }
        if (clock)
        {
            var clockPanel = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            timeText = new TextBlock
            {
                FontSize = Math.Clamp(screen.Bounds.Height * .19, 96, 230),
                FontWeight = FontWeights.Light,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Display"),
                FontStretch = FontStretches.Condensed,
                Foreground = WpfBrushes.White,
                TextAlignment = TextAlignment.Center,
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = .62, Color = Colors.Black }
            };
            dateText = new TextBlock
            {
                FontSize = Math.Clamp(screen.Bounds.Height * .035, 24, 48),
                FontWeight = FontWeights.Light,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text"),
                Foreground = new SolidColorBrush(WpfColor.FromArgb(225, 255, 255, 255)),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0),
                Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = .6, Color = Colors.Black }
            };
            clockPanel.Children.Add(timeText);
            clockPanel.Children.Add(dateText);
            root.Children.Add(clockPanel);
            UpdateClock();
            clockTimer.Tick += (_, _) => UpdateClock();
            clockTimer.Start();
        }

        SourceInitialized += (_, _) => SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, screenBounds.Left, screenBounds.Top, screenBounds.Width, screenBounds.Height, 0x0010 | 0x0040);
        Closed += (_, _) => clockTimer.Stop();
    }

    void AddClockBackground(Grid root, System.Windows.Forms.Screen screen, AppConfig config)
    {
        if (config.ClockBackgroundMode == ClockBackgroundMode.Solid)
        {
            root.Background = new SolidColorBrush(ParseClockColor(config.ClockSolidColor));
            return;
        }
        ImageSource? source = config.ClockBackgroundMode switch
        {
            ClockBackgroundMode.Image => LoadImage(config.ClockBackgroundImage),
            ClockBackgroundMode.FrostedScreen => CaptureScreen(screen.Bounds),
            _ => null
        };
        if (source == null)
        {
            root.Background = new LinearGradientBrush(
                WpfColor.FromRgb(20, 31, 46), WpfColor.FromRgb(8, 13, 21), new Point(0, 0), new Point(1, 1));
            return;
        }
        var image = new WpfImage { Source = source, Stretch = Stretch.UniformToFill };
        if (config.ClockBackgroundMode == ClockBackgroundMode.FrostedScreen)
            image.Effect = new BlurEffect { Radius = 28, RenderingBias = RenderingBias.Quality };
        root.Children.Add(image);
    }

    internal static WpfColor ParseClockColor(string? value)
    {
        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(value) is WpfColor color)
                return color;
        }
        catch (FormatException) { }
        return WpfColor.FromRgb(16, 31, 46);
    }

    void UpdateClock()
    {
        if (timeText == null || dateText == null)
            return;
        DateTime now = DateTime.Now;
        bool seconds = displayMode is ClockDisplayMode.TimeWithSeconds or ClockDisplayMode.FullDateAndTime;
        timeText.Text = now.ToString(seconds ? "H:mm:ss" : "H:mm");
        dateText.Text = displayMode switch
        {
            ClockDisplayMode.Time or ClockDisplayMode.TimeWithSeconds => "",
            ClockDisplayMode.DateAndTime => now.ToString("M月d日（ddd）"),
            _ => now.ToString("yyyy年M月d日（ddd）")
        };
        dateText.Visibility = dateText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    static ImageSource? LoadImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    static ImageSource? CaptureScreen(System.Drawing.Rectangle bounds)
    {
        try
        {
            using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, System.Drawing.CopyPixelOperation.SourceCopy);
            IntPtr handle = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally { DeleteObject(handle); }
        }
        catch { return null; }
    }

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
}
