# Get started with RELYR in three minutes

[English](getting-started.md) | [日本語](getting-started.ja.md)

This guide creates one simple Space layer action without changing normal typing.

## 1. Prepare

- Use Windows 10 or 11 x64.
- Temporarily disable AutoHotkey, PowerToys Keyboard Manager, and manufacturer-specific remapping tools.
- Remember the emergency stop shortcut: `Ctrl + Alt + Shift + F12`.
- Download `RELYR-Setup-<version>.exe` only from the [official latest release](https://github.com/zitan-source/RELYR/releases/latest).
- Check that the installer has a matching `.sha256` file. The public-beta installer is not code-signed, so Windows may display an unknown-publisher warning. If you cannot confirm the source and checksum, do not continue.

## 2. Install and open RELYR

Run the Setup installer, review and accept the displayed terms, and select the interface language. After installation, open RELYR from the Start menu.

RELYR may create an elevated startup task so its assignments can work with administrator-level windows. This avoids showing a UAC prompt at every launch.

## 3. Create your first layer action

1. Select the **Space** layer in the left column.
2. Select an unused key in the keyboard workspace.
3. Choose an Action in the right panel, such as launching Notepad, and assign or drag it to the selected key.
4. Save the configuration.

Start with one harmless action. Avoid assigning shutdown, deletion, scripts, or long macros until the basic layer works as expected.

## 4. Try it

1. Press and release Space normally. It should still type a space.
2. Hold Space and press the key you assigned.
3. Confirm that the assigned Action runs once.

If an action runs twice or a key appears stuck, press the emergency stop shortcut and disable other remapping software before trying again.

## 5. Change or remove the action

Return to the Space layer, select the assigned key, and edit or remove its Action. Save again after making the change.

CapsLock-layer changes are different: enabling that layer changes a Windows key mapping and requires a restart. RELYR settings and the uninstaller can restore normal CapsLock behavior.

## 6. Update and report problems

Installed copies can obtain the smaller update installer through RELYR. Normal updates do not show the initial terms again after they have been accepted.

For reproducible problems, use the [bug report form](https://github.com/zitan-source/RELYR/issues/new?template=bug_report.yml). Remove passwords, private macro content, and personal information before attaching logs or screenshots. See [Support](../SUPPORT.md) for the complete reporting checklist.
