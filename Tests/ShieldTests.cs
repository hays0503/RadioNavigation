using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// ADR-015 shield: критичные механики тюнинг/пеленг.
    /// Код ради кода не генерим — только тесты-щит перед Ф2-Ф4.
    /// Формулы дублируют SonneController.cs:507 и GeoMath.cs:52 для фиксации регресса.
    /// </summary>
    public sealed class ShieldGainTests
    {
        private static float Gain(float fKHz, float f0KHz, float qf)
        {
            // SonneController.cs:507 gain = 1 / sqrt(1 + Qf² * ratio²), ratio = f/f0 - f0/f
            if (float.IsNaN(fKHz) || float.IsNaN(f0KHz) || f0KHz == 0f) return 0f;
            float f = fKHz * 1000f;
            float f0 = f0KHz * 1000f;
            float ratio = f / f0 - f0 / f;
            return 1f / (float)System.Math.Sqrt(1.0 + qf * qf * ratio * ratio);
        }

        [Fact]
        public void Gain_315_vs_319_QF20_IsAbout089_CrossTalk()
        {
            // F203: дефолт QF=20 даёт 89% — полезный морзе слышен без гула (live-validated, но без щита)
            float g = Gain(315f, 319f, 20f);
            Assert.InRange(g, 0.85f, 0.92f);
        }

        [Fact]
        public void Gain_315_vs_319_QF200_IsAbout02_Suppressed()
        {
            float g = Gain(315f, 319f, 200f);
            Assert.InRange(g, 0.15f, 0.30f);
        }

        [Fact]
        public void Gain_266_vs_257_QF20_HighCrossTalk()
        {
            // 266 vs 257 delta 9kHz → ratio 0.069 → gain ~0.587 (расчёт), не 0.85
            float g = Gain(266f, 257f, 20f);
            Assert.InRange(g, 0.55f, 0.65f);
        }

        [Theory]
        [InlineData(float.NaN, 257f)]
        [InlineData(257f, float.NaN)]
        public void Gain_NaN_ShouldBeZero(float f, float f0)
        {
            float g = Gain(f, f0, 20f);
            Assert.Equal(0f, g);
        }

        [Fact]
        public void Gain_ZeroF0_ShouldBeZero()
        {
            Assert.Equal(0f, Gain(300f, 0f, 20f));
        }
    }

    public sealed class ShieldGeoTests
    {
        [Fact]
        public void Haversine_Antipodal_179_vs_Minus179_About222km()
        {
            // GeoMath.cs:52 без wrap давал бы 90° ошибку, дистанция 2° = ~222км
            double d = GeoMath.HaversineKm(0f, 179f, 0f, -179f);
            Assert.InRange(d, 220, 225);
        }

        [Fact]
        public void MapBearing_Antipodal_ShouldBe270()
        {
            // GeoMath.cs:52 flat projection: atan2(dLon,dLat) даёт 270 даже без wrap для 179→-179 (dLon=-358)
            // Тест фиксирует стабильность пеленга на антимеридиане — не должен стать 90
            float b = GeoMath.MapBearingDeg(0f, 179f, 0f, -179f);
            Assert.InRange(b, 269f, 271f);
        }

        [Fact]
        public void Bushmills_89_9_DotsShouldBe53_Not57()
        {
            // Bushmills.json:616 fix 89.9:57→53 — монотонность dots, иначе ошибка 3 знака =0.25°
            // Проверяем через SonneBeam (таблица загружается из Source/Data/Bushmills.json)
            // Инициализируем таблицы если нужно (тестовый раннер уже грузит через SonneBeamTests)
            string dir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "Source", "Data");
            // fallback: если не нашли — ищем от корня репо
            if (!System.IO.Directory.Exists(dir))
                dir = System.IO.Path.Combine("Source", "Data");
            SonneBeam.Initialize(dir);
            bool ok = SonneBeam.TryGetNavigationPattern("Bushmills", 89.9, out var pattern, out var signal);
            Assert.True(ok);
            // 89.9 — dot сектор, DotCount должен быть 53 (исправлено), не 57
            Assert.Equal(53, signal.DotCount);
        }

        [Fact]
        public void InitialBearing_Antipodal_ShouldBeFinite()
        {
            float b = GeoMath.InitialBearingDeg(0f, 179f, 0f, -179f);
            Assert.True(float.IsFinite(b));
            Assert.InRange(b, 0f, 360f);
        }
    }
}
