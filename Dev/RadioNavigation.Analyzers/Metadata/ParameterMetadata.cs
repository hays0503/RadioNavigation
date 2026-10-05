namespace RadioNavigation.Analyzers
{
    /// <summary>How a parameter is passed (from raw metadata signature).</summary>
    internal enum ParameterRefKind
    {
        None,
        Ref,
        Out,
        In
    }

    /// <summary>A single method parameter read from raw ECMA-335 metadata.</summary>
    internal sealed class ParameterMetadata
    {
        public ParameterMetadata(string name, string type, ParameterRefKind refKind, int position)
        {
            Name = name;
            Type = type;
            RefKind = refKind;
            Position = position;
        }

        public string Name { get; }
        public string Type { get; }
        public ParameterRefKind RefKind { get; }
        public int Position { get; }

        public override string ToString()
        {
            var prefix = RefKind switch
            {
                ParameterRefKind.Ref => "ref ",
                ParameterRefKind.Out => "out ",
                ParameterRefKind.In => "in ",
                _ => ""
            };
            return $"{prefix}{Type} {Name}";
        }
    }
}
