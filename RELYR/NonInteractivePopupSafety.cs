using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace RELYR;

/// <summary>
/// Applies the native styles required for a visual-only WPF popup. Setting
/// IsHitTestVisible on the WPF child is not sufficient because Popup owns a
/// separate top-level HWND that Windows can still choose as a click target.
/// </summary>
internal static class NonInteractivePopupSafety
{
    const int GwlExStyle = -20;
    const long WsExTransparent = 0x00000020L;
    const long WsExToolWindow = 0x00000080L;
    const long WsExNoActivate = 0x08000000L;
    const long RequiredStyles = WsExTransparent | WsExToolWindow | WsExNoActivate;

    internal static void Apply(Popup popup)
    {
        popup.IsHitTestVisible = false;
        if (popup.Child is not Visual child || PresentationSource.FromVisual(child) is not HwndSource source || source.Handle == IntPtr.Zero)
            return;

        try
        {
            long style = GetWindowLongPtr(source.Handle, GwlExStyle).ToInt64();
            _ = SetWindowLongPtr(source.Handle, GwlExStyle, new IntPtr(style | RequiredStyles));
        }
        catch { }
    }

#if !PRODUCTION_PUBLISH
    internal static bool HasRequiredStylesForTest(Popup popup)
    {
        if (popup.Child is not Visual child || PresentationSource.FromVisual(child) is not HwndSource source || source.Handle == IntPtr.Zero)
            return false;
        long style = GetWindowLongPtr(source.Handle, GwlExStyle).ToInt64();
        return !popup.IsHitTestVisible && (style & RequiredStyles) == RequiredStyles;
    }
#endif

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
