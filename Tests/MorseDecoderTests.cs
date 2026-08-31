using System.Text;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Locks MorseDecoder (P5.6 SC-1), extracted from
    /// SonneController.DecodeCallsignBuffer: pure morse-callsign decode with
    /// '?' fallback for mismatched segments.
    /// </summary>
    public sealed class MorseDecoderTests
    {
        [Fact]
        public void ExpectedLength_SumOfLetterMorse()
        {
            // "FRQ": F=..-. (4) R=.-. (3) Q=--.- (4) => 11
            Assert.Equal(11, MorseDecoder.ExpectedLength("FRQ"));
            // lowercase normalizes to uppercase
            Assert.Equal(11, MorseDecoder.ExpectedLength("frq"));
        }

        [Fact]
        public void ExpectedLength_EmptyOrNull_IsZero()
        {
            Assert.Equal(0, MorseDecoder.ExpectedLength(null));
            Assert.Equal(0, MorseDecoder.ExpectedLength(""));
        }

        [Fact]
        public void Decode_ExactMatch_ReturnsCallsign()
        {
            var buf = new StringBuilder("..-..-.--.-");
            Assert.Equal("FRQ", MorseDecoder.Decode("FRQ", buf));
        }

        [Fact]
        public void Decode_MismatchedSegment_EmitsQuestionMark()
        {
            // First letter wrong: expect F (..-.), got B (-...) -> mismatch;
            // second letter R (.-.) matches.
            var buf = new StringBuilder(MorseAlphabet.GetMorse('B') + MorseAlphabet.GetMorse('R'));
            Assert.Equal("?R", MorseDecoder.Decode("FR", buf));
        }

        [Fact]
        public void Decode_BufferTooShort_StopsEarly()
        {
            // Only enough for first letter F (..-.) then buffer ends.
            var buf = new StringBuilder("..-.");
            Assert.Equal("F", MorseDecoder.Decode("FRQ", buf));
        }

        [Fact]
        public void Decode_LowercaseCallsign_Normalizes()
        {
            var buf = new StringBuilder("..-..-.--.-");
            Assert.Equal("FRQ", MorseDecoder.Decode("frq", buf));
        }
    }
}
