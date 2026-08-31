using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure pixel-art generation for the chronometer scale cursors (CH-1,
    /// extracted from ChronometerOverlayHandler). The four corner arrows point
    /// outward (expand) or inward (reduce); rendering is deterministic and
    /// free of instance state, so RenderScaleCursorPixels is unit-testable
    /// without Unity.
    /// </summary>
    public static class ChronometerCursorArt
    {
        public const int CursorSize = 32;

        public static Texture2D CreateScaleCursor(bool inward)
        {
            Color32[] pixels = RenderScaleCursorPixels(inward);

            var texture = new Texture2D(CursorSize, CursorSize, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        /// <summary>Renders the 32x32 arrow glyph into a transparent buffer.</summary>
        public static Color32[] RenderScaleCursorPixels(bool inward)
        {
            var pixels = new Color32[CursorSize * CursorSize];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(0, 0, 0, 0);

            Color32 color = new Color32(255, 255, 255, 255);
            DrawScaleArrow(pixels, 15, 15, 5, 5, inward, color);
            DrawScaleArrow(pixels, 16, 15, 26, 5, inward, color);
            DrawScaleArrow(pixels, 15, 16, 5, 26, inward, color);
            DrawScaleArrow(pixels, 16, 16, 26, 26, inward, color);
            return pixels;
        }

        private static void DrawScaleArrow(
            Color32[] pixels,
            int centerX,
            int centerY,
            int cornerX,
            int cornerY,
            bool inward,
            Color32 color)
        {
            DrawLine(pixels, centerX, centerY, cornerX, cornerY, color);

            int tipX = inward ? centerX : cornerX;
            int tipY = inward ? centerY : cornerY;
            int horizontal = cornerX < centerX ? -5 : 5;
            int vertical = cornerY < centerY ? -5 : 5;
            if (!inward)
            {
                horizontal = -horizontal;
                vertical = -vertical;
            }

            DrawLine(pixels, tipX, tipY, tipX + horizontal, tipY, color);
            DrawLine(pixels, tipX, tipY, tipX, tipY + vertical, color);
        }

        private static void DrawLine(Color32[] pixels, int x0, int y0, int x1, int y1, Color32 color)
        {
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;

            while (true)
            {
                if (x0 >= 0 && x0 < CursorSize && y0 >= 0 && y0 < CursorSize)
                    pixels[y0 * CursorSize + x0] = color;
                if (x0 == x1 && y0 == y1)
                    break;

                int doubleError = 2 * error;
                if (doubleError >= dy)
                {
                    error += dy;
                    x0 += sx;
                }
                if (doubleError <= dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }
    }
}
