using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace RELYR;

// Limited to mapped input and the Deck -> elevated-helper boundary so a real
// privileged input can be diagnosed without creating a general-purpose input log.
internal static class DeckIpcDiagnostics
{
    [ThreadStatic] static HelperInputScope? activeHelperInput;

    internal static void LogUiShortcutDispatch(string source, string shortcut, WindowActionTarget intendedTarget, bool sent)
    {
        var helper = IpcRuntime.HelperIdentity;
        string helperText = helper == null
            ? "HelperPid=<none>; HelperPath=<none>; HelperIntegrity=<none>"
            : $"HelperPid={helper.ProcessId}; HelperPath={helper.ImagePath}; HelperIntegrity={helper.IntegrityLevel}";
        Write($"UI shortcut dispatch; Source={source}; " +
              $"UiPid={Environment.ProcessId}; UiPath={Environment.ProcessPath}; UiIntegrity={IpcProcessIdentity.CurrentIntegrityLevel()}; " +
              $"IpcConnected={IpcRuntime.IsConnected}; {helperText}; Shortcut={shortcut}; IntendedWindowActionTarget={intendedTarget}; SendResult={sent}");
    }

    internal static void LogMappedActionQueued(string input, Mapping mapping)
        => Write($"UI action queued; Input={input}; MappingInput={mapping.Input}; Kind={mapping.Kind}; Value={mapping.Value}; Layer={mapping.Layer}");

    // Keep one record for each mapped high-integrity input so its owner and
    // foreground target can be distinguished from an IPC failure.
    internal static void LogElevatedHookAction(string input, Mapping mapping)
        => Write($"Elevated helper input; Input={input}; MappingInput={mapping.Input}; Kind={mapping.Kind}; Value={mapping.Value}; Layer={mapping.Layer}; {DescribeForegroundWindow()}");

    // The always-on status log records only a fixed event name. Any detail is
    // written exclusively to the explicit opt-in detailed diagnostics log.
    internal static void LogIpcStartup(string stage, string detail)
    {
        DiagnosticLogStorage.WriteStatus(DiagnosticLogStorage.IpcStatusLogPath, "ipc", stage);
        DiagnosticLogStorage.WriteDetailed("ipc", stage, $"Integrity={IpcProcessIdentity.CurrentIntegrityLevel()}; {detail}");
    }

    internal static void LogHelperReceivedShortcut(string shortcut, WindowActionTarget target)
    {
        Write("Helper shortcut received; " +
              $"HelperPid={Environment.ProcessId}; HelperPath={Environment.ProcessPath}; HelperIntegrity={IpcProcessIdentity.CurrentIntegrityLevel()}; " +
              $"Shortcut={shortcut}; WindowActionTarget={target}; {DescribeForegroundWindow()}");
    }

    internal static HelperInputScope BeginHelperInput(string shortcut, WindowActionTarget target)
    {
        var scope = new HelperInputScope(shortcut, target);
        activeHelperInput = scope;
        return scope;
    }

    internal static void RecordSendInput(bool succeeded, int win32Error)
        => activeHelperInput?.Record(succeeded, win32Error);

    static string DescribeForegroundWindow()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero)
            return "ForegroundWindowPid=<none>; ForegroundWindowPath=<none>";
        GetWindowThreadProcessId(window, out uint pid);
        string path = "<unavailable>";
        try
        {
            using var process = Process.GetProcessById((int)pid);
            path = process.MainModule?.FileName ?? "<unavailable>";
        }
        catch { }
        return $"ForegroundWindowPid={pid}; ForegroundWindowPath={path}";
    }

    static void Write(string message)
    {
        DiagnosticLogStorage.WriteDetailed("ipc", "mapped-action", message);
    }

    internal sealed class HelperInputScope(string shortcut, WindowActionTarget target) : IDisposable
    {
        int calls;
        int failures;
        int lastError;
        bool completed;
        Exception? error;

        internal void Record(bool succeeded, int win32Error)
        {
            calls++;
            if (!succeeded)
            {
                failures++;
                lastError = win32Error;
            }
        }

        internal void Complete() => completed = true;
        internal void Fail(Exception exception) => error = exception;

        public void Dispose()
        {
            if (ReferenceEquals(activeHelperInput, this))
                activeHelperInput = null;
            string result = error == null
                ? $"Completed={completed}; SendInputSuccess={(calls == 0 ? "not-used" : failures == 0)}; SendInputCalls={calls}; Win32Error={lastError}"
                : $"Completed=false; SendInputSuccess=false; SendInputCalls={calls}; Win32Error={lastError}; Exception={error.GetType().Name}: {error.Message}";
            Write($"Helper input result; Value={shortcut}; WindowActionTarget={target}; {result}");
        }
    }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
