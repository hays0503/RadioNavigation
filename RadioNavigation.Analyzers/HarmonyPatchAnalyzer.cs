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
    /// Analyzes the mod's imperative Harmony patching pattern in Main.cs:
    ///
    ///   var continueStory = typeof(RadioStory).GetMethod("ContinueStory", BindingFlags...);
    ///   var injectT3PLL38  = patchType.GetMethod("InjectT3PLL38Decision", anyStatic);
    ///   var targets = new[] { new { method = continueStory, postfix = new HarmonyMethod(injectT3PLL38) }, ... };
    ///   foreach (var t in targets) harmony.Patch(t.method, prefix: t.prefix, postfix: t.postfix);
    ///
    /// Candidates are resolved through data flow (locals, anonymous objects, arrays, foreach)
    /// by <see cref="HarmonyPatchCandidateResolver"/>, then targets/handlers are validated.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class HarmonyPatchAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(
                Diagnostics.TargetNotFound,
                Diagnostics.TargetAmbiguous,
                Diagnostics.ParameterMismatch,
                Diagnostics.InvalidSpecialParameter,
                Diagnostics.RefOutMismatch,
                Diagnostics.InvalidHandlerReturnType);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // Drive analysis from each harmony.Patch(...) call; resolve its target/handlers
            // backward through data flow.
            context.RegisterSyntaxNodeAction(AnalyzePatchCall, SyntaxKind.InvocationExpression);
        }

        private static readonly string[] SpecialParams =
        {
            "__instance", "__result", "__state", "__originalMethod", "__args"
        };

        private static bool IsHarmonySpecial(string name) => SpecialParams.Contains(name);

        private void AnalyzePatchCall(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;

            foreach (var candidate in HarmonyPatchCandidateResolver.Resolve(context, invocation))
            {
                var target = ResolveMethodInfo(context, candidate.TargetExpr, out var getMethod);
                if (target == null)
                {
                    // We traced the target back to a concrete typeof(X).GetMethod("Name") call
                    // but Roslyn could not resolve it. Before declaring it missing, consult raw
                    // metadata: game assemblies are obfuscated and Roslyn under-reports their
                    // members (verified: ShipControlUI 30/87 methods), so "not found" against
                    // Roslyn alone would flood false positives.
                    if (getMethod != null)
                        ReportUnresolvedTarget(context, getMethod.Value);
                    continue;
                }

                if (candidate.PrefixExpr != null)
                    ValidateHandler(context, candidate, candidate.PrefixExpr, target, isPrefix: true);
                if (candidate.PostfixExpr != null)
                    ValidateHandler(context, candidate, candidate.PostfixExpr, target, isPrefix: false);
            }
        }

        /// <summary>
        /// Decide how to report a target Roslyn could not resolve, consulting raw metadata:
        ///
        ///   Found in metadata      -> obfuscation hid it; do NOT report (it exists).
        ///   Absent in metadata     -> genuinely missing; report RN0001 regardless of source.
        ///   Ambiguous in metadata  -> several overloads match a name-only lookup; report RN0002.
        ///   Metadata unavailable   -> fall back to old behavior: report RN0001 only for in-source
        ///                             types (game types can't be confirmed absent without an index).
        /// </summary>
        private static void ReportUnresolvedTarget(
            SyntaxNodeAnalysisContext context,
            ResolvedGetMethod getMethod)
        {
            var lookup = HarmonyTargetResolver.Lookup(
                HarmonyTargetResolver.GetIndex(context),
                getMethod.DeclaringType,
                getMethod.MethodName,
                getMethod.ParameterTypes,
                getMethod.RequireStatic);

            switch (lookup)
            {
                case MetadataLookupResult.Found:
                    return;
                case MetadataLookupResult.Ambiguous:
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.TargetAmbiguous,
                        getMethod.Location,
                        getMethod.MethodName,
                        getMethod.DeclaringType.ToDisplayString()));
                    return;
                case MetadataLookupResult.MethodNotFound:
                case MetadataLookupResult.TypeNotFound:
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.TargetNotFound,
                        getMethod.Location,
                        getMethod.MethodName,
                        getMethod.DeclaringType.ToDisplayString()));
                    return;
                default:
                    if (getMethod.DeclaringType.Locations.Any(l => l.IsInSource))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.TargetNotFound,
                            getMethod.Location,
                            getMethod.MethodName,
                            getMethod.DeclaringType.ToDisplayString()));
                    }
                    return;
            }
        }

        private void ValidateHandler(
            SyntaxNodeAnalysisContext context,
            HarmonyPatchCandidate candidate,
            ExpressionSyntax handlerExpr,
            IMethodSymbol target,
            bool isPrefix)
        {
            var inner = UnwrapHarmonyMethod(handlerExpr);
            var handlerSymbol = ResolveMethodInfo(context, inner, out _);
            if (handlerSymbol == null)
                return;

            ValidateHandlerReturnType(context, candidate.PatchCall, handlerSymbol, isPrefix);
            ValidateSignature(context, candidate.PatchCall, handlerSymbol, target, isPrefix);
        }

        /// <summary>
        /// RN0009 — Harmony handler return-type contract:
        ///   prefix  must return void or bool
        ///   postfix must return void
        /// A wrong return type breaks the patch at runtime (e.g. a non-bool prefix makes
        /// Harmony unable to skip the original method).
        /// </summary>
        private static void ValidateHandlerReturnType(
            SyntaxNodeAnalysisContext context,
            InvocationExpressionSyntax patchCall,
            IMethodSymbol handler,
            bool isPrefix)
        {
            var returnType = handler.ReturnType;
            if (returnType.SpecialType == SpecialType.System_Void)
                return;

            if (isPrefix && returnType.SpecialType == SpecialType.System_Boolean)
                return;

            string expected = isPrefix ? "void or bool" : "void";
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.InvalidHandlerReturnType,
                patchCall.GetLocation(),
                isPrefix ? "prefix" : "postfix",
                handler.Name,
                returnType.ToDisplayString(),
                expected));
        }

        /// <summary>
        /// Resolve an expression to the <see cref="IMethodSymbol"/> it denotes as a MethodInfo:
        ///   - inline <c>typeof(X).GetMethod("Name", ...)</c>
        ///   - a local/field whose initializer is such a GetMethod call
        ///   - a conditional that selects one of the above
        /// Returns null when unresolvable (e.g. a Type-typed variable).
        /// When the expression traces back to a concrete GetMethod call but the method does not
        /// exist, <paramref name="getMethod"/> carries the lookup details for RN0001 reporting.
        /// </summary>
        private static IMethodSymbol ResolveMethodInfo(
            SyntaxNodeAnalysisContext context,
            ExpressionSyntax expr,
            out ResolvedGetMethod? getMethod)
        {
            var semanticModel = context.SemanticModel;
            var current = expr;
            getMethod = null;

            for (int hop = 0; hop < 8; hop++)
            {
                current = UnwrapConditional(current);
                current = UnwrapHarmonyMethod(current);

                // Inline typeof(X).GetMethod("Name", ...) -> resolve X.Name directly.
                if (current is InvocationExpressionSyntax inv &&
                    TryGetMethodTarget(context, inv, out var declaringType, out var methodName,
                        out var paramTypes, out var requireStatic))
                {
                    var resolved = ResolveTargetMethod(context, declaringType, methodName, paramTypes);
                    if (resolved == null)
                        getMethod = new ResolvedGetMethod(declaringType, methodName, inv.GetLocation(), paramTypes, requireStatic);
                    return resolved;
                }

                // Any other invocation (e.g. patchType.GetMethod(...) on a Type VARIABLE) cannot
                // be followed statically. Return null rather than resolving the invoked method
                // symbol itself - otherwise Type.GetMethod(name, bindingAttr) would be mistaken
                // for a handler and its 'name'/'bindingAttr' params reported as mismatches.
                if (current is InvocationExpressionSyntax)
                    return null;

                // Direct method-group / delegate reference.
                var symbol = semanticModel.GetSymbolInfo(current).Symbol;
                if (symbol is IMethodSymbol m)
                    return m;

                // A local/field holding a MethodInfo: follow its initializer back to GetMethod.
                if (symbol is ILocalSymbol || symbol is IFieldSymbol)
                {
                    var init = GetVariableInitializer(symbol);
                    if (init == null)
                        return null;
                    current = init;
                    continue;
                }

                return null;
            }
            return null;
        }

        private readonly struct ResolvedGetMethod
        {
            public ResolvedGetMethod(
                ITypeSymbol declaringType,
                string methodName,
                Location location,
                IReadOnlyList<string> parameterTypes,
                bool? requireStatic)
            {
                DeclaringType = declaringType;
                MethodName = methodName;
                Location = location;
                ParameterTypes = parameterTypes;
                RequireStatic = requireStatic;
            }

            public ITypeSymbol DeclaringType { get; }
            public string MethodName { get; }
            public Location Location { get; }

            /// <summary>
            /// Explicit parameter type names from <c>GetMethod("X", new[]{ typeof(A), ... })</c>,
            /// or null for a name-only lookup.
            /// </summary>
            public IReadOnlyList<string> ParameterTypes { get; }

            /// <summary>
            /// True when BindingFlags contains Static (and not Instance), false when it contains
            /// Instance (and not Static), null when the flags don't pin down either. Used to
            /// disambiguate same-named static/instance overloads.
            /// </summary>
            public bool? RequireStatic { get; }
        }

        private static ExpressionSyntax GetVariableInitializer(ISymbol symbol)
        {
            foreach (var loc in symbol.DeclaringSyntaxReferences)
            {
                if (loc.GetSyntax() is VariableDeclaratorSyntax decl && decl.Initializer != null)
                    return decl.Initializer.Value;
            }
            return null;
        }

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

        private static ExpressionSyntax UnwrapHarmonyMethod(ExpressionSyntax expr)
        {
            if (expr is ObjectCreationExpressionSyntax oc &&
                oc.Type.ToString().Contains("HarmonyMethod") &&
                oc.ArgumentList != null && oc.ArgumentList.Arguments.Count > 0)
            {
                return oc.ArgumentList.Arguments[0].Expression;
            }
            return expr;
        }

        /// <summary>
        /// Recognize <c>&lt;receiver&gt;.GetMethod("Name", ...)</c> where the receiver is
        /// <c>typeof(X)</c>. Returns the declaring type, requested method name, explicit parameter
        /// types (when passed as <c>new[] { typeof(A), ... }</c>), and a static/instance pin from
        /// BindingFlags when present.
        /// </summary>
        private static bool TryGetMethodTarget(
            SyntaxNodeAnalysisContext context,
            InvocationExpressionSyntax invocation,
            out ITypeSymbol declaringType,
            out string methodName,
            out IReadOnlyList<string> parameterTypes,
            out bool? requireStatic)
        {
            declaringType = null;
            methodName = null;
            parameterTypes = null;
            requireStatic = null;

            var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
            if (memberAccess == null)
                return false;

            bool isConstructor = memberAccess.Name.Identifier.Text == "GetConstructor";
            if (!isConstructor && memberAccess.Name.Identifier.Text != "GetMethod")
                return false;

            if (invocation.ArgumentList.Arguments.Count == 0)
                return false;

            int scanStart;
            if (isConstructor)
            {
                // GetConstructor(Type[] types) -> target .ctor with those parameter types.
                methodName = ".ctor";
                var ctorArg = invocation.ArgumentList.Arguments[0].Expression;
                if (!(ctorArg is ImplicitArrayCreationExpressionSyntax ctorArr))
                    return false;
                parameterTypes = ExtractParameterTypes(context, ctorArr);
                if (parameterTypes == null)
                    return false;
                scanStart = 1;
            }
            else
            {
                var nameArg = invocation.ArgumentList.Arguments[0].Expression;
                if (!(nameArg is LiteralExpressionSyntax lit) || !lit.IsKind(SyntaxKind.StringLiteralExpression))
                    return false;
                methodName = lit.Token.ValueText;
                scanStart = 1;
            }

            // Scan remaining arguments for an explicit Type[] (parameter types) and/or a
            // BindingFlags expression. GetMethod overloads place them in different positions:
            //   GetMethod(name, Type[])                 -> arg1 = types
            //   GetMethod(name, BindingFlags)           -> arg1 = flags
            //   GetMethod(name, Type[], BindingFlags)   -> arg1 = types, arg2 = flags
            for (int i = scanStart; i < invocation.ArgumentList.Arguments.Count; i++)
            {
                var expr = invocation.ArgumentList.Arguments[i].Expression;
                if (expr is ImplicitArrayCreationExpressionSyntax arr)
                {
                    parameterTypes = ExtractParameterTypes(context, arr);
                }
                else if (IsBindingFlagsExpression(context, expr))
                {
                    requireStatic = ParseRequireStatic(context, expr);
                }
            }

            var receiver = memberAccess.Expression;
            if (receiver is TypeOfExpressionSyntax tof)
            {
                declaringType = context.SemanticModel.GetTypeInfo(tof.Type).Type;
                return declaringType != null;
            }

            // Receiver may be a Type-typed variable (e.g. patchType = typeof(RadioStoryIntegration)).
            // Follow its initializer back to the typeof(...) expression to recover the type.
            if (receiver is IdentifierNameSyntax id)
            {
                var sym = context.SemanticModel.GetSymbolInfo(id).Symbol;
                var init = GetVariableInitializer(sym);
                if (init is TypeOfExpressionSyntax initTof)
                {
                    declaringType = context.SemanticModel.GetTypeInfo(initTof.Type).Type;
                    return declaringType != null;
                }
            }
            return false;
        }

        private static bool IsBindingFlagsExpression(SyntaxNodeAnalysisContext context, ExpressionSyntax expr)
        {
            switch (expr)
            {
                case IdentifierNameSyntax id:
                    // A bare flag name (Static/Instance/...) or a local variable holding a
                    // BindingFlags bitwise-or (e.g. anyStatic = Public|NonPublic|Static).
                    if (BindingFlagNames.Contains(id.Identifier.Text))
                        return true;
                    return FollowBindingFlagsVariable(context, id) != null;
                // BindingFlags.Static etc. parse as a member access whose Name is the flag.
                case MemberAccessExpressionSyntax ma:
                    return BindingFlagNames.Contains(ma.Name.Identifier.Text);
                case BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.BitwiseOrExpression):
                    return IsBindingFlagsExpression(context, bin.Left) && IsBindingFlagsExpression(context, bin.Right);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Follow an identifier that names a local/field back to its initializer expression and
        /// return it when that initializer is itself a BindingFlags bitwise-or expression; else null.
        /// </summary>
        private static ExpressionSyntax FollowBindingFlagsVariable(
            SyntaxNodeAnalysisContext context,
            IdentifierNameSyntax id)
        {
            var symbol = context.SemanticModel.GetSymbolInfo(id).Symbol;
            if (symbol == null)
                return null;
            var init = GetVariableInitializer(symbol);
            if (init == null || !IsBindingFlagsExpression(context, init))
                return null;
            return init;
        }

        private static readonly System.Collections.Generic.HashSet<string> BindingFlagNames =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal)
            {
                "Public", "NonPublic", "Static", "Instance", "FlattenHierarchy"
            };

        /// <summary>
        /// Derive a static/instance requirement from a BindingFlags bitwise-or expression (which
        /// may be held in a local variable). Returns true when Static is set (and not Instance),
        /// false when Instance is set (and not Static), null when neither or both are set.
        /// </summary>
        private static bool? ParseRequireStatic(SyntaxNodeAnalysisContext context, ExpressionSyntax expr)
        {
            bool hasStatic = false, hasInstance = false;
            CollectBindingFlags(context, expr, ref hasStatic, ref hasInstance);

            if (hasStatic && !hasInstance) return true;
            if (hasInstance && !hasStatic) return false;
            return null;
        }

        private static void CollectBindingFlags(
            SyntaxNodeAnalysisContext context,
            ExpressionSyntax expr,
            ref bool hasStatic,
            ref bool hasInstance)
        {
            switch (expr)
            {
                case IdentifierNameSyntax id:
                    if (id.Identifier.Text == "Static") hasStatic = true;
                    else if (id.Identifier.Text == "Instance") hasInstance = true;
                    else
                    {
                        var followed = FollowBindingFlagsVariable(context, id);
                        if (followed != null)
                            CollectBindingFlags(context, followed, ref hasStatic, ref hasInstance);
                    }
                    break;
                case MemberAccessExpressionSyntax ma:
                    if (ma.Name.Identifier.Text == "Static") hasStatic = true;
                    else if (ma.Name.Identifier.Text == "Instance") hasInstance = true;
                    break;
                case BinaryExpressionSyntax bin when bin.IsKind(SyntaxKind.BitwiseOrExpression):
                    CollectBindingFlags(context, bin.Left, ref hasStatic, ref hasInstance);
                    CollectBindingFlags(context, bin.Right, ref hasStatic, ref hasInstance);
                    break;
            }
        }

        /// <summary>
        /// Parse <c>new[] { typeof(A), typeof(B) }</c> into normalized type names, or null when
        /// the argument is not such an array literal.
        /// </summary>
        private static IReadOnlyList<string> ExtractParameterTypes(
            SyntaxNodeAnalysisContext context,
            ExpressionSyntax arg)
        {
            if (!(arg is ImplicitArrayCreationExpressionSyntax arr))
                return null;

            var result = new List<string>();
            foreach (var element in arr.Initializer.Expressions)
            {
                if (element is TypeOfExpressionSyntax tof)
                {
                    var type = context.SemanticModel.GetTypeInfo(tof.Type).Type;
                    if (type == null)
                        return null;
                    result.Add(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }
                else
                {
                    return null;
                }
            }
            return result;
        }

        private static IMethodSymbol ResolveTargetMethod(
            SyntaxNodeAnalysisContext context,
            ITypeSymbol declaringType,
            string methodName,
            IReadOnlyList<string> parameterTypes)
        {
            foreach (var member in declaringType.GetMembers(methodName))
            {
                if (!(member is IMethodSymbol m) ||
                    (m.MethodKind != MethodKind.Ordinary && m.MethodKind != MethodKind.Constructor))
                    continue;
                // When an explicit Type[] was given, only a candidate whose parameter count and
                // canonical type names match can be the requested overload.
                if (parameterTypes != null)
                {
                    if (m.Parameters.Length != parameterTypes.Count)
                        continue;
                    bool allMatch = true;
                    for (int i = 0; i < parameterTypes.Count; i++)
                    {
                        var canon = CanonicalTypeName(m.Parameters[i].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                        if (!string.Equals(canon, CanonicalTypeName(parameterTypes[i]), System.StringComparison.Ordinal))
                        {
                            allMatch = false;
                            break;
                        }
                    }
                    if (!allMatch)
                        continue;
                }
                return m;
            }
            return null;
        }

        /// <summary>
        /// Reduce a type name to a canonical simple form for cross-source matching (mirrors
        /// GameMetadataIndex.CanonicalTypeName). "global::System.String" and "String" both -> "String".
        /// </summary>
        private static string CanonicalTypeName(string typeName)
        {
            if (typeName == null)
                return string.Empty;
            if (typeName.StartsWith("global::", System.StringComparison.Ordinal))
                typeName = typeName.Substring(8);
            int dot = typeName.LastIndexOf('.');
            return dot >= 0 ? typeName.Substring(dot + 1) : typeName;
        }

        private static void ValidateSignature(
            SyntaxNodeAnalysisContext context,
            InvocationExpressionSyntax patchCall,
            IMethodSymbol handler,
            IMethodSymbol target,
            bool isPrefix)
        {
            var targetParams = target.Parameters;

            foreach (var hp in handler.Parameters)
            {
                var hName = hp.Name;
                var hType = hp.Type;

                if (IsHarmonySpecial(hName))
                {
                    ValidateSpecialParam(context, patchCall, hp, target, isPrefix);
                    continue;
                }

                var tp = targetParams.FirstOrDefault(p => p.Name == hName);
                if (tp == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.ParameterMismatch,
                        patchCall.GetLocation(),
                        hName, hType?.ToDisplayString() ?? "?",
                        "(none)", "(no matching target parameter)"));
                    continue;
                }

                CheckRefOut(context, patchCall, hp, tp);
                CheckTypeCompatibility(context, patchCall, hp, tp);
            }
        }

        private static void ValidateSpecialParam(
            SyntaxNodeAnalysisContext context,
            InvocationExpressionSyntax patchCall,
            IParameterSymbol hp,
            IMethodSymbol target,
            bool isPrefix)
        {
            switch (hp.Name)
            {
                case "__result":
                    if (target.ReturnsVoid)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.InvalidSpecialParameter,
                            patchCall.GetLocation(),
                            "__result", "target method returns void"));
                    }
                    else if (!SymbolEqualityComparer.Default.Equals(hp.Type, target.ReturnType))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.ParameterMismatch,
                            patchCall.GetLocation(),
                            "__result", hp.Type?.ToDisplayString() ?? "?",
                            "__result", target.ReturnType?.ToDisplayString() ?? "?"));
                    }
                    break;

                case "__instance":
                    if (target.IsStatic)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.InvalidSpecialParameter,
                            patchCall.GetLocation(),
                            "__instance", "target method is static"));
                    }
                    break;

                default:
                    // __state, __originalMethod, __args are always legal.
                    break;
            }
        }

        private static void CheckRefOut(
            SyntaxNodeAnalysisContext context,
            InvocationExpressionSyntax patchCall,
            IParameterSymbol hp,
            IParameterSymbol tp)
        {
            bool hByRef = hp.RefKind != RefKind.None;
            bool tByRef = tp.RefKind != RefKind.None;
            if (hByRef != tByRef)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.RefOutMismatch,
                    patchCall.GetLocation(),
                    hp.Name, hp.RefKind.ToString(),
                    tp.Name, tp.RefKind.ToString()));
            }
        }

        private static void CheckTypeCompatibility(
            SyntaxNodeAnalysisContext context,
            InvocationExpressionSyntax patchCall,
            IParameterSymbol hp,
            IParameterSymbol tp)
        {
            var hType = hp.Type;
            var tType = tp.Type;
            if (hType == null || tType == null)
                return;

            // Strict contract: a named parameter must be declared with the SAME type as the
            // target parameter it binds to. We deliberately do NOT accept assignability here:
            // e.g. `Prefix(object decision)` binding to a `Decision decision` target parameter
            // would pass a ClassifyConversion check (object accepts anything) yet is almost
            // always a bug - the handler cannot use the typed members. Ref/out is validated
            // separately in CheckRefOut.
            if (SymbolEqualityComparer.Default.Equals(hType, tType))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.ParameterMismatch,
                patchCall.GetLocation(),
                hp.Name, hType.ToDisplayString(),
                tp.Name, tType.ToDisplayString()));
        }
    }
}
