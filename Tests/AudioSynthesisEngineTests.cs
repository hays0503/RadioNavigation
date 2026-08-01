using System.Linq;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    // F01/F29: scheduler/keyer state machine must run on the main thread
    // (UpdateStations); ProcessAudio is read-only (only samples KeyOn).
    // F98: exactly one scheduler tick per logical tick (no double tick).
    // F132: no simulation while the radio is off.
    public class AudioSynthesisEngineTests
    {
        private static AudioSynthesisEngine CreateEngine()
        {
            return new AudioSynthesisEngine();
        }

        private static StationState[] CreateStation(float gain)
        {
            var stations = new StationState[1];
            stations[0].Gain = gain;
            return stations;
        }

        private static StationScheduler CreateSchedulerInPreNavPause()
        {
            var sched = new StationScheduler("TEST")
            {
                Config = ConsolCycleConfig.Cycle120s // PreNavPauseMs = 3000 -> 60 ticks
            };
            sched.Tick();
            sched.OnSequenceFinished();
            Assert.Equal(StationScheduler.Phase.PreNavPause, sched.CurrentPhase);
            return sched;
        }

        // F13 (superseded design): NavigationStartTime is stamped on the main
        // thread when the scheduler crosses PreNavPause -> Navigation.
        [Fact]
        public void UpdateStations_PreNavTransition_StampsNavigationStartTime()
        {
            var engine = CreateEngine();
            var stations = CreateStation(1f);
            var schedulers = new[] { CreateSchedulerInPreNavPause() };
            var keyers = new[] { new MorseKeyer() };

            // 3.05 s = 61 logical ticks with margin; exactly 60 ticks would
            // sit on the float/double rounding boundary (F94) and could
            // deliver only 59.
            engine.UpdateStations(3.05f, 123.45f, stations, schedulers, keyers, true);

            Assert.Equal(StationScheduler.Phase.Navigation, schedulers[0].CurrentPhase);
            Assert.Equal(123.45f, stations[0].NavigationStartTime);
        }

        // F98: PreNavPause (60 ticks) must not expire early due to double ticking.
        [Fact]
        public void UpdateStations_PreNavPause_LastsFullSixtyTicks()
        {
            var engine = CreateEngine();
            var stations = CreateStation(1f);
            var schedulers = new[] { CreateSchedulerInPreNavPause() };
            var keyers = new[] { new MorseKeyer() };

            engine.UpdateStations(59 * 0.05f, 1f, stations, schedulers, keyers, true);
            Assert.Equal(StationScheduler.Phase.PreNavPause, schedulers[0].CurrentPhase);

            engine.UpdateStations(1 * 0.05f, 2f, stations, schedulers, keyers, true);
            Assert.Equal(StationScheduler.Phase.Navigation, schedulers[0].CurrentPhase);
        }

        // F98: Pause expires in exactly one tick (constructor leaves 1 tick).
        [Fact]
        public void UpdateStations_SingleTick_PauseEntersCallsign()
        {
            var engine = CreateEngine();
            var stations = CreateStation(1f);
            var schedulers = new[] { new StationScheduler("TEST") };
            var keyers = new[] { new MorseKeyer() };

            engine.UpdateStations(0.05f, 1f, stations, schedulers, keyers, true);

            Assert.Equal(StationScheduler.Phase.Callsign, schedulers[0].CurrentPhase);
        }

        // F132: radio off — scheduler state machine must not advance.
        [Fact]
        public void UpdateStations_RadioOff_KeepsSchedulerPaused()
        {
            var engine = CreateEngine();
            var stations = CreateStation(1f);
            var schedulers = new[] { new StationScheduler("TEST") };
            var keyers = new[] { new MorseKeyer() };

            engine.UpdateStations(10f, 1f, stations, schedulers, keyers, false);

            Assert.Equal(StationScheduler.Phase.Pause, schedulers[0].CurrentPhase);

            engine.UpdateStations(0.05f, 2f, stations, schedulers, keyers, true);
            Assert.Equal(StationScheduler.Phase.Callsign, schedulers[0].CurrentPhase);
        }

        // F132 + F01: radio off — audio buffer must be silent, not stale data.
        [Fact]
        public void ProcessAudio_RadioOff_ClearsBuffer()
        {
            var engine = CreateEngine();
            var stations = CreateStation(1f);
            var keyers = new[] { new MorseKeyer() };
            var carriers = new[] { new CarrierGenerator(AudioSynthesisEngine.SampleRate) };
            var data = Enumerable.Repeat(0.5f, 1024).ToArray();

            engine.ProcessAudio(data, 1, stations, keyers, carriers, false, 0, 0);

            Assert.All(data, sample => Assert.Equal(0f, sample));
        }

        [Fact]
        public void ProcessAudio_WrongZone_ClearsBuffer()
        {
            var engine = CreateEngine();
            var stations = CreateStation(1f);
            var keyers = new[] { new MorseKeyer() };
            var carriers = new[] { new CarrierGenerator(AudioSynthesisEngine.SampleRate) };
            var data = Enumerable.Repeat(0.5f, 1024).ToArray();

            engine.ProcessAudio(data, 1, stations, keyers, carriers, true, 1, 0);

            Assert.All(data, sample => Assert.Equal(0f, sample));
        }

        // F01: with the key on, ProcessAudio produces a non-silent buffer.
        [Fact]
        public void ProcessAudio_KeyOn_ProducesSignal()
        {
            var engine = CreateEngine();
            var stations = CreateStation(1f);
            var schedulers = new[] { new StationScheduler("TEST") };
            var keyers = new[] { new MorseKeyer() };
            var carriers = new[] { new CarrierGenerator(AudioSynthesisEngine.SampleRate) };

            // Tick 1: Pause -> Callsign. Tick 2: the callsign sequence is loaded
            // and the keyer starts the first element.
            engine.UpdateStations(0.1f, 1f, stations, schedulers, keyers, true);
            Assert.Equal(StationScheduler.Phase.Callsign, schedulers[0].CurrentPhase);
            Assert.True(keyers[0].KeyOn, "after loading the callsign the first element must be keyed on");

            var data = new float[512];
            engine.ProcessAudio(data, 1, stations, keyers, carriers, true, 0, 0);

            Assert.True(data.Any(sample => sample != 0f), "keyed signal must not be silent");
        }
    }
}
