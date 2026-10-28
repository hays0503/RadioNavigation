using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace RadioNavigation.SourceCheck
{
    /// <summary>Call-site/argument guards, not proof of pixels. Geometry, phase
    /// coverage and the real capture/codec path have executable unit regressions.
    /// The retired compensation gate enforced the enlargement itself and missed
    /// both a disconnected provider and a zeroed stored diameter.</summary>
    public sealed class CelestialDiscSourceTests
    {
        [Fact]
        public void CelestialDisc_SolarSizeAndLimbUsePhysicalGeometry()
        {
            var sun = Source("Logic/SunDiscRenderer.cs");
            var late = Method(sun, "LateUpdate");
            Assigns(late, "AngularDiameterDeg", "ConfiguredTableDiameterDeg");
            Var(late, "size", "(float)CelestialDiscRaster.QuadSide(radius, AngularDiameterDeg)");
            Assigns(late, "transform.localScale", "new Vector3(size, size, 1f)");
            var awake = Method(sun, "Awake");
            Var(awake, "radiance", "(float)SextantShadeFilter.SolarSceneRadiance");
            Call(awake, "material.SetColor", "\"_Color\"", "new Color(radiance, radiance, radiance, 1f)");
            var texture = Method(sun, "RebuildTexture");
            Var(texture, "alpha", "(float)CelestialDiscRaster.Coverage(r, TextureSize)");
            Assert.DoesNotContain(sun.DescendantTokens(), t => t.ValueText == "RenderMagnification");
            // The solar size must not retune the separately accepted lamp gate.
            var lens = Method(Source("UI/SternspreizLensEffect.cs"), "UpdateInteriorProjection");
            Var(lens, "interiorCompactDiameterDeg", "2.153125");
            Call(lens, "SternspreizLineProfile.PixelsForAngle", "interiorCompactDiameterDeg", "pipCamera.fieldOfView", "targetWidth");
            var controller = Source("UI/SextantController.cs");
            Assigns(Method(controller, "AttachAndConfigureCameras"), "SunDiscRenderer.Active", "true");
            Assigns(Method(controller, "SetFilters"), "SunDiscRenderer.Active", "IsOpen");
            Assigns(Method(controller, "SetFiltersForProbe"), "SunDiscRenderer.Active", "IsOpen");
            var glare = Method(Source("UI/SextantGlareCoordinator.cs"), "Update");
            Assert.DoesNotContain(glare.DescendantNodes().OfType<AssignmentExpressionSyntax>(),
                a => Equivalent(a.Left, "SunDiscRenderer.SunDirection"));
            Assigns(Method(controller, "UpdateTableSun"), "SunDiscRenderer.SunDirection", "solarOptics.Direction");
            Call(Method(controller, "LateUpdate"), "UpdateTableSun");
        }

        [Fact]
        public void CelestialDisc_CaptureProviderIsConnectedAndBodyGated()
        {
            var controller = Source("UI/SextantController.cs");
            Assigns(Method(controller, "Awake"), "inputPoller.SunDiscSightingProvider", "CaptureSunDiscSighting");
            var provider = Method(controller, "CaptureSunDiscSighting");
            Assert.Contains(provider.DescendantNodes().OfType<BinaryExpressionSyntax>(),
                b => Equivalent(b, "body.Kind != CelestialBodyKind.Sun"));
            Call(provider, "SextantCelestialRenderScopePatch.IsRegistered", "pipFactory.RightCamera");
            Call(provider, "solarOptics.Sighting", "ready");
            var poller = Source("UI/SextantInputPoller.cs");
            Call(Method(poller, "CaptureSunDiscSighting"), "provider", "body");
            Var(Method(poller, "PollFix"), "ho", "arcAngle");
            Call(Method(poller, "PollFix"), "worksheet.CaptureObservation", "starId.IdentifiedBody", "ho",
                "sigmaArcMin", "samples", "rejected", "CaptureSunDiscSighting(starId.IdentifiedBody)");
            Call(Method(Source("UI/SextantProbe.cs"), "Record"), "controller.CaptureSunDiscSighting", "body");
            var worksheet = Source("UI/SextantWorksheet.cs");
            var capture = worksheet.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(m => m.Identifier.Text == "CaptureObservation" && m.ParameterList.Parameters.Count == 6);
            Assigns(capture, "disc", "disc.ForBody(body)");
            Var(capture, "ho", "SextantArc.Quantize(SunDiscSighting.ApplyMeasuredChain(hoDeg, indexErrorArcMin))");
            Call(capture, "TryBuildCapture", "body", "utc", "hoDeg", "ho", "lat", "lon", "indexErrorArcMin", "disc", "out obs");
            var build = Method(worksheet, "TryBuildCapture");
            Call(build, "RunningFixCalculator.BuildObservation", "utc", "legacyHo", "lat", "lon", "body");
            Call(build, "Sh5SolarNavigation.TryObservation", "utc", "rawHs", "index", "lat", "lon", "disc", "out obs");
            Assigns(capture, "obs.ObservedSunAngularDiameterDeg", "disc.ObservedDiameterDeg");
            Assigns(capture, "obs.SunLimb", "disc.Limb");
            Assigns(capture, "obs.SunDiscCompensationArcMin", "0.0");
            Assert.DoesNotContain(worksheet.DescendantTokens(), t => t.ValueText == "SunDiscRenderer");
        }

        [Fact]
        public void CelestialDisc_MoonSizeOpacityAndDiagnosticMatchTheRenderer()
        {
            var moon = Source("Logic/MoonDiscRenderer.cs");
            var late = Method(moon, "LateUpdate");
            Call(late, "MoonDiscScale.AngularDiameterDeg", "jd", "altRad");
            Var(late, "size", "(float)CelestialDiscRaster.QuadSide(radius, diameter)");
            Assigns(late, "transform.localScale", "new Vector3(size, size, 1f)");
            var bake = Method(moon, "RebuildTexture");
            Var(bake, "coverage", "MoonPhaseDisc.BuildCoverageField(TextureSize)");
            Var(bake, "alphaByte", "(byte)(coverage[i] * 255f + 0.5f)");
            Assigns(late, "material.color", "new Color(visibility, visibility, visibility, 1f)");
            Call(Method(Source("UI/SextantController.cs"), "TryGetOwnMoon"), "AstroMath.MoonTopocentricRaDec",
                "jd", "lat", "lstDeg", "out double raRad", "out double decRad");
        }

        [Fact]
        public void CelestialDisc_NativeSuppressionIsScopedAndRestored()
        {
            var scope = Source("Logic/SextantCelestialRenderScopePatch.cs");
            var install = Method(scope, "Apply");
            Call(install, "Patch", "harmony", "pipeline", "\"RenderDeferredCamera\"");
            Call(install, "Patch", "harmony", "pipeline", "\"RenderForwardCamera\"");
            Call(Method(Source("Logic/HarmonyPatchInstaller.cs"), "Apply"), "SextantCelestialRenderScopePatch.Apply", "harmony");
            // Finalizer not just postfix: exception paths must restore as well.
            Call(Method(scope, "Patch"), "harmony.Patch", "target",
                "new HarmonyMethod(typeof(SextantCelestialRenderScopePatch), nameof(BeforeCamera))",
                "new HarmonyMethod(typeof(SextantCelestialRenderScopePatch), nameof(AfterCamera))");
            Assert.Contains(Method(scope, "Patch").DescendantNodes().OfType<ArgumentSyntax>(),
                a => a.NameColon != null && a.NameColon.Name.Identifier.Text == "finalizer");
            Call(Method(scope, "BeforeCamera"), "FindSources", "__1");
            foreach (string body in new[] { "Sun", "Moon" })
                Call(Method(scope, "BeforeCamera"), "ReplacementReady",
                    "ReadRenderer(sources, \"Measuring" + body + "\")", "__1");
            Assert.Contains(Method(scope, "ReplacementReady").DescendantNodes().OfType<BinaryExpressionSyntax>(),
                b => Equivalent(b, "(pip.cullingMask & (1 << renderer.gameObject.layer)) != 0"));
            Call(Method(scope, "BeforeCamera"), "CelestialRenderState.Begin", "sun", "replaceSun", "moon", "replaceMoon", "sunFlare");
            Var(Method(scope, "BeforeCamera"), "sunFlare", "ReadRenderer(sources, \"NativeSunFlare\")");
            Call(Method(scope, "Register"), "Write", "sources", "\"NativeSunFlare\"", "FindSunFlareRenderer(nativeSun)");
            var finder = Method(scope, "FindSunFlareRenderer");
            Call(finder, "nativeSun.GetComponentsInChildren<MonoBehaviour>", "true");
            Assert.Contains(finder.DescendantNodes().OfType<BinaryExpressionSyntax>(),
                b => Equivalent(b, "component.GetType().Name != \"HDRPLensFlare\""));
            Assert.DoesNotContain(finder.DescendantTokens(), t => t.ValueText == "FindObjectsOfType");
            Call(Method(scope, "AfterCamera"), "__state.Restore");
            Assert.Contains(Method(scope, "FindSources").DescendantNodes().OfType<BinaryExpressionSyntax>(),
                b => Equivalent(b, "pip.targetTexture == null"));
            var controller = Source("UI/SextantController.cs");
            Call(Method(controller, "UpdatePipPose"), "UpdateCelestialSources");
            foreach (string side in new[] { "Left", "Right" })
            {
                Call(Method(controller, "UpdateCelestialSources"), "SextantCelestialRenderScopePatch.Register",
                    "pipFactory." + side + "Camera", "nativeSun", "nativeMoon", "SunDiscRenderer.MeasuringRenderer", "MoonDiscRenderer.MeasuringRenderer");
                Call(Method(Source("UI/SextantPipFactory.cs"), "Destroy"), "SextantCelestialRenderScopePatch.Unregister", side + "Camera");
            }
        }

        [Fact]
        public void SolarAudit_IsReadOnlyAndSharesOneClockAndPosition()
        {
            var controller = Source("UI/SextantController.cs");
            var sample = Method(controller, "SolarAuditSnapshotForProbe");
            Call(sample, "GameTimeResolver.GetUtcNow");
            Call(sample, "TryGetPositionForProbe", "out lat", "out lon");
            Call(sample, "SolarSightAudit.Compute", "utc", "lat", "lon", "0.0");
            Call(sample, "SolarSightAudit.Compute", "utc", "lat", "lon", "delta");
            Call(sample, "IndexErrorCorrection.Load");
            Call(sample, "SolarSightAudit.Number", "arcAngle + index / 60.0");
            Call(sample, "SunDiscSighting.ApplyMeasuredChain", "arcAngle", "index");
            Call(sample, "SextantArc.Quantize", "SunDiscSighting.ApplyMeasuredChain(arcAngle, index)");
            Assert.Single(sample.DescendantNodes().OfType<InvocationExpressionSyntax>(),
                i => Equivalent(i.Expression, "GameTimeResolver.GetUtcNow"));
            Call(Method(Source("UI/SextantProbe.cs"), "Snapshot"), "AppendSolarAudit", "sb", "controller");
            Call(Method(Source("UI/SextantProbe.cs"), "AppendSolarAudit"), "controller.SolarAuditSnapshotForProbe");
            var compute = Method(Source("Logic/SolarSightAudit.cs"), "Compute");
            foreach (var method in new[] { sample, Method(controller, "AppendSolarSourceAudit"),
                Method(controller, "AppendSolarCameraAudit"), compute })
            {
                foreach (var call in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    string name = call.Expression.ToString();
                    Assert.DoesNotContain("SetDelta", name);
                    Assert.DoesNotContain("CaptureObservation", name);
                    Assert.DoesNotContain("ResetSeries", name);
                    Assert.DoesNotContain("PlayerPrefs.Set", name);
                    Assert.DoesNotContain(".Save", name);
                    Assert.DoesNotContain(".Render", name);
                }
                Assert.DoesNotContain(method.DescendantNodes().OfType<AssignmentExpressionSyntax>(),
                    a => a.Left is MemberAccessExpressionSyntax);
                // A bare field write (arcAngle = 0, etc.) must not slip past the
                // member-access check. Object initializers in pure Compute own
                // their returned value; everything else must target a local.
                var locals = method.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                    .Select(v => v.Identifier.ValueText).ToArray();
                foreach (var assignment in method.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                {
                    if (assignment.Parent is InitializerExpressionSyntax)
                    {
                        Assert.Same(compute, method);
                        continue; // Compute's Sample value initializer, never shared game state.
                    }
                    var identifier = assignment.Left as IdentifierNameSyntax;
                    Assert.NotNull(identifier);
                    Assert.Contains(identifier.Identifier.ValueText, locals);
                }
            }
        }

        [Fact]
        public void Sh5_OpticalAdapterFeedsTheLateRendererWithPlainUtcAndMeanWater()
        {
            var controller = Source("UI/SextantController.cs");
            var late = Method(controller, "LateUpdate");
            Call(late, "UpdatePipPose");
            Call(late, "UpdateTableSun");
            var calls = late.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
            Assert.True(calls.Single(c => Equivalent(c.Expression, "UpdatePipPose")).SpanStart
                < calls.Single(c => Equivalent(c.Expression, "UpdateTableSun")).SpanStart);
            var update = Method(controller, "UpdateTableSun");
            Call(update, "solarOptics.Refresh", "viewCamera", "positionOk && outside", "GameTimeResolver.GetUtcNow()", "lat", "lon");
            Assigns(update, "SunDiscRenderer.SunDirection", "solarOptics.Direction");
            Assigns(update, "SunDiscRenderer.ConfiguredTableDiameterDeg", "solarOptics.Ready ? solarOptics.DiameterDeg : 0");
            var refresh = Method(Source("UI/SextantSolarOptics.cs"), "Refresh");
            Var(refresh, "eye", "camera.transform.position.y - water.transform.position.y");
            Var(refresh, "curvature", "Shader.GetGlobalFloat(\"_EarthCurvatureCompensation\")");
            Call(refresh, "SolarSeaHorizon.TryAltitude", "eye", "SextantPipClip.FarPlane", "curvature == 1f", "out horizon");
            Var(refresh, "diameter", "SunDiscScale.AngularDiameterDeg(AstroMath.ToJulianDay(utc) + SunEphemerisCalibration.DeltaTSeconds / 86400)");
            Call(refresh, "Sh5SolarNavigation.TryApparentCentre", "utc", "lat", "lon", "eye", "horizon", "diameter", "out alt", "out az");
            Assert.DoesNotContain(refresh.DescendantNodes().OfType<InvocationExpressionSyntax>(),
                c => c.Expression.ToString().Contains("Calibrated") || c.Expression.ToString().Contains("JdTt"));
            Call(Method(Source("Logic/ModBootstrapper.cs"), "OnLoaded"), "Sh5SunAlmanac.Initialize", "lighthouseDataDirectory");
        }

        [Fact]
        public void Sh5_CaptureUsesFrozenSourceTimePositionAndUnquantizedRawDrum()
        {
            var controller = Source("UI/SextantController.cs");
            var provider = Method(controller, "CaptureSunDiscSighting");
            Assert.Contains(provider.DescendantNodes().OfType<BinaryExpressionSyntax>(),
                b => Equivalent(b, "Time.frameCount - solarOptics.ConfiguredFrame <= 1"));
            var worksheet = Source("UI/SextantWorksheet.cs");
            var capture = worksheet.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(m => m.Identifier.Text == "CaptureObservation" && m.ParameterList.Parameters.Count == 6);
            var frame = Method(worksheet, "TryCaptureFrame");
            Assigns(frame, "utc", "disc.ConfiguredUtc");
            Assigns(frame, "lat", "disc.TableRequested ? disc.ObserverLatDeg : latF");
            Assigns(frame, "lon", "disc.TableRequested ? disc.ObserverLonDeg : lonF");
            Call(capture, "TryCaptureFrame", "body", "disc", "out utc", "out lat", "out lon");
            Call(capture, "TryBuildCapture", "body", "utc", "hoDeg", "ho", "lat", "lon", "indexErrorArcMin", "disc", "out obs");
            var build = Method(worksheet, "TryBuildCapture");
            Assert.Contains(build.DescendantNodes().OfType<IfStatementSyntax>(),
                i => Equivalent(i.Condition, "body.Kind == CelestialBodyKind.Sun"));
            Call(build, "HasLegacySun", "runningFix.Observation1");
            Call(build, "HasLegacySun", "runningFix.Observation2");
            Call(build, "Sh5SolarNavigation.TryObservation", "utc", "rawHs", "index", "lat", "lon", "disc", "out obs");
            var observe = Method(Source("Logic/Sh5SolarNavigation.cs"), "TryObservation");
            Call(observe, "SolarHeightReduction.TryReduce", "rawHsDeg", "indexArcMin", "disc.EyeHeightM", "utc.Month", "disc.Limb", "out ho", "out correction");
        }

        [Fact]
        public void Sh5_SunIdentificationReadoutGlareAndCalibrationHaveNoNativeFallback()
        {
            var controller = Source("UI/SextantController.cs");
            var id = Method(controller, "UpdateStarId");
            Call(id, "TryGetMeasuringSun", "out ownDir", "out ownAlt");
            Call(id, "starId.Update", "dt", "glare.IsPained", "canSight",
                "SextantPipPose.Forward(pipFactory, viewCamera, gameMainCamera, arcAngle)",
                "ownOk", "ownDir", "ownAlt", "false", "Vector3.zero", "0f", "posOk", "lat", "lon", "nameText", "arcAngle", "daylightAlt");
            Call(Method(Source("UI/SextantStarId.cs"), "TickIdentification"), "SightabilityGate.Evaluate", "lastFoundBody.Kind", "bodyAlt", "daylightSunAlt", "illumination");
            var glare = Method(Source("UI/SextantGlareCoordinator.cs"), "Update");
            Call(glare, "Vector3.Angle", "frame.RightCamera.transform.forward", "frame.TableDir");
            Call(glare, "SextantGlare.Glare", "angle", "frame.TableAlt", "frame.RightFilterCount", "frame.RightCamera.fieldOfView");
            var calibration = Method(Source("UI/SextantInputPoller.cs"), "PollIndexError");
            Assigns(calibration, "alt", "(float)sun.ExpectedHsDeg");
            Call(Method(Source("UI/SextantStarId.cs"), "UpdateAimDebug"), "SunDiscSighting.ApplyMeasuredChain", "arcAngle", "IndexErrorCorrection.Load()");
            Call(Method(Source("UI/SextantBodiesReadout.cs"), "TrySunAltAz"), "Sh5SolarNavigation.TryApparentCentre", "sun.ConfiguredUtc", "sun.ObserverLatDeg", "sun.ObserverLonDeg", "sun.EyeHeightM", "sun.HorizonAltitudeDeg", "sun.ObservedDiameterDeg", "out altitude", "out azimuth");
            Call(Method(Source("Logic/CelestialForecast.cs"), "TryBodyAzimuth"), "Sh5SolarNavigation.TryCentre", "utc", "latDeg", "lonDeg", "out altitude", "out azimuthDeg");
        }

        [Fact]
        public void Sh5_DebugIndexUnitsAreArcminutesForSolarAndOtherBodies()
        {
            var debug = Method(Source("UI/SextantStarId.cs"), "UpdateAimDebug");
            Call(debug, "AimOffsetFormat.ToArcMin", "bodyAlt - SunDiscSighting.ApplyMeasuredChain(arcAngle, IndexErrorCorrection.Load())");
            Call(debug, "AimOffsetFormat.ToArcMin", "sun.ExpectedHsDeg - SunDiscSighting.ApplyMeasuredChain(arcAngle, IndexErrorCorrection.Load())");
        }

        private static SyntaxNode Source(string relative)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(ModCompiler.ModRoot,
                "Source", relative)), ModCompiler.GameParityParseOptions);
            Assert.DoesNotContain(tree.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
            return tree.GetRoot();
        }

        private static MethodDeclarationSyntax Method(SyntaxNode root, string name)
        {
            return root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(m => m.Identifier.Text == name);
        }

        private static bool Equivalent(SyntaxNode node, string expression)
        {
            // ParseExpression can mark a leading contextual token differently
            // from a node in a full method. Compare actual token kinds/text,
            // excluding trivia: comments or disconnected names cannot satisfy it.
            return node.DescendantTokens().Select(t => (t.RawKind, t.Text))
                .SequenceEqual(SyntaxFactory.ParseExpression(expression).DescendantTokens()
                    .Select(t => (t.RawKind, t.Text)));
        }

        private static void Assigns(SyntaxNode method, string target, string expression)
        {
            Assert.Contains(method.DescendantNodes().OfType<AssignmentExpressionSyntax>(),
                a => Equivalent(a.Left, target) && Equivalent(a.Right, expression));
        }

        private static void Var(SyntaxNode method, string name, string expression)
        {
            Assert.Contains(method.DescendantNodes().OfType<VariableDeclaratorSyntax>(),
                v => v.Identifier.Text == name && v.Initializer != null && Equivalent(v.Initializer.Value, expression));
        }

        private static void Call(SyntaxNode method, string name, params string[] args)
        {
            var expected = SyntaxFactory.ParseArgumentList("(" + string.Join(", ", args) + ")").Arguments;
            Assert.Contains(method.DescendantNodes().OfType<InvocationExpressionSyntax>(),
                i => Equivalent(i.Expression, name) && i.ArgumentList.Arguments.Count == args.Length
                    && i.ArgumentList.Arguments.Select((a, n) =>
                        a.Expression.DescendantTokens().Select(t => (t.RawKind, t.Text))
                            .SequenceEqual(expected[n].Expression.DescendantTokens().Select(t => (t.RawKind, t.Text)))
                        && a.RefKindKeyword.RawKind == expected[n].RefKindKeyword.RawKind).All(match => match));
        }
    }
}
