using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RELYR;

public sealed class ConfigService
{
    internal const int CurrentVersion = 26;

    const string SettingsFileName = "settings.json";
    const int RetainedBackupCount = 20;
    const int DisabledMappingMigrationVersion = 18;
    const int GestureLongPressMigrationVersion = 20;
    const int GestureThresholdMigrationVersion = 22;
    const int DeckLayoutMigrationVersion = 25;
    const int ForceEngineEnabledMigrationVersion = 8;
    const int ClearStaleLongPressValueMigrationVersion = 9;
    static readonly Lock MigrationLock = new();
    readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public string DirectoryPath
    {
        get;
    }
    public string FilePath => Path.Combine(DirectoryPath, SettingsFileName);
    internal static string DefaultDirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RELYR");
    internal static string LegacyDirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InputCustomizer");
    public ConfigService(string? directoryPath = null)
    {
        DirectoryPath = directoryPath ?? Environment.GetEnvironmentVariable("RELYR_CONFIG_DIR") ?? DefaultDirectoryPath;
        if (directoryPath == null && Environment.GetEnvironmentVariable("RELYR_CONFIG_DIR") == null)
            MigrateLegacyDirectory(LegacyDirectoryPath, DirectoryPath);
    }

    internal static bool MigrateLegacyDirectory(string legacyDirectory, string destinationDirectory)
    {
        lock (MigrationLock)
        {
            if (Path.GetFullPath(legacyDirectory).Equals(Path.GetFullPath(destinationDirectory), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(legacyDirectory))
                return false;
            if (!Directory.Exists(destinationDirectory))
            {
                Directory.Move(legacyDirectory, destinationDirectory);
                return true;
            }
            string backupRoot = Path.Combine(destinationDirectory, "migration-backup");
            foreach (string source in Directory.EnumerateFiles(legacyDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(legacyDirectory, source);
                string destination = Path.Combine(destinationDirectory, relative);
                if (File.Exists(destination))
                    destination = UniqueMigrationPath(Path.Combine(backupRoot, relative));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, false);
            }
            Directory.Delete(legacyDirectory, true);
            return true;
        }
    }

    static string UniqueMigrationPath(string path)
    {
        if (!File.Exists(path))
            return path;
        string directory = Path.GetDirectoryName(path)!, name = Path.GetFileNameWithoutExtension(path), extension = Path.GetExtension(path);
        for (int index = 2; ; index++)
        {
            string candidate = Path.Combine(directory, $"{name}-{index}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    internal static bool DeleteAllUserData() => DeleteUserDataDirectories(DefaultDirectoryPath, LegacyDirectoryPath);
    internal static bool DeleteUserDataDirectories(params string[] directories)
    {
        bool deleted = true;
        foreach (string directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
            catch { deleted = false; }
        }
        return deleted;
    }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return CreateDefault();
            var loaded = DeserializeConfig(File.ReadAllText(FilePath));
            int storedVersion = loaded.Version;
            var normalized = Normalize(loaded);
            if (normalized.Version != storedVersion)
                Save(normalized);
            return normalized;
        }
        catch
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Copy(FilePath, Path.Combine(DirectoryPath, $"settings-corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true);
            }
            catch { }
            return CreateDefault();
        }
    }

    public void Save(AppConfig value)
    {
        Directory.CreateDirectory(DirectoryPath);
        CreateBackupIfPresent();

        string temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, jsonOptions));
        File.Move(temporaryPath, FilePath, true);
    }

    void CreateBackupIfPresent()
    {
        if (!File.Exists(FilePath))
            return;

        string backupPath = Path.Combine(DirectoryPath, $"settings-{DateTime.Now:yyyyMMdd-HHmmss-fffffff}.bak.json");
        File.Copy(FilePath, backupPath, true);

        foreach (var oldBackup in new DirectoryInfo(DirectoryPath)
                     .GetFiles("*.bak.json")
                     .OrderByDescending(file => file.CreationTimeUtc)
                     .Skip(RetainedBackupCount))
        {
            oldBackup.Delete();
        }
    }

    public void Export(AppConfig value, string path) => File.WriteAllText(path, JsonSerializer.Serialize(value, jsonOptions));
    public AppConfig Clone(AppConfig value) => JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(value, jsonOptions), jsonOptions) ?? throw new InvalidOperationException("設定を複製できません。");
    public AppConfig Import(string path)
    {
        var result = DeserializeConfig(File.ReadAllText(path));
        if (result.Version > CurrentVersion)
            throw new InvalidDataException("この設定は新しいバージョンで作成されています。");
        if (result.Profiles is null || result.Profiles.Count == 0)
            throw new InvalidDataException("有効なプロファイルがありません。");
        result = Normalize(result);
        result.CapsLockRemapPendingRestart = false;
        result.CapsLockRemapEffectiveBeforeRestart = false;
        result.CapsLockRemapChangedAtUtcTicks = 0;
        var errors = ConfigValidator.Validate(result);
        if (errors.Count > 0)
            throw new InvalidDataException("設定ファイルに問題があります。\n" + string.Join("\n", errors));
        return result;
    }

    AppConfig DeserializeConfig(string json)
    {
        JsonNode? parsed = JsonNode.Parse(json);
        if (parsed is not JsonObject root)
            return new AppConfig();
        int storedVersion = root["Version"]?.GetValue<int>() ?? 0;
        if (storedVersion < DisabledMappingMigrationVersion && root["Profiles"] is JsonArray profiles)
        {
            foreach (JsonObject profile in profiles.OfType<JsonObject>())
            {
                if (profile["Mappings"] is not JsonArray mappings)
                    continue;
                for (int index = mappings.Count - 1; index >= 0; index--)
                {
                    if (mappings[index] is JsonObject mapping && mapping["Enabled"]?.GetValue<bool>() == false)
                        mappings.RemoveAt(index);
                }
            }
        }
        return root.Deserialize<AppConfig>(jsonOptions) ?? new AppConfig();
    }

    static AppConfig Normalize(AppConfig value)
    {
        int originalVersion = value.Version;
        EnsureRequiredCollections(value);
        NormalizeGestureThreshold(value, originalVersion);
        NormalizeArchiveFolders(value);
        NormalizeMacros(value.Macros);
        // v7以前ではWindows再起動を異常終了と誤認し、全レイヤー停止が保存されることがあったため一度だけ復旧する。
        if (originalVersion < ForceEngineEnabledMigrationVersion)
            value.EngineEnabled = true;
        if (value.Profiles.Count == 0)
            value.Profiles.Add(new Profile());
        foreach (var profile in value.Profiles)
        {
            profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? "名称未設定" : profile.Name;
            profile.Mappings ??= [];
            profile.DefaultDeckLayoutId ??= "";
            profile.AutoSwitchApplications ??= [];
            NormalizeMappings(profile.Mappings, originalVersion);
        }
        NormalizeSharedDeckMappings(value.SharedDeckMappings, originalVersion);
        NormalizeDeckLayouts(value, originalVersion);
        NormalizeOverlayPositions(value);
        NormalizeActiveProfile(value);
        NormalizeTheme(value);
        value.Version = CurrentVersion;
        return value;
    }

    static void EnsureRequiredCollections(AppConfig value)
    {
        value.Profiles ??= [];
        value.Macros ??= [];
        value.Gestures ??= [];
        value.SharedDeckMappings ??= [];
        value.DeckLayouts ??= [];
    }

    static void NormalizeGestureThreshold(AppConfig value, int originalVersion)
    {
        if (originalVersion < GestureThresholdMigrationVersion && value.GestureThresholdPixels is 6 or 24)
        {
            value.GestureThresholdPixels = 12;
            return;
        }

        value.GestureThresholdPixels = Math.Clamp(value.GestureThresholdPixels, 3, 100);
    }

    static void NormalizeArchiveFolders(AppConfig value)
    {
        value.ArchiveWatchFolder ??= "";
        value.ArchiveDestinationFolder ??= "";
    }

    static void NormalizeMacros(IEnumerable<MacroDefinition> macros)
    {
        var macroIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var macro in macros)
        {
            macro.Steps ??= [];
            foreach (var step in macro.Steps)
                step.RecordedActionValue ??= "";

            if (string.IsNullOrWhiteSpace(macro.Id) || !macroIds.Add(macro.Id))
                macro.Id = CreateUniqueMacroId(macroIds);
        }
    }

    static string CreateUniqueMacroId(ISet<string> existingIds)
    {
        string id;
        do
        {
            id = Guid.NewGuid().ToString("N");
        } while (!existingIds.Add(id));

        return id;
    }

    static void NormalizeSharedDeckMappings(List<Mapping> mappings, int originalVersion)
    {
        NormalizeMappings(mappings, originalVersion);
        mappings.RemoveAll(mapping => !DeckPanelLayout.IsInputName(mapping.Input));
    }

    static void NormalizeOverlayPositions(AppConfig value)
    {
        value.DeckPanelLeft = FiniteOrNull(value.DeckPanelLeft);
        value.DeckPanelTop = FiniteOrNull(value.DeckPanelTop);
        value.DeckPanelWidth = PositiveFiniteOrNull(value.DeckPanelWidth);
        value.DeckPanelHeight = PositiveFiniteOrNull(value.DeckPanelHeight);
        value.NumpadPanelLeft = FiniteOrNull(value.NumpadPanelLeft);
        value.NumpadPanelTop = FiniteOrNull(value.NumpadPanelTop);
        value.ExtendedKeypadPanelLeft = FiniteOrNull(value.ExtendedKeypadPanelLeft);
        value.ExtendedKeypadPanelTop = FiniteOrNull(value.ExtendedKeypadPanelTop);
    }

    static double? FiniteOrNull(double? value)
        => value is double number && double.IsFinite(number) ? number : null;
    static double? PositiveFiniteOrNull(double? value)
        => value is double number && double.IsFinite(number) && number > 0 ? number : null;

    static void NormalizeActiveProfile(AppConfig value)
    {
        if (!value.Profiles.Any(profile => profile.Name == value.ActiveProfile))
            value.ActiveProfile = value.Profiles[0].Name;
    }

    static void NormalizeTheme(AppConfig value)
    {
        if (!Enum.IsDefined(value.ThemeMode))
            value.ThemeMode = AppThemeMode.System;
    }

    static void NormalizeDeckLayouts(AppConfig value, int originalVersion)
    {
        value.DefaultDeckLayoutId ??= "";
        value.SharedDefaultDeckLayoutId ??= "";
        if (originalVersion < DeckLayoutMigrationVersion)
        {
            value.DeckLayouts.Clear();
            value.DefaultDeckLayoutId = "";
            value.SharedDefaultDeckLayoutId = "";
            foreach (var profile in value.Profiles)
                profile.DefaultDeckLayoutId = "";
            IEnumerable<Mapping> sourceMappings = value.UseSharedDeckPanel
                ? value.SharedDeckMappings
                : (value.Profiles.FirstOrDefault(x => x.Name.Equals(value.ActiveProfile, StringComparison.OrdinalIgnoreCase)) ?? value.Profiles[0]).Mappings;
            var standard = CreateMigratedLayout("標準Deck", sourceMappings);
            value.DeckLayouts.Add(standard);
            value.DefaultDeckLayoutId = standard.Id;

            if (!value.UseSharedDeckPanel)
            {
                foreach (var profile in value.Profiles)
                {
                    var mappings = profile.Mappings.Where(x => DeckPanelLayout.IsInputName(x.Input)).ToList();
                    if (mappings.Count > 0 && !DeckMappingsEqual(standard.Mappings, mappings))
                        value.DeckLayouts.Add(CreateMigratedLayout(profile.Name + " Deck", mappings));
                }
            }
        }
        if (value.DeckLayouts.Count == 0)
            value.DeckLayouts.Add(CreateMigratedLayout("標準Deck", []));

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layout in value.DeckLayouts)
        {
            layout.Name = string.IsNullOrWhiteSpace(layout.Name) ? "名称未設定" : layout.Name.Trim().Replace("デッキ", "Deck", StringComparison.Ordinal);
            layout.Columns = Math.Clamp(layout.Columns, 1, DeckPanelLayout.MaximumColumns);
            layout.Rows = Math.Clamp(layout.Rows, 1, DeckPanelLayout.MaximumRows);
            layout.PanelColor ??= "";
            if (!DeckPanelLayout.TryParseButtonColor(layout.PanelColor, out _))
                layout.PanelColor = "";
            layout.Mappings ??= [];
            NormalizeMappings(layout.Mappings, originalVersion);
            layout.Mappings.RemoveAll(x => !DeckPanelLayout.IsInputName(x.Input));
            if (string.IsNullOrWhiteSpace(layout.Id) || !ids.Add(layout.Id))
            {
                do
                {
                    layout.Id = Guid.NewGuid().ToString("N");
                } while (!ids.Add(layout.Id));
            }
        }
        ids = configuredIds(value.DeckLayouts);
        string fallback = ids.Contains(value.DefaultDeckLayoutId) ? value.DefaultDeckLayoutId
            : ids.Contains(value.SharedDefaultDeckLayoutId) ? value.SharedDefaultDeckLayoutId
            : value.Profiles.Select(x => x.DefaultDeckLayoutId).FirstOrDefault(ids.Contains) ?? value.DeckLayouts[0].Id;
        value.DefaultDeckLayoutId = fallback;

        if (originalVersion == DeckLayoutMigrationVersion)
        {
            var defaultLayout = value.DeckLayouts.First(x => x.Id.Equals(fallback, StringComparison.OrdinalIgnoreCase));
            foreach (var layout in value.DeckLayouts.Where(x => x != defaultLayout && x.Mappings.Count == 0 && IsGeneratedProfileDeckName(value.Profiles, x.Name)).ToList())
                value.DeckLayouts.Remove(layout);
            if (IsGeneratedProfileDeckName(value.Profiles, defaultLayout.Name) || defaultLayout.Name == "標準Deck")
                defaultLayout.Name = "標準Deck";
        }
        ids = configuredIds(value.DeckLayouts);
        foreach (var profile in value.Profiles)
            profile.DefaultDeckLayoutId = fallback;
        value.SharedDefaultDeckLayoutId = fallback;
        value.UseSharedDeckPanel = false;

        static HashSet<string> configuredIds(IEnumerable<DeckLayoutDefinition> layouts)
            => new(layouts.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
    }

    static bool IsGeneratedProfileDeckName(IEnumerable<Profile> profiles, string name)
        => profiles.Any(x => name.Equals(x.Name + " デッキ", StringComparison.Ordinal) || name.Equals(x.Name + " Deck", StringComparison.Ordinal));

    static bool DeckMappingsEqual(IEnumerable<Mapping> left, IEnumerable<Mapping> right)
        => DeckMappingSignature(left) == DeckMappingSignature(right);

    static string DeckMappingSignature(IEnumerable<Mapping> mappings) => string.Join("\n", mappings.Where(x => DeckPanelLayout.IsInputName(x.Input)).OrderBy(x => DeckPanelLayout.SlotNumber(x.Input)).Select(x => $"{x.Input}\u001f{x.Kind}\u001f{x.Value}\u001f{x.LongPressKind}\u001f{x.LongPressValue}\u001f{x.LongPressMs}\u001f{x.Application}\u001f{x.Description}\u001f{x.DeckColor}\u001f{x.DeckFilePath}"));

    static DeckLayoutDefinition CreateMigratedLayout(string name, IEnumerable<Mapping> mappings) => new()
    {
        Name = name,
        Columns = DeckPanelLayout.Columns,
        Rows = DeckPanelLayout.Rows,
        Mappings = [.. mappings.Where(x => DeckPanelLayout.IsInputName(x.Input)).Select(CloneMapping)]
    };

    static Mapping CloneMapping(Mapping mapping) => mapping.Copy();

    static void NormalizeMappings(IEnumerable<Mapping> mappings, int originalVersion)
    {
        foreach (var map in mappings)
        {
            map.Input ??= "";
            map.Value ??= "";
            map.LongPressValue ??= "";
            map.DragValue ??= "";
            map.DragEndValue ??= "";
            map.Application ??= "";
            map.Layer ??= "通常";
            map.Description ??= "";
            map.DeckColor ??= "";
            map.DeckFilePath ??= "";
            if (!DeckPanelLayout.TryParseButtonColor(map.DeckColor, out _))
                map.DeckColor = "";
            if (map.Input.Equals("F13", StringComparison.OrdinalIgnoreCase))
                map.Input = "CapsLock";
            else if (map.Input.StartsWith("F13+", StringComparison.OrdinalIgnoreCase))
                map.Input = "CapsLock" + map.Input[3..];
            if (map.Layer.Equals("F13", StringComparison.OrdinalIgnoreCase))
                map.Layer = "CapsLock";
            if (originalVersion < ClearStaleLongPressValueMigrationVersion && map.LongPressKind == ActionKind.None)
                map.LongPressValue = "";
            if (map.Kind is ActionKind.Key or ActionKind.Shortcut or ActionKind.Mouse && ActionCatalog.TryNormalizeMouseAction(map.Value, out string mouseValue))
            {
                map.Kind = ActionKind.Mouse;
                map.Value = mouseValue;
            }
            if (map.LongPressKind is ActionKind.Key or ActionKind.Shortcut or ActionKind.Mouse && ActionCatalog.TryNormalizeMouseAction(map.LongPressValue, out string longMouseValue))
            {
                map.LongPressKind = ActionKind.Mouse;
                map.LongPressValue = longMouseValue;
            }
            if (originalVersion < GestureLongPressMigrationVersion && map.LongPressKind == ActionKind.Gesture && (map.Kind == ActionKind.None || string.IsNullOrWhiteSpace(map.Value)))
            {
                map.Kind = ActionKind.Gesture;
                map.Value = map.LongPressValue;
                map.LongPressKind = ActionKind.None;
                map.LongPressValue = "";
            }
        }
    }

    public AppConfig ResetToDefaults()
    {
        var value = CreateDefault();
        value.FirstRunCompleted = true;
        Save(value);
        return value;
    }

    internal static AppConfig CreateDefault() => Normalize(new() { Profiles = [new Profile { Name = "標準" }] });
}
