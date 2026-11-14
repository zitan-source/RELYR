import Foundation

extension StudioAction {
    static let browsingCategories = ["文字・編集", "ウィンドウ・デスクトップ", "マウス・キー", "音量・画面", "アプリ・ファイル", "ブラウザー", "自動操作", "RELYRの機能"]
    var browsingCategory: String {
        if deckMonitor != nil { return "RELYRの機能" }
        switch kind {
        case .deck,.profile: return "RELYRの機能"
        case .macro,.gesture: return "自動操作"
        case .launch,.file,.url: return "アプリ・ファイル"
        case .text: return "文字・編集"
        case .mouse,.key,.disabled: return "マウス・キー"
        case .window: return "ウィンドウ・デスクトップ"
        default: break
        }
        if kind == .system, let command = SystemCommand(rawValue:value) {
            switch command {
            case .showRelyr,.numpad,.extendedKeypad: return "RELYRの機能"
            case .toggleAutoExtract: return "アプリ・ファイル"
            case .brightnessUp,.brightnessDown,.lockScreen: return "音量・画面"
            case .wifiOn,.wifiOff,.wifiToggle: return "アプリ・ファイル"
            default: break
            }
        }
        switch category {
        case "音量","メディア","スクリーンショット": return "音量・画面"
        case "デスクトップ","ウィンドウ": return "ウィンドウ・デスクトップ"
        case "マウス","キー","アクセシビリティ": return "マウス・キー"
        case "Finder","Mac標準アプリ","アプリ・URL": return "アプリ・ファイル"
        case "ブラウザー": return "ブラウザー"
        case "プロファイル","Deckパネル","Deck": return "RELYRの機能"
        case "マクロ","ジェスチャー": return "自動操作"
        case "Mac基本操作": return "ウィンドウ・デスクトップ"
        default:
            if name.contains("ウィンドウ") || name.contains("アプリを終了") { return "ウィンドウ・デスクトップ" }
            if name.contains("タブ") { return "ブラウザー" }
            return kind == .shortcut && category == "カスタム" ? "マウス・キー" : "文字・編集"
        }
    }
    func matchesSearch(_ query: String) -> Bool {
        var aliases = ""
        if kind == .system && ["scrollUp","scrollDown","scrollLeft","scrollRight"].contains(value) {
            aliases += " スクロール ホイール scroll wheel"
            if modifiers.contains(.control) { aliases += " Control Ctrl コントロール ズーム 拡大 縮小 Office オフィス" }
            if modifiers.contains(.command) { aliases += " Command コマンド" }
            if modifiers.contains(.shift) { aliases += " Shift シフト" }
            if modifiers.contains(.option) { aliases += " Option Alt オプション" }
        }
        if category == "スクリーンショット" { aliases += " スクショ 画面撮影 キャプチャ screenshot" }
        if value == SystemCommand.screenshotAreaClipboard.rawValue { aliases += " 選択範囲をクリップボードへ 範囲指定 コピー 貼り付け 画像" }
        if category == "音量" { aliases += " ボリューム volume" }
        if name.contains("輝度") { aliases += " 明るさ brightness" }
        if kind == .deck { aliases += " デック パネル deck" }
        if value == SystemCommand.clipboardHistory.rawValue { aliases += " コピー履歴 clipboard" }
        if kind == .shortcut && modifiers == .command {
            if output == 13 { aliases += " Command W コマンドW 閉じる" }
            if output == 12 { aliases += " Command Q コマンドQ 終了" }
        }
        let haystack = name + " " + category + " " + browsingCategory + " " + detail + aliases
        return query.split(whereSeparator: { $0.isWhitespace }).allSatisfy { haystack.localizedCaseInsensitiveContains(String($0)) }
    }
}
