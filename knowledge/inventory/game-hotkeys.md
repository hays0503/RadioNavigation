# Game hotkey inventory (effective bindings, dumped in-game 2026-08-25)

Source: InputDumpMod dump of 80/83 game InputAxis assets (effective bindings — user overrides applied). Missing 3 axes = Inputs/F4, Inputs/F5, Inputs/F6 (referenced by InputSettingsPanel but not resolvable — device hotkeys F4/F5/F6 from settings screen: телеграф/глубиномер/аксиометр).

## Occupied keys

| Key | Action |
|---|---|
| M | Map Toggle |
| B | Binocular Toggle |
| H | Hydrophone Check |
| U | Toggle UI |
| J | Open Journal |
| K / L | Time Compression 1 / 2 |
| ; (Semicolon) | Time Compression 4 |
| ' (Quote) | Time Compression 5 |
| { (LeftCurlyBracket) | Time Compression 3 |
| ] (RightBracket) | Swap Postprocessing |
| Q | Action - Previous / Stadimeter Down |
| E | Stadimeter Up (+ periscope hardcode) |
| C | Periscope - Reset Horizontal Alignment |
| V | Periscope - Reset Vertical Alignment |
| R | Reload |
| N | Free Camera |
| F | Flashlight |
| W / S | Vertical (+ periscope/gun/hydrophone axes) |
| A / D | Horizontal (+ periscope/gun/hydrophone axes) |
| F1 / F2 / F3 | View FPP / Section View / Orbit View |
| F8 | Quick Save |
| F9 | Quick Load |
| F10 | Capture Screenshot |
| F11 | Bug report dialog (errorReporting, NOT an InputAxis — hardcoded report hotkey) |
| Z | RadioNavigation mod: constellation lines toggle (free at dump time) |
| Space | Pause |
| Tab | Switch Character |
| Escape | Exit / Stop Interaction / Cancel |
| Return | Confirmation |
| BackQuote | Open Close Console |
| Backspace | (SectionView hardcode) |
| Delete | (Map point delete hardcode) |
| Left/Right Ctrl | Control (+ periscope zoom hardcode) |
| Left/Right Shift | Edit Route / Sprint |
| PageUp / PageDown | UI Scale |
| + / = / Keypad+ | Engine Gear Increase |
| - / Keypad- | Engine Gear Decrease |
| Keypad0 | Realtime |
| 1 | Выбрать персонажа 1 (settings) |
| Alpha2..Alpha9 | Hotkey - Character 2..8 |
| Mouse0 / Mouse1 / Mouse2 | Artillery Fire / Context Menu+Gun Zoom+TC / Time Compression |
| JoystickButton0..8 | gamepad actions |

## Verified free

- F12 — free in game, but reserved by Steam screenshot overlay — avoid
- I, O, P, T, X, Y — not present in the dump, not hardcoded in game source (verify per-context before use)
- Alpha0 — not present in the dump

## Method

InputDumpMod (temporary, deleted): enumerated 83 known `Inputs/...` paths, `RetrieveAsset<InputAxis>` each, logged effective PositiveKeys/NegativeKeys (user overrides applied to assets at settings load). Full path union from KeyboardShortcuts.cs:181-207 + InputSettingsPanel.cs:68-69 + grep "Inputs/" across game source.
