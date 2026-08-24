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
            Check(App.ShouldStartMediumUiHost(false, []) && App.ShouldStartMediumUiHost(false, ["--tray"])
                && !App.ShouldStartMediumUiHost(true, []) && !App.ShouldStartMediumUiHost(false, ["--macro-id", "abc"])
                && App.EnsureUiHostArgument(["--tray"]).SequenceEqual(["--tray", "--ui-host"])
                && App.EnsureUiHostArgument(["--ui-host"]).SequenceEqual(["--ui-host"]),
                "the visible main UI stays at ordinary integrity while privileged one-shot commands remain separate");
            string[] restartChildArguments = App.RestartChildArguments(1234);
            Check(restartChildArguments.SequenceEqual(["--restart-after-pid", "1234", App.RestartLauncherArgument])
                && App.IsRestartLauncher(restartChildArguments)
                && App.RestartTargetArguments([App.RestartLauncherArgument]).Length == 0,
                "tray restart waits for the old process and then re-enters through the ordinary UI-host launch path");
            Check(!App.ShouldScanForOrphans(["--tray"]) && App.ShouldScanForOrphans([]), "logon startup skips the blocking orphan-process scan");
            Check(App.ShouldDeferEditorUi(["--tray"], new AppConfig { FirstRunCompleted = true })
                && !App.ShouldDeferEditorUi([], new AppConfig { FirstRunCompleted = true })
                && !App.ShouldDeferEditorUi(["--tray"], new AppConfig { FirstRunCompleted = false }),
                "completed tray startup defers only editor visuals while normal and first-run launches initialize the full UI");
            Check(StartupService.SameExecutablePath(executable, executable.ToUpperInvariant()) && !StartupService.SameExecutablePath(executable, @"C:\Temp\RELYR.exe"), "executable path comparison remains case-insensitive");
            Check(StartupService.IsRelyrExecutableIdentity("RELYR.exe", "RELYR", "RELYR.dll"),
                "a RELYR process is identified independently of its folder and thread count");
            Check(!StartupService.IsRelyrExecutableIdentity("RELYR.exe", "Other product", "RELYR.dll")
                && !StartupService.IsRelyrExecutableIdentity("Other.exe", "RELYR", "RELYR.dll"),
                "unrelated processes are never treated as RELYR remnants");
            Check(IpcProcessIdentity.TryGetProcessImagePath((uint)Environment.ProcessId, out string currentImagePath)
                && File.Exists(currentImagePath),
                "stale-process recovery reads an executable path through the limited-information process API");
            using (var show = new EventWaitHandle(false, EventResetMode.AutoReset))
            using (var acknowledgement = new EventWaitHandle(false, EventResetMode.AutoReset))
            {
                var responder = Task.Run(() => { show.WaitOne(); acknowledgement.Set(); });
                Check(App.WaitForExistingInstanceResponse(show, acknowledgement, 1000), "single-instance notification requires a live UI acknowledgement");
                responder.Wait();
                Check(!App.WaitForExistingInstanceResponse(show, acknowledgement, 5), "an unresponsive instance is not mistaken for a healthy instance");
            }
            int exitChecks = 0;
            Check(App.WaitForInstanceExit(() => ++exitChecks < 3, 5, _ => { }),
                "a launcher waits through a resident shutdown race and continues after the mutex disappears");
            Check(!App.WaitForInstanceExit(() => true, 3, _ => { }),
                "a launcher never treats a resident that retained the mutex as stopped");
            string shutdownInProgress = App.BuildShutdownInProgressSignalName(executable);
            Check(shutdownInProgress != App.BuildShutdownSignalName(executable)
                && shutdownInProgress == App.BuildShutdownInProgressSignalName(executable.ToUpperInvariant()),
                "shutdown-in-progress handoff is path-specific and stable across path casing");
            string handoffPath = Path.Combine(Path.GetTempPath(), $"RELYR-startup-handoff-{Environment.ProcessId}.exe");
            using (var handoff = new EventWaitHandle(false, EventResetMode.ManualReset, App.BuildShutdownInProgressSignalName(handoffPath)))
            {
                Check(!App.IsResidentShutdownPending(handoffPath), "an ordinary resident is not mistaken for one that is exiting");
                handoff.Set();
                Check(App.IsResidentShutdownPending(handoffPath), "a launcher observes the resident shutdown handoff signal");
            }
            using var current = System.Diagnostics.Process.GetCurrentProcess();
            int before = current.HandleCount;
            for (int index = 0; index < 500; index++)
                ConditionMatcher.ForegroundProcessName();
            int growth = current.HandleCount - before;
            Check(growth < 20, $"foreground profile polling does not leak process handles (growth={growth})");
        }
        catch (Exception ex) { report.RecordException("exception", "FAIL exception: ", ex); }
        return report.Complete("STARTUP INTEGRATION TEST PASSED", "STARTUP INTEGRATION TEST FAILED", includeFailureNames: false);
    }
}
