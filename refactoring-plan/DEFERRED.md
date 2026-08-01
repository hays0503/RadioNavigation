## Deferred findings (live-validation required)

These confirmed findings are intentionally NOT fixed in code — they require validation on the live in-game panel, so they were left out of the audit fix commits (2026-08-02):

- **F202** SpinnerHandlerBase 90 deg angle offset — knob art orientation lives in the binary AssetBundle (`Bundles/testui`), unknown from sources; both proposed variants need a live-panel check.
- **F203** Receiver default QF=20 causes cross-talk of neighbouring stations at 59-99% loudness (307/306, 316/315, 266/257 kHz) — selectivity tuning needs listening; default state is the least selective.
- **F163** mutable struct (F17-adjacent), **F179/F180/F181** architectural refactors (Main.cs SRP split, typed references, log levels), **F186** RadioStoryIntegration (dead chain G1-G4).
