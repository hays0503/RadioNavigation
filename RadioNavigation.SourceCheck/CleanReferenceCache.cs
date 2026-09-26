using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RadioNavigation.SourceCheck
{
    /// <summary>
    /// Caches the compiled clean game assembly on disk (under obj/) so we do not
    /// recompile all ~2550 game sources on every run. The cache is invalidated when
    /// any game source file or referenced DLL changes (content-hash based).
    /// </summary>
    internal static class CleanReferenceCache
    {
        private static readonly string CacheDir = Path.Combine(
            AppContext.BaseDirectory, "obj", "cleanref");

        public const string DllFileName = "com.uboat.game.clean.dll";

        public static string DllPath => Path.Combine(CacheDir, DllFileName);

        public static bool TryLoad(out byte[] dll)
        {
            dll = null;
            if (!File.Exists(DllPath))
                return false;
            var stampPath = StampPath();
            if (!File.Exists(stampPath))
                return false;
            var stamp = File.ReadAllText(stampPath);
            if (stamp != ComputeStamp())
                return false;
            dll = File.ReadAllBytes(DllPath);
            return true;
        }

        public static void Save(byte[] dll)
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllBytes(DllPath, dll);
            File.WriteAllText(StampPath(), ComputeStamp());
        }

        private static string StampPath() => DllPath + ".stamp";

        /// <summary>
        /// Seed-quality comparison stamp: SeedQuality_NewVsOld re-runs the removed
        /// ~205 s semantic pass on every run, and SeedQuality_DllOnlyTypesAreBounded
        /// re-indexes the game source tree. Both comparisons are deterministic in
        /// their inputs, so a passing result stays valid until an input changes.
        /// Each test keeps its own stamp file (a shared one would let the first
        /// test to run mark the second as verified without it ever executing).
        /// The stamp is written only after the asserts pass - a failure leaves no
        /// stamp, so the next run re-verifies instead of going silently green.
        /// </summary>
        public static string SeedQualityStampPath(string testName) =>
            Path.Combine(CacheDir, "seedquality-" + testName + ".stamp");

        public static bool IsSeedQualityCurrent(string testName)
        {
            var path = SeedQualityStampPath(testName);
            if (!File.Exists(path))
                return false;
            return File.ReadAllText(path) == ComputeSeedQualityStamp();
        }

        public static void SaveSeedQuality(string testName)
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllText(SeedQualityStampPath(testName), ComputeSeedQualityStamp());
        }

        public static string ComputeSeedQualityStamp()
        {
            using var sha = SHA256.Create();
            var parts = new List<string>();
            CollectSharedInputParts(parts);
            // Both seed algorithms live in this tool: a change here can break the
            // old-vs-new equivalence (or the type index) with identical game/mod
            // inputs, so this tool's own sources join the stamp.
            var selfDir = Path.Combine(ModCompiler.ModRoot, "RadioNavigation.SourceCheck");
            if (Directory.Exists(selfDir))
                foreach (var self in Directory.EnumerateFiles(selfDir, "*.cs", SearchOption.AllDirectories))
                    parts.Add("self:" + self + ":" + File.GetLastWriteTimeUtc(self).Ticks);
            var joined = string.Join("\n", parts);
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(joined));
            return Convert.ToBase64String(hash);
        }

        private static void CollectSharedInputParts(List<string> parts)
        {
            foreach (var src in GameSourceCompiler.EnumerateSources())
                parts.Add(src + ":" + File.GetLastWriteTimeUtc(src).Ticks);

            foreach (var refPath in Directory.EnumerateFiles(GameSourceCompiler.ManagedDir, "*.dll"))
            {
                var name = Path.GetFileName(refPath);
                if (GameSourceCompiler.IsOwnAssembly(name))
                    continue;
                parts.Add(name + ":" + File.GetLastWriteTimeUtc(refPath).Ticks);
            }

            // The seed set (mod's referenced game types) drives which source files become
            // real vs stubbed. It derives from the mod source + game source + DLLs, all of
            // which are already hashed above, so a mod edit that adds/removes a game API
            // call changes this stamp and invalidates the cache. We deliberately do NOT
            // call ComputeSeedTypes() here - it is ~4 min and would make every cache check
            // (and thus every analyze.ps1 run) slow.
            foreach (var modSrc in ModCompiler.EnumerateSources())
                parts.Add("mod:" + modSrc + ":" + File.GetLastWriteTimeUtc(modSrc).Ticks);
        }

        private static string ComputeStamp()
        {
            using var sha = SHA256.Create();
            var parts = new List<string>();
            CollectSharedInputParts(parts);
            var joined = string.Join("\n", parts);
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(joined));
            return Convert.ToBase64String(hash);
        }
    }
}
