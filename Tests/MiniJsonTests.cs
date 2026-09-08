using System;
using System.Collections.Generic;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class MiniJsonTests
    {
        [Fact]
        public void Parse_EmptyObject()
        {
            Dictionary<string, object> root = MiniJson.Parse("{}");

            Assert.Empty(root);
        }

        [Fact]
        public void Parse_StringAndNumber()
        {
            Dictionary<string, object> root = MiniJson.Parse("{\"a\": \"x\", \"b\": 12.5}");

            Assert.Equal("x", root["a"]);
            Assert.Equal(12.5, (double)root["b"]);
        }

        [Fact]
        public void Parse_NestedObject()
        {
            Dictionary<string, object> root = MiniJson.Parse("{\"o\": {\"n\": -3}}");

            var inner = (Dictionary<string, object>)root["o"];
            Assert.Equal(-3.0, (double)inner["n"]);
        }

        [Fact]
        public void Parse_AllEightEscapes()
        {
            Dictionary<string, object> root = MiniJson.Parse(
                "{\"e\": \"\\\"\\\\\\/\\b\\f\\n\\r\\t\"}");

            Assert.Equal("\"\\/\b\f\n\r\t", (string)root["e"]);
        }

        [Fact]
        public void Parse_UnknownEscape_Throws()
        {
            Assert.Throws<FormatException>(() => MiniJson.Parse("{\"e\": \"\\u1234\"}"));
        }

        [Fact]
        public void Parse_Array_Throws()
        {
            Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\": [1]}"));
        }

        [Fact]
        public void Parse_Null_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => MiniJson.Parse(null));
        }

        [Fact]
        public void Parse_TrailingData_Throws()
        {
            Assert.Throws<FormatException>(() => MiniJson.Parse("{} {}"));
        }

        [Fact]
        public void Parse_UnterminatedString_Throws()
        {
            Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\": \"x}"));
        }
    }
}
