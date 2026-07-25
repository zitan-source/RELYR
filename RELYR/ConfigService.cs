using System.Text.Json;
using System.IO;

namespace RELYR;

public sealed class ConfigService
{
    static readonly object migrationLock=new();
    readonly JsonSerializerOptions options = new() { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");
    internal static string DefaultDirectoryPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"RELYR");
    internal static string LegacyDirectoryPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"InputCustomizer");
    public ConfigService(string? directoryPath=null)
    {
        DirectoryPath=directoryPath??Environment.GetEnvironmentVariable("RELYR_CONFIG_DIR")??DefaultDirectoryPath;
        if(directoryPath==null&&Environment.GetEnvironmentVariable("RELYR_CONFIG_DIR")==null)MigrateLegacyDirectory(LegacyDirectoryPath,DirectoryPath);
    }

    internal static bool MigrateLegacyDirectory(string legacyDirectory,string destinationDirectory)
    {
        lock(migrationLock)
        {
            if(Path.GetFullPath(legacyDirectory).Equals(Path.GetFullPath(destinationDirectory),StringComparison.OrdinalIgnoreCase)||!Directory.Exists(legacyDirectory))return false;
            if(!Directory.Exists(destinationDirectory)){Directory.Move(legacyDirectory,destinationDirectory);return true;}
            string backupRoot=Path.Combine(destinationDirectory,"migration-backup");
            foreach(string source in Directory.EnumerateFiles(legacyDirectory,"*",SearchOption.AllDirectories))
            {
                string relative=Path.GetRelativePath(legacyDirectory,source);string destination=Path.Combine(destinationDirectory,relative);
                if(File.Exists(destination))destination=UniqueMigrationPath(Path.Combine(backupRoot,relative));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(source,destination,false);
            }
            Directory.Delete(legacyDirectory,true);return true;
        }
    }

    static string UniqueMigrationPath(string path)
    {
        if(!File.Exists(path))return path;
        string directory=Path.GetDirectoryName(path)!,name=Path.GetFileNameWithoutExtension(path),extension=Path.GetExtension(path);
        for(int index=2;;index++){string candidate=Path.Combine(directory,$"{name}-{index}{extension}");if(!File.Exists(candidate))return candidate;}
    }

    internal static bool DeleteAllUserData()=>DeleteUserDataDirectories(DefaultDirectoryPath,LegacyDirectoryPath);
    internal static bool DeleteUserDataDirectories(params string[] directories)
    {
        bool deleted=true;
        foreach(string directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try{if(Directory.Exists(directory))Directory.Delete(directory,true);}
            catch{deleted=false;}
        }
        return deleted;
    }

    public AppConfig Load()
    {
        try
        {
            if(!File.Exists(FilePath))return CreateDefault();
            var loaded=JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath),options)??new();
            int storedVersion=loaded.Version;
            var normalized=Normalize(loaded);
            if(normalized.Version!=storedVersion)Save(normalized);
            return normalized;
        }
        catch
        {
            try{if(File.Exists(FilePath))File.Copy(FilePath,Path.Combine(DirectoryPath,$"settings-corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json"),true);}catch{}
            return CreateDefault();
        }
    }

    public void Save(AppConfig value)
    {
        Directory.CreateDirectory(DirectoryPath);
        if (File.Exists(FilePath))
        {
            var backup = Path.Combine(DirectoryPath, $"settings-{DateTime.Now:yyyyMMdd-HHmmss-fffffff}.bak.json");
            File.Copy(FilePath, backup, true);
            foreach (var old in new DirectoryInfo(DirectoryPath).GetFiles("*.bak.json").OrderByDescending(x => x.CreationTimeUtc).Skip(20)) old.Delete();
        }
        var temp=FilePath+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(value,options));
        File.Move(temp,FilePath,true);
    }

    public void Export(AppConfig value, string path) => File.WriteAllText(path, JsonSerializer.Serialize(value, options));
    public AppConfig Clone(AppConfig value)=>JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(value,options),options)??throw new InvalidOperationException("設定を複製できません。");
    public AppConfig Import(string path)
    {
        var result=JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path),options)??throw new InvalidDataException("設定ファイルが空です。");
        if(result.Version>16)throw new InvalidDataException("この設定は新しいバージョンで作成されています。");
        if(result.Profiles is null||result.Profiles.Count==0)throw new InvalidDataException("有効なプロファイルがありません。");
        result=Normalize(result);result.CapsLockRemapPendingRestart=false;result.CapsLockRemapEffectiveBeforeRestart=false;result.CapsLockRemapChangedAtUtcTicks=0;
        var errors=ConfigValidator.Validate(result);
        if(errors.Count>0)throw new InvalidDataException("設定ファイルに問題があります。\n"+string.Join("\n",errors));
        return result;
    }

    static AppConfig Normalize(AppConfig value)
    {
        int originalVersion=value.Version;
        value.Profiles??=[];
        value.Macros??=[];
        value.ArchiveWatchFolder??="";
        value.ArchiveDestinationFolder??="";
        var macroIds=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var macro in value.Macros)
        {
            macro.Steps??=[];
            foreach(var step in macro.Steps)step.RecordedActionValue??="";
            if(string.IsNullOrWhiteSpace(macro.Id)||!macroIds.Add(macro.Id))
            {
                do{macro.Id=Guid.NewGuid().ToString("N");}while(!macroIds.Add(macro.Id));
            }
        }
        // v7以前ではWindows再起動を異常終了と誤認し、全レイヤー停止が保存されることがあったため一度だけ復旧する。
        if(originalVersion<8)value.EngineEnabled=true;
        if(value.Profiles.Count==0)value.Profiles.Add(new Profile());
        foreach(var profile in value.Profiles){profile.Name=string.IsNullOrWhiteSpace(profile.Name)?"名称未設定":profile.Name;profile.Mappings??=[];profile.AutoSwitchApplications??=[];foreach(var map in profile.Mappings){if(map.Input.Equals("F13",StringComparison.OrdinalIgnoreCase))map.Input="CapsLock";else if(map.Input.StartsWith("F13+",StringComparison.OrdinalIgnoreCase))map.Input="CapsLock"+map.Input[3..];if(map.Layer.Equals("F13",StringComparison.OrdinalIgnoreCase))map.Layer="CapsLock";if(originalVersion<9&&map.LongPressKind==ActionKind.None)map.LongPressValue="";if(map.Kind is ActionKind.Key or ActionKind.Shortcut or ActionKind.Mouse&&ActionCatalog.TryNormalizeMouseAction(map.Value,out string mouseValue)){map.Kind=ActionKind.Mouse;map.Value=mouseValue;}if(map.LongPressKind is ActionKind.Key or ActionKind.Shortcut or ActionKind.Mouse&&ActionCatalog.TryNormalizeMouseAction(map.LongPressValue,out string longMouseValue)){map.LongPressKind=ActionKind.Mouse;map.LongPressValue=longMouseValue;}}}
        if(!value.Profiles.Any(x=>x.Name==value.ActiveProfile))value.ActiveProfile=value.Profiles[0].Name;
        if(!Enum.IsDefined(value.ThemeMode))value.ThemeMode=AppThemeMode.System;
        value.Version=16;
        return value;
    }

    public AppConfig ResetToDefaults()
    {
        var value=CreateDefault();
        value.FirstRunCompleted=true;
        Save(value);
        return value;
    }

    internal static AppConfig CreateDefault()=>Normalize(new(){Profiles=[new Profile{Name="標準"}]});
}
