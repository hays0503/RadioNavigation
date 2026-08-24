namespace UBOAT.Mods.RadioNavigation
{
    using UBOAT.Game.Scene;
    using UBOAT.Game.Scene.Camera;

    /// <summary>
    /// Shared visibility gate for the mod's map markers (lighthouse, radio
    /// stations): the marker layer shows only while the map camera is open
    /// in a non-headquarters display mode. Pure enum logic — unit-tested
    /// against the stub enums (ADR-015 test shield).
    /// </summary>
    public static class NavMarkerGate
    {
        public static bool IsMapOpen(
            bool cameraActiveAndEnabled,
            CameraMode cameraMode,
            bool hasMapController,
            MapCameraController.MapDisplayMode mapDisplayMode)
        {
            return cameraActiveAndEnabled
                && cameraMode == CameraMode.Map
                && hasMapController
                && mapDisplayMode != MapCameraController.MapDisplayMode.Headquarters;
        }
    }
}
