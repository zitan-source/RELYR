using System.IO;
using System.Text;

namespace RELYR;

internal static class LifecycleDiagnostics
{
    const long MaximumLogBytes = 512 * 1024;
    static readonly object Gate = new();

    internal static string LogPath
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("RELYR_CONFIG_DIR");
            string directory = string.IsNullOrWhiteSpace(configured) ? ConfigService.DefaultDirectoryPath : configured;
            return Path.Combine(directory, "lifecycle.log");
        }
    }

    internal static void Write(string eventName, string? detail = null)
    {
        try
        {
            lock (Gate)
            {
                string path = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                if (stream.Length > MaximumLogBytes)
                    stream.SetLength(0);
                stream.Seek(0, SeekOrigin.End);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(DateTimeOffset.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                writer.Write(" pid=");
                writer.Write(Environment.ProcessId);
                writer.Write(" event=");
                writer.Write(eventName);
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    writer.Write(" detail=");
                    writer.Write(detail.Replace('\r', ' ').Replace('\n', ' '));
                }
                writer.WriteLine();
            }
        }
        catch
        {
            // Diagnostics must never affect startup, shutdown, or input ownership.
        }
    }
}
