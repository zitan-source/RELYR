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
using WpfSize = System.Windows.Size;

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
    const int WmNcHitTest = 0x0084;
    const int WmSysCommand = 0x0112;
    const int WmSizing = 0x0214;
    const int WmEnterSizeMove = 0x0231;
    const int WmExitSizeMove = 0x0232;
    const int WmDropFiles = 0x0233;
    const int WmCopyGlobalData = 0x0049;
    const int WmCopyData = 0x004A;
    const uint MsgFiltAllow = 1;
    const int MaNoActivate = 3;
    const int ScCommandMask = 0xFFF0;
    const int ScMaximize = 0xF030;
    const int ScRestore = 0xF120;
    const int HtTopLeft = 13;
    const int HtTopRight = 14;
    const int HtBottomLeft = 16;
    const int HtBottomRight = 17;
    const int WmszLeft = 1;
    const int WmszRight = 2;
    const int WmszTop = 3;
    const int WmszTopLeft = 4;
    const int WmszTopRight = 5;
    const int WmszBottomLeft = 7;
    const int WmszBottom = 6;
    const int WmszBottomRight = 8;
    const double ResizeAxisLockThreshold = 4;
    const int WcaAccentPolicy = 19;
    const int AccentEnableAcrylicBlurBehind = 4;
    const int DwmwaWindowCornerPreference = 33;
    const int DwmwaUseImmersiveDarkMode = 20;
    const int DwmwaSystemBackdropType = 38;
    const int DwmWindowCornerPreferenceRound = 2;
    const int DwmsbtNone = 1;
    const double PanelCornerRadius = 14;
    const double PanelBorderThickness = 1;
    const double PanelInset = 12;
    const double HeaderHeight = 30;
    const double HeaderToGridGap = 12;
    const double CompactHeaderThreshold = 150;
    const double UltraCompactHeaderThreshold = 88;
    const double ResizeCornerSize = 12;
    const byte SolidSurfaceTintAlpha = 222;
    const int MinimumGlassOpacityPercent = 40;
    const int MaximumGlassOpacityPercent = 100;
    const byte MinimumBackdropTintAlpha = 20;
    const byte MaximumBackdropTintAlpha = 150;
    static readonly TimeSpan HoverAudioDelay = TimeSpan.FromMilliseconds(220);
    static readonly TimeSpan PointerLeaveAutoHideDelay = TimeSpan.FromMilliseconds(500);
    static readonly TimeSpan ActionAutoHideDelay = TimeSpan.FromMilliseconds(140);
    static readonly WpfColor AcrylicCharcoal = WpfColor.FromRgb(0x1C, 0x1F, 0x22);
    static readonly WpfColor DeckPrimaryText = WpfColor.FromRgb(0xF2, 0xF2, 0xF2);
    static readonly WpfColor DeckSecondaryText = WpfColor.FromRgb(0x9A, 0x9E, 0xA5);
    static readonly WpfColor DeckEmptyText = WpfColor.FromRgb(0x6B, 0x70, 0x77);
    static readonly WpfColor DeckAccent = WpfColor.FromRgb(0x35, 0xD0, 0xC5);

    readonly Border dragArea;
    readonly Action<Mapping>? execute;
    readonly Action<double, double>? savePosition;
    readonly Action<double, double>? saveCollapsedPosition;
    readonly Action<string, double, double>? saveSize;
    readonly Action<string, bool>? savePinned;
    bool hoverPreviewsEnabled;
    bool autoHideAfterAction;
    bool autoHideOnPointerLeave;
    bool pointerEnteredSinceShown;
    bool autoHideRequiresPointerOutside;
    int openContextMenus;
    bool dragging;
    bool positionDirty;
    Button? fileDragButton;
    DeckDragPreviewWindow? dragPreview;
    bool shellFileDropEnabled;
    bool internalDeckDragActive;
    bool deckReorderDragging;
    bool deckButtonsBuilt;
    bool deckBuildQueued;
    int deckReorderSourceSlot;
    Button? deckReorderTargetButton;
    Point fileDragStart;
    MediaPlayer? hoverAudioPlayer;
    Button? hoverAudioSource;
    Button? pendingHoverAudioSource;
    string pendingHoverAudioPath = "";
    readonly DispatcherTimer hoverAudioStartTimer = new() { Interval = HoverAudioDelay };
    readonly DispatcherTimer autoHideTimer = new() { Interval = PointerLeaveAutoHideDelay };
    DeckVideoPreviewPopup? videoPreview;
    CancellationTokenSource? previewLoadCancellation;
    Point dragStart;
    double windowStartLeft, windowStartTop;
    DpiScale dragDpi;

    internal IReadOnlyList<Button> DeckButtons => deckButtons;
    internal int VideoPreviewCountForTest => videoPreview == null ? 0 : 1;
    internal Button CloseButton { get; private set; } = null!;
    internal Button ResetSizeButton { get; private set; } = null!;
    internal Button PinButton { get; private set; } = null!;
    internal Button FullScreenButton { get; private set; } = null!;
    internal Button MoreButton { get; private set; } = null!;
    internal FrameworkElement CollapsedMoveHandle { get; private set; } = null!;
    internal bool IsPinnedForTest => layout.PanelPinned;
    internal bool PointerAutoHideArmedForTest => pointerEnteredSinceShown;
    internal void ArmPointerAutoHideForTest() => pointerEnteredSinceShown = true;
    internal void RequestPointerAutoHideForTest() => ScheduleAutoHide(PointerLeaveAutoHideDelay, true);
    internal void SetDragActiveForTest(bool value) => internalDeckDragActive = value;
    internal double VisualOpacityForTest => panelCard.Opacity;
    internal int GlassOpacityPercentForTest => glassOpacityPercent;
    internal byte BackdropTintAlphaForTest => backdropTintAlpha;
    internal bool UsesLegacyAcrylicFallbackForTest => backdropMode == DeckBackdropMode.AccentAcrylicOnly;
    internal string BackdropModeForTest => backdropMode.ToString();
    internal string LayoutId => layout.Id;
    internal Thickness PanelPaddingForTest => panelCard.Padding;
    internal System.Windows.Media.Brush HeaderBackgroundForTest => dragArea.Background;
    internal bool HeaderTitleVisibleForTest => headerTitle.Visibility == Visibility.Visible;
    internal bool HeaderGripVisibleForTest => headerGrip.Visibility == Visibility.Visible && headerGrip.ActualWidth >= 7;
    internal Rect HeaderGripBoundsForTest
    {
        get
        {
            var topLeft = headerGrip.TranslatePoint(new Point(), this);
            return new Rect(topLeft, new WpfSize(headerGrip.ActualWidth, headerGrip.ActualHeight));
        }
    }
    internal string HeaderToolTipForTest => dragArea.ToolTip?.ToString() ?? "";
    internal System.Windows.Controls.ContextMenu? HeaderContextMenuForTest => dragArea.ContextMenu;
    internal System.Windows.Controls.ContextMenu? PanelContextMenuForTest => panelCard.ContextMenu;
    internal double DefaultWidthForTest => defaultWindowWidth;
    internal double DefaultHeightForTest => defaultWindowHeight;
    internal bool UsesNoActivateStyle
    {
        get; private set;
    }
    internal bool UsesShellFileDrop => shellFileDropEnabled;
    readonly List<Button> deckButtons = [];
    readonly Border panelCard;
    readonly DeckLayoutDefinition layout;
    readonly UniformGrid deckGrid;
    readonly Grid root;
    readonly Viewbox deckView;
    TextBlock headerTitle = null!;
    TextBlock fullScreenGlyph = null!;
    FrameworkElement headerGrip = null!;
    Style? glassButtonStyle;
    Style? closeButtonStyle;
    DeckBackdropMode backdropMode;
    int glassOpacityPercent;
    byte backdropTintAlpha;
    Grid? acrylicDiffuseLayer;
    double naturalGridWidth;
    double naturalGridHeight;
    double defaultWindowWidth;
    double defaultWindowHeight;
    double minimumDeckScale;
    double maximumDeckScale;
    Rect? safeMaximizeRestoreBounds;
    bool changingWindowState;
    int renderedColumns;
    int renderedRows;
    bool collapsedToEdge;
    bool edgeExpansionArmed;
    bool collapsedPointerTransitionPending;
    Rect expandedBounds;
    Rect collapsedBounds;
    bool collapsedPositionCustomized;
    bool resetSizeWhenExpanded;
    bool interactiveSizing;
    bool? cornerResizeWidthDriven;
    double interactiveSizingStartWidth;
    double interactiveSizingStartHeight;
    int headerLayoutMode = -1;

    enum DeckBackdropMode
    {
        Pending,
        SystemBackdrop,
        AccentAcrylicOnly,
        SolidFallback
    }
    WpfColor panelTone;

    internal DeckPanelOverlayWindow(AppConfig config, Action<Mapping>? executeAction, int opacityPercent = 96, Action<double, double>? positionChanged = null, DeckLayoutDefinition? selectedLayout = null, Action<string, double, double>? sizeChanged = null, Action<string, bool>? pinnedChanged = null, Action<double, double>? collapsedPositionChanged = null)
    {
        execute = executeAction;
        savePosition = positionChanged;
        saveCollapsedPosition = collapsedPositionChanged;
        saveSize = sizeChanged;
        savePinned = pinnedChanged;
        hoverAudioStartTimer.Tick += HoverAudioStartTimerTick;
        autoHideTimer.Tick += AutoHideTimerTick;
        hoverPreviewsEnabled = config.DeckHoverPreviewsEnabled;
        autoHideAfterAction = config.DeckAutoHideAfterAction;
        autoHideOnPointerLeave = config.DeckAutoHideOnPointerLeave;
        layout = selectedLayout ?? DeckPanelLayout.DefaultLayout(config) ?? new DeckLayoutDefinition();
        renderedColumns = layout.Columns;
        renderedRows = layout.Rows;
        Title = "RELYR Deck - " + layout.Name;
        deckGrid = new UniformGrid();
        UpdateDeckDimensions(layout.PanelWidth, layout.PanelHeight);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = false;
        Background = new SolidColorBrush(AcrylicCharcoal);
        // Keep the whole borderless Deck inside WPF's opaque client area.
        // Without WindowChrome, a resizable opaque Window reserves an
        // invisible non-client strip; that changes the Deck aspect ratio and
        // can leave a misleading hit-test margin around the visible panel.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(ResizeCornerSize),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Point initial = InitialPosition(config, Width, Height, layout);
        Left = initial.X;
        Top = initial.Y;
        if ((layout.PanelCollapsedLeft ?? config.DeckPanelCollapsedLeft) is double collapsedLeft
            && (layout.PanelCollapsedTop ?? config.DeckPanelCollapsedTop) is double collapsedTop
            && double.IsFinite(collapsedLeft) && double.IsFinite(collapsedTop))
        {
            collapsedBounds = new Rect(collapsedLeft, collapsedTop, 0, 0);
            collapsedPositionCustomized = true;
        }

        panelCard = new Border
        {
            CornerRadius = new CornerRadius(PanelCornerRadius),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(PanelInset),
            ClipToBounds = true,
            Opacity = Math.Clamp(opacityPercent / 100.0, .4, 1)
        };
        SetGlassOpacity(opacityPercent);
        panelCard.SizeChanged += (_, _) => ApplyRoundedPanelClip();
        panelCard.PreviewMouseLeftButtonDown += DragStarted;
        panelCard.PreviewMouseMove += DragMoved;
        panelCard.PreviewMouseLeftButtonUp += DragEnded;
        panelCard.MouseEnter += PanelCard_MouseEnter;
        panelCard.MouseLeave += PanelCard_MouseLeave;
        // panelCard.SizeChanged already refreshes the WPF clip. Rebuilding it
        // again from the Window event doubles the render work for every native
        // sizing frame and makes interactive resize visibly pulse.
        SizeChanged += (_, _) => { if (!interactiveSizing) ApplyRoundedWindowRegion(); UpdateHeaderLayout(); };
        ApplyPanelColor();
        Content = panelCard;

        root = new Grid { ClipToBounds = true };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderToGridGap) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panelCard.Child = root;
        dragArea = BuildHeader();
        root.Children.Add(dragArea);
        UpdateHeaderLayout();

        deckView = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0), Child = deckGrid };
        Grid.SetRow(deckView, 2);
        root.Children.Add(deckView);
        BuildDeckButtons();

        SourceInitialized += WindowSourceInitialized;
        StateChanged += WindowStateChanged;
        ThemeService.ThemeChanged += ThemeChanged;
        panelCard.LostMouseCapture += (_, _) => dragging = false;
        Closed += (_, _) => { ThemeService.ThemeChanged -= ThemeChanged; autoHideTimer.Stop(); ReleaseOwnedMouseCapture(); CancelDeferredPreviews(); ClearDeckReorderTarget(); StopDragPreview(); ClearVideoPreviews(); CancelPendingHoverAudio(); StopHoverAudio(); StopShellFileDrop(); PersistPosition(); PersistCollapsedPosition(); PersistSize(); };
    }

    void UpdateDeckDimensions(double? preferredWidth = null, double? preferredHeight = null)
    {
        naturalGridWidth = Math.Clamp(layout.Columns, 1, DeckPanelLayout.MaximumColumns) * DeckPanelLayout.CellWidth;
        naturalGridHeight = Math.Clamp(layout.Rows, 1, DeckPanelLayout.MaximumRows) * DeckPanelLayout.CellHeight;
        double availableWidth = Math.Max(1, SystemParameters.WorkArea.Width - 48 - OverlayChromeWidth);
        double availableHeight = Math.Max(1, SystemParameters.WorkArea.Height - 48 - OverlayChromeHeight);
        double fittedScale = Math.Min(1, Math.Min(availableWidth / naturalGridWidth, availableHeight / naturalGridHeight));
        maximumDeckScale = Math.Min((SystemParameters.WorkArea.Width - 24 - OverlayChromeWidth) / naturalGridWidth, (SystemParameters.WorkArea.Height - 24 - OverlayChromeHeight) / naturalGridHeight);
        double minimumTargetWidth = Math.Min(240, naturalGridWidth * fittedScale + OverlayChromeWidth);
        double minimumTargetHeight = Math.Min(180, naturalGridHeight * fittedScale + OverlayChromeHeight);
        minimumDeckScale = Math.Max(.08, Math.Max((minimumTargetWidth - OverlayChromeWidth) / naturalGridWidth, (minimumTargetHeight - OverlayChromeHeight) / naturalGridHeight));
        maximumDeckScale = Math.Max(minimumDeckScale, maximumDeckScale);
        var fitted = WindowSizeForScale(fittedScale);
        defaultWindowWidth = fitted.Width;
        defaultWindowHeight = fitted.Height;
        var requested = preferredWidth is double savedWidth && preferredHeight is double savedHeight && double.IsFinite(savedWidth) && double.IsFinite(savedHeight)
            ? AspectLockedSize(savedWidth, savedHeight, fittedScale, false)
            : fitted;
        var minimum = WindowSizeForScale(minimumDeckScale);
        var maximum = WindowSizeForScale(maximumDeckScale);

        // A live Deck can change from a wide grid to a narrow grid while this
        // window is cached.  Clear the previous layout's constraints before
        // assigning the new size; otherwise WPF clamps Width to the old
        // MinWidth and persists a large empty band beside the narrow grid.
        MinWidth = 0;
        MinHeight = 0;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
        MinWidth = minimum.Width;
        MaxWidth = maximum.Width;
        MinHeight = minimum.Height;
        MaxHeight = maximum.Height;
        Width = requested.Width;
        Height = requested.Height;
        deckGrid.Rows = Math.Clamp(layout.Rows, 1, DeckPanelLayout.MaximumRows);
        deckGrid.Columns = Math.Clamp(layout.Columns, 1, DeckPanelLayout.MaximumColumns);
        deckGrid.Width = naturalGridWidth;
        deckGrid.Height = naturalGridHeight;
    }
    static double OverlayChromeWidth => PanelInset * 2 + PanelBorderThickness * 2;
    static double OverlayChromeHeight => PanelInset * 2 + PanelBorderThickness * 2 + HeaderHeight + HeaderToGridGap;
    WpfSize WindowSizeForScale(double scale) => new(naturalGridWidth * scale + OverlayChromeWidth, naturalGridHeight * scale + OverlayChromeHeight);
    WpfSize AspectLockedSize(double proposedWidth, double proposedHeight, double currentScale, bool followLargestChange)
    {
        double widthScale = (proposedWidth - OverlayChromeWidth) / Math.Max(1, naturalGridWidth);
        double heightScale = (proposedHeight - OverlayChromeHeight) / Math.Max(1, naturalGridHeight);
        double scale = followLargestChange && Math.Abs(widthScale - currentScale) >= Math.Abs(heightScale - currentScale) ? widthScale : heightScale;
        if (!followLargestChange)
            scale = Math.Min(widthScale, heightScale);
        return WindowSizeForScale(Math.Clamp(scale, minimumDeckScale, maximumDeckScale));
    }
    void BeginInteractiveSizing(double width, double height)
    {
        interactiveSizing = true;
        cornerResizeWidthDriven = null;
        interactiveSizingStartWidth = Math.Max(1, width);
        interactiveSizingStartHeight = Math.Max(1, height);
    }
    WpfSize ConstrainInteractiveSize(double proposedWidth, double proposedHeight, int edge)
    {
        if (!interactiveSizing)
            BeginInteractiveSizing(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
        bool widthDriven;
        if (edge is WmszLeft or WmszRight)
            widthDriven = true;
        else if (edge is WmszTop or WmszBottom)
            widthDriven = false;
        else
        {
            if (cornerResizeWidthDriven == null)
            {
                double widthDelta = Math.Abs(proposedWidth - interactiveSizingStartWidth);
                double heightDelta = Math.Abs(proposedHeight - interactiveSizingStartHeight);
                // WM_SIZING commonly begins with one or two rounding-only
                // frames. Locking the axis from that noise can choose the
                // opposite direction from the user's actual drag and makes the
                // window jump between the cursor and the constrained edge.
                if (Math.Max(widthDelta, heightDelta) < ResizeAxisLockThreshold)
                {
                    double startScale = (interactiveSizingStartWidth - OverlayChromeWidth) / Math.Max(1, naturalGridWidth);
                    return WindowSizeForScale(Math.Clamp(startScale, minimumDeckScale, maximumDeckScale));
                }
                double widthChange = widthDelta / Math.Max(1, naturalGridWidth);
                double heightChange = heightDelta / Math.Max(1, naturalGridHeight);
                cornerResizeWidthDriven = widthChange >= heightChange;
            }
            widthDriven = cornerResizeWidthDriven.Value;
        }
        double scale = widthDriven
            ? (proposedWidth - OverlayChromeWidth) / Math.Max(1, naturalGridWidth)
            : (proposedHeight - OverlayChromeHeight) / Math.Max(1, naturalGridHeight);
        return WindowSizeForScale(Math.Clamp(scale, minimumDeckScale, maximumDeckScale));
    }
    internal void BeginInteractiveSizingForTest(double width, double height) => BeginInteractiveSizing(width, height);
    internal WpfSize ConstrainInteractiveSizeForTest(double width, double height, int edge) => ConstrainInteractiveSize(width, height, edge);
    internal bool? CornerResizeWidthDrivenForTest => cornerResizeWidthDriven;
    internal bool AppliesRoundedRegionDuringResizeForTest => !interactiveSizing;
    internal void EndInteractiveSizingForTest() { interactiveSizing = false; cornerResizeWidthDriven = null; }

    void BuildDeckButtons()
    {
        CancelDeferredPreviews();
        previewLoadCancellation = new CancellationTokenSource();
        deckBuildQueued = false;
        deckButtonsBuilt = true;
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
    void QueueDeckButtonBuild()
    {
        if (deckBuildQueued || deckButtonsBuilt || Dispatcher.HasShutdownStarted)
            return;
        deckBuildQueued = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            deckBuildQueued = false;
            if (IsVisible && !deckButtonsBuilt)
                BuildDeckButtons();
        }));
    }
    internal void BeginDeferredBuild() => QueueDeckButtonBuild();

    internal void HideForReuse()
    {
        autoHideTimer.Stop();
        pointerEnteredSinceShown = false;
        ReleaseOwnedMouseCapture();
        PersistPosition();
        PersistCollapsedPosition();
        PersistSize();
        // Hide first so a pointer that is still over a Deck button cannot raise
        // another MouseEnter and recreate a media preview during teardown.
        Hide();
        ClearVideoPreviews();
        CancelPendingHoverAudio();
        StopHoverAudio();
    }

    void ReleaseOwnedMouseCapture()
    {
        dragging = false;
        CancelDeckReorder();
        fileDragButton = null;
        if (Mouse.Captured == panelCard)
            panelCard.ReleaseMouseCapture();
    }

    internal void CapturePanelMouseForTest() => panelCard.CaptureMouse();
    internal bool OwnsMouseCaptureForTest => Mouse.Captured == panelCard || Mouse.Captured is Button button && deckButtons.Contains(button);

    internal bool IsCollapsedToEdge => collapsedToEdge;
    internal bool EdgeExpansionArmedForTest => edgeExpansionArmed;
    internal void ArmEdgeExpansionForTest() => edgeExpansionArmed = true;
    internal void ContinueFromCollapsedMoveHandleForTest() => ContinueFromCollapsedMoveHandle(true);
    internal void HandlePointerEnteredForTest() => HandlePanelPointerEntered();
    internal void HandlePointerLeftForTest() => HandlePanelPointerLeft();
    internal bool CursorOutsideForTest => IsCursorOutsideDeckWindow();
    internal Rect ExpandedBoundsForTest => expandedBounds;
    internal Rect CollapsedBoundsForTest => collapsedBounds;
    static Rect VirtualDesktopBounds => new(
        SystemParameters.VirtualScreenLeft,
        SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth,
        SystemParameters.VirtualScreenHeight);

    internal void MoveCollapsedTabForTest(double left, double top)
    {
        if (!collapsedToEdge)
            return;
        Rect work = VirtualDesktopBounds;
        collapsedPositionCustomized = true;
        ApplyCollapsedBounds(new Rect(
            Math.Clamp(left, work.Left, work.Right - Width),
            Math.Clamp(top, work.Top, work.Bottom - Height),
            Width,
            Height));
        PersistCollapsedPosition();
    }

    internal void ExpandFromEdge()
    {
        if (!collapsedToEdge)
            return;
        collapsedToEdge = false;
        edgeExpansionArmed = false;
        collapsedPointerTransitionPending = false;
        changingWindowState = true;
        try
        {
            if (WindowState != WindowState.Normal)
                WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.CanResize;
            root.RowDefinitions[1].Height = new GridLength(HeaderToGridGap);
            root.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
            MinWidth = 0;
            MinHeight = 0;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
            UpdateDeckDimensions(resetSizeWhenExpanded ? layout.PanelWidth : expandedBounds.Width, resetSizeWhenExpanded ? layout.PanelHeight : expandedBounds.Height);
            resetSizeWhenExpanded = false;
            Left = expandedBounds.Left;
            Top = expandedBounds.Top;
            UpdateLayout();
            UpdateHeaderLayout();
        }
        finally { changingWindowState = false; }
    }

    internal void CollapseToEdge()
    {
        if (collapsedToEdge || layout.PanelPinned || !IsVisible)
            return;
        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : Height;
        expandedBounds = new Rect(Left, Top, width, height);
        collapsedToEdge = true;
        edgeExpansionArmed = false;
        collapsedPointerTransitionPending = true;
        root.RowDefinitions[1].Height = new GridLength(0);
        root.RowDefinitions[2].Height = new GridLength(0);
        double collapsedWidth = Math.Clamp(width, 140, 220);
        double collapsedHeight = OverlayChromeHeight - HeaderToGridGap;
        Rect work = VirtualDesktopBounds;
        if (collapsedPositionCustomized)
        {
            collapsedBounds = new Rect(
                Math.Clamp(collapsedBounds.Left, work.Left, work.Right - collapsedWidth),
                Math.Clamp(collapsedBounds.Top, work.Top, work.Bottom - collapsedHeight),
                collapsedWidth,
                collapsedHeight);
        }
        else
        {
            double leftDistance = Math.Abs(expandedBounds.Left - work.Left);
            double rightDistance = Math.Abs(work.Right - expandedBounds.Right);
            double topDistance = Math.Abs(expandedBounds.Top - work.Top);
            double bottomDistance = Math.Abs(work.Bottom - expandedBounds.Bottom);
            double nearest = Math.Min(Math.Min(leftDistance, rightDistance), Math.Min(topDistance, bottomDistance));
            if (nearest == leftDistance)
                collapsedBounds = new Rect(work.Left, Math.Clamp(expandedBounds.Top, work.Top, work.Bottom - collapsedHeight), collapsedWidth, collapsedHeight);
            else if (nearest == rightDistance)
                collapsedBounds = new Rect(work.Right - collapsedWidth, Math.Clamp(expandedBounds.Top, work.Top, work.Bottom - collapsedHeight), collapsedWidth, collapsedHeight);
            else if (nearest == topDistance)
                collapsedBounds = new Rect(Math.Clamp(expandedBounds.Left, work.Left, work.Right - collapsedWidth), work.Top, collapsedWidth, collapsedHeight);
            else
                collapsedBounds = new Rect(Math.Clamp(expandedBounds.Left, work.Left, work.Right - collapsedWidth), work.Bottom - collapsedHeight, collapsedWidth, collapsedHeight);
        }
        var pointerAtCollapse = System.Windows.Forms.Cursor.Position;
        bool pointerStartedOutsideCollapsedBounds = !collapsedBounds.Contains(new Point(pointerAtCollapse.X, pointerAtCollapse.Y));
        ApplyCollapsedBounds(collapsedBounds);
        // Moving the collapsed tab to the nearest screen edge can place it
        // underneath a stationary pointer.  Do not treat the resulting
        // synthetic MouseEnter as an intentional request to expand: that
        // would restore the old bounds, raise MouseLeave, and oscillate.
        // If the pointer is already outside the tab, arm it immediately;
        // otherwise require one genuine leave before the next enter expands.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (!collapsedToEdge)
                return;
            collapsedPointerTransitionPending = false;
            var currentPointer = System.Windows.Forms.Cursor.Position;
            bool pointerActuallyMoved = currentPointer.X != pointerAtCollapse.X || currentPointer.Y != pointerAtCollapse.Y;
            edgeExpansionArmed = pointerStartedOutsideCollapsedBounds || (pointerActuallyMoved && IsCursorOutsideDeckWindow());
        }));
    }

    void ApplyCollapsedBounds(Rect bounds)
    {
        changingWindowState = true;
        try
        {
            // A transparent WPF window can remain maximized after its content
            // is collapsed. That leaves an invisible, desktop-sized hit-test
            // surface. Normalize the native window before applying tab bounds.
            if (WindowState != WindowState.Normal)
                WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;
            MinWidth = 0;
            MinHeight = 0;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
            Width = bounds.Width;
            Height = bounds.Height;
            Left = bounds.Left;
            Top = bounds.Top;
            collapsedBounds = new Rect(Left, Top, Width, Height);
            UpdateLayout();
            UpdateHeaderLayout();
        }
        finally { changingWindowState = false; }
    }

    internal void PrepareForShow()
    {
        autoHideTimer.Stop();
        pointerEnteredSinceShown = false;
        autoHideRequiresPointerOutside = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (IsVisible && IsMouseOver)
                pointerEnteredSinceShown = true;
        }));
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
            Margin = new Thickness(DeckPanelLayout.ButtonGap / 2, 0, DeckPanelLayout.ButtonGap / 2, 0),
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
            var assignmentColor = MainWindow.AssignmentColorFor(mapping!);
            button.Background = new SolidColorBrush(assignmentColor);
            button.Foreground = new SolidColorBrush(DeckPanelLayout.TextColorFor(assignmentColor));
        }
        button.Click += DeckButtonClicked;
        button.PreviewMouseLeftButtonDown += DeckButtonDragStarted;
        button.PreviewMouseMove += DeckButtonDragMoved;
        button.PreviewMouseLeftButtonUp += DeckButtonDragEnded;
        button.GiveFeedback += DeckDragGiveFeedback;
        button.ContextMenu = CreateDeckButtonContextMenu(slot);
        button.AllowDrop = true;
        button.PreviewDragOver += DeckButtonDragOver;
        button.PreviewDrop += DeckButtonDropped;
        button.Resources["DeckFileAvailable"] = DeckPanelLayout.IsAvailableFile(mapping);
        button.MouseEnter += DeckButtonFileAvailability_MouseEnter;
        if (hoverPreviewsEnabled && (DeckPanelLayout.IsVideoFile(mapping?.DeckFilePath) || !NeedsDeferredFilePreview(mapping)))
            ConfigureHoverPreview(button, mapping);
        var nameLabel = DeckPanelLayout.CreateNameLabel(mapping);
        if (DeckPanelLayout.TryGetButtonColor(mapping, out _) || MainWindow.MappingInterceptsInput(mapping))
            nameLabel.Foreground = button.Foreground;
        var cell = new StackPanel { Width = DeckPanelLayout.CellWidth, Height = DeckPanelLayout.CellHeight };
        cell.Children.Add(button);
        cell.Children.Add(nameLabel);
        return (button, cell);
    }
    void DeckButtonFileAvailability_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Button { Tag: int slot } button)
            return;
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        if (!DeckPanelLayout.HasRegisteredFile(mapping))
            return;
        bool available = DeckPanelLayout.IsAvailableFile(mapping);
        bool previous = button.Resources["DeckFileAvailable"] is bool value && value;
        if (available == previous)
            return;
        button.Resources["DeckFileAvailable"] = available;
        Dispatcher.BeginInvoke(() => RefreshDeckSlots(slot, slot), DispatcherPriority.Background);
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
        CancellationToken cancellation = previewLoadCancellation?.Token ?? CancellationToken.None;

        _ = Task.Run(() =>
        {
            foreach (var item in pending)
            {
                if (cancellation.IsCancellationRequested)
                    return;
                try
                {
                    if (DeckPanelLayout.IsVideoFile(item.Path))
                    {
                        _ = DeckPanelLayout.LoadVideoThumbnail(item.Path, 96, 54);
                    }
                    else
                    {
                        _ = DeckPanelLayout.LoadImageThumbnail(item.Path, 96);
                    }
                }
                catch { }
            }

            try
            {
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (cancellation.IsCancellationRequested || !IsLoaded)
                        return;
                    foreach (var item in pending)
                    {
                        if (cancellation.IsCancellationRequested)
                            return;
                        try
                        {
                            if (!deckButtons.Contains(item.Button))
                                continue;
                            var mapping = DeckPanelLayout.FindMapping(layout, item.Slot);
                            if (mapping == null || !string.Equals(mapping.DeckFilePath, item.Path, StringComparison.OrdinalIgnoreCase))
                                continue;
                            item.Button.Content = DeckPanelLayout.CreateButtonContent(DeckPanelLayout.InputName(item.Slot), mapping);
                            if (hoverPreviewsEnabled && !DeckPanelLayout.IsVideoFile(item.Path))
                                ConfigureHoverPreview(item.Button, mapping);
                        }
                        catch { }
                    }
                }));
            }
            catch { }
        });
    }
    void CancelDeferredPreviews()
    {
        try { previewLoadCancellation?.Cancel(); } catch { }
        previewLoadCancellation?.Dispose();
        previewLoadCancellation = null;
    }
    void ClearVideoPreviewFor(Button source)
    {
        if (videoPreview?.IsFor(source) != true)
            return;
        videoPreview.Dispose();
        videoPreview = null;
    }

    internal void Refresh(int opacityPercent, bool previewsEnabled, bool? hideAfterAction = null, bool? hideOnPointerLeave = null)
    {
        RefreshAppearance(opacityPercent, previewsEnabled, hideAfterAction, hideOnPointerLeave);
        if (deckButtonsBuilt)
            BuildDeckButtons();
        else
            QueueDeckButtonBuild();
    }

    internal void RefreshAppearance(int opacityPercent, bool previewsEnabled, bool? hideAfterAction = null, bool? hideOnPointerLeave = null)
    {
        hoverPreviewsEnabled = previewsEnabled;
        if (hideAfterAction is bool afterAction)
            autoHideAfterAction = afterAction;
        if (hideOnPointerLeave is bool pointerLeave)
            autoHideOnPointerLeave = pointerLeave;
        SetGlassOpacity(opacityPercent);
        ApplyPanelColor();
        Title = "RELYR Deck - " + layout.Name;
        headerTitle.Text = layout.Name;
        dragArea.ToolTip = layout.Name;
        bool dimensionsChanged = renderedColumns != layout.Columns || renderedRows != layout.Rows;
        renderedColumns = layout.Columns;
        renderedRows = layout.Rows;
        if (collapsedToEdge)
            resetSizeWhenExpanded |= dimensionsChanged;
        else
            UpdateDeckDimensions(dimensionsChanged ? layout.PanelWidth : Width, dimensionsChanged ? layout.PanelHeight : Height);
        UpdateHeaderLayout();
    }

    void PanelCard_MouseEnter(object sender, MouseEventArgs e)
        => HandlePanelPointerEntered();

    void HandlePanelPointerEntered()
    {
        if (collapsedToEdge && collapsedPointerTransitionPending)
        {
            autoHideTimer.Stop();
            return;
        }
        if (collapsedToEdge && (dragging || CollapsedMoveHandle.IsMouseOver))
        {
            autoHideTimer.Stop();
            return;
        }
        if (collapsedToEdge && !edgeExpansionArmed)
        {
            autoHideTimer.Stop();
            return;
        }
        if (collapsedToEdge)
        {
            ExpandFromPointerHover();
            return;
        }
        pointerEnteredSinceShown = true;
        autoHideTimer.Stop();
    }

    void PanelCard_MouseLeave(object sender, MouseEventArgs e)
        => HandlePanelPointerLeft();

    void CollapsedMoveHandle_MouseLeave(object sender, MouseEventArgs e)
    {
        // Entering a collapsed tab through its right-edge move handle skips
        // expansion so a drag can begin. Moving from that handle into the tab
        // body does not raise PanelCard.MouseEnter again, so re-evaluate here.
        ContinueFromCollapsedMoveHandle(panelCard.IsMouseOver || !IsCursorOutsideDeckWindow());
    }

    void ContinueFromCollapsedMoveHandle(bool pointerInsideCollapsedDeck)
    {
        if (pointerInsideCollapsedDeck
            && collapsedToEdge
            && !collapsedPointerTransitionPending
            && edgeExpansionArmed
            && !dragging)
            ExpandFromPointerHover();
    }

    void ExpandFromPointerHover()
    {
        ExpandFromEdge();
        // Entering the small edge tab is not the same as entering the expanded
        // Deck. The Deck restores its independent previous position, which may
        // be away from the pointer. Do not collapse it again until the pointer
        // has genuinely reached that expanded surface at least once.
        pointerEnteredSinceShown = false;
        autoHideTimer.Stop();
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!collapsedToEdge && !IsCursorOutsideDeckWindow())
                pointerEnteredSinceShown = true;
        }));
    }

    void HandlePanelPointerLeft()
    {
        if (collapsedToEdge)
        {
            if (collapsedPointerTransitionPending)
            {
                autoHideTimer.Stop();
                return;
            }
            // Resizing or moving the native window can raise MouseLeave even
            // though the physical pointer never left the collapsed tab. Only
            // a cursor position that is really outside arms the next enter.
            edgeExpansionArmed = IsCursorOutsideDeckWindow();
            autoHideTimer.Stop();
            return;
        }
        if (pointerEnteredSinceShown && autoHideOnPointerLeave)
            ScheduleAutoHide(PointerLeaveAutoHideDelay, true);
    }

    void ScheduleAutoHide(TimeSpan delay, bool requirePointerOutside)
    {
        autoHideTimer.Stop();
        if (layout.PanelPinned || !IsVisible)
            return;
        autoHideRequiresPointerOutside = requirePointerOutside;
        autoHideTimer.Interval = delay;
        autoHideTimer.Start();
    }

    void AutoHideTimerTick(object? sender, EventArgs e)
    {
        autoHideTimer.Stop();
        if (layout.PanelPinned || !IsVisible)
            return;
        if (ShouldSuspendAutoHide())
        {
            ScheduleAutoHide(PointerLeaveAutoHideDelay, autoHideRequiresPointerOutside);
            return;
        }
        if (autoHideRequiresPointerOutside && !IsCursorOutsideDeckWindow())
            return;
        CollapseToEdge();
    }

    bool ShouldSuspendAutoHide() =>
        !IsEnabled || dragging || internalDeckDragActive || deckReorderDragging || fileDragButton != null || openContextMenus > 0 ||
        Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed || Mouse.MiddleButton == MouseButtonState.Pressed;

    void TrackContextMenu(System.Windows.Controls.ContextMenu menu)
    {
        menu.Opened += (_, _) => { openContextMenus++; autoHideTimer.Stop(); };
        menu.Closed += (_, _) =>
        {
            openContextMenus = Math.Max(0, openContextMenus - 1);
            if (pointerEnteredSinceShown && autoHideOnPointerLeave && IsCursorOutsideDeckWindow())
                ScheduleAutoHide(PointerLeaveAutoHideDelay, true);
        };
    }

    void ApplyPanelColor()
    {
        if (DeckPanelLayout.TryParseButtonColor(layout.PanelColor, out var customColor))
        {
            panelTone = customColor;
            panelCard.Background = new SolidColorBrush(customColor);
        }
        else
        {
            panelTone = ThemeService.Color("AppBackground");
            panelCard.SetResourceReference(Border.BackgroundProperty, "AppBackground");
        }
        panelCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        if (dragArea != null)
            dragArea.Background = WpfBrushes.Transparent;
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
        var border = new Border
        {
            CornerRadius = new CornerRadius(0),
            Margin = new Thickness(-PanelInset, 0, -PanelInset, 0),
            Padding = new Thickness(8, 0, 2, 0),
            Cursor = WpfCursors.SizeAll,
            Background = WpfBrushes.Transparent,
            ToolTip = layout.Name
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var grip = CreateHeaderGrip();
        headerGrip = grip;
        var title = new TextBlock { Text = layout.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        headerTitle = title;
        title.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        var pinGlyph = new TextBlock
        {
            Text = "\uE718",
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        pinGlyph.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        var pin = new Button
        {
            Width = 28,
            Height = 30,
            MinWidth = 28,
            MaxWidth = 28,
            MinHeight = 30,
            MaxHeight = 30,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            Focusable = false,
            Content = pinGlyph,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        pin.Style = CloseButtonStyle();
        pin.Click += (_, _) => TogglePinned();
        PinButton = pin;
        var reset = new Button
        {
            Width = 28,
            Height = 30,
            MinWidth = 28,
            MaxWidth = 28,
            MinHeight = 30,
            MaxHeight = 30,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            Focusable = false,
            ToolTip = "元の大きさに戻す",
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
            Content = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M1,5 V1 H5 M7,1 H11 V5 M11,7 V11 H7 M5,11 H1 V7"),
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            }
        };
        ((System.Windows.Shapes.Path)reset.Content).SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "PrimaryText");
        reset.Style = CloseButtonStyle();
        reset.Click += (_, _) => ResetToDefaultSize();
        ResetSizeButton = reset;
        fullScreenGlyph = new TextBlock
        {
            Text = "\uE740",
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        fullScreenGlyph.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        var fullScreen = new Button
        {
            Width = 28,
            Height = 30,
            MinWidth = 28,
            MaxWidth = 28,
            MinHeight = 30,
            MaxHeight = 30,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            Focusable = false,
            ToolTip = "全画面表示",
            Content = fullScreenGlyph,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        fullScreen.Style = CloseButtonStyle();
        fullScreen.Click += (_, _) => ToggleSafeMaximize();
        FullScreenButton = fullScreen;
        var moreGlyph = new TextBlock
        {
            Text = "\uE712",
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        moreGlyph.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        var more = new Button
        {
            Width = 24,
            Height = 30,
            MinWidth = 24,
            MaxWidth = 24,
            MinHeight = 30,
            MaxHeight = 30,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            Focusable = false,
            ToolTip = "その他",
            Content = moreGlyph,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        more.Style = CloseButtonStyle();
        more.Click += (_, _) => OpenHeaderMenu(more);
        MoreButton = more;
        var close = new Button
        {
            Width = 28,
            Height = 30,
            MinWidth = 28,
            MaxWidth = 28,
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
        close.Click += (_, _) => HideForReuse();
        CloseButton = close;
        CollapsedMoveHandle = CreateCollapsedMoveHandle();
        CollapsedMoveHandle.MouseLeave += CollapsedMoveHandle_MouseLeave;
        grid.Children.Add(grip);
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        Grid.SetColumn(pin, 2);
        grid.Children.Add(pin);
        Grid.SetColumn(reset, 3);
        grid.Children.Add(reset);
        Grid.SetColumn(fullScreen, 4);
        grid.Children.Add(fullScreen);
        Grid.SetColumn(more, 5);
        grid.Children.Add(more);
        Grid.SetColumn(close, 6);
        grid.Children.Add(close);
        Grid.SetColumn(CollapsedMoveHandle, 7);
        grid.Children.Add(CollapsedMoveHandle);
        border.Child = grid;
        border.ContextMenu = CreateHeaderContextMenu();
        panelCard.ContextMenu = border.ContextMenu;
        UpdatePinnedVisual();
        return border;
    }

    static FrameworkElement CreateCollapsedMoveHandle()
    {
        var glyph = new TextBlock
        {
            Text = "\uE7C2",
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 15,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        return new Border
        {
            Width = 30,
            Height = 30,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            Background = WpfBrushes.Transparent,
            Cursor = WpfCursors.SizeAll,
            ToolTip = "折りたたんだDeckを移動",
            Child = glyph,
            Visibility = Visibility.Collapsed
        };
    }

    System.Windows.Controls.ContextMenu CreateHeaderContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu { MinWidth = 190 };
        var store = new System.Windows.Controls.MenuItem { Header = "収納", IsCheckable = true };
        store.Click += (_, _) =>
        {
            SetPinned(!store.IsChecked);
            if (store.IsChecked)
                _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(CollapseToEdge));
        };
        var reset = new System.Windows.Controls.MenuItem { Header = "元のサイズに戻す" };
        reset.Click += (_, _) => ResetToDefaultSize();
        menu.Items.Add(store);
        menu.Items.Add(reset);
        menu.Opened += (_, _) => store.IsChecked = !layout.PanelPinned;
        TrackContextMenu(menu);
        return menu;
    }

    void OpenHeaderMenu(FrameworkElement target)
    {
        var menu = dragArea.ContextMenu;
        if (menu == null)
            return;
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    void TogglePinned() => SetPinned(!layout.PanelPinned);

    void SetPinned(bool pinned)
    {
        layout.PanelPinned = pinned;
        if (pinned)
            autoHideTimer.Stop();
        UpdatePinnedVisual();
        savePinned?.Invoke(layout.Id, pinned);
    }

    void UpdatePinnedVisual()
    {
        if (PinButton == null)
            return;
        PinButton.ToolTip = layout.PanelPinned ? "固定を解除" : "表示を固定";
        PinButton.Opacity = layout.PanelPinned ? 1 : .72;
        if (layout.PanelPinned)
            PinButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentSoftBrush");
        else
            PinButton.Background = WpfBrushes.Transparent;
    }

    static FrameworkElement CreateHeaderGrip()
    {
        var canvas = new Canvas { Width = 8, Height = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0), IsHitTestVisible = false };
        for (int row = 0; row < 3; row++)
        for (int column = 0; column < 2; column++)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 2, Height = 2 };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush");
            Canvas.SetLeft(dot, column * 5d);
            Canvas.SetTop(dot, 2 + row * 5d);
            canvas.Children.Add(dot);
        }
        return canvas;
    }

    void UpdateHeaderLayout()
    {
        if (dragArea == null || headerTitle == null || headerGrip == null || PinButton == null || FullScreenButton == null || MoreButton == null || ResetSizeButton == null || CloseButton == null || CollapsedMoveHandle == null)
            return;
        if (collapsedToEdge)
        {
            if (headerLayoutMode == 3)
                return;
            headerLayoutMode = 3;
            headerTitle.Visibility = Visibility.Visible;
            headerGrip.Visibility = Visibility.Collapsed;
            PinButton.Visibility = Visibility.Collapsed;
            ResetSizeButton.Visibility = Visibility.Collapsed;
            FullScreenButton.Visibility = Visibility.Collapsed;
            MoreButton.Visibility = Visibility.Collapsed;
            CloseButton.Visibility = Visibility.Collapsed;
            CollapsedMoveHandle.Visibility = Visibility.Visible;
            dragArea.Padding = new Thickness(8, 0, 2, 0);
            return;
        }
        double availableWidth = ActualWidth > 0 ? ActualWidth : Width;
        bool compact = availableWidth < CompactHeaderThreshold;
        bool ultraCompact = availableWidth < UltraCompactHeaderThreshold;
        int nextMode = ultraCompact ? 2 : compact ? 1 : 0;
        if (headerLayoutMode == nextMode)
            return;
        headerLayoutMode = nextMode;
        headerTitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        headerGrip.Visibility = ultraCompact ? Visibility.Collapsed : Visibility.Visible;
        PinButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ResetSizeButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        FullScreenButton.Visibility = Visibility.Visible;
        MoreButton.Visibility = compact && !ultraCompact ? Visibility.Visible : Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        CollapsedMoveHandle.Visibility = Visibility.Collapsed;
        headerGrip.Margin = new Thickness(0, 0, ultraCompact ? 1 : 7, 0);
        headerGrip.RenderTransform = Transform.Identity;
        dragArea.Padding = ultraCompact ? new Thickness(6, 0, 8, 0) : new Thickness(8, 0, 6, 0);
        SetFixedHeaderButtonWidth(ResetSizeButton, ultraCompact ? 24 : 28);
        SetFixedHeaderButtonWidth(PinButton, 28);
        SetFixedHeaderButtonWidth(FullScreenButton, ultraCompact ? 24 : 28);
        SetFixedHeaderButtonWidth(MoreButton, ultraCompact ? 24 : 28);
        SetFixedHeaderButtonWidth(CloseButton, ultraCompact ? 24 : 28);
    }

    static void SetFixedHeaderButtonWidth(Button button, double width)
    {
        button.Width = width;
        button.MinWidth = width;
        button.MaxWidth = width;
    }

    void ThemeChanged()
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            ApplyPanelColor();
            if (deckButtonsBuilt)
                BuildDeckButtons();
            else
                QueueDeckButtonBuild();
            CloseButton.Style = CloseButtonStyle();
            ResetSizeButton.Style = CloseButtonStyle();
            PinButton.Style = CloseButtonStyle();
            FullScreenButton.Style = CloseButtonStyle();
            MoreButton.Style = CloseButtonStyle();
            UpdatePinnedVisual();
        }));
    }

    Style GlassButtonStyle()
    {
        if (glassButtonStyle != null)
            return glassButtonStyle;
        var sharedRadius = WpfApplication.Current.TryFindResource("ControlCornerRadius") is CornerRadius radius
            ? radius
            : new CornerRadius(6);
        glassButtonStyle = CreateGlassButtonStyle(sharedRadius, WpfColor.FromArgb(24, 255, 255, 255));
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

    static System.Windows.Media.Brush FlatSurfaceBrush(WpfColor color, byte opacity)
    {
        var brush = new SolidColorBrush(WithAlpha(color, opacity));
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
        return FlatSurfaceBrush(baseColor, populated ? (byte)210 : (byte)126);
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
        {
            execute?.Invoke(mapping);
            if (autoHideAfterAction && !layout.PanelPinned)
                ScheduleAutoHide(ActionAutoHideDelay, false);
        }
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
        var icon = CreateDeckContextMenuItem("\uE8B9", "アイコン変更...", "");
        icon.Click += (_, _) => ChooseDeckButtonIcon(slot);
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
        menu.Items.Add(icon);
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
        TrackContextMenu(menu);
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
        => MainWindow.MappingHasConfiguredAction(mapping) || !string.IsNullOrWhiteSpace(mapping.Description) || !string.IsNullOrWhiteSpace(mapping.DeckColor) || DeckPanelLayout.HasRegisteredFile(mapping) || DeckIconCatalog.HasIcon(mapping);
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
        var cancel = new Button { Content = "キャンセル", Width = 98, Height = 40, Margin = new Thickness(6, 0, 0, 0), Style = (Style)WpfApplication.Current.FindResource("AppButtonStyle") };
        var ok = new Button { Content = "変更", Width = 98, Height = 40, Margin = new Thickness(6, 0, 0, 0), Style = (Style)WpfApplication.Current.FindResource("AccentAppButtonStyle"), IsDefault = true };
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
    void ChooseDeckButtonIcon(int slot)
    {
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        var picker = new DeckIconPickerWindow(mapping?.DeckIcon ?? "", mapping?.DeckIconPath ?? "") { Owner = this, Topmost = true };
        if (picker.ShowDialog() != true)
            return;
        mapping ??= GetOrCreateDeckMapping(slot);
        mapping.DeckIcon = picker.SelectedPresetId;
        mapping.DeckIconPath = picker.SelectedCustomPath;
        mapping.DeckIconAutoAssigned = false;
        if (!HasDeckButtonContent(mapping))
            layout.Mappings.Remove(mapping);
        RefreshDeckSlots(slot, slot);
        OverlayService.NotifyDeckLayoutChanged(false, layout.Id, slot, slot);
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
        if (DeckPanelLayout.HasRegisteredFile(mapping) && !DeckPanelLayout.IsAvailableFile(mapping))
        {
            button.ToolTip = DeckPanelLayout.CreateMissingFileToolTip();
            ToolTipService.SetInitialShowDelay(button, 220);
            ToolTipService.SetShowDuration(button, 20000);
            return;
        }
        if (DeckPanelLayout.IsVideoFile(mapping.DeckFilePath) && File.Exists(mapping.DeckFilePath))
        {
            string path = mapping.DeckFilePath;
            button.MouseEnter += (_, _) => ShowVideoPreview(button, path);
            return;
        }
        if (DeckPanelLayout.IsImageFile(mapping.DeckFilePath) && File.Exists(mapping.DeckFilePath))
        {
            string path = mapping.DeckFilePath;
            var tooltip = new System.Windows.Controls.ToolTip
            {
                Background = WpfBrushes.Transparent,
                BorderBrush = WpfBrushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Placement = PlacementMode.Custom,
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = .5, Color = Colors.Black }
            };
            ConfigureOutsideDeckPreview(tooltip, button);
            button.ToolTip = tooltip;
            button.ToolTipOpening += (_, _) =>
            {
                try
                {
                    var image = DeckPanelLayout.LoadImageThumbnail(path, 360);
                    tooltip.Content = image == null ? new TextBlock { Text = "画像を読み込めません", Foreground = ThemeService.Brush("PrimaryText") } : CreateHoverCard(new WpfImage { Source = image, Width = 240, Height = 180, Stretch = Stretch.Uniform });
                }
                catch { tooltip.Content = new TextBlock { Text = "画像を読み込めません", Foreground = ThemeService.Brush("PrimaryText") }; }
            };
            button.ToolTipClosing += (_, _) => tooltip.Content = null;
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
            return DeckPanelLayout.FileDisplayName(mapping);
        return MainWindow.AssignmentToolTipText(mapping);
    }
    void ClearVideoPreviews()
    {
        videoPreview?.Dispose();
        videoPreview = null;
    }
    void ShowVideoPreview(Button source, string path)
    {
        try
        {
            if (videoPreview?.IsFor(source) != true)
            {
                videoPreview?.Dispose();
                videoPreview = new DeckVideoPreviewPopup(source, path, this);
            }
            videoPreview.Show();
        }
        catch
        {
            try { videoPreview?.Dispose(); } catch { }
            videoPreview = null;
        }
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
            if (DeckIconCatalog.HasIcon(mapping))
                StartDragPreview(mapping);
            try
            {
                internalDeckDragActive = true;
                // Exposing Move lets Explorer relocate the registered source file.
                // Deck is a launcher/reference surface, so external drops are
                // copy-only and never change or delete the registered source.
                System.Windows.DragDrop.DoDragDrop(button, data, DeckPanelLayout.ExternalFileDragEffects);
            }
            finally { internalDeckDragActive = false; StopDragPreview(); }
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
            FrameworkElement? configuredIcon = DeckIconCatalog.CreateVisual(mapping, 42, false);
            var image = configuredIcon == null && DeckPanelLayout.HasRegisteredFile(mapping) ? DeckPanelLayout.LoadFileThumbnail(mapping.DeckFilePath, 128) : null;
            FrameworkElement preview = configuredIcon ?? (image != null
                ? new WpfImage { Source = image, Stretch = Stretch.Uniform }
                : DeckPanelLayout.CreateFileIcon(mapping.DeckFilePath, 42));
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
        DependencyObject? source = e.OriginalSource as DependencyObject;
        if (collapsedToEdge && !IsInsideElement(source, CollapsedMoveHandle))
            return;
        if (!collapsedToEdge && IsInsideButton(source))
            return;
        if (!collapsedToEdge && e.ClickCount == 2)
        {
            ToggleSafeMaximize();
            e.Handled = true;
            return;
        }
        dragging = true;
        dragStart = PointToScreen(e.GetPosition(this));
        dragDpi = VisualTreeHelper.GetDpi(this);
        windowStartLeft = Left;
        windowStartTop = Top;
        panelCard.CaptureMouse();
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
    static bool IsInsideElement(DependencyObject? source, DependencyObject ancestor)
    {
        for (var current = source; current != null;)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }
    internal static bool CanDragPanelFromForTest(DependencyObject source) => !IsInsideButton(source);
    void DragMoved(object sender, MouseEventArgs e)
    {
        if (!dragging || e.LeftButton != MouseButtonState.Pressed)
            return;
        Point current = PointToScreen(e.GetPosition(this));
        Point delta = InputPanelOverlayWindow.PhysicalDragDeltaToDip(current - dragStart, dragDpi);
        double left = windowStartLeft + delta.X;
        double top = windowStartTop + delta.Y;
        if (collapsedToEdge)
        {
            Rect work = VirtualDesktopBounds;
            left = Math.Clamp(left, work.Left, work.Right - ActualWidth);
            top = Math.Clamp(top, work.Top, work.Bottom - ActualHeight);
            collapsedBounds = new Rect(left, top, ActualWidth, ActualHeight);
            collapsedPositionCustomized = true;
        }
        Left = left;
        Top = top;
        positionDirty = true;
        e.Handled = true;
    }
    void DragEnded(object sender, MouseButtonEventArgs e)
    {
        if (!dragging)
            return;
        dragging = false;
        panelCard.ReleaseMouseCapture();
        if (collapsedToEdge)
            PersistCollapsedPosition();
        else
            PersistPosition();
        e.Handled = true;
    }
    void PersistCollapsedPosition()
    {
        if (!collapsedPositionCustomized || collapsedBounds.Width <= 0 || collapsedBounds.Height <= 0)
            return;
        try { saveCollapsedPosition?.Invoke(collapsedBounds.Left, collapsedBounds.Top); } catch { }
    }
    void PersistPosition()
    {
        if (!positionDirty)
            return;
        positionDirty = false;
        Point position = collapsedToEdge ? expandedBounds.TopLeft
            : safeMaximizeRestoreBounds is Rect restore ? restore.TopLeft
            : new Point(Left, Top);
        try { savePosition?.Invoke(position.X, position.Y); } catch { }
    }
    void PersistSize()
    {
        if (safeMaximizeRestoreBounds is Rect fullScreenRestore)
        {
            try { saveSize?.Invoke(layout.Id, fullScreenRestore.Width, fullScreenRestore.Height); } catch { }
            return;
        }
        if (collapsedToEdge)
        {
            if (expandedBounds.Width > 0 && expandedBounds.Height > 0)
            {
                try { saveSize?.Invoke(layout.Id, expandedBounds.Width, expandedBounds.Height); } catch { }
            }
            return;
        }
        if (double.IsFinite(ActualWidth) && double.IsFinite(ActualHeight) && ActualWidth > 0 && ActualHeight > 0)
        {
            try { saveSize?.Invoke(layout.Id, ActualWidth, ActualHeight); } catch { }
        }
    }
    void ResetToDefaultSize()
    {
        safeMaximizeRestoreBounds = null;
        ResizeMode = ResizeMode.CanResize;
        UpdateDeckDimensions();
        UpdateLayout();
        UpdateFullScreenVisual();
        PersistSize();
    }
    internal void MoveAndPersist(double left, double top)
    {
        Left = left;
        Top = top;
        positionDirty = false;
        try { savePosition?.Invoke(left, top); } catch { }
    }
    internal void MoveAndPersistForTest(double left, double top) => MoveAndPersist(left, top);
    internal void ResizeAndPersistForTest(double width, double height)
    {
        double currentScale = Math.Clamp((ActualWidth - OverlayChromeWidth) / Math.Max(1, naturalGridWidth), minimumDeckScale, maximumDeckScale);
        var constrained = AspectLockedSize(width, height, currentScale, true);
        Width = constrained.Width;
        Height = constrained.Height;
        UpdateLayout();
        PersistSize();
    }
    internal void ToggleSafeMaximizeForTest() => ToggleSafeMaximize();
    internal bool IsSafelyMaximizedForTest => safeMaximizeRestoreBounds != null && WindowState == WindowState.Normal;
    internal Rect CurrentMonitorWorkAreaForTest => CurrentMonitorWorkArea();

    void WindowStateChanged(object? sender, EventArgs e)
    {
        if (changingWindowState)
            return;
        if (collapsedToEdge && WindowState != WindowState.Normal)
            ApplyCollapsedBounds(collapsedBounds);
        else if (WindowState == WindowState.Maximized)
            MaximizeWithinWorkArea();
    }

    void ToggleSafeMaximize()
    {
        if (safeMaximizeRestoreBounds is Rect restore)
        {
            safeMaximizeRestoreBounds = null;
            ResizeMode = ResizeMode.CanResize;
            UpdateDeckDimensions(restore.Width, restore.Height);
            ApplyTemporaryBounds(new Rect(restore.Left, restore.Top, Width, Height));
            UpdateFullScreenVisual();
            return;
        }
        MaximizeWithinWorkArea();
    }

    void MaximizeWithinWorkArea()
    {
        if (safeMaximizeRestoreBounds == null)
            safeMaximizeRestoreBounds = new Rect(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
        autoHideTimer.Stop();
        ResizeMode = ResizeMode.NoResize;
        MinWidth = 0;
        MinHeight = 0;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
        ApplyTemporaryBounds(CurrentMonitorWorkArea());
        UpdateFullScreenVisual();
    }

    Rect CurrentMonitorWorkArea()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        var pixels = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        if (handle != IntPtr.Zero && GetWindowRect(handle, out var native))
            return new Rect(
                Left + (pixels.Left - native.Left) / dpi.DpiScaleX,
                Top + (pixels.Top - native.Top) / dpi.DpiScaleY,
                pixels.Width / dpi.DpiScaleX,
                pixels.Height / dpi.DpiScaleY);
        return SystemParameters.WorkArea;
    }

    void ApplyTemporaryBounds(Rect bounds)
    {
        changingWindowState = true;
        try
        {
            if (WindowState != WindowState.Normal)
                WindowState = WindowState.Normal;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            UpdateLayout();
            positionDirty = false;
        }
        finally { changingWindowState = false; }
    }

    void UpdateFullScreenVisual()
    {
        if (FullScreenButton == null || fullScreenGlyph == null)
            return;
        bool fullScreen = safeMaximizeRestoreBounds != null;
        FullScreenButton.ToolTip = fullScreen ? "元の大きさに戻す" : "全画面表示";
        fullScreenGlyph.Text = fullScreen ? "\uE73F" : "\uE740";
    }
    internal int ResizeHitTestForTest(Point point) => ResizeCornerHit(point.X, point.Y, ActualWidth, ActualHeight, ResizeCornerSize);
    static int ResizeCornerHit(double x, double y, double width, double height, double corner)
    {
        bool left = x >= 0 && x < corner, right = x <= width && x > width - corner;
        bool top = y >= 0 && y < corner, bottom = y <= height && y > height - corner;
        if (left && top) return HtTopLeft;
        if (right && top) return HtTopRight;
        if (left && bottom) return HtBottomLeft;
        if (right && bottom) return HtBottomRight;
        return 0;
    }
    internal static Point InitialPosition(AppConfig config, double width, double height, DeckLayoutDefinition? layout = null)
    {
        double defaultLeft = Math.Max(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Right - width - 24);
        double defaultTop = Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - height - 24);
        if ((layout?.PanelLeft ?? config.DeckPanelLeft) is not double savedLeft
            || (layout?.PanelTop ?? config.DeckPanelTop) is not double savedTop
            || !double.IsFinite(savedLeft) || !double.IsFinite(savedTop))
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
        ApplyRoundedWindowRegion();
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
        panelCard.Background = FlatSurfaceBrush(panelTone, alpha);
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
        if (msg == WmSysCommand)
        {
            int command = unchecked((int)wParam.ToInt64()) & ScCommandMask;
            if (command == ScMaximize)
            {
                MaximizeWithinWorkArea();
                handled = true;
                return IntPtr.Zero;
            }
            if (command == ScRestore && safeMaximizeRestoreBounds != null)
            {
                ToggleSafeMaximize();
                handled = true;
                return IntPtr.Zero;
            }
        }
        if (msg == WmEnterSizeMove)
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            if (GetWindowRect(hwnd, out var currentRect))
                BeginInteractiveSizing((currentRect.Right - currentRect.Left) / dpi.DpiScaleX, (currentRect.Bottom - currentRect.Top) / dpi.DpiScaleY);
            else
                BeginInteractiveSizing(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
        }
        if (msg == WmSizing && lParam != IntPtr.Zero)
        {
            var proposed = Marshal.PtrToStructure<NativeWindowRect>(lParam);
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            double proposedWidth = (proposed.Right - proposed.Left) / dpi.DpiScaleX;
            double proposedHeight = (proposed.Bottom - proposed.Top) / dpi.DpiScaleY;
            int edge = wParam.ToInt32();
            var constrained = ConstrainInteractiveSize(proposedWidth, proposedHeight, edge);
            int targetWidth = Math.Max(1, (int)Math.Round(constrained.Width * dpi.DpiScaleX));
            int targetHeight = Math.Max(1, (int)Math.Round(constrained.Height * dpi.DpiScaleY));
            if (edge is WmszLeft or WmszTopLeft or WmszBottomLeft)
                proposed.Left = proposed.Right - targetWidth;
            else
                proposed.Right = proposed.Left + targetWidth;
            if (edge is WmszTop or WmszTopLeft or WmszTopRight)
                proposed.Top = proposed.Bottom - targetHeight;
            else
                proposed.Bottom = proposed.Top + targetHeight;
            Marshal.StructureToPtr(proposed, lParam, false);
            handled = true;
            return new IntPtr(1);
        }
        if (msg == WmNcHitTest && GetWindowRect(hwnd, out var windowRect))
        {
            int screenX = unchecked((short)(long)lParam);
            int screenY = unchecked((short)((long)lParam >> 16));
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            double localX = (screenX - windowRect.Left) / dpi.DpiScaleX;
            double localY = (screenY - windowRect.Top) / dpi.DpiScaleY;
            int hit = ResizeCornerHit(localX, localY, ActualWidth, ActualHeight, ResizeCornerSize);
            if (hit != 0)
            {
                handled = true;
                return new IntPtr(hit);
            }
        }
        if (msg == WmExitSizeMove)
        {
            interactiveSizing = false;
            cornerResizeWidthDriven = null;
            ApplyRoundedWindowRegion();
            PersistPosition();
            PersistSize();
        }
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
    [StructLayout(LayoutKind.Sequential)]
    struct NativeWindowRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
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
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetWindowRect(IntPtr hwnd, out NativeWindowRect rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref NativeDropPoint point);
    [DllImport("shell32.dll")] static extern void DragAcceptFiles(IntPtr hwnd, bool accept);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder? fileName, int bufferLength);
    [DllImport("shell32.dll")] static extern bool DragQueryPoint(IntPtr drop, out NativeDropPoint point);
    [DllImport("shell32.dll")] static extern void DragFinish(IntPtr drop);
    static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index));
    static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value) => IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, value) : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));
}
