namespace RELYR;

public sealed record MacroPlaybackResult(bool Succeeded, bool Cancelled, string Message, int ExecutedSteps);

public static class MacroPlayer
{
    sealed class ExecutionState
    {
        public int ExecutedSteps;
    }
    static readonly Lock gate = new();
    static CancellationTokenSource? current;
    static Task currentTask = Task.CompletedTask;
    const int MaximumNestedDepth = 16;
    const int MaximumExecutedSteps = 10000;

    public static event Action<MacroPlaybackResult>? PlaybackFinished;

    public static void Play(MacroDefinition macro, AppConfig? config = null, Action<string>? switchProfile = null, IntPtr? preferredActiveWindow = null) => _ = PlayAsync(macro, config, switchProfile, preferredActiveWindow);

    internal static Task<MacroPlaybackResult> PlayAsync(MacroDefinition macro, AppConfig? config = null, Action<string>? switchProfile = null, IntPtr? preferredActiveWindow = null)
    {
        CancellationTokenSource source;
        Task previous;
        Task<MacroPlaybackResult> task;
        lock (gate)
        {
            current?.Cancel();
            source = new CancellationTokenSource();
            current = source;
            previous = currentTask;
            task = Task.Run(() => RunAfter(previous, macro, source, config, switchProfile, preferredActiveWindow));
            currentTask = task;
        }
        return task;
    }

    static async Task<MacroPlaybackResult> RunAfter(Task previous, MacroDefinition macro, CancellationTokenSource source, AppConfig? config, Action<string>? switchProfile, IntPtr? preferredActiveWindow)
    {
        MacroPlaybackResult result;
        var state = new ExecutionState();
        try
        {
            try
            {
                await previous.ConfigureAwait(false);
            }
            catch { }
            var token = source.Token;
            token.ThrowIfCancellationRequested();
            WindowMonitorService.PrepareShortcutTarget(preferredActiveWindow);
            var executor = config == null ? null : new MappingExecutor(new SystemInputOutput(name => config.Macros.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)), switchProfile ?? (name => { if (config.Profiles.Any(x => x.Name == name)) { config.ActiveProfile = name; new ConfigService().Save(config); } }), () => config.KeyboardLayout == "US", () => config, () => preferredActiveWindow));
            await RunSteps(macro, executor, config, switchProfile, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0, state, token).ConfigureAwait(false);
            result = new(true, false, $"「{macro.Name}」を実行しました。", state.ExecutedSteps);
        }
        catch (OperationCanceledException) { result = new(false, true, "マクロを停止しました。", state.ExecutedSteps); }
        catch (Exception ex) { result = new(false, false, BeginnerMessage(ex), state.ExecutedSteps); }
        finally
        {
            InputEngine.ReleaseAll();
            lock (gate)
            {
                if (ReferenceEquals(current, source))
                    current = null;
            }
            source.Dispose();
        }
        try
        {
            PlaybackFinished?.Invoke(result);
        }
        catch { }
        return result;
    }

    static async Task RunSteps(MacroDefinition macro, MappingExecutor? executor, AppConfig? config, Action<string>? switchProfile, HashSet<string> chain, int depth, ExecutionState state, CancellationToken token)
    {
        if (depth >= MaximumNestedDepth)
            throw new InvalidOperationException($"マクロの呼び出しが{MaximumNestedDepth}段を超えました。");
        string identity = string.IsNullOrWhiteSpace(macro.Id) ? macro.Name : macro.Id;
        if (!chain.Add(identity))
            throw new InvalidOperationException($"マクロ「{macro.Name}」が自分自身を繰り返し呼び出しています。");
        try
        {
            foreach (var step in macro.Steps)
            {
                token.ThrowIfCancellationRequested();
                if (++state.ExecutedSteps > MaximumExecutedSteps)
                    throw new InvalidOperationException("マクロの実行手順が多すぎるため、安全のため停止しました。");
                if (step.DelayMs > 0)
                    await Task.Delay(Math.Clamp(step.DelayMs, 0, 600000), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (step.RecordedActionKind is { } kind)
                {
                    if (kind == ActionKind.Macro)
                    {
                        if (config == null)
                            throw new InvalidOperationException("呼び出すマクロの設定を読み込めません。");
                        var nested = config.Macros.FirstOrDefault(x => x.Name.Equals(step.RecordedActionValue, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"マクロ「{step.RecordedActionValue}」が見つかりません。");
                        await RunSteps(nested, executor, config, switchProfile, chain, depth + 1, state, token).ConfigureAwait(false);
                    }
                    else
                    {
                        if (executor == null)
                            throw new InvalidOperationException("割り当てアクションを再生する設定がありません。");
                        if (!executor.Execute(new Mapping { Kind = kind, Value = step.RecordedActionValue, Layer = "通常" }, "Recorded", out string value))
                            throw new InvalidOperationException("アクションを実行できません: " + value);
                    }
                }
                else if (!step.Event.Equals("Wait", StringComparison.OrdinalIgnoreCase))
                    InputEngine.SendRecordedEvent(step.Event);
            }
        }
        finally { chain.Remove(identity); }
    }

    static string BeginnerMessage(Exception ex) => ex switch
    {
        ArgumentException => "認識できないキーまたは手順があります: " + ex.Message,
        InvalidOperationException => ex.Message,
        _ => "マクロの実行中にエラーが発生しました: " + ex.Message
    };

    public static void StopAll()
    {
        lock (gate)
        {
            current?.Cancel();
            current = null;
        }
        InputEngine.ReleaseAll();
    }
}
