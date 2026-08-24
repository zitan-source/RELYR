using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;

namespace RELYR;

public partial class App : System.Windows.Application
{
    internal const string RestartLauncherArgument = "--restart-launcher";
#if !PRODUCTION_PUBLISH
    internal static string EngineTestReportPath => VerificationPaths.GetFile("engine-test-last.log");
    internal static string UiTestReportPath => VerificationPaths.GetFile("ui-test-last.log");
    internal static string MouseUiTestReportPath => VerificationPaths.GetFile("mouse-ui-test-last.log");
    internal static string SelfTestReportPath => VerificationPaths.GetFile("self-test-last.log");
    internal static string ConfigurationMatrixTestReportPath => VerificationPaths.GetFile("configuration-matrix-last.log");
    internal static string StartupTestReportPath => VerificationPaths.GetFile("startup-test-last.log");
    internal static string ShutdownTestReportPath => VerificationPaths.GetFile("shutdown-test-last.log");
#endif
    internal const string InstanceMutexName = @"Local\RELYR.SingleInstance.v2";
    internal const string HelperMutexName = @"Local\RELYR.ElevatedHelper.v1";
    const string SignalName = @"Local\RELYR.ShowExisting.v1";
    const string AcknowledgementName = @"Local\RELYR.ShowExistingAck.v1";
    const int ExistingInstanceResponseTimeoutMs = 3000;
    Mutex? instanceMutex;
    EventWaitHandle? showSignal;
    EventWaitHandle? showAcknowledgement;
    EventWaitHandle? shutdownSignal;
    EventWaitHandle? shutdownInProgressSignal;
    ForegroundWindowTracker? foregroundWindowTracker;
    bool ownsMutex;
    int shutdownMarked;
    readonly CancellationTokenSource signalStop = new();
    public App()
    {
        // 入力フックを他のマウス常駐ソフトより先に解除できるよう、
        // Windows終了通知をアプリ用の最優先範囲で受け取る。
        SetProcessShutdownParameters(0x3FF, 0);
        DispatcherUnhandledException += (_, e) =>
        {
            if (UiMotionService.TryHandleDispatcherException(e.Exception))
            {
                e.Handled = true;
                return;
            }
            LifecycleDiagnostics.Write("dispatcher-unhandled-exception", e.Exception.ToString());
            MarkShutdownInProgress("dispatcher-unhandled-exception");
            InputEngine.ReleaseAllDefensively();
            e.Handled = true;
            ShutdownWithExitCode(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            LifecycleDiagnostics.Write("appdomain-unhandled-exception", e.ExceptionObject?.ToString());
            MarkShutdownInProgress("appdomain-unhandled-exception");
            InputEngine.ReleaseAllDefensively();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => InputEngine.ReleaseAllDefensively();
        TaskScheduler.UnobservedTaskException += (_, e) => { InputEngine.ReleaseAllDefensively(); e.SetObserved(); };
        SystemEvents.PowerModeChanged += SystemPowerModeChanged;
        SystemEvents.SessionSwitch += SystemSessionSwitch;
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string[] args = WaitForRestartParent(e.Args);
#if !PRODUCTION_PUBLISH
        if (args.Contains("--drop-diagnostics", StringComparer.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable("RELYR_DROP_DIAGNOSTICS", "1");
        if (e.Args.Contains("--normal-debug", StringComparer.OrdinalIgnoreCase))
        {
            var normalConfig = new ConfigService().Load();
            var normalWindow = new MainWindow(e.Args.Contains("--skip-setup", StringComparer.OrdinalIgnoreCase), startupConfig: normalConfig);
            MainWindow = normalWindow;
            normalWindow.Show();
            return;
        }
        if (args.Contains("--shutdown-test-host", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            shutdownSignal = new EventWaitHandle(false, EventResetMode.ManualReset, BuildShutdownSignalName(Environment.ProcessPath));
            _ = Task.Run(ListenForShutdown);
            string token = args.SkipWhile(x => !x.Equals("--shutdown-test-host", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() ?? "unknown";
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    using var ready = EventWaitHandle.OpenExisting(ShutdownIntegrationTest.ReadySignalName(token));
                    ready.Set();
                }
                catch { }
                Thread.Sleep(TimeSpan.FromSeconds(30));
            });
            return;
        }
        if (args.Contains("--profile-switch-test-host", StringComparer.OrdinalIgnoreCase))
        {
            string title = args.SkipWhile(x => !x.Equals("--profile-switch-test-host", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault()
                ?? ProfileSwitchRuntimeTest.HostWindowTitle;
            var host = new Window { Title = title, Left = 160, Top = 160, Width = 420, Height = 260, WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = true };
            MainWindow = host;
            host.Show();
            return;
        }
        if (args.Contains("--profile-switch-runtime-test", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(() => _ = RunProfileSwitchRuntimeTestAndExit());
            return;
        }
#endif
        // Installer and update shutdown is a path-scoped local signal. Handle it
        // before the production privilege relay so a one-shot shutdown command
        // cannot wait on Task Scheduler or launch another RELYR executable.
        if (args.Contains("--shutdown-existing", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(BuildShutdownSignalName(Environment.ProcessPath));
                signal.Set();
            }
            catch { }
            ExitImmediately(0);
            return;
        }
#if PRODUCTION_PUBLISH
        if(args.Length>=2&&args[0].Equals("--elevated-task",StringComparison.OrdinalIgnoreCase))
        {
            if(!StartupService.IsProcessElevated()){AppDialog.Show("管理者モードの起動タスクが正しく構成されていません。RELYRを再インストールしてください。","起動できません",MessageBoxButton.OK,MessageBoxImage.Error);ExitImmediately(1);return;}
            try{args=StartupService.DecodeElevatedArguments(args[1]);}
            catch(Exception ex){AppDialog.Show("起動情報を読み取れませんでした。\n\n"+ex.Message,"起動できません",MessageBoxButton.OK,MessageBoxImage.Error);ExitImmediately(1);return;}
        }
        if(args.Length==3&&args[0].Equals("--sensor-helper",StringComparison.OrdinalIgnoreCase))
        {
            if(!StartupService.IsProcessElevated()||!HardwareSensorProcess.ValidPipeName(args[1])||!HardwareSensorProcess.ValidPipeName(args[2])){ExitImmediately(1);return;}
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(async()=>
            {
                try{await HardwareSensorProcess.RunAsync(args[1],args[2]);}
                catch(Exception ex){LifecycleDiagnostics.Write("hardware-sensor-process-failed",ex.ToString());}
                finally{Shutdown(0);}
            });
            return;
        }
        if (IsRestartLauncher(args))
            args = RestartTargetArguments(args);
        if(args.Contains("--elevated-helper",StringComparer.OrdinalIgnoreCase))
        {
            if(!StartupService.IsProcessElevated()){AppDialog.Show("管理者ヘルパーを管理者権限で起動できません。","起動できません",MessageBoxButton.OK,MessageBoxImage.Error);ExitImmediately(1);return;}
            StartElevatedHelper(args);return;
        }
        else if (ShouldStartMediumUiHost(StartupService.IsProcessElevated(), args))
        {
            args = EnsureUiHostArgument(args);
        }
        else if(ShouldRelayToSingleElevatedHost(StartupService.IsProcessElevated()))
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
            if(StartupService.HasLegacyElevatedStartupTask())
            {
                StartupService.TryRunElevated(["--migrate-startup"],out _);
                for(int attempt=0;attempt<30&&MainInstanceExists();attempt++)Thread.Sleep(100);
            }
            if(IsMainUiLaunch(args)&&MainInstanceExists())
            {
                bool shutdownPending = IsResidentShutdownPending(Environment.ProcessPath);
                bool notified = false;
                if (!shutdownPending)
                {
                    notified = NotifyExistingInstance();
                    shutdownPending = IsResidentShutdownPending(Environment.ProcessPath);
                }
                if (shutdownPending || !MainInstanceExists())
                {
                    LifecycleDiagnostics.Write("launcher-waiting-for-resident-exit", shutdownPending ? "shutdown-pending" : "instance-disappeared-during-notification");
                    if (!WaitForMainInstanceExit(TimeSpan.FromSeconds(5)))
                    {
                        if (!RequestStaleInstanceRecovery(args, out string recoveryError))
                            AppDialog.Show(recoveryError,"RELYRを再起動できません",MessageBoxButton.OK,MessageBoxImage.Error);
                        ExitImmediately(0);return;
                    }
                    LifecycleDiagnostics.Write("launcher-replacing-exited-resident");
                }
                else if(notified)
                {
                    if(ShouldExplainDuplicate(args))ShowAlreadyRunningMessage();
                    ExitImmediately(0);return;
                }
                else if(!RequestStaleInstanceRecovery(args,out string recoveryError))
                {
                    AppDialog.Show(recoveryError,"RELYRを再起動できません",MessageBoxButton.OK,MessageBoxImage.Error);
                    ExitImmediately(0);return;
                }
                else
                {
                    ExitImmediately(0);return;
                }
            }
            if(!StartupService.TryRunElevated(args,out string elevatedLaunchError))
                AppDialog.Show(elevatedLaunchError,"RELYRを起動できません",MessageBoxButton.OK,MessageBoxImage.Error);
            ExitImmediately(0);return;
        }
#endif
#if PRODUCTION_PUBLISH
        if(args.Length>=1&&args[0].Equals("--migrate-startup",StringComparison.OrdinalIgnoreCase))
        {
            try{StartupService.MigrateLegacyStartup();ExitImmediately(0);}catch{ExitImmediately(1);}return;
        }
        if(args.Length>=2&&args[0].Equals("--recover-stale-instance",StringComparison.OrdinalIgnoreCase))
        {
            RunStaleInstanceRecovery(args[1]);
            return;
        }
#endif
#if PRODUCTION_PUBLISH
        if (IsRestartLauncher(args))
        {
            string[] restartArguments = RestartTargetArguments(args);
            if (!StartupService.TryRunElevated(restartArguments, out string restartError))
            {
                AppDialog.Show(restartError, "RELYRを再起動できません", MessageBoxButton.OK, MessageBoxImage.Error);
                ExitImmediately(1);
                return;
            }
            ExitImmediately(0);
            return;
        }
#endif
        IntPtr? shortcutTarget = ReadMacroShortcutTarget(args);
        if (ShortcutService.TryReadMacroId(args, out string macroId))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(() => _ = RunMacroShortcutAndExit(macroId, true, shortcutTarget));
            return;
        }
        if (ShortcutService.TryReadMacroName(args, out string macroName))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(() => _ = RunMacroShortcutAndExit(macroName, false, shortcutTarget));
            return;
        }
        if (args.Length >= 1 && args[0].Equals("--configure-elevated-launcher", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                StartupService.EnsureElevatedLauncher();
                ExitImmediately(0);
            }
            catch { ExitImmediately(1); }
            return;
        }
        if (args.Length >= 1 && args[0].Equals("--remove-elevated-tasks", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                StartupService.RemoveElevatedTasks();
                ExitImmediately(0);
            }
            catch { ExitImmediately(1); }
            return;
        }
        if (args.Length >= 1 && args[0].Equals("--uninstall-needs-restart", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uninstallState = new ConfigService().Load();
                ExitImmediately(UninstallRestartNeeded(uninstallState, LegacyKeyRemapService.HasCapsLockToF13()) ? 10 : 0);
            }
            catch { ExitImmediately(10); }
            return;
        }
        if (args.Length >= 1 && args[0].Equals("--prepare-uninstall", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                LegacyKeyRemapService.SetCapsLockToF13(false);
                StartupService.RemoveElevatedTasks();
                var uninstallConfig = new ConfigService();
                var value = uninstallConfig.Load();
                value.StartWithWindows = false;
                value.CapsLockLayerEnabled = false;
                uninstallConfig.Save(value);
                ExitImmediately(0);
            }
            catch { ExitImmediately(1); }
            return;
        }
        if (args.Length >= 1 && args[0].Equals("--delete-user-settings", StringComparison.OrdinalIgnoreCase))
        {
            ExitImmediately(ConfigService.DeleteAllUserData() ? 0 : 1);
            return;
        }
        if (args.Length >= 2 && args[0].Equals("--configure-caps-remap", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                LegacyKeyRemapService.SetCapsLockToF13(args[1].Equals("on", StringComparison.OrdinalIgnoreCase));
                ExitImmediately(0);
            }
            catch { ExitImmediately(1); }
            return;
        }
        if (args.Length >= 2 && args[0].Equals("--configure-startup", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                bool enabled = args[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                StartupService.SetUserStartupEnabled(enabled);
                var startupConfig = new ConfigService();
                var value = startupConfig.Load();
                value.StartWithWindows = enabled;
                startupConfig.Save(value);
                ExitImmediately(0);
            }
            catch { ExitImmediately(1); }
            return;
        }
#if !PRODUCTION_PUBLISH
        if (args.Contains("--elevated-helper", StringComparer.OrdinalIgnoreCase))
        {
            if (!StartupService.IsProcessElevated())
            {
                ExitImmediately(1);
                return;
            }
            StartElevatedHelper(args);
            return;
        }
        if (e.Args.Contains("--desktop-helper", StringComparer.OrdinalIgnoreCase))
        {
            var helper = new Window { Title = "RELYR Desktop Test", Width = 320, Height = 160, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            MainWindow = helper;
            helper.Show();
            return;
        }
        if (e.Args.Contains("--mouse-ui-test", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                File.Delete(MouseUiTestReportPath);
            }
            catch { }
            using var mouseUiLog = new StreamWriter(MouseUiTestReportPath, false, Encoding.UTF8) { AutoFlush = true };
            int result = UiIntegrationTest.RunMouseLayout(mouseUiLog);
            ShutdownWithExitCode(result);
            return;
        }
        if (e.Args.Contains("--ui-test", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                File.Delete(UiTestReportPath);
            }
            catch { }
            using var uiLog = new StreamWriter(UiTestReportPath, false, Encoding.UTF8) { AutoFlush = true };
            int result = UiIntegrationTest.Run(uiLog);
            ShutdownWithExitCode(result);
            return;
        }
        if (e.Args.Contains("--shell-drop-test", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownWithExitCode(ShellDropIntegrationTest.Run());
            return;
        }
        if (e.Args.Contains("--admin-deck-test", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(() => _ = RunAdminDeckTestAndExit());
            return;
        }
        if (e.Args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                File.Delete(SelfTestReportPath);
            }
            catch { }

            int result;
            using (var selfLog = new StreamWriter(SelfTestReportPath, false, Encoding.UTF8) { AutoFlush = true })
            {
                result = SelfTest.Run(selfLog);
            }

            try
            {
                Console.Out.Write(File.ReadAllText(SelfTestReportPath));
            }
            catch { }

            ShutdownWithExitCode(result);
            return;
        }
        if (e.Args.Contains("--configuration-matrix-test", StringComparer.OrdinalIgnoreCase))
        {
            try { File.Delete(ConfigurationMatrixTestReportPath); } catch { }
            int result;
            using (var matrixLog = new StreamWriter(ConfigurationMatrixTestReportPath, false, Encoding.UTF8) { AutoFlush = true })
                result = ConfigurationMatrixTest.Run(matrixLog);
            try { Console.Out.Write(File.ReadAllText(ConfigurationMatrixTestReportPath)); } catch { }
            ShutdownWithExitCode(result);
            return;
        }
        if (e.Args.Contains("--engine-test", StringComparer.OrdinalIgnoreCase) || e.Args.Contains("--engine-test-no-real", StringComparer.OrdinalIgnoreCase))
        {
            bool includeRealHook = !e.Args.Contains("--engine-test-no-real", StringComparer.OrdinalIgnoreCase);
            try
            {
                File.Delete(EngineTestReportPath);
            }
            catch { }
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(() => _ = RunEngineTestAndExit(includeRealHook));
            return;
        }
        if (e.Args.Contains("--startup-test", StringComparer.OrdinalIgnoreCase))
        {
            try { File.Delete(StartupTestReportPath); } catch { }
            int result;
            using (var startupLog = new StreamWriter(StartupTestReportPath, false, Encoding.UTF8) { AutoFlush = true })
                result = StartupIntegrationTest.Run(startupLog);
            try { Console.Out.Write(File.ReadAllText(StartupTestReportPath)); } catch { }
            ShutdownWithExitCode(result);
            return;
        }
        if (e.Args.Contains("--shutdown-test", StringComparer.OrdinalIgnoreCase))
        {
            try { File.Delete(ShutdownTestReportPath); } catch { }
            int result;
            using (var shutdownLog = new StreamWriter(ShutdownTestReportPath, false, Encoding.UTF8) { AutoFlush = true })
                result = ShutdownIntegrationTest.Run(shutdownLog);
            try { Console.Out.Write(File.ReadAllText(ShutdownTestReportPath)); } catch { }
            ShutdownWithExitCode(result);
            return;
        }
        if (e.Args.Contains("--update-test", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(() => _ = RunUpdateTestAndExit());
            return;
        }
        if (e.Args.Contains("--desktop-test", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownWithExitCode(VirtualDesktopIntegrationTest.Run(Console.Out));
            return;
        }
#endif
        bool uiHost = args.Contains("--ui-host", StringComparer.OrdinalIgnoreCase);
        IpcRuntime.IsUiHost = uiHost;
        string instanceMutexName = InstanceMutexName;
#if !PRODUCTION_PUBLISH
        if (args.Contains("--tray-exit-regression-host", StringComparer.OrdinalIgnoreCase))
            instanceMutexName += ".ShutdownTest." + Environment.ProcessId;
#endif
        instanceMutex = new Mutex(true, instanceMutexName, out ownsMutex);
        if (!ownsMutex)
        {
            if (NotifyExistingInstance() && ShouldExplainDuplicate(args))
                ShowAlreadyRunningMessage();
            ExitImmediately(0);
            return;
        }
        try
        {
            foregroundWindowTracker = new ForegroundWindowTracker();
        }
        catch { }
        var loadedStartupConfig = new ConfigService().Load();
        ThemeService.Apply(loadedStartupConfig.ThemeMode);
        UiMotionService.Apply(loadedStartupConfig.UiAnimationsEnabled);
        if (ShouldScanForOrphans(args)
           && !StartupService.TryTerminateOrphanedRelyrInstances(TimeSpan.FromSeconds(3), out string orphanError))
        {
            AppDialog.Show(
                "以前のRELYRの終了処理が残っているため、安全のため入力機能を開始しません。\n\n" + orphanError,
                "RELYRを起動できません",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ExitImmediately(1);
            return;
        }
        showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
        showAcknowledgement = new EventWaitHandle(false, EventResetMode.AutoReset, AcknowledgementName);
        shutdownSignal = new EventWaitHandle(false, EventResetMode.ManualReset, BuildShutdownSignalName(Environment.ProcessPath));
        shutdownInProgressSignal = new EventWaitHandle(false, EventResetMode.ManualReset, BuildShutdownInProgressSignalName(Environment.ProcessPath));
        LifecycleDiagnostics.Write("resident-started", $"version={typeof(App).Assembly.GetName().Version} elevated={StartupService.IsProcessElevated()}");
        bool startInputHooks = true;
#if !PRODUCTION_PUBLISH
        startInputHooks = !args.Contains("--no-input-hooks", StringComparer.OrdinalIgnoreCase);
#endif
        bool deferEditorUi = ShouldDeferEditorUi(args, loadedStartupConfig);
        var window = new MainWindow(args.Contains("--skip-setup", StringComparer.OrdinalIgnoreCase), startupConfig: loadedStartupConfig, runtimeRole: uiHost ? RuntimeRole.UiHost : RuntimeRole.Standard, startInputHooks: startInputHooks, deferEditorUiUntilShown: deferEditorUi);
        MainWindow = window;
        StartStartupMaintenance(loadedStartupConfig);
        bool needsSetup = window.NeedsFirstRunSetup;
        if (!args.Contains("--tray", StringComparer.OrdinalIgnoreCase) || needsSetup)
            window.Show();
        if (needsSetup)
            Dispatcher.BeginInvoke(window.ShowFirstRunSetup);
        _ = Task.Run(() => ListenForShow(window));
        _ = Task.Run(ListenForShutdown);
        if (uiHost)
            _ = IpcRuntime.StartUiHostAsync(window);
#if !PRODUCTION_PUBLISH
        if (args.Contains("--tray-exit-regression-host", StringComparer.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(window.ExecuteTrayExitMenuItemForTest));
#endif
    }

    void AppButtonMotionEntered(object sender, System.Windows.Input.MouseEventArgs e)
        => UiMotionService.RunSafely("button-hover-enter", () => AnimateAppButtonSignal(sender as WpfButton, 0.32, 140));

    void AppButtonMotionExited(object sender, System.Windows.Input.MouseEventArgs e)
        => UiMotionService.RunSafely("button-hover-exit", () => AnimateAppButtonSignal(sender as WpfButton, 0, 190));

    static void AnimateAppButtonSignal(WpfButton? button, double opacity, int durationMs)
    {
        if (button == null)
            return;
        button.ApplyTemplate();
        if (button.Template.FindName("ButtonSignal", button) is not UIElement signal)
            return;
        signal.BeginAnimation(UIElement.OpacityProperty, null);
        if (!UiMotionService.Enabled)
        {
            signal.Opacity = 0;
            return;
        }
        signal.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    void AppSwitchMotionEntered(object sender, System.Windows.Input.MouseEventArgs e)
        => UiMotionService.RunSafely("switch-hover-enter", () => AnimateAppSwitchThumb(sender as WpfCheckBox, 1.08, 140));

    void AppSwitchMotionExited(object sender, System.Windows.Input.MouseEventArgs e)
        => UiMotionService.RunSafely("switch-hover-exit", () => AnimateAppSwitchThumb(sender as WpfCheckBox, 1, 180));

    static void AnimateAppSwitchThumb(WpfCheckBox? checkBox, double target, int durationMs)
    {
        if (checkBox == null)
            return;
        checkBox.ApplyTemplate();
        if (checkBox.Template.FindName("SwitchThumb", checkBox) is not FrameworkElement thumb)
            return;
        var scale = UiMotionService.MutableScale(thumb);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (!UiMotionService.Enabled)
        {
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(durationMs);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(target, duration) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(target, duration) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
    }
    internal static bool ShouldStartMediumUiHost(bool processElevated, IReadOnlyList<string> args)
        => !processElevated && IsMainUiLaunch(args);
    internal static bool ShouldDeferEditorUi(IReadOnlyList<string> args, AppConfig config)
        => args.Contains("--tray", StringComparer.OrdinalIgnoreCase) && config.FirstRunCompleted;
    internal static string[] EnsureUiHostArgument(IEnumerable<string> args)
        => args.Contains("--ui-host", StringComparer.OrdinalIgnoreCase) ? args.ToArray() : [.. args, "--ui-host"];
    // Privileged one-shot commands still use the registered elevated launcher.
    // The visible main UI stays at the same integrity level as ordinary apps.
    internal static bool ShouldRelayToSingleElevatedHost(bool processElevated) => !processElevated;
    internal static string[] RestartChildArguments(int parentProcessId)
        => ["--restart-after-pid", parentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), RestartLauncherArgument];
    internal static bool IsRestartLauncher(IReadOnlyList<string> args)
        => args.Contains(RestartLauncherArgument, StringComparer.OrdinalIgnoreCase);
    internal static string[] RestartTargetArguments(IEnumerable<string> args)
        => args.Where(value => !value.Equals(RestartLauncherArgument, StringComparison.OrdinalIgnoreCase)).ToArray();
    static string[] WaitForRestartParent(string[] args)
    {
        int marker = Array.FindIndex(args, value => value.Equals("--restart-after-pid", StringComparison.OrdinalIgnoreCase));
        if (marker < 0)
            return args;
        if (marker + 1 < args.Length && int.TryParse(args[marker + 1], out int parentId) && parentId > 0 && parentId != Environment.ProcessId)
        {
            try
            {
                using var parent = Process.GetProcessById(parentId);
                parent.WaitForExit(10000);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }
        return args.Where((_, index) => index != marker && index != marker + 1).ToArray();
    }

    void StartElevatedHelper(IReadOnlyList<string> args)
    {
        if (args.Count < 3)
        {
            ExitImmediately(1);
            return;
        }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var helperMutex = new Mutex(true, HelperMutexName, out bool ownsHelperMutex);
        if (!ownsHelperMutex)
        {
            helperMutex.Dispose();
            ExitImmediately(0);
            return;
        }
        string pipeName = args[1], bootstrapName = args[2];
        var config = new ConfigService().Load();
        ThemeService.Apply(config.ThemeMode);
        UiMotionService.Apply(config.UiAnimationsEnabled);
        var window = new MainWindow(true, true, config, RuntimeRole.ElevatedHelper);
        MainWindow = window;
        window.Hide();
        IpcRuntime.IsElevatedHelper = true;
        _ = Task.Run(async () =>
        {
            try
            {
                await IpcRuntime.RunElevatedHelperAsync(pipeName, bootstrapName, window).ConfigureAwait(false);
            }
            catch { }
            finally { try { helperMutex.ReleaseMutex(); } catch { } helperMutex.Dispose(); ExitImmediately(0); }
        });
    }

    static void StartStartupMaintenance(AppConfig config)
    {
        var macros = config.Macros.Select(x => new MacroDefinition { Id = x.Id, Name = x.Name }).ToArray();
        bool startWithWindows = config.StartWithWindows;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var macro in macros)
                    ShortcutService.UpgradeExistingMacroShortcut(macro);
            }
            catch { }
            try
            {
                StartupService.EnsureMatchesConfig(startWithWindows);
            }
            catch { }
        })
        {
            IsBackground = true,
            Name = "RELYR startup maintenance"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    internal static bool IsMainUiLaunch(IReadOnlyList<string> args) => args.All(x => x.Equals("--tray", StringComparison.OrdinalIgnoreCase) || x.Equals("--skip-setup", StringComparison.OrdinalIgnoreCase) || x.Equals("--ui-host", StringComparison.OrdinalIgnoreCase));
    internal static bool ShouldScanForOrphans(IReadOnlyList<string> args) => !args.Contains("--tray", StringComparer.OrdinalIgnoreCase) && !args.Contains("--ui-host", StringComparer.OrdinalIgnoreCase) && !args.Contains("--elevated-helper", StringComparer.OrdinalIgnoreCase);
    static bool ShouldExplainDuplicate(IReadOnlyList<string> args) => !args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
    static bool MainInstanceExists()
    {
        try
        {
            if (!Mutex.TryOpenExisting(InstanceMutexName, out var existing))
                return false;
            existing.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException) { return true; }
    }
    internal static bool WaitForInstanceExit(Func<bool> instanceExists, int maximumAttempts, Action<int> delay)
    {
        for (int attempt = 0; attempt < maximumAttempts; attempt++)
        {
            if (!instanceExists())
                return true;
            delay(100);
        }
        return !instanceExists();
    }
    static bool WaitForMainInstanceExit(TimeSpan timeout)
    {
        int attempts = Math.Max(1, (int)Math.Ceiling(timeout.TotalMilliseconds / 100));
        return WaitForInstanceExit(MainInstanceExists, attempts, Thread.Sleep);
    }
    internal static bool IsResidentShutdownPending(string? executablePath)
        => IsNamedEventSet(BuildShutdownSignalName(executablePath))
            || IsNamedEventSet(BuildShutdownInProgressSignalName(executablePath));
    static bool IsNamedEventSet(string name)
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(name);
            return signal.WaitOne(0);
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return true; }
    }
    internal static bool NotifyExistingInstance(int timeoutMilliseconds = ExistingInstanceResponseTimeoutMs)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(SignalName);
                using var acknowledgement = EventWaitHandle.OpenExisting(AcknowledgementName);
                return WaitForExistingInstanceResponse(signal, acknowledgement, timeoutMilliseconds);
            }
            catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(50); }
            catch (UnauthorizedAccessException) { return false; }
        }
        return false;
    }
    internal static bool WaitForExistingInstanceResponse(EventWaitHandle signal, EventWaitHandle acknowledgement, int timeoutMilliseconds)
    {
        acknowledgement.Reset();
        signal.Set();
        return acknowledgement.WaitOne(timeoutMilliseconds);
    }
    static bool RequestStaleInstanceRecovery(IReadOnlyList<string> originalArguments, out string error)
    {
        string encoded = StartupService.EncodeArguments(originalArguments);
        return StartupService.TryRunElevated(["--recover-stale-instance", encoded], out error);
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
    static void ShowAlreadyRunningMessage() => AppDialog.Show("RELYRはすでに起動しています。\n通知領域のRELYRアイコンから開くこともできます。", "RELYRは起動中です", MessageBoxButton.OK, MessageBoxImage.Information);
    internal static void ArmForcedProcessExit(TimeSpan delay)
    {
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(delay);
            try
            {
                TerminateProcess(GetCurrentProcess(), unchecked((uint)Environment.ExitCode));
            }
            catch { }
        })
        {
            IsBackground = true,
            Name = "RELYR exit watchdog"
        };
        watchdog.Start();
    }
    internal static void ExitImmediately(int exitCode)
    {
        // Release injected input first, then let WPF close normally. A native
        // process kill is kept only as a silent watchdog so shutdown can never
        // surface the CLR "unknown software exception" dialog.
        ArmForcedProcessExit(TimeSpan.FromSeconds(3));
        try
        {
            if (Current?.MainWindow is RELYR.MainWindow window)
                window.PrepareVisualsForImmediateExit();
        }
        catch { }
        try
        {
            InputEngine.ReleaseAllDefensively();
        }
        catch { }
        Environment.ExitCode = exitCode;
        try
        {
            if (Current == null)
            {
                Process.GetCurrentProcess().Kill(false);
                return;
            }
            if (Current.Dispatcher.CheckAccess())
            {
                Current.Shutdown(exitCode);
                ForceTerminateProcess(exitCode);
            }
            else
                Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                Current.Shutdown(exitCode);
                ForceTerminateProcess(exitCode);
            }));
        }
        catch
        {
            try
            {
                Process.GetCurrentProcess().Kill(false);
            }
            catch { }
        }
    }
    static void ForceTerminateProcess(int exitCode)
    {
        try
        {
            if (TerminateProcess(GetCurrentProcess(), unchecked((uint)exitCode)))
                return;
        }
        catch { }
        try
        {
            Process.GetCurrentProcess().Kill(false);
        }
        catch { }
    }
    void ShutdownWithExitCode(int exitCode) => ExitImmediately(exitCode);
    void ListenForShow(MainWindow window)
    {
        try
        {
            while (WaitHandle.WaitAny([showSignal!, signalStop.Token.WaitHandle]) == 0)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        window.ShowFromExternalLaunch();
                    }
                    finally { showAcknowledgement?.Set(); }
                });
            }
        }
        catch (ObjectDisposedException) { }
    }
    void ListenForShutdown()
    {
        // Installer/update shutdown uses the same close path as the tray. The
        // watchdog remains independent so a hung dispatcher cannot leave a process behind.
        try
        {
            if (WaitHandle.WaitAny([shutdownSignal!, signalStop.Token.WaitHandle]) != 0)
                return;
            MarkShutdownInProgress("external-shutdown-signal");
            ArmForcedProcessExit(TimeSpan.FromSeconds(3));
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (MainWindow is RELYR.MainWindow window)
                    window.RequestApplicationExit("external-shutdown-signal");
                else
                    ExitImmediately(0);
            }));
        }
        catch (ObjectDisposedException) { }
        catch { ExitImmediately(0); }
    }
    internal static string BuildShutdownSignalName(string? executablePath)
    {
        string normalized = string.IsNullOrWhiteSpace(executablePath) ? "unknown" : Path.GetFullPath(executablePath).ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..20];
        return @"Local\RELYR.ShutdownExisting.v2." + hash;
    }
    internal static string BuildShutdownInProgressSignalName(string? executablePath)
    {
        string normalized = string.IsNullOrWhiteSpace(executablePath) ? "unknown" : Path.GetFullPath(executablePath).ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..20];
        return @"Local\RELYR.ShutdownInProgress.v1." + hash;
    }
    internal static void MarkShutdownInProgress(string reason)
    {
        if (Current is not App app || !app.ownsMutex || Interlocked.Exchange(ref app.shutdownMarked, 1) != 0)
            return;
        try { app.shutdownInProgressSignal?.Set(); } catch { }
        LifecycleDiagnostics.Write("resident-exit-requested", reason);
    }
    // A saved UI preference alone does not change Windows and must never cause
    // a reboot prompt.  A restart is needed only when the registry remap is
    // currently present, or a CapsLock registry change is awaiting a reboot.
    internal static bool UninstallRestartNeeded(AppConfig config, bool registryRemap, bool? pendingRestart = null) => registryRemap || (pendingRestart ?? LegacyKeyRemapService.IsRestartStillPending(config));
#if !PRODUCTION_PUBLISH
    async Task RunUpdateTestAndExit()
    {
        int result = await UpdateIntegrationTest.RunAsync(Console.Out);
        ShutdownWithExitCode(result);
    }
    async Task RunAdminDeckTestAndExit()
    {
        int result = await AdminDeckIntegrationTest.RunAsync();
        ShutdownWithExitCode(result);
    }
    async Task RunEngineTestAndExit(bool includeRealHook)
    {
        int result = 1;
        using var stream = new FileStream(EngineTestReportPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var log = TextWriter.Synchronized(new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true });
        try
        {
            result = await EngineIntegrationTest.RunAsync(log, includeRealHook).WaitAsync(TimeSpan.FromSeconds(45));
        }
        catch (TimeoutException) { log.WriteLine("FAIL engine test exceeded the 45 second safety limit"); }
        catch (Exception ex) { log.WriteLine(ex); }
        finally
        {
            try
            {
                InputEngine.ReleaseAll();
            }
            catch (Exception ex) { log.WriteLine("FAIL input release: " + ex.Message); result = 1; }
            log.WriteLine("EXIT_CODE=" + result);
            try
            {
                Console.Out.Write(File.ReadAllText(EngineTestReportPath));
                Console.Out.Flush();
            }
            catch { }
            ExitImmediately(result);
        }
    }
#if !PRODUCTION_PUBLISH
    async Task RunProfileSwitchRuntimeTestAndExit()
    {
        int result = await ProfileSwitchRuntimeTest.RunAsync();
        try
        {
            File.AppendAllText(ProfileSwitchRuntimeTest.ReportPath, "app:runtime-test-returned" + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
        ShutdownWithExitCode(result);
    }
#endif
#endif
    internal static string[] AttachMacroShortcutTarget(IReadOnlyList<string> arguments, Func<IntPtr> resolveTarget)
    {
        if (ReadMacroShortcutTarget(arguments) != null || !ShortcutService.TryReadMacroId(arguments, out _) && !ShortcutService.TryReadMacroName(arguments, out _))
            return [.. arguments];
        IntPtr target = resolveTarget();
        return target == IntPtr.Zero ? [.. arguments] : [.. arguments, "--target-hwnd", target.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)];
    }
    internal static IntPtr? ReadMacroShortcutTarget(IReadOnlyList<string> arguments)
    {
        for (int i = 0; i + 1 < arguments.Count; i++)
            if (arguments[i].Equals("--target-hwnd", StringComparison.OrdinalIgnoreCase) && long.TryParse(arguments[i + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long value) && value != 0)
                return new IntPtr(value);
        return null;
    }
    async Task RunMacroShortcutAndExit(string macroReference, bool byId, IntPtr? preferredActiveWindow)
    {
        int exitCode = 0;
        try
        {
            var macro = new ConfigService().Load().Macros.FirstOrDefault(x => (byId ? x.Id : x.Name).Equals(macroReference, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException(byId ? "ショートカットに対応するマクロが見つかりません。" : $"マクロ「{macroReference}」が見つかりません。");
            var macroConfig = new ConfigService().Load();
            macro = macroConfig.Macros.First(x => x.Id.Equals(macro.Id, StringComparison.OrdinalIgnoreCase));
            var result = await MacroPlayer.PlayAsync(macro, macroConfig, null, preferredActiveWindow);
            if (!result.Succeeded && !result.Cancelled)
                throw new InvalidOperationException(result.Message);
        }
        catch (Exception ex) { exitCode = 1; AppDialog.Show(ex.Message, "マクロを実行できません", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { ExitImmediately(exitCode); }
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (MainWindow is RELYR.MainWindow window)
            window.PrepareForSystemShutdown();
        base.OnSessionEnding(e);
    }
    void SystemPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.Suspend or PowerModes.Resume)
            ResetInputStateForSessionTransition();
    }
    void SystemSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.RemoteConnect)
            ResetInputStateForSessionTransition();
    }
    void ResetInputStateForSessionTransition()
    {
        if (MainWindow is RELYR.MainWindow window)
            window.ResetInputStateForSessionTransition();
        else
            InputEngine.ReleaseAllDefensively();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (ownsMutex)
            LifecycleDiagnostics.Write("resident-exited", $"code={e.ApplicationExitCode}");
        try
        {
            SystemMonitorService.Shared.Dispose();
            IpcRuntime.StopAsync().GetAwaiter().GetResult();
        }
        catch { }
        SystemEvents.PowerModeChanged -= SystemPowerModeChanged;
        SystemEvents.SessionSwitch -= SystemSessionSwitch;
        InputEngine.ReleaseAllDefensively();
        signalStop.Cancel();
        showSignal?.Set();
        shutdownSignal?.Set();
        foregroundWindowTracker?.Dispose();
        showSignal?.Dispose();
        showAcknowledgement?.Dispose();
        shutdownSignal?.Dispose();
        shutdownInProgressSignal?.Dispose();
        if (ownsMutex)
        {
            try
            {
                instanceMutex?.ReleaseMutex();
            }
            catch { }
        }
        instanceMutex?.Dispose();
        signalStop.Dispose();
        HookDiagnosticsTrace.Stop();
        base.OnExit(e);
    }
    [DllImport("kernel32.dll")]
    static extern bool SetProcessShutdownParameters(uint level, uint flags);
    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")]
    static extern bool TerminateProcess(IntPtr process, uint exitCode);
}
