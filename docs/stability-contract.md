# RELYR stability contract

Read this before changing input, Deck layout, startup, shutdown, or installer code. It is the short routing map for future work; `AGENTS.md` and `docs/modifier-click-contract.md` remain authoritative.

## Non-negotiable behavior

- Shift+left-click and Ctrl+left-click support both short clicks and drags. PowerPoint Ctrl-drag copy is a required case.
- Generated order is `modifier Down -> left Down -> left Up -> modifier Up`.
- Modifier Start/End, native right-drag output, and taskbar short-click replay run on dedicated workers. Never inject input synchronously from a low-level hook callback or while it owns `InputEngine.stateLock`.
- Physical movement stays suppressed until `NotifyNativeMouseDragStarted` confirms modifier Down plus synthetic left Down.
- Raw Input tracks physical buttons independently and recovers a missing low-level Up.
- Normal-layer MouseLeft remains protected. Taskbar long-press mapping takes priority over a normal mouse layer.
- Space, CapsLock, mouse layers, Deck close, single-instance ownership, and graceful installer shutdown are regression-protected.
- Deck buttons remain 54x52. The visible horizontal button gap equals the vertical button-to-button distance, including the name-label area. Names must remain visible.

## Change map

| Concern | Start here | Required regression evidence |
| --- | --- | --- |
| Hook state, Raw Input, modifier drag | `InputEngine.cs`, `InputEngine.RawInput.cs`, `InputEngine.Interop.cs` | self/UI/startup/shutdown plus documented modifier assertions |
| Mapping choice and worker queues | `MainWindow.xaml.cs` | self and UI tests |
| Deck geometry and names | `DeckPanelLayout.cs`, `MainWindow.Deck.cs`, `DeckPanelOverlayWindow.cs` | Deck geometry assertions in `UiIntegrationTest.cs` |
| One instance and stale-process recovery | `App.xaml.cs`, `StartupService.cs`, `IpcTransport.cs` | startup and shutdown tests |
| Installer/updater | `build-production.ps1`, `build-installer.ps1`, `installer.iss` | full installer gate, hashes, product version, Defender scan |

## Hot-path rules

- A hook callback may do bounded dictionary/list lookups and small user32 queries. It must not enumerate windows, wait on a task, perform file/process I/O, call a dispatcher synchronously, or call `SendInput`.
- Capture the selected mapping once in `activeInputMappings`; all later gesture/long-press/drag decisions for that physical press reuse the snapshot.
- Query taskbar state only when a matching Taskbar mapping exists. Query foreground process only when a candidate has an application condition.
- Mouse-move profile checks are coalesced. When automatic routing is disabled and already settled, do not queue a dispatcher operation.

## Shutdown and process ownership

- Production has one elevated resident input owner protected by `RELYR.SingleInstance.v2`.
- Tray Exit, emergency stop, updates, and uninstall signal the path-specific shutdown event. The independent process watchdog is the final fallback for a blocked UI dispatcher.
- Recovery may terminate only a process whose executable metadata identifies it as RELYR. Resolve its path with `PROCESS_QUERY_LIMITED_INFORMATION`; do not depend on `Process.MainModule`.
- Never kill child applications launched by mappings and never use a global image-name `taskkill` in the installer.

## Safe validation sequence

1. Run `powershell -ExecutionPolicy Bypass -File .\verify-source-safety.ps1`.
2. Build Release with warnings as errors into repository-root `.verification`.
3. Run `--self-test`, `--ui-test`, `--startup-test`, and `--shutdown-test` from the isolated build.
4. Do not run `--engine-test`, `--engine-test-no-real`, or `ModifierClickScenarioTest` in an active Windows session.
5. Only when a newer installer was explicitly requested: bump the project version, build in isolated staging, verify version and SHA-256, scan with Defender, then atomically replace the four files in `artifacts/production`.

`build-production.ps1` and `build-installer.ps1` skip input-engine tests by default and run the static safety gate. Do not turn an input-injecting test into a default build step.
