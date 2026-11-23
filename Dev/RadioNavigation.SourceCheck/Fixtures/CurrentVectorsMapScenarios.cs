using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    public static class CurrentVectorsMapProbe
    {
        private sealed class Rig
        {
            public MapCurrentVectorsDriver Driver;
            public GameObject CanvasGo;
            public GameObject LinesGo;
            public MainCamera Cam;
            public Sandbox Sandbox;
        }

        private static void Check(bool cond, string msg)
        {
            if (!cond) throw new InvalidOperationException(msg);
        }

        private static Rig Setup(bool clearEnv = true)
        {
            MapWorld.Reset();
            if (clearEnv)
            {
                Environment.SetEnvironmentVariable(CurrentSettings.MasterVariable, null);
                Environment.SetEnvironmentVariable(CurrentSettings.VectorsVariable, null);
            }
            CurrentRuntimeAdapter.HasField = true;
            CurrentRuntimeAdapter.BoundShip = null;
            CurrentRuntimeAdapter.Utc = new DateTime(1940, 6, 15, 12, 0, 0, DateTimeKind.Utc);
            CurrentRuntimeAdapter.SampleCalls = 0;
            CurrentRuntimeAdapter.Sampler = (km, depth) => new Vector3(1.6f, 0f, 0.4f);

            var sandboxGo = new GameObject("Sandbox");
            var sandbox = sandboxGo.AddComponent<Sandbox>();
            Sandbox.Instance = sandbox;

            var camGo = new GameObject("MainCamera");
            var unityCam = camGo.AddComponent<Camera>();
            var mainCam = camGo.AddComponent<MainCamera>();
            mainCam.Camera = unityCam;
            MainCamera.Instance = mainCam;

            var canvasGo = new GameObject("MapCanvas", typeof(RectTransform));
            var linesGo = new GameObject("LinesContainer", typeof(RectTransform));
            linesGo.transform.SetParent(canvasGo.transform, false);
            var charting = canvasGo.AddComponent<MapChartingUI>();
            charting.SetLinesContainer(linesGo.transform);

            MapCurrentVectorsDriver.EnsureRunning();
            var driverGo = GameObject.Find("RadioNavigation.MapCurrentVectors");
            var driver = driverGo.GetComponent<MapCurrentVectorsDriver>();
            return new Rig
            {
                Driver = driver,
                CanvasGo = canvasGo,
                LinesGo = linesGo,
                Cam = mainCam,
                Sandbox = sandbox
            };
        }

        private static GameObject ContainerOf(Rig rig)
        {
            return rig.CanvasGo.Children.FirstOrDefault(c => c.name == "RadioNavCurrentVectors");
        }

        private static int CountActiveSlots(GameObject container)
        {
            return container == null ? 0 : container.Children.Count(c => c.activeSelf);
        }

        private static List<GameObject> ActiveSlots(GameObject container)
        {
            return container.Children.Where(c => c.activeSelf).ToList();
        }

        public static void MapLifecycle_RendersNonInteractiveSiblingPoolAndHidesOutsideMap()
        {
            Rig rig = Setup();
            rig.Cam.CurrentMode = CameraMode.FPP;
            rig.Driver.LateUpdate();
            Check(ContainerOf(rig) == null, "Vectors created before map is opened");

            rig.Cam.CurrentMode = CameraMode.Map;
            rig.Cam.MapController.CameraElevation = 3000f;
            rig.Cam.Camera.NextCenterRay = new Ray(new Vector3(0f, 3000f, 0f), new Vector3(0f, -1f, 0f));
            rig.Driver.LateUpdate();

            GameObject container = ContainerOf(rig);
            Check(container != null && container.activeSelf, "Vectors container missing or inactive on Map");
            Check(rig.LinesGo.Children.Count == 0, "Overlay polluted MapChartingUI linesContainer");
            Check(container.Children.Count == CurrentVectorOverlayMath.MaxPoolSlots, "Unexpected pool size");
            int active = CountActiveSlots(container);
            Check(active > 60 && active <= CurrentVectorOverlayMath.MaxPoolSlots, "Bad tactical active arrow count: " + active);

            GameObject sample = ActiveSlots(container)[0];
            foreach (GameObject child in sample.Children)
            {
                Image img = child.GetComponent<Image>();
                Check(img != null && !img.raycastTarget, "Arrow graphic intercepts pointer raycasts");
                Check(img.material != null && img.material != Graphic.defaultGraphicMaterial, "Shared default UI material mutated");
                Check(img.material.Ints["unity_GUIZTestMode"] == 8, "Overlay lacks Always depth test");
            }

            // Switching to HQ display or FPP hides container without destroying pool.
            rig.Cam.MapController.Mode = MapCameraController.MapDisplayMode.Headquarters;
            rig.Driver.LateUpdate();
            Check(!container.activeSelf, "Container stayed visible in Headquarters mode");

            rig.Cam.MapController.Mode = MapCameraController.MapDisplayMode.Normal;
            rig.Cam.CurrentMode = CameraMode.FPP;
            rig.Driver.LateUpdate();
            Check(!container.activeSelf, "Container stayed visible in FPP mode");
        }

        public static void ZoomAdaptation_HighElevationReducesCountAndExpandsRadius()
        {
            Rig rig = Setup();
            rig.Cam.MapController.CameraElevation = 2500f;
            rig.Cam.Camera.NextCenterRay = new Ray(new Vector3(0f, 2500f, 0f), new Vector3(0f, -1f, 0f));
            rig.Driver.LateUpdate();

            GameObject container = ContainerOf(rig);
            List<GameObject> lowSlots = ActiveSlots(container);
            float lowMaxSpan = lowSlots.Max(s => Math.Max(Math.Abs(s.transform.position.x), Math.Abs(s.transform.position.z)));
            int callsAfterInit = CurrentRuntimeAdapter.SampleCalls;

            // Pan inside 0.25 * stepKm does not resample.
            rig.Cam.Camera.NextCenterRay = new Ray(new Vector3(50f, 2500f, 50f), new Vector3(0f, -1f, 0f));
            rig.Driver.LateUpdate();
            Check(CurrentRuntimeAdapter.SampleCalls == callsAfterInit, "Tiny pan forced unnecessary resample");

            // Large pan >= 0.25 * stepKm resamples.
            rig.Cam.Camera.NextCenterRay = new Ray(new Vector3(400f, 2500f, 0f), new Vector3(0f, -1f, 0f));
            rig.Driver.LateUpdate();
            Check(CurrentRuntimeAdapter.SampleCalls > callsAfterInit, "Significant pan did not resample");

            // Zooming out to Atlantic scale reduces arrow count while expanding geographic coverage.
            rig.Cam.MapController.CameraElevation = 1000000f;
            rig.Cam.Camera.NextCenterRay = new Ray(new Vector3(0f, 1000000f, 0f), new Vector3(0f, -1f, 0f));
            rig.Driver.LateUpdate();

            List<GameObject> highSlots = ActiveSlots(container);
            float highMaxSpan = highSlots.Max(s => Math.Max(Math.Abs(s.transform.position.x), Math.Abs(s.transform.position.z)));
            Check(highSlots.Count < lowSlots.Count, "Zoomed-out map did not reduce arrow count");
            Check(highMaxSpan > lowMaxSpan * 100f, "Zoomed-out map did not expand covered territory");
        }

        public static void CalmHide_TimeAdvanceResample_AndOnDestroyCleansResources()
        {
            Rig rig = Setup();
            rig.Driver.LateUpdate();
            int callsBefore = CurrentRuntimeAdapter.SampleCalls;

            // Advance 5 game minutes triggers resample.
            CurrentRuntimeAdapter.Utc = CurrentRuntimeAdapter.Utc.AddMinutes(6);
            CurrentRuntimeAdapter.Sampler = (km, depth) => Vector3.zero;
            rig.Driver.LateUpdate();
            Check(CurrentRuntimeAdapter.SampleCalls > callsBefore, "5-minute tide step did not trigger resample");
            GameObject container = ContainerOf(rig);
            Check(CountActiveSlots(container) == 0, "Calm water left arrows visible");

            Material mat = container.Children[0].Children[0].GetComponent<Image>().material;
            rig.Driver.OnDestroy();
            Check(container.Destroyed, "OnDestroy leaked vector container");
            Check(mat.Destroyed, "OnDestroy leaked shared overlay material");
            Check(!Graphic.defaultGraphicMaterial.Destroyed, "OnDestroy damaged default UI material");
        }

        public static void HotkeyShiftYAndDepthMode_ToggleAndDriveDepthSampling()
        {
            Rig rig = Setup();
            float observedDepth = -999f;
            var shipGo = new GameObject("PlayerShip");
            var ship = shipGo.AddComponent<PlayerShip>();
            ship.Depth = 42.5f;
            CurrentRuntimeAdapter.BoundShip = ship;
            CurrentRuntimeAdapter.Sampler = (km, depth) => { observedDepth = depth; return new Vector3(1f, 0f, 0f); };

            // Frame 1: initialize with ship depth.
            rig.Driver.LateUpdate();
            Check(Math.Abs(observedDepth - 42.5f) < 1e-3f, "Auto depth did not sample player ship depth: " + observedDepth);

            // Explicit depth override forces requested depth.
            CurrentSettings.SaveVectorSampleDepthMeters(80f);
            MapCurrentVectorsDriver.InvalidateSamples();
            rig.Driver.LateUpdate();
            Check(Math.Abs(observedDepth - 80f) < 1e-3f, "Explicit depth override ignored: " + observedDepth);

            // Plain Y without Shift does not toggle vectors off.
            MapWorld.HeldKeys.Add(KeyCode.Y);
            rig.Driver.LateUpdate();
            Check(CurrentSettings.MapVectorsEnabled(), "Plain Y without Shift toggled debug vectors");
            MapWorld.HeldKeys.Clear();
            rig.Driver.LateUpdate();

            // Shift+Y toggles OFF and hides container; pressing again toggles ON.
            MapWorld.HeldKeys.Add(KeyCode.LeftShift);
            MapWorld.HeldKeys.Add(KeyCode.Y);
            rig.Driver.LateUpdate();
            Check(!CurrentSettings.MapVectorsEnabled(), "Shift+Y failed to turn vectors OFF");
            Check(!ContainerOf(rig).activeSelf, "Container remained visible after Shift+Y OFF");

            MapWorld.HeldKeys.Clear();
            rig.Driver.LateUpdate();
            MapWorld.HeldKeys.Add(KeyCode.LeftShift);
            MapWorld.HeldKeys.Add(KeyCode.Y);
            rig.Driver.LateUpdate();
            Check(CurrentSettings.MapVectorsEnabled(), "Second Shift+Y failed to turn vectors ON");
            Check(ContainerOf(rig).activeSelf, "Container remained hidden after Shift+Y ON");
        }
    }
}
