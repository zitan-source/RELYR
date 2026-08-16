using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace RELYR;

internal sealed record DeckIconPreset(string Id, string Name, string Glyph);

internal static class DeckIconCatalog
{
    internal const string AnimatedPrefix = "animated:";
    internal static IReadOnlyList<DeckIconPreset> Presets { get; } =
    [
        new("home", "ホーム", "\uE80F"), new("search", "検索", "\uE721"), new("settings", "設定", "\uE713"), new("favorite", "お気に入り", "\uE734"), new("star", "スター", "\uE735"),
        new("play", "再生", "\uE768"), new("pause", "一時停止", "\uE769"), new("stop", "停止", "\uE71A"), new("record", "録画", "\uE7C8"), new("music", "音楽", "\uE8D6"),
        new("volume", "音量", "\uE767"), new("mute", "ミュート", "\uE74F"), new("camera", "カメラ", "\uE722"), new("video", "ビデオ", "\uE714"), new("picture", "画像", "\uEB9F"),
        new("folder", "フォルダー", "\uE8B7"), new("document", "ドキュメント", "\uE8A5"), new("copy", "コピー", "\uE8C8"), new("paste", "貼り付け", "\uE77F"), new("save", "保存", "\uE74E"),
        new("download", "ダウンロード", "\uE896"), new("upload", "アップロード", "\uE898"), new("cloud", "クラウド", "\uE753"), new("mail", "メール", "\uE715"), new("send", "送信", "\uE724"),
        new("link", "リンク", "\uE71B"), new("web", "Web", "\uE774"), new("refresh", "更新", "\uE72C"), new("undo", "元に戻す", "\uE7A7"), new("redo", "やり直す", "\uE7A6"),
        new("back", "戻る", "\uE72B"), new("forward", "進む", "\uE72A"), new("up", "上", "\uE74A"), new("down", "下", "\uE74B"), new("expand", "拡大", "\uE740"),
        new("keyboard", "キーボード", "\uE765"), new("mouse", "マウス", "\uE962"), new("desktop", "デスクトップ", "\uE7F4"), new("window", "ウィンドウ", "\uE737"), new("apps", "アプリ", "\uE71D"),
        new("person", "ユーザー", "\uE77B"), new("people", "ユーザー一覧", "\uE716"), new("phone", "電話", "\uE717"), new("calendar", "カレンダー", "\uE787"), new("clock", "時計", "\uE823"),
        new("lock", "ロック", "\uE72E"), new("unlock", "ロック解除", "\uE785"), new("pin", "ピン留め", "\uE718"), new("power", "電源", "\uE7E8"), new("delete", "削除", "\uE74D"),
        new("edit", "編集", "\uE70F"), new("add", "追加", "\uE710"), new("remove", "取り除く", "\uE738"), new("print", "印刷", "\uE749"), new("help", "ヘルプ", "\uE897"),
        new("info", "情報", "\uE946"), new("warning", "警告", "\uE7BA"), new("sync", "同期", "\uE895"), new("filter", "絞り込み", "\uE71C"), new("zoom-in", "拡大表示", "\uE8A3"),
        new("zoom-out", "縮小表示", "\uE71F"), new("full-screen", "全画面", "\uE740"), new("share", "共有", "\uE72D"), new("open-folder", "フォルダーを開く", "\uE838"), new("new-window", "新しいウィンドウ", "\uE78B"),
        new("switch", "切り替え", "\uE8AB"), new("list", "一覧", "\uE8FD"), new("grid", "グリッド", "\uE80A"), new("menu", "メニュー", "\uE700"), new("check", "完了", "\uE73E"),
        new("cancel", "キャンセル", "\uE711"), new("cut", "切り取り", "\uE8C6"), new("tag", "タグ", "\uE8EC"), new("location", "場所", "\uE81D"), new("map", "地図", "\uE707"),
        new("wifi", "Wi-Fi", "\uE701"), new("bluetooth", "Bluetooth", "\uE702"), new("brightness", "明るさ", "\uE706"), new("night", "夜間モード", "\uE708"), new("calculator", "電卓", "\uE8EF"),
        new("microphone", "マイク", "\uE720"), new("headphones", "ヘッドホン", "\uE7F6"), new("game", "ゲーム", "\uE7FC"), new("lightbulb", "ヒント", "\uEA80"), new("bookmark", "ブックマーク", "\uE8A4"),
        new("archive", "アーカイブ", "\uE7B8"), new("shield", "セキュリティ", "\uE83D"), new("key", "キー", "\uE8D7"), new("sort", "並べ替え", "\uE8CB"), new("view", "表示", "\uE890"),
        new("hide", "非表示", "\uED1A"), new("chat", "チャット", "\uE8BD"), new("comment", "コメント", "\uE90A"), new("notification", "通知", "\uE7ED"), new("code", "コード", "\uE943"),
        new("terminal", "ターミナル", "\uE756"), new("database", "データベース", "\uE8F1"), new("globe", "インターネット", "\uE774"), new("rocket", "起動", "\uE945"), new("gift", "ギフト", "\uEA39"),
        new("app-edge", "Microsoft Edge", "\uE774"), new("app-chrome", "Google Chrome", "\uE774"), new("app-firefox", "Mozilla Firefox", "\uE774"), new("app-opera", "Opera", "\uE774"), new("app-brave", "Brave", "\uE83D"),
        new("app-vivaldi", "Vivaldi", "\uE774"), new("app-word", "Microsoft Word", "\uE8A5"), new("app-excel", "Microsoft Excel", "\uE9F9"), new("app-powerpoint", "Microsoft PowerPoint", "\uE8A5"), new("app-outlook", "Microsoft Outlook", "\uE715"),
        new("app-onenote", "Microsoft OneNote", "\uE70B"), new("app-teams", "Microsoft Teams", "\uE716"), new("app-notepad", "メモ帳", "\uE70B"), new("app-paint", "ペイント", "\uE790"), new("app-file-explorer", "エクスプローラー", "\uE8B7"),
        new("app-powershell", "PowerShell", "\uE756"), new("app-windows-terminal", "Windows Terminal", "\uE756"), new("app-microsoft-store", "Microsoft Store", "\uE719"), new("app-photos", "フォト", "\uEB9F"), new("app-media-player", "メディア プレーヤー", "\uE768"),
        new("app-clipchamp", "Clipchamp", "\uE714"), new("app-acrobat", "Adobe Acrobat", "\uE8A5"), new("app-photoshop", "Adobe Photoshop", "\uEB9F"), new("app-illustrator", "Adobe Illustrator", "\uE790"), new("app-premiere", "Adobe Premiere Pro", "\uE714"),
        new("app-after-effects", "Adobe After Effects", "\uE7F4"), new("app-lightroom", "Adobe Lightroom", "\uEB9F"), new("app-audition", "Adobe Audition", "\uE8D6"), new("app-vlc", "VLC media player", "\uE768"), new("app-obs", "OBS Studio", "\uE7C8"),
        new("app-spotify", "Spotify", "\uE8D6"), new("app-discord", "Discord", "\uE8BD"), new("app-slack", "Slack", "\uE716"), new("app-zoom", "Zoom", "\uE714"), new("app-skype", "Skype", "\uE717"),
        new("app-line", "LINE", "\uE8BD"), new("app-whatsapp", "WhatsApp", "\uE717"), new("app-telegram", "Telegram", "\uE724"), new("app-steam", "Steam", "\uE7FC"), new("app-epic-games", "Epic Games", "\uE7FC"),
        new("app-xbox", "Xbox", "\uE7FC"), new("app-battlenet", "Battle.net", "\uE7FC"), new("app-minecraft", "Minecraft", "\uE7FC"), new("app-roblox", "Roblox", "\uE7FC"), new("app-vscode", "Visual Studio Code", "\uE943"),
        new("app-visual-studio", "Visual Studio", "\uE943"), new("app-github", "GitHub", "\uE943"), new("app-git", "Git", "\uE8AB"), new("app-gitlab", "GitLab", "\uE943"), new("app-docker", "Docker Desktop", "\uE7B8"),
        new("app-kubernetes", "Kubernetes", "\uE713"), new("app-postman", "Postman", "\uE724"), new("app-figma", "Figma", "\uE790"), new("app-blender", "Blender", "\uEB9F"), new("app-unity", "Unity", "\uE7FC"),
        new("app-unreal-engine", "Unreal Engine", "\uE7FC"), new("app-seven-zip", "7-Zip", "\uE7B8"), new("app-winrar", "WinRAR", "\uE7B8"), new("app-everything", "Everything", "\uE721"), new("app-powertoys", "Microsoft PowerToys", "\uE945"),
        new("app-autohotkey", "AutoHotkey", "\uE8D7"), new("app-dropbox", "Dropbox", "\uE753"), new("app-onedrive", "OneDrive", "\uE753"), new("app-google-drive", "Google Drive", "\uE838"), new("app-notion", "Notion", "\uE8A5"),
        new("app-trello", "Trello", "\uE80A"), new("app-asana", "Asana", "\uE73E"), new("app-evernote", "Evernote", "\uE70B"), new("app-todoist", "Todoist", "\uE73E"), new("app-davinci-resolve", "DaVinci Resolve", "\uE714"),
        new("app-filmora", "Filmora", "\uE714"), new("app-capcut", "CapCut", "\uE8C6"), new("app-handbrake", "HandBrake", "\uE714"), new("app-audacity", "Audacity", "\uE720"), new("app-thunderbird", "Thunderbird", "\uE715"),
        new("app-access", "Microsoft Access", "\uE8F1"), new("app-publisher", "Microsoft Publisher", "\uE8A5"), new("app-project", "Microsoft Project", "\uE787"), new("app-visio", "Microsoft Visio", "\uE8FD"), new("app-windows-security", "Windows セキュリティ", "\uE83D"),
        new("app-task-manager", "タスク マネージャー", "\uE9D9"), new("app-remote-desktop", "リモート デスクトップ", "\uE7F4"), new("app-snipping-tool", "Snipping Tool", "\uE8C6"), new("app-windows-camera", "Windows カメラ", "\uE722"), new("app-windows-maps", "Windows マップ", "\uE707"),
        new("app-windows-weather", "Windows 天気", "\uE706"), new("app-windows-clock", "Windows クロック", "\uE823"), new("app-chatgpt", "ChatGPT", "\uE8BD"), new("app-copilot", "Microsoft Copilot", "\uEA80"), new("app-gimp", "GIMP", "\uEB9F"),
        new("app-inkscape", "Inkscape", "\uE790"), new("app-krita", "Krita", "\uE790"), new("app-virtualbox", "VirtualBox", "\uE7F4"), new("app-vmware", "VMware Workstation", "\uE7F4"), new("app-teamspeak", "TeamSpeak", "\uE720"),
        new("app-foobar2000", "foobar2000", "\uE8D6"), new("app-putty", "PuTTY", "\uE756"), new("app-winscp", "WinSCP", "\uE898"), new("app-wireshark", "Wireshark", "\uE774"), new("app-unity-hub", "Unity Hub", "\uE71D")
    ];

    internal static FrameworkElement? CreateVisual(Mapping? mapping, double size, bool bindToButtonForeground = true)
    {
        if (!string.IsNullOrWhiteSpace(mapping?.DeckIconPath) && File.Exists(mapping.DeckIconPath))
        {
            if (Path.GetExtension(mapping.DeckIconPath).Equals(".gif", StringComparison.OrdinalIgnoreCase))
                return new AnimatedGifIcon(mapping.DeckIconPath, size);
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(Path.GetFullPath(mapping.DeckIconPath), UriKind.Absolute);
                image.DecodePixelWidth = Math.Max(32, (int)Math.Ceiling(size * 2));
                image.EndInit();
                image.Freeze();
                return new System.Windows.Controls.Image { Source = image, Width = size, Height = size, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            }
            catch { }
        }
        string presetId = mapping?.DeckIcon ?? "";
        bool animated = presetId.StartsWith(AnimatedPrefix, StringComparison.OrdinalIgnoreCase);
        if (animated)
            presetId = presetId[AnimatedPrefix.Length..];
        var preset = Presets.FirstOrDefault(item => item.Id.Equals(presetId, StringComparison.OrdinalIgnoreCase));
        if (preset == null)
            return null;
        bool softwarePreset = preset.Id.StartsWith("app-", StringComparison.OrdinalIgnoreCase);
        string displayGlyph = softwarePreset ? SoftwareMark(preset.Id) : preset.Glyph;
        var glyph = new TextBlock
        {
            Text = displayGlyph,
            FontFamily = new System.Windows.Media.FontFamily(softwarePreset ? "Segoe UI Variable, Segoe UI" : "Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = softwarePreset ? size * (displayGlyph.Length > 2 ? .46 : .62) : size,
            FontWeight = softwarePreset ? FontWeights.SemiBold : FontWeights.Normal,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        if (bindToButtonForeground)
            glyph.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(System.Windows.Controls.Button), 1) });
        else
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        if (!animated)
            return glyph;
        ApplyPresetAnimation(glyph, preset.Id);
        return glyph;
    }

    static string SoftwareMark(string presetId)
    {
        string id = presetId[4..];
        return id switch
        {
            "edge" => "E", "chrome" => "C", "firefox" => "Fx", "opera" => "O", "brave" => "B", "vivaldi" => "V",
            "word" => "W", "excel" => "X", "powerpoint" => "P", "outlook" => "O", "onenote" => "N", "teams" => "T",
            "notepad" => "N", "paint" => "P", "file-explorer" => "FE", "powershell" => ">_", "windows-terminal" => "WT", "microsoft-store" => "MS",
            "photos" => "Ph", "media-player" => "MP", "clipchamp" => "Cc", "acrobat" => "Ac", "photoshop" => "Ps", "illustrator" => "Ai",
            "premiere" => "Pr", "after-effects" => "Ae", "lightroom" => "Lr", "audition" => "Au", "vlc" => "VLC", "obs" => "OBS",
            "spotify" => "Sp", "discord" => "Dc", "slack" => "Sl", "zoom" => "Z", "skype" => "S", "line" => "L",
            "whatsapp" => "WA", "telegram" => "Tg", "steam" => "St", "epic-games" => "EG", "xbox" => "X", "battlenet" => "BN",
            "minecraft" => "MC", "roblox" => "R", "vscode" => "</>", "visual-studio" => "VS", "github" => "GH", "git" => "Git",
            "gitlab" => "GL", "docker" => "D", "kubernetes" => "K8", "postman" => "Pm", "figma" => "F", "blender" => "B",
            "unity" => "U", "unreal-engine" => "UE", "seven-zip" => "7z", "winrar" => "RAR", "everything" => "Ev", "powertoys" => "PT",
            "autohotkey" => "AHK", "dropbox" => "Db", "onedrive" => "1D", "google-drive" => "GD", "notion" => "N", "trello" => "Tr",
            "asana" => "As", "evernote" => "En", "todoist" => "Td", "davinci-resolve" => "DR", "filmora" => "Fl", "capcut" => "Cc",
            "handbrake" => "HB", "audacity" => "Ad", "thunderbird" => "Tb", "access" => "A", "publisher" => "Pb", "project" => "Pj",
            "visio" => "V", "windows-security" => "WS", "task-manager" => "TM", "remote-desktop" => "RD", "snipping-tool" => "ST", "windows-camera" => "Cam",
            "windows-maps" => "Map", "windows-weather" => "Wx", "windows-clock" => "Clk", "chatgpt" => "GPT", "copilot" => "Co", "gimp" => "Gi",
            "inkscape" => "In", "krita" => "Kr", "virtualbox" => "VB", "vmware" => "VM", "teamspeak" => "TS", "foobar2000" => "Fb",
            "putty" => "Pu", "winscp" => "SCP", "wireshark" => "Wi", "unity-hub" => "UH",
            _ => string.Concat(id.Split('-', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])))
        };
    }

    internal static string AnimatedId(string presetId) => AnimatedPrefix + presetId;
    internal static bool IsAnimatedPreset(string? id) => !string.IsNullOrWhiteSpace(id) && id.StartsWith(AnimatedPrefix, StringComparison.OrdinalIgnoreCase);

    static void ApplyPresetAnimation(TextBlock glyph, string presetId)
    {
        glyph.RenderTransformOrigin = new System.Windows.Point(.5, .5);
        var scale = new ScaleTransform(1, 1);
        var rotate = new RotateTransform();
        var translate = new TranslateTransform();
        glyph.RenderTransform = new TransformGroup { Children = new TransformCollection { scale, rotate, translate } };
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

        if (presetId == "cut")
        {
            var snip = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(VariedDuration(presetId, 1.35)),
                RepeatBehavior = RepeatBehavior.Forever
            };
            snip.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0), ease));
            snip.KeyFrames.Add(new EasingDoubleKeyFrame(.72, KeyTime.FromPercent(.10), ease));
            snip.KeyFrames.Add(new EasingDoubleKeyFrame(1.08, KeyTime.FromPercent(.20), ease));
            snip.KeyFrames.Add(new EasingDoubleKeyFrame(.72, KeyTime.FromPercent(.30), ease));
            snip.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(.42), ease));
            snip.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(1)));
            var angle = new DoubleAnimationUsingKeyFrames
            {
                Duration = snip.Duration,
                RepeatBehavior = RepeatBehavior.Forever
            };
            angle.KeyFrames.Add(new EasingDoubleKeyFrame(-4, KeyTime.FromPercent(0), ease));
            angle.KeyFrames.Add(new EasingDoubleKeyFrame(5, KeyTime.FromPercent(.10), ease));
            angle.KeyFrames.Add(new EasingDoubleKeyFrame(-3, KeyTime.FromPercent(.20), ease));
            angle.KeyFrames.Add(new EasingDoubleKeyFrame(4, KeyTime.FromPercent(.30), ease));
            angle.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(.42), ease));
            angle.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            Timeline.SetDesiredFrameRate(snip, VariedFrameRate(presetId, 22));
            Timeline.SetDesiredFrameRate(angle, VariedFrameRate(presetId, 22));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, snip);
            rotate.BeginAnimation(RotateTransform.AngleProperty, angle);
            return;
        }
        if (new[] { "settings", "refresh", "sync" }.Contains(presetId))
        {
            var animation = Loop(0, 360, VariedDuration(presetId, 1.5), false, ease);
            Timeline.SetDesiredFrameRate(animation, VariedFrameRate(presetId, 20));
            rotate.BeginAnimation(RotateTransform.AngleProperty, animation);
            return;
        }
        if (new[] { "notification", "warning", "microphone", "pin", "key", "mute", "game" }.Contains(presetId))
        {
            var animation = Loop(-9, 9, VariedDuration(presetId, .34), true, ease);
            Timeline.SetDesiredFrameRate(animation, VariedFrameRate(presetId, 15));
            rotate.BeginAnimation(RotateTransform.AngleProperty, animation);
            return;
        }
        if (new[] { "back", "forward", "up", "down", "download", "upload", "send", "rocket", "switch", "undo", "redo" }.Contains(presetId))
        {
            bool vertical = presetId is "up" or "down" or "download" or "upload" or "rocket";
            var animation = Loop(-2.5, 2.5, VariedDuration(presetId, .42), true, ease);
            Timeline.SetDesiredFrameRate(animation, VariedFrameRate(presetId, 15));
            if (vertical)
                translate.BeginAnimation(TranslateTransform.YProperty, animation);
            else
                translate.BeginAnimation(TranslateTransform.XProperty, animation);
            return;
        }
        if (new[] { "search", "zoom-in", "zoom-out", "expand", "full-screen", "view", "camera", "picture", "video", "favorite", "star", "lightbulb", "brightness", "volume", "record" }.Contains(presetId))
        {
            var animation = Loop(.88, 1.12, VariedDuration(presetId, .52), true, ease);
            Timeline.SetDesiredFrameRate(animation, VariedFrameRate(presetId, 18));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
            return;
        }
        if (new[] { "hide", "night", "wifi", "bluetooth" }.Contains(presetId))
        {
            var animation = Loop(.45, 1, VariedDuration(presetId, .62), true, ease);
            Timeline.SetDesiredFrameRate(animation, VariedFrameRate(presetId, 12));
            glyph.BeginAnimation(UIElement.OpacityProperty, animation);
            return;
        }
        if (new[] { "play", "forward", "send", "mail", "share", "new-window" }.Contains(presetId))
        {
            var animation = Loop(-1.2, 2.8, VariedDuration(presetId, .5), true, ease);
            Timeline.SetDesiredFrameRate(animation, VariedFrameRate(presetId, 18));
            translate.BeginAnimation(TranslateTransform.XProperty, animation);
            return;
        }
        if (new[] { "pause", "stop", "save", "copy", "paste", "calculator", "keyboard", "mouse", "apps", "grid", "menu" }.Contains(presetId))
        {
            var press = Loop(1, .84, VariedDuration(presetId, .36), true, ease);
            Timeline.SetDesiredFrameRate(press, VariedFrameRate(presetId, 17));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, press);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, press);
            return;
        }
        if (new[] { "delete", "remove", "cancel" }.Contains(presetId))
        {
            var shake = Loop(-2.2, 2.2, VariedDuration(presetId, .18), true, ease);
            Timeline.SetDesiredFrameRate(shake, VariedFrameRate(presetId, 20));
            translate.BeginAnimation(TranslateTransform.XProperty, shake);
            return;
        }
        if (new[] { "music", "headphones", "chat", "comment", "gift" }.Contains(presetId))
        {
            var bob = Loop(0, -2.8, VariedDuration(presetId, .38), true, ease);
            var rock = Loop(-4, 4, VariedDuration(presetId, .38), true, ease);
            Timeline.SetDesiredFrameRate(bob, VariedFrameRate(presetId, 18));
            Timeline.SetDesiredFrameRate(rock, VariedFrameRate(presetId, 18));
            translate.BeginAnimation(TranslateTransform.YProperty, bob);
            rotate.BeginAnimation(RotateTransform.AngleProperty, rock);
            return;
        }
        if (new[] { "lock", "unlock", "folder", "open-folder", "archive", "document", "print" }.Contains(presetId))
        {
            var open = Loop(1, .78, VariedDuration(presetId, .46), true, ease);
            Timeline.SetDesiredFrameRate(open, VariedFrameRate(presetId, 16));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, open);
            return;
        }
        if (new[] { "clock", "power" }.Contains(presetId))
        {
            var tick = Loop(-12, 12, VariedDuration(presetId, .72), true, ease);
            Timeline.SetDesiredFrameRate(tick, VariedFrameRate(presetId, 14));
            rotate.BeginAnimation(RotateTransform.AngleProperty, tick);
            return;
        }

        var bounce = Loop(0, -2.2, VariedDuration(presetId, .48), true, ease);
        Timeline.SetDesiredFrameRate(bounce, VariedFrameRate(presetId, 15));
        translate.BeginAnimation(TranslateTransform.YProperty, bounce);
    }

    static int StableVariation(string value) => value.Aggregate(17, (hash, character) => unchecked(hash * 31 + character)) & int.MaxValue;
    static double VariedDuration(string id, double duration) => duration * (.88 + StableVariation(id) % 25 / 100d);
    static int VariedFrameRate(string id, int center)
    {
        int[] offsets = [-3, 0, 2, 4];
        return Math.Clamp(center + offsets[StableVariation(id) % offsets.Length], 9, 24);
    }

    static DoubleAnimation Loop(double from, double to, double seconds, bool reverse, IEasingFunction? easing = null) => new(from, to, TimeSpan.FromSeconds(seconds))
    {
        AutoReverse = reverse,
        RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = easing
    };

    internal static bool HasIcon(Mapping? mapping) => !string.IsNullOrWhiteSpace(mapping?.DeckIcon) || !string.IsNullOrWhiteSpace(mapping?.DeckIconPath);
    internal static bool IsSupportedCustomIcon(string path) => File.Exists(path) && new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tif", ".tiff" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
