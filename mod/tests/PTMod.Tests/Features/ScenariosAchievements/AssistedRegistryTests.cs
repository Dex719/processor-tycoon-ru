using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Features.ScenariosAchievements.Logic;
using static PTMod.Tests.Features.ScenariosAchievements.RunTestData;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-013: the assisted flag of the run (FR-15). It is sticky, its reasons are short and not repeated, it is raised by the cheats, by a
    /// feature switched off during a scenario or a challenge, and by the other features through <c>Mark</c>; with <c>strictAssisted</c> an
    /// assisted run earns no achievements, otherwise it earns them marked "with help"; an assisted run makes no records. The registry is a static
    /// holder bound to the run, so every test frees it.
    /// </summary>
    [Covers(typeof(AssistedRegistry), typeof(RunState), typeof(RunOrigin), typeof(RunInfo))]
    public class AssistedRegistryTests
    {
        private static RunState FreeRun()
        {
            return RunState.StartNew("run-1", D(1985, 1, 1), 2, null, null, new[] { "event-system", "first-year-onboarding", "scenarios-achievements" });
        }

        private static RunState ScenarioRun(params string[] challenges)
        {
            return RunState.StartNew("run-1", D(1985, 1, 1), 2, ScenarioTestData.Scenario("fabless-1985"), challenges,
                new[] { "event-system", "first-year-onboarding", "scenarios-achievements" });
        }

        private static void Free()
        {
            AssistedRegistry.Reset(null);
        }

        // ---- the flag ----

        [Test]
        public void ACleanRun_IsNotAssisted()
        {
            RunState run = FreeRun();
            Assert.False(run.Assisted);
            Assert.Equal(0, run.AssistedReasons.Count);
            try
            {
                AssistedRegistry.Reset(run);
                Assert.False(AssistedRegistry.IsAssisted);
                Assert.Equal(0, AssistedRegistry.Reasons.Count);
            }
            finally
            {
                Free();
            }
        }

        [Test]
        public void Mark_RaisesTheFlag_AndNothingLowersIt()
        {
            RunState run = FreeRun();
            try
            {
                AssistedRegistry.Reset(run);
                Assert.True(AssistedRegistry.Mark("cheats.enabled"));
                Assert.True(AssistedRegistry.IsAssisted);
                Assert.True(run.Assisted);

                // however it is read, written back or restored, the flag does not come down
                run.RestoreAssisted(false, new string[0]);
                Assert.True(run.Assisted, "a restore with a cleared flag cannot lower it");
                AssistedRegistry.Mark("other");
                Assert.True(AssistedRegistry.IsAssisted);

                Assert.True(typeof(RunState).GetProperty("Assisted").GetSetMethod(true).IsPrivate, "the flag has no public setter");
                Assert.True(typeof(AssistedRegistry).GetMethod("Mark") != null && typeof(AssistedRegistry).GetMethod("Unmark") == null && typeof(AssistedRegistry).GetMethod("Clear") == null, "no way to take the mark off");
            }
            finally
            {
                Free();
            }
        }

        [Test]
        public void TheReasons_HaveNoDuplicates_AndKeepTheirOrder()
        {
            RunState run = FreeRun();
            try
            {
                AssistedRegistry.Reset(run);
                Assert.True(AssistedRegistry.Mark("cheats.enabled"));
                Assert.False(AssistedRegistry.Mark("cheats.enabled"), "the same reason is not added twice");
                Assert.True(AssistedRegistry.Mark("feature.disabled:event-system"));
                Assert.True(AssistedRegistry.Mark("some-other-feature.thing"));

                Assert.Equal(new[] { "cheats.enabled", "feature.disabled:event-system", "some-other-feature.thing" }, new List<string>(AssistedRegistry.Reasons).ToArray());
                Assert.Equal(3, run.AssistedReasons.Count);
            }
            finally
            {
                Free();
            }
        }

        [Test]
        public void AReasonIsAtMostSixtyFourLong_ALongerOneIsCutAndStillRaisesTheFlag()
        {
            RunState run = FreeRun();
            try
            {
                AssistedRegistry.Reset(run);
                Assert.True(AssistedRegistry.Mark(new string('x', 200)));
                Assert.True(run.Assisted, "a long reason is no way to escape the flag");
                Assert.Equal(RunState.MaxReasonLength, run.AssistedReasons[0].Length);
                Assert.Equal(64, RunState.MaxReasonLength);

                string exact = new string('y', 64);
                AssistedRegistry.Mark(exact);
                Assert.Equal(exact, run.AssistedReasons[1], "a reason of exactly 64 is kept whole");
                foreach (string reason in run.AssistedReasons) Assert.True(reason.Length <= 64);
            }
            finally
            {
                Free();
            }
        }

        [Test]
        public void ABlankReason_StillMarks_AsUnspecified()
        {
            RunState run = FreeRun();
            try
            {
                AssistedRegistry.Reset(run);
                Assert.True(AssistedRegistry.Mark(null));
                Assert.False(AssistedRegistry.Mark("   "), "a second blank reason is the same reason");
                Assert.True(run.Assisted);
                Assert.Equal(new[] { RunState.ReasonUnspecified }, new List<string>(run.AssistedReasons).ToArray());
                Assert.Equal("unspecified", RunState.ReasonUnspecified);
                Assert.Equal("trimmed", RunState.NormalizeReason("  trimmed  "));
            }
            finally
            {
                Free();
            }
        }

        [Test]
        public void ThereIsALimitOnTheReasons_ButTheFlagStays()
        {
            RunState run = FreeRun();
            try
            {
                AssistedRegistry.Reset(run);
                for (int i = 0; i < RunState.MaxReasons + 10; i++) AssistedRegistry.Mark("reason-" + i);
                Assert.Equal(RunState.MaxReasons, run.AssistedReasons.Count);
                Assert.True(run.Assisted);
                Assert.False(AssistedRegistry.Mark("one-more"), "nothing more to keep");
            }
            finally
            {
                Free();
            }
        }

        [Test]
        public void TheReasonsHandedOut_AreACopy()
        {
            RunState run = FreeRun();
            try
            {
                AssistedRegistry.Reset(run);
                AssistedRegistry.Mark("a");
                IList<string> copy = AssistedRegistry.Reasons;
                copy.Add("tampered");
                Assert.Equal(1, AssistedRegistry.Reasons.Count);
                Assert.Throws<NotSupportedException>(() => run.AssistedReasons.Add("tampered"));
            }
            finally
            {
                Free();
            }
        }

        // ---- the binding ----

        [Test]
        public void AMarkBeforeTheRunExists_WaitsForTheRun()
        {
            Free();
            try
            {
                Assert.True(AssistedRegistry.Mark("early.mark"), "another feature is quicker than the start of the run");
                Assert.True(AssistedRegistry.IsAssisted);
                Assert.Equal(new[] { "early.mark" }, new List<string>(AssistedRegistry.Reasons).ToArray());
                Assert.False(AssistedRegistry.Mark("early.mark"));

                RunState run = FreeRun();
                AssistedRegistry.Reset(run);
                Assert.True(run.Assisted);
                Assert.Equal(new[] { "early.mark" }, new List<string>(run.AssistedReasons).ToArray());

                RunState next = FreeRun();
                AssistedRegistry.Reset(next);
                Assert.False(next.Assisted, "the waiting marks were handed to the first run only");
                Assert.False(AssistedRegistry.IsAssisted);
            }
            finally
            {
                Free();
            }
        }

        [Test]
        public void LeavingTheRun_DropsTheWaitingMarks_AndTheBinding()
        {
            RunState run = FreeRun();
            try
            {
                AssistedRegistry.Reset(run);
                AssistedRegistry.Mark("on-the-run");
                AssistedRegistry.Reset(null);
                Assert.False(AssistedRegistry.IsAssisted, "no run, nothing assisted");
                Assert.True(run.Assisted, "the run itself keeps its flag");

                AssistedRegistry.Mark("while-in-the-menu");
                AssistedRegistry.Reset(null);
                Assert.False(AssistedRegistry.IsAssisted);
                RunState fresh = FreeRun();
                AssistedRegistry.Reset(fresh);
                Assert.False(fresh.Assisted, "a mark from the menu does not leak into the next run");
            }
            finally
            {
                Free();
            }
        }

        [Test]
        public void ALoadedAssistedRun_StaysAssistedWhenTheRegistryIsBoundToIt()
        {
            RunState run = FreeRun();
            run.RestoreAssisted(true, new[] { "cheats.enabled" });
            try
            {
                AssistedRegistry.Reset(run);
                Assert.True(AssistedRegistry.IsAssisted);
                Assert.Equal(new[] { "cheats.enabled" }, new List<string>(AssistedRegistry.Reasons).ToArray());
            }
            finally
            {
                Free();
            }
        }

        // ---- the causes ----

        [Test]
        public void StartingWithCheats_MarksTheRun_AC152()
        {
            RunState cheating = RunState.CreateLegacy("r", D(1990, 1, 1), 3, true, null);
            Assert.True(cheating.Assisted);
            Assert.Equal(new[] { "cheats.enabled" }, new List<string>(cheating.AssistedReasons).ToArray());
            Assert.Equal(AssistedRegistry.ReasonCheats, "cheats.enabled");

            RunState clean = RunState.CreateLegacy("r", D(1990, 1, 1), 3, false, null);
            Assert.False(clean.Assisted);
            Assert.Equal(RunOrigin.Legacy, clean.Origin);
            Assert.True(clean.IsLegacy);
            Assert.False(clean.HasScenario);
        }

        [Test]
        public void SwitchingAFeatureOff_DuringAScenario_Marks_AC153()
        {
            RunState run = ScenarioRun();
            Assert.Equal("feature.disabled:event-system", AssistedRegistry.ReasonForDisabledFeature(run, "event-system"));
            Assert.True(AssistedRegistry.MarkFeatureDisabled(run, "event-system"));
            Assert.True(run.Assisted);
            Assert.Equal(new[] { "feature.disabled:event-system" }, new List<string>(run.AssistedReasons).ToArray());
            Assert.Equal(AssistedRegistry.ReasonFeatureDisabledPrefix + "x", AssistedRegistry.FeatureDisabledReason("x"));

            Assert.False(AssistedRegistry.MarkFeatureDisabled(run, "event-system"), "the same reason is not added again");
        }

        [Test]
        public void SwitchingOffTheScenariosFeatureItself_Counts()
        {
            RunState run = ScenarioRun();
            Assert.Equal("feature.disabled:scenarios-achievements", AssistedRegistry.ReasonForDisabledFeature(run, "scenarios-achievements"));
        }

        [Test]
        public void AFeatureThatWasNotOnAtTheStart_IsNoBusinessOfTheRun()
        {
            RunState run = ScenarioRun();
            Assert.Null(AssistedRegistry.ReasonForDisabledFeature(run, "market-shocks"));
            Assert.False(AssistedRegistry.MarkFeatureDisabled(run, "market-shocks"));
            Assert.False(run.Assisted);
            Assert.Null(AssistedRegistry.ReasonForDisabledFeature(run, null));
            Assert.Null(AssistedRegistry.ReasonForDisabledFeature(run, ""));
            Assert.Null(AssistedRegistry.ReasonForDisabledFeature(null, "event-system"));
        }

        [Test]
        public void WithoutAScenarioOrAChallenge_SwitchingAFeatureOff_IsFree()
        {
            RunState free = FreeRun();
            Assert.Null(AssistedRegistry.ReasonForDisabledFeature(free, "event-system"), "free play without challenges");
            Assert.False(AssistedRegistry.MarkFeatureDisabled(free, "event-system"));
        }

        [Test]
        public void ActiveChallenges_AreEnoughInFreePlay()
        {
            RunState run = FreeRun();
            run.Challenges.Add("no-debt");
            run.ChallengeStates.Add("no-debt", new string[0]);
            Assert.Equal("feature.disabled:event-system", AssistedRegistry.ReasonForDisabledFeature(run, "event-system"));

            run.ChallengeStates.Get("no-debt").Status = ChallengeStatus.Failed;
            Assert.Null(AssistedRegistry.ReasonForDisabledFeature(run, "event-system"), "a challenge that is over no longer holds the run");
        }

        [Test]
        public void AFinishedScenario_NoLongerHoldsTheFeatures()
        {
            RunState run = ScenarioRun();
            run.ScenarioState.Status = ScenarioStatus.Won;
            Assert.Null(AssistedRegistry.ReasonForDisabledFeature(run, "event-system"));
            run.ScenarioState.Status = ScenarioStatus.Lost;
            Assert.Null(AssistedRegistry.ReasonForDisabledFeature(run, "event-system"));
        }

        [Test]
        public void TheNextMonthButton_AndTheRescue_AreNotCauses_AC154()
        {
            // FR-15 item 5: nothing in the registry knows about them; the only causes are the cheats, a feature switched off, and Mark
            RunState run = ScenarioRun();
            Assert.False(run.Assisted);
            Assert.Null(AssistedRegistry.ReasonForDisabledFeature(run, "bankruptcy-recovery"), "the rescue feature was not on at the start");
            Assert.False(run.Assisted);
        }

        // ---- what the flag decides ----

        [Test]
        public void Strict_BlocksTheAchievementsOfAnAssistedRun_Otherwise_TheyComeWithAMark()
        {
            Assert.False(AssistedRegistry.CanUnlockAchievements(true, true), "strictAssisted: none");
            Assert.True(AssistedRegistry.CanUnlockAchievements(true, false), "by default: with a mark");
            Assert.True(AssistedRegistry.CanUnlockAchievements(false, true), "a clean run is never blocked");
            Assert.True(AssistedRegistry.CanUnlockAchievements(false, false));
        }

        [Test]
        public void TheEvaluator_FollowsTheSetting_AndTheProfileKeepsTheMark()
        {
            var evaluator = new AchievementEvaluator(ScenarioTestData.StandardAchievements(), ScenarioTestData.ScenarioIds);
            MetricsSnapshot m = Snap("money", 150000000.0);
            var model = new ProfileModel(new JsonObject());

            var strict = new RunInfo { RunId = "r", Assisted = true, StrictAssisted = true, DifficultyAtStart = 2, DifficultyLevel = 2, GameDate = D(1990, 1, 1) };
            Assert.Equal(0, evaluator.Evaluate("daily", m, Book(), model.View, strict, null).Count, "strict: an assisted run earns nothing");

            var lenient = new RunInfo { RunId = "r", Assisted = true, StrictAssisted = false, DifficultyAtStart = 2, DifficultyLevel = 2, GameDate = D(1990, 1, 1) };
            IList<string> earned = evaluator.Evaluate("daily", m, Book(), model.View, lenient, null);
            Assert.True(earned.Contains("cash-100m"), "otherwise it earns it");

            foreach (string id in earned)
            {
                model.Unlock(id, new AchievementEntry { RunId = "r", Assisted = lenient.Assisted, GameDate = lenient.GameDate });
            }
            Assert.True(model.View.EntryOf("cash-100m").Assisted, "marked 'with help' in the profile");
        }

        [Test]
        public void AnAssistedRun_MakesNoRecords()
        {
            Assert.False(AssistedRegistry.CanMakeRecords(true));
            Assert.True(AssistedRegistry.CanMakeRecords(false));

            var model = new ProfileModel(new JsonObject());
            RunState run = ScenarioRun();
            run.MarkAssisted("cheats.enabled");
            RunInfo info = RunInfo.From(run, D(1991, 1, 1));
            Assert.True(info.Assisted);
            var result = new ScenarioResult { Outcome = ScenarioStatus.Won, Score = 700, Medal = "gold", FinishedOn = D(1990, 6, 1) };
            Assert.False(model.RecordResult(result, info));
            Assert.Null(model.View.BestMedalOf("fabless-1985"), "the best result of the profile is untouched");
        }

        // ---- the run state itself ----

        [Test]
        public void RunInfo_TakesTheRunsFields_AndTheLiveOnes()
        {
            RunState run = ScenarioRun("no-debt", "no-oem");
            run.MarkAssisted("x");
            RunInfo info = RunInfo.From(run, D(1988, 3, 1), "fabless", "Chipmakers", 4, true);

            Assert.Equal("run-1", info.RunId);
            Assert.Equal("fabless-1985", info.ScenarioId);
            Assert.False(info.IsLegacy);
            Assert.Equal(2, info.DifficultyAtStart);
            Assert.Equal(4, info.DifficultyLevel);
            Assert.Equal("fabless", info.CompanyType);
            Assert.Equal("Chipmakers", info.CompanyName);
            Assert.Equal(D(1988, 3, 1), info.GameDate);
            Assert.True(info.Assisted);
            Assert.True(info.StrictAssisted);
            Assert.False(info.Custom);
            Assert.Equal(2, info.ChallengeCount);

            RunInfo defaults = RunInfo.From(FreeRun(), D(1985, 2, 1));
            Assert.Equal(2, defaults.DifficultyLevel, "the difficulty now defaults to the one at the start");
            Assert.Equal(0, defaults.ChallengeCount);
            Assert.Null(defaults.ScenarioId);
            Assert.Throws<ArgumentNullException>(() => RunInfo.From(null, D(1985, 1, 1)));
        }

        [Test]
        public void StartNew_BuildsAFreshScenarioRun()
        {
            RunState run = ScenarioRun("no-debt");
            Assert.Equal(RunOrigin.NewGame, run.Origin);
            Assert.Equal(D(1985, 1, 1), run.StartDate);
            Assert.True(run.HasScenario);
            Assert.True(run.IsScenarioActive);
            Assert.Equal("fabless-1985", run.ScenarioId);
            Assert.Equal(ScenarioStatus.Active, run.ScenarioState.Status);
            Assert.Equal(D(1985, 1, 1), run.ChallengeStates.StartDate);
            Assert.Equal(new[] { "no-debt" }, run.Challenges.ToArray());
            Assert.Equal(3, run.FeaturesAtStart.Count);
            Assert.False(run.Custom);
            Assert.False(FreeRun().HasScenario);
            Assert.Null(FreeRun().ScenarioId);

            ScenarioDefinition custom = ScenarioTestData.Scenario("garage-1975");
            custom.IsCustom = true;
            Assert.True(RunState.StartNew("r", D(1975, 1, 1), 0, custom, null, null).Custom, "a custom scenario makes a custom run");
        }
    }
}
