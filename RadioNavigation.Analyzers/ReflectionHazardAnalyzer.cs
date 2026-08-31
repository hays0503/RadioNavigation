using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RadioNavigation.Analyzers
{
    /// <summary>
    /// RN0010 — reflection hazards.
    ///
    /// Flags a reflection member lookup by string name (GetMethod/GetField/GetProperty/
    /// GetConstructor) whose result is dereferenced immediately without a null guard:
    ///
    ///   typeof(X).GetField("f", flags).SetValue(obj, v);   // NRE if "f" missing
    ///   type.GetProperty("Passes").SetValue(data, passes); // NRE if renamed
    ///
    /// Game assemblies are obfuscated, so string-name lookups are fragile; a missing or
    /// renamed member returns null and the immediate dereference throws NullReferenceException.
    /// Guard with '?.', cache once in Awake/Start, or check for null first.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ReflectionHazardAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(Diagnostics.UnguardedReflectionDereference);

        private static readonly HashSet<string> LookupMethods = new HashSet<string>
        {
            "GetMethod", "GetField", "GetProperty", "GetConstructor"
        };

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(AnalyzeLookup, SyntaxKind.InvocationExpression);
        }

        private void AnalyzeLookup(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            if (!IsReflectionLookup(invocation))
                return;

            // Walk up through the expression chain. If the lookup result is used as the
            // receiver of a NON-null-conditional member access / invocation / element access,
            // it is dereferenced without a guard -> report.
            var current = (ExpressionSyntax)invocation;
            while (true)
            {
                var parent = current.Parent;
                switch (parent)
                {
                    case MemberAccessExpressionSyntax ma when ma.Expression == current:
                        // Non-null-conditional member access (e.g. .SetValue(...)) dereferences.
                        context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.UnguardedReflectionDereference,
                            invocation.GetLocation(),
                            GetMemberName(invocation)));
                        return;
                    case InvocationExpressionSyntax inv when inv.Expression == current:
                        // The lookup itself invoked (e.g. GetMethod(...)(...) is unusual) - skip.
                        return;
                    case ElementAccessExpressionSyntax ea when ea.Expression == current:
                        context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.UnguardedReflectionDereference,
                            invocation.GetLocation(),
                            GetMemberName(invocation)));
                        return;
                    case ConditionalAccessExpressionSyntax ca when ca.Expression == current:
                        // Null-conditional '?.' guards the dereference -> safe.
                        return;
                    default:
                        // Not directly dereferenced (assigned to a variable, passed as arg, etc.).
                        return;
                }
            }
        }

        private static bool IsReflectionLookup(InvocationExpressionSyntax invocation)
        {
            if (!(invocation.Expression is MemberAccessExpressionSyntax ma))
                return false;
            return LookupMethods.Contains(ma.Name.Identifier.ValueText);
        }

        private static string GetMemberName(InvocationExpressionSyntax invocation)
        {
            var ma = (MemberAccessExpressionSyntax)invocation.Expression;
            return ma.Name.Identifier.ValueText;
        }
    }
}
