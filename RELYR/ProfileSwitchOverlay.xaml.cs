using System.Windows;
using System.Runtime.InteropServices;

namespace RELYR;

public partial class ProfileSwitchOverlay:Window
{
    readonly TimeSpan visibleDuration;
    System.Threading.Timer? hideTimer;

    internal ProfileSwitchOverlay(string profileName,TimeSpan? duration=null)
    {
        InitializeComponent();
        ProfileNameText.Text=profileName;
        visibleDuration=duration??TimeSpan.FromSeconds(1);
        Loaded+=OverlayLoaded;
        Closed+=(_,_)=>{hideTimer?.Dispose();hideTimer=null;};
    }

    void OverlayLoaded(object sender,RoutedEventArgs e)
    {
        PositionOnCurrentScreen();
        var handle=new System.Windows.Interop.WindowInteropHelper(this).Handle;
        hideTimer=new System.Threading.Timer(_=>
        {
            // Hide through Win32 first so the notification cannot remain visible
            // when the WPF dispatcher is temporarily busy.
            if(handle!=IntPtr.Zero)ShowWindow(handle,0);
            _=Dispatcher.BeginInvoke(new Action(()=>
            {
                if(IsLoaded)Close();
            }));
        },null,visibleDuration,Timeout.InfiniteTimeSpan);
    }

    void PositionOnCurrentScreen()
    {
        var area=System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        var source=PresentationSource.FromVisual(this);
        if(source?.CompositionTarget is not { } target)return;
        var topLeft=target.TransformFromDevice.Transform(new System.Windows.Point(area.Left,area.Top));
        var bottomRight=target.TransformFromDevice.Transform(new System.Windows.Point(area.Right,area.Bottom));
        Left=topLeft.X+(bottomRight.X-topLeft.X-ActualWidth)/2;
        // 仮想デスクトップ番号と視線を離さず、重ならないように
        // Windowsの表示領域より少し上へ積み重ねる。
        Top=Math.Max(topLeft.Y+16,bottomRight.Y-ActualHeight-118);
    }

    internal void HideImmediatelyForProcessExit()
    {
        hideTimer?.Dispose();
        hideTimer=null;
        var handle=new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if(handle!=IntPtr.Zero)ShowWindow(handle,0);
    }

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr window,int command);
}
