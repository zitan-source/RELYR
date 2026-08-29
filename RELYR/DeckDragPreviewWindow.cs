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
    const double CompactWidthDip = 20, CompactHeightDip = 20;
    const int GwlExStyle = -20;
    const long WsExTransparent = 0x00000020L;
    const long WsExToolWindow = 0x00000080L;
    const long WsExNoActivate = 0x08000000L;
    const uint SwpNoActivate = 0x0010;
    const uint SwpNoOwnerZOrder = 0x0200;
    const uint SwpShowWindow = 0x0040;
    readonly double previewWidth;
    readonly double previewHeight;
    internal double PreviewWidthForTest => previewWidth;
    internal double PreviewHeightForTest => previewHeight;
    internal Rect LastPhysicalBoundsForTest { get; private set; }
    internal DeckDragPreviewWindow(
        FrameworkElement preview,
        bool compact = false,
        double? customWidth = null,
        double? customHeight = null,
        bool preservePreviewSurface = false)
    {
        previewWidth = customWidth ?? (compact ? CompactWidthDip : WidthDip);
        previewHeight = customHeight ?? (compact ? CompactHeightDip : HeightDip);
        Width = previewWidth;
        Height = previewHeight;
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
            Width = previewWidth,
            Height = previewHeight,
            Padding = preservePreviewSurface ? new Thickness(0) : new Thickness(compact ? 1.5 : 5),
            CornerRadius = new CornerRadius(preservePreviewSurface ? 8 : compact ? 5 : 9),
            Opacity = preservePreviewSurface ? .96 : compact ? .88 : .78,
            Background = preservePreviewSurface ? WpfBrushes.Transparent : new SolidColorBrush(WpfColor.FromArgb(214, 24, 28, 31)),
            BorderBrush = preservePreviewSurface ? WpfBrushes.Transparent : new SolidColorBrush(WpfColor.FromArgb(210, 120, 225, 210)),
            BorderThickness = preservePreviewSurface ? new Thickness(0) : new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = compact ? 7 : 18, ShadowDepth = compact ? 2 : 4, Opacity = .55, Color = Colors.Black },
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
            int width = (int)Math.Ceiling(previewWidth * scale), height = (int)Math.Ceiling(previewHeight * scale);
            var bounds = new Rect(x - width / 2d, y - height / 2d, width, height);
            MoveToPhysicalBounds(hwnd, bounds);
        }
        catch { }
    }
    internal Rect MoveToPhysicalAvoiding(int x, int y, Rect? avoidBounds)
    {
        if (!IsVisible)
            return Rect.Empty;
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            uint dpi = GetDpiForWindow(hwnd);
            if (dpi == 0)
                dpi = 96;
            double scale = dpi / 96d;
            var previewSize = new System.Windows.Size(
                Math.Ceiling(previewWidth * scale),
                Math.Ceiling(previewHeight * scale));
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x, y));
            var workingArea = screen.WorkingArea;
            var bounds = CalculateAvoidingPlacement(
                new Point(x, y),
                previewSize,
                new Rect(workingArea.Left, workingArea.Top, workingArea.Width, workingArea.Height),
                avoidBounds);
            MoveToPhysicalBounds(hwnd, bounds);
            return bounds;
        }
        catch
        {
            MoveToPhysical(x, y);
            return LastPhysicalBoundsForTest;
        }
    }
    internal static Rect CalculateAvoidingPlacement(Point cursor, System.Windows.Size previewSize, Rect workingArea, Rect? avoidBounds)
    {
        const double pointerGap = 18;
        const double targetGap = 12;
        double width = Math.Min(previewSize.Width, workingArea.Width);
        double height = Math.Min(previewSize.Height, workingArea.Height);

        Rect Clamp(Rect candidate)
        {
            double maxLeft = Math.Max(workingArea.Left, workingArea.Right - width);
            double maxTop = Math.Max(workingArea.Top, workingArea.Bottom - height);
            return new Rect(
                Math.Clamp(candidate.Left, workingArea.Left, maxLeft),
                Math.Clamp(candidate.Top, workingArea.Top, maxTop),
                width,
                height);
        }

        if (avoidBounds is not { IsEmpty: false } avoid)
        {
            Rect[] pointerCandidates =
            [
                new(cursor.X + pointerGap, cursor.Y + pointerGap, width, height),
                new(cursor.X + pointerGap, cursor.Y - pointerGap - height, width, height),
                new(cursor.X - pointerGap - width, cursor.Y + pointerGap, width, height),
                new(cursor.X - pointerGap - width, cursor.Y - pointerGap - height, width, height)
            ];
            return pointerCandidates.Select(Clamp).First();
        }

        avoid.Inflate(targetGap, targetGap);
        Rect right = new(avoid.Right, cursor.Y - height / 2, width, height);
        Rect left = new(avoid.Left - width, cursor.Y - height / 2, width, height);
        Rect above = new(cursor.X - width / 2, avoid.Top - height, width, height);
        Rect below = new(cursor.X - width / 2, avoid.Bottom, width, height);
        Rect[] candidates = avoid.Left + avoid.Width / 2 <= workingArea.Left + workingArea.Width / 2
            ? [right, left, above, below]
            : [left, right, above, below];

        foreach (Rect candidate in candidates.Select(Clamp))
            if (!candidate.IntersectsWith(avoid))
                return candidate;

        // A normal key always has room on at least one side. If a future target
        // fills nearly the whole work area, choose the least-overlapping side.
        return candidates.Select(Clamp)
            .OrderBy(candidate => IntersectionArea(candidate, avoid))
            .First();
    }
    static double IntersectionArea(Rect first, Rect second)
    {
        Rect intersection = Rect.Intersect(first, second);
        return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
    }
    void MoveToPhysicalBounds(IntPtr hwnd, Rect bounds)
    {
        int left = (int)Math.Round(bounds.Left);
        int top = (int)Math.Round(bounds.Top);
        int width = Math.Max(1, (int)Math.Round(bounds.Width));
        int height = Math.Max(1, (int)Math.Round(bounds.Height));
        LastPhysicalBoundsForTest = new Rect(left, top, width, height);
        SetWindowPos(hwnd, new IntPtr(-1), left, top, width, height, SwpNoActivate | SwpNoOwnerZOrder | SwpShowWindow);
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) => GetWindowLongPtr64(hwnd, index);
    static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value) => SetWindowLongPtr64(hwnd, index, value);
}
