using System.IO;
using System.Text;

namespace RELYR;

internal static class DiagnosticLogStorage
{
    internal const long MaximumLogBytes = 512 * 1024;
    internal static readonly TimeSpan MaximumLogAge = TimeSpan.FromDays(14);
    static readonly object Gate = new();
    static int detailedDiagnosticsEnabled;
    static int legacyCleanupCompleted;

    internal static bool DetailedDiagnosticsEnabled => Volatile.Read(ref detailedDiagnosticsEnabled) != 0;

    internal static string IpcStatusLogPath
    {
        get
        {
#if !PRODUCTION_PUBLISH
            return VerificationPaths.GetFile("ipc-status.log");
#else
            return Path.Combine(LocalLogDirectory, "ipc-status.log");
#endif
        }
    }

    internal static string DetailedLogPath
    {
        get
        {
#if !PRODUCTION_PUBLISH
            return VerificationPaths.GetFile("diagnostics-detail.log");
#else
            return Path.Combine(LocalLogDirectory, "diagnostics-detail.log");
#endif
        }
    }

    internal static string LifecycleStatusLogPath
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("RELYR_CONFIG_DIR");
            string directory = string.IsNullOrWhiteSpace(configured) ? ConfigService.DefaultDirectoryPath : configured;
            return Path.Combine(directory, "lifecycle-status.log");
        }
    }

    static string LocalLogDirectory => ConfigService.LocalDataDirectoryPath;

    internal static void Configure(bool detailedDiagnosticsEnabled)
    {
        Volatile.Write(ref DiagnosticLogStorage.detailedDiagnosticsEnabled, detailedDiagnosticsEnabled ? 1 : 0);
        DeleteLegacySensitiveLogsOnce();
        if (!detailedDiagnosticsEnabled)
            TryDelete(DetailedLogPath);
    }

    internal static void WriteStatus(string path, string source, string eventName)
        => AppendBounded(path, FormatLine(source, eventName, null));

    internal static void WriteDetailed(string source, string eventName, string? detail)
    {
        if (!DetailedDiagnosticsEnabled)
            return;
        AppendBounded(DetailedLogPath, FormatLine(source, eventName, detail));
    }

    internal static bool DeleteAllLogs()
    {
        bool deleted = true;
        foreach (string path in AllKnownLogPaths())
            deleted &= TryDelete(path);
        return deleted;
    }

    internal static void AppendBounded(string path, string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                int maximumCharacters = (int)(MaximumLogBytes / 4) - 32;
                if (line.Length > maximumCharacters)
                    line = line[..maximumCharacters] + "...[truncated]";
                byte[] content = Encoding.UTF8.GetBytes(line + Environment.NewLine);
                using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                bool expired = File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.Subtract(MaximumLogAge);
                if (expired || stream.Length + content.Length > MaximumLogBytes)
                    stream.SetLength(0);
                stream.Seek(0, SeekOrigin.End);
                stream.Write(content);
            }
        }
        catch
        {
            // Diagnostics must never affect startup, shutdown, or input ownership.
        }
    }

    static string FormatLine(string source, string eventName, string? detail)
    {
        var builder = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
            .Append(" pid=").Append(Environment.ProcessId)
            .Append(" source=").Append(NormalizeToken(source))
            .Append(" event=").Append(NormalizeToken(eventName));
        if (!string.IsNullOrWhiteSpace(detail))
            builder.Append(" detail=").Append(detail.Replace('\r', ' ').Replace('\n', ' '));
        return builder.ToString();
    }

    static string NormalizeToken(string value)
    {
        string normalized = new(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.').Take(80).ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }

    static void DeleteLegacySensitiveLogsOnce()
    {
        if (Interlocked.Exchange(ref legacyCleanupCompleted, 1) != 0)
            return;
        foreach (string path in LegacyLogPaths())
            TryDelete(path);
    }

    static IEnumerable<string> AllKnownLogPaths()
        => [LifecycleStatusLogPath, IpcStatusLogPath, DetailedLogPath, .. LegacyLogPaths()];

    static IEnumerable<string> LegacyLogPaths()
    {
        string? configured = Environment.GetEnvironmentVariable("RELYR_CONFIG_DIR");
        string configDirectory = string.IsNullOrWhiteSpace(configured) ? ConfigService.DefaultDirectoryPath : configured;
        yield return Path.Combine(configDirectory, "lifecycle.log");
#if !PRODUCTION_PUBLISH
        yield return VerificationPaths.GetFile("deck-ipc-diagnostics.log");
        yield return VerificationPaths.GetFile("elevated-ipc-startup.log");
#else
        yield return Path.Combine(LocalLogDirectory, "deck-ipc-diagnostics.log");
        yield return Path.Combine(LocalLogDirectory, "elevated-ipc-startup.log");
#endif
    }

    static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
