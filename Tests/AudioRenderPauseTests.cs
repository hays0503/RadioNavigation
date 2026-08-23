using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    // Golden render test: drives UpdateStations (exact 50 ms logical ticks) and
    // ProcessAudio fully offline, so scheduler+keyer+carrier chain renders a
    // deterministic signal whose pauses can be measured against the
    // ConsolCycleConfig model without any live recording.
    //
    // Model (Cycle60s): after the last navigation tone the keyer still plays
    // the trailing element silence, then PostNavSilenceMs (3000), then the
    // single Pause tick (50):
    //   dash sector ends on Dots  -> 800 + 3000 + 50 = 3850 ms
    //   dot  sector ends on Dashes-> 400 + 3000 + 50 = 3450 ms
    // Longest ON run must be the equisignal train (4 x EquisignalMs = 4000 ms,
    // Tone state chains without silence between equisignal elements).
    public class AudioRenderPauseTests
    {
        private const int SampleRate = AudioSynthesisEngine.SampleRate;
        private const float TickSeconds = 0.05f;
        private const int SamplesPerTick = (int)(SampleRate * TickSeconds);
        private const float RenderSeconds = 85f;

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Render_Cycle60_PauseAfterNavigation_MatchesModel(bool dashSector)
        {
            double expectedPostNavMs = dashSector ? 800 + 3000 + 50 : 400 + 3000 + 50;

            var engine = new AudioSynthesisEngine();
            engine.Atmosphere.NoiseFloor = 0f;
            engine.Atmosphere.ImpulseProbability = 0f;
            engine.Atmosphere.QsbDepth = 0f;
            engine.Dsp.WhistleLevel = 0f;

            var stations = new StationState[1];
            stations[0].Gain = 1f;
            stations[0].BeatFrequency = 500f;
            stations[0].Config = ConsolCycleConfig.Cycle60s;

            NavigationElementType[] pattern = SonneBeam.BuildPattern(30, dashSector);
            var scheduler = new StationScheduler("TEST")
            {
                Config = ConsolCycleConfig.Cycle60s,
                Navigation = SonneSignalGenerator.ToNavigationPattern(pattern),
                CurrentConsolPattern = pattern
            };

            var schedulers = new[] { scheduler };
            var keyers = new[] { new MorseKeyer() };
            var carriers = new[] { new CarrierGenerator(SampleRate) };

            int totalTicks = (int)(RenderSeconds / TickSeconds);
            var rendered = new float[(int)(totalTicks * (long)SamplesPerTick)];
            for (int tick = 0; tick < totalTicks; tick++)
            {
                float t = tick * TickSeconds;
                engine.UpdateStations(TickSeconds, t, stations, schedulers, keyers, carriers, true);
                var block = new float[SamplesPerTick];
                engine.ProcessAudio(block, 1, stations, keyers, carriers, true, 0, 0);
                Array.Copy(block, 0, rendered, (long)tick * SamplesPerTick, SamplesPerTick);
            }

            string dumpPath = Environment.GetEnvironmentVariable("RN_RENDER_WAV");
            if (!string.IsNullOrEmpty(dumpPath))
                WavWriter.WritePcm16Mono(dumpPath.Replace(".wav", dashSector ? "_dash_sector.wav" : "_dot_sector.wav"), rendered, SampleRate);

            var segments = KeyingEnvelope.Analyze(rendered, SampleRate);
            Assert.True(segments.Count > 10, "too few keying segments: " + KeyingEnvelope.Describe(segments, 15));

            KeyingSegment longestOn = null;
            foreach (var s in segments)
            {
                if (s.IsOn && (longestOn == null || s.DurationMs > longestOn.DurationMs))
                    longestOn = s;
            }

            // AGC tail from the louder dashes can merge the last dash into
            // the equisignal train, so the upper bound is generous.
            Assert.True(longestOn != null && longestOn.DurationMs >= 3800 && longestOn.DurationMs <= 4800,
                "expected ~4000ms equisignal train as longest ON, got: " + KeyingEnvelope.Describe(segments, 15));

            // Post-navigation silence: pick the OFF segment closest to the
            // model value (the pre-nav pause can be longer in the dot sector).
            KeyingSegment postNav = null;
            double bestDiff = double.MaxValue;
            bool hasPreNavPause = false;
            double runawayLimit = expectedPostNavMs + 500;
            foreach (var s in segments)
            {
                if (s.IsOn || s.DurationMs < 2500)
                    continue;
                double diff = Math.Abs(s.DurationMs - expectedPostNavMs);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    postNav = s;
                }
                if (s.DurationMs >= 3600 && s.DurationMs <= 4300)
                    hasPreNavPause = true;
                Assert.True(s.DurationMs <= runawayLimit,
                    "runaway silence detected: " + KeyingEnvelope.Describe(segments, 15));
            }

            Assert.True(postNav != null && bestDiff <= 150,
                "post-navigation silence expected " + expectedPostNavMs + "+/-150ms, got: "
                + KeyingEnvelope.Describe(segments, 15));

            // Pre-navigation pause: PreNavPauseMs (3000) plus the callsign
            // pattern's trailing inter-character silence units (~940ms with
            // Cycle60s) after the final character.
            Assert.True(hasPreNavPause,
                "pre-navigation pause expected ~3940ms, got: "
                + KeyingEnvelope.Describe(segments, 15));
        }
    }
}
