using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// The seams of the game layer of the feature (TSK-011 ... TSK-015): the parts that need the game and the core to run - the director, the mailer, the host,
    /// the new game option, the hub page - cannot run in the test build, so the facts the acceptance criteria name are read from their source (comments aside):
    /// what is subscribed to, which service call does what, that no patch is put anywhere, that every entry runs in the guard. The behaviour behind the seams is
    /// tested on the pure parts (<c>OnboardingController</c>, <c>MailPlanner</c>, <c>HubStatus</c>, <c>PausePolicy</c>, <c>PlaceholderResolver</c>).
    /// </summary>
    public class OnboardingSeamsTests
    {
        private static string FeatureFolder()
        {
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "FirstYearOnboarding");
            Assert.True(Directory.Exists(folder), "no feature folder " + folder);
            return folder;
        }

        /// <summary>The code of a source file of the feature with the comments removed and the strings kept.</summary>
        private static string Code(string relative)
        {
            string path = Path.Combine(FeatureFolder(), relative);
            string text = SourceScan.ReadText(path);
            Assert.True(text.Length > 0, "missing " + path);
            return SourceScan.Strip(text, false);
        }

        private static void Has(string code, string pattern, string what)
        {
            Assert.True(Regex.IsMatch(code, pattern), what);
        }

        // ---- no patches ----

        [Test]
        public void TheFeature_PutsNoPatchesIntoTheGame()
        {
            var offenders = new List<string>();
            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                if (Regex.IsMatch(code, @"\[\s*ModPatch\b|\bctx\s*\.\s*Patch\s*\(|\bnew\s+Harmony\s*\(|\bHarmony\s*\.\s*Create|\.PatchAll\s*\(|\bHarmonyPatch\b|\bModHooks\s*\.\s*ApplyFeature"))
                    offenders.Add(SourceScan.Relative(FeatureFolder(), path));
            }

            Assert.True(offenders.Count == 0, "design.md, 'Патчи Harmony': the tutorial puts no patches; found in " + string.Join(", ", offenders));
        }

        [Test]
        public void TheFeature_NeverNamesTheGamesNewGameWindowOrItsStart()
        {
            var offenders = new List<string>();
            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                if (Regex.IsMatch(code, @"\bMenuGameSetup\b|\bGameManager\b|\bStartNewGame\b"))
                    offenders.Add(SourceScan.Relative(FeatureFolder(), path));
            }

            Assert.True(offenders.Count == 0, "FR-1 item 4: no patch on MenuGameSetup.* and GameManager.StartNewGame, and no reach for them either: " + string.Join(", ", offenders));
        }

        // ---- the director (TSK-011) ----

        [Test]
        public void TheDirector_SubscribesToTheEventsOfTheCore()
        {
            string code = Code("OnboardingDirector.cs");

            Has(code, @"ctx\s*\.\s*OnTick\s*\(\s*TickPhase\s*\.\s*DayPassed\s*,", "ctx.OnTick(TickPhase.DayPassed, ...)");
            Has(code, @"ModTicks\s*\.\s*GameReady\s*\+=", "ModTicks.GameReady");
            Has(code, @"ModTicks\s*\.\s*BatchEnded\s*\+=", "ModTicks.BatchEnded");
            Has(code, @"ModMail\s*\.\s*Delivered\s*\+=", "ModMail.Delivered");
            Has(code, @"ModMail\s*\.\s*RegisterActionHandler\s*\(\s*OnboardingFeature\s*\.\s*FeatureId\s*,", "ModMail.RegisterActionHandler");
            Has(code, @"OnboardingService\s*\.\s*RegisterBackend\s*\(\s*this\s*\)", "the director is the backend of the API");
        }

        [Test]
        public void TheDirector_SubscribesToTheBankruptcyEventOfTheScene_OnGameReady_AndLetsItGo()
        {
            string code = Code("OnboardingDirector.cs");

            Has(code, @"OnCompanyWentBankrupt\s*\+=", "the subscription");
            Has(code, @"OnCompanyWentBankrupt\s*-=", "the unsubscription");
            Has(code, @"BankruptcyHandler\s*\.\s*Instance|GameBankruptcyHandler\s*\.\s*Instance", "the handler of the scene");
            Has(code, @"ModCompanies\s*\.\s*IsPlayer\s*\(", "only the player's bankruptcy counts");

            string ready = Regex.Match(code, @"private\s+void\s+OnGameReady\s*\([^)]*\)\s*\{.*?\n        \}", RegexOptions.Singleline).Value;
            Assert.True(ready.Length > 0, "OnGameReady is in the director");
            Has(ready, @"AttachBankruptcy\s*\(\s*\)", "GameReady attaches the event of the scene (the event goes with the scene)");
        }

        [Test]
        public void ThePoll_IsACoroutineOfTheHost_TwiceASecondOfRealTime_ThatEndsWithTheFeature()
        {
            string code = Code("OnboardingDirector.cs");

            Has(code, @"PollIntervalSeconds\s*=\s*0\.5f", "0.5 s (FR-4: the answer within a second)");
            Has(code, @"WaitForSecondsRealtime\s*\(\s*PollIntervalSeconds\s*\)", "real time, not game time");
            Has(code, @"host\s*\.\s*Run\s*\(\s*PollLoop\s*\(", "PTModHost.Run(PollLoop(...))");
            Has(code, @"!\s*OnboardingFeature\s*\.\s*IsActive\s*\)\s*yield\s+break", "the loop ends when the feature is switched off");
            Has(code, @"generation\s*!=\s*_generation", "a loop of an older generation ends");
            Has(code, @"!\s*_controller\s*\.\s*IsRunning\s*\)\s*yield\s+break", "the loop ends when the tutorial is not running");
            Has(code, @"if\s*\(\s*active\s*\)", "the switch of the feature starts and stops the poll");
        }

        [Test]
        public void EveryEntryOfTheDirector_RunsInTheGuard_AndItKeepsNoCounterOfErrorsOfItsOwn()
        {
            string code = Code("OnboardingDirector.cs");

            Has(code, @"ModHooks\s*\.\s*Guard\s*\(\s*OnboardingFeature\s*\.\s*FeatureId\s*,\s*SitePrefix\s*\+\s*site\s*,\s*body\s*\)", "one guard helper");
            foreach (string entry in new[] { "OnActiveChanged", "OnGameReady", "OnDayPassed", "OnBatchEnded", "OnMailDelivered", "OnMailAction", "OnPlayerBankrupt", "Poll" })
            {
                Has(code, @"\bGuard\s*\(\s*""" + entry + @"""", "the entry " + entry + " runs in Guard");
            }
            Assert.False(Regex.IsMatch(code, @"\b(?:_?faults?|_?errorCount|_?failures)\b\s*(?:\+\+|\+=)", RegexOptions.IgnoreCase), "design.md: the core counts the errors, the director has no counter");
        }

        [Test]
        public void TheFinalModeOfANewGame_IsTakenOnGameReady_TheForcedModeOverTheToggle()
        {
            string code = Code("OnboardingDirector.cs");

            Has(code, @"reason\s*==\s*GameReadyReason\s*\.\s*NewGame", "the new game branch");
            Has(code, @"OnboardingService\s*\.\s*TakePendingMode\s*\(\s*out\s+\w+\s*\)", "the pending mode is taken and reset");
            Has(code, @"PendingModeBook\s*\.\s*ResolveTutorial\s*\(\s*requested\s*,\s*PendingStart\s*\.\s*HasValue\s*,\s*PendingStart\s*\.\s*TutorialOn\s*\)", "PendingMode over PendingStart");
            Has(code, @"PendingStart\s*\.\s*Clear\s*\(\s*\)", "the toggle is used up");
            Has(code, @"OnboardingService\s*\.\s*ResetPendingMode\s*\(\s*\)", "a loaded save drops a stale request");
        }

        [Test]
        public void TheStateOfTheTutorial_ComesFromTheSaveSection_AndOnbStepDoneReadsIt()
        {
            string code = Code("OnboardingDirector.cs");

            Has(code, @"OnboardingFeature\s*\.\s*SaveParticipant\s*\.\s*State", "the state of the game in play");
            Has(code, @"StepDoneSource\s*=\s*_controller\s*\.\s*IsStepClosed", "the predicate onb.stepDone reads the controller");
        }

        // ---- the feature (TSK-001, TSK-011, TSK-013, TSK-015) ----

        [Test]
        public void TheFeature_WiresTheDirector_TheNewGameOption_AndTheHubPage_InRegister()
        {
            string code = Code("OnboardingFeature.cs");

            Has(code, @"new\s+OnboardingDirector\s*\(\s*Catalog\s*\)", "the director is built over the checked catalog");
            Has(code, @"_director\s*\.\s*Install\s*\(\s*ctx\s*\)", "the director subscribes");
            Has(code, @"Guarded\s*\(\s*""new game""\s*,\s*OnboardingNewGame\s*\.\s*Register\s*\)", "the option and the section of the new game window");
            Has(code, @"ctx\s*\.\s*HubPage\s*\(\s*OnboardingHubPage\s*\.\s*TitleKey\s*,\s*OnboardingHubPage\s*\.\s*Build\s*\)", "the page of the hub");
            Has(code, @"if\s*\(\s*!ContentBroken\s*\)", "broken content registers no option");
        }

        // ---- the new game window (TSK-013) ----

        [Test]
        public void TheNewGameOption_IsAToggleOfTheFeature_StartingAsTheProfileSays()
        {
            string code = Code("OnboardingNewGame.cs");

            Has(code, @"ModNewGame\s*\.\s*RegisterOption\s*\(", "RegisterOption");
            Has(code, @"FeatureId\s*=\s*OnboardingFeature\s*\.\s*FeatureId", "FeatureId = first-year-onboarding");
            Has(code, @"OptionId\s*=\s*""tutorial""", "Id = tutorial");
            Has(code, @"Kind\s*=\s*NewGameOptionKind\s*\.\s*Toggle", "a toggle");
            Has(code, @"DefaultOn\s*=\s*OnboardingProfile\s*\.\s*ToggleStartsOn", "DefaultOn = !hasSeenTutorial || defaultToggle");
            Has(code, @"LabelKey\s*=\s*""onb\.toggle\.label""", "the caption");
            Has(code, @"TooltipKey\s*=\s*""onb\.toggle\.tooltip""", "the hover text");
        }

        [Test]
        public void TheNewGameSection_IsOrder20_AndFollowsThePendingModeEvent()
        {
            string code = Code("OnboardingNewGame.cs");

            Has(code, @"SectionOrder\s*=\s*20", "order 20");
            Has(code, @"ModNewGame\s*\.\s*RegisterSection\s*\(\s*OnboardingFeature\s*\.\s*FeatureId\s*,\s*BuildForcedModeLine\s*,\s*SectionOrder\s*\)", "RegisterSection");
            Has(code, @"OnboardingService\s*\.\s*PendingModeChanged\s*\+=", "the line follows PendingModeChanged");
            Has(code, @"mode\s*==\s*PendingMode\s*\.\s*Allowed", "no line while nobody asked for a mode");
            Has(code, @"""onb\.toggle\.forced""", "the text 'Set by the scenario: tutorial on/off'");
        }

        [Test]
        public void BeforeStart_RecordsTheToggle_InTheProfileAndInPendingStart_AndLeavesTheInitialDataAlone()
        {
            string code = Code("OnboardingNewGame.cs");

            Has(code, @"ModNewGame\s*\.\s*BeforeStart\s*\+=\s*OnBeforeStart", "BeforeStart is subscribed");
            Has(code, @"ModNewGame\s*\.\s*GetToggle\s*\(\s*OnboardingFeature\s*\.\s*FeatureId\s*,\s*OptionId\s*\)", "GetToggle");
            Has(code, @"OnboardingProfile\s*\.\s*DefaultToggle\s*=\s*on", "defaultToggle goes to the profile");
            Has(code, @"PendingStart\s*\.\s*Set\s*\(\s*on\s*\)", "PendingStart");
            Has(code, @"PendingStart\s*\.\s*Clear\s*\(\s*\)", "a failure leaves the tutorial off");

            string method = Regex.Match(code, @"private\s+static\s+void\s+OnBeforeStart\s*\(.*?\n        \}", RegexOptions.Singleline).Value;
            Assert.True(method.Length > 0, "OnBeforeStart is in the class");
            Assert.False(Regex.IsMatch(method, @"\bdata\s*\.\s*\w+\s*="), "PlayerInitialData is not changed");
        }

        // ---- mail, pause, windows, numbers (TSK-012) ----

        [Test]
        public void TheMailer_SendsThroughModMail_WithTheTagThePriorityAndTheButtonsOfThePlan()
        {
            string code = Code("OnboardingMailer.cs");

            Has(code, @"ModMail\s*\.\s*Send\s*\(\s*message\s*\)", "ModMail.Send");
            Has(code, @"FeatureId\s*=\s*OnboardingFeature\s*\.\s*FeatureId", "the feature id");
            Has(code, @"Tag\s*=\s*plan\s*\.\s*Tag", "Tag = onb:<stepId>");
            Has(code, @"Priority\s*=\s*plan\s*\.\s*Priority", "the priority of the step");
            Has(code, @"Repeatable\s*=\s*action\s*\.\s*Repeatable", "the skip buttons work once");
            Has(code, @"ModMail\s*\.\s*SetStatusLine\s*\(\s*id\s*,", "the line Already done");
            Has(code, @"ModMail\s*\.\s*SetActionState\s*\(\s*mailId\s*,\s*OnboardingMachine\s*\.\s*ActionHint", "the hint button is switched off when the hints are gone");
            Has(code, @"ModNotify\s*\.\s*Show\s*\(", "the hints are notifications");
            Has(code, @"ModNotify\s*\.\s*ShowKey\s*\(\s*OnboardingFeature\s*\.\s*FeatureId\s*,\s*""onb\.start\.notify""", "the notice at the start");
            Has(code, @"OnboardingApiBridge\s*\.\s*PublishCompleted\s*\(", "the end of a run reaches the profile and the API");
        }

        [Test]
        public void TheHost_PausesWithManualPause_AndOpensTheTwoCreatorWindows()
        {
            string code = Code("GameOnboardingHost.cs");

            Has(code, @"ManualPause\s*\(\s*false\s*\)", "DateController.ManualPause(false)");
            Has(code, @"OpenCreatorWindowOfType\s*\(\s*type\s*\)", "CreatorManager.OpenCreatorWindowOfType");
            Has(code, @"GameCreatorWindow\s*\.\s*Type\s*\.\s*Cpu", "the CPU designer");
            Has(code, @"GameCreatorWindow\s*\.\s*Type\s*\.\s*FactoryExpansion", "the factory expansion");
            Has(code, @"""currentTimeSpeed""", "whether the time runs");
            Has(code, @"return\s+_speedField\s*==\s*null\s*\?\s*-1", "an unreadable speed is 'not running': no pause of a paused game");
            Has(code, @"ModSettings\s*\.\s*GetBool\s*\(\s*OnboardingFeature\s*\.\s*FeatureId\s*,\s*key\s*\)", "the hub settings are read at each use");
        }

        [Test]
        public void ThePlaceholders_AreReadFromTheSourcesOfTheDesign()
        {
            string code = Code("GamePlaceholderSource.cs");

            foreach (string budget in new[] { "HighEndBudget", "MidRangeBudget", "LowEndBudget", "IndustriesBudget" })
            {
                Has(code, @"market\s*\.\s*" + budget + @"\b", "Market." + budget);
            }
            Has(code, @"ModCredit\s*\.\s*PlayerDebtLimit\s*\(\s*\)", "debtLimit");
            Has(code, @"""defaultTimeLimit""", "defaultDays");
            Has(code, @"AccessTools\s*\.\s*Field\s*\(\s*typeof\s*\(\s*GameBankruptcyHandler\s*\)", "the private field through AccessTools");
            Has(code, @"_defaultDaysFallback", "the fallback of config.json (120)");
            Has(code, @"FloatToMoney\s*\(\s*amount\s*,\s*string\s*\.\s*Empty\s*,\s*true\s*,\s*true\s*,\s*true\s*\)", "money the way the game writes it");
            foreach (string name in new[] { "highEndBudget", "midRangeBudget", "lowEndBudget", "industriesBudget", "debtLimit", "defaultDays", "money", "company", "founder" })
            {
                Has(code, @"case\s+""" + name + @"""", "the placeholder " + name);
            }
        }

        [Test]
        public void ThePlaceholderSource_AnswersEveryNameOfTheWhiteList()
        {
            string code = Code("GamePlaceholderSource.cs");
            var cases = Regex.Matches(code, @"case\s+""(\w+)""\s*:").Cast<Match>().Select(m => m.Groups[1].Value).OrderBy(n => n).ToArray();

            Assert.Equal(PTMod.Features.FirstYearOnboarding.Logic.StepValidator.ArgNames.OrderBy(n => n).ToArray(), cases,
                "the names steps.json may use and the names the game answers are the same set");
        }

        // ---- the hub page (TSK-015) ----

        [Test]
        public void TheHubPage_HasTheFourButtons_AndAsksBeforeARestart()
        {
            string code = Code("OnboardingHubPage.cs");

            foreach (string key in new[] { "onb.hub.button.repeat", "onb.hub.button.skipStep", "onb.hub.button.skipAll", "onb.hub.button.restart" })
            {
                Has(code, @"ModUI\s*\.\s*Button\s*\(\s*container\s*,\s*ModL10n\s*\.\s*L\s*\(\s*""" + Regex.Escape(key) + @"""", "the button " + key);
            }
            Has(code, @"ModUI\s*\.\s*Confirm\s*\(\s*""onb\.hub\.restart\.title""\s*,\s*""onb\.hub\.restart\.body""", "the restart is confirmed");
            Has(code, @"_director\s*\.\s*Restart\s*\(\s*\)", "the restart goes through the director (it starts the poll)");
            Has(code, @"ResendCurrent\s*\(\s*\)", "repeat the mail");
            Has(code, @"SkipCurrentStep\s*\(\s*\)", "skip the step");
            Has(code, @"SkipAll\s*\(\s*""hub""\s*\)", "skip the tutorial");
            Has(code, @"onb\.error\.content", "broken content is said on the page");
            Has(code, @"HubStatus\s*\.\s*Of\s*\(", "the page draws what HubStatus decides");
        }

        [Test]
        public void TheTwoSettings_AreParamSpecs_SoTheCoreDrawsThemOnThePageAndAppliesThemAtOnce()
        {
            string code = Code("OnboardingFeature.cs");

            Has(code, @"Key\s*=\s*ParamPauseOnStep", "pauseOnStep");
            Has(code, @"Key\s*=\s*ParamRemindersEnabled", "remindersEnabled");
            Assert.Equal(2, Regex.Matches(code, @"ctx\s*\.\s*Param\s*\(").Count);
        }
    }
}
