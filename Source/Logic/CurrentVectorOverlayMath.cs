using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Zoom-adaptive grid geometry and visual scaling for debug ocean-current
    /// vectors on the map (pure C# 7.0, allocation-free, offline unit-tested).
    ///
    /// As map camera elevation increases (zooming out over larger areas),
    /// world grid spacing and radius grow while the node density per screen
    /// decreases so wide regions stay legible without cluttering the chart.
    /// </summary>
    internal struct CurrentVectorGridSpec
    {
        public float ElevationMeters;
        public float StepKm;
        public int HalfSteps;
        public float RadiusKm;
        public float ArrowThicknessScene;
        public float HeadSizeScene;
        public float MinArrowLengthScene;
        public float MaxArrowLengthScene;
    }

    internal static class CurrentVectorOverlayMath
    {
        internal const int MaxPoolSlots = 128;
        private const float KnotsPerMps = 1.9438445f;
        private const float MinVisibleKnots = 0.02f;
        private const float FullScaleKnots = 6.0f;

        private struct ZoomBand
        {
            public readonly float MaxElevationMeters;
            public readonly int HalfSteps;
            public readonly float StepKm;

            public ZoomBand(float maxElevationMeters, int halfSteps, float stepKm)
            {
                MaxElevationMeters = maxElevationMeters;
                HalfSteps = halfSteps;
                StepKm = stepKm;
            }
        }

        private static readonly ZoomBand[] Bands =
        {
            new ZoomBand(3500f, 6, 0.5f),
            new ZoomBand(8000f, 6, 1.0f),
            new ZoomBand(20000f, 6, 2.5f),
            new ZoomBand(45000f, 5, 6.0f),
            new ZoomBand(100000f, 5, 14.0f),
            new ZoomBand(220000f, 5, 30.0f),
            new ZoomBand(480000f, 4, 75.0f),
            new ZoomBand(900000f, 4, 150.0f),
            new ZoomBand(float.PositiveInfinity, 3, 320.0f)
        };

        internal static CurrentVectorGridSpec ComputeGridSpec(float elevationMeters)
        {
            float e = float.IsNaN(elevationMeters) || float.IsInfinity(elevationMeters)
                ? 20000f
                : Mathf.Clamp(elevationMeters, 1000f, 1500000f);

            ZoomBand chosen = Bands[Bands.Length - 1];
            for (int i = 0; i < Bands.Length; i++)
            {
                if (e < Bands[i].MaxElevationMeters)
                {
                    chosen = Bands[i];
                    break;
                }
            }

            float maxLenByCell = chosen.StepKm * 1000f * 0.74f;
            float maxLenByScreen = e * 0.20f;
            float maxArrow = Mathf.Min(maxLenByCell, maxLenByScreen);
            float headSize = Mathf.Clamp(e * 0.018f, maxArrow * 0.16f, maxArrow * 0.42f);

            return new CurrentVectorGridSpec
            {
                ElevationMeters = e,
                StepKm = chosen.StepKm,
                HalfSteps = chosen.HalfSteps,
                RadiusKm = chosen.HalfSteps * chosen.StepKm,
                ArrowThicknessScene = e * 0.0042f,
                HeadSizeScene = headSize,
                MinArrowLengthScene = Mathf.Max(headSize * 1.25f, maxArrow * 0.22f),
                MaxArrowLengthScene = maxArrow
            };
        }

        internal static int BuildSampleGrid(Vector2 centerKm, CurrentVectorGridSpec spec, Vector2[] buffer)
        {
            if (buffer == null || buffer.Length == 0 || spec.StepKm <= 0f || spec.HalfSteps < 0)
                return 0;

            float step = spec.StepKm;
            float snapX = (float)Math.Round(centerKm.x / step) * step;
            float snapY = (float)Math.Round(centerKm.y / step) * step;
            float maxRadius = spec.RadiusKm + step * 0.15f;
            float maxRadiusSq = maxRadius * maxRadius;

            int count = 0;
            int limit = spec.HalfSteps;
            for (int iy = -limit; iy <= limit; iy++)
            {
                float py = snapY + iy * step;
                float dy = py - centerKm.y;
                float dySq = dy * dy;
                for (int ix = -limit; ix <= limit; ix++)
                {
                    float px = snapX + ix * step;
                    float dx = px - centerKm.x;
                    if (dx * dx + dySq > maxRadiusSq)
                        continue;
                    if (count >= buffer.Length)
                        return count;
                    buffer[count++] = new Vector2(px, py);
                }
            }
            return count;
        }

        internal static float ComputeArrowLengthScene(float speedMps, CurrentVectorGridSpec spec)
        {
            if (float.IsNaN(speedMps) || float.IsInfinity(speedMps) || speedMps <= 0f)
                return 0f;
            float knots = speedMps * KnotsPerMps;
            if (knots < MinVisibleKnots)
                return 0f;
            float frac = Mathf.Clamp01(Mathf.Sqrt(knots / FullScaleKnots));
            return Mathf.Lerp(spec.MinArrowLengthScene, spec.MaxArrowLengthScene, frac);
        }

        internal static Color ComputeSpeedColor(float speedMps)
        {
            float knots = speedMps > 0f ? speedMps * KnotsPerMps : 0f;
            if (knots < 0.5f)
                return new Color(0.25f, 0.78f, 0.96f, 0.80f);
            if (knots < 2.0f)
                return new Color(0.22f, 0.92f, 0.56f, 0.86f);
            if (knots < 5.0f)
                return new Color(1.00f, 0.72f, 0.14f, 0.92f);
            return new Color(1.00f, 0.26f, 0.18f, 0.96f);
        }

        internal static float ComputeMapYawDegrees(float eastMps, float northMps)
        {
            return (float)(-Math.Atan2(northMps, eastMps) * (180.0 / Math.PI));
        }
    }
}
