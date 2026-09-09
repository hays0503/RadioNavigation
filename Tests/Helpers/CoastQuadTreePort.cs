using System;
using System.IO;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Offline port of the game's CoastQuadTree (UBOAT.Game.Sandbox,
    /// decompiled source C:\code\projects\uboat) for sign-convention tests
    /// against the real "Coast Quad Tree.dat" shipped in StreamingAssets.
    ///
    /// GetDistanceToCoast returns a SIGNED distance in km: the fallback
    /// branch maps isExterior (open water leaf) to positive and interior
    /// (land leaf) to negative, and the edge branch multiplies the
    /// distance by +0.001 / -0.001 depending on which side of the nearest
    /// coast edge the point lies. This port replicates the decision logic
    /// verbatim; only the deep-fallback magnitude source
    /// (WorldMaps.LandDistanceMap, a separate data file) is replaced by
    /// the sign-correct ±MaxDistance sentinel, so assertions must rely on
    /// the SIGN and on points that are unambiguously land or water.
    ///
    /// No Unity dependencies: Vector2 is replaced by a minimal struct
    /// with Unity's Perpendicular/Dot/epsilon-equality semantics.
    /// </summary>
    public sealed class CoastQuadTreePort
    {
        public const float MaxDistance = 4600f;
        private const float KwargsEpsilonSq = 9.99999944E-11f;

        private struct Vec2
        {
            public float X;
            public float Y;

            public Vec2(float x, float y)
            {
                X = x;
                Y = y;
            }

            public static Vec2 operator +(Vec2 a, Vec2 b)
            {
                return new Vec2(a.X + b.X, a.Y + b.Y);
            }

            public static Vec2 operator -(Vec2 a, Vec2 b)
            {
                return new Vec2(a.X - b.X, a.Y - b.Y);
            }

            public static Vec2 operator *(Vec2 a, float s)
            {
                return new Vec2(a.X * s, a.Y * s);
            }

            public float SqrMagnitude()
            {
                return X * X + Y * Y;
            }

            public static bool Approximately(Vec2 a, Vec2 b)
            {
                Vec2 d = a - b;
                return d.SqrMagnitude() < KwargsEpsilonSq;
            }
        }

        private struct TreeNode
        {
            public int ChildA;
            public int ChildB;
            public int ChildC;
            public int ChildD;
        }

        private struct CoastEdge
        {
            public Vec2 Start;
            public Vec2 End;
        }

        private CoastEdge[][] coastEdges;
        private TreeNode[] treeNodes;

        public bool IsLoading
        {
            get { return treeNodes == null; }
        }

        /// <summary>
        /// Reads the game's binary coast file (CoastQuadTree.LoadData):
        /// edge-group count, per-group edge lists (4 floats each), node
        /// count, node quads (4 ints each), all little-endian.
        /// </summary>
        public static CoastQuadTreePort Load(string path)
        {
            var port = new CoastQuadTreePort();
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(input))
            {
                int groupCount = reader.ReadInt32();
                port.coastEdges = new CoastEdge[groupCount][];
                for (int g = 0; g < groupCount; g++)
                {
                    int edgeCount = reader.ReadInt32();
                    CoastEdge[] edges = new CoastEdge[edgeCount];
                    for (int e = 0; e < edgeCount; e++)
                    {
                        edges[e].Start.X = reader.ReadSingle();
                        edges[e].Start.Y = reader.ReadSingle();
                        edges[e].End.X = reader.ReadSingle();
                        edges[e].End.Y = reader.ReadSingle();
                    }

                    port.coastEdges[g] = edges;
                }

                int nodeCount = reader.ReadInt32();
                TreeNode[] nodes = new TreeNode[nodeCount];
                for (int n = 0; n < nodeCount; n++)
                {
                    nodes[n].ChildA = reader.ReadInt32();
                    nodes[n].ChildB = reader.ReadInt32();
                    nodes[n].ChildC = reader.ReadInt32();
                    nodes[n].ChildD = reader.ReadInt32();
                }

                port.treeNodes = nodes;
            }

            return port;
        }

        /// <summary>
        /// Signed distance to the coast in km for a point given in
        /// encounter-space km (x = east, y = north) — the same units
        /// SandboxEntity passes in. Sign: negative = land, positive =
        /// water. Deep-fallback magnitudes use the ±MaxDistance sentinel
        /// (no LandDistanceMap in the port) — the sign stays exact.
        /// </summary>
        public float GetDistanceToCoastKm(float pointKmX, float pointKmY)
        {
            Vec2 point = new Vec2(pointKmX * 1000f, pointKmY * 1000f);
            bool isExterior;
            CoastEdge[] possible = FindPossibleEdges(
                0, default(Vec2), new Vec2(10018790f, 5009395f), point, out isExterior);
            if (possible == null)
            {
                // Port of the sampleFullRange branch without
                // WorldMaps.LandDistanceMap: the sign comes from isExterior.
                return isExterior ? MaxDistance : -MaxDistance;
            }

            double bestSq = double.PositiveInfinity;
            Vec2 bestStart = default(Vec2);
            Vec2 bestEnd = default(Vec2);
            Vec2 tieStart = default(Vec2);
            Vec2 tieEnd = default(Vec2);
            bool tie = false;
            for (int i = 0; i < possible.Length; i++)
            {
                double sq = SquareDistanceToSegmentDouble(possible[i].Start, possible[i].End, point);
                if (bestSq > sq)
                {
                    bestSq = sq;
                    bestStart = possible[i].Start;
                    bestEnd = possible[i].End;
                    tie = false;
                }
                else if (bestSq == sq)
                {
                    tieStart = possible[i].Start;
                    tieEnd = possible[i].End;
                    tie = true;
                }
            }

            double distanceMeters = Math.Sqrt(bestSq);
            if (!tie)
            {
                Vec2 rhs = Perpendicular(bestEnd - bestStart);
                bool outside = Dot(point - bestStart, rhs) >= 0f;
                return (float)(distanceMeters * (outside ? 0.001 : -0.001));
            }

            if (Vec2.Approximately(bestEnd, tieStart))
            {
                Vec2 rhs2 = Perpendicular(bestEnd - bestStart);
                bool flag3 = Dot(tieEnd - bestStart, rhs2) >= 0f;
                return (float)(distanceMeters * (flag3 ? -0.001 : 0.001));
            }

            if (Vec2.Approximately(tieEnd, bestStart))
            {
                Vec2 rhs3 = Perpendicular(tieEnd - tieStart);
                bool flag4 = Dot(bestEnd - tieStart, rhs3) >= 0f;
                return (float)(distanceMeters * (flag4 ? -0.001 : 0.001));
            }

            Vec2 rhs4 = Perpendicular(bestEnd - bestStart);
            bool flag5 = Dot(point - bestStart, rhs4) >= 0f;
            return (float)(distanceMeters * (flag5 ? 0.001 : -0.001));
        }

        /// <summary>
        /// Verbatim port of CoastQuadTree.FindPossibleEdges (decompiled
        /// lines 230-311), including its full-size child-center offsets.
        /// </summary>
        private CoastEdge[] FindPossibleEdges(
            int nodeIndex, Vec2 center, Vec2 size, Vec2 point, out bool isExterior)
        {
            if (treeNodes == null)
            {
                isExterior = true;
                return null;
            }

            TreeNode node = treeNodes[nodeIndex];
            if (point.X < center.X)
            {
                if (point.Y > center.Y)
                {
                    if (node.ChildA > 0)
                    {
                        return FindPossibleEdges(node.ChildA,
                            center + new Vec2(-size.X, size.Y), size * 0.5f, point, out isExterior);
                    }

                    if (node.ChildA < 0)
                    {
                        if (node.ChildA == int.MinValue)
                        {
                            isExterior = false;
                            return null;
                        }

                        isExterior = true;
                        return coastEdges[-node.ChildA - 1];
                    }

                    isExterior = true;
                    return null;
                }

                if (node.ChildC > 0)
                {
                    return FindPossibleEdges(node.ChildC,
                        center + new Vec2(-size.X, -size.Y), size * 0.5f, point, out isExterior);
                }

                if (node.ChildC < 0)
                {
                    if (node.ChildC == int.MinValue)
                    {
                        isExterior = false;
                        return null;
                    }

                    isExterior = true;
                    return coastEdges[-node.ChildC - 1];
                }

                isExterior = true;
                return null;
            }

            if (point.Y > center.Y)
            {
                if (node.ChildB > 0)
                {
                    return FindPossibleEdges(node.ChildB,
                        center + new Vec2(size.X, size.Y), size * 0.5f, point, out isExterior);
                }

                if (node.ChildB < 0)
                {
                    if (node.ChildB == int.MinValue)
                    {
                        isExterior = false;
                        return null;
                    }

                    isExterior = true;
                    return coastEdges[-node.ChildB - 1];
                }

                isExterior = true;
                return null;
            }

            if (node.ChildD > 0)
            {
                return FindPossibleEdges(node.ChildD,
                    center + new Vec2(size.X, -size.Y), size * 0.5f, point, out isExterior);
            }

            if (node.ChildD < 0)
            {
                if (node.ChildD == int.MinValue)
                {
                    isExterior = false;
                    return null;
                }

                isExterior = true;
                return coastEdges[-node.ChildD - 1];
            }

            isExterior = true;
            return null;
        }

        /// <summary>Unity Vector2.Perpendicular: (-y, x).</summary>
        private static Vec2 Perpendicular(Vec2 v)
        {
            return new Vec2(-v.Y, v.X);
        }

        private static float Dot(Vec2 a, Vec2 b)
        {
            return a.X * b.X + a.Y * b.Y;
        }

        /// <summary>Squared point-to-segment distance (double precision).</summary>
        private static double SquareDistanceToSegmentDouble(Vec2 a, Vec2 b, Vec2 p)
        {
            double abx = (double)b.X - a.X;
            double aby = (double)b.Y - a.Y;
            double apx = (double)p.X - a.X;
            double apy = (double)p.Y - a.Y;
            double abLenSq = abx * abx + aby * aby;
            double t = abLenSq > 0.0 ? (apx * abx + apy * aby) / abLenSq : 0.0;
            if (t < 0.0)
            {
                t = 0.0;
            }
            else if (t > 1.0)
            {
                t = 1.0;
            }

            double cx = apx - t * abx;
            double cy = apy - t * aby;
            return cx * cx + cy * cy;
        }
    }
}
