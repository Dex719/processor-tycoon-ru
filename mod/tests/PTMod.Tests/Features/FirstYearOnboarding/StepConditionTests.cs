using System;
using System.Linq;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// The conditions of the shipped steps (TSK-004, FR-4) run against a world (<see cref="IOnboardingWorld"/>) that parses them with the real parser of the
    /// core: each step becomes done or shown exactly when the state of the game in the design table says so. The world here is a stand-in made of the fake game
    /// state of the core and the fake facts; the real one (<c>GameOnboardingWorld</c>) differs only in where the numbers come from.
    /// </summary>
    [Covers(typeof(IOnboardingWorld))]
    public class StepConditionTests
    {
        private sealed class Scene
        {
            public readonly ShippedContext Context = ShippedContext.Load();
            public readonly ScriptedWorld World;
            public readonly StepCatalog Catalog;

            public Scene()
            {
                World = new ScriptedWorld(Context);
                Catalog = Context.BuildCatalog();
                Assert.Equal(0, Catalog.Errors.Count, string.Join("; ", Catalog.Errors));
            }

            public StepDefinition Step(string id)
            {
                StepDefinition step = Catalog.Find(id);
                Assert.NotNull(step, id);
                return step;
            }

            /// <summary>The step is done now (its completeWhen is true; an information step has none).</summary>
            public bool Done(string id)
            {
                StepDefinition step = Step(id);
                Assert.NotNull(step.CompleteWhen, id + " has no completeWhen");
                return World.Evaluate(step.CompleteWhen);
            }

            /// <summary>The step may become current now (no showWhen, or it is true).</summary>
            public bool Shown(string id)
            {
                StepDefinition step = Step(id);
                return step.ShowWhen == null || World.Evaluate(step.ShowWhen);
            }

            public void NoParseTroubles()
            {
                Assert.Equal(0, World.ParseErrors.Count, string.Join("; ", World.ParseErrors));
            }
        }

        [Test]
        public void Research_IsDoneWhenAResearchRuns_AC_4_1()
        {
            var scene = new Scene();

            Assert.False(scene.Done("research"));
            scene.World.State.Player.ResearchActive = true;
            Assert.True(scene.Done("research"));
            scene.NoParseTroubles();
        }

        [Test]
        public void DesignCpu_IsDoneByAProjectOrByAReleasedCpu_AndDevWaitShowsOnTheSame()
        {
            var scene = new Scene();
            Assert.False(scene.Done("design_cpu"));
            Assert.False(scene.Shown("dev_wait"), "nothing to wait for before the project");

            scene.World.Facts.CpuProject = true;
            Assert.True(scene.Done("design_cpu"), "a project in progress");
            Assert.True(scene.Shown("dev_wait"));

            scene.World.Facts.CpuProject = false;
            scene.World.State.Player.CpuCount = 1;
            Assert.True(scene.Done("design_cpu"), "the player was quick: the CPU is out already (AC-4.2)");
            Assert.True(scene.Shown("dev_wait"));
            scene.NoParseTroubles();
        }

        [Test]
        public void ReleasePrice_IsDoneByTheFirstReleasedCpu()
        {
            var scene = new Scene();

            scene.World.Facts.CpuProject = true;
            Assert.False(scene.Done("release_price"), "a project is not a release");
            scene.World.State.Player.CpuCount = 1;
            Assert.True(scene.Done("release_price"));
            scene.NoParseTroubles();
        }

        [Test]
        public void ProductionAndFirstSales_FollowTheFactsAndOneIsNotTheOther()
        {
            var scene = new Scene();

            Assert.False(scene.Done("production"));
            Assert.False(scene.Done("first_sales"));
            scene.World.Facts.Produced = true;
            Assert.True(scene.Done("production"));
            Assert.False(scene.Done("first_sales"));
            scene.World.Facts.Sold = true;
            Assert.True(scene.Done("first_sales"));
            scene.NoParseTroubles();
        }

        [Test]
        public void OemOffer_ShowsWhenOrdersExist_AndIsDoneByAnOfferOrByAutomation()
        {
            var scene = new Scene();
            Assert.False(scene.Shown("oem_offer"));
            Assert.False(scene.Done("oem_offer"));

            scene.World.Facts.Oem = true;
            Assert.True(scene.Shown("oem_offer"));
            Assert.False(scene.Done("oem_offer"), "orders alone do not finish the step");

            scene.World.State.Player.OemOfferSent = true;
            Assert.True(scene.Done("oem_offer"), "an offer was sent");

            scene.World.State.Player.OemOfferSent = false;
            scene.World.Facts.AutomateOn = true;
            Assert.True(scene.Done("oem_offer"), "automation sends the offers");
            scene.NoParseTroubles();
        }

        [Test]
        public void Finance_ShowsOnTheFirstOfAMonth_OnceResearchIsClosed()
        {
            var scene = new Scene();
            var first = new DateTime(1975, 4, 1);
            var second = new DateTime(1975, 4, 2);

            scene.World.Today = first;
            Assert.False(scene.Shown("finance"), "research is not done yet");

            scene.World.Facts.Done.Add("research");
            Assert.True(scene.Shown("finance"));
            scene.World.Today = second;
            Assert.False(scene.Shown("finance"), "only on the 1st");
            scene.World.Today = new DateTime(1975, 5, 1);
            Assert.True(scene.Shown("finance"), "every month has a 1st");
            scene.NoParseTroubles();
        }

        [Test]
        public void FoundryTrack_FollowsItsFacts()
        {
            var scene = new Scene();

            Assert.False(scene.Done("foundry_services"));
            scene.World.Facts.Foundry = true;
            Assert.True(scene.Done("foundry_services"), "services already on at the start are caught up (E6)");

            Assert.False(scene.Done("first_client"));
            scene.World.State.Player.BusinessContracts = 1;
            Assert.True(scene.Done("first_client"));

            Assert.False(scene.Done("factory_expansion"));
            scene.World.Facts.FactoryProject = true;
            Assert.True(scene.Done("factory_expansion"));
            scene.NoParseTroubles();
        }

        [Test]
        public void Welcome_AndGraduation_HaveNoConditionsAtAll()
        {
            var scene = new Scene();

            foreach (string id in new[] { "welcome", "graduation" })
            {
                Assert.Null(scene.Step(id).ShowWhen, id);
                Assert.Null(scene.Step(id).CompleteWhen, id);
                Assert.True(scene.Step(id).CompleteOnShow, id);
                Assert.True(scene.Shown(id), id);
            }
        }

        [Test]
        public void TipDebt_IsTrueBelowZero_AC_7_1()
        {
            var scene = new Scene();

            scene.World.State.Player.Money = 1000;
            Assert.False(scene.Shown("tip_debt"));
            scene.World.State.Player.Money = 0;
            Assert.False(scene.Shown("tip_debt"), "zero is not debt");
            scene.World.State.Player.Money = -1;
            Assert.True(scene.Shown("tip_debt"));
            scene.NoParseTroubles();
        }

        [Test]
        public void TipDefault_IsTrueInDefault_AndItsVariantFollowsBankruptcyRecovery()
        {
            var scene = new Scene();
            StepDefinition tip = scene.Step("tip_default");

            Assert.False(scene.Shown("tip_default"));
            scene.World.State.Player.InDefault = true;
            Assert.True(scene.Shown("tip_default"));

            BodyVariant variant = tip.Mail.Variants.Single();
            Assert.False(scene.World.Evaluate(variant.When), "bankruptcy-recovery is not installed");
            scene.World.State.SetFeatureActive("bankruptcy-recovery", true);
            Assert.True(scene.World.Evaluate(variant.When), "the text that points at the rescue menu");
            scene.NoParseTroubles();
        }

        [Test]
        public void TipContractSigned_NeedsAnActiveContract()
        {
            var scene = new Scene();

            Assert.False(scene.Shown("tip_contract_signed"));
            scene.World.State.Player.ActiveOemContracts = 1;
            Assert.True(scene.Shown("tip_contract_signed"));
            scene.NoParseTroubles();
        }

        [Test]
        public void TipMissedSales_AndTipOldCpu_FollowTheirFacts()
        {
            var scene = new Scene();

            Assert.False(scene.Shown("tip_missed_sales"));
            Assert.False(scene.Shown("tip_old_cpu"));
            scene.World.Facts.Missed = true;
            Assert.True(scene.Shown("tip_missed_sales"));
            Assert.False(scene.Shown("tip_old_cpu"));
            scene.World.Facts.Old = true;
            Assert.True(scene.Shown("tip_old_cpu"));
            scene.NoParseTroubles();
        }

        [Test]
        public void TipYearSummary_ComesOnJanuary1_AfterThirtyDaysOfPlay()
        {
            var scene = new Scene();   // the game started on 1975-01-01

            scene.World.Today = new DateTime(1975, 1, 1);
            Assert.False(scene.Shown("tip_year_summary"), "the first January 1 is the day the game starts");

            scene.World.Today = new DateTime(1976, 1, 1);
            Assert.True(scene.Shown("tip_year_summary"));

            scene.World.Today = new DateTime(1976, 1, 2);
            Assert.False(scene.Shown("tip_year_summary"), "only on January 1");

            scene.World.Today = new DateTime(1976, 7, 1);
            Assert.False(scene.Shown("tip_year_summary"));
            scene.NoParseTroubles();
        }

        [Test]
        public void TheParsedConditionIsKeptInTheReference()
        {
            var scene = new Scene();
            ConditionRef condition = scene.Step("research").CompleteWhen;

            Assert.Null(condition.Compiled);
            scene.World.Evaluate(condition);
            object compiled = condition.Compiled;
            Assert.NotNull(compiled, "parsed on the first evaluation");

            scene.World.State.Player.ResearchActive = true;
            Assert.True(scene.World.Evaluate(condition));
            Assert.True(ReferenceEquals(compiled, condition.Compiled), "and not parsed again");
        }

        [Test]
        public void AConditionThatCannotBeParsed_IsFalseAndNotParsedAgain()
        {
            var scene = new Scene();
            ConditionRef broken = OnbJson.Cond("{'type':'researchActve'}");

            Assert.False(scene.World.Evaluate(broken));
            Assert.True(broken.CompileFailed);
            Assert.Equal(1, scene.World.ParseErrors.Count);
            Assert.False(scene.World.Evaluate(broken));
            Assert.Equal(1, scene.World.ParseErrors.Count, "the second time it is false at once");
            Assert.False(scene.World.Evaluate(null));
            Assert.False(scene.World.Evaluate(new ConditionRef()));
        }

        [Test]
        public void TheWorld_AnswersTheContractOfTheInterface()
        {
            var scene = new Scene();
            IOnboardingWorld world = scene.World;

            Assert.False(world.IsBlocked);
            Assert.False(world.IsBatchRun);
            Assert.Equal(new DateTime(1975, 1, 2), world.GameDate);
            Assert.Equal("idm", world.DetectTrack());
            Assert.False(world.IsMailDelivered(100000));
            scene.World.Delivered.Add(100000);
            Assert.True(world.IsMailDelivered(100000));
        }
    }
}
