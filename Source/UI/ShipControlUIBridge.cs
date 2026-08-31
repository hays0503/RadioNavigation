using System;
using System.Reflection;
using UBOAT.Game.Core.Input;
using UBOAT.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Encapsulates every string-name reflection lookup against the native
    /// ShipControlUI / CursorManager / GameInput surfaces (CH-2, extracted
    /// from ChronometerOverlayHandler). All lookups are resolved once, cached,
    /// and null-guarded — the game assemblies are obfuscated, so a renamed or
    /// missing member must degrade gracefully instead of throwing.
    /// </summary>
    public static class ShipControlUIBridge
    {
        private static readonly FieldInfo DragContainerField = typeof(ShipControlUI).GetField(
            "chronometerToolDragContainer", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ButtonField = typeof(ShipControlUI).GetField(
            "chronometerButton", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DefaultPositionField = typeof(ShipControlUI).GetField(
            "chronometerToolDefaultPos", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DragStateField = typeof(ShipControlUI).GetField(
            "chronometerToolDragState", BindingFlags.Instance | BindingFlags.NonPublic);

        private static FieldInfo uiDialsDataHolderField;
        private static PropertyInfo chronoPosOverrideProp;
        private static MethodInfo applyToolPositionLimitsMethod;
        private static bool positionBridgeProbed;

        private static FieldInfo normalCursorTexField;
        private static Texture2D gameDefaultCursor;
        private static object dragStateNoneValue;

        public static bool HasDragContainer => DragContainerField != null;
        public static bool HasButton => ButtonField != null;
        public static bool HasDefaultPosition => DefaultPositionField != null;
        public static bool HasDragState => DragStateField != null;

        public static RectTransform GetDragContainer(ShipControlUI ui)
        {
            return DragContainerField == null ? null : DragContainerField.GetValue(ui) as RectTransform;
        }

        public static Toggle GetButton(ShipControlUI ui)
        {
            return ButtonField == null ? null : ButtonField.GetValue(ui) as Toggle;
        }

        public static Vector2? GetDefaultPosition(ShipControlUI ui)
        {
            if (DefaultPositionField == null)
                return null;
            object value = DefaultPositionField.GetValue(ui);
            return value is Vector2 v ? v : (Vector2?)null;
        }

        public static void SetDefaultPosition(ShipControlUI ui, Vector2 position)
        {
            if (DefaultPositionField != null)
                DefaultPositionField.SetValue(ui, position);
        }

        /// <summary>Forces the native drag state back to None (enum boxed to its underlying type).</summary>
        public static void ResetNativeDragState(ShipControlUI ui)
        {
            if (DragStateField == null)
                return;
            // Cache the boxed None value: this runs every frame during an
            // active local drag, so avoid re-boxing via Enum.ToObject each time.
            if (dragStateNoneValue == null)
                dragStateNoneValue = Enum.ToObject(DragStateField.FieldType, 0);
            DragStateField.SetValue(ui, dragStateNoneValue);
        }

        /// <summary>
        /// Lazily resolves the expanded-drag position bridge (static holder +
        /// interface property + edge-clamp method). Returns false until every
        /// piece is present; never throws.
        /// </summary>
        public static bool TryResolvePositionBridge()
        {
            if (positionBridgeProbed)
                return chronoPosOverrideProp != null && applyToolPositionLimitsMethod != null;
            positionBridgeProbed = true;

            try
            {
                if (uiDialsDataHolderField == null)
                    uiDialsDataHolderField = typeof(ShipControlUI).GetField(
                        "uiDialsDataHolder", BindingFlags.Static | BindingFlags.NonPublic);
                object holder = uiDialsDataHolderField?.GetValue(null);
                if (holder == null)
                    return false;

                Type iface = holder.GetType().GetInterface("IUserInterfaceDialsDataHolder");
                if (iface != null)
                    chronoPosOverrideProp = iface.GetProperty("ChronometerPosOverride");
                if (applyToolPositionLimitsMethod == null)
                    applyToolPositionLimitsMethod = typeof(ShipControlUI).GetMethod(
                        "ApplyToolPositionLimits", BindingFlags.Static | BindingFlags.NonPublic);

                return chronoPosOverrideProp != null && applyToolPositionLimitsMethod != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RadioNav][Chronometer] expanded-drag position bridge failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Writes the normalized screen position through the native override + clamp.</summary>
        public static bool PersistExpandedPosition(RectTransform container)
        {
            if (!TryResolvePositionBridge())
                return false;
            try
            {
                object holder = uiDialsDataHolderField.GetValue(null);
                Vector2 current = (Vector2)chronoPosOverrideProp.GetValue(holder, null);
                Vector2 screenPos = container.position;
                current = ChronometerMapDragMath.ToNormalizedOverride(screenPos, Screen.width, Screen.height);
                Vector2 limited = (Vector2)applyToolPositionLimitsMethod.Invoke(
                    null, new object[] { current, container });
                chronoPosOverrideProp.SetValue(holder, limited, null);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RadioNav][Chronometer] expanded-drag persist failed: " + e.Message);
                return false;
            }
        }

        /// <summary>The game's default cursor texture, resolved once via reflection.</summary>
        public static Texture2D GetGameDefaultCursor(CursorManager cursorManager)
        {
            if (gameDefaultCursor != null || cursorManager == null)
                return gameDefaultCursor;
            try
            {
                if (normalCursorTexField == null)
                    normalCursorTexField = typeof(CursorManager).GetField(
                        "normalCursorTex", BindingFlags.Instance | BindingFlags.NonPublic);
                gameDefaultCursor = normalCursorTexField?.GetValue(cursorManager) as Texture2D;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RadioNav][Chronometer] default cursor texture probe failed: " + e.Message);
            }
            return gameDefaultCursor;
        }
    }
}
