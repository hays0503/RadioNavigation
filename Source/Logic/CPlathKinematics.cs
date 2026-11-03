using System;

namespace UBOAT.Mods.RadioNavigation
{
    internal static class CPlathKinematics
    {
        internal static double Clamp(double degrees)
        {
            if (double.IsNaN(degrees) || double.IsInfinity(degrees)) throw new ArgumentException("Finite reading required.");
            return Math.Max(0, Math.Min(130, degrees));
        }
        internal static double IndexDegrees(double degrees) { return Clamp(degrees) / 2; }
        internal static double DrumDegrees(double degrees)
        {
            double reading = Clamp(degrees);
            return -360 * (reading - Math.Floor(reading));
        }
    }

    internal sealed class CPlathShadeState
    {
        private readonly bool[] active = new bool[7];
        internal int LeftCount { get; private set; }
        internal int RightCount { get; private set; }
        internal bool IsActive(int shade) { return active[shade]; }
        internal void SelectBank(int count, bool left)
        {
            int first = left ? 4 : 0, length = left ? 3 : 4;
            count = Math.Max(0, Math.Min(length, count));
            for (int i = 0; i < length; i++) active[first + i] = i < count;
            if (left) LeftCount = count; else RightCount = count;
        }
        internal void SyncCounts(int left, int right)
        {
            left = Math.Max(0, Math.Min(3, left)); right = Math.Max(0, Math.Min(4, right));
            if (left != LeftCount) SelectBank(left, true);
            if (right != RightCount) SelectBank(right, false);
        }
        internal void Toggle(int shade)
        {
            if (shade < 0 || shade >= 7) throw new ArgumentOutOfRangeException("shade");
            active[shade] = !active[shade];
            int direction = active[shade] ? 1 : -1;
            if (shade < 4) RightCount += direction; else LeftCount += direction;
        }
    }
}
