# Sun HDR flare — GPU / API, 2026-10-13

- `HDRPLensFlare` — global type in **com.unity.standardassets.Runtime**, not
  com.uboat.rendering/pipeline/dependencies. It requires a `MeshRenderer` and
  `MeshFilter`; `LateUpdate` updates its material property block, `OnEnable`
  enables the mesh renderer. Source extraction retained in the shade report.
- Live solar instance is a child of native Sun: `/Sky Dome/Sun/Lens Flare`.
  Identify by component type **under the exact native Sun**, not by a scene-wide
  name scan. Other instances are lamp/lighthouse flares.
- `solar-source_frame10295.rdc`, PIP right: measuring texture (orange disc with
  3 spots) **29398**, draw **2169** into scene target **315455**, 950². At pixel
  (832,637) it writes RGB **0.9863 / 0.4661 / 0.0389**. It is really submitted.
- Bright spot in processed target **29384** is elsewhere: pixel (715,560) in
  scene target receives PPv2 output at **3164** then additive flare draws
  **3192/3198** (PS **21850**, textures **3335/3321**, soft glow/core). Red goes
  **0.02837 → 0.36865 → 1.171875**. The measured disc draw does not touch that
  pixel. This identifies the post-process source, not an astronomical direction.
- These flare draws occur **after HDR shade grading**. Hiding only the native
  disc or applying a PPv2 Bloom override cannot suppress the separate renderer.
- Candidate correction extends the existing per-PIP render token to the Sun's
  flare mesh, guarded by replacement-ready, restoring in the same finalizer.
  Native main view, lamp/lighthouse effects, weather, lights and ephemerides are
  untouched. **Live scoped-fix acceptance still required**.

Evidence and scope: `artifacts/reports/sextant-solar-shades-20261012/`, especially
`runs/gpu-source/gpu-source-proof.json`. No world/camera matrices, constant-buffer
values, hidden time/position or navigation answers were read for this diagnosis.
The MCP replay formatter mislabeled several enum names; the local proof script
retains actual RenderDoc enums. Pixel values and IDs were independently replayed.
