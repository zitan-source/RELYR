namespace RELYR;

public sealed record DeckMonitorDefinition(
    string Id,
    string Name,
    string Description,
    string Glyph,
    string Category,
    DeckMonitorInteraction Interaction = DeckMonitorInteraction.None);

public enum DeckMonitorInteraction
{
    None,
    TaskManager,
    Volume,
    Microphone,
    Brightness,
    WifiSettings,
    BluetoothSettings,
    AutoExtractToggle,
    Timer
}

public static class DeckMonitorCatalog
{
    public const string Category = "モニター";

    public static IReadOnlyList<DeckMonitorDefinition> Items { get; } =
    [
        new("cpu", "CPU", "CPU使用率。クリックでタスクマネージャー", "\uE9D9", Category, DeckMonitorInteraction.TaskManager),
        new("memory", "RAM", "メモリ使用率。クリックでタスクマネージャー", "\uE8F1", Category, DeckMonitorInteraction.TaskManager),
        new("temperature", "CPU TEMP", "CPUパッケージ温度", "\uE706", Category),
        new("gpu-temperature", "GPU TEMP", "GPUコア温度", "\uE706", Category),
        new("gpu", "GPU", "GPU 3Dエンジン使用率。クリックでタスクマネージャー", "\uE7FC", Category, DeckMonitorInteraction.TaskManager),
        new("vram", "VRAM", "GPUメモリ使用量。クリックでタスクマネージャー", "\uE8F1", Category, DeckMonitorInteraction.TaskManager),
        new("fan", "FAN", "取得可能なファン回転数", "\uE72C", Category),
        new("disk", "SSD", "システムドライブ使用率。クリックでタスクマネージャー", "\uE8F1", Category, DeckMonitorInteraction.TaskManager),
        new("disk-read", "READ", "ディスク読み込み速度。クリックでタスクマネージャー", "\uE896", Category, DeckMonitorInteraction.TaskManager),
        new("disk-write", "WRITE", "ディスク書き込み速度。クリックでタスクマネージャー", "\uE898", Category, DeckMonitorInteraction.TaskManager),
        new("network-up", "UPLOAD", "ネットワーク送信速度。クリックでタスクマネージャー", "\uE898", Category, DeckMonitorInteraction.TaskManager),
        new("network-down", "DOWNLOAD", "ネットワーク受信速度。クリックでタスクマネージャー", "\uE896", Category, DeckMonitorInteraction.TaskManager),
        new("network-status", "NETWORK", "ネットワーク接続状態。クリックでタスクマネージャー", "\uE701", Category, DeckMonitorInteraction.TaskManager),
        new("network-latency", "PING", "既定ゲートウェイまでの応答時間。クリックでタスクマネージャー", "\uE823", Category, DeckMonitorInteraction.TaskManager),
        new("virtual-desktop", "DESKTOP", "現在の仮想デスクトップ番号", "\uE7C4", Category),
        new("timer", "TIMER", "残り時間。右クリックで時間を設定", "\uE823", Category, DeckMonitorInteraction.Timer),
        new("clock", "CLOCK", "現在時刻", "\uE823", Category),
        new("date", "DATE", "今日の日付", "\uE787", Category),
        new("uptime", "UPTIME", "Windowsの連続稼働時間", "\uE81C", Category),
        new("battery", "BATTERY", "残量と充電状態", "\uE7E8", Category),
        new("volume", "VOLUME", "現在の音量。クリックまたはホイールで調整", "\uE767", Category, DeckMonitorInteraction.Volume),
        new("microphone", "MIC", "マイク状態。クリックで調整", "\uE720", Category, DeckMonitorInteraction.Microphone),
        new("brightness", "BRIGHTNESS", "画面の明るさ。クリックまたはホイールで調整", "\uE706", Category, DeckMonitorInteraction.Brightness),
        new("wifi", "WI-FI", "Wi-Fi接続状態。クリックで設定", "\uE701", Category, DeckMonitorInteraction.WifiSettings),
        new("bluetooth", "BLUETOOTH", "BluetoothのON／OFF状態。クリックで設定", "\uE702", Category, DeckMonitorInteraction.BluetoothSettings),
        new("auto-extract", "AUTO EXTRACT", "自動解凍の状態。クリックでオン／オフ", "\uE7B8", Category, DeckMonitorInteraction.AutoExtractToggle),
        new("system-status", "STATUS", "CPU・メモリ・バッテリーの状態", "\uE9D9", Category)
    ];

    public static bool TryGet(string? id, out DeckMonitorDefinition definition)
    {
        definition = Items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))!;
        return definition != null;
    }

    public static bool IsMonitor(string? id) => TryGet(id, out _);

    public static string PaletteDescription(string? id) => id?.ToLowerInvariant() switch
    {
        "cpu" => "CPU使用率",
        "memory" => "メモリ使用率",
        "temperature" => "CPU温度",
        "gpu-temperature" => "GPU温度",
        "gpu" => "GPU使用率",
        "vram" => "GPUメモリ使用量",
        "fan" => "ファン回転数",
        "disk" => "システムドライブ使用率",
        "disk-read" => "ディスク読み込み速度",
        "disk-write" => "ディスク書き込み速度",
        "network-up" => "ネットワーク送信速度",
        "network-down" => "ネットワーク受信速度",
        "network-status" => "ネットワーク接続状態",
        "network-latency" => "ネットワーク応答時間",
        "virtual-desktop" => "現在の仮想デスクトップ",
        "timer" => "タイマーの残り時間",
        "clock" => "現在時刻",
        "date" => "今日の日付",
        "uptime" => "Windowsの連続稼働時間",
        "battery" => "バッテリー残量",
        "volume" => "現在の音量",
        "microphone" => "マイクの状態",
        "brightness" => "画面の明るさ",
        "wifi" => "Wi-Fi接続状態",
        "bluetooth" => "Bluetooth接続状態",
        "auto-extract" => "自動解凍の状態",
        "system-status" => "システム全体の状態",
        _ => "モニター"
    };
}
