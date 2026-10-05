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
        // Eyecup (наглазник): solid black up to the ocular rim, long soft
        // falloff outward (out-of-focus rubber close to the eye). The radius it
        // stays opaque to is what makes it able to hide the stencil cut — see
        // SextantRimProfile.ShadeCoversAperture.
        private const float EyecupSolidTo = SextantRimProfile.EyecupSolidTo;

        /// <summary>Creates the ring texture; caller owns and must destroy it.</summary>
        public static Texture2D CreateRingTexture()
        {
            const int size = SextantRimProfile.TextureSize;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "RadioNavSextantRing";
            tex.hideFlags = HideFlags.DontSave;
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            float half = size * 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float lightFacing = r > 0f
                        ? (-dx + dy) * 0.70710678f / r : 0f;
                    SextantRimSample sample = SextantRimProfile.Evaluate(r, lightFacing);
                    pixels[y * size + x] = new Color32(
                        ToByte(sample.R), ToByte(sample.G), ToByte(sample.B), ToByte(sample.A));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        private static byte ToByte(float value)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
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
                    // Subtle rubber grain changes RGB only; the established alpha
                    // silhouette/falloff is intentionally untouched.
                    float grain = SextantOpticsProfile.Hash01(x / 3, y / 3, 73) - 0.5f;
                    float rubber = Mathf.Clamp(0.012f + grain * 0.008f, 0.006f, 0.018f);
                    tex.SetPixel(x, y, new Color(rubber, rubber * 0.98f, rubber * 0.94f, a));
                }
            }
            tex.Apply();
            return tex;
        }

        /// <summary>Low-contrast dust/oil texture; transparent at centre and rim.</summary>
        public static Texture2D CreateOpticsGrimeTexture()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "RadioNavSextantOpticsGrime";
            tex.hideFlags = HideFlags.DontSave;
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = ((x + 0.5f) / size) * 2f - 1f;
                float py = ((y + 0.5f) / size) * 2f - 1f;
                float r = Mathf.Sqrt(px * px + py * py);
                float coverage = SextantOpticsProfile.GrimeCoverage(r);
                float noise = SextantOpticsProfile.Hash01(x, y, 191);
                float fleck = noise > 0.992f ? (noise - 0.992f) * 0.035f : 0f;
                byte alpha = ToByte(Mathf.Clamp(coverage * (0.018f + fleck), 0f, 0.025f));
                byte shade = ToByte(0.72f + noise * 0.18f);
                pixels[y * size + x] = new Color32(shade, shade, shade, alpha);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
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
