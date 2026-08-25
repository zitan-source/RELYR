namespace RELYR;

/// <summary>
/// Small process-local projection of the persisted auto-extraction setting.
/// Deck monitor controls read this state without reloading the configuration
/// file or rebuilding the complete Deck overlay.
/// </summary>
internal static class ArchiveAutomationState
{
    static int enabled;

    internal static bool Enabled => Volatile.Read(ref enabled) != 0;
    internal static event Action? Changed;

    internal static void Set(bool value)
    {
        int next = value ? 1 : 0;
        if (Interlocked.Exchange(ref enabled, next) == next)
            return;
        foreach (Action listener in Changed?.GetInvocationList().Cast<Action>() ?? [])
        {
            try { listener(); }
            catch (Exception error) { LifecycleDiagnostics.Write("archive-state-listener-failed", error.ToString()); }
        }
    }

    internal static SystemMonitorReading Reading()
        => Enabled
            ? new SystemMonitorReading("ON", "監視中", 1)
            : new SystemMonitorReading("OFF", "停止中", 0);
}
