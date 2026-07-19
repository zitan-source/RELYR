using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Windows.Input;

namespace RELYR;

public sealed class InputEngine : IDisposable
{
    static readonly BlockingCollection<Action> DesktopActions=new();
    static InputEngine? directTestTarget;
    static readonly object OutputLock=new();
    static readonly HashSet<ushort> InjectedKeysDown=[];
    static readonly HashSet<int> InjectedMouseButtonsDown=[];
    static readonly Dictionary<ushort,long> InjectedKeyDownAt=[];
    static readonly Dictionary<int,long> InjectedMouseDownAt=[];
    static readonly System.Threading.Timer InjectedInputSafetyTimer=new(_=>ReleaseStaleInjectedInputs(),null,250,250);
    static bool restoreMinimizedWindowsNext;
    static ushort modifierDragKey;
    static bool modifierDragMouseDown;
    static long modifierDragStartedAt;
    static System.Threading.Timer? modifierDragSafetyTimer;
    internal static Action<uint,uint>? MouseFlagOutputForTest=null;
    internal static Action<(uint Flag,uint Data)[]>? MouseClickBatchOutputForTest=null;
    internal static Func<ushort,bool,bool>? KeyOutputForTest=null;
    internal static Func<int,bool>? PhysicalKeyDownForTest=null;
    internal static Action<string>? UnicodeTextOutputForTest=null;
    internal static Action<int>? ImeActionOutputForTest=null;
    internal static Action<Action>? DesktopActionOutputForTest=null;
    internal static Func<bool>? LockWorkStationOutputForTest=null;
    internal Func<int,IntPtr,IntPtr,IntPtr>? NextHookForTest { get; set; }
    static long lastWheelOutput;
    public static Action<string>? DesktopActionFailed;
    static InputEngine()=>_=Task.Run(()=>{foreach(var action in DesktopActions.GetConsumingEnumerable()){try{action();}catch(Exception ex){DesktopActionFailed?.Invoke(ex.Message);}}});
    const int WH_KEYBOARD_LL=13, WH_MOUSE_LL=14, WM_KEYDOWN=0x100, WM_KEYUP=0x101, WM_SYSKEYDOWN=0x104, WM_SYSKEYUP=0x105;
    const uint WM_QUIT=0x0012,WM_RUN_HOOK_TEST=0x8001;
    const uint Marker=0x1C0570;
    IntPtr keyboardHook, mouseHook;
    Thread? hookThread;
    uint hookThreadId;
    readonly ManualResetEventSlim hookReady=new(false);
    readonly AutoResetEvent hookTestCompleted=new(false);
    Exception? hookStartException;
    Exception? hookTestException;
    bool hookTestStateClean;
    bool disposed;
    readonly HookProc keyboardProc, mouseProc;
    readonly object stateLock=new();
    readonly HashSet<int> held=[];
    readonly Dictionary<string, PressState> presses=new(StringComparer.OrdinalIgnoreCase);
    string? deferredLayer;
    System.Threading.Timer? layerSafetyTimer;
    volatile bool layerSafetyExpired;
    bool layerUsed;
    int mouseLayerStartX,mouseLayerStartY;
    bool nativeRightLayerDrag;
    System.Threading.Timer? layerRepeatTimer;volatile bool layerRepeatActive;
    long lastSpaceTapTick;
    int layerRepeatGeneration;
    long lastRecordedMove;
    long mousePassthroughUntil;
    bool enabled=true;
    public bool Enabled
    {
        get=>enabled;
        set
        {
            if(enabled==value)return;
            enabled=value;
            if(!value)ResetCapturedState(true);
        }
    }
    public bool CaptureMouseMoves { get; set; }
    public int DragPixels { get; set; }=6;
    public Func<string,bool>? HasMapping { get; set; }
    public Func<string,bool>? IsNativeMouseDrag { get; set; }
    public Func<string,bool>? SuppressLayerTap { get; set; }
    public bool UseUsLayout { get; set; }
    public bool TreatF13AsCapsLock { get; set; }
    public bool SpaceHoldRepeatEnabled { get; set; }=true;
    public bool ExitOnEmergency { get; set; }=true;
    public int SpaceHoldRepeatDelayMs { get; set; }=400;
    public int SpaceHoldRepeatIntervalMs { get; set; }=55;
    internal Action? RepeatOutputForTest { get; set; }
    public Func<string,bool>? InputReceived { get; set; }
    public Func<string,int>? LongPressDuration { get; set; }
    public Func<string,bool>? HasLongPress { get; set; }
    public event Action<string>? Detected;

    public InputEngine(){ keyboardProc=KeyboardCallback; mouseProc=MouseCallback; }
    public void Start()
    {
        if(disposed)throw new ObjectDisposedException(nameof(InputEngine));
        if(hookThread!=null)return;
        // 起動に使ったクリックの Down/Up の途中からフックしない。
        // 起動直後だけ物理マウス入力をそのまま通し、押下状態の食い違いを防ぐ。
        mousePassthroughUntil=Environment.TickCount64+500;
        hookStartException=null;hookReady.Reset();
        hookThread=new Thread(HookLoop){IsBackground=true,Name="RELYR input hook"};
        hookThread.Start();
        if(!hookReady.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException("入力フックの開始が5秒以内に完了しませんでした。");
        if(hookStartException!=null)throw new InvalidOperationException("入力フックを開始できませんでした。",hookStartException);
    }

    void HookLoop()
    {
        hookThreadId=GetCurrentThreadId();
        try
        {
            IntPtr module=GetModuleHandle(null);
            keyboardHook=SetWindowsHookEx(WH_KEYBOARD_LL,keyboardProc,module,0);
            mouseHook=SetWindowsHookEx(WH_MOUSE_LL,mouseProc,module,0);
            if(keyboardHook==IntPtr.Zero||mouseHook==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            hookReady.Set();
            while(GetMessage(out var message,IntPtr.Zero,0,0)>0)
            {
                if(message.message!=WM_RUN_HOOK_TEST)continue;
                try{RunHookTestSequence();}
                catch(Exception ex){hookTestException=ex;}
                finally{hookTestCompleted.Set();}
            }
        }
        catch(Exception ex){hookStartException=ex;hookReady.Set();}
        finally
        {
            if(keyboardHook!=IntPtr.Zero){UnhookWindowsHookEx(keyboardHook);keyboardHook=IntPtr.Zero;}
            if(mouseHook!=IntPtr.Zero){UnhookWindowsHookEx(mouseHook);mouseHook=IntPtr.Zero;}
            hookThreadId=0;
        }
    }

    internal void RunHookThreadSequenceForTest()
    {
        uint threadId=hookThreadId;if(threadId==0)throw new InvalidOperationException("入力フックスレッドが開始されていません。");
        hookTestException=null;hookTestStateClean=false;
        if(!PostThreadMessage(threadId,WM_RUN_HOOK_TEST,UIntPtr.Zero,IntPtr.Zero))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if(!hookTestCompleted.WaitOne(TimeSpan.FromSeconds(5)))throw new TimeoutException("入力フックスレッド試験が5秒以内に完了しませんでした。");
        if(hookTestException!=null)throw new InvalidOperationException("入力フックスレッド試験に失敗しました。",hookTestException);
    }

    void RunHookTestSequence()
    {
        foreach(var item in new[]{("F13",false),("U",false),("U",true),("U",false),("U",true),("F13",true),("Space",false),("J",false),("J",true),("Space",true)})
        {
            DirectKey(ParseKey(item.Item1),item.Item2);
            if(!item.Item2&&(item.Item1=="F13"||item.Item1=="Space"))Thread.Sleep(120);
        }
        DirectMouse(0x204);
        Thread.Sleep(120);
        DirectMouse(0x20A,120<<16);
        DirectMouse(0x205);
        hookTestStateClean=deferredLayer==null&&presses.Count==0&&held.Count==0;
    }

    IntPtr KeyboardCallback(int n,IntPtr w,IntPtr l)
    {
        lock(stateLock)return KeyboardCallbackCore(n,w,l);
    }

    IntPtr KeyboardCallbackCore(int n,IntPtr w,IntPtr l)
    {
        if(n<0) return Next(n,w,l);
        var d=Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(l);
        // 自分で生成した入力は再マッピングしないが、Windowsと後続フックへは必ず渡す。
        if(d.dwExtraInfo==(UIntPtr)Marker) return Next(n,w,l);
        bool down=w==(IntPtr)WM_KEYDOWN||w==(IntPtr)WM_SYSKEYDOWN, up=w==(IntPtr)WM_KEYUP||w==(IntPtr)WM_SYSKEYUP;
        if(!down&&!up) return Next(n,w,l);
        int vk=(int)d.vkCode; string key=HookKeyName(vk,d.scanCode,UseUsLayout,TreatF13AsCapsLock);
        bool currentLayerRelease=deferredLayer!=null&&up&&(key.Equals(deferredLayer,StringComparison.OrdinalIgnoreCase)||IsLayerRelease(deferredLayer,vk,d.scanCode,true));
        if(!currentLayerRelease)
        {
            if(layerSafetyExpired)ResetCapturedState(false);
        }
        if(down) held.Add(vk); else held.Remove(vk);
        if(down&&EmergencyHeld(vk)){ StopAndRelease(); Detected?.Invoke("緊急停止"); return (IntPtr)1; }
        Detected?.Invoke($"{key} {(down?"Down":"Up")}");
        if(!Enabled) return Next(n,w,l);
        if(nativeRightLayerDrag)return Next(n,w,l);

        bool reliableCapsLayer=key=="CapsLock"&&TreatF13AsCapsLock&&vk==0x7C;
        if(deferredLayer is null&&down&&((key!="CapsLock"&&HasLayerMappings(key))||reliableCapsLayer||(key=="Space"&&SpaceHoldRepeatEnabled)))
        {
            deferredLayer=key;ArmLayerSafety();layerUsed=false;layerRepeatActive=false;
            if(key=="Space"&&SpaceHoldRepeatEnabled&&Environment.TickCount64-lastSpaceTapTick<=500)
            {
                lastSpaceTapTick=0;
                int generation=Interlocked.Increment(ref layerRepeatGeneration);
                int interval=Math.Clamp(SpaceHoldRepeatIntervalMs,25,500);
                layerRepeatTimer=new System.Threading.Timer(_=>
                {
                    bool send=false;
                    lock(stateLock)
                    {
                        if(generation==Volatile.Read(ref layerRepeatGeneration)&&deferredLayer=="Space"&&!layerUsed)
                        {
                            layerRepeatActive=true;send=true;
                            layerRepeatTimer?.Change(interval,interval);
                        }
                    }
                    if(send)SendSpaceRepeat();
                },null,Math.Clamp(SpaceHoldRepeatDelayMs,100,2000),System.Threading.Timeout.Infinite);
            }
            return (IntPtr)1;
        }
        if(deferredLayer!=null&&(key.Equals(deferredLayer,StringComparison.OrdinalIgnoreCase)||IsLayerRelease(deferredLayer,vk,d.scanCode,up)))
        {
            if(up){string releasedLayer=deferredLayer;bool repeated=layerRepeatActive;EndImmediateLayerPresses(releasedLayer);CancelLayerRepeat();CancelLayerSafety();if(!layerUsed&&!repeated&&SuppressLayerTap?.Invoke(releasedLayer)!=true){if(HasMapping?.Invoke(releasedLayer)==true)InputReceived?.Invoke(releasedLayer);else _=Task.Run(()=>SendShortcut(releasedLayer));if(releasedLayer=="Space")lastSpaceTapTick=Environment.TickCount64;}deferredLayer=null;layerUsed=false;}return (IntPtr)1;
        }
        bool layerChord=deferredLayer!=null&&!layerRepeatActive;
        string? pendingInput=PendingLayerInput(key);
        string input=pendingInput??(layerChord?deferredLayer+"+"+key:key);
        if(deferredLayer!=null&&!layerRepeatActive&&down&&HasMapping?.Invoke(input)==true){layerUsed=true;if(deferredLayer=="Space")lastSpaceTapTick=0;CancelLayerRepeat();}
        bool keyboardLayer=deferredLayer is "Space" or "CapsLock";
        if(layerChord&&down&&!keyboardLayer)CancelOtherLayerPresses(input);
        bool fireLayerActionOnDown=layerChord&&keyboardLayer&&(pendingInput==null||input.StartsWith(deferredLayer+"+",StringComparison.OrdinalIgnoreCase));
        // Space/CapsLock はキーリピートを利用する。マウス側面レイヤーでは、
        // チルト等が生成したキーを解放時に1回だけ確定し、別操作が始まれば取り消す。
        bool repeatLayerAction=fireLayerActionOnDown;
        return ProcessPress(input,down,up,n,w,l,fireOnDown:fireLayerActionOnDown,repeatWhileHeld:repeatLayerAction);
    }

    IntPtr MouseCallback(int n,IntPtr w,IntPtr l)
    {
        lock(stateLock)return MouseCallbackCore(n,w,l);
    }

    IntPtr MouseCallbackCore(int n,IntPtr w,IntPtr l)
    {
        if(n<0) return Next(n,w,l);
        var d=Marshal.PtrToStructure<MSLLHOOKSTRUCT>(l); int msg=w.ToInt32();
        if(d.dwExtraInfo==(UIntPtr)Marker) return Next(n,w,l);
        if(Environment.TickCount64<mousePassthroughUntil)return Next(n,w,l);
        if(msg==0x200)
        {
            if(CaptureMouseMoves&&Environment.TickCount64-lastRecordedMove>=50){lastRecordedMove=Environment.TickCount64;Detected?.Invoke($"MouseMove:{d.pt.x},{d.pt.y}");}
            if(Enabled&&deferredLayer=="MouseRight"&&!layerUsed&&!nativeRightLayerDrag&&Distance(mouseLayerStartX,mouseLayerStartY,d.pt.x,d.pt.y)>=DragPixels)
            {
                nativeRightLayerDrag=SendMouseFlag(8);
                if(nativeRightLayerDrag){CancelLayerSafety();Detected?.Invoke("MouseRight Native Drag");}
            }
            foreach(var pair in presses.Where(x=>x.Key.Contains("Mouse",StringComparison.OrdinalIgnoreCase)&&x.Value.IsDown).ToArray()) if(Distance(pair.Value.X,pair.Value.Y,d.pt.x,d.pt.y)>=DragPixels&&!pair.Value.Dragged){pair.Value.Dragged=true;if(!pair.Value.Immediate)InputReceived?.Invoke(pair.Key+":DragStart");Detected?.Invoke(pair.Key+" Drag");}
            return Next(n,w,l);
        }
        string? name=msg switch{0x201 or 0x202=>"MouseLeft",0x204 or 0x205=>"MouseRight",0x207 or 0x208=>"MouseMiddle",0x20B or 0x20C=>((d.mouseData>>16)&0xffff)==1?"MouseBack":"MouseForward",0x20A=>d.mouseData>0?"WheelUp":"WheelDown",0x20E=>d.mouseData>0?"TiltRight":"TiltLeft",_=>null};
        if(name==null) return Next(n,w,l);
        bool buttonDown=msg is 0x201 or 0x204 or 0x207 or 0x20B, buttonUp=msg is 0x202 or 0x205 or 0x208 or 0x20C;
        bool rawDown=buttonDown||msg is 0x20A or 0x20E,rawUp=buttonUp;
        bool currentLayerRelease=deferredLayer!=null&&buttonUp&&name.Equals(deferredLayer,StringComparison.OrdinalIgnoreCase);
        if(!currentLayerRelease)
        {
            if(layerSafetyExpired)ResetCapturedState(false);
        }
        if(!Enabled){Detected?.Invoke(name+(rawDown?" Down":rawUp?" Up":""));return Next(n,w,l);}
        if(nativeRightLayerDrag&&!currentLayerRelease){Detected?.Invoke(name+(rawDown?" Down":rawUp?" Up":""));return Next(n,w,l);}
        if(deferredLayer is null&&buttonDown&&HasLayerMappings(name)){deferredLayer=name;mouseLayerStartX=d.pt.x;mouseLayerStartY=d.pt.y;nativeRightLayerDrag=false;ArmLayerSafety();layerUsed=false;Detected?.Invoke(name+" Layer Down");return (IntPtr)1;}
        if(deferredLayer!=null&&name.Equals(deferredLayer,StringComparison.OrdinalIgnoreCase)&&buttonUp)
        {
            string releasedLayer=deferredLayer;bool used=layerUsed,nativeDrag=nativeRightLayerDrag;
            Detected?.Invoke(name+" Layer Up");EndImmediateLayerPresses(releasedLayer);CancelLayerSafety();deferredLayer=null;layerUsed=false;nativeRightLayerDrag=false;
            if(nativeDrag){QueueMouseLayerRelease(releasedLayer);return (IntPtr)1;}
            // レイヤーの物理Upは抑止するため、Windows側に以前のDown状態が
            // 残っていても必ず解放する。Upの重複送信はクリックを発生させない。
            if(used)QueueMouseLayerRelease(releasedLayer);
            else if(HasMapping?.Invoke(releasedLayer)==true){QueueMouseLayerRelease(releasedLayer);InputReceived?.Invoke(releasedLayer);}
            else _=Task.Run(()=>SendMouseClickAtomic(releasedLayer));
            return (IntPtr)1;
        }
        if(buttonUp)name=PendingLayerInput(name)??(deferredLayer!=null&&!layerRepeatActive?deferredLayer+"+"+name:name);
        else if(deferredLayer!=null&&!layerRepeatActive)name=deferredLayer+"+"+name;
        bool down=buttonDown||msg is 0x20A or 0x20E, up=buttonUp;
        if(deferredLayer!=null&&!layerRepeatActive&&down)CancelOtherLayerPresses(name);
        if(deferredLayer!=null&&!layerRepeatActive&&down&&HasMapping?.Invoke(name)==true){layerUsed=true;if(deferredLayer=="Space")lastSpaceTapTick=0;CancelLayerRepeat();}
        Detected?.Invoke(name+(down?" Down":" Up"));
        if(down&&!up&&msg is 0x20A or 0x20E)
        {
            short delta=(short)((uint)d.mouseData>>16);int steps=Math.Max(1,Math.Abs((int)delta)/120);bool handled=false;
            for(int i=0;i<steps;i++)handled|=InputReceived?.Invoke(name)==true;
            return handled?(IntPtr)1:Next(n,w,l);
        }
        return ProcessPress(name,down,up,n,w,l,d.pt.x,d.pt.y);
    }

    IntPtr ProcessPress(string input,bool down,bool up,int n,IntPtr w,IntPtr l,int x=0,int y=0,bool fireOnDown=false,bool repeatWhileHeld=false)
    {
        bool mapped=HasMapping?.Invoke(input)==true;
        if(down&&!presses.ContainsKey(input))
        {
            var state=new PressState{IsDown=true,Handled=mapped,X=x,Y=y}; presses[input]=state;
            if(mapped)
            {
                state.NativeMouseDrag=IsNativeMouseDrag?.Invoke(input)==true;
                if(state.NativeMouseDrag){state.ReleaseSignal=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);state.Immediate=InputReceived?.Invoke(input+":PressStart")==true;if(state.Immediate){MonitorNativeMouseRelease(input,state);return (IntPtr)1;}state.NativeMouseDrag=false;}
                if(fireOnDown&&HasLongPress?.Invoke(input)!=true){state.FireOnDown=true;InputReceived?.Invoke(input);return (IntPtr)1;}
                if(HasLongPress?.Invoke(input)==true)
                {
                    int ms=Math.Clamp(LongPressDuration?.Invoke(input)??500,50,10000);state.DownTick=Environment.TickCount64;state.LongPressMs=ms;
                    state.Timer=new System.Threading.Timer(_=>{if(state.IsDown&&!state.Dragged&&Interlocked.CompareExchange(ref state.LongFired,1,0)==0){InputReceived?.Invoke(input+":Long");Detected?.Invoke(input+" Long");}},null,ms,System.Threading.Timeout.Infinite);
                }
                return (IntPtr)1;
            }
        }
        else if(down&&presses.TryGetValue(input,out var existing)){if(existing.Handled&&existing.FireOnDown&&repeatWhileHeld)InputReceived?.Invoke(input);return existing.Handled?(IntPtr)1:Next(n,w,l);}
        if(up&&presses.Remove(input,out var current))
        {
            current.IsDown=false; current.Timer?.Dispose();
            if(current.NativeMouseDrag){current.ReleaseSignal?.TrySetResult();return (IntPtr)1;}
            if(current.Handled)
            {
                if(current.Cancelled)return (IntPtr)1;
                if(current.Immediate){if(Interlocked.Exchange(ref current.Ended,1)==0)InputReceived?.Invoke(input+":PressEnd");}
                else if(current.FireOnDown)return (IntPtr)1;
                else if(current.Dragged)InputReceived?.Invoke(input+":DragEnd");
                else if(current.LongPressMs>0&&Environment.TickCount64-current.DownTick>=current.LongPressMs&&Interlocked.CompareExchange(ref current.LongFired,1,0)==0){InputReceived?.Invoke(input+":Long");Detected?.Invoke(input+" Long");}
                else if(Volatile.Read(ref current.LongFired)==0&&mapped)InputReceived?.Invoke(input);
                return (IntPtr)1;
            }
            return Next(n,w,l);
        }
        if(up)return Next(n,w,l);
        return mapped?(IntPtr)1:Next(n,w,l);
    }

    bool HasLayerMappings(string key)=>HasMapping?.Invoke(key+"+*")==true;
    void CancelOtherLayerPresses(string selectedInput)
    {
        if(deferredLayer==null)return;string prefix=deferredLayer+"+";
        foreach(var pair in presses.Where(x=>x.Key.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&!x.Key.Equals(selectedInput,StringComparison.OrdinalIgnoreCase)&&x.Value.IsDown).ToArray())
        {
            var state=pair.Value;state.Cancelled=true;state.Timer?.Dispose();state.ReleaseSignal?.TrySetResult();
            if(state.Immediate&&!state.NativeMouseDrag&&Interlocked.Exchange(ref state.Ended,1)==0)InputReceived?.Invoke(pair.Key+":PressEnd");
        }
    }
    void MonitorNativeMouseRelease(string input,PressState state)
    {
        var callback=InputReceived;
        _=Task.Run(async()=>{try{if(state.ReleaseSignal!=null)await state.ReleaseSignal.Task.WaitAsync(TimeSpan.FromSeconds(10));await Task.Delay(5);}catch(TimeoutException){}finally{if(Interlocked.Exchange(ref state.Ended,1)==0)callback?.Invoke(input+":PressEnd");}});
    }
    void EndImmediateLayerPresses(string layer)
    {
        foreach(var pair in presses.Where(x=>x.Key.StartsWith(layer+"+",StringComparison.OrdinalIgnoreCase)&&x.Value.Immediate&&!x.Value.NativeMouseDrag&&Volatile.Read(ref x.Value.Ended)==0).ToArray()){var state=pair.Value;state.IsDown=false;state.Timer?.Dispose();if(Interlocked.Exchange(ref state.Ended,1)==0)InputReceived?.Invoke(pair.Key+":PressEnd");Detected?.Invoke(pair.Key+" End");}
    }
    static void QueueMouseLayerRelease(string layer)
    {
        if(!TryGetMouseLayerRelease(layer,out _,out _))return;
        _=Task.Run(()=>ReleaseMouseLayerButtonIfInjected(layer));
    }
    static void ReleaseMouseLayerButtonIfInjected(string layer)
    {
        if(!TryGetMouseLayerRelease(layer,out uint flag,out uint data))return;
        int button=layer.ToUpperInvariant() switch
        {
            "MOUSELEFT"=>1,"MOUSERIGHT"=>2,"MOUSEMIDDLE"=>3,"MOUSEBACK"=>4,
            "MOUSEFORWARD" or "MOUSEX"=>5,_=>0
        };
        lock(OutputLock)
        {
            // A layer press is swallowed before Windows sees it. Sending an unmatched
            // button-up can itself trigger Back/Forward or a context menu in some apps.
            // Release only a button that RELYR actually injected (for example a native
            // right-button drag); ordinary layer chords therefore emit no source click.
            if(button!=0&&InjectedMouseButtonsDown.Contains(button))SendMouseUpWithRetry(flag,data);
        }
    }
    static bool TryGetMouseLayerRelease(string layer,out uint flag,out uint data)
    {
        (flag,data)=layer.ToUpperInvariant() switch
        {
            "MOUSELEFT"=>(4u,0u),"MOUSERIGHT"=>(16u,0u),"MOUSEMIDDLE"=>(64u,0u),
            "MOUSEBACK"=>(0x100u,1u),"MOUSEFORWARD" or "MOUSEX"=>(0x100u,2u),_=>(0u,0u)
        };
        return flag!=0;
    }
    bool EmergencyHeld(int vk)=>vk==0x7B&&HeldAny(0x11,0xA2,0xA3)&&HeldAny(0x12,0xA4,0xA5)&&HeldAny(0x10,0xA0,0xA1);
    bool HeldAny(params int[] keys)=>keys.Any(held.Contains);
    void SendSpaceRepeat(){if(RepeatOutputForTest is { } testOutput)testOutput();else SendShortcut("Space");}
    void CancelLayerRepeat(){Interlocked.Increment(ref layerRepeatGeneration);layerRepeatTimer?.Dispose();layerRepeatTimer=null;layerRepeatActive=false;}
    string? PendingLayerInput(string releasedKey)=>presses.Keys.FirstOrDefault(input=>input.EndsWith("+"+releasedKey,StringComparison.OrdinalIgnoreCase));
    bool IsLayerRelease(string layer,int vk,uint scanCode,bool up)=>up&&layer switch{"CapsLock"=>TreatF13AsCapsLock&&vk==0x7C,"Space"=>scanCode==0x39||vk==0x20,_=>false};
    void ArmLayerSafety()
    {
        CancelLayerSafety();
        layerSafetyTimer=new System.Threading.Timer(_=>layerSafetyExpired=true,null,10000,System.Threading.Timeout.Infinite);
    }
    void CancelLayerSafety(){layerSafetyTimer?.Dispose();layerSafetyTimer=null;layerSafetyExpired=false;}
    internal void ExpireLayerForTest()=>layerSafetyExpired=true;
    void ResetCapturedState(bool release){string? releasedLayer;lock(stateLock){releasedLayer=deferredLayer;foreach(var state in presses.Values){state.IsDown=false;state.Timer?.Dispose();Interlocked.Exchange(ref state.Ended,1);state.ReleaseSignal?.TrySetResult();}presses.Clear();held.Clear();CancelLayerRepeat();CancelLayerSafety();deferredLayer=null;layerUsed=false;nativeRightLayerDrag=false;}QueueMouseLayerRelease(releasedLayer??"");if(release)ReleaseAll();else EndModifierDrag();}
    void StopAndRelease(){enabled=false;ResetCapturedState(false);_=Task.Run(async()=>{ReleaseAll();if(ExitOnEmergency){await Task.Delay(80);Environment.Exit(2);}});}
    static double Distance(int x1,int y1,int x2,int y2)=>Math.Sqrt(Math.Pow(x2-x1,2)+Math.Pow(y2-y1,2));
    static string UsKeyName(int vk)=>vk switch{0xC0=>"`",0xBD=>"-",0xBB=>"=",0xDB=>"[",0xDD=>"]",0xDC=>"\\",0xBA=>";",0xDE=>"'",_=>KeyName(vk)};
    internal static string HookKeyName(int vk,uint scanCode,bool useUsLayout,bool treatF13AsCapsLock)
    {
        string key=useUsLayout?UsKeyName(vk):KeyName(vk);
        return treatF13AsCapsLock&&key=="F13"?"CapsLock":key;
    }
    internal static string KeyName(int vk)=>vk switch
    {
        >=0x30 and <=0x39=>((char)vk).ToString(),>=0x41 and <=0x5A=>((char)vk).ToString(),
        >=0x60 and <=0x69=>"NumPad"+(vk-0x60),>=0x70 and <=0x87=>"F"+(vk-0x6F),
        0x08=>"Back",0x09=>"Tab",0x0D=>"Enter",0x10=>"LeftShift",0x11=>"LeftCtrl",0x12=>"LeftAlt",0x13=>"Pause",0x1B=>"Esc",0x20=>"Space",
        0x21=>"PageUp",0x22=>"PageDown",0x23=>"End",0x24=>"Home",0x25=>"Left",0x26=>"Up",0x27=>"Right",0x28=>"Down",0x2D=>"Insert",0x2E=>"Delete",
        0x5B=>"LWin",0x5C=>"RWin",0x6A=>"Multiply",0x6B=>"Add",0x6D=>"Subtract",0x6E=>"Decimal",0x6F=>"Divide",0x90=>"NumLock",
        0xA0=>"LeftShift",0xA1=>"RightShift",0xA2=>"LeftCtrl",0xA3=>"RightCtrl",0xA4=>"LeftAlt",0xA5=>"RightAlt",
        0xF3=>"半角/全角",0x1D=>"無変換",0x1C=>"変換",0x15=>"カタカナ",0x14=>"CapsLock",0x2C=>"PrintScreen",0x91=>"ScrollLock",
        0xBD=>"-",0xDE=>"^",0xDC=>"¥",0xC0=>"@",0xDB=>"[",0xBB=>";",0xBA=>":",0xDD=>"]",0xBC=>",",0xBE=>".",0xBF=>"/",0xE2=>"_",
        _=>KeyInterop.KeyFromVirtualKey(vk).ToString()
    };
    IntPtr Next(int n,IntPtr w,IntPtr l)=>NextHookForTest?.Invoke(n,w,l)??CallNextHookEx(IntPtr.Zero,n,w,l);

    public static void SendShortcut(string value,bool useUsLayout=false)
    {
        value=ResolveShortcutAlias(value);
        if(IsLockWorkStationShortcut(value))
        {
            bool locked=LockWorkStationOutputForTest?.Invoke()??LockWorkStation();
            if(!locked)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Windowsをロックできませんでした。");
            return;
        }
        if(TryGetImeAction(value,out int imeMode)){QueueImeAction(imeMode);return;}
        // 仮想デスクトップの左右移動は疑似キーを送らない。Ctrl+Win+矢印を
        // 外部ツールが横取りしてウィンドウ移動へ変える環境でも、表示中の
        // デスクトップだけを確実に切り替える。
        if(TryGetDirectDesktopStep(value,out int desktopStep)){QueueDesktopAction(()=>VirtualDesktopAccessor.GoToNumber(VirtualDesktopAccessor.CurrentNumber+desktopStep));return;}
        if(value.Equals("MoveWindowDesktopRight",StringComparison.OrdinalIgnoreCase)){QueueWindowMove(1);return;}
        if(value.Equals("MoveWindowDesktopLeft",StringComparison.OrdinalIgnoreCase)){QueueWindowMove(-1);return;}
        if(value.Equals("ToggleMaximizeUnderCursor",StringComparison.OrdinalIgnoreCase)){QueueDesktopAction(WindowMonitorService.ToggleMaximizeUnderCursor);return;}
        if(value.Equals("MinimizeActiveWindow",StringComparison.OrdinalIgnoreCase)){QueueDesktopAction(WindowMonitorService.MinimizeForeground);return;}
        if(value.StartsWith("MoveWindowMonitor",StringComparison.OrdinalIgnoreCase)&&Enum.TryParse<WindowMonitorService.Direction>(value[17..],true,out var monitorDirection)){QueueDesktopAction(()=>WindowMonitorService.MoveForeground(monitorDirection));return;}
        if(value.StartsWith("Desktop",StringComparison.OrdinalIgnoreCase)&&int.TryParse(value[7..],out int desktop)){QueueDesktopAction(()=>VirtualDesktopAccessor.GoToNumber(desktop-1));return;}
        var names=SplitShortcut(value);
        string? mouse=names.FirstOrDefault(x=>x.StartsWith("Mouse",StringComparison.OrdinalIgnoreCase)||x.StartsWith("Wheel",StringComparison.OrdinalIgnoreCase)||x.StartsWith("Tilt",StringComparison.OrdinalIgnoreCase));
        var keyNames=names.Where(x=>x!=mouse).ToArray();var codes=new List<ushort>();
        foreach(string keyName in keyNames)
        {
            if(TryResolveShiftedSymbol(keyName,useUsLayout,out ushort symbolKey)){if(!codes.Contains(0x10))codes.Add(0x10);codes.Add(symbolKey);continue;}
            ushort code=ParseKey(keyName);if(code==0)throw new ArgumentException($"認識できないキーです: {keyName}");codes.Add(code);
        }
        lock(OutputLock)
        {
            if(mouse?.StartsWith("Wheel",StringComparison.OrdinalIgnoreCase)==true||mouse?.StartsWith("Tilt",StringComparison.OrdinalIgnoreCase)==true){long wait=4-(Environment.TickCount64-lastWheelOutput);if(wait>0)Thread.Sleep((int)wait);lastWheelOutput=Environment.TickCount64;}
            var pressed=new List<ushort>();try{foreach(var c in codes){if(SendKey(c,false))pressed.Add(c);}if(mouse!=null)SendMouse(mouse);}finally{foreach(var c in pressed.AsEnumerable().Reverse())SendKeyUpWithRetry(c);}
        }
    }
    static string ResolveShortcutAlias(string value)
    {
        if(value.Equals("CloseActiveWindow",StringComparison.OrdinalIgnoreCase))return "Alt+F4";
        if(!value.Equals("ToggleMinimizeAllWindows",StringComparison.OrdinalIgnoreCase))return value;
        lock(OutputLock)
        {
            string shortcut=restoreMinimizedWindowsNext?"Shift+Win+M":"Win+M";
            restoreMinimizedWindowsNext=!restoreMinimizedWindowsNext;
            return shortcut;
        }
    }
    internal static string ResolveShortcutAliasForTest(string value)=>ResolveShortcutAlias(value);
    internal static void ResetMinimizeAllToggleForTest(){lock(OutputLock)restoreMinimizedWindowsNext=false;}
    static bool IsLockWorkStationShortcut(string value)
    {
        var names=SplitShortcut(value);
        return names.Length==2&&names.Any(x=>x.Equals("L",StringComparison.OrdinalIgnoreCase))&&names.Any(x=>x.Equals("Win",StringComparison.OrdinalIgnoreCase)||x.Equals("LWin",StringComparison.OrdinalIgnoreCase)||x.Equals("RWin",StringComparison.OrdinalIgnoreCase));
    }
    static string[] SplitShortcut(string value)
    {
        if(value=="+")return ["+"];
        var names=value.Split('+',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        return value.EndsWith("++",StringComparison.Ordinal)?names.Append("+").ToArray():names;
    }
    static bool TryResolveShiftedSymbol(string value,bool useUsLayout,out ushort key)
    {
        key=0;if(value.Length!=1)return false;
        key=(ushort)(useUsLayout?value[0] switch
        {
            '~'=>0xC0,'!'=>0x31,'@'=>0x32,'#'=>0x33,'$'=>0x34,'%'=>0x35,'^'=>0x36,'&'=>0x37,'*'=>0x38,'('=>0x39,')'=>0x30,
            '_'=>0xBD,'+'=>0xBB,'{'=>0xDB,'}'=>0xDD,'|'=>0xDC,':'=>0xBA,'"'=>0xDE,'<'=>0xBC,'>'=>0xBE,'?'=>0xBF,_=>0
        }:value[0] switch
        {
            '!'=>0x31,'"'=>0x32,'#'=>0x33,'$'=>0x34,'%'=>0x35,'&'=>0x36,'\''=>0x37,'('=>0x38,')'=>0x39,'='=>0xBD,
            '~'=>0xDE,'|'=>0xDC,'`'=>0xC0,'{'=>0xDB,'+'=>0xBB,'*'=>0xBA,'}'=>0xDD,'<'=>0xBC,'>'=>0xBE,'?'=>0xBF,'_'=>0xE2,_=>0
        });
        return key!=0;
    }
    internal static bool TryResolveShiftedSymbolForTest(string value,bool useUsLayout,out ushort key)=>TryResolveShiftedSymbol(value,useUsLayout,out key);
    internal static bool IsRecognizedShortcut(string value)
    {
        if(TryGetImeAction(value,out _))return true;
        if(value.Equals("MoveWindowDesktopRight",StringComparison.OrdinalIgnoreCase)||value.Equals("MoveWindowDesktopLeft",StringComparison.OrdinalIgnoreCase)||value.Equals("ToggleMaximizeUnderCursor",StringComparison.OrdinalIgnoreCase)||value.Equals("MinimizeActiveWindow",StringComparison.OrdinalIgnoreCase)||value.Equals("CloseActiveWindow",StringComparison.OrdinalIgnoreCase)||value.Equals("ToggleMinimizeAllWindows",StringComparison.OrdinalIgnoreCase))return true;
        if(value.StartsWith("MoveWindowMonitor",StringComparison.OrdinalIgnoreCase))return Enum.TryParse<WindowMonitorService.Direction>(value[17..],true,out _);
        if(value.StartsWith("Desktop",StringComparison.OrdinalIgnoreCase))return int.TryParse(value[7..],out int desktop)&&desktop is >=1 and <=8;
        var names=value.Split('+',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        return names.Length>0&&names.All(name=>ParseKey(name)!=0||name.StartsWith("Mouse",StringComparison.OrdinalIgnoreCase)||name.StartsWith("Wheel",StringComparison.OrdinalIgnoreCase)||name.StartsWith("Tilt",StringComparison.OrdinalIgnoreCase));
    }
    internal static bool TryGetDirectDesktopStep(string value,out int offset)
    {
        if(value.Equals("Ctrl+Win+Left",StringComparison.OrdinalIgnoreCase)){offset=-1;return true;}
        if(value.Equals("Ctrl+Win+Right",StringComparison.OrdinalIgnoreCase)){offset=1;return true;}
        offset=0;return false;
    }
    internal static bool TryGetImeAction(string value,out int mode)
    {
        if(value.Equals("ImeOff",StringComparison.OrdinalIgnoreCase)){mode=0;return true;}
        if(value.Equals("ImeOn",StringComparison.OrdinalIgnoreCase)){mode=1;return true;}
        if(value.Equals("ImeToggle",StringComparison.OrdinalIgnoreCase)){mode=2;return true;}
        mode=-1;return false;
    }
    static void QueueImeAction(int mode)
    {
        if(ImeActionOutputForTest is { } output){output(mode);return;}
        IntPtr window=GetForegroundWindow();
        if(window==IntPtr.Zero)throw new InvalidOperationException("IMEを切り替える対象ウィンドウがありません。");
        QueueDesktopAction(()=>ApplyImeAction(window,mode));
    }
    static void ApplyImeAction(IntPtr window,int mode)
    {
        IntPtr imeWindow=ImmGetDefaultIMEWnd(window);
        if(imeWindow!=IntPtr.Zero)
        {
            if(SendMessageTimeout(imeWindow,0x0283,(IntPtr)0x0005,IntPtr.Zero,0x0002,1000,out UIntPtr current)==IntPtr.Zero)throw new InvalidOperationException("IMEの現在状態を取得できませんでした。");
            bool enabled=mode==2?current==UIntPtr.Zero:mode==1;
            if(SendMessageTimeout(imeWindow,0x0283,(IntPtr)0x0006,enabled?(IntPtr)1:IntPtr.Zero,0x0002,1000,out _)==IntPtr.Zero)throw new InvalidOperationException("IMEの状態を変更できませんでした。");
            return;
        }
        IntPtr context=ImmGetContext(window);
        if(context==IntPtr.Zero)throw new InvalidOperationException("このウィンドウではIMEを切り替えられません。");
        try{bool enabled=mode==2?!ImmGetOpenStatus(context):mode==1;if(!ImmSetOpenStatus(context,enabled))throw new InvalidOperationException("IMEの状態を変更できませんでした。");}
        finally{ImmReleaseContext(window,context);}
    }
    static void QueueWindowMove(int offset)
    {
        IntPtr window=VirtualDesktopService.GetForegroundRootWindow();
        QueueDesktopAction(()=>{VirtualDesktopAccessor.MoveWindowAndFollow(window,offset);_=Task.Run(async()=>{await Task.Delay(350);VirtualDesktopService.ActivateWindow(window);});});
    }
    internal static void QueueDesktopAction(Action action)
    {
        if(DesktopActionOutputForTest is { } testOutput){testOutput(action);return;}
        DesktopActions.Add(action);
    }
    internal static void MoveWindowAndFollowForTest(IntPtr window,int offset)
    {
        VirtualDesktopAccessor.MoveWindowAndFollow(window,offset);
        _=Task.Run(async()=>{await Task.Delay(350);VirtualDesktopService.ActivateWindow(window);});
    }
    static void SendChord(params ushort[] keys){var pressed=new List<ushort>();try{foreach(var key in keys)if(SendKey(key,false))pressed.Add(key);}finally{foreach(var key in pressed.AsEnumerable().Reverse())SendKeyUpWithRetry(key);}}
    public static void SendText(string value,bool useUsLayout=false)
    {
        if(string.IsNullOrEmpty(value))return;
        // Chromium/Electron の contenteditable は、VK_PACKET の単一記号に続く
        // Enter でキャレットを再配置することがある。通常キーで表せる1文字は
        // AHK と同じ物理キー列で送り、それ以外だけを Unicode 入力にする。
        if(value.Length==1&&TryResolveTextCharacter(value[0],useUsLayout,out ushort key,out bool shift))
        {
            SendTextKey(key,shift);
            return;
        }
        SendUnicodeText(value);
    }
    static bool TryResolveTextCharacter(char value,bool useUsLayout,out ushort key,out bool shift)
    {
        key=0;shift=false;
        if(value is >= 'a' and <= 'z'){key=(ushort)char.ToUpperInvariant(value);return true;}
        if(value is >= 'A' and <= 'Z'){key=value;shift=true;return true;}
        if(value is >= '0' and <= '9'){key=value;return true;}
        if(value==' '){key=0x20;return true;}
        if(TryResolveShiftedSymbol(value.ToString(),useUsLayout,out key)){shift=true;return true;}
        key=(ushort)(useUsLayout?value switch
        {
            '`'=>0xC0,'-'=>0xBD,'='=>0xBB,'['=>0xDB,']'=>0xDD,'\\'=>0xDC,';'=>0xBA,'\''=>0xDE,','=>0xBC,'.'=>0xBE,'/'=>0xBF,_=>0
        }:value switch
        {
            '-'=>0xBD,'^'=>0xDE,'¥'=>0xDC,'@'=>0xC0,'['=>0xDB,';'=>0xBB,':'=>0xBA,']'=>0xDD,','=>0xBC,'.'=>0xBE,'/'=>0xBF,'\\'=>0xE2,_=>0
        });
        return key!=0;
    }
    static void SendTextKey(ushort key,bool shift)
    {
        bool shiftDown=false,keyDown=false;
        lock(OutputLock)
        {
            try
            {
                if(shift){shiftDown=SendKey(0x10,false);if(!shiftDown)throw new InvalidOperationException("文字入力用のShiftキーを送信できませんでした。");}
                keyDown=SendKey(key,false);if(!keyDown)throw new InvalidOperationException("文字入力キーを送信できませんでした。");
            }
            finally
            {
                if(keyDown)SendKeyUpWithRetry(key);
                if(shiftDown)SendKeyUpWithRetry(0x10);
            }
        }
    }
    static void SendUnicodeText(string value)
    {
        if(UnicodeTextOutputForTest is { } testOutput){testOutput(value);return;}
        var inputs=new INPUT[value.Length*2];
        for(int i=0;i<value.Length;i++)
        {
            inputs[i*2]=new INPUT{type=1,U=new InputUnion{ki=new KEYBDINPUT{wScan=value[i],dwFlags=4u,dwExtraInfo=(UIntPtr)Marker}}};
            inputs[i*2+1]=new INPUT{type=1,U=new InputUnion{ki=new KEYBDINPUT{wScan=value[i],dwFlags=6u,dwExtraInfo=(UIntPtr)Marker}}};
        }
        lock(OutputLock)if(SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<INPUT>())!=(uint)inputs.Length)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"文字列を入力できませんでした。");
    }
    public static void SendRecordedEvent(string recordedEvent)
    {
        if(recordedEvent.StartsWith("MouseMoveRelative:",StringComparison.OrdinalIgnoreCase))
        {
            var parts=recordedEvent[18..].Split(',');if(parts.Length!=2||!int.TryParse(parts[0],out int dx)||!int.TryParse(parts[1],out int dy))throw new ArgumentException("認識できないマウス移動量です: "+recordedEvent);
            SendInput(1,[new INPUT{type=0,U=new InputUnion{mi=new MOUSEINPUT{dx=dx,dy=dy,dwFlags=1,dwExtraInfo=(UIntPtr)Marker}}}],Marshal.SizeOf<INPUT>());return;
        }
        if(recordedEvent.StartsWith("MouseMove:",StringComparison.OrdinalIgnoreCase))
        {
            var parts=recordedEvent[10..].Split(',');if(parts.Length!=2||!int.TryParse(parts[0],out int x)||!int.TryParse(parts[1],out int y))throw new ArgumentException("認識できないマウス座標です: "+recordedEvent);
            int left=GetSystemMetrics(76),top=GetSystemMetrics(77),width=Math.Max(1,GetSystemMetrics(78)-1),height=Math.Max(1,GetSystemMetrics(79)-1);SendInput(1,[new INPUT{type=0,U=new InputUnion{mi=new MOUSEINPUT{dx=(int)Math.Clamp((long)(x-left)*65535/width,0,65535),dy=(int)Math.Clamp((long)(y-top)*65535/height,0,65535),dwFlags=0xC001,dwExtraInfo=(UIntPtr)Marker}}}],Marshal.SizeOf<INPUT>());return;
        }
        bool up=recordedEvent.EndsWith(" Up",StringComparison.OrdinalIgnoreCase),down=recordedEvent.EndsWith(" Down",StringComparison.OrdinalIgnoreCase);
        if(!up&&!down)throw new ArgumentException("認識できないマクロイベントです: "+recordedEvent);
        string name=recordedEvent[..^(up?3:5)].TrimEnd();
        switch(name.ToUpperInvariant())
        {
            case "MOUSELEFT":if(up)SendMouseUpWithRetry(4);else SendMouseFlag(2);return;case "MOUSERIGHT":if(up)SendMouseUpWithRetry(16);else SendMouseFlag(8);return;case "MOUSEMIDDLE":if(up)SendMouseUpWithRetry(64);else SendMouseFlag(32);return;
            case "MOUSEBACK":if(up)SendMouseUpWithRetry(0x100,1);else SendMouseFlag(0x80,1);return;case "MOUSEFORWARD" or "MOUSEX":if(up)SendMouseUpWithRetry(0x100,2);else SendMouseFlag(0x80,2);return;
            case "WHEELUP":if(down)SendMouse("WheelUp");return;case "WHEELDOWN":if(down)SendMouse("WheelDown");return;case "TILTLEFT":if(down)SendMouse("TiltLeft");return;case "TILTRIGHT":if(down)SendMouse("TiltRight");return;
        }
        ushort key=ParseKey(name);if(key==0)throw new ArgumentException("認識できないマクロキーです: "+name);if(up)SendKeyUpWithRetry(key);else SendKey(key,false);
    }
    internal static bool IsValidRecordedEvent(string recordedEvent)
    {
        if(recordedEvent.StartsWith("MouseMoveRelative:",StringComparison.OrdinalIgnoreCase)){var p=recordedEvent[18..].Split(',');return p.Length==2&&int.TryParse(p[0],out _)&&int.TryParse(p[1],out _);}
        if(recordedEvent.StartsWith("MouseMove:",StringComparison.OrdinalIgnoreCase)){var p=recordedEvent[10..].Split(',');return p.Length==2&&int.TryParse(p[0],out _)&&int.TryParse(p[1],out _);}
        bool suffix=recordedEvent.EndsWith(" Down",StringComparison.OrdinalIgnoreCase)||recordedEvent.EndsWith(" Up",StringComparison.OrdinalIgnoreCase);if(!suffix)return false;
        string name=recordedEvent[..^(recordedEvent.EndsWith(" Up",StringComparison.OrdinalIgnoreCase)?3:5)].TrimEnd();
        if(new[]{"MouseLeft","MouseRight","MouseMiddle","MouseBack","MouseForward","MouseX","WheelUp","WheelDown","TiltLeft","TiltRight"}.Contains(name,StringComparer.OrdinalIgnoreCase))return true;
        return ParseKey(name)!=0;
    }
    public static void SendMouse(string action)
    {
        if(TryModifierDragAction(action,out ushort modifier,out int phase)){SendModifierDrag(modifier,phase);return;}
        switch(action.ToUpperInvariant())
        {
            case "LEFTDOWN":SendMouseFlag(2);return;case "LEFTUP":SendMouseUpWithRetry(4);return;
            case "RIGHTDOWN":SendMouseFlag(8);return;case "RIGHTUP":SendMouseUpWithRetry(16);return;
            case "MIDDLEDOWN":SendMouseFlag(32);return;case "MIDDLEUP":SendMouseUpWithRetry(64);return;
        }
        (uint down,uint up)=action.ToUpperInvariant() switch{"LEFT" or "CLICK" or "MOUSELEFT"=>(2u,4u),"RIGHT" or "MOUSERIGHT"=>(8u,16u),"MIDDLE" or "MOUSEMIDDLE"=>(32u,64u),_ =>(0u,0u)};
        if(down!=0){SendMouseFlag(down);SendMouseUpWithRetry(up);}else if(action.Equals("MouseBack",StringComparison.OrdinalIgnoreCase)){SendMouseFlag(0x80,1);SendMouseUpWithRetry(0x100,1);}else if(action.Equals("MouseForward",StringComparison.OrdinalIgnoreCase)||action.Equals("MouseX",StringComparison.OrdinalIgnoreCase)){SendMouseFlag(0x80,2);SendMouseUpWithRetry(0x100,2);}else if(action.Equals("WheelUp",StringComparison.OrdinalIgnoreCase))SendMouseFlag(0x800,120);else if(action.Equals("WheelDown",StringComparison.OrdinalIgnoreCase))SendMouseFlag(0x800,unchecked((uint)-120));else if(action.Equals("TiltRight",StringComparison.OrdinalIgnoreCase))SendMouseFlag(0x1000,120);else if(action.Equals("TiltLeft",StringComparison.OrdinalIgnoreCase))SendMouseFlag(0x1000,unchecked((uint)-120));else throw new ArgumentException("認識できないマウス操作です: "+action);
    }
    static void SendMouseClickAtomic(string action)
    {
        (uint down,uint up,uint data,int button)=action.ToUpperInvariant() switch
        {
            "MOUSELEFT"=>(2u,4u,0u,1),"MOUSERIGHT"=>(8u,16u,0u,2),"MOUSEMIDDLE"=>(32u,64u,0u,3),
            "MOUSEBACK"=>(0x80u,0x100u,1u,4),"MOUSEFORWARD" or "MOUSEX"=>(0x80u,0x100u,2u,5),_=>(0u,0u,0u,0)
        };
        if(down==0){SendMouse(action);return;}
        lock(OutputLock)
        {
            var batch=new[]{(Flag:down,Data:data),(Flag:up,Data:data)};
            if(MouseClickBatchOutputForTest is { } testBatch){testBatch(batch);return;}
            if(MouseFlagOutputForTest is { } testOutput){testOutput(down,data);testOutput(up,data);return;}
            var inputs=new[]
            {
                new INPUT{type=0,U=new InputUnion{mi=new MOUSEINPUT{dwFlags=down,mouseData=data,dwExtraInfo=(UIntPtr)Marker}}},
                new INPUT{type=0,U=new InputUnion{mi=new MOUSEINPUT{dwFlags=up,mouseData=data,dwExtraInfo=(UIntPtr)Marker}}}
            };
            uint sent=SendInput(2,inputs,Marshal.SizeOf<INPUT>());
            InjectedMouseButtonsDown.Remove(button);InjectedMouseDownAt.Remove(button);
            if(sent==2)return;
            if(sent==0)SendMouseFlag(down,data);
            SendMouseUpWithRetry(up,data);
        }
    }

    internal static void NeutralizePhysicalSourceKey(string input)
    {
        int separator=input.LastIndexOf('+');string source=(separator>=0?input[(separator+1)..]:input).Trim();
        if(source.StartsWith("Mouse",StringComparison.OrdinalIgnoreCase)||source.StartsWith("Wheel",StringComparison.OrdinalIgnoreCase)||source.StartsWith("Tilt",StringComparison.OrdinalIgnoreCase))return;
        ushort key=ParseKey(source);if(key!=0)SendKeyUpWithRetry(key);
    }

    static bool TryModifierDragAction(string action,out ushort modifier,out int phase)
    {
        string value=action.Trim();modifier=value.StartsWith("ShiftDrag",StringComparison.OrdinalIgnoreCase)?(ushort)0x10:value.StartsWith("CtrlDrag",StringComparison.OrdinalIgnoreCase)?(ushort)0x11:(ushort)0;
        if(modifier==0){phase=0;return false;}
        string name=modifier==0x10?"ShiftDrag":"CtrlDrag";
        if(value.Equals(name,StringComparison.OrdinalIgnoreCase)){phase=0;return true;}
        if(value.Equals(name+":Start",StringComparison.OrdinalIgnoreCase)){phase=1;return true;}
        if(value.Equals(name+":End",StringComparison.OrdinalIgnoreCase)){phase=2;return true;}
        modifier=0;phase=0;return false;
    }
    static void SendModifierDrag(ushort modifier,int phase)
    {
        lock(OutputLock)
        {
            if(phase==2){EndModifierDragLocked();return;}
            BeginModifierDragLocked(modifier);
            if(phase==0)EndModifierDragLocked();
        }
    }
    static void BeginModifierDragLocked(ushort modifier)
    {
        EndModifierDragLocked();modifierDragKey=modifier;
        if(!SendKey(modifier,false)){modifierDragKey=0;throw new InvalidOperationException("ドラッグ用の修飾キーを押せませんでした。");}
        if(!SendMouseFlag(2)){EndModifierDragLocked();throw new InvalidOperationException("ドラッグ用の左ボタンを押せませんでした。");}
        modifierDragMouseDown=true;
        modifierDragStartedAt=Environment.TickCount64;
        modifierDragSafetyTimer=new System.Threading.Timer(_=>{if(!(PhysicalKeyDownForTest?.Invoke(0x01)??((GetAsyncKeyState(0x01)&0x8000)!=0))||Environment.TickCount64-Interlocked.Read(ref modifierDragStartedAt)>30000)EndModifierDrag();},null,100,25);
    }
    public static void EndModifierDrag(){lock(OutputLock)EndModifierDragLocked();}
    static void EndModifierDragLocked()
    {
        ushort key=modifierDragKey;bool mouseDown=modifierDragMouseDown;modifierDragKey=0;modifierDragMouseDown=false;Interlocked.Exchange(ref modifierDragStartedAt,0);modifierDragSafetyTimer?.Dispose();modifierDragSafetyTimer=null;
        if(mouseDown)SendMouseUpWithRetry(4);
        if(key!=0)SendKeyUpWithRetry(key);
        if((mouseDown&&InjectedMouseButtonsDown.Contains(1))||(key!=0&&InjectedKeysDown.Contains(key)))ReleaseAll();
    }
    static ushort ParseKey(string s)=>s.ToUpperInvariant() switch{"半角/全角"=>0xF3,"無変換"=>0x1D,"変換"=>0x1C,"カタカナ"=>0x15,"PRINTSCREEN"=>0x2C,"CTRL"=>0x11,"SHIFT"=>0x10,"ALT"=>0x12,"WIN"=>0x5B,"CAPSLOCK"=>0x14,"NUMPADENTER"=>0x0D,"LEFT"=>0x25,"UP"=>0x26,"RIGHT"=>0x27,"DOWN"=>0x28,"ENTER"=>0x0D,"ESC"=>0x1B,"SPACE"=>0x20,"BACK" or "BACKSPACE"=>8,"DELETE"=>0x2E,_ when s.Length==1=>(ushort)(VkKeyScan(s[0])&0xff),_=>(ushort)KeyInterop.VirtualKeyFromKey(Enum.TryParse<Key>(s,true,out var k)?k:Key.None)};
    static bool SendKey(ushort vk,bool up)
    {
        lock(OutputLock)
        {
            bool sent=KeyOutputForTest?.Invoke(vk,up)??(SendInput(1,[new INPUT{type=1,U=new InputUnion{ki=new KEYBDINPUT{wVk=vk,dwFlags=up?2u:0,dwExtraInfo=(UIntPtr)Marker}}}],Marshal.SizeOf<INPUT>())==1);
            if(sent){if(up){InjectedKeysDown.Remove(vk);InjectedKeyDownAt.Remove(vk);}else{InjectedKeysDown.Add(vk);InjectedKeyDownAt[vk]=Environment.TickCount64;}}
            return sent;
        }
    }
    static bool SendKeyUpWithRetry(ushort vk)
    {
        for(int attempt=0;attempt<3;attempt++)if(SendKey(vk,true))return true;
        if(KeyOutputForTest!=null)return false;
        keybd_event((byte)vk,0,2,(UIntPtr)Marker);InjectedKeysDown.Remove(vk);InjectedKeyDownAt.Remove(vk);return true;
    }
    static bool SendMouseFlag(uint flag,uint data=0)
    {
        lock(OutputLock)
        {
            int button=flag switch{2=>1,4=>-1,8=>2,16=>-2,32=>3,64=>-3,0x80=>data==1?4:5,0x100=>data==1?-4:-5,_=>0};
            bool sent;if(MouseFlagOutputForTest is { } testOutput){testOutput(flag,data);sent=true;}else sent=SendInput(1,[new INPUT{type=0,U=new InputUnion{mi=new MOUSEINPUT{dwFlags=flag,mouseData=data,dwExtraInfo=(UIntPtr)Marker}}}],Marshal.SizeOf<INPUT>())==1;
            if(sent){if(button>0){InjectedMouseButtonsDown.Add(button);InjectedMouseDownAt[button]=Environment.TickCount64;}else if(button<0){InjectedMouseButtonsDown.Remove(-button);InjectedMouseDownAt.Remove(-button);}}
            return sent;
        }
    }
    static bool SendMouseUpWithRetry(uint flag,uint data=0)
    {
        for(int attempt=0;attempt<3;attempt++)if(SendMouseFlag(flag,data))return true;
        if(MouseFlagOutputForTest!=null)return false;
        mouse_event(flag,0,0,data,(UIntPtr)Marker);int button=flag switch{4=>1,16=>2,64=>3,0x100=>data==1?4:5,_=>0};if(button>0){InjectedMouseButtonsDown.Remove(button);InjectedMouseDownAt.Remove(button);}return true;
    }
    static void ReleaseStaleInjectedInputs()
    {
        lock(OutputLock)
        {
            long now=Environment.TickCount64;
            foreach(var pair in InjectedKeyDownAt.Where(x=>now-x.Value>=5000).ToArray())SendKeyUpWithRetry(pair.Key);
            foreach(var pair in InjectedMouseDownAt.Where(x=>now-x.Value>=5000).ToArray())switch(pair.Key){case 1:SendMouseUpWithRetry(4);break;case 2:SendMouseUpWithRetry(16);break;case 3:SendMouseUpWithRetry(64);break;case 4:SendMouseUpWithRetry(0x100,1);break;case 5:SendMouseUpWithRetry(0x100,2);break;}
        }
    }
    public static void ReleaseAll()
    {
        lock(OutputLock)
        {
            foreach(ushort key in InjectedKeysDown.ToArray())SendKeyUpWithRetry(key);
            foreach(int button in InjectedMouseButtonsDown.ToArray())switch(button){case 1:SendMouseUpWithRetry(4);break;case 2:SendMouseUpWithRetry(16);break;case 3:SendMouseUpWithRetry(64);break;case 4:SendMouseUpWithRetry(0x100,1);break;case 5:SendMouseUpWithRetry(0x100,2);break;}
            modifierDragKey=0;modifierDragMouseDown=false;Interlocked.Exchange(ref modifierDragStartedAt,0);modifierDragSafetyTimer?.Dispose();modifierDragSafetyTimer=null;
        }
    }
    internal static bool HasInjectedInputForTest(){lock(OutputLock)return InjectedKeysDown.Count>0||InjectedMouseButtonsDown.Count>0;}
    internal static void ExpireInjectedInputsForTest(){lock(OutputLock){foreach(var key in InjectedKeysDown)InjectedKeyDownAt[key]=0;foreach(var button in InjectedMouseButtonsDown)InjectedMouseDownAt[button]=0;}ReleaseStaleInjectedInputs();}
    public void EnableDirectTestInput()=>directTestTarget=this;
    public void DisableDirectTestInputForTest(){if(ReferenceEquals(directTestTarget,this))directTestTarget=null;}
    public static void InjectKeyForTest(string key,bool up){var vk=ParseKey(key);if(vk==0)throw new ArgumentException("Unknown key: "+key);if(directTestTarget is { } target){target.DirectKey(vk,up);return;}SendInput(1,[new INPUT{type=1,U=new InputUnion{ki=new KEYBDINPUT{wVk=vk,dwFlags=up?2u:0,dwExtraInfo=UIntPtr.Zero}}}],Marshal.SizeOf<INPUT>());}
    internal static void InjectRawKeyForTest(ushort vk,uint scanCode,bool up){if(directTestTarget is not { } target)throw new InvalidOperationException("Direct test input is not enabled.");target.DirectKey(vk,scanCode,up);}
    public static void InjectMouseForTest(string button,bool up){if(directTestTarget is { } target){int message=button.ToUpperInvariant() switch{"LEFT"=>up?0x202:0x201,"RIGHT"=>up?0x205:0x204,"MIDDLE"=>up?0x208:0x207,_=>throw new ArgumentException("Unknown mouse button")};target.DirectMouse(message);return;}uint flag=button.ToUpperInvariant() switch{"LEFT"=>up?4u:2u,"RIGHT"=>up?16u:8u,"MIDDLE"=>up?64u:32u,_=>throw new ArgumentException("Unknown mouse button")};SendInput(1,[new INPUT{type=0,U=new InputUnion{mi=new MOUSEINPUT{dwFlags=flag,dwExtraInfo=UIntPtr.Zero}}}],Marshal.SizeOf<INPUT>());}
    public static void InjectWheelForTest(bool up,bool horizontal=false){if(directTestTarget is { } target){target.DirectMouse(horizontal?0x20E:0x20A,up?120:unchecked((int)0xff880000));return;}SendInput(1,[new INPUT{type=0,U=new InputUnion{mi=new MOUSEINPUT{dwFlags=horizontal?0x1000u:0x800u,mouseData=up?120u:unchecked((uint)-120),dwExtraInfo=UIntPtr.Zero}}}],Marshal.SizeOf<INPUT>());}
    public static void InjectMouseMoveForTest(int dx,int dy){if(directTestTarget is { } target){target.DirectMouse(0x200,0,dx,dy);return;}SendInput(1,[new INPUT{type=0,U=new InputUnion{mi=new MOUSEINPUT{dx=dx,dy=dy,dwFlags=1,dwExtraInfo=UIntPtr.Zero}}}],Marshal.SizeOf<INPUT>());}
    IntPtr DirectKey(ushort vk,bool up)=>DirectKey(vk,0,up);
    IntPtr DirectKey(ushort vk,uint scanCode,bool up,UIntPtr extraInfo=default){var data=new KBDLLHOOKSTRUCT{vkCode=vk,scanCode=scanCode,dwExtraInfo=extraInfo};IntPtr pointer=Marshal.AllocHGlobal(Marshal.SizeOf<KBDLLHOOKSTRUCT>());try{Marshal.StructureToPtr(data,pointer,false);return KeyboardCallback(0,(IntPtr)(up?WM_KEYUP:WM_KEYDOWN),pointer);}finally{Marshal.FreeHGlobal(pointer);}}
    internal IntPtr DirectKeyForTest(ushort vk,bool up)=>DirectKey(vk,up);
    internal IntPtr DirectMarkedKeyForTest(ushort vk,bool up)=>DirectKey(vk,0,up,(UIntPtr)Marker);
    IntPtr DirectMouse(int message,int mouseData=0,int x=0,int y=0,UIntPtr extraInfo=default){var data=new MSLLHOOKSTRUCT{pt=new POINT{x=x,y=y},mouseData=mouseData,dwExtraInfo=extraInfo};IntPtr pointer=Marshal.AllocHGlobal(Marshal.SizeOf<MSLLHOOKSTRUCT>());try{Marshal.StructureToPtr(data,pointer,false);return MouseCallback(0,(IntPtr)message,pointer);}finally{Marshal.FreeHGlobal(pointer);}}
    internal IntPtr DirectMouseForTest(int message,int mouseData=0,int x=0,int y=0)=>DirectMouse(message,mouseData,x,y);
    internal IntPtr DirectMarkedMouseForTest(int message)=>DirectMouse(message,extraInfo:(UIntPtr)Marker);
    public static bool IsKeyDownForTest(int virtualKey)=>(GetAsyncKeyState(virtualKey)&0x8000)!=0;
    internal bool HasCapturedStateForTest(){lock(stateLock)return deferredLayer!=null||presses.Count>0||held.Count>0;}
    internal bool HasActiveLayerStateForTest(){lock(stateLock)return deferredLayer!=null||presses.Keys.Any(x=>x.StartsWith("Space+",StringComparison.OrdinalIgnoreCase)||x.StartsWith("CapsLock+",StringComparison.OrdinalIgnoreCase)||x.StartsWith("MouseRight+",StringComparison.OrdinalIgnoreCase));}
    internal bool HookTestStateCleanForTest=>hookTestStateClean;
    internal bool IsDisposedForTest=>disposed;
    internal void CancelLongPressTimerForTest(string input){lock(stateLock)if(presses.TryGetValue(input,out var state)){state.Timer?.Dispose();state.Timer=null;}}
    public void ResetStateForTest(){ResetCapturedState(false);held.Clear();lastSpaceTapTick=0;}
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        enabled=false;
        if(ReferenceEquals(directTestTarget,this))directTestTarget=null;
        uint threadId=hookThreadId;if(threadId!=0)PostThreadMessage(threadId,WM_QUIT,UIntPtr.Zero,IntPtr.Zero);
        if(hookThread!=null&&!ReferenceEquals(Thread.CurrentThread,hookThread))hookThread.Join(2000);
        if(keyboardHook!=IntPtr.Zero){UnhookWindowsHookEx(keyboardHook);keyboardHook=IntPtr.Zero;}
        if(mouseHook!=IntPtr.Zero){UnhookWindowsHookEx(mouseHook);mouseHook=IntPtr.Zero;}
        ResetCapturedState(true);
        hookThread=null;hookReady.Dispose();hookTestCompleted.Dispose();
    }

    sealed class PressState{public bool IsDown,Handled,Dragged,Immediate,NativeMouseDrag,FireOnDown,Cancelled;public int X,Y,Ended,LongFired,LongPressMs;public long DownTick;public System.Threading.Timer? Timer;public TaskCompletionSource? ReleaseSignal;}
    delegate IntPtr HookProc(int nCode,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)]struct KBDLLHOOKSTRUCT{public uint vkCode,scanCode,flags,time;public UIntPtr dwExtraInfo;}
    [StructLayout(LayoutKind.Sequential)]struct MSLLHOOKSTRUCT{public POINT pt;public int mouseData,flags,time;public UIntPtr dwExtraInfo;}
    [StructLayout(LayoutKind.Sequential)]struct POINT{public int x,y;}
    [StructLayout(LayoutKind.Sequential)]struct INPUT{public uint type;public InputUnion U;}
    [StructLayout(LayoutKind.Explicit)]struct InputUnion{[FieldOffset(0)]public MOUSEINPUT mi;[FieldOffset(0)]public KEYBDINPUT ki;}
    [StructLayout(LayoutKind.Sequential)]struct KEYBDINPUT{public ushort wVk,wScan;public uint dwFlags,time;public UIntPtr dwExtraInfo;}
    [StructLayout(LayoutKind.Sequential)]struct MOUSEINPUT{public int dx,dy;public uint mouseData,dwFlags,time;public UIntPtr dwExtraInfo;}
    [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetWindowsHookEx(int idHook,HookProc lpfn,IntPtr hMod,uint threadId);
    [DllImport("user32.dll")]static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]static extern IntPtr CallNextHookEx(IntPtr hhk,int nCode,IntPtr wParam,IntPtr lParam);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]static extern int GetMessage(out MSG message,IntPtr window,uint minimum,uint maximum);
    [DllImport("user32.dll")]static extern bool PostThreadMessage(uint threadId,uint message,UIntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")]static extern uint SendInput(uint count,INPUT[] inputs,int size);
    [DllImport("user32.dll")]static extern void keybd_event(byte virtualKey,byte scanCode,uint flags,UIntPtr extraInfo);
    [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extraInfo);
    [DllImport("user32.dll")]static extern short VkKeyScan(char ch);
    [DllImport("user32.dll")]static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")]static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",SetLastError=true)]static extern bool LockWorkStation();
    [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out UIntPtr result);
    [DllImport("imm32.dll")]static extern IntPtr ImmGetDefaultIMEWnd(IntPtr window);
    [DllImport("imm32.dll")]static extern IntPtr ImmGetContext(IntPtr window);
    [DllImport("imm32.dll")]static extern bool ImmReleaseContext(IntPtr window,IntPtr context);
    [DllImport("imm32.dll")]static extern bool ImmGetOpenStatus(IntPtr context);
    [DllImport("imm32.dll")]static extern bool ImmSetOpenStatus(IntPtr context,bool open);
    [StructLayout(LayoutKind.Sequential)]struct MSG{public IntPtr hwnd;public uint message;public UIntPtr wParam;public IntPtr lParam;public uint time;public POINT pt;public uint privateData;}
}
