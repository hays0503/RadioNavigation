using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Ideal mean-water silhouette of UBOAT's per-vertex curvature
    /// shader, sampled every 250 m. NOT terrestrial 1.76 sqrt(eye) dip.
    /// Source: installed uboat.core D3D11 water shader DXBC (2026-10-09), NOT
    /// TestUI's outdated EarthCurvature.cginc. Verified raw float32-bit fixture:
    /// Tools/solar-sight/water-curvature-verified.json. No fitted pixel offset.
    /// Finite PIP clip, curvature on/off; waves/fog/land need visual acceptance.</summary>
    public static class SolarSeaHorizon
    {
        private static readonly double[] fall =
        {
            0,0.004905038978904486,0.019620154052972794,0.04414534568786621,
            0.07848061621189117,0.12262596189975739,0.17658138275146484,0.24034687876701355,
            0.3139224648475647,0.3973081111907959,0.49050384759902954,0.5935096144676208,
            0.7063255310058594,0.8289514780044556,0.9613875150680542,1.1036336421966553,
            1.2556898593902588,1.4175560474395752,1.5892324447631836,1.7707188129425049,
            1.9620152711868286,2.1631219387054443,2.3740384578704834,2.5947651863098145,
            2.8253018856048584,3.0656487941741943,3.315805673599243,3.575772762298584,
            3.8455498218536377,4.125136852264404,4.414534091949463,4.713741302490234,
            5.022758483886719,5.341586112976074,5.670223712921143,6.008671283721924,
            6.356928825378418
        };

        public static bool TryAltitude(double eyeHeightM, double farPlaneM, bool curved,
            out double altitudeDeg)
        {
            altitudeDeg = 0;
            if (double.IsNaN(eyeHeightM) || double.IsInfinity(eyeHeightM)
                || eyeHeightM < 0 || eyeHeightM > 14 || double.IsNaN(farPlaneM)
                || farPlaneM < 250 || farPlaneM > 9000)
                return false;
            // For linear y(r) segments y/r is monotone: the highest mean-sea
            // ray is at a knot or the finite far edge. No guessed Earth radius.
            double slope = double.NegativeInfinity;
            for (int i = 1; i < fall.Length && i * 250 <= farPlaneM; i++)
                slope = Math.Max(slope, (-eyeHeightM - (curved ? fall[i] : 0)) / (i * 250));
            double index = farPlaneM / 250;
            int low = Math.Min(fall.Length - 2, (int)index);
            double drop = curved ? fall[low] + (index - low) * (fall[low + 1] - fall[low]) : 0;
            slope = Math.Max(slope, (-eyeHeightM - drop) / farPlaneM);
            // At zero height the tangent is the limiting r -> 0 ray (curved
            // polygon begins with a sloping segment). Respect that segment.
            altitudeDeg = Math.Atan(slope) * 180 / Math.PI;
            return true;
        }
    }
}
