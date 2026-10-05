using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace RadioNavigation.SourceCheck
{
    /// <summary>
    /// Custom test runner (no vstest - matches repo convention; vstest testhost
    /// crashes on restricted tokens). Dispatches [Fact] methods via reflection.
    /// </summary>
    internal static class Program
    {
        private static int passed;
        private static int failed;

        private static int Main(string[] args)
        {
            var assembly = typeof(Program).Assembly;

            Console.WriteLine("== RadioNavigation.SourceCheck (neural-slop guard, custom runner) ==");
            Console.WriteLine("Assembly: " + assembly.Location);
            // Optional substring filter: mutation runs need one gate, not the
            // whole suite, and the clean-reference comparison costs minutes once
            // the mod sources change. `dotnet run --project
            // RadioNavigation.SourceCheck -- AimProbe` runs just that gate.
            string filter = args != null && args.Length > 0 ? args[0] : null;
            if (!string.IsNullOrEmpty(filter))
                Console.WriteLine("Filter: " + filter);
            Console.WriteLine();

            var testTypes = assembly.GetExportedTypes()
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Any(m => m.IsDefined(typeof(FactAttribute), false)))
                .OrderBy(t => t.Name);

            foreach (var type in testTypes)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.IsDefined(typeof(FactAttribute), false))
                    .OrderBy(m => m.Name))
                {
                    if (filter != null
                        && method.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    Run(method);
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Passed: {passed}, Failed: {failed}");
            return failed == 0 ? 0 : 1;
        }

        private static void Run(MethodInfo method)
        {
            object instance = Activator.CreateInstance(method.DeclaringType);
            try
            {
                object result = method.Invoke(instance, null);
                if (result is Task task)
                    task.GetAwaiter().GetResult();
                passed++;
                Console.WriteLine("PASS  " + method.Name);
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException;
                while (inner != null && inner.InnerException != null)
                    inner = inner.InnerException;
                RecordFail(method, inner != null ? inner.Message : ex.Message);
            }
            catch (Exception ex)
            {
                RecordFail(method, ex.Message);
            }
        }

        private static void RecordFail(MethodInfo method, string message)
        {
            failed++;
            Console.WriteLine("FAIL  " + method.Name);
            Console.WriteLine("      " + message.Replace("\n", "\n      "));
        }
    }
}
