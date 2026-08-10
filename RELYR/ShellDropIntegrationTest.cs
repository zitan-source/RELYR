using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Point = System.Windows.Point;

namespace RELYR;

internal static class ShellDropIntegrationTest
{
    const int WmDropFiles = 0x0233;
    const uint GmemMoveable = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    struct DropFiles
    {
        internal uint Offset;
        internal int X;
        internal int Y;
        [MarshalAs(UnmanagedType.Bool)] internal bool NonClient;
        [MarshalAs(UnmanagedType.Bool)] internal bool Wide;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    internal static int Run()
    {
        string report = VerificationPaths.GetFile("shell-drop-test-last.log");
        string directory = VerificationPaths.CreateRunDirectory("shell-drop-test");
        string file = Path.Combine(directory, "dropped-file.txt");
        DeckPanelOverlayWindow? overlay = null;
        try
        {
            File.WriteAllText(file, "RELYR shell drop test", Encoding.UTF8);
            var layout = new DeckLayoutDefinition { Name = "Shell drop", Columns = 3, Rows = 3 };
            var config = new AppConfig { DeckLayouts = [layout], SharedDefaultDeckLayoutId = layout.Id, Profiles = [new Profile { Name = "標準", DefaultDeckLayoutId = layout.Id }] };
            overlay = new DeckPanelOverlayWindow(config, null, selectedLayout: layout);
            overlay.Show();
            overlay.BeginDeferredBuild();
            overlay.UpdateLayout();
            overlay.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
            IntPtr hwnd = new WindowInteropHelper(overlay).Handle;
            if (hwnd == IntPtr.Zero || !overlay.UsesShellFileDrop)
                throw new InvalidOperationException("Shell drop target was not enabled.");
            var center = overlay.DeckButtons[0].PointToScreen(new Point(overlay.DeckButtons[0].ActualWidth / 2, overlay.DeckButtons[0].ActualHeight / 2));
            var point = new NativePoint { X = (int)Math.Round(center.X), Y = (int)Math.Round(center.Y) };
            if (!ScreenToClient(hwnd, ref point))
                throw new InvalidOperationException("Could not resolve the Deck button client point.");
            IntPtr drop = CreateDropHandle(file, point);
            SendMessage(hwnd, WmDropFiles, drop, IntPtr.Zero);
            overlay.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
            bool accepted = DeckPanelLayout.FindMapping(layout, 1)?.DeckFilePath?.Equals(Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase) == true;
            File.WriteAllText(report, accepted ? "SHELL DROP INTEGRATION TEST PASSED" : "SHELL DROP INTEGRATION TEST FAILED", Encoding.UTF8);
            return accepted ? 0 : 1;
        }
        catch (Exception error)
        {
            File.WriteAllText(report, "SHELL DROP INTEGRATION TEST FAILED: " + error, Encoding.UTF8);
            return 1;
        }
        finally { overlay?.Close(); }
    }

    static IntPtr CreateDropHandle(string path, NativePoint point)
    {
        int header = Marshal.SizeOf<DropFiles>();
        byte[] text = Encoding.Unicode.GetBytes(path + "\0\0");
        IntPtr drop = GlobalAlloc(GmemMoveable, (nuint)(header + text.Length));
        if (drop == IntPtr.Zero)
            throw new OutOfMemoryException();
        IntPtr memory = GlobalLock(drop);
        if (memory == IntPtr.Zero)
        {
            GlobalFree(drop);
            throw new OutOfMemoryException();
        }
        try
        {
            Marshal.StructureToPtr(new DropFiles { Offset = (uint)header, X = point.X, Y = point.Y, Wide = true }, memory, false);
            Marshal.Copy(text, 0, IntPtr.Add(memory, header), text.Length);
        }
        finally { GlobalUnlock(drop); }
        return drop;
    }

    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint point);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr memory);
}
