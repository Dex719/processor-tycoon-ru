using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// TSK-001: <see cref="IEffectParams"/> over the <c>params</c> object of an effect. A missing key, a JSON null and a key of the wrong kind give the
    /// fallback; <c>GetRange</c> takes a number (one point) or a pair <c>[lo, hi]</c>.
    /// </summary>
    [Covers(typeof(EffectParams), typeof(IEffectParams))]
    public class EffectParamsTests
    {
        private static IEffectParams Params(string json)
        {
            return new EffectParams(EventTestKit.Obj(json));
        }

        private static readonly FloatRange Fallback = new FloatRange(-1f, -2f);

        // ---- GetRange ----

        [Test]
        public void GetRange_TakesANumberAsAPoint()
        {
            FloatRange range = Params("{ 'delta': 0.25 }").GetRange("delta", Fallback);
            Assert.Equal(0.25f, range.Lo);
            Assert.Equal(0.25f, range.Hi);
        }

        [Test]
        public void GetRange_TakesAPair()
        {
            FloatRange range = Params("{ 'revenueFraction': [0.04, 0.06] }").GetRange("revenueFraction", Fallback);
            Assert.Equal(0.04f, range.Lo);
            Assert.Equal(0.06f, range.Hi);
        }

        [Test]
        public void GetRange_TakesWholeNumbersAndNegativeOnes()
        {
            FloatRange range = Params("{ 'delayDays': [60, 120], 'delta': -3 }").GetRange("delayDays", Fallback);
            Assert.Equal(60f, range.Lo);
            Assert.Equal(120f, range.Hi);

            FloatRange point = Params("{ 'delta': -3 }").GetRange("delta", Fallback);
            Assert.Equal(-3f, point.Lo);
            Assert.Equal(-3f, point.Hi);
        }

        [Test]
        public void GetRange_KeepsThePairAsWritten()
        {
            // Whether lo may exceed hi is for the validator to say; the adapter does not guess.
            FloatRange range = Params("{ 'r': [5, 1] }").GetRange("r", Fallback);
            Assert.Equal(5f, range.Lo);
            Assert.Equal(1f, range.Hi);
        }

        [Test]
        public void GetRange_GivesTheFallback_ForAnythingElse()
        {
            string[] bad =
            {
                "{ }",
                "{ 'r': null }",
                "{ 'r': 'text' }",
                "{ 'r': true }",
                "{ 'r': [] }",
                "{ 'r': [1] }",
                "{ 'r': [1, 2, 3] }",
                "{ 'r': [1, 'x'] }",
                "{ 'r': ['a', 'b'] }",
                "{ 'r': { 'lo': 1, 'hi': 2 } }",
            };
            foreach (string json in bad)
            {
                FloatRange range = Params(json).GetRange("r", Fallback);
                Assert.Equal(Fallback.Lo, range.Lo, json);
                Assert.Equal(Fallback.Hi, range.Hi, json);
            }
        }

        [Test]
        public void GetRange_GivesTheFallback_ForANumberBeyondFloat()
        {
            FloatRange range = Params("{ 'r': 1e300 }").GetRange("r", Fallback);
            Assert.Equal(Fallback.Lo, range.Lo);
            Assert.Equal(Fallback.Hi, range.Hi);
        }

        // ---- the other readers ----

        [Test]
        public void GetFloat_ReadsNumbers_AndFallsBackOtherwise()
        {
            IEffectParams p = Params("{ 'a': 1.5, 'b': 'x', 'c': null, 'd': 2 }");
            Assert.Equal(1.5f, p.GetFloat("a", 9f));
            Assert.Equal(2f, p.GetFloat("d", 9f));
            Assert.Equal(9f, p.GetFloat("b", 9f), "a string is not a number");
            Assert.Equal(9f, p.GetFloat("c", 9f), "JSON null is missing");
            Assert.Equal(9f, p.GetFloat("missing", 9f));
            Assert.Equal(9f, p.GetFloat(null, 9f));
        }

        [Test]
        public void GetInt_ReadsWholeNumbersOnly()
        {
            IEffectParams p = Params("{ 'a': 14, 'b': 1.5, 'c': 3.0, 'd': 3000000000, 'e': '7', 'f': -2 }");
            Assert.Equal(14, p.GetInt("a", 9));
            Assert.Equal(9, p.GetInt("b", 9), "1.5 is not an int");
            Assert.Equal(3, p.GetInt("c", 9), "3.0 is a whole number");
            Assert.Equal(9, p.GetInt("d", 9), "beyond Int32");
            Assert.Equal(9, p.GetInt("e", 9), "a string is not a number");
            Assert.Equal(-2, p.GetInt("f", 9));
            Assert.Equal(9, p.GetInt("missing", 9));
        }

        [Test]
        public void GetBool_And_GetString_ReadTheirKind()
        {
            IEffectParams p = Params("{ 'yes': true, 'no': false, 'name': 'Fines', 'empty': '', 'num': 1 }");
            Assert.True(p.GetBool("yes", false));
            Assert.False(p.GetBool("no", true));
            Assert.True(p.GetBool("num", true), "1 is not a boolean");
            Assert.True(p.GetBool("missing", true));

            Assert.Equal("Fines", p.GetString("name", "x"));
            Assert.Equal(string.Empty, p.GetString("empty", "x"), "an empty string is a string");
            Assert.Equal("x", p.GetString("num", "x"));
            Assert.Equal("x", p.GetString("missing", "x"));
            Assert.Null(p.GetString("missing", null));
        }

        [Test]
        public void Has_IsTrueForAKeyThatIsNotNull()
        {
            IEffectParams p = Params("{ 'a': 0, 'b': null, 'c': '' }");
            Assert.True(p.Has("a"));
            Assert.False(p.Has("b"), "JSON null counts as missing");
            Assert.True(p.Has("c"));
            Assert.False(p.Has("missing"));
            Assert.False(p.Has(null));
        }

        [Test]
        public void Raw_IsTheObjectItself_AndNullMakesAnEmptyOne()
        {
            JsonObject raw = EventTestKit.Obj("{ 'list': [1, 2], 'nested': { 'a': 1 } }");
            var p = new EffectParams(raw);
            Assert.True(ReferenceEquals(raw, p.Raw), "handlers read lists and nested objects through Raw");
            Assert.NotNull(p.Raw.GetArray("list"));

            var empty = new EffectParams(null);
            Assert.NotNull(empty.Raw);
            Assert.Equal(0, empty.Raw.Count);
            Assert.Equal(7, empty.GetInt("anything", 7));
            Assert.False(empty.Has("anything"));
        }
    }
}
