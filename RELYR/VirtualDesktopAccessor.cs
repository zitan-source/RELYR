using System.Runtime.InteropServices;

namespace RELYR;

internal static class VirtualDesktopAccessor
{
    internal static int CurrentNumber=>GetCurrentDesktopNumber();
    internal static int Count=>GetDesktopCount();

    internal static void GoToNumber(int zeroBasedNumber)
    {
        int count=Count;
        if(zeroBasedNumber<0||zeroBasedNumber>=count)
            throw new InvalidOperationException($"仮想デスクトップ {zeroBasedNumber+1} が存在しません。");
        GoToDesktopNumber(zeroBasedNumber);
    }

    internal static void MoveWindowAndFollow(IntPtr window,int offset)
    {
        int current=CurrentNumber,target=current+offset,count=Count;
        if(target<0||target>=count)
            throw new InvalidOperationException(offset>0?"右側に仮想デスクトップがありません。":"左側に仮想デスクトップがありません。");
        MoveWindowToDesktopNumber(window,target);
        GoToDesktopNumber(target);
    }

    [DllImport("VirtualDesktopAccessor.dll",CallingConvention=CallingConvention.Cdecl)]
    static extern int GetCurrentDesktopNumber();
    [DllImport("VirtualDesktopAccessor.dll",CallingConvention=CallingConvention.Cdecl)]
    static extern int GetDesktopCount();
    [DllImport("VirtualDesktopAccessor.dll",CallingConvention=CallingConvention.Cdecl)]
    static extern void GoToDesktopNumber(int desktopNumber);
    [DllImport("VirtualDesktopAccessor.dll",CallingConvention=CallingConvention.Cdecl)]
    static extern void MoveWindowToDesktopNumber(IntPtr window,int desktopNumber);
}
