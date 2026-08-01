using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class AudioSynthesisEngineTests
    {
        // F13: ProcessAudio runs on the audio thread and must not call
        // UnityEngine API (Time.time, Debug.Log). NavigationStartTime is
        // therefore stamped on the main thread via ConsumeNavStartFlags once
        // the scheduler crosses PreNavPause -> Navigation.
        [Fact]
        public void ConsumeNavStartFlags_PreNavTransition_StampsTime()
        {
            var engine = new AudioSynthesisEngine();
            var stations = new StationState[1];
            stations[0].Gain = 1f;

            var schedulers = new StationScheduler[1];
            schedulers[0] = new StationScheduler("TEST");
            schedulers[0].Tick();
            schedulers[0].OnSequenceFinished();
            Assert.Equal(StationScheduler.Phase.PreNavPause, schedulers[0].CurrentPhase);

            var keyers = new MorseKeyer[1];
            keyers[0] = new MorseKeyer();
            var carriers = new CarrierGenerator[1];
            carriers[0] = new CarrierGenerator(AudioSynthesisEngine.SampleRate);

            // 2205 samples per tick; PreNavPause expires after 60 scheduler
            // ticks. Inside ProcessAudio the pause phases are double-ticked,
            // so 30 audio ticks (~66150 samples) are enough.
            var data = new float[70000];
            engine.ProcessAudio(data, 1, stations, schedulers, keyers, carriers, false, 0, 0);

            engine.ConsumeNavStartFlags(stations, 123.45f);
            Assert.Equal(123.45f, stations[0].NavigationStartTime);

            engine.ConsumeNavStartFlags(stations, 999f);
            Assert.Equal(123.45f, stations[0].NavigationStartTime);
        }

        // F13: without a pending transition the stamp must not fire.
        [Fact]
        public void ConsumeNavStartFlags_NoPending_LeavesTimeUnchanged()
        {
            var engine = new AudioSynthesisEngine();
            var stations = new StationState[1];
            stations[0].Gain = 1f;
            stations[0].NavigationStartTime = 7f;

            engine.ConsumeNavStartFlags(stations, 123.45f);
            Assert.Equal(7f, stations[0].NavigationStartTime);
        }
    }
}
