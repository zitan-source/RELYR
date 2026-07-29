using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RELYR;

internal static class WindowMonitorService
{
    internal enum Direction { Left, Right, Up, Down }
    internal readonly record struct WindowCandidate(IntPtr Handle,string ClassName,bool Visible);
    static readonly HashSet<string> ShortcutShellClasses=new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Progman",
        "WorkerW",
        "Windows.UI.Core.CoreWindow"
    };

    internal static IntPtr ResolveTarget(WindowActionTarget target,IntPtr? preferredActiveWindow=null)
    {
        var window=SelectResolvedTarget(
            target,
            preferredActiveWindow,
            IsUsableWindow,
            RootWindowUnderCursor,
            VirtualDesktopService.GetForegroundRootWindow);
        if(!IsUsableWindow(window))
            throw new InvalidOperationException(target==WindowActionTarget.WindowUnderCursor
                ?"マウスカーソルの位置に操作できるウィンドウがありません。"
                :"操作できるアクティブウィンドウがありません。");
        return window;
    }

    internal static IntPtr SelectResolvedTarget(
        WindowActionTarget target,
        IntPtr? preferredActiveWindow,
        Func<IntPtr,bool> isUsable,
        Func<IntPtr> windowUnderCursor,
        Func<IntPtr> activeWindow)
    {
        if(preferredActiveWindow is { } preferred&&isUsable(preferred))return preferred;
        return target==WindowActionTarget.WindowUnderCursor?windowUnderCursor():activeWindow();
    }

    // タスクバー等を前面ウィンドウの候補から除外し、直前の通常ウィンドウを取得する。
    internal static IntPtr GetActiveWindowForShortcut()
    {
        IntPtr remembered=SelectRememberedShortcutTarget(ForegroundWindowTracker.ReadLastWindow(),IsUsableWindow);
        if(remembered!=IntPtr.Zero)return remembered;
        IntPtr current=GetTopWindow(IntPtr.Zero);
        while(current!=IntPtr.Zero)
        {
            var className=new System.Text.StringBuilder(256);
            GetClassName(current,className,className.Capacity);
            if(IsShortcutTargetCandidate(new(current,className.ToString(),IsWindowVisible(current))))return current;
            current=GetWindow(current,2);
        }
        return IntPtr.Zero;
    }

    internal static IntPtr SelectRememberedShortcutTarget(IntPtr remembered,Func<IntPtr,bool> isUsable)
        =>remembered!=IntPtr.Zero&&isUsable(remembered)?remembered:IntPtr.Zero;

    internal static bool IsShortcutTargetCandidate(WindowCandidate candidate)
        =>candidate.Handle!=IntPtr.Zero&&candidate.Visible&&!ShortcutShellClasses.Contains(candidate.ClassName);

    internal static IntPtr SelectShortcutTarget(IEnumerable<WindowCandidate> candidates)
        =>candidates.FirstOrDefault(IsShortcutTargetCandidate).Handle;

    internal static bool IsUsableWindow(IntPtr window)=>window!=IntPtr.Zero&&IsWindow(window)&&IsWindowVisible(window);

    internal static void Minimize(WindowActionTarget target)=>Minimize(ResolveTarget(target));
    internal static void Minimize(IntPtr window)=>ShowWindow(ValidateTarget(window),6);

    internal static void ToggleMaximize(WindowActionTarget target)=>ToggleMaximize(ResolveTarget(target));

    internal static void ToggleMaximize(IntPtr window)
    {
        ValidateTarget(window);
        ShowWindow(window,IsZoomed(window)?9:3);
        VirtualDesktopService.ActivateWindow(window);
    }

    internal static void ToggleMaximizeUnderCursor()=>ToggleMaximize(WindowActionTarget.WindowUnderCursor);

    internal static void Maximize(WindowActionTarget target)=>Maximize(ResolveTarget(target));

    internal static void Maximize(IntPtr window)
    {
        ValidateTarget(window);
        ShowWindow(window,3);
        VirtualDesktopService.ActivateWindow(window);
    }

    internal static void RestoreOrMinimize(WindowActionTarget target)=>RestoreOrMinimize(ResolveTarget(target));

    internal static void RestoreOrMinimize(IntPtr window)
    {
        ValidateTarget(window);
        ShowWindow(window,IsZoomed(window)?9:6);
        VirtualDesktopService.ActivateWindow(window);
    }

    internal static void Close(WindowActionTarget target)=>Close(ResolveTarget(target));

    internal static void Close(IntPtr window)
    {
        ValidateTarget(window);
        // Posting SC_CLOSE matches Alt+F4 without changing focus and avoids a blocking
        // cross-process SendMessage call (which previously caused input-sync errors).
        if(!PostMessage(window,0x0112,(IntPtr)0xF060,IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"ウィンドウを閉じる命令を送れませんでした。");
    }

    internal static void Snap(WindowActionTarget target,Direction direction)=>Snap(ResolveTarget(target),direction);

    internal static void Snap(IntPtr window,Direction direction)
    {
        if(direction is not (Direction.Left or Direction.Right))throw new ArgumentOutOfRangeException(nameof(direction));
        ValidateTarget(window);
        if(IsZoomed(window)||IsIconic(window))ShowWindow(window,9);
        var area=Screen.FromHandle(window).WorkingArea;
        int left=direction==Direction.Left?area.Left:area.Left+area.Width/2;
        int width=direction==Direction.Left?area.Width/2:area.Width-area.Width/2;
        if(!SetWindowPos(window,IntPtr.Zero,left,area.Top,width,area.Height,0x0004|0x0040))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"ウィンドウを配置できませんでした。");
        VirtualDesktopService.ActivateWindow(window);
    }

    internal static void Move(WindowActionTarget target,Direction direction)=>Move(ResolveTarget(target),direction);

    internal static void Move(IntPtr window,Direction direction)
    {
        ValidateTarget(window);
        if(!GetWindowRect(window,out var rect))throw new InvalidOperationException("移動するウィンドウの位置を取得できませんでした。");
        var screens=Screen.AllScreens;
        if(screens.Length<2)throw new InvalidOperationException("移動先のモニターがありません。");
        int current=Array.FindIndex(screens,x=>x.DeviceName==Screen.FromHandle(window).DeviceName);
        if(current<0)current=0;
        int destinationIndex=SelectTargetIndex(screens.Select(x=>x.WorkingArea).ToArray(),current,direction);
        if(destinationIndex<0)throw new InvalidOperationException($"{DirectionName(direction)}側にモニターがありません。");

        bool maximized=IsZoomed(window);bool minimized=IsIconic(window);
        if(maximized||minimized)ShowWindow(window,9);
        var source=screens[current].WorkingArea;var destination=screens[destinationIndex].WorkingArea;
        int width=Math.Min(rect.Right-rect.Left,destination.Width);int height=Math.Min(rect.Bottom-rect.Top,destination.Height);
        double rx=(rect.Left-source.Left)/(double)Math.Max(1,source.Width-width);
        double ry=(rect.Top-source.Top)/(double)Math.Max(1,source.Height-height);
        int x=destination.Left+(int)Math.Round(Math.Clamp(rx,0,1)*Math.Max(0,destination.Width-width));
        int y=destination.Top+(int)Math.Round(Math.Clamp(ry,0,1)*Math.Max(0,destination.Height-height));
        if(!SetWindowPos(window,IntPtr.Zero,x,y,width,height,0x0004|0x0040))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"ウィンドウを移動できませんでした。");
        if(maximized)ShowWindow(window,3);
        VirtualDesktopService.ActivateWindow(window);
    }

    static IntPtr ValidateTarget(IntPtr window)
    {
        if(!IsUsableWindow(window))
            throw new InvalidOperationException("操作対象のウィンドウがなくなりました。");
        return window;
    }

    static IntPtr RootWindowUnderCursor()
    {
        if(!GetCursorPos(out var point))throw new InvalidOperationException("マウスカーソルの位置を取得できませんでした。");
        return GetAncestor(WindowFromPoint(point),2);
    }

    internal static IntPtr WindowUnderCursorForTest()=>RootWindowUnderCursor();
    internal static bool IsMaximizedForTest(IntPtr window)=>IsZoomed(window);

    internal static int SelectTargetIndex(IReadOnlyList<System.Drawing.Rectangle> areas,int current,Direction direction)
    {
        if(current<0||current>=areas.Count)return -1;var source=Center(areas[current]);int best=-1;double bestScore=double.MaxValue;
        for(int i=0;i<areas.Count;i++)
        {
            if(i==current)continue;var candidate=Center(areas[i]);double dx=candidate.X-source.X,dy=candidate.Y-source.Y;
            double primary=direction switch{Direction.Left=>-dx,Direction.Right=>dx,Direction.Up=>-dy,_=>dy};
            if(primary<=1)continue;double perpendicular=direction is Direction.Left or Direction.Right?Math.Abs(dy):Math.Abs(dx);
            double score=primary+perpendicular*1.5;if(score<bestScore){bestScore=score;best=i;}
        }
        return best;
    }

    static (double X,double Y) Center(System.Drawing.Rectangle r)=>(r.Left+r.Width/2d,r.Top+r.Height/2d);
    static string DirectionName(Direction direction)=>direction switch{Direction.Left=>"左",Direction.Right=>"右",Direction.Up=>"上",_=>"下"};
    [StructLayout(LayoutKind.Sequential)]struct RECT{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct POINT{public int X,Y;}
    [DllImport("user32.dll",SetLastError=true)]static extern bool GetWindowRect(IntPtr hWnd,out RECT rect);
    [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPos(IntPtr hWnd,IntPtr insertAfter,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")]static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr hWnd,int command);
    [DllImport("user32.dll",SetLastError=true)]static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr hWnd,uint flags);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")]static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")]static extern IntPtr GetTopWindow(IntPtr hWnd);
    [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr hWnd,uint command);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr hWnd,System.Text.StringBuilder className,int maxCount);
    [DllImport("user32.dll",SetLastError=true)]static extern bool PostMessage(IntPtr hWnd,uint message,IntPtr wParam,IntPtr lParam);
}
