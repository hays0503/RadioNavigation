"""Short, offline VIIC Full/Simple regression. Requires the earlier R2 fixture."""
import argparse
import csv
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import subprocess
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
WATER_SHA256 = "dea130b63519153c306b3e115b91983a916a80a9be0f1955f14ad0d4e6e8f454"
GAME_SHA256 = "b1dcee2c63f6d33464c33d6dcce1dcc33acaf8a4da04ef7a2cd9d2fd4890f16f"
STEPS = 1200


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError(f"Unexpected R2 fixture: {old}")
    return source.replace(old, new)


def prepare(fixture, project):
    dll = fixture / "Assets/Plugins/com.playway.water.Runtime.dll"
    digest = hashlib.sha256(dll.read_bytes()).hexdigest()
    if digest != WATER_SHA256:
        raise ValueError(f"Refusing unverified water DLL: {digest}")
    bridge = (fixture / "Assets/NativeGameBridge.cs").read_text(encoding="utf-8-sig")
    managed = re.search(r'const string Managed\s*=\s*@"([^"]+)"', bridge)
    if managed is None:
        raise ValueError("Could not resolve the game DLL path from the fixture")
    game_dll = Path(managed[1]) / "com.uboat.game.dll"
    if hashlib.sha256(game_dll.read_bytes()).hexdigest() != GAME_SHA256:
        raise ValueError("Refusing unverified game DLL")
    for folder in ("Assets", "Packages", "ProjectSettings"):
        shutil.copytree(fixture / folder, project / folder)
    shutil.copy2(Path(__file__).with_name("NativeSubmergedCurrentProbe.cs"), project / "Assets")
    # The production drag step is exercised by the stand, not a stand copy.
    shutil.copy2(ROOT / "Source/Logic/CurrentSurfaceDragStep.cs", project / "Assets")
    for name in ("CurrentRuntimeAdapter", "CurrentRudderAdapter", "CurrentDiagnostics", "ComparisonExtensions", "WarnOnce"):
        shutil.copy2(ROOT / f"Source/Logic/{name}.cs", project / "Assets")
    shutil.copy2(ROOT / "Source/Logic/CurrentProfileField.cs", project / "Assets")
    shutil.copy2(ROOT / "Source/Logic/CurrentAtlasField.cs", project / "Assets")
    shutil.copy2(ROOT / "Source/Logic/CurrentWitnessTile.cs", project / "Assets")
    shutil.copy2(Path(__file__).with_name("RuntimeCurrentFixture.cs"), project / "Assets")
    shutil.copy2(Path(__file__).with_name("NativeCurrentRudderProbe.cs"), project / "Assets")
    shutil.copy2(Path(__file__).with_name("NativeCurrentControlsProbe.cs"), project / "Assets")
    shutil.copy2(Path(__file__).with_name("NativeWindVectorProbe.cs"), project / "Assets")
    shutil.copy2(Path(__file__).with_name("NativeCurrentAssetProbe.cs"), project / "Assets")
    shutil.copy2(ROOT / "Source/Logic/ConstantCurrentField.cs", project / "Assets")
    runtime = (ROOT / "Source/Logic/WindProbeRuntime.cs").read_text(encoding="utf-8-sig")
    wind_trace = runtime[runtime.index("    internal static class SceneWindTrace"):]
    (project / "Assets/SceneWindTrace.cs").write_text(
        "using System;\nusing UnityEngine;\nnamespace UBOAT.Mods.RadioNavigation\n{\n" + wind_trace, encoding="utf-8")

    driver = project / "Assets/DriftDriver.cs"
    text = driver.read_text(encoding="utf-8-sig")
    text = replace_once(text, "bool freeDrift,calmSea;", """bool freeDrift,calmSea,currentMixed,currentSurface,currentDive,currentPerf,currentFree;
    string currentHold;
    string currentRuntime;
    float currentInitialNorth, hullTop, currentSwitch;
    bool currentSwitchReceipt;
    string currentPrefab;

    void SetFixtureDepth(float height)
    {
        var position = new Vector3(body.position.x, height, body.position.z);
        body.transform.SetPositionAndRotation(position, body.rotation);
        Physics.SyncTransforms();
        body.position = position;
    }""")
    text = replace_once(text, "QaTelemetry.Initialize(output);", """QaTelemetry.Initialize(output);
            currentMixed = Arg("-currentMixed","0") == "1";
            currentSurface = Arg("-currentSurface","0") == "1";
            currentDive = Arg("-currentDive","0") == "1";
            currentPerf = Arg("-currentPerf","0") == "1";
            currentFree = Arg("-currentFree","0") == "1";
            currentHold = Arg("-currentHold","");
            currentRuntime = Arg("-currentRuntime","");
            RuntimeCurrentFixture.Install(currentRuntime);
            currentPrefab = Arg("-currentPrefab","");
            currentSwitch = float.Parse(Arg("-currentSwitch","0"), Inv);
            currentInitialNorth = float.Parse(Arg("-currentInitialNorth","0"), Inv);
            currentSwitchReceipt = currentInitialNorth != 0f || Arg("-currentSwitchReceipt","0") == "1";
            if (Arg("-currentWindVector","0") == "1") {
                NativeWindVectorProbe.Run(output);
                finished = true;
                UnityEditor.EditorApplication.Exit(0);
                return;
            }
            if (Arg("-currentAssets","0") == "1") {
                NativeCurrentAssetProbe.Run(output);
                finished = true;
                UnityEditor.EditorApplication.Exit(0);
                return;
            }
            if (Arg("-currentControls","0") == "1") {
                NativeCurrentControlsProbe.Run(output, Arg("-currentSignal",""));
                finished = true;
                UnityEditor.EditorApplication.Exit(0);
                return;
            }
            if (Arg("-currentRudder","0") == "1") {
                NativeCurrentRudderProbe.Run(output);
                finished = true;
                UnityEditor.EditorApplication.Exit(0);
                return;
            }
            if (Arg("-currentPatch","0") == "1") NativeSubmergedCurrentProbe.Install(currentSurface);""")
    text = replace_once(text, 'prefab=hullBundle.LoadAsset<GameObject>("Entities/Type VIIC");', """
            prefab=hullBundle.LoadAsset<GameObject>("Entities/Type VIIC");
            if (currentPrefab != "") {
                string basePath = Path.Combine(Path.GetDirectoryName(NativeGameBridge.Managed),
                    "StreamingAssets/Packages/uboat/Bundles");
                bool freighter = currentPrefab == "liberty";
                hullBundle = AssetBundle.LoadFromFile(Path.Combine(basePath,
                    freighter ? "uboat.entities.liberty" : "uboat.entities.lifeboats"));
                prefab = hullBundle.LoadAsset<GameObject>(freighter ? "Entities/Liberty" : "Entities/Lifeboat 01");
            }""")
    text = replace_once(text, "body.mass=playerInputs.baseMass;", "if (currentPrefab == \"\") body.mass=playerInputs.baseMass;")
    text = replace_once(text, "foreach(var behaviour in instance.GetComponentsInChildren<Behaviour>(true)){", """
         if (currentPrefab != "") {
             // Required equipment can survive component removal on other
             // hulls. Leave the entire native prefab inert and build a
             // physics-only clone rather than awakening any equipment.
             var originalHull = instance.GetComponentInChildren<NeuralNetworkPhysics>(true);
             var originalBody = originalHull.GetComponent<Rigidbody>();
             if (originalBody == null)
                 throw new Exception("Expected transport water body and Rigidbody on the same object");
             var physicsOnly = new GameObject("Offline hull");
             physicsOnly.transform.SetParent(parent.transform, false);
             var copyBody = physicsOnly.AddComponent<Rigidbody>();
             copyBody.mass = originalBody.mass;
             copyBody.drag = originalBody.drag;
             copyBody.angularDrag = originalBody.angularDrag;
             foreach (var source in originalHull.GetComponentsInChildren<Collider>(true)) {
                 if (source.isTrigger || !source.enabled)
                     continue;
                 var mesh = source as MeshCollider;
                 var box = source as BoxCollider;
                 if (mesh == null && box == null)
                     continue;
                 var holder = new GameObject(source.name);
                 holder.transform.SetParent(physicsOnly.transform, false);
                 holder.transform.localPosition = originalHull.transform.InverseTransformPoint(source.transform.position);
                 holder.transform.localRotation = Quaternion.Inverse(originalHull.transform.rotation) * source.transform.rotation;
                 holder.transform.localScale = source.transform.lossyScale;
                 if (mesh != null) {
                     var copy = holder.AddComponent<MeshCollider>();
                     copy.sharedMesh = mesh.sharedMesh;
                     copy.convex = mesh.convex;
                 } else {
                     var copy = holder.AddComponent<BoxCollider>();
                     copy.center = box.center;
                     copy.size = box.size;
                 }
             }
             var copyHull = physicsOnly.AddComponent<NeuralNetworkPhysics>();
             UnityEditor.EditorUtility.CopySerialized(originalHull, copyHull);
             DestroyImmediate(instance);
             instance = physicsOnly;
         }
         foreach(var behaviour in instance.GetComponentsInChildren<Behaviour>(true)){""")
    text = replace_once(text, "hull.DragSimulationSteps=50;", "if (currentPrefab == \"\") hull.DragSimulationSteps=50;")
    text = replace_once(text, "float deckDepth=native.DeckDepth(elevation);", """if (currentPrefab == "") {
                 float deckDepth=native.DeckDepth(elevation);""")
    text = replace_once(text, "native.ApplyBowLift(bowForward);", "native.ApplyBowLift(bowForward);\n                 }")
    text = replace_once(text, "VerifyCoordinateContracts();", """VerifyCoordinateContracts();
         Set(hull, "enforceSafeMargin", false);
         SetFixtureDepth(currentSurface ? 0f : -30f);
         if (currentHold == "kinematic") body.isKinematic = true;
          body.constraints = currentHold == "freeze-all" ? RigidbodyConstraints.FreezeAll :
              (currentFree || currentHold == "kinematic" ? RigidbodyConstraints.None :
              RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation);
         body.velocity = new Vector3(0, 0, currentInitialNorth);
         float east = float.Parse(Arg("-east","0"), Inv);
         if (currentSurface) {
             foreach (var collider in body.GetComponentsInChildren<Collider>())
                 hullTop = Mathf.Max(hullTop, collider.bounds.max.y - body.position.y);
             if (hullTop <= 0f || hullTop >= 60f)
                 throw new Exception("Invalid VIIC collider bounds: " + hullTop.ToString("R", Inv));
             NativeSubmergedCurrentProbe.BindSurface(hull, east, float.Parse(Arg("-airEast","0"), Inv));
          } else NativeSubmergedCurrentProbe.Bind(hull, east);
          if (currentRuntime != "")
              RuntimeCurrentFixture.Bind(hull, east, float.Parse(Arg("-airEast","0"), Inv), Arg("-currentKind",""));""")
    text = replace_once(text, 'if(body.velocity!=Vector3.zero||body.angularVelocity!=Vector3.zero||body.constraints!=RigidbodyConstraints.None)throw new Exception("FREE_INITIAL_CONTRACT_FAIL");',
                        'if(body.velocity!=new Vector3(0,0,currentInitialNorth)||body.angularVelocity!=Vector3.zero)throw new Exception("F2_INITIAL_CONTRACT_FAIL");')
    text = replace_once(text, r'"free_drift=true\ninitial_velocity=0,0,0\ninitial_angular_velocity=0,0,0\ninitial_heading="',
                        r'"free_drift=true\ninitial_velocity=0,0,"+currentInitialNorth.ToString("R",Inv)+"\ninitial_angular_velocity=0,0,0\ninitial_heading="')
    text = replace_once(text, 'if(body.constraints!=RigidbodyConstraints.None)throw new Exception("FREE_CONSTRAINTS_CHANGED");',
                         'if(body.constraints!=(currentHold == "freeze-all" ? RigidbodyConstraints.FreezeAll : currentFree || currentHold == "kinematic" ? RigidbodyConstraints.None : RigidbodyConstraints.FreezePositionY|RigidbodyConstraints.FreezeRotation))throw new Exception("FIXTURE_CONSTRAINTS_CHANGED");')
    text = replace_once(text, "Physics.autoSimulation=true;nativeRunner.FixedUpdate();Physics.autoSimulation=false;", """
                 if (currentMixed && (step == 300 || step == 900))
                     hull.UpdateIntervals = step == 300 ? 3 : 0;
                 if (currentDive && step >= 240 && step <= 720)
                     SetFixtureDepth(Mathf.Lerp(0f, -hullTop - 1f, (step - 240) / 480f));
                 if (currentDive && step >= 900 && step <= 1140)
                     SetFixtureDepth(Mathf.Lerp(-hullTop - 1f, 0f, (step - 900) / 240f));
                  if (currentSurface) NativeSubmergedCurrentProbe.SetExposure(body.position.y + hullTop);
                  if (currentRuntime != "") RuntimeCurrentFixture.Tick(currentSurface ? body.position.y + hullTop : 0f);
                 if (currentSwitch != 0f && (step == 362 || step == 722))
                     NativeSubmergedCurrentProbe.SetSurfaceWater(step == 362 ? currentSwitch : 0f);
                 Vector3 beforeCurrent = body.velocity;
                 Physics.autoSimulation=true;
                 long nativeStart = System.Diagnostics.Stopwatch.GetTimestamp();
                 nativeRunner.FixedUpdate();
                 NativeSubmergedCurrentProbe.RecordNativeTicks(System.Diagnostics.Stopwatch.GetTimestamp()-nativeStart);
                 Physics.autoSimulation=false;""")
    text = replace_once(text, "t+=Dt;step++;PumpSamples();", """t+=Dt;step++;PumpSamples();
                 if (!currentPerf && ((step >= 300 && step <= 304) || step == 900 || step == 901))
                     NativeSubmergedCurrentProbe.RecordModeBoundary(output, step, body.velocity.x);""")
    text = replace_once(text, "QaTelemetry.EndStep(worldX);", """QaTelemetry.EndStep(worldX);
                 if (currentSurface && !currentPerf &&
                     (step <= 2 || (currentDive ? step % 4 == 0 : (step >= 300 && step <= 304))))
                     NativeSubmergedCurrentProbe.RecordSurface(output, step, body.position.y, body.velocity.x);
                 if (currentSurface && currentSwitchReceipt &&
                     ((step >= 359 && step <= 364) || (step >= 719 && step <= 724)))
                     NativeSubmergedCurrentProbe.RecordFieldSwitch(output, step, beforeCurrent, body.velocity);""")
    text = replace_once(text, "Record();writer.Flush();writer.Dispose();writer=null;",
                         "if (currentRuntime != \"\") RuntimeCurrentFixture.Write(output);"
                         "NativeSubmergedCurrentProbe.WriteReceipt(output);Record();writer.Flush();writer.Dispose();writer=null;")
    driver.write_text(text, encoding="utf-8")


def run_case(unity, project, work, spec, index, total, extra=(), north=None):
    name, patched, east, mixed = spec
    output = work / name
    output.mkdir()
    log = output / "unity.log"
    north = (0 if east or extra else 2) if north is None else north
    command = [str(unity), "-batchmode", "-nographics", "-projectPath", str(project),
               "-executeMethod", "DriftProbe.Launch", "-logFile", str(log),
               "-driftOutput", str(output), "-driftCase", "1", "-driftOneCase", "1",
               "-driftSteps", str(STEPS), "-driftNativeFft", "0", "-driftNativeNetwork", "0",
               "-freeDrift", "1", "-calmSea", "1", "-driftStorm", "0",
               "-currentPatch", str(int(patched)), "-east", str(east), "-currentMixed", str(int(mixed)),
               "-currentInitialNorth", str(north), *extra]
    print(f"{index}/{total} ({100*(index-1)//total}%) {name}; log: {log}; timeout 180 s", flush=True)
    start = time.monotonic()
    subprocess.run(command, check=True, timeout=180)
    if any(arg in extra for arg in ("-currentRudder", "-currentControls", "-currentWindVector", "-currentAssets")):
        return None
    with (output / "current-native.csv").open(newline="") as receipt:
        row = next(csv.DictReader(receipt))
    with (output / "free-motion.csv").open(newline="") as trajectory:
        samples = list(csv.DictReader(trajectory))
    final = samples[-1]
    require(int(row["runnerCalls"]) == int(final["step"]) == STEPS, name + ": incomplete run")
    require(extra or all(abs(float(sample["height_m"]) + 30.0) < 0.001 for sample in samples),
            name + ": fixture left the fixed-depth layer")
    require(abs(float(samples[0]["vz_mps"]) - north) < 1e-5,
            name + ": wrong initial forward speed")
    print(f"  x={final['x_m']} m vx={final['vx_mps']} m/s {row} wall={time.monotonic()-start:.1f}s", flush=True)
    return float(final["x_m"]), float(final["vx_mps"]), row


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def verify(work, result):
    for baseline, zero in (("stock-full", "zero-full"), ("stock-mixed", "zero-mixed")):
        require((work / baseline / "free-motion.csv").read_bytes() ==
                (work / zero / "free-motion.csv").read_bytes(), zero + ": stock trajectory changed")
    for name, sign in (("east-full", 1), ("west-full", -1), ("east-mixed", 1)):
        x, vx, row = result[name]
        require(math.isfinite(x) and math.isfinite(vx) and 0.02 < sign*x < 16 and 0.002 < sign*vx < 1,
                name + ": invalid direction or speed")
        require(int(row["eligibleFull"]) > 0, name + ": Full drag path was not reached")
        if name.endswith("mixed"):
            require(int(row["eligibleSimple"]) > 0, name + ": Simple drag path was not reached")
            require(int(row["full"]) + int(row["simple"]) == STEPS, name + ": native drag tick gap")
        else:
            require(int(row["full"]) == STEPS and int(row["simple"]) == 0, name + ": not Full-only")
    with (work / "east-mixed/current-mode-boundary.csv").open(newline="") as receipt:
        edges = [(int(step), mode, float(vx)) for step, mode, vx in csv.reader(receipt)]
    require([step for step, _, _ in edges] == [300, 301, 302, 303, 304, 900, 901],
            "mixed: missing transition samples")
    require(edges[0][1] == "full" and any(mode == "simple" for _, mode, _ in edges[1:5])
            and edges[-2][1] == "simple" and edges[-1][1] == "full", "mixed: unexpected native modes")
    for (step, mode, vx), (next_step, next_mode, next_vx) in zip(edges, edges[1:]):
        if next_step == step + 1 and mode != next_mode:
            require(abs(next_vx - vx) < 0.02, f"mixed: transverse speed jumped at step {next_step}")


def surface_rows(work, name):
    with (work / name / "surface-steps.csv").open(newline="") as receipt:
        return [(int(step), mode, float(y), float(exposure), float(vx), float(water), float(air))
                for step, mode, y, exposure, vx, water, air in csv.reader(receipt)]


def verify_surface(work, result):
    pairs = ("stock-surface", "zero-surface")
    require((work / pairs[0] / "free-motion.csv").read_bytes() ==
            (work / pairs[1] / "free-motion.csv").read_bytes(), "surface zero changed stock trajectory")
    water = surface_rows(work, "water")
    air = surface_rows(work, "air")
    both = surface_rows(work, "opposed")
    require(water[0][5] > 0 and water[0][6] == 0 and water[1][6] < 0, "water / still-air signs")
    require(air[0][5] == 0 and air[0][6] > 0 and air[1][5] < 0, "wind / still-water signs")
    require(both[0][5] > 0 and both[0][6] < 0 and result["opposed"][0] < result["water"][0],
            "opposed media were not separated")
    for name, rows in (("water", water), ("air", air), ("opposed", both)):
        require({mode for _, mode, *_ in rows} == {"full", "simple"}, name + ": missing native mode")
        require(all(math.isfinite(value) for row in rows for value in row[2:]), name + ": nonfinite result")
        for _, mode, _, _, _, water_force, air_force in rows:
            if mode == "simple":
                require(water_force < 0 if name == "air" else water_force > 0, name + ": Simple water sign")
                require(air_force > 0 if name == "air" else air_force < 0, name + ": Simple air sign")
    dive = surface_rows(work, "dive")
    require(any(0 < row[3] < 1 for row in dive), "no partial exposure samples")
    require(any(row[3] == 1 and row[6] < 0 for row in dive), "missing exposed air force")
    require(all(row[6] == 0 for row in dive if row[3] == 0), "air force below the plane")
    require(any(row[3] == 0 and row[5] != 0 for row in dive), "no submerged water force")
    require(dive[-1][3] == 1 and dive[-1][6] < 0, "air force did not return on resurfacing")
    require(all(abs(a[4] - b[4]) < 0.03 for a, b in zip(dive, dive[1:]) if b[0] - a[0] <= 4),
            "depth transition gave a lateral velocity jump")
    with (work / "rudder/rudder.csv").open(newline="") as receipt:
        r = {row.pop("case"): [float(value) for value in row.values()] for row in csv.DictReader(receipt)}
    baseline = r["stock-2"]
    require(all(abs(a - b) < 1e-5 for key in ("zero-2", "water-1-world-3")
                for a, b in zip(r[key], baseline)), f"rudder water-relative parity: {r}")
    require(all(abs(a - b) > 1e-4 for a, b in zip(r["stock-3"], baseline)), "stock-3 control insensitive")
    require(r["held-3"] == r["stock-3"], "held hull changed native rudder output")
    require(abs(r["coflow"][0]) < 1e-7 and abs(r["coflow"][2]) < 1e-7, "coflow full rudder or callback")
    require(all(a * b < 0 for a, b in zip(r["reverse-relative"], baseline)), "astern rudder sign")


def surface_suite(unity, project, work):
    tests = [
        (("stock-surface", False, 0, True), ("-currentSurface", "1")),
        (("zero-surface", True, 0, True), ("-currentSurface", "1")),
        (("water", True, 1, True), ("-currentSurface", "1")),
        (("air", True, 0, True), ("-currentSurface", "1", "-airEast", "12")),
        (("opposed", True, 1, True), ("-currentSurface", "1", "-airEast", "-12")),
        (("dive", True, 1, True), ("-currentSurface", "1", "-airEast", "-12", "-currentDive", "1")),
        (("rudder", False, 0, False), ("-currentRudder", "1")),
    ]
    result = {spec[0]: run_case(unity, project, work, spec, i, len(tests), extra)
              for i, (spec, extra) in enumerate(tests, 1)}
    verify_surface(work, result)
    (work / "surface-passed.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"100% NATIVE_SURFACE_PASS {work}", flush=True)


def performance_suite(unity, project, work):
    samples = []
    for pair, order in enumerate(("AB", "BA", "AB"), 1):
        rows = {}
        for offset, label in enumerate(order, 1):
            name = f"perf-{pair}-{label}"
            enabled = label == "B"
            _, _, row = run_case(unity, project, work, (name, enabled, 1 if enabled else 0, True),
                                 (pair - 1) * 2 + offset, 6, ("-currentSurface", "1", "-currentPerf", "1",
                                                                 "-airEast", "-12" if enabled else "0"))
            require(int(row["warmCalls"]) == 1000, name + ": incomplete measured window")
            rows[label] = {key: float(row[key]) for key in ("warmP50Us", "warmP99Us")}
        samples.append(rows)
    (work / "perf.json").write_text(json.dumps(samples, indent=2) + "\n", encoding="utf-8")
    print(f"100% NATIVE_WARM_AB_COMPLETE {work}", flush=True)


def read_switch(work, name):
    with (work / name / "field-switch.csv").open(newline="") as source:
        return {int(step): (mode, float(flow), float(x0), float(z0), float(x1), float(z1))
                for step, mode, flow, x0, z0, x1, z1 in csv.reader(source)}


def verify_controls(work):
    with (work / "controls/controls.csv").open(newline="") as source:
        controls = {row["case"]: (float(row["value1"]), float(row["value2"])) for row in csv.DictReader(source)}
    stock_turn, stock_readout = controls["course-stock-ahead"]
    astern_turn, astern_readout = controls["course-stock-world-astern"]
    adjusted_turn, adjusted_readout = controls["course-relative-ahead"]
    require(stock_turn * astern_turn < 0, "native course command did not reverse on ground-astern control")
    require(controls["course-zero"] == (stock_turn, stock_readout), "course patch changed zero field")
    require(abs(adjusted_turn - stock_turn) < 1e-6 and adjusted_readout == astern_readout,
            "course patch changed world readout or used the wrong helm sign")
    require(controls["course-beam-flow-on-heading"][0] == 0, "course hold unexpectedly compensates beam flow")
    require(controls["waypoint-on-line-beam-flow"][0] == 0, "unexpected pre-emptive beam-flow correction")
    east, west = (controls[f"waypoint-after-{direction}-drift"][0] for direction in ("east", "west"))
    require(east > 0 > west and abs(east + west) < 1e-6, "native waypoint feedback does not correct lateral offset")
    for left, right in (("depth-relative-2", "depth-stock-2"), ("depth-coflow", "depth-stock-rest")):
        require(all(abs(a - b) < 1e-6 for a, b in zip(controls[left], controls[right])), left + ": depth parity")
    require(all(abs(a - b) > 1e-4 for a, b in zip(controls["depth-stock-2"], controls["depth-stock-3"])),
            "depth speed response was not observed")
    require(all(a * b < 0 for a, b in zip(controls["depth-command-reverse"], controls["depth-relative-2"])),
            "depth command reverse sign")


def verify_field_case(work, mode, name, amplitude, north):
    zero, run = (read_switch(work, f"{key}-{mode}") for key in ("zero", name))
    require(set(run) == set(zero), mode + ": mismatched boundary receipt")
    for step, expected in ((362, 0), (363, amplitude), (722, amplitude), (723, 0)):
        row = run[step]
        require(abs(row[1] - expected) < 1e-8, mode + ": did not switch field")
        require(north == 0 or row[3] > 0.5, mode + ": did not switch while underway")
        require(all(math.isfinite(value) for value in row[1:]), mode + ": nonfinite boundary")
        require(math.hypot(row[4] - row[2], row[5] - row[3]) < 0.02, mode + ": speed impulse")
    require(run[363][0] == run[723][0] == ("simple" if mode == "mixed" else "full"),
            mode + ": switches did not exercise requested native mode")
    dx = run[722][4] - zero[722][4]
    dz = run[722][5] - zero[722][5]
    if abs(amplitude) == 1:
        require(dx * amplitude > 1e-3, mode + ": wrong transverse response")
    if abs(amplitude) == 1e-6:
        require(math.hypot(dx, dz) <= 1e-4, mode + f": almost-zero velocity discontinuity {dx=} {dz=}")
    return {"endOfOnLegVxDifference": dx, "endOfOnLegVzDifference": dz, "speedDifference": math.hypot(dx, dz)}


def controls_suite(unity, project, work):
    run_case(unity, project, work, ("controls", False, 0, False), 1, 7, ("-currentControls", "1"))
    verify_controls(work)
    discontinuity = {}
    index = 1
    for mode in ("full", "mixed"):
        for name, amplitude in (("zero", 0), ("switch", 1), ("tiny", 1e-6)):
            index += 1
            run_case(unity, project, work, (f"{name}-{mode}", True, 0, mode == "mixed"), index, 7,
                     ("-currentSurface", "1", "-currentSwitch", str(amplitude)), north=3)
        verify_field_case(work, mode, "switch", 1, 3)
        discontinuity[mode] = verify_field_case(work, mode, "tiny", 1e-6, 3)
    (work / "controls-passed.json").write_text(json.dumps({"instantDeltaZTinyMinusZero": discontinuity}, indent=2) + "\n")
    print(f"100% NATIVE_CONTROLS_PASS {work}; tiny-minus-zero delta vz: {discontinuity}", flush=True)


def continuity_suite(unity, project, work):
    magnitudes = (0, -1e-6, 1e-6, -1e-4, 1e-4, -1e-2, 1e-2, -1, 1)
    index = 0
    receipt = {}
    for north in (0, 3):
        folder = work / ("rest" if north == 0 else "underway")
        folder.mkdir()
        for mode in ("full", "mixed"):
            by_sign = {-1: [], 1: []}
            for i, flow in enumerate(magnitudes):
                index += 1
                name = "zero" if i == 0 else str(i)
                run_case(unity, project, folder, (f"{name}-{mode}", True, 0, mode == "mixed"),
                         index, 36, ("-currentSurface", "1", "-currentSwitch", str(flow),
                                     "-currentSwitchReceipt", "1"), north=north)
                if flow:
                    row = verify_field_case(folder, mode, name, flow, north)
                    receipt[f"{north},{mode},{flow}"] = row
                    by_sign[1 if flow > 0 else -1].append(row["speedDifference"])
            for differences in by_sign.values():
                require(all(a <= b + 1e-6 for a, b in zip(differences, differences[1:])),
                        f"{north},{mode}: nonmonotonic approach to zero")
    (work / "continuity-passed.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(f"100% NATIVE_CONTINUITY_PASS {work}", flush=True)


def depth_signal_suite(unity, project, work):
    for mode in (0, 1, 2):
        run_case(unity, project, work, (f"signal-{mode}", False, 0, False), mode + 1, 3,
                 ("-currentControls", "1", "-currentSignal", str(mode)))
    tables = {}
    for mode in (0, 1, 2):
        with (work / f"signal-{mode}/depth-signal.csv").open(newline="") as source:
            tables[mode] = [{key: float(value) for key, value in row.items()}
                            for row in csv.DictReader(source)]
    for mode in (0, 1, 2):
        require(len(tables[mode]) == 600, f"signal-{mode}: incomplete run")
        for tick in range(600):
            for column in ("world", "relative", "stock", "instant", "smoothed"):
                require(abs(tables[mode][tick][column] - tables[0][tick][column]) < 1e-6,
                        f"signal-{mode}: signal columns differ at tick {tick}")
    stock = tables[0]
    require(all(row["stock"] == 3.0 for row in stock), "stock signal changed without world motion")
    require(stock[199]["instant"] == 3.0 and stock[201]["instant"] == 2.0, "instant step missing")
    drop = stock[200]["smoothed"] - stock[201]["smoothed"]
    require(0.005 < drop < 0.02, f"filtered step is {drop}, expected the 0.012 low pass")
    require(abs(stock[599]["smoothed"] - stock[599]["instant"]) < 0.02, "filtered signal did not converge")
    base = {mode: tables[mode][199]["dvy"] for mode in (0, 1, 2)}
    for mode in (0, 1, 2):
        require(abs(base[mode]) > 1e-5, f"signal-{mode}: no depth force at all")
    instant_drop = abs(tables[1][201]["dvy"] - base[1]) / abs(base[1])
    smoothed_drop = abs(tables[2][201]["dvy"] - base[2]) / abs(base[2])
    require(instant_drop > 0.1, f"instant signal did not reach the force: {instant_drop}")
    require(smoothed_drop < 0.01, f"filtered signal still jumped the force: {smoothed_drop}")
    (work / "depth-signal-passed.json").write_text(json.dumps(
        {"instant_dvy_change": instant_drop, "smoothed_dvy_change": smoothed_drop,
         "smoothed_step": drop}, indent=2) + "\n", encoding="utf-8")
    print(f"100% NATIVE_DEPTH_SIGNAL_PASS {work}", flush=True)


def held_suite(unity, project, work):
    cases = (("stock-kinematic", False, "", 0, 0), ("field-kinematic", True, "kinematic", 1, 0),
             ("stock-frozen", False, "y", 0, 2), ("field-frozen", True, "y", 1, 2))
    result = {}
    for index, (name, patched, hold, east, north) in enumerate(cases, 1):
        extra = ("-currentHold", hold) if hold else ()
        result[name] = run_case(unity, project, work, (name, patched, east, True),
                                index, len(cases), extra, north=north)
    # A detoured method need not be bit-identical to the undetoured one:
    # code layout changes float rounding. The claim is that the current adds
    # no transport to a kinematic body (7e-6 m vs 4.67 m for the dynamic case).
    for name in ("stock-kinematic", "field-kinematic"):
        with (work / f"{name}/free-motion.csv").open(newline="") as source:
            rows = list(csv.DictReader(source))
        require(all(abs(float(row["x_m"])) < 1e-3 and abs(float(row["z_m"])) < 1e-3
                    and float(row["vx_mps"]) == 0 for row in rows),
                name + ": kinematic body accumulated transport")
    stock_x = result["stock-frozen"][0]
    field_x = result["field-frozen"][0]
    require(abs(stock_x) < 0.02, f"stock frozen-Y body drifted {stock_x}")
    require(field_x > 0.05, f"frozen-Y body did not respond to current: {field_x}")
    for name in ("stock-frozen", "field-frozen"):
        with (work / f"{name}/free-motion.csv").open(newline="") as source:
            heights = [float(row["height_m"]) for row in csv.DictReader(source)]
        require(all(abs(height + 30.0) < 0.001 for height in heights),
                name + ": frozen vertical axis changed")
    (work / "held-passed.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"100% NATIVE_HELD_PASS {work}", flush=True)


def free_suite(unity, project, work):
    cases = [("stock-free", False, 0, True), ("zero-free", True, 0, True),
             ("east-free", True, 1, True), ("west-free", True, -1, True)]
    result = {spec[0]: run_case(unity, project, work, spec, i, len(cases),
                               ("-currentSurface", "1", "-currentFree", "1"))
              for i, spec in enumerate(cases, 1)}
    require((work / "stock-free/free-motion.csv").read_bytes() ==
            (work / "zero-free/free-motion.csv").read_bytes(), "6 DoF zero-control changed")
    for name, sign in (("east-free", 1), ("west-free", -1)):
        x, vx, row = result[name]
        require(0.01 < x * sign < 16 and 0.001 < vx * sign < 1, name + ": wrong signed drift")
        require(int(row["full"]) + int(row["simple"]) == STEPS and int(row["simple"]) > 0,
                name + ": did not receive native drag in both modes")
    (work / "free-passed.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"100% NATIVE_FREE_PASS {work}", flush=True)


def object_suite(unity, project, work):
    result = {}
    index = 0
    for kind in ("liberty", "lifeboat"):
        for label, east, air in (("stock", 0, 0), ("zero", 0, 0), ("water", 1, 0), ("air", 0, 12)):
            name = f"{kind}-{label}"
            index += 1
            result[name] = run_case(unity, project, work, (name, label != "stock", east, True),
                                    index, 8, ("-currentSurface", "1", "-currentPrefab", kind,
                                               "-airEast", str(air)))
        require((work / f"{kind}-stock/free-motion.csv").read_bytes() ==
                (work / f"{kind}-zero/free-motion.csv").read_bytes(), kind + ": zero changed stock")
        for label in ("water", "air"):
            name = f"{kind}-{label}"
            x, vx, row = result[name]
            require(int(row["full"]) + int(row["simple"]) == STEPS and int(row["simple"]) > 0,
                    name + ": missing mode")
            require(x > 0 and vx > 0, name + ": wrong drift")
    (work / "objects-passed.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"100% NATIVE_OBJECTS_PASS {work}", flush=True)


def runtime_suite(unity, project, work):
    cases = [("stock", "stock", 0, 0, True), ("zero", "configured-zero", 0, 0, True),
             ("zero-installed", "installed", 0, 0, True), ("east-water", "installed", 1, 0, True),
             ("west-water", "installed", -1, 0, True), ("east-air", "installed", 0, 12, True),
             ("both", "installed", 1, 12, True), ("air-submerged", "installed", 0, 12, False)]
    outputs = {}
    for index, (name, mode, water, air, surface) in enumerate(cases, 1):
        outputs[name] = run_case(unity, project, work, (name, False, water, True), index, len(cases),
                                 ("-currentRuntime", mode, "-airEast", str(air),
                                  "-currentSurface", str(int(surface))))[:2]
        with (work / name / "runtime-current.csv").open(newline="") as receipt:
            row = next(csv.DictReader(receipt))
        active = water != 0 or air != 0 and surface
        require(int(row["full"]) == (750 if active else 0) and int(row["simple"]) == (450 if active else 0),
                name + ": production hook count")
        require((int(row["air"]) > 0) == (air != 0 and surface), name + ": wrong air activation")
        if not surface:
            require(float(row["airDeltaSquared"]) == 0, name + ": submerged air force")
    for label in ("zero", "zero-installed"):
        require((work / label / "free-motion.csv").read_bytes() == (work / "stock/free-motion.csv").read_bytes(),
                label + ": production zero changed stock trajectory")
    for name in ("east-water", "west-water", "east-air", "both"):
        sign = -1 if name == "west-water" else 1
        require(outputs[name][0] * sign > 0.5, name + ": wrong signed drift")
    require(outputs["both"][0] > max(outputs["east-air"][0], outputs["east-water"][0]),
            "combined aligned force did not increase drift")
    (work / "runtime-passed.json").write_text(json.dumps(outputs, indent=2) + "\n", encoding="utf-8")
    print(f"100% NATIVE_RUNTIME_PASS {work}", flush=True)


def runtime_nearby_suite(unity, project, work):
    results = {}
    cases = (("Liberty", "liberty", "Liberty", ""), ("Lifeboat", "lifeboat", "Lifeboat", ""),
             ("Unlisted", "liberty", "Unlisted", ""), ("Docked", "liberty", "Liberty", "freeze-all"))
    for index, (name, prefab, kind, hold) in enumerate(cases, 1):
        results[name] = run_case(unity, project, work, (name, False, 1, True), index, len(cases),
                                 ("-currentRuntime", "installed", "-airEast", "12", "-currentSurface", "1",
                                  "-currentPrefab", prefab, "-currentKind", kind, "-currentHold", hold))[:2]
        with (work / name / "runtime-current.csv").open(newline="") as receipt:
            row = next(csv.DictReader(receipt))
        if name in ("Unlisted", "Docked"):
            require(not int(row["full"]) and not int(row["simple"]) and abs(results[name][0]) < 0.01,
                    name + ": ship received correction")
        else:
            require(int(row["full"]) == 750 and int(row["simple"]) == 450 and int(row["air"]) > 0
                    and int(row["water"]) > 0 and results[name][0] > 0.5,
                    kind + ": missing production near-object response")
    (work / "runtime-nearby-passed.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    print(f"100% NATIVE_NEARBY_PASS {work}", flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("fixture", type=Path, help="retained offline engine-off VIIC fixture project")
    parser.add_argument("--unity", type=Path,
                        default=Path(r"C:\Program Files\Unity 2021.3.33f1\Editor\Unity.exe"))
    parser.add_argument("--suite", choices=("submerged", "surface", "perf", "controls", "continuity", "wind", "free",
                                           "assets", "objects", "depthsignal", "held", "runtime",
                                           "runtime-nearby"), default="submerged")
    args = parser.parse_args()
    work = ROOT / "artifacts" / "current-native" / uuid.uuid4().hex[:10]
    project = work / "obj"
    prepare(args.fixture.resolve(), project)
    if args.suite == "runtime":
        return runtime_suite(args.unity, project, work)
    if args.suite == "runtime-nearby":
        return runtime_nearby_suite(args.unity, project, work)
    if args.suite == "surface":
        return surface_suite(args.unity, project, work)
    if args.suite == "perf":
        return performance_suite(args.unity, project, work)
    if args.suite == "controls":
        return controls_suite(args.unity, project, work)
    if args.suite == "continuity":
        return continuity_suite(args.unity, project, work)
    if args.suite == "free":
        return free_suite(args.unity, project, work)
    if args.suite == "objects":
        return object_suite(args.unity, project, work)
    if args.suite == "depthsignal":
        return depth_signal_suite(args.unity, project, work)
    if args.suite == "held":
        return held_suite(args.unity, project, work)
    if args.suite == "wind":
        run_case(args.unity, project, work, ("wind", False, 0, False), 1, 1, ("-currentWindVector", "1"))
        print(f"100% NATIVE_WIND_PASS {work}\n" + (work / "wind/wind-vector.csv").read_text(), flush=True)
        return
    if args.suite == "assets":
        run_case(args.unity, project, work, ("assets", False, 0, False), 1, 1, ("-currentAssets", "1"))
        report = work / "assets/current-assets.tsv"
        with report.open(newline="") as source:
            rows = list(csv.DictReader(source, delimiter="\t"))
        require(any(row["bundle"] != "uboat.entities.lifeboats" and int(row["nativeWaterBodies"]) > 0 for row in rows),
                "no native-water transport prefab")
        require(any(row["bundle"] == "uboat.entities.lifeboats" and int(row["rigidBodies"]) > 0 for row in rows),
                "no Rigidbody lifeboat prefab")
        print(f"100% NATIVE_ASSETS_PASS {report}; {len(rows)} prefabs", flush=True)
        return
    cases = [("stock-full", False, 0, False), ("zero-full", True, 0, False),
             ("east-full", True, 1, False), ("west-full", True, -1, False),
             ("stock-mixed", False, 0, True), ("zero-mixed", True, 0, True),
             ("east-mixed", True, 1, True)]
    result = {case[0]: run_case(args.unity, project, work, case, i, len(cases))
              for i, case in enumerate(cases, 1)}
    verify(work, result)
    (work / "passed.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"100% NATIVE_CURRENT_PASS {work}", flush=True)


if __name__ == "__main__":
    main()
