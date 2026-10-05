using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Pins the Sternspreiz streak field: visibility gates, camera-frame
    /// orientation (the line must be horizontal in the OBSERVER's frame, not in
    /// world space), angular sizing, front-facing winding and energy-carried
    /// colour. The line length stays a caller-supplied baseline parameter.
    /// </summary>
    public sealed class SternspreizLensFieldTests
    {
        private static LensCamera IdentityCamera(float distanceUnused)
        {
            return new LensCamera
            {
                Px = 0f, Py = 0f, Pz = 0f,
                Rx = 1f, Ry = 0f, Rz = 0f,
                Ux = 0f, Uy = 1f, Uz = 0f,
                Fx = 0f, Fy = 0f, Fz = 1f,
                TanHalfFovX = 0.6f,
                TanHalfFovY = 0.6f
            };
        }

        private static LensCamera WideSkyCamera()
        {
            return new LensCamera
            {
                Rx = 1f, Ry = 0f, Rz = 0f,
                Ux = 0f, Uy = 0f, Uz = -1f,
                Fx = 0f, Fy = 1f, Fz = 0f,
                TanHalfFovX = 1000000f,
                TanHalfFovY = 1000000f
            };
        }

        private static LensCamera CameraLookingAt(LensStar target, float tanHalfFov)
        {
            float forwardLength = (float)Math.Sqrt(target.DirX * target.DirX
                + target.DirY * target.DirY + target.DirZ * target.DirZ);
            float fx = target.DirX / forwardLength;
            float fy = target.DirY / forwardLength;
            float fz = target.DirZ / forwardLength;
            const float upRefX = 0f;
            float upRefY = Math.Abs(fy) > 0.99f ? 0f : 1f;
            float upRefZ = Math.Abs(fy) > 0.99f ? 1f : 0f;

            float rx = upRefY * fz - upRefZ * fy;
            float ry = upRefZ * fx - upRefX * fz;
            float rz = upRefX * fy - upRefY * fx;
            float rightLength = (float)Math.Sqrt(rx * rx + ry * ry + rz * rz);
            rx /= rightLength;
            ry /= rightLength;
            rz /= rightLength;

            return new LensCamera
            {
                Rx = rx, Ry = ry, Rz = rz,
                Ux = fy * rz - fz * ry,
                Uy = fz * rx - fx * rz,
                Uz = fx * ry - fy * rx,
                Fx = fx, Fy = fy, Fz = fz,
                TanHalfFovX = tanHalfFov,
                TanHalfFovY = tanHalfFov
            };
        }

        private static LensOptics Optics(float lineLengthDeg, float thicknessDeg, float distanceM)
        {
            return new LensOptics
            {
                LineLengthDeg = lineLengthDeg,
                ThicknessDeg = thicknessDeg,
                DistanceM = distanceM
            };
        }

        private static LensStar Star(float x, float y, float z)
        {
            return new LensStar
            {
                DirX = x, DirY = y, DirZ = z,
                R = 1f, G = 0.5f, B = 0.25f, Alpha = 0.75f
            };
        }

        [Fact]
        public void CollectVisibleStars_FillsBufferWithGatedStars()
        {
            // Equinox, 48 N, midnight UTC: a real sky must yield some stars.
            var buffer = new LensStar[32];
            int count = SternspreizLensField.CollectVisibleStars(48f, 180d, 2451545d,
                6.5f, 0f, 1d, WideSkyCamera(), 1.5f, 0.189f, 0f, buffer.Length, buffer);

            Assert.True(count > 0);
            for (int i = 0; i < count; i++)
            {
                double length = Math.Sqrt(buffer[i].DirX * buffer[i].DirX
                    + buffer[i].DirY * buffer[i].DirY + buffer[i].DirZ * buffer[i].DirZ);
                Assert.Equal(1d, length, 4);
                Assert.True(buffer[i].Alpha > 0f);
                Assert.InRange(buffer[i].R, 0f, 1f);
            }
        }

        [Fact]
        public void CollectVisibleStars_RespectsTheCap()
        {
            var buffer = new LensStar[4];
            int count = SternspreizLensField.CollectVisibleStars(48f, 180d, 2451545d,
                6.5f, 0f, 1d, WideSkyCamera(), 1.5f, 0.189f, 0f, 4, buffer);
            Assert.True(count <= 4);
        }

        [Fact]
        public void CollectVisibleStars_AppliesTheCapAfterFrustumFiltering()
        {
            const float latDeg = 48f;
            const double lstDeg = 180d;
            const double jd = 2451545d;
            const float limitingMag = 6.5f;
            var allVisible = new LensStar[StarCatalog.Stars.Length];
            int allCount = SternspreizLensField.CollectVisibleStars(latDeg, lstDeg, jd,
                limitingMag, 0f, 1d, WideSkyCamera(), 0.02f, 0.01f,
                0f, allVisible.Length, allVisible);

            Assert.True(allCount > 64, "The deterministic sky must contain candidates beyond the cap.");

            // Choose a catalog candidate below the old global top-48 cutoff and
            // away from every other visible star. A narrow camera around it must
            // still select that star before the one-slot cap is applied.
            LensStar target = default(LensStar);
            bool foundIsolatedTarget = false;
            double isolationCos = Math.Cos(0.05d * Math.PI / 180d);
            for (int i = 48; i < allCount && !foundIsolatedTarget; i++)
            {
                bool isolated = true;
                for (int j = 0; j < allCount; j++)
                {
                    if (i == j)
                        continue;
                    double dot = allVisible[i].DirX * allVisible[j].DirX
                        + allVisible[i].DirY * allVisible[j].DirY
                        + allVisible[i].DirZ * allVisible[j].DirZ;
                    if (dot > isolationCos)
                    {
                        isolated = false;
                        break;
                    }
                }

                if (isolated)
                {
                    target = allVisible[i];
                    foundIsolatedTarget = true;
                }
            }
            Assert.True(foundIsolatedTarget, "Expected an isolated candidate beyond catalog slot 48.");

            var selected = new LensStar[1];
            int selectedCount = SternspreizLensField.CollectVisibleStars(latDeg, lstDeg, jd,
                limitingMag, 0f, 1d, CameraLookingAt(target, 0.00001f),
                0.02f, 0.01f, 0f, 1, selected);

            Assert.Equal(1, selectedCount);
            Assert.Equal(target.DirX, selected[0].DirX, 5);
            Assert.Equal(target.DirY, selected[0].DirY, 5);
            Assert.Equal(target.DirZ, selected[0].DirZ, 5);
        }

        [Fact]
        public void StreakIntersectsFrustum_IncludesQuadOverhangButRejectsDistantStars()
        {
            LensCamera camera = IdentityCamera(0f);
            camera.TanHalfFovX = 0.5f;
            camera.TanHalfFovY = 0.4f;

            Assert.True(SternspreizLensField.StreakIntersectsFrustum(
                Star(0.505f, 0f, 1f), camera, 1f, 0.2f, 0f));
            Assert.False(SternspreizLensField.StreakIntersectsFrustum(
                Star(0.52f, 0f, 1f), camera, 1f, 0.2f, 0f));
            Assert.False(SternspreizLensField.StreakIntersectsFrustum(
                Star(0f, 0f, -1f), camera, 1f, 0.2f, 0f));
        }

        [Fact]
        public void CollectVisibleStars_RejectsBadBuffer()
        {
            Assert.Throws<ArgumentNullException>(() =>
                SternspreizLensField.CollectVisibleStars(48f, 0d, 2451545d, 6f, 0f, 1d,
                    WideSkyCamera(), 1f, 0.2f, 0f, 1, null));
            Assert.Throws<ArgumentException>(() =>
                SternspreizLensField.CollectVisibleStars(48f, 0d, 2451545d, 6f, 0f, 1d,
                    WideSkyCamera(), 1f, 0.2f, 0f, 8, new LensStar[2]));
        }

        [Fact]
        public void BuildStreakMesh_EmitsOneCentredScreenHorizontalQuad()
        {
            var stars = new[] { Star(0f, 0f, 1f) };
            var optics = Optics(1f, 0.25f, 1000f);
            var positions = new float[SternspreizLensField.PositionFloatCount(1)];
            var colors = new float[SternspreizLensField.ColorFloatCount(1)];
            var uvs = new float[SternspreizLensField.UvFloatCount(1)];
            var indices = new int[SternspreizLensField.IndexCount(1)];

            int emitted = SternspreizLensField.BuildStreakMesh(stars, 1, IdentityCamera(0f),
                optics, positions, colors, uvs, indices);

            Assert.Equal(1, emitted);
            float expectedWidth = SternspreizLineProfile.AngularWidthMeters(1f, 1000f);
            float expectedThickness = expectedWidth * 0.25f;

            // All vertices sit on the star's shell plane (z = distance).
            for (int v = 0; v < 4; v++)
                Assert.Equal(1000f, positions[v * 3 + 2], 3);

            // Horizontal extent = the streak length (observer frame).
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int v = 0; v < 4; v++)
            {
                minX = Math.Min(minX, positions[v * 3 + 0]);
                maxX = Math.Max(maxX, positions[v * 3 + 0]);
                minY = Math.Min(minY, positions[v * 3 + 1]);
                maxY = Math.Max(maxY, positions[v * 3 + 1]);
            }
            Assert.Equal(expectedWidth, maxX - minX, 3);
            Assert.Equal(expectedThickness, maxY - minY, 3);
            Assert.True(maxX > 0f && minX < 0f, "The line must be centred on the star.");
            Assert.Equal(0f, (maxX + minX) * 0.5f, 3);

            // UVs: x along the line, y across it.
            Assert.Equal(0f, uvs[0], 6);
            Assert.Equal(0f, uvs[1], 6);
            Assert.Equal(1f, uvs[2], 6);
            Assert.Equal(0f, uvs[3], 6);
            Assert.Equal(1f, uvs[4], 6);
            Assert.Equal(1f, uvs[5], 6);
            Assert.Equal(0f, uvs[6], 6);
            Assert.Equal(1f, uvs[7], 6);

            // Colour carries the star's photometry on every vertex.
            for (int v = 0; v < 4; v++)
            {
                Assert.Equal(1f, colors[v * 4 + 0], 6);
                Assert.Equal(0.5f, colors[v * 4 + 1], 6);
                Assert.Equal(0.25f, colors[v * 4 + 2], 6);
                Assert.Equal(0.75f, colors[v * 4 + 3], 6);
            }
        }

        [Fact]
        public void BuildStreakMesh_IndicesFaceTheCameraUnderUnitysClockwiseRule()
        {
            var stars = new[] { Star(0f, 0f, 1f) };
            var positions = new float[SternspreizLensField.PositionFloatCount(1)];
            var colors = new float[SternspreizLensField.ColorFloatCount(1)];
            var uvs = new float[SternspreizLensField.UvFloatCount(1)];
            var indices = new int[SternspreizLensField.IndexCount(1)];
            SternspreizLensField.BuildStreakMesh(stars, 1, IdentityCamera(0f),
                Optics(1f, 0.5f, 500f), positions, colors, uvs, indices);

            Assert.Equal(new[] { 0, 3, 2, 0, 2, 1 }, indices);

            // Signed area in the camera's (right, up) frame must be negative
            // (clockwise) so the quad is front-facing.
            float SignedArea(int a, int b, int c)
            {
                float ax = positions[a * 3], ay = positions[a * 3 + 1];
                float bx = positions[b * 3], by = positions[b * 3 + 1];
                float cx2 = positions[c * 3], cy2 = positions[c * 3 + 1];
                return 0.5f * ((bx - ax) * (cy2 - ay) - (cx2 - ax) * (by - ay));
            }

            Assert.True(SignedArea(0, 3, 2) < 0f);
            Assert.True(SignedArea(0, 2, 1) < 0f);
        }

        [Fact]
        public void BuildStreakMesh_FollowsTheCameraRollNotTheWorldAxis()
        {
            // Camera rolled 90 degrees: right = +Y, up = -X. A world-horizontal
            // streak would be wrong here; the line must follow the observer.
            var rolled = new LensCamera
            {
                Px = 0f, Py = 0f, Pz = 0f,
                Rx = 0f, Ry = 1f, Rz = 0f,
                Ux = -1f, Uy = 0f, Uz = 0f,
                Fx = 0f, Fy = 0f, Fz = 1f,
                TanHalfFovY = 0.6f
            };
            var stars = new[] { Star(0f, 0f, 1f) };
            var positions = new float[SternspreizLensField.PositionFloatCount(1)];
            var colors = new float[SternspreizLensField.ColorFloatCount(1)];
            var uvs = new float[SternspreizLensField.UvFloatCount(1)];
            var indices = new int[SternspreizLensField.IndexCount(1)];

            SternspreizLensField.BuildStreakMesh(stars, 1, rolled,
                Optics(1f, 0.2f, 1000f), positions, colors, uvs, indices);

            float expectedWidth = SternspreizLineProfile.AngularWidthMeters(1f, 1000f);
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int v = 0; v < 4; v++)
            {
                minX = Math.Min(minX, positions[v * 3 + 0]);
                maxX = Math.Max(maxX, positions[v * 3 + 0]);
                minY = Math.Min(minY, positions[v * 3 + 1]);
                maxY = Math.Max(maxY, positions[v * 3 + 1]);
            }
            Assert.Equal(expectedWidth, maxY - minY, 3);          // long axis follows camera right (+Y)
            Assert.Equal(expectedWidth * 0.2f, maxX - minX, 3);
        }

        [Fact]
        public void BuildStreakMesh_SkipsStarsBehindTheCamera()
        {
            var stars = new[] { Star(0f, 0f, -1f), Star(0f, 0f, 1f) };
            var positions = new float[SternspreizLensField.PositionFloatCount(2)];
            var colors = new float[SternspreizLensField.ColorFloatCount(2)];
            var uvs = new float[SternspreizLensField.UvFloatCount(2)];
            var indices = new int[SternspreizLensField.IndexCount(2)];

            int emitted = SternspreizLensField.BuildStreakMesh(stars, 2, IdentityCamera(0f),
                Optics(1f, 0.2f, 1000f), positions, colors, uvs, indices);

            Assert.Equal(1, emitted);
            Assert.Equal(1000f, positions[2], 3);
        }

        [Fact]
        public void BuildStreakMesh_AngularLengthKeepsScreenSizeAcrossDistance()
        {
            var stars = new[] { Star(0f, 0f, 1f) };
            var positions = new float[SternspreizLensField.PositionFloatCount(1)];
            var colors = new float[SternspreizLensField.ColorFloatCount(1)];
            var uvs = new float[SternspreizLensField.UvFloatCount(1)];
            var indices = new int[SternspreizLensField.IndexCount(1)];

            SternspreizLensField.BuildStreakMesh(stars, 1, IdentityCamera(0f),
                Optics(0.5f, 0.1f, 1000f), positions, colors, uvs, indices);
            float near = positions[3] - positions[0];

            SternspreizLensField.BuildStreakMesh(stars, 1, IdentityCamera(0f),
                Optics(0.5f, 0.1f, 2000f), positions, colors, uvs, indices);
            float far = positions[3] - positions[0];

            Assert.Equal(2f, far / near, 3);
        }

        [Fact]
        public void BuildStreakMesh_ThicknessIsIndependentFromUserLength()
        {
            // Length is a user-facing angular control; thickness models the
            // cylindrical optic and must not grow when the user makes the dash
            // longer. The previous ratio-based design accidentally made a
            // 1.5-degree line 2.14x thicker than the 0.7-degree baseline.
            const float shortLengthDeg = 0.7f;
            const float longLengthDeg = 1.5f;
            const float thicknessDeg = 0.189f;

            var stars = new[] { Star(0f, 0f, 1f) };
            var positions = new float[SternspreizLensField.PositionFloatCount(1)];
            var colors = new float[SternspreizLensField.ColorFloatCount(1)];
            var uvs = new float[SternspreizLensField.UvFloatCount(1)];
            var indices = new int[SternspreizLensField.IndexCount(1)];

            SternspreizLensField.BuildStreakMesh(stars, 1, IdentityCamera(0f),
                Optics(shortLengthDeg, thicknessDeg, 1000f), positions, colors, uvs, indices);
            float shortWorldLength = positions[3] - positions[0];     // bottom-right - bottom-left
            float shortWorldThickness = positions[10] - positions[1]; // top-left - bottom-left

            SternspreizLensField.BuildStreakMesh(stars, 1, IdentityCamera(0f),
                Optics(longLengthDeg, thicknessDeg, 1000f), positions, colors, uvs, indices);
            float longWorldLength = positions[3] - positions[0];
            float longWorldThickness = positions[10] - positions[1];

            Assert.Equal(longLengthDeg / shortLengthDeg, longWorldLength / shortWorldLength, 3);
            Assert.Equal(shortWorldThickness, longWorldThickness, 5);
        }

        [Fact]
        public void BuildStreakMesh_ZeroLengthEmitsNothing()
        {
            var stars = new[] { Star(0f, 0f, 1f) };
            var positions = new float[SternspreizLensField.PositionFloatCount(1)];
            var colors = new float[SternspreizLensField.ColorFloatCount(1)];
            var uvs = new float[SternspreizLensField.UvFloatCount(1)];
            var indices = new int[SternspreizLensField.IndexCount(1)];

            int emitted = SternspreizLensField.BuildStreakMesh(stars, 1, IdentityCamera(0f),
                Optics(0f, 0.2f, 1000f), positions, colors, uvs, indices);
            Assert.Equal(0, emitted);
        }

        [Fact]
        public void BuildStreakMesh_RejectsBadInput()
        {
            Assert.Throws<ArgumentNullException>(() => SternspreizLensField.BuildStreakMesh(
                null, 0, IdentityCamera(0f), Optics(1f, 0.2f, 100f),
                new float[12], new float[16], new float[8], new int[6]));
            Assert.Throws<ArgumentOutOfRangeException>(() => SternspreizLensField.BuildStreakMesh(
                new[] { Star(0f, 0f, 1f) }, 5, IdentityCamera(0f), Optics(1f, 0.2f, 100f),
                new float[12], new float[16], new float[8], new int[6]));
        }

        [Fact]
        public void BufferSizeHelpers_MatchTheEmittedLayout()
        {
            Assert.Equal(12, SternspreizLensField.PositionFloatCount(1));
            Assert.Equal(16, SternspreizLensField.ColorFloatCount(1));
            Assert.Equal(8, SternspreizLensField.UvFloatCount(1));
            Assert.Equal(6, SternspreizLensField.IndexCount(1));
            Assert.Equal(120, SternspreizLensField.PositionFloatCount(10));
        }
    }
}
