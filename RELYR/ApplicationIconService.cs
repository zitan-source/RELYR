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
            : candidate.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
                ? TryExtractInternetShortcutIcon(candidate)
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
        var visitedShortcuts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int depth = 0; depth < 8 && candidate.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase); depth++)
        {
            string fullShortcut = Path.GetFullPath(candidate);
            if (!visitedShortcuts.Add(fullShortcut))
                return null;
            string? target = ShortcutService.ResolveShortcutTarget(fullShortcut);
            if (string.IsNullOrWhiteSpace(target))
                return null;
            candidate = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        }
        if (IsShortcutContainer(candidate))
            return null;
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
        if (!File.Exists(path) || IsShortcutContainer(path!))
            return null;
        ImageSource? raw = TryExtractRawIcon(path!, 0);
        if (raw != null)
            return raw;
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
        if (TryParseIconLocation(location, out string iconPath, out int iconIndex)
            && !IsShortcutContainer(iconPath))
        {
            ImageSource? explicitIcon = TryExtractRawIcon(iconPath, iconIndex);
            if (explicitIcon != null)
                return explicitIcon;
        }

        // Never ask the Shell for the .lnk presentation: that is where Windows
        // paints the shortcut-arrow overlay. Resolve nested links and extract
        // only the target's raw icon resource instead.
        return TryExtractIcon(ResolveExecutablePath(shortcutPath));
    }

    static ImageSource? TryExtractInternetShortcutIcon(string shortcutPath)
    {
        try
        {
            string? iconFile = null;
            int iconIndex = 0;
            foreach (string line in File.ReadLines(shortcutPath))
            {
                if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                    iconFile = line[9..].Trim();
                else if (line.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(line[10..].Trim(), out iconIndex);
            }
            if (string.IsNullOrWhiteSpace(iconFile))
                return null;
            string iconPath = Environment.ExpandEnvironmentVariables(iconFile.Trim().Trim('"'));
            return IsShortcutContainer(iconPath) ? null : TryExtractRawIcon(iconPath, iconIndex);
        }
        catch { return null; }
    }

    static bool TryParseIconLocation(string? location, out string iconPath, out int iconIndex)
    {
        iconPath = string.Empty;
        iconIndex = 0;
        if (string.IsNullOrWhiteSpace(location))
            return false;
        string expanded = Environment.ExpandEnvironmentVariables(location.Trim());
        int comma = expanded.LastIndexOf(',');
        if (comma >= 0 && int.TryParse(expanded[(comma + 1)..].Trim(), out int parsedIndex))
        {
            iconIndex = parsedIndex;
            expanded = expanded[..comma];
        }
        iconPath = expanded.Trim().Trim('"');
        return File.Exists(iconPath);
    }

    static bool IsShortcutContainer(string path)
        => Path.GetExtension(path) is string extension
           && (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase));

    static ImageSource? TryExtractRawIcon(string iconPath, int iconIndex)
    {
        if (!File.Exists(iconPath) || IsShortcutContainer(iconPath))
            return null;

        IntPtr large = IntPtr.Zero;
        IntPtr small = IntPtr.Zero;
        try
        {
            const uint requestedIconSize = 256u | (32u << 16);
            int result = SHDefExtractIcon(iconPath, iconIndex, 0, out large, out small, requestedIconSize);
            if (result < 0 || large == IntPtr.Zero)
                return null;
            return CreateImage(large);
        }
        catch { return null; }
        finally
        {
            if (large != IntPtr.Zero)
                DestroyIcon(large);
            if (small != IntPtr.Zero)
                DestroyIcon(small);
        }
    }

    static ImageSource FallbackIcon()
    {
        lock (CacheLock)
            return fallbackIcon ??= CreateImage(System.Drawing.SystemIcons.Application.Handle);
    }

    static ImageSource CreateImage(IntPtr iconHandle)
    {
        BitmapSource image = Imaging.CreateBitmapSourceFromHIcon(iconHandle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        image.Freeze();
        return image;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHDefExtractIcon(string iconFile, int iconIndex, uint flags, out IntPtr largeIcon, out IntPtr smallIcon, uint iconSize);

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
