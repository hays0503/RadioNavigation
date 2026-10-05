namespace UnityEngine
{
    /// <summary>
    /// Minimal Time stub for tests: frame clock behind StationManager
    /// elapsed math. Tests set time explicitly; defaults are neutral.
    /// </summary>
    public static class Time
    {
        public static float time { get; set; }

        public static float deltaTime { get; set; }

        public static float unscaledDeltaTime { get; set; }

        public static float timeScale { get; set; }

        public static int frameCount { get; set; }

        static Time()
        {
            timeScale = 1f;
        }
    }
}
