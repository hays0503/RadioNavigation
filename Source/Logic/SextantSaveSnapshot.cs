using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Save-local worksheet, not device/UI/camera state. The frozen
    /// section is restored verbatim; loading must never solve it again using
    /// the current clock, ship position, speed or ephemeris calibration.</summary>
    public sealed class SextantSaveSnapshot
    {
        public SextantObservation Observation1;
        public SextantObservation Observation2;
        public SextantObservation Observation3;
        public string SectionText;
        public double CalibrationMinutes;
        public bool FixPublished;
        public double FixLatDeg;
        public double FixLonDeg;
        public double FixErrorNm = double.NaN;
        public double FixCrossingDeg;
        public bool FixSameBody;
        public CourseSamplerSnapshot Course = new CourseSamplerSnapshot();
        public FixMarkerSnapshot Markers = new FixMarkerSnapshot();
    }

    /// <summary>Computed marker coordinates and player dismissal. Native
    /// MapPointData in the SAME save retains dragged positions and shapes.</summary>
    public sealed class FixMarkerSnapshot
    {
        public bool HasFix;
        public double Lat;
        public double Lon;
        public string Label;
        public bool HasTriangle;
        public double[] VertexLat = new double[3];
        public double[] VertexLon = new double[3];
        public int SuppressedMask;

        public bool IsVisible(int index)
        {
            return index >= 0 && index < 4 && HasFix
                && (index == 0 || HasTriangle)
                && (SuppressedMask & (1 << index)) == 0;
        }

        public static string Key(int index)
        {
            return index == 0 ? MapMarkerPoints.FixKey
                : MapMarkerPoints.LopVertexKey(index - 1);
        }

        public static int IndexOfKey(string key)
        {
            for (int i = 0; i < 4; i++)
            {
                if (string.Equals(key, Key(i), StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }
    }
}
