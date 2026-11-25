using System;
using System.Reflection;
using System.Reflection.Emit;
using DWS.Common.InjectionFramework;
using DWS.Common.Resources;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Core.Input;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Characters.Activators;
using UBOAT.Game.Scene.Characters.Navigation;
using UBOAT.Game.Scene.Selectables;
using UBOAT.Game.UI;
using UnityEngine;
using GameMainCamera = UBOAT.Game.Scene.Camera.MainCamera;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// External camera controller backing the seated radio direction finder station.
    /// Keeps cursor hidden for free mouse-look around the radio corner and exits cleanly on Esc.
    /// Exposed to GenericCameraController via a tiny runtime IExternalController proxy so the
    /// mod does not take a compile-time dependency on postprocessingv1.
    /// </summary>
    public sealed class RadioDirectionFinderController : MonoBehaviour
    {
        private const string ExternalInterfaceName = "UBOAT.Game.Scene.Camera.ExternalControllers.IExternalController";

        private static Type proxyType;
        private static bool proxyBuildAttempted;

        private Transform cameraAnchor;
        private Transform dofTargetAnchor;
        private Quaternion baseLocalRotation = Quaternion.identity;
        private ResourceLink<InputAxis> horizontalLookAxis;
        private ResourceLink<InputAxis> verticalLookAxis;
        private UnityEngine.Camera cachedMainCamera;
        private object externalProxy;
        private float yawDeg;
        private float pitchDeg;

        public UnityEngine.Camera MainCamera
        {
            get { return cachedMainCamera.IsNotNull() ? cachedMainCamera : UnityEngine.Camera.main; }
        }

        public Vector3 DOFTarget
        {
            get { return dofTargetAnchor.IsNotNull() ? dofTargetAnchor.position : transform.position; }
        }

        public bool Interior
        {
            get { return true; }
        }

        internal object ExternalControllerProxy
        {
            get
            {
                if (externalProxy == null)
                    externalProxy = CreateExternalProxy(this);
                return externalProxy;
            }
        }

        internal void Configure(Transform camAnchor, Transform dofAnchor, Quaternion initialLocalRotation)
        {
            cameraAnchor = camAnchor;
            dofTargetAnchor = dofAnchor;
            baseLocalRotation = initialLocalRotation;
        }

        public bool CanBeUsedByCharacter(PlayableCharacter character)
        {
            return character.IsNotNull();
        }

        public void StartController(GenericCameraController controller)
        {
            cachedMainCamera = UnityEngine.Camera.main;
            EnsureLookAxes();
            yawDeg = 0f;
            pitchDeg = 0f;

            if (cameraAnchor.IsNotNull())
                cameraAnchor.localRotation = baseLocalRotation;

            if (controller.IsNull())
                return;

            controller.DisplayCharacter = false;
            if (cameraAnchor.IsNotNull())
                controller.TweenCameraTo(cameraAnchor, 0.35f);
        }

        public void UpdateController(GenericCameraController controller)
        {
            if (cameraAnchor.IsNull() || controller.IsNull())
                return;

            if (!CanLookAround(controller))
                return;

            EnsureLookAxes();
            float deltaH = ReadAxisValue(horizontalLookAxis);
            float deltaV = ReadAxisValue(verticalLookAxis);
            if (deltaH == 0f && deltaV == 0f)
                return;

            float sensitivity = GetLookSensitivity();
            bool invert = GetInvertMouse();
            RadioDFPoseMath.StepLookAngles(yawDeg, pitchDeg, deltaH, deltaV, sensitivity, invert, out yawDeg, out pitchDeg);
            cameraAnchor.localRotation = baseLocalRotation * Quaternion.Euler(pitchDeg, yawDeg, 0f);
        }

        public bool OnUserExitRequest()
        {
            return true;
        }

        public void StopController(GenericCameraController controller, CameraModeDescriptor nextModeDescriptor)
        {
            if (cameraAnchor.IsNotNull())
                cameraAnchor.localRotation = baseLocalRotation;

            if (nextModeDescriptor.Mode == CameraMode.FPP)
                return;

            PlayableCharacter character = controller.IsNotNull() ? controller.Character : null;
            if (character.IsNotNull() && character.Action is SitAtDirectionFinderAction)
                character.StopCurrentAction();
        }

        private void EnsureLookAxes()
        {
            if (horizontalLookAxis == null)
                horizontalLookAxis = new ResourceLink<InputAxis>("Inputs/Horizontal Look");
            if (verticalLookAxis == null)
                verticalLookAxis = new ResourceLink<InputAxis>("Inputs/Vertical Look");
        }

        private static bool CanLookAround(GenericCameraController controller)
        {
            if (controller.IsFullInterfaceVisible)
                return false;

            GameInput gameInput = GetService<GameInput>();
            if (gameInput != null)
            {
                if (gameInput.HasActiveBlockades())
                    return false;
            }

            CursorManager cursorManager = GetService<CursorManager>();
            if (cursorManager != null)
            {
                if (!cursorManager.CursorHidden)
                    return false;
            }

            return true;
        }

        private static T GetService<T>() where T : class
        {
            InjectionFramework framework = InjectionFramework.Instance;
            return framework != null ? framework.GetInstance<T>() : null;
        }

        private static float ReadAxisValue(ResourceLink<InputAxis> axisLink)
        {
            if (axisLink == null)
                return 0f;
            InputAxis asset = axisLink.Asset;
            return asset != null ? asset.Value : 0f;
        }

        private static float GetLookSensitivity()
        {
            UserSettings userSettings = GetService<UserSettings>();
            if (userSettings == null)
                return 1f;
            ControlSettings settings = userSettings.ControlSettings;
            if (settings == null)
                return 1f;
            return settings.cameraLookSensitivity;
        }

        private static bool GetInvertMouse()
        {
            UserSettings userSettings = GetService<UserSettings>();
            if (userSettings == null)
                return false;
            ControlSettings settings = userSettings.ControlSettings;
            if (settings == null)
                return false;
            return settings.invertMouse;
        }

        private static object CreateExternalProxy(RadioDirectionFinderController controller)
        {
            Type type = EnsureProxyType();
            return type != null ? System.Activator.CreateInstance(type, new object[] { controller }) : null;
        }

        private static Type EnsureProxyType()
        {
            if (proxyBuildAttempted)
                return proxyType;
            proxyBuildAttempted = true;

            Type ifaceType = typeof(MainCamera).Assembly.GetType(ExternalInterfaceName, false);
            if (ifaceType == null)
                return null;

            string suffix = Guid.NewGuid().ToString("N");
            AssemblyBuilder asm = AppDomain.CurrentDomain.DefineDynamicAssembly(
                new AssemblyName("RadioDFProxyAsm_" + suffix), AssemblyBuilderAccess.Run);
            ModuleBuilder mod = asm.DefineDynamicModule("RadioDFProxyMod");
            TypeBuilder tb = mod.DefineType(
                "RadioDFExternalProxy_" + suffix,
                TypeAttributes.Public | TypeAttributes.Sealed,
                typeof(object),
                new Type[] { ifaceType });

            FieldBuilder targetField = tb.DefineField(
                "target",
                typeof(RadioDirectionFinderController),
                FieldAttributes.Private | FieldAttributes.InitOnly);

            EmitConstructor(tb, targetField);
            EmitInterfaceMethods(tb, ifaceType, targetField);
            proxyType = tb.CreateType();
            return proxyType;
        }

        private static void EmitConstructor(TypeBuilder tb, FieldBuilder targetField)
        {
            ConstructorBuilder ctor = tb.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                new Type[] { typeof(RadioDirectionFinderController) });
            ConstructorInfo objCtor = typeof(object).GetConstructor(Type.EmptyTypes);

            ILGenerator il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            if (objCtor != null)
                il.Emit(OpCodes.Call, objCtor);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, targetField);
            il.Emit(OpCodes.Ret);
        }

        private static void EmitInterfaceMethods(TypeBuilder tb, Type ifaceType, FieldBuilder targetField)
        {
            MethodInfo[] own = ifaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < own.Length; i++)
                EmitMethodForwarder(tb, own[i], targetField);

            Type[] parents = ifaceType.GetInterfaces();
            for (int i = 0; i < parents.Length; i++)
            {
                MethodInfo[] parentMethods = parents[i].GetMethods(BindingFlags.Public | BindingFlags.Instance);
                for (int j = 0; j < parentMethods.Length; j++)
                    EmitMethodForwarder(tb, parentMethods[j], targetField);
            }
        }

        private static void EmitMethodForwarder(TypeBuilder tb, MethodInfo ifaceMethod, FieldBuilder targetField)
        {
            const MethodAttributes attrs =
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot | MethodAttributes.HideBySig;
            MethodBuilder mb = tb.DefineMethod(ifaceMethod.Name, attrs);
            GenericTypeParameterBuilder[] genParams = DefineGenericArgsIfNeeded(mb, ifaceMethod);

            ParameterInfo[] pars = ifaceMethod.GetParameters();
            Type[] paramTypes = new Type[pars.Length];
            for (int i = 0; i < pars.Length; i++)
                paramTypes[i] = SubstituteGenericArgs(pars[i].ParameterType, genParams);

            Type returnType = SubstituteGenericArgs(ifaceMethod.ReturnType, genParams);
            mb.SetReturnType(returnType);
            mb.SetParameters(paramTypes);

            ILGenerator il = mb.GetILGenerator();
            if (ifaceMethod.Name == "get_PostProcessingProfile" || genParams != null)
            {
                EmitDefaultReturn(il, returnType);
                tb.DefineMethodOverride(mb, ifaceMethod);
                return;
            }

            MethodInfo impl = typeof(RadioDirectionFinderController).GetMethod(
                ifaceMethod.Name, BindingFlags.Public | BindingFlags.Instance, null, paramTypes, null);

            if (impl != null)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, targetField);
                for (int i = 0; i < paramTypes.Length; i++)
                    EmitLdarg(il, i + 1);
                il.Emit(OpCodes.Callvirt, impl);
                il.Emit(OpCodes.Ret);
            }
            else
            {
                EmitDefaultReturn(il, returnType);
            }

            tb.DefineMethodOverride(mb, ifaceMethod);
        }

        private static GenericTypeParameterBuilder[] DefineGenericArgsIfNeeded(MethodBuilder mb, MethodInfo ifaceMethod)
        {
            if (!ifaceMethod.IsGenericMethodDefinition)
                return null;
            Type[] origArgs = ifaceMethod.GetGenericArguments();
            string[] names = new string[origArgs.Length];
            for (int i = 0; i < origArgs.Length; i++)
                names[i] = origArgs[i].Name;
            return mb.DefineGenericParameters(names);
        }

        private static Type SubstituteGenericArgs(Type type, GenericTypeParameterBuilder[] genParams)
        {
            if (genParams == null)
                return type;
            if (type.IsGenericParameter)
                return genParams[type.GenericParameterPosition];
            if (type.IsArray)
                return SubstituteGenericArgs(type.GetElementType(), genParams).MakeArrayType();
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                Type[] args = type.GetGenericArguments();
                for (int i = 0; i < args.Length; i++)
                    args[i] = SubstituteGenericArgs(args[i], genParams);
                return type.GetGenericTypeDefinition().MakeGenericType(args);
            }
            return type;
        }

        private static void EmitLdarg(ILGenerator il, int index)
        {
            if (index == 1)
                il.Emit(OpCodes.Ldarg_1);
            else if (index == 2)
                il.Emit(OpCodes.Ldarg_2);
            else if (index == 3)
                il.Emit(OpCodes.Ldarg_3);
            else
                il.Emit(OpCodes.Ldarg_S, (byte)index);
        }

        private static void EmitDefaultReturn(ILGenerator il, Type returnType)
        {
            if (returnType == typeof(void))
            {
                il.Emit(OpCodes.Ret);
                return;
            }
            if (returnType == typeof(bool))
            {
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ret);
                return;
            }
            LocalBuilder loc = il.DeclareLocal(returnType);
            il.Emit(OpCodes.Ldloca, loc);
            il.Emit(OpCodes.Initobj, returnType);
            il.Emit(OpCodes.Ldloc, loc);
            il.Emit(OpCodes.Ret);
        }

        public static string ProbeAimAtStool()
        {
            RadioDirectionFinderController ctrl = FindObjectOfType<RadioDirectionFinderController>();
            GameMainCamera cam = GameMainCamera.Instance;
            if (ctrl == null || cam == null)
                return "missing-controller-or-cam";
            if (cam.FPPController == null)
                return "missing-fpp";

            PlayableCharacter ch = cam.FPPController.Character;
            if (ch == null)
                return "no-fpp-character";

            Vector3 stoolWorld = ctrl.transform.position + new Vector3(0f, 0.42f, 0f);
            Vector3 toChar = ch.transform.position - ctrl.transform.position;
            toChar.y = 0f;
            if (toChar.sqrMagnitude < 0.45f)
            {
                Vector3 backSpot = ctrl.transform.position + new Vector3(-0.35f, 0f, 0.55f);
                NavMeshNavigator nav = ch.Navigator;
                if (nav.IsNotNull())
                    nav.Warp(backSpot, LayerLookupMode.Auto);
                ch.transform.position = backSpot;
            }

            Vector3 headPos = ch.transform.position + new Vector3(0f, 1.48f, 0f);
            Vector3 localDir = ch.transform.InverseTransformDirection((stoolWorld - headPos).normalized);
            float yaw = Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(localDir.y, -1f, 1f)) * Mathf.Rad2Deg, -80f, 65f);

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo fx = typeof(FPPCameraController).GetField("fppX", flags);
            FieldInfo fy = typeof(FPPCameraController).GetField("fppY", flags);
            fx?.SetValue(cam.FPPController, yaw);
            fy?.SetValue(cam.FPPController, pitch);

            RaycastSelectable sel = ctrl.GetComponent<RaycastSelectable>();
            RaycastSelectable hov = RaycastSelectable.hoveredSelectable;
            string hovered = hov != null ? hov.name : "null";
            return string.Format("aimed yaw={0:F1} pitch={1:F1} sel={2} hovered={3}", yaw, pitch, sel != null, hovered);
        }

        public static string ProbeEnterStation()
        {
            try
            {
                RadioDirectionFinderActivator activator = FindObjectOfType<RadioDirectionFinderActivator>();
                GameMainCamera cam = GameMainCamera.Instance;
                if (activator == null || cam == null)
                    return "missing-activator-or-cam";
                if (cam.FPPController == null)
                    return "missing-fpp";

                PlayableCharacter ch = cam.FPPController.Character;
                if (ch == null)
                    return "no-character";

                var template = new SitAtDirectionFinderAction(activator);
                ActionStateData state = template.GetState(activator, ch, false, true, default(ActionContext));
                if (state.ActionState != ActionState.Enabled)
                    return "state=" + state.ActionState;

                CharacterAction instance = template.Duplicate(activator);
                instance.IsDirectPlayerOrder = true;
                instance.AddCharacter(ch);
                if (instance.Stage != CharacterActionStage.Completed && ch.ActionQueue != null)
                    ch.ActionQueue.ResetAndAddPreStarted(instance);
                UBOAT.Game.Core.AI.AIBehaviour topBehaviour = ch.Ai?.GetActiveBehaviour<UBOAT.Game.Core.AI.AIBehaviour>();
                string topAct = topBehaviour != null ? topBehaviour.GetType().Name : "none";
                return string.Format(
                    "entered cam={0} prev={1} targ={2} int={3} slot={4} res={5} act={6} topAi={7}",
                    cam.CurrentMode, cam.PreviousMode, cam.TransitionTargetMode,
                    ch.IsInteracting,
                    ch.InteractableSlot != null ? ch.InteractableSlot.name : "null",
                    ch.ReservedSlot != null ? ch.ReservedSlot.name : "null",
                    ch.Action != null ? ch.Action.GetType().Name : "null",
                    topAct);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return "EX:" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        public static string ProbeLookDelta(float deltaYaw, float deltaPitch)
        {
            RadioDirectionFinderController ctrl = FindObjectOfType<RadioDirectionFinderController>();
            if (ctrl == null)
                return "missing-controller";
            if (ctrl.cameraAnchor == null)
                return "missing-anchor";

            RadioDFPoseMath.StepLookAngles(
                ctrl.yawDeg, ctrl.pitchDeg, deltaYaw, deltaPitch, 1f, false, out ctrl.yawDeg, out ctrl.pitchDeg);
            ctrl.cameraAnchor.localRotation = ctrl.baseLocalRotation * Quaternion.Euler(ctrl.pitchDeg, ctrl.yawDeg, 0f);
            return string.Format("yaw={0:F1} pitch={1:F1}", ctrl.yawDeg, ctrl.pitchDeg);
        }

        public static string ProbeRequestExit()
        {
            RadioDirectionFinderController ctrl = FindObjectOfType<RadioDirectionFinderController>();
            GameMainCamera cam = GameMainCamera.Instance;
            if (ctrl == null || cam == null)
                return "missing-controller-or-cam";
            if (cam.GenericController == null)
                return "not-in-generic";

            ctrl.OnUserExitRequest();

            PlayableCharacter ch = cam.GenericController.Character;
            cam.SetFPPController(ch, CameraTransitionType.FadeInOut, true);
            return "exited";
        }
    }
}
