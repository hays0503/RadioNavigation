using Microsoft.CodeAnalysis;

namespace RadioNavigation.Analyzers
{
    /// <summary>
    /// Normalized identity of a type as it appears in ECMA-335 metadata, so that a Roslyn
    /// <see cref="ITypeSymbol"/> can be matched against raw metadata without fragile string
    /// comparisons. Metadata stores nested types with '/' between nesting levels, keeps the
    /// namespace separate (e.g. namespace "NS", type "Outer/Inner"), and appends a backtick +
    /// arity for generic types (e.g. "Foo`1").
    /// </summary>
    internal readonly struct MetadataTypeName
    {
        public MetadataTypeName(string assemblyName, string ns, string typeName, int genericArity)
        {
            AssemblyName = assemblyName;
            Namespace = ns;
            TypeName = typeName;
            GenericArity = genericArity;
        }

        /// <summary>Simple assembly name (e.g. "com.uboat.game"). May be null when unknown.</summary>
        public string AssemblyName { get; }

        /// <summary>Namespace (empty for the global namespace).</summary>
        public string Namespace { get; }

        /// <summary>
        /// Type name with '/' separating nested levels and a backtick+arity suffix for generics
        /// (e.g. "ShipControlUI", "Outer/Inner", "Foo`1").
        /// </summary>
        public string TypeName { get; }

        /// <summary>Number of generic type parameters (0 for non-generic types).</summary>
        public int GenericArity { get; }

        /// <summary>
        /// Build a <see cref="MetadataTypeName"/> from a Roslyn type symbol, walking nested
        /// containing types to produce the metadata-style '/' separated name.
        /// </summary>
        public static MetadataTypeName FromSymbol(ITypeSymbol symbol)
        {
            var ns = symbol.ContainingNamespace?.ToDisplayString() ?? "";
            var typeName = symbol.Name;

            // Walk outward through containing types to build the '/' separated nesting path.
            var container = symbol.ContainingType;
            while (container != null)
            {
                typeName = container.Name + "/" + typeName;
                container = container.ContainingType;
            }

            return new MetadataTypeName(
                symbol.ContainingAssembly?.Name,
                ns,
                typeName,
                symbol is INamedTypeSymbol named ? named.TypeParameters.Length : 0);
        }
    }
}
