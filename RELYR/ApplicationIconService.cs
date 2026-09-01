using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace RELYR;

internal static class ApplicationIconService
{
    internal const int MaxCacheEntries = 256;
    static readonly object CacheLock = new();
    static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, ImageSource?> ExtractedIconCache = new(StringComparer.OrdinalIgnoreCase);
    static ImageSource? fallbackIcon;

    internal static ImageSource GetIcon(string? pathOrExecutable)
    {
        string cacheKey = string.IsNullOrWhiteSpace(pathOrExecutable) ? "<application>" : pathOrExecutable.Trim();
        lock (CacheLock)
            if (Cache.TryGetValue(cacheKey, out var cached))
                return cached;

        ImageSource icon = TryGetExtractedIcon(pathOrExecutable) ?? FallbackIcon();
        lock (CacheLock)
        {
            TrimCache(Cache);
            Cache[cacheKey] = icon;
        }
        return icon;
    }

    internal static ImageSource? TryGetExtractedIcon(string? pathOrExecutable)
    {
        if (string.IsNullOrWhiteSpace(pathOrExecutable))
            return null;

        string cacheKey = pathOrExecutable.Trim();
        lock (CacheLock)
            if (ExtractedIconCache.TryGetValue(cacheKey, out var cached))
                return cached;

        string candidate = Environment.ExpandEnvironmentVariables(pathOrExecutable.Trim().Trim('"'));
        ImageSource? icon = candidate.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            ? TryExtractShortcutIcon(candidate)
            : null;
        icon ??= TryExtractIcon(ResolveExecutablePath(candidate));
        lock (CacheLock)
        {
            TrimCache(ExtractedIconCache);
            ExtractedIconCache[cacheKey] = icon;
        }
        return icon;
    }

    static void TrimCache<T>(Dictionary<string, T> cache)
    {
        while (cache.Count >= MaxCacheEntries)
            cache.Remove(cache.Keys.First());
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

    static ImageSource? TryExtractShortcutIcon(string shortcutPath)
    {
        string? location = ShortcutService.ResolveShortcutIconLocation(shortcutPath);
        if (string.IsNullOrWhiteSpace(location))
            return null;

        string expanded = Environment.ExpandEnvironmentVariables(location.Trim());
        int iconIndex = 0;
        int comma = expanded.LastIndexOf(',');
        if (comma >= 0 && int.TryParse(expanded[(comma + 1)..].Trim(), out int parsedIndex))
        {
            iconIndex = parsedIndex;
            expanded = expanded[..comma];
        }
        string iconPath = expanded.Trim().Trim('"');
        // Some shortcuts point IconLocation back to the .lnk. Asking Windows
        // for that shell presentation returns the shortcut overlay arrow.
        // Only extract a raw icon resource; otherwise fall back to the target.
        if (!File.Exists(iconPath) || Path.GetExtension(iconPath) is string extension
            && (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase)))
            return null;

        IntPtr[] large = [IntPtr.Zero];
        try
        {
            if (ExtractIconEx(iconPath, iconIndex, large, null, 1) == 0 || large[0] == IntPtr.Zero)
                return null;
            // ExtractIconEx returns the icon resource itself, not the Shell's
            // shortcut presentation, so the small arrow overlay is excluded.
            return CreateImage(large[0]);
        }
        catch { return null; }
        finally
        {
            if (large[0] != IntPtr.Zero)
                DestroyIcon(large[0]);
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint ExtractIconEx(string file, int index, IntPtr[]? largeIcons, IntPtr[]? smallIcons, uint iconCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(IntPtr icon);
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
