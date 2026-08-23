# Action palette specification

This document records the agreed editor workflow for the next major RELYR change. Do not implement it by altering the runtime input engine or by changing the established v0.1.323 layer behavior.

## Preserved baseline

- The verified restoration point is commit `697a2ce`, tag `v0.1.323`, and branch `stable/v0.1.323`.
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

## Assignment behavior

- An action can be dragged from the palette onto a main-keyboard key or Deck button.
- Dropping onto an already assigned target replaces its action immediately.
- After replacement, show a temporary `元に戻す` command that restores the complete previous assignment.
- Dropping onto one of multiple selected targets applies the action to every selected target as one undoable operation.
- Dropping outside a valid target or pressing Escape changes nothing.
- Invalid or protected keys remain unavailable and visually subdued.
- The drag-and-drop route invokes the same assignment command and persistence path as the existing editor; it must not introduce a parallel mapping model.

## Drag feedback

- Reuse the compact visual language of the current Deck assignment drag.
- A small action icon, approximately 20 × 20 px (about one quarter of a key face), follows the pointer with a slight offset.
- Never drag a full action row, large card, or full-size key image that hides the destination.
- Valid targets receive an unmistakable drop marker without dirtying their original face color.
- The preview disappears on drop, cancellation, lost capture, window close, or failure.

## Main keyboard versus Deck appearance

- Main keyboard: the action icon is shown only during drag. After drop, keep the physical key label and use the existing action-color behavior; do not place the icon permanently on the key.
- Deck: after drop, apply both the action and its palette icon to the Deck button face.
- Except for the explicitly applied Deck icon, do not silently change a Deck button's name or other user-customized appearance.
- With multiple selection, main-keyboard targets receive the assignment and existing color treatment; Deck targets receive the assignment and the same action icon.

## Work order

- Do not begin this feature while a newly reported regression is unresolved.
- Diagnose and fix the current regression first, preserve existing behavior, validate it, and create a new recovery point before starting this major change.
