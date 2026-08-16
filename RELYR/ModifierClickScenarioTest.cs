using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace RELYR;

internal static class ModifierClickScenarioTest
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var originalCursor = System.Windows.Forms.Cursor.Position;
        var surface = new ModifierTestSurface();
        surface.Show();
        surface.Activate();
        surface.UpdateLayout();
        await Task.Delay(80);

        string action = "ShiftDrag";
        using var engine = new InputEngine
        {
            Enabled = true,
            ExitOnEmergency = false,
            HasMapping = input => input is "Space+MouseLeft" or "Space+*",
            IsNativeMouseDrag = input => input == "Space+MouseLeft"
        };
        using var queue = new BlockingCollection<string>();
        var worker = Task.Factory.StartNew(
            () =>
            {
                foreach (string input in queue.GetConsumingEnumerable())
                {
                    InputEngine.SendMouse(action + (input.EndsWith(":PressStart", StringComparison.OrdinalIgnoreCase) ? ":Start" : ":End"));
                    if (input.EndsWith(":PressStart", StringComparison.OrdinalIgnoreCase))
                        engine.NotifyNativeMouseDragStarted(input);
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        engine.InputReceived = input => input is "Space+MouseLeft:PressStart" or "Space+MouseLeft:PressEnd" && queue.TryAdd(input);
        engine.EnableDirectTestInput();

        try
        {
            async Task<bool> ModifiedClick(int slot, string modifierAction)
            {
                action = modifierAction;
                int downBefore = surface.DownCount;
                int upBefore = surface.UpCount;
                MoveTo(surface.PointForSlot(slot));
                engine.DirectKeyForTest(0x20, false);
                var down = engine.DirectMouseForTest(0x201);
                bool sawDown = await WaitUntilAsync(() => surface.DownCount > downBefore, 500);
                var up = engine.DirectMouseForTest(0x202);
                engine.DirectKeyForTest(0x20, true);
                bool sawUp = await WaitUntilAsync(() => surface.UpCount > upBefore, 500);
                return down == (IntPtr)1 && up == (IntPtr)1 && sawDown && sawUp;
            }

            async Task<bool> ModifiedDrag(string modifierAction, Vector delta)
            {
                action = modifierAction;
                int downBefore = surface.DownCount;
                int upBefore = surface.UpCount;
                int moveBefore = surface.ModifiedMoveCount;
                WpfPoint start = surface.PointForSlot(1);
                MoveTo(start);
                engine.DirectKeyForTest(0x20, false);
                var down = engine.DirectMouseForTest(0x201);
                bool sawDown = await WaitUntilAsync(() => surface.DownCount > downBefore, 500);
                MoveTo(start + delta);
                bool sawMove = await WaitUntilAsync(() => surface.ModifiedMoveCount > moveBefore, 500);
                var up = engine.DirectMouseForTest(0x202);
                engine.DirectKeyForTest(0x20, true);
                bool sawUp = await WaitUntilAsync(() => surface.UpCount > upBefore, 500);
                return down == (IntPtr)1 && up == (IntPtr)1 && sawDown && sawMove && sawUp;
            }

            async Task PlainBlankClick()
            {
                int upBefore = surface.UpCount;
                MoveTo(surface.BlankPoint);
                engine.DisableDirectTestInputForTest();
                InputEngine.InjectMouseForTest("Left", false);
                InputEngine.InjectMouseForTest("Left", true);
                await WaitUntilAsync(() => surface.UpCount > upBefore, 500);
                engine.EnableDirectTestInput();
            }

            surface.SetAnchor(0);
            bool shiftRangeInput = await ModifiedClick(2, "ShiftDrag");
            bool rangeSelected = surface.Selected.SetEquals([0, 1, 2]) && surface.LastDownModifiers.HasFlag(ModifierKeys.Shift) && surface.LastUpModifiers.HasFlag(ModifierKeys.Shift);
            await PlainBlankClick();
            check(shiftRangeInput && rangeSelected && surface.Selected.Count == 0 && surface.LastUpModifiers == ModifierKeys.None,
                "Shift click selects a range and the immediately following blank click clears it without a delay");

            surface.ClearSelection();
            bool ctrlFirst = await ModifiedClick(0, "CtrlDrag");
            bool ctrlSecond = await ModifiedClick(2, "CtrlDrag");
            bool multiSelected = surface.Selected.SetEquals([0, 2]) && surface.LastDownModifiers.HasFlag(ModifierKeys.Control) && surface.LastUpModifiers.HasFlag(ModifierKeys.Control);
            await PlainBlankClick();
            check(ctrlFirst && ctrlSecond && multiSelected && surface.Selected.Count == 0 && surface.LastUpModifiers == ModifierKeys.None,
                "Ctrl click selects multiple independent items and the immediately following blank click clears them");

            surface.ResetDragEvidence();
            bool horizontal = await ModifiedDrag("ShiftDrag", new Vector(70, 0));
            bool horizontalObserved = surface.ShiftDragObserved && Math.Abs(surface.LastDragDelta.X) > 30 && Math.Abs(surface.LastDragDelta.Y) < 8;
            surface.ResetDragEvidence();
            bool vertical = await ModifiedDrag("ShiftDrag", new Vector(0, 70));
            bool verticalObserved = surface.ShiftDragObserved && Math.Abs(surface.LastDragDelta.Y) > 30 && Math.Abs(surface.LastDragDelta.X) < 8;
            check(horizontal && vertical && horizontalObserved && verticalObserved,
                "Shift remains held through horizontal and vertical shape drags until mouse-up");

            surface.ResetDragEvidence();
            bool ctrlDrag = await ModifiedDrag("CtrlDrag", new Vector(70, 45));
            check(ctrlDrag && surface.CtrlDragCopyCount == 1 && surface.LastDownModifiers.HasFlag(ModifierKeys.Control) && surface.LastUpModifiers.HasFlag(ModifierKeys.Control),
                "Ctrl remains held through a shape drag and the target receives one copy-on-drop operation");
        }
        finally
        {
            engine.DisableDirectTestInputForTest();
            queue.CompleteAdding();
            await worker;
            InputEngine.EndModifierDrag();
            surface.Close();
            System.Windows.Forms.Cursor.Position = originalCursor;
        }
    }

    static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(8);
        }
        return condition();
    }

    static void MoveTo(WpfPoint screenPoint)
        => System.Windows.Forms.Cursor.Position = new System.Drawing.Point((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y));

    sealed class ModifierTestSurface : Window
    {
        readonly Border root;
        readonly HashSet<int> selected = [];
        WpfPoint dragStart;
        bool dragging;
        ModifierKeys dragModifiers;

        internal IReadOnlySet<int> Selected => selected;
        internal int DownCount { get; private set; }
        internal int UpCount { get; private set; }
        internal int ModifiedMoveCount { get; private set; }
        internal int CtrlDragCopyCount { get; private set; }
        internal bool ShiftDragObserved { get; private set; }
        internal Vector LastDragDelta { get; private set; }
        internal ModifierKeys LastDownModifiers { get; private set; }
        internal ModifierKeys LastUpModifiers { get; private set; }

        internal ModifierTestSurface()
        {
            Title = "RELYR modifier input verification";
            Width = 460;
            Height = 260;
            Left = Math.Max(SystemParameters.WorkArea.Left + 20, SystemParameters.WorkArea.Right - 500);
            Top = Math.Max(SystemParameters.WorkArea.Top + 20, SystemParameters.WorkArea.Bottom - 300);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Topmost = true;
            ShowInTaskbar = false;
            root = new Border { Background = System.Windows.Media.Brushes.White, Focusable = true };
            Content = root;
            root.PreviewMouseLeftButtonDown += SurfaceMouseDown;
            root.PreviewMouseMove += SurfaceMouseMove;
            root.PreviewMouseLeftButtonUp += SurfaceMouseUp;
        }

        internal WpfPoint PointForSlot(int slot) => root.PointToScreen(new WpfPoint(70 + slot * 120, 70));
        internal WpfPoint BlankPoint => root.PointToScreen(new WpfPoint(220, 185));
        internal void SetAnchor(int slot) { selected.Clear(); selected.Add(slot); }
        internal void ClearSelection() => selected.Clear();
        internal void ResetDragEvidence()
        {
            ModifiedMoveCount = 0;
            CtrlDragCopyCount = 0;
            ShiftDragObserved = false;
            LastDragDelta = default;
        }

        void SurfaceMouseDown(object sender, MouseButtonEventArgs e)
        {
            DownCount++;
            LastDownModifiers = Keyboard.Modifiers;
            WpfPoint point = e.GetPosition(root);
            int slot = SlotAt(point);
            if (slot < 0)
                selected.Clear();
            else if (LastDownModifiers.HasFlag(ModifierKeys.Shift))
            {
                int anchor = selected.Count == 0 ? slot : selected.Min();
                selected.Clear();
                for (int i = Math.Min(anchor, slot); i <= Math.Max(anchor, slot); i++)
                    selected.Add(i);
            }
            else if (LastDownModifiers.HasFlag(ModifierKeys.Control))
            {
                if (!selected.Add(slot)) selected.Remove(slot);
            }
            else
            {
                selected.Clear();
                selected.Add(slot);
            }
            dragStart = point;
            dragModifiers = LastDownModifiers;
            dragging = true;
            root.CaptureMouse();
        }

        void SurfaceMouseMove(object sender, WpfMouseEventArgs e)
        {
            if (!dragging || e.LeftButton != MouseButtonState.Pressed)
                return;
            LastDragDelta = e.GetPosition(root) - dragStart;
            if (LastDragDelta.Length < 8)
                return;
            if (dragModifiers.HasFlag(ModifierKeys.Shift) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                ShiftDragObserved = true;
            if (dragModifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                ModifiedMoveCount++;
            else if (dragModifiers.HasFlag(ModifierKeys.Shift))
                ModifiedMoveCount++;
        }

        void SurfaceMouseUp(object sender, MouseButtonEventArgs e)
        {
            UpCount++;
            LastUpModifiers = Keyboard.Modifiers;
            if (dragging && LastDragDelta.Length >= 8 && dragModifiers.HasFlag(ModifierKeys.Control) && LastUpModifiers.HasFlag(ModifierKeys.Control))
                CtrlDragCopyCount++;
            dragging = false;
            root.ReleaseMouseCapture();
        }

        static int SlotAt(WpfPoint point)
        {
            if (point.Y is < 25 or > 125)
                return -1;
            int slot = (int)((point.X - 20) / 120);
            return slot is >= 0 and <= 2 ? slot : -1;
        }
    }
}
