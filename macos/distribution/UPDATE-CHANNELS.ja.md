# WindowsとMacの更新経路

同じ zitan-source/RELYR リポジトリで管理します。

| 項目 | Windows | Mac |
| --- | --- | --- |
| タグ | 既存の v番号 | macos-v番号（例 macos-v0.2.0） |
| 更新確認 | 既存の releases/latest | releases一覧からmacos-vタグを選別 |
| 配布物 | 既存のexe・sha256 | RELYR-Mac-番号-arm64.dmg・sha256 |
| 更新操作 | 既存方式を維持 | 「更新を確認…」→DMGを取得→終了して置き換え |

Macリリースの作成・公開・編集時は **make_latest: "false"** を必ず指定します。
GitHub画面の「Set as the latest release」は選択しません。
Windowsクライアントはlatestを参照するため、この指定を省略してはいけません。
公開前後に /repos/zitan-source/RELYR/releases/latest のタグとWindows資産が変化していないことを確認します。

Macはdraft・prereleaseを除外し、数値3桁のmacos-vタグ、同バージョンのApple Silicon DMG、正しいリポジトリのダウンロードURLを検証します。
Windowsリリースや同じ／古いMacバージョンは更新として扱いません。
ネットワークエラーは「更新なし」と区別します。

python3 macos/prepare-release.py でDMGのチェックサムを検証し、draft=true / make_latest=false のリクエストを.verificationに作成できます。公開自体は実行しません。
現在のMac更新は手動確認・手動置き換え方式です。バックグラウンド自動インストールは未実装です。
