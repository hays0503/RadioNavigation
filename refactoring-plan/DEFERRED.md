## Deferred findings (live-validation required)

These confirmed findings are intentionally NOT fixed in code — they require validation on the live in-game panel, so they were left out of the audit fix commits (2026-08-02):

- **F202** SpinnerHandlerBase 90 deg angle offset — knob art orientation lives in the binary AssetBundle (`Bundles/testui`), unknown from sources; both proposed variants need a live-panel check.
- **F203** Receiver default QF=20 causes cross-talk of neighbouring stations at 59-99% loudness (307/306, 316/315, 266/257 kHz) — selectivity tuning needs listening; default state is the least selective.
- **F163** mutable struct (F17-adjacent), **F179/F180/F181** architectural refactors (Main.cs SRP split, typed references, log levels), **F186** RadioStoryIntegration (dead chain G1-G4).

## Deferred findings (architectural / data / docs, no code change, 2026-08-02)

Resolved without code edits — each entry lists the reason:

- **F02** SonneController god-object (9 responsibilities) — architectural refactor, SAD 08 roadmap.
- **F03** InformationPanelHandler god-object (474 LOC, 9 responsibilities) — architectural refactor, SAD 08 roadmap.
- **F04** RadioStoryIntegration god-object + reflection fragility — dead chain G1-G4, see `refactoring-plan/game-source-findings/`.
- **F08** GC pressure (per-frame heap allocations) — not a hot path; call sites already allocate only on 0.2 s station updates; no per-frame `new` remains.
- **F10** 14 hardcoded HintPath + Steam-path dependency in csproj — dev-only build config; build.ps1 is the supported entry point; not game-facing.
- **F12** `ToRadians` duplicated (SonneController vs PositionFixCalculator) — 3-line copy-paste; dedup goes with F73 (Haversine) into a shared geo module, SAD 08 roadmap.
- **F15** No state persistence (volume/band/radio position reset) — feature request, not a defect; needs a save-system decision.
- **F18** PositionFixCalculator god object (224 LOC) — architectural refactor, SAD 08 roadmap.
- **F20** DialConfig band unit inconsistencies (Band I MHz vs kHz display) — display-string only; band math is consistent (Bands table).
- **F21** Main.cs responsibilities — architectural refactor, SAD 08 roadmap.
- **F25** AudioSynthesisEngine positional array coupling — architectural API design, SAD 08 roadmap.
- **F27** ComparisonExtensions anti-pattern (~93 call sites) — style-only; null-safety helpers used consistently (F74 notes deliberate asymmetry).
- **F28** ~40 magic numbers — partially fixed (BfoBase, thresholds); full Constants.cs is ADR-005, SAD 08 roadmap.
- **F31** 93 Debug.Log calls — remaining logs are intentional (errors/warnings); diagnostic block now #if DEBUG (F57/F93/F88).
- **F32** 5 GetComponentInChildren in IPH.Start — one-time init cost, not hot path.
- **F37** 6 stations with null callsign — historical data is correct (real Consol stations had no callsigns); runtime handles empty callsigns (F87).
- **F38** Plougourvest null frequency — gain code zeroes it (guard) and F131 excludes it from position fixes.
- **F39** 3 stations at 332.5 kHz — historical frequencies; co-channel behaviour is the point of Consol navigation (QSB separation).
- **F52** SAD Appendix B missing classes — documentation task, SAD update.
- **F62** StationState/NavigationPattern struct aliasing — patterns are replaced wholesale, never mutated through copies; theoretical risk only.
- **F63** DebugAngleHandler TMP dependency — debug-only helper; stripping risk accepted, SAD 08 note.
- **F70** NavigationPattern.FormatCompact transition misrepresentation — cosmetic string only; IPH reads pattern length, not this string.
- **F71** 2-3 Random.NextDouble() per sample in audio thread — noise character is intentional; redesign would change the sound, needs a live listen.
- **F73** Haversine duplicated (PositionFixCalculator vs NavigationPattern) — extracted only the NaN clamp (F80); full dedup is SAD 08 roadmap.
- **F83** RadioStoryIntegration zombie decisions on hot-reload — dead chain G1-G4, see `refactoring-plan/game-source-findings/`.
- **F173** InformationPanelHandler.CreateLabel OS font name ("Arial") — portability risk only; a missing font falls back to Unity's default, no crash; bundling a font is a content decision.

## Verified fixed without a new edit (code already satisfies, 2026-08-02)

- **F24** RemoveRange mutation bug — fixed by F120.
- **F30/F56** RadioHandler unsubscribes `zoneHandler.OnZoneChanged` in OnDestroy.
- **F33** Volume/FeedbackSpinnerHandler already merged into SpinnerHandlerBase.
- **F41** double/float phase mismatch — phase normalized to [0,1) by F169.
- **F55/F86** GetForCycleDuration stub — F164 logs a warning for non-120 s requests; all 14 beacons use 120 s.
- **F67** IPH `GetComponent<Toggle>()` NRE — RefreshAll uses cached `cachedToggle` with null guard.
- **F69** Main.cs swallow — LogError + LogException both present; swallow is deliberate (mod must not crash the game).
- **F74** ComparisonExtensions threshold asymmetry — deliberate; comment added in F74-commit.
- **F76** GetElementCount int overflow — method deleted (F193).
- **F82** Main.cs exception swallow in panel construction — deliberate + LogException (see F69).
- **F11** keyer lambda subscriptions — SonneController stores handlers and unsubscribes in OnDestroy (keyerElementHandlers/keyerSequenceHandlers arrays).
- **F14** event subscription leaks — all UI handlers balance subscribe/unsubscribe (IPH 6/6, RadioHandler, ToggleHandler, DebugAngleHandler); ZoneHandler uses Unity EventTrigger owned by the GameObject, no leak.
- **F167** StationScheduler.OnSequenceFinished — Callsign→PreNavPause and Navigation→PostNavSilence are handled; Pause/PreNavPause/PostNavSilence hit the guarded default branch (log + ignore), which is correct — no sequence is active in those phases.
