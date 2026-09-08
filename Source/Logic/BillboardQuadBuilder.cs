using System.Collections.Generic;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Shared camera-facing quad assembly for the procedural sky meshes
    /// (SRP/dedup: StarsMesh and PlanetRenderer carried verbatim copies).
    /// Appends 4 vertices, 6 indices, 4 colors and 4 UVs. Pure mesh math.
    /// </summary>
    public static class BillboardQuadBuilder
    {
        public static void AddQuad(List<Vector3> vertices, List<int> triangles,
            List<Color> colors, List<Vector2> uvs,
            Vector3 center, Vector3 forward, float size, Color color)
        {
            // Simple quad facing camera (not true billboard, but good enough for distant bodies)
            Vector3 right = Vector3.Cross(forward, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.right;
            Vector3 up = Vector3.Cross(right, forward).normalized;

            float halfSize = size * 0.5f;
            Vector3 v0 = center - right * halfSize - up * halfSize;
            Vector3 v1 = center + right * halfSize - up * halfSize;
            Vector3 v2 = center + right * halfSize + up * halfSize;
            Vector3 v3 = center - right * halfSize + up * halfSize;

            int baseIdx = vertices.Count;
            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);

            triangles.Add(baseIdx);
            triangles.Add(baseIdx + 1);
            triangles.Add(baseIdx + 2);
            triangles.Add(baseIdx);
            triangles.Add(baseIdx + 2);
            triangles.Add(baseIdx + 3);

            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);

            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(1, 0));
            uvs.Add(new Vector2(1, 1));
            uvs.Add(new Vector2(0, 1));
        }
    }
}
