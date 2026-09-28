using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Camera basis and projection for the lens field. Unity-free.</summary>
    public struct LensCamera
    {
        public float Px, Py, Pz;   // camera position
        public float Rx, Ry, Rz;   // camera right (screen +x)
        public float Ux, Uy, Uz;   // camera up (screen +y)
        public float Fx, Fy, Fz;   // camera forward
        public float TanHalfFovY;
    }

    /// <summary>
    /// Streak optics. LineLengthDeg is a caller-supplied parameter: no source
    /// publishes a measured line length for the Sternspreizglas, so this is a
    /// mathematical baseline (docs/sternspreiz-phase1-psf.md).
    /// </summary>
    public struct LensOptics
    {
        public float LineLengthDeg;
        public float ThicknessRatio;  // across-line thickness as a fraction of the length
        public float DistanceM;       // draw distance (same shell the star mesh uses)
    }

    /// <summary>One visible star ready for mesh emission. Unity-free.</summary>
    public struct LensStar
    {
        public float DirX, DirY, DirZ;
        public float R, G, B, Alpha;
    }

    /// <summary>
    /// Sternspreizglas streak field: catalog → visible stars → batched quads.
    ///
    /// Physics pinned here (Celestaire / instrument description):
    /// - the streak is horizontal *in the observer's frame*, not in world
    ///   space, so quads are built from the camera's right/up vectors;
    /// - the line is centred on the star, so the sight reading is unbiased;
    /// - the length is angular (zoom independent), converted to world units at
    ///   the star's shell distance.
    ///
    /// No per-frame mesh rebuild of the 5046-star catalog: the caller collects
    /// a capped visible subset and emits a few dozen quads, throttled by
    /// MeshRebuildPolicy on the runtime side.
    /// </summary>
    public static class SternspreizLensField
    {
        /// <summary>Same cut the point-star mesh uses (StarsMesh).</summary>
        public const float MinVisibilityFactor = 0.002f;

        public const int VerticesPerStreak = 4;
        public const int IndicesPerStreak = 6;

        public static int PositionFloatCount(int maxStars)
        {
            return maxStars * VerticesPerStreak * 3;
        }

        public static int ColorFloatCount(int maxStars)
        {
            return maxStars * VerticesPerStreak * 4;
        }

        public static int UvFloatCount(int maxStars)
        {
            return maxStars * VerticesPerStreak * 2;
        }

        public static int IndexCount(int maxStars)
        {
            return maxStars * IndicesPerStreak;
        }

        /// <summary>
        /// Collects the visible stars for the given observer/time/sky, capped at
        /// maxStars (the catalog is sorted by brightness, so the cap keeps the
        /// brightest). Returns the number written to <paramref name="buffer"/>.
        /// The visibility gates mirror StarsMesh.RebuildMesh.
        /// </summary>
        public static int CollectVisibleStars(float latDeg, double lstDeg, double jd,
            float limitingMag, float fogginess, double adaptation, int maxStars,
            LensStar[] buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");
            if (maxStars < 0)
                throw new ArgumentOutOfRangeException("maxStars");
            if (maxStars > buffer.Length)
                throw new ArgumentException("Buffer is smaller than maxStars.", "buffer");

            StarEntry[] catalog = StarCatalog.Stars;
            int count = 0;
            for (int i = 0; i < catalog.Length && count < maxStars; i++)
            {
                StarEntry star = catalog[i];
                if (star.Hip == 0)
                    continue; // no-HIP sentinel: never sightable

                if (!TryProject(star, latDeg, lstDeg, jd, limitingMag, fogginess,
                        adaptation, out LensStar lensStar))
                    continue;

                buffer[count] = lensStar;
                count++;
            }
            return count;
        }

        private static bool TryProject(StarEntry star, float latDeg, double lstDeg, double jd,
            float limitingMag, float fogginess, double adaptation, out LensStar result)
        {
            result = default(LensStar);

            CelestialEphemeris.PrecessCatalog(star.RaDeg, star.DecDeg, jd,
                out double raRad, out double decRad);
            AstroMath.RaDecToEnu(raRad, decRad, lstDeg, latDeg,
                out double altRad, out _, out double east, out double north, out double up);

            double altDeg = altRad * 180d / Math.PI;
            if (altDeg <= AtmosphericExtinction.CullAltitudeDeg)
                return false;

            double hazeLoss = SkyBrightness.HazeLoss(altDeg, fogginess);
            float skyFactor = SkyBrightness.VisibilityFactor(limitingMag, star.Vmag, hazeLoss);
            if (skyFactor <= MinVisibilityFactor)
                return false;

            double vEff = star.Vmag + AtmosphericExtinction.MagnitudeLoss(altDeg);
            double bvEff = star.Bv + AtmosphericExtinction.BvShift(altDeg);
            float horizonFade = AtmosphericExtinction.HorizonFade(altDeg);

            StarBrightness.BrightnessFor((float)vEff, star.IsNav, out _, out float alpha);
            alpha *= skyFactor * horizonFade * (float)adaptation;
            if (alpha <= 0f)
                return false;

            double length = Math.Sqrt(east * east + north * north + up * up);
            if (length <= 0d)
                return false;

            StarColor.FromBv((float)bvEff, out float r, out float g, out float b);
            result.DirX = (float)(east / length);
            result.DirY = (float)(up / length);
            result.DirZ = (float)(north / length);
            result.R = r;
            result.G = g;
            result.B = b;
            result.Alpha = alpha;
            return true;
        }

        /// <summary>
        /// Emits one quad per star into caller-owned arrays. Vertices are
        /// ordered bottom-left, bottom-right, top-right, top-left in the
        /// camera's (right, up) frame, which is front-facing under Unity's
        /// clockwise winding rule; indices are 0-3-2 and 0-2-1. Returns the
        /// number of streaks emitted.
        /// </summary>
        public static int BuildStreakMesh(LensStar[] stars, int starCount, LensCamera camera,
            LensOptics optics, float[] positions, float[] colors, float[] uvs, int[] indices)
        {
            if (stars == null)
                throw new ArgumentNullException("stars");
            if (positions == null)
                throw new ArgumentNullException("positions");
            if (colors == null)
                throw new ArgumentNullException("colors");
            if (uvs == null)
                throw new ArgumentNullException("uvs");
            if (indices == null)
                throw new ArgumentNullException("indices");
            if (starCount < 0 || starCount > stars.Length)
                throw new ArgumentOutOfRangeException("starCount");

            float fullWidth = SternspreizLineProfile.AngularWidthMeters(
                optics.LineLengthDeg, optics.DistanceM);
            if (fullWidth <= 0f)
                return 0;

            float halfLength = fullWidth * 0.5f;
            float halfThickness = halfLength * optics.ThicknessRatio;
            int maxEmittable = Math.Min(positions.Length / (VerticesPerStreak * 3),
                indices.Length / IndicesPerStreak);

            int emitted = 0;
            for (int s = 0; s < starCount && emitted < maxEmittable; s++)
            {
                LensStar star = stars[s];
                float facing = star.DirX * camera.Fx + star.DirY * camera.Fy + star.DirZ * camera.Fz;
                if (facing <= 0f)
                    continue; // behind the camera: the projection would invert

                float cx = camera.Px + star.DirX * optics.DistanceM;
                float cy = camera.Py + star.DirY * optics.DistanceM;
                float cz = camera.Pz + star.DirZ * optics.DistanceM;

                EmitStreak(star, camera, cx, cy, cz, halfLength, halfThickness,
                    emitted, positions, colors, uvs, indices);
                emitted++;
            }
            return emitted;
        }

        private static void EmitStreak(LensStar star, LensCamera camera,
            float cx, float cy, float cz, float halfLength, float halfThickness,
            int slot, float[] positions, float[] colors, float[] uvs, int[] indices)
        {
            int v = slot * VerticesPerStreak;
            // Screen-space corner signs: (x, y) with x along the line.
            EmitVertex(camera, cx, cy, cz, -halfLength, -halfThickness, v + 0, star, positions, colors, uvs);
            EmitVertex(camera, cx, cy, cz, halfLength, -halfThickness, v + 1, star, positions, colors, uvs);
            EmitVertex(camera, cx, cy, cz, halfLength, halfThickness, v + 2, star, positions, colors, uvs);
            EmitVertex(camera, cx, cy, cz, -halfLength, halfThickness, v + 3, star, positions, colors, uvs);

            int i = slot * IndicesPerStreak;
            indices[i + 0] = v + 0;
            indices[i + 1] = v + 3;
            indices[i + 2] = v + 2;
            indices[i + 3] = v + 0;
            indices[i + 4] = v + 2;
            indices[i + 5] = v + 1;
        }

        private static void EmitVertex(LensCamera camera,
            float cx, float cy, float cz, float alongLength, float alongThickness,
            int vertex, LensStar star, float[] positions, float[] colors, float[] uvs)
        {
            int p = vertex * 3;
            positions[p + 0] = cx + camera.Rx * alongLength + camera.Ux * alongThickness;
            positions[p + 1] = cy + camera.Ry * alongLength + camera.Uy * alongThickness;
            positions[p + 2] = cz + camera.Rz * alongLength + camera.Uz * alongThickness;

            int c = vertex * 4;
            colors[c + 0] = star.R;
            colors[c + 1] = star.G;
            colors[c + 2] = star.B;
            colors[c + 3] = star.Alpha;

            int uv = vertex * 2;
            bool isTop = alongThickness > 0f;
            bool isRight = alongLength > 0f;
            uvs[uv + 0] = isRight ? 1f : 0f;
            uvs[uv + 1] = isTop ? 1f : 0f;
        }
    }
}
