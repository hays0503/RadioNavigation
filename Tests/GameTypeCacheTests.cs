using System;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// GameTypeCache contract: cached assembly-qualified Type resolution with
    /// a probe list (dedups RadioStoryIntegration.ResolveType and
    /// SkyVisibilityResolver.typeCache). The default probe pair is the game
    /// assemblies; tests pass explicit probes so they run offline.
    /// </summary>
    public sealed class GameTypeCacheTests
    {
        private static string HostAssembly()
        {
            return typeof(string).Assembly.GetName().Name;
        }

        [Fact]
        public void Resolve_KnownType_ReturnsType()
        {
            GameTypeCache.Clear();

            Type type = GameTypeCache.Resolve("System.String", HostAssembly());

            Assert.Same(typeof(string), type);
        }

        [Fact]
        public void Resolve_UnknownType_ReturnsNullWithoutThrowing()
        {
            GameTypeCache.Clear();

            Type type = GameTypeCache.Resolve("Definitely.Not.A.Type", HostAssembly());

            Assert.Null(type);
        }

        [Fact]
        public void Resolve_FallsThroughProbeList()
        {
            GameTypeCache.Clear();

            Type type = GameTypeCache.Resolve(
                "System.String", "No.Such.Assembly", HostAssembly());

            Assert.Same(typeof(string), type);
        }

        [Fact]
        public void Resolve_CachesResult_SecondCallSameAnswer()
        {
            GameTypeCache.Clear();

            Type first = GameTypeCache.Resolve("System.String", HostAssembly());
            Type second = GameTypeCache.Resolve("System.String", HostAssembly());

            Assert.Same(first, second);
        }

        [Fact]
        public void Resolve_CachesMiss_RepeatedUnknownStaysNull()
        {
            GameTypeCache.Clear();

            Type first = GameTypeCache.Resolve("Definitely.Not.A.Type", HostAssembly());
            Type second = GameTypeCache.Resolve("Definitely.Not.A.Type", HostAssembly());

            Assert.Null(first);
            Assert.Null(second);
        }

        [Fact]
        public void Clear_DropsEntries_ResolveStillWorks()
        {
            GameTypeCache.Resolve("System.String", HostAssembly());

            GameTypeCache.Clear();

            Assert.Same(typeof(string), GameTypeCache.Resolve("System.String", HostAssembly()));
        }

        [Fact]
        public void ResolveDefault_ProbesGameAssemblies_MissingOffline()
        {
            GameTypeCache.Clear();

            Type type = GameTypeCache.Resolve("System.String");

            Assert.Null(type);
        }

        [Fact]
        public void Resolve_MissThenLateLoad_DoesNotStick()
        {
            GameTypeCache.Clear();
            const string assemblyName = "PoisonProbeDynamic";
            const string typeName = "PoisonProbeDynamic.Marker";

            Assert.Null(GameTypeCache.Resolve(typeName, assemblyName));

            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
            ModuleBuilder module = assembly.DefineDynamicModule(assemblyName);
            Type emitted = module.DefineType(
                typeName, TypeAttributes.Public | TypeAttributes.Class).CreateType();

            // In-memory assemblies are invisible to Type.GetType by name;
            // the hook models a game assembly loading after the first miss.
            ResolveEventHandler loader = (s, e) =>
                new AssemblyName(e.Name).Name == assemblyName ? assembly : null;
            AppDomain.CurrentDomain.AssemblyResolve += loader;
            try
            {
                Assert.Same(emitted, GameTypeCache.Resolve(typeName, assemblyName));
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= loader;
            }
        }
    }
}
