namespace RELYR;

public static class MacroPlayer
{
    static readonly object gate=new();
    static CancellationTokenSource? current;
    static Task currentTask=Task.CompletedTask;

    public static void Play(MacroDefinition macro,AppConfig? config=null,Action<string>? switchProfile=null)
    {
        _=PlayAsync(macro,config,switchProfile);
    }

    internal static Task PlayAsync(MacroDefinition macro,AppConfig? config=null,Action<string>? switchProfile=null)
    {
        CancellationTokenSource source;
        Task previous;
        lock(gate)
        {
            current?.Cancel();
            source=new CancellationTokenSource();
            current=source;
            previous=currentTask;
            currentTask=Task.Run(()=>RunAfter(previous,macro,source,config,switchProfile));
            return currentTask;
        }
    }

    static async Task RunAfter(Task previous,MacroDefinition macro,CancellationTokenSource source,AppConfig? config,Action<string>? switchProfile)
    {
        try
        {
            try{await previous.ConfigureAwait(false);}catch{}
            var token=source.Token;
            token.ThrowIfCancellationRequested();
            var executor=config==null?null:new MappingExecutor(new SystemInputOutput(name=>config.Macros.FirstOrDefault(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)),switchProfile??(name=>{if(config.Profiles.Any(x=>x.Name==name)){config.ActiveProfile=name;new ConfigService().Save(config);}}),()=>config.KeyboardLayout=="US",()=>config));
            foreach(var step in macro.Steps)
            {
                if(step.DelayMs>0)await Task.Delay(Math.Clamp(step.DelayMs,0,600000),token);
                token.ThrowIfCancellationRequested();
                if(step.RecordedActionKind is { } kind)
                {
                    if(executor==null)throw new InvalidOperationException("割り当てアクションを再生する設定がありません。");
                    executor.Execute(new Mapping{Kind=kind,Value=step.RecordedActionValue,Layer="通常"},"Recorded",out _);
                }
                else if(!step.Event.Equals("Wait",StringComparison.OrdinalIgnoreCase))InputEngine.SendRecordedEvent(step.Event);
            }
        }
        catch(OperationCanceledException){}
        catch{}
        finally
        {
            // Down で終わる不完全な記録でも、マウスや修飾キーを絶対に残さない。
            InputEngine.ReleaseAll();
            lock(gate){if(ReferenceEquals(current,source))current=null;}
            source.Dispose();
        }
    }

    public static void StopAll(){lock(gate){current?.Cancel();current=null;}InputEngine.ReleaseAll();}
}
