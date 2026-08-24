using Windows.Devices.Radios;

namespace RELYR;

/// <summary>
/// Maintains an event-driven snapshot of the Windows Bluetooth radio. Radio
/// enumeration is asynchronous and isolated from the UI/input paths; monitor
/// rendering reads only the cached tri-state value.
/// </summary>
internal static class RadioMonitorService
{
    static readonly object Sync = new();
    static List<Radio> radios = [];
    static int bluetoothState = -1;
    static int refreshPending;
    static long refreshAfterUtcTicks;
    static bool disposed;

    internal static bool? BluetoothEnabled
    {
        get
        {
            QueueRefreshIfNeeded();
            return Volatile.Read(ref bluetoothState) switch
            {
                0 => false,
                1 => true,
                _ => null
            };
        }
    }

    static void QueueRefreshIfNeeded()
    {
        if (disposed || DateTime.UtcNow.Ticks < Volatile.Read(ref refreshAfterUtcTicks)
            || Interlocked.CompareExchange(ref refreshPending, 1, 0) != 0)
            return;
        _ = Task.Run(RefreshAsync);
    }

    static async Task RefreshAsync()
    {
        try
        {
            var snapshot = await Radio.GetRadiosAsync();
            List<Radio> bluetooth = snapshot.Where(radio => radio.Kind == RadioKind.Bluetooth).ToList();
            lock (Sync)
            {
                if (disposed)
                    return;
                foreach (Radio radio in radios)
                    radio.StateChanged -= RadioStateChanged;
                radios = bluetooth;
                foreach (Radio radio in radios)
                    radio.StateChanged += RadioStateChanged;
                UpdateCachedStateLocked();
                Volatile.Write(ref refreshAfterUtcTicks, DateTime.UtcNow.AddMinutes(1).Ticks);
            }
            SystemMonitorService.Shared.RequestRefresh();
        }
        catch (Exception error)
        {
            Volatile.Write(ref bluetoothState, -1);
            Volatile.Write(ref refreshAfterUtcTicks, DateTime.UtcNow.AddMinutes(1).Ticks);
            LifecycleDiagnostics.Write("bluetooth-monitor-read-failed", error.ToString());
        }
        finally
        {
            Volatile.Write(ref refreshPending, 0);
        }
    }

    static void RadioStateChanged(Radio sender, object args)
    {
        lock (Sync)
        {
            if (disposed)
                return;
            UpdateCachedStateLocked();
        }
        SystemMonitorService.Shared.RequestRefresh();
    }

    static void UpdateCachedStateLocked()
    {
        bool? enabled = AggregateBluetoothState(radios.Select(radio => radio.State));
        int state = enabled is null ? -1 : enabled.Value ? 1 : 0;
        Volatile.Write(ref bluetoothState, state);
    }

    internal static bool? AggregateBluetoothState(IEnumerable<RadioState> states)
    {
        RadioState[] snapshot = states.ToArray();
        return snapshot.Length == 0 ? null : snapshot.Any(state => state == RadioState.On);
    }

    internal static void Dispose()
    {
        lock (Sync)
        {
            if (disposed)
                return;
            disposed = true;
            foreach (Radio radio in radios)
                radio.StateChanged -= RadioStateChanged;
            radios.Clear();
        }
    }
}
