using System;
using System.IO;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class CurrentWitnessTileTests
    {
        [Fact]
        public void SamplesDayDepthAndPositionInThePublishedAxisOrder()
        {
            Assert.True(CurrentWitnessTile.TryRead(Tile(), out var tile));
            Assert.True(tile.TrySample(50, 25, 45.25, 306.75, out var sample));
            Assert.Equal(0.6750f, sample.EastMetersPerSecond, 5);
            Assert.Equal(-0.1250f, sample.NorthMetersPerSecond, 5);
            Assert.True(tile.TrySample(100, 100, 46, 307, out sample));
            Assert.Equal(1.3f, sample.EastMetersPerSecond, 5);
            Assert.False(tile.TrySample(101, 100, 46, 307, out _));
            Assert.False(tile.TrySample(50, 0, 45.25, -53.25, out _));
        }

        [Fact]
        public void WetZeroIsAvailableButAnyMissingCornerRejectsTheCell()
        {
            Assert.True(CurrentWitnessTile.TryRead(Tile(), out var tile));
            Assert.True(tile.TrySample(0, 0, 45, 306, out var sample));
            Assert.Equal(0f, sample.EastMetersPerSecond);
            Assert.Equal(0f, sample.NorthMetersPerSecond);
            Assert.True(CurrentWitnessTile.TryRead(Tile(missing: true), out tile));
            Assert.False(tile.TrySample(0, 0, 45, 306, out _));
        }

        [Fact]
        public void RejectsTruncationMismatchedMaskAndUnorderedDepths()
        {
            var bytes = Tile();
            System.Array.Resize(ref bytes, bytes.Length - 1);
            Assert.False(CurrentWitnessTile.TryRead(bytes, out _));
            Assert.False(CurrentWitnessTile.TryRead(Tile(missing: true, mismatched: true), out _));
            Assert.False(CurrentWitnessTile.TryRead(Tile(unordered: true), out _));
        }

        [Fact]
        public void OptInAtlanticWitnessMatchesOfflineOracle()
        {
            var path = Environment.GetEnvironmentVariable("RADIONAV_RNW1_WITNESS");
            if (string.IsNullOrEmpty(path))
                return;
            var bytes = File.ReadAllBytes(path);
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var digest = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                Assert.Equal("5412aafad563c4b1cd9f0a35c6f0df416164bb38ac2b31f643368f25f3133482", digest);
            }
            Assert.True(CurrentWitnessTile.TryRead(bytes, out var tile));
            double t = new DateTimeOffset(2014, 4, 7, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            Assert.True(tile.TrySample(t, 100, 46.239999771118164, 338.239990234375, out var sample));
            Assert.Equal(-0.18799976f, sample.EastMetersPerSecond, 6);
            Assert.Equal(-0.06300114f, sample.NorthMetersPerSecond, 6);
            Assert.False(tile.TrySample(t, 201, 46.239999771118164, 338.239990234375, out _));
        }

        private static byte[] Tile(bool missing = false, bool mismatched = false, bool unordered = false)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(new byte[] { 82, 78, 87, 49 });
            for (int i = 0; i < 4; i++)
                writer.Write((ushort)2);
            foreach (double coordinate in new double[] { 0, 100, 0, unordered ? -100 : 100, 45, 46, 306, 307 })
                writer.Write(coordinate);
            for (int i = 0; i < 16; i++)
            {
                int east = (i & 8) * 1000 + (i & 2) * 1000 + (i & 1) * 3000;
                int north = (i & 4) * -1250;
                writer.Write(missing && i == 15 ? short.MinValue : (short)east);
                writer.Write(missing && i == 15 && !mismatched ? short.MinValue : (short)north);
            }
            return stream.ToArray();
        }
    }
}
