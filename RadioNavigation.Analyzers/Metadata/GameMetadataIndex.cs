using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Microsoft.CodeAnalysis;

namespace RadioNavigation.Analyzers
{
    /// <summary>Outcome of looking up a method in raw metadata.</summary>
    internal enum MetadataLookupResult
    {
        /// <summary>No game assembly index could be loaded.</summary>
        Unavailable,
        /// <summary>The declaring type does not exist in the indexed assembly.</summary>
        TypeNotFound,
        /// <summary>The type exists but declares no method with the requested name/signature.</summary>
        MethodNotFound,
        /// <summary>A single unambiguous method matched.</summary>
        Found,
        /// <summary>Several overloads match a name-only lookup; an explicit signature is required.</summary>
        Ambiguous
    }

    /// <summary>
    /// Reads a game assembly's ECMA-335 metadata directly with System.Reflection.Metadata,
    /// without loading it into the current process. This is the reliable source of truth for
    /// whether a Harmony target actually exists, because UBOAT assemblies are obfuscated and
    /// Roslyn under-reports their members (verified: ShipControlUI exposes 87 methods in raw
    /// metadata but only 30 to Roslyn).
    ///
    /// The index is deliberately ignorant of Harmony - it just answers "does this type/method
    /// exist, and how many overloads match?". It is keyed by normalized
    /// <see cref="MetadataTypeName"/> so a Roslyn symbol can be matched against raw metadata.
    ///
    /// No file IO here: the <see cref="MetadataReader"/> comes from the compilation's own
    /// <see cref="PortableExecutableReference"/>, which Roslyn has already loaded. Enumerating
    /// raw type/method definitions bypasses Roslyn's symbol-level filtering that hides
    /// obfuscated members.
    /// </summary>
    internal sealed class GameMetadataIndex
    {
        private readonly Dictionary<string, TypeEntry> _typesByFullName =
            new Dictionary<string, TypeEntry>(StringComparer.Ordinal);

        private sealed class TypeEntry
        {
            public Dictionary<string, List<MethodMetadata>> MethodsByName =
                new Dictionary<string, List<MethodMetadata>>(StringComparer.Ordinal);
        }

        private GameMetadataIndex()
        {
        }

        /// <summary>
        /// Build an index from a compilation reference's raw metadata. Returns null when the
        /// reference has no readable metadata.
        /// </summary>
        public static GameMetadataIndex TryCreateFromReference(PortableExecutableReference reference)
        {
            if (reference == null)
                return null;
            try
            {
                var assemblyMetadata = reference.GetMetadata() as AssemblyMetadata;
                if (assemblyMetadata == null)
                    return null;
                foreach (var module in assemblyMetadata.GetModules())
                {
                    var md = module.GetMetadataReader();
                    if (md != null && md.IsAssembly)
                        return Build(md);
                }
            }
            catch (BadImageFormatException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            return null;
        }

        private static GameMetadataIndex Build(MetadataReader md)
        {
            var index = new GameMetadataIndex();

            foreach (var th in md.TypeDefinitions)
            {
                var td = md.GetTypeDefinition(th);
                var ns = md.GetString(td.Namespace);
                var name = md.GetString(td.Name);
                var fullName = string.IsNullOrEmpty(ns) ? name : ns + "." + name;

                var entry = new TypeEntry();
                foreach (var mh in td.GetMethods())
                {
                    var m = md.GetMethodDefinition(mh);
                    var methodName = md.GetString(m.Name);

                    MethodSignature<string> sig;
                    try
                    {
                        sig = m.DecodeSignature(new MetadataTypeProvider(md), null);
                    }
                    catch (BadImageFormatException)
                    {
                        continue;
                    }

                    bool isStatic = (sig.Header.Attributes & SignatureAttributes.Instance) == 0;
                    var attrs = m.Attributes;
                    bool isPublic = (attrs & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
                    bool isPrivate = (attrs & MethodAttributes.MemberAccessMask) == MethodAttributes.Private;
                    bool isConstructor = methodName == ".ctor" || methodName == ".cctor";

                    var parameters = DecodeParameters(md, m, sig);

                    var meta = new MethodMetadata(
                        methodName,
                        isStatic,
                        isPublic,
                        isPrivate,
                        isConstructor,
                        sig.GenericParameterCount > 0,
                        sig.GenericParameterCount,
                        sig.ReturnType,
                        parameters,
                        fullName,
                        MetadataTokens.GetToken(mh));

                    if (!entry.MethodsByName.TryGetValue(methodName, out var overloads))
                    {
                        overloads = new List<MethodMetadata>();
                        entry.MethodsByName[methodName] = overloads;
                    }
                    overloads.Add(meta);
                }

                index._typesByFullName[fullName] = entry;
            }

            return index;
        }

        private static List<ParameterMetadata> DecodeParameters(
            MetadataReader md,
            MethodDefinition m,
            MethodSignature<string> sig)
        {
            var result = new List<ParameterMetadata>();

            // Names come from the method's Param table (positions 1-based).
            var names = new Dictionary<int, string>();
            foreach (var ph in m.GetParameters())
            {
                var p = md.GetParameter(ph);
                if (p.SequenceNumber >= 1)
                    names[p.SequenceNumber - 1] = md.GetString(p.Name);
            }

            for (int pos = 0; pos < sig.ParameterTypes.Length; pos++)
            {
                var type = sig.ParameterTypes[pos];
                // The type provider renders by-ref params with a trailing '&' (e.g. "Int32&").
                var kind = ParameterRefKind.None;
                if (type.EndsWith("&", StringComparison.Ordinal))
                {
                    kind = ParameterRefKind.Ref;
                    type = type.Substring(0, type.Length - 1);
                }
                var name = names.TryGetValue(pos, out var n) ? n : $"p{pos}";
                result.Add(new ParameterMetadata(name, type, kind, pos));
            }
            return result;
        }

        /// <summary>True if the type exists in the indexed assembly.</summary>
        public bool HasType(in MetadataTypeName typeName)
        {
            return _typesByFullName.ContainsKey(FullNameOf(typeName));
        }

        /// <summary>All overloads of a method name on the type (empty when none).</summary>
        public IReadOnlyList<MethodMetadata> FindMethods(in MetadataTypeName typeName, string methodName)
        {
            if (_typesByFullName.TryGetValue(FullNameOf(typeName), out var entry) &&
                entry.MethodsByName.TryGetValue(methodName, out var overloads))
            {
                return overloads;
            }
            return Array.Empty<MethodMetadata>();
        }

        /// <summary>Every method declared on the type (across all names/overloads).</summary>
        public IEnumerable<MethodMetadata> GetAllMethods(MetadataTypeName typeName)
        {
            if (!_typesByFullName.TryGetValue(FullNameOf(typeName), out var entry))
                yield break;
            foreach (var list in entry.MethodsByName.Values)
                foreach (var m in list)
                    yield return m;
        }

        /// <summary>
        /// Resolve a method by name, optional explicit parameter types, and an optional
        /// static/instance pin (from BindingFlags).
        /// </summary>
        /// <param name="typeName">Normalized declaring type.</param>
        /// <param name="methodName">Requested method name.</param>
        /// <param name="parameterTypes">
        /// Explicit parameter type names (from <c>GetMethod("X", new[]{ typeof(A), typeof(B) })</c>),
        /// or null for a name-only lookup.
        /// </param>
        /// <param name="requireStatic">
        /// True = only static overloads, false = only instance overloads, null = either.
        /// </param>
        /// <param name="match">When <see cref="MetadataLookupResult.Found"/>, the matched overload.</param>
        public MetadataLookupResult FindMethod(
            in MetadataTypeName typeName,
            string methodName,
            IReadOnlyList<string> parameterTypes,
            bool? requireStatic,
            out MethodMetadata match)
        {
            match = null;

            if (!_typesByFullName.TryGetValue(FullNameOf(typeName), out var entry))
                return MetadataLookupResult.TypeNotFound;

            if (!entry.MethodsByName.TryGetValue(methodName, out var overloads) || overloads.Count == 0)
                return MetadataLookupResult.MethodNotFound;

            // Apply the static/instance pin first so same-named static+instance overloads can be
            // disambiguated (e.g. ShipControlUI.ApplyToolPositionLimits has both).
            IEnumerable<MethodMetadata> candidates = overloads;
            if (requireStatic.HasValue)
                candidates = candidates.Where(o => o.IsStatic == requireStatic.Value);

            var candidateList = candidates.ToList();
            if (candidateList.Count == 0)
                return MetadataLookupResult.MethodNotFound;

            if (parameterTypes == null)
            {
                // Name-only lookup: unambiguous only when exactly one overload survives.
                if (candidateList.Count == 1)
                {
                    match = candidateList[0];
                    return MetadataLookupResult.Found;
                }
                return MetadataLookupResult.Ambiguous;
            }

            // Explicit signature: find the overload whose parameter count and types match.
            var sigCandidates = candidateList.Where(o => o.Parameters.Count == parameterTypes.Count).ToList();
            if (sigCandidates.Count == 0)
                return MetadataLookupResult.MethodNotFound;

            foreach (var c in sigCandidates)
            {
                bool allMatch = true;
                for (int i = 0; i < parameterTypes.Count; i++)
                {
                    if (!string.Equals(
                        CanonicalTypeName(c.Parameters[i].Type),
                        CanonicalTypeName(parameterTypes[i]),
                        StringComparison.Ordinal))
                    {
                        allMatch = false;
                        break;
                    }
                }
                if (allMatch)
                {
                    match = c;
                    return MetadataLookupResult.Found;
                }
            }

            return MetadataLookupResult.MethodNotFound;
        }

        /// <summary>
        /// Reduce a type name to a canonical simple form for cross-source matching. Raw metadata
        /// (obfuscated UBOAT) emits bare names like "String"/"Int32" with empty namespaces, while
        /// Roslyn emits "global::System.String". Both reduce to "String".
        /// </summary>
        private static string CanonicalTypeName(string typeName)
        {
            if (typeName == null)
                return string.Empty;
            if (typeName.StartsWith("global::", StringComparison.Ordinal))
                typeName = typeName.Substring(8);
            int dot = typeName.LastIndexOf('.');
            return dot >= 0 ? typeName.Substring(dot + 1) : typeName;
        }

        private static string FullNameOf(in MetadataTypeName typeName)
        {
            var name = typeName.TypeName;
            if (typeName.GenericArity > 0 && !name.EndsWith("`", StringComparison.Ordinal))
                name += "`" + typeName.GenericArity;
            return string.IsNullOrEmpty(typeName.Namespace) ? name : typeName.Namespace + "." + name;
        }

        /// <summary>Decodes type references/specifications to simple names for signature matching.</summary>
        private sealed class MetadataTypeProvider : ISignatureTypeProvider<string, object>
        {
            private readonly MetadataReader _md;

            public MetadataTypeProvider(MetadataReader md) => _md = md;

            public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
            public string GetByReferenceType(string elementType) => elementType + "&";
            public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
            public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments) =>
                genericType + "<" + string.Join(",", typeArguments) + ">";
            public string GetGenericMethodParameter(object genericContext, int index) => "!!" + index;
            public string GetGenericTypeParameter(object genericContext, int index) => "!" + index;
            public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
            public string GetPinnedType(string elementType) => elementType;
            public string GetPointerType(string elementType) => elementType + "*";
            public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
            public string GetSZArrayType(string elementType) => elementType + "[]";

            public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            {
                var td = reader.GetTypeDefinition(handle);
                var ns = reader.GetString(td.Namespace);
                var name = reader.GetString(td.Name);
                return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
            }

            public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            {
                var tr = reader.GetTypeReference(handle);
                var ns = reader.GetString(tr.Namespace);
                var name = reader.GetString(tr.Name);
                return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
            }

            public string GetTypeFromSpecification(MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
                "spec";
        }
    }
}
