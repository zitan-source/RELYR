# Repository Scope

- Work only inside this `RELYR` repository.
- Work only inside `C:\Users\freecar\Documents\RELYR`.
- The only exception is read/execute access to the existing Inno Setup compiler at `C:\Users\freecar\AppData\Local\Programs\Inno Setup 6\ISCC.exe`.
- Do not create, copy, build, test, or publish RELYR files in sibling directories.
- Treat `RELYR/RELYR.csproj` as the only application project.
- Use the repository-root `.verification` directory for disposable isolated validation output.
- Use `RELYR/artifacts/production` only for the current full setup, lightweight update installer, and their checksums.
- Keep the current `RELYR-Setup-<version>.exe` and `RELYR-Update-<version>.exe` with their matching `.sha256` files in `artifacts/production`.
- Replace them only when the user explicitly requests a newer installer and that installer has passed all required validation.
- After replacement, do not retain older installers or copied publish directories in the repository.

# Investigation Boundaries

- Exclude `artifacts`, `bin`, `obj`, `.verification`, and past `RELYR-Setup-*.exe` files from normal source-code investigation.
- Read those paths only when the task explicitly concerns building, installer verification, cleanup, or a specific retained diagnostic artifact.
- Keep the current installer and its matching `.sha256` file in `artifacts/production` as evidence until the user approves their replacement or deletion.

# Fast Stability Workflow

- Before changing input, Deck layout, startup, shutdown, or installer code, read `docs/stability-contract.md` and use its change map to keep investigation narrow.
- Before reporting any input, Deck, profile-routing, startup, shutdown, or installer change complete, re-read the `Regression prevention contract` in `docs/stability-contract.md` and explicitly check the permanent regression set. Do not treat mocked callbacks as proof of physical input, process-integrity routing, persistence, or cross-process Deck synchronization.
- Run `verify-source-safety.ps1` after those changes. It is a fast static guard, not a replacement for the required non-input tests.

# Installer Gate

- Before creating an installer, confirm the current source version from `RELYR/RELYR.csproj`.
- Use only the existing compiler at `C:\Users\freecar\AppData\Local\Programs\Inno Setup 6\ISCC.exe`; do not download, install, update, or uninstall Inno Setup.
- Confirm that the output directory is `artifacts/production` and that it contains only the current full setup, lightweight update installer, and their matching `.sha256` files after generation.
- After compilation, confirm that the generated Setup executable's product version matches the source version and that its SHA-256 matches the accompanying checksum file.

# Modifier-Click Input Contract

- Treat Shift+left-click and Ctrl+left-click as permanent, regression-protected behavior. They must support both a short modified click and a modified drag, including PowerPoint Ctrl-drag copy.
- Preserve this exact generated-input order: modifier Down, left-button Down, left-button Up, modifier Up.
- Queue modifier-click Start and End on the dedicated drag worker. Never call `SendInput` synchronously from the low-level input hook or while it owns the engine state lock.
- Suppress physical mouse movement until the worker has completed modifier Down plus synthetic left-button Down and has called `NotifyNativeMouseDragStarted`.
- On physical left-button Up, let the low-level hook return before the worker emits synthetic left-button Up followed by modifier Up. Do not add a user-visible release delay.
- Never discard a queued Start/End pair when the user performs a very short click.
- Recover a missing Up through Raw Input and the next physical Down, and track physical buttons independently from RELYR-generated buttons. Do not use `GetAsyncKeyState` to decide whether RELYR's own synthetic drag is still physically held.
- Keep normal-layer left click protected from assignment. Keep taskbar-specific mouse long-press mappings ahead of normal mouse-layer capture.
- Do not run `--engine-test`, `--engine-test-no-real`, or `ModifierClickScenarioTest` in an actively used Windows session. Production and installer builds must skip input-engine tests by default.
- Before replacing an installer after any input change, require a warning-free Release build plus passing self, UI, startup, and shutdown tests. Preserve the modifier-click regression assertions documented in `docs/modifier-click-contract.md`.
