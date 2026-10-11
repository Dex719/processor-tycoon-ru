using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// How the attitude, the triggers and the monthly pass are wired to the game (TSK-010..012). The classes that touch the game cannot run in the unit
    /// tests, so these facts are read from the source of the feature, comments aside: which game methods are patched (and which are not), what is
    /// subscribed once, in which phase the monthly pass runs and what it calls.
    /// </summary>
    public class TriggerWiringTests
    {
        private static string FeatureFolder()
        {
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities");
            Assert.True(Directory.Exists(folder), "no feature folder " + folder);
            return folder;
        }

        private static string Code(string relative)
        {
            string path = Path.Combine(FeatureFolder(), relative);
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static readonly Regex ModPatchTarget = new Regex(@"\[ModPatch\(\s*typeof\(\s*(\w+)\s*\)\s*,\s*""(\w+)""\s*,\s*PatchKind\.(\w+)", RegexOptions.Compiled);

        // ---- the patches ----

        [Test]
        public void NoPatchOnAddCpu_TheReleaseOfACpuIsTheCoreEvent()
        {
            // the core rejects a patch on Company.AddCpu / AICompany.AddCpu (FR-2 item 6); a release comes as ModEvents.CpuReleased
            string src = SourceScan.SrcDirOrFail();
            var problems = new List<string>();
            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match match in ModPatchTarget.Matches(code))
                {
                    if (match.Groups[2].Value == "AddCpu") problems.Add(SourceScan.Relative(src, path) + ": " + match.Value);
                }
            }

            Assert.True(problems.Count == 0, "a patch on AddCpu:\n  " + string.Join("\n  ", problems));
            Assert.True(Regex.IsMatch(Code("RivalPersonalitiesFeature.cs"), @"ModEvents\.CpuReleased\s*\+=\s*OnCpuReleased\s*;"),
                "Register subscribes to ModEvents.CpuReleased once");
        }

        [Test]
        public void TheTriggerPatches_AreThreeReadingPostfixes_OnTheTargetsOfTheDesign()
        {
            string code = Code(Path.Combine("Triggers", "TriggerPatches.cs"));

            List<string> patches = ModPatchTarget.Matches(code).Cast<Match>().Select(m => m.Groups[1].Value + "." + m.Groups[2].Value + "/" + m.Groups[3].Value).ToList();

            Assert.Equal(3, patches.Count, string.Join(", ", patches));
            Assert.True(patches.Contains("GamePlayerCompany.AddTechnology/Postfix"), "Company.AddTechnology");
            Assert.True(patches.Contains("GameContracts.PromoteContractToActive/Postfix"), "ContractManager.PromoteContractToActive");
            Assert.True(patches.Contains("GameBusinessContracts.BreakContract/Postfix"), "BusinessContractManager.BreakContract");

            // the aliases stand for the game's classes of the design
            Assert.True(Regex.IsMatch(code, @"using\s+GamePlayerCompany\s*=\s*ProcessorTycoon\.CompanySystem\.Company\s*;"));
            Assert.True(Regex.IsMatch(code, @"using\s+GameContracts\s*=\s*ProcessorTycoon\.ContractSystem\.ContractManager\s*;"));
            Assert.True(Regex.IsMatch(code, @"using\s+GameBusinessContracts\s*=\s*ProcessorTycoon\.CompanySystem\.Business\.BusinessContractManager\s*;"));
        }

        [Test]
        public void EveryTriggerPatch_IsGuarded_AndOnlyReads()
        {
            string code = Code(Path.Combine("Triggers", "TriggerPatches.cs"));

            Assert.Equal(3, Regex.Matches(code, @"ModHooks\.CanRun\(").Count, "CanRun before the body");
            Assert.Equal(3, Regex.Matches(code, @"ModHooks\.ReportFault\(").Count, "try/catch at the boundary of each hook");
            Assert.False(Regex.IsMatch(code, @"__result|ref\s+\w+\s+__"), "a postfix that only reads changes no result");
            Assert.True(Regex.IsMatch(code, @"__instance\.IsPlayer"), "AddTechnology counts for the player's company only");
        }

        // ---- the subscriptions ----

        [Test]
        public void TheContractManagerSubscription_IsMadeAgainForEveryScene_WithoutDuplicates()
        {
            string monitor = Code(Path.Combine("Triggers", "PlayerActionMonitor.cs"));
            string feature = Code("RivalPersonalitiesFeature.cs");

            Assert.True(Regex.IsMatch(monitor, @"SingleSubscription<\w+>"), "the subscription goes through SingleSubscription");
            Assert.Equal(1, Regex.Matches(monitor, @"\.OnPlayerSignedContract\s*\+=").Count, "one place subscribes");
            Assert.Equal(1, Regex.Matches(monitor, @"\.OnPlayerSignedContract\s*-=").Count, "one place unsubscribes");
            Assert.True(Regex.IsMatch(monitor, @"active subscriptions"), "the log tells the number of subscriptions");
            Assert.True(Regex.IsMatch(feature, @"ModTicks\.GameReady\s*\+=\s*OnGameReady"), "re-subscribed on every GameReady");
            Assert.True(Regex.IsMatch(feature, @"_monitor\.OnSceneReady"), "GameReady re-attaches the monitor");
            Assert.True(Regex.IsMatch(feature, @"_monitor\.Detach"), "leaving the scene and switching off let the manager go");
        }

        [Test]
        public void TheMonitor_TakesTheSegmentFromTheCore_AndIgnoresVariantsAndTheFastForward()
        {
            string monitor = Code(Path.Combine("Triggers", "PlayerActionMonitor.cs"));

            Assert.True(Regex.IsMatch(monitor, @"ModSegments\.Classify\s*\("), "ModSegments.Classify");
            Assert.True(Regex.IsMatch(monitor, @"isVariant\s*\|\|\s*duringFastForward"), "variants and the fast-forward are dropped");
            Assert.False(Regex.IsMatch(monitor, @"CpuTransferred"), "a transfer is not a release");
        }

        [Test]
        public void TheTriggers_AreBlockedWhileLoadingAndFastForwarding()
        {
            string feature = Code("RivalPersonalitiesFeature.cs");

            Assert.True(Regex.IsMatch(feature, @"TriggerRules\.IsBlocked\(\s*ModTicks\.IsLoading\s*,\s*ModTicks\.IsFastForwarding\s*,\s*false\s*,\s*ModTicks\.IsGameReady\s*\)"),
                "the recorder is blocked by ModTicks.IsLoading, ModTicks.IsFastForwarding and a game that is not ready");
        }

        // ---- the attitude and the monthly pass ----

        [Test]
        public void TheMonthlyPass_RunsInLateMonthPassed_AndCallsEverythingTheTaskNames()
        {
            string feature = Code("RivalPersonalitiesFeature.cs");
            string evaluator = Code(Path.Combine("Triggers", "MonthlyEvaluator.cs"));
            string core = Code(Path.Combine("Logic", "Triggers", "MonthlyEvaluatorCore.cs"));

            Assert.True(Regex.IsMatch(feature, @"ctx\.OnTick\(\s*TickPhase\.LateMonthPassed\s*,\s*OnLateMonthPassed\s*\)"), "the pass runs in TickPhase.LateMonthPassed");
            Assert.True(Regex.IsMatch(feature, @"OnLateMonthPassed\s*\(\s*\)\s*\{\s*_monthly\.Run\(\)"), "and it is MonthlyEvaluator.Run");
            Assert.True(Regex.IsMatch(evaluator, @"ExpireExternal\s*\("), "the pass ends the external price cuts");
            Assert.True(Regex.IsMatch(core, @"_ledger\.MonthlyDrift\(\)"), "and drifts the attitudes");
            Assert.True(Regex.IsMatch(core, @"CautionCleaner\.RemoveExpired"), "and cleans up the old cautions");
            Assert.True(Regex.IsMatch(evaluator, @"SoldByHighEnd") && Regex.IsMatch(evaluator, @"SoldByMidRange")
                        && Regex.IsMatch(evaluator, @"SoldByLowEnd") && Regex.IsMatch(evaluator, @"SoldByIndustries"), "the shares come from the monthly lists of the four segments");
            Assert.True(Regex.IsMatch(evaluator, @"months\.Count\s*-\s*2"), "the last full month is Count - 2, as in MarketDataProvider");
        }

        [Test]
        public void TheLevelChange_ShowsANotification_AndTheTextsExist()
        {
            string feature = Code("RivalPersonalitiesFeature.cs");
            string notifier = Code(Path.Combine("Attitude", "AttitudeNotifier.cs"));

            Assert.True(Regex.IsMatch(feature, @"_attitudes\.LevelChanged\s*\+=\s*AttitudeNotifier\.OnLevelChanged"), "LevelChanged is wired to the notification");
            Assert.True(Regex.IsMatch(notifier, @"ModNotify\.ShowKey\(\s*Feature\s*,\s*""rival\.notify\.level"""), "ModNotify.ShowKey(rival.notify.level)");
        }
    }
}
