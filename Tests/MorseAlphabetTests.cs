using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class MorseAlphabetTests
    {
        [Theory]
        [InlineData('B', "-...")]
        [InlineData('C', "-.-.")]
        [InlineData('5', ".....")]
        [InlineData('O', "---")]
        [InlineData('T', "-")]
        [InlineData('A', ".-")]
        public void GetMorse_Upper_ReturnsExpected(char c, string expected)
        {
            Assert.Equal(expected, MorseAlphabet.GetMorse(c));
        }

        [Fact]
        public void GetMorse_Lowercase_SameAsUppercase()
        {
            Assert.Equal(MorseAlphabet.GetMorse('B'), MorseAlphabet.GetMorse('b'));
        }

        [Fact]
        public void GetMorse_UnknownChar_ReturnsEmpty()
        {
            Assert.Equal("", MorseAlphabet.GetMorse('?'));
            Assert.Equal("", MorseAlphabet.GetMorse((char)0));
            Assert.Equal("", MorseAlphabet.GetMorse('{'));
        }

        [Theory]
        [InlineData("-...", 'B')]
        [InlineData("---", 'O')]
        [InlineData("....", 'H')]
        [InlineData(".-", 'A')]
        public void DecodeMorse_KnownSequence_ReturnsChar(string seq, char expected)
        {
            Assert.Equal(expected, MorseAlphabet.DecodeMorse(seq));
        }

        [Theory]
        [InlineData("")]
        [InlineData("nonsense")]
        [InlineData(".-.-.-")]
        public void DecodeMorse_UnknownSequence_ReturnsQuestionMark(string seq)
        {
            Assert.Equal('?', MorseAlphabet.DecodeMorse(seq));
        }

        [Fact]
        public void GetElementCount_SingleChar_IsMorseLength()
        {
            Assert.Equal(4, MorseAlphabet.GetElementCount("B"));
        }

        [Fact]
        public void GetElementCount_Empty_Zero()
        {
            Assert.Equal(0, MorseAlphabet.GetElementCount(""));
        }

        [Fact]
        public void GetElementCount_TwoChars_AddsInterCharacterPause()
        {
            Assert.Equal(4 + 3 + 4, MorseAlphabet.GetElementCount("BC"));
        }

        [Fact]
        public void TextToPattern_SingleChar_FirstElementIsDashForDashLetters()
        {
            var p = MorseAlphabet.TextToPattern("B");
            Assert.Equal(8, p.ElementsPerGroup);
            Assert.True(p.GetElement(0));
            Assert.False(p.GetElement(1));
            Assert.False(p.GetElement(2));
            Assert.False(p.GetElement(3));
        }

        [Fact]
        public void TextToPattern_Empty_NoGroups()
        {
            var p = MorseAlphabet.TextToPattern("");
            Assert.Empty(p.Groups);
        }

        [Fact]
        public void TextToPattern_MultiChar_PadsToGroupSize()
        {
            var p = MorseAlphabet.TextToPattern("MNO");
            Assert.Equal(16, p.ElementCount);
            Assert.True(p.GetElement(0));
            Assert.True(p.GetElement(1));
        }

        // F125: unknown characters must not throw; they encode as an
        // inter-character gap (3 silent elements), consistent with how a
        // space is treated, so GetElementCount and TextToPattern agree.
        [Fact]
        public void TextToPattern_UnknownCharacter_ActsLikeSpace()
        {
            var withUnknown = MorseAlphabet.TextToPattern("AB?C");
            var withSpace = MorseAlphabet.TextToPattern("AB C");
            Assert.Equal(withSpace.ElementCount, withUnknown.ElementCount);
            Assert.Equal(MorseAlphabet.GetElementCount("AB?C"), MorseAlphabet.GetElementCount("AB C"));
        }

        [Fact]
        public void TextToPattern_OnlyUnknownCharacters_NoThrow()
        {
            var p = MorseAlphabet.TextToPattern("???");
            Assert.NotNull(p.Groups);
        }
    }
}
