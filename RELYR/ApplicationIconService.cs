using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace RELYR;

internal static class ApplicationIconService
{
    static readonly object CacheLock = new();
    static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    static ImageSource? fallbackIcon;

    internal static ImageSource GetIcon(string? pathOrExecutable)
    {
        string cacheKey = string.IsNullOrWhiteSpace(pathOrExecutable) ? "<application>" : pathOrExecutable.Trim();
        lock (CacheLock)
            if (Cache.TryGetValue(cacheKey, out var cached))
                return cached;

        string? path = ResolveExecutablePath(pathOrExecutable);
        ImageSource icon = TryExtractIcon(path) ?? FallbackIcon();
        lock (CacheLock)
            Cache[cacheKey] = icon;
        return icon;
    }

    internal static string? ResolveExecutablePath(string? pathOrExecutable)
    {
        if (string.IsNullOrWhiteSpace(pathOrExecutable))
            return null;

        string candidate = Environment.ExpandEnvironmentVariables(pathOrExecutable.Trim().Trim('"'));
        if (candidate.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            candidate = ShortcutService.ResolveShortcutTarget(candidate) ?? candidate;
        if (File.Exists(candidate))
            return Path.GetFullPath(candidate);

        string executable = Path.GetFileName(candidate);
        if (string.IsNullOrWhiteSpace(executable))
            return null;
        if (!executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            executable += ".exe";

        string processName = Path.GetFileNameWithoutExtension(executable);
        try
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    string? runningPath = TryGetProcessPath(process);
                    if (File.Exists(runningPath))
                        return runningPath;
                }
            }
        }
        catch { }

        foreach (string directPath in new[]
                 {
                     Path.Combine(Environment.SystemDirectory, executable),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), executable)
                 })
            if (File.Exists(directPath))
                return directPath;

        foreach (var (hive, view) in new[]
                 {
                     (RegistryHive.CurrentUser, RegistryView.Registry64),
                     (RegistryHive.LocalMachine, RegistryView.Registry64),
                     (RegistryHive.LocalMachine, RegistryView.Registry32)
                 })
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + executable);
                string? registered = key?.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(registered))
                {
                    registered = Environment.ExpandEnvironmentVariables(registered.Trim().Trim('"'));
                    if (File.Exists(registered))
                        return registered;
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
            catch (IOException) { }
        }
        return null;
    }

    internal static string DisplayName(string executable, string? resolvedPath = null)
    {
        string? path = resolvedPath ?? ResolveExecutablePath(executable);
        if (File.Exists(path))
        {
            try
            {
                string? description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(description))
                    return description.Trim();
            }
            catch { }
        }
        return Path.GetFileName(executable);
    }

    internal static string? TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    static ImageSource? TryExtractIcon(string? path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            return icon == null ? null : CreateImage(icon.Handle);
        }
        catch
        {
            return null;
        }
    }

    static ImageSource FallbackIcon()
    {
        lock (CacheLock)
            return fallbackIcon ??= CreateImage(System.Drawing.SystemIcons.Application.Handle);
    }

    static ImageSource CreateImage(IntPtr iconHandle)
    {
        BitmapSource image = Imaging.CreateBitmapSourceFromHIcon(iconHandle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
        image.Freeze();
        return image;
    }
}

internal sealed class ApplicationDisplayItem
{
    internal ApplicationDisplayItem(string label, string value, string? executablePath = null)
    {
        Label = label;
        Value = value;
        ExecutablePath = executablePath ?? ApplicationIconService.ResolveExecutablePath(value);
        Icon = ApplicationIconService.GetIcon(ExecutablePath ?? value);
    }

    internal static ApplicationDisplayItem FromExecutable(string executable)
    {
        string? path = ApplicationIconService.ResolveExecutablePath(executable);
        return new(ApplicationIconService.DisplayName(executable, path), Path.GetFileName(executable), path);
    }

    public string Label { get; }
    public string Value { get; }
    public string? ExecutablePath { get; }
    public ImageSource Icon { get; }
    public string Detail => Label.Equals(Value, StringComparison.CurrentCultureIgnoreCase) ? "" : Value;
}
