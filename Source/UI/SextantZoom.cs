namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// PIP ocular zoom steps behind SextantController (P5.4 slice 4a, SRP):
    /// divisor on the main camera's FOV. Index 5 = no zoom by default
    /// (user decision 2026-09-17): the ocular shows the same FOV as the
    /// main camera; +/- keys zoom in from there (index 0 = 6x, the
    /// tightest). Smaller divisor = closer view. Pure C#.
    /// </summary>
    public sealed class SextantZoom
    {
        public static readonly float[] Steps = { 6f, 4.5f, 3f, 2f, 1.5f, 1f };

        public const int DefaultIndex = 5;

        private int index = DefaultIndex;

        public int Index => index;

        public float FovDivisor => Steps[index];

        public bool ZoomIn()
        {
            if (index <= 0)
                return false;
            index--;
            return true;
        }

        public bool ZoomOut()
        {
            if (index >= Steps.Length - 1)
                return false;
            index++;
            return true;
        }

        public float PipFov(float mainFov)
        {
            return mainFov / FovDivisor;
        }
    }
}
