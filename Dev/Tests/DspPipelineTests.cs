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
    }
}
