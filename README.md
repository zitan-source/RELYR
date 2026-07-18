# RELYR

キーボードとマウスの入力を、画面上からレイヤー・ショートカット・アプリ起動・マクロなどへ割り当てるWindows 10/11向けアプリです。

## ダウンロード

最新版は[GitHub Releases](https://github.com/zitan-source/RELYR/releases/latest)から
`RELYR-Setup-<version>.exe`をダウンロードしてください。
同じReleaseにある`.sha256`ファイルで、ダウンロードしたファイルが壊れていないか確認できます。

現在のインストーラーはコード署名を行っていないため、初回実行時にWindowsの
SmartScreenが「不明な発行元」と表示する場合があります。ソースコードとビルド手順はこのリポジトリで公開しています。

## 主な機能

- Space、CapsLock、マウスボタンを押している間だけ別の割り当てを使うレイヤー
- キー、ショートカット、文字列、アプリ・ファイル・URL、マウス操作の割り当て
- アプリごとのプロファイル自動切り替え
- キー・マウス操作のマクロ記録、編集、再生
- 仮想デスクトップとウィンドウの移動・整列
- JIS／US配列の画面キーボード
- 指定フォルダーに置かれた圧縮ファイルの自動解凍

## 最初の使い方

1. 左側で通常、Space、CapsLock、マウスなどのレイヤーを選びます。
2. 中央のキーボードまたはマウスから設定するボタンを選びます。
3. 右側で実行するアクションを選び、保存して反映します。

CapsLockレイヤーを有効にすると、Windowsのキー割り当て変更と再起動が必要です。
アプリ設定またはアンインストールから元のCapsLockへ戻せます。

## 安全・プライバシー

- RELYR本体はキーボードとマウスの割り当てを実現するため、Windowsのグローバル入力フックを使用します。
- 入力内容を外部サーバーへ送信する処理はありません。
- マクロの入力記録は、ユーザーが記録を開始した間だけ行います。
- 設定とマクロは`%AppData%\RELYR`へ保存します。
- 緊急停止は`Ctrl + Alt + Shift + F12`です。
- 管理者権限のウィンドウでも動作させるため、インストール版は管理者モードの起動タスクを使用します。

## 配布版

利用者は次の管理者インストーラーを実行します。

`artifacts\production\RELYR-Setup-<version>.exe`

- 64bit Windowsへ管理者権限でインストールします。
- インストール時に管理者モードの起動タスクを登録し、以後の手動起動・自動起動ではUAC確認を表示しません。
- .NET 10 Desktop Runtimeがない場合だけ、Microsoft公式サイトから自動取得してインストールします。
- Runtimeはインストーラー本体へ同梱していないため、配布ファイルを小さく保っています。
- アンインストール時は自動起動を解除し、CapsLockのF13割り当てを標準へ戻します。反映に再起動が必要な場合は、完了画面で「今すぐ再起動」または「後で再起動」を選べます。
- ユーザー設定は `%AppData%\RELYR` に保存します。旧版の設定は初回起動時に自動移行されます。
- アンインストール時に、ユーザー設定を残すか完全に削除するか選択できます。

## 開発・検証

必要なもの：

- Windows 10/11 x64
- .NET 10 SDK
- インストーラーを作る場合のみInno Setup 6

本番アプリを全テスト後に生成します。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-production.ps1
```

Inno Setup 6がある開発環境では、全テスト後にインストーラーまで生成します。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1
```

本番成果物は `artifacts\production` のみに生成します。ZIP、ポータブル版、自己完結ランタイム同梱版は作成しません。

## ライセンス

RELYR本体は[MIT License](LICENSE)で公開します。
同梱ライブラリと参考プロジェクトの著作権・ライセンスは
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)に記載しています。

GitHubのソースリポジトリには`bin/`、`obj/`、`artifacts/`、ユーザー設定を含めません。
インストーラーはソースへコミットせず、必要に応じてGitHub Releasesへ掲載します。
一般配布では`RELYR-Setup-<version>.exe`と、同時に生成される
`RELYR-Setup-<version>.exe.sha256`をGitHub Releasesへ掲載します。
