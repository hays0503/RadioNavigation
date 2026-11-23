namespace UBOAT.Mods.RadioNavigation
{
    internal struct WindLabelPlacement
    {
        public bool Visible;
        public float AnchorX;
        public bool AlignRight;
    }

    /// <summary>
    /// Horizontal placement for the map wind bearing label inside the active
    /// row container. Prefers space to the right of the rendered speed glyphs;
    /// falls back to the gap between the icon and glyphs; hides when neither
    /// fits without overlap or clipping.
    /// </summary>
    internal static class WindLabelLayout
    {
        internal const float DefaultGap = 6f;

        internal static WindLabelPlacement TryPlace(
            float glyphMinX, float glyphMaxX,
            float iconMinX, float iconMaxX,
            float visibleMinX, float visibleMaxX,
            float labelWidth, float gap)
        {
            if (!IsValidInput(glyphMinX, glyphMaxX, iconMinX, iconMaxX, visibleMinX, visibleMaxX, labelWidth, gap))
                return default(WindLabelPlacement);

            float rightStart = glyphMaxX + gap;
            float rightEnd = rightStart + labelWidth;
            if (rightStart >= visibleMinX && rightEnd <= visibleMaxX)
            {
                return new WindLabelPlacement
                {
                    Visible = true,
                    AnchorX = rightStart,
                    AlignRight = false
                };
            }

            float leftEnd = glyphMinX - gap;
            float leftStart = leftEnd - labelWidth;
            float minLeft = iconMaxX + gap;
            if (minLeft < visibleMinX)
                minLeft = visibleMinX;
            if (leftStart >= minLeft && leftEnd <= visibleMaxX)
            {
                return new WindLabelPlacement
                {
                    Visible = true,
                    AnchorX = leftEnd,
                    AlignRight = true
                };
            }

            return default(WindLabelPlacement);
        }

        private static bool IsValidInput(
            float glyphMinX, float glyphMaxX,
            float iconMinX, float iconMaxX,
            float visibleMinX, float visibleMaxX,
            float labelWidth, float gap)
        {
            return ValidRange(glyphMinX, glyphMaxX, false)
                && ValidRange(iconMinX, iconMaxX, false)
                && ValidRange(visibleMinX, visibleMaxX, true)
                && Finite(labelWidth) && labelWidth > 0f
                && Finite(gap) && gap >= 0f;
        }

        private static bool ValidRange(float min, float max, bool strict)
        {
            if (!Finite(min) || !Finite(max))
                return false;
            return strict ? max > min : max >= min;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
