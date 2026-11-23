using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// A depth-dependent source of water velocity in the game's local
    /// east/north frame. Depth is measured downward from the surface.
    /// Spatial and time coordinates are supplied for an atlas; the constant
    /// and profile sources ignore them.
    /// </summary>
    internal struct CurrentQuery
    {
        public double Latitude;
        public double Longitude;
        public float DepthMeters;
        public DateTime Utc;
        public bool HasPosition;
    }

    internal interface ICurrentField
    {
        bool HasFlow { get; }
        bool TrySample(CurrentQuery query, out Vector3 velocity);
    }

    /// <summary>
    /// Sparse depth profile, linearly interpolated and clamped at the ends.
    /// Experimental transport for validating depth handling before a real
    /// atlas exists; it is not a climatology and not a surface-to-depth
    /// extrapolation rule.
    /// </summary>
    internal sealed class CurrentProfileField : ICurrentField
    {
        private const int MaxLayers = 32;
        private readonly float[] depths;
        private readonly Vector3[] velocities;
        private readonly string text;

        private CurrentProfileField(float[] depths, Vector3[] velocities, string text)
        {
            this.depths = depths;
            this.velocities = velocities;
            this.text = text;
            bool flow = false;
            foreach (var velocity in velocities)
                flow |= velocity.x != 0f || velocity.z != 0f;
            HasFlow = flow;
        }

        public bool HasFlow { get; }

        public static bool TryParse(string raw, out CurrentProfileField field, out string error)
        {
            field = null;
            error = null;
            if (string.IsNullOrEmpty(raw))
            {
                error = "empty profile";
                return false;
            }
            string[] layers = raw.Split(';');
            if (layers.Length > MaxLayers)
            {
                error = "expected 1.." + MaxLayers + " layers";
                return false;
            }
            var depths = new float[layers.Length];
            var velocities = new Vector3[layers.Length];
            var text = new StringBuilder();
            float previous = -1f;
            for (int i = 0; i < layers.Length; i++)
            {
                string layer = layers[i].Trim();
                string[] parts = layer.Split(':');
                if (parts.Length != 2 || !TryFloat(parts[0], out float depth) || !(depth >= 0f)
                    || depth <= previous)
                {
                    error = "layer " + (i + 1) + ": expected increasing depth: east,north";
                    return false;
                }
                string[] components = parts[1].Split(',');
                if (components.Length != 2 || !TryFloat(components[0], out float east)
                    || !TryFloat(components[1], out float north))
                {
                    error = "layer " + (i + 1) + ": expected increasing depth: east,north";
                    return false;
                }
                depths[i] = depth;
                velocities[i] = new Vector3(east, 0f, north);
                previous = depth;
                if (i > 0)
                    text.Append(';');
                text.Append(depth.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(east.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(north.ToString("R", CultureInfo.InvariantCulture));
            }
            field = new CurrentProfileField(depths, velocities, text.ToString());
            return true;
        }

        public bool TrySample(CurrentQuery query, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            float depthMeters = query.DepthMeters;
            if (!(depthMeters >= 0f) || float.IsInfinity(depthMeters))
                return false;
            if (depthMeters <= depths[0])
            {
                velocity = velocities[0];
                return true;
            }
            int last = depths.Length - 1;
            if (depthMeters >= depths[last])
            {
                velocity = velocities[last];
                return true;
            }
            for (int i = 1; i <= last; i++)
            {
                if (depthMeters > depths[i])
                    continue;
                float span = depths[i] - depths[i - 1];
                float weight = (depthMeters - depths[i - 1]) / span;
                Vector3 low = velocities[i - 1];
                Vector3 high = velocities[i];
                velocity = new Vector3(low.x + (high.x - low.x) * weight, 0f,
                    low.z + (high.z - low.z) * weight);
                return true;
            }
            velocity = velocities[last];
            return true;
        }

        public override string ToString() => text;

        private static bool TryFloat(string raw, out float value)
        {
            return float.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
