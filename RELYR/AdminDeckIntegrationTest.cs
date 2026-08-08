using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RELYR;

internal static class AdminDeckIntegrationTest
{
    internal static string ReportPath => VerificationPaths.GetFile("admin-deck-test-last.log");

    internal static async Task<int> RunAsync()
    {
        string? previousConfigDirectory = Environment.GetEnvironmentVariable("RELYR_CONFIG_DIR");
        string directory = VerificationPaths.CreateRunDirectory("admin-deck-test");
        MainWindow? window = null;
        Process? taskManager = null;
        var lines = new List<string>();
        try
        {
            Environment.SetEnvironmentVariable("RELYR_CONFIG_DIR", directory);
            var config = new AppConfig { EngineEnabled = false };
            new ConfigService().Save(config);
            IpcRuntime.IsUiHost = true;
            window = new MainWindow(true, startupConfig: config, runtimeRole: RuntimeRole.UiHost);
            App.Current.MainWindow = window;
            window.Show();

            bool connected = await IpcRuntime.StartUiHostAsync(window).WaitAsync(TimeSpan.FromSeconds(15));
            var helper = IpcRuntime.HelperIdentity;
            bool identityValid = connected && helper is { IsElevated: true } &&
                string.Equals(Path.GetFullPath(helper.ImagePath), Path.GetFullPath(Environment.ProcessPath!), StringComparison.OrdinalIgnoreCase);
            lines.Add($"UI PID={Environment.ProcessId}; path={Environment.ProcessPath}; integrity=Medium");
            lines.Add(helper == null ? "Helper=not connected" : $"Helper PID={helper.ProcessId}; path={helper.ImagePath}; integrity={(helper.IsElevated ? "High" : "Medium")}");
            if (!identityValid)
                throw new InvalidOperationException("The elevated helper did not establish a verified high-integrity IPC connection.");

            if (Process.GetProcessesByName("Taskmgr").Any())
                throw new InvalidOperationException("Close an existing Task Manager window before running the admin Deck integration test.");
            taskManager = Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true, Verb = "runas" })
                ?? throw new InvalidOperationException("Task Manager could not be started.");
            if (!await WaitForWindowAsync(taskManager, TimeSpan.FromSeconds(12)))
                throw new InvalidOperationException("An elevated Task Manager window did not become available.");
            BringToFront(taskManager.MainWindowHandle);
            await Task.Delay(350);

            var deckAction = new Mapping { Input = DeckPanelLayout.InputName(1), Layer = DeckPanelLayout.Layer, Kind = ActionKind.Shortcut, Value = "Alt+F4" };
            if (!window.ExecuteDeckActionForTest(deckAction))
                throw new InvalidOperationException("The Deck action was not accepted for execution.");
            bool closed = await WaitForExitAsync(taskManager, TimeSpan.FromSeconds(8));
            lines.Add(closed ? "PASS elevated Task Manager closed through Deck -> IPC helper -> SendInput." : "FAIL elevated Task Manager remained open after Deck execution.");
            return closed ? 0 : 1;
        }
        catch (Exception error)
        {
            lines.Add("FAIL " + error.GetType().Name + ": " + error.Message);
            return 1;
        }
        finally
        {
            try
            {
                await IpcRuntime.StopAsync();
            }
            catch { }
            taskManager?.Dispose();
            Environment.SetEnvironmentVariable("RELYR_CONFIG_DIR", previousConfigDirectory);
            File.WriteAllLines(ReportPath, lines, Encoding.UTF8);
            try
            {
                window?.RequestApplicationExit();
            }
            catch { }
        }
    }

    static async Task<bool> WaitForWindowAsync(Process process, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                process.Refresh();
                if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                    return true;
            }
            catch { return false; }
            await Task.Delay(100);
        }
        return false;
    }

    static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                process.Refresh();
                if (process.HasExited)
                    return true;
            }
            catch { return true; }
            await Task.Delay(100);
        }
        return false;
    }

    static void BringToFront(IntPtr window)
    {
        if (window == IntPtr.Zero)
            return;
        ShowWindow(window, 9);
        SetForegroundWindow(window);
    }

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
}
