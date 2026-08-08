using System.IO;

namespace RELYR;

public static class StartupIntegrationTest
{
    public static int Run(TextWriter output)
    {
        var report = new VerificationReport(output);
        Action<bool, string> Check = report.Check;
        try
        {
            const string executable = @"C:\Program Files\RELYR\RELYR.exe";
            Check(StartupService.BuildCommand(executable) == $"\"{executable}\" --tray", "startup task launches the installed executable in tray mode");
            string launcher = StartupService.BuildLauncherCommand(executable);
            Check(launcher == $"\"{executable}\" --elevated-task \"$(Arg0)\"", "manual launch task accepts a safe encoded argument payload");
            string[] arguments = ["--macro-id", "日本語 マクロ", "quoted \"value\""];
            string encoded = StartupService.EncodeArguments(arguments);
            Check(StartupService.DecodeElevatedArguments(encoded).SequenceEqual(arguments), "scheduled-task argument forwarding preserves every argument exactly");
            Check(!encoded.Any(char.IsWhiteSpace), "scheduled-task argument payload contains no command-line whitespace");
            Check(StartupService.MultipleInstancePolicy(true) == 2 && StartupService.MultipleInstancePolicy(false) == 0, "logon startup ignores duplicate launches while command launcher remains available for macro actions");
            Check(App.IsMainUiLaunch([]) && App.IsMainUiLaunch(["--tray"]) && !App.IsMainUiLaunch(["--macro-id", "abc"]), "single-instance guard applies only to the resident main application");
            Check(!App.ShouldScanForOrphans(["--tray"]) && App.ShouldScanForOrphans([]), "logon startup skips the blocking orphan-process scan");
            Check(StartupService.SameExecutablePath(executable, executable.ToUpperInvariant()) && !StartupService.SameExecutablePath(executable, @"C:\Temp\RELYR.exe"), "executable path comparison remains case-insensitive");
            Check(StartupService.IsRelyrExecutableIdentity("RELYR.exe", "RELYR", "RELYR.dll"),
                "a RELYR process is identified independently of its folder and thread count");
            Check(!StartupService.IsRelyrExecutableIdentity("RELYR.exe", "Other product", "RELYR.dll")
                && !StartupService.IsRelyrExecutableIdentity("Other.exe", "RELYR", "RELYR.dll"),
                "unrelated processes are never treated as RELYR remnants");
            using (var show = new EventWaitHandle(false, EventResetMode.AutoReset))
            using (var acknowledgement = new EventWaitHandle(false, EventResetMode.AutoReset))
            {
                var responder = Task.Run(() => { show.WaitOne(); acknowledgement.Set(); });
                Check(App.WaitForExistingInstanceResponse(show, acknowledgement, 1000), "single-instance notification requires a live UI acknowledgement");
                responder.Wait();
                Check(!App.WaitForExistingInstanceResponse(show, acknowledgement, 5), "an unresponsive instance is not mistaken for a healthy instance");
            }
            using var current = System.Diagnostics.Process.GetCurrentProcess();
            int before = current.HandleCount;
            for (int index = 0; index < 500; index++)
                ConditionMatcher.ProcessUnderCursor();
            int growth = current.HandleCount - before;
            Check(growth < 20, $"cursor profile polling does not leak process handles (growth={growth})");
        }
        catch (Exception ex) { report.RecordException("exception", "FAIL exception: ", ex); }
        return report.Complete("STARTUP INTEGRATION TEST PASSED", "STARTUP INTEGRATION TEST FAILED", includeFailureNames: false);
    }
}
