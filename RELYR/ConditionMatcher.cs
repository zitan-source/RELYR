using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace RELYR;

public static class ConditionMatcher
{
    static readonly Lock CacheSync = new();
    static IntPtr cachedForegroundWindow;
    static string cachedForegroundProcess = "";

    public static bool IsCursorOverTaskbar()
    {
        if (!GetCursorPos(out var point))
            return false;
        var hwnd = WindowFromPoint(point);
        if (hwnd == IntPtr.Zero)
            return false;
        var name = new System.Text.StringBuilder(128);
        GetClassName(hwnd, name, name.Capacity);
        if (IsTaskbarClass(name.ToString()))
            return true;
        var root = GetAncestor(hwnd, 2);
        if (root == IntPtr.Zero || root == hwnd)
            return false;
        name.Clear();
        GetClassName(root, name, name.Capacity);
        return IsTaskbarClass(name.ToString());
    }
    public static bool IsTaskbarClass(string className) => className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "TrayNotifyWnd" or "ReBarWindow32" or "MSTaskListWClass";
    public static bool ForegroundProcessMatches(string condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return true;
        return Matches(condition, ForegroundProcessName());
    }
    public static string ForegroundProcessName()
    {
#if HOOK_DIAGNOSTICS
        long diagnosticStarted = Stopwatch.GetTimestamp();
#endif
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
#if HOOK_DIAGNOSTICS
            HookDiagnosticsTrace.Record(HookDiagnosticStage.ForegroundProcessNoWindow, Stopwatch.GetTimestamp() - diagnosticStarted, value1: hwnd.ToInt64());
#endif
            return "";
        }
        lock (CacheSync)
            if (hwnd == cachedForegroundWindow)
            {
#if HOOK_DIAGNOSTICS
                HookDiagnosticsTrace.Record(HookDiagnosticStage.ForegroundProcessCacheHit, Stopwatch.GetTimestamp() - diagnosticStarted, value1: hwnd.ToInt64());
#endif
                return cachedForegroundProcess;
            }
        GetWindowThreadProcessId(hwnd, out var pid);
#if HOOK_DIAGNOSTICS
        HookDiagnosticsTrace.Record(HookDiagnosticStage.ForegroundProcessLookupStarted, value1: hwnd.ToInt64(), value2: pid);
#endif
        string processName;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            processName = process.ProcessName;
        }
        catch { processName = ""; }
#if HOOK_DIAGNOSTICS
        HookDiagnosticsTrace.Record(HookDiagnosticStage.ForegroundProcessLookupCompleted, Stopwatch.GetTimestamp() - diagnosticStarted, value1: hwnd.ToInt64(), value2: pid, result: string.IsNullOrEmpty(processName) ? 0 : 1);
#endif
        lock (CacheSync)
        {
            cachedForegroundWindow = hwnd;
            cachedForegroundProcess = processName;
        }
        return processName;
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
    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X, Y;
    }
}
