using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>F VIII p.72 (PDF p.98): signed TOTAL lower-limb correction.
    /// Tenths of an arcminute transcribed from the user's scanned original.
    /// It already includes SD, dip and refraction. No extrapolation.</summary>
    public static class SolarHeightReduction
    {
        private static readonly double[] altitudes =
        {
            3, 3 + 1.0/3, 3 + 2.0/3, 4, 4 + 1.0/3, 4 + 2.0/3,
            5, 5 + 1.0/3, 5 + 2.0/3, 6, 6 + 1.0/3, 6 + 2.0/3,
            7, 7 + 1.0/3, 7 + 2.0/3, 8, 8 + 1.0/3, 8 + 2.0/3,
            9, 9 + 1.0/3, 9 + 2.0/3, 10, 10 + 1.0/3, 10 + 2.0/3,
            11, 11.5, 12, 12.5, 13, 13.5, 14, 15, 16, 17, 18, 19,
            20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 45, 50, 55, 60, 70, 80, 90
        };

        private static readonly int[] monthly = { 3, 2, 1, 0, -2, -2, -2, -2, -1, 1, 2, 3 };
        private static readonly int[,] tenths =
        {
            {18,-1,-9,-15,-20,-24,-28,-32,-36,-39,-42,-45,-48,-50,-52},
            {28,9,1,-5,-9,-14,-18,-22,-26,-29,-32,-35,-38,-40,-42},
            {36,18,10,4,0,-5,-9,-13,-17,-20,-23,-26,-28,-30,-33}, // printed -0.0 at eye 4 m
            {44,26,18,12,7,3,-1,-5,-8,-11,-14,-17,-20,-22,-25},
            {51,32,25,19,14,10,6,2,-1,-4,-7,-10,-13,-15,-18},
            {57,38,31,25,20,16,12,9,5,2,-1,-4,-6,-9,-11},
            {63,44,37,31,26,22,18,15,11,8,5,2,0,-3,-5},
            {68,50,42,36,31,27,23,20,17,13,10,7,5,2,0},
            {73,54,47,41,36,32,28,25,22,18,15,12,10,7,5},
            {77,58,51,46,41,37,33,29,26,23,19,17,14,11,9},
            {81,62,55,50,45,41,37,33,30,27,23,21,18,15,13},
            {85,66,59,53,48,44,40,36,33,30,27,24,22,19,17},
            {88,69,62,56,51,47,43,40,36,33,30,28,25,23,20},
            {91,72,65,59,54,50,46,43,40,37,34,31,28,26,23},
            {94,75,68,62,57,53,49,46,43,39,36,34,31,28,26},
            {96,78,70,65,60,56,52,48,45,42,39,36,34,31,29},
            {99,80,73,67,63,58,54,51,48,45,41,39,36,33,31},
            {101,82,75,70,65,61,57,53,50,47,44,41,38,36,33},
            {103,84,77,72,67,63,59,55,52,49,46,43,40,38,35},
            {105,86,79,74,69,65,61,57,54,51,48,45,42,40,37},
            {107,88,81,76,71,67,63,59,56,53,50,47,44,42,39},
            {109,90,83,77,72,68,64,61,57,54,51,49,46,44,41},
            {110,92,85,79,74,70,66,62,59,56,53,50,48,45,43},
            {112,93,86,81,76,72,68,64,61,58,55,52,49,47,45},
            {113,95,88,82,77,73,69,66,62,59,56,54,51,49,46},
            {115,97,90,84,79,75,71,68,64,61,58,56,53,51,48},
            {117,99,92,86,81,77,73,70,66,63,60,58,55,53,50},
            {119,100,93,88,83,79,75,71,68,65,62,59,57,54,52},
            {121,102,95,89,84,80,77,73,70,67,64,61,58,56,54},
            {122,104,96,91,86,82,78,75,71,68,65,63,60,58,55},
            {124,105,98,92,88,84,80,76,73,69,67,64,61,59,56},
            {126,108,101,95,90,86,82,79,75,72,69,67,64,61,59},
            {128,110,103,97,92,88,84,81,78,75,72,69,66,64,61},
            {130,112,105,99,94,90,86,83,80,77,74,71,68,66,63},
            {132,114,107,101,96,92,88,85,81,78,75,73,70,68,65},
            {134,116,108,103,98,94,90,86,83,80,77,74,72,69,67},
            {135,117,110,104,99,95,91,88,84,81,78,76,73,71,68},
            {138,120,112,107,102,98,94,90,87,84,81,78,76,73,71},
            {140,122,114,109,104,100,96,92,89,86,83,80,78,75,73},
            {142,124,116,111,106,102,98,94,91,88,85,82,80,78,75},
            {143,125,118,112,108,104,100,96,93,90,87,84,81,79,77},
            {145,127,119,114,109,105,101,97,94,91,88,85,83,81,78},
            {146,128,121,115,110,106,102,99,95,92,89,87,84,82,79},
            {147,129,122,116,111,107,103,100,96,93,90,88,85,83,81},
            {148,130,123,117,112,108,104,101,97,94,91,89,86,84,82},
            {149,131,124,118,113,109,105,102,98,95,92,90,87,85,83},
            {150,132,125,119,114,110,106,103,99,96,93,91,88,86,84},
            {151,134,126,121,116,112,108,104,101,98,95,92,90,87,85},
            {153,135,127,122,117,113,109,106,102,99,96,94,91,89,86},
            {154,136,129,123,118,114,111,107,104,101,98,95,92,90,88},
            {155,137,130,124,120,116,112,108,105,102,99,96,93,91,89}, // printed 12.0 at eye 4 m
            {157,139,132,126,122,118,114,110,107,104,101,98,95,93,91},
            {159,141,134,128,123,119,115,112,108,105,102,100,97,95,92},
            {160,142,135,129,125,121,117,113,110,107,104,101,98,96,94}
        };

        public static bool TryCorrection(double hDeg, double eyeHeightM, int month,
            out double baseArcMin, out double monthArcMin)
        {
            baseArcMin = 0;
            monthArcMin = 0;
            if (!Finite(hDeg) || !Finite(eyeHeightM) || hDeg < 3 || hDeg > 90
                || eyeHeightM < 0 || eyeHeightM > 14 || month < 1 || month > 12)
                return false;
            int row = 0;
            while (row < altitudes.Length - 2 && altitudes[row + 1] < hDeg)
                row++;
            int col = Math.Min(13, (int)eyeHeightM);
            double w = eyeHeightM - col;
            double low = tenths[row, col] + w * (tenths[row, col + 1] - tenths[row, col]);
            double high = tenths[row + 1, col] + w * (tenths[row + 1, col + 1] - tenths[row + 1, col]);
            double t = (hDeg - altitudes[row]) / (altitudes[row + 1] - altitudes[row]);
            baseArcMin = (low + t * (high - low)) / 10;
            monthArcMin = monthly[month - 1] / 10.0;
            return true;
        }

        public static bool TryReduce(double rawHsDeg, double indexArcMin, double eyeHeightM,
            int month, SunSightLimb limb, out double hoDeg, out double correctionArcMin)
        {
            hoDeg = 0;
            correctionArcMin = 0;
            if (limb != SunSightLimb.LowerLimb || !Finite(rawHsDeg)
                || !Finite(indexArcMin) || Math.Abs(indexArcMin) > 5)
                return false;
            double h = rawHsDeg + indexArcMin / 60;
            double b, m;
            if (!TryCorrection(h, eyeHeightM, month, out b, out m))
                return false;
            correctionArcMin = b + m;
            hoDeg = h + correctionArcMin / 60;
            return true;
        }

        /// <summary>Controlled table-compatible optics, NOT a fit to a sight:
        /// invert the same monotone F VIII relation to draw the lower limb.
        /// The physical SD is added only to position the disc's centre.</summary>
        public static bool TryLowerLimb(double centreHoDeg, double eyeHeightM, int month,
            out double hDeg)
        {
            hDeg = 0;
            double lowHo, highHo, correction;
            if (!Finite(centreHoDeg)
                || !TryReduce(3, 0, eyeHeightM, month, SunSightLimb.LowerLimb, out lowHo, out correction)
                || !TryReduce(90, 0, eyeHeightM, month, SunSightLimb.LowerLimb, out highHo, out correction)
                || centreHoDeg < lowHo || centreHoDeg > highHo)
                return false;
            double low = 3, high = 90;
            for (int i = 0; i < 48; i++)
            {
                double mid = (low + high) / 2;
                double ho;
                TryReduce(mid, 0, eyeHeightM, month, SunSightLimb.LowerLimb, out ho, out correction);
                if (ho < centreHoDeg) low = mid;
                else high = mid;
            }
            hDeg = (low + high) / 2;
            return true;
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
