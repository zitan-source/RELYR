# RELYR

**キーボードを、拡張する。**

キーボードとマウスの入力を、画面上からレイヤー・ショートカット・アプリ起動・マクロなどへ割り当てるWindows 10/11向けアプリです。

[English](README.md) | [日本語](README.ja.md) | [紹介サイト](https://zitan-source.github.io/RELYR/) | [最新版](https://github.com/zitan-source/RELYR/releases/latest)

![RELYRのキーボードレイヤーとDeck](https://zitan-source.github.io/RELYR/assets/og-image.png)

## 他のキーマッピングソフトとの併用

RELYRを使用するときは、AutoHotkey、PowerToys Keyboard Manager、メーカー製のキー割り当てソフトなど、他のキーマッピングソフトを無効にすることを推奨します。複数のソフトが同じキーやマウス入力を同時に処理すると、操作の二重実行、意図しないショートカット、キーやマウスボタンが押されたままに見える動作などが起きる可能性があります。

併用する場合は、同じキーやマウスボタンへ重複して割り当てないようにしてください。

## ダウンロード

最新版は[GitHub Releases](https://github.com/zitan-source/RELYR/releases/latest)から`RELYR-Setup-<version>.exe`をダウンロードしてください。同じReleaseにある`.sha256`ファイルで、ダウンロードしたファイルが壊れていないか確認できます。

初回用のSetup版にはMicrosoft公式の.NET Desktop Runtimeを同梱しているため、利用者がランタイムを別途入手する必要はありません。インストール済みのRELYRは、アプリ内の更新機能から軽量な`RELYR-Update-<version>.exe`を自動取得して更新します。

現在のインストーラーはコード署名を行っていないため、初回実行時にWindowsのSmartScreenが「不明な発行元」と表示する場合があります。ソースコードとビルド手順はこのリポジトリで公開しています。

## 主な機能

- Space、CapsLock、マウスボタンを押している間だけ別の割り当てを使うレイヤー
- キー、ショートカット、文字列、アプリ・ファイル・URL、マウス操作の割り当て
- アプリごとのプロファイル自動切り替え
- キー・マウス操作のマクロ記録、編集、再生
- マウスジェスチャーの方向・短押しごとのAction設定
- 仮想デスクトップとウィンドウの移動・整列
- JIS／US配列の画面キーボード
- ランチャー、Windows操作、PC状態を配置できるDeckオーバーレイ
- EXEとショートカットのドラッグ＆ドロップ登録
- 指定フォルダーに置かれた圧縮ファイルの自動解凍

## 最初の使い方

1. 左側で通常、Space、CapsLock、マウスなどのレイヤーを選びます。
2. 中央のキーボードまたはマウスから設定するボタンを選びます。
3. 右側からActionを選ぶか、目的のキーへドラッグします。
4. 保存して設定を反映します。

CapsLockレイヤーを有効にすると、Windowsのキー割り当て変更と再起動が必要です。アプリ設定またはアンインストールから元のCapsLockへ戻せます。

## 安全・プライバシー

- RELYR本体はキーボードとマウスの割り当てを実現するため、Windowsのグローバル入力フックを使用します。
- 入力内容を外部サーバーへ送信する処理はありません。
- マクロの入力記録は、ユーザーが記録を開始した間だけ行います。
- 設定とマクロは`%AppData%\RELYR`へ保存します。
- 緊急停止は`Ctrl + Alt + Shift + F12`です。
- 管理者権限のウィンドウでも動作させるため、インストール版は管理者モードの起動タスクを使用します。

## 配布版

一般配布では次のファイルをGitHub Releasesへ掲載します。

- `RELYR-Setup-<version>.exe` — .NET Desktop Runtimeを含む初回用インストーラー
- `RELYR-Update-<version>.exe` — 軽量な更新用インストーラー
- 各インストーラーに対応する`.sha256`

Setup版は64bit Windowsへ管理者権限でインストールします。管理者モードの起動タスクを登録し、Microsoft公式署名付き.NET 10 Desktop Runtimeを未導入の場合だけインストールします。更新版はチェックサムを検証してから上書き更新します。

アンインストール時は自動起動を解除し、CapsLockのF13割り当てを標準へ戻します。ユーザー設定を残すか完全に削除するか選択できます。

## 開発・検証

必要なもの：

- Windows 10/11 x64
- .NET 10 SDK
- インストーラーを作る場合のみInno Setup 6

コードの責務と変更時の入口は[アーキテクチャ概要](docs/architecture.md)を参照してください。

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
$dll = ".\RELYR\bin\Release\net10.0-windows10.0.17763.0\win-x64\RELYR.dll"
dotnet $dll --self-test
dotnet $dll --configuration-matrix-test
dotnet $dll --ui-test
dotnet $dll --startup-test
dotnet $dll --shutdown-test
```

`--ui-test`は、サインイン済みのWindowsデスクトップ上で実行してください。`build-production.ps1`と`build-installer.ps1`は、通常利用中のWindowsへ入力を注入しないよう、入力エンジンテストを既定で省略します。

`--engine-test`、`--engine-test-no-real`、`ModifierClickScenarioTest`は専用の未使用Windowsセッションでのみ実行してください。固定仕様と安全な検証順序は[stability-contract.md](docs/stability-contract.md)を参照してください。

本番成果物は`artifacts\production`のみに生成します。初回用Setup版と軽量なUpdate版を生成し、ZIPやポータブル版は作成しません。

## ライセンス

RELYR本体は[MIT License](LICENSE)で公開します。同梱ライブラリと参考プロジェクトの著作権・ライセンスは[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)に記載しています。

GitHubのソースリポジトリには`bin/`、`obj/`、`artifacts/`、ユーザー設定を含めません。インストーラーはソースへコミットせず、GitHub Releasesへ掲載します。
