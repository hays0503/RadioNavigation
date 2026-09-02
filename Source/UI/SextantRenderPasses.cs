using System;
using System.Reflection;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Reflection-based cloning of the main camera's SRP render passes onto
    /// the sextant PIP cameras. The game's SRP renders a camera through the
    /// passes configured on its AdditionalCameraData; a freshly added
    /// component has none, so the RT stays blank (in-game finding
    /// 2026-08-26). Extracted from SextantController (SRP: reflection
    /// plumbing is not device logic).
    /// </summary>
    internal static class SextantRenderPasses
    {
        private const string LogPrefix = "[RadioNav][Sextant]";

        /// <summary>
        /// Deep-copies the main camera's pass list when available - those
        /// are the exact culling groups the game renders the scene with;
        /// otherwise builds a single enabled pass with the camera's
        /// culling mask. Backing fields are private: "passes" (CameraPass[])
        /// and "useCustomPasses" (bool) - the Passes property is get-only.
        /// </summary>
        public static void Setup(UBOAT.Game.Scene.Rendering.AdditionalCameraData data,
            UBOAT.Game.Scene.Rendering.AdditionalCameraData mainData, int cullingMask)
        {
            Type type = data.GetType();
            Type passType = type.GetNestedType("CameraPass",
                BindingFlags.Public | BindingFlags.NonPublic);
            if (passType == null)
            {
                Debug.LogWarning(LogPrefix + " CameraPass type not found - PIP may stay blank");
                return;
            }

            try
            {
                FieldInfo passesField = null;
                FieldInfo useCustomField = null;
                FindPassFields(type, passType, ref passesField, ref useCustomField);

                Array source = ReadSourcePasses(mainData, type, passesField);
                int count = source != null && source.Length > 0 ? source.Length : 1;
                Array passes = CopyPasses(passType, source, cullingMask);

                WritePasses(data, type, passesField, useCustomField, passes);
                data.RenderScene = true;
                Debug.Log(LogPrefix + " PIP render passes configured: " + count + " pass(es), mask=" + cullingMask);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + " render pass setup failed: " + e.Message + " " + e.StackTrace);
            }
        }

        private static void FindPassFields(Type type, Type passType, ref FieldInfo passesField, ref FieldInfo useCustomField)
        {
            // Passes is get-only (CanWrite=false) - find backing field by type
            foreach (FieldInfo f in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (f.FieldType.IsArray && f.FieldType.GetElementType() == passType)
                    passesField = f;
                else if (f.FieldType == typeof(bool) && f.Name.IndexOf("useCustom", StringComparison.OrdinalIgnoreCase) >= 0)
                    useCustomField = f;
            }
            if (passesField == null)
                passesField = type.GetField("passes", BindingFlags.NonPublic | BindingFlags.Instance);
            if (useCustomField == null)
                useCustomField = type.GetField("useCustomPasses", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private static Array ReadSourcePasses(UBOAT.Game.Scene.Rendering.AdditionalCameraData mainData,
            Type type, FieldInfo passesField)
        {
            if (mainData == null)
                return null;
            PropertyInfo passesProp = type.GetProperty("Passes");
            Array source = passesProp != null ? passesProp.GetValue(mainData) as Array : null;
            // fallback: read backing field directly if property missing
            if (source == null && passesField != null)
                source = passesField.GetValue(mainData) as Array;
            return source;
        }

        private static Array CopyPasses(Type passType, Array source, int cullingMask)
        {
            int count = source != null && source.Length > 0 ? source.Length : 1;
            Array passes = Array.CreateInstance(passType, count);
            if (source != null && source.Length > 0)
            {
                // CameraPass(int cullingMask) is the only public ctor;
                // Enabled is read-write, ScreenSpaceReflections is
                // get-only and stays at its default.
                PropertyInfo cullingProp = passType.GetProperty("CullingMask");
                PropertyInfo enabledProp = passType.GetProperty("Enabled");
                for (int i = 0; i < source.Length; i++)
                {
                    object src = source.GetValue(i);
                    int mask = cullingMask;
                    if (cullingProp != null)
                    {
                        object maskBoxed = cullingProp.GetValue(src);
                        if (maskBoxed is int)
                            mask = (int)maskBoxed;
                    }
                    object copy = Activator.CreateInstance(passType, mask);
                    if (enabledProp != null)
                    {
                        object enabled = enabledProp.GetValue(src);
                        if (enabled is bool)
                            enabledProp.SetValue(copy, (bool)enabled);
                    }
                    passes.SetValue(copy, i);
                }
            }
            else
            {
                object pass = Activator.CreateInstance(passType, cullingMask);
                passType.GetProperty("Enabled").SetValue(pass, true);
                passes.SetValue(pass, 0);
            }
            return passes;
        }

        private static void WritePasses(UBOAT.Game.Scene.Rendering.AdditionalCameraData data,
            Type type, FieldInfo passesField, FieldInfo useCustomField, Array passes)
        {
            if (passesField != null)
                passesField.SetValue(data, passes);
            else
                type.GetProperty("Passes").SetValue(data, passes);
            if (useCustomField != null)
                useCustomField.SetValue(data, true);
            else
            {
                PropertyInfo useCustomProp = type.GetProperty("UseCustomPasses");
                if (useCustomProp != null && useCustomProp.CanWrite)
                    useCustomProp.SetValue(data, true);
            }
        }
    }
}
