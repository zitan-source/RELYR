namespace RELYR;

public sealed record CatalogAction(string Category, string Name, string Description, ActionKind Kind, string Value)
{
    public string MajorCategory => ActionCatalog.GetMajorCategory(Category);
}

public static class ActionCatalog
{
    public const string ShowRelyrMainWindowAction = "ShowRelyrMainWindow";
    static readonly IReadOnlyDictionary<string, string> MouseActionLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["MouseLeft"] = "左クリック",
        ["ShiftDrag"] = "Shift + 左クリック",
        ["CtrlDrag"] = "Ctrl + 左クリック",
        ["AltDrag"] = "Alt + 左クリック",
        ["MouseRight"] = "右クリック",
        ["MouseMiddle"] = "ホイールクリック",
        ["WheelUp"] = "ホイール上",
        ["WheelDown"] = "ホイール下",
        ["TiltLeft"] = "チルト左",
        ["TiltRight"] = "チルト右",
        ["MouseBack"] = "戻る",
        ["MouseForward"] = "進む",
        ["MouseX"] = "追加ボタン"
    };

    public static string DisplayMouseAction(string? value)
    {
        string input = (value ?? "").Trim();
        return MouseActionLabels.TryGetValue(input, out string? label) ? "マウス：" + label : input;
    }

    public static bool TryNormalizeMouseAction(string? value, out string normalized)
    {
        string input = (value ?? "").Trim();
        const string displayPrefix = "マウス：";
        bool displayedValue = input.StartsWith(displayPrefix, StringComparison.Ordinal);
        if (displayedValue)
            input = input[displayPrefix.Length..].Trim();
        if (displayedValue)
        {
            var displayed = MouseActionLabels.FirstOrDefault(x => x.Value.Equals(input, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(displayed.Key))
            {
                normalized = displayed.Key;
                return true;
            }
        }
        if (input.Equals("Shift+MouseLeft", StringComparison.OrdinalIgnoreCase) || input.Equals("LeftShift+MouseLeft", StringComparison.OrdinalIgnoreCase) || input.Equals("Shift+左クリック", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "ShiftDrag";
            return true;
        }
        if (input.Equals("Ctrl+MouseLeft", StringComparison.OrdinalIgnoreCase) || input.Equals("LeftCtrl+MouseLeft", StringComparison.OrdinalIgnoreCase) || input.Equals("Ctrl+左クリック", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "CtrlDrag";
            return true;
        }
        if (input.Equals("Alt+MouseLeft", StringComparison.OrdinalIgnoreCase) || input.Equals("LeftAlt+MouseLeft", StringComparison.OrdinalIgnoreCase) || input.Equals("Alt+左クリック", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "AltDrag";
            return true;
        }
        var action = Items.FirstOrDefault(x => x.Kind == ActionKind.Mouse && x.Value.Equals(input, StringComparison.OrdinalIgnoreCase));
        normalized = action?.Value ?? input;
        return action != null;
    }

    public static IReadOnlyList<CatalogAction> Items
    {
        get;
    } =
    [
        new("Windowsの基本機能","設定を開く","Windows設定を表示します",ActionKind.Shortcut,"Win+I"),
        new("Windowsの基本機能","エクスプローラーを開く","Windows標準の方法でファイルエクスプローラーを開きます",ActionKind.Shortcut,"Win+E"),
        new("Windowsの基本機能","新しいエクスプローラーを開く","既存のウィンドウとは別に新しいエクスプローラーを開きます",ActionKind.Launch,SystemInputOutput.NewExplorerWindowAction),
        new("Windowsの基本機能","デスクトップ表示","すべてのウィンドウを隠します",ActionKind.Shortcut,"Win+D"),
        new("Windowsの基本機能","タスクビュー","開いているウィンドウを一覧表示します",ActionKind.Shortcut,"Win+Tab"),
        new("Windowsの基本機能","画面をロック","Windowsをロックします",ActionKind.Shortcut,"Win+L"),
        new("Windowsの基本機能","ファイル名を指定して実行","「ファイル名を指定して実行」を開きます",ActionKind.Shortcut,"Win+R"),
        new("Windowsの基本機能","検索を開く","Windows検索を開きます",ActionKind.Shortcut,"Win+S"),
        new("Windowsの基本機能","クイック設定","Wi-Fiや音量のクイック設定を開きます",ActionKind.Shortcut,"Win+A"),
        new("Windowsの基本機能","通知センター","通知とカレンダーを表示します",ActionKind.Shortcut,"Win+N"),
        new("Windowsの基本機能","タスクマネージャー","タスクマネージャーを直接開きます",ActionKind.Shortcut,"Ctrl+Shift+Esc"),
        new("Windowsの基本機能","クリップボード履歴","コピー履歴を表示します",ActionKind.Shortcut,"Win+V"),
        new("Windowsの基本機能","クイックリンクメニュー","管理ツールにすぐアクセスできるメニューを開きます",ActionKind.Shortcut,"Win+X"),

        new("画面キャプチャ","画面全体をコピー","画面全体をクリップボードへコピーします",ActionKind.Key,"PrintScreen"),
        new("画面キャプチャ","使用中のウィンドウをコピー","アクティブウィンドウだけをクリップボードへコピーします",ActionKind.Shortcut,"Alt+PrintScreen"),
        new("画面キャプチャ","範囲を選んでコピー","切り取り領域を選択します",ActionKind.Shortcut,"Win+Shift+S"),

        new("音量・メディア","音量を上げる","システム音量を上げます",ActionKind.Key,"VolumeUp"),
        new("音量・メディア","音量を下げる","システム音量を下げます",ActionKind.Key,"VolumeDown"),
        new("音量・メディア","ミュート切り替え","システム音声のミュートを切り替えます",ActionKind.Key,"VolumeMute"),
        new("音量・メディア","再生／一時停止","メディアを再生または一時停止します",ActionKind.Key,"MediaPlayPause"),
        new("音量・メディア","次の曲","次のメディアへ進みます",ActionKind.Key,"MediaNextTrack"),
        new("音量・メディア","前の曲","前のメディアへ戻ります",ActionKind.Key,"MediaPreviousTrack"),
        new("音量・メディア","停止","メディア再生を停止します",ActionKind.Key,"MediaStop"),

        new("入力・アクセシビリティ","絵文字パネル","絵文字・顔文字・記号を開きます",ActionKind.Shortcut,"Win+."),
        new("入力・アクセシビリティ","入力言語を切り替え","キーボードの入力言語を切り替えます",ActionKind.Shortcut,"Win+Space"),
        new("入力・アクセシビリティ","音声入力","マイクを使ったWindows音声入力を開きます",ActionKind.Shortcut,"Win+H"),
        new("入力・アクセシビリティ","スクリーンキーボードを開く","画面上のキーボードを開閉します",ActionKind.Shortcut,"Ctrl+Win+O"),
        new("入力・アクセシビリティ","ナレーターを開く","画面読み上げ機能を開閉します",ActionKind.Shortcut,"Ctrl+Win+Enter"),
        new("入力・アクセシビリティ","拡大鏡を開く","Windows拡大鏡を起動します",ActionKind.Shortcut,"Win+Add"),
        new("入力・アクセシビリティ","拡大鏡を終了","Windows拡大鏡を終了します",ActionKind.Shortcut,"Win+Esc"),

        new("IME・日本語入力","IMEをオン","日本語入力をオンにします",ActionKind.Shortcut,"ImeOn"),
        new("IME・日本語入力","IMEをオフ","日本語入力をオフにします",ActionKind.Shortcut,"ImeOff"),
        new("IME・日本語入力","IMEオン／オフを切り替え","現在の日本語入力状態を反対へ切り替えます",ActionKind.Shortcut,"ImeToggle"),

        new("編集・クリップボード","コピー","選択内容をコピーします",ActionKind.Shortcut,"Ctrl+C"),
        new("編集・クリップボード","貼り付け","コピーした内容を貼り付けます",ActionKind.Shortcut,"Ctrl+V"),
        new("編集・クリップボード","切り取り","選択内容を切り取ります",ActionKind.Shortcut,"Ctrl+X"),
        new("編集・クリップボード","すべて選択","すべての項目を選択します",ActionKind.Shortcut,"Ctrl+A"),
        new("編集・クリップボード","元に戻す","直前の操作を取り消します",ActionKind.Shortcut,"Ctrl+Z"),
        new("編集・クリップボード","やり直す","取り消した操作をやり直します",ActionKind.Shortcut,"Ctrl+Y"),
        new("編集・クリップボード","検索","ページや文書内を検索します",ActionKind.Shortcut,"Ctrl+F"),

        new("ファイル・文書","新規作成","現在のアプリで新しい文書やウィンドウを作成します",ActionKind.Shortcut,"Ctrl+N"),
        new("ファイル・文書","開く","ファイルを開く画面を表示します",ActionKind.Shortcut,"Ctrl+O"),
        new("ファイル・文書","保存","現在のファイルを保存します",ActionKind.Shortcut,"Ctrl+S"),
        new("ファイル・文書","名前を付けて保存","保存先とファイル名を選択します",ActionKind.Shortcut,"Ctrl+Shift+S"),
        new("ファイル・文書","印刷","印刷画面を開きます",ActionKind.Shortcut,"Ctrl+P"),

        new("文書の書式","太字","選択文字の太字を切り替えます",ActionKind.Shortcut,"Ctrl+B"),
        new("文書の書式","斜体","選択文字の斜体を切り替えます",ActionKind.Shortcut,"Ctrl+I"),
        new("文書の書式","下線","選択文字の下線を切り替えます",ActionKind.Shortcut,"Ctrl+U"),

        new("ウィンドウ・基本操作","最大化","設定で選んだ対象ウィンドウを最大化します",ActionKind.Shortcut,"MaximizeWindow"),
        new("ウィンドウ・基本操作","最大化／元のサイズに戻す","設定で選んだ対象ウィンドウを、最大化または元のサイズへ切り替えます",ActionKind.Shortcut,"ToggleMaximizeWindow"),
        new("ウィンドウ・基本操作","最小化","設定で選んだ対象ウィンドウを最小化します",ActionKind.Shortcut,"MinimizeActiveWindow"),
        new("ウィンドウ・基本操作","下方向へ操作（復元／最小化）","設定で選んだ対象が最大化中なら復元し、通常サイズなら最小化します",ActionKind.Shortcut,"RestoreOrMinimizeWindow"),
        new("ウィンドウ・基本操作","ウィンドウを閉じる","設定で選んだ対象ウィンドウを閉じます",ActionKind.Shortcut,"CloseActiveWindow"),
        new("ウィンドウ・基本操作","次のウィンドウへ切り替え","開いている次のウィンドウへ切り替えます",ActionKind.Shortcut,"Alt+Tab"),
        new("ウィンドウ・基本操作","前のウィンドウへ切り替え","開いている前のウィンドウへ切り替えます",ActionKind.Shortcut,"Alt+Shift+Tab"),

        new("ウィンドウ・一括操作","すべて最小化／元に戻す","1回目にすべて最小化し、もう一度実行すると元に戻します",ActionKind.Shortcut,"ToggleMinimizeAllWindows"),
        new("ウィンドウ・一括操作","すべてのウィンドウを最小化","Windows + M ですべてのウィンドウを最小化します",ActionKind.Shortcut,"Win+M"),
        new("ウィンドウ・一括操作","最小化したウィンドウを元に戻す","Shift + Windows + M で直前に最小化したウィンドウを元に戻します",ActionKind.Shortcut,"Shift+Win+M"),

        new("ウィンドウ・整列","対象ウィンドウを左半分に配置","設定で選んだ対象ウィンドウを、現在のモニターの左半分へ配置します",ActionKind.Shortcut,"SnapWindowLeft"),
        new("ウィンドウ・整列","対象ウィンドウを右半分に配置","設定で選んだ対象ウィンドウを、現在のモニターの右半分へ配置します",ActionKind.Shortcut,"SnapWindowRight"),
        new("ウィンドウ・整列","スナップレイアウトを開く","ウィンドウの配置パターンを選びます",ActionKind.Shortcut,"Win+Z"),

        new("ウィンドウ・モニター移動","左のモニターへ移動","設定で選んだ対象ウィンドウを、左側で最も近いモニターへ移動します",ActionKind.Shortcut,"MoveWindowMonitorLeft"),
        new("ウィンドウ・モニター移動","右のモニターへ移動","設定で選んだ対象ウィンドウを、右側で最も近いモニターへ移動します",ActionKind.Shortcut,"MoveWindowMonitorRight"),
        new("ウィンドウ・モニター移動","上のモニターへ移動","設定で選んだ対象ウィンドウを、上側で最も近いモニターへ移動します",ActionKind.Shortcut,"MoveWindowMonitorUp"),
        new("ウィンドウ・モニター移動","下のモニターへ移動","設定で選んだ対象ウィンドウを、下側で最も近いモニターへ移動します",ActionKind.Shortcut,"MoveWindowMonitorDown"),

        new("仮想デスクトップ","左のデスクトップへ","左隣の仮想デスクトップへ移動します",ActionKind.Shortcut,"Ctrl+Win+Left"),
        new("仮想デスクトップ","右のデスクトップへ","右隣の仮想デスクトップへ移動します",ActionKind.Shortcut,"Ctrl+Win+Right"),
        new("仮想デスクトップ","新しいデスクトップ","仮想デスクトップを作成します",ActionKind.Shortcut,"Ctrl+Win+D"),
        new("仮想デスクトップ","現在のデスクトップを閉じる","現在の仮想デスクトップを閉じます",ActionKind.Shortcut,"Ctrl+Win+F4"),
        new("仮想デスクトップ","デスクトップ 1 へ","一番左の仮想デスクトップへ直接移動します",ActionKind.Shortcut,"Desktop1"),
        new("仮想デスクトップ","デスクトップ 2 へ","左から2番目の仮想デスクトップへ移動します",ActionKind.Shortcut,"Desktop2"),
        new("仮想デスクトップ","デスクトップ 3 へ","左から3番目の仮想デスクトップへ移動します",ActionKind.Shortcut,"Desktop3"),
        new("仮想デスクトップ","デスクトップ 4 へ","左から4番目の仮想デスクトップへ移動します",ActionKind.Shortcut,"Desktop4"),
        new("仮想デスクトップ","デスクトップ 5 へ","左から5番目の仮想デスクトップへ移動します",ActionKind.Shortcut,"Desktop5"),
        new("仮想デスクトップ","デスクトップ 6 へ","左から6番目の仮想デスクトップへ移動します",ActionKind.Shortcut,"Desktop6"),
        new("仮想デスクトップ","デスクトップ 7 へ","左から7番目の仮想デスクトップへ移動します",ActionKind.Shortcut,"Desktop7"),
        new("仮想デスクトップ","デスクトップ 8 へ","左から8番目の仮想デスクトップへ移動します",ActionKind.Shortcut,"Desktop8"),
        new("仮想デスクトップ","ウィンドウと一緒に右へ移動","設定で選んだ対象ウィンドウを右隣へ移し、その仮想デスクトップも表示します",ActionKind.Shortcut,"MoveWindowDesktopRight"),
        new("仮想デスクトップ","ウィンドウと一緒に左へ移動","設定で選んだ対象ウィンドウを左隣へ移し、その仮想デスクトップも表示します",ActionKind.Shortcut,"MoveWindowDesktopLeft"),

        new("ブラウザー・ページ操作","戻る","前のページへ戻ります",ActionKind.Shortcut,"Alt+Left"),
        new("ブラウザー・ページ操作","進む","次のページへ進みます",ActionKind.Shortcut,"Alt+Right"),
        new("ブラウザー・ページ操作","更新","現在のページを通常更新します",ActionKind.Shortcut,"Ctrl+R"),
        new("ブラウザー・ページ操作","キャッシュを無視して更新","キャッシュを使わずページを再取得します",ActionKind.Shortcut,"Ctrl+Shift+R"),
        new("ブラウザー・ページ操作","読み込みを中止","ページの読み込みを停止します",ActionKind.Key,"Esc"),
        new("ブラウザー・ページ操作","アドレスバー","アドレスバーを選択します",ActionKind.Shortcut,"Ctrl+L"),
        new("ブラウザー・ページ操作","ページ内検索","表示中のページを検索します",ActionKind.Shortcut,"Ctrl+F"),
        new("ブラウザー・ページ操作","ホームページへ","設定されたホームページを開きます",ActionKind.Shortcut,"Alt+Home"),

        new("ブラウザー・タブ操作","新しいタブ","新しいタブを開きます",ActionKind.Shortcut,"Ctrl+T"),
        new("ブラウザー・タブ操作","タブを閉じる","現在のタブを閉じます",ActionKind.Shortcut,"Ctrl+W"),
        new("ブラウザー・タブ操作","右のタブへ移動","右隣のタブを表示します",ActionKind.Shortcut,"Ctrl+Tab"),
        new("ブラウザー・タブ操作","左のタブへ移動","左隣のタブを表示します",ActionKind.Shortcut,"Ctrl+Shift+Tab"),
        new("ブラウザー・タブ操作","閉じたタブを開き直す","直前に閉じたタブを復元します",ActionKind.Shortcut,"Ctrl+Shift+T"),
        new("ブラウザー・タブ操作","最初のタブへ","一番左のタブを表示します",ActionKind.Shortcut,"Ctrl+1"),
        new("ブラウザー・タブ操作","最後のタブへ","一番右のタブを表示します",ActionKind.Shortcut,"Ctrl+9"),

        new("ブラウザー・履歴とブックマーク","履歴を開く","閲覧履歴を表示します",ActionKind.Shortcut,"Ctrl+H"),
        new("ブラウザー・履歴とブックマーク","ダウンロードを開く","ダウンロード一覧を表示します",ActionKind.Shortcut,"Ctrl+J"),
        new("ブラウザー・履歴とブックマーク","ブックマークに追加","現在のページをお気に入りへ追加します",ActionKind.Shortcut,"Ctrl+D"),
        new("ブラウザー・履歴とブックマーク","ブックマーク管理を開く","ブックマークの一覧と編集画面を開きます",ActionKind.Shortcut,"Ctrl+Shift+O"),

        new("ブラウザー・表示と開発","全画面表示を切り替え","ブラウザーの全画面表示を切り替えます",ActionKind.Key,"F11"),
        new("ブラウザー・表示と開発","開発者ツール","開発者ツールを開閉します",ActionKind.Key,"F12"),
        new("表示倍率","ズームイン（拡大） Ctrl + ＋","ブラウザーや対応アプリの表示を拡大します",ActionKind.Shortcut,"Ctrl+Add"),
        new("表示倍率","ズームアウト（縮小） Ctrl + －","ブラウザーや対応アプリの表示を縮小します",ActionKind.Shortcut,"Ctrl+Subtract"),
        new("表示倍率","表示倍率を100%に戻す","ブラウザーや対応アプリの表示倍率を標準に戻します",ActionKind.Shortcut,"Ctrl+0"),

        new("エクスプローラー・タブ","新しいウィンドウ","新しいエクスプローラーを開きます",ActionKind.Shortcut,"Ctrl+N"),
        new("エクスプローラー・タブ","新しいタブ","新しいタブを開きます",ActionKind.Shortcut,"Ctrl+T"),
        new("エクスプローラー・タブ","タブを閉じる","現在のタブを閉じます",ActionKind.Shortcut,"Ctrl+W"),
        new("エクスプローラー・タブ","右のタブへ移動","右隣のタブを表示します",ActionKind.Shortcut,"Ctrl+Tab"),
        new("エクスプローラー・タブ","左のタブへ移動","左隣のタブを表示します",ActionKind.Shortcut,"Ctrl+Shift+Tab"),

        new("エクスプローラー・移動","上の階層へ","現在のフォルダーの親フォルダーへ移動します",ActionKind.Shortcut,"Alt+Up"),
        new("エクスプローラー・移動","戻る","前に表示していたフォルダーへ戻ります",ActionKind.Shortcut,"Alt+Left"),
        new("エクスプローラー・移動","進む","次に表示していたフォルダーへ進みます",ActionKind.Shortcut,"Alt+Right"),
        new("エクスプローラー・移動","アドレスバー","現在のパスを選択します",ActionKind.Shortcut,"Ctrl+L"),
        new("エクスプローラー・移動","検索","現在のフォルダー内を検索します",ActionKind.Shortcut,"Ctrl+F"),

        new("エクスプローラー・ファイル操作","新しいフォルダー","現在の場所に新しいフォルダーを作ります",ActionKind.Shortcut,"Ctrl+Shift+N"),
        new("エクスプローラー・ファイル操作","名前を変更","選択したファイルやフォルダーの名前を変更します",ActionKind.Key,"F2"),
        new("エクスプローラー・ファイル操作","プロパティ","選択した項目のプロパティを開きます",ActionKind.Shortcut,"Alt+Enter"),
        new("エクスプローラー・ファイル操作","すべて選択","現在のフォルダー内をすべて選択します",ActionKind.Shortcut,"Ctrl+A"),
        new("エクスプローラー・ファイル操作","選択を反転","選択されていない項目と選択中の項目を入れ替えます",ActionKind.Shortcut,"Ctrl+Shift+I"),
        new("エクスプローラー・ファイル操作","ごみ箱へ削除","選択した項目をごみ箱へ移動します",ActionKind.Key,"Delete"),
        new("エクスプローラー・ファイル操作","完全に削除","選択した項目をごみ箱へ移さず削除します",ActionKind.Shortcut,"Shift+Delete"),

        new("エクスプローラー・表示","更新","現在のフォルダーを更新します",ActionKind.Key,"F5"),
        new("エクスプローラー・表示","プレビューウィンドウを切り替え","プレビューウィンドウの表示を切り替えます",ActionKind.Shortcut,"Alt+P"),
        new("エクスプローラー・表示","詳細ウィンドウを切り替え","ファイルの詳細情報を表示するウィンドウを開閉します",ActionKind.Shortcut,"Alt+Shift+P"),

        new("マウス・ホイール","左クリック","左ボタンをクリックします",ActionKind.Mouse,"MouseLeft"),
        new("マウス・ホイール","Shift+左クリック","単発ではShift+左クリック、ドラッグではShiftと左ボタンを終了まで保持します",ActionKind.Mouse,"ShiftDrag"),
        new("マウス・ホイール","Ctrl+左クリック","単発ではCtrl+左クリック、ドラッグではCtrlと左ボタンを終了まで保持します",ActionKind.Mouse,"CtrlDrag"),
        new("マウス・ホイール","Alt+左クリック","単発ではAlt+左クリック、ドラッグではAltと左ボタンを終了まで保持します",ActionKind.Mouse,"AltDrag"),
        new("マウス・ホイール","右クリック","右ボタンをクリックします",ActionKind.Mouse,"MouseRight"),
        new("マウス・ホイール","中クリック","ホイールボタンをクリックします",ActionKind.Mouse,"MouseMiddle"),
        new("マウス・ホイール","ホイール上","上方向へスクロールします",ActionKind.Mouse,"WheelUp"),
        new("マウス・ホイール","ホイール下","下方向へスクロールします",ActionKind.Mouse,"WheelDown"),
        new("マウス・ホイール","チルト左","対応マウスで横方向へスクロールします",ActionKind.Mouse,"TiltLeft"),
        new("マウス・ホイール","チルト右","対応マウスで横方向へスクロールします",ActionKind.Mouse,"TiltRight"),
        new("マウス・ホイール","戻るボタン","マウスの戻るボタンを送信します",ActionKind.Mouse,"MouseBack"),
        new("マウス・ホイール","進むボタン","マウスの進むボタンを送信します",ActionKind.Mouse,"MouseForward"),

        new("オーバーレイ・入力","テンキー","半透明のテンキーを表示し、直前の入力先へクリックした数字を送ります",ActionKind.Shortcut,OverlayService.NumpadAction),
        new("オーバーレイ・入力","ナビゲーション＋テンキー＋方向キー","ナビゲーション、方向キー、テンキーをまとめた半透明パネルを表示します",ActionKind.Shortcut,OverlayService.ExtendedKeypadAction),
        new("オーバーレイ・入力","Deckパネル","設定した45個のボタンを、入力先を変えない半透明パネルで表示します",ActionKind.Shortcut,OverlayService.DeckPanelAction),
        new("オーバーレイ・画面","ブランク","すべてのモニターを黒く覆います。キー入力またはマウス移動で戻ります",ActionKind.Shortcut,OverlayService.BlankAction),
        new("オーバーレイ・画面","クロック","時計を表示するスクリーンセーバー風オーバーレイを起動します",ActionKind.Shortcut,OverlayService.ClockAction),

        new("Windowsアプリ・基本","設定","Windowsの設定を開きます",ActionKind.Launch,"ms-settings:"),
        new("Windowsアプリ・基本","コントロールパネル","従来のコントロールパネルを開きます",ActionKind.Launch,"control.exe"),
        new("Windowsアプリ・基本","タスクマネージャー","実行中のアプリやシステム負荷を確認します",ActionKind.Launch,"taskmgr.exe"),
        new("Windowsアプリ・基本","Windows ターミナル","Windows ターミナルを開きます",ActionKind.Launch,"wt.exe"),
        new("Windowsアプリ・基本","コマンドプロンプト","コマンドプロンプトを開きます",ActionKind.Launch,"cmd.exe"),
        new("Windowsアプリ・基本","Windows PowerShell","Windows PowerShellを開きます",ActionKind.Launch,"powershell.exe"),
        new("Windowsアプリ・標準ツール","電卓","Windows電卓を開きます",ActionKind.Launch,"calc.exe"),
        new("Windowsアプリ・標準ツール","メモ帳","メモ帳を開きます",ActionKind.Launch,"notepad.exe"),
        new("Windowsアプリ・標準ツール","ペイント","ペイントを開きます",ActionKind.Launch,"mspaint.exe"),
        new("Windowsアプリ・標準ツール","切り取りツール","画面キャプチャ用の切り取りツールを開きます",ActionKind.Launch,"ms-screenclip:"),
        new("Windowsアプリ・管理ツール","ディスクの管理","ディスクとパーティションを管理します",ActionKind.Launch,"diskmgmt.msc"),
        new("Windowsアプリ・管理ツール","デバイスマネージャー","接続されているデバイスとドライバーを管理します",ActionKind.Launch,"devmgmt.msc"),
        new("Windowsアプリ・管理ツール","コンピューターの管理","Windowsの管理ツールをまとめて開きます",ActionKind.Launch,"compmgmt.msc"),
        new("Windowsアプリ・管理ツール","サービス","Windowsサービスを管理します",ActionKind.Launch,"services.msc"),
        new("Windowsアプリ・管理ツール","イベントビューアー","Windowsのイベントログを確認します",ActionKind.Launch,"eventvwr.msc"),
        new("Windowsアプリ・管理ツール","レジストリエディター","Windowsレジストリを編集します",ActionKind.Launch,"regedit.exe"),
        new("Windowsアプリ・診断","システム情報","PCのハードウェアとWindows情報を表示します",ActionKind.Launch,"msinfo32.exe"),
        new("Windowsアプリ・診断","リソースモニター","CPU・メモリ・ディスク・ネットワークの詳細を表示します",ActionKind.Launch,"resmon.exe"),
        new("Windowsアプリ・診断","DirectX 診断ツール","DirectXとグラフィックス環境を確認します",ActionKind.Launch,"dxdiag.exe"),

        new("RELYR","RELYRを表示","RELYRのメイン画面を表示して前面へ移動します",ActionKind.Shortcut,ShowRelyrMainWindowAction)
        , new("システム操作", "音量を上げる", "音量を5%上げます", ActionKind.Shortcut, SystemControlService.Prefix + "VolumeUp")
        , new("システム操作", "音量を下げる", "音量を5%下げます", ActionKind.Shortcut, SystemControlService.Prefix + "VolumeDown")
        , new("システム操作", "音量ミュート", "音量のミュートを切り替えます", ActionKind.Shortcut, SystemControlService.Prefix + "VolumeMute")
        , new("システム操作", "マイクミュート", "マイクのミュートを切り替えます", ActionKind.Shortcut, SystemControlService.Prefix + "MicMute")
        , new("システム操作", "明るさを上げる", "画面の明るさを10%上げます", ActionKind.Shortcut, SystemControlService.Prefix + "BrightnessUp")
        , new("システム操作", "明るさを下げる", "画面の明るさを10%下げます", ActionKind.Shortcut, SystemControlService.Prefix + "BrightnessDown")
        , new("システム操作", "Wi-Fiをオン", "Wi-Fiのソフトウェア無線をオンにします", ActionKind.Shortcut, SystemControlService.Prefix + "WifiOn")
        , new("システム操作", "Wi-Fiをオフ", "Wi-Fiのソフトウェア無線をオフにします", ActionKind.Shortcut, SystemControlService.Prefix + "WifiOff")
        , new("システム操作", "Wi-Fiを切り替え", "Wi-Fiのオン・オフを切り替えます", ActionKind.Shortcut, SystemControlService.Prefix + "WifiToggle")
        , new("システム操作", "Wi-Fi設定", "WindowsのWi-Fi設定を開きます", ActionKind.Shortcut, SystemControlService.Prefix + "WifiSettings")
        , new("システム操作", "Bluetooth設定", "WindowsのBluetooth設定を開きます", ActionKind.Shortcut, SystemControlService.Prefix + "BluetoothSettings")
    ];

    public static string GetMajorCategory(string category) => category switch
    {
        "Windowsの基本機能" or "画面キャプチャ" => "Windows",
        "入力・アクセシビリティ" or "IME・日本語入力" or "編集・クリップボード" => "入力・編集",
        "ファイル・文書" or "文書の書式" => "ファイル・文書",
        "音量・メディア" => "メディア",
        "仮想デスクトップ" => "ウィンドウ・デスクトップ",
        _ when category.StartsWith("ウィンドウ・", StringComparison.Ordinal) => "ウィンドウ・デスクトップ",
        "表示倍率" => "ブラウザー",
        _ when category.StartsWith("ブラウザー・", StringComparison.Ordinal) => "ブラウザー",
        _ when category.StartsWith("エクスプローラー・", StringComparison.Ordinal) => "エクスプローラー",
        "マウス・ホイール" => "マウス",
        _ when category.StartsWith("オーバーレイ・", StringComparison.Ordinal) => "オーバーレイ",
        "Deckパネル" => "Deckパネル",
        _ when category.StartsWith("Windowsアプリ・", StringComparison.Ordinal) => "Windowsアプリ",
        "プロファイル切替" => "プロファイル",
        "任意のショートカット" => "入力・編集",
        "RELYR" => "その他",
        _ => "その他"
    };

    public static IEnumerable<CatalogAction> Search(string? query)
    {
        string text = query?.Trim() ?? "";
        if (text.Length == 0)
            return Items;
        return Items.Where(x => new[] { x.MajorCategory, x.Category, x.Name, x.Description, x.Value }.Any(value => value.Contains(text, StringComparison.OrdinalIgnoreCase)));
    }
}
