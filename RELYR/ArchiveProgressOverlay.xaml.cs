using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace RELYR;

public partial class ArchiveProgressOverlay : Window
{
    const int GwlExStyle = -20;
    const long WsExTransparent = 0x00000020L;
    const long WsExToolWindow = 0x00000080L;
    const long WsExNoActivate = 0x08000000L;
    readonly DispatcherTimer activityTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    readonly DispatcherTimer hideTimer = new();
    double displayedProgress;
    bool closing;

    internal bool UsesCompactClickThroughSurfaceForTest => !ShowActivated && !IsHitTestVisible && AllowsTransparency && Width <= 320 && Height <= 72;
    internal bool UsesNativeClickThroughStylesForTest
    {
        get
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return false;
            long style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
            return (style & (WsExTransparent | WsExToolWindow | WsExNoActivate)) == (WsExTransparent | WsExToolWindow | WsExNoActivate);
        }
    }
    internal string StateTextForTest => StateText.Text;

    internal ArchiveProgressOverlay()
    {
        Opacity = 0;
        InitializeComponent();
        activityTimer.Tick += ActivityTimerTick;
        hideTimer.Tick += HideTimerTick;
        ThemeService.ThemeChanged += ApplyCurrentTheme;
        SourceInitialized += (_, _) => MakeNativeWindowClickThrough();
        Loaded += (_, _) =>
        {
            ApplyCurrentTheme();
            PositionOnCurrentScreen();
        };
        Closed += (_, _) =>
        {
            activityTimer.Stop();
            hideTimer.Stop();
            ThemeService.ThemeChanged -= ApplyCurrentTheme;
        };
        ApplyCurrentTheme();
    }

    internal void ShowActivity(ArchiveActivity activity)
    {
        if (closing)
            return;
        hideTimer.Stop();
        FileNameText.Text = activity.FileName;
        switch (activity.State)
        {
            case ArchiveActivityState.Extracting:
                StateText.Text = "解凍中";
                StateGlyph.Text = "\uE7B8";
                StateGlyph.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "AccentTextBrush");
                ProgressFill.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "AccentBrush");
                displayedProgress = 0.04;
                UpdateProgress();
                activityTimer.Start();
                Reveal();
                break;
            case ArchiveActivityState.Completed:
                activityTimer.Stop();
                StateText.Text = "解凍完了しました";
                StateGlyph.Text = "\uE73E";
                StateGlyph.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "AccentTextBrush");
                ProgressFill.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "AccentBrush");
                displayedProgress = 1;
                UpdateProgress();
                Reveal();
                StartHideTimer(TimeSpan.FromSeconds(2.8));
                break;
            case ArchiveActivityState.Failed:
                activityTimer.Stop();
                StateText.Text = "解凍できませんでした";
                StateGlyph.Text = "\uE783";
                StateGlyph.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "DangerBrush");
                ProgressFill.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "DangerBrush");
                displayedProgress = 1;
                UpdateProgress();
                Reveal();
                StartHideTimer(TimeSpan.FromSeconds(4));
                break;
            default:
                HideImmediately();
                break;
        }
    }

    void Reveal()
    {
        if (!IsVisible)
            Show();
        PositionOnCurrentScreen();
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            PositionOnCurrentScreen();
            UpdateProgress();
        }));
        BeginAnimation(OpacityProperty, null);
        if (!UiMotionService.Enabled)
        {
            Opacity = 1;
            return;
        }
        BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 1, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);
    }

    void ActivityTimerTick(object? sender, EventArgs e)
    {
        // Some archive formats do not expose a reliable byte total. This is an
        // activity indicator only: it advances gently but never claims 100%
        // until the extraction operation has actually completed.
        displayedProgress = Math.Min(.92, displayedProgress + Math.Max(.004, (.92 - displayedProgress) * .045));
        UpdateProgress();
    }

    void UpdateProgress()
    {
        double target = Math.Max(0, ProgressTrack.ActualWidth * displayedProgress);
        ProgressFill.BeginAnimation(FrameworkElement.WidthProperty, null);
        if (!UiMotionService.Enabled || !IsVisible)
        {
            ProgressFill.Width = target;
            return;
        }
        ProgressFill.BeginAnimation(FrameworkElement.WidthProperty,
            new DoubleAnimation(ProgressFill.ActualWidth, target, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            }, HandoffBehavior.SnapshotAndReplace);
    }

    void StartHideTimer(TimeSpan delay)
    {
        hideTimer.Stop();
        hideTimer.Interval = delay;
        hideTimer.Start();
    }

    void HideTimerTick(object? sender, EventArgs e)
    {
        hideTimer.Stop();
        HideImmediately();
    }

    internal void HideImmediately()
    {
        activityTimer.Stop();
        hideTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        if (IsVisible)
            Hide();
    }

    internal void CloseForProcessExit()
    {
        if (closing)
            return;
        closing = true;
        HideImmediately();
        Close();
    }

    void ApplyCurrentTheme()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke((Action)ApplyCurrentTheme);
            return;
        }
        OverlaySurface.Background = ThemeService.Brush("CardBackground");
        OverlaySurface.BorderBrush = ThemeService.Brush("SubtleBorderBrush");
        StateText.Foreground = ThemeService.Brush("PrimaryText");
        FileNameText.Foreground = ThemeService.Brush("SecondaryText");
    }

    void PositionOnCurrentScreen()
    {
        var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not { } target)
            return;
        var topLeft = target.TransformFromDevice.Transform(new System.Windows.Point(area.Left, area.Top));
        var bottomRight = target.TransformFromDevice.Transform(new System.Windows.Point(area.Right, area.Bottom));
        Left = topLeft.X + (bottomRight.X - topLeft.X - ActualWidth) / 2;
        Top = Math.Max(topLeft.Y + 16, bottomRight.Y - ActualHeight - 42);
    }

    void MakeNativeWindowClickThrough()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        long style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        _ = SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExTransparent | WsExToolWindow | WsExNoActivate));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
