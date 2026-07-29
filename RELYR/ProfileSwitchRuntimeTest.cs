using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace RELYR;

internal static class ProfileSwitchRuntimeTest
{
    internal const string HostWindowTitle="RELYR Profile Test Host";
    internal static string ReportPath=>Path.Combine(Path.GetTempPath(),"RELYR-profile-switch-runtime.log");

    internal static async Task<int> RunAsync()
    {
        var log=new StringBuilder();
        string source=Environment.ProcessPath??throw new InvalidOperationException("実行ファイルを取得できません。");
        string directory=Path.GetDirectoryName(source)??".";
        var hosts=new List<(Process Process,string Path)>();
        MainWindow? mainWindow=null;
        var originalCursor=new POINT();
        GetCursorPos(out originalCursor);
        try
        {
            var saved=new ConfigService().Load();
            var automaticProfiles=saved.Profiles.Skip(1)
                .Where(profile=>profile.AutoSwitchEnabled&&profile.AutoSwitchApplications.Count>0)
                .ToList();
            if(automaticProfiles.Count==0)throw new InvalidOperationException("自動切替が有効なプロファイルがありません。");

            var standard=await StartHost(source,directory,"RELYR AutoSwitch Standard Host.exe","標準");
            hosts.Add(standard);
            PositionHost(standard.Process.MainWindowHandle,40,120);
            var profileHosts=new List<(Profile Profile,Process Process,string Path)>();
            for(int index=0;index<automaticProfiles.Count;index++)
            {
                var profile=automaticProfiles[index];
                string executable=Path.GetFileName(profile.AutoSwitchApplications[0]);
                if(string.IsNullOrWhiteSpace(executable)||!executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))
                    executable=profile.Name+".exe";
                var host=await StartHost(source,directory,executable,profile.Name);
                hosts.Add(host);
                PositionHost(host.Process.MainWindowHandle,500+(index%3)*460,120+(index/3)*300);
                profileHosts.Add((profile,host.Process,host.Path));
            }

            MoveCursorTo(standard.Process.MainWindowHandle);
            // Runtime profile tests must not register a real notification icon:
            // the test process exits through the hard safety path by design.
            mainWindow=new MainWindow(skipSetup:true,suppressTray:true);
            await WaitForProfile(mainWindow,saved.Profiles[0].Name,2500);

            int totalChecks=0;
            int passedChecks=0;
            foreach(var item in profileHosts)
            {
                int entered=0,returned=0;
                for(int cycle=1;cycle<=3;cycle++)
                {
                    MoveCursorTo(item.Process.MainWindowHandle);
                    string[] candidates=ConditionMatcher.ProcessesUnderCursor().ToArray();
                    bool enteredThisCycle=await WaitForProfile(mainWindow,item.Profile.Name,2500);
                    totalChecks++;
                    if(enteredThisCycle){entered++;passedChecks++;}
                    log.AppendLine($"profile={item.Profile.Name} cycle={cycle} phase=enter candidates={string.Join(",",candidates)} active={mainWindow.AppliedProfileNameForTest} pass={enteredThisCycle}");

                    MoveCursorTo(standard.Process.MainWindowHandle);
                    candidates=ConditionMatcher.ProcessesUnderCursor().ToArray();
                    bool returnedThisCycle=await WaitForProfile(mainWindow,saved.Profiles[0].Name,2500);
                    totalChecks++;
                    if(returnedThisCycle){returned++;passedChecks++;}
                    log.AppendLine($"profile={item.Profile.Name} cycle={cycle} phase=return candidates={string.Join(",",candidates)} active={mainWindow.AppliedProfileNameForTest} pass={returnedThisCycle}");
                }
                log.AppendLine($"PROFILE_RESULT name={item.Profile.Name} enter={entered}/3 return={returned}/3");
            }
            log.AppendLine($"RESULT profiles={automaticProfiles.Count} checks={passedChecks}/{totalChecks}");
            File.WriteAllText(ReportPath,log.ToString(),Encoding.UTF8);
            return passedChecks==totalChecks?0:1;
        }
        catch(Exception ex)
        {
            log.AppendLine("ERROR "+ex);
            File.WriteAllText(ReportPath,log.ToString(),Encoding.UTF8);
            return 1;
        }
        finally
        {
            AppendCleanupLog("cleanup:start");
            SetCursorPos(originalCursor.X,originalCursor.Y);
            AppendCleanupLog("cleanup:cursor-restored");
            foreach(var host in hosts)
            {
                AppendCleanupLog($"cleanup:host-start pid={host.Process.Id}");
                try{if(!host.Process.HasExited)host.Process.Kill(false);}catch{}
                AppendCleanupLog($"cleanup:host-kill-requested pid={host.Process.Id}");
                host.Process.Dispose();
                AppendCleanupLog("cleanup:host-disposed");
                try{File.Delete(host.Path);}catch{}
                AppendCleanupLog("cleanup:host-file-delete-attempted");
            }
            // This command is a short-lived executable test. App.ExitImmediately
            // performs the process-wide hook release after this method returns;
            // closing a never-shown MainWindow here can wait on WPF shutdown.
            AppendCleanupLog("cleanup:release-input-start");
            try{InputEngine.ReleaseAll();}catch{}
            AppendCleanupLog("cleanup:release-input-finished");
        }
    }

    static void AppendCleanupLog(string message)
    {
        try{File.AppendAllText(ReportPath,message+Environment.NewLine,Encoding.UTF8);}catch{}
    }

    static async Task<(Process Process,string Path)> StartHost(string source,string directory,string executable,string profileName)
    {
        string helper=Path.Combine(directory,executable);
        File.Copy(source,helper,true);
        var process=Process.Start(new ProcessStartInfo(helper,$"--profile-switch-test-host \"RELYR Profile Test Host - {profileName}\""){UseShellExecute=false})
            ??throw new InvalidOperationException($"{profileName}テストウィンドウを起動できません。");
        for(int i=0;i<50;i++)
        {
            await Task.Delay(100);
            process.Refresh();
            if(process.HasExited)break;
            if(process.MainWindowHandle!=IntPtr.Zero)return(process,helper);
        }
        try{if(!process.HasExited)process.Kill(false);}catch{}
        process.Dispose();
        try{File.Delete(helper);}catch{}
        throw new InvalidOperationException($"{profileName}テストウィンドウを検出できません。");
    }

    static void MoveCursorTo(IntPtr window)
    {
        if(window==IntPtr.Zero||!GetWindowRect(window,out var rect))
            throw new InvalidOperationException("テストウィンドウの位置を取得できません。");
        SetForegroundWindow(window);
        SetCursorPos((rect.Left+rect.Right)/2,(rect.Top+rect.Bottom)/2);
    }

    static void PositionHost(IntPtr window,int left,int top)
    {
        if(window==IntPtr.Zero||!SetWindowPos(window,IntPtr.Zero,left,top,420,260,0x0004|0x0010))
            throw new InvalidOperationException("テストウィンドウを配置できません。");
    }

    static async Task<bool> WaitForProfile(MainWindow window,string expected,int timeoutMs)
    {
        var limit=DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while(DateTime.UtcNow<limit)
        {
            if(window.AppliedProfileNameForTest.Equals(expected,StringComparison.OrdinalIgnoreCase))return true;
            await Task.Delay(100);
        }
        return window.AppliedProfileNameForTest.Equals(expected,StringComparison.OrdinalIgnoreCase);
    }

    [StructLayout(LayoutKind.Sequential)]struct POINT{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]struct RECT{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT rect);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr insertAfter,int x,int y,int width,int height,uint flags);
}
