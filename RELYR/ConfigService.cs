using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RELYR;

public sealed class ConfigService
{
    internal const int CurrentVersion = 35;
    const int MappingApplicationLossVersion = 29;

    const string SettingsFileName = "settings.json";
    const int RetainedBackupCount = 20;
    const int DisabledMappingMigrationVersion = 18;
    const int GestureLongPressMigrationVersion = 20;
    const int GestureThresholdMigrationVersion = 22;
    const int DeckLayoutMigrationVersion = 25;
    const int ActionKindRepairMigrationVersion = 28;
    const int DeckAutoDismissBehaviorMigrationVersion = 32;
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
    internal static string LocalDataDirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RELYR");
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

    internal static bool DeleteAllUserData() => DeleteUserDataDirectories(DefaultDirectoryPath, LegacyDirectoryPath, LocalDataDirectoryPath);
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
            RestoreV29MappingApplicationsFromBackup(loaded);
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

    void RestoreV29MappingApplicationsFromBackup(AppConfig current)
    {
        if (current.Version != MappingApplicationLossVersion)
            return;
        try
        {
            var backups = new DirectoryInfo(DirectoryPath)
                .GetFiles("settings-*.bak.json")
                .OrderBy(file => file.Name, StringComparer.Ordinal)
                .ToArray();
            AppConfig? preV29 = null;
            AppConfig? firstV29 = null;
            foreach (var backup in backups)
            {
                AppConfig candidate;
                try { candidate = DeserializeConfig(File.ReadAllText(backup.FullName)); }
                catch { continue; }
                if (candidate.Profiles == null || candidate.Profiles.Count == 0)
                    continue;
                if (candidate.Version < MappingApplicationLossVersion)
                    preV29 = candidate;
                else if (candidate.Version == MappingApplicationLossVersion && firstV29 == null)
                    firstV29 = candidate;
            }
            if (preV29 != null)
                RestoreMappingApplications(current, preV29, firstV29);
        }
        catch
        {
            // Backup recovery is best-effort. A missing or unreadable backup
            // must never make a valid primary settings file look corrupt.
        }
    }

    internal static int RestoreMappingApplications(AppConfig current, AppConfig preV29, AppConfig? firstV29)
    {
        int restored = 0;
        foreach (var currentProfile in current.Profiles ?? [])
        {
            var backupProfile = (preV29.Profiles ?? []).FirstOrDefault(profile =>
                string.Equals(profile.Name, currentProfile.Name, StringComparison.OrdinalIgnoreCase));
            if (backupProfile == null)
                continue;
            var anchorProfile = (firstV29?.Profiles ?? []).FirstOrDefault(profile =>
                string.Equals(profile.Name, currentProfile.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var mapping in currentProfile.Mappings ?? [])
            {
                if (!string.IsNullOrWhiteSpace(mapping.Application))
                    continue;
                if (firstV29 != null && (anchorProfile?.Mappings?.Any(anchor =>
                    string.Equals(anchor.Input, mapping.Input, StringComparison.OrdinalIgnoreCase)) != true))
                    continue;
                var backupMapping = (backupProfile.Mappings ?? []).LastOrDefault(candidate =>
                    string.Equals(candidate.Input, mapping.Input, StringComparison.OrdinalIgnoreCase));
                if (backupMapping == null || string.IsNullOrWhiteSpace(backupMapping.Application))
                    continue;
                mapping.Application = backupMapping.Application.Trim();
                restored++;
            }
        }
        return restored;
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
        if (storedVersion < DeckAutoDismissBehaviorMigrationVersion)
        {
            bool collapseAfterAction = root["DeckAutoHideAfterAction"]?.GetValue<bool>() ?? true;
            bool collapseOnPointerLeave = root["DeckAutoHideOnPointerLeave"]?.GetValue<bool>() ?? true;
            root[nameof(AppConfig.DeckAfterActionBehavior)] = collapseAfterAction
                ? nameof(DeckAutoDismissBehavior.CollapseToEdge)
                : nameof(DeckAutoDismissBehavior.StayVisible);
            root[nameof(AppConfig.DeckPointerLeaveBehavior)] = collapseOnPointerLeave
                ? nameof(DeckAutoDismissBehavior.CollapseToEdge)
                : nameof(DeckAutoDismissBehavior.StayVisible);
            root.Remove("DeckAutoHideAfterAction");
            root.Remove("DeckAutoHideOnPointerLeave");
        }
        return root.Deserialize<AppConfig>(jsonOptions) ?? new AppConfig();
    }

    static AppConfig Normalize(AppConfig value)
    {
        int originalVersion = value.Version;
        EnsureRequiredCollections(value);
        NormalizeScalarSettings(value);
        NormalizeGestureThreshold(value, originalVersion);
        NormalizeArchiveFolders(value);
        NormalizeMacros(value.Macros, originalVersion);
        NormalizeGestures(value.Gestures, originalVersion);
        // v7以前ではWindows再起動を異常終了と誤認し、全レイヤー停止が保存されることがあったため一度だけ復旧する。
        if (originalVersion < ForceEngineEnabledMigrationVersion)
            value.EngineEnabled = true;
        if (value.Profiles.Count == 0)
            value.Profiles.Add(new Profile());
        var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in value.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id) || !profileIds.Add(profile.Id))
            {
                do profile.Id = Guid.NewGuid().ToString("N");
                while (!profileIds.Add(profile.Id));
            }
            profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? "名称未設定" : profile.Name;
            profile.Mappings ??= [];
            profile.DefaultDeckLayoutId ??= "";
            profile.AutoSwitchApplications ??= [];
            profile.AutoSwitchApplications = [.. profile.AutoSwitchApplications
                .Where(application => !string.IsNullOrWhiteSpace(application))
                .Select(application => application.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)];
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
        value.InputDisabledApplications ??= [];
        value.ActionPaletteFavorites ??= [];
        value.ActionPaletteRecentActions ??= [];
        value.Macros ??= [];
        value.Gestures ??= [];
        value.SharedDeckMappings ??= [];
        value.DeckLayouts ??= [];
        value.Profiles.RemoveAll(profile => profile is null);
        value.Macros.RemoveAll(macro => macro is null);
        value.Gestures.RemoveAll(gesture => gesture is null);
        value.SharedDeckMappings.RemoveAll(mapping => mapping is null);
        value.DeckLayouts.RemoveAll(layout => layout is null);
        value.InputDisabledApplications = [.. value.InputDisabledApplications
            .Where(application => !string.IsNullOrWhiteSpace(application))
            .Select(application => Path.GetFileName(application.Trim()))
            .Where(application => !string.IsNullOrWhiteSpace(application))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        value.ActionPaletteFavorites = [.. value.ActionPaletteFavorites
            .Where(signature => !string.IsNullOrWhiteSpace(signature))
            .Select(signature => signature.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        value.ActionPaletteRecentActions = [.. value.ActionPaletteRecentActions
            .Where(signature => !string.IsNullOrWhiteSpace(signature))
            .Select(signature => signature.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)];
    }

    static void NormalizeScalarSettings(AppConfig value)
    {
        value.ActiveProfile ??= "";
        value.DismissedUpdateVersion ??= "";
        value.PendingUpdateNotesVersion ??= "";
        value.PendingUpdateNotesBody ??= "";
        value.LastShownUpdateNotesVersion ??= "";
        value.KeyboardLayout = value.KeyboardLayout?.Equals("US", StringComparison.OrdinalIgnoreCase) == true ? "US" : "JIS";
        value.EmergencyShortcut = string.IsNullOrWhiteSpace(value.EmergencyShortcut) ? "Ctrl+Alt+Shift+F12" : value.EmergencyShortcut;
        value.ClockBackgroundImage ??= "";
        value.ClockSolidColor ??= "#101F2E";
        value.SpaceHoldRepeatDelayMs = Math.Clamp(value.SpaceHoldRepeatDelayMs, 100, 2000);
        value.DoubleClickMs = Math.Clamp(value.DoubleClickMs, 100, 2000);
        value.MouseDragPixels = Math.Clamp(value.MouseDragPixels, 1, 100);
        value.InputPanelOpacityPercent = Math.Clamp(value.InputPanelOpacityPercent, 40, 100);
        value.LastUpdateCheckUtcTicks = Math.Max(0, value.LastUpdateCheckUtcTicks);
        if (!Enum.IsDefined(value.WindowActionTarget))
            value.WindowActionTarget = WindowActionTarget.ActiveWindow;
        if (!Enum.IsDefined(value.ClockBackgroundMode))
            value.ClockBackgroundMode = ClockBackgroundMode.FrostedScreen;
        if (!Enum.IsDefined(value.ClockDisplayMode))
            value.ClockDisplayMode = ClockDisplayMode.DateAndTime;
        if (!Enum.IsDefined(value.DeckAfterActionBehavior))
            value.DeckAfterActionBehavior = DeckAutoDismissBehavior.CollapseToEdge;
        if (!Enum.IsDefined(value.DeckPointerLeaveBehavior))
            value.DeckPointerLeaveBehavior = DeckAutoDismissBehavior.CollapseToEdge;
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

    static void NormalizeMacros(IEnumerable<MacroDefinition> macros, int originalVersion)
    {
        var macroIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var macro in macros)
        {
            macro.Name ??= "";
            macro.Steps ??= [];
            // Unknown recorded-action enum values cannot be executed or shown
            // safely. Remove the whole corrupt step instead of converting it
            // into an empty raw event that would fail validation later.
            macro.Steps.RemoveAll(step => step is null
                || step.RecordedActionKind is { } kind && !Enum.IsDefined(kind));
            foreach (var step in macro.Steps)
            {
                step.Event ??= "";
                step.RecordedActionValue ??= "";
                if (step.Event.StartsWith("MouseX ", StringComparison.OrdinalIgnoreCase))
                    step.Event = "MouseForward" + step.Event["MouseX".Length..];
                if (step.RecordedActionKind is { } kind)
                {
                    var repaired = RepairLegacyActionKind(kind, step.RecordedActionValue, originalVersion);
                    var normalized = NormalizeMouseAction(repaired.Kind, repaired.Value);
                    step.RecordedActionKind = normalized.Kind;
                    step.RecordedActionValue = normalized.Value;
                }
            }

            if (string.IsNullOrWhiteSpace(macro.Id) || !macroIds.Add(macro.Id))
                macro.Id = CreateUniqueMacroId(macroIds);
        }
    }

    static void NormalizeGestures(IEnumerable<GestureDefinition> gestures, int originalVersion)
    {
        foreach (var gesture in gestures)
        {
            gesture.Name ??= "";
            gesture.UpValue ??= "";
            gesture.DownValue ??= "";
            gesture.LeftValue ??= "";
            gesture.RightValue ??= "";
            gesture.CenterValue ??= "";
            if (!Enum.IsDefined(gesture.UpKind)) gesture.UpKind = ActionKind.None;
            if (!Enum.IsDefined(gesture.DownKind)) gesture.DownKind = ActionKind.None;
            if (!Enum.IsDefined(gesture.LeftKind)) gesture.LeftKind = ActionKind.None;
            if (!Enum.IsDefined(gesture.RightKind)) gesture.RightKind = ActionKind.None;
            if (!Enum.IsDefined(gesture.CenterKind)) gesture.CenterKind = ActionKind.None;
            (gesture.UpKind, gesture.UpValue) = RepairLegacyActionKind(gesture.UpKind, gesture.UpValue, originalVersion);
            (gesture.DownKind, gesture.DownValue) = RepairLegacyActionKind(gesture.DownKind, gesture.DownValue, originalVersion);
            (gesture.LeftKind, gesture.LeftValue) = RepairLegacyActionKind(gesture.LeftKind, gesture.LeftValue, originalVersion);
            (gesture.RightKind, gesture.RightValue) = RepairLegacyActionKind(gesture.RightKind, gesture.RightValue, originalVersion);
            (gesture.CenterKind, gesture.CenterValue) = RepairLegacyActionKind(gesture.CenterKind, gesture.CenterValue, originalVersion);
            (gesture.UpKind, gesture.UpValue) = NormalizeMouseAction(gesture.UpKind, gesture.UpValue);
            (gesture.DownKind, gesture.DownValue) = NormalizeMouseAction(gesture.DownKind, gesture.DownValue);
            (gesture.LeftKind, gesture.LeftValue) = NormalizeMouseAction(gesture.LeftKind, gesture.LeftValue);
            (gesture.RightKind, gesture.RightValue) = NormalizeMouseAction(gesture.RightKind, gesture.RightValue);
            (gesture.CenterKind, gesture.CenterValue) = NormalizeMouseAction(gesture.CenterKind, gesture.CenterValue);
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
        value.DeckPanelCollapsedLeft = FiniteOrNull(value.DeckPanelCollapsedLeft);
        value.DeckPanelCollapsedTop = FiniteOrNull(value.DeckPanelCollapsedTop);
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
        var profileIds = value.Profiles.Select(profile => profile.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
            layout.PanelWidth = PositiveFiniteOrNull(layout.PanelWidth);
            layout.PanelHeight = PositiveFiniteOrNull(layout.PanelHeight);
            layout.PanelLeft = FiniteOrNull(layout.PanelLeft);
            layout.PanelTop = FiniteOrNull(layout.PanelTop);
            layout.PanelCollapsedLeft = FiniteOrNull(layout.PanelCollapsedLeft);
            layout.PanelCollapsedTop = FiniteOrNull(layout.PanelCollapsedTop);
            layout.PanelColor ??= "";
            layout.PanelPadding = double.IsFinite(layout.PanelPadding) ? Math.Clamp(layout.PanelPadding, 4, 24) : 12;
            layout.PanelCornerRadius = double.IsFinite(layout.PanelCornerRadius) ? Math.Clamp(layout.PanelCornerRadius, 0, 24) : 14;
            layout.ProfileGroupId ??= "";
            layout.ProfileId ??= "";
            if (!layout.ProfileSwitchEnabled || !profileIds.Contains(layout.ProfileId))
            {
                layout.ProfileSwitchEnabled = false;
                layout.ProfileGroupId = "";
                layout.ProfileId = "";
            }
            else if (string.IsNullOrWhiteSpace(layout.ProfileGroupId))
                layout.ProfileGroupId = Guid.NewGuid().ToString("N");
            if (!DeckPanelLayout.TryParseButtonColor(layout.PanelColor, out _))
                layout.PanelColor = "";
            layout.Mappings ??= [];
            layout.Mappings.RemoveAll(mapping => mapping is null);
            NormalizeMappings(layout.Mappings, originalVersion);
            layout.Mappings.RemoveAll(x => !DeckPanelLayout.IsInputName(x.Input));
            foreach (var mapping in layout.Mappings)
            {
                // A Deck button is a click command, not a held physical input.
                // Gesture and long-press values would be displayed but could
                // never execute, so repair stale/imported settings explicitly.
                if (mapping.Kind == ActionKind.Gesture)
                {
                    mapping.Kind = ActionKind.None;
                    mapping.Value = "";
                }
                mapping.LongPressKind = ActionKind.None;
                mapping.LongPressValue = "";
            }
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
        {
            bool valid = value.DeckLayouts.Any(layout => layout.Id.Equals(profile.DefaultDeckLayoutId, StringComparison.OrdinalIgnoreCase)
                && (!layout.ProfileSwitchEnabled || layout.ProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)));
            if (!valid)
                profile.DefaultDeckLayoutId = value.DeckLayouts.FirstOrDefault(layout => !layout.ProfileSwitchEnabled)?.Id
                    ?? value.DeckLayouts.FirstOrDefault(layout => layout.ProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))?.Id
                    ?? fallback;
        }
        value.SharedDefaultDeckLayoutId = fallback;
        value.UseSharedDeckPanel = false;

        static HashSet<string> configuredIds(IEnumerable<DeckLayoutDefinition> layouts)
            => new(layouts.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
    }

    static bool IsGeneratedProfileDeckName(IEnumerable<Profile> profiles, string name)
        => profiles.Any(x => name.Equals(x.Name + " デッキ", StringComparison.Ordinal) || name.Equals(x.Name + " Deck", StringComparison.Ordinal));

    static bool DeckMappingsEqual(IEnumerable<Mapping> left, IEnumerable<Mapping> right)
        => DeckMappingSignature(left) == DeckMappingSignature(right);

    static string DeckMappingSignature(IEnumerable<Mapping> mappings) => string.Join("\n", mappings.Where(x => DeckPanelLayout.IsInputName(x.Input)).OrderBy(x => DeckPanelLayout.SlotNumber(x.Input)).Select(x => $"{x.Input}\u001f{x.Kind}\u001f{x.Value}\u001f{x.LongPressKind}\u001f{x.LongPressValue}\u001f{x.LongPressMs}\u001f{x.Application}\u001f{x.Description}\u001f{x.DeckColor}\u001f{x.DeckFilePath}\u001f{x.DeckMonitor}"));

    static DeckLayoutDefinition CreateMigratedLayout(string name, IEnumerable<Mapping> mappings) => new()
    {
        Name = name,
        Columns = DeckPanelLayout.Columns,
        Rows = DeckPanelLayout.Rows,
        Mappings = [.. mappings.Where(x => DeckPanelLayout.IsInputName(x.Input)).Select(CloneMapping)]
    };

    static Mapping CloneMapping(Mapping mapping) => mapping.Copy();

    static void NormalizeMappings(List<Mapping> mappings, int originalVersion)
    {
        mappings.RemoveAll(map => map is null);
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
            map.DeckIcon ??= "";
            map.DeckIconPath ??= "";
            map.DeckMonitor ??= "";
            if (map.DeckMonitor.Length > 0 && !DeckMonitorCatalog.IsMonitor(map.DeckMonitor))
                map.DeckMonitor = "";
            if (!string.IsNullOrWhiteSpace(map.DeckIconPath))
                map.DeckIconAutoAssigned = false;
            if (!Enum.IsDefined(map.Kind))
            {
                map.Kind = ActionKind.None;
                map.Value = "";
            }
            if (!Enum.IsDefined(map.LongPressKind))
            {
                map.LongPressKind = ActionKind.None;
                map.LongPressValue = "";
            }
            if (!DeckPanelLayout.TryParseButtonColor(map.DeckColor, out _))
                map.DeckColor = "";
            if (map.Input.Equals("F13", StringComparison.OrdinalIgnoreCase))
                map.Input = "CapsLock";
            else if (map.Input.StartsWith("F13+", StringComparison.OrdinalIgnoreCase))
                map.Input = "CapsLock" + map.Input[3..];
            if (map.Layer.Equals("F13", StringComparison.OrdinalIgnoreCase))
                map.Layer = "CapsLock";
            map.Layer = CanonicalLayer(map.Input);
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
            (map.Kind, map.Value) = RepairLegacyActionKind(map.Kind, map.Value, originalVersion);
            (map.LongPressKind, map.LongPressValue) = RepairLegacyActionKind(map.LongPressKind, map.LongPressValue, originalVersion);
            if (originalVersion < GestureLongPressMigrationVersion && map.LongPressKind == ActionKind.Gesture && (map.Kind == ActionKind.None || string.IsNullOrWhiteSpace(map.Value)))
            {
                map.Kind = ActionKind.Gesture;
                map.Value = map.LongPressValue;
                map.LongPressKind = ActionKind.None;
                map.LongPressValue = "";
            }
        }
        // Normal-layer left click is permanently reserved. Old or hand-edited
        // settings must not bypass the UI guard and swallow every physical click.
        mappings.RemoveAll(map => map.Input.Equals("MouseLeft", StringComparison.OrdinalIgnoreCase)
            || map.Input.StartsWith("MouseLeft+", StringComparison.OrdinalIgnoreCase));
        InputAssignmentPolicy.SanitizeMappings(mappings);
    }

    static (ActionKind Kind, string Value) NormalizeMouseAction(ActionKind kind, string value)
        => kind is ActionKind.Key or ActionKind.Shortcut or ActionKind.Mouse
           && ActionCatalog.TryNormalizeMouseAction(value, out string normalized)
            ? (ActionKind.Mouse, normalized)
            : (kind, value);

    static (ActionKind Kind, string Value) RepairLegacyActionKind(ActionKind kind, string value, int originalVersion)
    {
        if (originalVersion >= ActionKindRepairMigrationVersion
            || kind is not (ActionKind.Key or ActionKind.Shortcut)
            || string.IsNullOrWhiteSpace(value)
            || InputEngine.IsRecognizedShortcut(value))
            return (kind, value);

        string repairedValue = value.Trim();
        return (LooksLikeLaunchTarget(repairedValue) ? ActionKind.Launch : ActionKind.Text, repairedValue);
    }

    static bool LooksLikeLaunchTarget(string value)
    {
        if (Path.IsPathFullyQualified(value))
            return true;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "file")
            return true;
        string extension = Path.GetExtension(value);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".com", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
    }

    internal static AppConfig NormalizeForSave(AppConfig value) => Normalize(value);

    static string CanonicalLayer(string input)
    {
        foreach (string layer in new[] { "Space", "CapsLock", "MouseRight", "MouseForward", "MouseBack", "Taskbar", DeckPanelLayout.Layer })
            if (input.StartsWith(layer + "+", StringComparison.OrdinalIgnoreCase))
                return layer;
        return "通常";
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
