using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace RELYR;

internal enum BatteryVisualState { Unknown, Discharging, Charging, Plugged, PluggedFull, Low }

internal sealed record SystemMonitorReading(
    string Text,
    string Detail = "",
    double? Level = null,
    bool Available = true,
    BatteryVisualState BatteryState = BatteryVisualState.Unknown,
    bool Warning = false);

internal sealed class SystemMonitorSnapshot
{
    readonly IReadOnlyDictionary<string, SystemMonitorReading> readings;

    internal SystemMonitorSnapshot(IReadOnlyDictionary<string, SystemMonitorReading> readings) => this.readings = readings;

    internal SystemMonitorReading Get(string id)
        => readings.TryGetValue(id, out var reading) ? reading : new SystemMonitorReading("—", "N/A", Available: false);
}

internal sealed class SystemMonitorService : IDisposable
{
    internal static SystemMonitorService Shared { get; } = new();

    readonly object sync = new();
    readonly System.Threading.Timer timer;
    int subscribers;
    int sampling;
    long previousIdle;
    long previousKernel;
    long previousUser;
    long previousNetworkSent;
    long previousNetworkReceived;
    long previousNetworkAt;
    int slowSampleCounter;
    bool slowSampleInitialized;
    double? cachedTemperature;
    string cachedTemperatureName = "";
    double? cachedGpuTemperature;
    string cachedGpuTemperatureName = "";
    double? cachedGpu;
    GpuMemorySample? cachedGpuMemory;
    double? cachedFanRpm;
    string cachedFanName = "";
    double? cachedDiskReadBytes;
    double? cachedDiskWriteBytes;
    double? cachedLatencyMs;
    bool disposed;

    internal event EventHandler<SystemMonitorSnapshot>? SnapshotChanged;

    SystemMonitorService()
    {
        timer = new System.Threading.Timer(_ => SampleSafely(), null, Timeout.Infinite, Timeout.Infinite);
        ReadCpu(out previousIdle, out previousKernel, out previousUser);
        ReadNetworkTotals(out previousNetworkSent, out previousNetworkReceived, out _);
        previousNetworkAt = Stopwatch.GetTimestamp();
    }

    internal void Subscribe(EventHandler<SystemMonitorSnapshot> handler)
    {
        lock (sync)
        {
            SnapshotChanged += handler;
            subscribers++;
            if (subscribers == 1 && !disposed)
                timer.Change(0, 1000);
        }
    }

    internal void Unsubscribe(EventHandler<SystemMonitorSnapshot> handler)
    {
        lock (sync)
        {
            SnapshotChanged -= handler;
            subscribers = Math.Max(0, subscribers - 1);
            if (subscribers == 0)
                timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    internal void RequestRefresh()
    {
        lock (sync)
        {
            if (subscribers > 0 && !disposed)
                timer.Change(0, 1000);
        }
    }

    void SampleSafely()
    {
        if (Interlocked.Exchange(ref sampling, 1) != 0)
            return;
        try
        {
            var snapshot = Capture();
            EventHandler<SystemMonitorSnapshot>? handlers;
            lock (sync) handlers = SnapshotChanged;
            if (handlers != null)
            {
                foreach (EventHandler<SystemMonitorSnapshot> handler in handlers.GetInvocationList())
                {
                    try { handler(this, snapshot); }
                    catch (Exception error) { LifecycleDiagnostics.Write("deck-monitor-subscriber-failed", error.ToString()); }
                }
            }
        }
        catch (Exception error)
        {
            LifecycleDiagnostics.Write("deck-monitor-sample-failed", error.ToString());
        }
        finally { Volatile.Write(ref sampling, 0); }
    }

    SystemMonitorSnapshot Capture()
    {
        var readings = new Dictionary<string, SystemMonitorReading>(StringComparer.OrdinalIgnoreCase);

        double? cpu = CpuUsage();
        readings["cpu"] = Percent(cpu, "CPU");

        double? memory = MemoryUsage();
        readings["memory"] = Percent(memory, "RAM");

        if (!slowSampleInitialized || ++slowSampleCounter >= 5)
        {
            HardwareSensorSnapshot? hardware = HardwareSensorClient.Shared.TryRead();
            cachedTemperature = hardware?.CpuTemperature;
            cachedTemperatureName = hardware?.CpuTemperature is double
                ? ShortSensorName(hardware.CpuTemperatureName, "CPU")
                : "";
            cachedGpuTemperature = hardware?.GpuTemperature;
            cachedGpuTemperatureName = hardware?.GpuTemperature is double
                ? ShortSensorName(hardware.GpuTemperatureName, "GPU")
                : "";
            cachedGpu = ReadGpuUsage();
            cachedGpuMemory = ReadGpuMemoryUsage();
            cachedFanRpm = hardware?.FanRpm;
            cachedFanName = hardware?.FanRpm is double
                ? ShortSensorName(hardware.FanName, "FAN")
                : "";
            cachedLatencyMs = ReadGatewayLatency();
            slowSampleCounter = 0;
            slowSampleInitialized = true;
        }
        readings["temperature"] = cachedTemperature is double temperature
            ? new SystemMonitorReading($"{temperature:0}°", cachedTemperatureName, Math.Clamp(temperature / 100, 0, 1), Warning: temperature >= 85)
            : new SystemMonitorReading("—", "N/A", Available: false);
        readings["gpu-temperature"] = cachedGpuTemperature is double gpuTemperature
            ? new SystemMonitorReading($"{gpuTemperature:0}°", cachedGpuTemperatureName, Math.Clamp(gpuTemperature / 100, 0, 1), Warning: gpuTemperature >= 90)
            : new SystemMonitorReading("—", "N/A", Available: false);

        readings["gpu"] = Percent(cachedGpu, "3D");
        readings["vram"] = cachedGpuMemory is { } gpuMemory
            ? new SystemMonitorReading(FormatCapacity(gpuMemory.Bytes), gpuMemory.Detail,
                Math.Clamp(gpuMemory.Bytes / (gpuMemory.Detail.StartsWith("SHARED", StringComparison.Ordinal) ? 16d : 8d) / (1024 * 1024 * 1024), 0, 1))
            : new SystemMonitorReading("—", "N/A", Available: false);
        readings["fan"] = cachedFanRpm is double fan
            ? new SystemMonitorReading($"{fan:0}", string.IsNullOrWhiteSpace(cachedFanName) ? "RPM" : $"{cachedFanName} RPM", Math.Clamp(fan / 5000, 0, 1))
            : new SystemMonitorReading("—", "N/A", Available: false);

        readings["disk"] = DiskUsage();
        (cachedDiskReadBytes, cachedDiskWriteBytes) = ReadDiskThroughput();
        readings["disk-read"] = cachedDiskReadBytes is double diskRead
            ? new SystemMonitorReading(FormatRate(diskRead), "READ/s", RateLevel(diskRead, 2L * 1024 * 1024 * 1024))
            : new SystemMonitorReading("—", "N/A", Available: false);
        readings["disk-write"] = cachedDiskWriteBytes is double diskWrite
            ? new SystemMonitorReading(FormatRate(diskWrite), "WRITE/s", RateLevel(diskWrite, 2L * 1024 * 1024 * 1024))
            : new SystemMonitorReading("—", "N/A", Available: false);
        CaptureNetwork(readings);
        readings["network-latency"] = cachedLatencyMs is double latency
            ? new SystemMonitorReading($"{latency:0}ms", "GATEWAY", Math.Clamp(latency / 250, 0, 1), Warning: latency >= 150)
            : new SystemMonitorReading("—", "N/A", Available: false);

        readings["virtual-desktop"] = ReadVirtualDesktop();
        readings["timer"] = DeckTimerService.Shared.Reading();
        DateTime now = DateTime.Now;
        readings["clock"] = new SystemMonitorReading(now.ToString("HH:mm"), now.ToString("ddd"), (now.Minute * 60d + now.Second) / 3600);
        readings["date"] = new SystemMonitorReading(now.ToString("M/d"), now.ToString("yyyy"), (now.Day - 1d) / DateTime.DaysInMonth(now.Year, now.Month));
        TimeSpan uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        readings["uptime"] = new SystemMonitorReading(uptime.TotalDays >= 1 ? $"{(int)uptime.TotalDays}d" : $"{(int)uptime.TotalHours}h", $"{uptime.Minutes:00}m", uptime.Minutes / 59d);

        SystemMonitorReading battery = Battery();
        readings["battery"] = battery;

        readings["volume"] = SystemControlService.TryGetVolume(false, out double volume, out bool muted)
            ? new SystemMonitorReading(muted ? "MUTE" : $"{volume:0}%", muted ? "MUTED" : "VOLUME", volume / 100, Warning: muted)
            : new SystemMonitorReading("—", "N/A", Available: false);
        readings["microphone"] = SystemControlService.TryGetVolume(true, out double microphone, out bool micMuted)
            ? new SystemMonitorReading(micMuted ? "OFF" : $"{microphone:0}%", micMuted ? "MUTED" : "MIC", microphone / 100, Warning: micMuted)
            : new SystemMonitorReading("—", "N/A", Available: false);
        readings["brightness"] = SystemControlService.TryGetBrightness(out double brightness)
            ? new SystemMonitorReading($"{brightness:0}%", "BRIGHTNESS", brightness / 100)
            : new SystemMonitorReading("—", "N/A", Available: false);

        bool warning = (cpu ?? 0) >= 90 || (memory ?? 0) >= 90 || battery.Warning;
        readings["system-status"] = new SystemMonitorReading(warning ? "CHECK" : "OK", warning ? "ATTENTION" : "NORMAL", warning ? .25 : 1, Warning: warning);
        return new SystemMonitorSnapshot(readings);
    }

    static SystemMonitorReading ReadVirtualDesktop()
    {
        try
        {
            var state = VirtualDesktopService.GetState();
            return VirtualDesktopReading(state.Count, state.CurrentNumber);
        }
        catch
        {
            // VirtualDesktopAccessor is an optional native boundary. A missing
            // or temporarily unavailable accessor must not abort every other
            // Deck monitor sample.
            return new SystemMonitorReading("—", "N/A", Available: false);
        }
    }

    internal static SystemMonitorReading VirtualDesktopReading(int count, int currentNumber)
        => count >= 1 && currentNumber >= 1 && currentNumber <= count
            ? new SystemMonitorReading(currentNumber.ToString(), $"OF {count}", currentNumber / (double)count)
            : new SystemMonitorReading("—", "N/A", Available: false);

    static SystemMonitorReading Percent(double? value, string detail)
        => value is double number
            ? new SystemMonitorReading($"{number:0}%", detail, Math.Clamp(number / 100, 0, 1), Warning: number >= 90)
            : new SystemMonitorReading("—", "N/A", Available: false);

    double? CpuUsage()
    {
        if (!ReadCpu(out long idle, out long kernel, out long user))
            return null;
        long idleDelta = idle - previousIdle;
        long totalDelta = (kernel - previousKernel) + (user - previousUser);
        previousIdle = idle;
        previousKernel = kernel;
        previousUser = user;
        if (totalDelta <= 0)
            return null;
        return Math.Clamp((totalDelta - idleDelta) * 100d / totalDelta, 0, 100);
    }

    static bool ReadCpu(out long idle, out long kernel, out long user)
    {
        idle = kernel = user = 0;
        if (!GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime))
            return false;
        idle = FileTime(idleTime);
        kernel = FileTime(kernelTime);
        user = FileTime(userTime);
        return true;
    }

    static long FileTime(FILETIME value) => ((long)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime;

    static double? MemoryUsage()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status.MemoryLoad : null;
    }

    static SystemMonitorReading DiskUsage()
    {
        try
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize <= 0)
                return new SystemMonitorReading("—", "N/A", Available: false);
            double usage = (drive.TotalSize - drive.AvailableFreeSpace) * 100d / drive.TotalSize;
            return new SystemMonitorReading($"{usage:0}%", drive.Name.TrimEnd('\\'), usage / 100, Warning: usage >= 92);
        }
        catch { return new SystemMonitorReading("—", "N/A", Available: false); }
    }

    void CaptureNetwork(Dictionary<string, SystemMonitorReading> readings)
    {
        ReadNetworkTotals(out long sent, out long received, out bool connected);
        long now = Stopwatch.GetTimestamp();
        double seconds = Math.Max((now - previousNetworkAt) / (double)Stopwatch.Frequency, .25);
        double sentPerSecond = Math.Max(0, sent - previousNetworkSent) / seconds;
        double receivedPerSecond = Math.Max(0, received - previousNetworkReceived) / seconds;
        previousNetworkSent = sent;
        previousNetworkReceived = received;
        previousNetworkAt = now;
        readings["network-up"] = new SystemMonitorReading(FormatRate(sentPerSecond), "SEND", RateLevel(sentPerSecond, 1024L * 1024 * 1024));
        readings["network-down"] = new SystemMonitorReading(FormatRate(receivedPerSecond), "RECEIVE", RateLevel(receivedPerSecond, 1024L * 1024 * 1024));
        readings["network-status"] = new SystemMonitorReading(connected ? "ONLINE" : "OFF", connected ? "CONNECTED" : "OFFLINE", connected ? 1 : 0, Warning: !connected);
        bool wifiConnected = NetworkInterface.GetAllNetworkInterfaces().Any(item => item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && item.OperationalStatus == OperationalStatus.Up);
        bool? wifiRadio = SystemControlService.TryGetWifiRadio(out bool wifiEnabled) ? wifiEnabled : null;
        readings["wifi"] = WifiReading(wifiConnected, wifiRadio);
        bool? bluetoothEnabled = RadioMonitorService.BluetoothEnabled;
        readings["bluetooth"] = bluetoothEnabled is bool bluetooth
            ? new SystemMonitorReading(bluetooth ? "ON" : "OFF", "CLICK TO OPEN", bluetooth ? 1 : 0, Warning: !bluetooth)
            : new SystemMonitorReading("—", "N/A", Available: false);
    }

    internal static SystemMonitorReading WifiReading(bool connected, bool? radioEnabled)
        => radioEnabled is bool enabled
            ? new SystemMonitorReading(enabled ? "ON" : "OFF", connected ? "CONNECTED" : "CLICK TO OPEN", enabled ? 1 : 0, Warning: !enabled)
            : connected
                ? new SystemMonitorReading("ON", "CONNECTED", 1)
                : new SystemMonitorReading("—", "N/A", Available: false);

    static void ReadNetworkTotals(out long sent, out long received, out bool connected)
    {
        sent = received = 0;
        connected = false;
        try
        {
            foreach (var item in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (item.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel || item.OperationalStatus != OperationalStatus.Up)
                    continue;
                var stats = item.GetIPv4Statistics();
                sent += stats.BytesSent;
                received += stats.BytesReceived;
                connected = true;
            }
        }
        catch { }
    }

    static string FormatRate(double bytes)
        => bytes >= 1024 * 1024 ? $"{bytes / (1024 * 1024):0.0}M" : bytes >= 1024 ? $"{bytes / 1024:0}K" : $"{bytes:0}B";

    internal static double RateLevel(double bytesPerSecond, double visualCeiling)
        => Math.Clamp(Math.Log10(1 + Math.Max(0, bytesPerSecond)) / Math.Log10(1 + visualCeiling), 0, 1);

    static string FormatCapacity(double bytes)
        => bytes >= 1024 * 1024 * 1024 ? $"{bytes / (1024 * 1024 * 1024):0.0}G" : $"{bytes / (1024 * 1024):0}M";

    static string ShortSensorName(string? name, string fallback)
    {
        string value = (name ?? string.Empty).Trim();
        if (value.Length == 0)
            return fallback;
        return value.Length <= 18 ? value : value[..17] + "…";
    }

    static SystemMonitorReading Battery()
    {
        try
        {
            var power = System.Windows.Forms.SystemInformation.PowerStatus;
            if (power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) || power.BatteryLifePercent < 0)
                return new SystemMonitorReading("—", "NO BATTERY", Available: false);
            double percent = Math.Clamp(power.BatteryLifePercent * 100d, 0, 100);
            bool plugged = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            bool charging = power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.Charging);
            bool low = !plugged && (percent <= 20 || power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.Low));
            BatteryVisualState state = charging ? BatteryVisualState.Charging
                : plugged && percent >= 99 ? BatteryVisualState.PluggedFull
                : plugged ? BatteryVisualState.Plugged
                : low ? BatteryVisualState.Low
                : BatteryVisualState.Discharging;
            string symbol = state switch
            {
                BatteryVisualState.Charging => "⚡",
                BatteryVisualState.PluggedFull => "✓",
                BatteryVisualState.Plugged => "AC",
                BatteryVisualState.Low => "!",
                _ => ""
            };
            string detail = state switch
            {
                BatteryVisualState.Charging => "CHARGING",
                BatteryVisualState.PluggedFull => "FULL",
                BatteryVisualState.Plugged => "PLUGGED IN",
                BatteryVisualState.Low => "LOW",
                _ => "BATTERY"
            };
            return new SystemMonitorReading($"{symbol}{percent:0}%", detail, percent / 100, BatteryState: state, Warning: low);
        }
        catch { return new SystemMonitorReading("—", "N/A", Available: false); }
    }

    static double? ReadGpuUsage()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            using var results = searcher.Get();
            var values = new List<double>();
            foreach (ManagementObject item in results)
            {
                string name = item["Name"]?.ToString() ?? "";
                if (!name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                    continue;
                values.Add(Convert.ToDouble(item["UtilizationPercentage"]));
            }
            return values.Count > 0 ? Math.Clamp(values.Max(), 0, 100) : null;
        }
        catch { return null; }
    }

    internal sealed record GpuMemorySample(double Bytes, string Detail);

    static GpuMemorySample? ReadGpuMemoryUsage()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DedicatedUsage, SharedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory");
            using var results = searcher.Get();
            double dedicated = 0;
            double shared = 0;
            int count = 0;
            foreach (ManagementObject item in results)
            {
                dedicated += Convert.ToDouble(item["DedicatedUsage"] ?? 0);
                shared += Convert.ToDouble(item["SharedUsage"] ?? 0);
                count++;
            }
            return SelectGpuMemoryUsage(dedicated, shared, count);
        }
        catch { return null; }
    }

    internal static GpuMemorySample? SelectGpuMemoryUsage(double dedicated, double shared, int adapterCount)
    {
        if (adapterCount <= 0)
            return null;
        if (dedicated > 0)
            return new GpuMemorySample(dedicated, "DEDICATED VRAM");
        if (shared > 0)
            return new GpuMemorySample(shared, "SHARED VRAM");
        return null;
    }

    internal const string DiskThroughputQuery = "SELECT Name, DiskReadBytesPersec, DiskWriteBytesPersec FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'";

    static (double? Read, double? Write) ReadDiskThroughput()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(DiskThroughputQuery);
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
                return (Convert.ToDouble(item["DiskReadBytesPersec"]), Convert.ToDouble(item["DiskWriteBytesPersec"]));
        }
        catch { }
        return (null, null);
    }

    static double? ReadGatewayLatency()
    {
        try
        {
            var gateway = NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.OperationalStatus == OperationalStatus.Up && item.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .SelectMany(item => item.GetIPProperties().GatewayAddresses)
                .Select(item => item.Address)
                .FirstOrDefault(address => !address.Equals(System.Net.IPAddress.Any) && !address.Equals(System.Net.IPAddress.IPv6Any));
            if (gateway == null)
                return null;
            using var ping = new Ping();
            var reply = ping.Send(gateway, 500);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch { return null; }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;
            disposed = true;
            timer.Dispose();
            SnapshotChanged = null;
            subscribers = 0;
            HardwareSensorClient.Shared.Dispose();
            RadioMonitorService.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct MemoryStatusEx
    {
        internal uint Length;
        internal uint MemoryLoad;
        internal ulong TotalPhysical;
        internal ulong AvailablePhysical;
        internal ulong TotalPageFile;
        internal ulong AvailablePageFile;
        internal ulong TotalVirtual;
        internal ulong AvailableVirtual;
        internal ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
}
