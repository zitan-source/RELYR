# RELYR

キーボードとマウスの入力を、画面上からレイヤー・ショートカット・アプリ起動・マクロなどへ割り当てるWindows 10/11向けアプリです。

## 他のキーマッピングソフトとの併用

RELYRを使用するときは、AutoHotkey、PowerToys Keyboard Manager、メーカー製のキー割り当てソフトなど、
他のキーマッピングソフトを無効にすることを推奨します。複数のソフトが同じキーやマウス入力を同時に処理すると、
操作の二重実行、意図しないショートカット、キーやマウスボタンが押されたままに見える動作などが起きる可能性があります。

併用する場合は、同じキーやマウスボタンへ重複して割り当てないようにしてください。

## ダウンロード

最新版は[GitHub Releases](https://github.com/zitan-source/RELYR/releases/latest)から
`RELYR-Setup-<version>.exe`をダウンロードしてください。
同じReleaseにある`.sha256`ファイルで、ダウンロードしたファイルが壊れていないか確認できます。

初回用のSetup版にはMicrosoft公式の.NET Desktop Runtimeを同梱しているため、利用者が
ランタイムを別途入手する必要はありません。インストール済みのRELYRは、アプリ内の
更新機能から軽量な`RELYR-Update-<version>.exe`を自動取得して更新します。

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
- 初回用Setup版はMicrosoft公式署名付き.NET 10 Desktop Runtime (x64)を内包し、未導入の場合だけ自動でインストールします。インストール中に外部EXEをダウンロードしません。
- 更新版にはRuntimeを同梱しません。RELYRがGitHub Releasesから更新版とチェックサムを取得し、SHA-256検証後に上書き更新します。
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

テストを個別に実行する場合は、リポジトリ直下で次の順に実行します。

```powershell
dotnet build .\RELYR\RELYR.csproj -c Release -warnaserror
$dll = ".\RELYR\bin\Release\net10.0-windows\win-x64\RELYR.dll"
dotnet $dll --self-test
dotnet $dll --engine-test-no-real
dotnet $dll --ui-test
dotnet $dll --startup-test
```

`--ui-test` は、サインイン済みのWindowsデスクトップ上で実行してください。
実際の低レベル入力フックまで検証する場合は `--engine-test-no-real` を
`--engine-test` に置き換えます。`build-production.ps1` と
`build-installer.ps1` は既定でこの実入力テストを含むため、通常はスクリプトを
1回実行するだけでビルド、全テスト、成果物生成まで完了します。実入力テストを
意図的に省く場合だけ `-SkipRealHookTest` を指定します。

本番成果物は `artifacts\production` のみに生成します。初回用Setup版と軽量なUpdate版を生成し、ZIPやポータブル版は作成しません。

## ライセンス

RELYR本体は[MIT License](LICENSE)で公開します。
同梱ライブラリと参考プロジェクトの著作権・ライセンスは
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)に記載しています。

GitHubのソースリポジトリには`bin/`、`obj/`、`artifacts/`、ユーザー設定を含めません。
インストーラーはソースへコミットせず、必要に応じてGitHub Releasesへ掲載します。
一般配布では`RELYR-Setup-<version>.exe`、`RELYR-Update-<version>.exe`と、
同時に生成される各`.sha256`をGitHub Releasesへ掲載します。
