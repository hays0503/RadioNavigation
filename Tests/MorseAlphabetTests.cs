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

        // F125/F129: unknown characters must not throw; they encode as an
        // inter-character gap (3 silent elements), while a space is an
        // inter-word gap (7). Both keep GetElementCount/TextToPattern
        // consistent (identical padded sizes here).
        [Fact]
        public void TextToPattern_UnknownCharacter_ActsAsGap()
        {
            var withUnknown = MorseAlphabet.TextToPattern("AB?C");
            var withLetter = MorseAlphabet.TextToPattern("ABCD");
            Assert.Equal(3, withUnknown.Groups.Length);
            Assert.Equal(withLetter.ElementCount, withUnknown.ElementCount);
            Assert.Equal(MorseAlphabet.GetElementCount("AB?C") + 5, withUnknown.ElementCount);
        }

        [Fact]
        public void TextToPattern_OnlyUnknownCharacters_NoThrow()
        {
            var p = MorseAlphabet.TextToPattern("???");
            Assert.NotNull(p.Groups);
        }

        // F129: inter-word pause is 7 elements, inter-character is 3.
        // A = ".-" (2), B = "-..." (4).
        [Fact]
        public void GetElementCount_InterWordPause_IsSeven()
        {
            Assert.Equal(2 + 7 + 4, MorseAlphabet.GetElementCount("A B"));
        }

        [Fact]
        public void GetElementCount_InterCharacterPause_IsThree()
        {
            Assert.Equal(2 + 3 + 4, MorseAlphabet.GetElementCount("AB"));
        }

        [Fact]
        public void TextToPattern_InterWordPause_IsSeven()
        {
            var p = MorseAlphabet.TextToPattern("A B");
            Assert.Equal(2, p.Groups.Length);
            Assert.False(p.GetElement(0));
            Assert.True(p.GetElement(1));
            Assert.False(p.GetElement(8));
            Assert.True(p.GetElement(9));
        }
    }
}
