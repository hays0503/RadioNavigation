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

        private static string ComputeStamp()
        {
            using var sha = SHA256.Create();
            var parts = new List<string>();

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

            var joined = string.Join("\n", parts);
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(joined));
            return Convert.ToBase64String(hash);
        }
    }
}
