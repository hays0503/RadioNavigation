# Game input semantics

Verified against the decompiled game source `C:\code\projects\uboat\UBOAT.Game.Core.Input\InputAxis.cs` on 2026-10-06. This records the active `InputAxis` path only; it is not a claim that every game input uses the same implementation.

## `InputAxis` state and edges

- `Value` is a sampled state. It reads configured axes and keyboard keys using `UnityEngine.Input.GetAxis` / `UnityEngine.Input.GetKey` and returns `-1`, `0`, or `1` for key bindings.
- The first `Value` read in each `Time.frameCount` stores the previous sample, updates the frame stamp, and samples the current state. Later reads in that frame return the cached value.
- `Pressed` is the positive-state predicate (`Value > 0.5`). `JustPressed` is derived from the state transition (`Value >= 0.5 && previousValue < 0.5`); `JustReleased` uses the inverse threshold transition.
- Thus this input abstraction derives edges from its own held-state samples, rather than treating a raw `GetKeyDown` call as its entire input state. Do not generalize this to unrelated hard-coded game input without checking its consumer.

## Native consumers and modal ownership

All game-source paths below are relative to `C:\code\projects\uboat`.

- `UBOAT.Game.Scene.Tasks/KeyboardShortcuts.cs:216-245`: journal toggling checks `HasActiveBlockadesExcept(journalUI)` and text focus; most later shortcuts require `gameUI.Interactivity` and no active input blockade. Map/binocular actions also check camera transitions (`:333`, `:375`).
- `UBOAT.Game.UI/ShipControlUI.cs:950-952`: the native chronometer shortcut uses `InputAxis.JustPressed` **and** `!gameInput.HasActiveBlockades()` before toggling. The edge predicate is not the full permission policy.
- `UBOAT.Game.Core.Input/GameInput.cs:106-169`: blockades are owner references. `AddBlockade` is idempotent; `RemoveBlockade` removes the same owner after a one-frame tween, not synchronously. Neither resets Unity key state. `HasActiveBlockades()` also checks the selected focused `TMP_InputField`; the `Except` overload checks only owner references, so using it alone does not guard text input.
- `UBOAT.Game.Core.Input/GameInput.cs:180-197`: native Cancel dispatch has a separate `escapeConsumedFrame` guard. `InputAxis.JustPressed` itself is **not** a consumable event: two reads in the press-start frame both return true.
- `UBOAT.Game.UI/JournalUI.cs:119-126` and `EscapeMenu.cs:156-186`: modal cleanup also runs in `OnDisable`. Explicit `Close`/destruction is not the only lifetime boundary.
- `UBOAT.Game.UI/EscapeMenu.cs:144-152`: opening the menu disables its active sibling UI objects. Runtime read-only enumeration on 2026-10-06 placed the menu and RadioNavigationPanel under `/GUI/Viewport`; the sextant overlay itself has no transform parent. A controller on that panel needs its own disable cleanup; its separate canvas does not inherit panel deactivation.
- `UBOAT.Game.Scene.Camera.ExternalControllers/PeriscopeController.cs:465-480`: the developers also use raw `GetKeyDown`/`GetKeyUp` for a hard-coded Ctrl camera action, with explicit `zooming` state guards. Raw `GetKeyDown` is not intrinsically a bug or contrary to every native input path.

## Mod implementation note

Unity 2021.3 documents `Input.GetKeyDown` as true during the frame the user starts pressing the key: https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Input.GetKeyDown.html. The RadioNavigation sextant symptom was not proven to be a Unity `GetKeyDown` repeat: the game log does not record hardware key transitions. The defensive `HeldKeyEdge` policy instead samples `Input.GetKey` continuously, synchronizes on component enable, and gates actions separately from edge sampling. Live Z acceptance passed in six bounded FPP/paused scenarios; that is not proof of the old root cause or full visual/lifecycle acceptance. See `docs/plans/KEY_EDGE_PLAN.md` §14.1.

Source audit (§14.2) found remaining problems in both the original controller and the current mitigation: no foreign-blockade check, no `OnDisable` cleanup, and shared `static IsOpen` without exclusive instance ownership. An offline source-slice model reproduced their state inconsistencies, **not** the historical physical-hold episode. The original `Update` under documented single-update/frame semantics also produces one action for a continuous hold. Native `InputAxis` alone would not solve duplicate owners because its edge remains true for all reads in the same frame.

Do not test `I` on a working campaign save: it commits an observation. Runtime validation of that binding requires a disposable save and explicit approval.
