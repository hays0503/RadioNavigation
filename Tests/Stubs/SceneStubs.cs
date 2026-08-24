namespace UBOAT.Game.Scene
{
    /// <summary>Test stub of the game's camera-mode flags (IntroScene/CameraMode.cs).</summary>
    [System.Flags]
    public enum CameraMode
    {
        None = 0,
        SectionView = 1,
        Transition = 2,
        Map = 4,
        FPP = 8,
        Generic = 0x10,
        FreeFly = 0x20,
        TPP = 0x40,
        Dialogue = 0x80,
        All = 0xFF
    }
}

namespace UBOAT.Game.Scene.Camera
{
    /// <summary>Test stub of the game's map display-mode flags (MapCameraController.cs:41-50).</summary>
    public class MapCameraController
    {
        [System.Flags]
        public enum MapDisplayMode
        {
            Normal = 1,
            Headquarters = 2,
            WorldMap = 4,
            TacticalMap = 8,
            Vacation = 0x10,
            AssignmentSelection = 0x20,
            AssignmentSummary = 0x40
        }
    }
}
