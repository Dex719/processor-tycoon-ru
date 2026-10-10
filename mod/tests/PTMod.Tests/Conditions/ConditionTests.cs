using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Json;

namespace PTMod.Tests.Conditions
{
    /// <summary>
    /// ModConditions logic (FR-11, TSK-019): parsing of all/any/not and predicates with error paths, every predicate of the
    /// design.md table on <see cref="FakeGameState"/>, the company parameter (player, ai:N, anyAI), unavailable sources,
    /// registration rules (duplicates, naming, the plug-in name policy of AC-11.2) and the guarantee that evaluation never throws.
    /// JSON in the tests is written with single quotes for readability and converted to double quotes.
    /// </summary>
    [Covers(typeof(AllCondition), typeof(AnyCondition), typeof(NotCondition), typeof(DelegateCondition), typeof(ConditionEvaluator),
        typeof(ConditionParser), typeof(ConditionArgs), typeof(CompanySelector), typeof(CompanyScope), typeof(CompanySelectorKind),
        typeof(PredicateRegistry), typeof(RejectedPredicate), typeof(Predicates), typeof(IGameState), typeof(ICondition),
        typeof(ConditionContext), typeof(MarketWindow))]
    public class ConditionTests
    {
        private static readonly DateTime Feb1980 = new DateTime(1980, 2, 1);

        /// <summary>The 32 predicates of the table in design.md "ModConditions: predicates and data sources" (without all/any/not).</summary>
        private static readonly string[] TablePredicates =
        {
            "dateAtLeast", "dateBefore", "daysSinceStartAtLeast", "moneyAtLeast", "moneyBelow", "inDefault", "daysInDefaultAtLeast",
            "cpuCountAtLeast", "cpuOnMarket", "productionLinesAllocatedAtLeast", "factoryLinesAtLeast", "oemContractsActiveAtLeast",
            "oemOfferSent", "businessContractsAtLeast", "marketShareAtLeast", "segmentShareAtLeast", "techResearched", "researchActive",
            "technologiesAtLeast", "rivalBankrupt", "rivalExists", "companyType", "difficultyAtLeast", "cheatsEnabled",
            "profitLastMonthAtLeast", "revenueLastMonthAtLeast", "teamsAtLeast", "reputationAtLeast", "reputationBelow", "pgaEra",
            "flag", "featureActive",
        };

        private static string J(string json)
        {
            return json.Replace('\'', '"');
        }

        private static ICondition Parse(string json, List<string> errors, PredicateRegistry registry = null)
        {
            var parser = new ConditionParser(registry ?? Predicates.CreateRegistry());
            return parser.Parse(J(json), errors);
        }

        private static ICondition ParseOk(string json, PredicateRegistry registry = null)
        {
            var errors = new List<string>();
            ICondition condition = Parse(json, errors, registry);
            Assert.NotNull(condition, "parse failed: " + string.Join("; ", errors));
            Assert.Equal(0, errors.Count);
            return condition;
        }

        private static bool Eval(string json, FakeGameState state, DateTime? today = null, string subject = null)
        {
            return ParseOk(json).Evaluate(state.Context(today ?? Feb1980, subject));
        }

        private static List<string> ParseErrors(string json, PredicateRegistry registry = null)
        {
            var errors = new List<string>();
            ICondition condition = Parse(json, errors, registry);
            Assert.Null(condition, "an erroneous condition must not be returned");
            Assert.True(errors.Count > 0, "an error must be reported");
            return errors;
        }

        // ---- AC-11.1 ----

        [Test]
        public void Evaluate_AC_11_1_DateAndOemContract()
        {
            const string json = "{'all':[{'type':'dateAtLeast','year':1980},{'type':'oemContractsActiveAtLeast','count':1}]}";
            var state = new FakeGameState();
            state.Player.ActiveOemContracts = 1;

            Assert.True(Eval(json, state, new DateTime(1980, 2, 1)), "1980-02-01 with an active OEM contract");

            state.Player.ActiveOemContracts = 0;
            Assert.False(Eval(json, state, new DateTime(1980, 2, 1)), "no contract");

            state.Player.ActiveOemContracts = 1;
            Assert.False(Eval(json, state, new DateTime(1979, 12, 31)), "too early");
        }

        // ---- tree structure ----

        [Test]
        public void Tree_EmptyAllIsTrueEmptyAnyIsFalse()
        {
            var state = new FakeGameState();

            Assert.True(Eval("{'all':[]}", state));
            Assert.False(Eval("{'any':[]}", state));
            Assert.False(Eval("{'not':{'all':[]}}", state));
            Assert.True(Eval("{'not':{'any':[]}}", state));
            Assert.False(Eval("{'all':[{'any':[]}]}", state));
            Assert.True(Eval("{'any':[{'all':[]}]}", state));
        }

        [Test]
        public void Tree_AllAnyNotCombine()
        {
            var state = new FakeGameState();
            state.Cheats = true;
            state.Pga = false;

            Assert.True(Eval("{'all':[{'type':'cheatsEnabled'},{'not':{'type':'pgaEra'}}]}", state));
            Assert.False(Eval("{'all':[{'type':'cheatsEnabled'},{'type':'pgaEra'}]}", state));
            Assert.True(Eval("{'any':[{'type':'pgaEra'},{'type':'cheatsEnabled'}]}", state));
            Assert.False(Eval("{'any':[{'type':'pgaEra'},{'type':'inDefault'}]}", state));
            Assert.True(Eval("{'not':{'any':[{'type':'pgaEra'},{'type':'inDefault'}]}}", state));
            Assert.True(Eval("{'all':[{'any':[{'type':'pgaEra'},{'type':'cheatsEnabled'}]},{'not':{'type':'inDefault'}}]}", state));
        }

        [Test]
        public void Tree_ShortCircuits()
        {
            var registry = Predicates.CreateRegistry();
            int calls = 0;
            registry.Register("t.count", "mod-framework-test", (node, errors, path) =>
                new DelegateCondition("t.count", ctx => { calls++; return true; }));
            var state = new FakeGameState();

            ParseOk("{'any':[{'type':'cheatsEnabled'},{'type':'t.count'}]}", registry).Evaluate(state.Context(Feb1980));
            Assert.Equal(1, calls, "any evaluates the second child when the first is false");

            state.Cheats = true;
            calls = 0;
            ParseOk("{'any':[{'type':'cheatsEnabled'},{'type':'t.count'}]}", registry).Evaluate(state.Context(Feb1980));
            Assert.Equal(0, calls, "any stops at the first true");

            state.Cheats = false;
            ParseOk("{'all':[{'type':'cheatsEnabled'},{'type':'t.count'}]}", registry).Evaluate(state.Context(Feb1980));
            Assert.Equal(0, calls, "all stops at the first false");
        }

        [Test]
        public void Describe_ShowsTreeAndGivenParameters()
        {
            Assert.Equal("all(dateAtLeast(year=1980), oemContractsActiveAtLeast(count=1))",
                ParseOk("{'all':[{'type':'dateAtLeast','year':1980},{'type':'oemContractsActiveAtLeast','count':1}]}").Describe());
            Assert.Equal("not(cheatsEnabled)", ParseOk("{'not':{'type':'cheatsEnabled'}}").Describe());
            Assert.Equal("any()", ParseOk("{'any':[]}").Describe());
            Assert.Equal("moneyAtLeast(amount=1000, company=ai:2)", ParseOk("{'type':'moneyAtLeast','amount':1000,'company':'ai:2'}").Describe());
            Assert.Equal("dateAtLeast(year=1980, month=2)", ParseOk("{'type':'dateAtLeast','year':1980,'month':2}").Describe());
            Assert.Equal("segmentShareAtLeast(segment=midrange, share=0.3, window=month)",
                ParseOk("{'type':'segmentShareAtLeast','segment':'MidRange','share':0.3,'window':'Month'}").Describe());
        }

        // ---- parse errors ----

        [Test]
        public void Errors_PathPointsAtTheParameter()
        {
            List<string> errors = ParseErrors("{'all':[{'type':'dateAtLeast','year':1980},{'type':'dateAtLeast','year':'abc'}]}");

            Assert.Equal(1, errors.Count);
            Assert.True(errors[0].StartsWith("$.all[1].year:"), errors[0]);
            Assert.True(errors[0].Contains("expected an integer"), errors[0]);
        }

        [Test]
        public void Errors_NestedPathsThroughNotAndAny()
        {
            List<string> errors = ParseErrors("{'any':[{'type':'cheatsEnabled'},{'not':{'type':'dateBefore','year':1980,'month':13}}]}");

            Assert.Equal(1, errors.Count);
            Assert.True(errors[0].StartsWith("$.any[1].not.month:"), errors[0]);
            Assert.True(errors[0].Contains("between 1 and 12"), errors[0]);
        }

        [Test]
        public void Errors_MissingRequiredParameter()
        {
            List<string> errors = ParseErrors("{'type':'moneyAtLeast'}");

            Assert.Equal(1, errors.Count);
            Assert.True(errors[0].StartsWith("$.amount:"), errors[0]);
            Assert.True(errors[0].Contains("required"), errors[0]);
        }

        [Test]
        public void Errors_UnknownParameterIsATypo()
        {
            List<string> errors = ParseErrors("{'all':[{'type':'dateAtLeast','year':1980,'yaer':1}]}");

            Assert.Equal(1, errors.Count);
            Assert.True(errors[0].StartsWith("$.all[0].yaer:"), errors[0]);
            Assert.True(errors[0].Contains("unknown parameter"), errors[0]);
            Assert.True(errors[0].Contains("year"), "the message lists the known parameters: " + errors[0]);
        }

        [Test]
        public void Errors_UnknownPredicate()
        {
            List<string> errors = ParseErrors("{'all':[{'type':'cheatsEnabled'},{'type':'noSuchThing'}]}");

            Assert.Equal(1, errors.Count);
            Assert.True(errors[0].StartsWith("$.all[1].type:"), errors[0]);
            Assert.True(errors[0].Contains("unknown predicate \"noSuchThing\""), errors[0]);
        }

        [Test]
        public void Errors_AllProblemsAreCollectedInOnePass()
        {
            List<string> errors = ParseErrors("{'all':[{'type':'x1'},{'type':'dateAtLeast'},{'type':'cheatsEnabled','extra':true},7]}");

            Assert.Equal(4, errors.Count);
            Assert.True(errors[0].StartsWith("$.all[0].type:"), errors[0]);
            Assert.True(errors[1].StartsWith("$.all[1].year:"), errors[1]);
            Assert.True(errors[2].StartsWith("$.all[2].extra:"), errors[2]);
            Assert.True(errors[3].StartsWith("$.all[3]:"), errors[3]);
        }

        [Test]
        public void Errors_BadNodeShapes()
        {
            Assert.True(ParseErrors("{}")[0].StartsWith("$: a condition needs one of"));
            Assert.True(ParseErrors("{'type':'cheatsEnabled','all':[]}")[0].Contains("only one of"));
            Assert.True(ParseErrors("{'all':[],'any':[]}")[0].Contains("only one of"));
            Assert.True(ParseErrors("[]")[0].StartsWith("$: expected an object, got an array"));
            Assert.True(ParseErrors("5")[0].StartsWith("$: expected an object, got a number"));
            Assert.True(ParseErrors("'text'")[0].StartsWith("$: expected an object, got a string"));
            Assert.True(ParseErrors("null")[0].StartsWith("$: expected an object, got null"));
            Assert.True(ParseErrors("{'all':{}}")[0].StartsWith("$.all: expected an array"));
            Assert.True(ParseErrors("{'any':'x'}")[0].StartsWith("$.any: expected an array"));
            Assert.True(ParseErrors("{'not':[]}")[0].StartsWith("$.not: expected an object, got an array"));
            Assert.True(ParseErrors("{'type':5}")[0].StartsWith("$.type: expected the name of a predicate"));
            Assert.True(ParseErrors("{'type':''}")[0].StartsWith("$.type: expected the name of a predicate"));
            Assert.True(ParseErrors("{'all':[],'comment':'x'}")[0].StartsWith("$.comment: unknown key"));
        }

        [Test]
        public void Errors_InvalidJsonText()
        {
            var errors = new List<string>();

            ICondition condition = new ConditionParser(Predicates.CreateRegistry()).Parse("{\"all\": [", errors);

            Assert.Null(condition);
            Assert.Equal(1, errors.Count);
            Assert.True(errors[0].StartsWith("$: invalid JSON:"), errors[0]);
            Assert.True(errors[0].Contains("line"), errors[0]);
        }

        [Test]
        public void Errors_NullErrorListIsAllowed()
        {
            var parser = new ConditionParser(Predicates.CreateRegistry());
            JsonNode good;
            JsonNode bad;
            JsonError error;
            Assert.True(ModJson.TryParse("{\"type\":\"cheatsEnabled\"}", out good, out error));
            Assert.True(ModJson.TryParse("{\"type\":\"nope\"}", out bad, out error));

            Assert.NotNull(parser.Parse(good, null));
            Assert.Null(parser.Parse(bad, null));
            Assert.Null(parser.Parse((JsonNode)null, null));
        }

        [Test]
        public void Errors_CustomRootPathPrefixesEveryError()
        {
            var parser = new ConditionParser(Predicates.CreateRegistry());
            JsonNode node;
            JsonError error;
            ModJson.TryParse("{\"all\":[{\"type\":\"dateAtLeast\"}]}", out node, out error);
            var errors = new List<string>();

            parser.Parse(node, errors, "$.events[3].condition");

            Assert.Equal(1, errors.Count);
            Assert.True(errors[0].StartsWith("$.events[3].condition.all[0].year:"), errors[0]);
        }

        [Test]
        public void Errors_ParameterTypesAndRanges()
        {
            Assert.True(ParseErrors("{'type':'dateAtLeast','year':1980.5}")[0].StartsWith("$.year: expected an integer, got 1980.5"));
            Assert.True(ParseErrors("{'type':'dateAtLeast','year':null}")[0].Contains("got null"));
            Assert.True(ParseErrors("{'type':'dateAtLeast','year':true}")[0].Contains("got a boolean"));
            Assert.True(ParseErrors("{'type':'dateAtLeast','year':0}")[0].Contains("between 1 and 9999"));
            Assert.True(ParseErrors("{'type':'cpuCountAtLeast','count':3,'includeRetired':'yes'}")[0].StartsWith("$.includeRetired: expected true or false"));
            Assert.True(ParseErrors("{'type':'techResearched','name':''}")[0].Contains("must not be empty"));
            Assert.True(ParseErrors("{'type':'techResearched','name':5}")[0].Contains("expected a string"));
            Assert.True(ParseErrors("{'type':'marketShareAtLeast','share':1.5}")[0].StartsWith("$.share: must be between 0 and 1"));
            Assert.True(ParseErrors("{'type':'marketShareAtLeast','share':0.5,'window':'week'}")[0].StartsWith("$.window: expected one of day, month"));
            Assert.True(ParseErrors("{'type':'segmentShareAtLeast','share':0.5}")[0].StartsWith("$.segment: required"));
            Assert.True(ParseErrors("{'type':'segmentShareAtLeast','share':0.5,'segment':'premium'}")[0].Contains("highend, midrange, lowend, industries"));
            Assert.True(ParseErrors("{'type':'difficultyAtLeast','level':6}")[0].StartsWith("$.level: must be between 0 and 5"));
            Assert.True(ParseErrors("{'type':'reputationAtLeast','value':101}")[0].StartsWith("$.value: must be between 0 and 100"));
            Assert.True(ParseErrors("{'type':'companyType','kind':'fab'}")[0].StartsWith("$.kind: expected one of idm, fabless, foundry"));
            Assert.True(ParseErrors("{'type':'daysSinceStartAtLeast','days':-1}")[0].StartsWith("$.days: must be between 0"));
        }

        [Test]
        public void Errors_ImpossibleDate()
        {
            List<string> errors = ParseErrors("{'type':'dateAtLeast','year':1981,'month':2,'day':30}");

            Assert.Equal(1, errors.Count);
            Assert.True(errors[0].StartsWith("$.day:"), errors[0]);

            Assert.NotNull(ParseOk("{'type':'dateAtLeast','year':1980,'month':2,'day':29}"), "1980 is a leap year");
            Assert.True(ParseErrors("{'type':'dateAtLeast','year':1981,'month':2,'day':29}")[0].StartsWith("$.day:"));
        }

        [Test]
        public void Errors_CompanyParameter()
        {
            Assert.True(ParseErrors("{'type':'moneyAtLeast','amount':1,'company':'ai:x'}")[0].StartsWith("$.company: expected player, ai:<number> or anyAI"));
            Assert.True(ParseErrors("{'type':'moneyAtLeast','amount':1,'company':5}")[0].StartsWith("$.company: expected a company key"));
            Assert.True(ParseErrors("{'type':'moneyAtLeast','amount':1,'company':'anyai'}")[0].Contains("got \"anyai\""));
            Assert.True(ParseErrors("{'type':'rivalExists'}")[0].StartsWith("$.company: required"));
            Assert.True(ParseErrors("{'type':'rivalExists','company':'anyAI'}")[0].StartsWith("$.company: expected ai:<number>"));
            Assert.True(ParseErrors("{'type':'rivalExists','company':'player'}")[0].StartsWith("$.company: expected ai:<number>"));
            Assert.True(ParseErrors("{'type':'rivalBankrupt','company':'player'}")[0].StartsWith("$.company: expected ai:<number> or anyAI"));
        }

        [Test]
        public void Errors_CyclicOrTooDeepTreeIsRejectedWithoutOverflow()
        {
            var parser = new ConditionParser(Predicates.CreateRegistry());

            JsonNode deep = new JsonObject().Set("type", "cheatsEnabled");
            for (int i = 0; i < 70; i++) deep = new JsonObject().Set("not", deep);
            var errors = new List<string>();
            Assert.Null(parser.Parse(deep, errors));
            Assert.True(errors.Count == 1 && errors[0].Contains("nested deeper than 64"), string.Join("; ", errors));

            var loop = new JsonObject();
            loop.Set("not", loop);
            errors = new List<string>();
            Assert.Null(parser.Parse(loop, errors));
            Assert.True(errors.Count > 0);

            JsonNode fine = new JsonObject().Set("type", "cheatsEnabled");
            for (int i = 0; i < 30; i++) fine = new JsonObject().Set("not", fine);
            Assert.NotNull(parser.Parse(fine, new List<string>()), "30 levels are fine");
        }

        // ---- every predicate of the table ----

        private sealed class Rows
        {
            public readonly List<string> Json = new List<string>();
            public readonly HashSet<string> Covered = new HashSet<string>(StringComparer.Ordinal);

            public void Check(string json, Action<FakeGameState> makeTrue, Action<FakeGameState> makeFalse,
                DateTime? trueDate = null, DateTime? falseDate = null)
            {
                string text = J(json);
                Json.Add(text);
                JsonNode node;
                JsonError error;
                Assert.True(ModJson.TryParse(text, out node, out error), text);
                Covered.Add(((JsonObject)node).GetString("type"));

                var yes = new FakeGameState();
                if (makeTrue != null) makeTrue(yes);
                Assert.True(ParseOk(text).Evaluate(yes.Context(trueDate ?? Feb1980)), "must be true: " + text);

                var no = new FakeGameState();
                if (makeFalse != null) makeFalse(no);
                Assert.False(ParseOk(text).Evaluate(no.Context(falseDate ?? Feb1980)), "must be false: " + text);
            }
        }

        [Test]
        public void EveryPredicateOfTheTableParsesAndEvaluates()
        {
            var r = new Rows();

            r.Check("{'type':'dateAtLeast','year':1980,'month':2}", null, null, new DateTime(1980, 2, 1), new DateTime(1980, 1, 31));
            r.Check("{'type':'dateBefore','year':1980,'month':2}", null, null, new DateTime(1980, 1, 31), new DateTime(1980, 2, 1));
            r.Check("{'type':'daysSinceStartAtLeast','days':365}", null, null, new DateTime(1976, 1, 1), new DateTime(1975, 12, 31));

            r.Check("{'type':'moneyAtLeast','amount':1000}", s => s.Player.Money = 1000, s => s.Player.Money = 999.99);
            r.Check("{'type':'moneyBelow','amount':0}", s => s.Player.Money = -1, s => s.Player.Money = 0);
            r.Check("{'type':'inDefault'}", s => s.Player.InDefault = true, s => s.Player.InDefault = false);
            r.Check("{'type':'daysInDefaultAtLeast','days':30}", s => s.Player.DaysInDefault = 30, s => s.Player.DaysInDefault = 29);

            r.Check("{'type':'cpuCountAtLeast','count':3}", s => s.Player.CpuCount = 3, s => { s.Player.CpuCount = 2; s.Player.RetiredCpuCount = 5; });
            r.Check("{'type':'cpuCountAtLeast','count':3,'includeRetired':true}", s => { s.Player.CpuCount = 2; s.Player.RetiredCpuCount = 1; }, s => s.Player.CpuCount = 2);
            r.Check("{'type':'cpuOnMarket'}", s => s.Player.CpuOnMarket = true, null);
            r.Check("{'type':'productionLinesAllocatedAtLeast','lines':4}", s => s.Player.ProductionLines = 4, s => s.Player.ProductionLines = 3);
            r.Check("{'type':'factoryLinesAtLeast','lines':2}", s => s.Player.FactoryLines = 2, s => s.Player.FactoryLines = 1);

            r.Check("{'type':'oemContractsActiveAtLeast','count':1}", s => s.Player.ActiveOemContracts = 1, null);
            r.Check("{'type':'oemOfferSent'}", s => s.Player.OemOfferSent = true, null);
            r.Check("{'type':'businessContractsAtLeast','count':2}", s => s.Player.BusinessContracts = 2, s => s.Player.BusinessContracts = 1);

            r.Check("{'type':'marketShareAtLeast','share':0.1}", s => s.Player.MarketShareDay = 0.1, s => { s.Player.MarketShareDay = 0.09; s.Player.MarketShareMonth = 0.9; });
            r.Check("{'type':'marketShareAtLeast','share':0.2,'window':'month'}", s => s.Player.MarketShareMonth = 0.2, s => { s.Player.MarketShareMonth = 0.19; s.Player.MarketShareDay = 0.9; });
            r.Check("{'type':'segmentShareAtLeast','segment':'midrange','share':0.3}",
                s => s.Player.SegmentShareDay["midrange"] = 0.3, s => { s.Player.SegmentShareDay["midrange"] = 0.29; s.Player.SegmentShareDay["highend"] = 0.9; });
            r.Check("{'type':'segmentShareAtLeast','segment':'industries','share':0.5,'window':'month'}",
                s => s.Player.SegmentShareMonth["industries"] = 0.5, s => s.Player.SegmentShareDay["industries"] = 0.5);

            r.Check("{'type':'techResearched','name':'45nm'}", s => s.Player.Technologies.Add("45nm"), s => s.Player.Technologies.Add("65nm"));
            r.Check("{'type':'researchActive'}", s => s.Player.ResearchActive = true, null);
            r.Check("{'type':'technologiesAtLeast','count':2}", s => { s.Player.Technologies.Add("a"); s.Player.Technologies.Add("b"); }, s => s.Player.Technologies.Add("a"));

            r.Check("{'type':'rivalBankrupt','company':'ai:3'}", s => s.Ai(3).Bankrupt = true, s => s.Ai(3));
            r.Check("{'type':'rivalExists','company':'ai:3'}", s => s.Ai(3), s => s.Ai(3).Bankrupt = true);
            r.Check("{'type':'companyType','kind':'fabless'}", s => s.Player.Type = "fabless", s => s.Player.Type = "foundry");

            r.Check("{'type':'difficultyAtLeast','level':3}", s => s.Difficulty = 3, s => s.Difficulty = 2);
            r.Check("{'type':'cheatsEnabled'}", s => s.Cheats = true, null);
            r.Check("{'type':'pgaEra'}", s => s.Pga = true, null);

            r.Check("{'type':'profitLastMonthAtLeast','amount':500}", s => s.Player.ProfitLastMonth = 500, s => s.Player.ProfitLastMonth = 499);
            r.Check("{'type':'profitLastMonthAtLeast','amount':-100}", s => s.Player.ProfitLastMonth = -100, null);
            r.Check("{'type':'revenueLastMonthAtLeast','amount':1000}", s => s.Player.RevenueLastMonth = 1000, s => s.Player.RevenueLastMonth = 999);
            r.Check("{'type':'teamsAtLeast','count':2}", s => s.Player.Teams = 2, s => s.Player.Teams = 1);
            r.Check("{'type':'reputationAtLeast','value':60}", s => s.Player.Reputation = 60, s => s.Player.Reputation = 59.9);
            r.Check("{'type':'reputationBelow','value':40}", s => s.Player.Reputation = 39.9, s => s.Player.Reputation = 40);

            r.Check("{'type':'flag','feature':'first-year-onboarding','key':'step'}", s => s.SetFlag("first-year-onboarding", "step", 1), s => s.SetFlag("first-year-onboarding", "step", 0));
            r.Check("{'type':'flag','feature':'onb','key':'step','value':3}", s => s.SetFlag("onb", "step", 3), s => s.SetFlag("onb", "step", 1));
            r.Check("{'type':'featureActive','id':'event-system'}", s => s.SetFeatureActive("event-system", true), s => s.SetFeatureActive("market-shocks", true));

            Assert.Equal(TablePredicates.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                r.Covered.OrderBy(n => n, StringComparer.Ordinal).ToArray(), "every predicate of the table has a case");
            Assert.Equal(TablePredicates.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                Predicates.CreateRegistry().KnownTypes.OrderBy(n => n, StringComparer.Ordinal).ToArray(), "and the registry holds exactly the table");

            // An unavailable source (every answer null) or a missing game view makes the predicate false, whatever the comparison says.
            foreach (string json in r.Json)
            {
                ICondition condition = ParseOk(json);
                bool isDate = json.Contains("dateAtLeast") || json.Contains("dateBefore");

                var unavailable = new FakeGameState { Unavailable = true };
                if (!isDate) Assert.False(condition.Evaluate(unavailable.Context(Feb1980)), "unavailable source: " + json);

                if (!isDate) Assert.False(condition.Evaluate(new ConditionContext(null, Feb1980)), "no game view: " + json);
                if (isDate) Assert.False(condition.Evaluate(new ConditionContext(new FakeGameState(), default(DateTime))), "no date: " + json);
            }
        }

        [Test]
        public void Predicates_DateWithoutAKnownTodayIsFalse()
        {
            var state = new FakeGameState();
            var noDate = new ConditionContext(state, default(DateTime));

            Assert.False(ParseOk("{'type':'dateAtLeast','year':1}").Evaluate(noDate));
            Assert.False(ParseOk("{'type':'dateBefore','year':9999}").Evaluate(noDate));
            Assert.False(ParseOk("{'type':'daysSinceStartAtLeast','days':0}").Evaluate(noDate));
            Assert.False(noDate.HasDate);
            Assert.True(state.Context(Feb1980).HasDate);
        }

        [Test]
        public void Predicates_DaysSinceStartNeedsAStartDate()
        {
            var state = new FakeGameState { Start = null };

            Assert.False(Eval("{'type':'daysSinceStartAtLeast','days':0}", state), "an unknown start date is unavailable");

            state.Start = new DateTime(1980, 2, 1);
            Assert.True(Eval("{'type':'daysSinceStartAtLeast','days':0}", state));
            Assert.False(Eval("{'type':'daysSinceStartAtLeast','days':1}", state));
        }

        [Test]
        public void Predicates_DateTimeOfDayIsIgnored()
        {
            var state = new FakeGameState();

            Assert.True(Eval("{'type':'dateAtLeast','year':1980,'month':2,'day':1}", state, new DateTime(1980, 2, 1, 15, 30, 0)));
            Assert.False(Eval("{'type':'dateBefore','year':1980,'month':2,'day':1}", state, new DateTime(1980, 2, 1, 15, 30, 0)));
        }

        [Test]
        public void Predicates_RepeatedParseGivesIndependentConditions()
        {
            var state = new FakeGameState();
            ICondition a = ParseOk("{'type':'moneyAtLeast','amount':100}");
            ICondition b = ParseOk("{'type':'moneyAtLeast','amount':200}");
            state.Player.Money = 150;

            Assert.True(a.Evaluate(state.Context(Feb1980)));
            Assert.False(b.Evaluate(state.Context(Feb1980)));
            Assert.True(a.Evaluate(state.Context(Feb1980)), "a condition can be evaluated again and again");
        }

        [Test]
        public void Predicates_ReputationToleranceForFloatStorage()
        {
            var state = new FakeGameState();
            state.Player.Reputation = 79.1f;   // stored as a float: 79.0999984741...

            Assert.True(Eval("{'type':'reputationAtLeast','value':79.1}", state));
            Assert.False(Eval("{'type':'reputationBelow','value':79.1}", state), "the two predicates stay complementary");

            state.Player.Reputation = 79.09;
            Assert.False(Eval("{'type':'reputationAtLeast','value':79.1}", state));
            Assert.True(Eval("{'type':'reputationBelow','value':79.1}", state));
        }

        [Test]
        public void Predicates_FlagWithoutValueMeansNonZero()
        {
            var state = new FakeGameState();
            const string any = "{'type':'flag','feature':'f','key':'k'}";

            Assert.False(Eval(any, state), "never set is 0");
            state.SetFlag("f", "k", 7);
            Assert.True(Eval(any, state));
            state.SetFlag("f", "k", -1);
            Assert.True(Eval(any, state));
            Assert.True(Eval("{'type':'flag','feature':'f','key':'k','value':-1}", state));
            Assert.False(Eval("{'type':'flag','feature':'f','key':'k','value':0}", state));
            Assert.False(Eval("{'type':'flag','feature':'other','key':'k'}", state), "features have separate flags");

            state.SetFlag("f", "k", 0);
            Assert.True(Eval("{'type':'flag','feature':'f','key':'k','value':0}", state), "an explicit 0 can be asked for");
        }

        [Test]
        public void Predicates_CompanyTypeIsCaseInsensitive()
        {
            var state = new FakeGameState();
            state.Player.Type = "idm";

            Assert.True(Eval("{'type':'companyType','kind':'IDM'}", state));
            Assert.False(Eval("{'type':'companyType','kind':'foundry'}", state));
            state.Player.Type = null;
            Assert.False(Eval("{'type':'companyType','kind':'idm'}", state), "an unknown type is unavailable");
        }

        [Test]
        public void Predicates_MissingFinanceReportsAreFalse()
        {
            var state = new FakeGameState();   // fewer than two reports: the sources answer null

            Assert.False(Eval("{'type':'profitLastMonthAtLeast','amount':-1000000}", state));
            Assert.False(Eval("{'type':'revenueLastMonthAtLeast','amount':0}", state));

            state.Player.ProfitLastMonth = 0;
            Assert.True(Eval("{'type':'profitLastMonthAtLeast','amount':0}", state));
        }

        [Test]
        public void Predicates_NotTurnsUnavailableIntoTrue()
        {
            var state = new FakeGameState { Unavailable = true };

            Assert.False(Eval("{'type':'moneyAtLeast','amount':0}", state));
            Assert.True(Eval("{'not':{'type':'moneyAtLeast','amount':0}}", state), "conditions are two-valued, as documented");
        }

        // ---- the company parameter ----

        [Test]
        public void Company_DefaultsToThePlayerAndHonoursTheContextSubject()
        {
            var state = new FakeGameState();
            state.Player.Money = 10;
            state.Ai(2).Money = 5000;
            const string rich = "{'type':'moneyAtLeast','amount':1000}";

            Assert.False(Eval(rich, state), "no company: the player");
            Assert.False(Eval(rich, state, null, "player"));
            Assert.True(Eval(rich, state, null, "ai:2"), "the subject of the context is ai:2");
            Assert.True(Eval("{'type':'moneyAtLeast','amount':1000,'company':'ai:2'}", state), "explicit ai:2");
            Assert.True(Eval("{'type':'moneyAtLeast','amount':1000,'company':'ai:2'}", state, null, "player"), "explicit ai:2 with the player as subject");
        }

        [Test]
        public void Company_ExplicitCompanyBeatsTheContextSubject()
        {
            var state = new FakeGameState();
            state.Player.Money = 5000;
            state.Ai(2).Money = 10;

            Assert.True(Eval("{'type':'moneyAtLeast','amount':1000,'company':'player'}", state, null, "ai:2"));
            Assert.False(Eval("{'type':'moneyAtLeast','amount':1000,'company':'ai:2'}", state, null, "player"));
        }

        [Test]
        public void Company_UnknownCompanyIsUnavailableNotAnError()
        {
            var state = new FakeGameState();

            Assert.False(Eval("{'type':'moneyBelow','amount':1000000,'company':'ai:99'}", state));
            Assert.False(Eval("{'type':'moneyAtLeast','amount':0}", state, null, "ai:99"));
        }

        [Test]
        public void Company_AnyAiMeansAtLeastOneNonBankruptRival()
        {
            var state = new FakeGameState();
            state.Ai(1).Money = 10;
            state.Ai(2).Money = 5000;
            const string rich = "{'type':'moneyAtLeast','amount':1000,'company':'anyAI'}";

            Assert.True(Eval(rich, state));

            state.Ai(2).Bankrupt = true;
            Assert.False(Eval(rich, state), "a bankrupt rival does not count");

            state.Ai(3).Money = 9999;
            Assert.True(Eval(rich, state));
        }

        [Test]
        public void Company_AnyAiWithoutRivalsOrWithoutTheCache()
        {
            var state = new FakeGameState();
            const string rich = "{'type':'moneyAtLeast','amount':0,'company':'anyAI'}";

            Assert.False(Eval(rich, state), "no rivals: nobody satisfies");

            state.Ai(1);
            Assert.True(Eval(rich, state));

            state.AiKeysUnavailable = true;
            Assert.False(Eval(rich, state), "the rival list is not available");
        }

        [Test]
        public void Company_RivalBankruptDefaultsToAnyAiAndCountsBankruptOnes()
        {
            var state = new FakeGameState();
            state.Ai(1);
            state.Ai(2);

            Assert.False(Eval("{'type':'rivalBankrupt'}", state));
            Assert.False(Eval("{'type':'rivalBankrupt','company':'anyAI'}", state));

            state.Ai(2).Bankrupt = true;
            Assert.True(Eval("{'type':'rivalBankrupt'}", state));
            Assert.True(Eval("{'type':'rivalBankrupt','company':'anyAI'}", state));
            Assert.True(Eval("{'type':'rivalBankrupt','company':'ai:2'}", state));
            Assert.False(Eval("{'type':'rivalBankrupt','company':'ai:1'}", state));
        }

        [Test]
        public void Company_RivalExistsNeedsAKnownNonBankruptCompany()
        {
            var state = new FakeGameState();
            state.Ai(4);
            const string json = "{'type':'rivalExists','company':'ai:4'}";

            Assert.True(Eval(json, state));
            state.Ai(4).Exists = false;
            Assert.False(Eval(json, state), "does not resolve in the world");
            state.Ai(4).Exists = true;
            state.Ai(4).Bankrupt = true;
            Assert.False(Eval(json, state));
            Assert.False(Eval("{'type':'rivalExists','company':'ai:5'}", state), "never heard of");
        }

        [Test]
        public void Company_ReputationOfARival()
        {
            var state = new FakeGameState();
            state.Player.Reputation = 30;
            state.Ai(1).Reputation = 80;

            Assert.False(Eval("{'type':'reputationAtLeast','value':60}", state));
            Assert.True(Eval("{'type':'reputationAtLeast','value':60,'company':'ai:1'}", state));
            Assert.True(Eval("{'type':'reputationAtLeast','value':60,'company':'anyAI'}", state));
            Assert.True(Eval("{'type':'reputationBelow','value':40}", state));
        }

        // ---- selector and context ----

        [Test]
        public void CompanySelector_KindsAndText()
        {
            Assert.Equal(CompanySelectorKind.Context, CompanySelector.Subject.Kind);
            Assert.Equal(CompanySelectorKind.AnyAi, CompanySelector.AnyAi.Kind);
            CompanySelector one = CompanySelector.ForKey("ai:7");
            Assert.Equal(CompanySelectorKind.Key, one.Kind);
            Assert.Equal("ai:7", one.Key);
            Assert.Equal("ai:7", one.ToString());
            Assert.Equal("anyAI", CompanySelector.AnyAi.ToString());
            Assert.Equal(string.Empty, CompanySelector.Subject.ToString());
            Assert.Equal("player", CompanySelector.ForKey("player").ToString());

            Assert.Throws<ArgumentException>(() => CompanySelector.ForKey("nobody"));
            Assert.Throws<ArgumentException>(() => CompanySelector.ForKey(null));
            Assert.Throws<ArgumentException>(() => CompanySelector.ForKey("anyAI"));
        }

        [Test]
        public void CompanySelector_TestHonoursIncludeBankrupt()
        {
            var state = new FakeGameState();
            state.Ai(1).Bankrupt = true;
            ConditionContext ctx = state.Context(Feb1980);

            Assert.False(CompanySelector.AnyAi.Test(ctx, (s, k) => s.IsBankrupt(k)), "bankrupt companies are skipped by default");
            Assert.True(CompanySelector.AnyAi.Test(ctx, (s, k) => s.IsBankrupt(k), true));
            Assert.True(CompanySelector.ForKey("ai:1").Test(ctx, (s, k) => s.IsBankrupt(k)), "an explicit key is always tested");
            Assert.False(CompanySelector.Subject.Test(ctx, (s, k) => s.IsBankrupt(k)));
            Assert.False(CompanySelector.Subject.Test(new ConditionContext(null, Feb1980), (s, k) => true));
        }

        [Test]
        public void ConditionContext_DefaultsToThePlayer()
        {
            var state = new FakeGameState();

            Assert.Equal("player", new ConditionContext(state, Feb1980).SubjectKey);
            Assert.Equal("player", new ConditionContext(state, Feb1980, "").SubjectKey);
            Assert.Equal("ai:3", new ConditionContext(state, Feb1980, "ai:3").SubjectKey);
            Assert.Equal(Feb1980, new ConditionContext(state, Feb1980).Today);
            Assert.True(ReferenceEquals(state, new ConditionContext(state, Feb1980).State));
        }

        // ---- evaluation never throws ----

        [Test]
        public void Evaluator_ThrowingSourceIsFalseAndReported()
        {
            var state = new FakeGameState { ThrowOnAccess = true };
            ICondition condition = ParseOk("{'all':[{'type':'cheatsEnabled'},{'type':'moneyAtLeast','amount':1}]}");
            var problems = new List<string>();

            Assert.Throws<InvalidOperationException>(() => condition.Evaluate(state.Context(Feb1980)), "the raw tree propagates");
            bool result = ConditionEvaluator.Evaluate(condition, state.Context(Feb1980), problems.Add);

            Assert.False(result);
            Assert.Equal(1, problems.Count);
            Assert.True(problems[0].Contains("all(cheatsEnabled, moneyAtLeast(amount=1))"), problems[0]);
            Assert.True(problems[0].Contains("InvalidOperationException"), problems[0]);
        }

        [Test]
        public void Evaluator_ThrowInsideNotOrAnyMakesTheWholeTreeFalse()
        {
            var state = new FakeGameState { ThrowOnAccess = true };

            Assert.False(ConditionEvaluator.Evaluate(ParseOk("{'not':{'type':'cheatsEnabled'}}"), state.Context(Feb1980)));
            Assert.False(ConditionEvaluator.Evaluate(ParseOk("{'any':[{'type':'cheatsEnabled'},{'all':[]}]}"), state.Context(Feb1980)));
        }

        [Test]
        public void Evaluator_NullArgumentsAreFalse()
        {
            var state = new FakeGameState();

            Assert.False(ConditionEvaluator.Evaluate(null, state.Context(Feb1980)));
            Assert.False(ConditionEvaluator.Evaluate(ParseOk("{'all':[]}"), null));
            Assert.True(ConditionEvaluator.Evaluate(ParseOk("{'all':[]}"), state.Context(Feb1980)));
        }

        [Test]
        public void Evaluator_WorksWithoutAnErrorCallback()
        {
            var state = new FakeGameState { ThrowOnAccess = true };

            Assert.False(ConditionEvaluator.Evaluate(ParseOk("{'type':'cheatsEnabled'}"), state.Context(Feb1980)));
        }

        [Test]
        public void Evaluator_DescribeFailureStillReports()
        {
            var problems = new List<string>();

            bool result = ConditionEvaluator.Evaluate(new Faulty(), new FakeGameState().Context(Feb1980), problems.Add);

            Assert.False(result);
            Assert.Equal(1, problems.Count);
            Assert.True(problems[0].Contains("Faulty"), problems[0]);
        }

        private sealed class Faulty : ICondition
        {
            public bool Evaluate(ConditionContext ctx)
            {
                throw new ApplicationException("boom");
            }

            public string Describe()
            {
                throw new ApplicationException("describe failed");
            }
        }

        // ---- registration ----

        [Test]
        public void Register_CorePredicatesAreRegisteredOnceWithoutDots()
        {
            var registry = new PredicateRegistry();

            int added = Predicates.RegisterCore(registry);

            Assert.Equal(32, added);
            Assert.Equal(32, registry.KnownTypes.Count);
            foreach (string name in registry.KnownTypes)
            {
                Assert.True(name.IndexOf('.') < 0, "core predicate names contain no dot: " + name);
                Assert.Equal(PredicateRegistry.CoreOwner, registry.OwnerOf(name));
                Assert.True(registry.IsKnown(name));
            }
            Assert.Equal("mod-framework", PredicateRegistry.CoreOwner);

            Assert.Equal(0, Predicates.RegisterCore(registry), "a second call finds every name taken");
            Assert.Equal(32, registry.Rejected.Count);
            Assert.True(registry.Rejected[0].Reason.Contains("already registered"), registry.Rejected[0].Reason);
            Assert.Equal(32, registry.KnownTypes.Count);
        }

        [Test]
        public void Register_DuplicateNameIsRejected()
        {
            var registry = Predicates.CreateRegistry();
            Func<JsonObject, List<string>, string, ICondition> factory = (node, errors, path) => new DelegateCondition("x", ctx => true);

            Assert.True(registry.Register("es.hasFab", "event-system", factory));
            string error;
            Assert.False(registry.TryRegister("es.hasFab", "event-system", factory, out error));
            Assert.True(error.Contains("already registered by event-system"), error);
            Assert.False(registry.Register("es.hasFab", "market-shocks", factory), "another feature cannot take it either");
            Assert.False(registry.Register("dateAtLeast", "mod-framework", factory), "nor can a core name be replaced");
            Assert.False(registry.Register("dateAtLeast", "event-system", factory));

            Assert.Equal(4, registry.Rejected.Count);
            Assert.Equal("es.hasFab", registry.Rejected[0].Type);
            Assert.Equal("event-system", registry.Rejected[0].OwnerFeature);
            Assert.Equal("event-system", registry.OwnerOf("es.hasFab"), "the first owner keeps it");
        }

        [Test]
        public void Register_FeaturePredicateNeedsAPrefix_AC_11_2()
        {
            var registry = Predicates.CreateRegistry(PrefixPolicy);
            Func<JsonObject, List<string>, string, ICondition> factory = (node, errors, path) => new DelegateCondition("x", ctx => true);

            string error;
            Assert.False(registry.TryRegister("hasFab", "event-system", factory, out error));
            Assert.True(error.Contains("no feature prefix"), error);
            Assert.False(registry.IsKnown("hasFab"));

            Assert.True(registry.Register("es.hasFab", "event-system", factory));
            Assert.True(registry.IsKnown("es.hasFab"));
            Assert.Equal(1, registry.Rejected.Count);
            Assert.Equal("hasFab", registry.Rejected[0].Type);

            // And the accepted one is available in JSON.
            Assert.NotNull(ParseOk("{'type':'es.hasFab'}", registry));
            Assert.True(ParseErrors("{'type':'hasFab'}", registry)[0].Contains("unknown predicate \"hasFab\""));
        }

        [Test]
        public void Register_NamePolicyChecksTheExactPrefix()
        {
            var registry = Predicates.CreateRegistry(PrefixPolicy);
            Func<JsonObject, List<string>, string, ICondition> factory = (node, errors, path) => new DelegateCondition("x", ctx => true);

            string error;
            Assert.False(registry.TryRegister("ms.hasFab", "event-system", factory, out error), "a prefix of another feature");
            Assert.True(error.Contains("must start with the prefix of that feature"), error);
            Assert.False(registry.Register("hist.hasFab", "event-system", factory));
            Assert.True(registry.Register("ms.sellsBelowCost", "market-shocks", factory));
            Assert.True(registry.Register("scn.a", "scenarios-achievements", factory));
            Assert.True(registry.Register("chl.b", "scenarios-achievements", factory), "a feature may own several prefixes");
            Assert.False(registry.Register("es.x", "scenarios-achievements", factory));
        }

        [Test]
        public void Register_WithoutAPolicyOnlyTheStructureIsChecked()
        {
            var registry = new PredicateRegistry();
            Func<JsonObject, List<string>, string, ICondition> factory = (node, errors, path) => new DelegateCondition("x", ctx => true);

            Assert.False(registry.Register("hasFab", "event-system", factory));
            Assert.True(registry.Register("ms.hasFab", "event-system", factory), "the exact prefix is the policy's business");
            Assert.False(registry.Register("core.thing", "mod-framework", factory), "core names have no dot");
            Assert.True(registry.Register("thing", "mod-framework", factory));
        }

        [Test]
        public void Register_PolicyCanBeSetLater()
        {
            var registry = new PredicateRegistry();
            Func<JsonObject, List<string>, string, ICondition> factory = (node, errors, path) => new DelegateCondition("x", ctx => true);
            Assert.True(registry.Register("ms.a", "event-system", factory));

            registry.NamePolicy = PrefixPolicy;

            Assert.False(registry.Register("ms.b", "event-system", factory));
            Assert.True(registry.IsKnown("ms.a"), "what was registered before stays");
            Assert.NotNull(registry.NamePolicy);
        }

        [Test]
        public void Register_ThrowingPolicyRejectsWithoutThrowing()
        {
            var registry = new PredicateRegistry((feature, name) => { throw new InvalidOperationException("policy broke"); });
            Func<JsonObject, List<string>, string, ICondition> factory = (node, errors, path) => new DelegateCondition("x", ctx => true);

            string error;
            bool accepted = registry.TryRegister("es.a", "event-system", factory, out error);

            Assert.False(accepted);
            Assert.True(error.Contains("policy broke"), error);
            Assert.Equal(1, registry.Rejected.Count);
        }

        [Test]
        public void Register_NameShapeIsChecked()
        {
            var registry = new PredicateRegistry();
            Func<JsonObject, List<string>, string, ICondition> factory = (node, errors, path) => new DelegateCondition("x", ctx => true);

            foreach (string bad in new[] { null, "", "1abc", "es.", ".es", "es..x", "es.1x", "a b", "es.has-fab", "es.hasFab!", "es.имя", new string('a', 65) })
                Assert.False(registry.Register(bad, "event-system", factory), "name \"" + bad + "\" must be refused");

            foreach (string good in new[] { "es.hasFab", "es.has_fab2", "es.a.b", "ms.X", new string('a', 30) + "." + new string('b', 30) })
                Assert.True(registry.Register(good, "event-system", factory), "name \"" + good + "\" must be accepted");
        }

        [Test]
        public void Register_TreeKeywordsAreReserved()
        {
            var registry = new PredicateRegistry();
            Func<JsonObject, List<string>, string, ICondition> factory = (node, errors, path) => new DelegateCondition("x", ctx => true);

            Assert.False(registry.Register("all", "mod-framework", factory));
            Assert.False(registry.Register("any", "mod-framework", factory));
            Assert.False(registry.Register("not", "mod-framework", factory));
            Assert.True(registry.Register("notEmpty", "mod-framework", factory));
        }

        [Test]
        public void Register_MissingOwnerOrFactoryIsRejected()
        {
            var registry = new PredicateRegistry();
            Func<JsonObject, List<string>, string, ICondition> factory = (node, errors, path) => new DelegateCondition("x", ctx => true);

            Assert.False(registry.Register("es.a", null, factory));
            Assert.False(registry.Register("es.a", "", factory));
            Assert.False(registry.Register("es.a", "event-system", null));
            Assert.Equal(3, registry.Rejected.Count);
            Assert.Equal(0, registry.KnownTypes.Count);
            Assert.True(registry.Rejected[0].ToString().Contains("es.a"));
        }

        [Test]
        public void Register_KnownTypesKeepRegistrationOrder()
        {
            var registry = Predicates.CreateRegistry();
            Func<JsonObject, List<string>, string, ICondition> factory = (node, errors, path) => new DelegateCondition("x", ctx => true);
            registry.Register("es.second", "event-system", factory);
            registry.Register("ms.first", "market-shocks", factory);

            Assert.Equal("dateAtLeast", registry.KnownTypes[0], "core predicates come first");
            Assert.Equal("es.second", registry.KnownTypes[32]);
            Assert.Equal("ms.first", registry.KnownTypes[33]);
            Assert.Null(registry.OwnerOf("nope"));
            Assert.Null(registry.OwnerOf(null));
            Assert.False(registry.IsKnown(null));
        }

        // ---- feature predicates ----

        private static PredicateRegistry FeatureRegistry()
        {
            var registry = Predicates.CreateRegistry(PrefixPolicy);
            registry.Register("es.hasFab", "event-system", (node, errors, path) =>
            {
                var args = new ConditionArgs(node, errors, path);
                int lines = args.OptionalInt("lines", 1, 1, 100);
                CompanySelector who = args.Company(CompanyScope.PlayerOrAi);
                if (!args.Finish()) return null;
                return new DelegateCondition(args.Describe("es.hasFab"), ctx => who.Test(ctx, (s, k) => s.FactoryLines(k) >= lines));
            });
            return registry;
        }

        [Test]
        public void FeaturePredicate_UsableInATreeWithParametersAndPaths()
        {
            PredicateRegistry registry = FeatureRegistry();
            var state = new FakeGameState();
            state.Player.FactoryLines = 2;
            const string json = "{'all':[{'type':'dateAtLeast','year':1980},{'type':'es.hasFab','lines':2}]}";

            ICondition condition = ParseOk(json, registry);

            Assert.True(condition.Evaluate(state.Context(Feb1980)));
            state.Player.FactoryLines = 1;
            Assert.False(condition.Evaluate(state.Context(Feb1980)));
            Assert.Equal("all(dateAtLeast(year=1980), es.hasFab(lines=2))", condition.Describe());

            List<string> errors = ParseErrors("{'all':[{'type':'cheatsEnabled'},{'type':'es.hasFab','lines':0}]}", registry);
            Assert.True(errors[0].StartsWith("$.all[1].lines:"), errors[0]);
        }

        [Test]
        public void FeaturePredicate_FactoryProblemsNeverEscape()
        {
            var registry = Predicates.CreateRegistry();
            registry.Register("t.throws", "event-system", (node, errors, path) => { throw new InvalidOperationException("factory broke"); });
            registry.Register("t.nothing", "event-system", (node, errors, path) => null);
            registry.Register("t.complains", "event-system", (node, errors, path) =>
            {
                errors.Add(path + ".x: bad");
                return new DelegateCondition("t", ctx => true);
            });

            List<string> throwing = ParseErrors("{'all':[{'type':'t.throws'}]}", registry);
            Assert.True(throwing[0].StartsWith("$.all[0]:"), throwing[0]);
            Assert.True(throwing[0].Contains("factory broke"), throwing[0]);

            List<string> silent = ParseErrors("{'type':'t.nothing'}", registry);
            Assert.True(silent[0].Contains("produced no condition"), silent[0]);

            List<string> complaining = ParseErrors("{'type':'t.complains'}", registry);
            Assert.Equal(1, complaining.Count);
            Assert.Equal("$.x: bad", complaining[0]);
        }

        [Test]
        public void FeaturePredicate_ThrowingEvaluationIsCaughtByTheEvaluator()
        {
            var registry = Predicates.CreateRegistry();
            registry.Register("t.boom", "event-system", (node, errors, path) =>
                new DelegateCondition("t.boom", ctx => { throw new InvalidOperationException("kaboom"); }));
            ICondition condition = ParseOk("{'any':[{'type':'t.boom'},{'all':[]}]}", registry);
            var problems = new List<string>();

            Assert.False(ConditionEvaluator.Evaluate(condition, new FakeGameState().Context(Feb1980), problems.Add));
            Assert.Equal(1, problems.Count);
            Assert.True(problems[0].Contains("kaboom"), problems[0]);
        }

        [Test]
        public void DelegateCondition_DescribeAndNullGuards()
        {
            var condition = new DelegateCondition("hello", ctx => true);

            Assert.Equal("hello", condition.Describe());
            Assert.Equal("predicate", new DelegateCondition(null, ctx => true).Describe());
            Assert.False(condition.Evaluate(null), "a null context is false, not a crash");
            Assert.True(condition.Evaluate(new FakeGameState().Context(Feb1980)));
            Assert.Throws<ArgumentNullException>(() => new DelegateCondition("x", null));
        }

        [Test]
        public void Nodes_RejectNullChildren()
        {
            Assert.Throws<ArgumentNullException>(() => new AllCondition(null));
            Assert.Throws<ArgumentNullException>(() => new AnyCondition(null));
            Assert.Throws<ArgumentNullException>(() => new NotCondition(null));
            Assert.Throws<ArgumentException>(() => new AllCondition(new ICondition[] { null }));
            Assert.Throws<ArgumentException>(() => new AnyCondition(new ICondition[] { new AllCondition(new ICondition[0]), null }));

            var all = new AllCondition(new ICondition[] { new AnyCondition(new ICondition[0]) });
            Assert.Equal(1, all.Children.Count);
            Assert.Equal("all(any())", all.Describe());
            Assert.Equal("not(all(any()))", new NotCondition(all).Describe());
        }

        // ---- ConditionArgs ----

        private static JsonObject Obj(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(J(json), out node, out error), json);
            return (JsonObject)node;
        }

        [Test]
        public void Args_ReadTypedValuesAndDescribe()
        {
            var errors = new List<string>();
            var args = new ConditionArgs(Obj("{'type':'x','n':5,'f':0.25,'b':true,'s':'text','c':'Month'}"), errors, "$.a[0]");

            Assert.Equal(5, args.RequireInt("n", 0, 10));
            Assert.Equal(0.25, args.RequireNumber("f", 0, 1));
            Assert.True(args.OptionalBool("b", false));
            Assert.Equal("text", args.RequireString("s"));
            Assert.Equal("month", args.RequireChoice("c", "day", "month"));
            Assert.Equal(7, args.OptionalInt("missingInt", 7, 0, 10));
            Assert.Equal(2.5, args.OptionalNumber("missingNumber", 2.5, 0, 10));
            Assert.False(args.OptionalBool("missingBool", false));
            Assert.Null(args.OptionalString("missingString"));
            Assert.Equal("day", args.OptionalChoice("missingChoice", "day", "day", "month"));
            Assert.True(args.Finish());
            Assert.True(args.Ok);
            Assert.Equal(0, errors.Count);
            Assert.Equal("$.a[0]", args.Path);
            Assert.Equal("name(n=5, f=0.25, b=true, s=\"text\", c=month)", args.Describe("name"));
        }

        [Test]
        public void Args_ErrorsKeepGoingAndFinishReportsTypos()
        {
            var errors = new List<string>();
            var args = new ConditionArgs(Obj("{'type':'x','n':'five','f':2,'extra':1,'s':7}"), errors, "$");

            args.RequireInt("n", 0, 10);
            args.RequireNumber("f", 0, 1);
            args.RequireString("s");
            args.RequireInt("gone", 0, 10);
            bool clean = args.Finish();

            Assert.False(clean);
            Assert.False(args.Ok);
            Assert.Equal(5, errors.Count);
            Assert.True(errors[0].StartsWith("$.n:"), errors[0]);
            Assert.True(errors[1].StartsWith("$.f: must be between 0 and 1, got 2"), errors[1]);
            Assert.True(errors[2].StartsWith("$.s: expected a string"), errors[2]);
            Assert.True(errors[3].StartsWith("$.gone: required"), errors[3]);
            Assert.True(errors[4].StartsWith("$.extra: unknown parameter"), errors[4]);
        }

        [Test]
        public void Args_OkIgnoresErrorsAddedBeforeTheNode()
        {
            var errors = new List<string> { "$.other: earlier problem" };
            var args = new ConditionArgs(Obj("{'type':'x'}"), errors, "$");

            Assert.True(args.Ok);
            Assert.True(args.Finish());

            args.Fail("p", "custom");
            Assert.False(args.Ok);
            Assert.Equal("$.p: custom", errors[1]);
            Assert.True(args.Has("type"));
            Assert.False(args.Has("p"));
        }

        [Test]
        public void Args_ConstructorRejectsNulls()
        {
            Assert.Throws<ArgumentNullException>(() => new ConditionArgs(null, new List<string>(), "$"));
            Assert.Throws<ArgumentNullException>(() => new ConditionArgs(new JsonObject(), null, "$"));
            Assert.Equal("$", new ConditionArgs(new JsonObject(), new List<string>(), null).Path);
        }

        [Test]
        public void Args_IntegerAcceptsWholeNumbersOnly()
        {
            var errors = new List<string>();
            var args = new ConditionArgs(Obj("{'a':3.0,'b':3.5,'c':1e3,'d':99999999999}"), errors, "$");

            Assert.Equal(3, args.RequireInt("a", 0, 10));
            args.RequireInt("b", 0, 10);
            Assert.Equal(1000, args.RequireInt("c", 0, 5000));
            args.RequireInt("d", 0, 10);

            Assert.Equal(2, errors.Count);
            Assert.True(errors[0].Contains("expected an integer, got 3.5"), errors[0]);
            Assert.True(errors[1].Contains("expected an integer"), errors[1]);
        }

        // ---- cost ----

        [Test]
        public void Evaluation_IsCheap()
        {
            var state = new FakeGameState();
            state.Player.Money = 5000;
            state.Player.CpuCount = 4;
            state.Ai(1).Money = 10;
            ICondition condition = ParseOk(
                "{'all':[{'type':'dateAtLeast','year':1980},{'type':'moneyAtLeast','amount':1000},{'type':'cpuCountAtLeast','count':3}," +
                "{'any':[{'type':'inDefault'},{'type':'moneyAtLeast','amount':10,'company':'anyAI'}]},{'not':{'type':'cheatsEnabled'}}]}");
            ConditionContext ctx = state.Context(Feb1980);
            Assert.True(condition.Evaluate(ctx));

            const int rounds = 20000;
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < rounds; i++) condition.Evaluate(ctx);
            watch.Stop();

            double millisecondsEach = watch.Elapsed.TotalMilliseconds / rounds;
            Assert.True(millisecondsEach < 0.05, "budget is 0.05 ms per condition, measured " + millisecondsEach.ToString("F4"));
        }

        // ---- helpers ----

        /// <summary>A stand-in for <c>ModIds.IsPredicateNameAllowed</c> over a small prefix table.</summary>
        private static bool PrefixPolicy(string feature, string name)
        {
            if (feature == PredicateRegistry.CoreOwner) return name.IndexOf('.') < 0;

            string[] prefixes;
            switch (feature)
            {
                case "event-system": prefixes = new[] { "es" }; break;
                case "market-shocks": prefixes = new[] { "ms" }; break;
                case "scenarios-achievements": prefixes = new[] { "scn", "chl", "ach" }; break;
                default: return false;
            }
            return prefixes.Any(prefix => name.StartsWith(prefix + ".", StringComparison.Ordinal));
        }
    }
}
