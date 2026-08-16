using System.Text.Json.Serialization;

namespace RELYR;

public enum ActionKind
{
    None, Disabled, Key, Shortcut, Text, Launch, Mouse, Macro, Profile, Gesture
}
public enum AppThemeMode
{
    System, Dark, Light
}
public enum WindowActionTarget
{
    ActiveWindow, WindowUnderCursor
}
public enum ClockBackgroundMode
{
    FrostedScreen, Image, Solid
}
public enum ClockDisplayMode
{
    Time, TimeWithSeconds, DateAndTime, FullDateAndTime
}

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
    public int LongPressMs { get; set; } = 500;
    public string LongPressValue { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ActionKind LongPressKind { get; set; }
    public string DragValue { get; set; } = "";
    public string DragEndValue { get; set; } = "";
    public string Application { get; set; } = "";
    public string Layer { get; set; } = "通常";
    public string Description { get; set; } = "";
    public string DeckColor { get; set; } = "";
    public string DeckFilePath { get; set; } = "";
    public string DeckIcon { get; set; } = "";
    public string DeckIconPath { get; set; } = "";

    public Mapping Copy() => new()
    {
        Input = Input,
        Kind = Kind,
        Value = Value,
        LongPressMs = LongPressMs,
        LongPressValue = LongPressValue,
        LongPressKind = LongPressKind,
        DragValue = DragValue,
        DragEndValue = DragEndValue,
        Application = Application,
        Layer = Layer,
        Description = Description,
        DeckColor = DeckColor,
        DeckFilePath = DeckFilePath,
        DeckIcon = DeckIcon,
        DeckIconPath = DeckIconPath
    };
}

public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "標準";
    public List<Mapping> Mappings { get; set; } = [];
    public string DefaultDeckLayoutId { get; set; } = "";
    public bool AutoSwitchEnabled { get; set; }
    public List<string> AutoSwitchApplications { get; set; } = [];
}

public sealed class DeckLayoutDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "標準Deck";
    public bool ProfileSwitchEnabled { get; set; }
    public string ProfileGroupId { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public int Columns { get; set; } = 9;
    public int Rows { get; set; } = 5;
    public string PanelColor { get; set; } = "";
    public bool PanelPinned { get; set; }
    public double? PanelWidth { get; set; }
    public double? PanelHeight { get; set; }
    public List<Mapping> Mappings { get; set; } = [];
}

public sealed class GestureDefinition
{
    public string Name { get; set; } = "";
    public ActionKind UpKind { get; set; }
    public string UpValue { get; set; } = "";
    public ActionKind DownKind { get; set; }
    public string DownValue { get; set; } = "";
    public ActionKind LeftKind { get; set; }
    public string LeftValue { get; set; } = "";
    public ActionKind RightKind { get; set; }
    public string RightValue { get; set; } = "";
    public ActionKind CenterKind { get; set; }
    public string CenterValue { get; set; } = "";
}

public sealed class AppConfig
{
    public AppConfig()
    {
        var layout = new DeckLayoutDefinition();
        DeckLayouts = [layout];
        DefaultDeckLayoutId = layout.Id;
        SharedDefaultDeckLayoutId = layout.Id;
        Profiles = [new Profile { DefaultDeckLayoutId = layout.Id }];
    }

    public int Version { get; set; } = ConfigService.CurrentVersion;
    public string ActiveProfile { get; set; } = "標準";
    public bool AutoSwitchProfilesByCursor { get; set; } = true;
    public bool ShowProfileSwitchOverlay { get; set; } = true;
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
    public int GestureThresholdPixels { get; set; } = 12;
    public bool LockCursorDuringGesture { get; set; } = true;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClockBackgroundMode ClockBackgroundMode { get; set; } = ClockBackgroundMode.FrostedScreen;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClockDisplayMode ClockDisplayMode { get; set; } = ClockDisplayMode.DateAndTime;
    public string ClockBackgroundImage { get; set; } = "";
    public string ClockSolidColor { get; set; } = "#101F2E";
    public bool ClockShowOnAllMonitors { get; set; } = true;
    public int InputPanelOpacityPercent { get; set; } = 96;
    public bool DeckHoverPreviewsEnabled { get; set; } = true;
    public bool DeckAutoHideAfterAction { get; set; } = true;
    public bool DeckAutoHideOnPointerLeave { get; set; } = true;
    public bool UseSharedDeckPanel { get; set; }
    public List<Mapping> SharedDeckMappings { get; set; } = [];
    public string DefaultDeckLayoutId { get; set; } = "";
    public string SharedDefaultDeckLayoutId { get; set; } = "";
    public List<DeckLayoutDefinition> DeckLayouts { get; set; } = [];
    public double? DeckPanelLeft { get; set; }
    public double? DeckPanelTop { get; set; }
    public double? DeckPanelWidth { get; set; }
    public double? DeckPanelHeight { get; set; }
    public double? NumpadPanelLeft { get; set; }
    public double? NumpadPanelTop { get; set; }
    public double? ExtendedKeypadPanelLeft { get; set; }
    public double? ExtendedKeypadPanelTop { get; set; }
    public List<MacroDefinition> Macros { get; set; } = [];
    public List<GestureDefinition> Gestures { get; set; } = [];
    public List<Profile> Profiles { get; set; } = [new()];
}
