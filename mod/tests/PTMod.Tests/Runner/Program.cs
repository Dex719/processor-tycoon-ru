using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

// Without a target framework attribute the CLR runs the exe in .NET 4.0 compatibility mode,
// which ignores portable pdb files: failure reports would lose file and line numbers.
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace PTMod.Tests
{
    /// <summary>
    /// Paths of the repository as seen by the tests (architecture checks read sources and the built PTMod.dll).
    /// build.py passes them in PTMOD_MOD_DIR / PTMOD_SRC_DIR / PTMOD_BUILD_DIR; when the exe is started by hand
    /// from mod/build the mod folder is found by walking up. A property is null when the folder cannot be found.
    /// </summary>
    public static class TestEnv
    {
        /// <summary>The mod/ folder.</summary>
        public static string ModDir { get; }

        /// <summary>mod/src/PTMod.</summary>
        public static string SrcDir { get; }

        /// <summary>mod/src/PTMod/Core/Logic (may not exist yet).</summary>
        public static string LogicDir { get; }

        /// <summary>mod/build (PTMod.dll, PTMod.Tests.exe).</summary>
        public static string BuildDir { get; }

        static TestEnv()
        {
            string mod = Environment.GetEnvironmentVariable("PTMOD_MOD_DIR");
            if (string.IsNullOrEmpty(mod)) mod = FindModDir(AppDomain.CurrentDomain.BaseDirectory);
            if (string.IsNullOrEmpty(mod)) return;

            ModDir = Path.GetFullPath(mod);
            SrcDir = Env("PTMOD_SRC_DIR") ?? Path.Combine(ModDir, "src", "PTMod");
            LogicDir = Path.Combine(SrcDir, "Core", "Logic");
            BuildDir = Env("PTMOD_BUILD_DIR") ?? Path.Combine(ModDir, "build");
        }

        private static string Env(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(value) ? null : Path.GetFullPath(value);
        }

        private static string FindModDir(string start)
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "src", "PTMod"))) return dir.FullName;
            }
            return null;
        }
    }

    /// <summary>
    /// Console entry of PTMod.Tests.exe (built by <c>python mod/build.py --tests</c>).
    /// Modes: <c>--tests</c> run tests (default when no mode is given), <c>--coverage-types</c> check [Covers].
    /// Exit code: 0 all good, 1 a test failed or a type is not covered, 2 bad arguments or nothing to run.
    /// </summary>
    public static class Program
    {
        private const string Usage =
            "PTMod.Tests - мини-раннер тестов чистой логики\n" +
            "  --tests              запустить тесты (режим по умолчанию)\n" +
            "  --coverage-types     проверить, что каждый публичный тип Core/Logic указан в [Covers]\n" +
            "  --filter <текст>     только тесты, в полном имени которых есть текст (без учёта регистра; можно повторять)\n" +
            "  --list               показать найденные тесты и выйти\n" +
            "  -v, --verbose        печатать каждый пройденный тест\n" +
            "  -h, --help           эта справка";

        public static int Main(string[] args)
        {
            Encoding previousEncoding = null;
            try
            {
                previousEncoding = PrepareConsole();
                return Execute(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Ошибка раннера: " + ex);
                return 2;
            }
            finally
            {
                Console.Out.Flush();
                RestoreConsole(previousEncoding);
            }
        }

        private static int Execute(string[] args)
        {
            bool runTests = false, coverage = false, verbose = false, list = false;
            var filters = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--tests": runTests = true; break;
                    case "--coverage-types": coverage = true; break;
                    case "-v": case "--verbose": verbose = true; break;
                    case "--list": list = true; break;
                    case "-h": case "--help": case "/?": Console.WriteLine(Usage); return 0;
                    case "--filter":
                        if (i + 1 >= args.Length) return BadArgs("после --filter нужен текст");
                        filters.Add(args[++i]);
                        break;
                    default:
                        return BadArgs("неизвестный аргумент: " + args[i]);
                }
            }
            if (!runTests && !coverage && !list) runTests = true;

            Assembly assembly = typeof(Program).Assembly;
            var problems = new List<string>();
            List<TestCase> all = TestRunner.Discover(assembly, problems);
            List<TestCase> selected = all.Where(t => TestRunner.MatchesFilter(t.Name, filters)).ToList();

            if (list)
            {
                foreach (TestCase test in selected) Console.WriteLine(test.Name);
                return 0;
            }

            int exitCode = 0;

            if (runTests)
            {
                if (selected.Count == 0 && (filters.Count > 0 || problems.Count == 0))
                {
                    Console.Error.WriteLine(filters.Count > 0
                        ? "Ни один тест не подходит под фильтр: " + string.Join(", ", filters)
                        : "Тесты не найдены.");
                    return 2;
                }

                Console.WriteLine("PTMod.Tests: тестов " + selected.Count + (filters.Count > 0 ? " (из " + all.Count + ", фильтр: " + string.Join(", ", filters) + ")" : ""));
                RunSummary summary = TestRunner.Run(selected, Console.Out, verbose);

                // Tests that could not even be started count as failures; with a filter they are not relevant.
                List<string> relevantProblems = filters.Count == 0
                    ? problems
                    : problems.Where(p => TestRunner.MatchesFilter(p, filters)).ToList();
                foreach (string problem in relevantProblems) Console.WriteLine("  FAIL  " + problem);

                int failed = summary.Failed + relevantProblems.Count;
                Console.WriteLine(summary.Passed + " прошло / " + failed + " упало (" + summary.Elapsed.TotalSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " с)");
                if (failed > 0) exitCode = 1;
            }

            if (coverage)
            {
                // Test classes and logic types of the whole assembly, not only the selected tests.
                List<Type> uncovered = TestRunner.FindUncovered(assembly.GetExportedTypes());
                if (uncovered.Count == 0)
                {
                    Console.WriteLine("coverage-types: все публичные типы логики указаны в [Covers]");
                }
                else
                {
                    Console.WriteLine("coverage-types: нет [Covers] у " + uncovered.Count + " тип(ов):");
                    foreach (Type type in uncovered) Console.WriteLine("  " + type.FullName);
                    Console.WriteLine("Добавьте [Covers(typeof(Тип))] на тестовый класс, в котором есть хотя бы один [Test].");
                    exitCode = 1;
                }
            }

            return exitCode;
        }

        private static int BadArgs(string message)
        {
            Console.Error.WriteLine(message);
            Console.Error.WriteLine(Usage);
            return 2;
        }

        /// <summary>
        /// Output must carry Cyrillic correctly both in a terminal and when build.py (or a tool) reads the pipe.
        /// Redirected: UTF-8 without BOM. Terminal: switch the console to UTF-8 for the run and restore it after.
        /// </summary>
        private static Encoding PrepareConsole()
        {
            Encoding previous = null;
            try
            {
                var utf8 = new UTF8Encoding(false);
                if (Console.IsOutputRedirected)
                {
                    Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
                }
                else
                {
                    previous = Console.OutputEncoding;
                    Console.OutputEncoding = utf8;
                }
                if (Console.IsErrorRedirected)
                {
                    Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
                }
            }
            catch (Exception)
            {
                // No usable console (service, detached process): keep the defaults.
            }
            return previous;
        }

        private static void RestoreConsole(Encoding previous)
        {
            if (previous == null) return;
            try
            {
                Console.OutputEncoding = previous;
            }
            catch (Exception)
            {
                // Best effort.
            }
        }
    }
}
