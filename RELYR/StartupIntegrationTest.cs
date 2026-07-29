using System.IO;

namespace RELYR;

public static class StartupIntegrationTest
{
    public static int Run(TextWriter output)
    {
        var failures=new List<string>();
        void Check(bool value,string name){output.WriteLine((value?"PASS ":"FAIL ")+name);if(!value)failures.Add(name);}
        try
        {
            const string executable=@"C:\Program Files\RELYR\RELYR.exe";
            Check(StartupService.BuildCommand(executable)==$"\"{executable}\" --tray","startup task launches the installed executable in tray mode");
            string launcher=StartupService.BuildLauncherCommand(executable);
            Check(launcher==$"\"{executable}\" --elevated-task \"$(Arg0)\"","manual launch task accepts a safe encoded argument payload");
            string[] arguments=["--macro-id","日本語 マクロ","quoted \"value\""];
            string encoded=StartupService.EncodeArguments(arguments);
            Check(StartupService.DecodeElevatedArguments(encoded).SequenceEqual(arguments),"scheduled-task argument forwarding preserves every argument exactly");
            Check(!encoded.Any(char.IsWhiteSpace),"scheduled-task argument payload contains no command-line whitespace");
            Check(StartupService.MultipleInstancePolicy(true)==2&&StartupService.MultipleInstancePolicy(false)==0,"logon startup ignores duplicate launches while command launcher remains available for macro actions");
            Check(App.IsMainUiLaunch([])&&App.IsMainUiLaunch(["--tray"])&&!App.IsMainUiLaunch(["--macro-id","abc"]),"single-instance guard applies only to the resident main application");
            Check(StartupService.SameExecutablePath(executable,executable.ToUpperInvariant())&&!StartupService.SameExecutablePath(executable,@"C:\Temp\RELYR.exe"),"stale-instance recovery only terminates the same installed executable");
            Check(StartupService.IsOrphanedRelyrProcess("RELYR.exe","RELYR","RELYR.dll",1),"a fully identified one-thread RELYR shutdown remnant is recoverable");
            Check(!StartupService.IsOrphanedRelyrProcess("RELYR.exe","RELYR","RELYR.dll",2)
                &&!StartupService.IsOrphanedRelyrProcess("RELYR.exe","Other product","RELYR.dll",1)
                &&!StartupService.IsOrphanedRelyrProcess("Other.exe","RELYR","RELYR.dll",1),
                "a healthy or unverified process is never treated as a RELYR shutdown remnant");
            Check(StartupService.ShouldBlockUnverifiedRelyr(1)&&!StartupService.ShouldBlockUnverifiedRelyr(2),
                "an inaccessible shutdown remnant blocks new hooks while a healthy process is left to the mutex guard");
            using(var show=new EventWaitHandle(false,EventResetMode.AutoReset))
            using(var acknowledgement=new EventWaitHandle(false,EventResetMode.AutoReset))
            {
                var responder=Task.Run(()=>{show.WaitOne();acknowledgement.Set();});
                Check(App.WaitForExistingInstanceResponse(show,acknowledgement,1000),"single-instance notification requires a live UI acknowledgement");
                responder.Wait();
                Check(!App.WaitForExistingInstanceResponse(show,acknowledgement,5),"an unresponsive instance is not mistaken for a healthy instance");
            }
            using(var current=System.Diagnostics.Process.GetCurrentProcess())
            {
                int before=current.HandleCount;
                for(int index=0;index<500;index++)ConditionMatcher.ProcessUnderCursor();
                int growth=current.HandleCount-before;
                Check(growth<20,$"cursor profile polling does not leak process handles (growth={growth})");
            }
        }
        catch(Exception ex){output.WriteLine("FAIL exception: "+ex);failures.Add("exception");}
        output.WriteLine(failures.Count==0?"STARTUP INTEGRATION TEST PASSED":"STARTUP INTEGRATION TEST FAILED");return failures.Count==0?0:1;
    }
}
