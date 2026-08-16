using System.Diagnostics;
using System.IO;

namespace RELYR;

internal static class ShutdownIntegrationTest
{
    internal static string ReadySignalName(string token) => @"Local\RELYR.ShutdownTestReady." + token;

    internal static int Run(TextWriter output)
    {
        Process? host = null;
        var report = new VerificationReport(output);
        Action<bool, string> Check = report.Check;

        try
        {
            string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("テスト実行プロセスを取得できません。");
            string assemblyPath = Environment.GetCommandLineArgs()[0];
            string token = Guid.NewGuid().ToString("N");
            using var ready = new EventWaitHandle(false, EventResetMode.AutoReset, ReadySignalName(token));
            string hostArguments = Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? $"\"{assemblyPath}\" --shutdown-test-host {token}"
                : $"--shutdown-test-host {token}";
            host = Process.Start(new ProcessStartInfo(processPath, hostArguments) { UseShellExecute = false })
                ?? throw new InvalidOperationException("停止状態のテストプロセスを起動できません。");

            Check(ready.WaitOne(TimeSpan.FromSeconds(8)), "shutdown test host reaches an intentionally blocked UI dispatcher");
            if (report.HasNoFailures)
            {
                using var shutdown = EventWaitHandle.OpenExisting(App.BuildShutdownSignalName(processPath));
                var elapsed = Stopwatch.StartNew();
                shutdown.Set();
                bool exited = host.WaitForExit(8000);
                elapsed.Stop();
                Check(exited && elapsed.Elapsed < TimeSpan.FromSeconds(7),
                    $"installer shutdown exits a UI-hung process through the independent watchdog ({elapsed.Elapsed.TotalSeconds:F1}s)");
            }
        }
        catch (Exception ex)
        {
            report.RecordException("exception", "FAIL shutdown exception: ", ex);
        }
        finally
        {
            if (host != null)
            {
                try
                {
                    if (!host.HasExited)
                        host.Kill(false);
                }
                catch { }
                host.Dispose();
            }
        }

        try
        {
            string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("トレイ終了テスト実行プロセスを取得できません。");
            string assemblyPath = Environment.GetCommandLineArgs()[0];
            string hostArguments = Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? $"\"{assemblyPath}\" --tray --tray-exit-regression-host --no-input-hooks"
                : "--tray --tray-exit-regression-host --no-input-hooks";
            using var trayHost = Process.Start(new ProcessStartInfo(processPath, hostArguments) { UseShellExecute = false })
                ?? throw new InvalidOperationException("トレイ終了テストプロセスを起動できません。");
            string normalizedPath = Path.GetFullPath(processPath);
            var elapsed = Stopwatch.StartNew();
            bool exited = false;
            while (elapsed.Elapsed < TimeSpan.FromSeconds(10))
            {
                bool hostExited = trayHost.HasExited;
                bool relatedProcessRemains = Process.GetProcessesByName("RELYR")
                    .Any(process =>
                    {
                        try
                        {
                            return process.Id != Environment.ProcessId
                                && IpcProcessIdentity.TryGetProcessImagePath((uint)process.Id, out string imagePath)
                                && Path.GetFullPath(imagePath).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase);
                        }
                        catch { return false; }
                        finally { process.Dispose(); }
                    });
                if (hostExited && !relatedProcessRemains)
                {
                    exited = true;
                    break;
                }
                Thread.Sleep(100);
            }
            Check(exited, "tray Exit menu item terminates RELYR.exe and any same-path helper within 10 seconds");
            if (!trayHost.HasExited)
                trayHost.Kill(false);
        }
        catch (Exception ex)
        {
            output.WriteLine("FAIL tray exit regression exception: " + ex);
            report.RecordException("tray exit regression exception", "FAIL tray exit regression exception: ", ex);
        }

        return report.Complete("SHUTDOWN INTEGRATION TEST PASSED", "SHUTDOWN INTEGRATION TEST FAILED", includeFailureNames: false);
    }
}
