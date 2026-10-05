using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace RadioNavigation.Analyzers.Tests
{
    internal static class Program
    {
        private static int passed;
        private static int failed;

        private static int Main()
        {
            var assembly = typeof(Program).Assembly;

            Console.WriteLine("== RadioNavigation.Analyzers tests (custom runner, no vstest) ==");
            Console.WriteLine("Assembly: " + assembly.Location);
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
