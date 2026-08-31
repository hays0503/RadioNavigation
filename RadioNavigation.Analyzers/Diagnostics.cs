using Microsoft.CodeAnalysis;

namespace RadioNavigation.Analyzers
{
    /// <summary>
    /// Diagnostic descriptors for the RadioNavigation Harmony/UBOAT analyzer.
    /// All rules target the mod's IMPERATIVE patching pattern:
    ///   typeof(X).GetMethod("Name", ...) + patchType.GetMethod(handler) + harmony.Patch(method, prefix:, postfix:)
    /// </summary>
    internal static class Diagnostics
    {
        private const string Category = "Harmony";

        public static readonly DiagnosticDescriptor TargetNotFound = new DiagnosticDescriptor(
            id: "RN0001",
            title: "Harmony target method could not be resolved",
            messageFormat: "Harmony target '{0}' could not be resolved on type '{1}' (check name, parameter types and BindingFlags)",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor TargetAmbiguous = new DiagnosticDescriptor(
            id: "RN0002",
            title: "Harmony target method is ambiguous",
            messageFormat: "Harmony target '{0}' on type '{1}' matches multiple overloads; specify parameter types (GetMethod(name, new[] {{ typeof(...) }}))",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ParameterMismatch = new DiagnosticDescriptor(
            id: "RN0003",
            title: "Harmony patch parameter incompatible with target",
            messageFormat: "Patch parameter '{0}' (type '{1}') is incompatible with target parameter '{2}' (type '{3}')",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidSpecialParameter = new DiagnosticDescriptor(
            id: "RN0004",
            title: "Invalid Harmony special parameter",
            messageFormat: "'{0}' cannot be used here: {1}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor RefOutMismatch = new DiagnosticDescriptor(
            id: "RN0005",
            title: "Harmony ref/out mismatch",
            messageFormat: "Patch parameter '{0}' has '{1}' but target parameter '{2}' has '{3}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private const string UnityCategory = "Unity";

        public static readonly DiagnosticDescriptor UnityLifecycleMisuse = new DiagnosticDescriptor(
            id: "RN0006",
            title: "Unity lifecycle misuse in per-frame callback",
            messageFormat: "'{0}' should not be called in {1}: cache it in Awake/Start instead of looking it up every frame",
            category: UnityCategory,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor AudioCallbackAllocation = new DiagnosticDescriptor(
            id: "RN0007",
            title: "Allocation in Unity audio callback",
            messageFormat: "'{0}' allocates memory and must not run in OnAudioFilterRead (audio glitches)",
            category: UnityCategory,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MainThreadApiMisuse = new DiagnosticDescriptor(
            id: "RN0008",
            title: "Unity main-thread API called from background context",
            messageFormat: "'{0}' is a Unity main-thread-only API but is called from a background context ({1}); marshal back to the main thread first",
            category: UnityCategory,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidHandlerReturnType = new DiagnosticDescriptor(
            id: "RN0009",
            title: "Invalid Harmony handler return type",
            messageFormat: "Harmony {0} '{1}' returns '{2}'; expected {3}",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnguardedReflectionDereference = new DiagnosticDescriptor(
            id: "RN0010",
            title: "Reflection member lookup dereferenced without a null check",
            messageFormat: "'{0}' may return null (string-name lookup on an obfuscated/renamed member) but is dereferenced immediately; guard with '?.', cache once, or check for null",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
    }
}
