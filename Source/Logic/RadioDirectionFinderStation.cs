using System.Collections.Generic;
using System.Reflection;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core;
using UBOAT.Game.Core.AI.Behaviours;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Characters.Activators;
using UBOAT.Game.Scene.Characters.Skills;
using UBOAT.Game.Scene.Interactions;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Activator mounted on the radio room stool that offers the seated direction finder view in FPP.
    /// </summary>
    public sealed class RadioDirectionFinderActivator : Activator
    {
        private SitAtDirectionFinderAction actionTemplate;

        public override string BannerPrefabPath
        {
            get { return "UI/Banners/Default Equipment Banner"; }
        }

        public void Awake()
        {
            if (BannerDisplayInfo != null)
            {
                BannerDisplayInfo.Visibility = ActivatorBannerDisplayInfo.BannerVisibility.Hidden;
                BannerDisplayInfo.CameraModesMask = (CameraMode)0;
            }
        }

        public override void AddActionInfos(
            List<CharacterActionInfo> actions,
            PlayableCharacter character,
            ActionContext context,
            bool isPlayerOrder)
        {
            if (actions == null)
                return;

            if (actionTemplate == null)
                actionTemplate = new SitAtDirectionFinderAction(this);

            ActionStateData state = actionTemplate.GetState(this, character, false, isPlayerOrder, context);
            if (state.ActionState != ActionState.Hidden)
                actions.Add(new CharacterActionInfo(actionTemplate, state.ActionState));
        }
    }

    /// <summary>
    /// Seats the impersonated character on the radio stool facing T3 PLL 38 and activates
    /// the mouse-look direction finder camera until the player presses Esc.
    /// </summary>
    public sealed class SitAtDirectionFinderAction : SingleCharacterAction, IFPPCompatibleAction
    {
        private static MethodInfo setExternalCameraControllerMethod;
        private static bool setExternalLookupDone;

        private bool reachedStation;

        public SitAtDirectionFinderAction(Activator activator)
            : base(activator)
        {
        }

        public SitAtDirectionFinderAction(SitAtDirectionFinderAction source, Activator activator)
            : base(source, activator)
        {
        }

        public override bool AllowDelegationToOtherCharacters
        {
            get { return false; }
        }

        public override bool IsInterruptable
        {
            get { return true; }
        }

        public override ActionStateData GetState(
            Activator activator,
            PlayableCharacter character,
            bool fillDetails,
            bool isPlayerOrder,
            ActionContext actionContext)
        {
            if (!(activator is RadioDirectionFinderActivator) || character.IsNull() || !character.IsImpersonated || character.Health <= 0f)
                return ActionState.Hidden;

            MainCamera mainCamera = GetMainCamera();
            if (mainCamera != null)
            {
                if (mainCamera.CurrentMode != CameraMode.FPP)
                    return ActionState.Hidden;
            }

            if (character.Action is SitAtDirectionFinderAction)
                return ActionState.Hidden;

            Interactable interactable = activator.GetComponent<Interactable>();
            if (interactable.IsNull() || interactable.Slots == null || interactable.Slots.Length == 0)
                return ActionState.Hidden;

            InteractableSlot slot = interactable.Slots[0];
            if (slot.IsNull() || (slot.Character.IsNotNull() && slot.Character != character))
                return ActionState.Hidden;

            return ActionState.Enabled;
        }

        public override LocalizedString GetLocalizedName(
            Activator activator,
            PlayableCharacter character,
            ActionActivationContext activationContext)
        {
            return LocalizedString.CreateUnlocalized(RadioDFPoseMath.GetSitActionLabel(GetCurrentLanguage()));
        }

        public override string GetLocalizedDescription(Activator activator, PlayableCharacter character)
        {
            return RadioDFPoseMath.GetSitActionDescription(GetCurrentLanguage());
        }

        public override string GetIconName(Activator activator, PlayableCharacter character)
        {
            return "Hydrophone Listen";
        }

        private static string GetCurrentLanguage()
        {
            InjectionFramework framework = InjectionFramework.Instance;
            if (framework != null)
            {
                ILocale loc = framework.GetInstance<ILocale>();
                if (loc != null && !string.IsNullOrEmpty(loc.Language))
                    return loc.Language;
                Locale concrete = framework.GetInstance<Locale>();
                if (concrete != null && !string.IsNullOrEmpty(concrete.Language))
                    return concrete.Language;
            }
            return RadioLocale.GetCurrentLanguage();
        }

        public override string GetLocalizationKeyword(Activator activator, PlayableCharacter character)
        {
            return "Use";
        }

        protected override void StartMainCharacter(PlayableCharacter character)
        {
            character.CancelOrders();
            Interactable interactable = Activator.IsNotNull() ? Activator.GetComponent<Interactable>() : null;
            if (interactable.IsNotNull())
            {
                IUseable target = ResolveUseTarget(interactable);
                CompoundBehaviour useBehaviour = character.Use(target, 2.3f, this);
                GoToBehaviour goTo = useBehaviour != null ? useBehaviour.GetBehaviour<GoToBehaviour>() : null;
                if (goTo != null)
                    useBehaviour.RemoveBehaviour(goTo);
            }

            if (character.Ai.IsNotNull())
                character.Ai.UpdateAI();

            RadioDirectionFinderController controller = Activator.IsNotNull()
                ? Activator.GetComponent<RadioDirectionFinderController>()
                : null;
            ActivateCamera(controller, character);
        }

        public override void UpdateAction()
        {
            if (Character.IsNull())
                return;

            if (reachedStation != Character.IsInteracting)
            {
                reachedStation = Character.IsInteracting;
                OnChanged();
            }

            if (!IsCameraDrivingThisCharacter(Character))
                Character.StopCurrentAction();
        }

        protected override void StopMainCharacter(PlayableCharacter character)
        {
            if (character.IsNotNull())
                character.CancelBehavioursByDirector(this);
        }

        private static IUseable ResolveUseTarget(Interactable interactable)
        {
            InteractableSlot[] slots = interactable.Slots;
            if (slots == null)
                return interactable;
            return slots.Length > 0 ? (IUseable)slots[0] : interactable;
        }

        private static MainCamera GetMainCamera()
        {
            MainCamera direct = MainCamera.Instance;
            if (direct != null)
                return direct;
            InjectionFramework framework = InjectionFramework.Instance;
            return framework != null ? framework.GetInstance<MainCamera>() : null;
        }

        private static void ActivateCamera(RadioDirectionFinderController controller, PlayableCharacter character)
        {
            if (controller == null)
                return;

            MainCamera mainCamera = GetMainCamera();
            if (mainCamera == null)
                return;

            object proxy = controller.ExternalControllerProxy;
            if (proxy == null)
                return;

            MethodInfo method = GetSetExternalCameraMethod();
            method?.Invoke(mainCamera, new object[] { proxy, character, CameraTransitionType.Instant });
        }

        private static MethodInfo GetSetExternalCameraMethod()
        {
            if (setExternalLookupDone)
                return setExternalCameraControllerMethod;
            setExternalLookupDone = true;

            MethodInfo[] methods = typeof(MainCamera).GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == "SetExternalCameraController" && methods[i].GetParameters().Length == 3)
                {
                    setExternalCameraControllerMethod = methods[i];
                    break;
                }
            }
            return setExternalCameraControllerMethod;
        }

        private static bool IsCameraDrivingThisCharacter(PlayableCharacter character)
        {
            MainCamera mainCamera = GetMainCamera();
            if (mainCamera == null)
                return false;

            if (mainCamera.TransitionTargetMode == CameraMode.Generic
                && mainCamera.TransitionTargetModeDescriptor.Character == character)
            {
                return true;
            }

            return mainCamera.CurrentMode == CameraMode.Generic
                && mainCamera.GenericController != null
                && mainCamera.GenericController.Character == character;
        }
    }
}
