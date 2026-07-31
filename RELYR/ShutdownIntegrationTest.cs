using System.Diagnostics;
using System.IO;

namespace RELYR;

internal static class ShutdownIntegrationTest
{
    internal static string ReadySignalName(string token)=>@"Local\RELYR.ShutdownTestReady."+token;

    internal static int Run(TextWriter output)
    {
        Process? host=null;
        var failures=new List<string>();
        void Check(bool value,string name)
        {
            output.WriteLine((value?"PASS ":"FAIL ")+name);
            if(!value)failures.Add(name);
        }

        try
        {
            string processPath=Environment.ProcessPath??throw new InvalidOperationException("テスト実行プロセスを取得できません。");
            string assemblyPath=Environment.GetCommandLineArgs()[0];
            string token=Guid.NewGuid().ToString("N");
            using var ready=new EventWaitHandle(false,EventResetMode.AutoReset,ReadySignalName(token));
            string hostArguments=Path.GetFileNameWithoutExtension(processPath).Equals("dotnet",StringComparison.OrdinalIgnoreCase)
                ?$"\"{assemblyPath}\" --shutdown-test-host {token}"
                :$"--shutdown-test-host {token}";
            host=Process.Start(new ProcessStartInfo(processPath,hostArguments){UseShellExecute=false})
                ??throw new InvalidOperationException("停止状態のテストプロセスを起動できません。");

            Check(ready.WaitOne(TimeSpan.FromSeconds(8)),"shutdown test host reaches an intentionally blocked UI dispatcher");
            if(failures.Count==0)
            {
                using var shutdown=EventWaitHandle.OpenExisting(App.BuildShutdownSignalName(processPath));
                var elapsed=Stopwatch.StartNew();
                shutdown.Set();
                bool exited=host.WaitForExit(8000);
                elapsed.Stop();
                Check(exited&&elapsed.Elapsed<TimeSpan.FromSeconds(7),
                    $"installer shutdown exits a UI-hung process through the independent watchdog ({elapsed.Elapsed.TotalSeconds:F1}s)");
            }
        }
        catch(Exception ex)
        {
            output.WriteLine("FAIL shutdown exception: "+ex);
            failures.Add("exception");
        }
        finally
        {
            if(host!=null)
            {
                try{if(!host.HasExited)host.Kill(false);}catch{}
                host.Dispose();
            }
        }

        output.WriteLine(failures.Count==0?"SHUTDOWN INTEGRATION TEST PASSED":"SHUTDOWN INTEGRATION TEST FAILED");
        return failures.Count==0?0:1;
    }
}
