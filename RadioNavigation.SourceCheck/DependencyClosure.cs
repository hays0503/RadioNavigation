using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RadioNavigation.SourceCheck
{
    /// <summary>
    /// Computes the NARROW signature-only reference for the mod's game-API surface.
    ///
    /// The full decompiled game source does not compile cleanly in any subset that
    /// contains the mod's real API surface: the mod's direct types (Story, PlayerShip,
    /// Weather, ...) transitively require nearly the whole game, and the decompiled
    /// mission/character/behaviour subsystems carry many artifacts. Pulling in the real
    /// files for those transitive deps explodes to hundreds of broken files.
    ///
    /// Instead we compile ONLY the mod's DIRECT game types (the seed set) and their
    /// base types, with METHOD BODIES STRIPPED (signatures/fields/properties/base types
    /// kept), and STUB every other referenced type (empty class in the correct
    /// namespace). Stripping bodies removes the cascade: the seed files' signatures only
    /// reference their direct type deps, which are bounded. The result is a small, clean
    /// reference that exposes the real API names and signatures the mod actually calls,
    /// without dragging in broken subsystems.
    ///
    /// The mod compiles against this reference: any call to a hallucinated member on a
    /// seed type (real signature) or on a stubbed type (empty) fails to compile.
    /// </summary>
    internal static class DependencyClosure
    {
        /// <summary>
        /// Game types the mod references, extracted from the obfuscated game DLL.
        /// Returns empty when the obfuscated DLL or mod source is unavailable.
        /// </summary>
        public static ImmutableArray<(string Name, int Arity)> ComputeSeedTypes()
        {
            var obfuscated = Path.Combine(GameSourceCompiler.ManagedDir, "com.uboat.game.dll");
            if (!File.Exists(obfuscated) || !ModCompiler.HasModSource)
                return ImmutableArray<(string, int)>.Empty;

            var trees = ModCompiler.EnumerateSources()
                .Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), path: p))
                .ToList();

            var seeds = new Dictionary<string, int>(StringComparer.Ordinal);

            // Obfuscation renames/removes some game types the mod uses (e.g.
            // GenericCameraController, Periscope), so a semantic pass against the
            // obfuscated DLL misses them. Instead, use a syntax pass: any simple type
            // name the mod uses that exists in the game SOURCE becomes a seed, so the
            // clean reference includes it as real. (A semantic pass was previously used
            // but is redundant — every type it found also exists in the game source, so
            // the syntax pass covers it.)
            var sourceTypeNames = BuildSourceTypeNameSet();
            foreach (var tree in trees)
            {
                foreach (var node in tree.GetRoot().DescendantNodes())
                {
                    if (node is IdentifierNameSyntax idn && sourceTypeNames.Contains(idn.Identifier.Text))
                        seeds[idn.Identifier.Text] = 0;
                    else if (node is GenericNameSyntax gn && sourceTypeNames.Contains(gn.Identifier.Text))
                        seeds[gn.Identifier.Text] = gn.TypeArgumentList.Arguments.Count;
                }
            }

            // Member-access chains (e.g. gameMainCamera.GenericController.ExternalController)
            // reference types that never appear as a literal identifier in the mod source
            // (GenericCameraController is only the return type of MainCamera.GenericController).
            // Resolve each chain against the game-source member index, starting from the
            // current seeds, and add every resolved member type as a seed. Iterate to a
            // fixed point so deep chains (A.B.C.D) are fully covered.
            var memberIndex = BuildMemberIndex();
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var tree in trees)
                {
                    foreach (var chain in tree.GetRoot().DescendantNodes()
                                 .OfType<MemberAccessExpressionSyntax>())
                    {
                        var segments = FlattenChain(chain);
                        if (segments.Count < 2) continue;
                        // Try every seed as the chain root; a seed whose first member
                        // matches starts a valid walk.
                        foreach (var seedName in seeds.Keys.ToList())
                        {
                            var curType = seedName;
                            var ok = true;
                            for (int i = 1; i < segments.Count; i++)
                            {
                                var mtype = ResolveMemberType(memberIndex, curType, segments[i]);
                                if (mtype.Length == 0)
                                {
                                    ok = false;
                                    break;
                                }
                                if (sourceTypeNames.Contains(mtype) && !seeds.ContainsKey(mtype))
                                {
                                    seeds[mtype] = 0;
                                    changed = true;
                                }
                                curType = mtype;
                            }
                        }
                    }
                }
            }

            return seeds.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => (kv.Key, kv.Value)).ToImmutableArray();
        }

        /// <summary>
        /// The OLD seed-discovery algorithm (semantic pass against the obfuscated DLL +
        /// syntax pass + member-chain expansion), kept only for the quality-regression
        /// test SeedQuality_NewVsOld. The semantic pass was removed from
        /// ComputeSeedTypes because it was redundant (~205s) — this method proves the
        /// new seed set is identical.
        /// </summary>
        public static ImmutableArray<(string Name, int Arity)> ComputeSeedTypesWithSemanticPass()
        {
            var obfuscated = Path.Combine(GameSourceCompiler.ManagedDir, "com.uboat.game.dll");
            if (!File.Exists(obfuscated) || !ModCompiler.HasModSource)
                return ImmutableArray<(string, int)>.Empty;

            var refs = new List<MetadataReference>();
            refs.Add(MetadataReference.CreateFromFile(obfuscated));
            foreach (var name in ModCompiler.ModRefNames)
            {
                var path = Path.Combine(GameSourceCompiler.ManagedDir, name);
                if (File.Exists(path))
                    refs.Add(MetadataReference.CreateFromFile(path));
            }

            var trees = ModCompiler.EnumerateSources()
                .Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), path: p))
                .ToList();
            var compilation = ModCompiler.CreateCompilation(trees, refs);

            var seeds = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var tree in trees)
            {
                var model = compilation.GetSemanticModel(tree);
                foreach (var node in tree.GetRoot().DescendantNodes())
                {
                    var info = model.GetSymbolInfo(node);
                    var sym = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
                    if (sym is INamedTypeSymbol type &&
                        type.ContainingAssembly?.Name == "com.uboat.game")
                    {
                        seeds[type.Name] = type.Arity;
                    }
                }
            }

            var sourceTypeNames = BuildSourceTypeNameSet();
            foreach (var tree in trees)
            {
                foreach (var node in tree.GetRoot().DescendantNodes())
                {
                    if (node is IdentifierNameSyntax idn && sourceTypeNames.Contains(idn.Identifier.Text))
                        seeds[idn.Identifier.Text] = 0;
                    else if (node is GenericNameSyntax gn && sourceTypeNames.Contains(gn.Identifier.Text))
                        seeds[gn.Identifier.Text] = gn.TypeArgumentList.Arguments.Count;
                }
            }

            var memberIndex = BuildMemberIndex();
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var tree in trees)
                {
                    foreach (var chain in tree.GetRoot().DescendantNodes()
                                 .OfType<MemberAccessExpressionSyntax>())
                    {
                        var segments = FlattenChain(chain);
                        if (segments.Count < 2) continue;
                        foreach (var seedName in seeds.Keys.ToList())
                        {
                            var curType = seedName;
                            var ok = true;
                            for (int i = 1; i < segments.Count; i++)
                            {
                                var mtype = ResolveMemberType(memberIndex, curType, segments[i]);
                                if (mtype.Length == 0)
                                {
                                    ok = false;
                                    break;
                                }
                                if (sourceTypeNames.Contains(mtype) && !seeds.ContainsKey(mtype))
                                {
                                    seeds[mtype] = 0;
                                    changed = true;
                                }
                                curType = mtype;
                            }
                        }
                    }
                }
            }

            return seeds.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => (kv.Key, kv.Value)).ToImmutableArray();
        }

        /// <summary>Flattens a member-access chain into its identifier segments
        /// (a.b.c.d -> [a, b, c, d]).</summary>
        private static List<string> FlattenChain(MemberAccessExpressionSyntax chain)
        {
            var segs = new List<string>();
            ExpressionSyntax expr = chain;
            while (expr is MemberAccessExpressionSyntax ma)
            {
                segs.Insert(0, ma.Name.Identifier.Text);
                expr = ma.Expression;
            }
            if (expr is IdentifierNameSyntax idn)
                segs.Insert(0, idn.Identifier.Text);
            else if (expr is GenericNameSyntax gn)
                segs.Insert(0, gn.Identifier.Text);
            else if (expr is ThisExpressionSyntax)
                segs.Insert(0, "this");
            else if (expr is InvocationExpressionSyntax inv &&
                     inv.Expression is IdentifierNameSyntax invId)
                segs.Insert(0, invId.Identifier.Text);
            return segs;
        }

        /// <summary>All type simple names declared anywhere in the game source.</summary>
        public static HashSet<string> BuildSourceTypeNameSet()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in GameSourceCompiler.EnumerateSources())
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot();
                foreach (var decl in root.DescendantNodes()
                             .Where(n => n is BaseTypeDeclarationSyntax || n is DelegateDeclarationSyntax))
                {
                    var id = decl is BaseTypeDeclarationSyntax b ? b.Identifier.Text
                        : ((DelegateDeclarationSyntax)decl).Identifier.Text;
                    names.Add(id);
                }
            }
            return names;
        }

        /// <summary>
        /// Enumerates the simple type names declared in an assembly (via Roslyn metadata,
        /// which handles obfuscated assemblies). Used by the quality probe
        /// SeedQuality_DllOnlyTypesAreBounded to bound the DLL-only type set.
        /// </summary>
        public static ImmutableArray<string> EnumerateAssemblyTypeNames(string assemblyPath)
        {
            var names = new List<string>();
            var refs = new List<MetadataReference> { MetadataReference.CreateFromFile(assemblyPath) };
            var compilation = CSharpCompilation.Create(
                "probe",
                new[] { CSharpSyntaxTree.ParseText("") },
                refs);
            var assembly = compilation.GetAssemblyOrModuleSymbol(
                compilation.References.First()) as IAssemblySymbol;
            if (assembly == null) return ImmutableArray<string>.Empty;
            foreach (var type in assembly.GlobalNamespace.GetNamespaceMembers()
                         .SelectMany(RecurseNamespaces)
                         .SelectMany(ns => ns.GetTypeMembers()))
            {
                names.Add(type.Name);
            }
            return names.Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal).ToImmutableArray();
        }

        private static IEnumerable<INamespaceSymbol> RecurseNamespaces(INamespaceSymbol ns)
        {
            yield return ns;
            foreach (var child in ns.GetNamespaceMembers())
                foreach (var sub in RecurseNamespaces(child))
                    yield return sub;
        }

        /// <summary>
        /// Builds the narrow signature-only reference. Returns the set of source files to
        /// compile: real seed/base-type files (bodies stripped) + generated stub files,
        /// plus the set of broken using-directive namespaces to strip from real files.
        /// </summary>
        public static (ImmutableArray<string> Files, ImmutableArray<string> BrokenUsings)
            BuildSignatureOnlyReference(
            ImmutableArray<(string Name, int Arity)> seedTypes,
            ImmutableArray<string> allSourceFiles,
            ImmutableArray<MetadataReference> references)
        {
            if (seedTypes.Length == 0)
                return (ImmutableArray<string>.Empty, ImmutableArray<string>.Empty);

            // type simple name -> declaring files (with declared arity).
            var typeToFiles = new Dictionary<string, List<(string Path, int Arity)>>(StringComparer.Ordinal);
            foreach (var path in allSourceFiles)
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)
                    .GetRoot();
                foreach (var decl in root.DescendantNodes()
                             .Where(n => n is BaseTypeDeclarationSyntax || n is DelegateDeclarationSyntax))
                {
                    var id = decl is BaseTypeDeclarationSyntax b ? b.Identifier.Text
                        : ((DelegateDeclarationSyntax)decl).Identifier.Text;
                    var arity = decl is TypeDeclarationSyntax td
                        ? td.TypeParameterList?.Parameters.Count ?? 0
                        : 0;
                    if (!typeToFiles.TryGetValue(id, out var list))
                        typeToFiles[id] = list = new List<(string, int)>();
                    list.Add((path, arity));
                }
            }

            // type simple name -> set of namespaces (full source tree + all managed DLLs).
            var typeNamespaces = BuildTypeNamespaces();
            // All game namespaces that exist anywhere (full source + DLLs). A using whose
            // namespace is NOT here is genuinely broken and can be stripped; a using whose
            // namespace IS here is kept even if it has no real type in the subset, so that
            // stubs placed in that namespace stay reachable.
            var knownGameNamespaces = BuildKnownNamespaces();

            // Types already resolvable from a referenced assembly, keyed by simple name to
            // the set of namespaces they live in. A stub must NOT be placed in a namespace
            // where the real type already exists (CS0436/CS1721), but a same-named type in a
            // DIFFERENT namespace (e.g. BCL System.Reflection.Metadata.Parameter vs the game's
            // UBOAT.Game.Core.Data.Parameter) must not block the game stub.
            var referencedTypeNs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (references.Length > 0)
            {
                try
                {
                    var comp = CSharpCompilation.Create("probe", references: references);
                    foreach (var r in references)
                    {
                        var asm = comp.GetAssemblyOrModuleSymbol(r) as IAssemblySymbol;
                        if (asm == null) continue;
                        foreach (var t in asm.GlobalNamespace.GetTypeMembers())
                            AddReferencedType(referencedTypeNs, t);
                        foreach (var ns in asm.GlobalNamespace.GetNamespaceMembers())
                            WalkReferencedTypes(referencedTypeNs, ns);
                    }
                }
                catch
                {
                    // unreadable reference; ignore
                }
            }

            // Real set: seed types + transitive base types (from source).
            var realByType = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var seed in seedTypes)
            {
                if (typeToFiles.TryGetValue(seed.Name, out var files))
                {
                    var match = files.FirstOrDefault(f => f.Arity == seed.Arity);
                    realByType[seed.Name] = match.Path ?? files[0].Path;
                }
            }

            var baseChanged = true;
            while (baseChanged)
            {
                baseChanged = false;
                foreach (var type in realByType.Keys.ToList())
                {
                    var root = CSharpSyntaxTree.ParseText(
                        File.ReadAllText(realByType[type]), path: realByType[type]).GetRoot();
                    foreach (var baseName in GetBaseTypeNames((CompilationUnitSyntax)root))
                    {
                        if (realByType.ContainsKey(baseName)) continue;
                        if (!typeToFiles.TryGetValue(baseName, out var files)) continue;
                        realByType[baseName] = files[0].Path;
                        baseChanged = true;
                    }
                }
            }

            // Types whose real file is not included -> stub them (empty class in the
            // correct namespace, resolved from the referencing file's context).
            var stubTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            var genericStubTypes = new HashSet<string>(StringComparer.Ordinal);
            var genericArity = new Dictionary<string, int>(StringComparer.Ordinal);
            var enumStubTypes = new HashSet<string>(StringComparer.Ordinal);
            var enumMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var interfaceStubTypes = new HashSet<string>(StringComparer.Ordinal);
            var nestedTypes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            // Stub type name -> set of "Member:Type" property stubs (CS1061 member access on
            // a stub). Type resolved from the real source declaration.
            var stubMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            // Full-source index: type simple name -> (base type names, member name -> type).
            var memberIndex = BuildMemberIndex();

            while (true)
            {
                var trees = new List<SyntaxTree>();
                foreach (var kv in realByType)
                {
                    var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(kv.Value), path: kv.Value);
                    trees.Add(StripBodies(tree));
                }
                foreach (var kv in stubTypes)
                    trees.Add(CSharpSyntaxTree.ParseText(
                        StubSource(kv.Key, kv.Value,
                            genericStubTypes.Contains(kv.Key),
                            kv.Key.EndsWith("Attribute", StringComparison.Ordinal),
                            enumStubTypes.Contains(kv.Key),
                            interfaceStubTypes.Contains(kv.Key),
                            genericArity.TryGetValue(kv.Key, out var ar) ? ar : 0,
                            enumMembers.TryGetValue(kv.Key, out var mems) ? string.Join(",", mems) : "",
                            nestedTypes.TryGetValue(kv.Key, out var nested) ? nested : null,
                            stubMembers.TryGetValue(kv.Key, out var sms) ? sms : null),
                        path: "stub:" + kv.Key));

                var compilation = GameSourceCompiler.CreateCompilation(trees, references);

                var missing = new HashSet<string>(StringComparer.Ordinal);
                var missingNs = new Dictionary<string, string>(StringComparer.Ordinal);
                var genericTypes = new HashSet<string>(StringComparer.Ordinal);
                var enumTypes = new HashSet<string>(StringComparer.Ordinal);
                var interfaceTypes = new HashSet<string>(StringComparer.Ordinal);
                var added = false;
                foreach (var diag in compilation.GetDiagnostics())
                {
                    if (diag.Severity != DiagnosticSeverity.Error) continue;
                    if (!diag.Location.IsInSource) continue;
                    var path = diag.Location.SourceTree.FilePath;
                    if (path == null || path.StartsWith("stub:", StringComparison.Ordinal))
                        continue;
                    if (diag.Id == "CS0308")
                    {
                        // "non-generic type 'X' cannot be used with type arguments"
                        var name = ExtractMissingName(diag);
                        if (name.Length > 0)
                        {
                            genericTypes.Add(name);
                            var arity = ExtractGenericArity(diag);
                            var cur = genericArity.TryGetValue(name, out var existing) ? existing : 0;
                            if (arity > cur)
                                genericArity[name] = arity;
                            var nsG = ResolveNamespace(name, diag.Location.SourceTree, typeNamespaces);
                            if (nsG != null)
                                missingNs[name] = nsG;
                        }
                        continue;
                    }
                    if (diag.Id == "CS0117")
                    {
                        // "'X' does not contain definition for 'Y'" -> X is an enum/class
                        // with members; stub as enum (member access like X.Member).
                        var name = ExtractMissingTypeFromMessage(diag);
                        if (name.Length == 0)
                            name = ExtractMissingName(diag);
                        if (name.Length > 0)
                        {
                            enumTypes.Add(name);
                            var member = ExtractMissingMember(diag);
                            if (member.Length > 0)
                            {
                                if (!enumMembers.TryGetValue(name, out var set))
                                    enumMembers[name] = set = new HashSet<string>(StringComparer.Ordinal);
                                set.Add(member);
                            }
                            var nsE = ResolveNamespace(name, diag.Location.SourceTree, typeNamespaces);
                            if (nsE != null)
                                missingNs[name] = nsE;
                        }
                        continue;
                    }
                    if (diag.Id == "CS0426")
                    {
                        // "Type name 'Y' does not exist in type 'X'" -> X is a stub class
                        // that needs a nested type Y. Emit Y as a nested enum inside X.
                        var nestedName = ExtractMissingTypeFromMessage(diag);
                        var parentName = ExtractMissingNamespaceFromMessage(diag);
                        if (nestedName.Length > 0 && parentName.Length > 0)
                        {
                            if (!nestedTypes.TryGetValue(parentName, out var set))
                                nestedTypes[parentName] = set = new HashSet<string>(StringComparer.Ordinal);
                            set.Add(nestedName);
                            if (!stubTypes.ContainsKey(parentName))
                            {
                                var nsP = ResolveNamespace(parentName, diag.Location.SourceTree, typeNamespaces);
                                if (nsP != null)
                                    stubTypes[parentName] = nsP;
                            }
                            added = true;
                        }
                        continue;
                    }
                    if (diag.Id == "CS1061")
                    {
                        // "'X' does not contain definition for 'M'" -> X is a stub class that
                        // needs a property M. Resolve M's type from the real source (X or its
                        // base chain) and emit `public <T> M { get; }`.
                        var typeName = ExtractMissingTypeFromMessage(diag);
                        var memberName = ExtractMissingMember(diag);
                        if (typeName.Length > 0 && memberName.Length > 0 &&
                            stubTypes.ContainsKey(typeName))
                        {
                            var mtype = ResolveMemberType(memberIndex, typeName, memberName);
                            if (mtype.Length > 0)
                            {
                                // Fully-qualify the member type so the stub compiles regardless
                                // of the stub's namespace (member type may live elsewhere).
                                var fq = QualifyTypeName(typeNamespaces, mtype);
                                if (!stubMembers.TryGetValue(typeName, out var set))
                                    stubMembers[typeName] = set = new HashSet<string>(StringComparer.Ordinal);
                                set.Add(memberName + ":" + fq);
                                added = true;
                            }
                        }
                        continue;
                    }
                    if (diag.Id == "CS0450")
                    {
                        // interface used as a generic constraint -> stub as interface.
                        var name = ExtractMissingName(diag);
                        if (name.Length > 0)
                        {
                            interfaceTypes.Add(name);
                            var nsI = ResolveNamespace(name, diag.Location.SourceTree, typeNamespaces);
                            if (nsI != null)
                                missingNs[name] = nsI;
                        }
                        continue;
                    }
                    if (diag.Id != "CS0246" && diag.Id != "CS0234" && diag.Id != "CS0103")
                        continue;
                    if (diag.Id == "CS0234")
                    {
                        // Two cases: (a) a broken `using X.Y;` where X.Y holds no real type in
                        // the subset (handled by StripBrokenUsings); (b) a fully-qualified type
                        // reference X.Y.Type where Type must be stubbed into X.Y. Distinguish by
                        // whether the diagnostic sits on a using directive.
                        var onUsing = diag.Location.SourceTree.GetRoot()
                            .FindNode(diag.Location.SourceSpan)
                            .AncestorsAndSelf().OfType<UsingDirectiveSyntax>().Any();
                        if (onUsing)
                            continue;
                        var typeName = ExtractMissingTypeFromMessage(diag);
                        var nsName = ExtractMissingNamespaceFromMessage(diag);
                        // If "nsName.typeName" is itself a known namespace, then typeName is a
                        // NAMESPACE segment (e.g. UBOAT.Game.Scene.Characters), not a type.
                        // Stubbing it would collide with the real namespace (CS0101).
                        if (typeName.Length > 0 && nsName.Length > 0 &&
                            !knownGameNamespaces.Contains(nsName + "." + typeName))
                        {
                            missing.Add(typeName);
                            missingNs[typeName] = nsName;
                        }
                        continue;
                    }
                    var name2 = ExtractMissingName(diag);
                    if (name2.Length == 0) continue;
                    missing.Add(name2);
                    // C# attribute shorthand: [Foo] resolves to FooAttribute, but the
                    // diagnostic span covers only "Foo". Register both.
                    missing.Add(name2 + "Attribute");
                    var ns = ResolveNamespace(name2, diag.Location.SourceTree, typeNamespaces);
                    if (ns != null)
                        missingNs[name2] = ns;
                    var nsAttr = ResolveNamespace(name2 + "Attribute", diag.Location.SourceTree, typeNamespaces);
                    if (nsAttr != null)
                        missingNs[name2 + "Attribute"] = nsAttr;
                }

foreach (var name in missing)
                {
                    if (stubTypes.ContainsKey(name)) continue;
                    var ns = missingNs.TryGetValue(name, out var v) ? v : null;
                    if (ns == null) continue;
                    if (ReferencedInNamespace(referencedTypeNs, name, ns)) continue;
                    // Skip if the type is already real in the SAME namespace (a stub would
                    // collide). A same-named real type in a different namespace is a distinct
                    // type (e.g. generic DWS.Common.Resources.Resource<T> vs the non-generic
                    // UBOAT.Game.Core.Data.Resource) and must still be stubbed.
                    if (realByType.TryGetValue(name, out var realFile) &&
                        RealInNamespace(realFile, ns)) continue;
                    stubTypes[name] = ns;
                    added = true;
                }
                foreach (var name in genericTypes)
                {
                    if (realByType.ContainsKey(name)) continue;
                    var ns = missingNs.TryGetValue(name, out var v) ? v : null;
                    if (ns == null) continue;
                    if (ReferencedInNamespace(referencedTypeNs, name, ns)) continue;
                    if (!stubTypes.ContainsKey(name))
                        stubTypes[name] = ns;
                    if (genericStubTypes.Add(name))
                        added = true;
                }
                foreach (var name in enumTypes)
                {
                    if (realByType.ContainsKey(name)) continue;
                    var ns = missingNs.TryGetValue(name, out var v) ? v : null;
                    if (ns == null) continue;
                    if (ReferencedInNamespace(referencedTypeNs, name, ns)) continue;
                    if (!stubTypes.ContainsKey(name))
                        stubTypes[name] = ns;
                    if (enumStubTypes.Add(name))
                        added = true;
                }
                foreach (var name in interfaceTypes)
                {
                    if (realByType.ContainsKey(name)) continue;
                    var ns = missingNs.TryGetValue(name, out var v) ? v : null;
                    if (ns == null) continue;
                    if (ReferencedInNamespace(referencedTypeNs, name, ns)) continue;
                    if (!stubTypes.ContainsKey(name))
                        stubTypes[name] = ns;
                    if (interfaceStubTypes.Add(name))
                        added = true;
                }
                if (!added)
                    break;
            }

            var result = new HashSet<string>(realByType.Values, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in stubTypes)
            {
                var g = genericStubTypes.Contains(kv.Key) ? "generic:" : "";
                var e = enumStubTypes.Contains(kv.Key) ? "enum:" : "";
                var i = interfaceStubTypes.Contains(kv.Key) ? "interface:" : "";
                var arity = genericArity.TryGetValue(kv.Key, out var a) ? a : 0;
                var members = enumMembers.TryGetValue(kv.Key, out var ms) ? string.Join(",", ms) : "";
                var nested = nestedTypes.TryGetValue(kv.Key, out var nset) && nset.Count > 0
                    ? "[" + string.Join(",", nset.OrderBy(x => x, StringComparer.Ordinal)) + "]"
                    : "";
                var props = stubMembers.TryGetValue(kv.Key, out var pset) && pset.Count > 0
                    ? "<" + string.Join(",", pset.OrderBy(x => x, StringComparer.Ordinal)) + ">"
                    : "";
                result.Add("stub:" + g + e + i + (arity > 0 ? arity + ":" : "")
                    + (string.IsNullOrEmpty(kv.Value) ? "" : kv.Value + ".") + kv.Key
                    + (members.Length > 0 ? "{" + members + "}" : "")
                    + nested + props);
            }

            // Namespaces that have neither a real type nor a stub in the subset. A using
            // for such a namespace must be stripped (the namespace is empty after
            // body-stripping, so the compiler reports CS0234). Namespaces that DO hold a
            // real type or stub keep their usings so the types stay reachable.
            var populatedNamespaces = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in realByType)
            {
                var ns = GetFileNamespace(
                    CSharpSyntaxTree.ParseText(File.ReadAllText(kv.Value), path: kv.Value).GetRoot());
                if (ns.Length > 0)
                    populatedNamespaces.Add(ns);
            }
            foreach (var kv in stubTypes)
            {
                if (kv.Value.Length > 0)
                    populatedNamespaces.Add(kv.Value);
            }
            // Namespaces that hold a type in a referenced assembly must keep their usings
            // too, otherwise a using needed to reach that referenced type is stripped and
            // the type becomes unresolvable (CS0246) even though it exists in a DLL.
            foreach (var set in referencedTypeNs.Values)
                foreach (var ns in set)
                    if (ns.Length > 0)
                        populatedNamespaces.Add(ns);
            var stripNamespaces = knownGameNamespaces
                .Where(n => (n.StartsWith("UBOAT.", StringComparison.Ordinal)
                             || n.StartsWith("DWS.", StringComparison.Ordinal))
                            && !populatedNamespaces.Contains(n))
                .ToImmutableArray();

            return (result.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
                stripNamespaces);
        }

        /// <summary>Namespace of a stub type (used to re-derive stub source from its path).</summary>
        public static string StubNamespace(string stubPath)
        {
            var s = StripStubPrefix(stubPath);
            var dot = s.LastIndexOf('.');
            return dot > 0 ? s.Substring(0, dot) : "";
        }

        /// <summary>Stub type simple name (used to re-derive stub source from its path).</summary>
        public static string StubTypeName(string stubPath)
        {
            var s = StripStubPrefix(stubPath);
            var dot = s.LastIndexOf('.');
            return dot > 0 ? s.Substring(dot + 1) : s;
        }

        /// <summary>True when the stub path marks a generic stub.</summary>
        public static bool StubIsGeneric(string stubPath)
        {
            return stubPath.StartsWith("stub:generic:", StringComparison.Ordinal);
        }

        /// <summary>True when the stub path marks an enum stub.</summary>
        public static bool StubIsEnum(string stubPath)
        {
            return stubPath.StartsWith("stub:enum:", StringComparison.Ordinal);
        }

        /// <summary>True when the stub path marks an interface stub.</summary>
        public static bool StubIsInterface(string stubPath)
        {
            return stubPath.StartsWith("stub:interface:", StringComparison.Ordinal);
        }

        /// <summary>True when the stub type is an attribute (name ends in Attribute).</summary>
        public static bool StubIsAttribute(string stubPath)
        {
            return StubTypeName(stubPath).EndsWith("Attribute", StringComparison.Ordinal);
        }

        private static string StripStubPrefix(string stubPath)
        {
            var s = stubPath;
            foreach (var p in new[] { "stub:generic:", "stub:enum:", "stub:interface:", "stub:" })
            {
                if (s.StartsWith(p, StringComparison.Ordinal))
                {
                    s = s.Substring(p.Length);
                    break;
                }
            }
            // Optional leading "N:" arity segment (generic stubs).
            var colon = s.IndexOf(':');
            if (colon > 0 && int.TryParse(s.Substring(0, colon), out _))
                s = s.Substring(colon + 1);
            // Optional trailing "{m1,m2}" enum-member segment.
            var brace = s.IndexOf('{');
            if (brace >= 0)
                s = s.Substring(0, brace);
            // Optional trailing "[n1,n2]" nested-type segment.
            var bracket = s.IndexOf('[');
            if (bracket >= 0)
                s = s.Substring(0, bracket);
            // Optional trailing "<m:T,m2:T2>" member-property segment.
            var angle = s.IndexOf('<');
            if (angle >= 0)
                s = s.Substring(0, angle);
            return s;
        }

        /// <summary>Enum members of a stub (comma-separated), or empty.</summary>
        public static string StubMembers(string stubPath)
        {
            var brace = stubPath.IndexOf('{');
            var close = stubPath.LastIndexOf('}');
            if (brace < 0 || close <= brace)
                return "";
            return stubPath.Substring(brace + 1, close - brace - 1);
        }

        /// <summary>Nested type names of a stub (comma-separated), or empty.</summary>
        public static string StubNested(string stubPath)
        {
            var open = stubPath.IndexOf('[');
            var close = stubPath.LastIndexOf(']');
            if (open < 0 || close <= open)
                return "";
            return stubPath.Substring(open + 1, close - open - 1);
        }

        /// <summary>Member-property stubs "M:T,M2:T2" of a stub, or empty.</summary>
        public static string StubMemberProps(string stubPath)
        {
            var open = stubPath.IndexOf('<');
            var close = stubPath.LastIndexOf('>');
            if (open < 0 || close <= open)
                return "";
            return stubPath.Substring(open + 1, close - open - 1);
        }

        /// <summary>Arity of a generic stub (0 for non-generic).</summary>
        public static int StubArity(string stubPath)
        {
            var s = stubPath;
            foreach (var p in new[] { "stub:generic:", "stub:enum:", "stub:interface:", "stub:" })
            {
                if (s.StartsWith(p, StringComparison.Ordinal))
                {
                    s = s.Substring(p.Length);
                    break;
                }
            }
            var colon = s.IndexOf(':');
            if (colon > 0 && int.TryParse(s.Substring(0, colon), out var arity))
                return arity;
            return 0;
        }

        public static string StubSource(string typeName, string ns, bool generic = false, bool attribute = false, bool isEnum = false, bool isInterface = false, int arity = 0, string members = "", HashSet<string> nested = null, HashSet<string> memberProps = null)
        {
            var n = string.IsNullOrEmpty(ns) ? "" : $"namespace {ns};\n";
            string t;
            if (attribute)
                t = $"public class {typeName} : System.Attribute {{ }}";
            else if (isEnum)
                t = members.Length > 0
                    ? $"public enum {typeName} {{ {members} }}"
                    : $"public enum {typeName} {{ }}";
            else if (isInterface)
                t = $"public interface {typeName} {{ }}";
            else if (generic)
            {
                var ps = arity > 0
                    ? string.Join(", ", Enumerable.Range(0, arity).Select(i => "T" + i))
                    : "T";
                t = $"public class {typeName}<{ps}> {{ }}";
            }
            else
            {
                t = $"public class {typeName} {{ ";
                if (nested != null && nested.Count > 0)
                {
                    foreach (var child in nested)
                        t += $"public enum {child} {{ }} ";
                }
                if (memberProps != null && memberProps.Count > 0)
                {
                    foreach (var mp in memberProps)
                    {
                        var colon = mp.IndexOf(':');
                        if (colon > 0)
                            t += $"public {mp.Substring(colon + 1)} {mp.Substring(0, colon)} {{ get; }} ";
                    }
                }
                t += "}";
            }
            return n + t + "\n";
        }

        // ---- namespace resolution ---------------------------------------------------

        private static Dictionary<string, HashSet<string>> BuildTypeNamespaces()
        {
            var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var path in GameSourceCompiler.EnumerateSources())
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)
                    .GetRoot();
                foreach (var decl in root.DescendantNodes()
                             .Where(n => n is BaseTypeDeclarationSyntax || n is DelegateDeclarationSyntax))
                {
                    var id = decl is BaseTypeDeclarationSyntax b ? b.Identifier.Text
                        : ((DelegateDeclarationSyntax)decl).Identifier.Text;
                    var ns = GetNamespace(decl);
                    if (!map.TryGetValue(id, out var set))
                        map[id] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.Add(ns);
                }
            }

            if (Directory.Exists(GameSourceCompiler.ManagedDir))
            {
                foreach (var dll in Directory.EnumerateFiles(GameSourceCompiler.ManagedDir, "*.dll"))
                {
                    try
                    {
                        var comp = CSharpCompilation.Create(
                            "probe",
                            references: new[] { MetadataReference.CreateFromFile(dll) });
                        var asm = comp.GetAssemblyOrModuleSymbol(
                            comp.References.First()) as IAssemblySymbol;
                        if (asm != null)
                        {
                            foreach (var t in asm.GlobalNamespace.GetTypeMembers())
                                AddDllType(map, t);
                            foreach (var ns in asm.GlobalNamespace.GetNamespaceMembers())
                                WalkDll(map, ns);
                        }
                    }
                    catch
                    {
                        // unreadable DLL; source namespaces suffice
                    }
                }
            }
            return map;
        }

        /// <summary>All game namespaces (UBOAT.*/DWS.*) declared in source or any DLL.</summary>
        public static HashSet<string> BuildKnownNamespaces()
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in GameSourceCompiler.EnumerateSources())
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)
                    .GetRoot();
                foreach (var ns in root.DescendantNodes()
                             .Where(n => n is NamespaceDeclarationSyntax || n is FileScopedNamespaceDeclarationSyntax))
                {
                    var name = ns is NamespaceDeclarationSyntax nd ? nd.Name.ToString()
                        : ((FileScopedNamespaceDeclarationSyntax)ns).Name.ToString();
                    known.Add(name);
                }
            }
            if (Directory.Exists(GameSourceCompiler.ManagedDir))
            {
                foreach (var dll in Directory.EnumerateFiles(GameSourceCompiler.ManagedDir, "*.dll"))
                {
                    try
                    {
                        var comp = CSharpCompilation.Create(
                            "probe",
                            references: new[] { MetadataReference.CreateFromFile(dll) });
                        var asm = comp.GetAssemblyOrModuleSymbol(
                            comp.References.First()) as IAssemblySymbol;
                        if (asm != null)
                        {
                            AddDllNamespaces(known, asm.GlobalNamespace);
                            foreach (var ns in asm.GlobalNamespace.GetNamespaceMembers())
                                WalkDllNamespaces(known, ns);
                        }
                    }
                    catch
                    {
                        // unreadable DLL; source namespaces suffice
                    }
                }
            }
            return known;
        }

        private static void WalkDllNamespaces(HashSet<string> known, INamespaceSymbol ns)
        {
            AddDllNamespaces(known, ns);
            foreach (var child in ns.GetNamespaceMembers())
                WalkDllNamespaces(known, child);
        }

        private static void AddDllNamespaces(HashSet<string> known, INamespaceSymbol ns)
        {
            var name = ns.ToString();
            if (name.Length > 0)
                known.Add(name);
        }

        private static void WalkDll(
            Dictionary<string, HashSet<string>> map, INamespaceSymbol ns)
        {
            foreach (var t in ns.GetTypeMembers())
                AddDllType(map, t);
            foreach (var child in ns.GetNamespaceMembers())
                WalkDll(map, child);
        }

        private static void WalkReferencedTypes(
            Dictionary<string, HashSet<string>> map, INamespaceSymbol ns)
        {
            foreach (var t in ns.GetTypeMembers())
                AddReferencedType(map, t);
            foreach (var child in ns.GetNamespaceMembers())
                WalkReferencedTypes(map, child);
        }

        private static void AddReferencedType(
            Dictionary<string, HashSet<string>> map, INamedTypeSymbol t)
        {
            var ns = t.ContainingNamespace?.ToString() ?? "";
            if (!map.TryGetValue(t.Name, out var set))
                map[t.Name] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(ns);
        }

        private static bool ReferencedInNamespace(
            Dictionary<string, HashSet<string>> map, string name, string ns)
        {
            return map.TryGetValue(name, out var set) && set.Contains(ns);
        }

        private static bool RealInNamespace(string file, string ns)
        {
            if (ns.Length == 0) return false;
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file).GetRoot();
            return GetFileNamespace(root) == ns;
        }

        private static void AddDllType(
            Dictionary<string, HashSet<string>> map, INamedTypeSymbol t)
        {
            var ns = t.ContainingNamespace?.ToString() ?? "";
            if (!map.TryGetValue(t.Name, out var set))
                map[t.Name] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(ns);
        }

        /// <summary>
        /// Resolves the namespace of a missing type from the referencing file's context:
        /// its implicit namespace plus its using directives. Picks the candidate that is
        /// a known namespace for the type (from source or the obfuscated DLL). This
        /// disambiguates same-named types in different namespaces.
        /// </summary>
        private static string ResolveNamespace(
            string typeName, SyntaxTree tree,
            Dictionary<string, HashSet<string>> typeNamespaces)
        {
            var root = tree.GetRoot();
            var implicitNs = GetFileNamespace(root);
            var usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
                .Select(u => u.Name.ToString())
                .ToList();

            // Try the bare name and the attribute-suffixed name (C# attribute shorthand).
            foreach (var candidate in new[] { typeName, typeName + "Attribute" })
            {
                var known = typeNamespaces.TryGetValue(candidate, out var set)
                    ? set : new HashSet<string>(StringComparer.Ordinal);
                // Walk the enclosing-namespace hierarchy (UBOAT.Game.Core -> UBOAT.Game -> UBOAT).
                for (var ns = implicitNs; ns.Length > 0;)
                {
                    if (known.Contains(ns))
                        return ns;
                    var dot = ns.LastIndexOf('.');
                    ns = dot > 0 ? ns.Substring(0, dot) : "";
                }
                foreach (var u in usings)
                {
                    if (known.Contains(u))
                        return u;
                }
                // Fallback: the type is unambiguous (single known namespace). The using
                // may have been stripped by StripBrokenUsings (namespace has no real type
                // in the subset), so reach it directly.
                if (known.Count == 1)
                    return known.First();
            }
            return null;
        }

        public static string GetFileNamespace(SyntaxNode node)
        {
            var root = node is CompilationUnitSyntax ? node : node.SyntaxTree.GetRoot();
            var fns = root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
            if (fns != null)
                return fns.Name.ToString();
            var ns = root.DescendantNodes().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
            if (ns != null)
                return ns.Name.ToString();
            return "";
        }

        private static string GetNamespace(SyntaxNode decl)
        {
            var ns = decl.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
            if (ns != null)
                return ns.Name.ToString();
            var fns = decl.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
            if (fns != null)
                return fns.Name.ToString();
            return "";
        }

        // ---- missing-type extraction ------------------------------------------------

        private static string ExtractMissingName(Diagnostic diag)
        {
            var span = diag.Location.SourceSpan;
            var text = diag.Location.SourceTree.GetText().ToString(span).Trim();
            var name = text.Split('.').Last().Trim('\'', '"');
            var lt = name.IndexOf('<');
            if (lt >= 0)
                name = name.Substring(0, lt);
            return name;
        }

        /// <summary>Extracts the member name 'Y' from a CS0117 "'X' does not contain
        /// definition for 'Y'" diagnostic (quotes may be ' or ").</summary>
        private static string ExtractMissingMember(Diagnostic diag)
        {
            var msg = diag.GetMessage();
            var q = msg.IndexOfAny(new[] { '\'', '"' });
            if (q < 0) return "";
            var q2 = msg.IndexOf(msg[q], q + 1);
            if (q2 < 0) return "";
            var q3 = msg.IndexOfAny(new[] { '\'', '"' }, q2 + 1);
            if (q3 < 0) return "";
            var q4 = msg.IndexOf(msg[q3], q3 + 1);
            if (q4 < 0) return "";
            return msg.Substring(q3 + 1, q4 - q3 - 1);
        }

        /// <summary>Extracts the type name 'X' (first quoted name) from a CS0117
        /// "'X' does not contain definition for 'Y'" diagnostic.</summary>
        private static string ExtractMissingTypeFromMessage(Diagnostic diag)
        {
            var msg = diag.GetMessage();
            var q = msg.IndexOfAny(new[] { '\'', '"' });
            if (q < 0) return "";
            var q2 = msg.IndexOf(msg[q], q + 1);
            if (q2 < 0) return "";
            return msg.Substring(q + 1, q2 - q - 1);
        }

        /// <summary>Extracts the namespace 'Y' (second quoted name) from a CS0234
        /// "Type or namespace name 'X' does not exist in namespace 'Y'" diagnostic.</summary>
        private static string ExtractMissingNamespaceFromMessage(Diagnostic diag)
        {
            var msg = diag.GetMessage();
            var q = msg.IndexOfAny(new[] { '\'', '"' });
            if (q < 0) return "";
            var q2 = msg.IndexOf(msg[q], q + 1);
            if (q2 < 0) return "";
            var q3 = msg.IndexOfAny(new[] { '\'', '"' }, q2 + 1);
            if (q3 < 0) return "";
            var q4 = msg.IndexOf(msg[q3], q3 + 1);
            if (q4 < 0) return "";
            return msg.Substring(q3 + 1, q4 - q3 - 1);
        }

        /// <summary>Full-source index of type declarations: type simple name -> (base type
        /// names, member name -> declared type name). Used to resolve the type of a member
        /// accessed on a stub (CS1061) by walking the real base chain.</summary>
        private static Dictionary<string, (HashSet<string> Bases, Dictionary<string, string> Members)>
            BuildMemberIndex()
        {
            var index = new Dictionary<string, (HashSet<string> Bases, Dictionary<string, string> Members)>(StringComparer.Ordinal);
            foreach (var path in GameSourceCompiler.EnumerateSources())
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot();
                foreach (var decl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    var name = decl.Identifier.Text;
                    if (!index.TryGetValue(name, out var entry))
                        index[name] = entry = (new HashSet<string>(StringComparer.Ordinal),
                            new Dictionary<string, string>(StringComparer.Ordinal));
                    if (decl.BaseList != null)
                    {
                        foreach (var bt in decl.BaseList.Types)
                        {
                            var bn = bt.Type.ToString().Split('.').Last();
                            entry.Bases.Add(bn);
                        }
                    }
                    foreach (var prop in decl.Members.OfType<PropertyDeclarationSyntax>())
                    {
                        var pname = prop.Identifier.Text;
                        var ptype = prop.Type.ToString().Split('.').Last();
                        if (!entry.Members.ContainsKey(pname))
                            entry.Members[pname] = ptype;
                    }
                    foreach (var field in decl.Members.OfType<FieldDeclarationSyntax>())
                    {
                        var ftype = field.Declaration.Type.ToString().Split('.').Last();
                        foreach (var v in field.Declaration.Variables)
                        {
                            if (!entry.Members.ContainsKey(v.Identifier.Text))
                                entry.Members[v.Identifier.Text] = ftype;
                        }
                    }
                }
            }
            return index;
        }

        /// <summary>Fully-qualifies a simple type name using its known namespaces, preferring
        /// a game (UBOAT./DWS.) namespace over BCL/Unity. Falls back to the bare name.</summary>
        private static string QualifyTypeName(
            Dictionary<string, HashSet<string>> typeNamespaces, string typeName)
        {
            if (!typeNamespaces.TryGetValue(typeName, out var set) || set.Count == 0)
                return typeName;
            var game = set.FirstOrDefault(n => n.StartsWith("UBOAT.", StringComparison.Ordinal)
                || n.StartsWith("DWS.", StringComparison.Ordinal));
            var ns = game ?? set.First();
            return ns.Length > 0 ? ns + "." + typeName : typeName;
        }

        /// <summary>Resolves the declared type of member M on type X (or its base chain),
        /// from the full-source member index. Returns "" if unresolvable.</summary>
        private static string ResolveMemberType(
            Dictionary<string, (HashSet<string> Bases, Dictionary<string, string> Members)> index,
            string typeName, string memberName)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            stack.Push(typeName);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (!visited.Add(cur)) continue;
                if (index.TryGetValue(cur, out var entry))
                {
                    if (entry.Members.TryGetValue(memberName, out var t))
                        return t;
                    foreach (var b in entry.Bases)
                        stack.Push(b);
                }
            }
            return "";
        }

        private static int ExtractGenericArity(Diagnostic diag)
        {
            var span = diag.Location.SourceSpan;
            var text = diag.Location.SourceTree.GetText().ToString(span);
            var lt = text.IndexOf('<');
            var gt = text.LastIndexOf('>');
            if (lt < 0 || gt <= lt)
                return 0;
            var args = text.Substring(lt + 1, gt - lt - 1);
            if (args.Length == 0)
                return 0;
            var depth = 0;
            var count = 1;
            foreach (var c in args)
            {
                if (c == '<') depth++;
                else if (c == '>') depth--;
                else if (c == ',' && depth == 0) count++;
            }
            return count;
        }

        // ---- base types --------------------------------------------------------------

        private static ImmutableArray<string> GetBaseTypeNames(CompilationUnitSyntax root)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var decl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (decl.BaseList != null)
                    foreach (var t in decl.BaseList.Types)
                        AddTypeName(names, t);
            }
            return names.OrderBy(n => n, StringComparer.Ordinal).ToImmutableArray();
        }

        private static void AddTypeName(HashSet<string> names, BaseTypeSyntax t)
        {
            var name = t.Type.ToString();
            var lt = name.IndexOf('<');
            if (lt >= 0)
                name = name.Substring(0, lt);
            var dot = name.LastIndexOf('.');
            if (dot >= 0)
                name = name.Substring(dot + 1);
            if (name.Length > 0)
                names.Add(name);
        }

        // ---- body stripping -----------------------------------------------------------

        public static SyntaxTree StripBodies(SyntaxTree tree)
        {
            var rewriter = new BodyStripper();
            var newRoot = rewriter.Visit(tree.GetRoot()) as CompilationUnitSyntax;
            return CSharpSyntaxTree.ParseText(newRoot.ToString(), path: tree.FilePath);
        }

        /// <summary>
        /// Remove using directives whose namespace is in <paramref name="stripNamespaces"/>
        /// (game namespaces that are empty in the subset). Framework usings are untouched.
        /// </summary>
        public static SyntaxTree StripBrokenUsings(SyntaxTree tree, HashSet<string> stripNamespaces)
        {
            if (stripNamespaces == null || stripNamespaces.Count == 0)
                return tree;
            var root = tree.GetRoot();
            var usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
                .Where(u => stripNamespaces.Contains(u.Name.ToString()))
                .ToList();
            if (usings.Count == 0)
                return tree;
            var newRoot = root.RemoveNodes(usings, SyntaxRemoveOptions.KeepNoTrivia);
            return CSharpSyntaxTree.ParseText(newRoot.ToString(), path: tree.FilePath);
        }

        private class BodyStripper : CSharpSyntaxRewriter
        {
            public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                if (node.Body != null)
                    node = node.WithBody(ThrowBlock());
                else if (node.ExpressionBody != null)
                    node = node.WithExpressionBody(null).WithSemicolonToken(
                        SyntaxFactory.Token(SyntaxKind.SemicolonToken));
                return base.VisitMethodDeclaration(node);
            }

            public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
            {
                if (node.Body != null)
                    node = node.WithBody(ThrowBlock());
                else if (node.ExpressionBody != null)
                    node = node.WithExpressionBody(null).WithSemicolonToken(
                        SyntaxFactory.Token(SyntaxKind.SemicolonToken));
                return base.VisitConstructorDeclaration(node);
            }

            public override SyntaxNode VisitDestructorDeclaration(DestructorDeclarationSyntax node)
            {
                if (node.Body != null)
                    node = node.WithBody(ThrowBlock());
                return base.VisitDestructorDeclaration(node);
            }

            public override SyntaxNode VisitOperatorDeclaration(OperatorDeclarationSyntax node)
            {
                if (node.Body != null)
                    node = node.WithBody(ThrowBlock());
                else if (node.ExpressionBody != null)
                    node = node.WithExpressionBody(null).WithSemicolonToken(
                        SyntaxFactory.Token(SyntaxKind.SemicolonToken));
                return base.VisitOperatorDeclaration(node);
            }

            public override SyntaxNode VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
            {
                if (node.Body != null)
                    node = node.WithBody(ThrowBlock());
                else if (node.ExpressionBody != null)
                    node = node.WithExpressionBody(null).WithSemicolonToken(
                        SyntaxFactory.Token(SyntaxKind.SemicolonToken));
                return base.VisitConversionOperatorDeclaration(node);
            }

            public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
            {
                if (node.ExpressionBody != null)
                {
                    // Expression-bodied property -> auto-property with a throwing getter.
                    var getter = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithBody(ThrowBlock());
                    node = node.WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithAccessorList(SyntaxFactory.AccessorList(
                            SyntaxFactory.List(new[] { getter })));
                }
                else if (node.AccessorList != null)
                {
                    var accessors = node.AccessorList.Accessors
                        .Select(a => a.Body != null ? a.WithBody(ThrowBlock()) : a)
                        .ToList();
                    node = node.WithAccessorList(
                        node.AccessorList.WithAccessors(SyntaxFactory.List(accessors)));
                }
                return base.VisitPropertyDeclaration(node);
            }

            public override SyntaxNode VisitIndexerDeclaration(IndexerDeclarationSyntax node)
            {
                if (node.ExpressionBody != null)
                {
                    var getter = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithBody(ThrowBlock());
                    node = node.WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithAccessorList(SyntaxFactory.AccessorList(
                            SyntaxFactory.List(new[] { getter })));
                }
                else if (node.AccessorList != null)
                {
                    var accessors = node.AccessorList.Accessors
                        .Select(a => a.Body != null ? a.WithBody(ThrowBlock()) : a)
                        .ToList();
                    node = node.WithAccessorList(
                        node.AccessorList.WithAccessors(SyntaxFactory.List(accessors)));
                }
                return base.VisitIndexerDeclaration(node);
            }

            public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node)
            {
                // const fields REQUIRE an initializer; keep it. Strip others.
                var isConst = node.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword));
                if (!isConst)
                {
                    var vars = node.Declaration.Variables.Select(v =>
                        v.Initializer != null ? v.WithInitializer(null) : v).ToList();
                    node = node.WithDeclaration(
                        node.Declaration.WithVariables(SyntaxFactory.SeparatedList(vars)));
                }
                return base.VisitFieldDeclaration(node);
            }
        }

        private static BlockSyntax ThrowBlock()
        {
            var cu = CSharpSyntaxTree.ParseText("class X { void f() { throw null; } }").GetRoot();
            return cu.DescendantNodes().OfType<MethodDeclarationSyntax>().First().Body;
        }
    }
}