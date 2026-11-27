using System;

namespace UBOAT.Mods.RadioNavigation
{
    internal enum T3ControlKind
    {
        None = 0,
        PowerSwitch = 1,
        BandSelector = 2,
        DialCursor = 3,
        TuningCrank = 4,
        Volume = 5,
        Feedback = 6,
        ReglerPeilen = 7,
        ReglerSeite = 8,
        SeitePeilen = 9,
        RundEmpfang = 10,
        HTrimmer = 11,
        BattRohrenSwitch = 12,
        LidFront = 13,
        DoorTubes = 14,
        CapBattery = 15,
        CoverTopAntenna = 16
    }

    internal struct T3StubCutoutBoxes
    {
        public float MinX, MinY, MinZ;
        public float MaxX, MaxY, MaxZ;
        public float WireMinX, WireMinY, WireMinZ;
        public float WireMaxX, WireMaxY, WireMaxZ;
        public float SpeakerColCenterY, SpeakerColSizeY;
        public float MountX, MountY, MountZ, MountScale;
    }

    internal struct T3LightingState
    {
        public float PowerFactor;
        public float FilamentWarmth;
        public float DialEmission;
        public float VoltmeterEmission;
        public float FilamentEmission;
        public float PointLightIntensity;
        public float TubeLightIntensity;
    }

    /// <summary>
    /// Pure kinematics, cutout bounds, lighting envelope, and localization helpers
    /// for the 3D T3PLLa38 direction finder receiver. C# 7.0, offline testable.
    /// </summary>
    internal static class T3PLLa38ControlMath
    {
        internal const float CabinetWidthM = 0.4482f;
        internal const float CabinetHeightM = 0.5040f;
        internal const float CabinetDepthM = 0.2100f;

        internal const float DialHalfSpanDeg = 85f;
        internal const float CursorPanelHalfSpanDeg = 90f;
        internal const float TuningCrankGearRatio = 7f;
        internal const float PowerSwitchAngleDeg = 42f;
        internal const float RundSwitchAngleDeg = 44f;
        internal const float CrankHandleOpenDeg = -90f;
        internal const float VolumeMaxDeg = 95f;
        internal const float FeedbackMaxDeg = 90f;
        internal const float HTrimmerMinDeg = -135f;
        internal const float HTrimmerMaxDeg = 55f;
        internal const float ReglerPeilenMaxDeg = 95f;
        internal const float ReglerSeiteMaxDeg = 265f;

        // Blender rotation_euler[1] band detents -> Unity localRotation.y = -BlenderY
        internal static readonly float[] BandAnglesBlenderDeg = { 69.0f, 28.5f, 0.0f, -35.5f, -70.0f };
        internal static readonly string[] BandRoman = { "I", "II", "III", "IV", "V" };

        // Batterie / Rohren test selector positions (Blender Y deg)
        internal static readonly float[] BattTestAnglesBlenderDeg = { 0.0f, 12.5f, 25.0f, 39.5f, 49.3f, 64.0f, 75.0f, 89.0f, 103.0f };
        internal static readonly string[] BattTestNames = { "H (4V)", "S (50V)", "A (95V)", "R I", "R II", "R III", "R IV", "R V", "R VI" };

        internal static T3StubCutoutBoxes ComputeSlotGeometry(
            float colCenterX,
            float colCenterY,
            float colCenterZ,
            float colSizeX,
            float colSizeY,
            float colSizeZ)
        {
            float halfX = colSizeX * 0.5f;
            float halfY = colSizeY * 0.5f;
            float halfZ = colSizeZ * 0.5f;

            float colMinY = colCenterY - halfY;
            float colMaxY = colCenterY + halfY;
            float colMinZ = colCenterZ - halfZ;
            float colMaxZ = colCenterZ + halfZ;

            // Lower ~58.5% of RR.BoxCollider[3] covers the vanilla T3 PLLa 38 box;
            // upper portion holds the intercom/speaker unit and stays untouched.
            float splitY = colMinY + colSizeY * 0.585f;
            float speakerCenterY = 0.5f * (splitY + colMaxY);
            float speakerSizeY = Math.Max(0.10f, colMaxY - splitY);

            // Fit 3D T3PLLa38 cabinet cleanly inside the stub width & height budget.
            float scaleByWidth = (colSizeX - 0.004f) / CabinetWidthM;
            float scale = Clamp(scaleByWidth, 0.65f, 0.92f);
            float halfDepthScaled = 0.5f * CabinetDepthM * scale;

            return new T3StubCutoutBoxes
            {
                MinX = colCenterX - halfX - 0.025f,
                MaxX = colCenterX + halfX + 0.020f,
                MinY = colMinY - 0.015f,
                MaxY = splitY,
                MinZ = colMinZ - 0.060f,
                MaxZ = colMaxZ + 0.080f,

                WireMinX = colCenterX - halfX - 0.035f,
                WireMaxX = colCenterX + halfX + 0.065f,
                WireMinY = colMinY - 0.110f,
                WireMaxY = colMinY - 0.002f,
                WireMinZ = colMinZ - 0.055f,
                WireMaxZ = colCenterZ + 0.055f,

                SpeakerColCenterY = speakerCenterY,
                SpeakerColSizeY = speakerSizeY,

                MountX = colCenterX,
                MountY = colMinY + 0.020f,
                MountZ = colMinZ + halfDepthScaled + 0.006f,
                MountScale = scale
            };
        }

        internal static float ComputePowerSwitchBlenderDeg(bool powerOn)
        {
            return powerOn ? PowerSwitchAngleDeg : -PowerSwitchAngleDeg;
        }

        internal const float SeitePeilenSeiteDeg = -45f;
        internal const float SeitePeilenPeilenDeg = -118f;

        internal static float ComputeRundSwitchBlenderDeg(bool rundEmpfangOn)
        {
            // Panel plate prints "Rund-Empfang" on the left (-44 deg) and "Peilen" on the right (+44 deg).
            return rundEmpfangOn ? -RundSwitchAngleDeg : RundSwitchAngleDeg;
        }

        internal static float ComputeSeitePeilenBlenderDeg(bool seiteMode)
        {
            // Colored sectors live on the left semicircle: Red/Seite up-left (-45 deg), Yellow/Peilen down-left (-118 deg).
            return seiteMode ? SeitePeilenSeiteDeg : SeitePeilenPeilenDeg;
        }

        internal static float ComputeTopAntennaCoverBlenderDeg(float openDeg)
        {
            // Rear-hinged lid extends toward the front panel (-Y): negative X rotation lifts it upward.
            return -openDeg;
        }

        internal static float DialDegToCursorPanelAngle(float dialBlenderDeg)
        {
            float clamped = Clamp(dialBlenderDeg, -DialHalfSpanDeg, DialHalfSpanDeg);
            return clamped * (CursorPanelHalfSpanDeg / DialHalfSpanDeg);
        }

        internal static float CursorPanelAngleToDialDeg(float cursorPanelAngle)
        {
            if (float.IsNaN(cursorPanelAngle) || float.IsInfinity(cursorPanelAngle))
                return 0f;
            float clamped = Clamp(cursorPanelAngle, -CursorPanelHalfSpanDeg, CursorPanelHalfSpanDeg);
            return clamped * (DialHalfSpanDeg / CursorPanelHalfSpanDeg);
        }

        internal static float BandIndexToBlenderAngle(int bandIndex)
        {
            int idx = ClampInt(bandIndex, 0, BandAnglesBlenderDeg.Length - 1);
            return BandAnglesBlenderDeg[idx];
        }

        internal static float BandIndexToSliderLocalZ(int bandIndex)
        {
            float bandRad = BandIndexToBlenderAngle(bandIndex) * (float)(Math.PI / 180.0);
            return Clamp(0.1135f - 0.0175f * bandRad, 0.0925f, 0.1345f);
        }

        internal static float SpinnerAngleToSymmetricDeg(float spinnerAngle, float maxAbsDeg)
        {
            float unit = SpinnerScale.ToUnit(spinnerAngle);
            return (unit * 2f - 1f) * maxAbsDeg;
        }

        internal static float SymmetricDegToSpinnerAngle(float deg, float maxAbsDeg)
        {
            if (maxAbsDeg <= 0f)
                return 0f;
            float unit = Clamp((deg / maxAbsDeg + 1f) * 0.5f, 0f, 1f);
            return unit * SpinnerScale.MaxAngle;
        }

        internal static float ComputeHTrimmerUnit(float hTrimmerDeg)
        {
            float span = HTrimmerMaxDeg - HTrimmerMinDeg;
            return Clamp((hTrimmerDeg - HTrimmerMinDeg) / span, 0f, 1f);
        }

        internal static float ComputeHeaterVolts(bool powerOn, float hTrimmerDeg)
        {
            if (!powerOn)
                return 0f;
            // Authentic range: ~2.6V..4.6V around 4.0V nominal red mark
            float unit = ComputeHTrimmerUnit(hTrimmerDeg);
            return 2.6f + unit * 2.0f;
        }

        internal static float ComputeVoltmeterNeedleBlenderDeg(bool powerOn, int battTestIndex, float hTrimmerDeg)
        {
            const float restDeg = -50.5f;
            if (!powerOn)
                return restDeg;

            int idx = ClampInt(battTestIndex, 0, BattTestAnglesBlenderDeg.Length - 1);
            float heaterUnit = ComputeHTrimmerUnit(hTrimmerDeg);
            // Position 0 ('H'): directly reflects heater rheostat 2.6..4.6V (center-high deflection).
            // S (1) = screen grid, A (2) = anode, 1..6 = emission check.
            float targetOffset;
            if (idx == 0)
                targetOffset = 22f + heaterUnit * 52f;
            else if (idx == 1)
                targetOffset = 58f + heaterUnit * 12f;
            else if (idx == 2)
                targetOffset = 78f + heaterUnit * 12f;
            else
                targetOffset = 46f + heaterUnit * 20f - (idx - 3) * 1.8f;

            return Clamp(restDeg + targetOffset, -50.5f, 50.5f);
        }

        internal static T3LightingState ComputeLighting(bool powerOn, float warmup01, float hTrimmerDeg)
        {
            float p = powerOn ? Clamp(warmup01, 0f, 1f) : 0f;
            float h = 0.28f + 0.72f * ComputeHTrimmerUnit(hTrimmerDeg);
            return new T3LightingState
            {
                PowerFactor = p,
                FilamentWarmth = p * h,
                DialEmission = 0.55f * p * h,
                VoltmeterEmission = 0.95f * p * h,
                FilamentEmission = 3.2f * p * h,
                PointLightIntensity = 0.42f * p * h,
                TubeLightIntensity = 1.45f * p * h
            };
        }

        private static readonly System.Collections.Generic.Dictionary<string, T3ControlKind> ControlNodeMap =
            new System.Collections.Generic.Dictionary<string, T3ControlKind>
        {
            { "Ctrl_Power_Aus_Ein", T3ControlKind.PowerSwitch },
            { "Ctrl_Band_Selector", T3ControlKind.BandSelector },
            { "Ctrl_Dial_Cursor", T3ControlKind.DialCursor },
            { "Ctrl_Tuning_Crank", T3ControlKind.TuningCrank },
            { "Ctrl_Crank_Handle", T3ControlKind.TuningCrank },
            { "Ctrl_Lautstaerke", T3ControlKind.Volume },
            { "Ctrl_Rueckkopplung", T3ControlKind.Feedback },
            { "Ctrl_Regler_Peilen", T3ControlKind.ReglerPeilen },
            { "Ctrl_Regler_Seite", T3ControlKind.ReglerSeite },
            { "Ctrl_Seite_Peilen", T3ControlKind.SeitePeilen },
            { "Ctrl_Rund_Empfang", T3ControlKind.RundEmpfang },
            { "Ctrl_H_Trimmer", T3ControlKind.HTrimmer },
            { "Ctrl_Batterie_Rohren", T3ControlKind.BattRohrenSwitch },
            { "Lid_Front", T3ControlKind.LidFront },
            { "Handle_Side_Left", T3ControlKind.LidFront },
            { "Door_Tubes", T3ControlKind.DoorTubes },
            { "Cap_Battery", T3ControlKind.CapBattery },
            { "Cover_Top_Antenna", T3ControlKind.CoverTopAntenna }
        };

        private static readonly System.Collections.Generic.Dictionary<T3ControlKind, string> RussianLabels =
            new System.Collections.Generic.Dictionary<T3ControlKind, string>
        {
            { T3ControlKind.PowerSwitch, "Питание (Aus / Ein)" },
            { T3ControlKind.BandSelector, "Диапазон (I–V)" },
            { T3ControlKind.DialCursor, "Шкала настройки" },
            { T3ControlKind.TuningCrank, "Точная настройка (1:7)" },
            { T3ControlKind.Volume, "Громкость (Lautstärke)" },
            { T3ControlKind.Feedback, "Обратная связь (Rückkopplung)" },
            { T3ControlKind.ReglerPeilen, "Регулятор пеленга (Peilen)" },
            { T3ControlKind.ReglerSeite, "Регулятор стороны (Seite)" },
            { T3ControlKind.SeitePeilen, "Сторона / Пеленг" },
            { T3ControlKind.RundEmpfang, "Круговой приём / Пеленг" },
            { T3ControlKind.HTrimmer, "Накал ламп (H)" },
            { T3ControlKind.BattRohrenSwitch, "Контроль ламп / питания" },
            { T3ControlKind.LidFront, "Передняя крышка" },
            { T3ControlKind.DoorTubes, "Дверца лампового отсека" },
            { T3ControlKind.CapBattery, "Заглушка батареи" },
            { T3ControlKind.CoverTopAntenna, "Крышка антенного ввода" }
        };

        private static readonly System.Collections.Generic.Dictionary<T3ControlKind, string> EnglishLabels =
            new System.Collections.Generic.Dictionary<T3ControlKind, string>
        {
            { T3ControlKind.PowerSwitch, "Power (Aus / Ein)" },
            { T3ControlKind.BandSelector, "Band Selector (I–V)" },
            { T3ControlKind.DialCursor, "Tuning Dial" },
            { T3ControlKind.TuningCrank, "Fine Tuning Crank (1:7)" },
            { T3ControlKind.Volume, "Volume (Lautstärke)" },
            { T3ControlKind.Feedback, "Feedback (Rückkopplung)" },
            { T3ControlKind.ReglerPeilen, "DF Bearing Trim (Peilen)" },
            { T3ControlKind.ReglerSeite, "Sense Balance (Seite)" },
            { T3ControlKind.SeitePeilen, "Seite / Peilen" },
            { T3ControlKind.RundEmpfang, "Rundempfang / Peilen" },
            { T3ControlKind.HTrimmer, "Heater Rheostat (H)" },
            { T3ControlKind.BattRohrenSwitch, "Battery / Tubes Test" },
            { T3ControlKind.LidFront, "Front Cover" },
            { T3ControlKind.DoorTubes, "Tube Bay Door" },
            { T3ControlKind.CapBattery, "Battery Cap" },
            { T3ControlKind.CoverTopAntenna, "Antenna Hatch" }
        };

        internal static T3ControlKind ClassifyControlNode(string nodeName)
        {
            if (string.IsNullOrEmpty(nodeName))
                return T3ControlKind.None;
            T3ControlKind kind;
            return ControlNodeMap.TryGetValue(nodeName, out kind) ? kind : T3ControlKind.None;
        }

        internal static bool IsContinuousControl(T3ControlKind kind)
        {
            return kind == T3ControlKind.DialCursor
                || kind == T3ControlKind.TuningCrank
                || kind == T3ControlKind.Volume
                || kind == T3ControlKind.Feedback
                || kind == T3ControlKind.ReglerPeilen
                || kind == T3ControlKind.ReglerSeite
                || kind == T3ControlKind.HTrimmer;
        }

        internal static void PadControlColliderSize(
            T3ControlKind kind,
            float rawX, float rawY, float rawZ,
            out float outX, out float outY, out float outZ)
        {
            if (kind == T3ControlKind.None)
            {
                outX = rawX;
                outY = rawY;
                outZ = rawZ;
                return;
            }

            float minX = 0.026f;
            float minY = 0.020f;
            float minZ = 0.026f;
            if (kind == T3ControlKind.PowerSwitch || kind == T3ControlKind.RundEmpfang)
            {
                minX = 0.030f;
                minY = 0.024f;
                minZ = 0.028f;
            }
            else if (kind == T3ControlKind.DialCursor)
            {
                minX = 0.056f;
                minY = 0.018f;
                minZ = 0.056f;
            }
            else if (kind == T3ControlKind.LidFront || kind == T3ControlKind.DoorTubes || kind == T3ControlKind.CapBattery || kind == T3ControlKind.CoverTopAntenna)
            {
                minX = 0.030f;
                minY = 0.024f;
                minZ = 0.030f;
            }

            outX = Math.Max(rawX, minX);
            outY = Math.Max(rawY, minY);
            outZ = Math.Max(rawZ, minZ);
        }

        internal static string GetControlLabel(T3ControlKind kind, string language)
        {
            var map = IsRussian(language) ? RussianLabels : EnglishLabels;
            string label;
            return map.TryGetValue(kind, out label) ? label : string.Empty;
        }

        internal static string FormatOpenState(bool isOpen, string language)
        {
            if (IsRussian(language))
                return isOpen ? "Открыта" : "Закрыта";
            return isOpen ? "Open" : "Closed";
        }

        internal static string FormatFrontLidState(bool attached, bool isOpen, string language)
        {
            if (!attached)
                return IsRussian(language) ? "Снята" : "Stowed";
            return FormatOpenState(isOpen, language);
        }

        internal static int ComputeReglerSeiteScale(float reglerSeiteDeg)
        {
            float unit = Clamp(reglerSeiteDeg / ReglerSeiteMaxDeg, 0f, 1f);
            return ClampInt((int)Math.Round(1f + unit * 19f), 1, 20);
        }

        private static bool IsRussian(string language)
        {
            return string.Equals(language, "Russian", StringComparison.OrdinalIgnoreCase);
        }

        internal static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        internal static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
