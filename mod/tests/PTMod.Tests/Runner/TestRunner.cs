using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace PTMod.Tests
{
    /// <summary>One discovered test: a public parameterless [Test] method of a public class.</summary>
    public sealed class TestCase
    {
        public Type Type { get; }
        public MethodInfo Method { get; }

        /// <summary>Full name used by --filter and in reports: Namespace.Class.Method.</summary>
        public string Name => Type.FullName + "." + Method.Name;

        public TestCase(Type type, MethodInfo method)
        {
            Type = type;
            Method = method;
        }
    }

    public sealed class RunSummary
    {
        public int Passed { get; set; }
        public int Failed { get; set; }
        public TimeSpan Elapsed { get; set; }
        public List<string> FailedNames { get; } = new List<string>();
    }

    /// <summary>
    /// Reflection-based test runner and the [Covers] coverage check. No external dependencies,
    /// no disk or time access in the logic itself (tests of the logic under test inject fakes).
    /// </summary>
    public static class TestRunner
    {
        private const BindingFlags DeclaredMethods =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        /// <summary>
        /// Finds tests in the public types of <paramref name="assembly"/>, ordered by full name.
        /// A method carrying [Test] that cannot run (not public, has parameters, is generic) is not
        /// skipped silently: it is reported in <paramref name="problems"/> and counts as a failure.
        /// </summary>
        public static List<TestCase> Discover(Assembly assembly, List<string> problems = null)
        {
            var result = new List<TestCase>();
            foreach (Type type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                if (type.IsGenericTypeDefinition) continue;
                // Abstract bases do not run; static classes (abstract + sealed) are fine.
                bool isStatic = type.IsAbstract && type.IsSealed;
                if (type.IsAbstract && !isStatic) continue;

                foreach (MethodInfo method in type.GetMethods(DeclaredMethods).OrderBy(m => m.Name, StringComparer.Ordinal))
                {
                    if (!method.IsDefined(typeof(TestAttribute), false)) continue;

                    string name = type.FullName + "." + method.Name;
                    if (!method.IsPublic)
                        problems?.Add(name + ": a [Test] method must be public");
                    else if (method.GetParameters().Length != 0 || method.IsGenericMethodDefinition)
                        problems?.Add(name + ": a [Test] method must have no parameters and no generic arguments");
                    else
                        result.Add(new TestCase(type, method));
                }
            }
            return result;
        }

        /// <summary>--filter semantics: case-insensitive substring of the full name; any of several filters matches; none means all.</summary>
        public static bool MatchesFilter(string fullName, IList<string> filters)
        {
            if (filters == null || filters.Count == 0) return true;
            foreach (string filter in filters)
            {
                if (fullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>Runs tests one by one in the given order; a throwing test never stops the run.</summary>
        public static RunSummary Run(IList<TestCase> tests, TextWriter output, bool verbose)
        {
            var summary = new RunSummary();
            var total = Stopwatch.StartNew();
            foreach (TestCase test in tests)
            {
                var watch = Stopwatch.StartNew();
                string error = Execute(test);
                watch.Stop();

                if (error == null)
                {
                    summary.Passed++;
                    if (verbose) output.WriteLine("  ok    " + test.Name + " (" + watch.ElapsedMilliseconds + " мс)");
                }
                else
                {
                    summary.Failed++;
                    summary.FailedNames.Add(test.Name);
                    output.WriteLine("  FAIL  " + test.Name);
                    output.WriteLine(Indent(error, "          "));
                }
            }
            total.Stop();
            summary.Elapsed = total.Elapsed;
            return summary;
        }

        /// <summary>Returns null when the test passed, otherwise a description of the failure.</summary>
        private static string Execute(TestCase test)
        {
            Exception failure = null;
            try
            {
                object instance = test.Method.IsStatic ? null : Activator.CreateInstance(test.Type);
                test.Method.Invoke(instance, null);
            }
            catch (TargetInvocationException ex)
            {
                failure = ex.InnerException ?? ex;
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            return failure == null ? null : Describe(failure);
        }

        private static string Describe(Exception failure)
        {
            if (failure is AssertFailedException)
            {
                string position = Location(failure);
                return position == null ? failure.Message : failure.Message + Environment.NewLine + "at " + position;
            }

            // Unexpected exception: type, message and the first frames of the stack.
            string text = failure.GetType().FullName + ": " + failure.Message + Environment.NewLine + failure.StackTrace;
            if (failure.InnerException != null) text += Environment.NewLine + "inner: " + failure.InnerException;
            string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            const int MaxLines = 14;
            return lines.Length <= MaxLines
                ? text
                : string.Join(Environment.NewLine, lines.Take(MaxLines)) + Environment.NewLine + "... (" + (lines.Length - MaxLines) + " lines more)";
        }

        /// <summary>First stack frame outside Assert that has a source position (needs the pdb next to the exe).</summary>
        private static string Location(Exception failure)
        {
            try
            {
                StackFrame[] frames = new StackTrace(failure, true).GetFrames();
                if (frames == null) return null;
                foreach (StackFrame frame in frames)
                {
                    MethodBase method = frame.GetMethod();
                    if (method == null || method.DeclaringType == typeof(Assert)) continue;
                    string file = frame.GetFileName();
                    if (string.IsNullOrEmpty(file)) continue;
                    return Path.GetFileName(file) + ":" + frame.GetFileLineNumber() + " (" + method.DeclaringType?.Name + "." + method.Name + ")";
                }
            }
            catch (Exception)
            {
                // Position is a nicety; never let it hide the actual failure.
            }
            return null;
        }

        private static string Indent(string text, string prefix)
        {
            return prefix + text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine + prefix);
        }

        // ---- --coverage-types ----

        /// <summary>
        /// A type must be covered when it is a public top-level type that is not a test helper, polyfill or
        /// compiler-generated: i.e. every public type of Core/Logic/**. Test types live in PTMod.Tests.*,
        /// polyfills in Core/Compat (namespace PTMod.Core.Compat or System.*, Microsoft.*).
        /// </summary>
        public static bool NeedsCoverage(Type type)
        {
            if (!type.IsPublic) return false; // top-level public only; nested types belong to their declaring type
            if (type.IsDefined(typeof(CompilerGeneratedAttribute), false)) return false;
            string ns = type.Namespace ?? string.Empty;
            return !InNamespace(ns, "PTMod.Tests")
                   && !InNamespace(ns, "PTMod.Core.Compat")
                   && !InNamespace(ns, "System")
                   && !InNamespace(ns, "Microsoft");
        }

        private static bool InNamespace(string ns, string root)
        {
            return ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal);
        }

        /// <summary>
        /// Types of <paramref name="types"/> that need coverage but are not named by [Covers] on any class
        /// of the same list that has at least one [Test] method (an empty [Covers] does not count).
        /// <paramref name="needsCoverage"/> defaults to <see cref="NeedsCoverage"/>; tests replace it.
        /// </summary>
        public static List<Type> FindUncovered(IEnumerable<Type> types, Func<Type, bool> needsCoverage = null)
        {
            Func<Type, bool> needs = needsCoverage ?? NeedsCoverage;
            List<Type> all = types.ToList();

            var covered = new HashSet<Type>();
            foreach (Type type in all)
            {
                object[] attributes = type.GetCustomAttributes(typeof(CoversAttribute), false);
                if (attributes.Length == 0 || !HasTests(type)) continue;
                foreach (CoversAttribute covers in attributes)
                {
                    foreach (Type target in covers.Types) covered.Add(Normalize(target));
                }
            }

            return all.Where(needs)
                      .Where(t => !covered.Contains(Normalize(t)))
                      .OrderBy(t => t.FullName, StringComparer.Ordinal)
                      .ToList();
        }

        private static bool HasTests(Type type)
        {
            return type.GetMethods(DeclaredMethods).Any(m => m.IsDefined(typeof(TestAttribute), false));
        }

        private static Type Normalize(Type type)
        {
            return type.IsGenericType && !type.IsGenericTypeDefinition ? type.GetGenericTypeDefinition() : type;
        }
    }
}
