using System.Text.Json.Serialization;

namespace RELYR;

public enum ActionKind { None, Disabled, Key, Shortcut, Text, Launch, Mouse, Macro, Profile }
public enum AppThemeMode { System, Dark, Light }
public enum WindowActionTarget { ActiveWindow, WindowUnderCursor }

public sealed class MacroStep
{
    public string Event { get; set; } = "";
    public int DelayMs { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ActionKind? RecordedActionKind { get; set; }
    public string RecordedActionValue { get; set; } = "";
}

public sealed class MacroDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新しいマクロ";
    public List<MacroStep> Steps { get; set; } = [];
}

public sealed class Mapping
{
    public string Input { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ActionKind Kind { get; set; }
    public string Value { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int LongPressMs { get; set; } = 500;
    public string LongPressValue { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ActionKind LongPressKind { get; set; }
    public string DragValue { get; set; } = "";
    public string DragEndValue { get; set; } = "";
    public string Application { get; set; } = "";
    public string Layer { get; set; } = "通常";
    public string Description { get; set; } = "";
}

public sealed class Profile
{
    public string Name { get; set; } = "標準";
    public List<Mapping> Mappings { get; set; } = [];
    public bool AutoSwitchEnabled { get; set; }
    public List<string> AutoSwitchApplications { get; set; } = [];
}

public sealed class AppConfig
{
    public int Version { get; set; } = 16;
    public string ActiveProfile { get; set; } = "標準";
    public bool AutoSwitchProfilesByCursor { get; set; } = true;
    public bool EngineEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool AutoExtractDesktopArchives { get; set; }
    public string ArchiveWatchFolder { get; set; } = "";
    public string ArchiveDestinationFolder { get; set; } = "";
    public bool ShowDesktopNumberInTray { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public string DismissedUpdateVersion { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WindowActionTarget WindowActionTarget { get; set; } = WindowActionTarget.ActiveWindow;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;
    public long LastUpdateCheckUtcTicks { get; set; }
    public bool RecordKeyboardInputInMacros { get; set; } = true;
    public bool RecordMappedActionsInMacros { get; set; } = true;
    public bool RecordMouseMovementInMacros { get; set; }
    public bool RecordMouseMovementRelativeInMacros { get; set; } = true;
    public bool AutoSave { get; set; }
    public bool CapsLockLayerWarningAccepted { get; set; }
    public bool CapsLockLayerEnabled { get; set; }
    public bool CapsLockRemapPendingRestart { get; set; }
    public bool CapsLockRemapEffectiveBeforeRestart { get; set; }
    public long CapsLockRemapChangedAtUtcTicks { get; set; }
    public string KeyboardLayout { get; set; } = "JIS";
    public bool SpaceHoldRepeatEnabled { get; set; } = true;
    public int SpaceHoldRepeatDelayMs { get; set; } = 400;
    public bool DeleteArchiveAfterExtract { get; set; }
    public bool FirstRunCompleted { get; set; }
    public string EmergencyShortcut { get; set; } = "Ctrl+Alt+Shift+F12";
    public int DoubleClickMs { get; set; } = 350;
    public int MouseDragPixels { get; set; } = 6;
    public List<MacroDefinition> Macros { get; set; } = [];
    public List<Profile> Profiles { get; set; } = [new()];
}
