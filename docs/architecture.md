# RELYR アーキテクチャ概要

この文書は、初めてコードを読む開発者が変更箇所と検証範囲を判断するための入口である。動作上の固定仕様は [stability-contract.md](stability-contract.md)、修飾キークリックは [modifier-click-contract.md](modifier-click-contract.md)、権限境界は [IPC-DESIGN.md](../IPC-DESIGN.md) を優先する。

## プロジェクト境界

- アプリケーションプロジェクトは `RELYR/RELYR.csproj` の1つだけである。
- WPFを中心に、一部の通知領域UIでWindows Forms、低レベル入力とウィンドウ操作でWin32 APIを使用する。
- `RuntimeRole.Standard` は通常起動、`RuntimeRole.UiHost` は非管理者UI、`RuntimeRole.ElevatedHelper` は入力と管理者対象操作を担当する。
- 設定モデルは `Models.cs`、永続化と移行は `ConfigService.cs` が所有する。

## 起動とプロセス境界

1. `App.xaml.cs` が引数、単一起動、ランタイム役割、診断コマンドを判定する。
2. `StartupService.cs` が通常権限UIと昇格ヘルパーの起動経路を管理する。
3. `IpcBootstrap.cs`、`ElevatedIpc.cs`、`IpcTransport.cs` が同一ユーザー・同一セッション・実行パスを検証して通信する。
4. `MainWindow` はUI役割に応じて編集画面、入力エンジン、トレイ、Deck、アーカイブ監視を接続する。

## MainWindow の責務マップ

`MainWindow` はWPFの部分クラスである。新しい処理は、次の所有ファイルに置く。

| ファイル | 所有する処理 |
| --- | --- |
| `MainWindow.xaml.cs` | 初期化、キーボード表示、割り当て編集の中核 |
| `MainWindow.ShellLayout.cs` | ツールバー、テーマ切替、レスポンシブ配置 |
| `MainWindow.Tray.cs` | 通知領域アイコンとメニュー |
| `MainWindow.ProfileRouting.cs` | 実行プロファイルと自動切替 |
| `MainWindow.Profiles.cs` | プロファイルの作成、複製、名前変更、削除 |
| `MainWindow.InputManagement.cs` | 入力検出、レイヤー切替、エンジンと自動保存 |
| `MainWindow.Settings.cs` | 設定画面、完全設定の適用、初回表示 |
| `MainWindow.Archive.cs` | アーカイブ監視と進捗表示 |
| `MainWindow.Lifecycle.cs` | 終了、再起動、セッション遷移時の解放 |
| `MainWindow.Dialogs.cs` | 共通入力・選択ダイアログ |
| `MainWindow.Deck.cs` | Deck編集画面と共有レイアウト |
| `MainWindow.ActionPalette.cs` | Actionライブラリ |
| `MainWindow.AssignmentDrag.cs` | 割り当てのドラッグ移動 |
| `MainWindow.Updates.cs` | 更新確認、検証、起動 |
| `MainWindow.TestHooks.cs` | テスト用アクセサーを本体の処理経路から分離して集約 |

## 入力経路

`InputEngine.cs` が物理状態と割り当て選択を管理し、`InputEngine.RawInput.cs` が物理ボタンを独立追跡する。Win32宣言は `InputEngine.Interop.cs`、生成入力と専用ワーカーは `InputEngine.Output.cs` に置く。選択済みアクションは `MainWindow` のキューを経由し、`MappingExecutor` と `SystemInputOutput` が実行する。

低レベルフック内で待機、ファイルI/O、同期Dispatcher呼び出し、`SendInput`を行ってはならない。入力変更前に固定仕様を読み、物理入力テストは利用中のWindowsセッションで実行しない。

## Deck経路

- `DeckLayoutDefinition` とその `Mappings` が編集画面とオーバーレイの共有状態である。
- `MainWindow.Deck.cs` が編集操作を所有し、`OverlayService.cs` が表示中オーバーレイを管理する。
- `DeckPanelOverlayWindow.cs` が表示と自動非表示、`DeckPanelOverlayWindow.Assignments.cs` が割り当て・ファイル・プレビュー・並べ替え、`DeckPanelOverlayWindow.Native.cs` がWin32ウィンドウ連携とExplorerドロップを所有する。
- 1スロットの変更は差分更新を維持し、外観変更だけで全Deckを再構築しない。

## 検証

- `SelfTest.cs`: 設定、移行、サービス、アクションの回帰検査。
- `ConfigurationMatrixTest.cs`: レイヤー、条件、アクション種別の組み合わせ検査。
- `UiIntegrationTest.cs`: 実際のWPF要素と編集経路。
- `StartupIntegrationTest.cs` / `ShutdownIntegrationTest.cs`: 単一起動、役割、終了監視。
- `build-production.ps1`: 静的安全検査、警告ゼロReleaseビルド、安全な非実入力テスト、本番Publish。
- `build-installer.ps1`: 本番Publishに加えてインストーラー、バージョン、SHA-256、Defender検査。

テストランナーは `ProductionPublish=true` の本番成果物から除外する。変更時は対象機能のテストだけで完了とせず、[stability-contract.md](stability-contract.md) の安全な検証順序を実行する。
