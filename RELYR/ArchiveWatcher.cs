using System.IO;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace RELYR;

public sealed class ArchiveWatcher(string? watchFolder = null) : IDisposable
{
    static readonly string[] SupportedSuffixes =
    [
        ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.lz", ".tar.zst", ".tgz", ".tbz2", ".txz", ".tzst",
        ".zip", ".7z", ".rar", ".tar", ".gz", ".gzip", ".bz2", ".xz", ".lz", ".lzip", ".zst", ".arj"
    ];
    const int MaximumEntries = 10_000;
    const long MaximumExpandedBytes = 20L * 1024 * 1024 * 1024;
    readonly string? watchFolderOverride = watchFolder;
    readonly HashSet<string> processing = new(StringComparer.OrdinalIgnoreCase);
    FileSystemWatcher? watcher;
    int generation;

    public event Action<string>? Status;

    public void Apply(AppConfig config)
    {
        generation++;
        watcher?.Dispose();
        watcher = null;
        if (!config.AutoExtractDesktopArchives)
            return;

        int currentGeneration = generation;
        string watchFolder, destinationFolder;
        try
        {
            watchFolder = watchFolderOverride ?? ResolveWatchFolder(config);
            destinationFolder = ResolveDestinationFolder(config, watchFolder);
        }
        catch (Exception ex) { Status?.Invoke($"自動解凍のフォルダー設定が正しくありません: {ex.Message}"); return; }
        if (!Directory.Exists(watchFolder))
        {
            Status?.Invoke($"自動解凍の監視フォルダーが見つかりません: {watchFolder}");
            return;
        }
        try
        {
            Directory.CreateDirectory(destinationFolder);
        }
        catch (Exception ex)
        {
            Status?.Invoke($"自動解凍の保存先を使用できません: {ex.Message}");
            return;
        }
        watcher = new FileSystemWatcher(watchFolder)
        {
            Filter = "*",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size
        };
        watcher.Created += (_, e) => StartExtract(e.FullPath, destinationFolder, config.DeleteArchiveAfterExtract, currentGeneration);
        watcher.Renamed += (_, e) => StartExtract(e.FullPath, destinationFolder, config.DeleteArchiveAfterExtract, currentGeneration);
        watcher.EnableRaisingEvents = true;
    }

    void StartExtract(string path, string destinationFolder, bool deleteArchive, int currentGeneration)
    {
        if (!IsSupported(path))
            return;
        lock (processing)
        if (!processing.Add(path))
            return;
        _ = ExtractWhenReady(path, destinationFolder, deleteArchive, currentGeneration);
    }

    async Task ExtractWhenReady(string path, string destinationFolder, bool deleteArchive, int currentGeneration)
    {
        try
        {
            for (int attempt = 0; attempt < 15 && currentGeneration == generation; attempt++)
            {
                try
                {
                    await Task.Delay(700);
                    if (currentGeneration != generation || !File.Exists(path))
                        return;
                    using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                    }
                    string destination = ExtractArchive(path, destinationFolder, () => currentGeneration != generation);
                    if (deleteArchive)
                        File.Delete(path);
                    Status?.Invoke($"自動解凍しました: {Path.GetFileName(path)} → {Path.GetFileName(destination)}");
                    return;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (InvalidDataException ex)
                {
                    Status?.Invoke($"解凍できません: {Path.GetFileName(path)}（{ex.Message}）");
                    return;
                }
                catch (NotSupportedException)
                {
                    Status?.Invoke($"未対応または暗号化された圧縮ファイルです: {Path.GetFileName(path)}");
                    return;
                }
            }
            if (currentGeneration == generation)
                Status?.Invoke($"圧縮ファイルを使用中のため解凍できませんでした: {Path.GetFileName(path)}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status?.Invoke($"自動解凍エラー: {ex.Message}"); }
        finally { lock (processing) processing.Remove(path); }
    }

    internal static bool IsSupported(string path) =>
        SupportedSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    internal static string ExtractArchive(string path, Func<bool>? cancelled = null)
        => ExtractArchive(path, null, cancelled);

    internal static string ExtractArchive(string path, string? destinationFolder, Func<bool>? cancelled = null)
    {
        if (!IsSupported(path))
            throw new NotSupportedException("対応していない拡張子です。");
        string parent = string.IsNullOrWhiteSpace(destinationFolder)
            ? Path.GetDirectoryName(path) ?? throw new InvalidDataException("保存先を取得できません。")
            : Path.GetFullPath(destinationFolder);
        Directory.CreateDirectory(parent);
        string baseName = ArchiveBaseName(path);
        string destination = UniqueDestination(parent, baseName);
        string temporary = Path.Combine(parent, $".{baseName}.extracting-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            string root = Path.GetFullPath(temporary) + Path.DirectorySeparatorChar;
            if (path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
            {
                ExtractSevenZip(path, temporary, root, cancelled);
                Directory.Move(temporary, destination);
                return destination;
            }
            using var input = File.OpenRead(path);
            using var reader = ReaderFactory.OpenReader(input, CreateReaderOptions());
            int entryCount = 0;
            long expanded = 0;
            while (reader.MoveToNextEntry())
            {
                var entry = reader.Entry;
                if (entry.IsDirectory)
                    continue;
                if (cancelled?.Invoke() == true)
                    throw new OperationCanceledException();
                if (++entryCount > MaximumEntries)
                    throw new InvalidDataException($"ファイル数が上限 {MaximumEntries:N0} を超えています。");
                checked
                {
                    expanded += entry.Size;
                }
                if (expanded > MaximumExpandedBytes)
                    throw new InvalidDataException("展開後のサイズが安全上限を超えています。");
                if (!string.IsNullOrEmpty(entry.LinkTarget))
                    throw new InvalidDataException("シンボリックリンクを含む圧縮ファイルは展開できません。");

                string relative = string.IsNullOrWhiteSpace(entry.Key)
                    ? Path.GetFileNameWithoutExtension(path)
                    : entry.Key.Replace('/', Path.DirectorySeparatorChar);
                string output = Path.GetFullPath(Path.Combine(temporary, relative));
                if (!output.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("保存先の外へ展開する項目を検出しました。");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                reader.WriteEntryToFile(output, new ExtractionOptions { ExtractFullPath = false, Overwrite = false });
            }
            Directory.Move(temporary, destination);
            return destination;
        }
        catch
        {
            try
            {
                if (Directory.Exists(temporary))
                    Directory.Delete(temporary, true);
            }
            catch { }
            throw;
        }
    }

    static void ExtractSevenZip(string path, string temporary, string root, Func<bool>? cancelled)
    {
        using var archive = ArchiveFactory.OpenArchive(path, CreateReaderOptions());
        var entries = archive.Entries.Where(entry => !entry.IsDirectory).ToArray();
        if (entries.Length > MaximumEntries)
            throw new InvalidDataException($"ファイル数が上限 {MaximumEntries:N0} を超えています。");
        long expanded = 0;
        foreach (var entry in entries)
        {
            if (cancelled?.Invoke() == true)
                throw new OperationCanceledException();
            checked
            {
                expanded += entry.Size;
            }
            if (expanded > MaximumExpandedBytes)
                throw new InvalidDataException("展開後のサイズが安全上限を超えています。");
            if (!string.IsNullOrEmpty(entry.LinkTarget))
                throw new InvalidDataException("シンボリックリンクを含む圧縮ファイルは展開できません。");
            string relative = string.IsNullOrWhiteSpace(entry.Key)
                ? Path.GetFileNameWithoutExtension(path)
                : entry.Key.Replace('/', Path.DirectorySeparatorChar);
            string output = Path.GetFullPath(Path.Combine(temporary, relative));
            if (!output.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("保存先の外へ展開する項目を検出しました。");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            entry.WriteToFile(output, new ExtractionOptions { ExtractFullPath = false, Overwrite = false });
        }
    }

    static string ArchiveBaseName(string path)
    {
        string name = Path.GetFileName(path);
        string? suffix = SupportedSuffixes.OrderByDescending(value => value.Length)
            .FirstOrDefault(value => name.EndsWith(value, StringComparison.OrdinalIgnoreCase));
        string result = suffix == null ? Path.GetFileNameWithoutExtension(name) : name[..^suffix.Length];
        return string.IsNullOrWhiteSpace(result) ? "展開したファイル" : result;
    }

    static string UniqueDestination(string parent, string baseName)
    {
        string destination = Path.Combine(parent, baseName);
        int suffix = 2;
        while (Directory.Exists(destination) || File.Exists(destination))
            destination = Path.Combine(parent, $"{baseName} ({suffix++})");
        return destination;
    }

    static ReaderOptions CreateReaderOptions()
    {
        // ZIP files created by older Japanese Windows tools often omit the UTF-8 flag
        // and store entry names as CP932. SharpCompress still honors UTF-8 metadata;
        // this encoding is only the fallback for archives that do not declare one.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return new ReaderOptions
        {
            ArchiveEncoding = new ArchiveEncoding { Default = Encoding.GetEncoding(932) }
        };
    }

    internal static string ResolveWatchFolder(AppConfig config) => string.IsNullOrWhiteSpace(config.ArchiveWatchFolder) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : Path.GetFullPath(config.ArchiveWatchFolder);
    internal static string ResolveDestinationFolder(AppConfig config, string watchFolder) => string.IsNullOrWhiteSpace(config.ArchiveDestinationFolder) ? watchFolder : Path.GetFullPath(config.ArchiveDestinationFolder);

    public void Dispose()
    {
        generation++;
        watcher?.Dispose();
        watcher = null;
    }
}
