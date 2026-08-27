# 004 - Reveal Action drops from the target center

- **Status**: DONE
- **Commit**: dfab4e0
- **Severity**: HIGH
- **Category**: Feedback, physicality
- **Estimated scope**: 3 files, about 100 lines

## Problem

`RELYR/MainWindow.ActionPalette.cs:1084` attempts a radial drop-success wave, but it
derives the wave color from the button's newly assigned background. The overlay therefore
often paints nearly the same color over itself and is visually imperceptible. It also runs
for more than 500 ms, outside the sub-300 ms UI budget.

## Target

- On a successful Action or monitor drop, an accent-colored radial highlight starts at the
  exact target center, expands to the button edges, and disappears within 280 ms.
- Use a brighter accent center and the theme accent at the middle stop so it is legible over
  every assignment color in light and dark themes.
- Clip the existing non-hit-testable tint layer to the target face; add no top-level surface.
- Retarget repeated drops safely and restore opacity, transform, and button scale exactly.
- With Animation off, remove every wave/scale clock and leave the settled assigned button
  immediately, with no visible tint.

## Boundaries

- Do not change the drop payload, mapping mutation, undo state, save timing, selection,
  target hit area, or DragDrop event handling.
- Animate only scale and opacity, and keep the final tint opacity at zero.

## Verification

- Drop Actions on a normal key, JIS Enter, mouse button, Deck button, and a multi-selection
  in both themes; every target must show the same center-out accent wave.
- Repeat rapidly, then turn Animation off and repeat; no tint or scale may remain.
- Run the standard source guard, Release build, and safe regression set.
