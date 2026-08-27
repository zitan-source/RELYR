# 003 - Give Deck a restrained arrival and scale-fade departure

- **Status**: DONE
- **Severity**: HIGH
- **Category**: Spatial consistency, restraint, safety

## Revised decision

The initial departure combined opacity, scale, and downward translation before hiding the
window. Removing departure motion completely fixed that over-animation but introduced a
jarring discontinuity, while a pure fade still lacked physical continuity. The restrained
solution is a very small centered scale reduction paired with a short fade.

## Final behavior

- Showing Deck preserves the bounded content-only arrival animation.
- Hiding Deck with animations on eases opacity over 155 ms while the content scales from its
  live value toward 0.975 over 135 ms. There is no translation, bounce, blur, or per-cell
  animation.
- The fade freezes any in-flight content arrival at its live value, releases Deck-owned
  mouse capture, disables content interaction, persists geometry, and cleans previews first.
- Reopening during the fade cancels its watchdog and stale completion, then retargets the
  existing arrival from the live opacity.
- Animation off and unconditional teardown remain immediate.
- Collapse/expand continue to apply complete native bounds synchronously; their bounded
  in-window state reveal remains independent from show/hide.
- Deck button hover uses stable template-local feedback and never scales the button or its
  content surface.

## Boundaries

- Preserve `AllowsTransparency=false`, native rounded bounds, no-activate behavior, and
  independent state per Deck.
- Never animate `Width`, `Height`, `Left`, or `Top`. Departure uses only opacity and the
  centered content transform. The fade stops above zero and is bounded by a watchdog so it
  cannot leave an invisible native hit-test surface or stale callback.
- Keep shutdown and failure cleanup immediate.

## Verification

- Show Deck and assert the arrival settles without clocks or offsets.
- Hide while Deck owns mouse capture and assert a centered scale-and-fade starts with capture
  and interaction cleared synchronously and with translation remaining exactly zero.
- Interrupt one fade with a show and assert its old completion cannot hide the reopened Deck.
- Let another fade complete and assert no visible, captured, or transparent surface remains.
- Raise 100 rapid hover cycles on a Deck overlay button and assert unchanged dimensions and
  no transform clock.
- Run the safe source guard and warning-free self/UI/startup/shutdown Release tests.
