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

/// <summary>割り当てから呼び出せる画面オーバーレイを一元管理します。</summary>
internal static class OverlayService
{
    internal enum DeckPresentationState
    {
        Hidden,
        Visible,
        Collapsed,
        Maximized
    }

    internal const string NumpadAction = "ShowNumpadOverlay";
    internal const string ExtendedKeypadAction = "ShowExtendedKeypadOverlay";
    internal const string DeckPanelAction = "ShowDeckPanelOverlay";
    internal const string BlankAction = "ShowBlankOverlay";
    internal const string ClockAction = "ShowClockOverlay";

    sealed class DeckPanelEntry(string action, DeckPanelOverlayWindow window)
    {
        internal string Action { get; set; } = action;
        internal DeckPanelOverlayWindow Window { get; } = window;
        internal long LastUsedAt { get; set; } = Environment.TickCount64;
    }

    static InputPanelOverlayWindow? inputPanel;
    static readonly Dictionary<string, DeckPanelEntry> deckPanels = new(StringComparer.OrdinalIgnoreCase);
    internal const int MaxHiddenDeckPanels = 1;
    static string lastDeckPanelKey = "";
    static readonly List<ScreenOverlayWindow> screenOverlays = [];
    static readonly object screenOverlayGate = new();
    const int FullScreenCloseFailOpenMilliseconds = 250;
    static Func<AppConfig>? configProvider;
    static Func<bool>? physicalInputDownProvider;
    static Action<Mapping>? deckActionRequested;
    static Action<string, double, double>? deckPositionChanged;
    static Action<string, double, double>? deckCollapsedPositionChanged;
    static Action<string, double, double>? deckSizeChanged;
    static Action<string, bool>? deckPinnedChanged;
    static Action? deckPresentationStateChanged;
    static Action? deckLayoutChanged;
    static Action<string, int, int>? deckSlotsChanged;
    static Action<bool, double, double>? inputPanelPositionChanged;
    static int fullScreenActive;
    static int fullScreenClosing;
    static int fullScreenDismissArmed;
    static int deckRefreshQueued;
    static int deckRefreshContentRequired;
    static int deckLayoutPreviewQueued;
    static System.Drawing.Point fullScreenStartCursor;
#if !PRODUCTION_PUBLISH
    internal static Action<string>? ActionRequestedForTest;
    internal static int DeckRefreshRequestCountForTest;
    internal static int DeckSlotRefreshRequestCountForTest;
    internal static int DeckLayoutPreviewRequestCountForTest;
    internal static DeckPanelOverlayWindow? DeckPanelInstanceForTest => deckPanels.TryGetValue(lastDeckPanelKey, out var entry) ? entry.Window : deckPanels.Values.LastOrDefault()?.Window;
    internal static IReadOnlyList<DeckPanelOverlayWindow> DeckPanelInstancesForTest => deckPanels.Values.Select(entry => entry.Window).ToArray();
    internal static void ResetDeckRefreshRequestCountForTest()
    {
        Interlocked.Exchange(ref DeckRefreshRequestCountForTest, 0);
        Interlocked.Exchange(ref DeckSlotRefreshRequestCountForTest, 0);
        Interlocked.Exchange(ref DeckLayoutPreviewRequestCountForTest, 0);
    }
    internal static bool RecoverFromFullScreenFailureForTest()
    {
        Interlocked.Exchange(ref fullScreenActive, 1);
        RunOverlayUiSafely(() => throw new InvalidOperationException("simulated overlay failure"), "test overlay failure");
        return !FullScreenVisible && Volatile.Read(ref fullScreenClosing) == 0 && Volatile.Read(ref fullScreenDismissArmed) == 0;
    }
    internal static int ScreenOverlayCountForTest
    {
        get { lock (screenOverlayGate) return screenOverlays.Count; }
    }
    internal static void CloseScreenOverlaysExternallyForTest()
    {
        foreach (var overlay in SnapshotScreenOverlays())
            overlay.Close();
    }
    internal static bool RecoverFromStalledFullScreenCloseForTest()
    {
        Interlocked.Exchange(ref fullScreenActive, 1);
        Interlocked.Exchange(ref fullScreenClosing, 1);
        ScheduleFullScreenCloseFailOpenWatchdog();
        bool recovered = SpinWait.SpinUntil(() => !FullScreenVisible, TimeSpan.FromSeconds(2));
        return recovered && Volatile.Read(ref fullScreenClosing) == 0 && Volatile.Read(ref fullScreenDismissArmed) == 0;
    }
    internal static void ArmStaleFullScreenTransactionForTest()
    {
        Interlocked.Exchange(ref fullScreenActive, 1);
        Interlocked.Exchange(ref fullScreenClosing, 0);
        Interlocked.Exchange(ref fullScreenDismissArmed, 1);
    }
#endif
    internal static bool FullScreenVisible
    {
        get
        {
            if (Volatile.Read(ref fullScreenActive) == 0)
                return false;
            // The low-level hooks must never consume input for a bookkeeping
            // flag alone. A display/session transition can destroy the native
            // HWND before WPF raises Closed; fail open as soon as no real,
            // visible fullscreen surface remains.
            if (SnapshotScreenOverlays().Any(window => window.HasVisibleNativeSurface))
                return true;
            ResetFullScreenTransaction();
            return false;
        }
    }

    internal static void Configure(Func<AppConfig>? provider, Func<bool>? inputDownProvider = null, Action<Mapping>? deckAction = null, Action<string, double, double>? positionChanged = null, Action<bool, double, double>? inputPositionChanged = null, Action? layoutChanged = null, Action<string, int, int>? slotsChanged = null, Action<string, double, double>? sizeChanged = null, Action<string, bool>? pinnedChanged = null, Action<string, double, double>? collapsedPositionChanged = null, Action? presentationStateChanged = null)
    {
        configProvider = provider;
        physicalInputDownProvider = inputDownProvider;
        deckActionRequested = deckAction;
        deckPositionChanged = positionChanged;
        deckCollapsedPositionChanged = collapsedPositionChanged;
        deckSizeChanged = sizeChanged;
        deckPinnedChanged = pinnedChanged;
        deckPresentationStateChanged = presentationStateChanged;
        inputPanelPositionChanged = inputPositionChanged;
        deckLayoutChanged = layoutChanged;
        deckSlotsChanged = slotsChanged;
    }

    static string DeckPanelKey(string action, DeckLayoutDefinition layout)
    {
        if (action.Equals(DeckPanelAction, StringComparison.OrdinalIgnoreCase))
            return "default";
        if (layout.ProfileSwitchEnabled && !string.IsNullOrWhiteSpace(layout.ProfileGroupId))
            return "group:" + layout.ProfileGroupId;
        return "layout:" + layout.Id;
    }

    static void CloseDeckPanels()
    {
        var windows = deckPanels.Values.Select(entry => entry.Window).Distinct().ToArray();
        deckPanels.Clear();
        lastDeckPanelKey = "";
        foreach (var window in windows)
        {
            try { window.Close(); } catch { }
        }
    }

    static DeckPanelOverlayWindow CreateDeckPanel(AppConfig config, string action, string key, DeckLayoutDefinition layout, bool cascade)
    {
        TrimHiddenDeckPanelCache();
        var previous = cascade ? deckPanels.Values.LastOrDefault(entry => entry.Window.IsVisible)?.Window : null;
        bool hasOwnPosition = layout.PanelLeft is double && layout.PanelTop is double;
        var panel = new DeckPanelOverlayWindow(
            config,
            deckActionRequested,
            config.InputPanelOpacityPercent,
            (left, top) => deckPositionChanged?.Invoke(layout.Id, left, top),
            layout,
            deckSizeChanged,
            deckPinnedChanged,
            (left, top) => deckCollapsedPositionChanged?.Invoke(layout.Id, left, top),
            NotifyDeckPresentationStateChanged);
        var entry = new DeckPanelEntry(action, panel);
        deckPanels[key] = entry;
        lastDeckPanelKey = key;
        panel.Closed += (_, _) =>
        {
            if (deckPanels.TryGetValue(key, out var current) && ReferenceEquals(current.Window, panel))
                deckPanels.Remove(key);
            if (lastDeckPanelKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                lastDeckPanelKey = deckPanels.Keys.LastOrDefault() ?? "";
            NotifyDeckPresentationStateChanged();
        };
        panel.PrepareForShow();
        panel.Show();
        NotifyDeckPresentationStateChanged();
        if (!hasOwnPosition && previous != null)
        {
            const double cascadeOffset = 32;
            double width = panel.ActualWidth > 0 ? panel.ActualWidth : panel.Width;
            double height = panel.ActualHeight > 0 ? panel.ActualHeight : panel.Height;
            double minLeft = SystemParameters.VirtualScreenLeft;
            double minTop = SystemParameters.VirtualScreenTop;
            double maxLeft = minLeft + SystemParameters.VirtualScreenWidth - Math.Min(width, SystemParameters.VirtualScreenWidth);
            double maxTop = minTop + SystemParameters.VirtualScreenHeight - Math.Min(height, SystemParameters.VirtualScreenHeight);
            double left = previous.Left + cascadeOffset;
            double top = previous.Top + cascadeOffset;
            if (left > maxLeft || top > maxTop)
            {
                left = minLeft + cascadeOffset;
                top = minTop + cascadeOffset;
            }
            panel.MoveAndPersist(Math.Clamp(left, minLeft, maxLeft), Math.Clamp(top, minTop, maxTop));
        }
        return panel;
    }

    static void TrimHiddenDeckPanelCache()
    {
        while (deckPanels.Values.Count(entry => !entry.Window.IsVisible) > MaxHiddenDeckPanels)
        {
            var oldest = deckPanels.Values
                .Where(entry => !entry.Window.IsVisible)
                .OrderBy(entry => entry.LastUsedAt)
                .FirstOrDefault();
            if (oldest == null)
                return;
            oldest.Window.Close();
        }
    }

    internal static bool IsDeckPanelVisible(string action)
    {
        var config = configProvider?.Invoke();
        var layout = config == null ? null : DeckPanelLayout.ResolveActionLayout(config, action);
        if (layout == null)
            return false;
        string key = DeckPanelKey(action, layout);
        return deckPanels.TryGetValue(key, out var exact) && exact.Window.IsVisible
            || deckPanels.Values.Any(entry => entry.Window.IsVisible && entry.Window.LayoutId.Equals(layout.Id, StringComparison.OrdinalIgnoreCase));
    }

    internal static DeckPresentationState DeckPanelPresentationState(string action)
    {
        var config = configProvider?.Invoke();
        var layout = config == null ? null : DeckPanelLayout.ResolveActionLayout(config, action);
        if (layout == null)
            return DeckPresentationState.Hidden;
        string key = DeckPanelKey(action, layout);
        DeckPanelOverlayWindow? panel = deckPanels.TryGetValue(key, out var exact)
            ? exact.Window
            : deckPanels.Values.FirstOrDefault(entry => entry.Window.LayoutId.Equals(layout.Id, StringComparison.OrdinalIgnoreCase))?.Window;
        if (panel?.IsVisible != true)
            return DeckPresentationState.Hidden;
        if (panel.IsCollapsedToEdge)
            return DeckPresentationState.Collapsed;
        return panel.IsSafelyMaximized ? DeckPresentationState.Maximized : DeckPresentationState.Visible;
    }

    static void NotifyDeckPresentationStateChanged()
    {
        TrimHiddenDeckPanelCache();
        try { deckPresentationStateChanged?.Invoke(); } catch { }
    }
    internal static void Shutdown()
    {
        if (WpfApplication.Current?.Dispatcher.CheckAccess() == true)
        {
            inputPanel?.Close();
            inputPanel = null;
            CloseDeckPanels();
            CloseScreenOverlays();
            configProvider = null;
            physicalInputDownProvider = null;
            deckActionRequested = null;
            deckPositionChanged = null;
            deckCollapsedPositionChanged = null;
            deckSizeChanged = null;
            deckPinnedChanged = null;
            deckPresentationStateChanged = null;
            inputPanelPositionChanged = null;
            deckLayoutChanged = null;
            deckSlotsChanged = null;
        }
        else if (WpfApplication.Current is { } app)
            _ = app.Dispatcher.BeginInvoke(Shutdown);
    }

    internal static void RefreshDeckPanel()
    {
#if !PRODUCTION_PUBLISH
        Interlocked.Increment(ref DeckRefreshRequestCountForTest);
#endif
        Interlocked.Exchange(ref deckRefreshContentRequired, 1);
        QueueDeckPanelRefresh();
    }

    internal static void RefreshDeckPanelSlots(string layoutId, IEnumerable<int> slots)
    {
        int[] requested = [.. slots.Where(slot => slot > 0).Distinct()];
        if (requested.Length == 0)
            return;
#if !PRODUCTION_PUBLISH
        Interlocked.Increment(ref DeckSlotRefreshRequestCountForTest);
#endif
        void Refresh()
        {
            foreach (var entry in deckPanels.Values.Where(entry => entry.Window.LayoutId.Equals(layoutId, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                try { entry.Window.RefreshDeckSlots(requested); }
                catch (Exception exception) { LogOverlayFailure("refresh Deck slots", exception); }
            }
        }
        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher?.CheckAccess() == true)
            Refresh();
        else if (dispatcher != null)
            _ = dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(Refresh));
    }

    internal static void RefreshDeckPanelLayoutPreview()
    {
#if !PRODUCTION_PUBLISH
        Interlocked.Increment(ref DeckLayoutPreviewRequestCountForTest);
#endif
        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher == null || Interlocked.Exchange(ref deckLayoutPreviewQueued, 1) != 0)
            return;
        _ = dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            RunOverlayUiSafely(() =>
            {
                Interlocked.Exchange(ref deckLayoutPreviewQueued, 0);
                var config = configProvider?.Invoke();
                if (config == null)
                    return;
                foreach (var entry in deckPanels.Values.ToArray())
                    entry.Window.RefreshLayoutPreview(
                        config.InputPanelOpacityPercent,
                        config.DeckHoverPreviewsEnabled,
                        config.DeckAfterActionBehavior,
                        config.DeckPointerLeaveBehavior);
            }, "preview Deck layout");
        }));
    }

    internal static void RefreshDeckPanelForProfileChange()
        => QueueDeckPanelRefresh();

    static void QueueDeckPanelRefresh()
    {
        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher == null || Interlocked.Exchange(ref deckRefreshQueued, 1) != 0)
            return;
        _ = dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            RunOverlayUiSafely(() =>
            {
                Interlocked.Exchange(ref deckRefreshQueued, 0);
                bool refreshContent = Interlocked.Exchange(ref deckRefreshContentRequired, 0) != 0;
                var config = configProvider?.Invoke();
                if (config == null)
                    return;
                foreach (var pair in deckPanels.ToArray())
                {
                    string key = pair.Key;
                    var entry = pair.Value;
                    var panel = entry.Window;
                    var layout = DeckPanelLayout.ResolveActionLayout(config, entry.Action);
                    if (layout?.Id.Equals(panel.LayoutId, StringComparison.OrdinalIgnoreCase) != true)
                    {
                        bool visible = panel.IsVisible;
                        bool collapsed = panel.IsCollapsedToEdge;
                        panel.Close();
                        if (visible && layout != null)
                        {
                            var replacement = CreateDeckPanel(config, entry.Action, key, layout, cascade: false);
                            if (collapsed)
                                replacement.CollapseToEdge();
                        }
                        continue;
                    }
                    if (refreshContent)
                        panel.Refresh(config.InputPanelOpacityPercent, config.DeckHoverPreviewsEnabled, config.DeckAfterActionBehavior, config.DeckPointerLeaveBehavior);
                }
            }, "refresh Deck panel");
        }));
    }

    internal static void NotifyDeckLayoutChanged(bool refreshDeckPanel = true, string? layoutId = null, int? firstSlot = null, int? secondSlot = null)
    {
        if (layoutId is not null && firstSlot is int first && secondSlot is int second)
        {
            try { deckSlotsChanged?.Invoke(layoutId, first, second); } catch (Exception exception) { LogOverlayFailure("persist Deck slot changes", exception); }
        }
        else
        {
            try { deckLayoutChanged?.Invoke(); } catch (Exception exception) { LogOverlayFailure("persist Deck layout changes", exception); }
        }
        if (refreshDeckPanel)
            RefreshDeckPanel();
    }

    internal static bool IsOverlayAction(string? value) => value is NumpadAction or ExtendedKeypadAction or BlankAction or ClockAction || DeckPanelLayout.IsDeckAction(value);

    internal static bool TryShow(string? value)
    {
        if (!IsOverlayAction(value))
            return false;
        string action = value!;
#if !PRODUCTION_PUBLISH
        if (ActionRequestedForTest is { } test)
        {
            test(action);
            return true;
        }
#endif
        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher == null)
            return false;
        _ = dispatcher.BeginInvoke(() => RunOverlayUiSafely(() => ShowOnUiThread(action), "show overlay"));
        return true;
    }

    static void RunOverlayUiSafely(Action action, string operation)
    {
        try { action(); }
        catch (Exception exception)
        {
            LogOverlayFailure(operation, exception);
            CloseDeckPanels();
            try { inputPanel?.Close(); } catch { }
            inputPanel = null;
            // If construction or Show fails after fullScreenActive was set,
            // leaving that flag behind makes both low-level hooks consume all
            // keyboard and mouse input even though no overlay is visible.
            // Always clear the complete fullscreen transaction on any UI fault.
            CloseScreenOverlays();
        }
    }

    static void LogOverlayFailure(string operation, Exception exception)
    {
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RELYR");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "overlay-errors.log"), $"{DateTimeOffset.Now:O} {operation}: {exception}{Environment.NewLine}");
        }
        catch { }
    }

    static void ShowOnUiThread(string action)
    {
        if (DeckPanelLayout.IsDeckAction(action))
        {
            inputPanel?.Close();
            inputPanel = null;
            AppConfig deckConfig = configProvider?.Invoke() ?? new AppConfig();
            var layout = DeckPanelLayout.ResolveActionLayout(deckConfig, action);
            if (layout == null)
                return;
            string key = DeckPanelKey(action, layout);
            var matching = deckPanels.FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
                || pair.Value.Window.LayoutId.Equals(layout.Id, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(matching.Key))
            {
                key = matching.Key;
                var entry = matching.Value;
                entry.LastUsedAt = Environment.TickCount64;
                var existing = entry.Window;
                bool same = layout.Id.Equals(existing.LayoutId, StringComparison.OrdinalIgnoreCase);
                if (same)
                {
                    lastDeckPanelKey = key;
                    if (existing.IsVisible)
                    {
                        if (existing.IsPresentationHiding)
                        {
                            existing.PrepareForShow();
                            NotifyDeckPresentationStateChanged();
                            return;
                        }
                        if (existing.IsCollapsedToEdge)
                        {
                            existing.ExpandFromEdge();
                            return;
                        }
                        existing.RequestHideForReuse();
                        return;
                    }
                    existing.RefreshAppearance(deckConfig.InputPanelOpacityPercent, deckConfig.DeckHoverPreviewsEnabled, deckConfig.DeckAfterActionBehavior, deckConfig.DeckPointerLeaveBehavior);
                    // An explicitly requested show must reveal the complete
                    // Deck even if this cached window was hidden while it was
                    // still in its edge-tab state.
                    if (existing.IsCollapsedToEdge)
                        existing.ExpandFromEdge();
                    existing.PrepareForShow();
                    existing.Show();
                    NotifyDeckPresentationStateChanged();
                    return;
                }
                existing.Close();
            }
            CreateDeckPanel(deckConfig, action, key, layout, cascade: true);
            return;
        }
        if (action is NumpadAction or ExtendedKeypadAction)
        {
            CloseDeckPanels();
            bool extended = action == ExtendedKeypadAction;
            if (inputPanel is { IsVisible: true } existing && existing.IsExtended == extended)
            {
                existing.Close();
                inputPanel = null;
                return;
            }
            inputPanel?.Close();
            AppConfig? panelConfig = configProvider?.Invoke();
            int opacity = panelConfig?.InputPanelOpacityPercent ?? 96;
            bool useUsLayout = panelConfig?.KeyboardLayout == "US";
            inputPanel = new InputPanelOverlayWindow(extended, opacity, useUsLayout, panelConfig, inputPanelPositionChanged);
            inputPanel.Closed += (_, _) => inputPanel = null;
            inputPanel.Show();
            return;
        }

        inputPanel?.Close();
        inputPanel = null;
        CloseDeckPanels();
        if (FullScreenVisible)
        {
            CloseScreenOverlays();
            return;
        }
        AppConfig config = configProvider?.Invoke() ?? new AppConfig();
        bool clockAction = action == ClockAction;
        var overlaysToShow = new List<ScreenOverlayWindow>();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            bool showClock = clockAction && (config.ClockShowOnAllMonitors || screen.Primary);
            var overlay = new ScreenOverlayWindow(screen, showClock, config, clockAction);
            overlay.Closed += ScreenOverlayClosed;
            overlaysToShow.Add(overlay);
        }
        lock (screenOverlayGate)
            screenOverlays.AddRange(overlaysToShow);
        fullScreenStartCursor = System.Windows.Forms.Cursor.Position;
        Interlocked.Exchange(ref fullScreenClosing, 0);
        // 物理キーから起動した場合は、その起動キーのUpを受け取るまで解除を
        // 許可しない。マクロ等からの起動は、最初の新しい入力ですぐ解除できる。
        Interlocked.Exchange(ref fullScreenDismissArmed, physicalInputDownProvider?.Invoke() == true ? 0 : 1);
        foreach (var overlay in overlaysToShow)
            overlay.Show();
        // Publish input consumption only after at least one native fullscreen
        // surface is actually visible. Construction or Show failure is handled
        // by RunOverlayUiSafely and can therefore never leave a flag-only wall.
        if (overlaysToShow.Any(overlay => overlay.HasVisibleNativeSurface))
            Interlocked.Exchange(ref fullScreenActive, 1);
        else
            CloseScreenOverlays();
    }

    /// <summary>起動に使ったキーを離すまでは待ち、次の新しいキー操作で全画面表示を閉じます。</summary>
    internal static bool TryDismissFullScreenKeyboard(bool down)
    {
        if (!FullScreenVisible)
            return false;
        bool armed = Volatile.Read(ref fullScreenDismissArmed) != 0;
        if (!armed && !down)
        {
            ArmFullScreenDismiss();
            return true;
        }
        if (ShouldDismissFullScreenKeyboard(armed, down))
            RequestCloseScreenOverlays();
        return true;
    }

    /// <summary>起動操作を離した後のマウス移動・押下・ホイールで全画面表示を閉じます。</summary>
    internal static bool TryDismissFullScreenMouse(int message, int x, int y)
    {
        if (!FullScreenVisible)
            return false;
        bool armed = Volatile.Read(ref fullScreenDismissArmed) != 0;
        if (!armed && message is 0x202 or 0x205 or 0x208 or 0x20C)
        {
            ArmFullScreenDismiss();
            return true;
        }
        bool moved = message == 0x200 && Math.Abs(x - fullScreenStartCursor.X) + Math.Abs(y - fullScreenStartCursor.Y) >= 3;
        if (ShouldDismissFullScreenMouse(armed, message, moved))
            RequestCloseScreenOverlays();
        return true;
    }

    internal static bool ShouldDismissFullScreenKeyboard(bool armed, bool down) => armed && down;
    internal static bool ShouldDismissFullScreenMouse(bool armed, int message, bool moved)
        => armed && (moved || message is 0x201 or 0x204 or 0x207 or 0x20B or 0x20A or 0x20E);
    internal static void ArmFullScreenDismissForTest() => ArmFullScreenDismiss();
    static void ArmFullScreenDismiss()
    {
        fullScreenStartCursor = System.Windows.Forms.Cursor.Position;
        Interlocked.Exchange(ref fullScreenDismissArmed, 1);
    }

    static void RequestCloseScreenOverlays()
    {
        if (Interlocked.Exchange(ref fullScreenClosing, 1) != 0)
            return;
        try
        {
            var dispatcher = WpfApplication.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                FailOpenScreenOverlays();
                return;
            }
            _ = dispatcher.BeginInvoke(CloseScreenOverlays);
            ScheduleFullScreenCloseFailOpenWatchdog();
        }
        catch
        {
            // The hook has already consumed the dismissing input. If the UI
            // dispatcher is unavailable, hide the native surfaces immediately
            // and stop consuming further physical input.
            FailOpenScreenOverlays();
        }
    }

    static void CloseScreenOverlays()
    {
        var windows = SnapshotScreenOverlays();
        try
        {
            foreach (var window in windows)
            {
                try { window.HideNativeImmediately(); } catch { }
                try { window.Close(); } catch { }
            }
        }
        finally
        {
            lock (screenOverlayGate)
                screenOverlays.Clear();
            // Input callbacks consult this flag before every mapping. Its
            // cleanup must not depend on every WPF Window closing cleanly.
            ResetFullScreenTransaction();
        }
    }

    static void ScreenOverlayClosed(object? sender, EventArgs e)
    {
        bool noOverlaysRemain;
        lock (screenOverlayGate)
        {
            if (sender is ScreenOverlayWindow overlay)
                screenOverlays.Remove(overlay);
            noOverlaysRemain = screenOverlays.Count == 0;
        }
        // A display/session transition can destroy a WPF overlay without going
        // through CloseScreenOverlays. Never leave the hooks consuming input
        // after the last native fullscreen surface is gone.
        if (noOverlaysRemain)
            ResetFullScreenTransaction();
    }

    static void FailOpenScreenOverlays()
    {
        ScreenOverlayWindow[] windows = SnapshotScreenOverlays();
        foreach (var window in windows)
            try { window.HideNativeImmediately(); } catch { }
        ResetFullScreenTransaction();
    }

    static ScreenOverlayWindow[] SnapshotScreenOverlays()
    {
        lock (screenOverlayGate)
            return screenOverlays.ToArray();
    }

    static void ScheduleFullScreenCloseFailOpenWatchdog()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(FullScreenCloseFailOpenMilliseconds).ConfigureAwait(false);
            if (FullScreenVisible && Volatile.Read(ref fullScreenClosing) != 0)
                FailOpenScreenOverlays();
        });
    }

    static void ResetFullScreenTransaction()
    {
        Interlocked.Exchange(ref fullScreenActive, 0);
        Interlocked.Exchange(ref fullScreenClosing, 0);
        Interlocked.Exchange(ref fullScreenDismissArmed, 0);
    }

}
