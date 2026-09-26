using System;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Screen-space uGUI overlay for the lighthouse dot fallback (P5.4
    /// LBG-2): a hidden overlay Canvas with three reflection-built Image
    /// children (core dot, halo, streak). Built without UnityEngine.UI
    /// compile references (the game's Roslyn mod compiler does not include
    /// them) — the Image type and its sprite/color/raycastTarget properties
    /// are resolved via cached reflection. Extracted from
    /// LighthouseBeamGlow (SRP: the uGUI plumbing is not the visibility
    /// controller's logic). One instance per lighthouse.
    /// </summary>
    internal sealed class LighthouseDotUi
    {
        private const string ImageTypeName = "UnityEngine.UI.Image";
        private const int CanvasSortingOrder = 500;

        private static readonly Color DotColor = new Color(1f, 0.94f, 0.84f);
        private static PropertyInfo imageColorProperty;

        private Canvas canvas;
        private Type imageType;
        private bool imageTypeResolveFailed;
        private Component coreImage;
        private Component haloImage;
        private Component streakImage;
        // F235: the three rects are stable for the lifetime of the overlay.
        // Getters used to call GetComponent on every LighthouseBeamGlow frame.
        private RectTransform coreRect;
        private RectTransform haloRect;
        private RectTransform streakRect;
        private float lastCoreAlpha = float.NaN;
        private float lastHaloAlpha = float.NaN;
        private float lastStreakAlpha = float.NaN;
        // Sprite.Create assets are NOT owned by the canvas GameObject —
        // destroying the canvas leaks them unless freed explicitly.
        private Sprite coreSprite;
        private Sprite streakSprite;

        /// <summary>True after a successful build (false when the Image type
        /// is unavailable — the dot fallback stays disabled).</summary>
        public bool Built { get; private set; }

        public bool Enabled
        {
            get { return canvas != null && canvas.enabled; }
        }

        /// <summary>The overlay canvas (null before a successful build).</summary>
        public Canvas Canvas
        {
            get { return canvas; }
        }

        public RectTransform CoreRect
        {
            get { return coreRect; }
        }
        public RectTransform HaloRect
        {
            get { return haloRect; }
        }
        public RectTransform StreakRect
        {
            get { return streakRect; }
        }
        public Component StreakImage
        {
            get { return streakImage; }
        }
        public Component CoreImage
        {
            get { return coreImage; }
        }
        public Component HaloImage
        {
            get { return haloImage; }
        }

        /// <summary>
        /// Builds the canvas and the three images once. A failed Image type
        /// resolution is latched (F223) so callers do not re-scan assemblies
        /// every frame.
        /// </summary>
        public bool EnsureBuilt(string ownerName)
        {
            if (Built)
                return true;
            if (!ResolveImageType())
                return false;

            var canvasGo = new GameObject("RadioNavLighthouseDot_" + ownerName, typeof(RectTransform));
            var canvasRect = canvasGo.GetComponent<RectTransform>();
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            canvas.enabled = false;

            Texture2D texture = LighthouseGlowArt.GetDotTexture();
            coreSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));

            Texture2D streakTex = LighthouseGlowArt.GetStreakTexture();
            streakSprite = Sprite.Create(streakTex, new Rect(0f, 0f, streakTex.width, streakTex.height), new Vector2(0.5f, 0.5f));

            streakImage = CreateImage(canvasRect, "Streak", streakSprite);
            coreImage = CreateImage(canvasRect, "Core", coreSprite);
            haloImage = CreateImage(canvasRect, "Halo", coreSprite);
            coreRect = coreImage != null ? coreImage.GetComponent<RectTransform>() : null;
            haloRect = haloImage != null ? haloImage.GetComponent<RectTransform>() : null;
            streakRect = streakImage != null ? streakImage.GetComponent<RectTransform>() : null;
            Built = true;
            return true;
        }

        /// <summary>Shows/hides the whole overlay canvas.</summary>
        public void SetEnabled(bool enabled)
        {
            if (canvas != null)
                canvas.enabled = enabled;
        }

        /// <summary>Destroys the canvas GameObject and the owned sprites
        /// (controller teardown).</summary>
        public void Destroy()
        {
            if (canvas != null)
                Object.Destroy(canvas.gameObject);
            canvas = null;
            coreImage = null;
            haloImage = null;
            streakImage = null;
            coreRect = null;
            haloRect = null;
            streakRect = null;
            lastCoreAlpha = float.NaN;
            lastHaloAlpha = float.NaN;
            lastStreakAlpha = float.NaN;
            if (coreSprite != null)
                Object.Destroy(coreSprite);
            if (streakSprite != null)
                Object.Destroy(streakSprite);
            coreSprite = null;
            streakSprite = null;
            Built = false;
        }

        /// <summary>
        /// Sets the alpha of a reflection-built Image. Hot path (dot +
        /// halo + streak every frame): the reflection property is resolved
        /// once and the last value is latched. Avoiding an unchanged SetValue
        /// avoids both the reflection call and the Color boxing it requires.
        /// </summary>
        public void SetImageColor(Component image, float alpha)
        {
            if (image.IsNull())
            {
                return;
            }
            bool isCore = ReferenceEquals(image, coreImage);
            bool isHalo = ReferenceEquals(image, haloImage);
            bool isStreak = ReferenceEquals(image, streakImage);
            if (isCore)
            {
                if (!float.IsNaN(lastCoreAlpha) && Math.Abs(lastCoreAlpha - alpha) < 0.0001f)
                    return;
                lastCoreAlpha = alpha;
            }
            else if (isHalo)
            {
                if (!float.IsNaN(lastHaloAlpha) && Math.Abs(lastHaloAlpha - alpha) < 0.0001f)
                    return;
                lastHaloAlpha = alpha;
            }
            else if (isStreak)
            {
                if (!float.IsNaN(lastStreakAlpha) && Math.Abs(lastStreakAlpha - alpha) < 0.0001f)
                    return;
                lastStreakAlpha = alpha;
            }
            if (imageColorProperty == null)
            {
                imageColorProperty = imageType.GetProperty("color", BindingFlags.Public | BindingFlags.Instance);
            }
            imageColorProperty
                ?.SetValue(image, new Color(DotColor.r, DotColor.g, DotColor.b, alpha), null);
        }

        private Component CreateImage(RectTransform parent, string childName, Sprite sprite)
        {
            var child = new GameObject(childName, typeof(RectTransform));
            var childRect = child.GetComponent<RectTransform>();
            childRect.SetParent(parent, false);

            Component image = child.AddComponent(imageType);
            imageType.GetProperty("sprite", BindingFlags.Public | BindingFlags.Instance)
                ?.SetValue(image, sprite, null);
            imageType.GetProperty("raycastTarget", BindingFlags.Public | BindingFlags.Instance)
                ?.SetValue(image, false, null);
            return image;
        }

        private bool ResolveImageType()
        {
            if (imageType.IsNotNull())
            {
                return true;
            }
            // F223: negative cache. Without it every frame re-scanned all
            // loaded assemblies and re-logged the warning when UnityEngine.UI
            // was unavailable. Core modules are loaded at startup, so a miss
            // is permanent for the session.
            if (imageTypeResolveFailed)
            {
                return false;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(ImageTypeName, false);
                if (type.IsNotNull())
                {
                    imageType = type;
                    return true;
                }
            }

            imageTypeResolveFailed = true;
            Debug.LogWarning("[RadioNav][Lighthouse] glow: '" + ImageTypeName +
                "' not found; screen-space dot fallback unavailable");
            return false;
        }
    }
}
