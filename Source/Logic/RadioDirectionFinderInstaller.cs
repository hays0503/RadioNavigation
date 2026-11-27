using System;
using System.Reflection;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.AI.Behaviours;
using UBOAT.Game.Scene.Characters.Activators;
using UBOAT.Game.Scene.Effects;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Interactions;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Selectables;
using UBOAT.Game.Scene.Spaces;
using UnityEngine;
using Activator = UBOAT.Game.Scene.Characters.Activators.Activator;
using Object = UnityEngine.Object;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Attaches the seated radio direction finder station to the player submarine's Radio Room stool.
    /// Survives boat swaps (port upgrades) and hot reloads without duplicating scene objects.
    /// </summary>
    public sealed class RadioDirectionFinderInstaller : MonoBehaviour
    {
        private const string RunnerName = "RadioDFInstaller";
        private const string SeatObjectName = "RadioDFSeat";
        private const string SlotObjectName = "Slot: Work DF";
        private const string CamAnchorName = "RadioDFCamAnchor";
        private const string DofAnchorName = "RadioDFDofTarget";
        private const string AnimOriginName = "RadioDFAnimOrigin";
        private const float PollIntervalSeconds = 0.5f;
        private const int InteriorLayer = 23;

        private static bool reflectionInitialized;
        private static FieldInfo interactableSlotsField;
        private static FieldInfo interactableMaxCharactersField;
        private static FieldInfo interactableOrderedSlotsField;
        private static FieldInfo interactableWorkerInteractablesField;
        private static FieldInfo slotAnimatorParamField;
        private static FieldInfo slotInteractionTypeField;
        private static FieldInfo slotTeleportLocationField;
        private static FieldInfo slotAlignModeField;
        private static FieldInfo slotAgentModeField;
        private static FieldInfo slotTriggerAnimField;
        private static FieldInfo slotMinTimeField;
        private static FieldInfo slotProvidesPerfField;
        private static FieldInfo slotDisabledInteractablesField;
        private static FieldInfo slotSynchronizedAnimatorsField;
        private static FieldInfo highlightStyleField;
        private static FieldInfo highlightRenderersField;
        private static FieldInfo selectableHighlightField;
        private static FieldInfo selectableTypeField;
        private static FieldInfo selectableTriggersField;
        private static FieldInfo selectableAcceptInputField;
        private static FieldInfo activatorBannerPointField;
        private static FieldInfo activatorSectionBannerPointField;

        private PlayerShip lastInstalledShip;
        private Transform lastInstalledStool;
        private float nextPollTime;

        internal static void EnsureRunning()
        {
            GameObject existing = GameObject.Find(RunnerName);
            if (existing.IsNotNull())
            {
                if (existing.GetComponent<RadioDirectionFinderInstaller>().IsNotNull())
                    return;

                existing.SetActive(false);
                Object.Destroy(existing);
            }

            GameObject go = new GameObject(RunnerName);
            go.AddComponent<RadioDirectionFinderInstaller>();
        }

        public void Start()
        {
            nextPollTime = 0f;
            TryInstallOnCurrentShip();
        }

        public void Update()
        {
            if (Time.unscaledTime < nextPollTime)
                return;
            nextPollTime = Time.unscaledTime + PollIntervalSeconds;
            TryInstallOnCurrentShip();
        }

        private static T GetService<T>() where T : class
        {
            InjectionFramework framework = InjectionFramework.Instance;
            return framework != null ? framework.GetInstance<T>() : null;
        }

        private void TryInstallOnCurrentShip()
        {
            IPlayerShipProxy proxy = GetService<IPlayerShipProxy>();
            PlayerShip ship = proxy != null ? proxy.CurrentShip : GetService<PlayerShip>();
            if (ship == null)
                return;

            PlayerShipRadio radio = ship.GetEquipment<PlayerShipRadio>();
            if (radio == null && ship.Interior != null)
                radio = ship.Interior.GetComponentInChildren<PlayerShipRadio>(true);
            if (radio == null)
                return;

            Transform stool = radio.transform.Find("Stool");
            if (stool == null)
            {
                lastInstalledShip = ship;
                lastInstalledStool = null;
                return;
            }

            if (ship == lastInstalledShip && stool == lastInstalledStool && HasCurrentSeat(stool))
                return;

            InstallOnStool(radio.transform, stool);
            lastInstalledShip = ship;
            lastInstalledStool = stool;
        }

        private static bool HasCurrentSeat(Transform stool)
        {
            Transform existingSeat = stool.Find(SeatObjectName);
            return existingSeat.IsNotNull() && existingSeat.GetComponent<RadioDirectionFinderController>().IsNotNull();
        }

        private static void InstallOnStool(Transform radioRoom, Transform stool)
        {
            EnsureReflectionFields();
            DestroyStaleChildren(radioRoom, stool);

            BoxCollider t3Collider = FindT3PllCollider(radioRoom);
            RadioDFPosedAnchor pose = ComputePoseForRadioRoom(radioRoom, stool);

            T3StubCutoutBoxes boxes = default(T3StubCutoutBoxes);
            GameObject prefab;
            Shader stubShader;
            T3PLLa38Rig receiverRig = null;
            if (t3Collider != null && T3PLLa38AssetLoader.TryLoadAssets(out prefab, out stubShader))
            {
                if (RadioDFStubRemover.Apply(radioRoom, t3Collider, stubShader, out boxes))
                    receiverRig = T3PLLa38Rig.Install(radioRoom, boxes);
            }

            Transform camAnchor = CreateChildTransform(radioRoom, CamAnchorName,
                new Vector3(pose.EyeX, pose.EyeY, pose.EyeZ), Quaternion.identity);
            float focusY = receiverRig != null ? boxes.MountY + (T3PLLa38ControlMath.CabinetHeightM * boxes.MountScale * 0.56f) : pose.TargetY;
            Vector3 lookDir = new Vector3(pose.TargetX - pose.EyeX, focusY - pose.EyeY, pose.TargetZ - pose.EyeZ);
            if (lookDir.sqrMagnitude > 1e-6f)
                camAnchor.localRotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);

            Transform dofAnchor = CreateChildTransform(radioRoom, DofAnchorName,
                new Vector3(pose.DofX, focusY, pose.DofZ), Quaternion.identity);

            Transform animOrigin = CreateAnimOrigin(radioRoom, stool, pose);

            GameObject seatGo = new GameObject(SeatObjectName);
            seatGo.layer = InteriorLayer;
            seatGo.transform.SetParent(stool, false);
            seatGo.transform.localPosition = Vector3.zero;
            seatGo.transform.localRotation = Quaternion.identity;
            seatGo.SetActive(false);

            ConfigureCollider(seatGo, stool);
            HighlightEffect highlight = ConfigureHighlight(seatGo, stool);
            InteractableSlot slot = CreateSlot(seatGo, animOrigin);
            ConfigureInteractable(seatGo, slot);
            seatGo.AddComponent<RadioDirectionFinderController>().Configure(camAnchor, dofAnchor, camAnchor.localRotation, receiverRig);
            ConfigureActivator(seatGo, stool);
            ConfigureSelectable(seatGo, highlight);

            seatGo.SetActive(true);
            Debug.Log("[RadioNav][DF] Installed seated direction finder station on Radio Room/Stool" + (receiverRig != null ? " with T3PLLa38 rig" : ""));
        }

        private static void DestroyStaleChildren(Transform radioRoom, Transform stool)
        {
            DestroyChildIfExists(stool, SeatObjectName);
            DestroyChildIfExists(stool, AnimOriginName);
            DestroyChildIfExists(radioRoom, CamAnchorName);
            DestroyChildIfExists(radioRoom, DofAnchorName);
        }

        private static void DestroyChildIfExists(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child.IsNull())
                return;

            child.gameObject.SetActive(false);
            Object.Destroy(child.gameObject);
        }

        private static Transform CreateChildTransform(Transform parent, string name, Vector3 localPos, Quaternion localRot)
        {
            GameObject go = new GameObject(name);
            go.layer = InteriorLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            return go.transform;
        }

        private static Transform CreateAnimOrigin(Transform radioRoom, Transform stool, RadioDFPosedAnchor pose)
        {
            Vector3 lookFlat = new Vector3(pose.TargetX - stool.localPosition.x, 0f, pose.TargetZ - stool.localPosition.z);
            Quaternion worldDir = lookFlat.sqrMagnitude > 1e-6f
                ? radioRoom.rotation * Quaternion.LookRotation(lookFlat.normalized, Vector3.up)
                : stool.rotation;

            GameObject originGo = new GameObject(AnimOriginName);
            originGo.layer = InteriorLayer;
            originGo.transform.SetParent(stool, false);
            originGo.transform.rotation = worldDir;
            originGo.transform.position = stool.position - (worldDir * Vector3.forward) * 0.30f;
            return originGo.transform;
        }

        private static void ConfigureCollider(GameObject seatGo, Transform stool)
        {
            MeshFilter mf = stool.GetComponent<MeshFilter>();
            bool hasBounds = mf.IsNotNull() && mf.sharedMesh.IsNotNull();
            Bounds b = hasBounds ? mf.sharedMesh.bounds : new Bounds();
            RadioDFStoolColliderBounds fitted = RadioDFPoseMath.ComputeColliderBounds(
                hasBounds, b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z);

            BoxCollider col = seatGo.AddComponent<BoxCollider>();
            col.center = new Vector3(fitted.CenterX, fitted.CenterY, fitted.CenterZ);
            col.size = new Vector3(fitted.SizeX, fitted.SizeY, fitted.SizeZ);
            col.isTrigger = false;
        }

        private static HighlightEffect ConfigureHighlight(GameObject seatGo, Transform stool)
        {
            HighlightEffect.SetOutline(seatGo, 1.8f, new Color(0.95f, 0.75f, 0.25f, 1f), 0.25f);
            HighlightEffect highlight = seatGo.GetComponent<HighlightEffect>();
            SetFieldValue(highlightStyleField, highlight, "Device");

            MeshRenderer stoolRenderer = stool.GetComponent<MeshRenderer>();
            if (stoolRenderer.IsNotNull())
                SetFieldValue(highlightRenderersField, highlight, new Renderer[] { stoolRenderer });
            return highlight;
        }

        private static InteractableSlot CreateSlot(GameObject seatGo, Transform animOrigin)
        {
            GameObject slotGo = new GameObject(SlotObjectName);
            slotGo.layer = InteriorLayer;
            slotGo.transform.SetParent(seatGo.transform, false);
            slotGo.transform.localPosition = Vector3.zero;
            slotGo.transform.localRotation = Quaternion.identity;

            InteractableSlot slot = slotGo.AddComponent<InteractableSlot>();
            slot.DisabledSlots = new InteractableSlot[0];
            SetFieldValue(slotAnimatorParamField, slot, "Radio");
            SetFieldValue(slotInteractionTypeField, slot, 232);
            SetFieldValue(slotTeleportLocationField, slot, animOrigin);
            SetFieldValue(slotAlignModeField, slot, AlignBehaviour.Mode.AlignPosition | AlignBehaviour.Mode.AlignRotation);
            SetFieldValue(slotTriggerAnimField, slot, false);
            SetFieldValue(slotMinTimeField, slot, 1000000f);
            SetFieldValue(slotProvidesPerfField, slot, false);
            SetFieldValue(slotDisabledInteractablesField, slot, new Interactable[0]);
            SetFieldValue(slotSynchronizedAnimatorsField, slot, new Animator[0]);
            if (slotAgentModeField != null)
            {
                object normalAgent = Enum.ToObject(slotAgentModeField.FieldType, 1);
                slotAgentModeField.SetValue(slot, normalAgent);
            }
            return slot;
        }

        private static void ConfigureInteractable(GameObject seatGo, InteractableSlot slot)
        {
            Interactable interactable = seatGo.AddComponent<Interactable>();
            SetFieldValue(interactableSlotsField, interactable, new InteractableSlot[] { slot });
            SetFieldValue(interactableMaxCharactersField, interactable, 1);
            SetFieldValue(interactableOrderedSlotsField, interactable, false);
            SetFieldValue(interactableWorkerInteractablesField, interactable, new Interactable[0]);
        }

        private static void ConfigureActivator(GameObject seatGo, Transform stool)
        {
            RadioDirectionFinderActivator activator = seatGo.AddComponent<RadioDirectionFinderActivator>();
            SetFieldValue(activatorBannerPointField, activator, stool);
            SetFieldValue(activatorSectionBannerPointField, activator, stool);
        }

        private static void ConfigureSelectable(GameObject seatGo, HighlightEffect highlight)
        {
            RaycastSelectable selectable = seatGo.AddComponent<RaycastSelectable>();
            SetFieldValue(selectableHighlightField, selectable, highlight);
            SetFieldValue(selectableTypeField, selectable, RaycastSelectable.SelectableType.Interactable);
            SetFieldValue(selectableTriggersField, selectable, RaycastSelectable.TriggersVisibility.FPP);
            SetFieldValue(selectableAcceptInputField, selectable, true);
        }

        private static RadioDFPosedAnchor ComputePoseForRadioRoom(Transform radioRoom, Transform stool)
        {
            BoxCollider t3Collider = FindT3PllCollider(radioRoom);
            bool hasT3 = t3Collider != null;
            Vector3 t3Center = hasT3 ? t3Collider.center : Vector3.zero;

            Transform clockTr = FindClockTransform(radioRoom);
            bool hasClock = clockTr != null;
            Vector3 clockPos = clockTr != null ? radioRoom.InverseTransformPoint(clockTr.position) : Vector3.zero;

            GyrocompassRepeater repeater = radioRoom.GetComponentInChildren<GyrocompassRepeater>(true);
            bool hasRep = repeater != null;
            Vector3 repPos = repeater != null ? radioRoom.InverseTransformPoint(repeater.transform.position) : Vector3.zero;

            Vector3 stoolLocal = stool.localPosition;
            return RadioDFPoseMath.ComputeStationAnchor(
                stoolLocal.x, stoolLocal.y, stoolLocal.z,
                hasT3, t3Center.x, t3Center.y, t3Center.z,
                hasClock, clockPos.x, clockPos.y, clockPos.z,
                hasRep, repPos.x, repPos.y, repPos.z);
        }

        private static BoxCollider FindT3PllCollider(Transform radioRoom)
        {
            BoxCollider[] colliders = radioRoom.GetComponents<BoxCollider>();
            BoxCollider best = null;
            float bestHeight = 0f;
            for (int i = 0; i < colliders.Length; i++)
            {
                BoxCollider col = colliders[i];
                if (col.IsNull())
                    continue;
                if (col.size.y > 0.55f && col.size.z < 0.35f && col.center.y > 0.8f && col.size.y > bestHeight)
                {
                    bestHeight = col.size.y;
                    best = col;
                }
            }
            return best;
        }

        private static Transform FindClockTransform(Transform radioRoom)
        {
            Transform direct = radioRoom.Find("Kriegsmarine Ship's Clock BR2");
            if (direct.IsNotNull())
                return direct;

            for (int i = 0; i < radioRoom.childCount; i++)
            {
                Transform child = radioRoom.GetChild(i);
                if (child.name.IndexOf("Clock", StringComparison.OrdinalIgnoreCase) >= 0)
                    return child;
            }
            return null;
        }

        private static void EnsureReflectionFields()
        {
            if (reflectionInitialized)
                return;
            reflectionInitialized = true;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            interactableSlotsField = typeof(Interactable).GetField("slots", flags);
            interactableMaxCharactersField = typeof(Interactable).GetField("maxCharacters", flags);
            interactableOrderedSlotsField = typeof(Interactable).GetField("orderedCrewSlotsUse", flags);
            interactableWorkerInteractablesField = typeof(Interactable).GetField("workerInteractables", flags);

            slotAnimatorParamField = typeof(InteractableSlot).GetField("animatorParameter", flags);
            slotInteractionTypeField = typeof(InteractableSlot).GetField("interactionType", flags);
            slotTeleportLocationField = typeof(InteractableSlot).GetField("teleportLocation", flags);
            slotAlignModeField = typeof(InteractableSlot).GetField("alignMode", flags);
            slotAgentModeField = typeof(InteractableSlot).GetField("agentMode", flags);
            slotTriggerAnimField = typeof(InteractableSlot).GetField("triggerAnimation", flags);
            slotMinTimeField = typeof(InteractableSlot).GetField("minInteractionTime", flags);
            slotProvidesPerfField = typeof(InteractableSlot).GetField("providesPerformance", flags);
            slotDisabledInteractablesField = typeof(InteractableSlot).GetField("disabledInteractables", flags);
            slotSynchronizedAnimatorsField = typeof(InteractableSlot).GetField("synchronizedAnimators", flags);

            highlightStyleField = typeof(HighlightEffect).GetField("style", flags);
            highlightRenderersField = typeof(HighlightEffect).GetField("renderers", flags);

            selectableHighlightField = typeof(RaycastSelectable).GetField("highlightEffect", flags);
            selectableTypeField = typeof(RaycastSelectable).GetField("type", flags);
            selectableTriggersField = typeof(RaycastSelectable).GetField("triggersVisibility", flags);
            selectableAcceptInputField = typeof(RaycastSelectable).GetField("acceptUserInput", flags);

            activatorBannerPointField = typeof(Activator).GetField("bannerAttachmentPoint", flags);
            activatorSectionBannerPointField = typeof(Activator).GetField("sectionBannerAttachmentPoint", flags);
        }

        private static void SetFieldValue(FieldInfo field, object target, object value)
        {
            if (field != null && target != null)
                field.SetValue(target, value);
        }
    }
}
