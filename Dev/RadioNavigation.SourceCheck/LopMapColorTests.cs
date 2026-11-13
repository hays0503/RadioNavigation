using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace RadioNavigation.SourceCheck
{
    /// <summary>Binding contract justified by the live A/B on 2026-11-12:
    /// same RawImage tint, native route material -> 0 blue pixels; default UI
    /// material -> 111 blue pixels. Later temporal repro exposed depth loss:
    /// local LEqual hides the line; local Always restores stable blue coverage
    /// (min/max >=0.99). Pin owned copy, depth and cleanup, not GPU output.
    /// Remove/change the gate if later shader acceptance replaces this path.</summary>
    public sealed class LopMapColorTests
    {
        private static MethodDeclarationSyntax Style(string name)
        {
            string source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),
                "Source", "UI", "SextantLopMapLines.cs"));
            return CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
                .OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == name);
        }

        [Fact]
        public void LopMapColor_LineAndEndpointsUseOwnedDepthIndependentMaterial()
        {
            foreach (string name in new[] { "StyleLine", "StylePoint" })
                Assert.Contains(Style(name).DescendantNodes().OfType<InvocationExpressionSyntax>(),
                    call => call.Expression.ToString() == "SextantLopMapMaterial.Apply"
                        && call.ArgumentList.Arguments.Single().ToString() == "image");
            var bind = Style("Bind");
            Assert.Contains(bind.DescendantNodes().OfType<ObjectCreationExpressionSyntax>(),
                creation => creation.Type.ToString() == "Material"
                    && creation.ArgumentList.Arguments.Single().ToString() == "Graphic.defaultGraphicMaterial");
            Assert.Contains(bind.DescendantNodes().OfType<InvocationExpressionSyntax>(),
                call => call.Expression.ToString() == "ownedMaterial.SetInt"
                    && call.ArgumentList.Arguments[0].ToString() == "\"unity_GUIZTestMode\""
                    && call.ArgumentList.Arguments[1].ToString() == "(int)UnityEngine.Rendering.CompareFunction.Always");
            Assert.Contains(Style("OnDestroy").DescendantNodes().OfType<InvocationExpressionSyntax>(),
                call => call.Expression.ToString() == "Destroy"
                    && call.ArgumentList.Arguments.Single().ToString() == "ownedMaterial");
        }

        [Fact]
        public void LopMapColor_LabelsKeepTheFontAtlasButNotTheMapLightingPreset()
        {
            Assert.Contains(Style("StylePoint").DescendantNodes().OfType<AssignmentExpressionSyntax>(),
                a => a.Left.ToString() == "label.fontSharedMaterial" && a.Right.ToString() == "label.font.material");
        }
    }
}
