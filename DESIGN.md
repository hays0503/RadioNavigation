# RadioNavigation  Design Specification

> Project: UBOAT Mod "RadioNavigation"  
> Type: Historical naval radio navigation instrument panel  
> Framework: Unity uGUI (prefabs loaded via AssetBundle)  
> Target game: UBOAT v2026.1+ by Deep Water Studio  

## Область действия

**Этот документ — спецификация радиопанели и только её.** Он писался до появления
остальных подсистем мода и никогда не обновлялся под них.

**Охватывает:** главная панель (диапазон, курсор, частотный дисплей, тумблеры),
информационная панель (декодирование Морзе, проверка земли, пеленги), визуальный
язык прибора, правила ввода с клавиатуры/мыши, запреты и анти-паттерны.

**Не охватывает** (у каждого пункта есть свой канонический документ):

| Подсистема | Где специфицирована |
|---|---|
| Секстант, ручное счисление FIX | `SEXTANT_PLAN.md` (v1.13), ADR-033 (селектор тел для пеленга) |
| Звёздное небо, каталог, созвездия, видимость | `STAR_SKY_PLAN.md` (v1.1), `CELESTIAL_REALISM_PLAN.md` (v3), ADR-020, ADR-021, ADR-022, ADR-026 |
| Планеты, Луна, игровое время неба | ADR-023, ADR-024, ADR-025 |
| Маяки на карте, наземные станции, FIX-маркеры | `MAP_MARKERS_PLAN.md`, ADR-027, ADR-028 |
| Кураторство данных маяков, деклаттер маркеров | ADR-030, ADR-031, ADR-032 |
| Связность подсистем (общая оценка) | ADR-029 |
| Хронометр, курсорные зоны, расширенный драг | ADR-034 |
| Защита загрузки/сейвов, гигиена рантайма | `refactoring-plan/PLAN_Phase6_LoadGate_Hygiene.md` |
| MCP-мост (живая диагностика и карта) | соседний мод `Mods/McpBridge`, `mcp_server.py` |
| Архитектура и обоснования | `SAD/`, `DOCS.md` |

**Клавиатура.** § 4 «Keyboard/Mouse Mapping» описывает только панель (Escape).
Управление секстантом (`Z`, `I`, `O`, `P`, стрелки, `Ctrl`, `R`), триады неба и
маркеры карты описаны в своих документах; полного единого списка клавиш в
репозитории нет.

**Актуальность.** Разделы 3-11 писались в августе 2026 и отражают состояние панели
на тот момент. С тех пор менялись: окуляр секстанта и PIP-камеры, состав звёздного
неба, поведение маркеров карты, способ доставки префабов — AssetBundle мода на игре
2026.1-patch21 не регистрируется в `ResourceManager`, поэтому префабы грузятся
напрямую (`ModBootstrapper.LoadBundleAsset`; ADR-028, реестр F229). Перед тем как
опираться на этот документ для новой работы, сверяйтесь с планом соответствующей
подсистемы.

---



## 1 Objective

Design a period-authentic Consol/Sonne radio navigation beacon panel that integrates seamlessly into UBOAT's WW2-era submarine simulator UI while providing functional audio synthesis and bearing diagnostics. The design must feel indistinguishable from native UBOAT instrumentation  no anachronisms, no "mod signature" styling.

**Scope includes:**  
- Main panel: zone selection dial, cursor spinner, frequency display, toggle controls  
- Information panel: real-time Morse signal extraction, ground condition and bearing diagnostics

---

## 2 Product Context

RadioNavigation adds a historically grounded naval navigation tool to UBOAT. The Consol (Consolensystem) and Sonne radio beacon systems were primary navigation aids for Nazi Germany's naval operations during WW2  exactly the context of UBOAT gameplay. This is **not** a utility overlay or modern dashboard; it is an in-universal instrument panel that functions *within* the game's established UI language.

The mod occupies a niche between:  
- The submarine's internal cockpit/radome instruments (compact, monochrome/green, vibration-hardened)  
- Bridge navigation tables (static maps, paper-based)  

This is the **cockpit instrument** category  portable, functional, always present during relevant gameplay sessions.

---

## 3 Visual Foundations

### Color Palette
Ultramarine green with amber backlighting  matching period Consol/Sonne radio console aesthetics. Avoid pure green (#00FF00) or pure amber; historical equipment used filtered glass/glass-illuminated panels.

```
--panel-bg:         #1a1d1e        // Dark brushed steel / submarine hull interior
--panel-border:     #28302f        // Slightly lighter frame (panel edge)
--text-primary:     #7ec86c        // Muted phosphor green (legible, period-appropriate)
--text-secondary:   #5a8b47        // Dimmed readout for inactive states  
--text-highlight:   #f0c040        // Warm amber for active/focus indicators
--accent-ground-ok: #4caf50        // Active ground check green
--accent-ground-bad:#c62828        // Degraded signal (red used sparingly)
--dial-face:        #1e2325        // Dial background, slightly darker than panel
--dial-ring:        #3a403f        // Dial outer ring / scale markings
--cursor-color:     #f0c040        // Cursor needle  bright amber always visible
```

### Typography
Mono-spaced number display for frequencies and coordinates. Sans-serif body text, matching UBOAT's clean reading clarity in cramped environments. All numbers right-aligned; labels left-aligned for scanability.

**Type scale (hierarchical):**  
| Role | Size | Weight | Alignment |  
|---|---|---|---|  
| Frequency (big) | 28px | Semibold | Right |  
| Coordinates | 14px | Regular | Right |  
| Status labels | 11px | Medium | Left |  
| Info/labels | 10px | Regular | Left / Uppercase tracking |

### Layout Grid
Fixed-width panel: **380420px** wide. No fluid resizing  instruments have fixed physical proportions in the source period equipment. Content stacks vertically with consistent **8px gutters**.

Sub-panels within the main UI follow a **card-like stacking order**:  
1. Zone selector (top)  horizontal dial band, left-aligned  
2. Cursor angle control (middle-left)  vertically centered spinner/dial  
3. Frequency output + status strip (right edge)  always visible next to cursor  
4. Expandable Information Panel (bottom/below toggle)  Morse decode history, nav position

### Motion & Feedback
- **Dial rotation:** smooth angular interpolation matched to user drag speed; angular snap zones at each of the 5 frequency bands (`DialConfig.Bands`, I–V)  
- **Cursor needle:** continuous real-time update synced to zone selection + angle input  
- **'Ground check' indicator:** binary pass/fail with brief color flash (transition duration ~200ms)  mirrors actual Consol audio discrimination  

No decorative animations. Every motion serves instrument-legibility or operational feedback only.

---

## 4 Accessibility

### Contrast Requirements
- Primary text on panel bg: ** 4.5:1** minimum  `#7ec86c` on `#1a1d1e` achieves ~5.2:1  
- Secondary/labels: meets AA at smaller sizes via bold letterform, not color lift  

### Color Independence
**Ground check must never be communicated by color alone.** Accompany with a shape/state indicator (checkmark icon, label text: "GROUND: OK / BAD") and brightness change. Every status needs a redundant non-color channel.

### Keyboard/Mouse Mapping
Escape key toggles panel visibility. All numeric inputs respond via drag or direct wheel input  no reliance on keyboard-only entry for submarine environment (user may lack physical keyboard access in UBOAT). Provide Escape-based close convention consistent with native UBOAT panels.

---

## 5 Voice & Tone

All UI text is **technical, terse, signal-report style**. No explanatory prose inside the instrument itself; all instructions live outside (in-game help, mod README).

**Label conventions:**  
- Use uppercase, reduced spacing: `CURSOR` not "Cursor Angle" in tight slots  
- Numeric-only zones where possible: `ZONE 1`  just `Z1` if space is constrained, or full word only when layout allows  
- No sentences. Status indicators carry their own meaning via visual state.

**Morse decode output:** Uppercase plain text. No punctuation except what Decoded Morse audio actually contains (historically rare). Timestamp-free; latest readings scroll upward for efficiency. Readout format: `STATION  CALLSIGN  SIGNAL_RATING`

---

## 6 Implementation Practices

### uGUI Architecture
- **One prefab root** loaded via AssetBundle (`Bundles/testui`, prefab name `"Radio"`)  
- Panel hierarchy validated against hardcoded child paths at runtime (Main.cs:200, `requiredChildren[]`, константы `PrefabNames.*`)  maintain strict naming: `Cursor`, `Toggle`, `Ground`, `Ground/Background/GroundCheckmark`, `Expander`, `RadioZone`, `VolumeSpinner`, `FeedbackSpinner`  
- **Do NOT add new top-level children** to the validated hierarchy; extend via sibling placement or nested child objects with stable paths  

### Component Boundaries
Each prefabricated uGUI element maps one-to-one to a C# `MonoBehaviour` handler. Handlers subscribe to domain events (SonneController, ZoneHandler) but **never implement business logic**. All calculation, synthesis, position-fixing belongs in Logic/ namespaces. UI handlers exist only for:  
- Input capture (drag, click)  
- Visual state mapping  
- UBOAT lifecycle bridge  

### Threading Discipline
Audio callback operates at 44.1kHz without Unity engine access. **Zero Debug.Log() calls** or UnityEngine API inside the DSP path  this is a hard constraint regardless of build type. Use buffered debug output flushed on next Update().

---

## 7 Anti-Patterns

### What This Design Is NOT
- **No modern flat design:** Avoid the "SaaS product card" aesthetic (rounded corners, soft shadows, blue accent). The UI must be angular/correct  physical instruments have straight mechanical geometry.  
- **No gradients for illumination:** Real Consol/Sonne consoles had backlit glass panels with uniform phosphor-green light. Use solid fills or very subtle single-stop shading only.  
- **No emoji or decorative glyphs anywhere.** All icons are geometric/schematic. A ground check is a filled rectangle, not "".  
- **No responsive/floating layout:** Panel position on screen is fixed by design (lower-right quadrant when visible). No center-screen alerts or modals.  
- **No "dark mode toggle" or theme system.** This instrument exists in one state: submarine interior, dim lighting with green illumination  

### Critical Anti-Patterns
| Pattern | Why it's wrong here | Correct approach |  
|---|---|---|  
| Rounded corners (8px+) on panel elements | Contradicts period hardware  instruments are machined metal/steel | Sharp 24 px radii only where the source equipment had physical rounding  
| Glossy/drop-shadow depth effects | 1940s radio consoles were flat-screened, not glass-faced smartphones | Flat fills with edge-bevel or rim-light sim via adjacent panel element |  
| Animated background grid/tech patterns | Distracts from instrument-readings which are the sole purpose of this UI | Solid or near-solid backgrounds only; no "data-driven" motion decoration |    

---

## 8 Decision-Making

### When Choosing Between Options
Every design decision in RadioNavigation must satisfy one question: **"Would a 1940s naval radio operator on an U-boat feel comfortable touching this?"** If a control looks like it belongs on modern software, refactor  even if the code works perfectly.

### Override Authority
UBOAT's native UI language takes precedence over period authenticity where they conflict and cannot be reconciled (e.g., missing uGUI primitives that would need to simulate historical hardware). In those cases: match UBOAT's visual vocabulary first, then inject period flavor through color/typography only.

### Change Review Protocol
Any modification to the prefab hierarchy (`Main.cs` line 165 validation array) requires review against the existing handler map before merge. Adding a top-level sibling without updating this list causes silent asset bundle parse failures (current code destroys and skips).

---

## 9 Workflow

### Iteration Order
When developing or modifying anything visual, follow:  
1. **Prototype state machines** first  toggle on/off, zone cycling, cursor rotation with no audio connected  
2. **Attach Info Panel content** (Morse decode, ground check signals)  verify data flow to display handlers  
3. **Visual styling pass** (color, type, layout refinement)  after the component has all its behaviors wired  
4. **Audio integration** last  audio verification is a gameplay/testing concern handled separately  

### Deliverables Checklist
- [ ] Prefab root with validated child hierarchy  
- [ ] All handlers initialized from UBOAT prefab instantiation  
- [ ] Escape key toggle behavior present and tested  
- [ ] No UnityEngine API calls inside `OnAudioFilterRead` DSP path  
- [ ] Period-authentic color palette applied to all uGUI elements  

---

## 10 Structure  Artifact Layout (Main Panel)

The following describes the main panel artifact as a single reading-order flow from top to bottom. Each element maps directly to an existing Unity prefab hierarchy node and its corresponding C# handler.

```
 Parent: "Radio" GameObject  Fixed width 380-420px, anchored lower-right viewport
                                              
   ZONE SELECTOR ROW                         Child #7: RadioZone (horizontal strip, Bands I through V)
                                             Active button  amber highlight (#f0c040), inactive  secondary text only  
             
 [Z1]   [Z2 ] [Z3]   ...  [ZV]              
 Zone-I  Zone-II Zone-III... Zone-V     Child #7: ZoneHandler.cs  maps zone index (0..4, `DialConfig.Bands.Length`) to frequency range via DialConfig.Bands[]  active state toggled visually
 Frequency Strip (right edge of Row)            
                                  
    72.4 kHz                                Size: inheritable with Mono typeface, right-aligned  
                                              Child #? (implicit): "FrequencyDisplay" text  needs constant in PrefabNames.cs for handler binding
                                                   Cursor Dial              Ground Check Strip           Top-Row children: child #1 "CursorSpinner", child #8 "Volume Spinner" (right-adjacent to freq)  
                                             
                      
   SPINNER           GROUND   Child, center: CursorHandler.cs  drag rotates cursor needle over the 360deg dial arc, angle  freq via DailConfig.AngleToFrequency()  smooth interpolation (no angular snap)           STATUS    Ground check is a narrow vertical strip next to dial binary OK/BAD indicator  Green (#7ec86c) or Red (#c42828) with matching label text  
                              Child : GroundHandler.cs, ZoneHandler.cs  subscription pair updates .IsGrounded flag every cycle, drives visual fill of Ground image element        
                   
                                              Expand/Collapse Toggle  
    [ EXPAND ]                              Child #5: Expander.cs  expand/collapse animation for secondary Info panel region below
                                                                                            INFO PANEL (collapsible)            
 DECODED TEXT STREAM                              
 BEAUVALS  R   ----                           Height: expands downward when toggled  max visible lines 6; content scrolls with newest entry at bottom                                         SIGNAL OK                             Child #4 (implied): "Ground" GameObject contains Background + GroundCheckmark child  image element fill
                                               Expandable Info Panel structure
```

### Element Crosswalk (Prefab  Handler  Behavior)

| Prefab Node | C# Handler | Role |
|---|---|---|
| CursorSpinner | `CursorHandler` | Draggable dial 90 from zone center; drives `SonneController.EmitFrequency()` |
| RadioZone | `ZoneHandler` | Zone band selection (I–V); maps index  frequency range via `DialConfig.Bands[]` |
| Toggle (child #3) | `ToggleHandler` | Panel open/close + info panel expand/collapse state |
| Ground/Background/GroundCheckmark | `GroundHandler` | Binary OK/BAD signal decode indicator driven by ground detection logic |
| RadioZone strip | Various zone scripts | Horizontal band selector strip, zone AI visual toggle active/inactive state |
| VolumeSpinner (child #8) | `VolumeSpinnerHandler` | Receiver gain volume level  same spinner archetype as feedback, injected `DebugTag` differs |
| FeedbackSpinner (child #9) | `FeedbackSpinnerHandler` | Receiver sensitivity/bias feedback control |
| Expander (child #5) | `ExpanderHandler` | Expand/collapse animation for Info Panel region; nested Canvas Group |
| **MISSING** | *FrequencyDisplay?* | Live frequency readout with zone label  NOT in `requiredChildren[]`; must add to Main.cs:200 array before any refactoring |

---

### Critical Missing Element Note

The current `requiredChildren[]` array in `Main.cs` (line ~200, константы `PrefabNames.*`) does not include a dedicated container for the **Information Panel itself**  only "Expander" (the toggle button). The info panel region that holds decoded text + position data either:
- Is an implicit sibling of Radio with no named path check, or
- Hangs under Expander as unnamed child, or  
- Exists in a separate prefab entirely.

This must be resolved before implementing any info-panel expansion  гипотеза о хрупкости хардкод-путей задокументирована в `refactoring-plan/hypothesis/INDEX.md` (F180); исходные файлы плана `P0.C4_ReflectionFragility.md`/`P2.M3_HardcodedPrefabPaths.md` не существуют (архив старых планов).

---

## 11 Decision Trace

Each entry records a non-obvious design choice in JSON format, with rationale tied to brief details (not just "looks better"), real alternatives rejected, and genuine tradeoffs.

### D1  Period-authentic over modern-interpretation
```json
{
  "id": "D1",
  "decision": "Deliberately target historical authenticity (WW2 naval console aesthetic) rather than a clean-modern simulation look or retro-nostalgia style",
  "reason": "UBOAT is a period submarine simulator; UI must feel in-universe. A 'modern software panel mod' breaks immersion immediately since operators would never see such controls on an U-boat console.",
  "alternatives": [
    "Clean modernist military design (NATO-era control interface look with blue-white palette)",
    "Retro-modern: vintage hardware silhouette rendered in polished contemporary graphic style",
    "Mixed approach  historic skeleton, modern UI polish"
  ],
  "tradeoff": "Harder to implement exact period look using only Unity uGUI primitives. Requires custom sprite sheets or texture baking for panel edge bevel simulation."
}
```

### D2  Fixed-width non-responsive panel
```json
{
  "id": "D2",
  "decision": "Panel has a fixed width (380420px) with no fluid responsiveness or window resizing support",
  "reason": "Period naval hardware does not resize dynamically; the mod simulates touching real dials on metal console. UBOAT's own panels use fixed positions rather than fluid adaptability.",
  "alternatives": [
    "Fully responsive layout scaling across resolutions and aspect ratios",
    "Floating dockable panel with resizable borders"
  ],
  "tradeoff": "At very small laptop resolutions or ultrawide displays, may require manual UI anchoring setup. UBOAT dynamic windowing additions will be brittle."
}
```

### D3  Green/amber dual-hue instead of monochrome or blue-white CRT
```json
{
  "id": "D3",
  "decision": "Use muted phosphor green (#7ec86c) for text + amber (#f0c040) as active-element highlight",
  "reason": "Actual Consol/Sonne consoles used filtered backlit glass yielding a warm-green hue rather than raw LED-green. Amber provides enough contrast variation against green to signal 'active' without relying solely on brightness.",
  "alternatives": [
    "Pure monochrome single-color (entirely green or entirely amber)",
    "Cold blue-white phosphor matching modern CRT simulation",
    "Warm sepia/brown tones for antique radio feel"
  ],
  "tradeoff": "Requires precise placement to avoid looking mismatched. Pure monochrome has less CSS/uGUI complexity but loses active-state distinction without color alone."
}
```

### D4  No decorative animations or transition polish
```json
{
  "id": "D4",
  "decision": "Panel contains zero decorative motion; every animation serves instrument-legibility or operational feedback only",
  "reason": "Real naval consoles have animated backgrounds, floating particles, or transition flair. Adding them signals 'software overlay' which contradicts the in-universe goal.",
  "alternatives": [
    "Cursor dial with subtle angular momentum physics easing (realistic feel)",
    "Status indicators with glow/pulse on state change for visual excitement"
  ],
  "tradeoff": "May feel 'static' or less lively compared to mod alternatives that add visual flair. Some users experience the panel as visually dull without decoration."
}
```

### D5  Ground check is never color-only
```json
{
  "id": "D5",
  "decision": "Ground OK/BAD communicated via shape fill (green or red square) + explicit label text ('OK'/'BAD')  never color alone in any state where the user must act on it",
  "reason": "In submarine conditions (night shifts, sick bay lighting), pure color discrimination is unreliable. Period operators relied on shape and signal tone as primary channels.",
  "alternatives": [
    "Color-only indicator with semantic green/red dots  minimal space overhead",
    "Shape only without color variation (a checkmark square or X)  safest accessibility but loses immediate readability"
  ],
  "tradeoff": "Slightly more visual real estate than a simple dot; text labels consume space on the fixed-width panel."
}
```

### D6  Info Panel as expandable inline region, not separate floating window
```json
{
  "id": "D6",
  "decision": "Information extension opens downward within same prefab hierarchy when toggle is pressed, rather than spawning a separate Unity dialog, overlay, or child Canvas.",
  "reason": "Real aircraft/submarine consoles combine multiple readouts and instruments in one face. The Consol/Sonne console itself had paper tape output and gauge cluster as single unit.",
  "alternatives": [
    "Separate floating window (two-independent prefab approach from Main.cs)",
    "Full-screen dedicated decode terminal view for heavy traffic periods"
  ],
  "tradeoff": "Expanding panel consumes more screen real estate; if Morse traffic is very dense, the expanded area may push below UBOAT's native HUD overlay."
}
```

### D7  Mono-spaced numeric display overriding proportional sans-serif
```json
{
  "id": "D7",
  "decision": "Frequency and coordinates rendered with mono-spaced number display even when surrounding label text uses a regular proportional font family.",
  "reason": "Single-column alignment of numbers is critical for operator readability on naval consoles where users scan across rows. Pitch-based number layout matches period instrument faces which used fixed-width stamped numerals.",
  "alternatives": [
    "Unified proportional font with tabular figure sizing",
    "Full mono everywhere  typewriter aesthetic throughout panel"
  ],
  "tradeoff": "Requires Unity uGUI to handle mixed-font rendering within a single Text element, which may need separate child text objects for number vs label."
}
```

### D8  Hardcoded prefab validation enforces hierarchy integrity
```json
{
  "id": "D8",
  "decision": "Prefab children validated against hardcoded paths in Main.cs (~line 200) requiredChildren[] array built from PrefabNames.* constants (ADR-019); any missing child triggers silent destroy-and-skip rather than graceful degradation.",
  "reason": "Broken partial instantiation produces inconsistent runtime errors downstream (e.g., CursorHandler attached but no Child transform for angle mapping).",
  "alternatives": [
    "Null-check each child at handler Start  fail silently per-component, continue panel load",
    "Soft degrade where missing UI just means nonfunctional button without destroying the entire panel"
  ],
  "tradeoff": "If UBOAT updates its GUI scene hierarchy and reorders children, this validation causes silent asset bundle failures (fragility flagged by old plan P0.C4_ReflectionFragility.md — файл архивирован; см. F180 в refactoring-plan/hypothesis/INDEX.md). Hard to recover from once AssetBundle is missing the prefab."
}
```

---

