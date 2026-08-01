using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class NavigationPatternTests
    {
        [Fact]
        public void FormatPattern_SingleGroup_Alternating()
        {
            var p = new NavigationPattern(new[] { new Group(0b10101010UL) }, 8);
            Assert.Equal("[.-.-.-.-]", NavigationPattern.FormatPattern(p));
        }

        [Fact]
        public void FormatPattern_TransitionGroup_UsesEquals()
        {
            var p = new NavigationPattern(new[] { new Group(0b11110000UL), new Group(0b00001111UL) }, 8, 1);
            Assert.Equal("[....----] [========]", NavigationPattern.FormatPattern(p));
        }

        [Fact]
        public void GetElement_TransitionGroup_AlwaysFalse()
        {
            var p = new NavigationPattern(new[] { new Group(0b11111111UL), new Group(0b11111111UL) }, 8, 1);
            Assert.False(p.GetElement(8));
            Assert.False(p.GetElement(15));
        }

        [Fact]
        public void GetElement_OutOfRange_False()
        {
            var p = new NavigationPattern(new[] { new Group(0b11111111UL) }, 8);
            Assert.False(p.GetElement(8));
            Assert.False(p.GetElement(100));
        }

        [Fact]
        public void ElementCount_GroupTimesSize()
        {
            var p = new NavigationPattern(new[] { new Group(0UL), new Group(0UL) }, 8);
            Assert.Equal(16, p.ElementCount);
        }

        [Fact]
        public void Description_ContainsTransitionIndex()
        {
            var p = new NavigationPattern(new[] { new Group(0UL) }, 8, 1);
            Assert.Equal("NAV T=1", p.Description);
        }

        [Fact]
        public void FormatCompact_WithTransition_ContainsEqualsAndDoesNotThrow()
        {
            var p = new NavigationPattern(new[] { new Group(0b11110000UL), new Group(0b00001111UL) }, 8, 1);
            string s = NavigationPattern.FormatCompact(p);
            Assert.NotEmpty(s);
            Assert.Contains("=", s);
        }

        [Fact]
        public void FormatCompact_EmptyPattern_Dashes()
        {
            Assert.Equal("---", NavigationPattern.FormatCompact(new NavigationPattern(new Group[0], 8)));
        }

        [Fact]
        public void CallsignPattern_BuildsFromMorse()
        {
            var c = new CallsignPattern("B");
            Assert.True(c.GetElement(0));
            Assert.False(c.GetElement(1));
        }

        [Fact]
        public void Group_GetElement_Bitwise()
        {
            var g = new Group(0b101UL);
            Assert.True(g.GetElement(0));
            Assert.False(g.GetElement(1));
            Assert.True(g.GetElement(2));
        }

        // F144: ElementsPerGroup == 0 used to divide by zero in GetElement / format helpers.
        [Fact]
        public void Constructor_ZeroElementsPerGroup_FallsBackToEight()
        {
            var p = new NavigationPattern(new[] { new Group(0b10101010UL) }, 0);
            Assert.Equal((byte)8, p.ElementsPerGroup);
            Assert.Equal(8, p.ElementCount);
        }

        [Fact]
        public void ZeroElementsPerGroup_GetElementDoesNotThrow()
        {
            var p = new NavigationPattern(new[] { new Group(0b10101010UL) }, 0);
            _ = p.GetElement(0);
            _ = p.GetElement(3);
            _ = p.GetElement(7);
            _ = p.GetElement(100);
        }

        [Fact]
        public void ZeroElementsPerGroup_FormattersDoNotThrow()
        {
            var p = new NavigationPattern(new[] { new Group(0b11110000UL), new Group(0b00001111UL) }, 0, 1);
            Assert.NotEmpty(NavigationPattern.FormatPattern(p));
            Assert.NotEmpty(NavigationPattern.FormatCompact(p));
        }
    }
}
