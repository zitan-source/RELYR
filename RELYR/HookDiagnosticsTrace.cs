using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RELYR;

internal enum HookDiagnosticStage
{
    TraceStarted, DroppedRecords, HookThreadStarted, HookThreadFault, HookThreadStopping, HookThreadStopped,
    KeyboardHookRegistered, MouseHookRegistered, KeyboardHookFinalUnhook, MouseHookFinalUnhook, RawInputMonitorStarted,
    RawInputEnter, RawInputSizeRead, RawInputDataCopied, RawInputDecoded, RawInputReconciled, RawInputFault, RawInputExit,
    RawReconcileStarted, RawReconcileLowLevelMatched, RawReconcileLayerEndStarted, RawReconcileLayerEndCompleted,
    RawReconcilePendingLookup, RawReconcilePressEndStarted, RawReconcilePressEndCompleted, RawReconcileCounterUpdated,
    RawKeyboardTransition, RawMouseTransition, KeyboardCallbackEnter, KeyboardCallbackExit, KeyboardCallbackFault,
    KeyboardStateLockAcquired, KeyboardInterceptEvaluated, KeyboardLayerLookupStarted, KeyboardLayerLookupCompleted,
    KeyboardLayerStarted, MouseCallbackEnter, MouseCallbackExit, MouseCallbackFault, MouseStateLockAcquired,
    MouseInterceptEvaluated, MouseGeneratedMarker, MouseLayerLookupStarted, MouseLayerLookupCompleted, MouseLayerStarted,
    PressDownCreated, PressDownExisting, PressUpRemoved, PressUpMissing,
    ForegroundProcessNoWindow, ForegroundProcessCacheHit,
    ForegroundProcessLookupStarted, ForegroundProcessLookupCompleted, KeyboardHealthCheck, MouseHealthCheck,
    RecoveryRequestCoalesced, RecoveryMessagePosted, RecoverMessageDequeued, RecoverMessageFault, RecoveryStarted,
    RecoverySkipped, KeyboardRecoveryRegistered, KeyboardRecoveryPreviousUnhook, MouseRecoveryRegistered,
    MouseRecoveryPreviousUnhook, RecoveryCompleted, IndependentMonitorHeartbeat, IndependentMonitorMouseCounters,
    IndependentMonitorState
}

internal static class HookDiagnosticsTrace
{
#if HOOK_DIAGNOSTICS
    const int Capacity = 16384;
    const int Mask = Capacity - 1;

    readonly struct TraceEntry
    {
        internal TraceEntry(long sequence, long timestamp, uint nativeThreadId, int managedThreadId, HookDiagnosticStage stage,
            long durationTicks, long keyboardHook, long mouseHook, long previousHook, long replacementHook,
            long value1, long value2, int result, int win32Error, bool inHookCallback)
        {
            Sequence = sequence; Timestamp = timestamp; NativeThreadId = nativeThreadId; ManagedThreadId = managedThreadId;
            Stage = stage; DurationTicks = durationTicks; KeyboardHook = keyboardHook; MouseHook = mouseHook;
            PreviousHook = previousHook; ReplacementHook = replacementHook; Value1 = value1; Value2 = value2;
            Result = result; Win32Error = win32Error; InHookCallback = inHookCallback;
        }
        internal long Sequence { get; }
        internal long Timestamp { get; }
        internal uint NativeThreadId { get; }
        internal int ManagedThreadId { get; }
        internal HookDiagnosticStage Stage { get; }
        internal long DurationTicks { get; }
        internal long KeyboardHook { get; }
        internal long MouseHook { get; }
        internal long PreviousHook { get; }
        internal long ReplacementHook { get; }
        internal long Value1 { get; }
        internal long Value2 { get; }
        internal int Result { get; }
        internal int Win32Error { get; }
        internal bool InHookCallback { get; }
    }

    sealed class Slot { internal long Sequence; internal TraceEntry Entry; }
    static readonly Slot[] Buffer = CreateBuffer();
    static readonly Thread WriterThread;
    static readonly long StartTimestamp = Stopwatch.GetTimestamp();
    static readonly DateTimeOffset StartUtc = DateTimeOffset.UtcNow;
    static long enqueuePosition, dequeuePosition, eventSequence, droppedRecords;
    static int stopping;
    [ThreadStatic] static int hookCallbackDepth;

    static HookDiagnosticsTrace()
    {
        WriterThread = new Thread(WriteLoop) { IsBackground = true, Name = "RELYR hook diagnostics writer" };
        WriterThread.Start();
    }

    static Slot[] CreateBuffer()
    {
        var slots = new Slot[Capacity];
        for (int index = 0; index < slots.Length; index++) slots[index] = new Slot { Sequence = index };
        return slots;
    }

    internal static bool InHookCallback => hookCallbackDepth != 0;
    [Conditional("HOOK_DIAGNOSTICS")] internal static void EnterHookCallback() => hookCallbackDepth++;
    [Conditional("HOOK_DIAGNOSTICS")] internal static void ExitHookCallback() { if (hookCallbackDepth > 0) hookCallbackDepth--; }

    [Conditional("HOOK_DIAGNOSTICS")]
    internal static void Record(HookDiagnosticStage stage, long durationTicks = 0, IntPtr keyboardHook = default,
        IntPtr mouseHook = default, IntPtr previousHook = default, IntPtr replacementHook = default,
        long value1 = 0, long value2 = 0, int result = 0, int win32Error = 0)
    {
        var entry = new TraceEntry(Interlocked.Increment(ref eventSequence), Stopwatch.GetTimestamp(), GetCurrentThreadId(),
            Environment.CurrentManagedThreadId, stage, durationTicks, keyboardHook.ToInt64(), mouseHook.ToInt64(),
            previousHook.ToInt64(), replacementHook.ToInt64(), value1, value2, result, win32Error, InHookCallback);
        if (!TryEnqueue(entry)) Interlocked.Increment(ref droppedRecords);
    }

    static bool TryEnqueue(TraceEntry entry)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            long position = Volatile.Read(ref enqueuePosition);
            Slot slot = Buffer[(int)(position & Mask)];
            long difference = Volatile.Read(ref slot.Sequence) - position;
            if (difference == 0)
            {
                if (Interlocked.CompareExchange(ref enqueuePosition, position + 1, position) != position) continue;
                slot.Entry = entry;
                Volatile.Write(ref slot.Sequence, position + 1);
                return true;
            }
            if (difference < 0) return false;
        }
        return false;
    }

    static bool TryDequeue(out TraceEntry entry)
    {
        long position = dequeuePosition;
        Slot slot = Buffer[(int)(position & Mask)];
        if (Volatile.Read(ref slot.Sequence) - (position + 1) != 0) { entry = default; return false; }
        entry = slot.Entry;
        Volatile.Write(ref slot.Sequence, position + Capacity);
        dequeuePosition = position + 1;
        return true;
    }

    [Conditional("HOOK_DIAGNOSTICS")]
    internal static void Stop()
    {
        if (Interlocked.Exchange(ref stopping, 1) != 0) return;
        if (Thread.CurrentThread != WriterThread) WriterThread.Join(TimeSpan.FromSeconds(2));
    }

    static void WriteLoop()
    {
        string path = Environment.GetEnvironmentVariable("RELYR_HOOK_DIAGNOSTIC_LOG") ?? "";
        if (string.IsNullOrWhiteSpace(path)) path = Path.Combine(AppContext.BaseDirectory, $"hook-diagnostic-{Environment.ProcessId}.tsv");
        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 64 * 1024, FileOptions.SequentialScan);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024);
            writer.WriteLine("Sequence\tUtc\tStopwatchTicks\tNativeThreadId\tManagedThreadId\tStage\tDurationTicks\tDurationMicroseconds\tKeyboardHook\tMouseHook\tPreviousHook\tReplacementHook\tValue1\tValue2\tResult\tWin32Error\tInHookCallback");
            WriteSynthetic(writer, HookDiagnosticStage.TraceStarted, Environment.ProcessId, Stopwatch.Frequency);
            writer.Flush();
            while (Volatile.Read(ref stopping) == 0 || Volatile.Read(ref dequeuePosition) != Volatile.Read(ref enqueuePosition))
            {
                bool wrote = false;
                while (TryDequeue(out var entry)) { WriteEntry(writer, entry); wrote = true; }
                long dropped = Interlocked.Exchange(ref droppedRecords, 0);
                if (dropped != 0) { WriteSynthetic(writer, HookDiagnosticStage.DroppedRecords, dropped, Volatile.Read(ref enqueuePosition)); wrote = true; }
                if (wrote) writer.Flush();
                Thread.Sleep(25);
            }
        }
        catch { }
    }

    static void WriteSynthetic(StreamWriter writer, HookDiagnosticStage stage, long value1, long value2)
        => WriteEntry(writer, new TraceEntry(Interlocked.Increment(ref eventSequence), Stopwatch.GetTimestamp(), GetCurrentThreadId(),
            Environment.CurrentManagedThreadId, stage, 0, 0, 0, 0, 0, value1, value2, 1, 0, false));

    static void WriteEntry(StreamWriter writer, TraceEntry entry)
    {
        double microseconds = entry.DurationTicks == 0 ? 0 : entry.DurationTicks * 1_000_000d / Stopwatch.Frequency;
        DateTimeOffset utc = StartUtc + TimeSpan.FromSeconds((entry.Timestamp - StartTimestamp) / (double)Stopwatch.Frequency);
        writer.Write(entry.Sequence.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t'); writer.Write(utc.ToString("O", CultureInfo.InvariantCulture));
        writer.Write('\t'); writer.Write(entry.Timestamp.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t'); writer.Write(entry.NativeThreadId.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t'); writer.Write(entry.ManagedThreadId.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t'); writer.Write(entry.Stage.ToString());
        writer.Write('\t'); writer.Write(entry.DurationTicks.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t'); writer.Write(microseconds.ToString("F3", CultureInfo.InvariantCulture));
        writer.Write('\t'); writer.Write(entry.KeyboardHook.ToString("X", CultureInfo.InvariantCulture));
        writer.Write('\t'); writer.Write(entry.MouseHook.ToString("X", CultureInfo.InvariantCulture));
        writer.Write('\t'); writer.Write(entry.PreviousHook.ToString("X", CultureInfo.InvariantCulture));
        writer.Write('\t'); writer.Write(entry.ReplacementHook.ToString("X", CultureInfo.InvariantCulture));
        writer.Write('\t'); writer.Write(entry.Value1.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t'); writer.Write(entry.Value2.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t'); writer.Write(entry.Result.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t'); writer.Write(entry.Win32Error.ToString(CultureInfo.InvariantCulture));
        writer.Write('\t'); writer.WriteLine(entry.InHookCallback ? "True" : "False");
    }

    internal static int ExceptionCode(Exception exception) => exception switch
    {
        NullReferenceException => 5,
        OutOfMemoryException => 1,
        OverflowException => 2,
        ObjectDisposedException => 3,
        InvalidOperationException => 4,
        ArgumentException => 5,
        System.ComponentModel.Win32Exception => 6,
        ExternalException => 7,
        _ => 255
    };

    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
#else
    internal static bool InHookCallback => false;
    [Conditional("HOOK_DIAGNOSTICS")] internal static void EnterHookCallback() { }
    [Conditional("HOOK_DIAGNOSTICS")] internal static void ExitHookCallback() { }
    [Conditional("HOOK_DIAGNOSTICS")] internal static void Record(HookDiagnosticStage stage, long durationTicks = 0,
        IntPtr keyboardHook = default, IntPtr mouseHook = default, IntPtr previousHook = default,
        IntPtr replacementHook = default, long value1 = 0, long value2 = 0, int result = 0, int win32Error = 0) { }
    [Conditional("HOOK_DIAGNOSTICS")] internal static void Stop() { }
    internal static int ExceptionCode(Exception exception) => 0;
#endif
}
