using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RELYR;

// The medium UI owns overlay windows.  Explorer cannot deliver a file drop to
// an elevated Deck, so the high helper sends only this display request.
internal static class OverlayUiBridge
{
    const int WmCopyData = 0x004A;
    static readonly IntPtr MessageTag = new(0x524F564C); // ROVL

    internal static void Attach(Window window)
    {
        if (PresentationSource.FromVisual(window) is HwndSource source)
            source.AddHook(WindowProcedure);
    }

    internal static bool RequestShow(string value)
    {
        if (!OverlayService.IsOverlayAction(value))
            return false;
        IntPtr destination = FindWindow(null, $"RELYR v{MainWindow.DisplayVersion}");
        if (destination == IntPtr.Zero)
            return false;
        IntPtr payload = Marshal.StringToHGlobalUni(value);
        try
        {
            var data = new CopyData { Data = MessageTag, ByteCount = (value.Length + 1) * sizeof(char), Payload = payload };
            return SendMessageTimeout(destination, WmCopyData, IntPtr.Zero, ref data, 2, 1500, out _) != IntPtr.Zero;
        }
        finally { Marshal.FreeHGlobal(payload); }
    }

    static IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmCopyData)
            return IntPtr.Zero;
        try
        {
            var data = Marshal.PtrToStructure<CopyData>(lParam);
            if (data.Data != MessageTag || data.Payload == IntPtr.Zero || data.ByteCount < sizeof(char))
                return IntPtr.Zero;
            string? value = Marshal.PtrToStringUni(data.Payload, data.ByteCount / sizeof(char));
            value = value?.TrimEnd('\0');
            if (string.IsNullOrWhiteSpace(value) || !OverlayService.IsOverlayAction(value))
                return IntPtr.Zero;
            OverlayService.TryShow(value);
            handled = true;
            return new IntPtr(1);
        }
        catch { return IntPtr.Zero; }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CopyData
    {
        public IntPtr Data; public int ByteCount; public IntPtr Payload;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string? className, string windowName);
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SendMessageTimeout(IntPtr hwnd, int message, IntPtr wParam, ref CopyData data, uint flags, uint timeout, out IntPtr result);
}
