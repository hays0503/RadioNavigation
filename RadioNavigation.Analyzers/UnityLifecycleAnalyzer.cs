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
    /// Unity-specific rules for the mod's MonoBehaviour code.
    ///
    /// RN0006 — per-frame lookup misuse: GetComponent*/FindObjectOfType/GameObject.Find/
    ///          called directly inside Update/LateUpdate/FixedUpdate. These should be
    ///          cached in Awake/Start, not resolved every frame.
    /// RN0007 — allocation in the audio callback: new / LINQ / boxing / string concat /
    ///          interpolation inside OnAudioFilterRead(float[], int). GC there causes
    ///          audible glitches.
    /// RN0008 — main-thread-only Unity API called from an explicit background context
    ///          (Task.Run, ThreadPool.QueueUserWorkItem, new Thread, Parallel.*). Only
    ///          fires when the background context is provable; ordinary methods are NOT
    ///          flagged because Roslyn cannot prove they run off the main thread.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnityLifecycleAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(
                Diagnostics.UnityLifecycleMisuse,
                Diagnostics.AudioCallbackAllocation,
                Diagnostics.MainThreadApiMisuse);

        private static readonly HashSet<string> PerFrameMethods = new HashSet<string>
        {
            "Update", "LateUpdate", "FixedUpdate"
        };

        private static readonly HashSet<string> LookupMethodNames = new HashSet<string>
        {
            "GetComponent", "GetComponents",
            "GetComponentInChildren", "GetComponentsInChildren",
            "GetComponentInParent", "GetComponentsInParent",
            "FindObjectOfType", "FindObjectsOfType",
            "Find", "FindWithTag", "FindObjectOfTypeAll"
        };

        private static readonly HashSet<string> LinqMethodNames = new HashSet<string>
        {
            "Where", "Select", "SelectMany", "Any", "All", "First", "FirstOrDefault",
            "Last", "LastOrDefault", "Single", "SingleOrDefault", "ToList", "ToArray",
            "ToDictionary", "OrderBy", "OrderByDescending", "ThenBy", "GroupBy",
            "Distinct", "Count", "Sum", "Aggregate", "Cast", "OfType", "Zip", "Concat"
        };

        private static readonly HashSet<string> KnownUnityApiNames = new HashSet<string>
        {
            "GetComponent", "GetComponents",
            "GetComponentInChildren", "GetComponentsInChildren",
            "GetComponentInParent", "GetComponentsInParent",
            "FindObjectOfType", "FindObjectsOfType",
            "Instantiate", "Destroy", "DestroyImmediate",
            "SendMessage", "BroadcastMessage", "Invoke", "InvokeRepeating",
            "AddComponent", "SetActive", "DontDestroyOnLoad"
        };

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(AnalyzePerFrameLookup, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeAudioAllocation, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeAudioAllocation, SyntaxKind.ObjectCreationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeAudioAllocation, SyntaxKind.InterpolatedStringExpression);
            context.RegisterSyntaxNodeAction(AnalyzeAudioAllocation, SyntaxKind.AddExpression);
            context.RegisterSyntaxNodeAction(AnalyzeAudioAllocation, SyntaxKind.EqualsValueClause);
            context.RegisterSyntaxNodeAction(AnalyzeAudioAllocation, SyntaxKind.SimpleAssignmentExpression);
            context.RegisterSyntaxNodeAction(AnalyzeBackgroundContext, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeBackgroundContext, SyntaxKind.ObjectCreationExpression);
        }

        private static bool IsInsideMethod(SyntaxNode node, out MethodDeclarationSyntax method)
        {
            method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            return method != null;
        }

        private void AnalyzePerFrameLookup(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            if (!IsInsideMethod(invocation, out var method))
                return;

            if (!PerFrameMethods.Contains(method.Identifier.ValueText))
                return;

            string name = GetCalledName(invocation);
            if (name == null || !LookupMethodNames.Contains(name))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.UnityLifecycleMisuse,
                invocation.GetLocation(),
                name,
                method.Identifier.ValueText));
        }

        private void AnalyzeAudioAllocation(SyntaxNodeAnalysisContext context)
        {
            if (!IsInsideMethod(context.Node, out var method))
                return;

            if (method.Identifier.ValueText != "OnAudioFilterRead")
                return;

            switch (context.Node.Kind())
            {
                case SyntaxKind.ObjectCreationExpression:
                    ReportAudioAllocation(context, context.Node, "new");
                    break;
                case SyntaxKind.InterpolatedStringExpression:
                    ReportAudioAllocation(context, context.Node, "string interpolation");
                    break;
                case SyntaxKind.AddExpression:
                    if (IsStringConcatenation((BinaryExpressionSyntax)context.Node, context))
                        ReportAudioAllocation(context, context.Node, "string concatenation");
                    break;
                case SyntaxKind.InvocationExpression:
                    AnalyzeAudioInvocation(context, (InvocationExpressionSyntax)context.Node);
                    break;
                case SyntaxKind.EqualsValueClause:
                    AnalyzeBoxing(context, ((EqualsValueClauseSyntax)context.Node).Value);
                    break;
                case SyntaxKind.SimpleAssignmentExpression:
                    AnalyzeBoxing(context, ((AssignmentExpressionSyntax)context.Node).Right);
                    break;
            }
        }

        private void AnalyzeBoxing(SyntaxNodeAnalysisContext context, ExpressionSyntax value)
        {
            var typeInfo = context.SemanticModel.GetTypeInfo(value);
            if (typeInfo.Type == null || typeInfo.ConvertedType == null)
                return;
            if (typeInfo.Type.IsValueType && !typeInfo.ConvertedType.IsValueType)
                ReportAudioAllocation(context, value, "boxing");
        }

        private void AnalyzeAudioInvocation(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
        {
            string name = GetCalledName(invocation);
            if (name == null)
                return;

            if (LinqMethodNames.Contains(name))
            {
                ReportAudioAllocation(context, invocation, $"LINQ {name}");
                return;
            }

            // Boxing: implicit conversion of a value type to object/interface via a call
            // such as string.Concat(object) or Debug.Log(object). Detect by checking whether
            // any argument is implicitly converted to a non-value-type parameter.
            foreach (var arg in invocation.ArgumentList.Arguments)
            {
                var typeInfo = context.SemanticModel.GetTypeInfo(arg.Expression);
                if (typeInfo.Type == null || typeInfo.ConvertedType == null)
                    continue;
                if (typeInfo.Type.IsValueType && !typeInfo.ConvertedType.IsValueType)
                {
                    ReportAudioAllocation(context, arg.Expression, "boxing");
                    return;
                }
            }
        }

        private static bool IsStringConcatenation(BinaryExpressionSyntax binary, SyntaxNodeAnalysisContext context)
        {
            var left = context.SemanticModel.GetTypeInfo(binary.Left).Type;
            var right = context.SemanticModel.GetTypeInfo(binary.Right).Type;
            return (left != null && left.SpecialType == SpecialType.System_String)
                || (right != null && right.SpecialType == SpecialType.System_String);
        }

        private static void ReportAudioAllocation(SyntaxNodeAnalysisContext context, SyntaxNode node, string what)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.AudioCallbackAllocation,
                node.GetLocation(),
                what));
        }

        private void AnalyzeBackgroundContext(SyntaxNodeAnalysisContext context)
        {
            // Only fire when we can PROVE a background execution context. We detect the
            // well-known executor call sites and inspect their delegate/lambda arguments.
            if (context.Node is InvocationExpressionSyntax invocation)
            {
                bool ok = TryGetBackgroundExecutor(context, invocation, out string executor);
                if (!ok)
                    return;
                foreach (var arg in invocation.ArgumentList.Arguments)
                    InspectDelegateForUnityApi(context, arg.Expression, executor);
            }
            else if (context.Node is ObjectCreationExpressionSyntax creation)
            {
                if (!IsThreadConstruction(creation))
                    return;
                foreach (var arg in creation.ArgumentList?.Arguments ?? default)
                    InspectDelegateForUnityApi(context, arg.Expression, "new Thread");
            }
        }

        private static bool TryGetBackgroundExecutor(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation, out string executor)
        {
            executor = null;
            var expr = invocation.Expression;

            string methodName = null;
            string receiverType = null;

            switch (expr)
            {
                case MemberAccessExpressionSyntax ma:
                    methodName = ma.Name.Identifier.ValueText;
                    receiverType = GetReceiverTypeName(context, ma.Expression);
                    break;
                case IdentifierNameSyntax id:
                    methodName = id.Identifier.ValueText;
                    break;
                case GenericNameSyntax gn:
                    methodName = gn.Identifier.ValueText;
                    break;
                default:
                    return false;
            }

            if (methodName == null)
                return false;

            switch (methodName)
            {
                case "Run":
                    if (receiverType == "Task") { executor = "Task.Run"; return true; }
                    return false;
                case "StartNew":
                    if (receiverType == "TaskFactory") { executor = "Task.Factory.StartNew"; return true; }
                    return false;
                case "QueueUserWorkItem":
                    if (receiverType == "ThreadPool") { executor = "ThreadPool.QueueUserWorkItem"; return true; }
                    return false;
                case "For":
                case "ForEach":
                case "Invoke":
                    if (receiverType == "Parallel") { executor = $"Parallel.{methodName}"; return true; }
                    return false;
                default:
                    return false;
            }
        }

        private static string GetReceiverTypeName(SyntaxNodeAnalysisContext context, ExpressionSyntax receiver)
        {
            var typeInfo = context.SemanticModel.GetTypeInfo(receiver);
            var symbol = typeInfo.Type ?? typeInfo.ConvertedType;
            if (symbol != null && !string.IsNullOrEmpty(symbol.Name))
                return symbol.Name;

            // Semantic resolution may be ambiguous (e.g. a stub Task colliding with the
            // BCL Task). Fall back to the syntactic identifier so the executor is still
            // recognized.
            switch (receiver)
            {
                case IdentifierNameSyntax id:
                    return id.Identifier.ValueText;
                case MemberAccessExpressionSyntax ma:
                    return ma.Name.Identifier.ValueText;
                default:
                    return null;
            }
        }

        private static bool IsThreadConstruction(ObjectCreationExpressionSyntax creation)
        {
            var typeName = creation.Type.ToString();
            return typeName == "Thread"
                || typeName.EndsWith(".Thread", System.StringComparison.Ordinal);
        }

        private void InspectDelegateForUnityApi(SyntaxNodeAnalysisContext context, ExpressionSyntax argument, string executor)
        {
            // Find the lambda / anonymous-method body inside the delegate argument.
            var lambda = argument.DescendantNodesAndSelf()
                .OfType<AnonymousFunctionExpressionSyntax>()
                .FirstOrDefault();
            if (lambda == null)
                return;

            var body = lambda.Body;
            foreach (var memberAccess in body.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
            {
                var receiverType = context.SemanticModel.GetTypeInfo(memberAccess.Expression).Type;
                if (receiverType == null)
                    continue;
                if (IsUnityMainThreadType(receiverType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.MainThreadApiMisuse,
                        memberAccess.GetLocation(),
                        $"{memberAccess.Name.Identifier.ValueText}",
                        executor));
                }
            }

            foreach (var invocation in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (methodSymbol != null)
                {
                    if (IsUnityMainThreadType(methodSymbol.ContainingType))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.MainThreadApiMisuse,
                            invocation.GetLocation(),
                            methodSymbol.Name,
                            executor));
                    }
                    continue;
                }

                // Unresolved symbol (e.g. GetComponent<T>() not declared in a stub): treat
                // a bare instance call inside a type deriving from UnityEngine as main-thread
                // API misuse, but only for well-known Unity API names to avoid over-firing.
                if (invocation.Expression is IdentifierNameSyntax || invocation.Expression is GenericNameSyntax)
                {
                    string calledName = GetCalledName(invocation);
                    if (calledName != null && KnownUnityApiNames.Contains(calledName))
                    {
                        var containingType = context.SemanticModel.GetEnclosingSymbol(invocation.SpanStart)?.ContainingType;
                        if (containingType != null && IsUnityMainThreadType(containingType))
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                Diagnostics.MainThreadApiMisuse,
                                invocation.GetLocation(),
                                calledName,
                                executor));
                        }
                    }
                }
            }
        }

        private static bool IsUnityMainThreadType(ITypeSymbol type)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                if (t.ContainingNamespace?.ToString() == "UnityEngine")
                    return true;
            }
            return false;
        }

        private static string GetCalledName(InvocationExpressionSyntax invocation)
        {
            var expr = invocation.Expression;
            switch (expr)
            {
                case MemberAccessExpressionSyntax ma:
                    return ma.Name.Identifier.ValueText;
                case GenericNameSyntax gn:
                    return gn.Identifier.ValueText;
                case IdentifierNameSyntax id:
                    return id.Identifier.ValueText;
                default:
                    return null;
            }
        }
    }
}
