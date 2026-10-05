using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class SignalTypesTests
    {
        [Fact]
        public void SignalEntry_Constructor_StoresValues()
        {
            var e = new SignalEntry('x', SignalCharType.Dash);
            Assert.Equal('x', e.Ch);
            Assert.Equal(SignalCharType.Dash, e.Type);
        }

        [Fact]
        public void SignalCharType_EnumOrder()
        {
            Assert.Equal(0, (byte)SignalCharType.Callsign);
            Assert.Equal(1, (byte)SignalCharType.Dot);
            Assert.Equal(2, (byte)SignalCharType.Dash);
            Assert.Equal(3, (byte)SignalCharType.Equisignal);
            Assert.Equal(4, (byte)SignalCharType.Separator);
        }

        [Fact]
        public void NavigationElementType_EnumValues()
        {
            Assert.Equal(0, (byte)NavigationElementType.Dot);
            Assert.Equal(1, (byte)NavigationElementType.Dash);
            Assert.Equal(2, (byte)NavigationElementType.Equisignal);
        }
    }
}
