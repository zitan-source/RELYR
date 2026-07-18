using Microsoft.Win32;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace RELYR;

public static class StartupService
{
    internal const string LauncherTaskName="RELYR Elevated Launcher";
    internal const string StartupTaskName="RELYR Elevated Startup";
    const string LegacyLauncherTaskName="InputCustomizer Elevated Launcher";
    const string LegacyStartupTaskName="InputCustomizer Elevated Startup";
    const string LegacyTaskName="InputCustomizer";
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    const int TaskActionExecute=0,TaskTriggerLogon=9,TaskCreateOrUpdate=6;
    const int TaskLogonInteractiveToken=3,TaskRunLevelHighest=1,TaskInstancesParallel=0;

    public static void SetEnabled(bool enabled)
    {
        RequireElevated();
        RemoveLegacyRunEntry();
        EnsureElevatedLauncher();
        if(enabled)RegisterTask(StartupTaskName,CurrentExecutablePath(),"--tray",true);
        else DeleteTask(StartupTaskName);
    }

    public static bool IsEnabled()
    {
        try{return TaskExists(StartupTaskName);}
        catch{return false;}
    }

    public static void EnsureMatchesConfig(bool enabled)
    {
        if(!IsProcessElevated())return;
        EnsureElevatedLauncher();
        string? command=GetRegisteredCommand();
        if(enabled&&!string.Equals(command,BuildCurrentCommand(),StringComparison.OrdinalIgnoreCase))SetEnabled(true);
        else if(!enabled&&command!=null)SetEnabled(false);
    }

    public static void EnsureElevatedLauncher()
    {
        RequireElevated();
        RemoveLegacyRunEntry();
        string expected=BuildLauncherCommand(CurrentExecutablePath());
        if(!string.Equals(GetTaskCommand(LauncherTaskName),expected,StringComparison.OrdinalIgnoreCase))
            RegisterTask(LauncherTaskName,CurrentExecutablePath(),"--elevated-task \"$(Arg0)\"",false);
        DeleteTask(LegacyLauncherTaskName);
        DeleteTask(LegacyStartupTaskName);
        DeleteTask(LegacyTaskName);
    }

    public static void RemoveElevatedTasks()
    {
        RequireElevated();
        DeleteTask(LauncherTaskName);
        DeleteTask(StartupTaskName);
        DeleteTask(LegacyLauncherTaskName);
        DeleteTask(LegacyStartupTaskName);
        DeleteTask(LegacyTaskName);
        RemoveLegacyRunEntry();
    }

    public static bool TryRunElevated(IReadOnlyList<string> arguments,out string error)
    {
        error="";
        try
        {
            dynamic service=ConnectService();dynamic root=service.GetFolder("\\");dynamic task=root.GetTask(LauncherTaskName);
            task.Run(EncodeArguments(arguments));
            return true;
        }
        catch(Exception ex)
        {
            error="管理者モードの起動タスクを開始できませんでした。RELYRを再インストールしてください。\n\n"+ex.Message;
            return false;
        }
    }

    public static string[] DecodeElevatedArguments(string encoded)
    {
        if(string.IsNullOrWhiteSpace(encoded))return [];
        byte[] bytes=Convert.FromBase64String(encoded);
        return JsonSerializer.Deserialize<string[]>(Encoding.UTF8.GetString(bytes))??[];
    }

    internal static string EncodeArguments(IReadOnlyList<string> arguments)=>Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(arguments)));
    public static bool IsProcessElevated()=>new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public static string? GetRegisteredCommand()=>GetTaskCommand(StartupTaskName);
    public static string BuildCommand(string executablePath)=>$"\"{executablePath}\" --tray";
    public static string BuildCurrentCommand()=>BuildCommand(CurrentExecutablePath());
    internal static string BuildLauncherCommand(string executablePath)=>$"\"{executablePath}\" --elevated-task \"$(Arg0)\"";

    static string CurrentExecutablePath()
    {
        string process=Environment.ProcessPath??throw new InvalidOperationException("実行ファイルのパスを取得できませんでした。");
        if(Path.GetFileNameWithoutExtension(process).Equals("dotnet",StringComparison.OrdinalIgnoreCase))
            return Path.Combine(AppContext.BaseDirectory,"RELYR.exe");
        return process;
    }

    static void RegisterTask(string taskName,string executablePath,string arguments,bool logonTrigger)
    {
        dynamic service=ConnectService();dynamic root=service.GetFolder("\\");dynamic definition=service.NewTask(0);
        string user=WindowsIdentity.GetCurrent().Name;
        definition.RegistrationInfo.Author="RELYR";
        definition.RegistrationInfo.Description=logonTrigger?"RELYRをサインイン時に管理者モードで起動します。":"RELYRをUAC確認なしで管理者モード起動します。";
        definition.Principal.UserId=user;
        definition.Principal.LogonType=TaskLogonInteractiveToken;
        definition.Principal.RunLevel=TaskRunLevelHighest;
        definition.Settings.Enabled=true;
        definition.Settings.AllowDemandStart=true;
        definition.Settings.StartWhenAvailable=true;
        definition.Settings.DisallowStartIfOnBatteries=false;
        definition.Settings.StopIfGoingOnBatteries=false;
        definition.Settings.ExecutionTimeLimit="PT0S";
        definition.Settings.MultipleInstances=TaskInstancesParallel;
        if(logonTrigger)
        {
            dynamic trigger=definition.Triggers.Create(TaskTriggerLogon);
            trigger.UserId=user;
            trigger.Enabled=true;
        }
        dynamic action=definition.Actions.Create(TaskActionExecute);
        action.Path=executablePath;
        action.Arguments=arguments;
        action.WorkingDirectory=Path.GetDirectoryName(executablePath)??AppContext.BaseDirectory;
        root.RegisterTaskDefinition(taskName,definition,TaskCreateOrUpdate,user,null,TaskLogonInteractiveToken,null);
    }

    static dynamic ConnectService()
    {
        var type=Type.GetTypeFromProgID("Schedule.Service")??throw new PlatformNotSupportedException("Windowsタスクスケジューラを利用できません。");
        dynamic service=Activator.CreateInstance(type)??throw new InvalidOperationException("Windowsタスクスケジューラへ接続できません。");
        service.Connect();return service;
    }

    static bool TaskExists(string name){try{dynamic service=ConnectService();dynamic root=service.GetFolder("\\");_ = root.GetTask(name);return true;}catch{return false;}}
    static string? GetTaskCommand(string name)
    {
        try{dynamic service=ConnectService();dynamic root=service.GetFolder("\\");dynamic task=root.GetTask(name);dynamic action=task.Definition.Actions.Item(1);return $"\"{(string)action.Path}\" {(string)action.Arguments}".TrimEnd();}
        catch{return null;}
    }
    static void DeleteTask(string name){try{dynamic service=ConnectService();dynamic root=service.GetFolder("\\");root.DeleteTask(name,0);}catch{}}
    static void RemoveLegacyRunEntry(){try{using var key=Registry.CurrentUser.CreateSubKey(RunKey);key?.DeleteValue(LegacyTaskName,false);}catch{}}
    static void RequireElevated(){if(!IsProcessElevated())throw new UnauthorizedAccessException("この設定には管理者モードが必要です。RELYRをインストールし直してください。");}
}

internal static class LegacyKeyRemapService
{
    const string KeyboardLayoutKey=@"SYSTEM\CurrentControlSet\Control\Keyboard Layout";
    const string ValueName="Scancode Map";
    const ushort CapsLockScanCode=0x003A,F13ScanCode=0x0064;

    internal static bool HasCapsLockToF13()
    {
        try
        {
            using var key=Registry.LocalMachine.OpenSubKey(KeyboardLayoutKey);
            return key?.GetValue(ValueName) is byte[] bytes&&ContainsCapsLockToF13(bytes);
        }
        catch{return false;}
    }

    internal static bool ContainsCapsLockToF13(byte[] bytes)=>Entries(bytes).Any(IsCapsLockToF13);

    internal static bool IsRestartStillPending(AppConfig config,DateTime? utcNow=null,long? uptimeMilliseconds=null)
    {
        if(!config.CapsLockRemapPendingRestart||config.CapsLockRemapChangedAtUtcTicks<=0)return false;
        long now=(utcNow??DateTime.UtcNow).Ticks;long elapsedTicks=Math.Max(0,now-config.CapsLockRemapChangedAtUtcTicks);long uptimeTicks=Math.Max(0,uptimeMilliseconds??Environment.TickCount64)*TimeSpan.TicksPerMillisecond;
        return elapsedTicks<=uptimeTicks+TimeSpan.FromSeconds(2).Ticks;
    }

    internal static void SetCapsLockToF13(bool enabled)
    {
        using var key=Registry.LocalMachine.CreateSubKey(KeyboardLayoutKey,true)
            ??throw new UnauthorizedAccessException("キーボード設定を変更できませんでした。");
        byte[] result=UpdateCapsLockToF13(key.GetValue(ValueName) as byte[],enabled);
        if(result.Length==0){key.DeleteValue(ValueName,false);return;}
        key.SetValue(ValueName,result,RegistryValueKind.Binary);
    }

    internal static byte[] UpdateCapsLockToF13(byte[]? current,bool enabled)
    {
        var entries=current is null?[]:Entries(current);
        entries.RemoveAll(entry=>(ushort)(entry>>16)==CapsLockScanCode);
        if(enabled)entries.Add(((uint)CapsLockScanCode<<16)|F13ScanCode);
        if(entries.Count==0)return [];
        byte[] result=new byte[12+(entries.Count+1)*4];
        BitConverter.GetBytes(entries.Count+1).CopyTo(result,8);
        for(int i=0;i<entries.Count;i++)BitConverter.GetBytes(entries[i]).CopyTo(result,12+i*4);
        return result;
    }

    static List<uint> Entries(byte[] bytes)
    {
        if(bytes.Length<16)return [];
        int declared=BitConverter.ToInt32(bytes,8)-1;
        if(declared<=0)return [];
        int count=Math.Min(declared,(bytes.Length-12)/4);
        var result=new List<uint>(count);
        for(int i=0;i<count;i++)result.Add(BitConverter.ToUInt32(bytes,12+i*4));
        return result;
    }

    static bool IsCapsLockToF13(uint entry)=>(ushort)(entry>>16)==CapsLockScanCode&&(ushort)entry==F13ScanCode;
}
