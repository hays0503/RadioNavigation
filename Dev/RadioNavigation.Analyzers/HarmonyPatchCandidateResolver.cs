using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RadioNavigation.Analyzers
{
    /// <summary>
    /// A single Harmony patch site: one target method plus optional prefix/postfix handlers.
    /// Expressions are kept as syntax so they can be resolved lazily against the semantic model.
    /// </summary>
    internal sealed class HarmonyPatchCandidate
    {
        public InvocationExpressionSyntax PatchCall;
        public ExpressionSyntax TargetExpr;
        public ExpressionSyntax PrefixExpr;
        public ExpressionSyntax PostfixExpr;
    }

    /// <summary>
    /// Resolves <c>harmony.Patch(...)</c> calls into concrete <see cref="HarmonyPatchCandidate"/>s
    /// by tracing data flow back to their originating <c>typeof(X).GetMethod("Name", ...)</c>
    /// lookups. Supports:
    ///   - direct / named-argument calls:  h.Patch(target, prefix:, postfix:)
    ///   - arrays of anonymous objects iterated by foreach:
    ///         var targets = new[] { new { method = m1, prefix = p1 }, ... };
    ///         foreach (var t in targets) h.Patch(t.method, t.prefix, t.postfix);
    ///   Each array element expands into its own candidate.
    /// </summary>
    internal static class HarmonyPatchCandidateResolver
    {
        /// <summary>
        /// Produce the candidates described by a single <c>harmony.Patch(...)</c> invocation.
        /// A foreach over an array of anonymous objects yields one candidate per element.
        /// </summary>
        public static IEnumerable<HarmonyPatchCandidate> Resolve(
            SyntaxNodeAnalysisContext context,
            InvocationExpressionSyntax patchCall)
        {
            if (!IsPatchCall(patchCall, context.SemanticModel))
                yield break;

            var expanded = TryExpandForeach(context, patchCall);
            if (expanded != null)
            {
                foreach (var c in expanded)
                    yield return c;
                yield break;
            }

            var single = BuildFromArguments(context, patchCall);
            if (single != null)
                yield return single;
        }

        private static bool IsPatchCall(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
        {
            var expr = invocation.Expression as MemberAccessExpressionSyntax;
            if (expr == null || expr.Name.Identifier.Text != "Patch")
                return false;
            var symbol = semanticModel.GetSymbolInfo(invocation).Symbol;
            return symbol != null && symbol.ContainingType != null &&
                   symbol.ContainingType.ToDisplayString().StartsWith("HarmonyLib.");
        }

        /// <summary>
        /// If this Patch call sits inside a <c>foreach (var t in arrOfAnonObjects)</c>, expand
        /// it into one candidate per array element. Returns null when not applicable.
        /// </summary>
        private static List<HarmonyPatchCandidate> TryExpandForeach(
            SyntaxNodeAnalysisContext context,
            InvocationExpressionSyntax patchCall)
        {
            var foreachStmt = patchCall.Ancestors().OfType<ForEachStatementSyntax>().FirstOrDefault();
            if (foreachStmt == null)
                return null;

            var elements = ResolveCollectionElements(context, foreachStmt.Expression);
            if (elements == null || elements.Count == 0)
                return null;

            var result = new List<HarmonyPatchCandidate>();
            foreach (var elem in elements)
            {
                if (!TryReadAnonymousProperties(elem, out var method, out var prefix, out var postfix))
                    continue;

                result.Add(new HarmonyPatchCandidate
                {
                    PatchCall = patchCall,
                    TargetExpr = method,
                    PrefixExpr = prefix,
                    PostfixExpr = postfix
                });
            }
            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// Resolve a collection expression (e.g. <c>targets</c>) to its array element expressions.
        /// Follows local/field initializers to the <c>new[] { ... }</c> literal.
        /// </summary>
        private static List<ExpressionSyntax> ResolveCollectionElements(
            SyntaxNodeAnalysisContext context,
            ExpressionSyntax collectionExpr)
        {
            var current = collectionExpr;
            for (int hop = 0; hop < 8; hop++)
            {
                switch (current)
                {
                    case ImplicitArrayCreationExpressionSyntax implicitArr:
                        return implicitArr.Initializer.Expressions.ToList();

                    case ArrayCreationExpressionSyntax arr:
                        return arr.Initializer?.Expressions.ToList();

                    case IdentifierNameSyntax id:
                        var symbol = context.SemanticModel.GetSymbolInfo(id).Symbol;
                        var init = GetVariableInitializer(symbol);
                        if (init == null)
                            return null;
                        current = init;
                        continue;

                    default:
                        return null;
                }
            }
            return null;
        }

        private static ExpressionSyntax GetVariableInitializer(ISymbol symbol)
        {
            if (symbol == null)
                return null;
            foreach (var loc in symbol.DeclaringSyntaxReferences)
            {
                if (loc.GetSyntax() is VariableDeclaratorSyntax decl && decl.Initializer != null)
                    return decl.Initializer.Value;
            }
            return null;
        }

        /// <summary>
        /// Read the <c>method</c>/<c>prefix</c>/<c>postfix</c> property initializers from an
        /// anonymous object creation expression. Returns false if the element isn't an anonymous
        /// object with at least a <c>method</c> member.
        /// </summary>
        private static bool TryReadAnonymousProperties(
            ExpressionSyntax element,
            out ExpressionSyntax method,
            out ExpressionSyntax prefix,
            out ExpressionSyntax postfix)
        {
            method = null;
            prefix = null;
            postfix = null;

            var unwrapped = UnwrapConditional(element);
            if (!(unwrapped is AnonymousObjectCreationExpressionSyntax anon))
                return false;

            foreach (var init in anon.Initializers)
            {
                var name = init.NameEquals?.Name?.ToString();
                switch (name)
                {
                    case "method": method = init.Expression; break;
                    case "prefix": prefix = init.Expression; break;
                    case "postfix": postfix = init.Expression; break;
                }
            }
            return method != null;
        }

        /// <summary>
        /// Build a single candidate directly from a Patch call's arguments (positional original +
        /// named prefix/postfix), without any foreach indirection.
        /// </summary>
        private static HarmonyPatchCandidate BuildFromArguments(
            SyntaxNodeAnalysisContext context,
            InvocationExpressionSyntax patchCall)
        {
            var target = GetArgument(patchCall, "original", 0);
            if (target == null)
                return null;

            return new HarmonyPatchCandidate
            {
                PatchCall = patchCall,
                TargetExpr = target,
                PrefixExpr = GetArgument(patchCall, "prefix", -1),
                PostfixExpr = GetArgument(patchCall, "postfix", -1)
            };
        }

        /// <summary>
        /// Fetch an argument by named parameter, falling back to a positional index when the
        /// argument has no name colon. Returns null when absent.
        /// </summary>
        private static ExpressionSyntax GetArgument(InvocationExpressionSyntax invocation, string name, int position)
        {
            foreach (var arg in invocation.ArgumentList.Arguments)
            {
                if (arg.NameColon != null && arg.NameColon.Name.Identifier.Text == name)
                    return arg.Expression;
            }
            if (position >= 0 && position < invocation.ArgumentList.Arguments.Count)
            {
                var arg = invocation.ArgumentList.Arguments[position];
                if (arg.NameColon == null)
                    return arg.Expression;
            }
            return null;
        }

        /// <summary>
        /// Peel a ternary conditional, returning the first branch that is not a null literal.
        /// Used to skip <c>handler.IsNotNull() ? new HarmonyMethod(h) : null</c> wrappers.
        /// </summary>
        private static ExpressionSyntax UnwrapConditional(ExpressionSyntax expr)
        {
            while (expr is ConditionalExpressionSyntax cond)
            {
                var whenTrue = cond.WhenTrue;
                var whenFalse = cond.WhenFalse;
                if (!IsNullLiteral(whenTrue))
                    expr = whenTrue;
                else if (!IsNullLiteral(whenFalse))
                    expr = whenFalse;
                else
                    return null;
            }
            return expr;
        }

        private static bool IsNullLiteral(ExpressionSyntax expr)
        {
            return expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression);
        }
    }
}
