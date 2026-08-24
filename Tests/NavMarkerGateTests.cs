using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Visibility gate for the mod's map markers (NavMapMarkerBase): map
    /// camera open and active, controller present, headquarters mode
    /// excluded. Everything else (world/tactical/vacation map modes) passes.
    /// </summary>
    public sealed class NavMarkerGateTests
    {
        private static bool Open(CameraMode mode, MapCameraController.MapDisplayMode display, bool active = true, bool hasController = true)
        {
            return NavMarkerGate.IsMapOpen(active, mode, hasController, display);
        }

        [Fact]
        public void MapMode_NormalDisplay_IsOpen()
        {
            Assert.True(Open(CameraMode.Map, MapCameraController.MapDisplayMode.Normal));
        }

        [Fact]
        public void HeadquartersDisplay_IsClosed()
        {
            Assert.False(Open(CameraMode.Map, MapCameraController.MapDisplayMode.Headquarters));
        }

        [Fact]
        public void NonMapCameraMode_IsClosed()
        {
            Assert.False(Open(CameraMode.FPP, MapCameraController.MapDisplayMode.Normal));
            Assert.False(Open(CameraMode.FreeFly, MapCameraController.MapDisplayMode.Normal));
            Assert.False(Open(CameraMode.SectionView, MapCameraController.MapDisplayMode.Normal));
        }

        [Fact]
        public void InactiveCamera_IsClosed()
        {
            Assert.False(Open(CameraMode.Map, MapCameraController.MapDisplayMode.Normal, active: false));
        }

        [Fact]
        public void MissingMapController_IsClosed()
        {
            Assert.False(Open(CameraMode.Map, MapCameraController.MapDisplayMode.Normal, hasController: false));
        }

        [Fact]
        public void OtherMapDisplayModes_PassLikeNormal()
        {
            Assert.True(Open(CameraMode.Map, MapCameraController.MapDisplayMode.WorldMap));
            Assert.True(Open(CameraMode.Map, MapCameraController.MapDisplayMode.TacticalMap));
            Assert.True(Open(CameraMode.Map, MapCameraController.MapDisplayMode.Vacation));
        }
    }
}
