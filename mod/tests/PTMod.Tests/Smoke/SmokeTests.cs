using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PTMod.Tests.Smoke
{
    /// <summary>
    /// Self-test of the mini runner and the toolchain: assertions, discovery, filter, [Covers] check,
    /// repository paths, and that modern C# compiles for the .NET Framework 4.8 test build.
    /// Not a logic test, so it has no [Covers] (the namespace PTMod.Tests.* is exempt from the check).
    /// </summary>
    public class SmokeTests
    {
        [Test]
        public void Assert_True_False()
        {
            Assert.True(true);
            Assert.False(false);

            var ex = Assert.Throws<AssertFailedException>(() => Assert.True(false, "boom"));
            Assert.True(ex.Message.Contains("boom"), "custom message must be kept");
            Assert.Throws<AssertFailedException>(() => Assert.False(true));
        }

        [Test]
        public void Assert_Equal_NotEqual()
        {
            Assert.Equal(3, 3);
            Assert.Equal("абв", "абв");
            Assert.Equal<string>(null, null);
            Assert.Equal(new[] { 1, 2, 3 }, new[] { 1, 2, 3 });
            Assert.Equal(new List<string> { "a", "b" }, new List<string> { "a", "b" });
            Assert.Equal(
                new Dictionary<string, int> { { "x", 1 }, { "y", 2 } },
                new Dictionary<string, int> { { "y", 2 }, { "x", 1 } });

            Assert.NotEqual(3, 4);
            Assert.NotEqual("a", "A");
            Assert.NotEqual(new[] { 1, 2 }, new[] { 1, 2, 3 });
            Assert.NotEqual<string>("a", null);

            Assert.Throws<AssertFailedException>(() => Assert.Equal(3, 4));
            Assert.Throws<AssertFailedException>(() => Assert.Equal(new[] { 1, 2 }, new[] { 2, 1 }));
            Assert.Throws<AssertFailedException>(() => Assert.NotEqual("x", "x"));

            var ex = Assert.Throws<AssertFailedException>(() => Assert.Equal("a\"b", "a\tb", "text"));
            Assert.True(ex.Message.StartsWith("text"), "message goes first");
            Assert.True(ex.Message.Contains("expected: \"a\\\"b\", actual: \"a\\tb\""), "values are quoted and escaped: " + ex.Message);
        }

        [Test]
        public void Assert_Near()
        {
            Assert.Near(1.0, 1.0);
            Assert.Near(0.1 + 0.2, 0.3);
            Assert.Near(100.0, 100.4, 0.5);
            Assert.Near(double.NaN, double.NaN);
            Assert.Near(double.PositiveInfinity, double.PositiveInfinity);

            Assert.Throws<AssertFailedException>(() => Assert.Near(1.0, 1.1, 0.05));
            Assert.Throws<AssertFailedException>(() => Assert.Near(double.NaN, 1.0));
            Assert.Throws<AssertFailedException>(() => Assert.Near(double.PositiveInfinity, double.NegativeInfinity, 1e300));
            Assert.Throws<ArgumentException>(() => Assert.Near(1.0, 1.0, -1.0));
        }

        [Test]
        public void Assert_InRange()
        {
            Assert.InRange(5, 1, 10);
            Assert.InRange(1, 1, 10);
            Assert.InRange(10, 1, 10);
            Assert.InRange(0.5, 0, 1);
            Assert.InRange("b", "a", "c");

            Assert.Throws<AssertFailedException>(() => Assert.InRange(11, 1, 10));
            Assert.Throws<AssertFailedException>(() => Assert.InRange(0.999, 1.0, 2.0));
            Assert.Throws<AssertFailedException>(() => Assert.InRange(double.NaN, 0.0, 1.0));
        }

        [Test]
        public void Assert_Null_NotNull()
        {
            Assert.Null(null);
            Assert.NotNull("x");
            Assert.Throws<AssertFailedException>(() => Assert.Null("x"));
            Assert.Throws<AssertFailedException>(() => Assert.NotNull(null));
        }

        [Test]
        public void Assert_Throws()
        {
            var thrown = Assert.Throws<InvalidOperationException>(() => throw new InvalidOperationException("bad state"));
            Assert.Equal("bad state", thrown.Message);

            // A subclass satisfies the expected type.
            Assert.Throws<ArgumentException>(() => throw new ArgumentNullException("p"));
            Assert.Throws<Exception>(() => throw new FormatException());

            // Nothing thrown, or a different type, fails.
            Assert.Throws<AssertFailedException>(() => Assert.Throws<ArgumentException>(() => { }));
            Assert.Throws<AssertFailedException>(() =>
                Assert.Throws<ArgumentException>(() => throw new InvalidOperationException()));

            // A failed assertion inside the action is not swallowed by a broad expectation.
            Assert.Throws<AssertFailedException>(() => Assert.Throws<Exception>(() => Assert.Fail("inner")));
        }

        [Test]
        public void Assert_Fail()
        {
            var ex = Assert.Throws<AssertFailedException>(() => Assert.Fail("manual"));
            Assert.Equal("manual", ex.Message);
        }

        [Test]
        public void Runner_Discovers_TestMethods()
        {
            var problems = new List<string>();
            List<TestCase> found = TestRunner.Discover(typeof(SmokeTests).Assembly, problems);

            Assert.True(found.Any(t => t.Name == "PTMod.Tests.Smoke.SmokeTests.Runner_Discovers_TestMethods"), "this test is found");
            Assert.True(found.All(t => t.Method.GetParameters().Length == 0), "only parameterless methods");
            Assert.True(found.All(t => t.Type.IsPublic || t.Type.IsNestedPublic), "only public classes");
            Assert.False(found.Any(t => t.Type.Name.StartsWith("Fixture")), "private nested fixtures are never discovered");
            Assert.NotNull(problems); // unrunnable [Test] methods of any class are reported by the runner itself, not here
        }

        [Test]
        public void Runner_Filter()
        {
            const string name = "PTMod.Tests.Json.ModJsonTests.Parse_Numbers";
            Assert.True(TestRunner.MatchesFilter(name, null));
            Assert.True(TestRunner.MatchesFilter(name, new string[0]));
            Assert.True(TestRunner.MatchesFilter(name, new[] { "modjson" }), "case-insensitive substring");
            Assert.True(TestRunner.MatchesFilter(name, new[] { "nothing", "numbers" }), "any filter may match");
            Assert.False(TestRunner.MatchesFilter(name, new[] { "Binder" }));
        }

        [Test]
        public void Coverage_FindsUncoveredTypes()
        {
            var types = new[]
            {
                typeof(FixtureLogic), typeof(FixtureOther), typeof(FixtureUntested), typeof(FixtureGeneric<>),
                typeof(FixtureCoveringTests), typeof(FixtureCoverWithoutTests), typeof(FixtureGenericTests),
            };
            Func<Type, bool> needs = t => t.Name.StartsWith("FixtureLogic")
                                          || t.Name.StartsWith("FixtureOther")
                                          || t.Name.StartsWith("FixtureUntested")
                                          || t.Name.StartsWith("FixtureGeneric") && !t.Name.EndsWith("Tests");

            List<Type> uncovered = TestRunner.FindUncovered(types, needs);

            // FixtureLogic and FixtureGeneric<> are covered by classes with tests;
            // FixtureOther only by a class without [Test], so it still counts as uncovered.
            Assert.Equal(new[] { "FixtureOther", "FixtureUntested" }, uncovered.Select(t => t.Name).OrderBy(n => n).ToArray());
        }

        [Test]
        public void Coverage_NeedsCoverage_Rules()
        {
            Assert.False(TestRunner.NeedsCoverage(typeof(SmokeTests)), "test classes are exempt (PTMod.Tests.*)");
            Assert.False(TestRunner.NeedsCoverage(typeof(string)), "System types are exempt (polyfills)");
            Assert.False(TestRunner.NeedsCoverage(typeof(FixtureLogic)), "nested types belong to the declaring type");
            Assert.False(TestRunner.NeedsCoverage(typeof(Assert)));
        }

        [Test]
        public void Environment_RepositoryLayout()
        {
            Assert.NotNull(TestEnv.ModDir, "mod folder not found; run the tests through python mod/build.py --tests");
            Assert.True(Directory.Exists(TestEnv.SrcDir), "src dir: " + TestEnv.SrcDir);
            Assert.True(File.Exists(Path.Combine(TestEnv.ModDir, "VERSION")), "mod/VERSION");
        }

        [Test]
        public void Language_ModernCSharp_Compiles()
        {
            string nothing = null;
            Assert.Equal(0, nothing?.Length ?? 0);

            int n = 7;
            Assert.Equal("n=7", $"n={n}");
            Assert.Equal("n", nameof(n));

            int Twice(int x) => x * 2;
            Assert.Equal(14, Twice(n));

            static int Square(int x) => x * x;
            Assert.Equal(49, Square(n));

            var pair = new Pair(3, 4);
            var (a, b) = (pair.A, pair.B);
            Assert.Equal(7, a + b);
            Assert.Equal(7, pair.Sum);

            object boxed = 5;
            Assert.True(boxed is int five && five == 5);
            Assert.True(int.TryParse("12", out var parsed) && parsed == 12);

            string kind = n switch { 7 => "seven", _ => "other" };
            Assert.Equal("seven", kind);
        }

        private readonly struct Pair
        {
            public Pair(int a, int b)
            {
                A = a;
                B = b;
            }

            public int A { get; }
            public int B { get; }
            public int Sum => A + B;
        }

        // Fixtures of Coverage_FindsUncoveredTypes: nested and private, so the runner never discovers them.
        private sealed class FixtureLogic { }

        private sealed class FixtureOther { }

        private sealed class FixtureUntested { }

        private sealed class FixtureGeneric<T> { }

        [Covers(typeof(FixtureLogic))]
        private sealed class FixtureCoveringTests
        {
            [Test]
            public void Works() { }
        }

        [Covers(typeof(FixtureOther))]
        private sealed class FixtureCoverWithoutTests { }

        [Covers(typeof(FixtureGeneric<int>))]
        private sealed class FixtureGenericTests
        {
            [Test]
            public void Works() { }
        }
    }
}
