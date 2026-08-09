using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace RELYR;

public static class ConditionMatcher
{
    public static bool IsCursorOverTaskbar()
    {
        if (!GetCursorPos(out var point))
            return false;
        var hwnd = WindowFromPoint(point);
        if (hwnd == IntPtr.Zero)
            return false;
        var name = new System.Text.StringBuilder(128);
        GetClassName(hwnd, name, name.Capacity);
        string value = name.ToString();
        if (IsTaskbarClass(value))
            return true;
        var root = GetAncestor(hwnd, 2);
        if (root != IntPtr.Zero)
        {
            name.Clear();
            GetClassName(root, name, name.Capacity);
            if (IsTaskbarClass(name.ToString()))
                return true;
        }
        bool insideTaskbar = false;
        EnumWindows((window, _) =>
        {
            name.Clear();
            GetClassName(window, name, name.Capacity);
            if (IsTaskbarClass(name.ToString()) && GetWindowRect(window, out var rect) && point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom)
            {
                insideTaskbar = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return insideTaskbar;
    }
    public static bool IsTaskbarClass(string className) => className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "TrayNotifyWnd" or "ReBarWindow32" or "MSTaskListWClass";
    public static bool ForegroundProcessMatches(string condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return true;
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return Matches(condition, process.ProcessName);
        }
        catch { return false; }
    }
    internal static bool IsForegroundVirtualMachineConsole()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return IsVirtualMachineConsoleProcess(process.ProcessName);
        }
        catch { return false; }
    }
    internal static bool IsVirtualMachineConsoleProcess(string processName)
        => Path.GetFileNameWithoutExtension(processName).Equals("VirtualBoxVM", StringComparison.OrdinalIgnoreCase);
    public static string ProcessUnderCursor()
    {
        return ProcessesUnderCursor().FirstOrDefault() ?? "";
    }
    internal static IntPtr RootWindowUnderCursor()
    {
        if (!GetCursorPos(out var point))
            return IntPtr.Zero;
        IntPtr leaf = WindowFromPoint(point);
        if (leaf == IntPtr.Zero)
            return IntPtr.Zero;
        IntPtr root = GetAncestor(leaf, 2);
        return root == IntPtr.Zero ? leaf : root;
    }
    public static IReadOnlyList<string> ProcessesUnderCursor()
    {
        if (!GetCursorPos(out var point))
            return [];
        IntPtr leaf = WindowFromPoint(point);
        if (leaf == IntPtr.Zero)
            return [];
        IntPtr root = GetAncestor(leaf, 2);
        IntPtr owner = root == IntPtr.Zero ? IntPtr.Zero : GetWindow(root, 4);
        var processes = new List<string>(3);
        // Top-level ownership identifies the host application for Chromium/Qt
        // render children; the leaf remains a fallback for unusual windows.
        foreach (IntPtr window in new[] { root, owner, leaf })
        {
            if (window == IntPtr.Zero)
                continue;
            var className = new System.Text.StringBuilder(128);
            GetClassName(window, className, className.Capacity);
            if (IsShellClass(className.ToString()))
                continue;
            GetWindowThreadProcessId(window, out var pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (!processes.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
                    processes.Add(process.ProcessName);
            }
            catch { }
        }
        return processes;
    }
    internal static bool IsShellClass(string className) => IsTaskbarClass(className) || className is "Progman" or "WorkerW" or "Windows.UI.Core.CoreWindow";
    public static bool Matches(string condition, string actualProcess) => string.IsNullOrWhiteSpace(condition) || Path.GetFileNameWithoutExtension(condition).Equals(Path.GetFileNameWithoutExtension(actualProcess), StringComparison.OrdinalIgnoreCase);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder text, int maxCount);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X, Y;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
}
