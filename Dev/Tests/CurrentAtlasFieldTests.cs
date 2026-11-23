using System;
using System.IO;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class CurrentAtlasFieldTests
    {
        private static readonly DateTime T0 = new DateTime(1941, 11, 5, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void SamplesTheTileByCampaignTimeAndHullCoordinates()
        {
            string path = WriteTile();
            try
            {
                Assert.True(CurrentAtlasField.TryLoad(path, out var field, out var error), error);
                Assert.True(field.HasFlow);
                Assert.Contains("load=", field.ToString());
                Assert.Contains(" ms)", field.ToString());
                var query = new CurrentQuery
                {
                    HasPosition = true, Latitude = 48.5, Longitude = -15.5, DepthMeters = 5,
                    Utc = T0.AddHours(3)
                };
                Assert.True(field.TrySample(query, out var water));
                Assert.Equal(1f, water.x, 6);
                Assert.Equal(0f, water.z, 6);

                query.Utc = T0.AddDays(3);
                Assert.False(field.TrySample(query, out _));
                query.Utc = T0.AddHours(3);
                query.HasPosition = false;
                Assert.False(field.TrySample(query, out _));
                query.HasPosition = true;
                query.Utc = default(DateTime);
                Assert.False(field.TrySample(query, out _));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void AcceptsWestLongitudeAgainstAZeroTo360Tile()
        {
            string path = WriteTile(338, 339);
            try
            {
                Assert.True(CurrentAtlasField.TryLoad(path, out var field, out var error), error);
                var query = new CurrentQuery
                {
                    HasPosition = true, Latitude = 48.5, Longitude = -21.5, DepthMeters = 5,
                    Utc = T0.AddHours(3)
                };
                Assert.True(field.TrySample(query, out var west));
                Assert.Equal(1f, west.x, 6);
                query.Longitude = 338.5;
                Assert.True(field.TrySample(query, out var east));
                Assert.Equal(west, east);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void RejectsMissingTruncatedOrOversizedFiles()
        {
            Assert.False(CurrentAtlasField.TryLoad(Path.Combine(Path.GetTempPath(), "rn-absent-atlas.rnw"), out _, out _));
            string path = NewPath();
            try
            {
                File.WriteAllBytes(path, new byte[] { 82, 78, 87, 49, 2 });
                Assert.False(CurrentAtlasField.TryLoad(path, out _, out _));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void AdjacentTilesRequireEveryPackedSeamValueToMatch()
        {
            string directory = Path.ChangeExtension(NewPath(), null);
            Directory.CreateDirectory(directory);
            WriteTile(-16, -15, 2000, 10000, Path.Combine(directory, "west.rnw"));
            string east = Path.Combine(directory, "east.rnw");
            try
            {
                WriteTile(-15, -14, 10000, 3000, east);
                Assert.True(CurrentAtlasField.TryLoad(directory, out var field, out var error), error);
                var query = new CurrentQuery { HasPosition = true, Utc = T0, Latitude = 48.5, DepthMeters = 5 };
                query.Longitude = -15.5;
                Assert.True(field.TrySample(query, out var velocity));
                Assert.Equal(0.6f, velocity.x, 6);
                query.Longitude = -15;
                Assert.True(field.TrySample(query, out velocity));
                Assert.Equal(1f, velocity.x, 6);
                query.Longitude = -14.5;
                Assert.True(field.TrySample(query, out velocity));
                Assert.Equal(0.65f, velocity.x, 6);
                query.Longitude = -13.9;
                Assert.False(field.TrySample(query, out _));

                WriteTile(-15, -14, 9999, 3000, east);
                Assert.False(CurrentAtlasField.TryLoad(directory, out _, out _));
                WriteTile(-15.5, -14, 10000, 3000, east);
                Assert.False(CurrentAtlasField.TryLoad(directory, out _, out _));
                WriteTile(-15, -14, 10000, 3000, east, lastNorth: 1);
                Assert.False(CurrentAtlasField.TryLoad(directory, out _, out _));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void AZeroTileWithMissingNodesDoesNotRequestDragPatches()
        {
            string path = WriteTile(westValue: short.MinValue, eastValue: 0);
            try
            {
                Assert.True(CurrentAtlasField.TryLoad(path, out var field, out var error), error);
                Assert.False(field.HasFlow);
                var query = new CurrentQuery
                {
                    HasPosition = true, Utc = T0, Latitude = 48.5, Longitude = -15, DepthMeters = 5
                };
                Assert.False(field.TrySample(query, out _));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ShippedRegionalAtlasLoadsFromTheRepositoryFolder()
        {
            string directory = FindShippedAtlas();
            Assert.False(directory == null,
                "Source/Data/CurrentAtlas not found from " + Directory.GetCurrentDirectory());
            Assert.True(CurrentAtlasField.TryLoad(directory, out var field, out var error), error);
            Assert.True(field.HasFlow);
            Assert.Contains("tiles=6", field.ToString());

            var query = new CurrentQuery
            {
                HasPosition = true, Latitude = 47.5, Longitude = 344.5, DepthMeters = 10, Utc = T0
            };
            Assert.True(field.TrySample(query, out var water));
            Assert.InRange(Math.Sqrt(water.x * water.x + water.z * water.z), 0.001, 0.5);

            // The bundled reconstruction duplicates only the shallow wet layer.
            query.DepthMeters = 0;
            Assert.True(field.TrySample(query, out var surface));
            query.DepthMeters = 5;
            Assert.True(field.TrySample(query, out var shallow));
            Assert.Equal(surface, shallow);
            Assert.InRange(Math.Sqrt(surface.x * surface.x + surface.z * surface.z), 0.001, 0.5);

            // Expanded coverage: 1939-1945 and up to 75.5 N (Norwegian Sea / Arctic).
            query.Latitude = 54;
            query.Longitude = -35;
            query.Utc = new DateTime(1941, 2, 25, 0, 0, 0, DateTimeKind.Utc);
            Assert.True(field.TrySample(query, out _));
            query.Latitude = 58;
            query.Longitude = 0.01;
            Assert.True(field.TrySample(query, out _)); // crosses the +0.5 longitude seam
            query.Latitude = 68;
            query.Longitude = 5;
            query.Utc = new DateTime(1944, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            Assert.True(field.TrySample(query, out _)); // Norwegian Sea in 1944
            query.Latitude = 48;
            query.Longitude = 2;
            query.Utc = T0;
            Assert.False(field.TrySample(query, out _)); // land mask
            query.DepthMeters = 10;
            query.Latitude = 29;
            Assert.False(field.TrySample(query, out _));
            query.Latitude = 76.5;
            Assert.False(field.TrySample(query, out _));
            query.Latitude = 54;
            query.Longitude = -35;
            query.Utc = new DateTime(1946, 2, 1, 0, 0, 0, DateTimeKind.Utc);
            Assert.False(field.TrySample(query, out _));
            query.Utc = new DateTime(1938, 12, 1, 0, 0, 0, DateTimeKind.Utc);
            Assert.False(field.TrySample(query, out _));
        }

        [Fact]
        public void AtlanticPackageSamplesBothSidesOfThePackedTileSeam()
        {
            Assert.True(CurrentAtlasField.TryLoad(FindShippedAtlas(), out var field, out var error), error);
            var query = new CurrentQuery
            {
                HasPosition = true, Latitude = 48, Longitude = -24.5001, DepthMeters = 0, Utc = T0
            };
            Assert.True(field.TrySample(query, out var west));
            query.Longitude = -24.4999;
            Assert.True(field.TrySample(query, out var east));
            Assert.InRange(Math.Abs(east.x - west.x), 0f, 0.0001f);
            Assert.InRange(Math.Abs(east.z - west.z), 0f, 0.0001f);
        }

        private static string FindShippedAtlas()
        {
            // Start from the test assembly: it is built inside the repository,
            // so the lookup does not depend on the caller's working directory.
            foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var directory = new DirectoryInfo(start);
                for (int i = 0; i < 8 && directory != null; i++, directory = directory.Parent)
                {
                    string candidate = Path.Combine(directory.FullName, "Source", "Data", "CurrentAtlas");
                    if (Directory.Exists(candidate))
                        return candidate;
                }
            }
            return null;
        }

        private static string NewPath()
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "tests", "current-atlas");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "tile-" + Guid.NewGuid().ToString("N") + ".rnw");
        }

        private static string WriteTile(double lonWest = -16, double lonEast = -15,
            short westValue = 10000, short eastValue = 10000, string file = null, short lastNorth = 0)
        {
            string path = file ?? NewPath();
            double s0 = new DateTimeOffset(T0).ToUnixTimeSeconds();
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[] { 82, 78, 87, 49 });
                foreach (ushort size in new ushort[] { 2, 2, 2, 2 })
                    writer.Write(size);
                foreach (double coordinate in new double[]
                         {
                             s0, s0 + 86400, 0, 10, 48, 49, lonWest, lonEast
                         })
                    writer.Write(coordinate);
                for (int i = 0; i < 16; i++)
                {
                    short east = (i & 1) == 0 ? westValue : eastValue;
                    writer.Write(east);
                    writer.Write(east == short.MinValue ? short.MinValue : i == 14 ? lastNorth : (short)0);
                }
            }
            return path;
        }
    }
}
