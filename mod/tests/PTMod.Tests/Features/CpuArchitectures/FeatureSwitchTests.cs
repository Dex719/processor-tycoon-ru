using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// Switching the feature off in the middle of a game and on again (FR-9 items 2 and 3, AC-9.1, AC-9.2; TSK-018), the state side without the game: the
    /// research of the player goes on pause with all it has (progress, support levels, the families), the lab goes back to the technology it displaced,
    /// nothing is deleted or made anew, the pause does not lift by itself, the CPUs made while the feature was off get the default family, and the knob
    /// "strength of the rating effect" at 0 % makes every multiplier 1. What needs the game (the patches coming out, the providers being skipped, the
    /// money not being spent) is in <c>PatchGuardsTests</c>, which checks the wiring in the source.
    /// </summary>
    [Covers(typeof(LabSwitch), typeof(DisableOutcome))]
    public class FeatureSwitchTests
    {
        /// <summary>The player of AC-9.1: x86 on the way (43 %), ARM researched with the support on "high", "DDR" interrupted by the architecture.</summary>
        private static ArchRegistry RegistryWithAResearch(out CompanyArchState player, out CompanyArchState ai)
        {
            ArchRegistry registry = ArchTestKit.BuiltInRegistry();

            player = registry.GetOrCreate(3, true);
            player.Active = "arm";
            player.CurrentResearch = "x86";
            player.ResumeTechnologyId = 5;
            player.Families["proprietary"] = new FamilyState("proprietary") { Researched = true, Progress = 1f, Support = 31f };
            player.Families["arm"] = new FamilyState("arm") { Researched = true, Progress = 1f, ResearchedYear = 1988, ResearchedMonth = 4, Support = 88.5f, Tier = SupportTier.High };
            player.Families["x86"] = new FamilyState("x86") { Progress = 0.43f };

            ai = registry.GetOrCreate(17, false);
            ai.Active = "x86";
            ai.CurrentResearch = "risc";
            ai.Families["proprietary"] = new FamilyState("proprietary") { Researched = true, Progress = 1f };
            ai.Families["x86"] = new FamilyState("x86") { Researched = true, Progress = 1f, Support = 60f, Tier = SupportTier.Normal };
            ai.Families["risc"] = new FamilyState("risc") { Progress = 0.1f };

            registry.Bind(1043, "x86");
            registry.MarkSeeded();
            return registry;
        }

        private static string Written(ArchRegistry registry)
        {
            return ModJson.Write(new ArchSaveSection(registry).Write(), false);
        }

        // ---- AC-9.1 and AC-9.2 ----

        [Test]
        public void Disable_TheRunningResearchOfThePlayerGoesOnPause_AC92()
        {
            CompanyArchState player, ai;
            RegistryWithAResearch(out player, out ai);

            DisableOutcome outcome = LabSwitch.Disable(player, false);

            Assert.True(outcome.Paused);
            Assert.True(player.Paused);
            Assert.Equal(PauseReason.FeatureDisabled, player.PauseReason);
            Assert.Equal("x86", player.CurrentResearch, "the research stays the lab's research, only on pause");
            Assert.Near(0.43, player.GetFamily("x86").Progress, 1e-6, "x86 on pause with its progress");
            Assert.True(outcome.GiveBack, "'DDR' is the current technology again (the lab hands it back)");
            Assert.Equal(5, player.ResumeTechnologyId, "until the lab has given it back the id stays; LabArbiter.Release forgets it");
        }

        [Test]
        public void DisableAndEnable_ThreeMonths_NothingOfTheStateChanges_AC91()
        {
            CompanyArchState player, ai;
            ArchRegistry registry = RegistryWithAResearch(out player, out ai);
            string before = Written(registry);

            LabSwitch.Disable(player, false);
            LabSwitch.Disable(ai, false);

            // The feature is off for three months: no hook of the feature changes anything. What the player's research has is what it had.
            Assert.Near(0.43, player.GetFamily("x86").Progress, 1e-6, "progress");
            Assert.Equal(SupportTier.High, player.GetFamily("arm").Tier, "the support level of ARM is 'высокий'");
            Assert.Near(88.5, player.GetFamily("arm").Support, 1e-4, "and the support itself");
            Assert.Equal(3, player.Families.Count);

            // Written to the save in this state and read back (the game is saved and loaded while the feature is off).
            string off = Written(registry);
            var copy = ArchTestKit.BuiltInRegistry();
            new ArchSaveSection(copy).Read(ArchTestKit.ParseObject(off), ArchSaveSection.Version);
            Assert.Equal(off, Written(copy), "the save of a switched-off feature round-trips");

            // Switched on again: the research waits for the button, nothing starts by itself.
            Assert.False(LabRules.AutoResumes(player.PauseReason));
            Assert.True(player.Paused);

            // The only differences to the state before are the pause and its reason; the research, the progress and the levels are the same.
            ArchRegistry restored = ArchTestKit.BuiltInRegistry();
            new ArchSaveSection(restored).Read(ArchTestKit.ParseObject(before), ArchSaveSection.Version);
            CompanyArchState playerBefore = restored.GetOrNull(3);
            Assert.Equal(playerBefore.CurrentResearch, player.CurrentResearch);
            Assert.Equal(playerBefore.Active, player.Active);
            foreach (KeyValuePair<string, FamilyState> pair in playerBefore.Families)
            {
                FamilyState now = player.GetFamily(pair.Key);
                Assert.NotNull(now, pair.Key);
                Assert.Equal(pair.Value.Researched, now.Researched, pair.Key);
                Assert.Equal(pair.Value.Progress, now.Progress, pair.Key);
                Assert.Equal(pair.Value.Support, now.Support, pair.Key);
                Assert.Equal(pair.Value.Tier, now.Tier, pair.Key);
            }
        }

        [Test]
        public void Disable_TheRegistryAndItsBindingsAreNotRemadeOrEmptied()
        {
            CompanyArchState player, ai;
            ArchRegistry registry = RegistryWithAResearch(out player, out ai);

            LabSwitch.Disable(player, false);

            Assert.True(registry.Seeded, "a registry that was sown is not reset");
            Assert.Equal(2, registry.Companies.Count);
            Assert.True(ReferenceEquals(player, registry.GetOrNull(3)), "the same state object, not a new one");
            Assert.Equal("x86", registry.GetCpuArch(1043), "the CPUs keep their families");
        }

        // ---- who is touched ----

        [Test]
        public void Disable_OnlyThePlayerIsTouched_AnAiCompanyKeepsItsState()
        {
            CompanyArchState player, ai;
            RegistryWithAResearch(out player, out ai);

            DisableOutcome outcome = LabSwitch.Disable(ai, false);

            Assert.False(outcome.Paused);
            Assert.False(outcome.GiveBack);
            Assert.False(ai.Paused, "the AI's research is not paused (the arbiter settles what it did meanwhile when the feature is on again)");
            Assert.Equal("risc", ai.CurrentResearch);
            Assert.Equal(PauseReason.None, ai.PauseReason);
        }

        [Test]
        public void Disable_AnEmptyStateOrNothingResearched_ChangesNothing()
        {
            Assert.False(LabSwitch.Disable(null, false).Paused);

            var idle = new CompanyArchState(3, true);
            DisableOutcome outcome = LabSwitch.Disable(idle, false);
            Assert.False(outcome.Paused, "no architecture in the lab: no pause");
            Assert.False(outcome.GiveBack, "and nothing displaced");
            Assert.False(idle.Paused);
        }

        // ---- the lab and the pause reasons ----

        [Test]
        public void Disable_ThePlayerChoseATechnologyHimself_ItStaysInTheLab()
        {
            var player = new CompanyArchState(3, true)
            {
                CurrentResearch = "x86",
                Paused = true,
                PauseReason = PauseReason.TechnologyChosen,
                ResumeTechnologyId = 5,
            };

            DisableOutcome outcome = LabSwitch.Disable(player, true);   // "L1 64 KB" is in the slot

            Assert.False(outcome.GiveBack, "the lab is not taken from the technology the player chose");
            Assert.False(outcome.Paused, "it was on pause already");
            Assert.Equal(PauseReason.TechnologyChosen, player.PauseReason, "a pause that waits for the player anyway keeps its reason");
        }

        [Test]
        public void Disable_APauseForTheDefault_WouldLiftByItself_SoItsReasonBecomesTheSwitch()
        {
            var player = new CompanyArchState(3, true) { CurrentResearch = "x86", Paused = true, PauseReason = PauseReason.Default };
            Assert.True(LabRules.AutoResumes(player.PauseReason), "before: it would resume as soon as the default is over");

            DisableOutcome outcome = LabSwitch.Disable(player, false);

            Assert.True(outcome.Paused);
            Assert.Equal(PauseReason.FeatureDisabled, player.PauseReason);
            Assert.False(LabRules.AutoResumes(player.PauseReason), "FR-9 item 3: it waits for the button after the switch-on");
        }

        [Test]
        public void Disable_NoFundsPauseKeepsItsReason_AndASecondSwitchOffIsHarmless()
        {
            var player = new CompanyArchState(3, true) { CurrentResearch = "x86", Paused = true, PauseReason = PauseReason.NoFunds };
            Assert.False(LabSwitch.Disable(player, false).Paused);
            Assert.Equal(PauseReason.NoFunds, player.PauseReason);

            var running = new CompanyArchState(3, true) { CurrentResearch = "x86" };
            Assert.True(LabSwitch.Disable(running, false).Paused);
            DisableOutcome again = LabSwitch.Disable(running, false);
            Assert.False(again.Paused, "already on pause");
            Assert.Equal(PauseReason.FeatureDisabled, running.PauseReason);
        }

        [Test]
        public void Disable_NothingDisplaced_NothingToGiveBack()
        {
            var player = new CompanyArchState(3, true) { CurrentResearch = "x86" };
            Assert.False(LabSwitch.Disable(player, false).GiveBack, "the lab was free when the architecture started");
        }

        // ---- the CPUs made while the feature was off ----

        [Test]
        public void ACpuMadeWhileTheFeatureWasOff_GetsTheDefaultFamilyOfItsOwnerAndYear()
        {
            ArchRegistry registry = ArchTestKit.BuiltInRegistry();
            registry.MarkSeeded();

            // The binder did not run (its patches and handlers are out while the feature is off): CPU 2001 has no binding. Asked later, it gets the
            // default of its owner and year (FR-5 item 3): the player's CPU of 2001 is an x86, Intel's CPU of 2008 an x86-64.
            Assert.False(registry.TryGetCpuArch(2001, out string unbound));
            Assert.Equal("x86", registry.DefaultFamilyFor(true, -1, 2001), "the player's CPU of 2001");
            Assert.Equal("x86_64", registry.DefaultFamilyFor(false, 0, 2008), "Intel, 2008");
            Assert.Equal("x86", registry.ResolveOrBind(2001, registry.DefaultFamilyFor(true, -1, 2001)));
            Assert.Equal("x86", registry.GetCpuArch(2001), "kept once asked: the same answer every time");
        }

        // ---- the knob of the hub ----

        [Test]
        public void TheKnobAtZero_EveryMultiplierOfTheRatingIsOne()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            BalanceConfig balance = ArchTestKit.BuiltInBalance();
            foreach (ArchFamily family in catalog.Families)
            {
                foreach (int year in new[] { 1975, 1985, 1990, 1996, 2005, 2020 })
                {
                    float[] values = RatingForecast.All(family, year, balance, 50f, -1f, 1, false, 0f);
                    for (int segment = 0; segment < values.Length; segment++)
                    {
                        Assert.Near(1.0, values[segment], 1e-6, family.Id + " " + year + " segment " + segment + " at 0 %");
                    }
                }
            }
        }

        [Test]
        public void TheKnobIsTheDeviationFromOne_ScaledByK()
        {
            ArchCatalog catalog = ArchTestKit.BuiltInCatalog();
            BalanceConfig balance = ArchTestKit.BuiltInBalance();
            ArchFamily x86 = catalog.Get("x86");

            float full = RatingForecast.All(x86, 1980, balance, 50f, -1f, 1, false, 1f)[0];
            float half = RatingForecast.All(x86, 1980, balance, 50f, -1f, 1, false, 0.5f)[0];
            float over = RatingForecast.All(x86, 1980, balance, 50f, -1f, 1, false, 1.5f)[0];

            Assert.Near(1.0 + (full - 1.0) * 0.5, half, 1e-5, "m' = 1 + (m - 1) * k, k = 0.5");
            Assert.Near(1.0 + (full - 1.0) * 1.5, over, 1e-5, "k = 1.5 widens the deviation");
            Assert.True(full > 1f, "x86 gains in the high-end segment of 1980 (the example is not a degenerate one)");
        }
    }
}
