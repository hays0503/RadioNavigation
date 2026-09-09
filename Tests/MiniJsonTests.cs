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
        public void Parse_Array_ReturnsList()
        {
            // Arrays were unsupported before the Leuchtfeuer registry needed
            // them; they now parse into List<object>.
            object value = MiniJson.Parse("{\"a\": [1, \"x\", {\"b\": 2}], \"c\": []}");
            var root = Assert.IsType<Dictionary<string, object>>(value);
            var array = Assert.IsType<List<object>>(root["a"]);
            Assert.Equal(3, array.Count);
            Assert.Equal(1.0, array[0]);
            Assert.Equal("x", array[1]);
            var nested = Assert.IsType<Dictionary<string, object>>(array[2]);
            Assert.Equal(2.0, nested["b"]);
            Assert.Empty(Assert.IsType<List<object>>(root["c"]));
        }

        [Fact]
        public void Parse_Array_Unterminated_Throws()
        {
            Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\": [1"));
        }

        [Fact]
        public void Parse_Array_MissingComma_Throws()
        {
            Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\": [1 2]}"));
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
