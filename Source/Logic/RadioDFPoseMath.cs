using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Calculated local-space camera and focus coordinates inside Radio Room for the
    /// seated radio direction finder station.
    /// </summary>
    internal struct RadioDFPosedAnchor
    {
        public float EyeX;
        public float EyeY;
        public float EyeZ;
        public float TargetX;
        public float TargetY;
        public float TargetZ;
        public float DofX;
        public float DofY;
        public float DofZ;
    }

    /// <summary>
    /// Box dimensions in local space for the stool interaction collider.
    /// </summary>
    internal struct RadioDFStoolColliderBounds
    {
        public float CenterX;
        public float CenterY;
        public float CenterZ;
        public float SizeX;
        public float SizeY;
        public float SizeZ;
    }

    /// <summary>
    /// Pure framing, mouse-look clamping, collider fitting, and localization helpers
    /// for the seated radio direction finder position.
    /// </summary>
    internal static class RadioDFPoseMath
    {
        internal const float DefaultEyeHeightAboveDeck = 1.15f;
        internal const float MinEyeHeightAboveStool = 1.05f;
        internal const float MaxEyeHeightAboveStool = 1.24f;
        internal const float SeatBackOffsetZ = 0.06f;
        internal const float MaxYawDeg = 85f;
        internal const float MinPitchDeg = -55f;
        internal const float MaxPitchDeg = 55f;

        internal static RadioDFPosedAnchor ComputeStationAnchor(
            float stoolX,
            float stoolY,
            float stoolZ,
            bool hasT3Pll,
            float t3X,
            float t3Y,
            float t3Z,
            bool hasClock,
            float clockX,
            float clockY,
            float clockZ,
            bool hasRepeater,
            float repX,
            float repY,
            float repZ)
        {
            float targetY = ResolveTargetY(stoolY, hasT3Pll, t3Y, hasClock, clockY, hasRepeater, repY);
            float eyeY = Clamp(targetY - 0.02f, stoolY + MinEyeHeightAboveStool, stoolY + MaxEyeHeightAboveStool);

            float targetX = ResolveTargetX(stoolX, hasT3Pll, t3X, hasClock, clockX, hasRepeater, repX);
            float targetZ = ResolveTargetZ(stoolZ, hasT3Pll, t3Z, hasClock, clockZ, hasRepeater, repZ);
            float dofX = hasT3Pll ? t3X : targetX;

            return new RadioDFPosedAnchor
            {
                EyeX = stoolX,
                EyeY = eyeY,
                EyeZ = stoolZ + SeatBackOffsetZ,
                TargetX = targetX,
                TargetY = targetY,
                TargetZ = targetZ,
                DofX = dofX,
                DofY = targetY,
                DofZ = targetZ
            };
        }

        private static float ResolveTargetY(
            float stoolY,
            bool hasT3Pll,
            float t3Y,
            bool hasClock,
            float clockY,
            bool hasRepeater,
            float repY)
        {
            if (hasT3Pll)
                return t3Y;
            if (hasClock)
                return clockY + 0.25f;
            if (hasRepeater)
                return repY + 0.48f;
            return stoolY + DefaultEyeHeightAboveDeck;
        }

        private static float ResolveTargetX(
            float stoolX,
            bool hasT3Pll,
            float t3X,
            bool hasClock,
            float clockX,
            bool hasRepeater,
            float repX)
        {
            if (hasT3Pll && hasClock)
                return 0.72f * t3X + 0.28f * clockX;
            if (hasT3Pll)
                return t3X;
            if (hasRepeater)
                return repX;
            return stoolX;
        }

        private static float ResolveTargetZ(
            float stoolZ,
            bool hasT3Pll,
            float t3Z,
            bool hasClock,
            float clockZ,
            bool hasRepeater,
            float repZ)
        {
            if (hasT3Pll)
                return t3Z + 0.09f;
            if (hasClock)
                return clockZ + 0.09f;
            if (hasRepeater)
                return repZ - 0.14f;
            return stoolZ - 0.50f;
        }

        internal static void StepLookAngles(
            float yawDeg,
            float pitchDeg,
            float horizontalDelta,
            float verticalDelta,
            float sensitivity,
            bool invertMouse,
            out float nextYawDeg,
            out float nextPitchDeg)
        {
            float safeSensitivity = sensitivity > 0f ? sensitivity : 1f;
            nextYawDeg = Clamp(yawDeg + horizontalDelta * safeSensitivity, -MaxYawDeg, MaxYawDeg);

            float verticalStep = verticalDelta * safeSensitivity;
            float signedVertical = invertMouse ? verticalStep : -verticalStep;
            nextPitchDeg = Clamp(pitchDeg + signedVertical, MinPitchDeg, MaxPitchDeg);
        }

        internal static RadioDFStoolColliderBounds ComputeColliderBounds(
            bool hasMeshBounds,
            float centerX,
            float centerY,
            float centerZ,
            float sizeX,
            float sizeY,
            float sizeZ)
        {
            if (!hasMeshBounds)
            {
                return new RadioDFStoolColliderBounds
                {
                    CenterX = 0f,
                    CenterY = 0.24f,
                    CenterZ = 0f,
                    SizeX = 0.36f,
                    SizeY = 0.48f,
                    SizeZ = 0.36f
                };
            }

            return new RadioDFStoolColliderBounds
            {
                CenterX = centerX,
                CenterY = centerY,
                CenterZ = centerZ,
                SizeX = sizeX + 0.08f > 0.34f ? sizeX + 0.08f : 0.34f,
                SizeY = sizeY + 0.04f > 0.46f ? sizeY + 0.04f : 0.46f,
                SizeZ = sizeZ + 0.08f > 0.34f ? sizeZ + 0.08f : 0.34f
            };
        }

        internal static string GetSitActionLabel(string language)
        {
            if (IsRussian(language))
                return "\u0421\u0435\u0441\u0442\u044c \u043d\u0430 \u0441\u0442\u0443\u043b \u0432 \u0440\u0430\u0434\u0438\u043e \u0440\u0443\u0431\u043a\u0435";
            return "Sit at direction finder";
        }

        internal static string GetSitActionDescription(string language)
        {
            if (IsRussian(language))
            {
                return "\u0421\u0435\u0441\u0442\u044c \u0437\u0430 \u0441\u0442\u043e\u043b \u0440\u0430\u0434\u0438\u043e\u043f\u0435\u043b\u0435\u043d\u0433\u0430\u0442\u043e\u0440\u0430 "
                    + "(\u043e\u0441\u043c\u043e\u0442\u0440 \u043c\u044b\u0448\u043a\u043e\u0439, Esc \u2014 \u0432\u0441\u0442\u0430\u0442\u044c)";
            }
            return "Sit facing the radio direction finder (mouse to look around, Esc to stand up)";
        }

        private static bool IsRussian(string language)
        {
            return string.Equals(language, "Russian", StringComparison.OrdinalIgnoreCase);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
