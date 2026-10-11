using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// How an AI company decides (FR-4 item 6, FR-6 item 7, TSK-016/020): the chain of choosers by priority (AC-6.4: with two choosers the first non-null answer wins and
    /// nobody after it is asked), then the default policy - <c>aiWeight</c> among the choices the company can afford - then <c>defaultChoice</c>, always on the day of the
    /// firing (AC-4.3), always with one number of the generator for the default policy.
    /// </summary>
    [Covers(typeof(DefaultAiChooser), typeof(AiDecision), typeof(AiChooserChain), typeof(PriorityList<>))]
    public class AiChooserTests
    {
        private sealed class Chooser
        {
            public string Name;
            public Func<string> Answer = () => null;
            public bool Handles = true;
            public int Asked;
        }

        private static EventDefinition Event(params EventChoice[] choices)
        {
            return ChoiceEvents.Make("es.x", choices);
        }

        private static EventChoice Choice(string id, float aiWeight = 1f, float aiMinCashMultiple = 0f)
        {
            EventChoice choice = Fx.Choice(id);
            choice.aiWeight = aiWeight;
            choice.aiMinCashMultiple = aiMinCashMultiple;
            return choice;
        }

        private static ChoiceOption Option(string id, bool available = true, float cost = 0f, float weight = 1f)
        {
            return new ChoiceOption { ChoiceId = id, Available = available, Cost = cost, AiWeight = weight };
        }

        private static string Decide(IList<Chooser> chain, EventDefinition definition, IList<ChoiceOption> options, float money, ScriptedRng rng, List<string> faults = null)
        {
            return AiDecision.Decide(chain, chooser => chooser.Handles, chooser =>
                {
                    chooser.Asked++;
                    return chooser.Answer();
                },
                definition, options, money, () => rng, (chooser, error) =>
                {
                    if (faults != null) faults.Add(chooser.Name + ": " + error.Message);
                });
        }

        // ---- AC-6.4: the chain ----

        [Test]
        public void AC_6_4_TheFirstNonNullAnswerWins_AndNobodyAfterItIsAsked()
        {
            EventDefinition definition = Event(Choice("settle"), Choice("fight"));
            ChoiceOption[] options = { Option("settle"), Option("fight") };

            var chain = new PriorityList<Chooser>();
            var high = new Chooser { Name = "priority 100", Answer = () => null };   // not its event: null
            var low = new Chooser { Name = "priority 10", Answer = () => "settle" };
            chain.Add(low, 10);
            chain.Add(high, 100);

            Assert.Equal("settle", Decide(chain.Items, definition, options, 1e9f, new ScriptedRng(0.9f)), "the first chooser says null, the second decides");
            Assert.Equal(1, high.Asked);
            Assert.Equal(1, low.Asked);

            high.Answer = () => "fight";
            low.Asked = 0;
            Assert.Equal("fight", Decide(chain.Items, definition, options, 1e9f, new ScriptedRng(0.9f)), "the chooser of priority 100 decides");
            Assert.Equal(0, low.Asked, "the second chooser is not called");
        }

        [Test]
        public void AChooserThatThrows_IsCountedAgainstItself_AndTheNextOneDecides()
        {
            EventDefinition definition = Event(Choice("a"), Choice("b"));
            ChoiceOption[] options = { Option("a"), Option("b") };
            var broken = new Chooser { Name = "broken", Answer = () => { throw new InvalidOperationException("boom"); } };
            var good = new Chooser { Name = "good", Answer = () => "b" };
            var faults = new List<string>();

            Assert.Equal("b", Decide(new[] { broken, good }, definition, options, 0f, new ScriptedRng(0.1f), faults));
            Assert.Equal(1, faults.Count);
            Assert.Equal("broken: boom", faults[0]);
        }

        [Test]
        public void AnAnswerThatNamesAnUnknownOrUnavailableChoice_IsAsGoodAsNull()
        {
            EventDefinition definition = Event(Choice("a"), Choice("b"), Choice("c"));
            ChoiceOption[] options = { Option("a"), Option("b", available: false), Option("c") };
            var unknown = new Chooser { Answer = () => "zzz" };
            var unavailable = new Chooser { Answer = () => "b" };
            var third = new Chooser { Answer = () => "c" };

            Assert.Equal("c", Decide(new[] { unknown, unavailable, third }, definition, options, 0f, new ScriptedRng(0.1f)));
            Assert.Equal(1, unknown.Asked);
            Assert.Equal(1, unavailable.Asked);
        }

        [Test]
        public void AChooserThatDoesNotHandleTheEvent_IsSkippedWithoutBeingAsked()
        {
            EventDefinition definition = Event(Choice("a"), Choice("b"));
            ChoiceOption[] options = { Option("a"), Option("b") };
            var foreign = new Chooser { Handles = false, Answer = () => "b" };
            var own = new Chooser { Answer = () => "a" };

            Assert.Equal("a", Decide(new[] { foreign, own }, definition, options, 0f, new ScriptedRng(0.9f)));
            Assert.Equal(0, foreign.Asked);
        }

        [Test]
        public void WhenNobodyAnswers_TheDefaultPolicyDecides_AndAnEmptyChainIsFine()
        {
            EventDefinition definition = Event(Choice("a", 1f), Choice("b", 1f));
            ChoiceOption[] options = { Option("a"), Option("b") };

            Assert.Equal("b", Decide(new Chooser[0], definition, options, 0f, new ScriptedRng(0.9f)), "u = 0.9 of two equal weights");
            Assert.Equal("b", Decide(new[] { new Chooser() }, definition, options, 0f, new ScriptedRng(0.9f)), "a chooser that says null changes nothing");
            Assert.Equal("a", AiDecision.Decide<Chooser>(null, null, null, definition, options, 0f, () => new ScriptedRng(0.1f), null));
        }

        // ---- the default policy ----

        [Test]
        public void TheDefaultPolicy_PicksByAiWeight()
        {
            EventDefinition definition = Event(Choice("a", 3f), Choice("b", 1f));
            ChoiceOption[] options = { Option("a", weight: 3f), Option("b", weight: 1f) };

            Assert.Equal("a", DefaultAiChooser.Choose(definition, options, 0f, new ScriptedRng(0.10f)));
            Assert.Equal("a", DefaultAiChooser.Choose(definition, options, 0f, new ScriptedRng(0.74f)), "3 of 4 belong to a");
            Assert.Equal("b", DefaultAiChooser.Choose(definition, options, 0f, new ScriptedRng(0.80f)));
            Assert.Equal("b", DefaultAiChooser.Choose(definition, options, 0f, new ScriptedRng(0.9999f)));
        }

        [Test]
        public void TheDefaultPolicy_FollowsTheWeightsOverManyDraws()
        {
            EventDefinition definition = Event(Choice("a", 3f), Choice("b", 1f));
            ChoiceOption[] options = { Option("a", weight: 3f), Option("b", weight: 1f) };

            var rng = new CountingRng(20261011u);
            int a = 0;
            const int draws = 4000;
            for (int i = 0; i < draws; i++)
            {
                if (DefaultAiChooser.Choose(definition, options, 0f, rng) == "a") a++;
            }
            Assert.Near(0.75, (double)a / draws, 0.03, "about 3 of 4");
            Assert.Equal(draws, rng.Draws, "one number per decision");
        }

        [Test]
        public void TheDefaultPolicy_LeavesOutWhatTheCompanyCannotAfford_WhatIsNotAvailable_AndWhatHasNoWeight()
        {
            EventDefinition definition = Event(Choice("cheap", 1f), Choice("expand", 100f, aiMinCashMultiple: 3f), Choice("locked", 100f), Choice("never", 0f));
            ChoiceOption[] options =
            {
                Option("cheap", cost: 0f, weight: 1f),
                Option("expand", cost: 10000000f, weight: 100f),
                Option("locked", available: false, weight: 100f),
                Option("never", weight: 0f),
            };

            Assert.Equal("cheap", DefaultAiChooser.Choose(definition, options, 29000000f, new ScriptedRng(0.5f)), "$30M are needed for the expensive one: 3 x $10M");
            Assert.Equal("expand", DefaultAiChooser.Choose(definition, options, 30000000f, new ScriptedRng(0.5f)), "exactly 3 times the cost is enough");
            Assert.False(DefaultAiChooser.CanAfford(definition, options[1], 1000f));
            Assert.True(DefaultAiChooser.CanAfford(definition, options[0], 0f), "a free choice is always affordable");
            Assert.False(DefaultAiChooser.CanAfford(definition, null, 1e9f));
        }

        [Test]
        public void TheDefaultPolicy_FallsBackToTheDefaultChoice_WhenNothingIsEligible_OrThereAreNoOptions()
        {
            EventDefinition definition = Event(Choice("stay", 0f), Choice("go", 1f, aiMinCashMultiple: 5f));
            ChoiceOption[] options = { Option("stay", weight: 0f), Option("go", cost: 1000000f, weight: 1f) };

            Assert.Equal("stay", DefaultAiChooser.Choose(definition, options, 100f, new ScriptedRng(0.5f)), "defaultChoice: the first choice of the event");
            Assert.Equal("stay", DefaultAiChooser.Choose(definition, new ChoiceOption[0], 100f, new ScriptedRng(0.5f)));
            Assert.Equal("stay", DefaultAiChooser.Choose(definition, null, 100f, new ScriptedRng(0.5f)));
            Assert.Null(DefaultAiChooser.Choose(null, new[] { Option("stay", weight: 0f) }, 100f, new ScriptedRng(0.5f)), "no event, no default");
        }

        [Test]
        public void TheDefaultPolicy_TakesExactlyOneNumber_WhateverTheData()
        {
            EventDefinition definition = Event(Choice("a"), Choice("b", 1f, aiMinCashMultiple: 2f));
            var cases = new List<ChoiceOption[]>
            {
                new[] { Option("a"), Option("b") },
                new[] { Option("a", available: false), Option("b", available: false) },
                new[] { Option("a", weight: 0f), Option("b", cost: 5f, weight: 1f) },
                new ChoiceOption[0],
            };
            foreach (ChoiceOption[] options in cases)
            {
                var rng = new ScriptedRng(0.3f);
                DefaultAiChooser.Choose(definition, options, 0f, rng);
                Assert.Equal(1, rng.Draws, "one draw for " + options.Length + " option(s)");
            }
        }

        // ---- on the engine's rig ----

        [Test]
        public void AC_4_3_AnAiCompanyDecidesOnTheDayTheEventFires_AndTheJournalNamesTheCompanyAndTheChoice()
        {
            EventDefinition definition = ChoiceEvents.Make("es.strike", new[]
            {
                Fx.Choice("settle", Fx.Effect("Reputation", "{ 'delta': 1 }")),
                Fx.Choice("refuse", Fx.Effect("Reputation", "{ 'delta': -1 }")),
            });
            definition.choices[0].aiWeight = 0f;   // the default policy prefers "refuse"
            definition.choices[1].aiWeight = 5f;
            var rig = new ChoiceRig(definition);
            rig.SetDay(Defs.Day(1985, 6, 1));
            rig.Rig.Options.DecideForAi = instance => AiDecision.Decide(new Chooser[0], null, null, instance.Definition,
                AiDecision.OptionsOf(instance, requires => rig.World.Check(requires, instance.Data.companyKey)), rig.World.Money(instance.Data.companyKey),
                () => rig.Rig.Random.ForDecision(instance.Data.id), null);

            Assert.Equal(EventFireResult.Fired, rig.Rig.Fire("es.strike", "ai:2"));
            Assert.Null(rig.Rig.Instance("es.strike"), "decided at once: nothing waits");
            EventLogEntry entry = rig.Rig.Entries(EventLogKinds.AutoResolved)[0];
            Assert.Equal("ai:2", entry.companyKey);
            Assert.Equal("refuse", entry.choiceId);
            Assert.Equal(-1f, rig.Rig.Game.Reputation["ai:2"]);
            Assert.Equal(0, rig.Mail.Letters.Count, "an AI company gets no letter and no window");
            Assert.False(rig.Window.IsOpen);
        }

        [Test]
        public void TheDecisionOfAnAiCompany_IsTheSameInTheSameWorld_AndDiffersByInstance()
        {
            EventDefinition definition = ChoiceEvents.Make("es.coin", new[] { Fx.Choice("heads"), Fx.Choice("tails") });
            var picks = new List<string>();
            for (int run = 0; run < 2; run++)
            {
                var rig = new ChoiceRig(definition);
                rig.SetDay(Defs.Day(1985, 6, 1));
                rig.Rig.Options.DecideForAi = instance => AiDecision.Decide(new Chooser[0], null, null, instance.Definition,
                    AiDecision.OptionsOf(instance, requires => true), 0f, () => rig.Rig.Random.ForDecision(instance.Data.id), null);
                for (int i = 0; i < 12; i++)
                {
                    rig.Rig.Fire("es.coin", CompanyKey.ForAi(i % 3));
                    rig.Rig.RunDays(Defs.Day(1985, 6, 2) + i, Defs.Day(1985, 6, 2) + i);
                }
                picks.Add(string.Join(",", rig.Rig.Entries(EventLogKinds.AutoResolved).ConvertAll(e => e.choiceId).ToArray()));
            }

            Assert.Equal(picks[0], picks[1], "the same world seed, the same decisions");
            Assert.True(picks[0].Contains("heads") && picks[0].Contains("tails"), "twelve instances do not all pick alike: " + picks[0]);
        }

        [Test]
        public void TheChoicesOfAnInstance_AreShownToAChooserWithAvailabilityCostAndWeight()
        {
            EventChoice locked = Fx.Choice("locked");
            locked.requires = ChoiceEvents.Flag("never", () => false);
            locked.aiWeight = 7f;
            EventDefinition definition = ChoiceEvents.Make("es.menu", new[] { Fx.Choice("free"), ChoiceEvents.Costing("paid", 2500000f), locked });
            var rig = new ChoiceRig(definition);
            rig.SetDay(Defs.Day(1985, 6, 1));
            rig.Mail.Refuse = true;
            rig.Rig.Fire("es.menu", CompanyKey.Player);

            var instance = new EventInstance(rig.Rig.Instance("es.menu"), definition);
            ChoiceOption[] options = AiDecision.OptionsOf(instance, requires => rig.World.Check(requires, CompanyKey.Player));
            Assert.Equal(3, options.Length);
            Assert.True(options[0].Available);
            Assert.Near(2500000, options[1].Cost, 1);
            Assert.False(options[2].Available, "requires is false");
            Assert.Equal(7f, options[2].AiWeight);
            Assert.Equal(0, AiDecision.OptionsOf(null, null).Length);
        }
    }
}
