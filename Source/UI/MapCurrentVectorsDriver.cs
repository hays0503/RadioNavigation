using System;
using System.Reflection;
using DWS.Common.InjectionFramework;
using TMPro;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Debug visualization of ocean &amp; tidal current vectors directly on the
    /// UBOAT map canvas. Adapts coverage radius and node count to the map camera
    /// elevation, snaps samples to world coordinates so panning stays rock-steady,
    /// and updates purely via pooled graphics with zero MapChartingDatabase writes.
    /// Toggle: map hotkey Shift+Y or console command "rn_current_vectors [on|off]".
    /// </summary>
    [DefaultExecutionOrder(31000)]
    public sealed class MapCurrentVectorsDriver : MonoBehaviour
    {
        private const string DriverName = "RadioNavigation.MapCurrentVectors";
        private const string ContainerName = "RadioNavCurrentVectors";
        private const float ResolveCooldownSeconds = 1.0f;
        private const long SampleTimeStepTicks = 3000000000L; // 5 game minutes

        private static readonly FieldInfo LinesContainerField = typeof(MapChartingUI).GetField(
            "linesContainer", BindingFlags.Instance | BindingFlags.NonPublic);

        private sealed class ArrowSlot
        {
            public GameObject Root;
            public RectTransform RootRect;
            public Image Shaft;
            public RectTransform ShaftRect;
            public Image Head;
            public RectTransform HeadRect;
        }

        private readonly HeldKeyEdge yEdge = new HeldKeyEdge();
        private readonly ArrowSlot[] slots = new ArrowSlot[CurrentVectorOverlayMath.MaxPoolSlots];
        private readonly Vector2[] samplePointsKm = new Vector2[CurrentVectorOverlayMath.MaxPoolSlots];
        private readonly Vector3[] sampledFlowsMps = new Vector3[CurrentVectorOverlayMath.MaxPoolSlots];

        private MainCamera mainCamera;
        private Sandbox sandbox;
        private MapChartingUI charting;
        private RectTransform container;
        private Material overlayMaterial;
        private Texture2D shaftTexture;
        private Texture2D headTexture;
        private Sprite shaftSprite;
        private Sprite headSprite;
        private float nextResolve;
        private int activeGridCount;
        private float cachedStepKm = -1f;
        private Vector2 cachedCenterKm;
        private long cachedUtcTicks;
        private float cachedDepthM = float.NaN;
        private bool hasSamples;
        private bool warned;

        public static void EnsureRunning()
        {
            GameObject root = GameObject.Find(DriverName);
            if (root != null)
            {
                MonoBehaviour[] existing = root.GetComponents<MonoBehaviour>();
                for (int i = 0; i < existing.Length; i++)
                {
                    if (existing[i].IsNotNull()
                        && existing[i].GetType().Name == nameof(MapCurrentVectorsDriver))
                        return;
                }
            }
            else
            {
                root = new GameObject(DriverName);
                DontDestroyOnLoad(root);
            }
            root.AddComponent<MapCurrentVectorsDriver>();
        }

        public static void InvalidateSamples()
        {
            GameObject root = GameObject.Find(DriverName);
            if (root == null)
                return;
            MapCurrentVectorsDriver driver = root.GetComponent<MapCurrentVectorsDriver>();
            if (driver != null)
                driver.hasSamples = false;
        }

        public void OnDestroy()
        {
            if (container != null)
                Destroy(container.gameObject);
            if (shaftSprite != null)
                Destroy(shaftSprite);
            if (headSprite != null)
                Destroy(headSprite);
            if (shaftTexture != null)
                Destroy(shaftTexture);
            if (headTexture != null)
                Destroy(headTexture);
            if (overlayMaterial != null)
                Destroy(overlayMaterial);
        }

        public void LateUpdate()
        {
            try
            {
                Refresh();
            }
            catch (Exception ex)
            {
                if (warned)
                    return;
                warned = true;
                Debug.LogWarning("[RadioNav][CURRENT] map vector overlay disabled: " + ex.Message);
                SetContainerActive(false);
                enabled = false;
            }
        }

        private void Refresh()
        {
            if (!ResolveCamera())
            {
                yEdge.Synchronize(false);
                SetContainerActive(false);
                return;
            }

            MapCameraController mapController = mainCamera.MapController;
            bool mapOpen = NavMarkerGate.IsMapOpen(
                mainCamera.isActiveAndEnabled,
                mainCamera.CurrentMode,
                mapController != null,
                mapController != null ? mapController.Mode : default(MapCameraController.MapDisplayMode));

            if (!mapOpen)
            {
                yEdge.Synchronize(false);
                SetContainerActive(false);
                return;
            }

            PollMapHotkey();

            if (!CurrentSettings.IsMasterEnabled()
                || !CurrentSettings.MapVectorsEnabled()
                || !CurrentRuntimeAdapter.HasField)
            {
                SetContainerActive(false);
                return;
            }

            float elevation = mapController.CameraElevation;
            if (elevation <= 0f || !EnsureOverlayReady())
            {
                SetContainerActive(false);
                return;
            }

            SetContainerActive(true);
            RenderGrid(elevation);
        }

        private void PollMapHotkey()
        {
            bool pressed = yEdge.Advance(Input.GetKey(KeyCode.Y));
            if (!pressed || IsTextInputFocused())
                return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!shift)
                return;

            bool next = !CurrentSettings.MapVectorsEnabled();
            CurrentSettings.SaveMapVectorsPreference(next);
            hasSamples = false;
            Debug.Log("[RadioNav][CURRENT] map vectors " + (next ? "ON" : "OFF") + " (Shift+Y)");
        }

        private static bool IsTextInputFocused()
        {
            EventSystem current = EventSystem.current;
            GameObject selected = current != null ? current.currentSelectedGameObject : null;
            if (selected == null)
                return false;
            return selected.GetComponentInParent<TMP_InputField>() != null
                || selected.GetComponentInParent<InputField>() != null;
        }

        private bool ResolveCamera()
        {
            if (mainCamera == null)
                mainCamera = MainCamera.Instance;
            if (sandbox == null)
                sandbox = Sandbox.Instance;
            if (sandbox == null)
                sandbox = InjectionFramework.Instance?.GetInstance<Sandbox>();
            return mainCamera != null && sandbox != null;
        }

        private bool EnsureOverlayReady()
        {
            if (container != null)
                return true;
            if (Time.unscaledTime < nextResolve)
                return false;
            nextResolve = Time.unscaledTime + ResolveCooldownSeconds;

            if (charting == null)
                charting = Object.FindObjectOfType<MapChartingUI>(true);
            if (charting == null || LinesContainerField == null)
                return false;
            if (!(LinesContainerField.GetValue(charting) is Transform linesTransform) || linesTransform.parent == null)
                return false;

            EnsureAssets();
            GameObject rootGo = new GameObject(ContainerName, typeof(RectTransform));
            rootGo.transform.SetParent(linesTransform.parent, false);
            rootGo.transform.SetSiblingIndex(linesTransform.GetSiblingIndex());
            container = rootGo.GetComponent<RectTransform>();
            container.anchorMin = Vector2.zero;
            container.anchorMax = Vector2.one;
            container.sizeDelta = Vector2.zero;
            BuildPool(container);
            return true;
        }

        private void RenderGrid(float elevation)
        {
            CurrentVectorGridSpec spec = CurrentVectorOverlayMath.ComputeGridSpec(elevation);
            Vector2 centerKm = ResolveMapCenterEncounterKm();
            DateTime utc = CurrentRuntimeAdapter.GameUtc();
            float depthM = ResolveSampleDepthMeters();

            if (NeedsResample(spec, centerKm, utc, depthM))
            {
                activeGridCount = CurrentVectorOverlayMath.BuildSampleGrid(centerKm, spec, samplePointsKm);
                for (int i = 0; i < activeGridCount; i++)
                {
                    if (!CurrentRuntimeAdapter.TrySampleEncounterAtDepth(samplePointsKm[i], depthM, utc, out Vector3 vel))
                        vel = Vector3.zero;
                    sampledFlowsMps[i] = vel;
                }
                cachedStepKm = spec.StepKm;
                cachedCenterKm = centerKm;
                cachedUtcTicks = utc.Ticks;
                cachedDepthM = depthM;
                hasSamples = true;
            }

            ApplyVisuals(spec);
        }

        private bool NeedsResample(CurrentVectorGridSpec spec, Vector2 centerKm, DateTime utc, float depthM)
        {
            if (!hasSamples || Mathf.Abs(spec.StepKm - cachedStepKm) > 1e-3f)
                return true;
            if (Mathf.Abs(depthM - cachedDepthM) > 0.25f)
                return true;
            if (Math.Abs(utc.Ticks - cachedUtcTicks) >= SampleTimeStepTicks)
                return true;
            float shiftLimit = spec.StepKm * 0.25f;
            float dx = centerKm.x - cachedCenterKm.x;
            float dy = centerKm.y - cachedCenterKm.y;
            return dx * dx + dy * dy >= shiftLimit * shiftLimit;
        }

        private void ApplyVisuals(CurrentVectorGridSpec spec)
        {
            float posY = 2.0f + spec.ElevationMeters * 0.0005f;
            for (int i = 0; i < slots.Length; i++)
            {
                ArrowSlot slot = slots[i];
                if (i >= activeGridCount)
                {
                    if (slot.Root.activeSelf)
                        slot.Root.SetActive(false);
                    continue;
                }

                Vector3 flow = sampledFlowsMps[i];
                float speed = Mathf.Sqrt(flow.x * flow.x + flow.z * flow.z);
                float totalLen = CurrentVectorOverlayMath.ComputeArrowLengthScene(speed, spec);
                if (totalLen <= 0f)
                {
                    if (slot.Root.activeSelf)
                        slot.Root.SetActive(false);
                    continue;
                }

                if (!slot.Root.activeSelf)
                    slot.Root.SetActive(true);

                Vector3 world = sandbox.EncounterToSceneSpace(samplePointsKm[i]);
                slot.RootRect.position = new Vector3(world.x, posY, world.z);
                slot.RootRect.eulerAngles = new Vector3(
                    90f, CurrentVectorOverlayMath.ComputeMapYawDegrees(flow.x, flow.z), 0f);

                float head = spec.HeadSizeScene;
                float shaftLen = Mathf.Max(0.1f, totalLen - head);
                slot.ShaftRect.anchoredPosition = new Vector2(-0.5f * head, 0f);
                slot.ShaftRect.sizeDelta = new Vector2(shaftLen, spec.ArrowThicknessScene);
                slot.HeadRect.anchoredPosition = new Vector2(0.5f * totalLen - 0.5f * head, 0f);
                slot.HeadRect.sizeDelta = new Vector2(head, head);

                Color color = CurrentVectorOverlayMath.ComputeSpeedColor(speed);
                slot.Shaft.color = color;
                slot.Head.color = color;
            }
        }

        private Vector2 ResolveMapCenterEncounterKm()
        {
            Camera cam = mainCamera.Camera;
            if (cam != null)
            {
                Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                if (ray.direction.y < -0.01f)
                {
                    float t = -ray.origin.y / ray.direction.y;
                    Vector3 hit = ray.origin + ray.direction * t;
                    return sandbox.SceneToEncounterSpace(hit);
                }
                return sandbox.SceneToEncounterSpace(cam.transform.position);
            }
            return sandbox.SceneToEncounterSpace(mainCamera.transform.position);
        }

        private static float ResolveSampleDepthMeters()
        {
            float configured = CurrentSettings.GetVectorSampleDepthMeters();
            if (configured >= 0f)
                return configured;
            var ship = CurrentRuntimeAdapter.BoundShip;
            return ship != null ? Mathf.Max(0f, ship.Depth) : 0f;
        }

        private void SetContainerActive(bool active)
        {
            if (container != null && container.gameObject.activeSelf != active)
                container.gameObject.SetActive(active);
        }

        private void EnsureAssets()
        {
            if (overlayMaterial == null)
            {
                overlayMaterial = new Material(Graphic.defaultGraphicMaterial);
                overlayMaterial.name = "RadioNav Current Vectors";
                overlayMaterial.hideFlags = HideFlags.DontSave;
                overlayMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            }

            if (shaftTexture == null)
            {
                shaftTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                shaftTexture.hideFlags = HideFlags.DontSave;
                shaftTexture.SetPixel(0, 0, Color.white);
                shaftTexture.Apply();
                shaftSprite = Sprite.Create(shaftTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
                shaftSprite.hideFlags = HideFlags.DontSave;
            }

            if (headTexture == null)
                BuildTriangleHeadSprite();
        }

        private void BuildTriangleHeadSprite()
        {
            const int size = 32;
            headTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            headTexture.hideFlags = HideFlags.DontSave;
            headTexture.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                float ny = Mathf.Abs((y - half) / half);
                for (int x = 0; x < size; x++)
                {
                    float nx = x / (float)(size - 1);
                    float maxNy = 1f - nx;
                    float alpha = ny <= maxNy ? Mathf.Clamp01((maxNy - ny) * size * 0.35f) : 0f;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            headTexture.SetPixels(pixels);
            headTexture.Apply();
            headSprite = Sprite.Create(headTexture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            headSprite.hideFlags = HideFlags.DontSave;
        }

        private void BuildPool(RectTransform parent)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var rootGo = new GameObject("CurrentVec_" + i, typeof(RectTransform));
                rootGo.transform.SetParent(parent, false);
                rootGo.SetActive(false);
                var rootRect = rootGo.GetComponent<RectTransform>();
                rootRect.sizeDelta = Vector2.zero;

                var shaftGo = new GameObject("Shaft", typeof(RectTransform));
                shaftGo.transform.SetParent(rootRect, false);
                Image shaft = shaftGo.AddComponent<Image>();
                shaft.sprite = shaftSprite;
                shaft.material = overlayMaterial;
                shaft.raycastTarget = false;

                var headGo = new GameObject("Head", typeof(RectTransform));
                headGo.transform.SetParent(rootRect, false);
                Image head = headGo.AddComponent<Image>();
                head.sprite = headSprite;
                head.material = overlayMaterial;
                head.raycastTarget = false;

                slots[i] = new ArrowSlot
                {
                    Root = rootGo,
                    RootRect = rootRect,
                    Shaft = shaft,
                    ShaftRect = shaft.rectTransform,
                    Head = head,
                    HeadRect = head.rectTransform
                };
            }
        }
    }
}
