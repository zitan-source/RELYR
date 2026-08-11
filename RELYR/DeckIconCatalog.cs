using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RELYR;

internal sealed record DeckIconPreset(string Id, string Name, string Glyph);

internal static class DeckIconCatalog
{
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
        new("lock", "ロック", "\uE72E"), new("unlock", "ロック解除", "\uE785"), new("pin", "ピン留め", "\uE718"), new("power", "電源", "\uE7E8"), new("delete", "削除", "\uE74D")
    ];

    internal static FrameworkElement? CreateVisual(Mapping? mapping, double size, bool bindToButtonForeground = true)
    {
        if (!string.IsNullOrWhiteSpace(mapping?.DeckIconPath) && File.Exists(mapping.DeckIconPath))
        {
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
        var preset = Presets.FirstOrDefault(item => item.Id.Equals(mapping?.DeckIcon, StringComparison.OrdinalIgnoreCase));
        if (preset == null)
            return null;
        var glyph = new TextBlock
        {
            Text = preset.Glyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = size,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        if (bindToButtonForeground)
            glyph.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(System.Windows.Controls.Button), 1) });
        else
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        return glyph;
    }

    internal static bool HasIcon(Mapping? mapping) => !string.IsNullOrWhiteSpace(mapping?.DeckIcon) || !string.IsNullOrWhiteSpace(mapping?.DeckIconPath);
    internal static bool IsSupportedCustomIcon(string path) => File.Exists(path) && new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tif", ".tiff" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
