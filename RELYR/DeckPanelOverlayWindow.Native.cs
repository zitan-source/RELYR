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

internal sealed partial class DeckPanelOverlayWindow
{
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
            AssignDeckFile(targetSlot, filePath);
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
                AssignDeckFile(targetSlot, path);
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
