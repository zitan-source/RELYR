using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RELYR;

internal static class WindowMonitorService
{
    internal enum Direction { Left, Right, Up, Down }

    internal static void MinimizeForeground()
    {
        var window=GetActionableForegroundWindow();
        ShowWindow(window,6);
    }

    static IntPtr GetActionableForegroundWindow()
    {
        var window=VirtualDesktopService.GetForegroundRootWindow();
        if(window==IntPtr.Zero||!IsWindowVisible(window))throw new InvalidOperationException("操作するアクティブウィンドウがありません。");
        return window;
    }

    internal static void MoveForeground(Direction direction)
    {
        var window=VirtualDesktopService.GetForegroundRootWindow();
        if(window==IntPtr.Zero||!GetWindowRect(window,out var rect))throw new InvalidOperationException("移動するアクティブウィンドウがありません。");
        var screens=Screen.AllScreens;
        if(screens.Length<2)throw new InvalidOperationException("移動先のモニターがありません。");
        int current=Array.FindIndex(screens,x=>x.DeviceName==Screen.FromHandle(window).DeviceName);
        if(current<0)current=0;
        int target=SelectTargetIndex(screens.Select(x=>x.WorkingArea).ToArray(),current,direction);
        if(target<0)throw new InvalidOperationException($"{DirectionName(direction)}側にモニターがありません。");

        bool maximized=IsZoomed(window);bool minimized=IsIconic(window);
        if(maximized||minimized)ShowWindow(window,9);
        var source=screens[current].WorkingArea;var destination=screens[target].WorkingArea;
        int width=Math.Min(rect.Right-rect.Left,destination.Width);int height=Math.Min(rect.Bottom-rect.Top,destination.Height);
        double rx=(rect.Left-source.Left)/(double)Math.Max(1,source.Width-width);
        double ry=(rect.Top-source.Top)/(double)Math.Max(1,source.Height-height);
        int x=destination.Left+(int)Math.Round(Math.Clamp(rx,0,1)*Math.Max(0,destination.Width-width));
        int y=destination.Top+(int)Math.Round(Math.Clamp(ry,0,1)*Math.Max(0,destination.Height-height));
        if(!SetWindowPos(window,IntPtr.Zero,x,y,width,height,0x0004|0x0040))throw new InvalidOperationException("ウィンドウを移動できませんでした。");
        if(maximized)ShowWindow(window,3);
        VirtualDesktopService.ActivateWindow(window);
    }

    internal static void ToggleMaximizeUnderCursor()
    {
        var window=RootWindowUnderCursor();
        if(window==IntPtr.Zero||!IsWindowVisible(window))throw new InvalidOperationException("カーソル位置に操作できるウィンドウがありません。");
        ShowWindow(window,IsZoomed(window)?9:3);
        VirtualDesktopService.ActivateWindow(window);
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
    static string DirectionName(Direction d)=>d switch{Direction.Left=>"左",Direction.Right=>"右",Direction.Up=>"上",_=>"下"};
    [StructLayout(LayoutKind.Sequential)]struct RECT{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct POINT{public int X,Y;}
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr hWnd,out RECT rect);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hWnd,IntPtr insertAfter,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")]static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr hWnd,int command);
    [DllImport("user32.dll")]static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr hWnd,uint flags);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hWnd);
}
