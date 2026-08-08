namespace RELYR;

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;

public static class VirtualDesktopIntegrationTest
{
    public static int Run(TextWriter output)
    {
        Process? process = null;
        try
        {
            var (Count, CurrentNumber) = VirtualDesktopService.GetState();
            bool valid = Count >= 1 && CurrentNumber >= 1 && CurrentNumber <= Count;
            output.WriteLine(valid ? $"PASS virtual desktops count={Count} current={CurrentNumber}" : $"FAIL virtual desktop state count={Count} current={CurrentNumber}");
            if (!valid)
                return 1;
            if (Count > 1)
            {
                int directTarget = CurrentNumber == 1 ? Count : 1;
                InputEngine.SendShortcut("Desktop" + directTarget);
                bool directMoved = WaitForDesktop(directTarget);
                InputEngine.SendShortcut("Desktop" + CurrentNumber);
                bool directRestored = WaitForDesktop(CurrentNumber);
                output.WriteLine(directMoved && directRestored ? "PASS direct numbered desktop jump and restore" : "FAIL direct numbered desktop jump");
                if (!directMoved || !directRestored)
                    return 1;
            }
            using (var hookEngine = new InputEngine())
            {
                using var invoked = new ManualResetEventSlim();
                string? asyncError = null;
                InputEngine.DesktopActionFailed = message => asyncError = message;
                hookEngine.HasMapping = input => input.Equals("F24", StringComparison.OrdinalIgnoreCase);
                hookEngine.InputReceived = _ => { InputEngine.SendShortcut("Desktop" + CurrentNumber); invoked.Set(); return true; };
                hookEngine.Start();
                InputEngine.InjectKeyForTest("F24", false);
                InputEngine.InjectKeyForTest("F24", true);
                bool hookReturned = invoked.Wait(1500);
                Thread.Sleep(300);
                output.WriteLine(hookReturned && asyncError == null ? "PASS desktop action dispatched outside synchronous input hook" : "FAIL desktop action from input hook: " + asyncError);
                if (!hookReturned || asyncError != null)
                    return 1;
            }
            int offset = CurrentNumber < Count ? 1 : -1;
            string host = Environment.ProcessPath ?? throw new InvalidOperationException("Current process path is unavailable.");
            bool hostedByDotnet = Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
            string arguments = hostedByDotnet ? $"\"{Path.Combine(AppContext.BaseDirectory, "RELYR.dll")}\" --desktop-helper" : "--desktop-helper";
            process = Process.Start(new ProcessStartInfo(host, arguments) { UseShellExecute = false, CreateNoWindow = true }) ?? throw new InvalidOperationException("External test window could not be started.");
            IntPtr handle = IntPtr.Zero;
            for (int i = 0; i < 50 && handle == IntPtr.Zero; i++)
            {
                process.Refresh();
                handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero)
                    Thread.Sleep(100);
            }
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("External test window was not found.");
            Guid before = VirtualDesktopService.GetWindowDesktopId(handle);
            int moveOffset = offset;
            using var moveEngine = new InputEngine();
            moveEngine.HasMapping = input => input == "F23";
            moveEngine.InputReceived = _ => { InputEngine.SendShortcut(moveOffset > 0 ? "MoveWindowDesktopRight" : "MoveWindowDesktopLeft"); return true; };
            moveEngine.Start();
            VirtualDesktopService.ActivateWindow(handle);
            Thread.Sleep(150);
            InputEngine.InjectKeyForTest("F23", false);
            InputEngine.InjectKeyForTest("F23", true);
            WaitForDesktop(CurrentNumber + offset);
            Thread.Sleep(500);
            Guid moved = VirtualDesktopService.GetWindowDesktopId(handle);
            var followed = VirtualDesktopService.GetState();
            bool movedSuccessfully = before != moved && followed.CurrentNumber == CurrentNumber + offset;
            if (movedSuccessfully)
            {
                moveOffset = -offset;
                VirtualDesktopService.ActivateWindow(handle);
                Thread.Sleep(150);
                InputEngine.InjectKeyForTest("F23", false);
                InputEngine.InjectKeyForTest("F23", true);
                WaitForDesktop(CurrentNumber);
                Thread.Sleep(500);
            }
            var restored = VirtualDesktopService.GetState();
            bool restoredSuccessfully = restored.CurrentNumber == CurrentNumber && VirtualDesktopService.GetWindowDesktopId(handle) == before;
            process.CloseMainWindow();
            if (!process.WaitForExit(1500))
                process.Kill();
            output.WriteLine(movedSuccessfully && restoredSuccessfully ? "PASS external window and visible desktop moved together and restored" : "FAIL window and visible desktop did not move together");
            return movedSuccessfully && restoredSuccessfully ? 0 : 1;
        }
        catch (Exception ex) { output.WriteLine("FAIL virtual desktop integration: " + ex.Message); return 1; }
        finally { if (process is { HasExited: false }) { try { process.Kill(); } catch { } } process?.Dispose(); }
    }
    static bool WaitForDesktop(int oneBasedNumber)
    {
        for (int i = 0; i < 30; i++)
        {
            Thread.Sleep(100);
            if (VirtualDesktopService.GetState().CurrentNumber == oneBasedNumber)
                return true;
        }
        return false;
    }
}
