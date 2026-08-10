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

/// <summary>ブランク画面またはクロックを、指定モニター全体へ表示します。</summary>
/// <summary>Deckの動画を、実比率のままホバー再生・スクラブ表示します。</summary>
sealed class DeckVideoPreviewPopup : IDisposable
{
    const double MaxPreviewWidth = 360;
    const double MaxPreviewHeight = 300;
    readonly Button source;
    readonly FrameworkElement placementBoundary;
    readonly Popup popup;
    readonly Grid frame;
    readonly MediaElement media;
    readonly Button playPauseButton;
    readonly System.Windows.Shapes.Path playPauseGlyph;
    readonly System.Windows.Shapes.Rectangle timelineFill;
    readonly System.Windows.Shapes.Rectangle timelineMarker;
    readonly TextBlock failureLabel;
    readonly Border card;
    readonly DispatcherTimer closeTimer;
    readonly DispatcherTimer playbackTimer;
    readonly DispatcherTimer pointerTimer;
    TimeSpan duration;
    bool mediaOpened;
    bool playRequested;
    bool draggingPlayhead;
    double playheadPointerDownX;
    double? pendingScrubRatio;
    bool surfacePointerCaptured;
    bool surfaceDragging;
    double surfacePointerDownX;
    bool disposed;

    internal DeckVideoPreviewPopup(Button source, string path, FrameworkElement placementBoundary)
    {
        this.source = source;
        this.placementBoundary = placementBoundary;
        ImageSource? thumbnail = DeckPanelLayout.LoadVideoThumbnail(path, 640, 360);
        System.Windows.Size initialSize = PreviewSize(thumbnail?.Width ?? 0, thumbnail?.Height ?? 0);
        frame = new Grid { Width = initialSize.Width, Height = initialSize.Height, Background = new SolidColorBrush(WpfColor.FromRgb(12, 17, 21)), ClipToBounds = true, SnapsToDevicePixels = true, Cursor = WpfCursors.SizeWE };
        if (thumbnail != null)
            frame.Children.Add(new WpfImage { Source = thumbnail, Stretch = Stretch.Uniform, IsHitTestVisible = false });

        media = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, ScrubbingEnabled = true, Volume = 0, Stretch = Stretch.Uniform, Source = new Uri(path, UriKind.Absolute), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        media.MediaOpened += MediaOpened;
        media.MediaEnded += MediaEnded;
        media.MediaFailed += MediaFailed;
        frame.Children.Add(media);

        timelineFill = new System.Windows.Shapes.Rectangle { Height = 3, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        timelineFill.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush");
        frame.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(WpfColor.FromArgb(142, 240, 246, 250)), IsHitTestVisible = false, Child = timelineFill });
        timelineMarker = new System.Windows.Shapes.Rectangle { Width = 2, Height = 11, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Fill = WpfBrushes.White, Opacity = .9, IsHitTestVisible = false };
        frame.Children.Add(timelineMarker);

        playPauseGlyph = new System.Windows.Shapes.Path { Fill = WpfBrushes.White, Stretch = Stretch.Uniform, Margin = new Thickness(11) };
        playPauseButton = new Button
        {
            Width = 42,
            Height = 42,
            Padding = new Thickness(0),
            Focusable = false,
            Content = playPauseGlyph,
            Background = new SolidColorBrush(WpfColor.FromArgb(224, 14, 21, 26)),
            BorderBrush = new SolidColorBrush(WpfColor.FromArgb(188, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Template = RoundButtonTemplate()
        };
        // Click runs after Button has released its mouse capture.  Handling the
        // preview mouse-up event here kept the button captured, which made the
        // next click on the video surface look like another pause click.
        playPauseButton.Click += PlayPauseClicked;
        frame.Children.Add(playPauseButton);
        failureLabel = new TextBlock { Text = "この動画は再生できません", FontSize = 12, FontWeight = FontWeights.SemiBold, Padding = new Thickness(10, 5, 10, 5), Background = new SolidColorBrush(WpfColor.FromArgb(224, 14, 21, 26)), Foreground = WpfBrushes.White, Visibility = Visibility.Collapsed, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        frame.Children.Add(failureLabel);
        frame.SizeChanged += (_, _) => { frame.Clip = new RectangleGeometry(new Rect(frame.RenderSize), 10, 10); UpdateTimeline(); };

        var inner = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = frame, SnapsToDevicePixels = true };
        card = new Border { Padding = new Thickness(1), CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), Child = inner, Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = .56, Color = Colors.Black }, SnapsToDevicePixels = true };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        popup = new Popup { Child = card, PlacementTarget = source, Placement = PlacementMode.Custom, StaysOpen = true, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
        popup.CustomPopupPlacementCallback = OutsideDeckPlacements;

        closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        closeTimer.Tick += CloseTimerTick;
        playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        playbackTimer.Tick += PlaybackTimerTick;
        pointerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        pointerTimer.Tick += PointerTimerTick;
        source.MouseEnter += SourceMouseEnter;
        source.MouseLeave += SourceMouseLeave;
        source.PreviewMouseLeftButtonDown += SourceMouseLeftButtonDown;
        card.MouseEnter += CardMouseEnter;
        card.MouseLeave += CardMouseLeave;
        card.PreviewMouseLeftButtonDown += CardMouseLeftButtonDown;
        card.PreviewMouseMove += CardMouseMove;
        card.PreviewMouseLeftButtonUp += CardMouseLeftButtonUp;
        SetPlayGlyph(false);
    }
    CustomPopupPlacement[] OutsideDeckPlacements(System.Windows.Size popupSize, System.Windows.Size targetSize, Point offset)
    {
        try
        {
            double gap = targetSize.Width;
            double y = (targetSize.Height - popupSize.Height) / 2;
            var right = new CustomPopupPlacement(new Point(targetSize.Width + gap, y), PopupPrimaryAxis.Vertical);
            var left = new CustomPopupPlacement(new Point(-popupSize.Width - gap, y), PopupPrimaryAxis.Vertical);
            Point sourceInDeck = source.TranslatePoint(new Point(0, 0), placementBoundary);
            double deckWidth = placementBoundary.ActualWidth > 0 ? placementBoundary.ActualWidth : placementBoundary.Width;
            return sourceInDeck.X + targetSize.Width / 2 > deckWidth / 2 ? [left, right] : [right, left];
        }
        catch { return [new CustomPopupPlacement(new Point(targetSize.Width + 8, 0), PopupPrimaryAxis.None)]; }
    }
    internal bool IsFor(Button button) => ReferenceEquals(source, button);
    internal void Hide() => ClosePreview();
    internal void Show() => OpenPreview();

    static System.Windows.Size PreviewSize(double width, double height)
    {
        if (width <= 0 || height <= 0)
            return new System.Windows.Size(260, 146);
        double scale = Math.Min(MaxPreviewWidth / width, MaxPreviewHeight / height);
        scale = Math.Min(1, scale);
        return new System.Windows.Size(Math.Max(96, Math.Round(width * scale)), Math.Max(72, Math.Round(height * scale)));
    }
    static ControlTemplate RoundButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(21));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }
    void SourceMouseEnter(object sender, MouseEventArgs e)
        => OpenPreview();
    void OpenPreview()
    {
        if (disposed)
            return;
        try
        {
            closeTimer.Stop();
            if (!popup.IsOpen)
                popup.IsOpen = true;
            // Force the media pipeline to open while muted. ScrubbingEnabled
            // then presents the frame at each pointer position without audio.
            media.Volume = 0;
            media.Play();
            media.Pause();
            pointerTimer.Start();
        }
        catch { try { ShowPlaybackFailure(); } catch { ClosePreview(); } }
    }
    void SourceMouseLeave(object sender, MouseEventArgs e) => ScheduleClose();
    void SourceMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => ClosePreview();
    void CardMouseEnter(object sender, MouseEventArgs e) => closeTimer.Stop();
    void CardMouseLeave(object sender, MouseEventArgs e) => ScheduleClose();
    void CardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject target && FindButton(target) != null)
        {
            TraceVideo("card-down button=" + target.GetType().Name);
            return;
        }
        surfacePointerCaptured = true;
        surfaceDragging = false;
        surfacePointerDownX = e.GetPosition(frame).X;
        TraceVideo($"card-down x={surfacePointerDownX:F0} playing={playRequested}");
        card.CaptureMouse();
        Scrub(surfacePointerDownX);
        StartPlayback();
        e.Handled = true;
    }
    void CardMouseMove(object sender, MouseEventArgs e)
    {
        double x = e.GetPosition(frame).X;
        if (surfacePointerCaptured)
        {
            if (!surfaceDragging && Math.Abs(x - surfacePointerDownX) >= SystemParameters.MinimumHorizontalDragDistance)
                surfaceDragging = true;
            if (surfaceDragging)
                Scrub(x);
            return;
        }
        if (!playRequested)
            Scrub(x);
    }
    void CardMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!surfacePointerCaptured)
            return;
        if (surfaceDragging)
            Scrub(e.GetPosition(frame).X);
        surfacePointerCaptured = false;
        surfaceDragging = false;
        if (Mouse.Captured == card)
            card.ReleaseMouseCapture();
        TraceVideo("card-up start");
        StartPlayback();
        e.Handled = true;
    }
    void ScheduleClose()
    {
        if (!disposed)
            closeTimer.Start();
    }
    void CloseTimerTick(object? sender, EventArgs e)
    {
        closeTimer.Stop();
        if (disposed || IsPointerOverPreview())
            return;
        ClosePreview();
    }
    void ClosePreview()
    {
        closeTimer.Stop();
        pointerTimer.Stop();
        playbackTimer.Stop();
        try
        {
            media.Stop();
            media.Position = TimeSpan.Zero;
        }
        catch { }
        playRequested = false;
        SetPlayGlyph(false);
        try { popup.IsOpen = false; } catch { }
    }
    void PointerTimerTick(object? sender, EventArgs e)
    {
        if (disposed || !popup.IsOpen)
        {
            pointerTimer.Stop();
            return;
        }
        if (IsPointerOverPreview())
            closeTimer.Stop();
        else
            ScheduleClose();
    }
    bool IsPointerOverPreview() => ContainsCursor(source) || ContainsCursor(card);
    static bool ContainsCursor(FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return false;
        try
        {
            Point topLeft = element.PointToScreen(new Point(0, 0));
            DpiScale dpi = VisualTreeHelper.GetDpi(element);
            var cursor = System.Windows.Forms.Cursor.Position;
            double right = topLeft.X + element.ActualWidth * dpi.DpiScaleX;
            double bottom = topLeft.Y + element.ActualHeight * dpi.DpiScaleY;
            return cursor.X >= topLeft.X && cursor.X <= right && cursor.Y >= topLeft.Y && cursor.Y <= bottom;
        }
        catch { return false; }
    }
    void MediaOpened(object? sender, RoutedEventArgs e)
    {
        try
        {
            mediaOpened = true;
            if (media.NaturalDuration.HasTimeSpan)
                duration = media.NaturalDuration.TimeSpan;
            if (media.NaturalVideoWidth > 0 && media.NaturalVideoHeight > 0)
            {
                System.Windows.Size size = PreviewSize(media.NaturalVideoWidth, media.NaturalVideoHeight);
                frame.Width = size.Width;
                frame.Height = size.Height;
            }
            media.Visibility = Visibility.Visible;
            if (pendingScrubRatio is double pending && duration > TimeSpan.Zero)
            {
                media.Position = TimeSpan.FromTicks((long)(duration.Ticks * pending));
                pendingScrubRatio = null;
            }
            if (playRequested)
            {
                media.Volume = 1;
                media.Play();
                playbackTimer.Start();
            }
            else
            {
                media.Volume = 0;
                media.Pause();
            }
            UpdateTimeline();
        }
        catch { ShowPlaybackFailure(); }
    }
    void MediaEnded(object? sender, RoutedEventArgs e)
    {
        TraceVideo("media-ended");
        playbackTimer.Stop();
        playRequested = false;
        SetPlayGlyph(false);
        try
        {
            media.Position = TimeSpan.Zero;
        }
        catch { }
        UpdateTimeline();
    }
    void MediaFailed(object? sender, ExceptionRoutedEventArgs e) => ShowPlaybackFailure();
    void ShowPlaybackFailure()
    {
        playbackTimer.Stop();
        playRequested = false;
        mediaOpened = false;
        playPauseButton.Visibility = Visibility.Collapsed;
        failureLabel.Visibility = Visibility.Visible;
        media.Visibility = Visibility.Collapsed;
    }
    void PlayPauseClicked(object sender, RoutedEventArgs e)
    {
        TogglePlayback();
    }
    void TogglePlayback()
    {
        if (playRequested)
        {
            PausePlayback();
            return;
        }
        StartPlayback();
    }
    void StartPlayback()
    {
        TraceVideo($"start position={media.Position.TotalMilliseconds:F0}");
        playRequested = true;
        SetPlayGlyph(true);
        try
        {
            media.Volume = 1;
            media.Play();
            if (mediaOpened)
                playbackTimer.Start();
        }
        catch { ShowPlaybackFailure(); }
    }
    void PausePlayback()
    {
        TraceVideo($"pause position={media.Position.TotalMilliseconds:F0}");
        playRequested = false;
        try
        {
            media.Pause();
            media.Volume = 0;
            playbackTimer.Stop();
            SetPlayGlyph(false);
        }
        catch { ShowPlaybackFailure(); }
    }
    void FrameMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject target && FindButton(target) != null)
            return;
        playheadPointerDownX = e.GetPosition(frame).X;
        draggingPlayhead = false;
        frame.CaptureMouse();
        // A surface press is always seek-and-play. It never toggles playback;
        // the centre pause control is the sole way to stop a preview.
        Scrub(playheadPointerDownX);
        StartPlayback();
        e.Handled = true;
    }
    void FrameMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject target && FindButton(target) != null)
            return;
        double x = e.GetPosition(frame).X;
        if (Mouse.Captured == frame && draggingPlayhead)
            Scrub(x);
        draggingPlayhead = false;
        if (Mouse.Captured == frame)
            frame.ReleaseMouseCapture();
        e.Handled = true;
        // The video surface itself is always play-only.  Handling this during
        // the preview phase makes a click deterministic even after a pause.
        StartPlayback();
    }
    static Button? FindButton(DependencyObject source)
    {
        for (DependencyObject? current = source; current != null; current = current is Visual visual ? VisualTreeHelper.GetParent(visual) : LogicalTreeHelper.GetParent(current))
        if (current is Button button)
            return button;
        return null;
    }
    void FrameMouseMove(object sender, MouseEventArgs e)
    {
        double x = e.GetPosition(frame).X;
        if (Mouse.Captured == frame)
        {
            if (!draggingPlayhead && Math.Abs(x - playheadPointerDownX) >= SystemParameters.MinimumHorizontalDragDistance)
                draggingPlayhead = true;
            if (draggingPlayhead)
                Scrub(x);
            return;
        }
        if (!playRequested)
            Scrub(x);
    }
    void Scrub(double x)
    {
        if (frame.ActualWidth <= 0)
            return;
        double ratio = Math.Clamp(x / frame.ActualWidth, 0, 1);
        if (!mediaOpened || duration <= TimeSpan.Zero)
        {
            pendingScrubRatio = ratio;
            UpdateTimeline(ratio);
            return;
        }
        try
        {
            media.Position = TimeSpan.FromTicks((long)(duration.Ticks * ratio));
            media.Volume = playRequested ? 1 : 0;
            UpdateTimeline(ratio);
        }
        catch { }
    }
    void UpdateTimeline(double? ratioOverride = null)
    {
        double ratio = ratioOverride ?? (duration > TimeSpan.Zero ? Math.Clamp(media.Position.TotalMilliseconds / duration.TotalMilliseconds, 0, 1) : 0);
        double width = Math.Max(0, frame.ActualWidth);
        timelineFill.Width = width * ratio;
        timelineMarker.Margin = new Thickness(Math.Max(0, width * ratio - 1), 0, 0, 0);
    }
    void PlaybackTimerTick(object? sender, EventArgs e)
    {
        try { UpdateTimeline(); } catch { ClosePreview(); }
    }
    void SetPlayGlyph(bool playing) => playPauseGlyph.Data = Geometry.Parse(playing ? "M 7,5 L 11,5 L 11,23 L 7,23 Z M 17,5 L 21,5 L 21,23 L 17,23 Z" : "M 7,5 L 22,14 L 7,23 Z");
    static void TraceVideo(string message)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RELYR_VIDEO_DIAGNOSTICS"), "1", StringComparison.Ordinal))
            return;
        try
        {
            File.AppendAllText(VerificationPaths.GetFile("video-preview-runtime.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        ClosePreview();
        closeTimer.Tick -= CloseTimerTick;
        playbackTimer.Tick -= PlaybackTimerTick;
        pointerTimer.Tick -= PointerTimerTick;
        source.MouseEnter -= SourceMouseEnter;
        source.MouseLeave -= SourceMouseLeave;
        source.PreviewMouseLeftButtonDown -= SourceMouseLeftButtonDown;
        card.PreviewMouseLeftButtonDown -= CardMouseLeftButtonDown;
        card.PreviewMouseMove -= CardMouseMove;
        card.PreviewMouseLeftButtonUp -= CardMouseLeftButtonUp;
        media.MediaOpened -= MediaOpened;
        media.MediaEnded -= MediaEnded;
        media.MediaFailed -= MediaFailed;
        try
        {
            media.Stop();
            media.Close();
        }
        catch { }
        try { popup.IsOpen = false; } catch { }
    }
}
