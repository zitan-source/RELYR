# Shift/Ctrlクリック固定仕様

この仕様は回帰禁止です。Shift+左クリックとCtrl+左クリックは、短い修飾クリックとドラッグの両方で正しく動作しなければなりません。CtrlドラッグはPowerPointの図形コピーを含みます。

## 必須のイベント順序

1. 物理左ボタンDownをRELYRが捕捉する。
2. 専用ドラッグワーカーが `ModifierDown` を送る。
3. 同じワーカーが合成 `LeftDown` を送る。
4. `NotifyNativeMouseDragStarted` が完了するまで物理MouseMoveをWindowsへ渡さない。
5. 物理左ボタンUpを捕捉し、低レベルフックを先に復帰させる。
6. 専用ワーカーが合成 `LeftUp` を送る。
7. 最後に `ModifierUp` を送る。

したがって生成順序は必ず次のとおりです。

`ModifierDown -> LeftDown -> LeftUp -> ModifierUp`

## 安全条件

- 低レベル入力フック内、または入力状態ロック保持中に同期的な `SendInput` を実行しない。
- 非常に短いクリックでも、キューへ入ったStart/Endの組を破棄しない。
- Raw Inputで物理マウスボタンのDown/Upを追跡し、RELYRが生成したボタン状態と分離する。
- 物理Upを失った場合はRaw Inputで回復し、それも失った場合は次の物理Downで合成ドラッグを解放する。
- 合成LeftUpをModifierUpより必ず先に送る。
- `GetAsyncKeyState`で合成ボタンを物理押下と誤認しない。
- 通常レイヤーのMouseLeftは割り当て不可のまま維持する。
- タスクバー固有のマウス長押しは通常マウスレイヤーより先に判定する。

## 必須回帰検査

`EngineIntegrationTest`では少なくとも次を維持する。

- 非同期Start完了までMouseMoveを抑止する検査
- `CtrlDown -> LeftDown -> LeftUp -> CtrlUp` の順序検査
- 物理Upフック復帰後に解放し、Officeがコピーを確定できる検査
- 短いCtrlクリックでもStart/Endを保持する検査
- Spaceを先に離した場合も左ボタンUpまでCtrlを保持する検査
- Raw Inputが先にUpを受けた場合の一度だけの解放検査
- 物理Up欠落時と次の物理Downによる回復検査

`UiIntegrationTest`では、実際のMainWindow経路についてShiftDragとCtrlDragの両方を検査する。

## 実行禁止の検査

ユーザーが操作中のWindowsセッションでは、以下を実行しない。

- `--engine-test`
- `--engine-test-no-real`
- `ModifierClickScenarioTest`

これらはWindows全体へ実入力を送る可能性がある。通常のRelease／インストーラービルドでは入力エンジン検査を既定でスキップし、自己テスト、UI統合、起動統合、終了統合だけを実行する。
