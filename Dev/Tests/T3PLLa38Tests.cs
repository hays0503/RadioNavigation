using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class T3PLLa38Tests
    {
        [Fact]
        public void ComputeSlotGeometry_TypeViic_SplitsT3AndPreservesUpperSpeaker()
        {
            T3StubCutoutBoxes boxes = T3PLLa38ControlMath.ComputeSlotGeometry(
                0.969f, 1.179f, 6.687f,
                0.360f, 0.810f, 0.180f);

            // Split cleanly below upper speaker and cover entire stub box
            Assert.InRange(boxes.MaxY, 1.22f, 1.28f);
            Assert.True(boxes.MinY < 0.774f);
            Assert.True(boxes.SpeakerColCenterY > boxes.MaxY);
            Assert.True(boxes.SpeakerColSizeY > 0.25f && boxes.SpeakerColSizeY < 0.45f);

            // Fitted cabinet width stays within the 0.36m slot (never clips clock or wall)
            float fittedWidth = T3PLLa38ControlMath.CabinetWidthM * boxes.MountScale;
            Assert.InRange(fittedWidth, 0.34f, 0.36f);
        }

        [Fact]
        public void DialConversion_RoundTripsAndAppliesCrankRatio()
        {
            Assert.Equal(-90f, T3PLLa38ControlMath.DialDegToCursorPanelAngle(-85f), 2);
            Assert.Equal(0f, T3PLLa38ControlMath.DialDegToCursorPanelAngle(0f), 2);
            Assert.Equal(90f, T3PLLa38ControlMath.DialDegToCursorPanelAngle(85f), 2);

            float dial = T3PLLa38ControlMath.CursorPanelAngleToDialDeg(45f);
            Assert.Equal(42.5f, dial, 2);
            Assert.Equal(45f, T3PLLa38ControlMath.DialDegToCursorPanelAngle(dial), 2);
        }

        [Fact]
        public void BandSliderLocalZ_MatchesCalibratedBlenderPositions()
        {
            // Band II = +28.5 deg -> 0.1048m calibrated in FBX
            Assert.Equal(0.1048f, T3PLLa38ControlMath.BandIndexToSliderLocalZ(1), 3);
            Assert.True(T3PLLa38ControlMath.BandIndexToSliderLocalZ(0) < T3PLLa38ControlMath.BandIndexToSliderLocalZ(4));
        }

        [Fact]
        public void VoltmeterNeedleAndLighting_RespondToPowerAndHeater()
        {
            Assert.Equal(-50.5f, T3PLLa38ControlMath.ComputeVoltmeterNeedleBlenderDeg(false, 0, 0f), 2);
            float onH = T3PLLa38ControlMath.ComputeVoltmeterNeedleBlenderDeg(true, 0, 20f);
            Assert.True(onH > -10f && onH <= 50.5f);

            T3LightingState offLight = T3PLLa38ControlMath.ComputeLighting(false, 1f, 0f);
            Assert.Equal(0f, offLight.DialEmission, 3);
            Assert.Equal(0f, offLight.FilamentEmission, 3);
            Assert.Equal(0f, offLight.TubeLightIntensity, 3);

            T3LightingState onLight = T3PLLa38ControlMath.ComputeLighting(true, 1f, 30f);
            Assert.True(onLight.DialEmission > 0.2f);
            Assert.True(onLight.VoltmeterEmission > 0.3f);
            Assert.True(onLight.FilamentEmission > 1.0f);
            Assert.True(onLight.PointLightIntensity > 0.2f);
            Assert.True(onLight.TubeLightIntensity > 0.5f);
        }

        [Fact]
        public void ClassifyControlNode_MapsAllInteractivePartsAndLabels()
        {
            Assert.Equal(T3ControlKind.PowerSwitch, T3PLLa38ControlMath.ClassifyControlNode("Ctrl_Power_Aus_Ein"));
            Assert.Equal(T3ControlKind.TuningCrank, T3PLLa38ControlMath.ClassifyControlNode("Ctrl_Crank_Handle"));
            Assert.Equal(T3ControlKind.LidFront, T3PLLa38ControlMath.ClassifyControlNode("Lid_Front"));
            Assert.True(T3PLLa38ControlMath.IsContinuousControl(T3ControlKind.DialCursor));
            Assert.False(T3PLLa38ControlMath.IsContinuousControl(T3ControlKind.PowerSwitch));
            Assert.False(string.IsNullOrEmpty(T3PLLa38ControlMath.GetControlLabel(T3ControlKind.DialCursor, "Russian")));
            Assert.False(string.IsNullOrEmpty(T3PLLa38ControlMath.GetControlLabel(T3ControlKind.DialCursor, "English")));

            float px, py, pz;
            T3PLLa38ControlMath.PadControlColliderSize(T3ControlKind.PowerSwitch, 0.0044f, 0.014f, 0.005f, out px, out py, out pz);
            Assert.True(px >= 0.028f && py >= 0.020f && pz >= 0.026f);

            foreach (T3ControlKind kind in System.Enum.GetValues(typeof(T3ControlKind)))
            {
                if (kind == T3ControlKind.None)
                    continue;
                Assert.False(string.IsNullOrEmpty(T3PLLa38ControlMath.GetControlLabel(kind, "Russian")));
                Assert.False(string.IsNullOrEmpty(T3PLLa38ControlMath.GetControlLabel(kind, "English")));
            }
            Assert.Equal("Открыта", T3PLLa38ControlMath.FormatOpenState(true, "Russian"));
            Assert.Equal("Closed", T3PLLa38ControlMath.FormatOpenState(false, "English"));
            Assert.Equal("Снята", T3PLLa38ControlMath.FormatFrontLidState(false, false, "Russian"));
            Assert.Equal(20, T3PLLa38ControlMath.ComputeReglerSeiteScale(265f));
            Assert.Equal(42f, T3PLLa38ControlMath.ComputePowerSwitchBlenderDeg(true), 2);
            Assert.Equal(-42f, T3PLLa38ControlMath.ComputePowerSwitchBlenderDeg(false), 2);
            Assert.Equal(-44f, T3PLLa38ControlMath.ComputeRundSwitchBlenderDeg(true), 2);
            Assert.Equal(44f, T3PLLa38ControlMath.ComputeRundSwitchBlenderDeg(false), 2);
            Assert.Equal(-45f, T3PLLa38ControlMath.ComputeSeitePeilenBlenderDeg(true), 2);
            Assert.Equal(-118f, T3PLLa38ControlMath.ComputeSeitePeilenBlenderDeg(false), 2);
            Assert.Equal(-90f, T3PLLa38ControlMath.CrankHandleOpenDeg, 2);
            Assert.Equal(-90f, T3PLLa38ControlMath.ComputeTopAntennaCoverBlenderDeg(90f), 2);
        }
    }
}
