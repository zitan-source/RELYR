using System.Runtime.InteropServices;

namespace RELYR;

public static class VirtualDesktopService
{
    internal static (int Count, int CurrentNumber) GetState()
    {
        int count = VirtualDesktopAccessor.Count;
        int current = VirtualDesktopAccessor.CurrentNumber + 1;
        if (count < 1 || current < 1 || current > count)
            throw new InvalidOperationException($"仮想デスクトップの状態を取得できませんでした（個数={count}、現在={current}）。");
        return (count, current);
    }

    internal static IntPtr GetForegroundRootWindow()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero)
            throw new InvalidOperationException("移動するアクティブウィンドウがありません。");
        IntPtr root = GetAncestor(window, 2);
        return root != IntPtr.Zero ? root : window;
    }

    internal static Guid GetWindowDesktopId(IntPtr window)
    {
        var manager = (IVirtualDesktopManager)(object)new VirtualDesktopManager();
        try
        {
            int hr = manager.GetWindowDesktopId(window, out Guid id);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
            return id;
        }
        finally { Marshal.ReleaseComObject(manager); }
    }

    internal static bool AllowAnyProcessToSetForegroundWindow() => AllowSetForegroundWindow(unchecked((uint)-1));

    internal static bool ActivateWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window))
            return false;
        if (IsIconic(window))
            ShowWindowAsync(window, 9);
        if (GetForegroundWindow() == window)
            return true;

        BringWindowToTop(window);
        if (SetForegroundWindow(window) || GetForegroundWindow() == window)
            return true;

        uint currentThread = GetCurrentThreadId();
        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        bool attached = foregroundThread != 0
            && foregroundThread != currentThread
            && AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            BringWindowToTop(window);
            return SetForegroundWindow(window) || GetForegroundWindow() == window;
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    internal static List<Guid> ParseDesktopIds(byte[]? bytes)
    {
        var result = new List<Guid>();
        if (bytes == null || bytes.Length % 16 != 0)
            return result;
        for (int i = 0; i < bytes.Length; i += 16)
            result.Add(new Guid(bytes.AsSpan(i, 16)));
        return result;
    }

    [ComImport, Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")]
    sealed class VirtualDesktopManager
    {
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out int onCurrentDesktop);
        [PreserveSig] int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);
    }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] static extern bool AttachThreadInput(uint attach, uint attachTo, bool value);
}
