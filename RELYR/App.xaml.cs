using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace RELYR;

public partial class App : System.Windows.Application
{
#if !PRODUCTION_PUBLISH
    internal static string EngineTestReportPath=>Path.Combine(Path.GetTempPath(),"RELYR-engine-test-last.log");
#endif
    const string MutexName=@"Local\RELYR.SingleInstance.v2";
    const string RecoveryMutexName=@"Local\RELYR.SingleInstanceRecovery.v1";
    const string SignalName=@"Local\RELYR.ShowExisting.v1";
    const string ShowAckName=@"Local\RELYR.ShowExistingAck.v1";
    Mutex? instanceMutex;
    Mutex? recoveryMutex;
    EventWaitHandle? showSignal;
    EventWaitHandle? showAckSignal;
    EventWaitHandle? shutdownSignal;
    bool ownsMutex;
    bool ownsRecoveryMutex;
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
#if PRODUCTION_PUBLISH
        if(args.Length>=2&&args[0].Equals("--elevated-task",StringComparison.OrdinalIgnoreCase))
        {
            if(!StartupService.IsProcessElevated()){System.Windows.MessageBox.Show("管理者モードの起動タスクが正しく構成されていません。RELYRを再インストールしてください。","起動できません",MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);return;}
            try{args=StartupService.DecodeElevatedArguments(args[1]);}
            catch(Exception ex){System.Windows.MessageBox.Show("起動情報を読み取れませんでした。\n\n"+ex.Message,"起動できません",MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);return;}
        }
        else if(!StartupService.IsProcessElevated())
        {
            if(!StartupService.TryRunElevated(args,out string error))System.Windows.MessageBox.Show(error,"起動できません",MessageBoxButton.OK,MessageBoxImage.Error);
            Shutdown(string.IsNullOrEmpty(error)?0:1);return;
        }
#endif
        if(ShortcutService.TryReadMacroId(args,out string macroId)){ShutdownMode=ShutdownMode.OnExplicitShutdown;Dispatcher.BeginInvoke(()=>_=RunMacroShortcutAndExit(macroId,true));return;}
        if(ShortcutService.TryReadMacroName(args,out string macroName)){ShutdownMode=ShutdownMode.OnExplicitShutdown;Dispatcher.BeginInvoke(()=>_=RunMacroShortcutAndExit(macroName,false));return;}
        if(args.Length>=1&&args[0].Equals("--configure-elevated-launcher",StringComparison.OrdinalIgnoreCase)){try{StartupService.EnsureElevatedLauncher();Shutdown(0);}catch{Shutdown(1);}return;}
        if(args.Length>=1&&args[0].Equals("--remove-elevated-tasks",StringComparison.OrdinalIgnoreCase)){try{StartupService.RemoveElevatedTasks();Shutdown(0);}catch{Shutdown(1);}return;}
        if(args.Length>=1&&args[0].Equals("--uninstall-needs-restart",StringComparison.OrdinalIgnoreCase)){try{var uninstallState=new ConfigService().Load();Shutdown(UninstallRestartNeeded(uninstallState,LegacyKeyRemapService.HasCapsLockToF13())?10:0);}catch{Shutdown(10);}return;}
        if(args.Length>=1&&args[0].Equals("--prepare-uninstall",StringComparison.OrdinalIgnoreCase)){try{LegacyKeyRemapService.SetCapsLockToF13(false);StartupService.RemoveElevatedTasks();var uninstallConfig=new ConfigService();var value=uninstallConfig.Load();value.StartWithWindows=false;value.CapsLockLayerEnabled=false;uninstallConfig.Save(value);Shutdown(0);}catch{Shutdown(1);}return;}
        if(args.Length>=1&&args[0].Equals("--delete-user-settings",StringComparison.OrdinalIgnoreCase)){Shutdown(ConfigService.DeleteAllUserData()?0:1);return;}
        if(args.Length>=2&&args[0].Equals("--configure-caps-remap",StringComparison.OrdinalIgnoreCase)){try{LegacyKeyRemapService.SetCapsLockToF13(args[1].Equals("on",StringComparison.OrdinalIgnoreCase));Shutdown(0);}catch{Shutdown(1);}return;}
        if(args.Contains("--shutdown-existing",StringComparer.OrdinalIgnoreCase)){try{EventWaitHandle.OpenExisting(BuildShutdownSignalName(Environment.ProcessPath)).Set();}catch{}Shutdown(0);return;}
        if(args.Length>=2&&args[0].Equals("--configure-startup",StringComparison.OrdinalIgnoreCase)){try{bool enabled=args[1].Equals("on",StringComparison.OrdinalIgnoreCase);StartupService.SetEnabled(enabled);var startupConfig=new ConfigService();var value=startupConfig.Load();value.StartWithWindows=enabled;startupConfig.Save(value);Shutdown(0);}catch{Shutdown(1);}return;}
#if !PRODUCTION_PUBLISH
        if(e.Args.Contains("--desktop-helper",StringComparer.OrdinalIgnoreCase))
        {
            var helper=new Window{Title="RELYR Desktop Test",Width=320,Height=160,WindowStartupLocation=WindowStartupLocation.CenterScreen};
            MainWindow=helper;helper.Show();return;
        }
        if(e.Args.Contains("--ui-test",StringComparer.OrdinalIgnoreCase)){ShutdownWithExitCode(UiIntegrationTest.Run(Console.Out));return;}
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
        if(e.Args.Contains("--desktop-test",StringComparer.OrdinalIgnoreCase)){ShutdownWithExitCode(VirtualDesktopIntegrationTest.Run(Console.Out));return;}
#endif
        var loadedStartupConfig=new ConfigService().Load();
        try{foreach(var macro in loadedStartupConfig.Macros)ShortcutService.UpgradeExistingMacroShortcut(macro);}catch{}
        try{StartupService.EnsureMatchesConfig(loadedStartupConfig.StartWithWindows);}catch{}
        instanceMutex=new Mutex(true,MutexName,out ownsMutex);
        showAckSignal=new EventWaitHandle(false,EventResetMode.AutoReset,ShowAckName);
        if(!ownsMutex)
        {
            if(NotifyExistingInstance(showAckSignal)){Shutdown(0);return;}
            recoveryMutex=new Mutex(true,RecoveryMutexName,out ownsRecoveryMutex);
            if(!ownsRecoveryMutex)
            {
                NotifyExistingInstance(showAckSignal);
                Shutdown(0);return;
            }
        }
        showSignal=new EventWaitHandle(false,EventResetMode.AutoReset,SignalName);
        shutdownSignal=new EventWaitHandle(false,EventResetMode.ManualReset,BuildShutdownSignalName(Environment.ProcessPath));
        var window=new MainWindow(args.Contains("--skip-setup",StringComparer.OrdinalIgnoreCase));
        MainWindow=window;
        bool needsSetup=window.NeedsFirstRunSetup;
        if(!args.Contains("--tray",StringComparer.OrdinalIgnoreCase)||needsSetup)window.Show();
        if(needsSetup)Dispatcher.BeginInvoke(window.ShowFirstRunSetup);
        _=Task.Run(()=>ListenForShow(window));
        _=Task.Run(()=>ListenForShutdown(window));
    }
    static bool NotifyExistingInstance(EventWaitHandle acknowledgement)
    {
        long deadline=Environment.TickCount64+2000;
        while(Environment.TickCount64<deadline)
        {
            acknowledgement.Reset();
            try{using var signal=EventWaitHandle.OpenExisting(SignalName);signal.Set();if(acknowledgement.WaitOne(300))return true;}catch(WaitHandleCannotBeOpenedException){}
            Thread.Sleep(50);
        }
        return false;
    }
    void ShutdownWithExitCode(int exitCode){Environment.ExitCode=exitCode;Shutdown(exitCode);}
    void ListenForShow(MainWindow window)
    {
        try
        {
            while(WaitHandle.WaitAny([showSignal!,signalStop.Token.WaitHandle])==0)
            {
                showAckSignal?.Set();
                Dispatcher.BeginInvoke(window.ShowFromExternalLaunch);
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
    internal static bool UninstallRestartNeeded(AppConfig config,bool registryRemap,bool? pendingRestart=null)=>registryRemap||config.CapsLockLayerEnabled||(pendingRestart??LegacyKeyRemapService.IsRestartStillPending(config));
#if !PRODUCTION_PUBLISH
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
            Environment.Exit(result);
        }
    }
#endif
    async Task RunMacroShortcutAndExit(string macroReference,bool byId)
    {
        int exitCode=0;
        try
        {
            var macro=new ConfigService().Load().Macros.FirstOrDefault(x=>(byId?x.Id:x.Name).Equals(macroReference,StringComparison.OrdinalIgnoreCase))??throw new InvalidOperationException(byId?"ショートカットに対応するマクロが見つかりません。":$"マクロ「{macroReference}」が見つかりません。");
            var macroConfig=new ConfigService().Load();macro=macroConfig.Macros.First(x=>x.Id.Equals(macro.Id,StringComparison.OrdinalIgnoreCase));await MacroPlayer.PlayAsync(macro,macroConfig);
        }
        catch(Exception ex){exitCode=1;System.Windows.MessageBox.Show(ex.Message,"マクロを実行できません",MessageBoxButton.OK,MessageBoxImage.Error);}
        finally{InputEngine.ReleaseAll();Shutdown(exitCode);}
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e){if(MainWindow is RELYR.MainWindow window)window.PrepareForSystemShutdown();base.OnSessionEnding(e);}
    void SystemPowerModeChanged(object sender,PowerModeChangedEventArgs e){if(e.Mode==PowerModes.Suspend)InputEngine.ReleaseAll();}
    void SystemSessionSwitch(object sender,SessionSwitchEventArgs e){if(e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)InputEngine.ReleaseAll();}
    protected override void OnExit(ExitEventArgs e){SystemEvents.PowerModeChanged-=SystemPowerModeChanged;SystemEvents.SessionSwitch-=SystemSessionSwitch;InputEngine.ReleaseAll();signalStop.Cancel();showSignal?.Set();shutdownSignal?.Set();showSignal?.Dispose();showAckSignal?.Dispose();shutdownSignal?.Dispose();if(ownsMutex){try{instanceMutex?.ReleaseMutex();}catch{}}if(ownsRecoveryMutex){try{recoveryMutex?.ReleaseMutex();}catch{}}instanceMutex?.Dispose();recoveryMutex?.Dispose();signalStop.Dispose();base.OnExit(e);}
    [DllImport("kernel32.dll")]
    static extern bool SetProcessShutdownParameters(uint level,uint flags);
}
