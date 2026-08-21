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

/// <summary>フォーカスを奪わず、クリックしたキーを直前の入力先へ送る半透明パネルです。</summary>
internal sealed class InputPanelOverlayWindow : Window
{
    const double KeyHeight = 52;
    const double KeyGap = 4;
    const int GwlExStyle = -20;
    const long WsExToolWindow = 0x00000080L;
    const long WsExNoActivate = 0x08000000L;
    const int WmMouseActivate = 0x0021;
    const int MaNoActivate = 3;
    const double PanelCornerRadius = 14;

    readonly Border dragArea;
    readonly Action<double, double>? savePosition;
    bool dragging;
    bool positionDirty;
    Point dragStart;
    double windowStartLeft, windowStartTop;
    DpiScale dragDpi;
    readonly double keyWidth;
    double KeyCellWidth => keyWidth + KeyGap;
    double KeyCellHeight => KeyHeight + KeyGap;

    internal bool IsExtended
    {
        get;
    }
    internal IReadOnlyList<Button> InputButtons => inputButtons;
    internal Button CloseButton { get; private set; } = null!;
    internal double PanelOpacity => panelCard.Opacity;
    readonly List<Button> inputButtons = [];
    readonly Border panelCard;

    internal InputPanelOverlayWindow(bool extended, int opacityPercent = 96, bool useUsLayout = false, AppConfig? config = null, Action<bool, double, double>? positionChanged = null)
    {
        IsExtended = extended;
        savePosition = positionChanged == null ? null : (left, top) => positionChanged(extended, left, top);
        keyWidth = useUsLayout ? 56 : 54;
        Title = extended ? "ナビゲーション・テンキー" : "テンキー";
        Width = extended ? KeyCellWidth * 7 + 16 + 24 : KeyCellWidth * 4 + 24;
        Height = extended ? 440 : 400;
        MinWidth = Width;
        MinHeight = Height;
        MaxWidth = Width;
        MaxHeight = Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        Background = new SolidColorBrush(WpfColor.FromRgb(0x1C, 0x1F, 0x22));
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Point initial = InitialPosition(config, extended, Width, Height);
        Left = initial.X;
        Top = initial.Y;

        panelCard = new Border
        {
            CornerRadius = new CornerRadius(PanelCornerRadius),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Opacity = Math.Clamp(opacityPercent, 40, 100) / 100d,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 5, Opacity = .4, Color = Colors.Black }
        };
        panelCard.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        panelCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Content = panelCard;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panelCard.Child = root;

        dragArea = BuildHeader();
        root.Children.Add(dragArea);
        var body = extended ? BuildExtendedBody() : BuildNumpad();
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        SourceInitialized += WindowSourceInitialized;
        SizeChanged += (_, _) => ApplyRoundedWindowRegion();
        dragArea.LostMouseCapture += (_, _) => dragging = false;
        Closed += (_, _) => { ReleaseOwnedMouseCapture(); PersistPosition(); };
    }

    Border BuildHeader()
    {
        var border = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 4, 8, 4), Cursor = WpfCursors.SizeAll };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var grip = new TextBlock { Text = "⠿", FontSize = 22, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
        grip.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var title = new TextBlock { Text = Title, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        var close = new Button
        {
            Width = 34,
            Height = 34,
            MinWidth = 34,
            MaxWidth = 34,
            MinHeight = 34,
            MaxHeight = 34,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            Focusable = false,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
            Content = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 1,1 L 11,11 M 11,1 L 1,11"),
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            }
        };
        ((System.Windows.Shapes.Path)close.Content).SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "PrimaryText");
        if (WpfApplication.Current?.Resources["AppButtonStyle"] is Style closeStyle)
            close.Style = closeStyle;
        close.Click += (_, _) => Close();
        CloseButton = close;
        grid.Children.Add(grip);
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);
        border.Child = grid;
        border.PreviewMouseLeftButtonDown += DragStarted;
        border.PreviewMouseMove += DragMoved;
        border.PreviewMouseLeftButtonUp += DragEnded;
        return border;
    }

    FrameworkElement BuildExtendedBody()
    {
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(KeyCellWidth * 3) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(KeyCellWidth * 4) });

        var left = new StackPanel();
        left.Children.Add(SectionLabel("ナビゲーション"));
        left.Children.Add(BuildNavigation());
        left.Children.Add(SectionLabel("カーソルキー", new Thickness(0, 14, 0, 6)));
        left.Children.Add(BuildCursorKeys());
        grid.Children.Add(left);

        var numpadHost = new StackPanel();
        numpadHost.Children.Add(SectionLabel("テンキー"));
        numpadHost.Children.Add(BuildNumpad());
        Grid.SetColumn(numpadHost, 2);
        grid.Children.Add(numpadHost);
        return grid;
    }

    FrameworkElement BuildNavigation()
    {
        var grid = CreateUniformGrid(3, 3);
        grid.Width = KeyCellWidth * 3;
        grid.Height = KeyCellHeight * 3;
        AddUniformKey(grid, "Insert", "Insert");
        AddUniformKey(grid, "Home", "Home");
        AddUniformKey(grid, "Page\nUp", "PageUp");
        AddUniformKey(grid, "Delete", "Delete");
        AddUniformKey(grid, "End", "End");
        AddUniformKey(grid, "Page\nDown", "PageDown");
        AddUniformKey(grid, "Print", "PrintScreen");
        AddUniformKey(grid, "Scroll", "ScrollLock");
        AddUniformKey(grid, "Pause", "Pause");
        return grid;
    }

    FrameworkElement BuildCursorKeys()
    {
        var grid = CreateUniformGrid(3, 2);
        grid.Width = KeyCellWidth * 3;
        grid.Height = KeyCellHeight * 2;
        grid.Children.Add(new Border());
        AddUniformKey(grid, "↑", "Up");
        grid.Children.Add(new Border());
        AddUniformKey(grid, "←", "Left");
        AddUniformKey(grid, "↓", "Down");
        AddUniformKey(grid, "→", "Right");
        return grid;
    }

    FrameworkElement BuildNumpad()
    {
        var outer = new Grid
        {
            Width = KeyCellWidth * 4,
            Height = KeyCellHeight * 6,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = IsExtended ? new Thickness(0) : new Thickness(0, 12, 0, 0)
        };
        for (int row = 0; row < 6; row++)
            outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(KeyCellHeight) });
        for (int col = 0; col < 4; col++)
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(KeyCellWidth) });

        AddGridKey(outer, "⌫  Backspace", "Back", 0, 0, 1, 4);
        AddGridKey(outer, "Num", "NumLock", 1, 0);
        AddGridKey(outer, "÷", "Divide", 1, 1);
        AddGridKey(outer, "×", "Multiply", 1, 2);
        AddGridKey(outer, "−", "Subtract", 1, 3);
        AddGridKey(outer, "7", "NumPad7", 2, 0);
        AddGridKey(outer, "8", "NumPad8", 2, 1);
        AddGridKey(outer, "9", "NumPad9", 2, 2);
        AddGridKey(outer, "＋", "Add", 2, 3, 2, 1);
        AddGridKey(outer, "4", "NumPad4", 3, 0);
        AddGridKey(outer, "5", "NumPad5", 3, 1);
        AddGridKey(outer, "6", "NumPad6", 3, 2);
        AddGridKey(outer, "1", "NumPad1", 4, 0);
        AddGridKey(outer, "2", "NumPad2", 4, 1);
        AddGridKey(outer, "3", "NumPad3", 4, 2);
        AddGridKey(outer, "Enter", "NumPadEnter", 4, 3, 2, 1);
        AddGridKey(outer, "0", "NumPad0", 5, 0, 1, 2);
        AddGridKey(outer, ".", "Decimal", 5, 2);
        return outer;
    }

    static TextBlock SectionLabel(string text, Thickness? margin = null)
    {
        var label = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = margin ?? new Thickness(0, 0, 0, 6) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        return label;
    }

    static UniformGrid CreateUniformGrid(int columns, int rows) => new() { Columns = columns, Rows = rows };

    void AddUniformKey(Panel panel, string label, string action) => panel.Children.Add(CreateInputButton(label, action));

    void AddGridKey(Grid grid, string label, string action, int row, int column, int rowSpan = 1, int columnSpan = 1)
    {
        var button = CreateInputButton(label, action);
        Grid.SetRow(button, row);
        Grid.SetColumn(button, column);
        Grid.SetRowSpan(button, rowSpan);
        Grid.SetColumnSpan(button, columnSpan);
        grid.Children.Add(button);
    }

    Button CreateInputButton(string label, string action)
    {
        var button = CreateKeyButton(label, action, double.NaN, double.NaN);
        button.Click += InputButtonClicked;
        inputButtons.Add(button);
        return button;
    }

    static Button CreateKeyButton(string label, string action, double width, double height)
    {
        var button = new Button
        {
            Content = KeyLabel(label),
            Tag = action,
            Margin = new Thickness(KeyGap / 2),
            Padding = new Thickness(4, 5, 4, 5),
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalContentAlignment = System.Windows.VerticalAlignment.Center
        };
        if (!double.IsNaN(width))
            button.Width = width;
        if (!double.IsNaN(height))
            button.Height = height;
        if (WpfApplication.Current?.Resources["AppButtonStyle"] is Style style)
            button.Style = style;
        return button;
    }
    static object KeyLabel(string label) => label.Contains('\n')
        ? new TextBlock { Text = label, TextAlignment = TextAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center }
        : label;

    void InputButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action } || action.Length == 0)
            return;
        try
        {
            InputEngine.SendShortcut(action);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Overlay key '{action}' failed: {ex.Message}"); }
    }

    void DragStarted(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject))
            return;
        dragging = true;
        dragStart = PointToScreen(e.GetPosition(this));
        dragDpi = VisualTreeHelper.GetDpi(this);
        windowStartLeft = Left;
        windowStartTop = Top;
        dragArea.CaptureMouse();
        e.Handled = true;
    }

    static bool IsInsideButton(DependencyObject? source)
    {
        for (var current = source; current != null;)
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase)
                return true;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    void DragMoved(object sender, MouseEventArgs e)
    {
        if (!dragging || e.LeftButton != MouseButtonState.Pressed)
            return;
        Point current = PointToScreen(e.GetPosition(this));
        Point delta = PhysicalDragDeltaToDip(current - dragStart, dragDpi);
        Left = windowStartLeft + delta.X;
        Top = windowStartTop + delta.Y;
        positionDirty = true;
        e.Handled = true;
    }

    internal static Point PhysicalDragDeltaToDip(Vector physicalDelta, DpiScale dpi) =>
        new(physicalDelta.X / Math.Max(.01, dpi.DpiScaleX), physicalDelta.Y / Math.Max(.01, dpi.DpiScaleY));

    void DragEnded(object sender, MouseButtonEventArgs e)
    {
        if (!dragging)
            return;
        dragging = false;
        dragArea.ReleaseMouseCapture();
        PersistPosition();
        e.Handled = true;
    }

    void ReleaseOwnedMouseCapture()
    {
        dragging = false;
        if (Mouse.Captured == dragArea)
            dragArea.ReleaseMouseCapture();
    }

    internal void CapturePanelMouseForTest() => dragArea.CaptureMouse();
    internal bool OwnsMouseCaptureForTest => Mouse.Captured == dragArea;

    void PersistPosition()
    {
        if (!positionDirty)
            return;
        positionDirty = false;
        savePosition?.Invoke(Left, Top);
    }
    internal void MoveAndPersistForTest(double left, double top)
    {
        Left = left;
        Top = top;
        positionDirty = false;
        savePosition?.Invoke(left, top);
    }
    internal static Point InitialPosition(AppConfig? config, bool extended, double width, double height)
    {
        double defaultLeft = Math.Max(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Right - width - 24);
        double defaultTop = Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - height - 24);
        if (config == null)
            return new Point(defaultLeft, defaultTop);
        double? configuredLeft = extended ? config.ExtendedKeypadPanelLeft : config.NumpadPanelLeft;
        double? configuredTop = extended ? config.ExtendedKeypadPanelTop : config.NumpadPanelTop;
        if (configuredLeft is not double savedLeft || configuredTop is not double savedTop || !double.IsFinite(savedLeft) || !double.IsFinite(savedTop))
            return new Point(defaultLeft, defaultTop);
        const double visibleEdge = 48;
        double minLeft = SystemParameters.VirtualScreenLeft - width + visibleEdge, maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - visibleEdge;
        double minTop = SystemParameters.VirtualScreenTop - height + visibleEdge, maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - visibleEdge;
        return new Point(Math.Clamp(savedLeft, minLeft, maxLeft), Math.Clamp(savedTop, minTop, maxTop));
    }

    void WindowSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        long style = GetWindowLongPtr(helper.Handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(helper.Handle, GwlExStyle, new IntPtr(style | WsExToolWindow | WsExNoActivate));
        HwndSource.FromHwnd(helper.Handle)?.AddHook(WindowMessageHook);
        ApplyRoundedWindowRegion();
    }

    void ApplyRoundedWindowRegion()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource)
            return;
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || ActualWidth <= 0 || ActualHeight <= 0)
            return;
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        int width = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX));
        int height = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));
        int diameter = Math.Max(2, (int)Math.Round(PanelCornerRadius * 2 * Math.Max(dpi.DpiScaleX, dpi.DpiScaleY)));
        IntPtr region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
        if (region != IntPtr.Zero && SetWindowRgn(hwnd, region, true) == 0)
            DeleteObject(region);
    }

    static IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmMouseActivate)
        {
            handled = true;
            return new IntPtr(MaNoActivate);
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool DeleteObject(IntPtr value);
    static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index));
    static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value) => IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, value) : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));
}
