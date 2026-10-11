using System.Collections.Generic;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Rng;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The targets of events (FR-3; design.md "Выбор цели"): the weights and their formulas (AC-3.1), the kinds of targets with the same rules for the player
    /// and the AI, the filter and the conditions per candidate, <c>Specific</c> without a company, and the group of <c>AllCompanies</c> with its global effects
    /// applied once (AC-3.2, D8).
    /// </summary>
    [Covers(typeof(TargetSelector), typeof(TargetQuery), typeof(FiringPlan), typeof(PlannedInstance), typeof(WeightedPick), typeof(EffectScope), typeof(IEventWorld))]
    public class TargetSelectorTests
    {
        private static EventDefinition Target(EventTargetType type, TargetWeighting weighting = TargetWeighting.Uniform)
        {
            EventDefinition definition = Defs.Manual("es.t", type);
            definition.targets.weighting = weighting;
            return definition;
        }

        private static Dictionary<string, int> Count(TargetSelector selector, EventDefinition definition, int draws, uint seed)
        {
            var rng = new Xoshiro128(seed);
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < draws; i++)
            {
                FiringPlan plan = selector.Select(definition, rng.Value(), null);
                Assert.NotNull(plan);
                string key = plan.Targets[0];
                int n;
                counts.TryGetValue(key, out n);
                counts[key] = n + 1;
            }
            return counts;
        }

        [Test]
        public void AC_3_1_FactoryCapacity_ThePlayerWith30Of150Lines_IsTheTargetIn20Percent()
        {
            var world = new FakeWorld()
                .Add(CompanyKey.Player, lines: 30)
                .Add("ai:0", lines: 50)
                .Add("ai:1", lines: 40)
                .Add("ai:2", lines: 30)
                .Add("ai:3", lines: 0);   // fabless: not a candidate
            var selector = new TargetSelector(world);

            Dictionary<string, int> counts = Count(selector, Target(EventTargetType.RandomCompany, TargetWeighting.FactoryCapacity), 10000, 31337u);
            double share = counts[CompanyKey.Player] / 10000.0;
            Assert.InRange(share, 0.18, 0.22, "30 of 150 lines: 20 % ± 2 %, got " + share);
            Assert.False(counts.ContainsKey("ai:3"), "a company without lines is never a FactoryCapacity target");
        }

        [Test]
        public void Weights_FollowTheFormulasOfTheDesign()
        {
            Assert.Equal(1f, TargetSelector.WeightOf(TargetWeighting.Uniform, 0, 0f, 0f));
            Assert.Equal(30f, TargetSelector.WeightOf(TargetWeighting.FactoryCapacity, 30, 0f, 0f));
            Assert.Equal(0f, TargetSelector.WeightOf(TargetWeighting.FactoryCapacity, 0, 0.5f, 1e6f), "0 lines: not a candidate");
            Assert.Near(0.26, TargetSelector.WeightOf(TargetWeighting.MarketShare, 0, 0.25f, 0f), 1e-6, "share + 0.01");
            Assert.Near(0.01, TargetSelector.WeightOf(TargetWeighting.MarketShare, 0, 0f, 0f), 1e-6, "no sales still leaves a chance");
            Assert.Equal(1000001f, TargetSelector.WeightOf(TargetWeighting.QuarterlyRevenue, 0, 0f, 1000000f), "max(QR, 0) + 1");
            Assert.Equal(1f, TargetSelector.WeightOf(TargetWeighting.QuarterlyRevenue, 0, 0f, -5000f), "a negative revenue counts as 0");
            Assert.Equal(1f, TargetSelector.WeightOf(TargetWeighting.QuarterlyRevenue, 0, 0f, float.NaN));
        }

        [Test]
        public void MarketShareAndRevenueWeights_PickInProportion()
        {
            var world = new FakeWorld().Add(CompanyKey.Player, share: 0.49f).Add("ai:0", share: 0.24f).Add("ai:1", share: 0.24f);
            Dictionary<string, int> byShare = Count(new TargetSelector(world), Target(EventTargetType.RandomCompany, TargetWeighting.MarketShare), 10000, 7u);
            Assert.InRange(byShare[CompanyKey.Player] / 10000.0, 0.48, 0.52, "0.50 / 1.00");

            var rich = new FakeWorld().Add(CompanyKey.Player, revenue: 2999f).Add("ai:0", revenue: 999f);
            Dictionary<string, int> byRevenue = Count(new TargetSelector(rich), Target(EventTargetType.RandomCompany, TargetWeighting.QuarterlyRevenue), 10000, 9u);
            Assert.InRange(byRevenue[CompanyKey.Player] / 10000.0, 0.73, 0.77, "3000 / 4000");
        }

        [Test]
        public void TheKindsOfTargets_SameRulesForThePlayerAndTheAi()
        {
            FakeWorld world = FakeWorld.WithCompanies(3);
            var selector = new TargetSelector(world);
            var keys = new List<string>();
            var weights = new List<float>();

            selector.Candidates(Target(EventTargetType.Player), null, keys, weights);
            Assert.Equal("player", string.Join(",", keys));

            selector.Candidates(Target(EventTargetType.RandomAI), null, keys, weights);
            Assert.Equal("ai:0,ai:1,ai:2", string.Join(",", keys), "RandomAI: the AI companies only");

            selector.Candidates(Target(EventTargetType.RandomCompany), null, keys, weights);
            Assert.Equal("player,ai:0,ai:1,ai:2", string.Join(",", keys), "RandomCompany: the player too, with the same weight");
            Assert.Equal("1,1,1,1", string.Join(",", weights));

            world.Bankrupt.Add("ai:1");
            selector.Candidates(Target(EventTargetType.RandomAI), null, keys, weights);
            Assert.Equal("ai:0,ai:2", string.Join(",", keys), "a bankrupt company is never a target");

            world.Bankrupt.Add(CompanyKey.Player);
            Assert.Null(selector.Select(Target(EventTargetType.Player), 0.5f, null), "no target, no firing (FR-3 item 6)");
        }

        [Test]
        public void Specific_FindsItsCompany_OrNothing()
        {
            FakeWorld world = FakeWorld.WithCompanies(2);
            var selector = new TargetSelector(world);
            EventDefinition specific = Target(EventTargetType.Specific);

            specific.targets.companyKey = "ai:1";
            Assert.Equal("ai:1", selector.Select(specific, 0.9f, null).Targets[0]);

            specific.targets.companyKey = "ai:5";
            Assert.Null(selector.Select(specific, 0.9f, null), "no such company");

            specific.targets.companyKey = "ai:1";
            world.Bankrupt.Add("ai:1");
            Assert.Null(selector.Select(specific, 0.9f, null), "bankrupt");

            specific.targets.companyKey = "nonsense";
            Assert.Null(selector.Select(specific, 0.9f, null), "not a key");
        }

        [Test]
        public void FilterAndConditions_AreCheckedForEveryCandidate_WithTheCandidateAsTheSubject()
        {
            FakeWorld world = FakeWorld.WithCompanies(3);
            var selector = new TargetSelector(world);
            EventDefinition definition = Target(EventTargetType.RandomCompany);
            definition.targets.filter = Defs.When((key, day) => key != "ai:0");
            definition.conditions = Defs.When((key, day) => key != CompanyKey.Player);
            var keys = new List<string>();
            var weights = new List<float>();

            selector.Candidates(definition, null, keys, weights);
            Assert.Equal("ai:1,ai:2", string.Join(",", keys));

            selector.Candidates(definition, new TargetQuery { IgnoreConditions = true }, keys, weights);
            Assert.Equal("player,ai:1,ai:2", string.Join(",", keys), "IgnoreConditions skips the conditions, the filter still says who can be a target");

            selector.Candidates(definition, new TargetQuery { Allowed = key => key == "ai:2" }, keys, weights);
            Assert.Equal("ai:2", string.Join(",", keys), "the caller's pacing");

            selector.Candidates(definition, new TargetQuery { CompanyKey = "ai:0", IgnoreConditions = true }, keys, weights);
            Assert.Equal("ai:0", string.Join(",", keys), "a named company with IgnoreConditions: the caller decided");

            selector.Candidates(definition, new TargetQuery { CompanyKey = "ai:0" }, keys, weights);
            Assert.Equal(0, keys.Count, "a named company still has to pass the filter otherwise");
        }

        [Test]
        public void AllCompanies_IsOneGroupOfEveryFittingCompany()
        {
            FakeWorld world = FakeWorld.WithCompanies(4);
            EventDefinition all = Target(EventTargetType.AllCompanies);
            all.targets.filter = Defs.When((key, day) => key != "ai:3");

            FiringPlan plan = new TargetSelector(world).Select(all, 0.3f, null);
            Assert.True(plan.IsGroup);
            Assert.Equal("player,ai:0,ai:1,ai:2", string.Join(",", plan.Targets));
            Assert.True(plan.IncludesPlayer);

            FiringPlan one = new TargetSelector(world).Select(all, 0.3f, new TargetQuery { CompanyKey = "ai:1" });
            Assert.False(one.IsGroup, "a named company is one instance");
            Assert.Equal("ai:1", one.Targets[0]);
        }

        [Test]
        public void AC_3_2_AllCompaniesWithAGlobalDemandEffect_OneContribution_TenChoices()
        {
            var world = new FakeWorld();
            world.Add(CompanyKey.Player);
            for (int i = 0; i < 9; i++) world.Add(CompanyKey.ForAi(i));

            EventDefinition shock = Target(EventTargetType.AllCompanies).WithChoices();
            shock.effects = new[]
            {
                Effect("MarketDemand", "{ 'segment': 'all', 'multiplier': 0.8, 'scope': 'Global' }", 90),
                Effect("Reputation", "{ 'delta': -2 }", 0),
            };

            FiringPlan plan = new TargetSelector(world).Select(shock, 0.5f, null);
            IList<PlannedInstance> instances = plan.Expand();

            Assert.Equal(10, instances.Count, "an instance for each of the 10 companies");
            int choices = 0, globals = 0, targetEffects = 0;
            foreach (PlannedInstance instance in instances)
            {
                if (instance.HasChoices) choices++;
                globals += instance.GlobalEffects.Count;
                targetEffects += instance.TargetEffects.Count;
            }
            Assert.Equal(10, choices, "a choice at every company");
            Assert.Equal(1, globals, "the global demand effect once for the whole group: one contribution to the channel");
            Assert.Equal(1, instances[0].GlobalEffects.Count, "carried by the first instance");
            Assert.Equal(10, targetEffects, "a target-scope effect on every company");
            Assert.Equal(1, instances[1].Index);
        }

        [Test]
        public void EffectScope_ReadsTheCommonParameter()
        {
            Assert.True(EffectScope.IsGlobal(Effect("MarketDemand", "{ 'scope': 'Global' }", 0)));
            Assert.True(EffectScope.IsGlobal(Effect("MarketDemand", "{ 'scope': 'global' }", 0)), "any case");
            Assert.False(EffectScope.IsGlobal(Effect("Money", "{ 'scope': 'Target' }", 0)));
            Assert.False(EffectScope.IsGlobal(Effect("Money", "{ }", 0)), "Target by default");
            Assert.False(EffectScope.IsGlobal(new EventEffect { type = "Money" }), "no params at all");
            Assert.False(EffectScope.IsGlobal(null));
        }

        [Test]
        public void WeightedPick_UsesTheGivenNumber()
        {
            var weights = new List<float> { 1f, 0f, 3f, float.NaN, -2f };
            Assert.Equal(0, WeightedPick.Index(weights, 0f));
            Assert.Equal(0, WeightedPick.Index(weights, 0.2499f));
            Assert.Equal(2, WeightedPick.Index(weights, 0.25f));
            Assert.Equal(2, WeightedPick.Index(weights, 0.9999999f));
            Assert.Equal(2, WeightedPick.Index(weights, 1f), "1 is treated as the end of the range");
            Assert.Equal(0, WeightedPick.Index(weights, float.NaN));
            Assert.Equal(-1, WeightedPick.Index(new List<float> { 0f, -1f }, 0.5f), "no usable weight");
            Assert.Equal(-1, WeightedPick.Index(null, 0.5f));
        }

        [Test]
        public void FiringPlan_NeedsADefinitionAndATarget()
        {
            Assert.Throws<System.ArgumentNullException>(() => new FiringPlan(null, new[] { "player" }, false));
            Assert.Throws<System.ArgumentException>(() => new FiringPlan(Target(EventTargetType.Player), new string[0], false));

            var plan = new FiringPlan(Target(EventTargetType.Player), new[] { "player" }, false);
            Assert.Equal(0, plan.Args.Length, "never null");
            Assert.False(plan.HasChoices);
        }

        private static EventEffect Effect(string type, string paramsJson, int durationDays)
        {
            return new EventEffect { type = type, @params = new EffectParams(EventTestKit.Obj(paramsJson)), durationDays = durationDays };
        }
    }
}
