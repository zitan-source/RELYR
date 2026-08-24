# Action palette specification

This document records the agreed editor workflow for the next major RELYR change. Do not implement it by altering the runtime input engine or by changing the established v0.1.323 layer behavior.

## Preserved baseline

- The verified restoration point for this redesign is commit `eca5660`, tag `v0.1.325`, branch `stable/v0.1.325`, and the retained `RELYR-Update-0.1.325.exe` with its matching SHA-256 sidecar.
- Major-change work starts from `next/major-redesign`.
- The existing select-a-key-then-edit workflow remains available.
- Standard, Space, CapsLock, MouseRight, MouseBack, MouseForward, Taskbar, profile routing, and Deck runtime behavior must remain unchanged.

## Right-pane action palette

- A fixed-position control in the right pane opens the action palette.
- Opening the palette replaces the hint content; it does not permanently widen the right pane.
- Use a compact vertically scrollable list, approximately 44–48 px per row, with an icon and concise single-line action name.
- Keep search and category selection at the top.
- Categories that require a concrete choice, including macros, gestures, profiles, and Deck panels, drill into the same pane instead of opening another side-by-side column.
- Avoid explanatory cards, repeated descriptions, excessive borders, and wrapped labels.
- At constrained widths, use an in-window responsive drawer only. Never create another transparent top-level native window for this UI.
- Keep the palette open for consecutive assignments unless the user explicitly closes it.
- One click on blank editor space closes the palette. Clicking or dragging a key, Deck slot, search field, or other interactive control does not count as a blank click.

## Information architecture

- The palette lists concrete, ready-to-run actions. The nine existing entries such as `別のキー`, `文字列`, `マクロ`, and `Deckパネル` are configuration methods, not accordion categories, and must not be expanded into a deep nested accordion.
- The ready-to-run library combines the built-in `ActionCatalog` with configured profiles, macros, gestures, Deck layouts, and custom actions already used in mappings. It remains a view of the existing models rather than a new persistence store.
- Search covers action name, category, description, and execution value. Category filtering uses one compact control and does not add a second side-by-side column.
- Used actions sort ahead of unused catalog actions so the narrow pane remains useful without forcing a separate recent-items screen.
- Custom values that do not yet exist are still created through the existing select-a-key-then-edit workflow. The palette provides a direct return to that editor instead of pretending an unfinished value is draggable.
- The old large input-detection control is replaced by the action-library launcher at the exact same empty-state position. Do not add a redundant input-detection or drag-instruction footer to the action library.

## Assignment behavior

- An action can be dragged from the palette onto a main-keyboard key or Deck button.
- Dropping onto an already assigned target replaces its action immediately.
- After replacement, show a temporary `元に戻す` command for five seconds that restores the complete previous assignment.
- Dropping onto one of multiple selected targets applies the action to every selected target as one undoable operation.
- Dropping outside a valid target or pressing Escape changes nothing.
- Invalid or protected keys remain unavailable and visually subdued.
- The drag-and-drop route invokes the same assignment command and persistence path as the existing editor; it must not introduce a parallel mapping model.

## Drag feedback

- Reuse the compact visual language of the current Deck assignment drag without reducing the payload to an ambiguous icon.
- A reduced whole-action row follows the pointer: icon, concise name, category, and drag handle remain visible at about 82% of the source-row width, clamped to 172–220 px and 42 px high.
- The drag preview remains offset from the pointer and compact enough that it does not hide the destination.
- Valid targets receive an unmistakable drop marker without dirtying their original face color.
- The preview disappears on drop, cancellation, lost capture, window close, or failure.

## Motion language

- Motion is cyber-inspired but restrained: RELYR accent colors, short fades, a few pixels of glide, and gentle spring easing appear only in direct response to user input.
- Opening and closing the action library uses opacity plus a 5–6 px vertical glide. Action rows move only 2 px on hover, so layout and text alignment remain stable.
- A successful Action drop reuses the destination key's existing non-hit-test overlay. Color radiates from the center while the key performs one small spring response; no new top-level surface or input layer is created.
- The five-second undo bar enters and exits with a short fade and vertical glide.
- Shared app buttons reveal only a thin accent signal on hover, and switch thumbs use a small hover response; neither animation covers or fades the control label.
- All motion derives colors from dynamic theme resources. Text and icons retain their normal foreground resources in both light and dark themes and are never faded together with the drop-color layer.
- RELYR's own Animation setting controls code-driven transitions independently of Windows. New configurations default to on; off uses immediate, stable visual states without changing labels, colors, assignment behavior, or hit testing.

## Main keyboard versus Deck appearance

- Main keyboard: the action icon is shown only during drag. After drop, keep the physical key label and use the existing action-color behavior; do not place the icon permanently on the key.
- Deck: after drop, apply both the action and its palette icon to the Deck button face.
- Except for the explicitly applied Deck icon, do not silently change a Deck button's name or other user-customized appearance.
- With multiple selection, main-keyboard targets receive the assignment and existing color treatment; Deck targets receive the assignment and the same action icon.

## Work order

- Do not begin this feature while a newly reported regression is unresolved.
- Diagnose and fix the current regression first, preserve existing behavior, validate it, and create a new recovery point before starting this major change.
