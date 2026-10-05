## Deferred findings (live-validation required)

## Current reading guide — 2026-10-04

This is a historical deferral register, not a current total of unresolved bugs.
The recorded F202/F203 live results remain valid for their scenario; they do not
close F71, lifecycle, pointer or astronomy acceptance. Current scope checks are in
`PLAN_Phase5.md` and `RESOLVED_LEDGER.md` §7; no new complete 194-file audit was run.

- F02/F03 have real extractions (`SignalDecoder`, `StationManager`,
  `UIElementFactory`, `PanelState`), but not every proposed interface/slice.
- F04/G1–G4 lives in `StoryDecisionGateway`; F21/F179 bootstrap responsibilities
  moved out of the 24-line `Main` into bootstrap/patch/content helpers.
- F25 still exposes parallel raw arrays; removing a scheduler parameter was a
  partial change, not the immutable-snapshot design.
- F83 hot-reload is not a reliable runtime deployment path. Use game compilation
  of newer Source plus restart; repeated initialization/decision dedup requires
  its own live evidence.
- F31/F181 diagnostic policy is now PlayerPrefs-gated and cached (with release
  structural gates); `#if DEBUG` alone cannot enable logs in the game's compiler.
  Remaining unconditional diagnostics are not re-audited here as a new count.

The legacy radio `PositionFixCalculator` was removed; the current **sextant**
running/three-sight fix is a different chain. Do not read the removal below as
removal of today's FIX/LOP features. Gameplay items and evidence rules:
`LIVE_VALIDATION_CHECKLIST.md`, `MAP_MARKERS_PLAN.md`, `docs/live-diagnostics.md`.

## Historical notes (2026-08-04, later additions dated separately)

These findings required validation on the live in-game panel at that stage:

> **2026-08-24:** полная сверка решённости всех гипотез — в `RESOLVED_LEDGER.md`. Открытыми остаются 22; записи ниже сохраняют историческую формулировку.
>
> **2026-09-26:** «22» — размер снимка от 2026-08-24, а не текущий статус. Частичный
> пересчёт по коду закрыл 4 из них (F70, F105, F112, F114) и уточнил F125;
> доказательства — в `RESOLVED_LEDGER.md` § 5. Остальные в этом проходе не
> перепроверялись.

Position Fix chain (`PositionFixCalculator`, measurement aggregation, and panel
output) was removed on 2026-08-08. Historical position-fix findings remain in
the audit archive only.

- **F202** SpinnerHandlerBase 90 deg angle offset — ✅ live-validated by checklist; no strange knob behavior observed, no code change required.
- **F203** Receiver default QF=20 causes cross-talk of neighbouring stations at 59-99% loudness (307/306, 316/315, 266/257 kHz) — ✅ live-validated by checklist; useful Morse was audible without cross-talk, no tuning change required.
- **F163** mutable struct (F17-adjacent), **F179/F180/F181** architectural refactors (Main.cs SRP split, typed references, log levels), **F186** RadioStoryIntegration — *ядро G1-G4 закрыто 2026-08-23 через `StoryDecisionGateway.cs` (commit 28f4a62); в `rejected\F186_*.md` отвергнут только param-bounds подпункт — это разные объёмы одного номера.*

## Deferred findings (architectural / data / docs, no code change, 2026-08-02)

Resolved without code edits — each entry lists the reason. *Построчные доказательства закрытия смежных гипотез: `RESOLVED_LEDGER.md`.*

- **F02** SonneController god-object (9 responsibilities) — architectural refactor, SAD 08 roadmap.
- **F03** InformationPanelHandler god-object (474 LOC, 9 responsibilities) — architectural refactor, SAD 08 roadmap.
- **F04** RadioStoryIntegration god-object + reflection fragility — dead chain G1-G4, see `refactoring-plan/game-source-findings/`.
- **F08** GC pressure (per-frame heap allocations) — not a hot path; call sites already allocate only on 0.2 s station updates; no per-frame `new` remains.
- **F10** 14 hardcoded HintPath + Steam-path dependency in csproj — **✅ реализовано (2026-08-02)**: shared `GamePath`/`GameManaged` MSBuild properties with `UBOAT_GAME_PATH` override.
- **F12** `ToRadians` duplicated — **✅ реализовано (2026-08-02)** через общий `GeoMath`.
- **F15** No state persistence (volume/band/radio position reset) — **✅ реализовано (2026-08-02, feat(F15) в refactor/deferred-code)**: `PanelStateStore` (PlayerPrefs) сохраняет volume/feedback/zone/cursor, восстанавливает в `InformationPanelHandler.Start`.
- **F20** DialConfig band unit inconsistencies (Band I MHz vs kHz display) — display-string only; band math is consistent (Bands table).
- **F21** Main.cs responsibilities — architectural refactor, SAD 08 roadmap.
- **F25** AudioSynthesisEngine positional array coupling — architectural API design, SAD 08 roadmap.
- **F27** ComparisonExtensions anti-pattern (~93 call sites) — style-only; null-safety helpers used consistently (F74 notes deliberate asymmetry).
- **F28** ~40 magic numbers — **✅ частично реализовано (2026-08-02)**: semantic BFO, Q-factor, station-match and cursor constants moved to `Constants` (ADR-005); remaining local geometry literals stay local.
- **F31** 93 Debug.Log calls — remaining logs are intentional (errors/warnings); diagnostic block now #if DEBUG (F57/F93/F88).
- **F32** 5 GetComponentInChildren in IPH.Start — one-time init cost, not hot path.
- **F37** 6 stations with null callsign — historical data is correct (real Consol stations had no callsigns); runtime handles empty callsigns (F87).
- **F39** 3 stations at 332.5 kHz — historical frequencies; co-channel behaviour is the point of Consol navigation (QSB separation).
- **F52** SAD Appendix B missing classes — documentation task, SAD update.
- **F62** StationState/NavigationPattern struct aliasing — patterns are replaced wholesale, never mutated through copies; theoretical risk only.
- **F63** DebugAngleHandler TMP dependency — debug-only helper; stripping risk accepted, SAD 08 note.
- **F70** NavigationPattern.FormatCompact transition misrepresentation — cosmetic string only; IPH reads pattern length, not this string.
- **F71** 2-3 Random.NextDouble() per sample in audio thread — time-scale mute/pause behavior is implemented in `SonneController`; noise/per-sample RNG redesign remains deferred because it changes sound and needs live listening.
- **F83** RadioStoryIntegration zombie decisions on hot-reload — dead chain G1-G4, see `refactoring-plan/game-source-findings/`.
- **F173** InformationPanelHandler.CreateLabel OS font name ("Arial") — **✅ реализовано (2026-08-02)**: fallback to Unity's `LegacyRuntime.ttf` with warning.

## Verified fixed without a new edit (code already satisfies, 2026-08-02)

- **F24** RemoveRange mutation bug — fixed by F120.
- **F30/F56** RadioHandler unsubscribes `zoneHandler.OnZoneChanged` in OnDestroy.
- **F33** Volume/FeedbackSpinnerHandler already merged into SpinnerHandlerBase.
- **F41** double/float phase mismatch — phase normalized to [0,1) by F169.
- **F55/F86** GetForCycleDuration stub — resolved for the historical CAP set: 40 s and 60 s profiles are selected; full profile-driven T/D/R cycle remains a separate rewrite step.
- **F67** IPH `GetComponent<Toggle>()` NRE — RefreshAll uses cached `cachedToggle` with null guard.
- **F69** Main.cs swallow — LogError + LogException both present; swallow is deliberate (mod must not crash the game).
- **F74** ComparisonExtensions threshold asymmetry — deliberate; comment added in F74-commit.
- **F76** GetElementCount int overflow — method deleted (F193).
- **F82** Main.cs exception swallow in panel construction — deliberate + LogException (see F69).
- **F11** keyer lambda subscriptions — SonneController stores handlers and unsubscribes in OnDestroy (keyerElementHandlers/keyerSequenceHandlers arrays).
- **F14** event subscription leaks — all UI handlers balance subscribe/unsubscribe (IPH 6/6, RadioHandler, ToggleHandler, DebugAngleHandler); ZoneHandler uses Unity EventTrigger owned by the GameObject, no leak.
- **F167** StationScheduler.OnSequenceFinished — Callsign→PreNavPause and Navigation→PostNavSilence are handled; Pause/PreNavPause/PostNavSilence hit the guarded default branch (log + ignore), which is correct — no sequence is active in those phases.
