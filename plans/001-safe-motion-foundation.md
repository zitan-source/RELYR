# 001 - Centralize safe optional motion

- **Status**: DONE
- **Commit**: dfab4e0
- **Severity**: HIGH
- **Category**: Accessibility, performance, cohesion
- **Estimated scope**: 3 files, about 140 lines

## Problem

Optional WPF motion is currently implemented ad hoc. `RELYR/UiMotionService.cs:6`
contains only the global switch, exception containment, and mutable transform helpers.
Each caller owns clock cleanup, easing, and disabled-state finalization. That makes it
easy for an animation-off path to retain a clock or for a frozen template transform to
throw. The stability contract requires visual motion to fail independently from input,
Deck synchronization, persistence, and hit testing.

```csharp
// RELYR/UiMotionService.cs:8 - current
internal static bool Enabled { get; private set; } = true;

internal static void Apply(bool enabled)
    => Enabled = enabled;
```

## Target

Add reusable WPF-native helpers that:

- animate only `Opacity`, `TranslateTransform`, and `ScaleTransform`;
- clone frozen transforms before use;
- remove the prior clock with `BeginAnimation(property, null)`, preserve the live value,
  and use `HandoffBehavior.SnapshotAndReplace`;
- use strong non-overshooting ease-out motion equivalent to the React Bits/Apple motion
  guidance (`PowerEase`, `Power=4`, `EaseOut`);
- apply the exact final state synchronously when `UiMotionService.Enabled` is false;
- keep every entry point inside `RunSafely` and disable motion only for the process on an
  animation-only failure;
- never alter the saved animation preference, input engine, Deck geometry, or hit tests.

## Repo conventions to follow

- Preserve `UiMotionService.MutableScale` and `MutableTranslate`; they already clone frozen
  template transforms.
- Preserve `App.xaml.cs:49`, which handles only animation-specific dispatcher exceptions.
- Preserve the no-input-test policy in `docs/stability-contract.md`.

## Steps

1. Extend `RELYR/UiMotionService.cs` with shared easing, clock removal, final-state, and
   transform-group helpers. Do not add a timer or rendering-loop subscription.
2. Add test-only accessors in the owning UI classes only where required to prove final
   state and active clocks.
3. Add `RELYR/UiIntegrationTest.cs` assertions for frozen transforms, animation-off
   immediate final states, and repeated retargeting without accumulating clocks.

## Boundaries

- Do not add React, GSAP, Motion, npm, WebView, or another dependency.
- Do not touch input hooks, mapping routing, persistence formats, or installer files.
- Do not create a top-level animation surface.

## Verification

- **Mechanical**: run `verify-source-safety.ps1`, a warning-free Release build, then
  `--self-test`, `--ui-test`, `--startup-test`, and `--shutdown-test` from `.verification`.
- **Feel check**: toggle Settings > Animation off while the app is open. New presses,
  layer changes, and Deck presentation changes must jump directly to their final visual
  state, with no delayed completion.
- **Done when**: optional animation can fail or be disabled without changing input,
  Deck state, persistence, process lifetime, or any hit-test region.
