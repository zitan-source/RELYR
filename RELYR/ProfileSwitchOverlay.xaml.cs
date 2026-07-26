using System.Windows;
using System.Windows.Media.Animation;

namespace RELYR;

public partial class ProfileSwitchOverlay:Window
{
    readonly TimeSpan visibleDuration;

    internal ProfileSwitchOverlay(string profileName,TimeSpan? duration=null)
    {
        InitializeComponent();
        ProfileNameText.Text=profileName;
        visibleDuration=duration??TimeSpan.FromSeconds(1);
        Loaded+=OverlayLoaded;
    }

    async void OverlayLoaded(object sender,RoutedEventArgs e)
    {
        PositionOnCurrentScreen();
        try
        {
            var fadeDuration=TimeSpan.FromMilliseconds(Math.Min(180,visibleDuration.TotalMilliseconds/3));
            var hold=visibleDuration-fadeDuration;
            if(hold>TimeSpan.Zero)await Task.Delay(hold);
            BeginAnimation(OpacityProperty,new DoubleAnimation(1,0,fadeDuration));
            await Task.Delay(fadeDuration);
            Close();
        }
        catch(TaskCanceledException){}
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
}
