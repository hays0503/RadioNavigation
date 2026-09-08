using System.Collections.Generic;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class SignalRichTextFormatterTests
    {
        // F120: TrimEntries must behave exactly as the old RemoveAt(0)
        // loop, but collapse the shift into a single RemoveRange.
        [Fact]
        public void TrimEntries_MorseOverflow_TrimsToMaxMorseChars()
        {
            var entries = new List<SignalEntry>();
            int count = 0;
            for (int i = 0; i < 25; i++)
            {
                entries.Add(new SignalEntry('.', SignalCharType.Dot));
                count++;
            }

            SignalRichTextFormatter.TrimEntries(entries, ref count);

            Assert.Equal(20, count);
            Assert.Equal(20, entries.Count);
            Assert.Equal('.', entries[0].Ch);
        }

        [Fact]
        public void TrimEntries_EntriesOverflow_TrimsToMaxSignalEntries()
        {
            var entries = new List<SignalEntry>();
            int count = 0;
            for (int i = 0; i < 70; i++)
            {
                entries.Add(new SignalEntry(' ', SignalCharType.Separator));
            }

            SignalRichTextFormatter.TrimEntries(entries, ref count);

            Assert.Equal(SignalRichTextFormatter.MaxSignalEntries, entries.Count);
            Assert.Equal(0, count);
        }

        [Fact]
        public void TrimEntries_Separators_DoNotDecrementMorseCount()
        {
            var entries = new List<SignalEntry>();
            int count = 0;
            for (int i = 0; i < 60; i++)
                entries.Add(new SignalEntry(' ', SignalCharType.Separator));
            for (int i = 0; i < 25; i++)
            {
                entries.Add(new SignalEntry('-', SignalCharType.Dash));
                count++;
            }

            SignalRichTextFormatter.TrimEntries(entries, ref count);

            // 60 separators go first (count unchanged), then 5 dashes: the
            // same outcome as the old RemoveAt(0) loop.
            Assert.Equal(20, count);
            Assert.Equal(20, entries.Count);
            Assert.Equal('-', entries[0].Ch);
        }

        [Fact]
        public void TrimEntries_WithinLimits_NoChanges()
        {
            var entries = new List<SignalEntry>();
            int count = 0;
            for (int i = 0; i < 10; i++)
            {
                entries.Add(new SignalEntry('.', SignalCharType.Dot));
                count++;
            }

            SignalRichTextFormatter.TrimEntries(entries, ref count);

            Assert.Equal(10, entries.Count);
            Assert.Equal(10, count);
        }

        [Fact]
        public void FormatRichText_DashFirst_RendersDashEquisignalDot()
        {
            var entries = new List<SignalEntry>
            {
                new SignalEntry('-', SignalCharType.Dash),
                new SignalEntry('=', SignalCharType.Equisignal),
                new SignalEntry('.', SignalCharType.Dot)
            };

            string rich = SignalRichTextFormatter.FormatRichText(entries);

            int dash = rich.IndexOf("#2288FF", System.StringComparison.Ordinal);
            int equisignal = rich.IndexOf("#FF00FF", System.StringComparison.Ordinal);
            int dot = rich.IndexOf("#FF2222", System.StringComparison.Ordinal);
            Assert.True(dash >= 0);
            Assert.True(equisignal > dash);
            Assert.True(dot > equisignal);
        }

        [Fact]
        public void FormatRichText_Callsign_RendersGreen()
        {
            var entries = new List<SignalEntry>
            {
                new SignalEntry('-', SignalCharType.Callsign),
                new SignalEntry('.', SignalCharType.Callsign)
            };

            string rich = SignalRichTextFormatter.FormatRichText(entries);

            Assert.Contains("#00FF00", rich);
            Assert.Contains("\u25AC", rich);
            Assert.Contains("\u25CF", rich);
        }

        [Fact]
        public void FormatRichText_Separator_RendersSpace()
        {
            var entries = new List<SignalEntry>
            {
                new SignalEntry(' ', SignalCharType.Separator)
            };

            string rich = SignalRichTextFormatter.FormatRichText(entries);

            Assert.Contains("> </color>", rich);
        }

        [Fact]
        public void TrimDecodedText_LongText_TrimsToMax()
        {
            var sb = new System.Text.StringBuilder("0123456789ABCDEFGHIJ0123456789ABCDE");

            SignalRichTextFormatter.TrimDecodedText(sb);

            Assert.Equal(30, sb.Length);
            Assert.Equal("56789ABCDE", sb.ToString().Substring(20));
        }
    }
}
