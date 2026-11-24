using System;
using System.Reflection;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class DspPipelineTests
    {
        // F141: switching Frequency must not produce a phase discontinuity (click).
        [Fact]
        public void CarrierGenerator_FrequencyChange_NoPhaseJump()
        {
            var c = new CarrierGenerator(44100f);
            c.Frequency = 200f;
            for (int i = 0; i < 20000; i++)
                c.NextSample();
            float prev = c.NextSample();
            c.Frequency = 201f;
            for (int i = 1; i < 200; i++)
            {
                float cur = c.NextSample();
                Assert.True(Math.Abs(cur - prev) < 0.5f, "sample jump at " + i + ": " + (cur - prev));
                prev = cur;
            }
        }

        [Fact]
        public void CarrierGenerator_FrequencyClampedAtZero()
        {
            var c = new CarrierGenerator(44100f);
            c.Frequency = -50f;
            Assert.Equal(0f, c.Frequency);
        }

        [Fact]
        public void CarrierGenerator_Reset_RestartsPhase()
        {
            var c = new CarrierGenerator(44100f);
            c.Frequency = 440f;
            for (int i = 0; i < 100; i++)
                c.NextSample();

            c.Reset();

            Assert.Equal(0f, c.NextSample());
        }

        [Fact]
        public void ReceiverDsp_FilterCutoff_RoundTrips()
        {
            var d = new ReceiverDsp(44100f);
            Assert.Equal(800f, d.FilterCutoff);
            d.FilterCutoff = 400f;
            Assert.Equal(400f, d.FilterCutoff);
        }

        // F142: whistle added on top of tanh saturation must not leave [-1, 1].
        [Fact]
        public void ReceiverDsp_Whistle_OutputClamped()
        {
            var d = new ReceiverDsp(44100f);
            d.WhistleLevel = 1f;
            d.SaturationDrive = 10f;
            d.AgcAttackRate = 1f;
            float max = 0f;
            for (int i = 0; i < 2000; i++)
            {
                float sample = d.Process(1f, 44100f);
                max = Math.Max(max, Math.Abs(sample));
            }
            Assert.True(max <= 1.0001f, "max=" + max);
        }

        // F143: worst-case noise must not push the output outside [-1, 1].
        [Fact]
        public void Atmosphere_WorstCaseNoise_OutputClamped()
        {
            var a = new Atmosphere(12345);
            a.NoiseFloor = 0.002f;
            a.ImpulseProbability = 1f;
            a.ImpulseAmplitude = 0.3f;
            a.QsbDepth = 0f;
            float max = 0f;
            for (int i = 0; i < 20000; i++)
            {
                float sample = a.Apply(1f, 44100f);
                max = Math.Max(max, Math.Abs(sample));
            }
            Assert.True(max <= 1.0001f, "max=" + max);
        }

        // Audio-thread visibility: tuning written on the main thread and read
        // in the audio callback must be volatile, or the JIT may hoist the read
        // out of the sample loop and the operator never hears the change.
        [Fact]
        public void Atmosphere_TuningFields_AreVolatile()
        {
            AssertVolatile(typeof(Atmosphere), "noiseFloor");
            AssertVolatile(typeof(Atmosphere), "impulseProbability");
            AssertVolatile(typeof(ReceiverDsp), "whistleLevel");
        }

        private static void AssertVolatile(Type type, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(field != null, type.Name + "." + fieldName + " field not found");
            Type[] mods = field.GetRequiredCustomModifiers();
            bool isVolatile = false;
            for (int i = 0; i < mods.Length; i++)
            {
                if (mods[i].FullName == "System.Runtime.CompilerServices.IsVolatile")
                    isVolatile = true;
            }
            Assert.True(isVolatile, type.Name + "." + fieldName + " must be volatile");
        }

        [Fact]
        public void CarrierGenerator_InvalidSampleRateAndNaNFrequency_NeverEmitsNaN()
        {
            var zeroRate = new CarrierGenerator(0f);
            zeroRate.Frequency = float.NaN;
            zeroRate.Frequency = 500f;
            float s1 = zeroRate.NextSample();
            Assert.False(float.IsNaN(s1) || float.IsInfinity(s1));

            var c = new CarrierGenerator(44100f);
            c.Frequency = 500f;
            c.NextSample();
            c.Frequency = float.NaN;
            float s2 = c.NextSample();
            Assert.False(float.IsNaN(s2) || float.IsInfinity(s2));
            c.Reset();
            Assert.Equal(0f, c.NextSample());

            var lowRate = new CarrierGenerator(8000f);
            lowRate.Frequency = 12000f;
            for (int i = 0; i < 40; i++)
            {
                float sample = lowRate.NextSample();
                Assert.False(float.IsNaN(sample) || float.IsInfinity(sample));
                Assert.InRange(sample, -1f, 1f);
            }
        }

        [Fact]
        public void AtmosphereAndReceiverDsp_RejectNonFiniteAndNegativeCutoff()
        {
            var atmo = new Atmosphere(7);
            float a1 = atmo.Apply(float.NaN, 0f);
            Assert.False(float.IsNaN(a1) || float.IsInfinity(a1));

            var dsp = new ReceiverDsp(0f);
            dsp.FilterCutoff = -5000f;
            Assert.True(dsp.FilterCutoff >= 0f);
            dsp.FilterCutoff = float.NaN;
            dsp.WhistleLevel = 1f;
            Assert.Equal(0f, dsp.Process(float.NaN, 0f));
            float outVal = dsp.Process(0.5f, 0f);
            Assert.InRange(outVal, -1f, 1f);
        }

        [Fact]
        public void Atmosphere_QsbEnvelopeIsContinuousAcrossOneSecondBoundary()
        {
            var atmo = new Atmosphere(1);
            atmo.NoiseFloor = 0f;
            atmo.ImpulseProbability = 0f;
            atmo.QsbDepth = 0.75f;
            atmo.QsbFrequency = 0.15f;

            float at44099 = 0f, at44100 = 0f;
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 44100 * 7; i++)
            {
                float cur = atmo.Apply(1f, 44100f);
                if (i == 44099) at44099 = cur;
                if (i == 44100) at44100 = cur;
                if (cur < min) min = cur;
                if (cur > max) max = cur;
            }
            float stepAt1s = Math.Abs(at44100 - at44099);
            Assert.True(stepAt1s < 1e-3f, "QSB envelope jumped across 1s boundary by " + stepAt1s);
            Assert.True(min < 0.27f, "Expected QSB to reach full trough (~0.25), got min=" + min);
            Assert.True(max > 0.99f, "Expected QSB to reach peak (~1.0), got max=" + max);
        }

        [Fact]
        public void ReceiverDsp_RecoversFromNonFiniteFilterState()
        {
            var dsp = new ReceiverDsp(44100f);
            FieldInfo prevField = typeof(ReceiverDsp).GetField("prevFilter", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(prevField);
            prevField.SetValue(dsp, float.NaN);

            float recovery = 0f;
            for (int i = 0; i < 10; i++)
                recovery = dsp.Process(0.5f, 44100f);

            Assert.False(float.IsNaN(recovery) || float.IsInfinity(recovery));
            Assert.True(recovery > 0.05f, "Expected filter to recover from NaN, got " + recovery);
        }

        [Fact]
        public void StationState_GainField_IsVolatile()
        {
            FieldInfo field = typeof(StationState).GetField("Gain", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(field);
            Type[] mods = field.GetRequiredCustomModifiers();
            bool isVolatile = false;
            for (int i = 0; i < mods.Length; i++)
            {
                if (mods[i].FullName == "System.Runtime.CompilerServices.IsVolatile")
                    isVolatile = true;
            }
            Assert.True(isVolatile, "StationState.Gain must be volatile across main/audio threads");
        }
    }
}
