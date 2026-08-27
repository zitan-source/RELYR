# 002 - Keep layer editing direct and stable

- **Status**: DONE
- **Severity**: HIGH
- **Category**: Restraint, stability, pointer predictability

## Revised decision

The first implementation added scale feedback to assignment controls and moved/faded the
workspace during every left-navigation layer switch. Real use showed that both interactions
are too frequent for spatial motion: fast pointer travel could produce visible jitter, and
repeated layer changes made the entire editor feel unsettled.

## Final behavior

- Keyboard, mouse, and Deck assignment controls keep fixed hit-test geometry on hover.
- Hover feedback is limited to the existing template-local color, border, and opacity state.
- Layer navigation updates the selected card and editor workspace immediately, without
  workspace translation, scaling, fading, or icon overshoot.
- The Action-drop center wave and assignment-editor reveal remain because they acknowledge
  discrete, consequential actions rather than high-frequency navigation.
- Turning animations off still settles every optional motion clock synchronously.

## Boundaries

- Do not animate button bounds, hit areas, focus, selection, persistence, or input routing.
- Do not add motion to physical layer activation or any input-hook path.
- Keep assignment drag/drop and protected normal-layer left click behavior unchanged.

## Verification

- Raise 100 rapid enter/leave cycles across adjacent keyboard, mouse, and Deck controls;
  assert unchanged dimensions, no scale clocks, and no z-order lift.
- Switch layers with animation both on and off; assert the workspace is immediately at its
  final opacity and transform.
- Run the safe source guard and warning-free self/UI/startup/shutdown Release tests.
