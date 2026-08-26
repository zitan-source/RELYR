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

internal sealed partial class DeckPanelOverlayWindow : Window
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
    const double DefaultPanelCornerRadius = 14;
    const double PanelBorderThickness = 1;
    const double DefaultPanelInset = 12;
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
    static readonly TimeSpan PresentationFadeDuration = TimeSpan.FromMilliseconds(155);
    static readonly TimeSpan PresentationScaleDuration = TimeSpan.FromMilliseconds(135);
    static readonly TimeSpan PresentationFadeWatchdog = TimeSpan.FromMilliseconds(230);
    const double PresentationDepartureScale = .975;
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
    readonly Action? stateChanged;
    bool hoverPreviewsEnabled;
    DeckAutoDismissBehavior afterActionBehavior;
    DeckAutoDismissBehavior pointerLeaveBehavior;
    DeckAutoDismissBehavior pendingAutoDismissBehavior;
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
    System.Windows.Media.Brush? deckReorderTargetOriginalBorderBrush;
    Thickness deckReorderTargetOriginalBorderThickness;
    Point fileDragStart;
    MediaPlayer? hoverAudioPlayer;
    Button? hoverAudioSource;
    Button? pendingHoverAudioSource;
    string pendingHoverAudioPath = "";
    readonly DispatcherTimer hoverAudioStartTimer = new() { Interval = HoverAudioDelay };
    readonly DispatcherTimer autoHideTimer = new() { Interval = PointerLeaveAutoHideDelay };
    readonly DispatcherTimer presentationFadeTimer = new() { Interval = PresentationFadeWatchdog };
    DeckVideoPreviewPopup? videoPreview;
    Border? monitorControlPanel;
    readonly object brightnessWheelSync = new();
    double? pendingBrightnessWheelPercent;
    int brightnessWheelWorkerActive;
    Slider? monitorControlSlider;
    TextBlock? monitorControlValue;
    Button? monitorControlSource;
    DeckMonitorInteraction monitorControlInteraction;
    bool updatingMonitorControl;
    CancellationTokenSource? previewLoadCancellation;
    Point dragStart;
    double windowStartLeft, windowStartTop;
    DpiScale dragDpi;

    internal IReadOnlyList<Button> DeckButtons => deckButtons;
    internal int VideoPreviewCountForTest => videoPreview == null ? 0 : 1;
    internal bool? VideoPreviewUsesSourceHoverForTest => videoPreview?.SourceHoverEnabled;
    internal bool AudioPreviewActiveForTest => hoverAudioPlayer != null;
    internal Func<System.Drawing.Point>? CursorPositionProviderForTest { get; set; }
    internal Func<bool>? PointerButtonsPressedProviderForTest { get; set; }
    internal Button CloseButton { get; private set; } = null!;
    internal Button ResetSizeButton { get; private set; } = null!;
    internal Button PinButton { get; private set; } = null!;
    internal Button FullScreenButton { get; private set; } = null!;
    internal Button MoreButton { get; private set; } = null!;
    internal FrameworkElement CollapsedMoveHandle { get; private set; } = null!;
    internal bool IsPinnedForTest => layout.PanelPinned;
    internal bool PointerAutoHideArmedForTest => pointerEnteredSinceShown;
    internal void ArmPointerAutoHideForTest() => pointerEnteredSinceShown = true;
    internal void RequestPointerAutoHideForTest() => ScheduleAutoDismiss(pointerLeaveBehavior, PointerLeaveAutoHideDelay, true);
    internal void SetDragActiveForTest(bool value) => internalDeckDragActive = value;
    internal string AutoHideStateForTest => $"visible={IsVisible}, pinned={layout.PanelPinned}, collapsed={collapsedToEdge}, timer={autoHideTimer.IsEnabled}, behavior={pointerLeaveBehavior}, pending={pendingAutoDismissBehavior}, outside={IsCursorOutsideDeckWindow()}, suspended={ShouldSuspendAutoHide()}, enabled={IsEnabled}, dragging={dragging}, internalDrag={internalDeckDragActive}, reorder={deckReorderDragging}, fileDrag={fileDragButton != null}, menus={openContextMenus}";
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
    double PanelCornerRadius => Math.Clamp(double.IsFinite(layout.PanelCornerRadius) ? layout.PanelCornerRadius : DefaultPanelCornerRadius, 0, 24);
    double PanelInset => Math.Clamp(double.IsFinite(layout.PanelPadding) ? layout.PanelPadding : DefaultPanelInset, 4, 24);
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
    double renderedPanelInset;
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
    int presentationGeneration;
    int presentationFadeTimerGeneration;
    bool presentationFadePending;

    enum DeckBackdropMode
    {
        Pending,
        SystemBackdrop,
        AccentAcrylicOnly,
        SolidFallback
    }
    WpfColor panelTone;

    internal DeckPanelOverlayWindow(AppConfig config, Action<Mapping>? executeAction, int opacityPercent = 96, Action<double, double>? positionChanged = null, DeckLayoutDefinition? selectedLayout = null, Action<string, double, double>? sizeChanged = null, Action<string, bool>? pinnedChanged = null, Action<double, double>? collapsedPositionChanged = null, Action? presentationStateChanged = null)
    {
        execute = executeAction;
        savePosition = positionChanged;
        saveCollapsedPosition = collapsedPositionChanged;
        saveSize = sizeChanged;
        savePinned = pinnedChanged;
        stateChanged = presentationStateChanged;
        hoverAudioStartTimer.Tick += HoverAudioStartTimerTick;
        autoHideTimer.Tick += AutoHideTimerTick;
        presentationFadeTimer.Tick += PresentationFadeTimerTick;
        hoverPreviewsEnabled = config.DeckHoverPreviewsEnabled;
        afterActionBehavior = config.DeckAfterActionBehavior;
        pointerLeaveBehavior = config.DeckPointerLeaveBehavior;
        layout = selectedLayout ?? DeckPanelLayout.DefaultLayout(config) ?? new DeckLayoutDefinition();
        renderedColumns = layout.Columns;
        renderedRows = layout.Rows;
        renderedPanelInset = PanelInset;
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
        root.PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (monitorControlPanel != null && !MonitorControlContains(args.OriginalSource as DependencyObject))
                CloseMonitorControl();
        };
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
        Closed += (_, _) => { CloseMonitorControl(); ThemeService.ThemeChanged -= ThemeChanged; autoHideTimer.Stop(); presentationFadeTimer.Stop(); ReleaseOwnedMouseCapture(); CancelDeferredPreviews(); ClearDeckReorderTarget(); StopDragPreview(); ClearVideoPreviews(); CancelPendingHoverAudio(); StopHoverAudio(); StopShellFileDrop(); PersistPosition(); PersistCollapsedPosition(); PersistSize(); };
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
    double OverlayChromeWidth => PanelInset * 2 + PanelBorderThickness * 2;
    double OverlayChromeHeight => PanelInset * 2 + PanelBorderThickness * 2 + HeaderHeight + HeaderToGridGap;
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
        presentationGeneration++;
        presentationFadePending = false;
        presentationFadeTimer.Stop();
        autoHideTimer.Stop();
        pointerEnteredSinceShown = false;
        ReleaseOwnedMouseCapture();
        PersistPosition();
        PersistCollapsedPosition();
        PersistSize();
        // Hide first so a pointer that is still over a Deck button cannot raise
        // another MouseEnter and recreate a media preview during teardown.
        Hide();
        ResetPresentationVisualsBestEffort();
        panelCard.IsHitTestVisible = true;
        NotifyPresentationStateChanged();
        ClearVideoPreviews();
        CancelPendingHoverAudio();
        StopHoverAudio();
    }

    internal void RequestHideForReuse()
    {
        if (!IsVisible || !UiMotionService.Enabled)
        {
            HideForReuse();
            return;
        }

        int generation = ++presentationGeneration;
        presentationFadePending = true;
        presentationFadeTimerGeneration = generation;
        autoHideTimer.Stop();
        pointerEnteredSinceShown = false;
        ReleaseOwnedMouseCapture();
        PersistPosition();
        PersistCollapsedPosition();
        PersistSize();
        panelCard.IsHitTestVisible = false;
        ClearVideoPreviews();
        CancelPendingHoverAudio();
        StopHoverAudio();

        if (!FreezePresentationContentForDeparture())
        {
            HideForReuse();
            return;
        }

        presentationFadeTimer.Stop();
        presentationFadeTimer.Start();
        var (departureScale, _) = UiMotionService.MutableMotionTransform(root);
        UiMotionService.AnimateDouble(
            "deck-hide-scale-x",
            departureScale,
            ScaleTransform.ScaleXProperty,
            PresentationDepartureScale,
            PresentationScaleDuration,
            UiMotionService.ResponsiveEaseOut());
        UiMotionService.AnimateDouble(
            "deck-hide-scale-y",
            departureScale,
            ScaleTransform.ScaleYProperty,
            PresentationDepartureScale,
            PresentationScaleDuration,
            UiMotionService.ResponsiveEaseOut());
        bool started = UiMotionService.AnimateDouble(
            "deck-hide-opacity",
            this,
            UIElement.OpacityProperty,
            .12,
            PresentationFadeDuration,
            UiMotionService.ResponsiveEaseOut(),
            completed: () => CompletePresentationFade(generation));
        if (!started)
            CompletePresentationFade(generation);
    }

    bool FreezePresentationContentForDeparture()
        => UiMotionService.TryRunSafely("deck-hide-content-freeze", () =>
        {
            var (scale, translate) = UiMotionService.MutableMotionTransform(root);
            double rootOpacity = root.Opacity;
            double scaleX = scale.ScaleX;
            double scaleY = scale.ScaleY;
            double translateX = translate.X;
            double translateY = translate.Y;
            UiMotionService.StopAndSetDouble(root, UIElement.OpacityProperty, rootOpacity);
            UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleXProperty, scaleX);
            UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleYProperty, scaleY);
            UiMotionService.StopAndSetDouble(translate, TranslateTransform.XProperty, translateX);
            UiMotionService.StopAndSetDouble(translate, TranslateTransform.YProperty, translateY);
        });

    void CompletePresentationFade(int generation)
    {
        if (!presentationFadePending || generation != presentationGeneration)
            return;
        HideForReuse();
    }

    void PresentationFadeTimerTick(object? sender, EventArgs e)
    {
        presentationFadeTimer.Stop();
        CompletePresentationFade(presentationFadeTimerGeneration);
    }

    void ResetPresentationVisualsBestEffort()
    {
        try
        {
            UiMotionService.StopAndSetDouble(this, UIElement.OpacityProperty, 1);
            UiMotionService.StopAndSetDouble(root, UIElement.OpacityProperty, 1);
            var (scale, translate) = UiMotionService.MutableMotionTransform(root);
            UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleXProperty, 1);
            UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleYProperty, 1);
            UiMotionService.StopAndSetDouble(translate, TranslateTransform.XProperty, 0);
            UiMotionService.StopAndSetDouble(translate, TranslateTransform.YProperty, 0);
        }
        catch
        {
            try
            {
                Opacity = 1;
                root.Opacity = 1;
                root.RenderTransform = Transform.Identity;
            }
            catch { }
        }
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
    internal bool IsPresentationHiding => presentationFadePending;
    internal bool PresentationMotionActiveForTest => HasAnimatedProperties || root.HasAnimatedProperties
        || root.RenderTransform is TransformGroup group && group.Children.Any(transform => transform.HasAnimatedProperties);
    internal bool DepartureUsesScaleFadeOnlyForTest => presentationFadePending
        && HasAnimatedProperties
        && root.RenderTransform is TransformGroup group
        && group.Children.OfType<ScaleTransform>().FirstOrDefault() is { HasAnimatedProperties: true }
        && group.Children.OfType<TranslateTransform>().FirstOrDefault() is { HasAnimatedProperties: false };
    internal double PresentationScaleForTest => root.RenderTransform is TransformGroup group
        ? group.Children.OfType<ScaleTransform>().FirstOrDefault()?.ScaleX ?? 1
        : 1;
    internal bool PresentationContentHitTestVisibleForTest => panelCard.IsHitTestVisible;
    internal double PresentationOffsetForTest => root.RenderTransform is TransformGroup group
        ? group.Children.OfType<TranslateTransform>().FirstOrDefault()?.Y ?? 0
        : 0;
    internal bool EdgeExpansionArmedForTest => edgeExpansionArmed;
    internal void ArmEdgeExpansionForTest()
    {
        // The test hook represents the state after the asynchronous collapsed-
        // bounds transition has completed. Do not leave the transition gate at
        // the scheduler-dependent value from the preceding ContextIdle callback.
        collapsedPointerTransitionPending = false;
        edgeExpansionArmed = true;
    }
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
        Rect previousBounds = new(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
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
        PlayDeckStateReveal(previousBounds, new Rect(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height));
        NotifyPresentationStateChanged();
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
        var pointerAtCollapse = CurrentCursorPosition();
        bool pointerStartedOutsideCollapsedBounds = !collapsedBounds.Contains(new Point(pointerAtCollapse.X, pointerAtCollapse.Y));
        ApplyCollapsedBounds(collapsedBounds);
        PlayDeckStateReveal(expandedBounds, collapsedBounds);
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
            var currentPointer = CurrentCursorPosition();
            bool pointerActuallyMoved = currentPointer.X != pointerAtCollapse.X || currentPointer.Y != pointerAtCollapse.Y;
            edgeExpansionArmed = pointerStartedOutsideCollapsedBounds || (pointerActuallyMoved && IsCursorOutsideDeckWindow());
        }));
        NotifyPresentationStateChanged();
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
        int generation = ++presentationGeneration;
        presentationFadePending = false;
        presentationFadeTimer.Stop();
        panelCard.IsHitTestVisible = true;
        autoHideTimer.Stop();
        pointerEnteredSinceShown = false;
        autoHideRequiresPointerOutside = true;
        if (!UiMotionService.Enabled)
            ResetPresentationVisualsBestEffort();
        else
        {
            UiMotionService.RunSafely("deck-presentation-prepare", () =>
            {
                var (scale, translate) = UiMotionService.MutableMotionTransform(root);
                bool inFlight = HasAnimatedProperties || root.HasAnimatedProperties || scale.HasAnimatedProperties || translate.HasAnimatedProperties;
                if (!inFlight)
                {
                    UiMotionService.StopAndSetDouble(this, UIElement.OpacityProperty, .74);
                    UiMotionService.StopAndSetDouble(root, UIElement.OpacityProperty, .72);
                    UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleXProperty, .985);
                    UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleYProperty, .985);
                    UiMotionService.StopAndSetDouble(translate, TranslateTransform.YProperty, 10);
                }
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => BeginDeckPresentationReveal(generation)));
            });
        }
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (IsVisible && IsMouseOver)
                pointerEnteredSinceShown = true;
        }));
    }

    void BeginDeckPresentationReveal(int generation)
    {
        if (!IsVisible || generation != presentationGeneration)
            return;
        if (!UiMotionService.Enabled)
        {
            ResetPresentationVisualsBestEffort();
            return;
        }
        UiMotionService.RunSafely("deck-presentation-reveal", () =>
        {
            var (scale, translate) = UiMotionService.MutableMotionTransform(root);
            UiMotionService.AnimateDouble("deck-show-window-opacity", this, UIElement.OpacityProperty, 1, TimeSpan.FromMilliseconds(190));
            UiMotionService.AnimateDouble("deck-show-root-opacity", root, UIElement.OpacityProperty, 1, TimeSpan.FromMilliseconds(205));
            UiMotionService.AnimateDouble("deck-show-root-scale-x", scale, ScaleTransform.ScaleXProperty, 1, TimeSpan.FromMilliseconds(220));
            UiMotionService.AnimateDouble("deck-show-root-scale-y", scale, ScaleTransform.ScaleYProperty, 1, TimeSpan.FromMilliseconds(220));
            UiMotionService.AnimateDouble("deck-show-root-translate", translate, TranslateTransform.YProperty, 0, TimeSpan.FromMilliseconds(220));
        });
    }

    void PlayDeckStateReveal(Rect fromBounds, Rect toBounds)
    {
        if (!UiMotionService.Enabled)
        {
            ResetPresentationVisualsBestEffort();
            return;
        }
        UiMotionService.RunSafely("deck-state-reveal", () =>
        {
            var (scale, translate) = UiMotionService.MutableMotionTransform(root);
            bool inFlight = root.HasAnimatedProperties || scale.HasAnimatedProperties || translate.HasAnimatedProperties;
            if (!inFlight)
            {
                var fromCenter = new Point(fromBounds.Left + fromBounds.Width / 2, fromBounds.Top + fromBounds.Height / 2);
                var toCenter = new Point(toBounds.Left + toBounds.Width / 2, toBounds.Top + toBounds.Height / 2);
                double x = Math.Abs(fromCenter.X - toCenter.X) < 1 ? 0 : Math.Sign(fromCenter.X - toCenter.X) * 10;
                double y = Math.Abs(fromCenter.Y - toCenter.Y) < 1 ? 0 : Math.Sign(fromCenter.Y - toCenter.Y) * 10;
                UiMotionService.StopAndSetDouble(root, UIElement.OpacityProperty, .76);
                UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleXProperty, .97);
                UiMotionService.StopAndSetDouble(scale, ScaleTransform.ScaleYProperty, .97);
                UiMotionService.StopAndSetDouble(translate, TranslateTransform.XProperty, x);
                UiMotionService.StopAndSetDouble(translate, TranslateTransform.YProperty, y);
            }
            UiMotionService.AnimateDouble("deck-state-opacity", root, UIElement.OpacityProperty, 1, TimeSpan.FromMilliseconds(180));
            UiMotionService.AnimateDouble("deck-state-scale-x", scale, ScaleTransform.ScaleXProperty, 1, TimeSpan.FromMilliseconds(210));
            UiMotionService.AnimateDouble("deck-state-scale-y", scale, ScaleTransform.ScaleYProperty, 1, TimeSpan.FromMilliseconds(210));
            UiMotionService.AnimateDouble("deck-state-translate-x", translate, TranslateTransform.XProperty, 0, TimeSpan.FromMilliseconds(210));
            UiMotionService.AnimateDouble("deck-state-translate-y", translate, TranslateTransform.YProperty, 0, TimeSpan.FromMilliseconds(210));
        });
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
        button.PreviewMouseWheel += DeckMonitorMouseWheel;
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
        => RefreshDeckSlots([firstSlot, secondSlot]);

    internal void RefreshDeckSlots(IEnumerable<int> slots)
    {
        var deferredPreviews = new List<Button>();
        foreach (int slot in slots.Distinct())
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

    internal void Refresh(int opacityPercent, bool previewsEnabled, DeckAutoDismissBehavior? afterAction = null, DeckAutoDismissBehavior? pointerLeave = null)
    {
        RefreshAppearance(opacityPercent, previewsEnabled, afterAction, pointerLeave);
        if (deckButtonsBuilt)
            BuildDeckButtons();
        else
            QueueDeckButtonBuild();
    }

    internal void RefreshLayoutPreview(int opacityPercent, bool previewsEnabled, DeckAutoDismissBehavior? afterAction = null, DeckAutoDismissBehavior? pointerLeave = null)
    {
        int previousCount = deckButtons.Count;
        RefreshAppearance(opacityPercent, previewsEnabled, afterAction, pointerLeave);
        if (!deckButtonsBuilt)
        {
            QueueDeckButtonBuild();
            return;
        }
        int desiredCount = DeckPanelLayout.VisibleSlotCount(layout);
        while (deckButtons.Count > desiredCount)
        {
            int last = deckButtons.Count - 1;
            ClearVideoPreviewFor(deckButtons[last]);
            deckButtons.RemoveAt(last);
            deckGrid.Children.RemoveAt(last);
        }
        while (deckButtons.Count < desiredCount)
        {
            var entry = CreateDeckButtonCell(deckButtons.Count + 1);
            deckButtons.Add(entry.Button);
            deckGrid.Children.Add(entry.Cell);
        }
        // Existing button content and monitor subscriptions are intentionally
        // retained. Only the changed tail is touched while a layout Slider is
        // moving, so a 18x18 Deck never recreates all 324 cells per tick.
        if (previousCount != desiredCount)
            UpdateHeaderLayout();
    }

    internal void RefreshAppearance(int opacityPercent, bool previewsEnabled, DeckAutoDismissBehavior? afterAction = null, DeckAutoDismissBehavior? pointerLeave = null)
    {
        if (!UiMotionService.Enabled)
        {
            if (presentationFadePending)
                HideForReuse();
            else
                ResetPresentationVisualsBestEffort();
        }
        hoverPreviewsEnabled = previewsEnabled;
        if (afterAction != null || pointerLeave != null)
            autoHideTimer.Stop();
        if (afterAction is DeckAutoDismissBehavior newAfterAction)
            afterActionBehavior = newAfterAction;
        if (pointerLeave is DeckAutoDismissBehavior newPointerLeave)
            pointerLeaveBehavior = newPointerLeave;
        SetGlassOpacity(opacityPercent);
        panelCard.Padding = new Thickness(PanelInset);
        panelCard.CornerRadius = new CornerRadius(PanelCornerRadius);
        dragArea.Margin = new Thickness(-PanelInset, 0, -PanelInset, 0);
        glassButtonStyle = null;
        ApplyPanelColor();
        Title = "RELYR Deck - " + layout.Name;
        headerTitle.Text = layout.Name;
        dragArea.ToolTip = layout.Name;
        bool dimensionsChanged = renderedColumns != layout.Columns || renderedRows != layout.Rows;
        bool panelInsetChanged = Math.Abs(renderedPanelInset - PanelInset) > .01;
        renderedColumns = layout.Columns;
        renderedRows = layout.Rows;
        renderedPanelInset = PanelInset;
        if (collapsedToEdge)
            resetSizeWhenExpanded |= dimensionsChanged || panelInsetChanged;
        else
            UpdateDeckDimensions(dimensionsChanged || panelInsetChanged ? layout.PanelWidth : Width, dimensionsChanged || panelInsetChanged ? layout.PanelHeight : Height);
        UpdateHeaderLayout();
        ApplyRoundedPanelClip();
        ApplyRoundedWindowRegion();
    }

    void PanelCard_MouseEnter(object sender, MouseEventArgs e)
        => HandlePanelPointerEntered();

    void HandlePanelPointerEntered()
    {
        // Restoring or resizing the native Deck window can raise a synthetic
        // WPF MouseEnter even though the physical pointer is still outside.
        // Such a transition must not cancel a pointer-leave hide/collapse that
        // was scheduled immediately after the bounds change.
        if (!collapsedToEdge && IsCursorOutsideDeckWindow())
            return;
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
        if (pointerEnteredSinceShown)
            ScheduleAutoDismiss(pointerLeaveBehavior, PointerLeaveAutoHideDelay, true);
    }

    void ScheduleAutoDismiss(DeckAutoDismissBehavior behavior, TimeSpan delay, bool requirePointerOutside)
    {
        autoHideTimer.Stop();
        if (behavior == DeckAutoDismissBehavior.StayVisible || layout.PanelPinned || !IsVisible)
            return;
        pendingAutoDismissBehavior = behavior;
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
            ScheduleAutoDismiss(pendingAutoDismissBehavior, PointerLeaveAutoHideDelay, autoHideRequiresPointerOutside);
            return;
        }
        if (autoHideRequiresPointerOutside && !IsCursorOutsideDeckWindow())
            return;
        if (pendingAutoDismissBehavior == DeckAutoDismissBehavior.Hide)
            RequestHideForReuse();
        else if (pendingAutoDismissBehavior == DeckAutoDismissBehavior.CollapseToEdge)
            CollapseToEdge();
    }

    bool ShouldSuspendAutoHide() =>
        !IsEnabled || dragging || internalDeckDragActive || deckReorderDragging || fileDragButton != null || openContextMenus > 0 ||
        (PointerButtonsPressedProviderForTest?.Invoke() ??
            (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed || Mouse.MiddleButton == MouseButtonState.Pressed));

    void TrackContextMenu(System.Windows.Controls.ContextMenu menu)
    {
        menu.Opened += (_, _) => { openContextMenus++; autoHideTimer.Stop(); };
        menu.Closed += (_, _) =>
        {
            openContextMenus = Math.Max(0, openContextMenus - 1);
            if (pointerEnteredSinceShown && IsCursorOutsideDeckWindow())
                ScheduleAutoDismiss(pointerLeaveBehavior, PointerLeaveAutoHideDelay, true);
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
            ToolTip = "初期サイズに戻す",
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
            ToolTip = "最大化",
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
        close.ToolTip = "Deckを非表示";
        close.Click += (_, _) => RequestHideForReuse();
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
        var store = new System.Windows.Controls.MenuItem { Header = "画面端に折りたたむ", IsCheckable = true };
        store.Click += (_, _) =>
        {
            SetPinned(!store.IsChecked);
            if (store.IsChecked)
                _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(CollapseToEdge));
        };
        var reset = new System.Windows.Controls.MenuItem { Header = "初期サイズに戻す" };
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
        PinButton.ToolTip = layout.PanelPinned ? "固定表示を解除" : "Deckを固定表示";
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
        glassButtonStyle = CreateGlassButtonStyle(sharedRadius);
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
    static Style CreateGlassButtonStyle(CornerRadius cornerRadius)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.CursorProperty, WpfCursors.Hand));
        var template = new ControlTemplate(typeof(Button));
        var root = new FrameworkElementFactory(typeof(Grid)) { Name = "HoverRoot" };
        var surface = new FrameworkElementFactory(typeof(Border)) { Name = "GlassSurface" };
        surface.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.Control.BackgroundProperty));
        surface.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(System.Windows.Controls.Control.BorderBrushProperty));
        surface.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(System.Windows.Controls.Control.BorderThicknessProperty));
        surface.SetValue(Border.CornerRadiusProperty, cornerRadius);
        surface.SetValue(Border.PaddingProperty, new TemplateBindingExtension(System.Windows.Controls.Control.PaddingProperty));
        root.AppendChild(surface);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(System.Windows.Controls.Control.HorizontalContentAlignmentProperty));
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, new TemplateBindingExtension(System.Windows.Controls.Control.VerticalContentAlignmentProperty));
        content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        content.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
        content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        root.AppendChild(content);
        var dropBadge = new FrameworkElementFactory(typeof(Border)) { Name = "DropTargetBadge" };
        dropBadge.SetValue(FrameworkElement.WidthProperty, 26d);
        dropBadge.SetValue(FrameworkElement.HeightProperty, 26d);
        dropBadge.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
        dropBadge.SetValue(Border.BackgroundProperty, new SolidColorBrush(DeckAccent));
        dropBadge.SetValue(Border.BorderBrushProperty, WpfBrushes.White);
        dropBadge.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        dropBadge.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        dropBadge.SetValue(FrameworkElement.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
        dropBadge.SetValue(UIElement.OpacityProperty, 0d);
        dropBadge.SetValue(UIElement.IsHitTestVisibleProperty, false);
        dropBadge.SetValue(System.Windows.Controls.Panel.ZIndexProperty, 2);
        var dropGlyph = new FrameworkElementFactory(typeof(TextBlock));
        dropGlyph.SetValue(TextBlock.TextProperty, "↓");
        dropGlyph.SetValue(TextBlock.FontSizeProperty, 17d);
        dropGlyph.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        dropGlyph.SetValue(TextBlock.ForegroundProperty, WpfBrushes.White);
        dropGlyph.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        dropGlyph.SetValue(FrameworkElement.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
        dropGlyph.SetValue(FrameworkElement.MarginProperty, new Thickness(0, -2, 0, 0));
        dropBadge.AppendChild(dropGlyph);
        root.AppendChild(dropBadge);
        template.VisualTree = root;
        template.Triggers.Add(new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
            Setters = { new Setter(UIElement.OpacityProperty, .98d, "GlassSurface") }
        });
        template.Triggers.Add(new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true, Setters = { new Setter(UIElement.OpacityProperty, .84d, "GlassSurface") } });
        template.Triggers.Add(new Trigger { Property = UIElement.IsEnabledProperty, Value = false, Setters = { new Setter(UIElement.OpacityProperty, .42d) } });
        style.Setters.Add(new Setter(System.Windows.Controls.Control.TemplateProperty, template));
        return style;
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
        (MainWindow.MappingInterceptsInput(mapping) || DeckMonitorCatalog.IsMonitor(mapping.DeckMonitor) || DeckPanelLayout.HasRegisteredFile(mapping) || !string.IsNullOrWhiteSpace(mapping.Description));
    static WpfColor WithAlpha(WpfColor color, byte alpha) => WpfColor.FromArgb(alpha, color.R, color.G, color.B);
    static WpfColor Mix(WpfColor first, WpfColor second, double amount) => WpfColor.FromArgb(255, (byte)(first.R + (second.R - first.R) * amount), (byte)(first.G + (second.G - first.G) * amount), (byte)(first.B + (second.B - first.B) * amount));

    void DeckButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int slot })
            return;
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        if (mapping == null || mapping.Kind == ActionKind.Gesture)
            return;
        if (DeckMonitorCatalog.TryGet(mapping.DeckMonitor, out var monitor))
        {
            OpenMonitorInteraction((Button)sender, monitor);
            return;
        }
        if (MainWindow.MappingInterceptsInput(mapping))
        {
            execute?.Invoke(mapping);
            ScheduleAutoDismiss(afterActionBehavior, ActionAutoHideDelay, false);
        }
        else if (DeckPanelLayout.IsAudioFile(mapping.DeckFilePath))
            PlayFileAudio(mapping.DeckFilePath, (Button)sender, requireHoverEnabled: false);
        else if (DeckPanelLayout.IsVideoFile(mapping.DeckFilePath) && File.Exists(mapping.DeckFilePath))
            ShowVideoPreview((Button)sender, mapping.DeckFilePath, hoverPreviewsEnabled);
    }

    void DeckMonitorMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Button { Tag: int slot })
            return;
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        if (!DeckMonitorCatalog.TryGet(mapping?.DeckMonitor, out var monitor)
            || !SupportsMonitorWheelAdjustment(monitor.Interaction))
            return;

        if (monitor.Interaction == DeckMonitorInteraction.Brightness)
        {
            double? currentBrightness = InteractiveMonitorPercent((Button)sender, monitor.Interaction);
            if (currentBrightness is not double brightness)
                return;
            double requestedBrightness = WheelAdjustedPercent(brightness, e.Delta, 2);
            ShowMonitorControl((Button)sender, monitor, requestedBrightness);
            ApplyInteractiveMonitorValue((Button)sender, monitor, requestedBrightness);
            QueueBrightnessValue(requestedBrightness);
            e.Handled = true;
            return;
        }

        if (!SystemControlService.TryGetVolume(false, out double current, out bool muted))
            return;

        double requested = WheelAdjustedPercent(current, e.Delta, 2);
        if (!SystemControlService.TrySetVolume(false, requested))
            return;
        ShowMonitorControl((Button)sender, monitor, requested, muted);
        ApplyInteractiveMonitorValue((Button)sender, monitor, requested);
        e.Handled = true;
        SystemMonitorService.Shared.RequestRefresh();
    }

    internal static bool SupportsMonitorWheelAdjustment(DeckMonitorInteraction interaction)
        => interaction is DeckMonitorInteraction.Volume or DeckMonitorInteraction.Brightness;

    double? InteractiveMonitorPercent(Button source, DeckMonitorInteraction interaction)
    {
        if (ReferenceEquals(source, monitorControlSource)
            && monitorControlInteraction == interaction
            && monitorControlSlider is { IsEnabled: true } openSlider)
            return openSlider.Value;
        return source.Content is DeckMonitorView view ? view.CurrentPercent : null;
    }

    static void ApplyInteractiveMonitorValue(Button source, DeckMonitorDefinition monitor, double requested)
    {
        if (source.Content is DeckMonitorView view)
            view.ApplyInteractivePercent(requested, monitor.Interaction == DeckMonitorInteraction.Microphone ? "MIC" : monitor.Name);
    }

    void QueueBrightnessValue(double requested)
    {
        lock (brightnessWheelSync)
            pendingBrightnessWheelPercent = requested;
        StartBrightnessWheelWorker();
    }

    void StartBrightnessWheelWorker()
    {
        if (Interlocked.CompareExchange(ref brightnessWheelWorkerActive, 1, 0) != 0)
            return;
        _ = ProcessBrightnessWheelAdjustmentsAsync();
    }

    async Task ProcessBrightnessWheelAdjustmentsAsync()
    {
        try
        {
            // Coalesce a physical wheel burst so the WMI brightness provider is
            // never queried once per raw wheel message on the UI thread.
            await Task.Delay(45).ConfigureAwait(false);
            while (true)
            {
                double? requested;
                lock (brightnessWheelSync)
                {
                    requested = pendingBrightnessWheelPercent;
                    pendingBrightnessWheelPercent = null;
                }
                if (requested is not double percent)
                    break;
                bool changed = SystemControlService.TrySetBrightness(percent);
                if (changed)
                    SystemMonitorService.Shared.RequestRefresh();
                await Task.Delay(45).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            LifecycleDiagnostics.Write("deck-brightness-wheel-failed", error.ToString());
        }
        finally
        {
            Volatile.Write(ref brightnessWheelWorkerActive, 0);
            bool pending;
            lock (brightnessWheelSync)
                pending = pendingBrightnessWheelPercent.HasValue;
            if (pending)
                StartBrightnessWheelWorker();
        }
    }

    internal static double WheelAdjustedPercent(double current, int wheelDelta, double step)
    {
        int direction = Math.Sign(wheelDelta);
        int notches = Math.Max(1, Math.Abs(wheelDelta) / 120);
        return Math.Clamp(current + direction * Math.Abs(step) * notches, 0, 100);
    }

    void OpenMonitorInteraction(Button source, DeckMonitorDefinition monitor)
    {
        try
        {
            switch (monitor.Interaction)
            {
                case DeckMonitorInteraction.TaskManager:
                    SystemControlService.OpenTaskManager();
                    break;
                case DeckMonitorInteraction.WifiSettings:
                case DeckMonitorInteraction.BluetoothSettings:
                    InputEngine.SendShortcut("Win+A");
                    break;
                case DeckMonitorInteraction.Volume:
                case DeckMonitorInteraction.Microphone:
                case DeckMonitorInteraction.Brightness:
                    ShowMonitorControl(source, monitor);
                    break;
                case DeckMonitorInteraction.AutoExtractToggle:
                    execute?.Invoke(new Mapping
                    {
                        Input = "DeckMonitor:auto-extract",
                        Layer = DeckPanelLayout.Layer,
                        Kind = ActionKind.Shortcut,
                        Value = ActionCatalog.ToggleAutoExtractAction
                    });
                    break;
            }
        }
        catch (Exception error)
        {
            LifecycleDiagnostics.Write("deck-monitor-interaction-failed", error.ToString());
        }
    }

    void ShowMonitorControl(Button source, DeckMonitorDefinition monitor, double? knownValue = null, bool? knownMuted = null)
    {
        if (monitorControlPanel != null
            && ReferenceEquals(monitorControlSource, source)
            && monitorControlInteraction == monitor.Interaction)
        {
            if (knownValue is double updated)
                UpdateMonitorControlDisplay(updated);
            return;
        }
        CloseMonitorControl();
        bool audio = monitor.Interaction is DeckMonitorInteraction.Volume or DeckMonitorInteraction.Microphone;
        bool capture = monitor.Interaction == DeckMonitorInteraction.Microphone;
        bool available;
        double current;
        bool muted = knownMuted ?? false;
        if (knownValue is double supplied)
        {
            available = true;
            current = supplied;
        }
        else if (audio)
            available = SystemControlService.TryGetVolume(capture, out current, out muted);
        else
            available = SystemControlService.TryGetBrightness(out current);

        double availableWidth = Math.Max(46, root.ActualWidth - 4);
        double availableHeight = Math.Max(58, root.ActualHeight - 4);
        bool narrow = availableWidth < 150;
        var card = new Border
        {
            Width = Math.Min(narrow ? availableWidth : 224, availableWidth),
            Height = Math.Min(narrow ? 166 : 94, availableHeight),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(narrow ? 7 : 12),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = ThemeService.Brush("CardBackground"),
            BorderBrush = ThemeService.Brush("AccentBrush")
        };
        Panel.SetZIndex(card, 200);
        Grid.SetRowSpan(card, 3);
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            Text = monitor.Name,
            FontSize = narrow ? 9 : 12,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeService.Brush("PrimaryText")
        };
        header.Children.Add(title);
        var close = new Button
        {
            Content = "×",
            Width = 24,
            Height = 24,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Style = GlassButtonStyle(),
            Focusable = false
        };
        close.Click += (_, _) => CloseMonitorControl();
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        body.Children.Add(header);

        var controls = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        if (!narrow)
        {
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = available ? current : 0,
            IsEnabled = available,
            Orientation = narrow ? System.Windows.Controls.Orientation.Vertical : System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = narrow ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Width = narrow ? 34 : double.NaN,
            Height = narrow ? Math.Max(42, card.Height - 58) : 32,
            TickFrequency = 1,
            IsSnapToTickEnabled = true
        };
        var value = new TextBlock
        {
            Text = available ? $"{current:0}%" : "—",
            FontFamily = new System.Windows.Media.FontFamily("Bahnschrift, Segoe UI"),
            FontSize = narrow ? 10 : 12,
            FontWeight = FontWeights.SemiBold,
            MinWidth = narrow ? 0 : 42,
            Margin = narrow ? new Thickness(0) : new Thickness(9, 0, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Foreground = available ? ThemeService.Brush("PrimaryText") : ThemeService.Brush("MutedText")
        };
        slider.ValueChanged += (_, args) =>
        {
            if (!slider.IsEnabled || updatingMonitorControl)
                return;
            double requested = Math.Round(args.NewValue);
            value.Text = $"{requested:0}%";
            ApplyInteractiveMonitorValue(source, monitor, requested);
            if (!audio)
            {
                QueueBrightnessValue(requested);
                return;
            }
            if (SystemControlService.TrySetVolume(capture, requested))
                SystemMonitorService.Shared.RequestRefresh();
        };
        controls.Children.Add(slider);
        if (narrow)
        {
            value.VerticalAlignment = VerticalAlignment.Bottom;
            controls.Children.Add(value);
        }
        else
        {
            Grid.SetColumn(value, 1);
            controls.Children.Add(value);
        }
        if (audio && !narrow)
        {
            var mute = new Button
            {
                Content = muted ? "解除" : "ミュート",
                Height = 28,
                MinWidth = 58,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(8, 2, 8, 2),
                Style = GlassButtonStyle(),
                Focusable = false
            };
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(mute, 2);
            mute.Click += (_, _) =>
            {
                muted = !muted;
                if (SystemControlService.TrySetMute(capture, muted))
                {
                    mute.Content = muted ? "解除" : "ミュート";
                    SystemMonitorService.Shared.RequestRefresh();
                }
                else
                    muted = !muted;
            };
            controls.Children.Add(mute);
        }
        Grid.SetRow(controls, 1);
        body.Children.Add(controls);
        card.Child = body;
        card.PreviewMouseDown += (_, args) => args.Handled = false;
        monitorControlPanel = card;
        monitorControlSlider = slider;
        monitorControlValue = value;
        monitorControlSource = source;
        monitorControlInteraction = monitor.Interaction;
        root.Children.Add(card);
        openContextMenus++;
        autoHideTimer.Stop();
    }

    void UpdateMonitorControlDisplay(double percent)
    {
        if (monitorControlSlider == null || monitorControlValue == null)
            return;
        updatingMonitorControl = true;
        try
        {
            monitorControlSlider.Value = Math.Clamp(percent, 0, 100);
            monitorControlValue.Text = $"{percent:0}%";
        }
        finally
        {
            updatingMonitorControl = false;
        }
    }

    void CloseMonitorControl()
    {
        if (monitorControlPanel == null)
            return;
        root.Children.Remove(monitorControlPanel);
        monitorControlPanel = null;
        monitorControlSlider = null;
        monitorControlValue = null;
        monitorControlSource = null;
        monitorControlInteraction = DeckMonitorInteraction.None;
        openContextMenus = Math.Max(0, openContextMenus - 1);
    }

    bool MonitorControlContains(DependencyObject? source)
    {
        for (DependencyObject? current = source; current != null; current = current is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, monitorControlPanel))
                return true;
        }
        return false;
    }
    void DragStarted(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? source = e.OriginalSource as DependencyObject;
        if (collapsedToEdge && !IsInsideElement(source, CollapsedMoveHandle))
            return;
        if (!collapsedToEdge && IsInsideInteractiveControl(source))
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
    static bool IsInsideInteractiveControl(DependencyObject? source)
    {
        for (var current = source; current != null;)
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.Primitives.RangeBase
                or System.Windows.Controls.Primitives.Thumb
                or System.Windows.Controls.Primitives.Selector
                or System.Windows.Controls.Primitives.TextBoxBase
                or PasswordBox)
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
    internal static bool CanDragPanelFromForTest(DependencyObject source) => !IsInsideInteractiveControl(source);
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
        NotifyPresentationStateChanged();
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
    internal bool IsSafelyMaximized => safeMaximizeRestoreBounds != null && WindowState == WindowState.Normal;
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
            NotifyPresentationStateChanged();
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
        NotifyPresentationStateChanged();
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
        FullScreenButton.ToolTip = fullScreen ? "元の位置とサイズに戻す" : "最大化";
        fullScreenGlyph.Text = fullScreen ? "\uE73F" : "\uE740";
    }

    void NotifyPresentationStateChanged()
    {
        try { stateChanged?.Invoke(); } catch { }
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
}
