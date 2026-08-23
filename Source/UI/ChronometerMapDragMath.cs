using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure math for the expanded-chronometer map drag: mirrors the game's
    /// position-override normalization (InverseLerp over the 20px screen
    /// margins) so the mod writes exactly what native HandleDialDragging
    /// would have written.
    /// </summary>
    public static class ChronometerMapDragMath
    {
        public static Vector2 ToNormalizedOverride(Vector2 screenPos, float screenWidth, float screenHeight)
        {
            return new Vector2(
                Mathf.InverseLerp(20f, screenWidth - 20f, screenPos.x),
                Mathf.InverseLerp(20f, screenHeight - 20f, screenPos.y));
        }
    }
}
