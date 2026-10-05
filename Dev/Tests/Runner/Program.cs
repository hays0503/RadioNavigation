using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests.Runner
{
    internal static class Program
    {
        private static int passed;
        private static int failed;
        private static int skipped;

        private static int Main()
        {
            var assembly = typeof(UBOAT.Mods.RadioNavigation.Tests.GeoMathTests).Assembly;

            Console.WriteLine("== RadioNavigation tests (F22 baseline) ==");
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
                    if (method.IsDefined(typeof(TheoryAttribute), false))
                    {
                        var datas = method.GetCustomAttributes<InlineDataAttribute>();
                        if (!datas.Any())
                        {
                            RecordFail(method, "Theory without InlineData");
                            continue;
                        }
                        foreach (var data in datas)
                        {
                            foreach (object[] args in data.GetData(method))
                                Run(method, args);
                        }
                    }
                    else
                    {
                        Run(method, null);
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Passed: {passed}, Failed: {failed}, Skipped: {skipped}");
            return failed == 0 ? 0 : 1;
        }

        private static void Run(MethodInfo method, object[] args)
        {
            string name = args == null || args.Length == 0
                ? method.Name
                : method.Name + "(" + string.Join(", ", args) + ")";

            object instance = Activator.CreateInstance(method.DeclaringType);
            try
            {
                method.Invoke(instance, args);
                passed++;
                Console.WriteLine("PASS  " + name);
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException;
                if (inner != null && inner.GetType().Name == "SkipTestException")
                {
                    skipped++;
                    Console.WriteLine("SKIP  " + name);
                    Console.WriteLine("      " + inner.Message);
                    return;
                }
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
