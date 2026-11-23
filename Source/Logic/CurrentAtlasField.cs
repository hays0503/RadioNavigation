using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Optional runtime consumer for a bounded regional RNW1 atlas. Tiles are
    /// loaded once and sampled by the hull's own coordinates and
    /// the campaign UTC. Depth and time coverage come from the tiles.
    /// </summary>
    internal sealed class CurrentAtlasField : ICurrentField
    {
        private const int MaxBytes = 2 * 1024 * 1024;
        private const int MaxTiles = 8;
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly CurrentWitnessTile[] tiles;
        private readonly string description;

        private CurrentAtlasField(CurrentWitnessTile[] tiles, string path, int bytes, long loadMs)
        {
            this.tiles = tiles;
            HasFlow = Array.Exists(tiles, tile => tile.HasAnyFlow());
            description = Path.GetFileName(path) + " (" + bytes + " B, flow=" + HasFlow
                + (tiles.Length == 1 ? "" : ", tiles=" + tiles.Length)
                + ", load=" + loadMs + " ms)";
        }

        public bool HasFlow { get; }

        public static bool TryLoad(string path, out CurrentAtlasField field, out string error)
        {
            field = null;
            error = null;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string fullPath = Path.GetFullPath(path);
                if (!TryGetPaths(fullPath, out List<string> paths, out error)
                    || !TryReadTiles(paths, out CurrentWitnessTile[] loaded, out int bytes, out error))
                    return false;
                watch.Stop();
                field = new CurrentAtlasField(loaded, fullPath, bytes, watch.ElapsedMilliseconds);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name;
                return false;
            }
        }

        private static bool TryGetPaths(string path, out List<string> paths, out string error)
        {
            paths = new List<string>();
            error = null;
            if (!Directory.Exists(path))
            {
                paths.Add(path);
                return true;
            }
            foreach (string candidate in Directory.EnumerateFiles(path, "*.rnw", SearchOption.TopDirectoryOnly))
            {
                if (paths.Count == MaxTiles)
                {
                    error = "atlas has more than " + MaxTiles + " RNW1 tiles";
                    return false;
                }
                paths.Add(candidate);
            }
            paths.Sort(StringComparer.Ordinal);
            if (paths.Count != 0)
                return true;
            error = "atlas directory contains no RNW1 tiles";
            return false;
        }

        private static bool TryReadTiles(List<string> paths, out CurrentWitnessTile[] loaded,
            out int bytes, out string error)
        {
            loaded = new CurrentWitnessTile[paths.Count];
            bytes = 0;
            error = null;
            for (int i = 0; i < loaded.Length; i++)
            {
                if (!TryReadFile(paths[i], out loaded[i], out int size, out error))
                    return false;
                bytes += size;
                for (int j = 0; j < i; j++)
                    if (!loaded[j].IsCompatibleWith(loaded[i]))
                    {
                        error = "overlap or incompatible axes/seam: " + Path.GetFileName(paths[i]);
                        return false;
                    }
            }
            if (loaded.Length == 1 || HasOneLongitudeConvention(loaded))
                return true;
            error = "multi-tile longitude axes require one convention and a span below 360 degrees";
            return false;
        }

        private static bool TryReadFile(string path, out CurrentWitnessTile tile, out int size, out string error)
        {
            tile = null;
            size = 0;
            error = null;
            using (var stream = File.OpenRead(path))
            {
                long length = stream.Length;
                if (length <= 0 || length > MaxBytes)
                {
                    error = "size outside 1.." + MaxBytes + " bytes";
                    return false;
                }
                size = (int)length;
                using (var reader = new BinaryReader(stream))
                {
                    byte[] data = reader.ReadBytes(size);
                    if (data.Length == size && stream.ReadByte() == -1 && CurrentWitnessTile.TryRead(data, out tile))
                        return true;
                }
                error = "not a valid RNW1 tile";
                return false;
            }
        }

        private static bool HasOneLongitudeConvention(CurrentWitnessTile[] loaded)
        {
            double west = double.PositiveInfinity;
            double east = double.NegativeInfinity;
            foreach (CurrentWitnessTile tile in loaded)
            {
                west = Math.Min(west, tile.West);
                east = Math.Max(east, tile.East);
            }
            return east - west < 360
                && ((west >= -180 && east <= 180) || (west >= 0 && east <= 360));
        }

        public bool TrySample(CurrentQuery query, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (!query.HasPosition || query.Utc == default(DateTime))
                return false;
            double seconds = (query.Utc - Epoch).TotalSeconds;
            if (!SampleAt(seconds, query, query.Longitude, out CurrentVelocity water)
                && !SampleAt(seconds, query, query.Longitude + 360.0, out water)
                && !SampleAt(seconds, query, query.Longitude - 360.0, out water))
                return false;
            velocity = new Vector3(water.EastMetersPerSecond, 0f, water.NorthMetersPerSecond);
            return true;
        }

        // Tile axes are source-native: HYCOM and the repo witness use 0..360,
        // while Coordinates.ToLatLong returns west as negative. Try the
        // equivalent longitude instead of silently reporting "no coverage".
        private bool SampleAt(double seconds, CurrentQuery query, double longitude, out CurrentVelocity water)
        {
            foreach (CurrentWitnessTile tile in tiles)
                if (tile.TrySample(seconds, query.DepthMeters, query.Latitude, longitude, out water))
                    return true;
            water = default(CurrentVelocity);
            return false;
        }

        public override string ToString() => description;
    }
}
