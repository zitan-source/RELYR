using System.IO.MemoryMappedFiles;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RELYR;

// 常駐プロセスが最後の通常ウィンドウを記憶し、タスクバー起動プロセスへ共有する。
internal sealed class ForegroundWindowTracker : IDisposable
{
    const string SharedMemoryName=@"Local\RELYR.LastForegroundWindow.v1";
    const long SharedMemorySize=sizeof(long);
    const uint EventSystemForeground=0x0003;
    const uint WineventOutOfContext=0x0000;
    readonly object sync=new();
    readonly MemoryMappedFile sharedMemory;
    readonly MemoryMappedViewAccessor sharedView;
    readonly WinEventDelegate callback;
    IntPtr hook;
    bool disposed;

    internal ForegroundWindowTracker()
    {
        sharedMemory=MemoryMappedFile.CreateOrOpen(SharedMemoryName,SharedMemorySize,MemoryMappedFileAccess.ReadWrite);
        sharedView=sharedMemory.CreateViewAccessor(0,SharedMemorySize,MemoryMappedFileAccess.ReadWrite);
        callback=ForegroundChanged;
        hook=SetWinEventHook(EventSystemForeground,EventSystemForeground,IntPtr.Zero,callback,0,0,WineventOutOfContext);
        if(hook==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"前面ウィンドウの監視を開始できませんでした。");
        Remember(GetForegroundWindow());
    }

    void ForegroundChanged(IntPtr eventHook,uint eventType,IntPtr window,int objectId,int childId,uint eventThread,uint eventTime)
    {
        if(eventType==EventSystemForeground&&objectId==0&&childId==0)Remember(window);
    }

    void Remember(IntPtr window)
    {
        if(window==IntPtr.Zero)return;
        GetWindowThreadProcessId(window,out uint processId);
        var className=new StringBuilder(256);
        GetClassName(window,className,className.Capacity);
        var candidate=new WindowMonitorService.WindowCandidate(window,className.ToString(),IsWindowVisible(window));
        if(!ShouldTrackWindow(candidate,processId,(uint)Environment.ProcessId))return;
        lock(sync)
        {
            if(disposed)return;
            sharedView.Write(0,window.ToInt64());
            sharedView.Flush();
        }
    }

    internal static bool ShouldTrackWindow(WindowMonitorService.WindowCandidate candidate,uint processId,uint ownProcessId)
        =>processId!=0&&processId!=ownProcessId&&WindowMonitorService.IsShortcutTargetCandidate(candidate);

    internal static IntPtr ReadLastWindow()
    {
        try
        {
            using var memory=MemoryMappedFile.OpenExisting(SharedMemoryName,MemoryMappedFileRights.Read);
            using var view=memory.CreateViewAccessor(0,SharedMemorySize,MemoryMappedFileAccess.Read);
            return new IntPtr(view.ReadInt64(0));
        }
        catch(FileNotFoundException){return IntPtr.Zero;}
        catch(UnauthorizedAccessException){return IntPtr.Zero;}
        catch(IOException){return IntPtr.Zero;}
    }

    public void Dispose()
    {
        lock(sync)
        {
            if(disposed)return;
            disposed=true;
            if(hook!=IntPtr.Zero){UnhookWinEvent(hook);hook=IntPtr.Zero;}
            sharedView.Dispose();
            sharedMemory.Dispose();
        }
    }

    delegate void WinEventDelegate(IntPtr eventHook,uint eventType,IntPtr window,int objectId,int childId,uint eventThread,uint eventTime);
    [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetWinEventHook(uint eventMin,uint eventMax,IntPtr module,WinEventDelegate callback,uint processId,uint threadId,uint flags);
    [DllImport("user32.dll")]static extern bool UnhookWinEvent(IntPtr eventHook);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder className,int maxCount);
}
