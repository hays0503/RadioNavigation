using System;
using System.Globalization;
using DWS.Common.Resources;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Builds the sextant ocular assembly (§13.3): dark veil, eyecup shade,
    /// the circular ocular (Mask) with the two PIP RawImage halves, sight
    /// lines, shade-filter dimmers, glare flash and the edge ring. Extracted
    /// from SextantOverlay.Build (SRP: lens assembly vs text columns).
    /// Pure builder: fills the shared Result DTO, owns nothing.
    /// </summary>
    internal static class SextantOcularBuilder
    {
        private const string LogPrefix = "[RadioNav][Sextant]";
        private const float VeilAlpha = 0.85f;
        private const float EyecupSizeUnits = 1000f;
        // Bundle asset names (manifest "Paths:" section): the opaque view
        // material and the shader it is built from.
        private const string OpaqueViewAssetName = "RadioNavOpaqueView";
        private const string OpaqueViewShaderName = "RadioNavUIOpaque";

        /// <summary>
        /// Builds veil, eyecup, ocular, views, sights, dimmers, glare and
        /// ring under canvasRoot. canvasScaleFactor sizes the 1-pixel sight
        /// lines; ocularSizeUnits is the live ocular side (SextantOcularSize);
        /// resourceManager/assetContext resolve the opaque view material (may
        /// yield null — views keep the stock UI shader).
        /// </summary>
        public static void BuildOcular(GameObject canvasRoot, float canvasScaleFactor,
            RenderTexture leftRt, RenderTexture rightRt,
            ResourceManager resourceManager, UnityEngine.Object assetContext,
            SextantOverlay.Result result, float ocularSizeUnits)
        {
            if (ocularSizeUnits <= 0f || float.IsNaN(ocularSizeUnits))
                ocularSizeUnits = 600f;
            result.OcularSizeUnits = ocularSizeUnits;

            // Veil: solid dark layer, no cut-out (§13.3 - aspect-safe).
            GameObject veilGo = new GameObject("Veil", typeof(Image));
            veilGo.transform.SetParent(canvasRoot.transform, false);
            Image veil = veilGo.GetComponent<Image>();
            veil.color = new Color(0f, 0f, 0f, VeilAlpha);
            Stretch(veil.rectTransform);

            // Eyecup (наглазник): black shade larger than the ocular, drawn
            // between the veil and the lens so the world around the field of
            // view sinks into near-black while instrument texts (later
            // siblings) stay readable.
            GameObject eyecupGo = new GameObject("Eyecup", typeof(Image));
            eyecupGo.transform.SetParent(canvasRoot.transform, false);
            Image eyecup = eyecupGo.GetComponent<Image>();
            Texture2D eyecupTexture = SextantSprites.CreateEyecupTexture();
            result.EyecupTexture = eyecupTexture;
            Sprite eyecupSprite = SextantSprites.CreateEyecupSprite(eyecupTexture);
            eyecup.sprite = eyecupSprite;
            result.OwnedSprites.Add(eyecupSprite);
            eyecup.raycastTarget = false;
            AnchorCenter(eyecup.rectTransform, EyecupSizeUnits, EyecupSizeUnits);

            // Ocular: square container clipped to a circle (in-game finding
            // 2026-08-26: unclipped corners painted opaque black over the live
            // horizon). Mask clips both RT views, glare and ring to the circle;
            // outside it the veil shows through.
            GameObject pipGo = new GameObject("Ocular", typeof(Image), typeof(Mask));
            pipGo.transform.SetParent(canvasRoot.transform, false);
            result.PipRect = pipGo.GetComponent<RectTransform>();
            AnchorCenter(result.PipRect, ocularSizeUnits, ocularSizeUnits);
            Image maskGraphic = pipGo.GetComponent<Image>();
            result.CircleMaskTexture = SextantSprites.CreateCircleMaskTexture();
            Sprite circleMaskSprite = SextantSprites.CreateCircleMaskSprite(result.CircleMaskTexture);
            maskGraphic.sprite = circleMaskSprite;
            result.OwnedSprites.Add(circleMaskSprite);
            maskGraphic.raycastTarget = false;
            Mask ocularMask = pipGo.GetComponent<Mask>();
            ocularMask.showMaskGraphic = false;

            // Opaque black plate under the PIP views: hides the world behind
            // the lenses regardless of the RT alpha left by the game's post
            // stack. Clipped to the circle by the Mask; the veil outside
            // stays unchanged (vignette).
            GameObject backingGo = new GameObject("LensBacking", typeof(Image));
            backingGo.transform.SetParent(pipGo.transform, false);
            Image backing = backingGo.GetComponent<Image>();
            backing.color = new Color(0f, 0f, 0f, 1f);
            backing.raycastTarget = false;
            Stretch(backing.rectTransform);

            // Left half: fixed-horizon camera view (pitch 0). Shows only the
            // left half of its RT so the optical axis sits at the split line
            // (screen center); the right half of the frame is not drawn.
            GameObject leftGo = new GameObject("LeftView", typeof(RawImage));
            leftGo.transform.SetParent(pipGo.transform, false);
            result.LeftViewImage = leftGo.GetComponent<RawImage>();
            result.LeftViewImage.raycastTarget = false;
            result.LeftViewImage.texture = leftRt;
            result.LeftViewImage.rectTransform.anchorMin = new Vector2(0f, 0f);
            result.LeftViewImage.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            result.LeftViewImage.rectTransform.offsetMin = Vector2.zero;
            result.LeftViewImage.rectTransform.offsetMax = Vector2.zero;
            result.LeftViewImage.uvRect = new Rect(0f, 0f, 0.5f, 1f);

            // Right half: mirror camera pitched to the arc angle (0-130).
            // Right half of its RT only; at arc=0 the halves form one
            // continuous circling picture.
            GameObject rightGo = new GameObject("RightView", typeof(RawImage));
            rightGo.transform.SetParent(pipGo.transform, false);
            result.RightViewImage = rightGo.GetComponent<RawImage>();
            result.RightViewImage.raycastTarget = false;
            result.RightViewImage.texture = rightRt;
            result.RightViewImage.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            result.RightViewImage.rectTransform.anchorMax = new Vector2(1f, 1f);
            result.RightViewImage.rectTransform.offsetMin = Vector2.zero;
            result.RightViewImage.rectTransform.offsetMax = Vector2.zero;
            result.RightViewImage.uvRect = new Rect(0.5f, 0f, 0.5f, 1f);

            // Opaque view material (bundle shader RadioNav/UIOpaque, Blend
            // One Zero): the game's post stack writes alpha < 1 into the
            // RTs and the stock UI shader blends the black lens backing
            // through, darkening the picture.
            result.OpaqueViewMaterial = ResolveOpaqueViewMaterial(resourceManager, assetContext);
            if (result.OpaqueViewMaterial != null)
            {
                result.LeftViewImage.material = result.OpaqueViewMaterial;
                result.RightViewImage.material = result.OpaqueViewMaterial;
            }

            // Vertical sight line (1 physical pixel — drum alignment hairline
            // between the direct/mirror halves).
            GameObject splitGo = new GameObject("VerticalSplit", typeof(Image));
            splitGo.transform.SetParent(pipGo.transform, false);
            Image splitImage = splitGo.GetComponent<Image>();
            splitImage.color = new Color(1f, 1f, 1f, 0.5f);
            splitImage.raycastTarget = false;
            AnchorCenter(splitImage.rectTransform, 1f / canvasScaleFactor, ocularSizeUnits);
            result.VerticalSplitImage = splitImage;

            // Horizontal sight line (1 physical pixel, crosshair) — line of
            // sight, drawn above the views/below filter dims so it stays
            // subtle and never interferes with the lenses.
            GameObject horizGo = new GameObject("HorizontalSight", typeof(Image));
            horizGo.transform.SetParent(pipGo.transform, false);
            Image horizImage = horizGo.GetComponent<Image>();
            horizImage.color = new Color(1f, 1f, 1f, 0.5f);
            horizImage.raycastTarget = false;
            AnchorCenter(horizImage.rectTransform, ocularSizeUnits, 1f / canvasScaleFactor);
            result.HorizontalSightImage = horizImage;

            // Centred aiming window (PU-style: top + sides, no bottom line)
            // whose size equals the identification cone (author 2026-09-22):
            // three 1-px bars so the outline stays crisp at 9-12 px.
            result.SightLineThicknessUnits = 1f / canvasScaleFactor;
            result.ConeBoxTopImage = MakeSightBar(pipGo.transform, "ConeBoxTop");
            result.ConeBoxLeftImage = MakeSightBar(pipGo.transform, "ConeBoxLeft");
            result.ConeBoxRightImage = MakeSightBar(pipGo.transform, "ConeBoxRight");

            // Shade filters dim the scope view (v1.5): separate left (horizon)
            // and right (sun) half dimmers - like the separate shade glasses
            // of a real sextant.
            GameObject dimLeftGo = new GameObject("FilterDimLeft", typeof(Image));
            dimLeftGo.transform.SetParent(pipGo.transform, false);
            result.FilterDimLeftImage = dimLeftGo.GetComponent<Image>();
            result.FilterDimLeftImage.color = new Color(0f, 0f, 0f, 0f);
            result.FilterDimLeftImage.raycastTarget = false;
            RectTransform dimLeftRt = result.FilterDimLeftImage.rectTransform;
            dimLeftRt.anchorMin = new Vector2(0f, 0f);
            dimLeftRt.anchorMax = new Vector2(0.5f, 1f);
            dimLeftRt.offsetMin = Vector2.zero;
            dimLeftRt.offsetMax = Vector2.zero;

            GameObject dimRightGo = new GameObject("FilterDimRight", typeof(Image));
            dimRightGo.transform.SetParent(pipGo.transform, false);
            result.FilterDimRightImage = dimRightGo.GetComponent<Image>();
            result.FilterDimRightImage.color = new Color(0f, 0f, 0f, 0f);
            result.FilterDimRightImage.raycastTarget = false;
            RectTransform dimRightRt = result.FilterDimRightImage.rectTransform;
            dimRightRt.anchorMin = new Vector2(0.5f, 0f);
            dimRightRt.anchorMax = new Vector2(1f, 1f);
            dimRightRt.offsetMin = Vector2.zero;
            dimRightRt.offsetMax = Vector2.zero;

            // One-shot placement proof: screen-space corners of both dim
            // halves (matches the FILTERSIDE logs when a side is pressed).
            try
            {
                Vector3[] corners = new Vector3[4];
                result.FilterDimLeftImage.rectTransform.GetWorldCorners(corners);
                Vector2[] tl = new Vector2[4];
                for (int i = 0; i < 4; i++)
                    tl[i] = corners[i];
                result.FilterDimRightImage.rectTransform.GetWorldCorners(corners);
                Vector2[] tr = new Vector2[4];
                for (int i = 0; i < 4; i++)
                    tr[i] = corners[i];
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    LogPrefix + " DIMRECT left=({0:F0},{1:F0})-({2:F0},{3:F0}) right=({4:F0},{5:F0})-({6:F0},{7:F0}) screen={8}x{9}",
                    tl[0].x, tl[0].y, tl[2].x, tl[2].y,
                    tr[0].x, tr[0].y, tr[2].x, tr[2].y, Screen.width, Screen.height));
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SextantOcularBuilder.DimRect"))
                    Debug.LogWarning("[RadioNav][Sextant] DIMRECT proof failed: " + e.Message);
            }

            // Glare flash inside the ocular only.
            GameObject glareGo = new GameObject("Glare", typeof(Image));
            glareGo.transform.SetParent(pipGo.transform, false);
            result.GlareImage = glareGo.GetComponent<Image>();
            result.GlareImage.color = new Color(1f, 1f, 1f, 0f);
            result.GlareImage.raycastTarget = false;
            Stretch(result.GlareImage.rectTransform);

            // Soft radial edge blending the ocular rim into the veil.
            GameObject ringGo = new GameObject("EdgeRing", typeof(Image));
            ringGo.transform.SetParent(pipGo.transform, false);
            Image ring = ringGo.GetComponent<Image>();
            result.RingTexture = SextantSprites.CreateRingTexture();
            Sprite ringSprite = SextantSprites.CreateRingSprite(result.RingTexture);
            ring.sprite = ringSprite;
            result.OwnedSprites.Add(ringSprite);
            ring.raycastTarget = false;
            Stretch(ring.rectTransform);
        }

        /// <summary>Creates one 1-px aiming-window bar (top/left/right).</summary>
        private static Image MakeSightBar(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.5f);
            image.raycastTarget = false;
            AnchorCenter(image.rectTransform, 0f, 0f);
            return image;
        }

        /// <summary>Sizes and colours the aiming window (P7 follow-up): the
        /// side equals the identification cone in canvas units, the outline is
        /// one physical pixel, the colour follows the day/night policy. Called
        /// every frame while the sextant is open — every write is guarded.</summary>
        public static void ApplyConeBox(SextantOverlay.Result result, float sideUnits,
            Color color)
        {
            if (result == null || result.ConeBoxTopImage == null)
                return;

            float half = sideUnits * 0.5f;
            float thickness = result.SightLineThicknessUnits;
            ApplyBar(result.ConeBoxTopImage, sideUnits, thickness, 0f, half, color);
            ApplyBar(result.ConeBoxLeftImage, thickness, sideUnits, -half, 0f, color);
            ApplyBar(result.ConeBoxRightImage, thickness, sideUnits, half, 0f, color);
        }

        /// <summary>
        /// Re-applies the 1-physical-pixel hairline thickness (2026-09-22):
        /// the overlay follows the game's UI scale, so the unit thickness
        /// changes with it (1 unit = uiScale pixels). The cone bars read the
        /// value from the result every frame; the two sight lines are sized
        /// here.
        /// </summary>
        public static void ApplySightLineThickness(SextantOverlay.Result result,
            float thicknessUnits)
        {
            if (result == null || thicknessUnits <= 0f)
                return;

            result.SightLineThicknessUnits = thicknessUnits;
            float ocular = result.OcularSizeUnits > 0f ? result.OcularSizeUnits : 600f;
            if (result.VerticalSplitImage != null)
            {
                AnchorCenter(result.VerticalSplitImage.rectTransform, thicknessUnits,
                    ocular);
            }
            if (result.HorizontalSightImage != null)
            {
                AnchorCenter(result.HorizontalSightImage.rectTransform,
                    ocular, thicknessUnits);
            }
        }

        private static void ApplyBar(Image bar, float width, float height,
            float x, float y, Color color)
        {
            if (bar == null)
                return;

            RectTransform rt = bar.rectTransform;
            Vector2 size = new Vector2(width, height);
            if (rt.sizeDelta != size)
                rt.sizeDelta = size;
            Vector2 position = new Vector2(x, y);
            if (rt.anchoredPosition != position)
                rt.anchoredPosition = position;
            if (bar.color != color)
                bar.color = color;
        }

        /// <summary>
        /// Loads the opaque view material from the mod's AssetBundle
        /// (RadioNavOpaqueView, shader RadioNav/UIOpaque). The game
        /// ResourceManager is tried first, but on 2026.1-patch21 it never
        /// registers the mod bundle (F229) and silently degrades to
        /// Resources.Load, so the material is loaded straight from the
        /// already-loaded bundle as a fallback. GPU-verified 2026-09-17
        /// (RenderDoc capture): without it the stock UI shader premultiplies
        /// the view by the RT alpha (0.03..0.64) and the ocular picture is
        /// darkened/gated. Returns null only when the asset is genuinely
        /// missing — the views then keep the stock UI shader.
        /// </summary>
        private static Material ResolveOpaqueViewMaterial(ResourceManager resourceManager, UnityEngine.Object assetContext)
        {
            if (resourceManager != null)
            {
                Resource<Material> material =
                    resourceManager.RetrieveAsset<Material>(OpaqueViewAssetName, assetContext, false);
                if (material.Valid && material.Asset != null)
                    return material.Asset;
            }

            // F229 fallback: the bundle is loaded (ModBootstrapper) but not
            // registered with the game's ResourceManager, so RetrieveAsset()
            // cannot see it. Load the material — or build one from the bundled
            // shader — directly from the bundle.
            Material fromBundle = ModBootstrapper.LoadBundleAsset(OpaqueViewAssetName) as Material;
            if (fromBundle != null)
                return fromBundle;

            Shader shader = ModBootstrapper.LoadBundleAsset(OpaqueViewShaderName) as Shader;
            if (shader != null)
            {
                Material created = new Material(shader);
                created.name = OpaqueViewAssetName + " (runtime)";
                return created;
            }

            Debug.LogWarning(LogPrefix +
                " '" + OpaqueViewAssetName + "' not found in bundle - ocular views keep the stock UI shader (alpha darkening possible)");
            return null;
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        private static void AnchorCenter(RectTransform r, float w, float h)
        {
            r.anchorMin = new Vector2(0.5f, 0.5f);
            r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = Vector2.zero;
        }
    }
}
