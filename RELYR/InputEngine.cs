using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace RELYR;

public sealed partial class InputEngine : IDisposable
{
    enum NativeRightDragCommand { Start, End }
    const int GestureIdleSafetyMs = 30000;
    const int GestureStopDelayMs = 110;
    static readonly BlockingCollection<Action> DesktopActions = [];
    static InputEngine? directTestTarget;
    static readonly object OutputLock = new();
    static readonly Lock CoordinateCaptureLock = new();
    static Action<int, int>? pendingCoordinateCapture;
    static bool suppressCoordinateCaptureLeftUp;
    static readonly HashSet<ushort> InjectedKeysDown = [];
    static readonly HashSet<int> InjectedMouseButtonsDown = [];
    static readonly Dictionary<ushort, long> InjectedKeyDownAt = [];
    static readonly Dictionary<int, long> InjectedMouseDownAt = [];
    static readonly System.Threading.Timer InjectedInputSafetyTimer = new(_ => ReleaseStaleInjectedInputs(), null, 250, 250);
    static bool restoreMinimizedWindowsNext;
    static ushort modifierDragKey;
    static bool modifierDragMouseDown;
    static long modifierDragStartedAt;
    static System.Threading.Timer? modifierDragSafetyTimer;
    // GetAsyncKeyState includes SendInput-generated button state.  It therefore
    // cannot tell a real held button from RELYR's own synthetic drag.  Keep the
    // unmarked low-level-hook transitions separately so a lost action-end can
    // never leave the desktop in a permanent left-button-down state.
    static int physicalMouseButtonsDownMask;
    internal static Action<uint, uint>? MouseFlagOutputForTest = null;
    internal static Action<(uint Flag, uint Data)[]>? MouseClickBatchOutputForTest = null;
    internal static Func<ushort, bool, bool>? KeyOutputForTest = null;
    internal static Func<int, bool>? PhysicalKeyDownForTest = null;
    internal static Action<string>? UnicodeTextOutputForTest = null;
    internal static Action<int>? ImeActionOutputForTest = null;
    internal static Action<Action>? DesktopActionOutputForTest = null;
    internal static Func<bool>? LockWorkStationOutputForTest = null;
    internal static Action? ShowRelyrMainWindowOutputForTest = null;
    internal Func<int, IntPtr, IntPtr, IntPtr>? NextHookForTest
    {
        get; set;
    }
    static long lastWheelOutput;
    public static Action<string>? DesktopActionFailed;
    static InputEngine() => _ = Task.Run(() => { foreach (var action in DesktopActions.GetConsumingEnumerable()) { try { action(); } catch (Exception ex) { DesktopActionFailed?.Invoke(ex.Message); } } });
    const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14, WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    const uint WM_QUIT = 0x0012, WM_RUN_HOOK_TEST = 0x8001;
    const uint Marker = 0x1C0570;
    IntPtr keyboardHook, mouseHook;
    Thread? hookThread;
    uint hookThreadId;
    volatile bool rawInputMonitorStarted;
#if HOOK_DIAGNOSTICS
    readonly System.Threading.Timer hookDiagnosticsHeartbeatTimer;
#endif
    long rawKeyboardTransitions, lowLevelKeyboardTransitions;
    long rawMouseTransitions, lowLevelMouseTransitions;
    readonly int[] lowLevelMouseUpsPendingRaw = new int[6];
    readonly int[] rawMouseUpsAwaitingLowLevel = new int[6];
    readonly ManualResetEventSlim hookReady = new(false);
    readonly AutoResetEvent hookTestCompleted = new(false);
    Exception? hookStartException;
    Exception? hookTestException;
    bool hookTestStateClean;
    bool disposed;
    readonly HookProc keyboardProc, mouseProc;
    readonly Lock stateLock = new();
    readonly HashSet<int> held = [];
    readonly Dictionary<string, PressState> presses = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> committedGestureSources = new(StringComparer.OrdinalIgnoreCase);
    string? deferredLayer;
    System.Threading.Timer? layerSafetyTimer;
    volatile bool layerSafetyExpired;
    bool layerUsed;
    int mouseLayerStartX, mouseLayerStartY;
    bool nativeRightLayerDrag;
    bool nativeRightLayerDragStarting;
    readonly BlockingCollection<NativeRightDragCommand> nativeRightDragOutputQueue = [];
    readonly Task nativeRightDragOutputWorker;
    System.Threading.Timer? nativeRightDragSafetyTimer;
    System.Threading.Timer? layerRepeatTimer; volatile bool layerRepeatActive;
    long lastSpaceTapTick;
    int layerRepeatGeneration;
    long lastRecordedMove;
    long mousePassthroughUntil;
    bool enabled = true;
    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value)
                return;
            enabled = value;
            if (!value)
                ResetCapturedState(true, true);
        }
    }
    public bool CaptureMouseMoves
    {
        get; set;
    }
    // A second hook can coexist with the normal UI hook when it is scoped to a
    // different integrity boundary. Returning false leaves the event entirely
    // untouched so another hook (or Windows) can handle it.
    public Func<bool>? ShouldInterceptInput
    {
        get; set;
    }
    // Text editors need ordinary keyboard input, but a mouse button can still
    // be the source of a layer chord (for example MouseRight+Space). Keep the
    // mouse policy independent; callers that do not set it retain the legacy
    // shared policy through the fallback in MouseCallbackCore.
    public Func<bool>? ShouldInterceptMouseInput
    {
        get; set;
    }
    public int DragPixels { get; set; } = 6;
    public int GestureThresholdPixels { get; set; } = 12;
    public bool LockCursorDuringGesture { get; set; } = true;
    internal (int X, int Y)? GestureCursorForTest
    {
        get; set;
    }
    public Func<string, string>? QualifyInput
    {
        get; set;
    }
    public Func<string, bool>? HasMapping
    {
        get; set;
    }
    public Func<string, bool>? IsNativeMouseDrag
    {
        get; set;
    }
    public Func<string, bool>? HasLegacyMouseDrag
    {
        get; set;
    }
    public Func<string, bool>? SuppressLayerTap
    {
        get; set;
    }
    public bool UseUsLayout
    {
        get; set;
    }
    public bool TreatF13AsCapsLock
    {
        get; set;
    }
    public bool SpaceHoldRepeatEnabled { get; set; } = true;
    public bool ExitOnEmergency { get; set; } = true;
    public int SpaceHoldRepeatDelayMs { get; set; } = 400;
    public int SpaceHoldRepeatIntervalMs { get; set; } = 55;
    internal Action? RepeatOutputForTest
    {
        get; set;
    }
    public Func<string, bool>? InputReceived
    {
        get; set;
    }
    public Func<string, int>? LongPressDuration
    {
        get; set;
    }
    public Func<string, bool>? HasLongPress
    {
        get; set;
    }
    public Func<string, bool>? IsGesturePress
    {
        get; set;
    }
    public Func<string, bool>? IsGestureLongPress
    {
        get; set;
    }
    public Action<string>? InputStarted
    {
        get; set;
    }
    public Action<string>? InputEnded
    {
        get; set;
    }
    public Action<string>? LayerStarted
    {
        get; set;
    }
    public Action<string>? LayerEnded
    {
        get; set;
    }
    public event Action<string>? Detected;
    public event Action? PointerMoved;

    public InputEngine()
    {
        keyboardProc = KeyboardCallback;
        mouseProc = MouseCallback;
#if HOOK_DIAGNOSTICS
        hookDiagnosticsHeartbeatTimer = new(_ => RecordHookDiagnosticsHeartbeat(), null, 1000, 1000);
#endif
        nativeRightDragOutputWorker = Task.Factory.StartNew(ProcessNativeRightDragOutput, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public void Start()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(InputEngine));
        if (hookThread != null)
            return;
        // 起動に使ったクリックの Down/Up の途中からフックしない。
        // 起動直後だけ物理マウス入力をそのまま通し、押下状態の食い違いを防ぐ。
        mousePassthroughUntil = Environment.TickCount64 + 500;
        hookStartException = null;
        hookReady.Reset();
        hookThread = new Thread(HookLoop) { IsBackground = true, Name = "RELYR input hook" };
        hookThread.SetApartmentState(ApartmentState.STA);
        hookThread.Start();
        if (!hookReady.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("入力フックの開始が5秒以内に完了しませんでした。");
        if (hookStartException != null)
            throw new InvalidOperationException("入力フックを開始できませんでした。", hookStartException);
    }

    void HookLoop()
    {
        hookThreadId = GetCurrentThreadId();
        HookDiagnosticsTrace.Record(HookDiagnosticStage.HookThreadStarted, value1: hookThreadId);
        HwndSource? rawInputSource = null;
        try
        {
            IntPtr module = GetModuleHandle(null);
            keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, module, 0);
            int keyboardError = keyboardHook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
            HookDiagnosticsTrace.Record(HookDiagnosticStage.KeyboardHookRegistered, keyboardHook: keyboardHook, mouseHook: mouseHook, replacementHook: keyboardHook, result: keyboardHook != IntPtr.Zero ? 1 : 0, win32Error: keyboardError);
            mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, module, 0);
            int mouseError = mouseHook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
            HookDiagnosticsTrace.Record(HookDiagnosticStage.MouseHookRegistered, keyboardHook: keyboardHook, mouseHook: mouseHook, replacementHook: mouseHook, result: mouseHook != IntPtr.Zero ? 1 : 0, win32Error: mouseError);
            if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            // Raw Input is independent from the low-level hook chain. It supplies an
            // authoritative physical Up when a desktop/integrity transition prevents
            // one hook instance from seeing the matching WH_MOUSE_LL release.
            rawInputSource = CreateRawMouseInputSource();
            rawInputMonitorStarted = true;
            HookDiagnosticsTrace.Record(HookDiagnosticStage.RawInputMonitorStarted, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: rawInputSource.Handle.ToInt64(), result: 1);
            hookReady.Set();
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.message != WM_RUN_HOOK_TEST)
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                    continue;
                }
                try
                {
                    RunHookTestSequence();
                }
                catch (Exception ex) { hookTestException = ex; }
                finally { hookTestCompleted.Set(); }
            }
        }
        catch (Exception ex)
        {
            HookDiagnosticsTrace.Record(HookDiagnosticStage.HookThreadFault, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: ex.HResult, value2: HookDiagnosticsTrace.ExceptionCode(ex), result: 0, win32Error: Marshal.GetLastWin32Error());
            hookStartException = ex;
            hookReady.Set();
        }
        finally
        {
            HookDiagnosticsTrace.Record(HookDiagnosticStage.HookThreadStopping, keyboardHook: keyboardHook, mouseHook: mouseHook);
            rawInputMonitorStarted = false;
            rawInputSource?.Dispose();
            if (keyboardHook != IntPtr.Zero)
            {
                IntPtr previous = keyboardHook;
                bool unhooked = UnhookWindowsHookEx(previous);
                int error = unhooked ? 0 : Marshal.GetLastWin32Error();
                HookDiagnosticsTrace.Record(HookDiagnosticStage.KeyboardHookFinalUnhook, keyboardHook: keyboardHook, mouseHook: mouseHook, previousHook: previous, result: unhooked ? 1 : 0, win32Error: error);
                keyboardHook = IntPtr.Zero;
            }
            if (mouseHook != IntPtr.Zero)
            {
                IntPtr previous = mouseHook;
                bool unhooked = UnhookWindowsHookEx(previous);
                int error = unhooked ? 0 : Marshal.GetLastWin32Error();
                HookDiagnosticsTrace.Record(HookDiagnosticStage.MouseHookFinalUnhook, keyboardHook: keyboardHook, mouseHook: mouseHook, previousHook: previous, result: unhooked ? 1 : 0, win32Error: error);
                mouseHook = IntPtr.Zero;
            }
            hookThreadId = 0;
            HookDiagnosticsTrace.Record(HookDiagnosticStage.HookThreadStopped);
        }
    }

    internal void RunHookThreadSequenceForTest()
    {
        uint threadId = hookThreadId;
        if (threadId == 0)
            throw new InvalidOperationException("入力フックスレッドが開始されていません。");
        hookTestException = null;
        hookTestStateClean = false;
        if (!PostThreadMessage(threadId, WM_RUN_HOOK_TEST, UIntPtr.Zero, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (!hookTestCompleted.WaitOne(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("入力フックスレッド試験が5秒以内に完了しませんでした。");
        if (hookTestException != null)
            throw new InvalidOperationException("入力フックスレッド試験に失敗しました。", hookTestException);
    }

    void RunHookTestSequence()
    {
        foreach (var item in new[] { ("F13", false), ("U", false), ("U", true), ("U", false), ("U", true), ("F13", true), ("Space", false), ("J", false), ("J", true), ("Space", true) })
        {
            DirectKey(ParseKey(item.Item1), item.Item2);
            if (!item.Item2 && (item.Item1 == "F13" || item.Item1 == "Space"))
                Thread.Sleep(120);
        }
        DirectMouse(0x204);
        Thread.Sleep(120);
        DirectMouse(0x20A, 120 << 16);
        DirectMouse(0x205);
        hookTestStateClean = deferredLayer == null && presses.Count == 0 && held.Count == 0;
    }

    IntPtr KeyboardCallback(int n, IntPtr w, IntPtr l)
    {
#if HOOK_DIAGNOSTICS
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        HookDiagnosticsTrace.EnterHookCallback();
        HookDiagnosticsTrace.Record(HookDiagnosticStage.KeyboardCallbackEnter, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: n, value2: w.ToInt64());
#endif
        try
        {
            lock (stateLock)
            {
                HookDiagnosticsTrace.Record(HookDiagnosticStage.KeyboardStateLockAcquired, keyboardHook: keyboardHook, mouseHook: mouseHook);
                return KeyboardCallbackCore(n, w, l);
            }
        }
        catch
        {
            HookDiagnosticsTrace.Record(HookDiagnosticStage.KeyboardCallbackFault, keyboardHook: keyboardHook, mouseHook: mouseHook);
            return RecoverFromHookFault(n, w, l);
        }
#if HOOK_DIAGNOSTICS
        finally
        {
            HookDiagnosticsTrace.Record(HookDiagnosticStage.KeyboardCallbackExit, System.Diagnostics.Stopwatch.GetTimestamp() - started, keyboardHook, mouseHook);
            HookDiagnosticsTrace.ExitHookCallback();
        }
#endif
    }

    IntPtr KeyboardCallbackCore(int n, IntPtr w, IntPtr l)
    {
        if (n < 0)
            return Next(n, w, l);
        bool intercept = ShouldInterceptInput?.Invoke() ?? true;
        HookDiagnosticsTrace.Record(HookDiagnosticStage.KeyboardInterceptEvaluated, keyboardHook: keyboardHook, mouseHook: mouseHook, result: intercept ? 1 : 0);
        if (!intercept)
        {
            return Next(n, w, l);
        }
        var d = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(l);
        HookDiagnosticsTrace.Record(HookDiagnosticStage.KeyboardCallbackDecoded, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: d.vkCode, value2: unchecked((long)d.dwExtraInfo.ToUInt64()), result: unchecked((int)d.flags));
        // 自分で生成した入力は再マッピングしないが、Windowsと後続フックへは必ず渡す。
        if (d.dwExtraInfo == (UIntPtr)Marker)
        {
            HookDiagnosticsTrace.Record(HookDiagnosticStage.KeyboardGeneratedMarker, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: d.vkCode, value2: w.ToInt64(), result: unchecked((int)d.flags));
            return Next(n, w, l);
        }
        bool down = w == (IntPtr)WM_KEYDOWN || w == (IntPtr)WM_SYSKEYDOWN, up = w == (IntPtr)WM_KEYUP || w == (IntPtr)WM_SYSKEYUP;
        if (!down && !up)
            return Next(n, w, l);
        Interlocked.Increment(ref lowLevelKeyboardTransitions);
        if (OverlayService.FullScreenVisible)
        {
            ResetCapturedState(false, true);
            OverlayService.TryDismissFullScreenKeyboard(down);
            return (IntPtr)1;
        }
        int vk = (int)d.vkCode;
        string key = HookKeyName(vk, d.scanCode, UseUsLayout, TreatF13AsCapsLock);
        bool currentLayerRelease = deferredLayer != null && up && (key.Equals(deferredLayer, StringComparison.OrdinalIgnoreCase) || IsLayerRelease(deferredLayer, vk, d.scanCode, true));
        if (!currentLayerRelease)
        {
            if (layerSafetyExpired)
                ResetCapturedState(false);
        }
        if (down)
            held.Add(vk);
        else
            held.Remove(vk);
        if (down && EmergencyHeld(vk))
        {
            StopAndRelease();
            Detected?.Invoke("緊急停止");
            return (IntPtr)1;
        }
        Detected?.Invoke($"{key} {(down ? "Down" : "Up")}");
        if (!Enabled)
        {
            if (up)
                committedGestureSources.Remove(key);
            return Next(n, w, l);
        }
        if (nativeRightLayerDrag)
        {
            string rightLayerInput = "MouseRight+" + key;
            if (down && HasMapping?.Invoke(rightLayerInput) == true)
                EndNativeRightDragForMappedChord();
            else
                return Next(n, w, l);
        }
        // A gesture assigned directly to a layer-capable key owns that physical
        // press. This must run before the key is deferred as a layer source.
        if (deferredLayer is null && down && HasMapping?.Invoke(key) == true && IsGesturePress?.Invoke(key) == true)
            return ProcessPress(key, true, false, n, w, l);

        bool reliableCapsLayer = key == "CapsLock" && TreatF13AsCapsLock && vk == 0x7C;
#if HOOK_DIAGNOSTICS
        long layerLookupStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        HookDiagnosticsTrace.Record(HookDiagnosticStage.KeyboardLayerLookupStarted, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: vk, result: down ? 1 : 0);
#endif
        bool hasKeyboardLayerMappings = down && key != "CapsLock" && HasLayerMappings(key);
#if HOOK_DIAGNOSTICS
        HookDiagnosticsTrace.Record(HookDiagnosticStage.KeyboardLayerLookupCompleted, System.Diagnostics.Stopwatch.GetTimestamp() - layerLookupStarted, keyboardHook, mouseHook, value1: vk, result: hasKeyboardLayerMappings ? 1 : 0);
#endif
        if (deferredLayer is null && down && (hasKeyboardLayerMappings || reliableCapsLayer || (key == "Space" && SpaceHoldRepeatEnabled)))
        {
            deferredLayer = key;
            HookDiagnosticsTrace.Record(HookDiagnosticStage.KeyboardLayerStarted, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: vk);
            LayerStarted?.Invoke(key);
            ArmLayerSafety();
            layerUsed = false;
            layerRepeatActive = false;
            if (key == "Space" && SpaceHoldRepeatEnabled && Environment.TickCount64 - lastSpaceTapTick <= 500)
            {
                lastSpaceTapTick = 0;
                int generation = Interlocked.Increment(ref layerRepeatGeneration);
                int interval = Math.Clamp(SpaceHoldRepeatIntervalMs, 25, 500);
                layerRepeatTimer = new System.Threading.Timer(_ =>
                {
                    bool send = false;
                    lock (stateLock)
                    {
                        if (generation == Volatile.Read(ref layerRepeatGeneration) && deferredLayer == "Space" && !layerUsed)
                        {
                            layerRepeatActive = true;
                            send = true;
                            layerRepeatTimer?.Change(interval, interval);
                        }
                    }
                    if (send)
                        SendSpaceRepeat();
                }, null, Math.Clamp(SpaceHoldRepeatDelayMs, 100, 2000), System.Threading.Timeout.Infinite);
            }
            return (IntPtr)1;
        }
        if (deferredLayer != null && (key.Equals(deferredLayer, StringComparison.OrdinalIgnoreCase) || IsLayerRelease(deferredLayer, vk, d.scanCode, up)))
        {
            if (up)
                EndDeferredKeyboardLayer(deferredLayer);
            return (IntPtr)1;
        }
        bool layerChord = deferredLayer != null && !layerRepeatActive;
        string? pendingInput = PendingLayerInput(key);
        string input = pendingInput ?? (layerChord ? deferredLayer + "+" + key : QualifyInput?.Invoke(key) ?? key);
        if (deferredLayer != null && !layerRepeatActive && down && HasMapping?.Invoke(input) == true)
        {
            layerUsed = true;
            if (deferredLayer == "Space")
                lastSpaceTapTick = 0;
            CancelLayerRepeat();
        }
        bool keyboardLayer = deferredLayer is "Space" or "CapsLock";
        if (layerChord && down && !keyboardLayer)
            CancelOtherLayerPresses(input);
        bool fireLayerActionOnDown = layerChord && keyboardLayer && (pendingInput == null || input.StartsWith(deferredLayer + "+", StringComparison.OrdinalIgnoreCase));
        // Space/CapsLock はキーリピートを利用する。マウス側面レイヤーでは、
        // チルト等が生成したキーを解放時に1回だけ確定し、別操作が始まれば取り消す。
        bool repeatLayerAction = fireLayerActionOnDown;
        return ProcessPress(input, down, up, n, w, l, fireOnDown: fireLayerActionOnDown, repeatWhileHeld: repeatLayerAction);
    }

    void EndDeferredKeyboardLayer(string releasedLayer)
    {
        bool repeated = layerRepeatActive;
        EndImmediateLayerPresses(releasedLayer);
        CancelLayerRepeat();
        CancelLayerSafety();
        if (!layerUsed && !repeated && SuppressLayerTap?.Invoke(releasedLayer) != true)
        {
            if (HasMapping?.Invoke(releasedLayer) == true)
                InputReceived?.Invoke(releasedLayer);
            else
                _ = Task.Run(() => SendShortcut(releasedLayer));
            if (releasedLayer == "Space")
                lastSpaceTapTick = Environment.TickCount64;
        }
        deferredLayer = null;
        layerUsed = false;
        LayerEnded?.Invoke(releasedLayer);
    }

    IntPtr MouseCallback(int n, IntPtr w, IntPtr l)
    {
#if HOOK_DIAGNOSTICS
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        bool traceTransition = w.ToInt32() != 0x200;
        HookDiagnosticsTrace.EnterHookCallback();
        if (traceTransition)
            HookDiagnosticsTrace.Record(HookDiagnosticStage.MouseCallbackEnter, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: n, value2: w.ToInt64());
#endif
        try
        {
            lock (stateLock)
            {
                HookDiagnosticsTrace.Record(HookDiagnosticStage.MouseStateLockAcquired, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: w.ToInt64());
                return MouseCallbackCore(n, w, l);
            }
        }
        catch
        {
            HookDiagnosticsTrace.Record(HookDiagnosticStage.MouseCallbackFault, keyboardHook: keyboardHook, mouseHook: mouseHook);
            return RecoverFromHookFault(n, w, l);
        }
#if HOOK_DIAGNOSTICS
        finally
        {
            if (traceTransition)
                HookDiagnosticsTrace.Record(HookDiagnosticStage.MouseCallbackExit, System.Diagnostics.Stopwatch.GetTimestamp() - started, keyboardHook, mouseHook);
            HookDiagnosticsTrace.ExitHookCallback();
        }
#endif
    }

    IntPtr RecoverFromHookFault(int n, IntPtr w, IntPtr l)
    {
        // A hook callback must never let one malformed input or consumer
        // exception retire the whole hook thread. Drop only RELYR's captured
        // state, release any generated buttons, then let Windows receive the
        // physical input normally.
        try
        {
            ResetCapturedState(false, true);
        }
        catch { }
        try
        {
            Detected?.Invoke("Input hook recovered");
        }
        catch { }
        return Next(n, w, l);
    }

    IntPtr MouseCallbackCore(int n, IntPtr w, IntPtr l)
    {
        if (n < 0)
            return Next(n, w, l);
        var interceptPolicy = ShouldInterceptMouseInput ?? ShouldInterceptInput;
        bool intercept = interceptPolicy?.Invoke() ?? true;
        HookDiagnosticsTrace.Record(HookDiagnosticStage.MouseInterceptEvaluated, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: w.ToInt64(), result: intercept ? 1 : 0);
        if (!intercept)
        {
            return Next(n, w, l);
        }
        var d = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(l);
        int msg = w.ToInt32();
        if (d.dwExtraInfo == (UIntPtr)Marker)
        {
            HookDiagnosticsTrace.Record(HookDiagnosticStage.MouseGeneratedMarker, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: msg, value2: d.mouseData);
            return Next(n, w, l);
        }
        if (msg is 0x201 or 0x202 or 0x204 or 0x205 or 0x207 or 0x208 or 0x20B or 0x20C)
            Interlocked.Increment(ref lowLevelMouseTransitions);
        if (ObservePhysicalMouseTransition(msg, d.mouseData))
            return (IntPtr)1;
        if (OverlayService.FullScreenVisible)
        {
            ResetCapturedState(false, true);
            OverlayService.TryDismissFullScreenMouse(msg, d.pt.x, d.pt.y);
            return (IntPtr)1;
        }
        if (TryHandleCoordinateCapture(msg, d.pt.x, d.pt.y))
            return (IntPtr)1;
        if (Environment.TickCount64 < mousePassthroughUntil)
            return Next(n, w, l);
        if (msg == 0x200)
        {
            // Keep the hook callback lightweight: consumers only enqueue work and
            // resolve the application under the pointer later on the UI thread.
            PointerMoved?.Invoke();
            // Modifier-click Start is emitted by a dedicated worker so the
            // low-level hook never calls SendInput while holding stateLock.
            // Do not let an early physical move reach Office before that worker
            // has established Ctrl/Shift + synthetic LeftDown.
            if (presses.Values.Any(state => state.IsDown && state.NativeMouseDrag && !state.NativeMouseDragReady))
                return (IntPtr)1;
            if (CaptureMouseMoves && Environment.TickCount64 - lastRecordedMove >= 50)
            {
                lastRecordedMove = Environment.TickCount64;
                Detected?.Invoke($"MouseMove:{d.pt.x},{d.pt.y}");
            }
            var gesture = presses.FirstOrDefault(x => x.Value.IsDown && x.Value.GestureActive && !x.Value.GestureExpired);
            if (gesture.Value != null)
            {
                var state = gesture.Value;
                RefreshGestureSafety(state);
                int eventDx, eventDy;
                if (LockCursorDuringGesture)
                {
                    eventDx = d.pt.x - state.GestureCursorX;
                    eventDy = d.pt.y - state.GestureCursorY;
                }
                else
                {
                    eventDx = d.pt.x - state.GestureLastX;
                    eventDy = d.pt.y - state.GestureLastY;
                    state.GestureLastX = d.pt.x;
                    state.GestureLastY = d.pt.y;
                }
                state.GestureDx += eventDx;
                state.GestureDy += eventDy;
                state.GestureMotionTimer ??= new System.Threading.Timer(_ =>
                {
                    lock (stateLock)
                        if (state.IsDown && state.GestureActive && !state.GestureExpired)
                            CommitGestureMovement(gesture.Key, state);
                });
                // マウスが止まるまで確定を延ばし、1回の連続移動を1ジェスチャーとして扱う。
                state.GestureMotionTimer.Change(GestureStopDelayMs, System.Threading.Timeout.Infinite);
                return LockCursorDuringGesture ? (IntPtr)1 : Next(n, w, l);
            }
            if (Enabled && deferredLayer == "MouseRight" && !layerUsed && !nativeRightLayerDrag && !nativeRightLayerDragStarting
                && Distance(mouseLayerStartX, mouseLayerStartY, d.pt.x, d.pt.y) >= DragPixels)
                QueueNativeRightDragStart();
            // RightDown is emitted by a serial output worker. Do not let the
            // triggering movement overtake it and reach the target first.
            if (nativeRightLayerDragStarting)
                return (IntPtr)1;
            // Pointer movement must not turn an ordinary layer+click assignment into a
            // legacy drag event. Only old mappings that explicitly contain drag actions
            // use the distance based DragStart/DragEnd path. Modifier-click actions use
            // their dedicated PressStart/PressEnd lifecycle instead.
            foreach (var pair in presses.Where(x => x.Key.Contains("Mouse", StringComparison.OrdinalIgnoreCase) && x.Value.IsDown && !x.Value.NativeMouseDrag && !x.Value.IsGesture && HasLegacyMouseDrag?.Invoke(x.Key) == true).ToArray())
                if (Distance(pair.Value.X, pair.Value.Y, d.pt.x, d.pt.y) >= DragPixels && !pair.Value.Dragged)
                {
                    pair.Value.Dragged = true;
                    if (!pair.Value.Immediate)
                        InputReceived?.Invoke(pair.Key + ":DragStart");
                    Detected?.Invoke(pair.Key + " Drag");
                }
            return Next(n, w, l);
        }
        string? name = msg switch
        {
            0x201 or 0x202 => "MouseLeft",
            0x204 or 0x205 => "MouseRight",
            0x207 or 0x208 => "MouseMiddle",
            0x20B or 0x20C => ((d.mouseData >> 16) & 0xffff) == 1 ? "MouseBack" : "MouseForward",
            0x20A => d.mouseData > 0 ? "WheelUp" : "WheelDown",
            0x20E => d.mouseData > 0 ? "TiltRight" : "TiltLeft",
            _ => null
        };
        if (name == null)
            return Next(n, w, l);
        bool buttonDown = msg is 0x201 or 0x204 or 0x207 or 0x20B, buttonUp = msg is 0x202 or 0x205 or 0x208 or 0x20C;
        bool rawDown = buttonDown || msg is 0x20A or 0x20E, rawUp = buttonUp;
        bool currentLayerRelease = deferredLayer != null && buttonUp && name.Equals(deferredLayer, StringComparison.OrdinalIgnoreCase);
        if (!currentLayerRelease)
        {
            if (layerSafetyExpired)
                ResetCapturedState(false);
        }
        if (!Enabled)
        {
            if (rawUp)
                committedGestureSources.Remove(name);
            Detected?.Invoke(name + (rawDown ? " Down" : rawUp ? " Up" : ""));
            return Next(n, w, l);
        }
        if ((nativeRightLayerDrag || nativeRightLayerDragStarting) && !currentLayerRelease)
        {
            string rightLayerInput = "MouseRight+" + name;
            if (rawDown && HasMapping?.Invoke(rightLayerInput) == true)
                EndNativeRightDragForMappedChord();
            else
            {
                Detected?.Invoke(name + (rawDown ? " Down" : rawUp ? " Up" : ""));
                return Next(n, w, l);
            }
        }
        // A taskbar-specific button mapping must win over a mouse layer with the
        // same physical button. Otherwise Taskbar+MouseRight long-press is
        // captured as the MouseRight layer before its own timer can start.
        string directInput = QualifyInput?.Invoke(name) ?? name;
        if (deferredLayer is null && buttonDown
            && !directInput.Equals(name, StringComparison.OrdinalIgnoreCase)
            && HasMapping?.Invoke(directInput) == true)
        {
            Detected?.Invoke(directInput + " Down");
            return ProcessPress(directInput, true, false, n, w, l, d.pt.x, d.pt.y);
        }
        // A gesture on a normal mouse button takes precedence over using the same
        // button as a layer source, because both interactions begin with a hold.
        if (deferredLayer is null && buttonDown && HasMapping?.Invoke(name) == true && IsGesturePress?.Invoke(name) == true)
        {
            Detected?.Invoke(name + " Down");
            return ProcessPress(name, true, false, n, w, l, d.pt.x, d.pt.y);
        }
        // MouseLeft is the system's primary click and is never a layer source.
        // Reject stale or hand-edited MouseLeft+... mappings here as a final
        // runtime guard so one malformed assignment cannot swallow every click.
#if HOOK_DIAGNOSTICS
        long mouseLayerLookupStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        HookDiagnosticsTrace.Record(HookDiagnosticStage.MouseLayerLookupStarted, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: msg, value2: d.mouseData, result: buttonDown ? 1 : 0);
#endif
        bool hasMouseLayerMappings = buttonDown && !name.Equals("MouseLeft", StringComparison.OrdinalIgnoreCase) && HasLayerMappings(name);
#if HOOK_DIAGNOSTICS
        HookDiagnosticsTrace.Record(HookDiagnosticStage.MouseLayerLookupCompleted, System.Diagnostics.Stopwatch.GetTimestamp() - mouseLayerLookupStarted, keyboardHook, mouseHook, value1: msg, value2: d.mouseData, result: hasMouseLayerMappings ? 1 : 0);
#endif
        if (deferredLayer is null && buttonDown
            && !name.Equals("MouseLeft", StringComparison.OrdinalIgnoreCase)
            && hasMouseLayerMappings)
        {
            deferredLayer = name;
            HookDiagnosticsTrace.Record(HookDiagnosticStage.MouseLayerStarted, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: msg, value2: d.mouseData);
            LayerStarted?.Invoke(name);
            mouseLayerStartX = d.pt.x;
            mouseLayerStartY = d.pt.y;
            nativeRightLayerDrag = false;
            nativeRightLayerDragStarting = false;
            ArmLayerSafety();
            layerUsed = false;
            Detected?.Invoke(name + " Layer Down");
            return (IntPtr)1;
        }
        if (deferredLayer != null && name.Equals(deferredLayer, StringComparison.OrdinalIgnoreCase) && buttonUp)
        {
            EndDeferredMouseLayer(name);
            return (IntPtr)1;
        }
        if (buttonUp)
            name = PendingLayerInput(name) ?? (deferredLayer != null && !layerRepeatActive ? deferredLayer + "+" + name : QualifyInput?.Invoke(name) ?? name);
        else if (deferredLayer != null && !layerRepeatActive)
            name = deferredLayer + "+" + name;
        else
            name = QualifyInput?.Invoke(name) ?? name;
        bool down = buttonDown || msg is 0x20A or 0x20E, up = buttonUp;
        if (deferredLayer != null && !layerRepeatActive && down)
            CancelOtherLayerPresses(name);
        if (deferredLayer != null && !layerRepeatActive && down && HasMapping?.Invoke(name) == true)
        {
            layerUsed = true;
            if (deferredLayer == "Space")
                lastSpaceTapTick = 0;
            CancelLayerRepeat();
        }
        Detected?.Invoke(name + (down ? " Down" : " Up"));
        if (down && !up && msg is 0x20A or 0x20E)
        {
            short delta = (short)((uint)d.mouseData >> 16);
            int steps = Math.Max(1, Math.Abs((int)delta) / 120);
            bool handled = false;
            for (int i = 0; i < steps; i++)
                handled |= InputReceived?.Invoke(name) == true;
            return handled ? (IntPtr)1 : Next(n, w, l);
        }
        return ProcessPress(name, down, up, n, w, l, d.pt.x, d.pt.y);
    }

    void EndDeferredMouseLayer(string releasedLayer)
    {
        bool used = layerUsed, nativeDrag = nativeRightLayerDrag || nativeRightLayerDragStarting;
        Detected?.Invoke(releasedLayer + " Layer Up");
        EndImmediateLayerPresses(releasedLayer);
        CancelLayerSafety();
        CancelNativeRightDragSafety();
        deferredLayer = null;
        layerUsed = false;
        nativeRightLayerDrag = false;
        nativeRightLayerDragStarting = false;
        if (nativeDrag)
            QueueNativeRightDragEnd();
        else if (used)
            QueueMouseLayerRelease(releasedLayer);
        else if (HasMapping?.Invoke(releasedLayer) == true)
        {
            QueueMouseLayerRelease(releasedLayer);
            InputReceived?.Invoke(releasedLayer);
        }
        else
            _ = Task.Run(() => SendMouseClickAtomic(releasedLayer));
        LayerEnded?.Invoke(releasedLayer);
    }

    void ObserveRawMouseButtonDown(string physicalInput)
    {
        int button = MouseButtonNumber(physicalInput);
        if (button == 0)
            return;
        Interlocked.Or(ref physicalMouseButtonsDownMask, 1 << (button - 1));
        lock (stateLock)
        {
            lowLevelMouseUpsPendingRaw[button] = 0;
            rawMouseUpsAwaitingLowLevel[button] = 0;
        }
    }

    void ReconcileRawMouseButtonUp(string physicalInput)
    {
        int rawButton = MouseButtonNumber(physicalInput);
        HookDiagnosticsTrace.Record(HookDiagnosticStage.RawReconcileStarted, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: rawButton);
        if (rawButton != 0)
            Interlocked.And(ref physicalMouseButtonsDownMask, ~(1 << (rawButton - 1)));
        lock (stateLock)
        {
            int button = rawButton;
            if (button != 0 && lowLevelMouseUpsPendingRaw[button] > 0)
            {
                lowLevelMouseUpsPendingRaw[button]--;
                HookDiagnosticsTrace.Record(HookDiagnosticStage.RawReconcileLowLevelMatched, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: button);
                return;
            }
            if (deferredLayer != null && deferredLayer.Equals(physicalInput, StringComparison.OrdinalIgnoreCase))
            {
                Detected?.Invoke(physicalInput + " Raw Release Recovery");
                HookDiagnosticsTrace.Record(HookDiagnosticStage.RawReconcileLayerEndStarted, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: button);
                EndDeferredMouseLayer(deferredLayer);
                HookDiagnosticsTrace.Record(HookDiagnosticStage.RawReconcileLayerEndCompleted, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: button);
                if (button != 0)
                    rawMouseUpsAwaitingLowLevel[button]++;
                HookDiagnosticsTrace.Record(HookDiagnosticStage.RawReconcileCounterUpdated, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: button);
                return;
            }

            string? pending = PendingLayerInput(physicalInput);
            HookDiagnosticsTrace.Record(HookDiagnosticStage.RawReconcilePendingLookup, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: button, result: pending == null ? 0 : 1);
            if (pending == null || !presses.TryGetValue(pending, out var state))
                return;
            Detected?.Invoke(pending + " Raw Release Recovery");
            if (state.Handled)
            {
                HookDiagnosticsTrace.Record(HookDiagnosticStage.RawReconcilePressEndStarted, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: button);
                ProcessPress(pending, false, true, 0, IntPtr.Zero, IntPtr.Zero);
                HookDiagnosticsTrace.Record(HookDiagnosticStage.RawReconcilePressEndCompleted, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: button);
            }
            else
                presses.Remove(pending);
            if (button != 0)
                rawMouseUpsAwaitingLowLevel[button]++;
            HookDiagnosticsTrace.Record(HookDiagnosticStage.RawReconcileCounterUpdated, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: button);
        }
    }

    internal void ReconcileRawMouseButtonUpForTest(string physicalInput)
        => ReconcileRawMouseButtonUp(physicalInput);

    internal void ObserveRawMouseButtonDownForTest(string physicalInput)
        => ObserveRawMouseButtonDown(physicalInput);

    static int MouseButtonNumber(string physicalInput) => physicalInput.ToUpperInvariant() switch
    {
        "MOUSELEFT" => 1,
        "MOUSERIGHT" => 2,
        "MOUSEMIDDLE" => 3,
        "MOUSEBACK" => 4,
        "MOUSEFORWARD" or "MOUSEX" => 5,
        _ => 0
    };

    internal static bool BeginCoordinateCapture(Action<int, int> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (CoordinateCaptureLock)
        {
            if (pendingCoordinateCapture != null || suppressCoordinateCaptureLeftUp)
                return false;
            pendingCoordinateCapture = callback;
            return true;
        }
    }

    internal static void CancelCoordinateCapture(Action<int, int>? callback = null)
    {
        lock (CoordinateCaptureLock)
            if (callback == null || ReferenceEquals(pendingCoordinateCapture, callback))
                pendingCoordinateCapture = null;
    }

    static bool TryHandleCoordinateCapture(int message, int x, int y)
    {
        Action<int, int>? callback = null;
        lock (CoordinateCaptureLock)
        {
            if (message == 0x201 && pendingCoordinateCapture != null)
            {
                callback = pendingCoordinateCapture;
                pendingCoordinateCapture = null;
                suppressCoordinateCaptureLeftUp = true;
            }
            else if (message == 0x202 && suppressCoordinateCaptureLeftUp)
            {
                suppressCoordinateCaptureLeftUp = false;
                return true;
            }
            else
                return false;
        }
        try
        {
            callback(x, y);
        }
        catch { }
        return true;
    }

    internal static bool CoordinateCapturePendingForTest
    {
        get
        {
            lock (CoordinateCaptureLock)
                return pendingCoordinateCapture != null || suppressCoordinateCaptureLeftUp;
        }
    }

    IntPtr ProcessPress(string input, bool down, bool up, int n, IntPtr w, IntPtr l, int x = 0, int y = 0, bool fireOnDown = false, bool repeatWhileHeld = false)
    {
        string gestureSource = PhysicalInputToken(input);
        bool committedGesture = committedGestureSources.Contains(gestureSource);
        bool mapped = HasMapping?.Invoke(input) == true;
        if (down && !presses.ContainsKey(input))
        {
            HookDiagnosticsTrace.Record(HookDiagnosticStage.PressDownCreated, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: input.GetHashCode(StringComparison.OrdinalIgnoreCase));
            var state = new PressState { IsDown = true, Handled = mapped, X = x, Y = y, GestureActionCommitted = committedGesture };
            presses[input] = state;
            if (mapped)
            {
                InputStarted?.Invoke(input);
                bool immediateGesture = IsGesturePress?.Invoke(input) == true;
                state.IsGesture = immediateGesture || IsGestureLongPress?.Invoke(input) == true;
                state.NativeMouseDrag = !state.IsGesture && IsNativeMouseDrag?.Invoke(input) == true;
                if (immediateGesture)
                {
                    ActivateGesture(input, state);
                    return (IntPtr)1;
                }
                if (state.NativeMouseDrag)
                {
                    state.ReleaseSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    state.Immediate = InputReceived?.Invoke(input + ":PressStart") == true;
                    if (state.Immediate)
                    {
                        MonitorNativeMouseRelease(input, state);
                        return (IntPtr)1;
                    }
                    state.NativeMouseDrag = false;
                }
                if (fireOnDown && HasLongPress?.Invoke(input) != true)
                {
                    state.FireOnDown = true;
                    InputReceived?.Invoke(input);
                    return (IntPtr)1;
                }
                if (HasLongPress?.Invoke(input) == true)
                {
                    int ms = Math.Clamp(LongPressDuration?.Invoke(input) ?? 500, 50, 10000);
                    state.DownTick = Environment.TickCount64;
                    state.LongPressMs = ms;
                    state.Timer = new System.Threading.Timer(_ => FireLongPress(input, state), null, ms, System.Threading.Timeout.Infinite);
                }
                return (IntPtr)1;
            }
        }
        else if (down && presses.TryGetValue(input, out var existing))
        {
            HookDiagnosticsTrace.Record(HookDiagnosticStage.PressDownExisting, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: input.GetHashCode(StringComparison.OrdinalIgnoreCase), result: existing.Handled ? 1 : 0);
            if (existing.Handled && existing.FireOnDown && repeatWhileHeld)
                InputReceived?.Invoke(input);
            return existing.Handled ? (IntPtr)1 : Next(n, w, l);
        }
        if (up && presses.Remove(input, out var current))
        {
            HookDiagnosticsTrace.Record(HookDiagnosticStage.PressUpRemoved, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: input.GetHashCode(StringComparison.OrdinalIgnoreCase), result: current.Handled ? 1 : 0);
            committedGesture |= current.GestureActionCommitted;
            committedGestureSources.Remove(gestureSource);
            current.IsDown = false;
            current.Timer?.Dispose();
            current.GestureSafetyTimer?.Dispose();
            current.GestureMotionTimer?.Dispose();
            if (current.NativeMouseDrag)
            {
                // Let the physical Up hook return before emitting the synthetic
                // mouse-up/modifier-up pair. Office finalizes Ctrl-drag copy at
                // that boundary.
                current.ReleaseSignal?.TrySetResult();
                return (IntPtr)1;
            }
            if (current.Handled)
            {
                try
                {
                    if (current.Cancelled)
                        return (IntPtr)1;
                    if (current.Immediate)
                    {
                        if (Interlocked.Exchange(ref current.Ended, 1) == 0)
                            InputReceived?.Invoke(input + ":PressEnd");
                    }
                    else if (current.FireOnDown)
                        return (IntPtr)1;
                    else if (current.IsGesture)
                    {
                        CommitGestureMovement(input, current);
                        if (current.GestureActive && !current.GestureExpired && !current.GestureMoved && !committedGesture && current.GestureDirection == null)
                            InputReceived?.Invoke(input + ":Gesture:Center");
                    }
                    else if (current.Dragged)
                        InputReceived?.Invoke(input + ":DragEnd");
                    else if (current.LongPressMs > 0 && Environment.TickCount64 - current.DownTick >= current.LongPressMs && Interlocked.CompareExchange(ref current.LongFired, 1, 0) == 0)
                    {
                        if (current.IsGesture)
                        {
                            ActivateGesture(input, current);
                            if (!current.GestureExpired)
                                InputReceived?.Invoke(input + ":Gesture:Center");
                        }
                        else
                        {
                            InputReceived?.Invoke(input + ":Long");
                            Detected?.Invoke(input + " Long");
                        }
                    }
                    else if (Volatile.Read(ref current.LongFired) == 0)
                        InputReceived?.Invoke(input);
                    return (IntPtr)1;
                }
                finally { InputEnded?.Invoke(input); }
            }
            return Next(n, w, l);
        }
        if (up)
        {
            HookDiagnosticsTrace.Record(HookDiagnosticStage.PressUpMissing, keyboardHook: keyboardHook, mouseHook: mouseHook, value1: input.GetHashCode(StringComparison.OrdinalIgnoreCase));
            committedGestureSources.Remove(gestureSource);
            if (gestureSource.Equals("MouseLeft", StringComparison.OrdinalIgnoreCase) && Volatile.Read(ref modifierDragMouseDown))
                _ = Task.Run(EndModifierDrag);
            return Next(n, w, l);
        }
        return mapped ? (IntPtr)1 : Next(n, w, l);
    }

    void FireLongPress(string input, PressState state)
    {
        lock (stateLock)
        {
            if (!state.IsDown || state.Dragged || Interlocked.CompareExchange(ref state.LongFired, 1, 0) != 0)
                return;
            if (state.IsGesture)
                ActivateGesture(input, state);
            else
            {
                InputReceived?.Invoke(input + ":Long");
                Detected?.Invoke(input + " Long");
            }
        }
    }

    void ActivateGesture(string input, PressState state)
    {
        if (!state.IsDown || state.GestureActive || state.GestureExpired)
            return;
        if (presses.Values.Any(x => !ReferenceEquals(x, state) && x.IsDown && (x.NativeMouseDrag || x.GestureActive)))
        {
            state.GestureExpired = true;
            return;
        }
        int cursorX, cursorY;
        if (GestureCursorForTest is { } testCursor)
        {
            cursorX = testCursor.X;
            cursorY = testCursor.Y;
        }
        else if (GetCursorPos(out var point))
        {
            cursorX = point.x;
            cursorY = point.y;
        }
        else
        {
            state.GestureExpired = true;
            return;
        }
        state.GestureCursorX = cursorX;
        state.GestureCursorY = cursorY;
        state.GestureLastX = cursorX;
        state.GestureLastY = cursorY;
        state.GestureActive = true;
        state.GestureSafetyTimer = new System.Threading.Timer(_ =>
        {
            lock (stateLock)
            {
                if (!state.GestureActive)
                    return;
                state.GestureMotionTimer?.Dispose();
                state.GestureActive = false;
                state.GestureExpired = true;
                Detected?.Invoke(input + " Gesture Safety Release");
            }
        }, null, GestureIdleSafetyMs, System.Threading.Timeout.Infinite);
        RefreshGestureSafety(state);
        Detected?.Invoke(input + " Gesture Ready");
    }

    void RefreshGestureSafety(PressState state)
    {
        state.GestureSafetyTimer?.Change(GestureIdleSafetyMs, System.Threading.Timeout.Infinite);
        if (deferredLayer == null)
            return;
        layerSafetyExpired = false;
        layerSafetyTimer?.Change(GestureIdleSafetyMs, System.Threading.Timeout.Infinite);
    }

    bool CommitGestureMovement(string input, PressState state)
    {
        int dx = state.GestureDx, dy = state.GestureDy;
        state.GestureDx = 0;
        state.GestureDy = 0;
        if (!TryGetGestureDirection(dx, dy, GestureThresholdPixels, out string direction))
            return false;
        state.GestureMoved = true;
        state.GestureActionCommitted = true;
        state.GestureDirection = direction;
        committedGestureSources.Add(PhysicalInputToken(input));
        InputReceived?.Invoke(input + ":Gesture:" + direction);
        Detected?.Invoke(input + " Gesture " + direction);
        return true;
    }

    internal static bool TryGetGestureDirection(int dx, int dy, int threshold, out string direction)
    {
        direction = "";
        threshold = Math.Max(1, threshold);
        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < threshold)
            return false;
        int absX = Math.Abs(dx), absY = Math.Abs(dy);
        // 中心から45度のX字を境界にし、上下左右を均等な4領域として判定する。
        direction = absX >= absY ? (dx >= 0 ? "Right" : "Left") : (dy >= 0 ? "Down" : "Up");
        return true;
    }
    internal void ExpireGestureForTest()
    {
        lock (stateLock)
        {
            foreach (var state in presses.Values.Where(x => x.GestureActive))
            {
                state.GestureSafetyTimer?.Dispose();
                state.GestureMotionTimer?.Dispose();
                state.GestureActive = false;
                state.GestureExpired = true;
            }
        }
    }

    bool HasLayerMappings(string key) => HasMapping?.Invoke(key + "+*") == true;
    void CancelOtherLayerPresses(string selectedInput)
    {
        if (deferredLayer == null)
            return;
        string prefix = deferredLayer + "+";
        foreach (var pair in presses.Where(x => x.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !x.Key.Equals(selectedInput, StringComparison.OrdinalIgnoreCase) && x.Value.IsDown).ToArray())
        {
            var state = pair.Value;
            state.Cancelled = true;
            state.Timer?.Dispose();
            state.ReleaseSignal?.TrySetResult();
            state.GestureSafetyTimer?.Dispose();
            state.GestureMotionTimer?.Dispose();
            state.GestureActive = false;
            state.GestureExpired = true;
            if (state.Immediate && !state.NativeMouseDrag && Interlocked.Exchange(ref state.Ended, 1) == 0)
                InputReceived?.Invoke(pair.Key + ":PressEnd");
        }
    }
    void MonitorNativeMouseRelease(string input, PressState state)
    {
        var callback = InputReceived;
        var ended = InputEnded;
        _ = Task.Run(async () =>
        {
            try
            {
                if (state.ReleaseSignal != null)
                    await state.ReleaseSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));
                // Yield beyond the physical low-level hook callback without a
                // visible delay, preserving Office drop/copy ordering.
                await Task.Delay(4);
            }
            catch (TimeoutException) { }
            finally
            {
                if (Interlocked.Exchange(ref state.Ended, 1) == 0)
                {
                    try { callback?.Invoke(input + ":PressEnd"); }
                    finally { ended?.Invoke(input); }
                }
            }
        });
    }
    void EndNativeRightDragForMappedChord()
    {
        if (!nativeRightLayerDrag && !nativeRightLayerDragStarting)
            return;
        CancelNativeRightDragSafety();
        nativeRightLayerDrag = false;
        nativeRightLayerDragStarting = false;
        QueueNativeRightDragEnd();
        Detected?.Invoke("MouseRight Native Drag End");
    }
    void QueueNativeRightDragStart()
    {
        if (nativeRightDragOutputQueue.IsAddingCompleted)
            return;
        nativeRightLayerDragStarting = true;
        try
        {
            if (!nativeRightDragOutputQueue.TryAdd(NativeRightDragCommand.Start))
                nativeRightLayerDragStarting = false;
        }
        catch (InvalidOperationException) { nativeRightLayerDragStarting = false; }
    }
    void QueueNativeRightDragEnd()
    {
        if (nativeRightDragOutputQueue.IsAddingCompleted)
            return;
        try { nativeRightDragOutputQueue.TryAdd(NativeRightDragCommand.End); }
        catch (InvalidOperationException) { }
    }
    void ProcessNativeRightDragOutput()
    {
        foreach (var command in nativeRightDragOutputQueue.GetConsumingEnumerable())
        {
            if (command == NativeRightDragCommand.End)
            {
                ReleaseMouseLayerButtonIfInjected("MouseRight");
                continue;
            }

            bool sent = false;
            try { sent = SendMouseFlag(8); }
            catch { }
            bool accepted;
            lock (stateLock)
            {
                accepted = sent && !disposed && nativeRightLayerDragStarting
                    && deferredLayer == "MouseRight" && !layerUsed;
                nativeRightLayerDragStarting = false;
                nativeRightLayerDrag = accepted;
                if (accepted)
                    ArmNativeRightDragSafety();
            }
            if (!accepted)
            {
                if (sent)
                    ReleaseMouseLayerButtonIfInjected("MouseRight");
                continue;
            }
            Detected?.Invoke("MouseRight Native Drag");
        }
    }
    void EndImmediateLayerPresses(string layer)
    {
        foreach (var pair in presses.Where(x => x.Key.StartsWith(layer + "+", StringComparison.OrdinalIgnoreCase) && x.Value.IsDown).ToArray())
        {
            var state = pair.Value;
            if (state.IsGesture)
            {
                state.Cancelled = true;
                state.GestureSafetyTimer?.Dispose();
                state.GestureMotionTimer?.Dispose();
                state.GestureActive = false;
                state.GestureExpired = true;
            }
            if (!state.Immediate || state.NativeMouseDrag || Volatile.Read(ref state.Ended) != 0)
                continue;
            state.IsDown = false;
            state.Timer?.Dispose();
            if (Interlocked.Exchange(ref state.Ended, 1) == 0)
                InputReceived?.Invoke(pair.Key + ":PressEnd");
            Detected?.Invoke(pair.Key + " End");
        }
    }
    static void QueueMouseLayerRelease(string layer)
    {
        if (!TryGetMouseLayerRelease(layer, out _, out _))
            return;
        _ = Task.Run(() => ReleaseMouseLayerButtonIfInjected(layer));
    }
    static void ReleaseMouseLayerButtonIfInjected(string layer)
    {
        if (!TryGetMouseLayerRelease(layer, out uint flag, out uint data))
            return;
        int button = layer.ToUpperInvariant() switch
        {
            "MOUSELEFT" => 1,
            "MOUSERIGHT" => 2,
            "MOUSEMIDDLE" => 3,
            "MOUSEBACK" => 4,
            "MOUSEFORWARD" or "MOUSEX" => 5,
            _ => 0
        };
        lock (OutputLock)
        {
            // A layer press is swallowed before Windows sees it. Sending an unmatched
            // button-up can itself trigger Back/Forward or a context menu in some apps.
            // Release only a button that RELYR actually injected (for example a native
            // right-button drag); ordinary layer chords therefore emit no source click.
            if (button != 0 && InjectedMouseButtonsDown.Contains(button))
                SendMouseUpWithRetry(flag, data);
        }
    }
    static bool TryGetMouseLayerRelease(string layer, out uint flag, out uint data)
    {
        (flag, data) = layer.ToUpperInvariant() switch
        {
            "MOUSELEFT" => (4u, 0u),
            "MOUSERIGHT" => (16u, 0u),
            "MOUSEMIDDLE" => (64u, 0u),
            "MOUSEBACK" => (0x100u, 1u),
            "MOUSEFORWARD" or "MOUSEX" => (0x100u, 2u),
            _ => (0u, 0u)
        };
        return flag != 0;
    }
    bool EmergencyHeld(int vk) => vk == 0x7B && HeldAny(0x11, 0xA2, 0xA3) && HeldAny(0x12, 0xA4, 0xA5) && HeldAny(0x10, 0xA0, 0xA1);
    bool HeldAny(params int[] keys) => keys.Any(held.Contains);
    void SendSpaceRepeat()
    {
        if (RepeatOutputForTest is { } testOutput)
            testOutput();
        else
            SendShortcut("Space");
    }
    void CancelLayerRepeat()
    {
        Interlocked.Increment(ref layerRepeatGeneration);
        layerRepeatTimer?.Dispose();
        layerRepeatTimer = null;
        layerRepeatActive = false;
    }
    string? PendingLayerInput(string releasedKey) => presses.Keys.FirstOrDefault(input => input.EndsWith("+" + releasedKey, StringComparison.OrdinalIgnoreCase));
    bool IsLayerRelease(string layer, int vk, uint scanCode, bool up) => up && layer switch { "CapsLock" => TreatF13AsCapsLock && vk == 0x7C, "Space" => scanCode == 0x39 || vk == 0x20, _ => false };
    void ArmLayerSafety()
    {
        CancelLayerSafety();
        layerSafetyTimer = new System.Threading.Timer(_ => layerSafetyExpired = true, null, 10000, System.Threading.Timeout.Infinite);
    }
    void CancelLayerSafety()
    {
        layerSafetyTimer?.Dispose();
        layerSafetyTimer = null;
        layerSafetyExpired = false;
    }
    internal void NotifyNativeMouseDragStarted(string input)
    {
        const string startSuffix = ":PressStart";
        if (input.EndsWith(startSuffix, StringComparison.OrdinalIgnoreCase))
            input = input[..^startSuffix.Length];
        lock (stateLock)
            if (presses.TryGetValue(input, out var state) && state.NativeMouseDrag && state.IsDown)
                state.NativeMouseDragReady = true;
    }

    internal bool IsNativeMouseDragReadyForTest(string input)
    {
        lock (stateLock)
            return presses.TryGetValue(input, out var state)
                && state.NativeMouseDrag
                && state.IsDown
                && state.NativeMouseDragReady;
    }

    bool ObservePhysicalMouseTransition(int message, int mouseData)
    {
        (int button, bool down, bool up) = message switch
        {
            0x201 => (1, true, false),
            0x202 => (1, false, true),
            0x204 => (2, true, false),
            0x205 => (2, false, true),
            0x207 => (3, true, false),
            0x208 => (3, false, true),
            0x20B => (((mouseData >> 16) & 0xffff) == 1 ? 4 : 5, true, false),
            0x20C => (((mouseData >> 16) & 0xffff) == 1 ? 4 : 5, false, true),
            _ => (0, false, false)
        };
        if (button == 0)
            return false;
        int bit = 1 << (button - 1);
        bool suppressUpAlreadyRecoveredByRawInput = false;
        if (down)
        {
            int previous = Interlocked.Or(ref physicalMouseButtonsDownMask, bit);
            // Mouse buttons do not auto-repeat. A repeated physical Down means
            // an earlier Up was lost. Never inject a recovery Up while this
            // callback owns stateLock: SendInput re-enters the low-level hook
            // and can stall Windows input until the hook timeout.
            if (button == 1 && (previous & bit) != 0 && Volatile.Read(ref modifierDragMouseDown))
                _ = Task.Run(EndModifierDrag);
        }
        else if (up)
        {
            Interlocked.And(ref physicalMouseButtonsDownMask, ~bit);
            if (rawInputMonitorStarted)
            {
                if (rawMouseUpsAwaitingLowLevel[button] > 0)
                {
                    rawMouseUpsAwaitingLowLevel[button]--;
                    suppressUpAlreadyRecoveredByRawInput = true;
                }
                else
                    lowLevelMouseUpsPendingRaw[button] = Math.Min(32, lowLevelMouseUpsPendingRaw[button] + 1);
            }
        }

        // ProcessPress signals the ordered PressEnd below. Never inject from
        // here while the hook owns stateLock; doing so stalls Windows input.
        return suppressUpAlreadyRecoveredByRawInput;
    }

    static bool IsObservedPhysicalMouseButtonDown(int button)
        => button is >= 1 and <= 5 && (Volatile.Read(ref physicalMouseButtonsDownMask) & (1 << (button - 1))) != 0;

    internal static bool IsObservedPhysicalMouseButtonDownForTest(int button)
        => IsObservedPhysicalMouseButtonDown(button);
    internal static bool IsPhysicalLeftButtonDown => IsObservedPhysicalMouseButtonDown(1);
    void ArmNativeRightDragSafety()
    {
        CancelNativeRightDragSafety();
        nativeRightDragSafetyTimer = new System.Threading.Timer(_ =>
        {
            // GetAsyncKeyState also reports RELYR's synthetic RightDown and can
            // therefore keep its own stuck state alive forever.  Observe only
            // unmarked low-level/Raw Input physical transitions.
            bool rightButtonIsDown = PhysicalKeyDownForTest?.Invoke(0x02) ?? IsObservedPhysicalMouseButtonDown(2);
            if (!rightButtonIsDown)
                ResetCapturedState(false, true);
        }, null, 100, 25);
    }
    void CancelNativeRightDragSafety()
    {
        nativeRightDragSafetyTimer?.Dispose();
        nativeRightDragSafetyTimer = null;
    }
    internal void ExpireLayerForTest() => layerSafetyExpired = true;
    void ResetCapturedState(bool release, bool clearGestureSuppressions = false)
    {
        string? releasedLayer;
        string[] endedInputs;
        bool endNativeRightDrag;
        lock (stateLock)
        {
            releasedLayer = deferredLayer;
            endNativeRightDrag = nativeRightLayerDrag || nativeRightLayerDragStarting;
            endedInputs = [.. presses.Where(x => x.Value.Handled).Select(x => x.Key)];
            foreach (var state in presses.Values)
            {
                state.IsDown = false;
                state.Timer?.Dispose();
                state.GestureSafetyTimer?.Dispose();
                state.GestureMotionTimer?.Dispose();
                state.GestureActive = false;
                state.GestureExpired = true;
                Interlocked.Exchange(ref state.Ended, 1);
                state.ReleaseSignal?.TrySetResult();
            }
            presses.Clear();
            held.Clear();
            CancelLayerRepeat();
            CancelLayerSafety();
            CancelNativeRightDragSafety();
            deferredLayer = null;
            layerUsed = false;
            nativeRightLayerDrag = false;
            nativeRightLayerDragStarting = false;
            Volatile.Write(ref physicalMouseButtonsDownMask, 0);
            if (clearGestureSuppressions)
                committedGestureSources.Clear();
        }
        foreach (string input in endedInputs)
            InputEnded?.Invoke(input);
        if (!string.IsNullOrWhiteSpace(releasedLayer))
            LayerEnded?.Invoke(releasedLayer);
        if (endNativeRightDrag)
            QueueNativeRightDragEnd();
        else
            QueueMouseLayerRelease(releasedLayer ?? "");
        if (release)
            ReleaseAll();
        else
            _ = Task.Run(EndModifierDrag);
    }
    void StopAndRelease()
    {
        enabled = false;
        ResetCapturedState(false, true);
        _ = Task.Run(async () => { ReleaseAll(); if (ExitOnEmergency) { await Task.Delay(80); App.ExitImmediately(2); } });
    }
    static double Distance(int x1, int y1, int x2, int y2) => Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
    static string UsKeyName(int vk) => vk switch { 0xC0 => "`", 0xBD => "-", 0xBB => "=", 0xDB => "[", 0xDD => "]", 0xDC => "\\", 0xBA => ";", 0xDE => "'", _ => KeyName(vk) };
    internal static string HookKeyName(int vk, uint scanCode, bool useUsLayout, bool treatF13AsCapsLock)
    {
        string key = useUsLayout ? UsKeyName(vk) : KeyName(vk);
        return treatF13AsCapsLock && key == "F13" ? "CapsLock" : key;
    }
    internal static string KeyName(int vk) => vk switch
    {
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x60 and <= 0x69 => "NumPad" + (vk - 0x60),
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
        0x08 => "Back",
        0x09 => "Tab",
        0x0D => "Enter",
        0x10 => "LeftShift",
        0x11 => "LeftCtrl",
        0x12 => "LeftAlt",
        0x13 => "Pause",
        0x1B => "Esc",
        0x20 => "Space",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2D => "Insert",
        0x2E => "Delete",
        0x5B => "LWin",
        0x5C => "RWin",
        0x6A => "Multiply",
        0x6B => "Add",
        0x6D => "Subtract",
        0x6E => "Decimal",
        0x6F => "Divide",
        0x90 => "NumLock",
        0xA0 => "LeftShift",
        0xA1 => "RightShift",
        0xA2 => "LeftCtrl",
        0xA3 => "RightCtrl",
        0xA4 => "LeftAlt",
        0xA5 => "RightAlt",
        0xF3 => "半角/全角",
        0x1D => "無変換",
        0x1C => "変換",
        0x15 => "カタカナ",
        0x14 => "CapsLock",
        0x2C => "PrintScreen",
        0x91 => "ScrollLock",
        0xBD => "-",
        0xDE => "^",
        0xDC => "¥",
        0xC0 => "@",
        0xDB => "[",
        0xBB => ";",
        0xBA => ":",
        0xDD => "]",
        0xBC => ",",
        0xBE => ".",
        0xBF => "/",
        0xE2 => "_",
        _ => KeyInterop.KeyFromVirtualKey(vk).ToString()
    };
    IntPtr Next(int n, IntPtr w, IntPtr l) => NextHookForTest?.Invoke(n, w, l) ?? CallNextHookEx(IntPtr.Zero, n, w, l);

    internal static bool HasInjectedInputForTest()
    {
        lock (OutputLock)
            return InjectedKeysDown.Count > 0 || InjectedMouseButtonsDown.Count > 0;
    }
    internal static void ExpireInjectedInputsForTest()
    {
        lock (OutputLock)
        {
            foreach (var key in InjectedKeysDown)
                InjectedKeyDownAt[key] = 0;
            foreach (var button in InjectedMouseButtonsDown)
                InjectedMouseDownAt[button] = 0;
        }
        ReleaseStaleInjectedInputs();
    }
    public void EnableDirectTestInput() => directTestTarget = this;
    public void DisableDirectTestInputForTest()
    {
        if (ReferenceEquals(directTestTarget, this))
            directTestTarget = null;
    }
    public static void InjectKeyForTest(string key, bool up)
    {
        var vk = ParseKey(key);
        if (vk == 0)
            throw new ArgumentException("Unknown key: " + key);
        if (directTestTarget is { } target)
        {
            target.DirectKey(vk, up);
            return;
        }
        SendInput(1, [new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0, dwExtraInfo = UIntPtr.Zero } } }], Marshal.SizeOf<INPUT>());
    }
    internal static void InjectRawKeyForTest(ushort vk, uint scanCode, bool up)
    {
        if (directTestTarget is not { } target)
            throw new InvalidOperationException("Direct test input is not enabled.");
        target.DirectKey(vk, scanCode, up);
    }
    public static void InjectMouseForTest(string button, bool up)
    {
        if (directTestTarget is { } target)
        {
            int message = button.ToUpperInvariant() switch
            {
                "LEFT" => up ? 0x202 : 0x201,
                "RIGHT" => up ? 0x205 : 0x204,
                "MIDDLE" => up ? 0x208 : 0x207,
                _ => throw new ArgumentException("Unknown mouse button")
            };
            target.DirectMouse(message);
            return;
        }
        uint flag = button.ToUpperInvariant() switch
        {
            "LEFT" => up ? 4u : 2u,
            "RIGHT" => up ? 16u : 8u,
            "MIDDLE" => up ? 64u : 32u,
            _ => throw new ArgumentException("Unknown mouse button")
        };
        SendInput(1, [new INPUT { type = 0, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flag, dwExtraInfo = UIntPtr.Zero } } }], Marshal.SizeOf<INPUT>());
    }
    public static void InjectWheelForTest(bool up, bool horizontal = false)
    {
        if (directTestTarget is { } target)
        {
            target.DirectMouse(horizontal ? 0x20E : 0x20A, up ? 120 : unchecked((int)0xff880000));
            return;
        }
        SendInput(1, [new INPUT { type = 0, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = horizontal ? 0x1000u : 0x800u, mouseData = up ? 120u : unchecked((uint)-120), dwExtraInfo = UIntPtr.Zero } } }], Marshal.SizeOf<INPUT>());
    }
    public static void InjectMouseMoveForTest(int dx, int dy)
    {
        if (directTestTarget is { } target)
        {
            target.DirectMouse(0x200, 0, dx, dy);
            return;
        }
        SendInput(1, [new INPUT { type = 0, U = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = 1, dwExtraInfo = UIntPtr.Zero } } }], Marshal.SizeOf<INPUT>());
    }
    IntPtr DirectKey(ushort vk, bool up) => DirectKey(vk, 0, up);
    IntPtr DirectKey(ushort vk, uint scanCode, bool up, UIntPtr extraInfo = default)
    {
        var data = new KBDLLHOOKSTRUCT { vkCode = vk, scanCode = scanCode, dwExtraInfo = extraInfo };
        IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<KBDLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(data, pointer, false);
            return KeyboardCallback(0, (IntPtr)(up ? WM_KEYUP : WM_KEYDOWN), pointer);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    internal IntPtr DirectKeyForTest(ushort vk, bool up) => DirectKey(vk, up);
    internal IntPtr DirectMarkedKeyForTest(ushort vk, bool up) => DirectKey(vk, 0, up, (UIntPtr)Marker);
    IntPtr DirectMouse(int message, int mouseData = 0, int x = 0, int y = 0, UIntPtr extraInfo = default)
    {
        var data = new MSLLHOOKSTRUCT { pt = new POINT { x = x, y = y }, mouseData = mouseData, dwExtraInfo = extraInfo };
        IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf<MSLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(data, pointer, false);
            return MouseCallback(0, (IntPtr)message, pointer);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    internal IntPtr DirectMouseForTest(int message, int mouseData = 0, int x = 0, int y = 0) => DirectMouse(message, mouseData, x, y);
    internal IntPtr DirectMarkedMouseForTest(int message) => DirectMouse(message, extraInfo: (UIntPtr)Marker);
    public static bool IsKeyDownForTest(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    internal bool HasCapturedStateForTest()
    {
        lock (stateLock)
            return deferredLayer != null || presses.Count > 0 || held.Count > 0;
    }
    internal bool HasActiveLayerStateForTest()
    {
        lock (stateLock)
            return deferredLayer != null || presses.Keys.Any(x => x.StartsWith("Space+", StringComparison.OrdinalIgnoreCase) || x.StartsWith("CapsLock+", StringComparison.OrdinalIgnoreCase) || x.StartsWith("MouseRight+", StringComparison.OrdinalIgnoreCase));
    }
    public bool TryPrepareForProfileChange()
        => TryPrepareForProfileChange(vk => (GetAsyncKeyState(vk) & 0x8000) != 0);

    internal bool TryPrepareForProfileChange(Func<int, bool> isKeyDown)
    {
        lock (stateLock)
        {
            // Presses keep the mapping captured on Down, so a profile can change while
            // a layer or gesture is held without making its eventual Up use another map.
            bool captured = deferredLayer != null || held.Count > 0 || presses.Values.Any(x => x.IsDown);
            // A swallowed mouse Down is not reflected by GetAsyncKeyState on every
            // driver. Keep RELYR's captured mouse state until its real Up or safety
            // timeout instead of dropping the layer during a profile switch.
            if (captured && (CapturedMouseInputIsDown() || CapturedPhysicalInputIsDown(isKeyDown)))
                return true;
            ResetCapturedState(false);
            lastSpaceTapTick = 0;
            return true;
        }
    }
    static bool TryDispatchApplicationAction(string value)
    {
        if (!value.Equals(ActionCatalog.ShowRelyrMainWindowAction, StringComparison.OrdinalIgnoreCase))
            return false;
#if !PRODUCTION_PUBLISH
        if (ShowRelyrMainWindowOutputForTest is { } test)
        {
            test();
            return true;
        }
#endif
        if (System.Windows.Application.Current is { } app)
            _ = app.Dispatcher.BeginInvoke(() => { if (app.MainWindow is MainWindow window) window.ShowFromExternalLaunch(); });
        return true;
    }
    bool CapturedMouseInputIsDown()
        => deferredLayer != null && PhysicalInputToken(deferredLayer).StartsWith("Mouse", StringComparison.OrdinalIgnoreCase)
          || presses.Any(x => x.Value.IsDown && PhysicalInputToken(x.Key).StartsWith("Mouse", StringComparison.OrdinalIgnoreCase));
    bool CapturedPhysicalInputIsDown(Func<int, bool> isKeyDown)
    {
        if (held.Any(isKeyDown))
            return true;
        if (deferredLayer != null && PhysicalInputVirtualKey(deferredLayer) is int layerKey && isKeyDown(layerKey))
            return true;
        return presses.Where(x => x.Value.IsDown)
            .Select(x => PhysicalInputVirtualKey(x.Key))
            .Any(vk => vk is int key && isKeyDown(key));
    }
    static int? PhysicalInputVirtualKey(string input)
    {
        string token = PhysicalInputToken(input);
        return token.ToUpperInvariant() switch
        {
            "MOUSELEFT" => 0x01,
            "MOUSERIGHT" => 0x02,
            "MOUSEMIDDLE" => 0x04,
            "MOUSEBACK" => 0x05,
            "MOUSEFORWARD" or "MOUSEX" => 0x06,
            "WHEELUP" or "WHEELDOWN" or "TILTLEFT" or "TILTRIGHT" => null,
            _ => ParseKey(token) is ushort vk && vk != 0 ? vk : null
        };
    }
    static string PhysicalInputToken(string input) => input[(input.LastIndexOf('+') + 1)..];
    internal bool HookTestStateCleanForTest => hookTestStateClean;
    internal bool RawInputMonitorStartedForTest => rawInputMonitorStarted;
    internal void SetRawInputMonitorStartedForTest(bool started) => rawInputMonitorStarted = started;
    internal static bool HookMissedRawTransitions(long rawTransitions, long lowLevelTransitions)
        => rawTransitions > lowLevelTransitions;
    void ObserveRawHookTransition(bool keyboard, int count = 1)
    {
        if (keyboard)
            Interlocked.Add(ref rawKeyboardTransitions, count);
        else
            Interlocked.Add(ref rawMouseTransitions, count);
    }
#if HOOK_DIAGNOSTICS
    void RecordHookDiagnosticsHeartbeat()
    {
        Thread? thread = hookThread;
        long rawKeyboard = Volatile.Read(ref rawKeyboardTransitions);
        long rawMouse = Volatile.Read(ref rawMouseTransitions);
        long lowKeyboard = Volatile.Read(ref lowLevelKeyboardTransitions);
        long lowMouse = Volatile.Read(ref lowLevelMouseTransitions);
        HookDiagnosticsTrace.Record(
            HookDiagnosticStage.IndependentMonitorHeartbeat,
            keyboardHook: keyboardHook,
            mouseHook: mouseHook,
            value1: rawKeyboard,
            value2: lowKeyboard,
            result: thread?.IsAlive == true ? 1 : 0);
        HookDiagnosticsTrace.Record(
            HookDiagnosticStage.IndependentMonitorMouseCounters,
            keyboardHook: keyboardHook,
            mouseHook: mouseHook,
            value1: rawMouse,
            value2: lowMouse,
            result: rawInputMonitorStarted ? 1 : 0);
        HookDiagnosticsTrace.Record(
            HookDiagnosticStage.IndependentMonitorState,
            keyboardHook: keyboardHook,
            mouseHook: mouseHook,
            value1: Volatile.Read(ref hookThreadId),
            value2: 0,
            result: disposed ? 0 : 1);
    }
#endif
    internal bool HasCapturedPhysicalInput
    {
        get
        {
            lock (stateLock)
                return deferredLayer != null || held.Count > 0 || presses.Values.Any(x => x.IsDown);
        }
    }
    internal bool IsDisposedForTest => disposed;
    internal void CancelLongPressTimerForTest(string input)
    {
        lock (stateLock)
        if (presses.TryGetValue(input, out var state))
        {
            state.Timer?.Dispose();
            state.Timer = null;
        }
    }
    public void ResetStateForTest()
    {
        ResetCapturedState(false, true);
        held.Clear();
        Array.Clear(lowLevelMouseUpsPendingRaw);
        Array.Clear(rawMouseUpsAwaitingLowLevel);
        lastSpaceTapTick = 0;
    }
    public void ResetForSessionTransition()
    {
        ResetCapturedState(true, true);
        Array.Clear(lowLevelMouseUpsPendingRaw);
        Array.Clear(rawMouseUpsAwaitingLowLevel);
        lastSpaceTapTick = 0;
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        enabled = false;
        if (ReferenceEquals(directTestTarget, this))
            directTestTarget = null;
        ResetCapturedState(true, true);
        nativeRightDragOutputQueue.CompleteAdding();
#if HOOK_DIAGNOSTICS
        hookDiagnosticsHeartbeatTimer.Dispose();
#endif
        try { nativeRightDragOutputWorker.Wait(1000); }
        catch { }
        if (keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(keyboardHook);
            keyboardHook = IntPtr.Zero;
        }
        if (mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(mouseHook);
            mouseHook = IntPtr.Zero;
        }
        uint threadId = hookThreadId;
        if (threadId != 0)
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (PostThreadMessage(threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero))
                    break;
                Thread.Sleep(20);
            }
        if (hookThread != null && !ReferenceEquals(Thread.CurrentThread, hookThread) && !hookThread.Join(2000) && threadId != 0)
            TerminateHookThread(threadId);
        hookThread = null;
        hookReady.Dispose();
        hookTestCompleted.Dispose();
    }

    static void TerminateHookThread(uint threadId)
    {
        IntPtr handle = OpenThread(0x0001, false, threadId);
        if (handle == IntPtr.Zero)
            return;
        try
        {
            TerminateThread(handle, 0);
        }
        finally { CloseHandle(handle); }
    }

}
