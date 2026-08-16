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
        var devices = new[]
        {
            new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RidevInputSink, hwndTarget = source.Handle },
            new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x06, dwFlags = RidevInputSink, hwndTarget = source.Handle }
        };
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
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
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        HookDiagnosticsTrace.Record(HookDiagnosticStage.RawInputEnter, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: lParam.ToInt64());
        try
        {
            return ProcessRawInput(lParam);
        }
        catch (Exception exception)
        {
            HookDiagnosticsTrace.Record(HookDiagnosticStage.RawInputFault, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: exception.HResult, value2: HookDiagnosticsTrace.ExceptionCode(exception));
            throw;
        }
        finally
        {
            HookDiagnosticsTrace.Record(HookDiagnosticStage.RawInputExit, System.Diagnostics.Stopwatch.GetTimestamp() - started, keyboardHook, mouseHook);
        }
    }

    IntPtr ProcessRawInput(IntPtr lParam)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        if (GetRawInputData(lParam, RidInput, IntPtr.Zero, ref size, headerSize) != 0 || size < headerSize + 8)
            return IntPtr.Zero;
        HookDiagnosticsTrace.Record(HookDiagnosticStage.RawInputSizeRead, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: size, value2: headerSize);
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            uint copied = size;
            if (GetRawInputData(lParam, RidInput, buffer, ref copied, headerSize) != size)
                return IntPtr.Zero;
            HookDiagnosticsTrace.Record(HookDiagnosticStage.RawInputDataCopied, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: copied, value2: size);
            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            HookDiagnosticsTrace.Record(HookDiagnosticStage.RawInputDecoded, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: header.dwType, value2: header.dwSize);
            if (header.dwType == 1)
            {
                HookDiagnosticsTrace.Record(HookDiagnosticStage.RawKeyboardTransition, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: Volatile.Read(ref rawKeyboardTransitions) + 1, value2: Volatile.Read(ref lowLevelKeyboardTransitions));
                ObserveRawHookTransition(true);
                return IntPtr.Zero;
            }
            if (header.dwType != 0)
                return IntPtr.Zero;
            IntPtr mouse = IntPtr.Add(buffer, checked((int)headerSize));
            ushort flags = unchecked((ushort)Marshal.ReadInt16(mouse, 4));
            int transitions = 0;
            foreach (ushort flag in new ushort[] { 0x0001, 0x0002, 0x0004, 0x0008, 0x0010, 0x0020, 0x0040, 0x0080, 0x0100, 0x0200 })
                if ((flags & flag) != 0)
                    transitions++;
            if (transitions > 0)
            {
                HookDiagnosticsTrace.Record(HookDiagnosticStage.RawMouseTransition, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: flags, value2: transitions);
                ObserveRawHookTransition(false, transitions);
            }
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
            HookDiagnosticsTrace.Record(HookDiagnosticStage.RawInputReconciled, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: flags, value2: transitions);
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
