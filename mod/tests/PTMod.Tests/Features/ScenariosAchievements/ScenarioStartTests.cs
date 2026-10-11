using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-019, TSK-020, TSK-021: from the choice in the new-game window to the run - what the player chose (<see cref="ScenarioSelection"/>), what the
    /// start writes into the game's data and how it is checked afterwards (<see cref="StartParameters"/>, AC-4.1, AC-4.2, FR-4 items 4 and 5), the
    /// pending run that crosses the change of scene, and the run that is born of it (<see cref="RunStart"/>, FR-15 item 1).
    /// </summary>
    [Covers(typeof(PendingRun), typeof(StartParameters), typeof(StartFields), typeof(RunStart), typeof(RunPlan), typeof(ScenarioSelection))]
    public class ScenarioStartTests
    {
        private static readonly string[] Features = { "event-system", "scenarios-achievements" };

        private static ChallengeDefinition FindChallenge(string id)
        {
            return ScenarioTestData.StandardChallenges().FirstOrDefault(c => c.Id == id);
        }

        // ---- the start of a scenario ----

        [Test]
        public void TheStart_IsWrittenAsInTheJson_ForEveryStandardScenario()
        {
            var player = new StartFields { Year = 1975, CompanyType = 0, Difficulty = 2, StartingTechnology = 0, Funds = 1, FactorySize = 5, Cheats = true };
            foreach (ScenarioDefinition scenario in ScenarioTestData.StandardScenarios())
            {
                StartFields fields = StartParameters.Apply(player, scenario.Start);
                Assert.Equal(scenario.Start.Year, fields.Year, scenario.Id);
                Assert.Equal(scenario.Start.DifficultyLevel, fields.Difficulty, scenario.Id);
                Assert.Equal(scenario.Start.StartingTechnology, fields.StartingTechnology, scenario.Id);
                Assert.Equal(scenario.Start.StartingFunds, fields.Funds, scenario.Id);
                Assert.Equal(scenario.Start.FactorySize, fields.FactorySize, scenario.Id);
                Assert.Equal(ContentVocabulary.CompanyTypeCode(scenario.Start.CompanyType), fields.CompanyType, scenario.Id);
                Assert.False(fields.Cheats, scenario.Id + ": a scenario that does not allow cheats switches them off (FR-3 item 5)");
            }
        }

        [Test]
        public void TheComebackScenario_StartsWithTheDebt_AC41()
        {
            StartFields fields = StartParameters.Apply(default(StartFields), ScenarioTestData.Scenario("comeback-1985").Start);
            Assert.Equal(-20000000f, fields.Funds);
            Assert.Equal(30, fields.FactorySize);
            Assert.Equal(1985, fields.Year);
            Assert.Equal(0, fields.CompanyType, "idm");
        }

        [Test]
        public void TheFablessScenario_StartsAsFabless_WithNoLines_AC42()
        {
            StartFields fields = StartParameters.Apply(default(StartFields), ScenarioTestData.Scenario("fabless-1985").Start);
            Assert.Equal(1, fields.CompanyType, "CPU, Fabless");
            Assert.Equal(0, fields.FactorySize);
            Assert.Equal(125000000f, fields.Funds);
        }

        [Test]
        public void TheFoundryScenario_StartsAsFoundry()
        {
            Assert.Equal(2, StartParameters.Apply(default(StartFields), ScenarioTestData.Scenario("foundry-1990").Start).CompanyType);
        }

        [Test]
        public void Cheats_StayAsThePlayerLeftThem_OnlyWhenTheScenarioAllowsThem()
        {
            var start = new ScenarioStart { Year = 1980, CompanyType = "idm", AllowCheats = true };
            Assert.True(StartParameters.Apply(new StartFields { Cheats = true }, start).Cheats);
            Assert.False(StartParameters.Apply(new StartFields { Cheats = false }, start).Cheats, "allowing is not forcing");

            start.AllowCheats = false;
            Assert.False(StartParameters.Apply(new StartFields { Cheats = true }, start).Cheats);
        }

        [Test]
        public void AScenarioWithoutAStart_OrWithAnUnknownCompanyType_CannotBeApplied_SoTheStartFallsBack()
        {
            Assert.Throws<ArgumentNullException>(() => StartParameters.Apply(default(StartFields), null));
            Assert.Throws<ArgumentException>(() => StartParameters.Apply(default(StartFields), new ScenarioStart { Year = 1980, CompanyType = "bank" }));
        }

        // ---- the check after the start ----

        [Test]
        public void WhenTheGameSetsTheMoneyAndTheLinesAsTheScenarioSays_NothingIsReported()
        {
            foreach (ScenarioDefinition scenario in ScenarioTestData.StandardScenarios())
            {
                Assert.Null(StartParameters.Check(scenario.Start, scenario.Start.StartingFunds, scenario.Start.FactorySize), scenario.Id);
            }
        }

        [Test]
        public void AMismatch_IsDescribedForTheLog()
        {
            ScenarioStart start = ScenarioTestData.Scenario("garage-1975").Start;
            string money = StartParameters.Check(start, 500000, start.FactorySize);
            Assert.True(money.Contains("money") && money.Contains("500000") && money.Contains("2500000") && !money.Contains("lines"), money);

            string lines = StartParameters.Check(start, start.StartingFunds, 5);
            Assert.True(lines.Contains("lines 5 instead of 10") && !lines.Contains("money"), lines);

            string both = StartParameters.Check(start, 0, 0);
            Assert.True(both.Contains("money") && both.Contains("lines"), both);
        }

        [Test]
        public void HalfAUnitOfMoney_IsNoMismatch_ButANaNIs()
        {
            ScenarioStart start = ScenarioTestData.Scenario("garage-1975").Start;
            Assert.Null(StartParameters.Check(start, start.StartingFunds + 0.4, start.FactorySize));
            Assert.NotNull(StartParameters.Check(start, start.StartingFunds + 1, start.FactorySize));
            Assert.NotNull(StartParameters.Check(start, double.NaN, start.FactorySize));
            Assert.Throws<ArgumentNullException>(() => StartParameters.Check(null, 0, 0));
        }

        [Test]
        public void TheWindowShowsTheScenario_WhenTheYearTheTypeAndTheDifficultyAreTheScenarios()
        {
            ScenarioStart start = ScenarioTestData.Scenario("mhz-race-1990").Start;
            var shown = new StartFields { Year = 1990, CompanyType = 0, Difficulty = 3, Funds = 1, FactorySize = 7, Cheats = true };
            Assert.True(StartParameters.Matches(start, shown), "the money, the lines and the cheats are not compared: the start overwrites them");

            shown.Year = 1985;
            Assert.False(StartParameters.Matches(start, shown));
            shown.Year = 1990;
            shown.CompanyType = 1;
            Assert.False(StartParameters.Matches(start, shown));
            shown.CompanyType = 0;
            shown.Difficulty = 2;
            Assert.False(StartParameters.Matches(start, shown));
            Assert.Throws<ArgumentNullException>(() => StartParameters.Matches(null, shown));
        }

        // ---- the pending run ----

        [Test]
        public void ThePendingRun_OfAScenario_CarriesTheSnapshotTheHashAndTheChallenges()
        {
            ScenarioDefinition scenario = ScenarioTestData.Scenario("garage-1975");
            scenario.ContentHash = "9b1e0f3c";
            scenario.IsCustom = true;
            PendingRun pending = PendingRun.For(scenario, new[] { "taxman", "no-debt", "taxman", "", null });

            Assert.Equal("garage-1975", pending.ScenarioId);
            Assert.Equal("9b1e0f3c", pending.ContentHash);
            Assert.True(pending.IsCustom);
            Assert.True(pending.HasScenario);
            Assert.True(ReferenceEquals(scenario, pending.Scenario));
            Assert.Equal(new[] { "taxman", "no-debt" }, pending.Challenges, "no repeats, no empty ids");
            Assert.False(pending.StartFailed);
        }

        [Test]
        public void FreePlay_MayHaveChallenges_AndNoScenario()
        {
            PendingRun pending = PendingRun.Free(new[] { "expensive-research" });
            Assert.False(pending.HasScenario);
            Assert.Null(pending.ScenarioId);
            Assert.Equal(new[] { "expensive-research" }, pending.Challenges);
            Assert.Equal(0, PendingRun.Free().Challenges.Length);
        }

        [Test]
        public void AFailedStart_IsPlainFreePlay_WithTheFlagThatTellsThePlayer()
        {
            PendingRun pending = PendingRun.Failed();
            Assert.True(pending.StartFailed);
            Assert.False(pending.HasScenario);
            Assert.Equal(0, pending.Challenges.Length, "vanilla: no challenges either");
            Assert.Throws<ArgumentNullException>(() => PendingRun.For(null));
        }

        // ---- the run that is born ----

        [Test]
        public void ARunOfAScenario_StartsActive_WithTheSnapshot_TheChallenges_AndTheFeaturesThatWereOn()
        {
            ScenarioDefinition scenario = ScenarioTestData.Scenario("fabless-1985");
            PendingRun pending = PendingRun.For(scenario, new[] { "taxman", "no-debt" });
            var skipped = new List<string>();

            RunPlan plan = RunStart.Create("run-9", D(1985, 1, 1), 2, false, pending, Features, FindChallenge, skipped);

            RunState run = plan.Run;
            Assert.Equal("run-9", run.RunId);
            Assert.Equal(RunOrigin.NewGame, run.Origin);
            Assert.Equal(D(1985, 1, 1), run.StartDate);
            Assert.Equal(2, run.DifficultyAtStart);
            Assert.True(ReferenceEquals(scenario, run.Scenario));
            Assert.Equal(ScenarioStatus.Active, run.ScenarioState.Status);
            Assert.Equal(new[] { "taxman", "no-debt" }, run.Challenges.ToArray());
            Assert.Equal(new[] { "taxman", "no-debt" }, plan.Challenges.Select(c => c.Id).ToArray());
            Assert.Equal(Features, run.FeaturesAtStart.ToArray());
            Assert.False(run.Assisted);
            Assert.False(run.Custom);
            Assert.Equal(0, skipped.Count);
        }

        [Test]
        public void ACustomScenario_MakesACustomRun()
        {
            ScenarioDefinition scenario = ScenarioTestData.Scenario("garage-1975");
            scenario.IsCustom = true;
            RunPlan plan = RunStart.Create("run-9", D(1975, 1, 1), 2, false, PendingRun.For(scenario), Features, FindChallenge);
            Assert.True(plan.Run.Custom);
        }

        [Test]
        public void AnUnknownChallenge_IsLeftOut_AndReported()
        {
            var skipped = new List<string>();
            RunPlan plan = RunStart.Create("run-9", D(1980, 1, 1), 2, false, PendingRun.Free(new[] { "taxman", "from-the-future" }), Features, FindChallenge, skipped);
            Assert.Equal(new[] { "taxman" }, plan.Run.Challenges.ToArray());
            Assert.Equal(new[] { "from-the-future" }, skipped.ToArray());
        }

        [Test]
        public void Cheats_MakeTheRunAssisted_FromTheFirstDay_FR151()
        {
            RunPlan with = RunStart.Create("run-9", D(1980, 1, 1), 2, true, null, Features, FindChallenge);
            Assert.True(with.Run.Assisted);
            Assert.Equal(new[] { AssistedRegistry.ReasonCheats }, with.Run.AssistedReasons.ToArray());

            RunPlan without = RunStart.Create("run-9", D(1980, 1, 1), 2, false, null, Features, FindChallenge);
            Assert.False(without.Run.Assisted);
        }

        [Test]
        public void NoPendingRun_IsFreePlayWithNoChallenges()
        {
            RunPlan plan = RunStart.Create("run-9", D(1980, 1, 1), 3, false, null, Features, FindChallenge);
            Assert.Null(plan.Run.Scenario);
            Assert.Null(plan.Run.ScenarioState);
            Assert.Equal(0, plan.Run.Challenges.Count);
            Assert.Equal(0, plan.Challenges.Count);
            Assert.Equal(3, plan.Run.DifficultyAtStart);
            Assert.False(plan.Run.IsScenarioActive);
        }

        [Test]
        public void ARunNeedsAnId()
        {
            Assert.Throws<ArgumentException>(() => RunStart.Create("", D(1980, 1, 1), 2, false, null, Features, FindChallenge));
            Assert.Throws<ArgumentException>(() => RunStart.Create(null, D(1980, 1, 1), 2, false, null, Features, FindChallenge));
        }

        // ---- the choice in the window ----

        [Test]
        public void TheChoice_StartsAsFreePlay_AndRaisesChangedOnlyForRealChanges()
        {
            var selection = new ScenarioSelection();
            int changes = 0;
            selection.Changed += () => changes++;
            Assert.True(selection.IsFreePlay);
            Assert.Equal(0, selection.ChallengeCount);

            Assert.True(selection.SelectScenario("garage-1975"));
            Assert.False(selection.SelectScenario("garage-1975"), "the same choice");
            Assert.False(selection.IsFreePlay);
            Assert.Equal("garage-1975", selection.ScenarioId);

            Assert.True(selection.SetChallenge("taxman", true));
            Assert.False(selection.SetChallenge("taxman", true));
            Assert.True(selection.IsChallengeSelected("taxman"));
            Assert.False(selection.SetChallenge("no-debt", false), "dropping what is not chosen");
            Assert.False(selection.SetChallenge("", true));
            Assert.False(selection.SetChallenge(null, true));
            Assert.Equal(1, selection.ChallengeCount);

            Assert.True(selection.SelectScenario(null), "back to free play");
            Assert.True(selection.IsFreePlay);
            Assert.Equal(3, changes);
        }

        [Test]
        public void AnEmptyScenarioId_IsFreePlay()
        {
            var selection = new ScenarioSelection();
            selection.SelectScenario("garage-1975");
            Assert.True(selection.SelectScenario(""));
            Assert.True(selection.IsFreePlay);
        }

        [Test]
        public void TheBonusOfTheChosenChallenges_Adds_AC33()
        {
            var selection = new ScenarioSelection();
            Assert.Equal(0, selection.BonusPercent(FindChallenge));
            selection.SetChallenge("expensive-research", true);
            selection.SetChallenge("taxman", true);
            Assert.Equal(25, selection.BonusPercent(FindChallenge), "15% + 10%");

            selection.SetChallenge("unknown", true);
            Assert.Equal(25, selection.BonusPercent(FindChallenge), "a challenge nobody knows adds nothing");
        }

        [Test]
        public void AChallengeThatTheNewScenarioForbids_IsTakenOff()
        {
            var selection = new ScenarioSelection();
            selection.SetChallenge("tight-credit", true);
            selection.SetChallenge("expensive-research", true);
            selection.SetChallenge("no-oem", true);
            int changes = 0;
            selection.Changed += () => changes++;

            // garage-1975 allows expensive-research, fickle-market, taxman, no-debt only
            ScenarioDefinition garage = ScenarioTestData.Scenario("garage-1975");
            IList<string> removed = selection.Revalidate(FindChallenge, "idm", garage, true);

            Assert.Equal(new[] { "tight-credit", "no-oem" }, removed.ToArray());
            Assert.Equal(new[] { "expensive-research" }, selection.Challenges.ToArray());
            Assert.Equal(1, changes);
            Assert.Equal(0, selection.Revalidate(FindChallenge, "idm", garage, true).Count, "nothing more to take off");
            Assert.Equal(1, changes, "and nothing is announced");
        }

        [Test]
        public void AChallengeThatTheCompanyTypeForbids_AndTheCreditThatIsNotThere_AreTakenOff()
        {
            var selection = new ScenarioSelection();
            selection.SetChallenge("fabless-purist", true);   // fabless only
            selection.SetChallenge("tight-credit", true);     // needs the per-company threshold
            selection.SetChallenge("taxman", true);

            IList<string> removed = selection.Revalidate(FindChallenge, "idm", null, false);
            Assert.Equal(new[] { "fabless-purist", "tight-credit" }, removed.ToArray());
            Assert.Equal(new[] { "taxman" }, selection.Challenges.ToArray());
        }

        [Test]
        public void AChallengeTheCatalogDoesNotKnow_IsTakenOff()
        {
            var selection = new ScenarioSelection();
            selection.SetChallenge("from-the-future", true);
            Assert.Equal(1, selection.Revalidate(FindChallenge, "idm", null, true).Count);
            Assert.Equal(0, selection.ChallengeCount);
            Assert.Equal(0, new ScenarioSelection().Revalidate(null, "idm", null, true).Count);
        }

        [Test]
        public void StartPressed_TheChoiceBecomesThePendingRun()
        {
            var selection = new ScenarioSelection();
            selection.SelectScenario("mhz-race-1990");
            selection.SetChallenge("strong-rivals", true);

            PendingRun pending = selection.ToPending(id => ScenarioTestData.StandardScenarios().FirstOrDefault(s => s.Id == id));
            Assert.Equal("mhz-race-1990", pending.ScenarioId);
            Assert.True(pending.HasScenario);
            Assert.Equal(new[] { "strong-rivals" }, pending.Challenges);
        }

        [Test]
        public void AScenarioTheCatalogNoLongerHas_IsFreePlayWithTheChallenges()
        {
            var selection = new ScenarioSelection();
            selection.SelectScenario("vanished");
            selection.SetChallenge("taxman", true);
            PendingRun pending = selection.ToPending(id => null);
            Assert.False(pending.HasScenario);
            Assert.Equal(new[] { "taxman" }, pending.Challenges);

            Assert.False(new ScenarioSelection().ToPending(null).HasScenario);
        }

        [Test]
        public void Clear_GoesBackToFreePlay_AndTellsOnlyWhenSomethingWasChosen()
        {
            var selection = new ScenarioSelection();
            int changes = 0;
            selection.Changed += () => changes++;
            selection.Clear();
            Assert.Equal(0, changes);

            selection.SelectScenario("garage-1975");
            selection.SetChallenge("taxman", true);
            selection.Clear();
            Assert.True(selection.IsFreePlay);
            Assert.Equal(0, selection.ChallengeCount);
            Assert.Equal(3, changes);
        }
    }
}
