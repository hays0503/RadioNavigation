using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class SightBookTextTests
    {
        private static readonly DateTime Time = new DateTime(1939, 9, 2, 10, 3, 7, DateTimeKind.Utc);

        private sealed class Position : IPlayerPositionProvider
        {
            public bool TryGetPosition(out float lat, out float lon)
            {
                lat = 55;
                lon = 6.2025f;
                return true;
            }
        }

        private static SextantObservation Sun()
        {
            Assert.True(Sh5SunAlmanac.Initialize(Path.Combine(Directory.GetCurrentDirectory(), "Source", "Data")));
            var disc = Disc(Time);
            SextantObservation obs;
            Assert.True(Sh5SolarNavigation.TryObservation(Time, 39 + 13.4 / 60, 0, 55, 6.2025, disc, out obs));
            return obs;
        }

        private static SunDiscSighting Disc(DateTime time)
        {
            return new SunDiscSighting
            {
                TableRequested = true, TableCompatible = true, ConfiguredUtc = time,
                ObserverLatDeg = 55, ObserverLonDeg = 6.2025, EyeHeightM = 5.5,
                ObservedDiameterDeg = 0.53, Limb = SunSightLimb.LowerLimb,
                HorizonAltitudeDeg = -0.06
            };
        }

        [Fact]
        public void SightBook_SolarExampleUsesRealCellsAndAlmanacRatherThanChatConstants()
        {
            SextantObservation obs = Sun();
            SolarCorrectionDetails d;
            Assert.True(SolarHeightReduction.TryDetails(obs.IndexCorrectedHDeg, 5.5, 9, out d));
            Assert.Equal(38, d.LowAltitudeDeg);
            Assert.Equal(40, d.HighAltitudeDeg);
            Assert.Equal(10.9, d.LowLeftArcMin);
            Assert.Equal(10.5, d.LowRightArcMin);
            Assert.Equal(11.0, d.HighLeftArcMin);
            Assert.Equal(10.6, d.HighRightArcMin);
            Assert.Equal(0.5, d.EyeWeight);
            Assert.Equal(10.761166666666666, d.BaseArcMin, 10);
            Assert.Equal(-0.1, d.MonthArcMin);
            Assert.Equal(337, obs.LhaDeg, 10);
            // Independently evaluated spherical formula, NOT the earlier chat's
            // 149.86/149.9 azimuth and rounded 39°42.25′ height.
            Assert.Equal(39.704547918709515, obs.HcDeg, 10);
            Assert.Equal(149.81869927153878, obs.AzDeg, 10);
            Assert.Equal(-18.211708455903874, obs.InterceptNm, 9);
            string text = SightBookText.Compose(new SextantSaveSnapshot { Observation1 = obs }, null);
            Assert.Contains("глаз(высота глаза) = 5.500000 м; месяц = 9", text);
            Assert.Contains("b(базовая поправка) = 10.7612′", text);
            Assert.Contains("F VIII(сохранённая полная поправка) = 10.6612′", text);
            Assert.Contains("GHA₀(угол строки) = 300°01.100′", text);
            Assert.Contains("d(изменение за четыре часа) = -3.60′", text);
            Assert.Contains("LHA(местный часовой угол) = 337°00.000′", text);
            Assert.Contains("t(меньший часовой угол) = 23°00.000′", text);
            Assert.Contains("Готовые минуты ещё раз на 60 не умножай", text);
            Assert.DoesNotContain("149.8 — округление", text);
            Assert.DoesNotContain("9.98881", text);
            Assert.Contains("F I/F II не оцифрованы", text);
            WriteExample("sight-book-sun-example", "# OFFLINE SOLAR EXAMPLE A — not a live game observation\n" + text);
        }

        [Theory]
        [InlineData(3, 0, 1)]
        [InlineData(90, 14, 12)]
        [InlineData(39.22333333333, 5.5, 9)]
        [InlineData(10.33333333333, 1, 4)]
        public void SightBook_DetailApiHasExactlyTheSameCorrection(double altitude, double eye, int month)
        {
            SolarCorrectionDetails d;
            double b, m;
            Assert.True(SolarHeightReduction.TryDetails(altitude, eye, month, out d));
            Assert.True(SolarHeightReduction.TryCorrection(altitude, eye, month, out b, out m));
            Assert.Equal(b, d.BaseArcMin);
            Assert.Equal(m, d.MonthArcMin);
            Assert.InRange(d.EyeWeight, 0, 1);
            Assert.InRange(d.AltitudeWeight, 0, 1);
        }

        [Fact]
        public void SightBook_AllBodiesHaveExpandedSelfContainedCards()
        {
            SextantObservation solar = Sun();
            var state = new SextantSaveSnapshot
            {
                Observation1 = solar,
                Observation2 = RunningFixCalculator.BuildObservation(Time, 20, 55, 6.2025, CelestialBody.Moon()),
                Observation3 = RunningFixCalculator.BuildObservation(Time, 50, 55, 6.2025, CelestialBody.Star(11767, "Polaris"))
            };
            string text = SightBookText.Compose(state, null);
            Assert.Equal(3, Count(text, "[4b] КАКОЙ МЕСТНЫЙ ЧАСОВОЙ УГОЛ?"));
            Assert.Equal(3, Count(text, "[7b] ГДЕ ПРОВЕСТИ ЛИНИЮ ПОЛОЖЕНИЯ?"));
            Assert.Equal(2, Count(text, "Hs(отсчёт барабана, до поправок) = нет данных"));
            Assert.Contains("топоцентрически", text);
            Assert.Contains("каталог эпохи 1926.0", text);
            Assert.DoesNotContain("как у Луны", text);
            Assert.DoesNotContain("[Hs]", text);
            Assert.Equal(Count(text, "=====Начало====="), Count(text, "=====Конец====="));
            Assert.Equal(6, Count(text, "=====Начало====="));
        }

        [Theory]
        [InlineData(181, 179)]
        [InlineData(61, 61)]
        [InlineData(337, 23)]
        [InlineData(180, 180)]
        public void SightBook_HourAngleBranchesUseRecordedAngle(double lha, double smaller)
        {
            SextantObservation obs = Sun();
            obs.LhaDeg = lha;
            string text = SightBookText.Compose(new SextantSaveSnapshot { Observation1 = obs }, null);
            Assert.Contains("t(меньший часовой угол) = " + SightBookText.Angle(smaller), text);
        }

        [Theory]
        [InlineData(-2, 210)]
        [InlineData(2, 30)]
        [InlineData(0, 30)]
        public void SightBook_InterceptDirectionMatchesActualLop(double intercept, double direction)
        {
            SextantObservation obs = Sun();
            obs.AzDeg = 30;
            obs.InterceptNm = intercept;
            obs.HoDeg = obs.HcDeg + intercept / 60;
            LineOfPosition lop = RunningFixCalculator.BuildLop(obs);
            Assert.Equal(Math.Sign(intercept), Math.Sign(lop.TangentLatDeg - obs.DrLatDeg));
            string text = SightBookText.Compose(new SextantSaveSnapshot { Observation1 = obs }, null);
            Assert.Contains("Направление смещения = " + SightBookText.Number(direction, "F3") + "°", text);
            Assert.Contains(SightBookText.Coordinate(lop.TangentLatDeg, true), text);
        }

        [Fact]
        public void SightBook_ActualCaptureExportsFirstSecondThirdAndRejectedNeverWrites()
        {
            Sun();
            DateTime old = GameTimeResolver.Now;
            double oldIndex = IndexErrorCorrection.Load();
            var books = new List<string>();
            var worksheet = new SextantWorksheet(new Position(), books.Add);
            try
            {
                IndexErrorCorrection.Save(0);
                worksheet.ResetSeries();
                Assert.Empty(books);
                Assert.False(worksheet.CaptureObservation(CelestialBody.None(), 30));
                Assert.Empty(books);
                for (int i = 0; i < 3; i++)
                {
                    DateTime time = Time.AddMinutes(i * 45);
                    GameTimeResolver.Now = time;
                    Assert.True(worksheet.CaptureObservation(CelestialBody.Sun(), 39 + i * 2, 0, 1, 0, Disc(time)));
                    Assert.Equal(i + 1, books.Count);
                    Assert.Contains("НАБЛЮДЕНИЕ №" + (i + 1), books[i]);
                }
                Assert.True(worksheet.FixPublished);
                Assert.Contains("ЗАМОРОЖЕННЫЙ РЕЗУЛЬТАТ", books[2]);
                Assert.Contains("момент FIX пары не возвращаются", books[2]);
                Assert.DoesNotContain("[F3a]", books[2]);
                Assert.Contains("[F4]", books[2]);
                worksheet.ReportBlockedSight(SightBlock.Low);
                Assert.False(worksheet.CaptureObservation(CelestialBody.Sun(), 30, 0, 1, 0, new SunDiscSighting()));
                worksheet.UpdatePanel(10);
                worksheet.InvalidateCard();
                Assert.Equal(3, books.Count);
                worksheet.ResetSeries();
                Assert.Equal(4, books.Count);
                Assert.Contains("НОВАЯ СЕРИЯ", books[3]);
                Assert.DoesNotContain("НАБЛЮДЕНИЕ №", books[3]);
            }
            finally
            {
                GameTimeResolver.Now = old;
                IndexErrorCorrection.Save(oldIndex);
            }
        }

        [Fact]
        public void SightBook_RestoreUsesFrozenFixNotCurrentSpeedOrClock()
        {
            SextantObservation obs = Sun();
            var state = new SextantSaveSnapshot
            {
                Observation1 = obs, Observation2 = obs, FixPublished = true,
                FixLatDeg = 48.5083333, FixLonDeg = 1.2683333, FixErrorNm = 1.8,
                SectionText = "FROZEN SECTION 123.456", Course = new CourseSamplerSnapshot { SpeedKn = 20, CourseDeg = 270 }
            };
            string exported = null;
            var worksheet = new SextantWorksheet(new Position(), text => exported = text);
            worksheet.RestoreState(state);
            Assert.Contains("FROZEN SECTION 123.456", exported);
            Assert.Contains("след решения не сохранён", exported);
            Assert.DoesNotContain("[F1a]", exported);
            string frozen = exported;
            worksheet.UpdateCourseSampler(100);
            worksheet.UpdatePanel(100);
            Assert.Equal(frozen, exported);
            Assert.Equal(state.FixLatDeg, worksheet.FixLatDeg);
            worksheet.RestoreState(new SextantSaveSnapshot());
            Assert.Contains("НОВАЯ СЕРИЯ", exported);
            Assert.DoesNotContain("FROZEN SECTION", exported);
        }

        [Fact]
        public void SightBook_EmptySaveClearsAnExportLeftByAPreviousGameProcess()
        {
            string exported = "old process book";
            var worksheet = new SextantWorksheet(new Position(), text => exported = text, true);
            worksheet.RestoreState(new SextantSaveSnapshot());
            Assert.Contains("НОВАЯ СЕРИЯ", exported);
            Assert.DoesNotContain("old process book", exported);
        }

        [Fact]
        public void SightBook_TriangleMetricsAreFromOneRealSolverResult()
        {
            DateTime time = new DateTime(1942, 6, 15, 22, 0, 0, DateTimeKind.Utc);
            var calc = new RunningFixCalculator();
            calc.Record(Star(CelestialBody.Star(91262, "Vega"), time, 0));
            calc.Record(Star(CelestialBody.Star(69673, "Arcturus"), time, 0));
            calc.Record(Star(CelestialBody.Star(102098, "Deneb"), time, 0.5));
            CockedHatResult hat;
            Assert.True(calc.CalculateTriangleFix(0, 0, 2, out hat));
            Assert.True(hat.UsedBestPairFallback);
            Assert.True(hat.RadiusNm > 6);
            double p = (hat.SideANm + hat.SideBNm + hat.SideCNm) / 2;
            Assert.Equal(Math.Sqrt(p * (p - hat.SideANm) * (p - hat.SideBNm) * (p - hat.SideCNm)), hat.AreaNm2, 9);
            string steps = SightBookFixText.ComposeSolution(true, hat);
            double ab = RunningFixCalculator.DistanceNm(hat.VertexALatDeg, hat.VertexALonDeg, hat.VertexBLatDeg, hat.VertexBLonDeg);
            double bc = RunningFixCalculator.DistanceNm(hat.VertexBLatDeg, hat.VertexBLonDeg, hat.VertexCLatDeg, hat.VertexCLonDeg);
            double ca = RunningFixCalculator.DistanceNm(hat.VertexCLatDeg, hat.VertexCLonDeg, hat.VertexALatDeg, hat.VertexALonDeg);
            Assert.Equal(2, Count(steps, "AB = " + SightBookText.Number(ab, "F6")));
            Assert.Equal(2, Count(steps, "BC = " + SightBookText.Number(bc, "F6")));
            Assert.Equal(2, Count(steps, "CA = " + SightBookText.Number(ca, "F6")));
            Assert.Contains("Площадь = " + SightBookText.Number(hat.AreaNm2, "F6"), steps);
            Assert.Contains("R(максимальное расстояние от центроида до вершины) = " + SightBookText.Number(hat.RadiusNm, "F6"), steps);
            Assert.Contains("Центр не опубликован; использована пара", steps);
            var state = new SextantSaveSnapshot
            {
                Observation1 = calc.Observation1, Observation2 = calc.Observation2, Observation3 = calc.Observation3,
                FixPublished = true, FixLatDeg = hat.FixLatDeg, FixLonDeg = hat.FixLonDeg
            };
            WriteExample("sight-book-example", "# OFFLINE SYNTHETIC FIX B — not a live game series\n" + SightBookText.Compose(state, steps));
        }

        private static void WriteExample(string folder, string text)
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "tests", folder);
            string path, error;
            Assert.True(SightBookFileWriter.TryWrite(directory, text, out path, out error), error);
        }

        [Fact]
        public void SightBook_FallbackDoesNotExplainPairFixByTransferToThirdTime()
        {
            var calc = new RunningFixCalculator();
            SextantObservation a = Star(CelestialBody.Star(91262, "Vega"), Time, 0);
            SextantObservation b = Star(CelestialBody.Star(69673, "Arcturus"), Time.AddMinutes(5), 0);
            SextantObservation c = Star(CelestialBody.Star(102098, "Deneb"), Time.AddMinutes(10), 0);
            // Parallel first/third lines force fallback; the widest crossing
            // ties, so the actual solver selects 1/2 (target time = sight 2).
            a.AzDeg = 0; b.AzDeg = 90; c.AzDeg = 0;
            calc.Record(a); calc.Record(b); calc.Record(c);
            CockedHatResult hat;
            Assert.True(calc.CalculateTriangleFix(90, 12, 2, out hat));
            Assert.True(hat.UsedBestPairFallback);
            double pairLat, pairLon;
            Assert.True(calc.CalculateFix(90, 12, out pairLat, out pairLon));
            Assert.Equal(pairLat, hat.FixLatDeg, 10);
            Assert.Equal(pairLon, hat.FixLonDeg, 10);
            string book = null;
            var worksheet = new SextantWorksheet(new Position(), text => book = text);
            worksheet.RestoreState(new SextantSaveSnapshot
            {
                Observation1 = a, Observation2 = b, Observation3 = c,
                Course = new CourseSamplerSnapshot { CourseDeg = 90, SpeedKn = 12 }
            });
            typeof(SextantWorksheet).GetMethod("RecomputeFixSnapshot", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(worksheet, null);
            typeof(SextantWorksheet).GetMethod("ExportSightBook", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(worksheet, null);
            Assert.True(worksheet.FixPublished);
            Assert.Equal(pairLat, worksheet.FixLatDeg, 10);
            Assert.Contains("момент FIX пары не возвращаются", book);
            Assert.DoesNotContain("Общий момент линий", book);
            Assert.DoesNotContain("[F1a]", book);
        }

        [Fact]
        public void SightBook_CentroidPublicationStillIncludesAllThreeTransfers()
        {
            DateTime time = new DateTime(1942, 6, 15, 22, 0, 0, DateTimeKind.Utc);
            var a = Star(CelestialBody.Star(91262, "Vega"), time, 0);
            var b = Star(CelestialBody.Star(69673, "Arcturus"), time, 0);
            var c = Star(CelestialBody.Star(102098, "Deneb"), time, 0);
            var calc = new RunningFixCalculator();
            calc.Record(a); calc.Record(b); calc.Record(c);
            CockedHatResult hat;
            Assert.True(calc.CalculateTriangleFix(0, 0, 2, out hat));
            Assert.False(hat.UsedBestPairFallback);
            string book = null;
            var worksheet = new SextantWorksheet(new Position(), text => book = text);
            worksheet.RestoreState(new SextantSaveSnapshot { Observation1 = a, Observation2 = b, Observation3 = c });
            typeof(SextantWorksheet).GetMethod("RecomputeFixSnapshot", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(worksheet, null);
            typeof(SextantWorksheet).GetMethod("ExportSightBook", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(worksheet, null);
            Assert.True(worksheet.FixPublished);
            Assert.Contains("Опубликован центроид", book);
            Assert.Contains("[F1a]", book);
            Assert.Contains("[F2a]", book);
            Assert.Contains("[F3a]", book);
        }

        [Fact]
        public void SightBook_PrintedSolarInputsPreserveFractionalTimeAndEyeHeight()
        {
            Sun();
            DateTime time = Time.AddTicks(9123456);
            SunDiscSighting disc = Disc(time);
            disc.EyeHeightM = 5.123456;
            SextantObservation obs;
            Assert.True(Sh5SolarNavigation.TryObservation(time, 39 + 13.4 / 60, 0, 55, 6.2025, disc, out obs));
            string book = SightBookText.Compose(new SextantSaveSnapshot { Observation1 = obs }, null);
            string ut = time.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            Assert.Equal(2, Count(book, "UT(всемирное время) = " + ut));
            Assert.Equal(2, Count(book, "глаз(высота глаза) = 5.123456 м"));
            DateTime printedUtc = DateTime.ParseExact(ut, "yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            double gha, dec;
            Assert.True(Sh5SunAlmanac.TryPlace(printedUtc, out gha, out dec));
            Assert.Equal(obs.GhaBodyDeg, gha, 10);
            Assert.Contains("w(доля шага по высоте глаза) = " + (5.123456 - 5).ToString("F4", CultureInfo.InvariantCulture), book);
        }

        private static SextantObservation Star(CelestialBody body, DateTime time, double error)
        {
            var truth = RunningFixCalculator.BuildObservation(time, 0, 54, 18, body);
            return RunningFixCalculator.BuildObservation(time, truth.HcDeg + error, 54 + 5.0 / 60, 18 + 10.0 / 60, body);
        }

        [Fact]
        public void SightBook_DegenerateTriangleDoesNotInventZeroMetrics()
        {
            string text = SightBookFixText.ComposeSolution(true, new CockedHatResult { Ok = true, UsedBestPairFallback = true });
            Assert.Contains("числовых метрик нет", text);
            Assert.DoesNotContain("Площадь =", text);
        }

        [Fact]
        public void SightBook_TransferUsesActualSolverTargetTimeAndNotAnUnusedThirdSight()
        {
            var calc = new RunningFixCalculator();
            var body = CelestialBody.Star(91262, "Vega");
            calc.Record(Star(body, Time, 0));
            calc.Record(Star(body, Time.AddMinutes(5), 0));
            calc.Record(Star(body, Time.AddMinutes(10), 0));
            string three = SightBookFixText.ComposeTransfers(calc, 90, 12, true, true);
            Assert.Contains("S(ход лодки для переноса) = 2.000 морской мили", three);
            Assert.Contains("[F3a]", three);
            string two = SightBookFixText.ComposeTransfers(calc, 90, 12, false, false);
            Assert.Contains("S(ход лодки для переноса) = 1.000 морской мили", two);
            Assert.DoesNotContain("[F3a]", two);
            Assert.Contains("оценка курса/скорости устарела", two);
            Assert.Contains(Time.AddMinutes(5).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), two);
        }

        [Fact]
        public void SightBook_MoonAndStarKeepTheirOwnCalibrationConvention()
        {
            var state = new SextantSaveSnapshot
            {
                CalibrationMinutes = -6.47,
                Observation1 = RunningFixCalculator.BuildObservation(Time, 30, 55, 6, CelestialBody.Moon()),
                Observation2 = RunningFixCalculator.BuildObservation(Time, 30, 55, 6, CelestialBody.Star(11767, "Polaris"))
            };
            string text = SightBookText.Compose(state, null);
            Assert.Contains("Δt(применённая калибровка времени серии) = -6.470 мин", text);
            Assert.Contains("Δt(применённая калибровка времени серии) = 0.000 мин", text);
        }

        [Theory]
        [InlineData((string)null)]
        [InlineData("")]
        public void SightBook_MissingDirectoryReturnsFailure(string directory)
        {
            string path, error;
            Assert.False(SightBookFileWriter.TryWrite(directory, "x", out path, out error));
            Assert.Null(path);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void SightBook_WriterCreatesDirectoryBomCrlfAndOverwrites()
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "tests", "sight-book-" + Guid.NewGuid().ToString("N"));
            string path, error;
            Assert.True(SightBookFileWriter.TryWrite(directory, "a\r\nb\nc\n", out path, out error), error);
            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, new[] { bytes[0], bytes[1], bytes[2] });
            Assert.Equal("a\r\nb\r\nc\r\n", File.ReadAllText(path));
            Assert.True(SightBookFileWriter.TryWrite(directory, "новая серия\n", out path, out error), error);
            Assert.Equal("новая серия\r\n", File.ReadAllText(path));
            File.Delete(path);
            Directory.Delete(directory);
        }

        [Fact]
        public void SightBook_WriterIoFailureDoesNotEscapeSuccessfulCapture()
        {
            Sun();
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "tests");
            Directory.CreateDirectory(directory);
            string blocked = Path.Combine(directory, "sight-book-blocked-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(blocked, "not a directory");
            try
            {
                bool result = true;
                string path = "unexpected", error = null;
                var worksheet = new SextantWorksheet(new Position(), text => result = SightBookFileWriter.TryWrite(blocked, text, out path, out error));
                Assert.True(worksheet.CaptureObservation(CelestialBody.Sun(), 39, 0, 1, 0, Disc(Time)));
                Assert.NotNull(worksheet.Calculator.Observation1);
                Assert.False(result);
                Assert.Null(path);
                Assert.False(string.IsNullOrEmpty(error));
            }
            finally { File.Delete(blocked); }
        }

        [Fact]
        public void SightBook_FormattingIsCultureInvariantAndCarriesMinutes()
        {
            CultureInfo old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
                Assert.Equal("40°00.000′", SightBookText.Angle(39 + 59.9999 / 60));
                Assert.Equal("1.25", SightBookText.Number(1.25));
                string text = SightBookText.Compose(new SextantSaveSnapshot { Observation1 = Sun() }, null);
                Assert.Contains("10.6612′", text);
            }
            finally { CultureInfo.CurrentCulture = old; }
        }

        private static int Count(string text, string token)
        {
            return (text.Length - text.Replace(token, "").Length) / token.Length;
        }
    }
}
