using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Static art assets and small geometry helpers for the lighthouse glow
    /// (P5.4 LBG slices): the procedural dot/streak textures (cached once,
    /// shared by every lighthouse instance), the screen-space horizon angle
    /// for the streak rotation, and the window-renderer sweep sync
    /// (_RotationOffset on the lighthouse shader). Extracted from
    /// LighthouseBeamGlow (SRP: pixel generation and shader plumbing are not
    /// the visibility controller's logic).
    /// </summary>
    internal static class LighthouseGlowArt
    {
        private const string LighthouseShaderName = "Standard (DWS, Lighthouse)";
        private const int StreakTextureWidth = 128;
        private const int StreakTextureHeight = 16;
        private const int DotTextureSize = 64;

        private static readonly Color DotColor = new Color(1f, 0.94f, 0.84f);
        private static readonly int RotationOffsetId = Shader.PropertyToID("_RotationOffset");

        private static Texture2D dotTexture;
        private static Texture2D streakTexture;

        /// <summary>
        /// Soft radial dot texture (cached; shared across all lighthouse
        /// instances). Never destroyed - hideFlags.DontSave, one per session.
        /// </summary>
        public static Texture2D GetDotTexture()
        {
            if (dotTexture.IsNotNull())
            {
                return dotTexture;
            }

            dotTexture = new Texture2D(DotTextureSize, DotTextureSize, TextureFormat.RGBA32, false);
            dotTexture.name = "RadioNavLighthouseDot";
            dotTexture.hideFlags = HideFlags.DontSave;
            float half = DotTextureSize * 0.5f;
            for (int y = 0; y < DotTextureSize; y++)
            {
                for (int x = 0; x < DotTextureSize; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float falloff = r >= 1f ? 0f : (1f - r) * (1f - r);
                    Color32 pixel = new Color32(
                        (byte)Mathf.RoundToInt(DotColor.r * 255f),
                        (byte)Mathf.RoundToInt(DotColor.g * 255f),
                        (byte)Mathf.RoundToInt(DotColor.b * 255f),
                        (byte)Mathf.RoundToInt(falloff * 255f));
                    dotTexture.SetPixel(x, y, pixel);
                }
            }
            dotTexture.Apply();
            return dotTexture;
        }

        /// <summary>
        /// Horizontal line texture for the screen-space streak element:
        /// a soft gaussian falloff along the line axis and a tighter one
        /// across it, so the stretched glow reads as a thin beam.
        /// </summary>
        public static Texture2D GetStreakTexture()
        {
            if (streakTexture.IsNotNull())
            {
                return streakTexture;
            }

            streakTexture = new Texture2D(StreakTextureWidth, StreakTextureHeight, TextureFormat.RGBA32, false);
            streakTexture.name = "RadioNavLighthouseStreak";
            streakTexture.hideFlags = HideFlags.DontSave;
            float hw = StreakTextureWidth * 0.5f;
            float hh = StreakTextureHeight * 0.5f;
            for (int y = 0; y < StreakTextureHeight; y++)
            {
                for (int x = 0; x < StreakTextureWidth; x++)
                {
                    float dx = (x + 0.5f - hw) / hw;
                    float dy = (y + 0.5f - hh) / hh;
                    float falloff = Mathf.Exp(-(dx * dx * 2.5f)) * Mathf.Exp(-(dy * dy * 5f));
                    Color32 pixel = new Color32(
                        (byte)Mathf.RoundToInt(DotColor.r * 255f),
                        (byte)Mathf.RoundToInt(DotColor.g * 255f),
                        (byte)Mathf.RoundToInt(DotColor.b * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(falloff) * 255f));
                    streakTexture.SetPixel(x, y, pixel);
                }
            }
            streakTexture.Apply();
            return streakTexture;
        }

        /// <summary>
        /// Screen-space angle of the world horizon line, so the streak
        /// stays parallel to the ground even when the camera rolls
        /// (boat heel, wave motion). The world direction is the ground
        /// plane vector perpendicular to the camera's view direction,
        /// projected through the camera into screen space.
        /// </summary>
        public static float GetHorizonAngleDeg(Camera cam, Vector3 worldPos)
        {
            Vector3 forward = cam.transform.forward;
            Vector3 groundForward = new Vector3(forward.x, 0f, forward.z);
            if (groundForward.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }
            groundForward.Normalize();
            Vector3 horizonDir = Vector3.Cross(groundForward, Vector3.up);
            Vector3 p0 = cam.WorldToScreenPoint(worldPos);
            Vector3 p1 = cam.WorldToScreenPoint(worldPos + horizonDir * 1000f);
            float dx = p1.x - p0.x;
            float dy = p1.y - p0.y;
            if (dx * dx + dy * dy < 0.0001f)
            {
                return 0f;
            }
            return Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// ADR-017/018: releases the shared textures on hot-reload. Main
        /// thread only, called when all glow GameObjects are gone.
        /// </summary>
        public static void ResetStatics()
        {
            if (dotTexture.IsNotNull())
            {
                Object.Destroy(dotTexture);
                dotTexture = null;
            }
            if (streakTexture.IsNotNull())
            {
                Object.Destroy(streakTexture);
                streakTexture = null;
            }
        }

        /// <summary>
        /// Collects renderers whose material uses the lighthouse shader so
        /// their _RotationOffset can be fed in sync with the rotating beam
        /// (the emissive windows sweep together with the light).
        /// </summary>
        public static List<Renderer> FindWindowRenderers(Transform rotator)
        {
            var windowRenderers = new List<Renderer>();
            Renderer[] renderers = rotator.root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer.IsNull() || renderer.sharedMaterial.IsNull())
                {
                    continue;
                }
                Shader shader = renderer.sharedMaterial.shader;
                if (shader.IsNotNull() && shader.name == LighthouseShaderName)
                {
                    windowRenderers.Add(renderer);
                }
            }
            return windowRenderers;
        }

        /// <summary>
        /// Pushes the rotator's current Y angle to every window renderer's
        /// _RotationOffset via a shared MaterialPropertyBlock.
        /// </summary>
        public static void UpdateWindowRotation(List<Renderer> windowRenderers,
            Transform rotator, ref MaterialPropertyBlock rotationBlock)
        {
            if (windowRenderers.Count == 0 || rotator.IsNull())
            {
                return;
            }
            if (rotationBlock.IsNull())
            {
                rotationBlock = new MaterialPropertyBlock();
            }
            rotationBlock.SetFloat(RotationOffsetId, rotator.eulerAngles.y);
            for (int i = 0; i < windowRenderers.Count; i++)
            {
                if (windowRenderers[i].IsNotNull())
                {
                    windowRenderers[i].SetPropertyBlock(rotationBlock);
                }
            }
        }
    }
}
