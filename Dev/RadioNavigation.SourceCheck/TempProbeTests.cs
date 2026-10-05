using System;
using Xunit;

namespace RadioNavigation.SourceCheck
{
    public class TempProbeTests
    {
        [Fact]
        public void Probe_MissingTypes()
        {
            if (!GameSourceCompiler.HasGameSource) return;
            foreach (var t in new[]
            {
                "InjectAttribute", "ISingleton", "TaggedInjectAttribute",
                "TOD_Sky", "TOD_Animation", "TOD_AmbientType", "TOD_Util",
                "Blake2B", "Hasher", "Slicer", "Plane", "TextureRegion",
                "LipSync", "LipSyncData", "BlendSystem", "PhonemeMarker", "UnluckAnimatedMesh",
                "VirtualSceneTransform", "IOpaqueImageEffect", "VirtualSceneRenderer",
            })
            {
                Console.WriteLine($"  {t} -> {GameSourceCompiler.FindAssemblyForType(t) ?? "(NOT FOUND)"}");
            }
        }
    }
}
