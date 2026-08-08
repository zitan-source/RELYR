using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
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

[StructLayout(LayoutKind.Sequential)]
struct NativeDropPoint
{
    internal int X;
    internal int Y;
}

internal sealed class DeckPanelOverlayWindow : Window
{
    const int GwlExStyle = -20;
    const long WsExToolWindow = 0x00000080L;
    const long WsExNoActivate = 0x08000000L;
    const int WmMouseActivate = 0x0021;
    const int WmDropFiles = 0x0233;
    const int WmCopyGlobalData = 0x0049;
    const int WmCopyData = 0x004A;
    const uint MsgFiltAllow = 1;
    const int MaNoActivate = 3;
    const int WcaAccentPolicy = 19;
    const int AccentEnableAcrylicBlurBehind = 4;
    const int DwmwaWindowCornerPreference = 33;
    const int DwmwaUseImmersiveDarkMode = 20;
    const int DwmwaSystemBackdropType = 38;
    const int DwmWindowCornerPreferenceRound = 2;
    const int DwmsbtNone = 1;
    const double PanelCornerRadius = 14;
    const byte SolidSurfaceTintAlpha = 222;
    const int MinimumGlassOpacityPercent = 40;
    const int MaximumGlassOpacityPercent = 100;
    const byte MinimumBackdropTintAlpha = 20;
    const byte MaximumBackdropTintAlpha = 150;
    static readonly TimeSpan HoverAudioDelay = TimeSpan.FromMilliseconds(220);
    static readonly WpfColor AcrylicCharcoal = WpfColor.FromRgb(0x1C, 0x1F, 0x22);
    static readonly WpfColor DeckPrimaryText = WpfColor.FromRgb(0xF2, 0xF2, 0xF2);
    static readonly WpfColor DeckSecondaryText = WpfColor.FromRgb(0x9A, 0x9E, 0xA5);
    static readonly WpfColor DeckEmptyText = WpfColor.FromRgb(0x6B, 0x70, 0x77);
    static readonly WpfColor DeckAccent = WpfColor.FromRgb(0x35, 0xD0, 0xC5);

    readonly Border dragArea;
    readonly Action<Mapping>? execute;
    readonly Action<double, double>? savePosition;
    bool hoverPreviewsEnabled;
    bool dragging;
    bool positionDirty;
    Button? fileDragButton;
    DeckDragPreviewWindow? dragPreview;
    bool shellFileDropEnabled;
    bool internalDeckDragActive;
    bool deckReorderDragging;
    int deckReorderSourceSlot;
    Button? deckReorderTargetButton;
    Point fileDragStart;
    MediaPlayer? hoverAudioPlayer;
    Button? hoverAudioSource;
    Button? pendingHoverAudioSource;
    string pendingHoverAudioPath = "";
    readonly DispatcherTimer hoverAudioStartTimer = new() { Interval = HoverAudioDelay };
    readonly List<DeckVideoPreviewPopup> videoPreviews = [];
    Point dragStart;
    double windowStartLeft, windowStartTop;
    DpiScale dragDpi;

    internal IReadOnlyList<Button> DeckButtons => deckButtons;
    internal Button CloseButton { get; private set; } = null!;
    internal double VisualOpacityForTest => panelCard.Opacity;
    internal int GlassOpacityPercentForTest => glassOpacityPercent;
    internal byte BackdropTintAlphaForTest => backdropTintAlpha;
    internal bool UsesLegacyAcrylicFallbackForTest => backdropMode == DeckBackdropMode.AccentAcrylicOnly;
    internal string BackdropModeForTest => backdropMode.ToString();
    internal string LayoutId => layout.Id;
    internal bool UsesNoActivateStyle
    {
        get; private set;
    }
    internal bool UsesShellFileDrop => shellFileDropEnabled;
    readonly List<Button> deckButtons = [];
    readonly Border panelCard;
    readonly DeckLayoutDefinition layout;
    readonly UniformGrid deckGrid;
    TextBlock headerTitle = null!;
    Style? glassButtonStyle;
    Style? closeButtonStyle;
    DeckBackdropMode backdropMode;
    int glassOpacityPercent;
    byte backdropTintAlpha;
    Grid? acrylicDiffuseLayer;

    enum DeckBackdropMode
    {
        Pending,
        SystemBackdrop,
        AccentAcrylicOnly,
        SolidFallback
    }
    WpfColor panelTone;

    internal DeckPanelOverlayWindow(AppConfig config, Action<Mapping>? executeAction, int opacityPercent = 96, Action<double, double>? positionChanged = null, DeckLayoutDefinition? selectedLayout = null)
    {
        execute = executeAction;
        savePosition = positionChanged;
        hoverAudioStartTimer.Tick += HoverAudioStartTimerTick;
        hoverPreviewsEnabled = config.DeckHoverPreviewsEnabled;
        layout = selectedLayout ?? DeckPanelLayout.DefaultLayout(config) ?? new DeckLayoutDefinition();
        Title = "RELYR Deck - " + layout.Name;
        deckGrid = new UniformGrid();
        UpdateDeckDimensions();
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Point initial = InitialPosition(config, Width, Height);
        Left = initial.X;
        Top = initial.Y;

        panelCard = new Border
        {
            CornerRadius = new CornerRadius(PanelCornerRadius),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 8, 12, 8),
            ClipToBounds = true,
            Opacity = Math.Clamp(opacityPercent / 100.0, .4, 1),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 5, Opacity = .4, Color = Colors.Black }
        };
        SetGlassOpacity(opacityPercent);
        panelCard.SizeChanged += (_, _) => ApplyRoundedPanelClip();
        SizeChanged += (_, _) => ApplyRoundedPanelClip();
        ApplyPanelColor();
        Content = panelCard;

        var root = new Grid { ClipToBounds = true };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panelCard.Child = root;
        dragArea = BuildHeader();
        root.Children.Add(dragArea);

        BuildDeckButtons();
        var deckView = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0), Child = deckGrid };
        Grid.SetRow(deckView, 1);
        root.Children.Add(deckView);

        SourceInitialized += WindowSourceInitialized;
        ThemeService.ThemeChanged += ThemeChanged;
        Closed += (_, _) => { ThemeService.ThemeChanged -= ThemeChanged; dragging = false; fileDragButton = null; ClearDeckReorderTarget(); StopDragPreview(); ClearVideoPreviews(); CancelPendingHoverAudio(); StopHoverAudio(); StopShellFileDrop(); PersistPosition(); };
    }

    void UpdateDeckDimensions()
    {
        double naturalGridWidth = Math.Clamp(layout.Columns, 1, DeckPanelLayout.MaximumColumns) * (DeckPanelLayout.KeyWidth + DeckPanelLayout.Gap);
        double naturalGridHeight = Math.Clamp(layout.Rows, 1, DeckPanelLayout.MaximumRows) * DeckPanelLayout.CellHeight;
        double scale = Math.Min(1, Math.Min((SystemParameters.WorkArea.Width - 48) / Math.Max(1, naturalGridWidth), (SystemParameters.WorkArea.Height - 76) / Math.Max(1, naturalGridHeight)));
        Width = naturalGridWidth * scale + 24;
        Height = naturalGridHeight * scale + 52;
        MinWidth = Width;
        MaxWidth = Width;
        MinHeight = Height;
        MaxHeight = Height;
        deckGrid.Rows = Math.Clamp(layout.Rows, 1, DeckPanelLayout.MaximumRows);
        deckGrid.Columns = Math.Clamp(layout.Columns, 1, DeckPanelLayout.MaximumColumns);
        deckGrid.Width = naturalGridWidth;
        deckGrid.Height = naturalGridHeight;
    }

    void BuildDeckButtons()
    {
        ClearVideoPreviews();
        CancelPendingHoverAudio();
        StopHoverAudio();
        deckButtons.Clear();
        deckGrid.Children.Clear();
        var deferredPreviews = new List<Button>();
        for (int slot = 1; slot <= DeckPanelLayout.VisibleSlotCount(layout); slot++)
        {
            var entry = CreateDeckButtonCell(slot);
            deckButtons.Add(entry.Button);
            deckGrid.Children.Add(entry.Cell);
            if (NeedsDeferredFilePreview(DeckPanelLayout.FindMapping(layout, slot)))
                deferredPreviews.Add(entry.Button);
        }
        BeginDeferredFilePreviews(deferredPreviews);
    }
    (Button Button, StackPanel Cell) CreateDeckButtonCell(int slot)
    {
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        var button = new Button
        {
            Tag = slot,
            Content = DeckPanelLayout.CreateButtonContent(DeckPanelLayout.InputName(slot), mapping, !NeedsDeferredFilePreview(mapping)),
            Width = DeckPanelLayout.KeyWidth,
            Height = DeckPanelLayout.KeyHeight,
            MinWidth = 0,
            MinHeight = 0,
            Margin = new Thickness(DeckPanelLayout.Gap / 2, 0, DeckPanelLayout.Gap / 2, 0),
            Padding = new Thickness(3),
            Focusable = false,
            IsEnabled = true,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalContentAlignment = System.Windows.VerticalAlignment.Center
        };
        button.Style = GlassButtonStyle();
        button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ControlBackground");
        button.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "PrimaryText");
        button.BorderBrush = WpfBrushes.Transparent;
        button.BorderThickness = new Thickness(0);
        if (DeckPanelLayout.TryGetButtonColor(mapping, out var customColor))
        {
            button.Background = new SolidColorBrush(customColor);
            button.Foreground = new SolidColorBrush(DeckPanelLayout.TextColorFor(customColor));
        }
        else if (MainWindow.MappingInterceptsInput(mapping))
        {
            button.Background = new SolidColorBrush(MainWindow.AssignmentColorFor(mapping!));
            button.Foreground = WpfBrushes.White;
        }
        button.Click += DeckButtonClicked;
        button.PreviewMouseLeftButtonDown += DeckButtonDragStarted;
        button.PreviewMouseMove += DeckButtonDragMoved;
        button.PreviewMouseLeftButtonUp += DeckButtonDragEnded;
        button.ContextMenu = CreateDeckButtonContextMenu(slot);
        button.AllowDrop = true;
        button.PreviewDragOver += DeckButtonDragOver;
        button.PreviewDrop += DeckButtonDropped;
        if (hoverPreviewsEnabled && !NeedsDeferredFilePreview(mapping))
            ConfigureHoverPreview(button, mapping);
        var cell = new StackPanel { Width = DeckPanelLayout.KeyWidth + DeckPanelLayout.Gap, Height = DeckPanelLayout.CellHeight };
        cell.Children.Add(button);
        cell.Children.Add(DeckPanelLayout.CreateNameLabel(mapping));
        return (button, cell);
    }
    void RefreshDeckSlots(int firstSlot, int secondSlot)
    {
        var deferredPreviews = new List<Button>();
        foreach (int slot in new[] { firstSlot, secondSlot }.Distinct())
        {
            int index = slot - 1;
            if (index < 0 || index >= deckButtons.Count || index >= deckGrid.Children.Count)
                continue;
            ClearVideoPreviewFor(deckButtons[index]);
            var entry = CreateDeckButtonCell(slot);
            deckButtons[index] = entry.Button;
            deckGrid.Children.RemoveAt(index);
            deckGrid.Children.Insert(index, entry.Cell);
            if (NeedsDeferredFilePreview(DeckPanelLayout.FindMapping(layout, slot)))
                deferredPreviews.Add(entry.Button);
        }
        BeginDeferredFilePreviews(deferredPreviews);
    }
    static bool NeedsDeferredFilePreview(Mapping? mapping) =>
        mapping != null && File.Exists(mapping.DeckFilePath) &&
        (DeckPanelLayout.IsImageFile(mapping.DeckFilePath) || DeckPanelLayout.IsVideoFile(mapping.DeckFilePath));

    void BeginDeferredFilePreviews(IReadOnlyCollection<Button> buttons)
    {
        if (buttons.Count == 0)
            return;
        var pending = buttons.Select(button =>
        {
            int slot = button.Tag is int value ? value : 0;
            var mapping = DeckPanelLayout.FindMapping(layout, slot);
            return (Button: button, Slot: slot, Path: mapping?.DeckFilePath ?? "");
        }).Where(x => x.Slot > 0 && x.Path.Length > 0).ToArray();
        if (pending.Length == 0)
            return;

        _ = Task.Run(() =>
        {
            foreach (var item in pending)
            {
                try
                {
                    if (DeckPanelLayout.IsVideoFile(item.Path))
                    {
                        _ = DeckPanelLayout.LoadVideoThumbnail(item.Path, 96, 54);
                        if (hoverPreviewsEnabled)
                            _ = DeckPanelLayout.LoadVideoThumbnail(item.Path, 640, 360);
                    }
                    else
                    {
                        _ = DeckPanelLayout.LoadImageThumbnail(item.Path, 96);
                        if (hoverPreviewsEnabled)
                            _ = DeckPanelLayout.LoadImageThumbnail(item.Path, 360);
                    }
                }
                catch { }
            }

            try
            {
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (!IsLoaded)
                        return;
                    foreach (var item in pending)
                    {
                        if (!deckButtons.Contains(item.Button))
                            continue;
                        var mapping = DeckPanelLayout.FindMapping(layout, item.Slot);
                        if (mapping == null || !string.Equals(mapping.DeckFilePath, item.Path, StringComparison.OrdinalIgnoreCase))
                            continue;
                        item.Button.Content = DeckPanelLayout.CreateButtonContent(DeckPanelLayout.InputName(item.Slot), mapping);
                        if (hoverPreviewsEnabled)
                            ConfigureHoverPreview(item.Button, mapping);
                    }
                }));
            }
            catch { }
        });
    }
    void ClearVideoPreviewFor(Button source)
    {
        foreach (var preview in videoPreviews.Where(x => x.IsFor(source)).ToList())
        {
            preview.Dispose();
            videoPreviews.Remove(preview);
        }
    }

    internal void Refresh(int opacityPercent, bool previewsEnabled)
    {
        hoverPreviewsEnabled = previewsEnabled;
        SetGlassOpacity(opacityPercent);
        ApplyPanelColor();
        Title = "RELYR Deck - " + layout.Name;
        headerTitle.Text = layout.Name;
        UpdateDeckDimensions();
        BuildDeckButtons();
    }

    void ApplyPanelColor()
    {
        if (DeckPanelLayout.TryParseButtonColor(layout.PanelColor, out var customColor))
            panelCard.Background = new SolidColorBrush(customColor);
        else
            panelCard.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        panelCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        if (dragArea != null)
        {
            dragArea.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
        }
    }

    void SetGlassOpacity(int opacityPercent)
    {
        glassOpacityPercent = Math.Clamp(opacityPercent, MinimumGlassOpacityPercent, MaximumGlassOpacityPercent);
        backdropTintAlpha = BackdropTintAlphaFor(glassOpacityPercent);
        panelCard.Opacity = glassOpacityPercent / 100.0;
    }

    internal static byte BackdropTintAlphaFor(int opacityPercent)
    {
        int clamped = Math.Clamp(opacityPercent, MinimumGlassOpacityPercent, MaximumGlassOpacityPercent);
        double progress = (clamped - MinimumGlassOpacityPercent) / (double)(MaximumGlassOpacityPercent - MinimumGlassOpacityPercent);
        return (byte)Math.Round(MinimumBackdropTintAlpha + (MaximumBackdropTintAlpha - MinimumBackdropTintAlpha) * progress);
    }

    static double DiffuseLayerOpacityFor(int opacityPercent)
    {
        int clamped = Math.Clamp(opacityPercent, MinimumGlassOpacityPercent, MaximumGlassOpacityPercent);
        return .38 + (clamped - MinimumGlassOpacityPercent) / (double)(MaximumGlassOpacityPercent - MinimumGlassOpacityPercent) * .26;
    }

    Border BuildHeader()
    {
        var border = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 0, 0, 0), Cursor = WpfCursors.SizeAll };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBackground");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var grip = new TextBlock { Text = "⋮⋮", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        grip.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var title = new TextBlock { Text = layout.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        headerTitle = title;
        title.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        var close = new Button
        {
            Width = 44,
            Height = 30,
            MinWidth = 44,
            MaxWidth = 44,
            MinHeight = 30,
            MaxHeight = 30,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            Focusable = false,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
            Content = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 1,1 L 9,9 M 9,1 L 1,9"),
                Width = 10,
                Height = 10,
                Stretch = Stretch.Uniform,
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            }
        };
        ((System.Windows.Shapes.Path)close.Content).SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "PrimaryText");
        close.Style = CloseButtonStyle();
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

    void ThemeChanged()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            ApplyPanelColor();
            BuildDeckButtons();
            CloseButton.Style = CloseButtonStyle();
        }));
    }

    Style GlassButtonStyle()
    {
        if (glassButtonStyle != null)
            return glassButtonStyle;
        glassButtonStyle = CreateGlassButtonStyle(new CornerRadius(12), WpfColor.FromArgb(24, 255, 255, 255));
        return glassButtonStyle;
    }
    Style CloseButtonStyle()
    {
        if (closeButtonStyle != null)
            return closeButtonStyle;
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.CursorProperty, WpfCursors.Hand));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, WpfBrushes.Transparent));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.BorderBrushProperty, WpfBrushes.Transparent));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.FocusVisualStyleProperty, null));
        var template = new ControlTemplate(typeof(Button));
        var hitSurface = new FrameworkElementFactory(typeof(Border));
        hitSurface.SetValue(Border.BackgroundProperty, WpfBrushes.Transparent);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(System.Windows.Controls.Control.HorizontalContentAlignmentProperty));
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, new TemplateBindingExtension(System.Windows.Controls.Control.VerticalContentAlignmentProperty));
        content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        hitSurface.AppendChild(content);
        template.VisualTree = hitSurface;
        template.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(UIElement.OpacityProperty, .72d) } });
        template.Triggers.Add(new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true, Setters = { new Setter(UIElement.OpacityProperty, .5d) } });
        style.Setters.Add(new Setter(System.Windows.Controls.Control.TemplateProperty, template));
        closeButtonStyle = style;
        return closeButtonStyle;
    }
    static Style CreateGlassButtonStyle(CornerRadius cornerRadius, WpfColor hoverHighlight)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.CursorProperty, WpfCursors.Hand));
        var template = new ControlTemplate(typeof(Button));
        var root = new FrameworkElementFactory(typeof(Grid)) { Name = "HoverRoot" };
        root.SetValue(UIElement.RenderTransformProperty, new TranslateTransform());
        var surface = new FrameworkElementFactory(typeof(Border)) { Name = "GlassSurface" };
        surface.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.Control.BackgroundProperty));
        surface.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(System.Windows.Controls.Control.BorderBrushProperty));
        surface.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(System.Windows.Controls.Control.BorderThicknessProperty));
        surface.SetValue(Border.CornerRadiusProperty, cornerRadius);
        surface.SetValue(Border.PaddingProperty, new TemplateBindingExtension(System.Windows.Controls.Control.PaddingProperty));
        root.AppendChild(surface);
        var highlight = new FrameworkElementFactory(typeof(Border)) { Name = "GlassHighlight" };
        highlight.SetValue(Border.BackgroundProperty, new SolidColorBrush(hoverHighlight));
        highlight.SetValue(Border.CornerRadiusProperty, cornerRadius);
        highlight.SetValue(UIElement.IsHitTestVisibleProperty, false);
        highlight.SetValue(UIElement.OpacityProperty, 0d);
        root.AppendChild(highlight);
        var underline = new FrameworkElementFactory(typeof(Border)) { Name = "HoverUnderline" };
        underline.SetValue(Border.BackgroundProperty, new SolidColorBrush(WpfColor.FromArgb(204, 242, 246, 248)));
        underline.SetValue(Border.CornerRadiusProperty, new CornerRadius(1));
        underline.SetValue(FrameworkElement.HeightProperty, 2d);
        underline.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 10, 3));
        underline.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
        underline.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
        underline.SetValue(FrameworkElement.RenderTransformOriginProperty, new Point(.5, .5));
        underline.SetValue(UIElement.RenderTransformProperty, new ScaleTransform(.35, 1));
        underline.SetValue(UIElement.IsHitTestVisibleProperty, false);
        underline.SetValue(UIElement.OpacityProperty, 0d);
        root.AppendChild(underline);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(System.Windows.Controls.Control.HorizontalContentAlignmentProperty));
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, new TemplateBindingExtension(System.Windows.Controls.Control.VerticalContentAlignmentProperty));
        content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        content.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
        content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        root.AppendChild(content);
        template.VisualTree = root;
        template.Triggers.Add(new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
            Setters =
            {
                new Setter(UIElement.OpacityProperty, .98d, "GlassSurface"),
                new Setter(UIElement.EffectProperty, new DropShadowEffect { Color = DeckAccent, BlurRadius = 11, ShadowDepth = 0, Opacity = .24 }, "HoverRoot")
            }
        });
        template.Triggers.Add(new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true, Setters = { new Setter(UIElement.OpacityProperty, .84d, "GlassSurface") } });
        template.Triggers.Add(new Trigger { Property = UIElement.IsEnabledProperty, Value = false, Setters = { new Setter(UIElement.OpacityProperty, .42d) } });
        AddDeckHoverTransition(template, UIElement.MouseEnterEvent, true);
        AddDeckHoverTransition(template, UIElement.MouseLeaveEvent, false);
        style.Setters.Add(new Setter(System.Windows.Controls.Control.TemplateProperty, template));
        return style;
    }
    static void AddDeckHoverTransition(ControlTemplate template, RoutedEvent routedEvent, bool entering)
    {
        var duration = TimeSpan.FromMilliseconds(entering ? 150 : 180);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard();
        AddDeckHoverAnimation(storyboard, "GlassHighlight", new PropertyPath(UIElement.OpacityProperty), entering ? 1 : 0, duration, easing);
        AddDeckHoverAnimation(storyboard, "HoverUnderline", new PropertyPath(UIElement.OpacityProperty), entering ? .92 : 0, duration, easing);
        AddDeckHoverAnimation(storyboard, "HoverUnderline", new PropertyPath("(0).(1)", UIElement.RenderTransformProperty, ScaleTransform.ScaleXProperty), entering ? 1 : .35, duration, easing);
        AddDeckHoverAnimation(storyboard, "HoverRoot", new PropertyPath("(0).(1)", UIElement.RenderTransformProperty, TranslateTransform.YProperty), entering ? -1 : 0, duration, easing);
        var trigger = new EventTrigger(routedEvent);
        trigger.Actions.Add(new BeginStoryboard { Storyboard = storyboard });
        template.Triggers.Add(trigger);
    }
    static void AddDeckHoverAnimation(Storyboard storyboard, string targetName, PropertyPath property, double value, TimeSpan duration, IEasingFunction easing)
    {
        var animation = new DoubleAnimation(value, duration)
        {
            EasingFunction = easing
        };
        Storyboard.SetTargetName(animation, targetName);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    static System.Windows.Media.Brush GlassSurfaceBrush(WpfColor color, byte opacity)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(WithAlpha(Mix(color, Colors.White, .085), opacity), 0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, (byte)Math.Max(0, opacity - 12)), .46));
        brush.GradientStops.Add(new GradientStop(WithAlpha(Mix(color, Colors.Black, .09), (byte)Math.Max(0, opacity - 22)), 1));
        brush.Freeze();
        return brush;
    }

    UIElement BuildAcrylicDiffuseLayer()
    {
        var layer = new Grid { IsHitTestVisible = false, ClipToBounds = true, Opacity = DiffuseLayerOpacityFor(glassOpacityPercent) };
        acrylicDiffuseLayer = layer;
        var topGlaze = new RadialGradientBrush
        {
            Center = new Point(.18, -.12),
            GradientOrigin = new Point(.18, -.12),
            RadiusX = 1.05,
            RadiusY = .86
        };
        topGlaze.GradientStops.Add(new GradientStop(WpfColor.FromArgb(23, 255, 255, 255), 0));
        topGlaze.GradientStops.Add(new GradientStop(WpfColor.FromArgb(8, 194, 211, 218), .46));
        topGlaze.GradientStops.Add(new GradientStop(WpfColor.FromArgb(0, 0, 0, 0), .90));
        layer.Children.Add(new Border { Background = topGlaze, Opacity = .9 });

        var lowerDiffuse = new RadialGradientBrush
        {
            Center = new Point(.78, .86),
            GradientOrigin = new Point(.78, .86),
            RadiusX = .72,
            RadiusY = .66
        };
        lowerDiffuse.GradientStops.Add(new GradientStop(WpfColor.FromArgb(12, 104, 136, 144), 0));
        lowerDiffuse.GradientStops.Add(new GradientStop(WpfColor.FromArgb(0, 0, 0, 0), 1));
        layer.Children.Add(new Border { Background = lowerDiffuse, Opacity = .75 });
        return layer;
    }

    System.Windows.Media.Brush GlassKeyBrush(bool populated, WpfColor? tint = null)
    {
        WpfColor baseColor = populated ? Mix(panelTone, Colors.White, .13) : Mix(panelTone, Colors.White, .055);
        if (tint is WpfColor requestedTint)
            baseColor = Mix(baseColor, requestedTint, .08);
        return GlassSurfaceBrush(baseColor, populated ? (byte)210 : (byte)126);
    }
    static System.Windows.Media.Brush GlassKeyBorderBrush(bool populated) => new SolidColorBrush(WpfColor.FromArgb(populated ? (byte)42 : (byte)24, 255, 255, 255));
    static System.Windows.Media.Brush GlassFrameBrush() => new SolidColorBrush(WpfColor.FromArgb(22, 255, 255, 255));
    static WpfColor DeckSurfaceTone(WpfColor requested) => Mix(AcrylicCharcoal, requested, .14);
    static string DisplayLayoutName(string name)
    {
        const string suffix = "Deck";
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^suffix.Length].TrimEnd()
            : name;
    }
    static bool HasDeckContent(Mapping? mapping) => mapping != null &&
        (MainWindow.MappingInterceptsInput(mapping) || DeckPanelLayout.HasRegisteredFile(mapping) || !string.IsNullOrWhiteSpace(mapping.Description));
    static WpfColor WithAlpha(WpfColor color, byte alpha) => WpfColor.FromArgb(alpha, color.R, color.G, color.B);
    static WpfColor Mix(WpfColor first, WpfColor second, double amount) => WpfColor.FromArgb(255, (byte)(first.R + (second.R - first.R) * amount), (byte)(first.G + (second.G - first.G) * amount), (byte)(first.B + (second.B - first.B) * amount));

    void DeckButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int slot })
            return;
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        if (mapping == null || mapping.Kind == ActionKind.Gesture)
            return;
        if (MainWindow.MappingInterceptsInput(mapping))
            execute?.Invoke(mapping);
        else if (DeckPanelLayout.IsAudioFile(mapping.DeckFilePath))
            PlayHoverAudio(mapping.DeckFilePath, (Button)sender);
    }
    System.Windows.Controls.ContextMenu CreateDeckButtonContextMenu(int slot)
    {
        var menu = new System.Windows.Controls.ContextMenu { MinWidth = 242 };
        var rename = CreateDeckContextMenuItem("\uE70F", "名前の変更...", "");
        rename.Click += (_, _) => RenameDeckButton(slot);
        var copy = CreateDeckContextMenuItem("\uE8C8", "コピー", "");
        copy.Click += (_, _) => CopyDeckFile(slot);
        var paste = CreateDeckContextMenuItem("\uE77F", "貼り付け", "");
        paste.Click += (_, _) => PasteDeckFile(slot);
        var reveal = CreateDeckContextMenuItem("\uE838", "ファイルの場所を開く", "");
        reveal.Click += (_, _) => RevealDeckFile(slot);
        var color = CreateDeckContextMenuItem("\uE790", "色を変更...", "");
        color.Click += (_, _) => ChooseDeckButtonColor(slot);
        var resetColor = CreateDeckContextMenuItem("\uE777", "色を標準に戻す", "");
        resetColor.Click += (_, _) => ResetDeckButtonColor(slot);
        var delete = CreateDeckContextMenuItem("\uE74D", "削除", "Del", true);
        delete.Click += (_, _) => DeleteDeckButton(slot);
        menu.Items.Add(rename);
        menu.Items.Add(new Separator());
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(reveal);
        menu.Items.Add(new Separator());
        menu.Items.Add(color);
        menu.Items.Add(resetColor);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        menu.Opened += (_, _) =>
        {
            var mapping = DeckPanelLayout.FindMapping(layout, slot);
            copy.IsEnabled = DeckPanelLayout.IsAvailableFile(mapping);
            paste.IsEnabled = ClipboardFile() != null;
            reveal.IsEnabled = DeckPanelLayout.IsAvailableFile(mapping);
            resetColor.IsEnabled = DeckPanelLayout.TryGetButtonColor(mapping, out _);
            delete.IsEnabled = mapping != null;
        };
        return menu;
    }
    static System.Windows.Controls.MenuItem CreateDeckContextMenuItem(string icon, string label, string shortcut, bool danger = false)
    {
        var header = new Grid { Width = 208, Height = 30 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var color = danger ? ThemeService.Brush("DangerBrush") : ThemeService.Brush("AccentBrush");
        header.Children.Add(new TextBlock { Text = icon, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 15, Foreground = color, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
        var text = new TextBlock { Text = label, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = danger ? ThemeService.Brush("DangerBrush") : ThemeService.Brush("PrimaryText") };
        Grid.SetColumn(text, 2);
        header.Children.Add(text);
        if (shortcut.Length > 0)
        {
            var key = new TextBlock { Text = shortcut, FontSize = 10.5, Foreground = ThemeService.Brush("SecondaryText"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(key, 3);
            header.Children.Add(key);
        }
        return new System.Windows.Controls.MenuItem { Header = header, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 1, 0, 1), Foreground = danger ? ThemeService.Brush("DangerBrush") : ThemeService.Brush("PrimaryText") };
    }
    Mapping GetOrCreateDeckMapping(int slot)
    {
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        if (mapping != null)
            return mapping;
        mapping = new Mapping { Input = DeckPanelLayout.InputName(slot), Layer = DeckPanelLayout.Layer };
        layout.Mappings.Add(mapping);
        return mapping;
    }
    static bool HasDeckButtonContent(Mapping mapping)
        => MainWindow.MappingHasConfiguredAction(mapping) || !string.IsNullOrWhiteSpace(mapping.Description) || !string.IsNullOrWhiteSpace(mapping.DeckColor) || DeckPanelLayout.HasRegisteredFile(mapping);
    void RenameDeckButton(int slot)
    {
        var existing = DeckPanelLayout.FindMapping(layout, slot);
        string? name = PromptDeckButtonName(existing?.Description ?? "");
        if (name == null || (existing == null && name.Length == 0))
            return;
        var mapping = existing ?? GetOrCreateDeckMapping(slot);
        mapping.Description = name;
        if (!HasDeckButtonContent(mapping))
            layout.Mappings.Remove(mapping);
        OverlayService.NotifyDeckLayoutChanged();
    }
    string? PromptDeckButtonName(string initial)
    {
        var dialog = new Window { Title = "Deckボタン名", Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Width = 420, Height = 196, ResizeMode = ResizeMode.NoResize, Background = ThemeService.Brush("SurfaceBackground"), Foreground = ThemeService.Brush("PrimaryText"), ShowInTaskbar = false };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "ボタンの下に表示する名前", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var box = new System.Windows.Controls.TextBox { Text = initial, FontSize = 15, Height = 40, Padding = new Thickness(10, 7, 10, 7), Background = ThemeService.Brush("InputBackground"), Foreground = ThemeService.Brush("PrimaryText"), BorderBrush = ThemeService.Brush("BorderBrush") };
        panel.Children.Add(box);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "キャンセル", Width = 98, Height = 36, Margin = new Thickness(6, 0, 0, 0), Style = (Style)WpfApplication.Current.FindResource("AppButtonStyle") };
        var ok = new Button { Content = "変更", Width = 98, Height = 36, Margin = new Thickness(6, 0, 0, 0), Style = (Style)WpfApplication.Current.FindResource("AccentAppButtonStyle"), IsDefault = true };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        ok.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }
    static string? ClipboardFile()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsFileDropList())
                return null;
            foreach (object? value in System.Windows.Clipboard.GetFileDropList())
            if (value is string file && File.Exists(file))
                return file;
        }
        catch (COMException) { }
        return null;
    }
    void CopyDeckFile(int slot)
    {
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        if (!DeckPanelLayout.IsAvailableFile(mapping))
            return;
        try
        {
            var data = new System.Windows.DataObject();
            data.SetData(System.Windows.DataFormats.FileDrop, new[] { mapping!.DeckFilePath });
            System.Windows.Clipboard.SetDataObject(data, true);
        }
        catch (COMException) { }
    }
    void PasteDeckFile(int slot)
    {
        string? file = ClipboardFile();
        if (file == null)
            return;
        GetOrCreateDeckMapping(slot).DeckFilePath = Path.GetFullPath(file);
        OverlayService.NotifyDeckLayoutChanged();
    }
    void RevealDeckFile(int slot)
    {
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        if (!DeckPanelLayout.IsAvailableFile(mapping))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{mapping!.DeckFilePath}\"") { UseShellExecute = true });
        }
        catch { }
    }
    void ChooseDeckButtonColor(int slot)
    {
        var existing = DeckPanelLayout.FindMapping(layout, slot);
        var initial = DeckPanelLayout.TryGetButtonColor(existing, out var current) ? current : ThemeService.Color("AccentBrush");
        var picker = new ThemeColorPickerWindow(initial) { Owner = this, Topmost = true };
        if (picker.ShowDialog() != true)
            return;
        var selectedColor = picker.SelectedColor;
        GetOrCreateDeckMapping(slot).DeckColor = $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";
        RefreshDeckColorSlot(slot);
    }
    void ResetDeckButtonColor(int slot)
    {
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        if (mapping == null)
            return;
        mapping.DeckColor = "";
        if (!HasDeckButtonContent(mapping))
            layout.Mappings.Remove(mapping);
        RefreshDeckColorSlot(slot);
    }
    void RefreshDeckColorSlot(int slot)
    {
        RefreshDeckSlots(slot, slot);
        OverlayService.NotifyDeckLayoutChanged(false, layout.Id, slot, slot);
    }
    void DeleteDeckButton(int slot)
    {
        string input = DeckPanelLayout.InputName(slot);
        if (layout.Mappings.RemoveAll(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase)) > 0)
            OverlayService.NotifyDeckLayoutChanged();
    }
    void ConfigureHoverPreview(Button button, Mapping? mapping)
    {
        if (mapping == null)
            return;
        if (DeckPanelLayout.IsVideoFile(mapping.DeckFilePath) && File.Exists(mapping.DeckFilePath))
        {
            videoPreviews.Add(new DeckVideoPreviewPopup(button, mapping.DeckFilePath, this));
            return;
        }
        object? content = CreateHoverContent(mapping, button);
        if (content != null)
        {
            if (content is string text)
            {
                var label = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 340,
                    Foreground = ThemeService.Brush("PrimaryText")
                };
                var tooltip = new System.Windows.Controls.ToolTip
                {
                    Content = label,
                    Padding = new Thickness(10, 8, 10, 8),
                    BorderThickness = new Thickness(1),
                    Placement = PlacementMode.Mouse,
                    Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = .5, Color = Colors.Black }
                };
                tooltip.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "CardBackground");
                tooltip.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "AccentBrush");
                ConfigureOutsideDeckPreview(tooltip, button);
                button.ToolTip = tooltip;
            }
            else
            {
                var tooltip = new System.Windows.Controls.ToolTip
                {
                    Content = content,
                    Background = WpfBrushes.Transparent,
                    BorderBrush = WpfBrushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    Placement = PlacementMode.Custom
                };
                ConfigureOutsideDeckPreview(tooltip, button);
                button.ToolTip = tooltip;
            }
            ToolTipService.SetInitialShowDelay(button, 220);
            ToolTipService.SetShowDuration(button, 20000);
        }
        if (DeckPanelLayout.IsAudioFile(mapping.DeckFilePath))
        {
            string audioPath = mapping.DeckFilePath;
            button.MouseEnter += (_, _) => ScheduleHoverAudio(button, audioPath);
            button.MouseLeave += (_, _) => StopHoverAudioFor(button);
            button.Unloaded += (_, _) => StopHoverAudioFor(button);
        }
    }
    void ConfigureOutsideDeckPreview(System.Windows.Controls.ToolTip tooltip, Button source)
    {
        tooltip.PlacementTarget = source;
        tooltip.Placement = PlacementMode.Custom;
        tooltip.CustomPopupPlacementCallback = (popupSize, targetSize, offset) => OutsideDeckPlacements(source, popupSize, targetSize);
    }
    CustomPopupPlacement[] OutsideDeckPlacements(FrameworkElement source, System.Windows.Size popupSize, System.Windows.Size targetSize)
    {
        double gap = targetSize.Width;
        double y = (targetSize.Height - popupSize.Height) / 2;
        var right = new CustomPopupPlacement(new Point(targetSize.Width + gap, y), PopupPrimaryAxis.Vertical);
        var left = new CustomPopupPlacement(new Point(-popupSize.Width - gap, y), PopupPrimaryAxis.Vertical);
        Point sourceInDeck = source.TranslatePoint(new Point(0, 0), this);
        double deckWidth = ActualWidth > 0 ? ActualWidth : Width;
        return sourceInDeck.X + targetSize.Width / 2 > deckWidth / 2 ? [left, right] : [right, left];
    }
    object? CreateHoverContent(Mapping mapping, FrameworkElement source)
    {
        if (DeckPanelLayout.IsImageFile(mapping.DeckFilePath))
        {
            var image = DeckPanelLayout.LoadFileThumbnail(mapping.DeckFilePath, 360);
            if (image != null)
            {
                var preview = new WpfImage { Source = image, Width = 240, Height = 180, Stretch = Stretch.Uniform };
                return CreateHoverCard(preview);
            }
        }
        if (DeckPanelLayout.HasRegisteredFile(mapping))
            return DeckPanelLayout.FileDisplayName(mapping) + (File.Exists(mapping.DeckFilePath) ? "" : "\nファイルが見つかりません");
        return MainWindow.AssignmentToolTipText(mapping);
    }
    void ClearVideoPreviews()
    {
        foreach (var preview in videoPreviews)
            preview.Dispose();
        videoPreviews.Clear();
    }
    FrameworkElement CreateHoverCard(FrameworkElement content)
    {
        var inner = new Border { Padding = new Thickness(4), CornerRadius = new CornerRadius(8), Child = content, SnapsToDevicePixels = true, ClipToBounds = true };
        inner.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        inner.SizeChanged += (_, _) => inner.Clip = new RectangleGeometry(new Rect(inner.RenderSize), 8, 8);
        var border = new Border { Padding = new Thickness(1), CornerRadius = new CornerRadius(10), Child = inner, SnapsToDevicePixels = true, Opacity = .98, Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 7, Opacity = .62, Color = Colors.Black } };
        border.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        border.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        border.BorderThickness = new Thickness(1);
        return border;
    }
    void ScheduleHoverAudio(Button source, string path)
    {
        CancelPendingHoverAudio();
        StopHoverAudio();
        if (!hoverPreviewsEnabled || !DeckPanelLayout.IsAudioFile(path) || !File.Exists(path))
            return;
        pendingHoverAudioSource = source;
        pendingHoverAudioPath = path;
        hoverAudioStartTimer.Start();
    }
    void HoverAudioStartTimerTick(object? sender, EventArgs e)
    {
        Button? source = pendingHoverAudioSource;
        string path = pendingHoverAudioPath;
        CancelPendingHoverAudio();
        if (source?.IsMouseOver == true)
            PlayHoverAudio(path, source);
    }
    void CancelPendingHoverAudio()
    {
        hoverAudioStartTimer.Stop();
        pendingHoverAudioSource = null;
        pendingHoverAudioPath = "";
    }
    void StopHoverAudioFor(Button source)
    {
        if (ReferenceEquals(pendingHoverAudioSource, source))
            CancelPendingHoverAudio();
        if (ReferenceEquals(hoverAudioSource, source))
            StopHoverAudio();
    }
    void PlayHoverAudio(string path, Button source)
    {
        if (!hoverPreviewsEnabled || !DeckPanelLayout.IsAudioFile(path) || !File.Exists(path))
            return;
        try
        {
            StopHoverAudio();
            var player = new MediaPlayer();
            hoverAudioPlayer = player;
            hoverAudioSource = source;
            player.MediaEnded += (_, _) => { if (ReferenceEquals(hoverAudioPlayer, player)) StopHoverAudio(); };
            player.MediaFailed += (_, _) => { if (ReferenceEquals(hoverAudioPlayer, player)) StopHoverAudio(); };
            player.Open(new Uri(path, UriKind.Absolute));
            player.Volume = .8;
            player.Play();
        }
        catch { StopHoverAudio(); }
    }
    void StopHoverAudio()
    {
        var player = hoverAudioPlayer;
        hoverAudioPlayer = null;
        hoverAudioSource = null;
        if (player == null)
            return;
        try
        {
            player.Stop();
            player.Close();
        }
        catch { }
    }
    void DeckButtonDragStarted(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: int })
            return;
        fileDragButton = (Button)sender;
        fileDragStart = e.GetPosition(fileDragButton);
    }
    void DeckButtonDragMoved(object sender, MouseEventArgs e)
    {
        if (sender is not Button button || !ReferenceEquals(fileDragButton, button) || e.LeftButton != MouseButtonState.Pressed || button.Tag is not int slot)
            return;
        Point current = e.GetPosition(button);
        if (Math.Abs(current.X - fileDragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(current.Y - fileDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        if (!deckReorderDragging)
        {
            CancelPendingHoverAudio();
            StopHoverAudio();
            deckReorderDragging = true;
            deckReorderSourceSlot = slot;
            button.Opacity = .72;
            button.CaptureMouse();
            StartReorderDragPreview(button, mapping);
        }
        UpdateDeckReorderDrag();
        if (DeckPanelLayout.IsAvailableFile(mapping) && IsCursorOutsideDeckWindow())
            StartExternalFileDrag(button, mapping!);
        e.Handled = true;
    }
    bool IsCursorOutsideDeckWindow()
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        Point point = PointFromScreen(new Point(cursor.X, cursor.Y));
        return point.X < 0 || point.Y < 0 || point.X > ActualWidth || point.Y > ActualHeight;
    }
    void StartExternalFileDrag(Button button, Mapping mapping)
    {
        CancelDeckReorder();
        fileDragButton = null;
        try
        {
            var data = new System.Windows.DataObject();
            data.SetData(System.Windows.DataFormats.FileDrop, new[] { mapping.DeckFilePath });
            data.SetData(DeckPanelLayout.FileSourceDragFormat, true);
            // External targets already provide their own drag affordance.  A
            // topmost Deck preview can outlive a drop in Chromium-based apps,
            // so reserve the custom preview exclusively for Deck reordering.
            try
            {
                internalDeckDragActive = true;
                System.Windows.DragDrop.DoDragDrop(button, data, System.Windows.DragDropEffects.Move | System.Windows.DragDropEffects.Copy);
            }
            finally { internalDeckDragActive = false; }
        }
        catch (COMException) { }
    }
    void DeckButtonDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not Button)
            return;
        if (e.Data.GetDataPresent(DeckPanelLayout.SlotDragFormat))
        {
            // Normal Deck mode deliberately has no internal drop operation.
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (!DeckPanelLayout.IsInternalFileDrag(e.Data) && DeckPanelLayout.GetDroppedFile(e.Data) != null)
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            e.Handled = true;
        }
    }
    void DeckButtonDropped(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not Button { Tag: int target } || target < 1)
            return;
        if (e.Data.GetDataPresent(DeckPanelLayout.SlotDragFormat))
        {
            e.Handled = true;
            return;
        }
        if (!DeckPanelLayout.IsInternalFileDrag(e.Data) && DeckPanelLayout.GetDroppedFile(e.Data) is string file)
        {
            var mapping = DeckPanelLayout.FindMapping(layout, target);
            if (mapping == null)
            {
                mapping = new Mapping { Input = DeckPanelLayout.InputName(target), Layer = DeckPanelLayout.Layer };
                layout.Mappings.Add(mapping);
            }
            mapping.DeckFilePath = Path.GetFullPath(file);
            OverlayService.NotifyDeckLayoutChanged();
            e.Handled = true;
        }
    }
    void StartDragPreview(Mapping mapping)
    {
        if (!DeckPanelLayout.HasRegisteredFile(mapping))
            return;
        try
        {
            var image = DeckPanelLayout.LoadFileThumbnail(mapping.DeckFilePath, 128);
            FrameworkElement preview = image != null
                ? new WpfImage { Source = image, Stretch = Stretch.Uniform }
                : DeckPanelLayout.CreateFileIcon(mapping.DeckFilePath, 42);
            dragPreview = new DeckDragPreviewWindow(preview);
            dragPreview.Show();
            UpdateDragPreview();
        }
        catch { dragPreview = null; }
    }
    void UpdateDragPreview()
    {
        if (dragPreview == null)
            return;
        var screen = System.Windows.Forms.Cursor.Position;
        dragPreview.MoveToPhysical(screen.X, screen.Y);
    }
    void DeckDragGiveFeedback(object sender, System.Windows.GiveFeedbackEventArgs e)
    {
        UpdateDragPreview();
        e.UseDefaultCursors = false;
        e.Handled = true;
    }
    void StopDragPreview()
    {
        if (dragPreview == null)
            return;
        try
        {
            dragPreview.Close();
        }
        catch { }
        dragPreview = null;
        Mouse.OverrideCursor = null;
    }
    void DeckButtonDragEnded(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button && ReferenceEquals(fileDragButton, button) && deckReorderDragging)
        {
            try
            {
                Point point = button.PointToScreen(e.GetPosition(button));
                int targetSlot = DeckSlotAt(new NativeDropPoint { X = (int)Math.Round(point.X), Y = (int)Math.Round(point.Y) });
                if (targetSlot > 0 && targetSlot != deckReorderSourceSlot)
                {
                    DeckPanelLayout.SwapSlots(layout, deckReorderSourceSlot, targetSlot);
                    RefreshDeckSlots(deckReorderSourceSlot, targetSlot);
                    OverlayService.NotifyDeckLayoutChanged(false, layout.Id, deckReorderSourceSlot, targetSlot);
                }
            }
            finally { CancelDeckReorder(); }
        }
        fileDragButton = null;
    }
    void CancelDeckReorder()
    {
        fileDragButton?.Opacity = 1;
        if (Mouse.Captured is Button captured && ReferenceEquals(captured, fileDragButton))
            captured.ReleaseMouseCapture();
        deckReorderDragging = false;
        deckReorderSourceSlot = 0;
        ClearDeckReorderTarget();
        StopDragPreview();
    }
    void UpdateDeckReorderDrag()
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        int targetSlot = DeckSlotAt(new NativeDropPoint { X = cursor.X, Y = cursor.Y });
        SetDeckReorderTarget(targetSlot);
        UpdateDragPreview();
    }
    void SetDeckReorderTarget(int slot)
    {
        Button? target = slot > 0 && slot != deckReorderSourceSlot ? deckButtons.FirstOrDefault(x => x.Tag is int candidate && candidate == slot) : null;
        if (ReferenceEquals(target, deckReorderTargetButton))
            return;
        ClearDeckReorderTarget();
        if (target == null)
            return;
        target.Effect = new DropShadowEffect
        {
            Color = DeckAccent,
            BlurRadius = 14,
            ShadowDepth = 0,
            Opacity = .58
        };
        deckReorderTargetButton = target;
    }
    void ClearDeckReorderTarget()
    {
        if (deckReorderTargetButton == null)
            return;
        deckReorderTargetButton.ClearValue(UIElement.EffectProperty);
        deckReorderTargetButton = null;
    }
    void StartReorderDragPreview(Button source, Mapping? mapping)
    {
        try
        {
            FrameworkElement content = DeckPanelLayout.CreateButtonContent(DeckPanelLayout.InputName((int)source.Tag), mapping);
            if (content is TextBlock text)
                text.Foreground = source.Foreground;
            var face = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(3),
                Background = source.Background,
                BorderBrush = source.BorderBrush,
                BorderThickness = source.BorderThickness,
                Child = content
            };
            dragPreview = new DeckDragPreviewWindow(face);
            dragPreview.Show();
        }
        catch { dragPreview = null; }
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
        Point delta = InputPanelOverlayWindow.PhysicalDragDeltaToDip(current - dragStart, dragDpi);
        Left = windowStartLeft + delta.X;
        Top = windowStartTop + delta.Y;
        positionDirty = true;
        e.Handled = true;
    }
    void DragEnded(object sender, MouseButtonEventArgs e)
    {
        if (!dragging)
            return;
        dragging = false;
        dragArea.ReleaseMouseCapture();
        PersistPosition();
        e.Handled = true;
    }
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
    internal static Point InitialPosition(AppConfig config, double width, double height)
    {
        double defaultLeft = Math.Max(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Right - width - 24);
        double defaultTop = Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - height - 24);
        if (config.DeckPanelLeft is not double savedLeft || config.DeckPanelTop is not double savedTop || !double.IsFinite(savedLeft) || !double.IsFinite(savedTop))
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
        long updated = style | WsExToolWindow | WsExNoActivate;
        SetWindowLongPtr(helper.Handle, GwlExStyle, new IntPtr(updated));
        UsesNoActivateStyle = (GetWindowLongPtr(helper.Handle, GwlExStyle).ToInt64() & WsExNoActivate) != 0;
        HwndSource.FromHwnd(helper.Handle)?.AddHook(WindowMessageHook);
        EnableShellFileDrop(helper.Handle);
        ApplyRoundedPanelClip();
    }

    void ApplyRoundedPanelClip()
    {
        if (panelCard.ActualWidth <= 0 || panelCard.ActualHeight <= 0)
            return;
        panelCard.Clip = new RectangleGeometry(
            new Rect(0, 0, panelCard.ActualWidth, panelCard.ActualHeight),
            PanelCornerRadius,
            PanelCornerRadius);
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
        if (region == IntPtr.Zero)
            return;
        if (SetWindowRgn(hwnd, region, true) == 0)
            DeleteObject(region);
    }

    void ApplyAcrylicBackdrop()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource)
        {
            ApplyBackdropSurface(DeckBackdropMode.Pending);
            return;
        }
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            ApplyBackdropSurface(DeckBackdropMode.Pending);
            return;
        }

        if (SystemParameters.HighContrast)
        {
            SetBackdropMode(DeckBackdropMode.SolidFallback, "high contrast is enabled");
            return;
        }

        int compositionResult = DwmIsCompositionEnabled(out bool compositionEnabled);
        LogBackdrop($"DwmIsCompositionEnabled result={FormatHResult(compositionResult)}; enabled={compositionEnabled}");
        if (compositionResult != 0 || !compositionEnabled)
        {
            SetBackdropMode(DeckBackdropMode.SolidFallback, "DWM composition is unavailable");
            return;
        }

        int cornerPreference = DwmWindowCornerPreferenceRound;
        int cornerResult = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));
        LogBackdrop($"DWMWA_WINDOW_CORNER_PREFERENCE result={FormatHResult(cornerResult)}");

        int immersiveDarkMode = 1;
        int darkModeResult = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref immersiveDarkMode, sizeof(int));
        LogBackdrop($"DWMWA_USE_IMMERSIVE_DARK_MODE result={FormatHResult(darkModeResult)}");

        int systemBackdrop = DwmsbtNone;
        int backdropResult = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref systemBackdrop, sizeof(int));
        LogBackdrop($"DWMWA_SYSTEMBACKDROP_TYPE=DWMSBT_NONE result={FormatHResult(backdropResult)}");
        if (backdropResult != 0)
        {
            SetBackdropMode(DeckBackdropMode.SolidFallback, "System Backdrop could not be disabled; Acrylic-only mode was not applied");
            return;
        }

        ApplyAccentAcrylicFallback(hwnd);
    }

    void ApplyAccentAcrylicFallback(IntPtr hwnd)
    {
        var policy = new AccentPolicy
        {
            AccentState = AccentEnableAcrylicBlurBehind,
            GradientColor = ToAbgr(WithAlpha(panelTone, backdropTintAlpha))
        };
        IntPtr policyPointer = IntPtr.Zero;
        try
        {
            int size = Marshal.SizeOf<AccentPolicy>();
            policyPointer = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(policy, policyPointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = policyPointer,
                SizeOfData = size
            };
            bool applied = SetWindowCompositionAttribute(hwnd, ref data);
            int error = applied ? 0 : Marshal.GetLastWin32Error();
            LogBackdrop($"SetWindowCompositionAttribute ACCENT_ENABLE_ACRYLICBLURBEHIND applied={applied}; win32Error={error}");
            if (applied)
            {
                SetBackdropMode(DeckBackdropMode.AccentAcrylicOnly, "System Backdrop disabled; legacy Acrylic applied alone");
            }
            else
            {
                SetBackdropMode(DeckBackdropMode.SolidFallback, "legacy acrylic fallback failed");
            }
        }
        catch (Exception exception)
        {
            LogBackdrop($"SetWindowCompositionAttribute exception={exception.GetType().Name}; message={exception.Message}");
            SetBackdropMode(DeckBackdropMode.SolidFallback, "legacy acrylic fallback threw an exception");
        }
        finally
        {
            if (policyPointer != IntPtr.Zero)
                Marshal.FreeHGlobal(policyPointer);
        }
    }

    void SetBackdropMode(DeckBackdropMode mode, string reason)
    {
        backdropMode = mode;
        ApplyBackdropSurface(mode);
        LogBackdrop($"BackdropMode={mode}; reason={reason}; glassOpacityPercent={glassOpacityPercent}; panelTintAlpha={(mode is DeckBackdropMode.SystemBackdrop or DeckBackdropMode.AccentAcrylicOnly ? backdropTintAlpha : SolidSurfaceTintAlpha)}; panelTone=#{panelTone.R:X2}{panelTone.G:X2}{panelTone.B:X2}");
    }

    void ApplyBackdropSurface(DeckBackdropMode mode)
    {
        if (panelCard == null)
            return;
        byte alpha = mode is DeckBackdropMode.SystemBackdrop or DeckBackdropMode.AccentAcrylicOnly
            ? backdropTintAlpha
            : SolidSurfaceTintAlpha;
        panelCard.Background = GlassSurfaceBrush(panelTone, alpha);
    }

    static string FormatHResult(int result) => $"0x{unchecked((uint)result):X8}";

    static void LogBackdrop(string message)
    {
        try
        {
#if !PRODUCTION_PUBLISH
            string path = VerificationPaths.GetFile("deck-backdrop-diagnostics.log");
#else
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RELYR");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "deck-backdrop-diagnostics.log");
#endif
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Deck backdrop diagnostics failed: {exception.Message}");
        }
    }

    static int ToAbgr(WpfColor color) => unchecked((int)((uint)color.A << 24 | (uint)color.B << 16 | (uint)color.G << 8 | color.R));
    internal bool IsReorderMode => true;
    internal bool HasNativeDropTarget(NativeDropPoint point) => DeckSlotAt(point) > 0;
    internal void AcceptNativeDrop(NativeDropPoint point, string? filePath, string? sourceSlot)
    {
        int targetSlot = DeckSlotAt(point);
        if (targetSlot <= 0)
            return;
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var mapping = DeckPanelLayout.FindMapping(layout, targetSlot);
            if (mapping == null)
            {
                mapping = new Mapping { Input = DeckPanelLayout.InputName(targetSlot), Layer = DeckPanelLayout.Layer };
                layout.Mappings.Add(mapping);
            }
            mapping.DeckFilePath = Path.GetFullPath(filePath);
            OverlayService.NotifyDeckLayoutChanged();
            return;
        }
        if (DeckPanelLayout.IsInputName(sourceSlot))
        {
            int source = DeckPanelLayout.SlotNumber(sourceSlot!);
            if (source != targetSlot)
            {
                DeckPanelLayout.SwapSlots(layout, source, targetSlot);
                RefreshDeckSlots(source, targetSlot);
                OverlayService.NotifyDeckLayoutChanged(false, layout.Id, source, targetSlot);
            }
        }
    }
    void EnableShellFileDrop(IntPtr hwnd)
    {
        // Explorer normally runs at medium integrity while RELYR can run elevated.
        // Permit only the documented file-drop messages on this overlay window.
        bool dropFilter = ChangeWindowMessageFilterEx(hwnd, WmDropFiles, MsgFiltAllow, IntPtr.Zero);
        int dropFilterError = dropFilter ? 0 : Marshal.GetLastWin32Error();
        bool copyDataFilter = ChangeWindowMessageFilterEx(hwnd, WmCopyData, MsgFiltAllow, IntPtr.Zero);
        int copyDataFilterError = copyDataFilter ? 0 : Marshal.GetLastWin32Error();
        bool copyGlobalDataFilter = ChangeWindowMessageFilterEx(hwnd, WmCopyGlobalData, MsgFiltAllow, IntPtr.Zero);
        int copyGlobalDataFilterError = copyGlobalDataFilter ? 0 : Marshal.GetLastWin32Error();
        DragAcceptFiles(hwnd, true);
        shellFileDropEnabled = true;
        AppendDropDiagnostic($"EnableShellFileDrop hwnd={hwnd}, WM_DROPFILES={dropFilter}/{dropFilterError}, WM_COPYDATA={copyDataFilter}/{copyDataFilterError}, WM_COPYGLOBALDATA={copyGlobalDataFilter}/{copyGlobalDataFilterError}");
    }
    void StopShellFileDrop()
    {
        if (!shellFileDropEnabled)
            return;
        try
        {
            DragAcceptFiles(new WindowInteropHelper(this).Handle, false);
        }
        catch { }
        shellFileDropEnabled = false;
    }
    void HandleShellFileDrop(IntPtr hwnd, IntPtr dropHandle)
    {
        try
        {
            if (internalDeckDragActive)
                return;
            if (!DragQueryPoint(dropHandle, out var point) || !ClientToScreen(hwnd, ref point))
                return;
            int targetSlot = DeckSlotAt(point);
            uint count = DragQueryFile(dropHandle, 0xffffffff, null, 0);
            AppendDropDiagnostic($"HandleShellFileDrop hwnd={hwnd}, point={point.X},{point.Y}, targetSlot={targetSlot}, fileCount={count}");
            if (targetSlot <= 0)
                return;
            for (uint index = 0; index < count; index++)
            {
                uint length = DragQueryFile(dropHandle, index, null, 0);
                if (length == 0)
                    continue;
                var buffer = new StringBuilder((int)length + 1);
                DragQueryFile(dropHandle, index, buffer, buffer.Capacity);
                string path = buffer.ToString();
                if (!File.Exists(path))
                    continue;
                var mapping = DeckPanelLayout.FindMapping(layout, targetSlot);
                if (mapping == null)
                {
                    mapping = new Mapping { Input = DeckPanelLayout.InputName(targetSlot), Layer = DeckPanelLayout.Layer };
                    layout.Mappings.Add(mapping);
                }
                mapping.DeckFilePath = Path.GetFullPath(path);
                OverlayService.NotifyDeckLayoutChanged();
                return;
            }
        }
        finally { DragFinish(dropHandle); }
    }
    static void AppendDropDiagnostic(string message)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RELYR_DROP_DIAGNOSTICS"), "1", StringComparison.Ordinal))
            return;
        try
        {
            File.AppendAllText(VerificationPaths.GetFile("deck-drop-runtime.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }
    int DeckSlotAt(NativeDropPoint point)
    {
        try
        {
            var element = InputHitTest(PointFromScreen(new Point(point.X, point.Y))) as DependencyObject;
            for (var current = element; current != null;)
            {
                if (current is Button { Tag: int slot } && slot > 0 && slot <= DeckPanelLayout.VisibleSlotCount(layout))
                    return slot;
                current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
            }
        }
        catch { }
        return 0;
    }
    IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDropFiles)
        {
            AppendDropDiagnostic($"WM_DROPFILES received, hwnd={hwnd}, wParam={wParam}");
            HandleShellFileDrop(hwnd, wParam);
            handled = true;
            return IntPtr.Zero;
        }
        if (msg == WmMouseActivate)
        {
            handled = true;
            return new IntPtr(MaNoActivate);
        }
        return IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct AccentPolicy
    {
        internal int AccentState;
        internal int AccentFlags;
        internal int GradientColor;
        internal int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WindowCompositionAttributeData
    {
        internal int Attribute;
        internal IntPtr Data;
        internal int SizeOfData;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll", PreserveSig = true)] static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr objectHandle);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll", SetLastError = true)] static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr changeInfo);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref NativeDropPoint point);
    [DllImport("shell32.dll")] static extern void DragAcceptFiles(IntPtr hwnd, bool accept);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder? fileName, int bufferLength);
    [DllImport("shell32.dll")] static extern bool DragQueryPoint(IntPtr drop, out NativeDropPoint point);
    [DllImport("shell32.dll")] static extern void DragFinish(IntPtr drop);
    static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index));
    static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value) => IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, value) : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));
}
