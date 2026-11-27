using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class RadioDFStationTests
    {
        private const string DefaultManagedDir =
            @"C:\Program Files (x86)\Steam\steamapps\common\UBOAT\UBOAT_Data\Managed";

        private static Assembly _game;
        private static bool _attempted;

        private static Assembly Game()
        {
            if (_game != null)
                return _game;
            if (_attempted)
                return null;
            _attempted = true;

            string dir = Environment.GetEnvironmentVariable("UBOAT_GAME_MANAGED");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                dir = Directory.Exists(DefaultManagedDir) ? DefaultManagedDir : null;
            if (dir == null)
                return null;

            var alc = new AssemblyLoadContext("radionav-df-contract", isCollectible: false);
            alc.Resolving += (ctx, name) =>
            {
                string path = Path.Combine(dir, name.Name + ".dll");
                return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null;
            };

            try
            {
                _game = alc.LoadFromAssemblyPath(Path.Combine(dir, "com.uboat.game.dll"));
            }
            catch
            {
                _game = null;
            }
            return _game;
        }

        private static void RequireGame()
        {
            if (Game() == null)
                throw new SkipTestException("com.uboat.game.dll not found.");
        }

        [Fact]
        public void ComputeStationAnchor_TypeViic_FramesBetweenT3PllAndClock()
        {
            var pose = RadioDFPoseMath.ComputeStationAnchor(
                1.065f, 0.055f, 7.195f,
                true, 0.969f, 1.179f, 6.687f,
                true, 1.283f, 0.898f, 6.665f,
                true, 0.985f, 0.672f, 6.836f);

            Assert.Equal(1.065f, pose.EyeX, 3);
            Assert.Equal(1.159f, pose.EyeY, 3);
            Assert.Equal(7.145f, pose.EyeZ, 3);
            Assert.True(pose.EyeZ < 7.195f);
            Assert.True(pose.TargetX > 0.969f && pose.TargetX < 1.283f);
            Assert.Equal(1.179f, pose.TargetY, 3);
            Assert.Equal(6.777f, pose.TargetZ, 3);
        }

        [Fact]
        public void ComputeStationAnchor_TypeIxc_ClampsEyeHeightWithinNaturalSeatedRange()
        {
            var pose = RadioDFPoseMath.ComputeStationAnchor(
                0.780f, 0.055f, 6.829f,
                true, 0.476f, 1.389f, 6.233f,
                true, 0.901f, 0.880f, 6.194f,
                true, 0.384f, 0.638f, 6.304f);

            Assert.Equal(0.780f, pose.EyeX, 3);
            Assert.Equal(1.295f, pose.EyeY, 3);
            Assert.Equal(6.779f, pose.EyeZ, 3);
            Assert.True(pose.EyeZ < 6.829f);
            Assert.True(pose.TargetX > 0.476f && pose.TargetX < 0.901f);
            Assert.Equal(1.389f, pose.TargetY, 3);
        }

        [Fact]
        public void ComputeStationAnchor_FallbacksSafelyWithoutEquipmentChildren()
        {
            var pose = RadioDFPoseMath.ComputeStationAnchor(
                1.0f, 0.055f, 7.2f,
                false, 0f, 0f, 0f,
                false, 0f, 0f, 0f,
                false, 0f, 0f, 0f);

            Assert.Equal(1.0f, pose.TargetX, 3);
            Assert.Equal(1.205f, pose.TargetY, 3);
            Assert.Equal(6.70f, pose.TargetZ, 3);
        }

        [Fact]
        public void StepLookAngles_ClampsYawAndPitchAndHonorsInvertMouse()
        {
            float yaw;
            float pitch;

            RadioDFPoseMath.StepLookAngles(80f, 0f, 10f, 5f, 1f, false, out yaw, out pitch);
            Assert.Equal(RadioDFPoseMath.MaxYawDeg, yaw, 3);
            Assert.Equal(-5f, pitch, 3);

            RadioDFPoseMath.StepLookAngles(-80f, -50f, -10f, 20f, 1f, false, out yaw, out pitch);
            Assert.Equal(-RadioDFPoseMath.MaxYawDeg, yaw, 3);
            Assert.Equal(RadioDFPoseMath.MinPitchDeg, pitch, 3);

            RadioDFPoseMath.StepLookAngles(0f, 0f, 0f, 12f, 2f, true, out yaw, out pitch);
            Assert.Equal(24f, pitch, 3);
        }

        [Fact]
        public void StepZoomFov_ClampsBetweenFiveAndSixtyFiveAndZoomsMonotonically()
        {
            Assert.Equal(RadioDFPoseMath.MaxZoomFovDeg, RadioDFPoseMath.ClampZoomFov(90f), 3);
            Assert.Equal(RadioDFPoseMath.MinZoomFovDeg, RadioDFPoseMath.ClampZoomFov(1f), 3);
            Assert.Equal(RadioDFPoseMath.MaxZoomFovDeg, RadioDFPoseMath.ClampZoomFov(float.NaN), 3);

            float zoomedIn = RadioDFPoseMath.StepZoomFov(65f, 0.1f);
            Assert.True(zoomedIn < 65f && zoomedIn >= 5f);

            float zoomedMin = zoomedIn;
            for (int i = 0; i < 40; i++)
                zoomedMin = RadioDFPoseMath.StepZoomFov(zoomedMin, 0.1f);
            Assert.Equal(5f, zoomedMin, 3);

            float zoomedOut = RadioDFPoseMath.StepZoomFov(5f, -0.1f);
            Assert.True(zoomedOut > 5f && zoomedOut <= 65f);

            float zoomedMax = zoomedOut;
            for (int i = 0; i < 40; i++)
                zoomedMax = RadioDFPoseMath.StepZoomFov(zoomedMax, -0.1f);
            Assert.Equal(65f, zoomedMax, 3);
        }

        [Fact]
        public void ComputeZoomScaledLookSensitivity_ScalesProportionallyWithFov()
        {
            Assert.Equal(2f, RadioDFPoseMath.ComputeZoomScaledLookSensitivity(2f, 65f), 3);
            float atFive = RadioDFPoseMath.ComputeZoomScaledLookSensitivity(2f, 5f);
            Assert.Equal(2f * (5f / 65f), atFive, 4);
            Assert.Equal(1f, RadioDFPoseMath.ComputeZoomScaledLookSensitivity(-1f, 65f), 3);
        }

        [Fact]
        public void ComputeColliderBounds_EnforcesMinimumHitboxSize()
        {
            var fallback = RadioDFPoseMath.ComputeColliderBounds(false, 0f, 0f, 0f, 0f, 0f, 0f);
            Assert.Equal(0.36f, fallback.SizeX, 3);
            Assert.Equal(0.48f, fallback.SizeY, 3);

            var fitted = RadioDFPoseMath.ComputeColliderBounds(true, 0f, 0.22f, 0f, 0.254f, 0.440f, 0.271f);
            Assert.True(fitted.SizeX >= 0.34f);
            Assert.True(fitted.SizeY >= 0.46f);
            Assert.True(fitted.SizeZ >= 0.34f);
        }

        [Fact]
        public void SitActionLocalization_ReturnsExpectedRussianAndEnglishStrings()
        {
            Assert.Equal("Сесть на стул в радио рубке", RadioDFPoseMath.GetSitActionLabel("Russian"));
            Assert.Equal("Sit at direction finder", RadioDFPoseMath.GetSitActionLabel("English"));
            Assert.Contains("Esc", RadioDFPoseMath.GetSitActionDescription("Russian"));
            Assert.Contains("Esc", RadioDFPoseMath.GetSitActionDescription(null));
        }

        [Fact]
        public void GameContract_InteractableSlotAndSelectableFieldsMatchExpectedNames()
        {
            RequireGame();
            Assembly game = Game();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            Type interactable = game.GetType("UBOAT.Game.Scene.Interactions.Interactable", false);
            Assert.NotNull(interactable);
            Assert.NotNull(interactable.GetField("slots", flags));
            Assert.NotNull(interactable.GetField("maxCharacters", flags));
            Assert.NotNull(interactable.GetField("orderedCrewSlotsUse", flags));

            Type slot = game.GetType("UBOAT.Game.Scene.Interactions.InteractableSlot", false);
            Assert.NotNull(slot);
            Assert.NotNull(slot.GetField("animatorParameter", flags));
            Assert.NotNull(slot.GetField("interactionType", flags));
            Assert.NotNull(slot.GetField("teleportLocation", flags));
            Assert.NotNull(slot.GetField("alignMode", flags));
            Assert.NotNull(slot.GetField("agentMode", flags));
            Assert.NotNull(slot.GetField("triggerAnimation", flags));
            Assert.NotNull(slot.GetField("minInteractionTime", flags));
            Assert.NotNull(slot.GetField("providesPerformance", flags));

            Type highlight = game.GetType("UBOAT.Game.Scene.Effects.HighlightEffect", false);
            Assert.NotNull(highlight);
            Assert.NotNull(highlight.GetField("style", flags));
            Assert.NotNull(highlight.GetField("renderers", flags));

            Type selectable = game.GetType("UBOAT.Game.Scene.Selectables.RaycastSelectable", false);
            Assert.NotNull(selectable);
            Assert.NotNull(selectable.GetField("highlightEffect", flags));
            Assert.NotNull(selectable.GetField("type", flags));
            Assert.NotNull(selectable.GetField("triggersVisibility", flags));
            Assert.NotNull(selectable.GetField("acceptUserInput", flags));

            Type activator = game.GetType("UBOAT.Game.Scene.Characters.Activators.Activator", false);
            Assert.NotNull(activator);
            Assert.NotNull(activator.GetField("bannerAttachmentPoint", flags));
            Assert.NotNull(activator.GetField("sectionBannerAttachmentPoint", flags));

            Type extIface = game.GetType("UBOAT.Game.Scene.Camera.ExternalControllers.IExternalController", false);
            Assert.NotNull(extIface);
            Assert.NotNull(extIface.GetMethod("get_MainCamera"));
            Assert.NotNull(extIface.GetMethod("get_DOFTarget"));
            Assert.NotNull(extIface.GetMethod("get_Interior"));
            Assert.NotNull(extIface.GetMethod("get_PostProcessingProfile"));
            Assert.NotNull(extIface.GetMethod("CanBeUsedByCharacter"));
            Assert.NotNull(extIface.GetMethod("StartController"));
            Assert.NotNull(extIface.GetMethod("UpdateController"));
            Assert.NotNull(extIface.GetMethod("LateUpdateController"));
            Assert.NotNull(extIface.GetMethod("PreCull"));
            Assert.NotNull(extIface.GetMethod("StopController"));
            Assert.NotNull(extIface.GetMethod("OnUserExitRequest"));
        }
    }
}
