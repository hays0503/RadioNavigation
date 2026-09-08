using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Procedural sprite generation for the sextant overlay: tube-shadow
    /// ring, eyecup shade, and the hard circle mask. Extracted from
    /// SextantController (SRP: pixel-art generation is not device logic).
    /// The controller keeps ownership of the textures for teardown.
    /// </summary>
    internal static class SextantSprites
    {
        // Tube light falloff (ring): clear center, gradual shadow, fully
        // black before the mask edge so no seam shows at the rim.
        private const float TubeShadowStart = 0.55f;
        private const float TubeShadowMid = 0.85f;
        private const float TubeShadowMidAlpha = 0.45f;
        private const float TubeShadowFull = 0.97f;

        // Eyecup (наглазник): solid black up to the ocular rim, long soft
        // falloff outward (out-of-focus rubber close to the eye).
        private const float EyecupSolidTo = 0.60f;

        /// <summary>Creates the ring texture; caller owns and must destroy it.</summary>
        public static Texture2D CreateRingTexture()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "RadioNavSextantRing";
            tex.hideFlags = HideFlags.DontSave;
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a;
                    if (r <= TubeShadowStart) a = 0f;
                    else if (r >= TubeShadowFull) a = 1f;
                    else if (r <= TubeShadowMid)
                    {
                        float t = (r - TubeShadowStart) / (TubeShadowMid - TubeShadowStart);
                        a = TubeShadowMidAlpha * (t * t * (3f - 2f * t));
                    }
                    else
                    {
                        float t = (r - TubeShadowMid) / (TubeShadowFull - TubeShadowMid);
                        a = TubeShadowMidAlpha + (1f - TubeShadowMidAlpha) * (t * t * (3f - 2f * t));
                    }
                    tex.SetPixel(x, y, new Color(0f, 0f, 0f, a));
                }
            }
            tex.Apply();
            return tex;
        }

        public static Sprite CreateRingSprite(Texture2D ringTexture)
        {
            return Sprite.Create(ringTexture, new Rect(0f, 0f, ringTexture.width, ringTexture.height), new Vector2(0.5f, 0.5f));
        }

        /// <summary>Creates the eyecup texture; caller owns and must destroy it.</summary>
        public static Texture2D CreateEyecupTexture()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "RadioNavSextantEyecup";
            tex.hideFlags = HideFlags.DontSave;
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a;
                    if (r <= EyecupSolidTo) a = 1f;
                    else if (r >= 1f) a = 0f;
                    else
                    {
                        float t = (r - EyecupSolidTo) / (1f - EyecupSolidTo);
                        a = 1f - t * t * (3f - 2f * t);
                    }
                    tex.SetPixel(x, y, new Color(0f, 0f, 0f, a));
                }
            }
            tex.Apply();
            return tex;
        }

        public static Sprite CreateEyecupSprite(Texture2D tex)
        {
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        /// <summary>Creates the circle-mask texture; caller owns and must destroy it.</summary>
        public static Texture2D CreateCircleMaskTexture()
        {
            const int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "RadioNavSextantCircleMask";
            tex.hideFlags = HideFlags.DontSave;
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a;
                    if (r <= 0.96f) a = 1f;
                    else if (r >= 1f) a = 0f;
                    else a = 1f - (r - 0.96f) / 0.04f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return tex;
        }

        /// <summary>Hard circle sprite for the ocular Mask (smooth AA at the rim).</summary>
        public static Sprite CreateCircleMaskSprite(Texture2D tex)
        {
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
    }
}
