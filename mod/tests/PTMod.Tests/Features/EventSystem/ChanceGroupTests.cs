using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The chance of effects and the exclusive groups (FR-5 items 4-5; design.md "Шанс и эксклюзивные группы"): a roll per effect at <c>Resolve</c>, one number
    /// per effect whatever the data (D5), a group decided by one number over the running sums - exactly one member when the chances add up to 1 (AC-5.3), none
    /// when the number is past a total below 1. The result is kept hidden in the resolved effect until the decision, and a miss is not applied.
    /// </summary>
    [Covers(typeof(ChanceRoller))]
    public class ChanceGroupTests
    {
        [Test]
        public void AnEffectWithoutAGroup_HitsWhenTheNumberIsBelowItsChance()
        {
            bool[] hits = ChanceRoller.Roll(new[] { 0.45f, 0.45f, 1f, 0f }, null, new[] { 0.44f, 0.45f, 0.999f, 0f });
            Assert.True(hits[0]);
            Assert.False(hits[1], "u < chance, not <=");
            Assert.True(hits[2], "chance 1 always hits");
            Assert.False(hits[3], "chance 0 never hits");
        }

        [Test]
        public void AGroup_TakesTheNumberOfItsFirstMember_AndTheFirstRunningSumAboveIt()
        {
            string[] groups = { "verdict", "verdict" };
            Assert.Equal("True,False", Join(ChanceRoller.Roll(new[] { 0.6f, 0.4f }, groups, new[] { 0.59f, 0.01f })));
            Assert.Equal("False,True", Join(ChanceRoller.Roll(new[] { 0.6f, 0.4f }, groups, new[] { 0.6f, 0.99f })), "the second number is not used");
            Assert.Equal("False,True", Join(ChanceRoller.Roll(new[] { 0.6f, 0.4f }, groups, new[] { 0.999f, 0f })));

            // Σ = 0.5: a number past it hits nobody.
            Assert.Equal("False,False", Join(ChanceRoller.Roll(new[] { 0.2f, 0.3f }, groups, new[] { 0.7f, 0f })));
            // Two groups and a free effect in one list.
            Assert.Equal("True,False,True,False,True", Join(ChanceRoller.Roll(new[] { 0.5f, 0.5f, 1f, 0.3f, 0.7f },
                new[] { "a", "a", null, "b", "b" }, new[] { 0.1f, 0.9f, 0.5f, 0.35f, 0.0f })));
        }

        [Test]
        public void AC_5_3_ChancesOf0_6And0_4InTheGroupVerdict_GiveExactlyOneHit_EveryTime()
        {
            EventEffect a = Fx.Effect("FireEvent", "{ 'eventId': 'es.patent_win', 'delayDays': [60, 120], 'chance': 0.6, 'exclusiveGroup': 'verdict' }");
            EventEffect b = Fx.Effect("FireEvent", "{ 'eventId': 'es.patent_loss', 'delayDays': [60, 120], 'chance': 0.4, 'exclusiveGroup': 'verdict' }");
            int firstWins = 0;
            const int Runs = 4000;
            for (uint seed = 1; seed <= Runs; seed++)
            {
                bool[] hits = ChanceRoller.Roll(new[] { a, b }, new CountingRng(seed));
                Assert.True(hits[0] ^ hits[1], "exactly one of the group hits (seed " + seed + ")");
                if (hits[0]) firstWins++;
            }
            Assert.InRange(firstWins / (double)Runs, 0.57, 0.63, "the first by its share 0.6");
        }

        [Test]
        public void IndependentChances_HitByTheirShare()
        {
            EventEffect effect = Fx.Effect("Reputation", "{ 'delta': -3, 'chance': 0.45 }");
            int hits = 0;
            const int Runs = 4000;
            for (uint seed = 1; seed <= Runs; seed++)
            {
                if (ChanceRoller.Roll(new[] { effect }, new CountingRng(seed))[0]) hits++;
            }
            Assert.InRange(hits / (double)Runs, 0.42, 0.48);
        }

        [Test]
        public void ExactlyOneNumberIsTakenPerEffect_WhateverTheChancesAndGroups()
        {
            var rng = new CountingRng(5);
            ChanceRoller.Roll(new[]
            {
                Fx.Effect("Money"), Fx.Effect("Money", "{ 'chance': 0.5, 'exclusiveGroup': 'g' }"), Fx.Effect("Money", "{ 'chance': 0.5, 'exclusiveGroup': 'g' }"),
                Fx.Effect("Money", "{ 'chance': 0 }"),
            }, rng);
            Assert.Equal(4, rng.Draws);
            Assert.Equal(0, ChanceRoller.Roll(new EventEffect[0], rng).Length);
            Assert.Equal(4, rng.Draws, "an empty list takes nothing");

            Assert.Equal(1f, ChanceRoller.ChanceOf(Fx.Effect("Money")), "chance 1 by default");
            Assert.Equal(0f, ChanceRoller.ChanceOf(Fx.Effect("Money", "{ 'chance': -2 }")));
            Assert.Equal(1f, ChanceRoller.ChanceOf(Fx.Effect("Money", "{ 'chance': 7 }")));
            Assert.Null(ChanceRoller.GroupOf(Fx.Effect("Money", "{ 'exclusiveGroup': '' }")));
            Assert.Equal("g", ChanceRoller.GroupOf(Fx.Effect("Money", "{ 'exclusiveGroup': 'g' }")));
        }

        [Test]
        public void TheRoll_IsKeptHiddenInTheEffect_UntilTheDecision_AndAMissIsNotApplied()
        {
            // es.tax_audit "lawyers": a spending, and with chance 0.45 a second one (FR-5 item 4).
            EventDefinition definition = Fx.Event("es.tax_audit", new[]
            {
                Fx.Choice("cooperate", Fx.Effect("Money", "{ 'direction': 'Spend', 'min': 20000, 'max': 20000 }")),
                Fx.Choice("lawyers",
                    Fx.Effect("Money", "{ 'direction': 'Spend', 'min': 10000, 'max': 10000 }"),
                    Fx.Effect("Money", "{ 'direction': 'Spend', 'min': 40000, 'max': 40000, 'chance': 0.45 }")),
            });

            int hits = 0, misses = 0;
            for (uint worldSeed = 1; worldSeed <= 60 && (hits == 0 || misses == 0); worldSeed++)
            {
                var rig = new EffectsRig(new FakeCatalog(null, definition), null, worldSeed);
                rig.World.Today = 7000;
                rig.Fire("es.tax_audit", CompanyKey.Player);
                EventInstanceDto instance = rig.Instance("es.tax_audit");
                AppliedEffect maybe = instance.options[1].effects[1];
                Assert.Equal(0.45f, maybe.chance);
                Assert.Equal(AppliedEffectStates.Pending, maybe.state, "the outcome waits for the decision");
                Assert.Equal(-40000f, maybe.value, "the number is resolved whether it hits or not");

                rig.Runtime.ResolveChoice(instance.id, "lawyers", false, 7001);
                if (maybe.hit)
                {
                    hits++;
                    Assert.Equal(AppliedEffectStates.Done, maybe.state);
                    Assert.Equal(2, rig.Game.Money.Count);
                }
                else
                {
                    misses++;
                    Assert.Equal(AppliedEffectStates.Skipped, maybe.state);
                    Assert.Equal(1, rig.Game.Money.Count, "a miss is not applied");
                }
            }
            Assert.True(hits > 0 && misses > 0, "both outcomes were seen");
        }

        private static string Join(bool[] hits)
        {
            var parts = new string[hits.Length];
            for (int i = 0; i < hits.Length; i++) parts[i] = hits[i] ? "True" : "False";
            return string.Join(",", parts);
        }
    }
}
