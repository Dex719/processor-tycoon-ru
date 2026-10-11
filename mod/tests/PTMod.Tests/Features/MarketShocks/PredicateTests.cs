using System;
using System.Collections.Generic;
using System.IO;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.MarketShocks;
using PTMod.Tests.Architecture;
using PTMod.Tests.Conditions;
using static PTMod.Tests.Features.MarketShocks.ShockTestData;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// market-shocks, FR-12 (TSK-007, TSK-008): the predicates <c>ms.sellsBelowCost</c> (AC-6.1: a CPU sold at $90 against a real cost of $100 with a share of
    /// 12 % qualifies the player, at $96 it does not) and <c>ms.hasRecentCpu</c> (AC-12.1: a CPU of 1994 is recent in 1995, not in 1996), on the core's
    /// <c>FakeGameState</c> and a list of CPUs. A missing source - <c>MarketDataProvider.Instance == null</c> shows up as no market share - is "false" and never an
    /// exception (FR-12 item 3). The names pass the convention of identifiers (FR-12 item 4).
    /// </summary>
    [Covers(typeof(ShockPredicateLogic), typeof(CpuSalesFacts), typeof(IShockCpuSource))]
    public class PredicateTests
    {
        private static readonly DateTime In1995 = new DateTime(1995, 6, 1);

        private static JsonObject Node(string type, string extra = "")
        {
            return Params("{'type':'" + type + "'" + extra + "}");
        }

        private static bool Sells(FakeGameState state, IShockCpuSource cpus, string extra = "", string subject = null, DateTime? today = null)
        {
            return ShockPredicateLogic.SellsBelowCost(Node("ms.sellsBelowCost", extra), state.Context(today ?? In1995, subject), cpus);
        }

        private static bool Recent(FakeGameState state, IShockCpuSource cpus, string extra = "", string subject = null, DateTime? today = null)
        {
            return ShockPredicateLogic.HasRecentCpu(Node("ms.hasRecentCpu", extra), state.Context(today ?? In1995, subject), cpus);
        }

        private static FakeGameState PlayerWithShare(double share)
        {
            var state = new FakeGameState();
            state.Player.MarketShareDay = share;
            return state;
        }

        // ---- ms.sellsBelowCost (AC-6.1) ----

        [Test]
        public void SellsBelowCost_Ac61_NinetyAgainstAHundredWithTwelvePercent_QualifiesThePlayer()
        {
            FakeGameState state = PlayerWithShare(0.12);
            var cpus = new FakeCpuSource().Add(CompanyKey.Player, OnSale(90f, 100f));

            Assert.True(Sells(state, cpus));
        }

        [Test]
        public void SellsBelowCost_Ac61_NinetySixAgainstAHundred_DoesNotQualify()
        {
            FakeGameState state = PlayerWithShare(0.12);
            var cpus = new FakeCpuSource().Add(CompanyKey.Player, OnSale(96f, 100f));

            Assert.False(Sells(state, cpus));
        }

        [Test]
        public void SellsBelowCost_TheLineIs95PercentOfTheCost_AndStrict()
        {
            FakeGameState state = PlayerWithShare(0.12);

            Assert.True(Sells(state, new FakeCpuSource().Add(CompanyKey.Player, OnSale(94.99f, 100f))));
            Assert.False(Sells(state, new FakeCpuSource().Add(CompanyKey.Player, OnSale(95f, 100f))), "exactly 95 % is not below");
            Assert.False(Sells(state, new FakeCpuSource().Add(CompanyKey.Player, OnSale(100f, 100f))));
            Assert.False(Sells(state, new FakeCpuSource().Add(CompanyKey.Player, OnSale(130f, 100f))), "above the cost is no dumping");
        }

        [Test]
        public void SellsBelowCost_PriceRatioParameter_MovesTheLine()
        {
            FakeGameState state = PlayerWithShare(0.12);
            var cpus = new FakeCpuSource().Add(CompanyKey.Player, OnSale(96f, 100f));

            Assert.False(Sells(state, cpus));
            Assert.True(Sells(state, cpus, ",'priceRatio':1.0"), "below the cost itself");
            Assert.False(Sells(state, cpus, ",'priceRatio':0.9"));
            Assert.True(Sells(state, new FakeCpuSource().Add(CompanyKey.Player, OnSale(60f, 100f)), ",'priceRatio':0.7"));
        }

        [Test]
        public void SellsBelowCost_MinShare_IsAtLeast_FivePercentByDefault()
        {
            var cpus = new FakeCpuSource().Add(CompanyKey.Player, OnSale(90f, 100f));

            Assert.True(Sells(PlayerWithShare(0.05), cpus), "exactly 5 % is enough");
            Assert.True(Sells(PlayerWithShare(0.50), cpus));
            Assert.False(Sells(PlayerWithShare(0.0499), cpus));
            Assert.False(Sells(PlayerWithShare(0.0), cpus));
        }

        [Test]
        public void SellsBelowCost_MinShareParameter_MovesTheBar()
        {
            var cpus = new FakeCpuSource().Add(CompanyKey.Player, OnSale(90f, 100f));

            Assert.False(Sells(PlayerWithShare(0.12), cpus, ",'minShare':0.2"));
            Assert.True(Sells(PlayerWithShare(0.12), cpus, ",'minShare':0.1"));
            Assert.True(Sells(PlayerWithShare(0.0), cpus, ",'minShare':0"), "no bar at all");
        }

        [Test]
        public void SellsBelowCost_OnlyACpuThatIsOnSaleAndSoldSomething_Counts()
        {
            FakeGameState state = PlayerWithShare(0.12);
            Func<CpuSalesFacts, bool> counts = cpu => Sells(state, new FakeCpuSource().Add(CompanyKey.Player, cpu));

            Assert.True(counts(new CpuSalesFacts(true, true, false, 10, 90f, 100f, 1990)), "the baseline");
            Assert.False(counts(new CpuSalesFacts(false, true, false, 10, 90f, 100f, 1990)), "SellOnMarket is off");
            Assert.False(counts(new CpuSalesFacts(true, false, false, 10, 90f, 100f, 1990)), "not released");
            Assert.False(counts(new CpuSalesFacts(true, true, true, 10, 90f, 100f, 1990)), "retired");
            Assert.False(counts(new CpuSalesFacts(true, true, false, 0, 90f, 100f, 1990)), "sold nothing yesterday: the price is only on paper");
        }

        [Test]
        public void SellsBelowCost_AnyOfTheCpusMayQualify()
        {
            FakeGameState state = PlayerWithShare(0.12);
            var cpus = new FakeCpuSource()
                .Add(CompanyKey.Player, OnSale(150f, 100f))
                .Add(CompanyKey.Player, new CpuSalesFacts(true, true, true, 10, 10f, 100f, 1990))
                .Add(CompanyKey.Player, OnSale(80f, 100f));

            Assert.True(Sells(state, cpus));
            Assert.Equal(CompanyKey.Player, cpus.LastCompanyKey);
        }

        [Test]
        public void SellsBelowCost_NoCpus_IsFalse()
        {
            Assert.False(Sells(PlayerWithShare(0.12), new FakeCpuSource()));
        }

        [Test]
        public void SellsBelowCost_AsksForTheRealUnitCost()
        {
            var cpus = new FakeCpuSource().Add(CompanyKey.Player, OnSale(90f, 100f));

            Sells(PlayerWithShare(0.12), cpus);

            Assert.True(cpus.LastNeedUnitCost, "the cost is what the predicate compares with");
        }

        [Test]
        public void SellsBelowCost_ASourceThatDoesNotWorkTheCostOut_IsNotDumping()
        {
            // cost 0 (the source skipped it): nothing is below 0, so a missing number can never make an innocent company a target.
            Assert.False(Sells(PlayerWithShare(0.12), new FakeCpuSource().Add(CompanyKey.Player, OnSale(90f, 0f))));
            Assert.False(Sells(PlayerWithShare(0.12), new FakeCpuSource().Add(CompanyKey.Player, OnSale(90f, float.NaN))));
        }

        [Test]
        public void SellsBelowCost_AnAiCompanyIsJudgedByItsOwnShareAndCpus()
        {
            var state = new FakeGameState();
            state.Ai(7).MarketShareDay = 0.20;
            state.Player.MarketShareDay = 0.01;
            var cpus = new FakeCpuSource()
                .Add("ai:7", OnSale(50f, 100f))
                .Add(CompanyKey.Player, OnSale(50f, 100f));

            Assert.True(Sells(state, cpus, subject: "ai:7"));
            Assert.Equal("ai:7", cpus.LastCompanyKey);
            Assert.False(Sells(state, cpus), "the player has the CPU but not the share");
            Assert.False(Sells(state, cpus, subject: "ai:8"), "a company the game does not know");
        }

        // ---- missing sources (FR-12 item 3) ----

        [Test]
        public void SellsBelowCost_NoMarketData_IsFalse_WithoutAnException()
        {
            // MarketDataProvider.Instance == null: the game state answers null for the share.
            FakeGameState state = PlayerWithShare(0.12);
            state.Unavailable = true;
            var cpus = new FakeCpuSource().Add(CompanyKey.Player, OnSale(90f, 100f));

            Assert.False(Sells(state, cpus));
            Assert.Equal(0, cpus.Calls, "no need to list the CPUs without a share");
        }

        [Test]
        public void SellsBelowCost_NoCpuData_IsFalse_WithoutAnException()
        {
            FakeGameState state = PlayerWithShare(0.12);
            var cpus = new FakeCpuSource { Unavailable = true };

            Assert.False(Sells(state, cpus));
            Assert.Equal(1, cpus.Calls);
        }

        [Test]
        public void SellsBelowCost_MissingPieces_AreFalse()
        {
            var cpus = new FakeCpuSource().Add(CompanyKey.Player, OnSale(90f, 100f));
            FakeGameState state = PlayerWithShare(0.12);

            Assert.False(ShockPredicateLogic.SellsBelowCost(Node("ms.sellsBelowCost"), null, cpus), "no context");
            Assert.False(ShockPredicateLogic.SellsBelowCost(Node("ms.sellsBelowCost"), new ConditionContext(null, In1995), cpus), "no game state");
            Assert.False(ShockPredicateLogic.SellsBelowCost(Node("ms.sellsBelowCost"), state.Context(In1995), null), "no CPU source");
        }

        [Test]
        public void SellsBelowCost_NoParameters_MeansTheDefaults()
        {
            // The node of the content is often just { "type": "ms.sellsBelowCost" } (or no node object at all in a test).
            FakeGameState state = PlayerWithShare(0.12);
            var cpus = new FakeCpuSource().Add(CompanyKey.Player, OnSale(90f, 100f));

            Assert.True(ShockPredicateLogic.SellsBelowCost(null, state.Context(In1995), cpus));
            Assert.True(ShockPredicateLogic.SellsBelowCost(new JsonObject(), state.Context(In1995), cpus));
            Assert.Equal(0.95, ShockPredicateLogic.DefaultPriceRatio);
            Assert.Equal(0.05, ShockPredicateLogic.DefaultMinShare);
        }

        [Test]
        public void SellsBelowCost_BadParameters_MakeItFalse_NeverAnException()
        {
            FakeGameState state = PlayerWithShare(0.12);
            var cpus = new FakeCpuSource().Add(CompanyKey.Player, OnSale(90f, 100f));

            Assert.False(Sells(state, cpus, ",'priceRatio':'high'"));
            Assert.False(Sells(state, cpus, ",'priceRatio':0"), "a ratio of 0 can never be undercut");
            Assert.False(Sells(state, cpus, ",'priceRatio':-1"));
            Assert.False(Sells(state, cpus, ",'priceRatio':5"));
            Assert.False(Sells(state, cpus, ",'minShare':2"));
            Assert.False(Sells(state, cpus, ",'minShare':-0.1"));
            Assert.False(Sells(state, cpus, ",'minShare':null"));
        }

        // ---- ms.hasRecentCpu (AC-12.1) ----

        [Test]
        public void HasRecentCpu_Ac121_ACpuOf1994_IsRecentIn1995_NotIn1996()
        {
            var state = new FakeGameState();
            var cpus = new FakeCpuSource().Add(CompanyKey.Player, OnSale(100f, 80f, year: 1994));

            Assert.True(Recent(state, cpus, today: new DateTime(1995, 3, 1)));
            Assert.True(Recent(state, cpus, today: new DateTime(1995, 12, 31)));
            Assert.False(Recent(state, cpus, today: new DateTime(1996, 1, 1)));
            Assert.False(Recent(state, cpus, today: new DateTime(1996, 6, 1)));
        }

        [Test]
        public void HasRecentCpu_ACpuOfTheCurrentYear_IsRecent_AndSoIsOneOfTheYearBefore()
        {
            var state = new FakeGameState();

            Assert.True(Recent(state, new FakeCpuSource().Add(CompanyKey.Player, OnSale(1f, 1f, year: 1995))));
            Assert.True(Recent(state, new FakeCpuSource().Add(CompanyKey.Player, OnSale(1f, 1f, year: 1994))));
            Assert.False(Recent(state, new FakeCpuSource().Add(CompanyKey.Player, OnSale(1f, 1f, year: 1993))));
        }

        [Test]
        public void HasRecentCpu_MaxAgeYearsParameter_MovesTheLine()
        {
            var state = new FakeGameState();
            var cpus = new FakeCpuSource().Add(CompanyKey.Player, OnSale(1f, 1f, year: 1993));

            Assert.False(Recent(state, cpus));
            Assert.True(Recent(state, cpus, ",'maxAgeYears':2"));
            Assert.True(Recent(state, cpus, ",'maxAgeYears':5"));
            Assert.False(Recent(state, new FakeCpuSource().Add(CompanyKey.Player, OnSale(1f, 1f, year: 1994)), ",'maxAgeYears':0"), "0: this year's CPUs only");
            Assert.True(Recent(state, new FakeCpuSource().Add(CompanyKey.Player, OnSale(1f, 1f, year: 1995)), ",'maxAgeYears':0"));
            Assert.Equal(1, ShockPredicateLogic.DefaultMaxAgeYears);
        }

        [Test]
        public void HasRecentCpu_OnlyACpuOnSaleCounts_ButItNeedNotHaveSold()
        {
            var state = new FakeGameState();
            Func<CpuSalesFacts, bool> counts = cpu => Recent(state, new FakeCpuSource().Add(CompanyKey.Player, cpu));

            Assert.True(counts(new CpuSalesFacts(true, true, false, 0, 100f, 0f, 1995)), "a fresh CPU that has not sold yet is still new");
            Assert.False(counts(new CpuSalesFacts(false, true, false, 10, 100f, 0f, 1995)), "not sold on the market");
            Assert.False(counts(new CpuSalesFacts(true, false, false, 10, 100f, 0f, 1995)), "not released");
            Assert.False(counts(new CpuSalesFacts(true, true, true, 10, 100f, 0f, 1995)), "retired");
            Assert.False(counts(new CpuSalesFacts(true, true, false, 10, 100f, 0f, 0)), "a CPU without a year is not recent");
        }

        [Test]
        public void HasRecentCpu_AnyCpuMayQualify_AndItDoesNotAskForTheCost()
        {
            var state = new FakeGameState();
            var cpus = new FakeCpuSource()
                .Add(CompanyKey.Player, OnSale(1f, 1f, year: 1980))
                .Add(CompanyKey.Player, OnSale(1f, 1f, year: 1995));

            Assert.True(Recent(state, cpus));
            Assert.False(cpus.LastNeedUnitCost, "the cost of making a CPU is a calculation the predicate does not need");
        }

        [Test]
        public void HasRecentCpu_AnAiCompanyIsJudgedByItsOwnCpus()
        {
            var state = new FakeGameState();
            state.Ai(3);
            var cpus = new FakeCpuSource().Add("ai:3", OnSale(1f, 1f, year: 1995));

            Assert.True(Recent(state, cpus, subject: "ai:3"));
            Assert.False(Recent(state, cpus), "the player has none");
        }

        [Test]
        public void HasRecentCpu_NoDate_NoCpuData_NoContext_AreFalse()
        {
            var state = new FakeGameState();
            var cpus = new FakeCpuSource().Add(CompanyKey.Player, OnSale(1f, 1f, year: 1995));

            Assert.False(ShockPredicateLogic.HasRecentCpu(Node("ms.hasRecentCpu"), state.Context(default(DateTime)), cpus), "the date is unknown");
            Assert.False(ShockPredicateLogic.HasRecentCpu(Node("ms.hasRecentCpu"), null, cpus));
            Assert.False(ShockPredicateLogic.HasRecentCpu(Node("ms.hasRecentCpu"), state.Context(In1995), null));
            Assert.False(Recent(state, new FakeCpuSource { Unavailable = true }), "no CPU provider");
        }

        [Test]
        public void HasRecentCpu_BadParameters_MakeItFalse()
        {
            var state = new FakeGameState();
            var cpus = new FakeCpuSource().Add(CompanyKey.Player, OnSale(1f, 1f, year: 1995));

            Assert.False(Recent(state, cpus, ",'maxAgeYears':-1"));
            Assert.False(Recent(state, cpus, ",'maxAgeYears':1.5"));
            Assert.False(Recent(state, cpus, ",'maxAgeYears':'two'"));
            Assert.False(Recent(state, cpus, ",'maxAgeYears':100"));
        }

        [Test]
        public void HasRecentCpu_NoParameters_MeansOneYear()
        {
            var state = new FakeGameState();
            var cpus = new FakeCpuSource().Add(CompanyKey.Player, OnSale(1f, 1f, year: 1994));

            Assert.True(ShockPredicateLogic.HasRecentCpu(null, state.Context(In1995), cpus));
        }

        // ---- the content check of the parameters ----

        [Test]
        public void Validate_ReportsTheBadParametersOfTheTwoPredicates()
        {
            var errors = new List<string>();

            Assert.True(ShockPredicateLogic.Validate(Node("ms.sellsBelowCost"), errors));
            Assert.True(ShockPredicateLogic.Validate(Node("ms.sellsBelowCost", ",'priceRatio':0.9,'minShare':0.1"), errors));
            Assert.True(ShockPredicateLogic.Validate(Node("ms.hasRecentCpu", ",'maxAgeYears':2"), errors));
            Assert.Equal(0, errors.Count);

            Assert.False(ShockPredicateLogic.Validate(Node("ms.sellsBelowCost", ",'priceRatio':'x','minShare':3"), errors));
            Assert.Equal(2, errors.Count, string.Join("; ", errors));
            Assert.True(errors[0].Contains("ms.sellsBelowCost.params.priceRatio"), errors[0]);

            errors.Clear();
            Assert.False(ShockPredicateLogic.Validate(Node("ms.hasRecentCpu", ",'maxAgeYears':-3"), errors));
            Assert.Equal(1, errors.Count);

            errors.Clear();
            Assert.False(ShockPredicateLogic.Validate(Node("ms.sellsBelowCost", ",'priceRatio':0"), errors), "0 is in range of numbers but can never be undercut");
            Assert.Equal(1, errors.Count);
        }

        [Test]
        public void Validate_OtherNodes_AreNotItsBusiness()
        {
            Assert.True(ShockPredicateLogic.Validate(Node("dateAtLeast", ",'year':1980"), null));
            Assert.True(ShockPredicateLogic.Validate(null, null));
            Assert.True(ShockPredicateLogic.Validate(new JsonObject(), null));
        }

        // ---- the names (FR-12 item 4) ----

        private static void WithCoreTable(Action body)
        {
            string text = SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Content", "mod-framework", "core.json"));
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "core.json is not valid JSON: " + error);
            JsonObject prefixes = ((JsonObject)node).GetObject("prefixes");
            Assert.NotNull(prefixes, "core.json lacks 'prefixes'");

            try
            {
                ModIds.Load(prefixes);
                body();
            }
            finally
            {
                ModIds.Clear();
            }
        }

        [Test]
        public void Names_CarryThePrefixOfTheFeature_AndPassTheConvention()
        {
            Assert.Equal("ms.sellsBelowCost", ShockPredicateLogic.SellsBelowCostName);
            Assert.Equal("ms.hasRecentCpu", ShockPredicateLogic.HasRecentCpuName);

            WithCoreTable(() =>
            {
                Assert.True(ModIds.IsPredicateNameAllowed("market-shocks", ShockPredicateLogic.SellsBelowCostName));
                Assert.True(ModIds.IsPredicateNameAllowed("market-shocks", ShockPredicateLogic.HasRecentCpuName));
                Assert.False(ModIds.IsPredicateNameAllowed("event-system", ShockPredicateLogic.SellsBelowCostName), "the prefix belongs to market-shocks alone");
                Assert.False(ModIds.IsPredicateNameAllowed("market-shocks", "sellsBelowCost"), "a name without the prefix is refused");
                Assert.False(ModIds.IsPredicateNameAllowed("market-shocks", "es.sellsBelowCost"));
            });
        }
    }
}
