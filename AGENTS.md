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

# Installer Gate

- Before creating an installer, confirm the current source version from `RELYR/RELYR.csproj`.
- Use only the existing compiler at `C:\Users\freecar\AppData\Local\Programs\Inno Setup 6\ISCC.exe`; do not download, install, update, or uninstall Inno Setup.
- Confirm that the output directory is `artifacts/production` and that it contains only the current full setup, lightweight update installer, and their matching `.sha256` files after generation.
- After compilation, confirm that the generated Setup executable's product version matches the source version and that its SHA-256 matches the accompanying checksum file.
