using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Environment;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Opt-in live measurement of the wind contract and a synthetic per-frame
    /// water/wind cost model. Enabled by the RADIONAV_WIND_PROBE=1 environment
    /// variable in the game process; writes one JSON receipt and never saves.
    /// This is NOT the ocean-current implementation and cannot accept F8.
    /// </summary>
    public sealed class WindProbeRuntime : MonoBehaviour
    {
        private const string LogPrefix = "[RadioNav][WindProbe]";
        private const int ForecastCalls = 512;
        private const int WaterQueries = 64;
        private const int WindObjects = 264;
        private const int Harmonics = 34;
        private const float WarmupSeconds = 5f;
        private const float SettleSeconds = 30f;
        private const float IdleSeconds = 15f;
        private const float LoadSeconds = 15f;
        private const float HeavySeconds = 10f;
        private const int MaxFrames = 20000;
        private const int MirrorCalls = 512;

        [Inject] private static Weather weather;
        [Inject] private static GameTime gameTime;

        private readonly double[] basisCos = new double[Harmonics];
        private readonly double[] basisSin = new double[Harmonics];
        private readonly float[] idleFrames = new float[MaxFrames];
        private readonly float[] loadFrames = new float[MaxFrames];
        private readonly float[] heavyFrames = new float[MaxFrames];
        private readonly float[] hitchAt = new float[64];
        private readonly float[] hitchMs = new float[64];
        private readonly int[] hitchPhase = new int[64];
        private readonly long[] hitchManaged = new long[64];
        private readonly long[] hitchDrop = new long[64];
        private long previousManaged;
        private int hitchCount;
        private float phaseStart;
        private IPlayerPositionProvider positionProvider;
        private FieldInfo windDegreesField;
        private FieldInfo weatherSeedField;
        private FieldInfo windOverrideField;
        private FieldInfo weatherTimeField;
        private ProbeSample forecast;
        private double forecastColdMs;
        private double[] forecastSamples;
        private double forecastTotalMs;
        private long forecastManagedDelta;
        private float mirrorAngle;
        private double[] mirrorSamples;
        private double mirrorTotalMs;
        private long mirrorManagedDelta;
        private string cameraModeStart = "unknown";
        private string cameraModeEnd = "unknown";
        private bool cameraModeStable = true;
        private int idleGc0, idleGc1, idleGc2, loadGc0, loadGc1, loadGc2, heavyGc0, heavyGc1, heavyGc2;
        private long idleManagedDelta, loadManagedDelta, heavyManagedDelta;
        private int idleCount;
        private int loadCount;
        private int heavyCount;
        private bool running;
        private bool samplingIdle;
        private bool samplingLoad;
        private bool samplingHeavy;
        private int waterQueries = WaterQueries;
        private int windObjects = WindObjects;
        private double loadWorkMs;
        private double heavyWorkMs;
        private double sink;
        private bool savedDisplayLimiter;
        private int savedVSyncCount;
        private int savedTargetFrameRate;

        public static void EnsureRunning()
        {
            if (Environment.GetEnvironmentVariable("RADIONAV_WIND_PROBE") != "1")
                return;
            foreach (WindProbeRuntime existing in FindObjectsOfType<WindProbeRuntime>())
            {
                if (existing != null)
                    return;
            }
            foreach (MonoBehaviour candidate in FindObjectsOfType<MonoBehaviour>())
            {
                if (candidate != null && candidate.GetType().Name == typeof(WindProbeRuntime).Name)
                    Destroy(candidate.gameObject);
            }
            var go = new GameObject("WindProbeRuntime");
            go.AddComponent<WindProbeRuntime>();
            DontDestroyOnLoad(go);
            Debug.Log(LogPrefix + " enabled by RADIONAV_WIND_PROBE=1");
        }

        private void Awake()
        {
            positionProvider = new GamePlayerPositionProvider();
            windDegreesField = typeof(Weather).GetField("windDegrees",
                BindingFlags.Instance | BindingFlags.NonPublic);
            weatherSeedField = typeof(Weather).GetField("weatherSeed",
                BindingFlags.Instance | BindingFlags.NonPublic);
            windOverrideField = typeof(Weather).GetField("windSpeedOverride",
                BindingFlags.Instance | BindingFlags.NonPublic);
            weatherTimeField = typeof(Weather).GetField("previousWeatherTime",
                BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < Harmonics; i++)
            {
                basisCos[i] = Math.Cos(i * 0.17);
                basisSin[i] = Math.Sin(i * 0.17);
            }
        }

        private void OnDestroy()
        {
            if (savedDisplayLimiter)
            {
                QualitySettings.vSyncCount = savedVSyncCount;
                Application.targetFrameRate = savedTargetFrameRate;
                savedDisplayLimiter = false;
            }
        }

        private void Start()
        {
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            if (running)
                yield break;
            running = true;
            yield return StartCoroutine(WaitReady());
            if (weather == null || gameTime == null || positionProvider == null)
            {
                WriteFailure("dependencies-unavailable");
                yield break;
            }

            yield return new WaitForSecondsRealtime(WarmupSeconds);
            while (gameTime.Paused)
                yield return new WaitForSecondsRealtime(0.5f);
            DisableSkyIfRequested();
            DisableSelectedIfRequested();
            yield return new WaitForSecondsRealtime(SettleSeconds);

            float lat, lon;
            DateTime dateTime;
            Vector2 position;
            if (!TryCaptureAnchor(out dateTime, out position, out lat, out lon))
            {
                WriteFailure("player-position-not-ready");
                yield break;
            }
            MeasureForecast(dateTime, position);

            ProbeSample scene = ReadScene();
            MeasureMirror(SceneTime(), position, scene);
            float mirrorError = AngleError(mirrorAngle, scene.WindDegrees);
            var report = new StringBuilder(4096);
            report.Append("{\n");
            AppendCommon(report, dateTime, lat, lon);
            report.Append("  \"scene\": {");
            AppendSample(report, scene, true);
            report.Append("},\n  \"forecast\": {");
            AppendSample(report, forecast, false);
            report.Append("},\n");
            report.Append("  \"forecastColdMs\": ").Append(Fmt(forecastColdMs)).Append(",\n");
            report.Append("  \"forecastTiming\": {\"calls\": ").Append(ForecastCalls)
                  .Append(", \"totalMs\": ").Append(Fmt(forecastTotalMs))
                  .Append(", \"p50Ms\": ").Append(Fmt(Percentile(forecastSamples, 0.50)))
                  .Append(", \"p95Ms\": ").Append(Fmt(Percentile(forecastSamples, 0.95)))
                  .Append(", \"p99Ms\": ").Append(Fmt(Percentile(forecastSamples, 0.99)))
                  .Append(", \"maxMs\": ").Append(Fmt(forecastSamples[forecastSamples.Length - 1]))
                  .Append(", \"managedDeltaBytes\": ").Append(forecastManagedDelta).Append("},\n");
            report.Append("  \"mirror\": {\"angle\": ").Append(Fmt(mirrorAngle))
                  .Append(", \"errorDeg\": ").Append(Fmt(mirrorError))
                  .Append(", \"calls\": ").Append(MirrorCalls)
                  .Append(", \"totalMs\": ").Append(Fmt(mirrorTotalMs))
                  .Append(", \"p50Ms\": ").Append(Fmt(Percentile(mirrorSamples, 0.50)))
                  .Append(", \"p99Ms\": ").Append(Fmt(Percentile(mirrorSamples, 0.99)))
                  .Append(", \"maxMs\": ").Append(Fmt(mirrorSamples[mirrorSamples.Length - 1]))
                  .Append(", \"managedDeltaBytes\": ").Append(mirrorManagedDelta).Append("},\n");

            // Idle frame sampling. Remove the display limiter only for the
            // measurement window and restore it afterwards; nothing is saved.
            int originalVSync = QualitySettings.vSyncCount;
            int originalTarget = Application.targetFrameRate;
            savedVSyncCount = originalVSync;
            savedTargetFrameRate = originalTarget;
            savedDisplayLimiter = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            idleCount = 0;
            hitchCount = 0;
            samplingIdle = true;
            phaseStart = Time.realtimeSinceStartup;
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            long managed0 = GC.GetTotalMemory(false);
            yield return StartCoroutine(SamplePhase(IdleSeconds));
            samplingIdle = false;
            idleGc0 = GC.CollectionCount(0) - g0;
            idleGc1 = GC.CollectionCount(1) - g1;
            idleGc2 = GC.CollectionCount(2) - g2;
            idleManagedDelta = GC.GetTotalMemory(false) - managed0;

            // Synthetic load: 64 water queries (4 corners x 34 harmonics) and
            // 264 wind-force evaluations per frame. Arithmetic proxy only.
            waterQueries = WaterQueries;
            windObjects = WindObjects;
            loadCount = 0;
            samplingLoad = true;
            phaseStart = Time.realtimeSinceStartup;
            g0 = GC.CollectionCount(0);
            g1 = GC.CollectionCount(1);
            g2 = GC.CollectionCount(2);
            managed0 = GC.GetTotalMemory(false);
            yield return StartCoroutine(SamplePhase(LoadSeconds));
            samplingLoad = false;
            loadGc0 = GC.CollectionCount(0) - g0;
            loadGc1 = GC.CollectionCount(1) - g1;
            loadGc2 = GC.CollectionCount(2) - g2;
            loadManagedDelta = GC.GetTotalMemory(false) - managed0;

            // 4x the arithmetic to expose sensitivity, still no atlas memory.
            waterQueries = 4 * WaterQueries;
            windObjects = 4 * WindObjects;
            heavyCount = 0;
            samplingHeavy = true;
            phaseStart = Time.realtimeSinceStartup;
            g0 = GC.CollectionCount(0);
            g1 = GC.CollectionCount(1);
            g2 = GC.CollectionCount(2);
            managed0 = GC.GetTotalMemory(false);
            yield return StartCoroutine(SamplePhase(HeavySeconds));
            samplingHeavy = false;
            heavyGc0 = GC.CollectionCount(0) - g0;
            heavyGc1 = GC.CollectionCount(1) - g1;
            heavyGc2 = GC.CollectionCount(2) - g2;
            heavyManagedDelta = GC.GetTotalMemory(false) - managed0;
            QualitySettings.vSyncCount = originalVSync;
            Application.targetFrameRate = originalTarget;
            savedDisplayLimiter = false;

            MainCamera endCamera = FindObjectOfType<MainCamera>();
            cameraModeEnd = endCamera != null ? endCamera.CurrentMode.ToString() : "unknown";
            cameraModeStable = string.Equals(cameraModeStart, cameraModeEnd, StringComparison.Ordinal);
            report.Append("  \"cameraModeEnd\": \"").Append(cameraModeEnd)
                  .Append("\",\n  \"cameraModeStable\": ")
                  .Append(cameraModeStable ? "true" : "false").Append(",\n");

            report.Append("  \"frames\": {\"idle\": ");
            AppendFrames(report, idleFrames, idleCount, IdleSeconds);
            report.Append(", \"syntheticLoad\": ");
            AppendFrames(report, loadFrames, loadCount, LoadSeconds);
            report.Append(", \"syntheticHeavy\": ");
            AppendFrames(report, heavyFrames, heavyCount, HeavySeconds);
            report.Append("},\n  \"gc\": {\"idle\": ");
            AppendGc(report, idleGc0, idleGc1, idleGc2, idleManagedDelta);
            report.Append(", \"load\": ");
            AppendGc(report, loadGc0, loadGc1, loadGc2, loadManagedDelta);
            report.Append(", \"heavy\": ");
            AppendGc(report, heavyGc0, heavyGc1, heavyGc2, heavyManagedDelta);
            report.Append("},\n  \"starSuppressor\": {\"scans\": ")
                  .Append(GameStarLayerSuppressor.ScanCount)
                  .Append(", \"blockedFrames\": ").Append(GameStarLayerSuppressor.BlockedCount)
                  .Append(", \"hidden\": ").Append(GameStarLayerSuppressor.HiddenRendererCount)
                  .Append("},\n  \"hitches\": [");
            for (int i = 0; i < hitchCount; i++)
            {
                if (i != 0) report.Append(", ");
                report.Append("{\"phase\": ").Append(hitchPhase[i])
                      .Append(", \"at\": ").Append(Fmt(hitchAt[i]))
                      .Append(", \"ms\": ").Append(Fmt(hitchMs[i]))
                      .Append(", \"managed\": ").Append(hitchManaged[i])
                      .Append(", \"drop\": ").Append(hitchDrop[i]).Append("}");
            }
            report.Append("],\n  \"workload\": {\"waterQueries\": ").Append(WaterQueries)
                  .Append(", \"windObjects\": ").Append(WindObjects)
                  .Append(", \"heavyWaterQueries\": ").Append(4 * WaterQueries)
                  .Append(", \"heavyWindObjects\": ").Append(4 * WindObjects)
                  .Append(", \"harmonics\": ").Append(Harmonics)
                  .Append(", \"baseSelfMsPerFrame\": ").Append(Fmt(loadWorkMs / Math.Max(1, loadCount)))
                  .Append(", \"heavySelfMsPerFrame\": ").Append(Fmt(heavyWorkMs / Math.Max(1, heavyCount)))
                  .Append("},\n  \"checksum\": ").Append(Fmt(sink))
                  .Append(",\n  \"display\": {\"originalVSyncCount\": ").Append(originalVSync)
                  .Append(", \"originalTargetFrameRate\": ").Append(originalTarget)
                  .Append(", \"measuredVSyncCount\": 0, \"measuredTargetFrameRate\": -1")
                  .Append(", \"screenHz\": ").Append(Fmt(Screen.currentResolution.refreshRate)).Append("}\n}\n");
            Write(report.ToString());
        }

        private bool TryCaptureAnchor(out DateTime dateTime, out Vector2 position,
            out float lat, out float lon)
        {
            dateTime = default(DateTime);
            position = default(Vector2);
            if (!positionProvider.TryGetPosition(out lat, out lon))
                return false;
            if (Math.Abs(lat) <= 0.001f && Math.Abs(lon) <= 0.001f)
                return false;
            position = Coordinates.ToWorldPosition(lat, lon);
            dateTime = gameTime.CurrentDateTime;
            return true;
        }

        private float SceneTime()
        {
            return weatherTimeField == null ? 0f
                : Convert.ToSingle(weatherTimeField.GetValue(weather), CultureInfo.InvariantCulture);
        }

        // Cold call once, then warm timings on shifted but plausible dates.
        private void MeasureForecast(DateTime dateTime, Vector2 position)
        {
            var coldWatch = Stopwatch.StartNew();
            Weather.WeatherData cold = weather.GetWeatherDataAt(dateTime, position);
            coldWatch.Stop();
            forecastColdMs = coldWatch.Elapsed.TotalMilliseconds;
            forecast = ProbeSample.From(cold);
            forecastSamples = new double[ForecastCalls];
            long before = GC.GetTotalMemory(false);
            var total = Stopwatch.StartNew();
            for (int i = 0; i < ForecastCalls; i++)
            {
                long start = Stopwatch.GetTimestamp();
                Weather.WeatherData value = weather.GetWeatherDataAt(
                    dateTime.AddMinutes(i), position);
                forecastSamples[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                sink += value.WindSpeed + value.WindDegrees;
            }
            total.Stop();
            forecastTotalMs = total.Elapsed.TotalMilliseconds;
            forecastManagedDelta = GC.GetTotalMemory(false) - before;
            Array.Sort(forecastSamples);
        }

        // Warm loop of the scene-angle mirror; parity is checked separately.
        private void MeasureMirror(float sceneT, Vector2 position, ProbeSample scene)
        {
            mirrorAngle = SceneWindTrace.Angle(sceneT, position.x, position.y, scene.Seed);
            mirrorSamples = new double[MirrorCalls];
            long before = GC.GetTotalMemory(false);
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < MirrorCalls; i++)
            {
                long start = Stopwatch.GetTimestamp();
                float value = SceneWindTrace.Angle(sceneT + i * 0.001f, position.x, position.y, scene.Seed);
                mirrorSamples[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                sink += value;
            }
            watch.Stop();
            mirrorTotalMs = watch.Elapsed.TotalMilliseconds;
            mirrorManagedDelta = GC.GetTotalMemory(false) - before;
            Array.Sort(mirrorSamples);
        }

        /// <summary>Diagnostic A/B only: disables the mod sky renderers to see
        /// whether the periodic hitch comes from their mesh rebuilds.</summary>
        private void DisableSkyIfRequested()
        {
            if (Environment.GetEnvironmentVariable("RADIONAV_WIND_PROBE_NOSKY") != "1")
                return;
            int disabled = DisableNamedSystems() + DisableCanvases() + DisableModComponents();
            Debug.Log(LogPrefix + " diagnostic sky disabled components=" + disabled);
        }

        private static int DisableNamedSystems()
        {
            int disabled = 0;
            foreach (MonoBehaviour component in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (component == null)
                    continue;
                if (!IsDiagnosticSystem(component.GetType().Name))
                    continue;
                component.enabled = false;
                disabled++;
            }
            return disabled;
        }

        private static bool IsDiagnosticSystem(string name)
        {
            return name == "StarsMesh" || name == "ConstellationMesh" || name == "PlanetRenderer"
                || name == "ClockSecondsDriver" || name == "ContactsUI" || name == "JournalUI";
        }

        private static int DisableCanvases()
        {
            int disabled = 0;
            foreach (Canvas canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (canvas == null)
                    continue;
                canvas.enabled = false;
                disabled++;
            }
            return disabled;
        }

        // Isolate the whole mod: disable every mod MonoBehaviour except this
        // probe, so a remaining hitch can only be stock/engine work.
        private int DisableModComponents()
        {
            int disabled = 0;
            foreach (MonoBehaviour component in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (component == null || ReferenceEquals(component, this))
                    continue;
                if (component.GetType().Assembly != typeof(WindProbeRuntime).Assembly)
                    continue;
                component.enabled = false;
                disabled++;
            }
            return disabled;
        }

        /// <summary>Diagnostic bisect: disables mod components whose type name
        /// contains one of the comma-separated RADIONAV_WIND_PROBE_DISABLE
        /// tokens. Probe itself is never disabled.</summary>
        private void DisableSelectedIfRequested()
        {
            string filter = Environment.GetEnvironmentVariable("RADIONAV_WIND_PROBE_DISABLE");
            if (string.IsNullOrEmpty(filter))
                return;
            string[] tokens = filter.Split(',');
            int disabled = 0;
            foreach (MonoBehaviour component in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (component == null || ReferenceEquals(component, this))
                    continue;
                if (component.GetType().Assembly != typeof(WindProbeRuntime).Assembly)
                    continue;
                string name = component.GetType().Name;
                bool match = false;
                for (int i = 0; i < tokens.Length; i++)
                {
                    if (tokens[i].Length > 0 && name.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        match = true;
                        break;
                    }
                }
                if (match)
                {
                    component.enabled = false;
                    disabled++;
                }
            }
            Debug.Log(LogPrefix + " diagnostic selected disabled=" + disabled + " filter=" + filter);
        }

        private IEnumerator WaitReady()
        {
            float started = Time.realtimeSinceStartup;
            float positionedSince = -1f;
            for (int i = 0; i < 1200; i++)
            {
                ResolveDependencies();
                if (IsReadyAt(ref positionedSince, started))
                    yield break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        private void ResolveDependencies()
        {
            if (weather == null)
                weather = InjectionFramework.Instance?.GetInstance<Weather>();
            if (gameTime == null)
                gameTime = InjectionFramework.Instance?.GetInstance<GameTime>();
        }

        private bool IsReadyAt(ref float positionedSince, float started)
        {
            if (weather == null || gameTime == null || Sandbox.Instance == null
                || positionProvider == null)
                return false;
            float lat, lon;
            bool positioned = positionProvider.TryGetPosition(out lat, out lon)
                && (Math.Abs(lat) > 0.001f || Math.Abs(lon) > 0.001f);
            if (!positioned || gameTime.Paused)
            {
                positionedSince = -1f;
                return false;
            }
            if (positionedSince < 0f)
                positionedSince = Time.realtimeSinceStartup;
            return Time.realtimeSinceStartup - positionedSince >= 5f
                && Time.realtimeSinceStartup - started >= 20f;
        }

        private IEnumerator SamplePhase(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
        }

        private ProbeSample ReadScene()
        {
            var sample = new ProbeSample();
            sample.SceneSpeed = weather.WindSpeed.Value;
            sample.TotalSpeed = weather.TotalWindSpeed;
            sample.SeaCondition = weather.SeaCondition;
            sample.WindDegrees = windDegreesField == null ? float.NaN
                : Convert.ToSingle(windDegreesField.GetValue(weather), CultureInfo.InvariantCulture);
            sample.Seed = weatherSeedField == null ? float.NaN
                : Convert.ToSingle(weatherSeedField.GetValue(weather), CultureInfo.InvariantCulture);
            sample.Override = windOverrideField == null ? float.NaN
                : Convert.ToSingle(windOverrideField.GetValue(weather), CultureInfo.InvariantCulture);
            return sample;
        }

        private struct ProbeSample
        {
            internal float SceneSpeed;
            internal float TotalSpeed;
            internal float SeaCondition;
            internal float WindDegrees;
            internal float Seed;
            internal float Override;

            internal static ProbeSample From(Weather.WeatherData data)
            {
                return new ProbeSample { SceneSpeed = data.WindSpeed, WindDegrees = data.WindDegrees };
            }
        }

        private void Update()
        {
            long managed = GC.GetTotalMemory(false);
            long managedDrop = previousManaged - managed;
            previousManaged = managed;
            float ms = Time.unscaledDeltaTime * 1000f;
            RecordHitch(ms, managed, managedDrop);
            RecordFrame(ms);
        }

        private void RecordHitch(float ms, long managed, long managedDrop)
        {
            if (!samplingIdle && !samplingLoad && !samplingHeavy)
                return;
            if (ms <= 25f || hitchCount >= hitchAt.Length)
                return;
            hitchAt[hitchCount] = Time.realtimeSinceStartup - phaseStart;
            hitchMs[hitchCount] = ms;
            int phase = 2;
            if (samplingIdle)
                phase = 0;
            else if (samplingLoad)
                phase = 1;
            hitchPhase[hitchCount] = phase;
            hitchManaged[hitchCount] = managed;
            hitchDrop[hitchCount] = managedDrop;
            hitchCount++;
        }

        private void RecordFrame(float ms)
        {
            if (samplingIdle)
            {
                if (idleCount < idleFrames.Length) idleFrames[idleCount++] = ms;
                return;
            }
            if (!samplingLoad && !samplingHeavy)
                return;
            if (samplingLoad && loadCount < loadFrames.Length) loadFrames[loadCount++] = ms;
            if (samplingHeavy && heavyCount < heavyFrames.Length) heavyFrames[heavyCount++] = ms;
            RunSyntheticWork();
        }

        private void RunSyntheticWork()
        {
            long start = Stopwatch.GetTimestamp();
            double total = 0;
            for (int q = 0; q < waterQueries; q++)
            {
                double u = 0, v = 0;
                for (int i = 0; i < Harmonics; i++)
                {
                    u += basisCos[i] * (q + i) + basisSin[i] * (q - i);
                    v += basisCos[i] * (q - 2 * i);
                }
                total += u + v;
            }
            for (int o = 0; o < windObjects; o++)
            {
                double slip = 6 + (o & 7) - 2;
                total += slip * Math.Abs(slip) * 0.001;
            }
            double elapsed = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            if (samplingLoad) loadWorkMs += elapsed; else heavyWorkMs += elapsed;
            sink += total;
        }

        private void AppendCommon(StringBuilder sb, DateTime dateTime, float lat, float lon)
        {
            sb.Append("  \"utc\": \"").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append("\",\n");
            sb.Append("  \"gameDateTime\": \"").Append(dateTime.ToString("o", CultureInfo.InvariantCulture)).Append("\",\n");
            sb.Append("  \"storyTicks\": ").Append(gameTime == null ? -1 : gameTime.StoryTicks).Append(",\n");
            sb.Append("  \"latitude\": ").Append(Fmt(lat)).Append(",\n");
            sb.Append("  \"longitude\": ").Append(Fmt(lon)).Append(",\n");
            sb.Append("  \"skyDisabled\": ")
              .Append(Environment.GetEnvironmentVariable("RADIONAV_WIND_PROBE_NOSKY") == "1"
                  ? "true" : "false").Append(",\n");
            sb.Append("  \"disabledFilter\": \"")
              .Append(Environment.GetEnvironmentVariable("RADIONAV_WIND_PROBE_DISABLE") ?? "")
              .Append("\",\n");
            MainCamera probeCamera = FindObjectOfType<MainCamera>();
            cameraModeStart = probeCamera != null ? probeCamera.CurrentMode.ToString() : "unknown";
            sb.Append("  \"cameraMode\": \"").Append(cameraModeStart).Append("\",\n");
        }

        private static void AppendSample(StringBuilder sb, ProbeSample s, bool scene)
        {
            sb.Append("\"windSpeed\": ").Append(Fmt(s.SceneSpeed))
              .Append(", \"windDegrees\": ").Append(Fmt(s.WindDegrees));
            if (!scene)
                return;
            sb.Append(", \"totalWindSpeed\": ").Append(Fmt(s.TotalSpeed))
              .Append(", \"seaCondition\": ").Append(Fmt(s.SeaCondition))
              .Append(", \"seed\": ").Append(Fmt(s.Seed))
              .Append(", \"override\": ").Append(Fmt(s.Override));
        }

        private static void AppendGc(StringBuilder sb, int g0, int g1, int g2, long managed)
        {
            sb.Append("{\"gen0\": ").Append(g0)
              .Append(", \"gen1\": ").Append(g1)
              .Append(", \"gen2\": ").Append(g2)
              .Append(", \"managedDeltaBytes\": ").Append(managed).Append("}");
        }

        private static void AppendFrames(StringBuilder sb, float[] values, int count, float seconds)
        {
            var copy = new float[count];
            Array.Copy(values, copy, count);
            Array.Sort(copy);
            int over16 = 0, over25 = 0;
            for (int i = 0; i < count; i++)
            {
                if (copy[i] > 16.667f) over16++;
                if (copy[i] > 25f) over25++;
            }
            sb.Append("{\"count\": ").Append(count)
              .Append(", \"fps\": ").Append(Fmt(seconds > 0 ? count / seconds : 0))
              .Append(", \"over16_667\": ").Append(over16)
              .Append(", \"over25\": ").Append(over25)
              .Append(", \"p50Ms\": ").Append(Fmt(PercentileF(copy, 0.50)))
              .Append(", \"p95Ms\": ").Append(Fmt(PercentileF(copy, 0.95)))
              .Append(", \"p99Ms\": ").Append(Fmt(PercentileF(copy, 0.99)))
              .Append(", \"p999Ms\": ").Append(Fmt(PercentileF(copy, 0.999)))
              .Append(", \"maxMs\": ").Append(count == 0 ? "null" : Fmt(copy[count - 1]))
              .Append("}");
        }

        private static float AngleError(float a, float b)
        {
            float d = Math.Abs(a - b) % 360f;
            return Math.Min(d, 360f - d);
        }

        private static double Percentile(double[] sorted, double p)
        {
            if (sorted.Length == 0) return double.NaN;
            int index = (int)Math.Round(p * (sorted.Length - 1));
            return sorted[Math.Max(0, Math.Min(sorted.Length - 1, index))];
        }

        private static double PercentileF(float[] sorted, double p)
        {
            if (sorted.Length == 0) return double.NaN;
            int index = (int)Math.Round(p * (sorted.Length - 1));
            return sorted[Math.Max(0, Math.Min(sorted.Length - 1, index))];
        }

        private static string Fmt(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "null";
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private void WriteFailure(string reason)
        {
            Write("{\"ok\": false, \"reason\": \"" + reason + "\"}\n");
        }

        private void Write(string json)
        {
            string path = Path.Combine(Application.persistentDataPath,
                "radionav-wind-probe-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + ".json");
            File.WriteAllText(path, json, new UTF8Encoding(false));
            Debug.Log(LogPrefix + " receipt written: " + path);
        }
    }

    /// <summary>
    /// Exact local mirror of the game's scene wind-angle Perlin trace
    /// (9 draws on coords, then one on coords*0.25). Diagnostic only;
    /// verified against Weather.windDegrees at runtime.
    /// </summary>
    internal static class SceneWindTrace
    {
        private static readonly int[] Perm = new int[257]
        {
            151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225,
            140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10, 23, 190, 6, 148,
            247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32,
            57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175,
            74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122,
            60, 211, 133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54,
            65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169,
            200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64,
            52, 217, 226, 250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212,
            207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213,
            119, 248, 152, 2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9,
            129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104,
            218, 246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241,
            81, 51, 145, 235, 249, 14, 239, 107, 49, 192, 214, 31, 181, 199, 106, 157,
            184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93,
            222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180,
            151
        };

        internal static float Angle(float t, float eastKm, float northKm, float seed)
        {
            var coord = new Vector3(t, eastKm * 0.00015f + t, northKm * 0.00015f - t);
            for (int i = 0; i < 9; i++)
                Next(ref coord, seed);
            coord = new Vector3(coord.x * 0.25f, coord.y * 0.25f, coord.z * 0.25f);
            return 360f * Next(ref coord, seed);
        }

        internal static float SpeedNoise(float t, float eastKm, float northKm, float seed)
        {
            var coord = new Vector3(t, eastKm * 0.00015f + t, northKm * 0.00015f - t);
            return Next(ref coord, seed);
        }

        private static float Next(ref Vector3 coord, float seed)
        {
            coord.x += seed;
            return Noise(coord) * 0.5f + 0.5f;
        }

        private static float Noise(Vector3 p)
        {
            int xi = (int)Math.Floor(p.x) & 0xFF;
            int yi = (int)Math.Floor(p.y) & 0xFF;
            int zi = (int)Math.Floor(p.z) & 0xFF;
            float x = p.x - (float)Math.Floor(p.x);
            float y = p.y - (float)Math.Floor(p.y);
            float z = p.z - (float)Math.Floor(p.z);
            float fx = Fade(x), fy = Fade(y), fz = Fade(z);
            int a = (Perm[xi] + yi) & 0xFF;
            int b = (Perm[xi + 1] + yi) & 0xFF;
            int aa = (Perm[a] + zi) & 0xFF;
            int ab = (Perm[a + 1] + zi) & 0xFF;
            int ba = (Perm[b] + zi) & 0xFF;
            int bb = (Perm[b + 1] + zi) & 0xFF;
            return Lerp(fz,
                Lerp(fy, Lerp(fx, Grad(Perm[aa], x, y, z), Grad(Perm[ba], x - 1, y, z)),
                          Lerp(fx, Grad(Perm[ab], x, y - 1, z), Grad(Perm[bb], x - 1, y - 1, z))),
                Lerp(fy, Lerp(fx, Grad(Perm[aa + 1], x, y, z - 1), Grad(Perm[ba + 1], x - 1, y, z - 1)),
                          Lerp(fx, Grad(Perm[ab + 1], x, y - 1, z - 1),
                               Grad(Perm[bb + 1], x - 1, y - 1, z - 1))));
        }

        private static float Fade(float t)
        {
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private static float Lerp(float t, float a, float b)
        {
            return a + t * (b - a);
        }

        private static float Grad(int hash, float x, float y, float z)
        {
            int h = hash & 0xF;
            float u = h < 8 ? x : y;
            float v;
            if (h < 4)
                v = y;
            else if (h == 12 || h == 14)
                v = x;
            else
                v = z;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }
    }
}
