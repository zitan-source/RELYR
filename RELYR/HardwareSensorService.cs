using System.IO;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;

namespace RELYR;

internal sealed record HardwareSensorSnapshot(
    double? CpuTemperature = null,
    string CpuTemperatureName = "",
    double? GpuTemperature = null,
    string GpuTemperatureName = "",
    double? FanRpm = null,
    string FanName = "");

internal sealed record HardwareSensorCandidate(HardwareType HardwareType, SensorType SensorType, string Name, double Value);

/// <summary>
/// Owns LibreHardwareMonitor only inside the dedicated sensor process.  The
/// ordinary UI/input processes never load a ring-0 sensor provider, so a bad
/// device driver or unsupported controller cannot terminate RELYR itself.
/// </summary>
internal sealed class HardwareSensorProvider : IDisposable
{
    readonly object sync = new();
    Computer? computer;
    bool disposed;

    internal HardwareSensorSnapshot Read()
    {
        lock (sync)
        {
            if (disposed)
                return new HardwareSensorSnapshot();
            HardwareSensorSnapshot snapshot = new();
            try
            {
                if (EnsureOpen())
                {
                    var candidates = new List<HardwareSensorCandidate>();
                    foreach (IHardware hardware in computer!.Hardware)
                        UpdateAndCollect(hardware, candidates);
                    snapshot = Select(candidates);
                }
            }
            catch (Exception error)
            {
                LifecycleDiagnostics.Write("hardware-sensor-read-failed", error.ToString());
                CloseComputer();
            }
            return KeepVerifiedHardwareSensors(snapshot);
        }
    }

    internal static HardwareSensorSnapshot KeepVerifiedHardwareSensors(HardwareSensorSnapshot snapshot)
    {
        // MSAcpi_ThermalZoneTemperature is a firmware thermal-zone value, not
        // necessarily the CPU package temperature, and it commonly remains at
        // a fixed value. Win32_Fan.DesiredSpeed is a requested target, not the
        // measured RPM. Never present either as a precise CPU/FAN reading.
        return snapshot;
    }

    bool EnsureOpen()
    {
        if (computer != null)
            return true;
        try
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true
            };
            computer.Open();
            return true;
        }
        catch (Exception error)
        {
            LifecycleDiagnostics.Write("hardware-sensor-open-failed", error.ToString());
            CloseComputer();
            return false;
        }
    }

    static void UpdateAndCollect(IHardware hardware, List<HardwareSensorCandidate> candidates)
    {
        try { hardware.Update(); }
        catch (Exception error) { LifecycleDiagnostics.Write("hardware-sensor-update-failed", $"{hardware.HardwareType}: {error.GetType().Name}"); }

        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.Value is not float value || !float.IsFinite(value))
                continue;
            candidates.Add(new HardwareSensorCandidate(hardware.HardwareType, sensor.SensorType, sensor.Name ?? string.Empty, value));
        }
        foreach (IHardware child in hardware.SubHardware)
            UpdateAndCollect(child, candidates);
    }

    internal static HardwareSensorSnapshot Select(IEnumerable<HardwareSensorCandidate> source)
    {
        HardwareSensorCandidate[] candidates = [.. source];
        var cpuTemperatures = candidates.Where(candidate => candidate.HardwareType == HardwareType.Cpu
            && candidate.SensorType == SensorType.Temperature && ValidTemperature(candidate.Value)).ToArray();
        var gpuTemperatures = candidates.Where(candidate => IsGpu(candidate.HardwareType)
            && candidate.SensorType == SensorType.Temperature && ValidTemperature(candidate.Value)).ToArray();
        var fans = candidates.Where(candidate => candidate.SensorType == SensorType.Fan && candidate.Value > 0 && candidate.Value < 100_000).ToArray();

        HardwareSensorCandidate? cpu = cpuTemperatures
            .OrderBy(candidate => TemperaturePriority(candidate.Name))
            .ThenByDescending(candidate => candidate.Value)
            .FirstOrDefault();
        HardwareSensorCandidate? gpu = gpuTemperatures
            .OrderBy(candidate => TemperaturePriority(candidate.Name))
            .ThenByDescending(candidate => candidate.Value)
            .FirstOrDefault();
        HardwareSensorCandidate? fan = fans
            .OrderBy(candidate => FanPriority(candidate.Name))
            .ThenByDescending(candidate => candidate.Value)
            .FirstOrDefault();

        return new HardwareSensorSnapshot(
            cpu?.Value, cpu?.Name ?? string.Empty,
            gpu?.Value, gpu?.Name ?? string.Empty,
            fan?.Value, fan?.Name ?? string.Empty);
    }

    static bool ValidTemperature(double value) => value is > -20 and < 150;
    static bool IsGpu(HardwareType type) => type is HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia;
    static int TemperaturePriority(string name)
    {
        if (name.Contains("Package", StringComparison.OrdinalIgnoreCase) || name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) || name.Contains("Tdie", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (name.Contains("Core", StringComparison.OrdinalIgnoreCase) || name.Contains("Average", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 2;
    }
    static int FanPriority(string name)
    {
        if (name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (name.Contains("System", StringComparison.OrdinalIgnoreCase) || name.Contains("Chassis", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 2;
    }

    void CloseComputer()
    {
        try { computer?.Close(); }
        catch { }
        computer = null;
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;
            disposed = true;
            CloseComputer();
        }
    }
}

internal static class HardwareSensorProcess
{
    internal static bool ValidPipeName(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && value.StartsWith("RELYR-", StringComparison.Ordinal);

    internal static async Task RunAsync(string pipeName, string bootstrapName)
    {
        if (!ValidPipeName(pipeName) || !ValidPipeName(bootstrapName))
            return;
        string executable = Environment.ProcessPath ?? string.Empty;
        string? secret = await IpcBootstrapClient.ReceiveSecretAsync(bootstrapName, executable, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(secret))
            return;
        using var provider = new HardwareSensorProvider();
        await using var server = new ElevatedIpcServer(
            pipeName,
            secret,
            executable,
            (message, _) => Task.FromResult(Handle(message, provider, secret)),
            expectedClientElevated: false);
        await server.Completion.ConfigureAwait(false);
    }

    static IpcMessage Handle(IpcMessage message, HardwareSensorProvider provider, string secret)
    {
        string value = message.Command switch
        {
            IpcCommand.Ping => "ok",
            IpcCommand.ReadHardwareSensors => JsonSerializer.Serialize(provider.Read()),
            IpcCommand.Shutdown => "ok",
            _ => "rejected"
        };
        return new IpcMessage(message.Command, message.RequestId, value, secret);
    }
}

internal sealed class HardwareSensorClient : IDisposable
{
    internal static HardwareSensorClient Shared { get; } = new();

    readonly SemaphoreSlim gate = new(1, 1);
    ElevatedIpcClient? client;
    DateTime retryAfterUtc;
    bool disposed;

    internal HardwareSensorSnapshot? TryRead()
    {
        if (!IpcRuntime.IsUiHost || StartupService.IsProcessElevated() || disposed || DateTime.UtcNow < retryAfterUtc)
            return null;
        try { return TryReadAsync().GetAwaiter().GetResult(); }
        catch (Exception error)
        {
            LifecycleDiagnostics.Write("hardware-sensor-client-failed", error.ToString());
            retryAfterUtc = DateTime.UtcNow.AddMinutes(1);
            return null;
        }
    }

    async Task<HardwareSensorSnapshot?> TryReadAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
                return null;
            if (client == null && !await StartAsync().ConfigureAwait(false))
            {
                retryAfterUtc = DateTime.UtcNow.AddMinutes(1);
                return null;
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            IpcMessage? response = await client!.SendAsync(IpcCommand.ReadHardwareSensors, cancellationToken: timeout.Token).ConfigureAwait(false);
            if (response == null || response.Value is "error" or "rejected")
                return null;
            return JsonSerializer.Deserialize<HardwareSensorSnapshot>(response.Value);
        }
        catch (OperationCanceledException)
        {
            await ResetClientAsync().ConfigureAwait(false);
            retryAfterUtc = DateTime.UtcNow.AddMinutes(1);
            return null;
        }
        catch (IOException)
        {
            await ResetClientAsync().ConfigureAwait(false);
            retryAfterUtc = DateTime.UtcNow.AddMinutes(1);
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        finally { gate.Release(); }
    }

    async Task<bool> StartAsync()
    {
        string executable = Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(executable))
            return false;
        string pipeName = IpcTransport.NewName("sensors");
        string bootstrapName = IpcTransport.NewName("sensors-bootstrap");
        string secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await using var bootstrap = new IpcBootstrapServer(bootstrapName, secret, executable);
        if (!StartupService.TryRunElevated(["--sensor-helper", pipeName, bootstrapName], out string launchError))
        {
            LifecycleDiagnostics.Write("hardware-sensor-launch-failed", launchError);
            return false;
        }
        try { await bootstrap.Completion.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            LifecycleDiagnostics.Write("hardware-sensor-bootstrap-timeout", bootstrap.Outcome);
            return false;
        }
        if (!bootstrap.Succeeded)
        {
            LifecycleDiagnostics.Write("hardware-sensor-bootstrap-failed", bootstrap.Outcome);
            return false;
        }

        var candidate = new ElevatedIpcClient(pipeName, secret, executable);
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (await candidate.ConnectAsync(TimeSpan.FromMilliseconds(700)).ConfigureAwait(false))
            {
                client = candidate;
                return true;
            }
            await Task.Delay(120).ConfigureAwait(false);
        }
        await candidate.DisposeAsync();
        return false;
    }

    async Task ResetClientAsync()
    {
        var current = client;
        client = null;
        if (current != null)
            await current.DisposeAsync();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (!gate.Wait(TimeSpan.FromMilliseconds(150)))
            return;
        try
        {
            if (client != null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { client.SendAsync(IpcCommand.Shutdown, cancellationToken: timeout.Token).GetAwaiter().GetResult(); }
                catch { }
                client.DisposeAsync().AsTask().GetAwaiter().GetResult();
                client = null;
            }
        }
        finally { gate.Release(); gate.Dispose(); }
    }
}
