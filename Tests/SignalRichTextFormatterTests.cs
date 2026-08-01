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
    }
}
