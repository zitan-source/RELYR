namespace RELYR;

internal sealed record DeckTimerSnapshot(bool IsRunning, TimeSpan Duration, TimeSpan Remaining);

internal sealed record DeckTimerCompletion(TimeSpan Duration, DateTimeOffset CompletedAt);

/// <summary>
/// Owns the single Deck timer independently from any visible Deck window so a
/// timer keeps running while its panel is hidden or rebuilt.
/// </summary>
internal sealed class DeckTimerService : IDisposable
{
    internal static DeckTimerService Shared { get; } = new();

    readonly object sync = new();
    readonly System.Threading.Timer timer;
    DateTimeOffset endsAtUtc;
    TimeSpan duration;
    bool running;
    bool disposed;

    internal event Action? Changed;
    internal event Action<DeckTimerCompletion>? Completed;

    DeckTimerService()
    {
        timer = new System.Threading.Timer(_ => TimerElapsed(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    internal void Start(TimeSpan requestedDuration)
    {
        if (requestedDuration < TimeSpan.FromSeconds(1) || requestedDuration > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(requestedDuration));

        DeckTimerNotificationPresenter.EnsureStarted();
        lock (sync)
        {
            if (disposed)
                return;
            duration = requestedDuration;
            endsAtUtc = DateTimeOffset.UtcNow + requestedDuration;
            running = true;
            timer.Change(requestedDuration, Timeout.InfiniteTimeSpan);
        }
        NotifyChanged();
    }

    internal bool Cancel()
    {
        bool changed;
        lock (sync)
        {
            changed = running;
            running = false;
            timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        if (changed)
            NotifyChanged();
        return changed;
    }

    internal DeckTimerSnapshot Snapshot()
    {
        lock (sync)
        {
            TimeSpan remaining = running ? endsAtUtc - DateTimeOffset.UtcNow : TimeSpan.Zero;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;
            return new DeckTimerSnapshot(running, duration, remaining);
        }
    }

    internal SystemMonitorReading Reading()
    {
        DeckTimerSnapshot snapshot = Snapshot();
        if (!snapshot.IsRunning)
            return new SystemMonitorReading("READY", "RIGHT CLICK", 0);

        double level = snapshot.Duration.TotalMilliseconds <= 0
            ? 0
            : Math.Clamp(snapshot.Remaining.TotalMilliseconds / snapshot.Duration.TotalMilliseconds, 0, 1);
        return new SystemMonitorReading(FormatRemaining(snapshot.Remaining), "REMAINING", level);
    }

    internal static string FormatRemaining(TimeSpan remaining)
    {
        remaining = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        int totalSeconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        int hours = totalSeconds / 3600;
        int minutes = totalSeconds % 3600 / 60;
        int seconds = totalSeconds % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes:00}:{seconds:00}";
    }

    internal static string DurationLabel(TimeSpan value)
    {
        if (value.TotalHours >= 1 && Math.Abs(value.TotalMinutes % 60) < .001)
            return $"{value.TotalHours:0}時間";
        if (value.TotalMinutes >= 1)
            return $"{value.TotalMinutes:0.#}分";
        return $"{value.TotalSeconds:0}秒";
    }

    void TimerElapsed()
    {
        DeckTimerCompletion? completion = null;
        lock (sync)
        {
            if (disposed || !running)
                return;

            TimeSpan remaining = endsAtUtc - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.FromMilliseconds(40))
            {
                timer.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }

            running = false;
            completion = new DeckTimerCompletion(duration, DateTimeOffset.UtcNow);
        }

        NotifyChanged();
        if (completion != null)
        {
            foreach (Action<DeckTimerCompletion> handler in (Completed?.GetInvocationList() ?? []).Cast<Action<DeckTimerCompletion>>())
            {
                try { handler(completion); }
                catch (Exception error) { LifecycleDiagnostics.Write("deck-timer-completion-failed", error.ToString()); }
            }
        }
    }

    void NotifyChanged()
    {
        try { SystemMonitorService.Shared.RequestRefresh(); }
        catch (Exception error) { LifecycleDiagnostics.Write("deck-timer-refresh-failed", error.ToString()); }

        foreach (Action handler in (Changed?.GetInvocationList() ?? []).Cast<Action>())
        {
            try { handler(); }
            catch (Exception error) { LifecycleDiagnostics.Write("deck-timer-subscriber-failed", error.ToString()); }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;
            disposed = true;
            running = false;
            timer.Dispose();
        }
    }
}

internal static class DeckTimerNotificationPresenter
{
    static readonly object Sync = new();
    static bool started;
    static DeckTimerNotificationWindow? notification;

    internal static void EnsureStarted()
    {
        lock (Sync)
        {
            if (started)
                return;
            DeckTimerService.Shared.Completed += TimerCompleted;
            started = true;
        }
    }

    static void TimerCompleted(DeckTimerCompletion completion)
    {
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher == null || app.Dispatcher.HasShutdownStarted)
            return;
        try
        {
            _ = app.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    notification?.HideImmediatelyForProcessExit();
                    notification?.Close();
                    notification = new DeckTimerNotificationWindow(completion.Duration);
                    notification.Closed += (_, _) => notification = null;
                    notification.Show();
                }
                catch (Exception error)
                {
                    notification = null;
                    LifecycleDiagnostics.Write("deck-timer-notification-failed", error.ToString());
                }
            }));
        }
        catch (Exception error) { LifecycleDiagnostics.Write("deck-timer-dispatch-failed", error.ToString()); }
    }

    internal static DeckTimerNotificationWindow? NotificationForTest => notification;
}
