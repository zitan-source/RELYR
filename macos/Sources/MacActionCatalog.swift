import Foundation

extension StudioAction {
    /// Stable IDs: inserting rows must not retarget saved favorites or recent actions.
    static let macParityActions: [StudioAction] = {
        var result: [StudioAction] = []
        func key(_ id: String, _ name: String, _ category: String, _ code: UInt16, _ flags: Modifiers = .command) {
            result.append(StudioAction(id:"parity-" + id,name:name,category:category,symbol:"keyboard",kind:flags.isEmpty ? .key : .shortcut,output:code,modifiers:flags))
        }
        key("open","ファイルを開く","文書・編集",31)
        key("save-as","名前を付けて保存（対応アプリ）","文書・編集",1,[.command,.shift,.option])
        key("duplicate-document","文書を複製（対応アプリ）","文書・編集",1,[.command,.shift])
        key("bold","太字","文書・編集",11)
        key("italic","斜体","文書・編集",34)
        key("underline","下線","文書・編集",32)
        key("paste-style","ペーストしてスタイルを合わせる","文書・編集",9,[.command,.option,.shift])
        key("find-next","次を検索","文書・編集",5)
        key("find-previous","前を検索","文書・編集",5,[.command,.shift])
        key("line-start","行頭へ","文書・編集",123)
        key("line-end","行末へ","文書・編集",124)
        key("document-start","文頭へ","文書・編集",126)
        key("document-end","文末へ","文書・編集",125)
        key("word-left","前の単語へ","文書・編集",123,.option)
        key("word-right","次の単語へ","文書・編集",124,.option)
        key("delete-word","前の単語を削除","文書・編集",51,.option)
        key("kana","日本語入力にする（かな）","文書・編集",104,[])
        key("eisu","英字入力にする（英数）","文書・編集",102,[])
        key("app-next","次のアプリへ切り替え","Mac基本操作",48)
        key("app-previous","前のアプリへ切り替え","Mac基本操作",48,[.command,.shift])
        key("window-next","同じアプリの次のウィンドウへ","Mac基本操作",50)
        key("window-previous","同じアプリの前のウィンドウへ","Mac基本操作",50,[.command,.shift])
        key("hide","現在のアプリを隠す","Mac基本操作",4)
        key("hide-others","ほかのアプリを隠す","Mac基本操作",4,[.command,.option])
        key("fullscreen","フルスクリーン切り替え","ウィンドウ",3,[.command,.control])
        key("force-quit-dialog","アプリの強制終了画面を開く","Mac基本操作",53,[.command,.option])
        key("current-app-settings","使用中のアプリの設定を開く","Mac基本操作",43)
        result.append(StudioAction(id:"parity-browser-back",name:"ページを戻る",category:"ブラウザー",symbol:"arrow.left",kind:.system,value:SystemCommand.navigateBack.rawValue))
        result.append(StudioAction(id:"parity-browser-forward",name:"ページを進む",category:"ブラウザー",symbol:"arrow.right",kind:.system,value:SystemCommand.navigateForward.rawValue))
        key("browser-hard-reload","キャッシュを無視して更新（Chrome）","ブラウザー",15,[.command,.shift])
        key("safari-reload","キャッシュを無視して更新（Safari）","ブラウザー",15,[.command,.option])
        key("browser-stop","ページの読み込みを中止","ブラウザー",53,[])
        key("browser-address","アドレスバーを選択","ブラウザー",37)
        key("browser-home","ホームページを開く","ブラウザー",4,[.command,.shift])
        key("tab-first","最初のタブへ","ブラウザー",18)
        key("tab-last","最後のタブへ","ブラウザー",25)
        key("browser-history","履歴を開く（Chrome／Safari）","ブラウザー",16)
        key("chrome-downloads","ダウンロードを開く（Chrome）","ブラウザー",38,[.command,.shift])
        key("safari-downloads","ダウンロードを開く（Safari）","ブラウザー",37,[.command,.option])
        key("browser-bookmark","ブックマークに追加","ブラウザー",2)
        key("chrome-bookmarks","ブックマーク管理（Chrome）","ブラウザー",11,[.command,.option])
        key("safari-bookmarks","ブックマークサイドバー（Safari）","ブラウザー",18,[.command,.control])
        key("browser-devtools","開発者ツール（有効化したブラウザー）","ブラウザー",34,[.command,.option])
        key("browser-zoom-in","表示を拡大","ブラウザー",24)
        key("browser-zoom-out","表示を縮小","ブラウザー",27)
        key("finder-new","新しいFinderウィンドウ（Finder内）","Finder",45)
        key("finder-parent","上の階層へ","Finder",126)
        key("finder-open","選択項目を開く","Finder",125)
        key("finder-back","前のフォルダへ","Finder",33)
        key("finder-forward","次のフォルダへ","Finder",30)
        key("finder-path","フォルダへ移動（パスを入力）","Finder",5,[.command,.shift])
        key("finder-new-folder","新しいフォルダ","Finder",45,[.command,.shift])
        key("finder-rename","名前を変更（Finder内）","Finder",36,[])
        key("finder-info","情報を見る","Finder",34)
        key("finder-trash","ゴミ箱へ移動","Finder",51)
        key("finder-delete","すぐに削除（Finderの確認あり）","Finder",51,[.command,.option])
        key("finder-preview","クイックルック","Finder",49,[])
        key("finder-preview-pane","プレビュー欄を切り替え","Finder",35,[.command,.shift])
        key("finder-options","表示オプションを開く","Finder",38)
        key("finder-pathbar","パスバーを切り替え","Finder",35,[.command,.option])
        key("finder-hidden","隠しファイルを表示／非表示","Finder",47,[.command,.shift])
        key("finder-icons","アイコン表示","Finder",18)
        key("finder-list","リスト表示","Finder",19)
        key("finder-columns","カラム表示","Finder",20)
        key("finder-gallery","ギャラリー表示","Finder",21)
        key("finder-home","ホームフォルダへ","Finder",4,[.command,.shift])
        key("finder-downloads","ダウンロードフォルダへ","Finder",37,[.command,.option])
        let apps = [
            ("settings","システム設定","/System/Applications/System Settings.app"),
            ("finder","Finderを開く","/System/Library/CoreServices/Finder.app"),
            ("activity","アクティビティモニタ","/System/Applications/Utilities/Activity Monitor.app"),
            ("terminal","ターミナル","/System/Applications/Utilities/Terminal.app"),
            ("calculator","電卓","/System/Applications/Calculator.app"),
            ("textedit","テキストエディット","/System/Applications/TextEdit.app"),
            ("preview","プレビュー","/System/Applications/Preview.app"),
            ("freeform","フリーボード","/System/Applications/Freeform.app"),
            ("screenshot","スクリーンショットツール","/System/Applications/Utilities/Screenshot.app"),
            ("disk","ディスクユーティリティ","/System/Applications/Utilities/Disk Utility.app"),
            ("console","コンソール（ログ）","/System/Applications/Utilities/Console.app"),
            ("information","システム情報","/System/Applications/Utilities/System Information.app"),
            ("shortcuts","ショートカット","/System/Applications/Shortcuts.app")]
        for (id,name,path) in apps {
            result.append(StudioAction(id:"parity-app-" + id,name:name,category:"Mac標準アプリ",symbol:"app",kind:.launch,value:path))
        }
        return result
    }()
}
