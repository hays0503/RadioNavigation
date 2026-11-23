# Ocean-current atlas: CPU-efficient sampling research

Access date for **all external sources below: 2026-11-16**.
Context: [plan](OCEAN_CURRENTS_PLAN.md), [open risks](OCEAN_CURRENTS_RISKS.md).
Scope: evidence and proposed F4/F5/F8 decisions, **not implementation or acceptance**.
No game launch, dataset download or runtime change performed. Standalone
synthetic follow-up is recorded below; it is not Unity acceptance.
Existing OC identifiers remain unchanged; proposed refinements do not close them.

## Findings and recommended direction

Keep scientific processing offline. Ship a validated, versioned binary atlas;
sample resident arrays through a pure C# 7.0 function. Separate seasonal
east/north profiles from tidal complex coefficients and from a shared
game-UTC harmonic basis. Load/decompress/check tiles outside physics; capture
one immutable data/time snapshot for each calculation. These are **proposals**,
not performance guarantees. A 60 fps target gives the whole frame about
16.67 ms, not an atlas allowance; its share needs measurement.

### 1. FES: interpolate complex coefficients, not amplitude and phase

**Verified source evidence.** At CNES commit
`9bf10a443d9fb47941a61c1daa9ff5789b8bca57`:

- `src/pyfes/config.py::_amp_phase_to_wave` converts amplitude/phase to
  `A * exp(+i*g)` (degrees converted to radians) **before** model interpolation [F1].
- `include/fes/tidal_model/cartesian.hpp` stores `std::complex<T>` per
  constituent, computes one spatial interpolation context, then interpolates
  each constituent [F2].
- `include/fes/detail/math.hpp::bilinear_interpolation` skips non-finite
  corners and renormalizes the remaining weights; the specialized complex
  overload applies real weights separately to real and imaginary parts [F3].
  This is evidence of predictor behavior, not a coastal-connectivity guarantee.
- `WaveTableInterface::tide_from_tide_series` evaluates
  `f * (wave.real * cos(vu) + wave.imag * sin(vu))` [F4]. Therefore with the
  stored positive phase the result is `f*A*cos(vu-g)`, not `cos(vu+g)`.
- FES2014 current configuration uses `Ua`/`Ug` for eastward coefficients,
  includes M4/M6/M8 and long-period constituents, and is distinct from heights
  [F5]. README says current grids are 1/16 degree and **not coastal-extrapolated**
  [F6]. Actual file units/masks still require header/data inspection.

**Proposal.** Offline convert both velocity components to Cartesian coefficient
pairs `(a,b)=(A*cos(g), A*sin(g))` in m/s. Runtime bilinear interpolation of
`a,b`, followed by a shared-basis dot product, needs no spatial-query trig,
phase unwrapping or complex-number library. Reuse grid weights across
constituents only where grids really match.

**Analytic counterexamples, not measurements:** phases 359 and 1 degrees
average numerically to 180 degrees; equal-amplitude complex interpolation
instead stays near 0 degrees. Equal and opposite coefficients cancel:
amplitude-plus-unwrapped-phase interpolation can incorrectly retain amplitude.
Near zero amplitude, phase is ill-conditioned; compare Cartesian velocity
errors, not phase-relative errors.

Do not copy CNES missing-corner renormalization blindly across islands,
disconnected channels, different u/v masks or depth masks. Retain support
quality and apply the plan's explicit coastal policy.

### 2. Shared tidal basis; bounded interpolation error including slack water

**Evidence.** CNES describes astronomical arguments and constituent-specific
nodal amplitude/phase corrections [F7]. Its Darwin defaults include minor-wave
inference and long-period equilibrium computation [F7]; a generic height
prediction default is **not automatically a velocity prescription**.
NIST gives linear interpolation and its `1/8` second-derivative bound [N1].

**Proposed factorization**, for either velocity component at a fixed position:

```text
U(x,t) = sum_k [a_k(x)*p_k(t) + b_k(x)*q_k(t)]
p_k(t) = f_k(t)*cos(theta_k(t))
q_k(t) = f_k(t)*sin(theta_k(t))
```

Compute the astronomical/nodal basis once per shared timestamp, or interpolate
between validated game-time anchors. All objects at that timestamp reuse it.
`Sample` performs interpolation and multiply-adds, not sin/cos per vessel,
probe or constituent. Any inferred waves must be incorporated consistently
with the pinned oracle; the number of atlas files is not necessarily the
number of synthesized waves. Do not silently add equilibrium *height* to m/s.

**Derived bound**, using NIST's remainder, for anchor interval `h` seconds:

```text
|U - U_linear_basis|
 <= h^2/8 * sum_k (|a_k|*sup|p_k''| + |b_k|*sup|q_k''|)
```

Suprema cover the interval. With frozen `f` and constant `omega`, a sharper
single-harmonic bound is `A*f*omega^2*h^2/8`. For the unit rotating basis its
exact midpoint chord error is `1-cos(omega*h/2)`; small-angle expansion is
`(omega*h)^2/8`. These are analytic bounds, not observed PyFES/UBOAT errors.
Changing nodal factors/arguments require their derivatives or empirical
oracle residual bounds, not the frozen-frequency formula alone.

Use an **absolute m/s budget** and trajectory error: dividing by instantaneous
`|U|` is undefined at slack water or cancellation between constituents.
Bound east/north separately, then combine their bounds in Euclidean norm.
This temporal bound excludes spatial resampling, quantization, source-model
uncertainty and changes of interpolation support.

**Lowest-complexity candidate:** exact shared basis at each distinct simulation
timestamp first; consider interpolated anchors only if profiling justifies it.
For campaign dates 1939–1945, pin astronomical formulae, inferred-wave policy,
epoch/time-scale conventions and leap-day behavior against CNES. Long-period
or seasonal layers must have an explicit constituent ownership rule.

### 3. Climatology, depth and int16 packing

**Verified metadata evidence.**

- GLORYS12 description: 1/12 degree regular output, 50 standard depth levels,
  daily/monthly **means**, from 1993 onward [D1].
- IBI description: 1/36 degree model, 3D/2D daily/monthly/yearly fields,
  hourly means for surface variables, and delivered monthly climatological
  means/standard deviations for **1993–2016** [D2]. This avoids rebuilding that
  climatology only if the selected variables/depths satisfy the contract.
- NWS description: model **with tides**, 7 km, catalogue lists 24 depth levels
  and hourly/daily/monthly availability [D3]. Catalogue-wide availability
  does not prove every variable has every depth/cadence.
- CF 1.12 §§2.5.1/8.1: reject packed `_FillValue`, missing values and invalid
  range **before** `unpacked = packed*scale_factor + add_offset` [C1].
  CF §§4.3/5.6 cover vertical coordinates and mappings; §§7.3/7.4 distinguish
  cell means and climatological statistics. Monthly climatology values are
  not asserted instantaneous midpoint currents.

**Proposed preparation and layout.**

- Build/reuse an explicitly dated seasonal background; preserve actual
  coordinates, calendar, averaging bounds and contributing valid samples.
  Do not substitute an arithmetic average of unequal time intervals.
  Define leap-year/year-wrap mapping; test February 1940/1944.
- Regional replacement is not global-plus-regional addition. First establish
  tide ownership/detiding. Monthly averaging alone is not proof of detiding.
  Verify vector orientation; curvilinear or rotated grids may need offline
  vector rotation and regridding, not merely renamed axes.
- Choose retained depth levels by profile/trajectory validation, not a preset
  number. Convert parametric levels offline where applicable. Masks are
  depth/time/component-specific; no surface substitution below a valid profile
  or interpolation across an unknown/below-bottom layer.
- Flat arrays with explicit axis order; seasonal layout can be
  `[season,depth,y,x,component]`, tidal layout
  `[constituent,y,x,component,re/im]`. Store independent coordinate/depth
  metadata and source/provenance identifiers; these layouts are not CF claims.
- Reserve `short.MinValue` for missing, use valid integer range
  `[-32767,32767]`, validate jointly required u/v support, then decode.
  Keep separate status/provenance bits: valid zero, land, below bottom,
  unsupported, transiently unavailable and corrupt are not interchangeable.
- With round-to-nearest and no overflow, scalar quantization error is at most
  `scale/2`; vector error is at most `sqrt(scale_u^2+scale_v^2)/2`.
  This is derived arithmetic, excluding float decoding error. Choose scale
  from measured retained ranges/error limits; reject overflow, never silently
  clip. For tides, coefficient errors accumulate through the harmonic sum;
  a per-coefficient tolerance is not the final velocity tolerance.
- Different per-tile scales can produce seams after independent rounding.
  Derive halo nodes from the same canonical data/quantization rule and test
  bit identity or a bounded seam error. Reject malformed scales and lengths.

### 4. Compression, asynchronous loading and immutable publication

**Evidence.** `DeflateStream` is a .NET Standard 2.0 API and does not support
seeking in decompressed data [M1]. `Interlocked.Exchange<T>` supports atomic
reference exchange; `Volatile.Read<T>` supplies ordered reference reads [M2].
Unity says its API is not thread-safe, async tasks can allocate, and managed
tasks are not automatically stopped on lifecycle exit [U3].

**Proposal, no new runtime dependency.**

- Use independently compressed tiles/blocks with directory entries containing
  offset, compressed length, exact decoded length, dimensions, codec/version,
  scale metadata and checksums. Candidate codec: existing BCL DeflateStream;
  raw uncompressed blocks remain a valid option. Verify codec and assembly
  availability against installed game Mono, not merely modern .NET.
- Specify raw DEFLATE versus zlib/gzip framing explicitly in the offline writer
  and C# reader. Test cross-tool round trips; source `.tar.xz` is an offline
  distribution format, not a reason to ship an xz/NetCDF dependency.
- Read/decompress/hash/validate on a bounded managed loader with **no Unity
  object or scene access**. Coroutines alone do not move synchronous CPU
  decompression off the main thread. Avoid task-per-query dispatch.
- Bound compressed input, decoded output, concurrent decoders, pending requests
  and completed-but-unpublished results. Validate products/offsets using checked
  wide arithmetic before allocation; truncated/overlong output fails closed.
  Read loops must handle short reads. Decode into sized buffers, not an
  unbounded growing MemoryStream.
- Construct a complete immutable candidate. At a main-thread step boundary,
  verify campaign generation, atlas hash/version and required support; publish
  **one reference**. Capture that reference once for the entire physics/job
  schedule/apply interval, with matching time/basis. Atomic reference exchange
  does not make separately mutated arrays or dictionaries safe.
- Retain old storage until all readers finish; never return its arrays to a
  pool on publication alone. `readonly` array fields do not make elements
  immutable. Reject late results after save/load, boat/scene change or shutdown.
  Include a bounded retirement policy: readers cannot pin unlimited snapshots.

### 5. Missing tiles: performance policy must not become a different ocean

**Refinement of OC-016/035/041/049; proposed, not implemented.**
A last cached *velocity* has the wrong tide phase after time advances; a tile
from another location is not a safe fallback. A regional full field plus a
global full field on cache miss can also reintroduce double-counted tides.

Define coverage separately from residency. At minimum return
`availability + source/atlasVersion + support quality`; never return an
unlabelled zero. Options requiring an explicit product decision:

- Required data absent: resolve at a controlled loading barrier **outside**
  physics, or declare that simulation interval unsupported.
- Coarse complete fallback: keep its required domain resident and validate its
  layer composition. Optional refinement must follow a deterministic selection
  policy, not whichever disk request finished first.

A frozen per-step readiness snapshot prevents mid-step inconsistency but does
**not** by itself make warm-cache and cold-cache runs reproducible. Even a
smooth blend based on real-time load completion changes trajectories.
Persist/configure a stable refinement policy, preflight required working sets,
or explicitly weaken reproducibility with a recorded degraded mode. Report
degradation, age in game seconds, affected area and missing layer.

### 6. Cadence, aliasing and memory: specific new failure modes

These refine OC-015/037/038/040/049; **no new OC numbers assigned**.

- **Real seconds versus game seconds.** For compression factor `s`, a refresh
  every `r` real seconds spans approximately `s*r` game seconds when `s` is
  constant. Tidal interpolation error scales as that interval squared, not
  `r^2`. Use actual game timestamps, not FPS, unscaled time or OS UTC. Fixed
  real-second throttling is suitable for UI/telemetry, not basis validity.
- **Fastest retained wave, not M2 alone.** M4/M6/M8 are present [F5]; nodal
  corrections and inferred waves also affect the synthesized signal. For
  point samples spaced by `h`, frequencies `f` and `f+n/h` give identical
  samples (direct exponential identity). Thus `h < 1/(2*f_max)` is only a
  necessary band-limited sampling condition, not an integration/error target.
  Choose substantially tighter spacing from the error budget; no universal
  “one minute” or “1 Hz” recommendation follows.
- **Seasonal alias/double count.** Downsample time/spatial fields only after
  suitable offline filtering or explicit constituent separation. A box mean
  attenuates a sinusoid by `sin(omega*H/2)/(omega*H/2)`, not necessarily zero
  (derived integral). Monthly means are not an ideal anti-alias filter, nor
  permission to retain only six annual Fourier modes without loss tests.
  Sa/Ssa in FES can overlap seasonal content already present in a background;
  document which represented tidal constituents were removed or retained.
- **Correct instantaneous water is not correct interval transport.** A skipped
  interval needs path/time integration. For fixed coefficients and constant
  `f,omega`, the analytic integral uses sine/cosine endpoint differences;
  moving position, changing depths, routes, nodal terms and seams still require
  controlled substeps. Spatial variation adds an along-track frequency:
  a field wavelength `L` traversed at speed `V` varies at `V/L`.
  Per-query arithmetic cost can be small while catch-up work explodes.
- **Prefetch can lose the race under compression.** Disk latency is real time;
  distances/phase changes advance in game time. Predict working sets using
  game-time travel and all strategic groups, not only the player. Teleport,
  load and large skip invalidate ordinary neighbor-prefetch assumptions.
- **LRU thrash and order dependence.** Many remote groups can defeat a tiny
  player-centered cache. Bound and deduplicate miss queues, pin the step's
  working set and apply backpressure outside physics. Per-query LRU mutation,
  task creation or whole-map snapshot copying can become the actual hotspot.
- **Peak memory is not compressed file size.** For `N` stored horizontal
  nodes including halos, `D` depths, `S` seasons, `K` tidal constituents:
  int16 seasonal u/v payload is `4*N*D*S` bytes; int16 complex u/v tidal payload
  is `8*N*K` bytes for a depth-independent tidal layer. Float32-expanded caches
  double those payloads. Masks, coordinates, metadata and regional overlaps
  are additional; a depth-resolved tidal representation adds its depth factor.
  Multiply by resident tiles and add pinned-old snapshots, new candidates,
  decoder input/output/scratch, queues and shared basis buffers. Bound all
  simultaneously, including cancellation/reload bursts and unread results.
  Compute from manifest maxima; never assume a compression ratio.

### 7. Unity/Mono validation gates before claiming 60 fps

**Verified guidance.** Unity recommends near-zero frequent managed allocations,
collection/array reuse, and avoiding boxing, closures and array-returning APIs
in hot paths [U1]. Unity uses Boehm GC for Mono/IL2CPP and explicitly makes no
cross-version .NET-library performance/allocation guarantees [U3]. CPU Profiler
shows call counts, time and `GC.Alloc`; allocation call stacks can be recorded
without Deep Profiling, whose overhead can be high [U2].

**Proposed acceptance work, not results:**

- Resident `Sample`: target **0 B/query**, bounded loops, scalar/struct return,
  caller-owned buffers where needed; no LINQ, boxing, strings, exceptions on
  ordinary misses, IO, decompression or blocking synchronization.
  Keep trig in shared basis preparation; record preparation cost separately.
- Compile C# 7.0 against game references. Do not copy modern documentation
  examples using `using var`, Span-based APIs, records or new pool packages.
  .NET/CoreCLR microbenchmarks are not Mono/UBOAT acceptance.
- Profile warmed resident sampling, many hull probes/groups, distinct strategic
  timestamps, maximum supported compression, cold start, regional/year/depth
  seams, corrupt/missing blocks, cancellation and reload. Record calls per
  render frame **and** per simulated game hour; do not budget only one
  FixedUpdate per rendered frame.
- Measure main-thread/frame tails, shared-basis cost, loader CPU, decode/publish
  stalls, GC on **all** threads, peak managed/native memory, queue high-water
  marks and fallback counts. Background allocation still contributes to the
  shared managed heap; “no GC.Alloc in Sample” is necessary, not sufficient.
- Establish unoptimized float/reference fixtures first: phase wrap/cancellation,
  zeros, masked corners/depths, tile seams, dates/leap days, harmonic extrema,
  missing/corrupt inputs, reordered groups, slow loading and warm/cold caches.
  Compare identical game-time trajectories at different render FPS/compression.
  A source-level no-trig check cannot replace numeric oracle checks.

## Primary-source register

All links retrieved successfully on **2026-11-16**. CNES code links are pinned
to the commit above; hosted manuals/catalogues are mutable.

- **F1:** [CNES config.py, amplitude/phase conversion and Cartesian loading](https://github.com/CNES/aviso-fes/blob/9bf10a443d9fb47941a61c1daa9ff5789b8bca57/src/pyfes/config.py).
- **F2:** [CNES Cartesian model/interpolation](https://github.com/CNES/aviso-fes/blob/9bf10a443d9fb47941a61c1daa9ff5789b8bca57/include/fes/tidal_model/cartesian.hpp).
- **F3:** [CNES interpolation math and missing-corner normalization](https://github.com/CNES/aviso-fes/blob/9bf10a443d9fb47941a61c1daa9ff5789b8bca57/include/fes/detail/math.hpp).
- **F4:** [CNES WaveTableInterface synthesis](https://github.com/CNES/aviso-fes/blob/9bf10a443d9fb47941a61c1daa9ff5789b8bca57/src/library/interface/wave_table.cpp).
- **F5:** [CNES FES2014 eastward current configuration](https://github.com/CNES/aviso-fes/blob/9bf10a443d9fb47941a61c1daa9ff5789b8bca57/data/fes2014/eastward_velocity.yaml).
- **F6:** [CNES FES2014 data README](https://github.com/CNES/aviso-fes/blob/9bf10a443d9fb47941a61c1daa9ff5789b8bca57/data/fes2014/README.md).
- **F7:** [CNES prediction engines, arguments, corrections and defaults](https://cnes.github.io/aviso-fes/engines.html).
- **F8:** [CNES pinned LICENSE: BSD 3-Clause](https://github.com/CNES/aviso-fes/blob/9bf10a443d9fb47941a61c1daa9ff5789b8bca57/LICENSE).
- **F9:** [AVISO FES catalogue, data access and historical code citations](https://www.aviso.altimetry.fr/en/data/products/auxiliary-products/global-tide-fes.html).
- **C1:** [CF 1.12, §§2.5.1, 4.3, 5.6, 7.3–7.4, 8.1](https://cfconventions.org/Data/cf-conventions/cf-conventions-1.12/cf-conventions.html).
- **D1:** [Copernicus GLORYS12 product description](https://data.marine.copernicus.eu/product/GLOBAL_MULTIYEAR_PHY_001_030/description).
- **D2:** [Copernicus IBI product description](https://data.marine.copernicus.eu/product/IBI_MULTIYEAR_PHY_005_002/description).
- **D3:** [Copernicus NWS product description](https://data.marine.copernicus.eu/product/NWSHELF_MULTIYEAR_PHY_004_009/description).
- **N1:** [NIST DLMF §3.3, equations 3.3.5 and 3.3.14–15](https://dlmf.nist.gov/3.3).
- **U1:** [Unity 2022.3 GC best practices](https://docs.unity3d.com/2022.3/Documentation/Manual/performance-garbage-collection-best-practices.html).
- **U2:** [Unity 2022.3 CPU Profiler and allocation call stacks](https://docs.unity3d.com/2022.3/Documentation/Manual/ProfilerCPU.html).
- **U3:** [Unity 2022.3 .NET overview: Mono, GC, threading, library caveats](https://docs.unity3d.com/2022.3/Documentation/Manual/overview-of-dot-net-in-unity.html).
- **M1:** [Microsoft DeflateStream, .NET Standard 2.0 view](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.deflatestream?view=netstandard-2.0).
- **M2:** [Microsoft Interlocked.Exchange](https://learn.microsoft.com/en-us/dotnet/api/system.threading.interlocked.exchange?view=netstandard-2.0),
  [Volatile.Read](https://learn.microsoft.com/en-us/dotnet/api/system.threading.volatile.read?view=netstandard-2.0).

## Licence finding and remaining evidence gaps

**Actual PyFES repository licence checked:** pinned CNES `LICENSE` is
**BSD 3-Clause**, copyright CNES 2025 [F8]. It permits source/binary
redistribution with retained notices/disclaimer and no unauthorized endorsement.
This does **not** establish licences of every dependency or older release.
Notably, AVISO's current product page still asks users to cite the prediction
code as **GNU GPL**, including its PyFES paragraph [F9]. Record this discrepancy;
do not assert “all FES software is GPL” or retroactively apply BSD to a historical
LIBFES/PyFES version. Freeze the chosen source revision and inspect included
notices/dependencies before any code reuse/distribution (OC-048).

AVISO separately says FES2014 heights/currents are available for all purposes
under its linked data agreement, while FES2022 currents are not currently
distributed routinely and are offered on demand for scientific studies [F9].
The linked full data agreement was **not reviewed here**; this research is not
approval to redistribute a derived atlas.

No real NetCDF headers/data inspected, no independent 1939–1945 prediction
comparison run, no Unity/real-atlas performance measurement, and no confirmation
of installed UBOAT Unity/Mono profiler capabilities. Standalone synthetic
measurements below do not close those gaps.
Unity 2022.3 manuals support design reasoning, not a claim that this game uses
that exact engine/version/settings. Those gaps remain F4/F5/F8 work.

## Offline prototype follow-up (2026-11-16)

The measurements below are historical v4, not results of the portable
[review-corrected prototype](../../Dev/CurrentAtlasPrototype/). The latter
checks the actual workload anchors, composes component bounds in Euclidean
norm and compares snapshot/cursor vectors per query. Reproduction commands
are in [REALTIME](OCEAN_CURRENTS_REALTIME.md) §15.

Artifacts (evidence lives **outside version control**: `artifacts/` is gitignored,
so these paths are local-only and do not resolve on a clean clone):
`artifacts/prototypes/current-atlas-20261116/evidence-v4/results-summary.json`,
`.../evidence-v4/manifest.json`,
`.../analytic-qa-v2/qa.json`.
Host: AMD Ryzen 9 7950X, 16 cores/32 logical processors. These are synthetic
standalone observations, not UBOAT FPS or FES-predictor acceptance.

- Preserved v1 source/executable and original Framework/CoreCLR result hashes
  verified unchanged. Preservation was retrospective, before local fixes,
  not a pre-v1 execution source attestation.
- V1 direct/shared queries represented 123456 seconds; snapshot interpolation
  represented 123480 (anchors 123456/123516, weight 0.4). V2 still allowed
  refresh timing to overwrite tile 0's Tide1 before the 16-tile pass; executable
  regression failed in `results-refresh-red/run.log`. V3 isolated refresh
  outputs. V4 additionally re-anchors Tide0/Tide1 for every workload and runs a
  numeric preflight at 123480 before timing. V1/v2/v3 results were not overwritten.
- **V3 and v4 are not repeat measurements of identical code.** V4 adds
  `WorkloadPreflight` (`Program.cs`), so v3→v4 differences contain a code change,
  not only run-to-run noise. Single-run p99 is unstable; **no repeat-run
  dispersion (min/median/spread over N independent runs) exists yet**, so all
  values below are indicative, not a stable performance characterisation.
- CoreCLR v4, CLR 10.0.11/C# language 7: **34 checks**, **120 timing rows**,
  zero current-thread allocations in measured hot loops. All 24 workload groups
  passed direct/shared and snapshot/cursor checksum checks, plus a conservative
  packed-versus-snapshot checksum envelope.
- K34/16 tiles/8192 queries, normalized-batch p99, **v4**: Framework per-object
  trig **21.8782 ms**, float shared basis **8.1508 ms**, packed shared basis
  **6.0316 ms**, snapshots **0.7875 ms**; CoreCLR **7.51455 / 4.4736 / 3.7824 /
  0.6225 ms**. CoreCLR v3 was per-object 11.56825, float shared 8.2904,
  packed direct 4.43365, snapshots 0.69265 ms; those are historical and not a
  fair delta because the code changed.
- Separate refresh p95: Framework **7.7131 ms**, CoreCLR **5.3573 ms** (v3
  CoreCLR 5.1421). Memory DEFLATE+parse p95: Framework **15.1074 ms**, CoreCLR
  **6.3205 ms**, max 7.8262 ms (v3 CoreCLR 5.9875/16.5885).
- **Measured prototype defects.** Decode still allocates the float reference
  arrays (`Tile.Base`/`Tile.Coeff`): K34 is 574,600 + 67,600 floats ≈ **2.57 MB
  per tile**, above the LOH threshold; 16 tiles ≈ **41 MB transient LOH**. This
  is prototype-only and directly contradicts using this loader shape on the main
  thread. CoreCLR `DeflateStream.Fastest` output is **larger than raw**
  (1,288,657 → 1,358,071 B, +5.4%) in both v3 and v4, while Framework output is
  smaller (1,117,439 B); the writer must choose per-block raw when compression
  does not help, and the runtime must accept both. Refresh p95 5.14–7.71 ms and
  decode p95 5.99–15.11 ms are large fractions of a 16.67 ms frame; scheduling
  and a stall budget are required, not optional.
- **Frame-budget conversion is not available from these runs.** There was no
  queries-per-frame workload: the 0.62–0.69 ms snapshot figure is a normalized
  batch of 8192 queries, not a per-frame measurement. Derived per-query cost is
  0.0846 µs (v3) / 0.076 µs (v4) on this CPU; with assumed 2.5x/4x mid-range
  slowdowns that suggests roughly 1,478–2,631 queries per 0.5 ms and
  2,956–5,263 per 1.0 ms — assumptions, not measured on target hardware.
- Python stdlib analytic QA: **94 PASS / 0 FAIL** (`analytic-qa-v2`). JSON and
  separate CSVs cover phase wrap/cancellation, game-second interpolation bounds,
  slack water, fixed-coefficient analytic integration versus frozen/midpoint
  estimates over 1 h/6 h/M2 period/24 h/72 h/30 days, validity/depth distinctions
  and memory arithmetic without full-grid allocation. Python depth/component-mask
  policy fixtures do not establish that the C# prototype implements those masks:
  it does not.

**Measurement boundary:** p99 is an order statistic of 101 timings normalized
by repetitions, not game-frame p99. Shared basis preparation is outside sample
timers; basis computation is outside refresh timers. JIT/tiering conditions
were not controlled as Unity conditions. No real atlas/source masks, geographic
projection, disk IO, asynchronous publication, world integration or rendering
was exercised. K34 repeats eight synthetic periods, not 34 distinct astronomical
constituents. Allocation-free sampling does not establish allocation-free
loading or 60 fps.

## Offline coastal screening: Natural Earth and GEBCO (2026-11-21)

**Natural Earth 1:10m Land.** The [Land layer](https://www.naturalearthdata.com/downloads/10m-physical-vectors/10m-land/)
lists `ne_10m_land` as 5.1.1; the [publisher's pinned version file](https://raw.githubusercontent.com/nvkelso/natural-earth-vector/9380cca83db5f9aef52d5e762765100745f84b27/10m_physical/ne_10m_land.VERSION.txt)
confirms it at commit `9380cca83db5f9aef52d5e762765100745f84b27` (tag `v5.1.1`).
The [official ZIP](https://naturalearth.s3.amazonaws.com/10m_physical/ne_10m_land.zip)
has an unversioned URL; record the archive SHA-256 when acquiring it.
[Terms](https://www.naturalearthdata.com/about/terms-of-use/) put the published
map data in the public domain; modification and redistribution are allowed,
credit is optional. 1:10m means 1:10,000,000, a [country/regional map scale](https://www.naturalearthdata.com/downloads/),
not 10 m accuracy. Land is derived from a [generalized coastline](https://www.naturalearthdata.com/downloads/10m-physical-vectors/10m-coastline/);
the publisher explicitly excludes its separate [Minor Islands](https://www.naturalearthdata.com/downloads/10m-physical-vectors/10m-minor-islands/)
from Land and flags suspect coastline accuracy in northern Russia and southern
Chile. An apparent gap is not evidence that a narrow strait or harbor approach
is unobstructed; the sources give no guaranteed minimum resolvable width.

**GEBCO_2026 Grid + matching TID.** The [release specification](https://www.gebco.net/data-products-gridded-bathymetry-data/gebco2026-grid)
([DOI](https://doi.org/10.5285/4f68d5c7-45eb-f999-e063-7086abc036fa))
defines 15 arc-second pixel-centre elevations in metres, negative for depth;
15″ is grid spacing, not survey accuracy or a bound throughout a cell. TID
identifies source type, not uncertainty or datum: e.g. 11 = multibeam,
40 = satellite-gravity-guided prediction, 41 = algorithmic interpolation,
70 = pre-generated mixed-source grid. Horizontal coordinates can be treated
as WGS84. Compilation assumes mean sea level vertically, but some shallow-water
inputs use other vertical datums. Do not infer a
uniform conversion to the game's depth reference or safe clearance over a sill.
[Official downloads](https://www.gebco.net/data-products/gridded-bathymetry-data)
offer global NetCDF or raster tiles, area extracts and CEDA OPeNDAP; pin the
release, elevation variant (ice surface or sub-ice), extent and hashes for both
inputs. [Terms](https://www.gebco.net/data-products/gridded-bathymetry/terms-of-use)
describe the grid as public domain and allow adaptation, redistribution and
commercial use, but require source attribution and prohibit implied endorsement
or misrepresentation. No navigation or safety-at-sea use; underlying soundings
are not supplied with the grid.

**Application to [REALTIME](OCEAN_CURRENTS_REALTIME.md) §6.1.** Use these
sources offline to flag potential coast/depth conflicts, not to replace either
the model's wet support or the game's geometry. A gap between polygons or deep
cell centres cannot establish wet support across a whole interpolation footprint
or at depth. Unverified straits/approaches stay `Unsupported`, not `Valid` or
zero current. This review did not inspect geometry or raster files, validate
any site, or measure FPS.

### Measured overlay: installed UBOAT vector coast vs. Natural Earth Land

**Offline measurement on real files; not an in-game or hydrodynamic test.**
Compared `Coast Quad Tree.dat` from the installed UBOAT via the existing
`CoastQuadTreePort` and `LighthouseLandGate` projection, against the official
Natural Earth Land 5.1.1 ZIP. Both were sampled at the same 0.005° lattice,
including edges; zero signed game distance and points on the polygon boundary
are not counted as water. Coarse test cells are 0.05°; sizes in metres vary by
latitude. These are test cells, not proposed native atlas resolution.

| Window (longitude; latitude) | Points | Different land/water classification | Cells with four corners wet in both maps, but a non-water sample inside or on an edge | Cells with four corners wet in both maps, but intersecting a Natural Earth land polygon |
|---|---:|---:|---:|---:|
| Gibraltar (−6.4…−5.0; 35.7…36.4) | 39,621 | 32 | 1 / 392 | 1 / 392 |
| Jade approach (7.7…8.6; 53.3…54.1) | 29,141 | 71 | 8 / 288 | 8 / 288 |
| Open Atlantic (−22.0…−21.5; 46.0…46.5) | 10,201 | 0 | 0 / 100 | 0 / 100 |

The last column tests the **entire rectangle** against Natural Earth polygons;
it is not based on the 0.005° samples. At Jade, 53 sampled points were water
only in the game vector mask and 18 only in Natural Earth. Eight disagreements
were outside the lighthouse test's ±0.3 projected-km diagnostic band (not an accuracy
guarantee or an atlas threshold). The two sources are not guaranteed independent
in provenance; agreement does not establish true coastline or actual clearance.
Even the very close masks do not support a four-corner admission rule. Reject
those footprints unless a smaller independently supported stencil is available.

**Reproduce:** `dotnet build Dev/CurrentAtlasPrototype/CoastCompare/CoastSamples.csproj -c Release -o <build-dir>`;
then `python Dev/CurrentAtlasPrototype/CoastCompare/compare.py --coast <Coast-Quad-Tree.dat> --land-zip <ne_10m_land.zip> --samples-dll <build-dir>/CoastSamples.dll --output <new.json>`.
Used .NET 10, NumPy 2.4.6, Shapely 2.2.0 and pyshp 3.0.3. Two independent
runs produced byte-identical JSON (`60f4d121924cace8bece852dc537739a95a0c86edbec797b20d258dfabe9851e`).
Input SHA-256: game coast `43175e2aa909db335bded3281b489d36db57d545c4a515fd382aa3306294cd5b`;
Natural Earth ZIP `e547d749445eaa0964aba76738090ec88f5e63c4585122170f98c67a7ea922dc`.
No game assets are shipped by the witness.

**Not checked:** full-footprint coverage against game polygons (the game port
is queried at points), native terrain/navmesh, GEBCO and TID values, actual
current products, wind, hydraulic connectivity and flow, or Unity frame time.
Game coast sign is only one witness: the port uses a signed ±4600 km sentinel
for edge-less leaves, not a usable clearance distance. This result does not
qualify either coastal window for current simulation.

### Measured depth support in the installed world raster

**Read-only offline measurement, not generated scene terrain.** In
`WorldMaps.cs:93–131,214–243,348`, UBOAT maps encounter km to north-to-south
rows and bilinearly interpolates float samples from padded 256×256 tiles.
The data path is `etopo1_bed_g_f4.dat`; its cold path can lock and read files.
Do not call it per current sample.
`WorldSegmentGenerator.cs:1496–1532` extracts base elevation for a scene
segment, but its job modifies it with GPU and other inputs (`:110–153`).
`Hull.cs:1714–1739` distinguishes generated terrain from the global
map. The global raster cannot replace a scene bottom/collision check.

`CoastCompare/depth_audit.py` splits every 0.05° test rectangle at native
raster grid lines. A bilinear patch reaches its min/max at rectangle corners,
so extrema over the clipped subrectangles bound the **base interpolant**,
not just sampled centres. The witness uses float64 weights and ideal
geographic coordinates; it is not bit parity with native float calls.
The installed tiled `.dat` and flat `.flt` matched on **7,608 intersecting
native nodes** across the three windows. Both files are opened read-only.

| Window | Test cells | Threshold | Whole cell deeper | Centre passes, footprint fails | All four corners pass, footprint fails |
|---|---:|---:|---:|---:|---:|
| Gibraltar | 392 | 20 m | 195 | 43 | 2 |
| Gibraltar | 392 | 200 m | 99 | 44 | 3 |
| Jade approach | 288 | 0 m | 194 | 42 | 20 |
| Jade approach | 288 | 10 m | 53 | 21 | 1 |
| Open Atlantic | 100 | 200 m | 100 | 0 | 0 |

Counts exclude threshold ties within 1 cm in this diagnostic; that is not a
physical clearance or datum allowance. No intersection with the vector coast
masks was used for these counts. A passing cell is **not** qualified for
current simulation: velocity support, datum compatibility and generated
terrain remain separate checks.

**Reproduce:** `python Dev/CurrentAtlasPrototype/CoastCompare/depth_audit.py --tiled <etopo1_bed_g_f4.dat> --flat <etopo1_bed_g_f4.flt> --output <new.json>`.
NumPy 2.4.6. SHA-256: `.dat`
`142101cf8af7fdc04da744fa36fe5fdc10168f9540772bd910c51edcb43670c8`;
`.flt` `93d676ef3c53b150e87037acf43ac0707cc849b685edbcadc5ea47320a6a4c1a`.
Two runs produced identical JSON
(`501c6fffd2608372c68900e8ed46609549efbedec6be5c9535254f08d9a11c84`).
No bathymetry grids are shipped. GEBCO/TID, real velocity files, native
terrain and Unity frame time were not exercised in this measurement.

### Measured velocity-mask and vertical decimation on HYCOM NetCDF

**Offline measurement on real model output, not observations or WWII data.**
Used the public [NOAA CoastWatch ERDDAP dataset nrlHycomGLBu008e910D](https://coastwatch.pfeg.noaa.gov/erddap/griddap/nrlHycomGLBu008e910D.html)
(`GLBu0.08/expt 91.0`) for the three coastal-audit regions, rounded by the
server to native nodes. Checked three samples at 00:00 UTC on 6–8 April 2014;
each cube contains 23 native depth levels between 0 and 200 m. This is an
import/decimation witness only. It neither selects the shipping source nor
licenses a tide residual: no additional tide, Stokes or wind has been added.

Actual files were classic NetCDF, 32-bit floats, ordered
`time, depth, latitude, longitude`, positive-down depth in metres and
east/north components in m/s. Both components used finite fill `−30.0`,
without `scale_factor` in this ERDDAP export. Neither a u/v mask mismatch nor
a mask change across these three dates was seen. Preserve intersection of u/v
validity and check all intermediate native levels; the synthetic packed and
missing-interior cases still fail if their guards are removed. Native profiles
in this sample had no invalid intermediate level between valid endpoints.

| Region | Valid vectors at 0 / 200 m (node × date) | Surface held to depth, p95 / max (m/s) | Six-level interpolation, p95 / max (m/s) |
|---|---:|---:|---:|
| Gibraltar | 369 / 231 | 0.378 / 1.317 | 0.062 / 0.463 |
| Jade approach | 117 / 0 | 0.343 / 0.512 | 0.065 / 0.091 |
| Open Atlantic | 147 / 147 | 0.173 / 0.253 | 0.030 / 0.049 |

The six levels were `0, 10, 20, 50, 100, 200 m`. The worst Gibraltar error
occurred between 100 and 200 m. Holding the surface vector was worse than
just losing magnitude: in Gibraltar, **837 of 5,956** valid surface/depth
pairs with both speeds ≥0.05 m/s differed in direction by more than 90°.
Errors are Euclidean differences from **that model's** native samples at the
same time and horizontal node, not position errors or an accuracy claim.

Compared with `0, 4, 10, 15, 20, 30, 40, 50, 70, 100, 125, 150, 200 m`.
On **identical valid queries omitted by both profiles**, sparse → denser
p95 was 0.040 → 0.020 m/s (Gibraltar, 3,381), 0.061 → 0.029 m/s (Jade,
393), and 0.029 → 0.015 m/s (Atlantic, 1,470). Jade's max did **not**
improve (0.09125 → 0.09150 m/s). The denser profile also supported 60/27/0
additional omitted-depth queries, because a narrower bracket could end above
the missing deeper endpoint. That is source-column support, not game
clearance. No global profile or error tolerance is accepted by this test.

**Reproduce:** `python Dev/CurrentAtlasPrototype/CoastCompare/velocity_audit.py --output-dir <new-dir>`; use `--input-dir <saved-nc-dir>` for replay. NumPy 2.4.6,
SciPy 1.18.0. Two offline replays produced matching JSON SHA-256
`922776fa13fed3e63323d61847f000a650e5bf18ce30f44affee0de8aade7c10`.
Input SHA-256, Gibraltar `e24f2108e57c51c0c95a238b25b52ca530c12fb3158e13ea63fa9eae211b8098`,
Jade `0e90ac6aff5f3e718d26bba6c454592434157f704f6ea8a7b5de285da8de2bd8`,
Atlantic `1c321e7f894fc7d981c282d37b9046c9c47df7fca41a34a4a337955540ac35c9`.
The NetCDF files are not required for a clean checkout. This did not test
horizontal interpolation, release-provider masks, seasons, native terrain,
paths, historical tide predictions, wind response, or Unity FPS.

#### Follow-up: potential mismatch at a shared horizontal node

`python Dev/CurrentAtlasPrototype/CoastCompare/seam_qa.py --input-dir <saved-nc-dir>`
evaluates both vertical profiles at identical native depths and midpoints
(45 depths). Each horizontal node/date is a possible shared anchor; this is
not a measured geographic tile seam. The raw profile difference on jointly
valid queries (p95/max m/s) was Gibraltar **0.0423/0.4628** (14,685), Jade
**0.0542/0.0706** (1,539), Atlantic **0.0210/0.0493** (6,615). Additionally
the profiles disagreed on support for 390/108/0 depth×position×date queries.
Selecting the denser trace for both sides removes that representation
mismatch where the shared support is admitted, but does not make the trace
exact: on the same Gibraltar queries its maximum error vs. the native
piecewise-linear trace was **0.0708 m/s**. The witness does not authorize
merging incompatible products or replacing the whole-cell support guard.

## Historical North Atlantic currents: November 1941 (2026-11-22)

**ORA-20C — working small download for a local experiment.** The Hamburg ICDC
[record](https://www.geo.uni-hamburg.de/en/service/icdc/data/ocean/easy-init-ocean/ecmwf-ensemble-of-ocean-reanalyses-of-the-20th-century-ora-20c.html)
lists its archive as 1900–2009: NEMO/LIM2 with three-hour ERA-20C surface fluxes
(CORE bulk formula), EN4 temperature/salinity assimilation and HadISST2 SST
relaxation. The files are model estimates, not direct current observations.
The ICDC OPeNDAP slices for [`uo`](https://icdc.cen.uni-hamburg.de/thredds/dodsC/ftpthredds/EASYInit/ora20c/opa0/uo_ora20c_1m_1941_grid_1x1.nc.gz.ascii?uo%5B10%5D%5B0%3A1%3A16%5D%5B136%3A137%5D%5B337%3A338%5D,mask%5B0%3A1%3A16%5D%5B136%3A137%5D%5B337%3A338%5D,depth%5B0%3A1%3A16%5D,lat%5B136%3A137%5D,lon%5B337%3A338%5D,time%5B10%5D,time_counter_bnds%5B10%5D%5B0%3A1%5D)
and [`vo`](https://icdc.cen.uni-hamburg.de/thredds/dodsC/ftpthredds/EASYInit/ora20c/opa0/vo_ora20c_1m_1941_grid_1x1.nc.gz.ascii?vo%5B10%5D%5B0%3A1%3A16%5D%5B136%3A137%5D%5B337%3A338%5D,mask%5B0%3A1%3A16%5D%5B136%3A137%5D%5B337%3A338%5D,depth%5B0%3A1%3A16%5D,lat%5B136%3A137%5D,lon%5B337%3A338%5D,time%5B10%5D,time_counter_bnds%5B10%5D%5B0%3A1%5D)
returned HTTP 200 (5,175 bytes total): member `opa0`, WOA 1° grid, 46.5–47.5°N,
22.5–21.5°W (file longitude 337.5–338.5°E), 17 of 42 levels at 5.02–207.43 m
positive down. All 68 vectors were finite and both masks were 1 at those nodes.
The [file attributes](https://icdc.cen.uni-hamburg.de/thredds/dodsC/ftpthredds/EASYInit/ora20c/opa0/uo_ora20c_1m_1941_grid_1x1.nc.gz.das)
label the fields zonal/meridional current in `m s**-1` (m/s), fill `9.96921E36`.
Time index 10 is centred on 1941-11-16 00:00, with bounds Nov 1–Dec 1: a monthly
mean, not the instantaneous current on Nov 16. No full-year files downloaded.
Reply SHA-256: `uo` `5d89c5491f487575e9a0165b4530eeb0a8037b2f46e3c0d32380b622b5da9d02`;
`vo` `395f0854cc8851232766f90fef581fabd1319f82da560e76dff60c1519de3dbe`.
Public ICDC access is not itself a data licence. The
[ECMWF announcement](https://forum.ecmwf.int/t/cc-by-licence-to-replace-licence-to-use-copernicus-products-on-02-july-2025/13464)
confirms CC BY 4.0 for Copernicus products through CDS, ADS and EWDS. Neither
that announcement nor the ECMWF Open Data terms identifies this ORA-20C
OPeNDAP collection. Project decision: retain derived tiles with ECMWF/ICDC
attribution and without claiming a verified dataset-specific grant. The
current package, axes, and source hashes are in `Source/Data/CurrentAtlas/`.
Raw replies are not bundled; no endorsement is implied.

**SODA 2.2.4 — clearer redistribution terms, no working data slice found.**
[Giese and Ray](https://marinesciences.uconn.edu/wp-content/uploads/sites/459/2018/04/2011-GieseRay.pdf)
and [Hamburg ICDC](https://www.geo.uni-hamburg.de/en/service/icdc/data/ocean/easy-init-ocean/soda-224.html)
describe POP, 1871–2008 and NOAA 20CRv2 atmospheric forcing. The NOAA
[Upwell metadata](https://upwell.pfeg.noaa.gov/erddap/info/hawaii_d90f_20ee_c4cb_LonPM180/index.csv)
advertise monthly 1871–2010 on a 0.5° grid, 40 depth levels (5.01–5375 m), and
eastward `u` / northward `v` in `m s-1`. Its legacy COADS/NCEP summary conflicts
with the version-specific sources; do not use it to infer 1941 forcing.
The [APDRC DataDoc](http://apdrc.soest.hawaii.edu/datadoc/soda_2.2.4.php) redirects
HTTP→HTTPS then returns 404, as does the APDRC DAP header on HTTPS. Upwell serves
metadata but the small `.csv`/`.nc` queries redirect to
[old NOAA CoastWatch](https://coastwatch.pfeg.noaa.gov/erddap/griddap/hawaii_d90f_20ee_c4cb_LonPM180.html),
which returns 404. The [current UMD site](https://soda.umd.edu/) lists SODA3/4,
not a working 2.2.4 download. Upwell's dataset `license` explicitly permits free
redistribution, with a disclaimer; the [NOAA catalogue entry](https://catalog.data.gov/dataset/soda-pop-2-2-4-monthly-means-1871-2010-at-depths-lon-180)
lists CC0. Retain attribution and disclaimer, and match any recovered bytes to
that exact product before relying on these terms for a release.

**Other checks.** [GECCO](https://www.geo.uni-hamburg.de/en/service/icdc/data/ocean/easy-init-ocean/gecco.html)
starts in 1952; [GECCO2](https://www.geo.uni-hamburg.de/en/service/icdc/data/ocean/easy-init-ocean/gecco2.html)
and [GECCO3](https://www.geo.uni-hamburg.de/en/service/icdc/data/ocean/easy-init-ocean/gecco3.html)
offer CC BY 4.0 data but begin in 1948. [CERA-20C](https://www.ecmwf.int/en/newsletter/150/meteorology/cera-20c-earth-system-approach-climate-reanalysis)
is coupled and covers 1901–2010, but ECMWF [retired the public access route](https://www.ecmwf.int/en/forecasts/datasets/browse-reanalysis-datasets);
no working multilevel velocity subset or applicable data licence was verified.
[CMCC CHOR/CHORE](http://c-glors.cmcc.it/index/index-3.html?sec=2) span 1900–2010,
with 20CRv2/ERA-20C forcing respectively; [FTP requires registration](http://c-glors.cmcc.it/index/index-7.html?sec=7).
No velocity subset or redistribution terms were verified there.

**Scope of the first witness:** the two ORA-20C replies checked joint support
and depth interpolation without a 0 m value. The expanded Atlantic package
uses an explicitly reconstructed 0 m level and keeps URLs, hashes, monthly
centres and masks (plan §13.33). Neither package validates coastal tides or
daily weather; modern HYCOM remains an import witness, not a historical
substitute.

**Local RNW1 fixture at the campaign position.** `ora20c_trial.py` acquired
2,824 bytes for October/November 1941, 47.5–48.5°N, 343.5–344.5°E,
five depths from 5.02 to 45.45 m. All 40 vectors were supported.
Maximum observed vector quantization error was `6.53e-5 m/s`.
The 260-byte tile uses the two monthly centres as interpolation anchors,
not individual same-day observations; there is no extrapolation to the
surface. Provenance and both SHA-256 values are in
`artifacts/reports/current-ora20c-local-1/manifest.json`; raw and derived
data stay local pending licensing.
