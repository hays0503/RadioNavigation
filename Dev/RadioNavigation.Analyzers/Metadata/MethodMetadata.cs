using System.Collections.Generic;

namespace RadioNavigation.Analyzers
{
    /// <summary>
    /// A single method read from raw ECMA-335 metadata (System.Reflection.Metadata),
    /// independent of Roslyn's semantic model. Used as a fallback when Roslyn cannot see
    /// a member because the game assembly is obfuscated and under-reports its members.
    /// </summary>
    internal sealed class MethodMetadata
    {
        public MethodMetadata(
            string name,
            bool isStatic,
            bool isPublic,
            bool isPrivate,
            bool isConstructor,
            bool isGeneric,
            int genericArity,
            string returnType,
            IReadOnlyList<ParameterMetadata> parameters,
            string declaringType,
            int metadataToken)
        {
            Name = name;
            IsStatic = isStatic;
            IsPublic = isPublic;
            IsPrivate = isPrivate;
            IsConstructor = isConstructor;
            IsGeneric = isGeneric;
            GenericArity = genericArity;
            ReturnType = returnType;
            Parameters = parameters;
            DeclaringType = declaringType;
            MetadataToken = metadataToken;
        }

        public string Name { get; }
        public bool IsStatic { get; }
        public bool IsPublic { get; }
        public bool IsPrivate { get; }
        public bool IsConstructor { get; }
        public bool IsGeneric { get; }
        public int GenericArity { get; }
        public string ReturnType { get; }
        public IReadOnlyList<ParameterMetadata> Parameters { get; }
        public string DeclaringType { get; }
        public int MetadataToken { get; }

        public override string ToString()
        {
            var gen = IsGeneric ? $"<{GenericArity}>" : "";
            return $"{Name}{gen} static={IsStatic} ({string.Join(", ", Parameters)}) -> {ReturnType}";
        }
    }
}
