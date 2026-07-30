# RadioNavigation — Design Specification

> Project: UBOAT Mod "RadioNavigation"  
> Type: Historical naval radio navigation instrument panel  
> Framework: Unity uGUI (prefabs loaded via AssetBundle)  
> Target game: UBOAT v2026.1+ by Deep Water Studio  

---

## §1 Objective

Design a period-authentic Consol/Sonne radio navigation beacon panel that integrates seamlessly into UBOAT's WW2-era submarine simulator UI while providing functional audio synthesis and position-fixing capabilities. The design must feel indistinguishable from native UBOAT instrumentation — no anachronisms, no "mod signature" styling.

**Scope includes:**  
- Main panel: zone selection dial, cursor spinner, frequency display, toggle controls  
- Information panel: real-time Morse signal extraction, ground condition indicators, position fix calculator output  

---

## §2 Product Context

RadioNavigation adds a historically grounded naval navigation tool to UBOAT. The Consol (Consolensystem) and Sonne radio beacon systems were primary navigation aids for Nazi Germany's naval operations during WW2 — exactly the context of UBOAT gameplay. This is **not** a utility overlay or modern dashboard; it is an in-universal instrument panel that functions *within* the game's established UI language.

The mod occupies a niche between:  
- The submarine's internal cockpit/radome instruments (compact, monochrome/green, vibration-hardened)  
- Bridge navigation tables (static maps, paper-based)  

This is the **cockpit instrument** category — portable, functional, always present during relevant gameplay sessions.

---

## §3 Visual Foundations

### Color Palette
Ultramarine green with amber backlighting — matching period Consol/Sonne radio console aesthetics. Avoid pure green (#00FF00) or pure amber; historical equipment used filtered glass/glass-illuminated panels.

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
--cursor-color:     #f0c040        // Cursor needle — bright amber always visible
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
Fixed-width panel: **380–420px** wide. No fluid resizing — instruments have fixed physical proportions in the source period equipment. Content stacks vertically with consistent **8px gutters**.

Sub-panels within the main UI follow a **card-like stacking order**:  
1. Zone selector (top) — horizontal dial band, left-aligned  
2. Cursor angle control (middle-left) — vertically centered spinner/dial  
3. Frequency output + status strip (right edge) — always visible next to cursor  
4. Expandable Information Panel (bottom/below toggle) — Morse decode history, nav position

### Motion & Feedback
- **Dial rotation:** smooth angular interpolation matched to user drag speed; angular snap zones at each of the 8 frequency bands  
- **Cursor needle:** continuous real-time update synced to zone selection + angle input  
- **'Ground check' indicator:** binary pass/fail with brief color flash (transition duration ~200ms) — mirrors actual Consol audio discrimination  

No decorative animations. Every motion serves instrument-legibility or operational feedback only.

---

## §4 Accessibility

### Contrast Requirements
- Primary text on panel bg: **≥ 4.5:1** minimum → `#7ec86c` on `#1a1d1e` achieves ~5.2:1  
- Secondary/labels: meets AA at smaller sizes via bold letterform, not color lift  

### Color Independence
**Ground check must never be communicated by color alone.** Accompany with a shape/state indicator (checkmark icon, label text: "GROUND: OK / BAD") and brightness change. Every status needs a redundant non-color channel.

### Keyboard/Mouse Mapping
Escape key toggles panel visibility. All numeric inputs respond via drag or direct wheel input — no reliance on keyboard-only entry for submarine environment (user may lack physical keyboard access in UBOAT). Provide Escape-based close convention consistent with native UBOAT panels.

---

## §5 Voice & Tone

All UI text is **technical, terse, signal-report style**. No explanatory prose inside the instrument itself; all instructions live outside (in-game help, mod README).

**Label conventions:**  
- Use uppercase, reduced spacing: `CURSOR` not "Cursor Angle" in tight slots  
- Numeric-only zones where possible: `ZONE 1` → just `Z1` if space is constrained, or full word only when layout allows  
- No sentences. Status indicators carry their own meaning via visual state.

**Morse decode output:** Uppercase plain text. No punctuation except what Decoded Morse audio actually contains (historically rare). Timestamp-free; latest readings scroll upward for efficiency. Readout format: `STATION  CALLSIGN  SIGNAL_RATING`

---

## §6 Implementation Practices

### uGUI Architecture
- **One prefab root** loaded via AssetBundle (`Bundles/testui`, prefab name `"Radio"`)  
- Panel hierarchy validated against hardcoded child paths at runtime (Main.cs:165) — maintain strict naming: `CursorSpinner`, `Toggle`, `Ground`, `Ground/Background/GroundCheckmark`, `Expander`, `RadioZone`, `VolumeSpinner`, `FeedbackSpinner`  
- **Do NOT add new top-level children** to the validated hierarchy; extend via sibling placement or nested child objects with stable paths  

### Component Boundaries
Each prefabricated uGUI element maps one-to-one to a C# `MonoBehaviour` handler. Handlers subscribe to domain events (SonneController, ZoneHandler) but **never implement business logic**. All calculation, synthesis, position-fixing belongs in Logic/ namespaces. UI handlers exist only for:  
- Input capture (drag, click)  
- Visual state mapping  
- UBOAT lifecycle bridge  

### Threading Discipline
Audio callback operates at 44.1kHz without Unity engine access. **Zero Debug.Log() calls** or UnityEngine API inside the DSP path — this is a hard constraint regardless of build type. Use buffered debug output flushed on next Update().

---

## §7 Anti-Patterns

### What This Design Is NOT
- **No modern flat design:** Avoid the "SaaS product card" aesthetic (rounded corners, soft shadows, blue accent). The UI must be angular/correct — physical instruments have straight mechanical geometry.  
- **No gradients for illumination:** Real Consol/Sonne consoles had backlit glass panels with uniform phosphor-green light. Use solid fills or very subtle single-stop shading only.  
- **No emoji or decorative glyphs anywhere.** All icons are geometric/schematic. A ground check is a filled rectangle, not "✓".  
- **No responsive/floating layout:** Panel position on screen is fixed by design (lower-right quadrant when visible). No center-screen alerts or modals.  
- **No "dark mode toggle" or theme system.** This instrument exists in one state: submarine interior, dim lighting with green illumination  

### Critical Anti-Patterns
| Pattern | Why it's wrong here | Correct approach |  
|---|---|---|  
| Rounded corners (8px+) on panel elements | Contradicts period hardware — instruments are machined metal/steel | Sharp 2–4 px radii only where the source equipment had physical rounding  
| Glossy/drop-shadow depth effects | 1940s radio consoles were flat-screened, not glass-faced smartphones | Flat fills with edge-bevel or rim-light sim via adjacent panel element |  
| Animated background grid/tech patterns | Distracts from instrument-readings which are the sole purpose of this UI | Solid or near-solid backgrounds only; no "data-driven" motion decoration |    

---

## §8 Decision-Making

### When Choosing Between Options
Every design decision in RadioNavigation must satisfy one question: **"Would a 1940s naval radio operator on an U-boat feel comfortable touching this?"** If a control looks like it belongs on modern software, refactor — even if the code works perfectly.

### Override Authority
UBOAT's native UI language takes precedence over period authenticity where they conflict and cannot be reconciled (e.g., missing uGUI primitives that would need to simulate historical hardware). In those cases: match UBOAT's visual vocabulary first, then inject period flavor through color/typography only.

### Change Review Protocol
Any modification to the prefab hierarchy (`Main.cs` line 165 validation array) requires review against the existing handler map before merge. Adding a top-level sibling without updating this list causes silent asset bundle parse failures (current code destroys and skips).

---

## §9 Workflow

### Iteration Order
When developing or modifying anything visual, follow:  
1. **Prototype state machines** first — toggle on/off, zone cycling, cursor rotation with no audio connected  
2. **Attach Info Panel content** (Morse decode, ground check signals) — verify data flow to display handlers  
3. **Visual styling pass** (color, type, layout refinement) — after the component has all its behaviors wired  
4. **Audio integration** last — audio verification is a gameplay/testing concern handled separately  

### Deliverables Checklist
- [ ] Prefab root with validated child hierarchy  
- [ ] All handlers initialized from UBOAT prefab instantiation  
- [ ] Escape key toggle behavior present and tested  
- [ ] No UnityEngine API calls inside `OnAudioFilterRead` DSP path  
- [ ] Period-authentic color palette applied to all uGUI elements  
