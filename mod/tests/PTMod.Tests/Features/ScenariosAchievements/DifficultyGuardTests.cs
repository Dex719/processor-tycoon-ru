using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Features.ScenariosAchievements.Logic;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-018: the ban on lowering the difficulty during a scenario (D14, E16, FR-15 item 3) - the decision as a function (<see cref="DifficultyRule"/>) - and the
    /// shape of the patches of the feature: exactly two (the postfix on the OEM contracts and the optional prefix on the difficulty), none of the targets the core's
    /// services replaced, and the assisted causes that come from the game's setup. The classes themselves need the game; their source is read as text.
    /// </summary>
    [Covers(typeof(DifficultyRule), typeof(DifficultyAction))]
    public class DifficultyGuardTests
    {
        private static string Source(params string[] path)
        {
            string file = Path.Combine(new[] { SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements" }.Concat(path).ToArray());
            Assert.True(File.Exists(file), "no source file: " + file);
            return SourceScan.Strip(SourceScan.ReadText(file), false);
        }

        // ---- the decision ----

        [Test]
        public void LoweringTheDifficulty_DuringAScenario_IsRefused()
        {
            Assert.Equal(DifficultyAction.Revert, DifficultyRule.Decide(true, 1, 2));
            Assert.Equal(DifficultyAction.Revert, DifficultyRule.Decide(true, 0, 3));
            Assert.Equal(DifficultyAction.Revert, DifficultyRule.Decide(true, 2, 4));
        }

        [Test]
        public void RaisingTheDifficulty_OrKeepingIt_IsAllowed()
        {
            Assert.Equal(DifficultyAction.Allow, DifficultyRule.Decide(true, 3, 2));
            Assert.Equal(DifficultyAction.Allow, DifficultyRule.Decide(true, 2, 2), "the list put back to the present level raises the change again: it must pass");
            Assert.Equal(DifficultyAction.Allow, DifficultyRule.Decide(true, 4, 0));
        }

        [Test]
        public void FreePlay_AndAScenarioThatIsOver_AreNotGuarded()
        {
            Assert.Equal(DifficultyAction.Allow, DifficultyRule.Decide(false, 0, 4), "free play, or a scenario that was won or lost (the game goes on as free play)");
            for (int current = 0; current <= 5; current++)
            {
                for (int selected = 0; selected <= 4; selected++)
                {
                    Assert.Equal(DifficultyAction.Allow, DifficultyRule.Decide(false, selected, current), selected + " -> " + current);
                }
            }
        }

        [Test]
        public void TheImpossibleLevel_IsTheGamesOwnBusiness()
        {
            // the original method returns at once for level 5 and the list has no such entry: the guard must not try to put it back
            Assert.Equal(5, DifficultyRule.ImpossibleLevel);
            Assert.Equal(DifficultyAction.Allow, DifficultyRule.Decide(true, 0, DifficultyRule.ImpossibleLevel));
            Assert.Equal(DifficultyAction.Allow, DifficultyRule.Decide(true, 4, DifficultyRule.ImpossibleLevel));
        }

        [Test]
        public void TheDecision_IsTotal_RevertOnlyWhenAScenarioIsGoingAndTheLevelWouldFall()
        {
            for (int current = 0; current <= 5; current++)
            {
                for (int selected = 0; selected <= 5; selected++)
                {
                    DifficultyAction action = DifficultyRule.Decide(true, selected, current);
                    bool expected = current != 5 && selected < current;
                    Assert.Equal(expected, action == DifficultyAction.Revert, "selected " + selected + ", current " + current);
                }
            }
        }

        // ---- the patches of the feature ----

        [Test]
        public void TheFeatureHasExactlyTwoPatches_AndNoneOfTheTargetsTheCoreTook()
        {
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements");
            var patches = new System.Collections.Generic.List<string>();
            foreach (string path in SourceScan.Files(root, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match match in Regex.Matches(code, @"\[ModPatch\(\s*typeof\((\w+)\)\s*,\s*""(\w+)""\s*,\s*PatchKind\.(\w+)"))
                {
                    patches.Add(match.Groups[1].Value + "." + match.Groups[2].Value + "/" + match.Groups[3].Value);
                }
            }
            patches.Sort(StringComparer.Ordinal);

            Assert.Equal(new[] { "GameContractManager.PromoteContractToActive/Postfix", "GameplaySettingsWindow.ChangeDifficulty/Prefix" }, patches.ToArray());
        }

        [Test]
        public void TheFeature_PatchesNothingTheCoresServicesReplaced()
        {
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements");
            string all = string.Join("\n", SourceScan.Files(root, ".cs").Select(p => SourceScan.Strip(SourceScan.ReadText(p), true)));
            foreach (string forbidden in new[] { "AddCpu", "StartNewGame", "InitializeAsNewGame", "UpdateInitialData", "AddMoney", "NextMonth" })
            {
                foreach (Match match in Regex.Matches(all, @"\[ModPatch\([^\]]*""" + forbidden + @""""))
                {
                    Assert.Fail("a patch on " + forbidden + " (the core's service replaces it): " + match.Value);
                }
            }
            Assert.False(Regex.IsMatch(all, @"new\s+Harmony\(|\bHarmony\.(Patch|CreateAndPatchAll|PatchAll)|\bPatchAll\("), "no Harmony of its own: the patches go through ctx.Patch");
        }

        [Test]
        public void TheEntryClass_DeclaresTheTwoPatchClasses()
        {
            string code = Source("ScenariosFeature.cs");
            Assert.True(Regex.IsMatch(code, @"ctx\.Patch\(\s*typeof\(\s*ScenarioPatches\s*\)\s*\)"), "the postfix on the OEM contracts");
            Assert.True(Regex.IsMatch(code, @"ctx\.Patch\(\s*typeof\(\s*DifficultyGuard\s*\)\s*\)"), "the prefix on the difficulty");
            Assert.Equal(2, Regex.Matches(code, @"ctx\.Patch\(").Count);
        }

        [Test]
        public void TheDifficultyPatch_IsAPrefixThatIsOptional_AndPutsTheListAndThePlayerBack()
        {
            string code = Source("Integration", "DifficultyGuard.cs");
            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(GameplaySettingsWindow\),\s*""ChangeDifficulty"",\s*PatchKind\.Prefix[^
]*Optional\s*=\s*true"), "optional: the feature stays available without it");
            Assert.True(code.Contains("___difficultySettings"), "the private field comes in by injection");
            Assert.True(Regex.IsMatch(code, @"static\s+bool\s+Prefix\("), "a prefix that can cancel the original");
            Assert.True(code.Contains("DifficultyRule.Decide("), "the decision is the tested function");
            Assert.True(Regex.IsMatch(code, @"dropdown\.SetSelected\(\s*current\s*\)"), "the list goes back");
            Assert.True(Regex.IsMatch(code, @"player\.DifficultyLevel\s*=\s*current"), "and so does Player.DifficultyLevel");
            Assert.True(code.Contains("scn.difficulty.locked"), "the player is told");
            Assert.True(code.Contains("ModHooks.Guard"), "under the guard of the feature");
            Assert.False(code.Contains("MarkAssisted") || code.Contains("AssistedRegistry.Mark"), "refusing the change is not a reason for the assisted flag (D9)");
        }

        [Test]
        public void TheDifficultyGuard_LeavesTheWindowAloneWhileTheGameLoads()
        {
            string code = Source("Integration", "DifficultyGuard.cs");
            Assert.True(code.Contains("ModTicks.IsLoading") && code.Contains("ModTicks.IsGameReady"), "the window sets its list by itself while a game loads: not a choice of the player");
            Assert.True(code.Contains("run.IsScenarioActive"), "only a scenario that is going is guarded");
        }

        [Test]
        public void TheOemPostfix_LeavesOnItsFirstLine_WhileLoadingOrFastForwarding()
        {
            string code = Source("Integration", "ScenarioPatches.cs");
            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(GameContractManager\),\s*""PromoteContractToActive"",\s*PatchKind\.Postfix\)\]"), "a postfix");

            string body = code.Substring(code.IndexOf("PromoteContractToActivePostfix(", StringComparison.Ordinal));
            body = body.Substring(body.IndexOf('{') + 1);
            string first = body.Trim().Split('\n')[0].Trim();
            Assert.Equal("if (ModTicks.IsLoading || ModTicks.IsFastForwarding) return;", first, "the first statement of the postfix");
            Assert.True(code.Contains("ModHooks.Guard"), "and the rest runs under the guard");
        }

        // ---- the assisted causes of the setup ----

        [Test]
        public void TheFeatureWatch_MarksOnlyAFeatureSwitchedOff_AndNotItsOwnSwitchOnOrAFault()
        {
            string code = Source("Integration", "FeatureWatch.cs");
            Assert.True(code.Contains("ModFeatures.StateChanged += OnStateChanged"), "it follows the switches of the features");
            Assert.True(code.Contains("ScenariosFeature.Disabled += OnSelfDisabled"), "the core skips the handlers of a feature that is off: its own switch-off comes by its own event");
            Assert.True(Regex.IsMatch(code, @"if\s*\(\s*state\s*!=\s*FeatureState\.Disabled\s*\)\s*return"), "Enabled, Faulted and Unavailable are not marked");
            Assert.True(code.Contains("AssistedRegistry.MarkFeatureDisabled("), "the rule is the tested one: only a feature that was on at the start, during a scenario or a challenge");
            Assert.False(code.Contains("NextMonth") || code.Contains("Cheats.AddMoney"), "'Next month' and the cheats window mark nothing");
        }

        [Test]
        public void TheCheatsOfTheStart_MakeTheRunAssisted_WhenTheRunIsBorn()
        {
            string director = Source("Integration", "RunDirector.cs");
            Assert.True(director.Contains("GameFacts.CheatsEnabled()"), "the cheats of the game that just started are read from the player");
            Assert.True(Regex.IsMatch(director, @"RunStart\.Create\([^;]*GameFacts\.CheatsEnabled\(\)"), "and given to the run's birth");
            Assert.True(Regex.IsMatch(director, @"RunState\.CreateLegacy\([^;]*GameFacts\.CheatsEnabled\(\)"), "also for a legacy run");
        }
    }
}
