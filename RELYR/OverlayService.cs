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
    internal const string NumpadAction = "ShowNumpadOverlay";
    internal const string ExtendedKeypadAction = "ShowExtendedKeypadOverlay";
    internal const string DeckPanelAction = "ShowDeckPanelOverlay";
    internal const string BlankAction = "ShowBlankOverlay";
    internal const string ClockAction = "ShowClockOverlay";

    static InputPanelOverlayWindow? inputPanel;
    static DeckPanelOverlayWindow? deckPanel;
    static readonly List<ScreenOverlayWindow> screenOverlays = [];
    static Func<AppConfig>? configProvider;
    static Func<bool>? physicalInputDownProvider;
    static Action<Mapping>? deckActionRequested;
    static Action<double, double>? deckPositionChanged;
    static Action? deckLayoutChanged;
    static Action<string, int, int>? deckSlotsChanged;
    static Action<bool, double, double>? inputPanelPositionChanged;
    static int fullScreenActive;
    static int fullScreenClosing;
    static int fullScreenDismissArmed;
    static int deckRefreshQueued;
    static System.Drawing.Point fullScreenStartCursor;
#if !PRODUCTION_PUBLISH
    internal static Action<string>? ActionRequestedForTest;
#endif
    internal static bool FullScreenVisible => Volatile.Read(ref fullScreenActive) != 0;

    internal static void Configure(Func<AppConfig>? provider, Func<bool>? inputDownProvider = null, Action<Mapping>? deckAction = null, Action<double, double>? positionChanged = null, Action<bool, double, double>? inputPositionChanged = null, Action? layoutChanged = null, Action<string, int, int>? slotsChanged = null)
    {
        configProvider = provider;
        physicalInputDownProvider = inputDownProvider;
        deckActionRequested = deckAction;
        deckPositionChanged = positionChanged;
        inputPanelPositionChanged = inputPositionChanged;
        deckLayoutChanged = layoutChanged;
        deckSlotsChanged = slotsChanged;
    }
    internal static void Shutdown()
    {
        if (WpfApplication.Current?.Dispatcher.CheckAccess() == true)
        {
            inputPanel?.Close();
            inputPanel = null;
            deckPanel?.Close();
            deckPanel = null;
            CloseScreenOverlays();
            configProvider = null;
            physicalInputDownProvider = null;
            deckActionRequested = null;
            deckPositionChanged = null;
            inputPanelPositionChanged = null;
            deckLayoutChanged = null;
            deckSlotsChanged = null;
        }
        else if (WpfApplication.Current is { } app)
            _ = app.Dispatcher.BeginInvoke(Shutdown);
    }

    internal static void RefreshDeckPanel()
    {
        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher == null || Interlocked.Exchange(ref deckRefreshQueued, 1) != 0)
            return;
        _ = dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            Interlocked.Exchange(ref deckRefreshQueued, 0);
            if (deckPanel is not { IsVisible: true } panel)
                return;
            var config = configProvider?.Invoke();
            if (config != null)
                panel.Refresh(config.InputPanelOpacityPercent, config.DeckHoverPreviewsEnabled);
        }));
    }

    internal static void NotifyDeckLayoutChanged(bool refreshDeckPanel = true, string? layoutId = null, int? firstSlot = null, int? secondSlot = null)
    {
        if (layoutId is not null && firstSlot is int first && secondSlot is int second)
            deckSlotsChanged?.Invoke(layoutId, first, second);
        else
            deckLayoutChanged?.Invoke();
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
        _ = dispatcher.BeginInvoke(() => ShowOnUiThread(action));
        return true;
    }

    static void ShowOnUiThread(string action)
    {
        if (DeckPanelLayout.IsDeckAction(action))
        {
            inputPanel?.Close();
            inputPanel = null;
            AppConfig deckConfig = configProvider?.Invoke() ?? new AppConfig();
            var layout = DeckPanelLayout.ResolveActionLayout(deckConfig, action);
            if (deckPanel is { IsVisible: true } existing)
            {
                bool same = layout?.Id.Equals(existing.LayoutId, StringComparison.OrdinalIgnoreCase) == true;
                existing.Close();
                deckPanel = null;
                if (same)
                    return;
            }
            if (layout == null)
                return;
            deckPanel = new DeckPanelOverlayWindow(deckConfig, deckActionRequested, deckConfig.InputPanelOpacityPercent, deckPositionChanged, layout);
            deckPanel.Closed += (_, _) => deckPanel = null;
            deckPanel.Show();
            return;
        }
        if (action is NumpadAction or ExtendedKeypadAction)
        {
            deckPanel?.Close();
            deckPanel = null;
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
        deckPanel?.Close();
        deckPanel = null;
        if (FullScreenVisible)
        {
            CloseScreenOverlays();
            return;
        }
        AppConfig config = configProvider?.Invoke() ?? new AppConfig();
        bool clockAction = action == ClockAction;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            bool showClock = clockAction && (config.ClockShowOnAllMonitors || screen.Primary);
            var overlay = new ScreenOverlayWindow(screen, showClock, config, clockAction);
            screenOverlays.Add(overlay);
        }
        fullScreenStartCursor = System.Windows.Forms.Cursor.Position;
        Interlocked.Exchange(ref fullScreenActive, 1);
        Interlocked.Exchange(ref fullScreenClosing, 0);
        // 物理キーから起動した場合は、その起動キーのUpを受け取るまで解除を
        // 許可しない。マクロ等からの起動は、最初の新しい入力ですぐ解除できる。
        Interlocked.Exchange(ref fullScreenDismissArmed, physicalInputDownProvider?.Invoke() == true ? 0 : 1);
        foreach (var overlay in screenOverlays)
            overlay.Show();
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
        _ = WpfApplication.Current.Dispatcher.BeginInvoke(CloseScreenOverlays);
    }

    static void CloseScreenOverlays()
    {
        foreach (var window in screenOverlays.ToArray())
            window.Close();
        screenOverlays.Clear();
        Interlocked.Exchange(ref fullScreenActive, 0);
        Interlocked.Exchange(ref fullScreenClosing, 0);
        Interlocked.Exchange(ref fullScreenDismissArmed, 0);
    }

}
