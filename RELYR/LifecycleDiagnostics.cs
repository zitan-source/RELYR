namespace RELYR;

internal static class LifecycleDiagnostics
{
    internal static string LogPath => DiagnosticLogStorage.LifecycleStatusLogPath;

    internal static void Write(string eventName, string? detail = null)
    {
        DiagnosticLogStorage.WriteStatus(LogPath, "lifecycle", eventName);
        DiagnosticLogStorage.WriteDetailed("lifecycle", eventName, detail);
    }
}
