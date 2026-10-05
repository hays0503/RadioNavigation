using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
                        // Not directly dereferenced. Follow an assignment to a local:
                        //   var fi = t.GetField("f"); fi.SetValue(...);   // unguarded -> report
                        AnalyzeAssignmentToLocal(context, invocation, current, parent);
                        return;
                }
            }
        }

        /// <summary>
        /// A reflection lookup stored in a local and dereferenced later without a null
        /// guard is the same NRE hazard as the immediate form. Conservative: if the local
        /// is null-checked anywhere in scope (== null / != null / x?. / x is null), it is
        /// treated as guarded and not reported.
        /// </summary>
        private static void AnalyzeAssignmentToLocal(
            SyntaxNodeAnalysisContext context,
            InvocationExpressionSyntax lookup,
            ExpressionSyntax current,
            SyntaxNode parent)
        {
            ISymbol symbol = null;

            if (parent is EqualsValueClauseSyntax evc && evc.Value == current
                && evc.Parent is VariableDeclaratorSyntax decl)
            {
                symbol = context.SemanticModel.GetDeclaredSymbol(decl);
            }
            else if (parent is AssignmentExpressionSyntax assignment && assignment.Right == current)
            {
                symbol = context.SemanticModel.GetSymbolInfo(assignment.Left).Symbol;
            }

            var local = symbol as ILocalSymbol;
            if (local == null)
                return;

            SyntaxNode scope = lookup.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (scope == null)
                scope = lookup.Ancestors().OfType<AnonymousFunctionExpressionSyntax>().FirstOrDefault();
            if (scope == null)
                return;

            if (HasNullGuard(scope, local.Name))
                return;

            if (!HasUnguardedDereference(scope, local.Name))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.UnguardedReflectionDereference,
                lookup.GetLocation(),
                GetMemberName(lookup)));
        }

        private static bool HasNullGuard(SyntaxNode scope, string name)
        {
            foreach (var binary in scope.DescendantNodes().OfType<BinaryExpressionSyntax>())
            {
                bool isEquality = binary.IsKind(SyntaxKind.EqualsExpression)
                    || binary.IsKind(SyntaxKind.NotEqualsExpression);
                if (!isEquality)
                    continue;
                if ((IsName(binary.Left, name) && IsNullLiteral(binary.Right))
                    || (IsName(binary.Right, name) && IsNullLiteral(binary.Left)))
                    return true;
            }

            foreach (var conditional in scope.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>())
            {
                if (IsName(conditional.Expression, name))
                    return true;
            }

            foreach (var isPattern in scope.DescendantNodes().OfType<IsPatternExpressionSyntax>())
            {
                if (IsName(isPattern.Expression, name))
                    return true;
            }

            // Project null-guard idiom: x.IsNull() / x.IsNotNull() (ComparisonExtensions).
            foreach (var invocation in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is MemberAccessExpressionSyntax ma
                    && (ma.Name.Identifier.ValueText == "IsNull" || ma.Name.Identifier.ValueText == "IsNotNull")
                    && IsName(ma.Expression, name))
                    return true;
            }

            return false;
        }

        private static bool HasUnguardedDereference(SyntaxNode scope, string name)
        {
            foreach (var ma in scope.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                if (!IsName(ma.Expression, name))
                    continue;
                // 'x?.y' is a ConditionalAccessExpression, not a MemberAccessExpression,
                // so reaching here means a plain dereference.
                return true;
            }
            return false;
        }

        private static bool IsName(ExpressionSyntax expr, string name)
        {
            return expr is IdentifierNameSyntax id && id.Identifier.ValueText == name;
        }

        private static bool IsNullLiteral(ExpressionSyntax expr)
        {
            return expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression);
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
