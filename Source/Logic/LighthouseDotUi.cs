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
        private Component coreImage;
        private Component haloImage;
        private Component streakImage;
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
            get { return coreImage != null ? coreImage.GetComponent<RectTransform>() : null; }
        }
        public RectTransform HaloRect
        {
            get { return haloImage != null ? haloImage.GetComponent<RectTransform>() : null; }
        }
        public RectTransform StreakRect
        {
            get { return streakImage != null ? streakImage.GetComponent<RectTransform>() : null; }
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
        /// Builds the canvas and the three images once. Safe to call again
        /// after a failed attempt (retries the Image type resolution).
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
        /// once and cached statically.
        /// </summary>
        public void SetImageColor(Component image, float alpha)
        {
            if (image.IsNull())
            {
                return;
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

            Debug.LogWarning("[RadioNav][Lighthouse] glow: '" + ImageTypeName +
                "' not found; screen-space dot fallback unavailable");
            return false;
        }
    }
}
