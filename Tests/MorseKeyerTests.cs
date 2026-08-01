using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class MorseKeyerTests
    {
        [Fact]
        public void Load_AfterLoadConsol_ClearsStaleConsolPattern()
        {
            var keyer = new MorseKeyer();

            // Load a consol pattern whose first element is a Dot.
            keyer.LoadConsol(new[] { NavigationElementType.Dot, NavigationElementType.Dash });
            keyer.Advance();
            keyer.Advance(); // dot completes at GetDotSoundTicks

            // Then load a plain sequence whose first element is a Dash ("T").
            // Without the F65 fix the stale consolPattern wins in
            // GetCurrentElementType/GetCurrentSoundTicks -> Dot instead of Dash.
            var seq = MorseAlphabet.TextToPattern("T");
            keyer.Load(seq);

            var completed = (NavigationElementType)255;
            keyer.OnElementCompleted += (type, isCallsign) => completed = type;

            for (int i = 0; i < MorseTiming.GetDashSoundTicks(ConsolCycleConfig.Cycle120s); i++)
                keyer.Advance();

            Assert.Equal(NavigationElementType.Dash, completed);
        }

        [Fact]
        public void Reset_ClearsConsolPattern()
        {
            var keyer = new MorseKeyer();
            keyer.LoadConsol(new[] { NavigationElementType.Dash });
            keyer.Reset();

            var completed = (NavigationElementType)255;
            keyer.OnElementCompleted += (type, isCallsign) => completed = type;
            keyer.Load(MorseAlphabet.TextToPattern("E")); // E = "."

            for (int i = 0; i < MorseTiming.GetDotSoundTicks(ConsolCycleConfig.Cycle120s); i++)
                keyer.Advance();

            Assert.Equal(NavigationElementType.Dot, completed);
        }

        // F165: Reset must clear the loaded sequence so an idle keyer cannot
        // report stale sequence data through the getters.
        [Fact]
        public void Reset_ClearsSequence_KeyerStaysInactive()
        {
            var keyer = new MorseKeyer();
            keyer.Load(MorseAlphabet.TextToPattern("TEST"));
            keyer.Reset();
            Assert.False(keyer.IsActive);
        }
    }
}
