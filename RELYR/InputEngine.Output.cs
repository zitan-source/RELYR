using System.Runtime.InteropServices;
using System.Windows.Input;

namespace RELYR;

/// <summary>
/// Shortcut, text, mouse, and window output for <see cref="InputEngine" />.
/// This file contains only injected-output behavior; hook capture stays in InputEngine.cs.
/// </summary>
public sealed partial class InputEngine
{
    public static void SendShortcut(string value, bool useUsLayout = false, WindowActionTarget windowTarget = WindowActionTarget.ActiveWindow, IntPtr? preferredActiveWindow = null)
    {
        if (TryDispatchApplicationAction(value))
            return;
        if (OverlayService.TryShow(value))
            return;
        if (TryDispatchWindowAction(value, windowTarget, preferredActiveWindow))
            return;
        value = ResolveShortcutAlias(value);
        if (IsLockWorkStationShortcut(value))
        {
            bool locked = LockWorkStationOutputForTest?.Invoke() ?? LockWorkStation();
            if (!locked)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windowsをロックできませんでした。");
            return;
        }
        if (TryGetImeAction(value, out int imeMode))
        {
            QueueImeAction(imeMode);
            return;
        }
        // 仮想デスクトップの左右移動は疑似キーを送らない。Ctrl+Win+矢印を
        // 外部ツールが横取りしてウィンドウ移動へ変える環境でも、表示中の
        // デスクトップだけを確実に切り替える。
        if (TryGetDirectDesktopStep(value, out int desktopStep))
        {
            QueueDesktopAction(() => VirtualDesktopAccessor.GoToNumber(VirtualDesktopAccessor.CurrentNumber + desktopStep));
            return;
        }
        if (value.StartsWith("Desktop", StringComparison.OrdinalIgnoreCase) && int.TryParse(value[7..], out int desktop))
        {
            QueueDesktopAction(() => VirtualDesktopAccessor.GoToNumber(desktop - 1));
            return;
        }
        var names = SplitShortcut(value);
        string? mouse = names.FirstOrDefault(x => x.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase) || x.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase) || x.StartsWith("Tilt", StringComparison.OrdinalIgnoreCase));
        var keyNames = names.Where(x => x != mouse).ToArray();
        var codes = new List<ushort>();
        foreach (string keyName in keyNames)
        {
            if (TryResolveShiftedSymbol(keyName, useUsLayout, out ushort symbolKey))
            {
                if (!codes.Contains(0x10))
                    codes.Add(0x10);
                codes.Add(symbolKey);
                continue;
            }
            ushort code = ParseKey(keyName);
            if (code == 0)
                throw new ArgumentException($"認識できないキーです: {keyName}");
            codes.Add(code);
        }
        lock (OutputLock)
        {
            if (mouse?.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase) == true || mouse?.StartsWith("Tilt", StringComparison.OrdinalIgnoreCase) == true)
            {
                long wait = 4 - (Environment.TickCount64 - lastWheelOutput);
                if (wait > 0)
                    Thread.Sleep((int)wait);
                lastWheelOutput = Environment.TickCount64;
            }
            var pressed = new List<ushort>();
            try
            {
                foreach (var c in codes)
                {
                    if (SendKey(c, false))
                        pressed.Add(c);
                }
                if (mouse != null)
                    SendMouse(mouse);
            }
            finally { foreach (var c in pressed.AsEnumerable().Reverse()) SendKeyUpWithRetry(c); }
        }
    }
    static bool TryDispatchWindowAction(string value, WindowActionTarget target, IntPtr? preferredActiveWindow)
    {
        // 旧版の定番アクションは実際のショートカット文字列で保存されている。
        // カーソル下を選んだ場合は、既存の割り当ても現在の対象設定に従わせる。
        if (target == WindowActionTarget.WindowUnderCursor)
        {
            if (ShortcutMatches(value, "Alt", "F4"))
            {
                QueueWindowAction(target, preferredActiveWindow, WindowMonitorService.Close);
                return true;
            }
            if (ShortcutMatches(value, "Win", "Left"))
            {
                QueueWindowAction(target, preferredActiveWindow, window => WindowMonitorService.Snap(window, WindowMonitorService.Direction.Left));
                return true;
            }
            if (ShortcutMatches(value, "Win", "Right"))
            {
                QueueWindowAction(target, preferredActiveWindow, window => WindowMonitorService.Snap(window, WindowMonitorService.Direction.Right));
                return true;
            }
            if (ShortcutMatches(value, "Win", "Up"))
            {
                QueueWindowAction(target, preferredActiveWindow, WindowMonitorService.Maximize);
                return true;
            }
            if (ShortcutMatches(value, "Win", "Down"))
            {
                QueueWindowAction(target, preferredActiveWindow, WindowMonitorService.RestoreOrMinimize);
                return true;
            }
        }

        switch (value.ToUpperInvariant())
        {
            case "CLOSEACTIVEWINDOW":
                QueueWindowAction(target, preferredActiveWindow, WindowMonitorService.Close);
                return true;
            case "MOVEWINDOWDESKTOPRIGHT":
                QueueWindowMove(1, target, preferredActiveWindow);
                return true;
            case "MOVEWINDOWDESKTOPLEFT":
                QueueWindowMove(-1, target, preferredActiveWindow);
                return true;
            case "TOGGLEMAXIMIZEUNDERCURSOR":
            case "TOGGLEMAXIMIZEWINDOW":
                QueueWindowAction(target, preferredActiveWindow, WindowMonitorService.ToggleMaximize);
                return true;
            case "MAXIMIZEWINDOW":
                QueueWindowAction(target, preferredActiveWindow, WindowMonitorService.Maximize);
                return true;
            case "RESTOREORMINIMIZEWINDOW":
                QueueWindowAction(target, preferredActiveWindow, WindowMonitorService.RestoreOrMinimize);
                return true;
            case "MINIMIZEACTIVEWINDOW":
                QueueWindowAction(target, preferredActiveWindow, WindowMonitorService.Minimize);
                return true;
            case "SNAPWINDOWLEFT":
                QueueWindowAction(target, preferredActiveWindow, window => WindowMonitorService.Snap(window, WindowMonitorService.Direction.Left));
                return true;
            case "SNAPWINDOWRIGHT":
                QueueWindowAction(target, preferredActiveWindow, window => WindowMonitorService.Snap(window, WindowMonitorService.Direction.Right));
                return true;
        }

        const string monitorActionPrefix = "MoveWindowMonitor";
        if (value.StartsWith(monitorActionPrefix, StringComparison.OrdinalIgnoreCase)
           && Enum.TryParse<WindowMonitorService.Direction>(value[monitorActionPrefix.Length..], true, out var direction))
        {
            QueueWindowAction(target, preferredActiveWindow, window => WindowMonitorService.Move(window, direction));
            return true;
        }
        return false;
    }
    static string ResolveShortcutAlias(string value)
    {
        if (value.Equals("CloseActiveWindow", StringComparison.OrdinalIgnoreCase))
            return "Alt+F4";
        if (!value.Equals("ToggleMinimizeAllWindows", StringComparison.OrdinalIgnoreCase))
            return value;
        lock (OutputLock)
        {
            string shortcut = restoreMinimizedWindowsNext ? "Shift+Win+M" : "Win+M";
            restoreMinimizedWindowsNext = !restoreMinimizedWindowsNext;
            return shortcut;
        }
    }
    internal static string ResolveShortcutAliasForTest(string value) => ResolveShortcutAlias(value);
    internal static void ResetMinimizeAllToggleForTest()
    {
        lock (OutputLock)
            restoreMinimizedWindowsNext = false;
    }
    static bool IsLockWorkStationShortcut(string value)
    {
        var names = SplitShortcut(value);
        return names.Length == 2 && names.Any(x => x.Equals("L", StringComparison.OrdinalIgnoreCase)) && names.Any(x => x.Equals("Win", StringComparison.OrdinalIgnoreCase) || x.Equals("LWin", StringComparison.OrdinalIgnoreCase) || x.Equals("RWin", StringComparison.OrdinalIgnoreCase));
    }
    static string[] SplitShortcut(string value)
    {
        if (value == "+")
            return ["+"];
        var names = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return value.EndsWith("++", StringComparison.Ordinal) ? [.. names, "+"] : names;
    }
    static bool ShortcutMatches(string value, params string[] expected)
    {
        static string Normalize(string key) => key.ToUpperInvariant() switch
        {
            "LEFTALT" or "RIGHTALT" => "ALT",
            "LEFTCTRL" or "RIGHTCTRL" => "CTRL",
            "LEFTSHIFT" or "RIGHTSHIFT" => "SHIFT",
            "LWIN" or "RWIN" or "LEFTWIN" or "RIGHTWIN" => "WIN",
            var other => other
        };
        var actual = SplitShortcut(value).Select(Normalize).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var wanted = expected.Select(Normalize).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        return actual.SequenceEqual(wanted, StringComparer.Ordinal);
    }
    internal static bool ShortcutMatchesForTest(string value, params string[] expected) => ShortcutMatches(value, expected);
    static bool TryResolveShiftedSymbol(string value, bool useUsLayout, out ushort key)
    {
        key = 0;
        if (value.Length != 1)
            return false;
        key = (ushort)(useUsLayout ? value[0] switch
        {
            '~' => 0xC0,
            '!' => 0x31,
            '@' => 0x32,
            '#' => 0x33,
            '$' => 0x34,
            '%' => 0x35,
            '^' => 0x36,
            '&' => 0x37,
            '*' => 0x38,
            '(' => 0x39,
            ')' => 0x30,
            '_' => 0xBD,
            '+' => 0xBB,
            '{' => 0xDB,
            '}' => 0xDD,
            '|' => 0xDC,
            ':' => 0xBA,
            '"' => 0xDE,
            '<' => 0xBC,
            '>' => 0xBE,
            '?' => 0xBF,
            _ => 0
        } : value[0] switch
        {
            '!' => 0x31,
            '"' => 0x32,
            '#' => 0x33,
            '$' => 0x34,
            '%' => 0x35,
            '&' => 0x36,
            '\'' => 0x37,
            '(' => 0x38,
            ')' => 0x39,
            '=' => 0xBD,
            '~' => 0xDE,
            '|' => 0xDC,
            '`' => 0xC0,
            '{' => 0xDB,
            '+' => 0xBB,
            '*' => 0xBA,
            '}' => 0xDD,
            '<' => 0xBC,
            '>' => 0xBE,
            '?' => 0xBF,
            '_' => 0xE2,
            _ => 0
        });
        return key != 0;
    }
    internal static bool TryResolveShiftedSymbolForTest(string value, bool useUsLayout, out ushort key) => TryResolveShiftedSymbol(value, useUsLayout, out key);
    internal static bool IsRecognizedShortcut(string value)
    {
        if (value.Equals(ActionCatalog.ShowRelyrMainWindowAction, StringComparison.OrdinalIgnoreCase))
            return true;
        if (OverlayService.IsOverlayAction(value))
            return true;
        if (TryGetImeAction(value, out _))
            return true;
        if (value.Equals("MoveWindowDesktopRight", StringComparison.OrdinalIgnoreCase) || value.Equals("MoveWindowDesktopLeft", StringComparison.OrdinalIgnoreCase) || value.Equals("ToggleMaximizeUnderCursor", StringComparison.OrdinalIgnoreCase) || value.Equals("ToggleMaximizeWindow", StringComparison.OrdinalIgnoreCase) || value.Equals("MaximizeWindow", StringComparison.OrdinalIgnoreCase) || value.Equals("RestoreOrMinimizeWindow", StringComparison.OrdinalIgnoreCase) || value.Equals("MinimizeActiveWindow", StringComparison.OrdinalIgnoreCase) || value.Equals("CloseActiveWindow", StringComparison.OrdinalIgnoreCase) || value.Equals("SnapWindowLeft", StringComparison.OrdinalIgnoreCase) || value.Equals("SnapWindowRight", StringComparison.OrdinalIgnoreCase) || value.Equals("ToggleMinimizeAllWindows", StringComparison.OrdinalIgnoreCase))
            return true;
        if (value.StartsWith("MoveWindowMonitor", StringComparison.OrdinalIgnoreCase))
            return Enum.TryParse<WindowMonitorService.Direction>(value[17..], true, out _);
        if (value.StartsWith("Desktop", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(value[7..], out int desktop) && desktop is >= 1 and <= 8;
        var names = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return names.Length > 0 && names.All(name => ParseKey(name) != 0 || name.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Tilt", StringComparison.OrdinalIgnoreCase));
    }
    internal static bool TryGetDirectDesktopStep(string value, out int offset)
    {
        if (value.Equals("Ctrl+Win+Left", StringComparison.OrdinalIgnoreCase))
        {
            offset = -1;
            return true;
        }
        if (value.Equals("Ctrl+Win+Right", StringComparison.OrdinalIgnoreCase))
        {
            offset = 1;
            return true;
        }
        offset = 0;
        return false;
    }
    internal static bool TryGetImeAction(string value, out int mode)
    {
        if (value.Equals("ImeOff", StringComparison.OrdinalIgnoreCase))
        {
            mode = 0;
            return true;
        }
        if (value.Equals("ImeOn", StringComparison.OrdinalIgnoreCase))
        {
            mode = 1;
            return true;
        }
        if (value.Equals("ImeToggle", StringComparison.OrdinalIgnoreCase))
        {
            mode = 2;
            return true;
        }
        mode = -1;
        return false;
    }
    static void QueueImeAction(int mode)
    {
        if (ImeActionOutputForTest is { } output)
        {
            output(mode);
            return;
        }
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero)
            throw new InvalidOperationException("IMEを切り替える対象ウィンドウがありません。");
        QueueDesktopAction(() => ApplyImeAction(window, mode));
    }
    static void ApplyImeAction(IntPtr window, int mode)
    {
        IntPtr imeWindow = ImmGetDefaultIMEWnd(window);
        if (imeWindow != IntPtr.Zero)
        {
            if (SendMessageTimeout(imeWindow, 0x0283, (IntPtr)0x0005, IntPtr.Zero, 0x0002, 1000, out UIntPtr current) == IntPtr.Zero)
                throw new InvalidOperationException("IMEの現在状態を取得できませんでした。");
            bool enabled = mode == 2 ? current == UIntPtr.Zero : mode == 1;
            if (SendMessageTimeout(imeWindow, 0x0283, (IntPtr)0x0006, enabled ? (IntPtr)1 : IntPtr.Zero, 0x0002, 1000, out _) == IntPtr.Zero)
                throw new InvalidOperationException("IMEの状態を変更できませんでした。");
            return;
        }
        IntPtr context = ImmGetContext(window);
        if (context == IntPtr.Zero)
            throw new InvalidOperationException("このウィンドウではIMEを切り替えられません。");
        try
        {
            bool enabled = mode == 2 ? !ImmGetOpenStatus(context) : mode == 1;
            if (!ImmSetOpenStatus(context, enabled))
                throw new InvalidOperationException("IMEの状態を変更できませんでした。");
        }
        finally { ImmReleaseContext(window, context); }
    }
    static void QueueWindowMove(int offset, WindowActionTarget target, IntPtr? preferredActiveWindow)
    {
        IntPtr window = WindowMonitorService.ResolveTarget(target, preferredActiveWindow);
        QueueDesktopAction(() => { VirtualDesktopAccessor.MoveWindowAndFollow(window, offset); _ = Task.Run(async () => { await Task.Delay(350); VirtualDesktopService.ActivateWindow(window); }); });
    }
    static void QueueWindowAction(WindowActionTarget target, IntPtr? preferredActiveWindow, Action<IntPtr> action)
    {
        // フック処理中のカーソル位置を確定する。バックグラウンド処理を待つ間に
        // カーソルや前面ウィンドウが変わっても、別のウィンドウへ誤送信しない。
        IntPtr window = WindowMonitorService.ResolveTarget(target, preferredActiveWindow);
        QueueDesktopAction(() => action(window));
    }
    internal static void QueueDesktopAction(Action action)
    {
        if (DesktopActionOutputForTest is { } testOutput)
        {
            testOutput(action);
            return;
        }
        DesktopActions.Add(action);
    }
    internal static void MoveWindowAndFollowForTest(IntPtr window, int offset)
    {
        VirtualDesktopAccessor.MoveWindowAndFollow(window, offset);
        _ = Task.Run(async () => { await Task.Delay(350); VirtualDesktopService.ActivateWindow(window); });
    }
    static void SendChord(params ushort[] keys)
    {
        var pressed = new List<ushort>();
        try
        {
            foreach (var key in keys)
            if (SendKey(key, false))
                pressed.Add(key);
        }
        finally { foreach (var key in pressed.AsEnumerable().Reverse()) SendKeyUpWithRetry(key); }
    }
    public static void SendText(string value, bool useUsLayout = false)
    {
        if (string.IsNullOrEmpty(value))
            return;
        // Chromium/Electron の contenteditable は、VK_PACKET の単一記号に続く
        // Enter でキャレットを再配置することがある。通常キーで表せる1文字は
        // AHK と同じ物理キー列で送り、それ以外だけを Unicode 入力にする。
        if (value.Length == 1 && TryResolveTextCharacter(value[0], useUsLayout, out ushort key, out bool shift))
        {
            SendTextKey(key, shift);
            return;
        }
        SendUnicodeText(value);
    }
    static bool TryResolveTextCharacter(char value, bool useUsLayout, out ushort key, out bool shift)
    {
        key = 0;
        shift = false;
        if (value is >= 'a' and <= 'z')
        {
            key = (ushort)char.ToUpperInvariant(value);
            return true;
        }
        if (value is >= 'A' and <= 'Z')
        {
            key = value;
            shift = true;
            return true;
        }
        if (value is >= '0' and <= '9')
        {
            key = value;
            return true;
        }
        if (value == ' ')
        {
            key = 0x20;
            return true;
        }
        if (TryResolveShiftedSymbol(value.ToString(), useUsLayout, out key))
        {
            shift = true;
            return true;
        }
        key = (ushort)(useUsLayout ? value switch
        {
            '`' => 0xC0,
            '-' => 0xBD,
            '=' => 0xBB,
            '[' => 0xDB,
            ']' => 0xDD,
            '\\' => 0xDC,
            ';' => 0xBA,
            '\'' => 0xDE,
            ',' => 0xBC,
            '.' => 0xBE,
            '/' => 0xBF,
            _ => 0
        } : value switch
        {
            '-' => 0xBD,
            '^' => 0xDE,
            '¥' => 0xDC,
            '@' => 0xC0,
            '[' => 0xDB,
            ';' => 0xBB,
            ':' => 0xBA,
            ']' => 0xDD,
            ',' => 0xBC,
            '.' => 0xBE,
            '/' => 0xBF,
            '\\' => 0xE2,
            _ => 0
        });
        return key != 0;
    }
    static void SendTextKey(ushort key, bool shift)
    {
        bool shiftDown = false, keyDown = false;
        lock (OutputLock)
        {
            try
            {
                if (shift)
                {
                    shiftDown = SendKey(0x10, false);
                    if (!shiftDown)
                        throw new InvalidOperationException("文字入力用のShiftキーを送信できませんでした。");
                }
                keyDown = SendKey(key, false);
                if (!keyDown)
                    throw new InvalidOperationException("文字入力キーを送信できませんでした。");
            }
            finally
            {
                if (keyDown)
                    SendKeyUpWithRetry(key);
                if (shiftDown)
                    SendKeyUpWithRetry(0x10);
            }
        }
    }
    static void SendUnicodeText(string value)
    {
        if (UnicodeTextOutputForTest is { } testOutput)
        {
            testOutput(value);
            return;
        }
        var inputs = new INPUT[value.Length * 2];
        for (int i = 0; i < value.Length; i++)
        {
            inputs[i * 2] = new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wScan = value[i], dwFlags = 4u, dwExtraInfo = (UIntPtr)Marker } } };
            inputs[i * 2 + 1] = new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wScan = value[i], dwFlags = 6u, dwExtraInfo = (UIntPtr)Marker } } };
        }
        lock (OutputLock)
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != (uint)inputs.Length)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "文字列を入力できませんでした。");
    }
    public static void SendRecordedEvent(string recordedEvent)
    {
        if (recordedEvent.StartsWith("MouseMoveRelative:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = recordedEvent[18..].Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int dx) || !int.TryParse(parts[1], out int dy))
                throw new ArgumentException("認識できないマウス移動量です: " + recordedEvent);
            SendInput(1, [new INPUT { type = 0, U = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = 1, dwExtraInfo = (UIntPtr)Marker } } }], Marshal.SizeOf<INPUT>());
            return;
        }
        if (recordedEvent.StartsWith("MouseMove:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = recordedEvent[10..].Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y))
                throw new ArgumentException("認識できないマウス座標です: " + recordedEvent);
            int left = GetSystemMetrics(76), top = GetSystemMetrics(77), width = Math.Max(1, GetSystemMetrics(78) - 1), height = Math.Max(1, GetSystemMetrics(79) - 1);
            SendInput(1, [new INPUT { type = 0, U = new InputUnion { mi = new MOUSEINPUT { dx = (int)Math.Clamp((long)(x - left) * 65535 / width, 0, 65535), dy = (int)Math.Clamp((long)(y - top) * 65535 / height, 0, 65535), dwFlags = 0xC001, dwExtraInfo = (UIntPtr)Marker } } }], Marshal.SizeOf<INPUT>());
            return;
        }
        bool up = recordedEvent.EndsWith(" Up", StringComparison.OrdinalIgnoreCase), down = recordedEvent.EndsWith(" Down", StringComparison.OrdinalIgnoreCase);
        if (!up && !down)
            throw new ArgumentException("認識できないマクロイベントです: " + recordedEvent);
        string name = recordedEvent[..^(up ? 3 : 5)].TrimEnd();
        switch (name.ToUpperInvariant())
        {
            case "MOUSELEFT":
                if (up)
                    SendMouseUpWithRetry(4);
                else
                    SendMouseFlag(2);
                return;
            case "MOUSERIGHT":
                if (up)
                    SendMouseUpWithRetry(16);
                else
                    SendMouseFlag(8);
                return;
            case "MOUSEMIDDLE":
                if (up)
                    SendMouseUpWithRetry(64);
                else
                    SendMouseFlag(32);
                return;
            case "MOUSEBACK":
                if (up)
                    SendMouseUpWithRetry(0x100, 1);
                else
                    SendMouseFlag(0x80, 1);
                return;
            case "MOUSEFORWARD" or "MOUSEX":
                if (up)
                    SendMouseUpWithRetry(0x100, 2);
                else
                    SendMouseFlag(0x80, 2);
                return;
            case "WHEELUP":
                if (down)
                    SendMouse("WheelUp");
                return;
            case "WHEELDOWN":
                if (down)
                    SendMouse("WheelDown");
                return;
            case "TILTLEFT":
                if (down)
                    SendMouse("TiltLeft");
                return;
            case "TILTRIGHT":
                if (down)
                    SendMouse("TiltRight");
                return;
        }
        ushort key = ParseKey(name);
        if (key == 0)
            throw new ArgumentException("認識できないマクロキーです: " + name);
        if (up)
            SendKeyUpWithRetry(key);
        else
            SendKey(key, false);
    }
    internal static bool IsValidRecordedEvent(string recordedEvent)
    {
        if (recordedEvent.StartsWith("MouseMoveRelative:", StringComparison.OrdinalIgnoreCase))
        {
            var p = recordedEvent[18..].Split(',');
            return p.Length == 2 && int.TryParse(p[0], out _) && int.TryParse(p[1], out _);
        }
        if (recordedEvent.StartsWith("MouseMove:", StringComparison.OrdinalIgnoreCase))
        {
            var p = recordedEvent[10..].Split(',');
            return p.Length == 2 && int.TryParse(p[0], out _) && int.TryParse(p[1], out _);
        }
        bool suffix = recordedEvent.EndsWith(" Down", StringComparison.OrdinalIgnoreCase) || recordedEvent.EndsWith(" Up", StringComparison.OrdinalIgnoreCase);
        if (!suffix)
            return false;
        string name = recordedEvent[..^(recordedEvent.EndsWith(" Up", StringComparison.OrdinalIgnoreCase) ? 3 : 5)].TrimEnd();
        if (new[] { "MouseLeft", "MouseRight", "MouseMiddle", "MouseBack", "MouseForward", "MouseX", "WheelUp", "WheelDown", "TiltLeft", "TiltRight" }.Contains(name, StringComparer.OrdinalIgnoreCase))
            return true;
        return ParseKey(name) != 0;
    }
    public static void SendMouse(string action)
    {
        if (TryModifierDragAction(action, out ushort modifier, out int phase))
        {
            SendModifierDrag(modifier, phase);
            return;
        }
        switch (action.ToUpperInvariant())
        {
            case "LEFTDOWN":
                SendMouseFlag(2);
                return;
            case "LEFTUP":
                SendMouseUpWithRetry(4);
                return;
            case "RIGHTDOWN":
                SendMouseFlag(8);
                return;
            case "RIGHTUP":
                SendMouseUpWithRetry(16);
                return;
            case "MIDDLEDOWN":
                SendMouseFlag(32);
                return;
            case "MIDDLEUP":
                SendMouseUpWithRetry(64);
                return;
        }
        (uint down, uint up) = action.ToUpperInvariant() switch
        {
            "LEFT" or "CLICK" or "MOUSELEFT" => (2u, 4u),
            "RIGHT" or "MOUSERIGHT" => (8u, 16u),
            "MIDDLE" or "MOUSEMIDDLE" => (32u, 64u),
            _ => (0u, 0u)
        };
        if (down != 0)
        {
            SendMouseFlag(down);
            SendMouseUpWithRetry(up);
        }
        else if (action.Equals("MouseBack", StringComparison.OrdinalIgnoreCase))
        {
            SendMouseFlag(0x80, 1);
            SendMouseUpWithRetry(0x100, 1);
        }
        else if (action.Equals("MouseForward", StringComparison.OrdinalIgnoreCase) || action.Equals("MouseX", StringComparison.OrdinalIgnoreCase))
        {
            SendMouseFlag(0x80, 2);
            SendMouseUpWithRetry(0x100, 2);
        }
        else if (action.Equals("WheelUp", StringComparison.OrdinalIgnoreCase))
            SendMouseFlag(0x800, 120);
        else if (action.Equals("WheelDown", StringComparison.OrdinalIgnoreCase))
            SendMouseFlag(0x800, unchecked((uint)-120));
        else if (action.Equals("TiltRight", StringComparison.OrdinalIgnoreCase))
            SendMouseFlag(0x1000, 120);
        else if (action.Equals("TiltLeft", StringComparison.OrdinalIgnoreCase))
            SendMouseFlag(0x1000, unchecked((uint)-120));
        else
            throw new ArgumentException("認識できないマウス操作です: " + action);
    }
    static void SendMouseClickAtomic(string action)
    {
        (uint down, uint up, uint data, int button) = action.ToUpperInvariant() switch
        {
            "MOUSELEFT" => (2u, 4u, 0u, 1),
            "MOUSERIGHT" => (8u, 16u, 0u, 2),
            "MOUSEMIDDLE" => (32u, 64u, 0u, 3),
            "MOUSEBACK" => (0x80u, 0x100u, 1u, 4),
            "MOUSEFORWARD" or "MOUSEX" => (0x80u, 0x100u, 2u, 5),
            _ => (0u, 0u, 0u, 0)
        };
        if (down == 0)
        {
            SendMouse(action);
            return;
        }
        lock (OutputLock)
        {
            var batch = new[] { (Flag: down, Data: data), (Flag: up, Data: data) };
            if (MouseClickBatchOutputForTest is { } testBatch)
            {
                testBatch(batch);
                return;
            }
            if (MouseFlagOutputForTest is { } testOutput)
            {
                testOutput(down, data);
                testOutput(up, data);
                return;
            }
            var inputs = new[]
            {
                new INPUT{type=0,U=new InputUnion{mi=new MOUSEINPUT{dwFlags=down,mouseData=data,dwExtraInfo=(UIntPtr)Marker}}},
                new INPUT{type=0,U=new InputUnion{mi=new MOUSEINPUT{dwFlags=up,mouseData=data,dwExtraInfo=(UIntPtr)Marker}}}
            };
            uint sent = SendInput(2, inputs, Marshal.SizeOf<INPUT>());
            InjectedMouseButtonsDown.Remove(button);
            InjectedMouseDownAt.Remove(button);
            if (sent == 2)
                return;
            if (sent == 0)
                SendMouseFlag(down, data);
            SendMouseUpWithRetry(up, data);
        }
    }

    internal static void NeutralizePhysicalSourceKey(string input)
    {
        int separator = input.LastIndexOf('+');
        string source = (separator >= 0 ? input[(separator + 1)..] : input).Trim();
        if (source.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase) || source.StartsWith("Wheel", StringComparison.OrdinalIgnoreCase) || source.StartsWith("Tilt", StringComparison.OrdinalIgnoreCase))
            return;
        ushort key = ParseKey(source);
        if (key != 0)
            SendKeyUpWithRetry(key);
    }

    static bool TryModifierDragAction(string action, out ushort modifier, out int phase)
    {
        string value = action.Trim();
        modifier = value.StartsWith("ShiftDrag", StringComparison.OrdinalIgnoreCase) ? (ushort)0x10 : value.StartsWith("CtrlDrag", StringComparison.OrdinalIgnoreCase) ? (ushort)0x11 : value.StartsWith("AltDrag", StringComparison.OrdinalIgnoreCase) ? (ushort)0x12 : (ushort)0;
        if (modifier == 0)
        {
            phase = 0;
            return false;
        }
        string name = modifier == 0x10 ? "ShiftDrag" : modifier == 0x11 ? "CtrlDrag" : "AltDrag";
        if (value.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            phase = 0;
            return true;
        }
        if (value.Equals(name + ":Start", StringComparison.OrdinalIgnoreCase))
        {
            phase = 1;
            return true;
        }
        if (value.Equals(name + ":End", StringComparison.OrdinalIgnoreCase))
        {
            phase = 2;
            return true;
        }
        modifier = 0;
        phase = 0;
        return false;
    }
    static void SendModifierDrag(ushort modifier, int phase)
    {
        lock (OutputLock)
        {
            if (phase == 2)
            {
                EndModifierDragLocked();
                return;
            }
            BeginModifierDragLocked(modifier);
            if (phase == 0)
                EndModifierDragLocked();
        }
    }
    static void BeginModifierDragLocked(ushort modifier)
    {
        EndModifierDragLocked();
        modifierDragKey = modifier;
        if (!SendKey(modifier, false))
        {
            modifierDragKey = 0;
            throw new InvalidOperationException("ドラッグ用の修飾キーを押せませんでした。");
        }
        if (!SendMouseFlag(2))
        {
            EndModifierDragLocked();
            throw new InvalidOperationException("ドラッグ用の左ボタンを押せませんでした。");
        }
        modifierDragMouseDown = true;
        modifierDragStartedAt = Environment.TickCount64;
        modifierDragSafetyTimer = new System.Threading.Timer(_ => { if (!(PhysicalKeyDownForTest?.Invoke(0x01) ?? ((GetAsyncKeyState(0x01) & 0x8000) != 0)) || Environment.TickCount64 - Interlocked.Read(ref modifierDragStartedAt) > 30000) EndModifierDrag(); }, null, 100, 25);
    }
    public static void EndModifierDrag()
    {
        lock (OutputLock)
            EndModifierDragLocked();
    }
    static void EndModifierDragLocked()
    {
        ushort key = modifierDragKey;
        bool mouseDown = modifierDragMouseDown;
        modifierDragKey = 0;
        modifierDragMouseDown = false;
        Interlocked.Exchange(ref modifierDragStartedAt, 0);
        modifierDragSafetyTimer?.Dispose();
        modifierDragSafetyTimer = null;
        if (mouseDown)
            SendMouseUpWithRetry(4);
        if (key != 0)
            SendKeyUpWithRetry(key);
        if ((mouseDown && InjectedMouseButtonsDown.Contains(1)) || (key != 0 && InjectedKeysDown.Contains(key)))
            ReleaseAll();
    }
    static ushort ParseKey(string s) => s.ToUpperInvariant() switch { "半角/全角" => 0xF3, "無変換" => 0x1D, "変換" => 0x1C, "カタカナ" => 0x15, "PRINTSCREEN" => 0x2C, "SCROLLLOCK" => 0x91, "CTRL" => 0x11, "SHIFT" => 0x10, "ALT" => 0x12, "WIN" => 0x5B, "CAPSLOCK" => 0x14, "NUMPADENTER" => 0x0D, "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28, "ENTER" => 0x0D, "ESC" => 0x1B, "SPACE" => 0x20, "BACK" or "BACKSPACE" => 8, "DELETE" => 0x2E, _ when s.Length == 1 => (ushort)(VkKeyScan(s[0]) & 0xff), _ => (ushort)KeyInterop.VirtualKeyFromKey(Enum.TryParse<Key>(s, true, out var k) ? k : Key.None) };
    static bool SendKey(ushort vk, bool up)
    {
        lock (OutputLock)
        {
            bool sent;
            if (KeyOutputForTest is { } testOutput)
                sent = testOutput(vk, up);
            else
            {
                uint count = SendInput(1, [new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0, dwExtraInfo = (UIntPtr)Marker } } }], Marshal.SizeOf<INPUT>());
                sent = count == 1;
                DeckIpcDiagnostics.RecordSendInput(sent, sent ? 0 : Marshal.GetLastWin32Error());
            }
            if (sent)
            {
                if (up)
                {
                    InjectedKeysDown.Remove(vk);
                    InjectedKeyDownAt.Remove(vk);
                }
                else
                {
                    InjectedKeysDown.Add(vk);
                    InjectedKeyDownAt[vk] = Environment.TickCount64;
                }
            }
            return sent;
        }
    }
    static bool SendKeyUpWithRetry(ushort vk)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        if (SendKey(vk, true))
            return true;
        if (KeyOutputForTest != null)
            return false;
        keybd_event((byte)vk, 0, 2, (UIntPtr)Marker);
        InjectedKeysDown.Remove(vk);
        InjectedKeyDownAt.Remove(vk);
        return true;
    }
    static bool SendMouseFlag(uint flag, uint data = 0)
    {
        lock (OutputLock)
        {
            int button = flag switch
            {
                2 => 1,
                4 => -1,
                8 => 2,
                16 => -2,
                32 => 3,
                64 => -3,
                0x80 => data == 1 ? 4 : 5,
                0x100 => data == 1 ? -4 : -5,
                _ => 0
            };
            bool sent;
            if (MouseFlagOutputForTest is { } testOutput)
            {
                testOutput(flag, data);
                sent = true;
            }
            else
            {
                uint count = SendInput(1, [new INPUT { type = 0, U = new InputUnion { mi = new MOUSEINPUT { dx = 0, dy = 0, dwFlags = flag, mouseData = data, dwExtraInfo = (UIntPtr)Marker } } }], Marshal.SizeOf<INPUT>());
                sent = count == 1;
                DeckIpcDiagnostics.RecordSendInput(sent, sent ? 0 : Marshal.GetLastWin32Error());
            }
            if (sent)
            {
                if (button > 0)
                {
                    InjectedMouseButtonsDown.Add(button);
                    InjectedMouseDownAt[button] = Environment.TickCount64;
                }
                else if (button < 0)
                {
                    InjectedMouseButtonsDown.Remove(-button);
                    InjectedMouseDownAt.Remove(-button);
                }
            }
            return sent;
        }
    }
    static bool SendMouseUpWithRetry(uint flag, uint data = 0)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        if (SendMouseFlag(flag, data))
            return true;
        if (MouseFlagOutputForTest != null)
            return false;
        mouse_event(flag, 0, 0, data, (UIntPtr)Marker);
        int button = flag switch
        {
            4 => 1,
            16 => 2,
            64 => 3,
            0x100 => data == 1 ? 4 : 5,
            _ => 0
        };
        if (button > 0)
        {
            InjectedMouseButtonsDown.Remove(button);
            InjectedMouseDownAt.Remove(button);
        }
        return true;
    }
    static void ReleaseStaleInjectedInputs()
    {
        lock (OutputLock)
        {
            long now = Environment.TickCount64;
            foreach (var pair in InjectedKeyDownAt.Where(x => now - x.Value >= 5000).ToArray())
                SendKeyUpWithRetry(pair.Key);
            foreach (var pair in InjectedMouseDownAt.Where(x => now - x.Value >= 5000).ToArray())
                switch (pair.Key)
                {
                    case 1:
                        SendMouseUpWithRetry(4);
                        break;
                    case 2:
                        SendMouseUpWithRetry(16);
                        break;
                    case 3:
                        SendMouseUpWithRetry(64);
                        break;
                    case 4:
                        SendMouseUpWithRetry(0x100, 1);
                        break;
                    case 5:
                        SendMouseUpWithRetry(0x100, 2);
                        break;
                }
        }
    }
    public static void ReleaseAll()
    {
        // 終了時に入力送信スレッドとフックコールバックが互いのロックを
        // 待っても、プロセスを永久に残さない。通常経路を短時間だけ待ち、
        // 取得できなければ保持され得る入力をWin32へ直接解放する。
        if (!Monitor.TryEnter(OutputLock, 250))
        {
            ForceReleaseWithoutOutputLock();
            return;
        }
        try
        {
            foreach (ushort key in InjectedKeysDown.ToArray())
                SendKeyUpWithRetry(key);
            foreach (int button in InjectedMouseButtonsDown.ToArray())
                switch (button)
                {
                    case 1:
                        SendMouseUpWithRetry(4);
                        break;
                    case 2:
                        SendMouseUpWithRetry(16);
                        break;
                    case 3:
                        SendMouseUpWithRetry(64);
                        break;
                    case 4:
                        SendMouseUpWithRetry(0x100, 1);
                        break;
                    case 5:
                        SendMouseUpWithRetry(0x100, 2);
                        break;
                }
            modifierDragKey = 0;
            modifierDragMouseDown = false;
            Interlocked.Exchange(ref modifierDragStartedAt, 0);
            modifierDragSafetyTimer?.Dispose();
            modifierDragSafetyTimer = null;
        }
        finally { Monitor.Exit(OutputLock); }
    }
    static void ForceReleaseWithoutOutputLock()
    {
        foreach (byte key in new byte[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x14, 0x20 })
            keybd_event(key, 0, 2, (UIntPtr)Marker);
        mouse_event(4, 0, 0, 0, (UIntPtr)Marker);
        mouse_event(16, 0, 0, 0, (UIntPtr)Marker);
        mouse_event(64, 0, 0, 0, (UIntPtr)Marker);
        mouse_event(0x100, 0, 0, 1, (UIntPtr)Marker);
        mouse_event(0x100, 0, 0, 2, (UIntPtr)Marker);
        modifierDragKey = 0;
        modifierDragMouseDown = false;
        Interlocked.Exchange(ref modifierDragStartedAt, 0);
    }
}
