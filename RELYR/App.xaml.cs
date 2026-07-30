using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace RELYR;

public partial class App : System.Windows.Application
{
#if !PRODUCTION_PUBLISH
    internal static string EngineTestReportPath=>Path.Combine(Path.GetTempPath(),"RELYR-engine-test-last.log");
    internal static string UiTestReportPath=>Path.Combine(Path.GetTempPath(),"RELYR-ui-test-last.log");
#endif
    internal const string InstanceMutexName=@"Local\RELYR.SingleInstance.v2";
    const string SignalName=@"Local\RELYR.ShowExisting.v1";
    const string AcknowledgementName=@"Local\RELYR.ShowExistingAck.v1";
    const int ExistingInstanceResponseTimeoutMs=3000;
    Mutex? instanceMutex;
    EventWaitHandle? showSignal;
    EventWaitHandle? showAcknowledgement;
    EventWaitHandle? shutdownSignal;
    ForegroundWindowTracker? foregroundWindowTracker;
    bool ownsMutex;
    readonly CancellationTokenSource signalStop=new();
    public App()
    {
        // 入力フックを他のマウス常駐ソフトより先に解除できるよう、
        // Windows終了通知をアプリ用の最優先範囲で受け取る。
        SetProcessShutdownParameters(0x3FF,0);
        DispatcherUnhandledException+=(_,e)=>{InputEngine.ReleaseAll();e.Handled=true;ShutdownWithExitCode(1);};
        AppDomain.CurrentDomain.UnhandledException+=(_,_)=>InputEngine.ReleaseAll();
        AppDomain.CurrentDomain.ProcessExit+=(_,_)=>InputEngine.ReleaseAll();
        TaskScheduler.UnobservedTaskException+=(_,e)=>{InputEngine.ReleaseAll();e.SetObserved();};
        SystemEvents.PowerModeChanged+=SystemPowerModeChanged;
        SystemEvents.SessionSwitch+=SystemSessionSwitch;
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string[] args=e.Args;
#if !PRODUCTION_PUBLISH
        if(args.Contains("--profile-switch-test-host",StringComparer.OrdinalIgnoreCase))
        {
            string title=args.SkipWhile(x=>!x.Equals("--profile-switch-test-host",StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault()
                ??ProfileSwitchRuntimeTest.HostWindowTitle;
            var host=new Window{Title=title,Left=160,Top=160,Width=420,Height=260,WindowStartupLocation=WindowStartupLocation.Manual,ShowInTaskbar=true};
            MainWindow=host;host.Show();return;
        }
        if(args.Contains("--profile-switch-runtime-test",StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(()=>_=RunProfileSwitchRuntimeTestAndExit());
            return;
        }
#endif
#if PRODUCTION_PUBLISH
        if(args.Length>=2&&args[0].Equals("--elevated-task",StringComparison.OrdinalIgnoreCase))
        {
            if(!StartupService.IsProcessElevated()){AppDialog.Show("管理者モードの起動タスクが正しく構成されていません。RELYRを再インストールしてください。","起動できません",MessageBoxButton.OK,MessageBoxImage.Error);ExitImmediately(1);return;}
            try{args=StartupService.DecodeElevatedArguments(args[1]);}
            catch(Exception ex){AppDialog.Show("起動情報を読み取れませんでした。\n\n"+ex.Message,"起動できません",MessageBoxButton.OK,MessageBoxImage.Error);ExitImmediately(1);return;}
        }
        else if(!StartupService.IsProcessElevated())
        {
            args=AttachMacroShortcutTarget(args,WindowMonitorService.GetActiveWindowForShortcut);
            // The process launched by the taskbar owns the foreground permission that
            // the elevated scheduled-task process does not. Restore the captured target
            // and pass that permission on before signalling or starting RELYR.
            if(ReadMacroShortcutTarget(args) is { } launchTarget)
            {
                VirtualDesktopService.AllowAnyProcessToSetForegroundWindow();
                WindowMonitorService.PrepareShortcutTarget(launchTarget);
            }
            if(IsMainUiLaunch(args)&&MainInstanceExists())
            {
                if(NotifyExistingInstance())
                {
                    if(ShouldExplainDuplicate(args))ShowAlreadyRunningMessage();
                }
                else if(!RequestStaleInstanceRecovery(args,out string recoveryError))
                {
                    AppDialog.Show(recoveryError,"RELYRを再起動できません",MessageBoxButton.OK,MessageBoxImage.Error);
                }
                ExitImmediately(0);return;
            }
            if(!StartupService.TryRunElevated(args,out string error))AppDialog.Show(error,"起動できません",MessageBoxButton.OK,MessageBoxImage.Error);
            ExitImmediately(string.IsNullOrEmpty(error)?0:1);return;
        }
#endif
#if PRODUCTION_PUBLISH
        if(args.Length>=2&&args[0].Equals("--recover-stale-instance",StringComparison.OrdinalIgnoreCase))
        {
            RunStaleInstanceRecovery(args[1]);
            return;
        }
#endif
        IntPtr? shortcutTarget=ReadMacroShortcutTarget(args);
        if(ShortcutService.TryReadMacroId(args,out string macroId)){ShutdownMode=ShutdownMode.OnExplicitShutdown;Dispatcher.BeginInvoke(()=>_=RunMacroShortcutAndExit(macroId,true,shortcutTarget));return;}
        if(ShortcutService.TryReadMacroName(args,out string macroName)){ShutdownMode=ShutdownMode.OnExplicitShutdown;Dispatcher.BeginInvoke(()=>_=RunMacroShortcutAndExit(macroName,false,shortcutTarget));return;}
        if(args.Length>=1&&args[0].Equals("--configure-elevated-launcher",StringComparison.OrdinalIgnoreCase)){try{StartupService.EnsureElevatedLauncher();ExitImmediately(0);}catch{ExitImmediately(1);}return;}
        if(args.Length>=1&&args[0].Equals("--remove-elevated-tasks",StringComparison.OrdinalIgnoreCase)){try{StartupService.RemoveElevatedTasks();ExitImmediately(0);}catch{ExitImmediately(1);}return;}
        if(args.Length>=1&&args[0].Equals("--uninstall-needs-restart",StringComparison.OrdinalIgnoreCase)){try{var uninstallState=new ConfigService().Load();ExitImmediately(UninstallRestartNeeded(uninstallState,LegacyKeyRemapService.HasCapsLockToF13())?10:0);}catch{ExitImmediately(10);}return;}
        if(args.Length>=1&&args[0].Equals("--prepare-uninstall",StringComparison.OrdinalIgnoreCase)){try{LegacyKeyRemapService.SetCapsLockToF13(false);StartupService.RemoveElevatedTasks();var uninstallConfig=new ConfigService();var value=uninstallConfig.Load();value.StartWithWindows=false;value.CapsLockLayerEnabled=false;uninstallConfig.Save(value);ExitImmediately(0);}catch{ExitImmediately(1);}return;}
        if(args.Length>=1&&args[0].Equals("--delete-user-settings",StringComparison.OrdinalIgnoreCase)){ExitImmediately(ConfigService.DeleteAllUserData()?0:1);return;}
        if(args.Length>=2&&args[0].Equals("--configure-caps-remap",StringComparison.OrdinalIgnoreCase)){try{LegacyKeyRemapService.SetCapsLockToF13(args[1].Equals("on",StringComparison.OrdinalIgnoreCase));ExitImmediately(0);}catch{ExitImmediately(1);}return;}
        if(args.Contains("--shutdown-existing",StringComparer.OrdinalIgnoreCase)){try{EventWaitHandle.OpenExisting(BuildShutdownSignalName(Environment.ProcessPath)).Set();}catch{}ExitImmediately(0);return;}
        if(args.Length>=2&&args[0].Equals("--configure-startup",StringComparison.OrdinalIgnoreCase)){try{bool enabled=args[1].Equals("on",StringComparison.OrdinalIgnoreCase);StartupService.SetEnabled(enabled);var startupConfig=new ConfigService();var value=startupConfig.Load();value.StartWithWindows=enabled;startupConfig.Save(value);ExitImmediately(0);}catch{ExitImmediately(1);}return;}
#if !PRODUCTION_PUBLISH
        if(e.Args.Contains("--desktop-helper",StringComparer.OrdinalIgnoreCase))
        {
            var helper=new Window{Title="RELYR Desktop Test",Width=320,Height=160,WindowStartupLocation=WindowStartupLocation.CenterScreen};
            MainWindow=helper;helper.Show();return;
        }
        if(e.Args.Contains("--ui-test",StringComparer.OrdinalIgnoreCase))
        {
            try{File.Delete(UiTestReportPath);}catch{}
            using var uiLog=new StreamWriter(UiTestReportPath,false,Encoding.UTF8){AutoFlush=true};
            int result=UiIntegrationTest.Run(uiLog);
            ShutdownWithExitCode(result);
            return;
        }
        if (e.Args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownWithExitCode(SelfTest.Run(Console.Out));
            return;
        }
        if(e.Args.Contains("--engine-test",StringComparer.OrdinalIgnoreCase)||e.Args.Contains("--engine-test-no-real",StringComparer.OrdinalIgnoreCase))
        {
            bool includeRealHook=!e.Args.Contains("--engine-test-no-real",StringComparer.OrdinalIgnoreCase);
            try{File.Delete(EngineTestReportPath);}catch{}
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(()=>_=RunEngineTestAndExit(includeRealHook));
            return;
        }
        if(e.Args.Contains("--startup-test",StringComparer.OrdinalIgnoreCase)){ShutdownWithExitCode(StartupIntegrationTest.Run(Console.Out));return;}
        if(e.Args.Contains("--update-test",StringComparer.OrdinalIgnoreCase)){ShutdownMode=ShutdownMode.OnExplicitShutdown;Dispatcher.BeginInvoke(()=>_=RunUpdateTestAndExit());return;}
        if(e.Args.Contains("--desktop-test",StringComparer.OrdinalIgnoreCase)){ShutdownWithExitCode(VirtualDesktopIntegrationTest.Run(Console.Out));return;}
#endif
        instanceMutex=new Mutex(true,InstanceMutexName,out ownsMutex);
        if(!ownsMutex)
        {
            if(NotifyExistingInstance()&&ShouldExplainDuplicate(args))ShowAlreadyRunningMessage();
            ExitImmediately(0);return;
        }
        try{foregroundWindowTracker=new ForegroundWindowTracker();}catch{}
        var loadedStartupConfig=new ConfigService().Load();
        ThemeService.Apply(loadedStartupConfig.ThemeMode);
        if(ShouldScanForOrphans(args)
           &&!StartupService.TryTerminateOrphanedRelyrInstances(TimeSpan.FromSeconds(3),out string orphanError))
        {
            AppDialog.Show(
                "以前のRELYRの終了処理が残っているため、安全のため入力機能を開始しません。\n\n"+orphanError,
                "RELYRを起動できません",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ExitImmediately(1);return;
        }
        showSignal=new EventWaitHandle(false,EventResetMode.AutoReset,SignalName);
        showAcknowledgement=new EventWaitHandle(false,EventResetMode.AutoReset,AcknowledgementName);
        shutdownSignal=new EventWaitHandle(false,EventResetMode.ManualReset,BuildShutdownSignalName(Environment.ProcessPath));
        var window=new MainWindow(args.Contains("--skip-setup",StringComparer.OrdinalIgnoreCase),startupConfig:loadedStartupConfig);
        MainWindow=window;
        StartStartupMaintenance(loadedStartupConfig);
        bool needsSetup=window.NeedsFirstRunSetup;
        if(!args.Contains("--tray",StringComparer.OrdinalIgnoreCase)||needsSetup)window.Show();
        if(needsSetup)Dispatcher.BeginInvoke(window.ShowFirstRunSetup);
        _=Task.Run(()=>ListenForShow(window));
        _=Task.Run(()=>ListenForShutdown(window));
    }

    static void StartStartupMaintenance(AppConfig config)
    {
        var macros=config.Macros.Select(x=>new MacroDefinition{Id=x.Id,Name=x.Name}).ToArray();
        bool startWithWindows=config.StartWithWindows;
        var thread=new Thread(()=>
        {
            try{foreach(var macro in macros)ShortcutService.UpgradeExistingMacroShortcut(macro);}catch{}
            try{StartupService.EnsureMatchesConfig(startWithWindows);}catch{}
        })
        {
            IsBackground=true,
            Name="RELYR startup maintenance"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    internal static bool IsMainUiLaunch(IReadOnlyList<string> args)=>args.All(x=>x.Equals("--tray",StringComparison.OrdinalIgnoreCase)||x.Equals("--skip-setup",StringComparison.OrdinalIgnoreCase));
    internal static bool ShouldScanForOrphans(IReadOnlyList<string> args)=>!args.Contains("--tray",StringComparer.OrdinalIgnoreCase);
    static bool ShouldExplainDuplicate(IReadOnlyList<string> args)=>!args.Contains("--tray",StringComparer.OrdinalIgnoreCase);
    static bool MainInstanceExists()
    {
        try
        {
            if(!Mutex.TryOpenExisting(InstanceMutexName,out var existing))return false;
            existing.Dispose();return true;
        }
        catch(UnauthorizedAccessException){return true;}
    }
    internal static bool NotifyExistingInstance(int timeoutMilliseconds=ExistingInstanceResponseTimeoutMs)
    {
        for(int attempt=0;attempt<8;attempt++)
        {
            try
            {
                using var signal=EventWaitHandle.OpenExisting(SignalName);
                using var acknowledgement=EventWaitHandle.OpenExisting(AcknowledgementName);
                return WaitForExistingInstanceResponse(signal,acknowledgement,timeoutMilliseconds);
            }
            catch(WaitHandleCannotBeOpenedException){Thread.Sleep(50);}
            catch(UnauthorizedAccessException){return false;}
        }
        return false;
    }
    internal static bool WaitForExistingInstanceResponse(EventWaitHandle signal,EventWaitHandle acknowledgement,int timeoutMilliseconds)
    {
        acknowledgement.Reset();
        signal.Set();
        return acknowledgement.WaitOne(timeoutMilliseconds);
    }
    static bool RequestStaleInstanceRecovery(IReadOnlyList<string> originalArguments,out string error)
    {
        string encoded=StartupService.EncodeArguments(originalArguments);
        return StartupService.TryRunElevated(["--recover-stale-instance",encoded],out error);
    }
#if PRODUCTION_PUBLISH
    void RunStaleInstanceRecovery(string encodedArguments)
    {
        ShutdownMode=ShutdownMode.OnExplicitShutdown;
        try
        {
            string[] originalArguments=StartupService.DecodeElevatedArguments(encodedArguments);
            if(!StartupService.TryTerminateOtherInstalledInstances(TimeSpan.FromSeconds(6),out string terminationError))
                throw new InvalidOperationException(terminationError);
            if(!StartupService.TryRunElevated(originalArguments,out string launchError))
                throw new InvalidOperationException(launchError);
            ExitImmediately(0);
        }
        catch(Exception ex)
        {
            AppDialog.Show("応答しないRELYRを自動復旧できませんでした。\n\n"+ex.Message,"RELYRを再起動できません",MessageBoxButton.OK,MessageBoxImage.Error);
            ExitImmediately(1);
        }
    }
#endif
    static void ShowAlreadyRunningMessage()=>AppDialog.Show("RELYRはすでに起動しています。\n通知領域のRELYRアイコンから開くこともできます。","RELYRは起動中です",MessageBoxButton.OK,MessageBoxImage.Information);
    internal static void ArmForcedProcessExit(TimeSpan delay)
    {
        int processId=Environment.ProcessId;
        var watchdog=new Thread(()=>
        {
            Thread.Sleep(delay);
            try{Process.GetProcessById(processId).Kill(false);}catch{}
        }){IsBackground=true,Name="RELYR exit watchdog"};
        watchdog.Start();
    }
    internal static void ExitImmediately(int exitCode)
    {
        // Release injected input first, then let WPF close normally. A native
        // process kill is kept only as a silent watchdog so shutdown can never
        // surface the CLR "unknown software exception" dialog.
        try{if(Current?.MainWindow is RELYR.MainWindow window)window.PrepareVisualsForImmediateExit();}catch{}
        try{InputEngine.ReleaseAll();}catch{}
        Environment.ExitCode=exitCode;
        ArmForcedProcessExit(TimeSpan.FromSeconds(3));
        try
        {
            if(Current==null){Process.GetCurrentProcess().Kill(false);return;}
            if(Current.Dispatcher.CheckAccess())Current.Shutdown(exitCode);
            else Current.Dispatcher.BeginInvoke(new Action(()=>Current.Shutdown(exitCode)));
        }
        catch
        {
            try{Process.GetCurrentProcess().Kill(false);}catch{}
        }
    }
    void ShutdownWithExitCode(int exitCode)=>ExitImmediately(exitCode);
    void ListenForShow(MainWindow window)
    {
        try
        {
            while(WaitHandle.WaitAny([showSignal!,signalStop.Token.WaitHandle])==0)
            {
                Dispatcher.BeginInvoke(()=>
                {
                    try{window.ShowFromExternalLaunch();}
                    finally{showAcknowledgement?.Set();}
                });
            }
        }
        catch(ObjectDisposedException){}
    }
    void ListenForShutdown(MainWindow window)
    {
        try{if(WaitHandle.WaitAny([shutdownSignal!,signalStop.Token.WaitHandle])==0)Dispatcher.BeginInvoke(window.RequestApplicationExit);}
        catch(ObjectDisposedException){}
    }
    internal static string BuildShutdownSignalName(string? executablePath)
    {
        string normalized=string.IsNullOrWhiteSpace(executablePath)?"unknown":Path.GetFullPath(executablePath).ToUpperInvariant();
        string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..20];
        return @"Local\RELYR.ShutdownExisting.v2."+hash;
    }
    // A saved UI preference alone does not change Windows and must never cause
    // a reboot prompt.  A restart is needed only when the registry remap is
    // currently present, or a CapsLock registry change is awaiting a reboot.
    internal static bool UninstallRestartNeeded(AppConfig config,bool registryRemap,bool? pendingRestart=null)=>registryRemap||(pendingRestart??LegacyKeyRemapService.IsRestartStillPending(config));
#if !PRODUCTION_PUBLISH
    async Task RunUpdateTestAndExit(){int result=await UpdateIntegrationTest.RunAsync(Console.Out);ShutdownWithExitCode(result);}
    async Task RunEngineTestAndExit(bool includeRealHook)
    {
        int result=1;
        using var stream=new FileStream(EngineTestReportPath,FileMode.Create,FileAccess.Write,FileShare.ReadWrite);
        using var log=TextWriter.Synchronized(new StreamWriter(stream,new UTF8Encoding(false)){AutoFlush=true});
        try{result=await EngineIntegrationTest.RunAsync(log,includeRealHook).WaitAsync(TimeSpan.FromSeconds(45));}
        catch(TimeoutException){log.WriteLine("FAIL engine test exceeded the 45 second safety limit");}
        catch(Exception ex){log.WriteLine(ex);}
        finally
        {
            try{InputEngine.ReleaseAll();}catch(Exception ex){log.WriteLine("FAIL input release: "+ex.Message);result=1;}
            log.WriteLine("EXIT_CODE="+result);
            try{Console.Out.Write(File.ReadAllText(EngineTestReportPath));Console.Out.Flush();}catch{}
            ExitImmediately(result);
        }
    }
#if !PRODUCTION_PUBLISH
    async Task RunProfileSwitchRuntimeTestAndExit()
    {
        int result=await ProfileSwitchRuntimeTest.RunAsync();
        try{File.AppendAllText(ProfileSwitchRuntimeTest.ReportPath,"app:runtime-test-returned"+Environment.NewLine,Encoding.UTF8);}catch{}
        ShutdownWithExitCode(result);
    }
#endif
#endif
    internal static string[] AttachMacroShortcutTarget(IReadOnlyList<string> arguments,Func<IntPtr> resolveTarget)
    {
        if(ReadMacroShortcutTarget(arguments)!=null||!ShortcutService.TryReadMacroId(arguments,out _)&&!ShortcutService.TryReadMacroName(arguments,out _))return [..arguments];
        IntPtr target=resolveTarget();
        return target==IntPtr.Zero?[..arguments]:[..arguments,"--target-hwnd",target.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)];
    }
    internal static IntPtr? ReadMacroShortcutTarget(IReadOnlyList<string> arguments)
    {
        for(int i=0;i+1<arguments.Count;i++)
            if(arguments[i].Equals("--target-hwnd",StringComparison.OrdinalIgnoreCase)&&long.TryParse(arguments[i+1],System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out long value)&&value!=0)
                return new IntPtr(value);
        return null;
    }
    async Task RunMacroShortcutAndExit(string macroReference,bool byId,IntPtr? preferredActiveWindow)
    {
        int exitCode=0;
        try
        {
            var macro=new ConfigService().Load().Macros.FirstOrDefault(x=>(byId?x.Id:x.Name).Equals(macroReference,StringComparison.OrdinalIgnoreCase))??throw new InvalidOperationException(byId?"ショートカットに対応するマクロが見つかりません。":$"マクロ「{macroReference}」が見つかりません。");
            var macroConfig=new ConfigService().Load();macro=macroConfig.Macros.First(x=>x.Id.Equals(macro.Id,StringComparison.OrdinalIgnoreCase));var result=await MacroPlayer.PlayAsync(macro,macroConfig,null,preferredActiveWindow);if(!result.Succeeded&&!result.Cancelled)throw new InvalidOperationException(result.Message);
        }
        catch(Exception ex){exitCode=1;AppDialog.Show(ex.Message,"マクロを実行できません",MessageBoxButton.OK,MessageBoxImage.Error);}
        finally{ExitImmediately(exitCode);}
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e){if(MainWindow is RELYR.MainWindow window)window.PrepareForSystemShutdown();base.OnSessionEnding(e);}
    void SystemPowerModeChanged(object sender,PowerModeChangedEventArgs e){if(e.Mode==PowerModes.Suspend)InputEngine.ReleaseAll();}
    void SystemSessionSwitch(object sender,SessionSwitchEventArgs e){if(e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)InputEngine.ReleaseAll();}
    protected override void OnExit(ExitEventArgs e){SystemEvents.PowerModeChanged-=SystemPowerModeChanged;SystemEvents.SessionSwitch-=SystemSessionSwitch;InputEngine.ReleaseAll();signalStop.Cancel();showSignal?.Set();shutdownSignal?.Set();foregroundWindowTracker?.Dispose();showSignal?.Dispose();showAcknowledgement?.Dispose();shutdownSignal?.Dispose();if(ownsMutex){try{instanceMutex?.ReleaseMutex();}catch{}}instanceMutex?.Dispose();signalStop.Dispose();base.OnExit(e);}
    [DllImport("kernel32.dll")]
    static extern bool SetProcessShutdownParameters(uint level,uint flags);
}
