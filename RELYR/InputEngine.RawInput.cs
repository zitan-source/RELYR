using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace RELYR;

public sealed partial class InputEngine
{
    const int WmInput = 0x00FF;
    const uint RidInput = 0x10000003;
    const uint RidevInputSink = 0x00000100;

    HwndSource CreateRawMouseInputSource()
    {
        var parameters = new HwndSourceParameters("RELYR raw mouse release monitor")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            WindowStyle = 0,
            Width = 0,
            Height = 0
        };
        var source = new HwndSource(parameters);
        source.AddHook(RawMouseWindowProc);
        var device = new RAWINPUTDEVICE
        {
            usUsagePage = 0x01,
            usUsage = 0x02,
            dwFlags = RidevInputSink,
            hwndTarget = source.Handle
        };
        if (!RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
        {
            int error = Marshal.GetLastWin32Error();
            source.Dispose();
            throw new System.ComponentModel.Win32Exception(error);
        }
        return source;
    }

    IntPtr RawMouseWindowProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmInput)
            return IntPtr.Zero;
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        if (GetRawInputData(lParam, RidInput, IntPtr.Zero, ref size, headerSize) != 0 || size < headerSize + 8)
            return IntPtr.Zero;
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            uint copied = size;
            if (GetRawInputData(lParam, RidInput, buffer, ref copied, headerSize) != size)
                return IntPtr.Zero;
            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            if (header.dwType != 0)
                return IntPtr.Zero;
            IntPtr mouse = IntPtr.Add(buffer, checked((int)headerSize));
            ushort flags = unchecked((ushort)Marshal.ReadInt16(mouse, 4));
            if ((flags & 0x0001) != 0) ObserveRawMouseButtonDown("MouseLeft");
            if ((flags & 0x0004) != 0) ObserveRawMouseButtonDown("MouseRight");
            if ((flags & 0x0010) != 0) ObserveRawMouseButtonDown("MouseMiddle");
            if ((flags & 0x0040) != 0) ObserveRawMouseButtonDown("MouseBack");
            if ((flags & 0x0100) != 0) ObserveRawMouseButtonDown("MouseForward");
            if ((flags & 0x0002) != 0) ReconcileRawMouseButtonUp("MouseLeft");
            if ((flags & 0x0008) != 0) ReconcileRawMouseButtonUp("MouseRight");
            if ((flags & 0x0020) != 0) ReconcileRawMouseButtonUp("MouseMiddle");
            if ((flags & 0x0080) != 0) ReconcileRawMouseButtonUp("MouseBack");
            if ((flags & 0x0200) != 0) ReconcileRawMouseButtonUp("MouseForward");
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint deviceCount, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);
}
