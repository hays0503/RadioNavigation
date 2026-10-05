using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace RadioNavigation.Analyzers.Tests
{
    public class UnityLifecycleAnalyzerTests
    {
        private static readonly ImmutableArray<MetadataReference> References =
            BuildReferences();

        private static ImmutableArray<MetadataReference> BuildReferences()
        {
            var tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            return tpa.Split(';')
                .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                .ToImmutableArray();
        }

        private const string UnityStub = @"
namespace UnityEngine
{
    public class Object { public static Object Instantiate(Object o) => null; }
    public class Component : Object {}
    public class Behaviour : Component {}
    public class MonoBehaviour : Behaviour
    {
        public GameObject gameObject { get; }
        public Transform transform { get; }
    }
    public class Canvas : Behaviour {}
    public class WaitForSeconds { public WaitForSeconds(float s) {} }
    public class GameObject : Object {}
    public class Transform : Component { public Vector3 position { get; set; } }
    public struct Vector3 { public float x, y, z; }
}
namespace UnityEngine.UI
{
    public class Image : UnityEngine.Behaviour { public bool enabled { get; set; } }
}
namespace System.Threading.Tasks
{
    public delegate void Action();
    public class Task { public static Task Run(Action a) => null; }
    public class TaskFactory { public Task StartNew(Action a) => null; }
}
namespace System.Threading
{
    public class Thread { public Thread(global::System.Threading.ThreadStart s) {} public void Start() {} }
    public delegate void ThreadStart();
    public static class ThreadPool { public static void QueueUserWorkItem(WaitCallback cb) {} }
    public delegate void WaitCallback(object state);
}
namespace System.Threading.Tasks
{
    public static class Parallel { public static void For(int a, int b, global::System.Action<int> body) {} }
}
";

        private static List<Diagnostic> Run(string source)
        {
            var tree = CSharpSyntaxTree.ParseText(UnityStub + source);
            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                new[] { tree },
                References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var analyzer = new UnityLifecycleAnalyzer();
            var withAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
            return withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult()
                .OrderBy(d => d.Location.SourceSpan.Start)
                .ToList();
        }

        private static void AssertHas(string id, List<Diagnostic> diags)
        {
            Assert.True(diags.Any(d => d.Id == id),
                $"Expected diagnostic {id} but got: [{string.Join(", ", diags.Select(d => d.Id))}]");
        }

        private static void AssertNone(List<Diagnostic> diags)
        {
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Warning);
        }

        [Fact]
        public void FindObjectOfTypeInUpdate_ReportsRN0006()
        {
            var src = @"
public class Foo {}
public class M : UnityEngine.MonoBehaviour
{
    private void Update() { var x = UnityEngine.Object.FindObjectOfType<Foo>(); }
}
";
            AssertHas("RN0006", Run(src));
        }

        [Fact]
        public void GetComponentInUpdate_ReportsRN0006()
        {
            var src = @"
public class M : UnityEngine.MonoBehaviour
{
    private void Update() { var c = GetComponent<UnityEngine.Canvas>(); }
}
";
            AssertHas("RN0006", Run(src));
        }

        [Fact]
        public void InstantiateInLateUpdate_NoDiagnostic()
        {
            var src = @"
public class M : UnityEngine.MonoBehaviour
{
    private void LateUpdate() { UnityEngine.Object.Instantiate(gameObject); }
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void GetComponentInAwake_NoDiagnostic()
        {
            var src = @"
public class M : UnityEngine.MonoBehaviour
{
    private void Awake() { var c = GetComponent<UnityEngine.Canvas>(); }
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void GetComponentInStart_NoDiagnostic()
        {
            var src = @"
public class M : UnityEngine.MonoBehaviour
{
    private void Start() { var c = GetComponent<UnityEngine.Canvas>(); }
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void NonLookupCallInUpdate_NoDiagnostic()
        {
            var src = @"
public class M : UnityEngine.MonoBehaviour
{
    private int Tick(int v) { return v + 1; }
    private void Update() { var r = Tick(3); }
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void NewInOnAudioFilterRead_ReportsRN0007()
        {
            var src = @"
public class M : UnityEngine.MonoBehaviour
{
    private void OnAudioFilterRead(float[] data, int channels) { var sb = new System.Text.StringBuilder(); }
}
";
            AssertHas("RN0007", Run(src));
        }

        [Fact]
        public void LinqInOnAudioFilterRead_ReportsRN0007()
        {
            var src = @"
using System.Linq;
public class M : UnityEngine.MonoBehaviour
{
    private void OnAudioFilterRead(float[] data, int channels) { var s = data.Where(x => x > 0f).Count(); }
}
";
            AssertHas("RN0007", Run(src));
        }

        [Fact]
        public void StringConcatInOnAudioFilterRead_ReportsRN0007()
        {
            var src = @"
public class M : UnityEngine.MonoBehaviour
{
    private void OnAudioFilterRead(float[] data, int channels) { var s = ""a"" + 1; }
}
";
            AssertHas("RN0007", Run(src));
        }

        [Fact]
        public void BoxingInOnAudioFilterRead_ReportsRN0007()
        {
            var src = @"
public class M : UnityEngine.MonoBehaviour
{
    private void OnAudioFilterRead(float[] data, int channels) { object o = 42; }
}
";
            AssertHas("RN0007", Run(src));
        }

        [Fact]
        public void PureMathInOnAudioFilterRead_NoDiagnostic()
        {
            var src = @"
public class M : UnityEngine.MonoBehaviour
{
    private void OnAudioFilterRead(float[] data, int channels)
    {
        for (int i = 0; i < data.Length; i++) data[i] = data[i] * 0.5f;
    }
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void NewInUpdate_NoRN0007()
        {
            var src = @"
public class M : UnityEngine.MonoBehaviour
{
    private void Update() { var sb = new System.Text.StringBuilder(); }
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void GetComponentInTaskRun_ReportsRN0008()
        {
            var src = @"
using System.Threading.Tasks;
public class M : UnityEngine.MonoBehaviour
{
    private void Go() { Task.Run(() => gameObject.GetComponent<UnityEngine.Canvas>()); }
}
";
            AssertHas("RN0008", Run(src));
        }

        [Fact]
        public void FindObjectOfTypeInThreadPool_ReportsRN0008()
        {
            var src = @"
using System.Threading;
public class M : UnityEngine.MonoBehaviour
{
    private void Go() { ThreadPool.QueueUserWorkItem(_ => { UnityEngine.Object.FindObjectOfType<UnityEngine.Canvas>(); }); }
}
";
            AssertHas("RN0008", Run(src));
        }

        [Fact]
        public void TransformPositionInNewThread_ReportsRN0008()
        {
            var src = @"
using System.Threading;
public class M : UnityEngine.MonoBehaviour
{
    private void Go() { new Thread(() => { transform.position = new UnityEngine.Vector3(); }).Start(); }
}
";
            AssertHas("RN0008", Run(src));
        }

        [Fact]
        public void ParallelForUnityApi_ReportsRN0008()
        {
            var src = @"
using System.Threading.Tasks;
public class M : UnityEngine.MonoBehaviour
{
    private void Go() { Parallel.For(0, 10, i => { var c = GetComponent<UnityEngine.Canvas>(); }); }
}
";
            AssertHas("RN0008", Run(src));
        }

        [Fact]
        public void UnityApiInOrdinaryMethod_NoRN0008()
        {
            var src = @"
public class M : UnityEngine.MonoBehaviour
{
    private void Go() { var c = GetComponent<UnityEngine.Canvas>(); }
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void NonUnityApiInTaskRun_NoRN0008()
        {
            var src = @"
using System.Threading.Tasks;
public class M
{
    private int Tick(int v) { return v + 1; }
    private void Go() { Task.Run(() => Tick(3)); }
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void TaskRunWithoutLambda_NoRN0008()
        {
            var src = @"
using System.Threading.Tasks;
public class M
{
    private void Go() { Task.Run(MethodGroup); }
    private static void MethodGroup() {}
}
";
            AssertNone(Run(src));
        }

        // ---- Track 3 hardening: RN0007 gaps (was: +=, arrays, casts, lambdas) ----

        [Fact]
        public void AddAssignmentStringInOnAudioFilterRead_ReportsRN0007()
        {
            var src = @"
public class M
{
    private string s = """";
    private void OnAudioFilterRead(float[] data, int channels) { s += ""x""; }
}
";
            AssertHas("RN0007", Run(src));
        }

        [Fact]
        public void AddAssignmentIntInOnAudioFilterRead_NoRN0007()
        {
            var src = @"
public class M
{
    private int i;
    private void OnAudioFilterRead(float[] data, int channels) { i += 1; }
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void ArrayCreationInOnAudioFilterRead_ReportsRN0007()
        {
            var src = @"
public class M
{
    private void OnAudioFilterRead(float[] data, int channels)
    {
        float[] scratch = new float[16];
    }
}
";
            AssertHas("RN0007", Run(src));
        }

        [Fact]
        public void ImplicitArrayCreationInOnAudioFilterRead_ReportsRN0007()
        {
            var src = @"
public class M
{
    private void OnAudioFilterRead(float[] data, int channels)
    {
        var a = new[] { 1, 2, 3 };
    }
}
";
            AssertHas("RN0007", Run(src));
        }

        [Fact]
        public void BoxingCastInOnAudioFilterRead_ReportsRN0007()
        {
            var src = @"
public class M
{
    private void OnAudioFilterRead(float[] data, int channels)
    {
        object boxed = (object)1.5f;
    }
}
";
            AssertHas("RN0007", Run(src));
        }

        [Fact]
        public void LambdaInOnAudioFilterRead_ReportsRN0007()
        {
            var src = @"
public class M
{
    private void OnAudioFilterRead(float[] data, int channels)
    {
        System.Action a = () => { };
    }
}
";
            AssertHas("RN0007", Run(src));
        }

        // ---- Track 3 hardening: RN0006 OnGUI / coroutine ----

        [Fact]
        public void LookupInOnGUI_ReportsRN0006()
        {
            var src = @"
public class Foo {}
public class M : UnityEngine.MonoBehaviour
{
    private void OnGUI() { var x = UnityEngine.Object.FindObjectOfType<Foo>(); }
}
";
            AssertHas("RN0006", Run(src));
        }

        [Fact]
        public void LookupInCoroutineLoop_ReportsRN0006()
        {
            var src = @"
using System.Collections;
public class Foo {}
public class M : UnityEngine.MonoBehaviour
{
    private IEnumerator Loop()
    {
        while (true)
        {
            var x = UnityEngine.Object.FindObjectOfType<Foo>();
            yield return null;
        }
    }
}
";
            AssertHas("RN0006", Run(src));
        }

        [Fact]
        public void LookupInCoroutineWithWait_NoRN0006()
        {
            var src = @"
using System.Collections;
public class Foo {}
public class M : UnityEngine.MonoBehaviour
{
    private IEnumerator Once()
    {
        var x = UnityEngine.Object.FindObjectOfType<Foo>();
        yield return new UnityEngine.WaitForSeconds(5f);
    }
}
";
            AssertNone(Run(src));
        }

        // ---- Track 3 hardening: RN0008 namespace prefix ----

        [Fact]
        public void UnityUIInTaskRun_ReportsRN0008()
        {
            var src = @"
using System.Threading.Tasks;
public class M : UnityEngine.MonoBehaviour
{
    private void Go()
    {
        Task.Run(() => { var img = new UnityEngine.UI.Image(); img.enabled = false; });
    }
}
";
            AssertHas("RN0008", Run(src));
        }
    }
}
