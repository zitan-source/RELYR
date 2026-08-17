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

## Regression prevention contract

Every change must preserve already-working behavior. A feature request does not authorize redesigning an unrelated subsystem or changing an existing specification as a side effect.

- Change one execution path at a time. An input fix must not alter Deck state, a Deck fix must not alter hooks or runtime integrity, and a visual change must not alter profile routing unless the request explicitly requires it.
- Treat a reported failure as a regression first. Identify the last known-good artifact/source and the first bad change before adding a new recovery mechanism or special case.
- Prefer reverting the change that introduced the regression. Do not add foreground-app exceptions, own-process exceptions, fallback input paths, or action-specific workarounds unless the specification explicitly requires them.
- The visible RELYR main window is an ordinary application surface. It must run at ordinary user integrity and accept the same normal-profile keyboard and mouse layer input as Notepad, Word, Chrome, Filmora, and other applications. Only privileged operations use the elevated helper.
- RELYR foreground state, focus, text fields, Deck editing mode, and the selected layer editor page must not disable standard, Space, CapsLock, MouseRight, MouseBack, or MouseForward layer processing.
- Automatic profile routing follows only the foreground application. Moving the pointer over an inactive application must not change the runtime profile. RELYR's main window and owned management dialogs are ordinary foreground application surfaces, resolving to a specifically assigned RELYR profile or otherwise the standard profile.
- A close action targeting the window under the cursor requests activation and immediately posts `SC_CLOSE` to the same already-resolved target without waiting for foreground acknowledgement. An inactive target must receive the close request on the first invocation, and the action must never be discarded or fall back to another window.
- Key and shortcut actions targeting the window under the cursor activate that resolved window immediately before `SendInput`. This includes keyboard and wheel zoom shortcuts. Activation failure must never fall back to the previously active window.
- A virtual-desktop action queued from a low-level callback must not begin until that originating callback has returned. The output worker waits on the callback-return barrier; the hook thread never waits on the desktop worker. This preserves the matching mouse-button Up and prevents a layer from retaining normal clicks across a desktop change.
- The Deck editor and the live Deck overlay use the same `DeckLayoutDefinition` objects. Runtime-profile selection may use a snapshot, but `DeckLayouts` and `SharedDeckMappings` must not be deep-cloned or allowed to diverge.
- A file, name, icon, color, assignment, reorder, deletion, row/column change, or profile-linked Deck change made in either the editor or overlay must appear in the other immediately and persist across restart.
- Do not replace the retained known-good installer until the candidate passes the required gates and the user has approved replacement. Record version and SHA-256 for every candidate supplied for user verification.
- Diagnostic and release builds are distinct. A diagnostic build must write logs to a user-writable path. User-environment confirmation of a diagnostic build is not, by itself, permission to call it a final release.
- Internal callbacks and mocked output prove only state-machine intent. They do not prove physical hook delivery, process-integrity routing, visible overlay behavior, persistence, or cross-process synchronization.

Before reporting completion, explicitly review this permanent regression set:

1. RELYR main window: standard, Space, CapsLock, MouseRight, MouseBack, and MouseForward layers remain active.
2. Ordinary applications: the same layers retain their existing behavior.
3. Normal left click is never assigned, replaced, or captured; ordinary right/back/forward clicks still replay correctly.
4. Profile auto-switching follows the foreground application only, remains stable, and does not change the editor dropdown unless the user selects it. Opening an owned RELYR management dialog must not preserve an unrelated application's runtime profile, and the dialog's title-bar close command remains usable.
5. Deck editor and live overlay synchronize in both directions without restart, including profile-linked layouts with different dimensions.
6. Tray restart preserves input ownership, profile state, mappings, and Deck synchronization.
7. Repeated actions and virtual-desktop changes do not leave captured keys/buttons, double-execute actions, or stop the hooks.
8. Settings, mapping `Application` conditions, profiles, and Deck layouts are not erased, globally broadened, or silently migrated.

If a required item cannot be exercised safely in the current session, say so. Do not substitute an unrelated mocked test or claim that the item was verified.

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
