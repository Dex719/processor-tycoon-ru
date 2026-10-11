using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.L10n;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-022 .. TSK-027: the windows of the feature need Unity and the game and cannot run in the test exe, so their source is read as text, like the seam tests of the core do. What is
    /// checked is what the acceptance of each task says about the wiring - which template a window is made from, what it listens to, what it calls in the core and in the game, what stays out
    /// of a tick - and that every text key the windows and the models ask for is in both string tables. The words and the numbers behind the windows are the models', tested on their own.
    /// </summary>
    public class WindowsSeamTests
    {
        private static string Source(params string[] path)
        {
            string file = Path.Combine(new[] { SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements" }.Concat(path).ToArray());
            Assert.True(File.Exists(file), "no source file: " + file);
            return SourceScan.Strip(SourceScan.ReadText(file), false);
        }

        private static string Between(string text, string from, string to)
        {
            int start = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(start >= 0, "no '" + from + "' in the source");
            int end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
            return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
        }

        private static void AssertGuarded(string code, string what)
        {
            Assert.True(code.Contains("ModHooks.Guard(ScenariosFeature.FeatureId"), what + " runs under the guard of the feature");
        }

        // ---- TSK-022: the window of the challenges ----

        [Test]
        public void TheChallengesWindow_IsMadeFromTheMenuPanel_ListsTheChallengesWithSwitches_AndFollowsTheChoice()
        {
            string code = Source("UI", "ChallengesWindow.cs");
            Assert.True(code.Contains("Slot.Ensure(WindowTemplate.MenuPanel)"), "scene 0, the template Window.MenuPanel");
            Assert.True(code.Contains("ModUI.Toggle(list,"), "a switch per challenge (ModUI.Toggle)");
            Assert.True(code.Contains("ScenarioSetupSection.ChallengesRequested += Open"), "the button 'Challenges (N)...' of the panel opens it");
            Assert.True(code.Contains("ScenarioStarter.Selection.SetChallenge(challengeId, on)"), "the choice goes to the one the panel and the pending run read");
            Assert.True(code.Contains("ScenarioStarter.Selection.Changed += OnChoiceChanged") && code.Contains("ModNewGame.PreviewChanged += OnChoiceChanged"), "it follows the choice and the settings of the game window");
            AssertGuarded(code, "the window");

            string view = Between(code, "private static ChallengeListView MakeView()", "// ---- the player and the choice ----");
            Assert.True(view.Contains("ChallengeListBuilder.Build(") && view.Contains("ModCredit.IsPerCompanyActive"), "the model decides, with the credit patch of the core");
            Assert.True(view.Contains("ScenarioSetupSection.CompanyTypeId()") && view.Contains("ScenarioSetupSection.Chosen()"), "the company type and the scenario of the panel");
        }

        [Test]
        public void TheChallengesWindow_RefreshesInPlace_WithoutARedraw()
        {
            string code = Source("UI", "ChallengesWindow.cs");
            string refresh = Between(code, "private static void OnChoiceChanged()", "private static RowWidgets Find(");
            Assert.False(refresh.Contains("Clear(") || refresh.Contains("Build("), "a change of the choice does not rebuild the window (the scroll stays)");
            Assert.True(refresh.Contains("Apply(widgets, row)"), "the rows are brought to the model");

            string apply = Between(code, "private static void Apply(", "private static ChallengeListView MakeView()");
            Assert.True(apply.Contains("SetWithoutNotify") && apply.Contains("Interactable = row.IsAvailable"), "the switch follows the model and does not call back");
        }

        [Test]
        public void TheSectionOfThePanel_HandsOverToTheWindow_ThroughTheEventsItAlreadyRaises()
        {
            string section = Source("UI", "ScenarioSetupSection.cs");
            Assert.True(section.Contains("internal static event Action ChallengesRequested") && section.Contains("internal static event Action AchievementsRequested"));
            Assert.True(section.Contains("_challengesButton.Interactable = ChallengesRequested != null"), "the button works because the window listens");
            Assert.True(section.Contains("internal static ScenarioDefinition Chosen()") && section.Contains("internal static string CompanyTypeId()"), "the window asks the panel for the mode");
        }

        // ---- TSK-023: the briefing and the letters ----

        [Test]
        public void TheBriefingLetter_IsCritical_HasItsTag_AndIsKeptInTheRun()
        {
            string code = Source("Integration", "ScenarioMailer.cs");
            string send = Between(code, "internal static bool SendBriefing(", "internal static bool SendResult(");

            Assert.True(code.Contains("BriefingTagPrefix = \"scn:briefing:\""), "tag scn:briefing:<runId>");
            Assert.True(send.Contains("Tag = tag") && send.Contains("BriefingTagPrefix + run.RunId"), "the run id is in the tag");
            Assert.True(send.Contains("Priority = MailPriority.Critical"), "Critical");
            Assert.True(send.Contains("ModMail.Send(") && send.Contains("run.BriefingMailId = id"), "the id the core returned goes into the section of the run");
            Assert.True(send.Contains("TitleKey = \"scn.briefing.title\"") && send.Contains("BodyKey = \"scn.mail.briefing.body\""), "keys with arguments: the core draws them in the language of the moment");
            Assert.True(send.Contains("ModMail.Find(ScenariosFeature.FeatureId, tag) != null") && send.Contains("run.BriefingMailId > 0"), "one letter per run");
        }

        [Test]
        public void TheBriefing_IsOnlyForANewGameWithAScenario_AndOpensTheWindow()
        {
            string code = Source("Integration", "ScenarioMailer.cs");
            string ready = Between(code, "private static void OnRunReady(", "private static void OnResultReady(");
            Assert.True(Regex.IsMatch(ready, @"reason\s*!=\s*GameReadyReason\.NewGame\s*\|\|\s*run\s*==\s*null\s*\|\|\s*!run\.HasScenario"), "not for a loaded game, not for free play");
            Assert.True(ready.Contains("SendBriefing(run)") && ready.Contains("BriefingWindow.Open(run)"), "the letter and the window");
            Assert.True(code.Contains("RunDirector.RunReady += OnRunReady"));
            AssertGuarded(code, "the mailer");

            string window = Source("UI", "BriefingWindow.cs");
            Assert.True(window.Contains("Slot.Ensure(WindowTemplate.Modal)"), "modal: the time stands");
            Assert.True(window.Contains("ButtonStyle.CallToAction") && window.Contains("scn.briefing.button"), "the button 'Get to work'");
            Assert.True(window.Contains("BriefingBuilder.Build("), "the same pieces as the letter");
        }

        [Test]
        public void TheResultLetter_IsNormal_HasItsTag_AndIsKeptInTheRun()
        {
            string code = Source("Integration", "ScenarioMailer.cs");
            string send = Between(code, "internal static bool SendResult(", "ModLog.Debug(ScenariosFeature.FeatureId, \"mail: the result");

            Assert.True(code.Contains("ResultTagPrefix = \"scn:result:\""), "tag scn:result:<runId>");
            Assert.True(send.Contains("Priority = MailPriority.Normal"), "Normal");
            Assert.True(send.Contains("ResultBuilder.Build(") && send.Contains("ResultBuilder.MailArgs("), "the outcome, the score, the medal, the goals");
            Assert.True(send.Contains("run.ResultMailId = id") && send.Contains("run.ResultMailId > 0"), "one letter per run, its id in the section");
            Assert.True(code.Contains("RunDirector.ResultReady += OnResultReady"), "after the profile was written and, in a batch run, after BatchEnded");
        }

        [Test]
        public void TheFeatureReservesNoBlockOfLetterIds()
        {
            string all = string.Join("\n", new[] { Source("Integration", "ScenarioMailer.cs"), Source("UI", "BriefingWindow.cs"), Source("Integration", "RunDirector.cs") });
            Assert.False(Regex.IsMatch(all, @"FirstModMailId|ReserveMailIds|MailIdBlock|mailBlock", RegexOptions.IgnoreCase), "the ids come from the counter of the core");
        }

        [Test]
        public void TheNumbers_ComeFromTheFormatsOfTheGame()
        {
            string texts = Source("Integration", "GameUiTexts.cs");
            Assert.True(texts.Contains("GameFormatter.FloatToMoney(amount, string.Empty, true, true, true)"), "StringFormatter.FloatToMoney");
            Assert.True(texts.Contains("GameFormatter.FloatToPercentageString(fraction, true)"), "FloatToPercentageString");
            Assert.True(texts.Contains("GameFormatter.FloatToFrequency(kilohertz)"), "FloatToFrequency");
            Assert.True(Regex.Matches(texts, @"MethodImplOptions\.NoInlining").Count >= 3, "the game's class is touched in small methods only");

            Assert.True(Source("UI", "BriefingWindow.cs").Contains("GameUiTexts.Instance"), "the window writes numbers with them");
            Assert.True(Source("Integration", "ScenarioMailer.cs").Contains("GameUiTexts.Instance"), "the letters, too");
        }

        // ---- TSK-024: the goal tracker ----

        [Test]
        public void TheTracker_IsAPanel_PlacedInACorner_AndOpensWhenTheRunIsReady()
        {
            string code = Source("UI", "GoalTrackerPanel.cs");
            Assert.True(code.Contains("Slot.Ensure(WindowTemplate.Panel)"), "a panel that does not stop the time");
            Assert.True(code.Contains("RunDirector.RunReady += OnRunReady"));
            string ready = Between(code, "private static void OnRunReady(", "private static void OnEvaluated(");
            Assert.True(ready.Contains("run.IsScenarioActive") && ready.Contains("ScenarioParams.TrackerAutoOpen"), "a scenario that goes, and the parameter trackerAutoOpen");
            Assert.True(code.Contains("PlaceTopRight(window)"));
        }

        [Test]
        public void TheTracker_UpdatesOncePerGameDay_AfterTheDirector_AndOnlyChangesTexts()
        {
            string code = Source("UI", "GoalTrackerPanel.cs");
            Assert.True(Regex.IsMatch(code, @"ctx\.OnTick\(TickPhase\.DayPassed,\s*OnDay,\s*AfterDirector\)"), "the day tick of the feature");
            Assert.True(code.Contains("private const int AfterDirector = -100;"), "after the pass of the director, whose priority is 0");

            string refresh = Between(code, "private static void Refresh()", "private static string BadgesText(");
            Assert.False(refresh.Contains("Clear(") || refresh.Contains("ModUI.") || refresh.Contains("new GameObject"), "no widget is built or destroyed on a tick");
            Assert.True(refresh.Contains("UiKit.SetText("), "only texts change, and only when they differ");

            string day = Between(code, "private static void OnDay()", "// The player closed the panel");
            Assert.True(day.Contains("Slot.IsOpen") && day.Contains("Refresh()"), "nothing is read when the panel is closed");
        }

        [Test]
        public void TheTracker_RemembersTheChoiceOfThePlayer_AndComesBackFromTheHub()
        {
            string code = Source("UI", "GoalTrackerPanel.cs");
            string closed = Between(code, "private static void OnClosed()", "private static void Remember(");
            Assert.True(closed.Contains("_leaving || !ScenariosFeature.IsActive") && closed.Contains("Remember(false)"), "the player's cross is remembered, the end of the scene and the feature going off are not");
            Assert.True(code.Contains("ModSettings.Set(ScenariosFeature.FeatureId, ScenarioParamSpecs.TrackerAutoOpenKey,"), "trackerAutoOpen in ModSettings");
            Assert.True(code.Contains("else Open(true)"), "opened from the hub: remembered as 'open it by itself'");
            Assert.True(code.Contains("internal static void ToggleFromHub()"));

            string hub = Source("ScenariosHubPage.cs");
            Assert.True(hub.Contains("GoalTrackerPanel.ToggleFromHub") && hub.Contains("scn.hub.tracker"), "the button on the page of the feature in the hub");
        }

        [Test]
        public void TheRace_ReadsTheCpusOfTheRivalsOnlyWhileARaceIsOpen()
        {
            string code = Source("UI", "GoalTrackerPanel.cs");
            string standing = Between(code, "private static RaceStanding StandingOf(", "private static bool HasOpenRace(");
            Assert.True(standing.Contains("if (!HasOpenRace(run)) return null;"), "no race, no pass over the CPUs");
            Assert.True(standing.Contains("director.RivalBestFrequency()") && standing.Contains("metrics.TryGet(PlayerBestMetric, out mine)") && code.Contains("PlayerBestMetric = \"cpu.maxFrequency\""), "the player's best from the metric, the rivals' from one pass");

            string source = Source("Integration", "GameMetricsSource.cs");
            string rival = Between(source, "private double ReadRivalBestFrequency()", "private void ReadDay(");
            Assert.True(rival.Contains("!cpu.IsReleased") && rival.Contains("ReferenceEquals(cpu.Company, player)"), "released CPUs of the companies other than the player's");
            Assert.True(Regex.Matches(rival, @"for\s*\(").Count == 1, "one loop");
        }

        [Test]
        public void TheTracker_CollapsesAndKeepsItsTopEdge()
        {
            string code = Source("UI", "GoalTrackerPanel.cs");
            string collapse = Between(code, "private static void ApplyCollapsed(", "private static void PlaceTopRight(");
            Assert.True(collapse.Contains("_scrollRoot.SetActive(!_collapsed)") && collapse.Contains("rect.sizeDelta ="), "the list is hidden and the window is smaller");
            Assert.True(collapse.Contains("rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y - change * 0.5f)"), "the window hangs on its centre: the top edge stays");
        }

        // ---- TSK-025: the results ----

        [Test]
        public void TheResults_AreModal_OpenOnResultReady_AndWriteTheProfile()
        {
            string code = Source("UI", "ScorecardWindow.cs");
            Assert.True(code.Contains("Slot.Ensure(WindowTemplate.Modal)"), "the time stands");
            Assert.True(code.Contains("RunDirector.ResultReady += OnResultReady"), "the director raises it at once, or after BatchEnded");
            string open = Between(code, "private static void Open(RunState run, ScenarioResult result)", "// ---- the contents ----");
            Assert.True(open.Contains("ProfileGate.Save()"), "ModProfile.Save() when the results are shown");

            string director = Source("Integration", "RunDirector.cs");
            string publish = Between(director, "private void Publish(RunPassResult result)", "private RunEngine BuildEngine(");
            Assert.True(publish.Contains("if (ModTicks.IsBatchRun) _deferredResult = result.Finished;"), "FR-9 item 6: a result decided in a month's batch run waits");
            string batch = Between(director, "private void OnBatchEnded()", "// ---- the events of the game");
            Assert.True(batch.Contains("RunDirector.ResultReady"), "and opens after BatchEnded");
        }

        [Test]
        public void TheResults_ShowTheTableOfTheCompanies_AndWatchTheTimeItTakes()
        {
            Assert.True(Source("UI", "ScorecardWindow.cs").Contains("ComparisonSection.Add(page, texts)"), "the results use the section the career summary shares");
            string code = Source("UI", "ComparisonSection.cs");
            Assert.True(code.Contains("director.BuildComparison()") && code.Contains("ComparisonTableBuilder.Rows(rows, texts)") && code.Contains("UiKit.ComparisonTable("), "the table is built when the window opens");
            Assert.True(code.Contains("TableBudgetMs = 50") && code.Contains("Stopwatch.StartNew()"), "NFR-1: 50 ms, and a slower build is a line in the log");
            Assert.True(Regex.IsMatch(code, @"catch\s*\(Exception ex\)\s*\{\s*ModLog\.Warn"), "a table that cannot be built leaves the rest of the window");

            string tick = Source("Integration", "RunDirector.cs");
            Assert.True(tick.Contains("internal ComparisonRow[] BuildComparison("), "built on demand, never on a tick");
            string day = Between(tick, "private void OnDayPassed()", "private void OnLateMonthPassed()");
            Assert.False(day.Contains("BuildComparison"), "not on a tick");
        }

        [Test]
        public void KeepPlaying_ClosesTheWindow_AndTheMainMenuAsksFirst_WithoutSaving()
        {
            string code = Source("UI", "ScorecardWindow.cs");
            Assert.True(code.Contains("scn.button.continue") && code.Contains("Slot.Close"), "'Keep playing' closes the window and the pause goes");
            Assert.True(code.Contains("ModUI.Confirm(\"scn.button.menu\", \"scn.menu.confirm\", QuitToMenu, null)"), "ModUI.Confirm; 'No' changes nothing");
            string quit = Between(code, "private static void QuitToMenu()", "}\n    }\n}");
            Assert.True(quit.Contains("GameFacts.QuitToMenu()"), "only on 'Yes'");

            string facts = Source("Integration", "GameFacts.cs");
            string method = Between(facts, "internal static bool QuitToMenu()", "/// <summary><c>ResearchSector.UnlockTechnologies");
            Assert.True(method.Contains("GameManager.Instance") && method.Contains("manager.QuitToMenu()"), "GameManager.QuitToMenu");

            string all = code + facts + Source("UI", "AchievementsWindow.cs");
            Assert.False(Regex.IsMatch(all, @"Autosave|SaveHandler|SaveSystem\.Save|\.Save\(\)\s*;.*QuitToMenu"), "no save of the game on the way out");
        }

        [Test]
        public void TheHubPage_CanShowTheResultsAgain()
        {
            string hub = Source("ScenariosHubPage.cs");
            Assert.True(hub.Contains("ScorecardWindow.HasResult") && hub.Contains("ScorecardWindow.OpenCurrent") && hub.Contains("scn.hub.results"));
            Assert.True(hub.Contains("AchievementsWindow.Open") && hub.Contains("scn.hub.achievements"), "the achievements from the hub");
        }

        // ---- TSK-026: the achievements and the records ----

        [Test]
        public void TheAchievementsWindow_IsModalInTheGame_AndAMenuPanelInTheMenu()
        {
            string code = Source("UI", "AchievementsWindow.cs");
            Assert.True(Regex.IsMatch(code, @"PTModHost\.CurrentScene\s*==\s*PTModHost\.GameScene\s*\?\s*WindowTemplate\.Modal\s*:\s*WindowTemplate\.MenuPanel"), "FR-14 item 3");
            Assert.True(code.Contains("ScenarioSetupSection.AchievementsRequested += Open"), "from the section of the new-game panel");
            Assert.True(code.Contains("AchievementsViewBuilder.Build(") && code.Contains("AchievementsViewBuilder.BuildRecords("), "the pages are the models'");
            Assert.True(code.Contains("ach.tab.achievements") && code.Contains("ach.tab.records"), "the two pages");
            Assert.True(code.Contains("window.SetTitle(UiKit.Safe(view.Title))"), "the title says 'Achievements: N / 37'");
            AssertGuarded(code, "the window");
        }

        [Test]
        public void ResettingTheProfile_AsksTwice_AndOnlyTheSecondYesResets()
        {
            string code = Source("UI", "AchievementsWindow.cs");
            Assert.True(code.Contains("ModUI.Confirm(\"ach.reset\", \"ach.reset.confirm1\", AskAgain, null)"), "the first question");
            Assert.True(code.Contains("ModUI.Confirm(\"ach.reset\", \"ach.reset.confirm2\", Reset, null,"), "the second question");
            Assert.Equal(1, Regex.Matches(code, @"ProfileGate\.Reset\(\)").Count, "one place resets");

            string ask = Between(code, "private static void AskToReset()", "private static void Reset()");
            Assert.False(ask.Contains("ProfileGate.Reset"), "neither of the questions resets by itself");
            string reset = Between(code, "private static void Reset()", "}\n    }\n}");
            Assert.True(reset.Contains("ProfileGate.Reset()"));

            string gate = Source("Integration", "ProfileGate.cs");
            string gateReset = Between(gate, "internal static void Reset()", "// The model over the live section");
            Assert.True(gateReset.Contains("model.Clear()") && gateReset.Contains("ModProfile.Save()"), "ProfileModel.Clear and ModProfile.Save()");
        }

        // ---- TSK-027: the pop-ups ----

        [Test]
        public void ThePopUps_AreShownThroughModNotify_WhenTheParameterAllows_AndOnlyAfterAPass()
        {
            string code = Source("UI", "AchievementToastQueue.cs");
            Assert.True(code.Contains("RunDirector.Evaluated += OnEvaluated"), "after every pass that brought something");
            Assert.True(code.Contains("!ScenarioParams.ToastsEnabled"), "toastsEnabled = false shows nothing (the profile was written before)");
            Assert.True(code.Contains("ModNotify.Show(ScenariosFeature.FeatureId, line)"), "ModNotify holds them back while the game loads, fast-forwards or runs a batch");
            Assert.True(code.Contains("ToastPlanner.Plan("), "up to three by name and 'and N more'");
            Assert.False(Regex.IsMatch(code, @"IsLoading|IsFastForwarding|IsBatchRun|Update\(|FrameUpdate|StartCoroutine"), "the clock is the core's: no waiting of its own here");
            AssertGuarded(code, "the queue");
        }

        // ---- TSK-028: the summary of the career ----

        [Test]
        public void TheCareerSummary_IsModal_OpensOnCareerEnded_AndWritesTheProfile()
        {
            string code = Source("UI", "CareerSummaryWindow.cs");
            Assert.True(code.Contains("Slot.Ensure(WindowTemplate.Modal)"), "the time stands while it is open");
            Assert.True(code.Contains("RunDirector.CareerEnded += OnCareerEnded"), "the director raises it on the year of the end of the game");
            Assert.True(code.Contains("\"scn.career.title\""), "the caption");
            AssertGuarded(code, "the window");

            string open = Between(code, "private static void Open(RunState run)", "// ---- the contents ----");
            Assert.True(open.Contains("window.Open()") && open.Contains("ProfileGate.Save()"), "the achievement of the end of the era was just earned: the profile is written");

            string ended = Between(code, "private static void OnCareerEnded()", "private static void Open(RunState run)");
            Assert.True(ended.Contains("run.IsScenarioActive"), "a scenario that goes has no career summary");
        }

        [Test]
        public void TheCareerSummary_ShowsTheTableTheAchievementsAndTheChallenges_AndKeepsPlaying()
        {
            string code = Source("UI", "CareerSummaryWindow.cs");
            string build = Between(code, "private static void Build(ModWindow window, RunState run)", "}\n    }\n}");
            Assert.True(build.Contains("CareerSummaryBuilder.Build("), "the words are the model's");
            Assert.True(build.Contains("ComparisonSection.Add(page, texts)"), "the same table as the results of a scenario");
            Assert.True(build.Contains("view.Achievements") && build.Contains("view.Challenges"), "the achievements of the game and its challenges");
            Assert.True(build.Contains("scn.button.continue") && build.Contains("Slot.Close"), "'Keep playing' closes the window and the time goes on");
            Assert.False(build.Contains("QuitToMenu") || build.Contains("ModUI.Confirm"), "no way out of the game from here: the window of the game is under it");
        }

        [Test]
        public void TheCareerSummary_LeavesTheWindowOfTheGameAlone()
        {
            // EndGameWindow opens by itself on DateController.OnYearPassed; nothing here patches it, waits for it or closes it
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements");
            foreach (string file in SourceScan.Files(root, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(file), false);
                Assert.False(code.Contains("EndGameWindow"), Path.GetFileName(file) + " does not touch the window of the game");
            }
        }

        [Test]
        public void TheEndYear_IsCountedFirst_AndInABatchRunTheSummaryWaitsForTheEndOfTheBatch()
        {
            string code = Source("Integration", "RunDirector.cs");
            string year = Between(code, "private void OnYearPassed()", "private void OnBatchEnded()");
            Assert.True(year.IndexOf("\"endyear.reached\"", StringComparison.Ordinal) < year.IndexOf("CareerEnded", StringComparison.Ordinal), "the achievement is there before the window is asked for its lines");
            Assert.True(Regex.IsMatch(year, @"if\s*\(\s*ModTicks\.IsBatchRun\s*\)\s*_careerPending\s*=\s*true;"), "kept while the month runs");

            string batch = Between(code, "private void OnBatchEnded()", "internal void OnCpuReleased(");
            Assert.True(batch.Contains("_careerPending = false") && batch.Contains("RunDirector.CareerEnded"), "shown when the batch ends, once");
            Assert.True(batch.IndexOf("RunDirector.CareerEnded", StringComparison.Ordinal) < batch.IndexOf("RunDirector.ResultReady", StringComparison.Ordinal),
                "the results of a scenario open last and stay on top");

            string leaving = Between(code, "private void OnSceneLeaving()", "// A load or a new game replaced the run");
            Assert.True(leaving.Contains("_careerPending = false"), "a summary that waited does not come to the next game");
            string replaced = Between(code, "private void OnRunReplaced()", "// ---- helpers ----");
            Assert.True(replaced.Contains("_careerPending = false"));
        }

        [Test]
        public void TheCareerSummary_CanBeOpenedAgainFromTheHub_FromTheLastYearOfTheGameOn()
        {
            string hub = Source("ScenariosHubPage.cs");
            Assert.True(hub.Contains("CareerSummaryWindow.CanOpenNow") && hub.Contains("CareerSummaryWindow.OpenCurrent") && hub.Contains("scn.hub.career"));

            string code = Source("UI", "CareerSummaryWindow.cs");
            string can = Between(code, "internal static bool CanOpenNow", "/// <summary>Opens the summary again");
            Assert.True(can.Contains("!run.IsScenarioActive") && can.Contains("ModTicks.IsGameReady") && can.Contains("GameFacts.EndYear()"), "the conditions the window opens on by itself");
            string again = Between(code, "internal static void OpenCurrent()", "private static void OnCareerEnded()");
            Assert.True(again.Contains("if (!CanOpenNow) return;"));
            AssertGuarded(again, "the button");
        }

        // ---- the feature ----

        [Test]
        public void TheFeature_InstallsEveryWindow_TrackerBeforeTheBriefing()
        {
            string code = Source("ScenariosFeature.cs");
            foreach (string install in new[] { "ChallengesWindow.Install", "AchievementsWindow.Install", "GoalTrackerPanel.Install(ctx)", "ScenarioMailer.Install", "ScorecardWindow.Install", "CareerSummaryWindow.Install", "AchievementToastQueue.Install" })
            {
                Assert.True(code.Contains(install), install);
            }
            Assert.True(code.IndexOf("GoalTrackerPanel.Install(ctx)", StringComparison.Ordinal) < code.IndexOf("ScenarioMailer.Install", StringComparison.Ordinal),
                "the tracker opens first, so the modal briefing stays on top");
            Assert.True(code.IndexOf("ScenarioSetupSection.Install", StringComparison.Ordinal) > 0);
        }

        [Test]
        public void TheWindowsAreMadeThroughModUI_AndNeverPatchTheGame()
        {
            foreach (string file in new[] { "ChallengesWindow.cs", "BriefingWindow.cs", "GoalTrackerPanel.cs", "ScorecardWindow.cs", "CareerSummaryWindow.cs", "ComparisonSection.cs", "AchievementsWindow.cs", "AchievementToastQueue.cs", "WindowSlot.cs", "UiKit.cs" })
            {
                string code = Source("UI", file);
                Assert.False(code.Contains("HarmonyLib") || code.Contains("[ModPatch"), file + " patches nothing");
                Assert.False(Regex.IsMatch(code, @"new\s+GameObject\(\s*""[^""]*Window"), file + " clones windows through ModUI.CreateWindow only");
            }
            Assert.True(Source("UI", "WindowSlot.cs").Contains("ModUI.CreateWindow(_id, template, _titleKey, _size)"));
            Assert.True(Regex.Matches(Source("UI", "WindowSlot.cs"), "ModUI.IsReady").Count >= 1, "the templates of the scene are looked up first");
        }

        // ---- the texts ----

        private static readonly Regex KeyLiteral = new Regex("\"((?:scn|chl|ach)\\.[A-Za-z0-9.\\-]*[A-Za-z0-9])\"", RegexOptions.Compiled);

        [Test]
        public void EveryKeyTheWindowsAndTheModelsAskFor_IsInBothTables()
        {
            var tables = new L10nTables();
            var problems = new List<string>();
            tables.AddJson("ru", "l10n/ru/scenarios-achievements.json", ScenarioTestData.ReadStrings("ru"), problems);
            tables.AddJson("en", "l10n/en/scenarios-achievements.json", ScenarioTestData.ReadStrings("en"), problems);
            Assert.Equal(0, problems.Count);

            string root = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements");
            var files = new List<string>();
            foreach (string folder in new[] { "Logic" + Path.DirectorySeparatorChar + "Ui", "UI" })
            {
                files.AddRange(SourceScan.Files(Path.Combine(root, folder), ".cs"));
            }
            files.Add(Path.Combine(root, "Integration", "ScenarioMailer.cs"));
            files.Add(Path.Combine(root, "Integration", "GameUiTexts.cs"));
            files.Add(Path.Combine(root, "ScenariosHubPage.cs"));

            string[] suffixes = { ".one", ".few", ".many", ".other" };
            var missing = new List<string>();
            int seen = 0;
            foreach (string path in files)
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match m in KeyLiteral.Matches(code))
                {
                    string key = m.Groups[1].Value;
                    if (key.EndsWith(".", StringComparison.Ordinal)) continue;
                    seen++;
                    foreach (string language in new[] { "ru", "en" })
                    {
                        bool has = tables.Contains(language, key) || suffixes.Any(s => tables.Contains(language, key + s));
                        if (!has) missing.Add(language + ": " + key + " (" + Path.GetFileName(path) + ")");
                    }
                }
            }
            Assert.True(seen > 60, "the scan found only " + seen + " keys: is it broken?");
            Assert.True(missing.Count == 0, "keys the windows ask for that a table lacks:\n  " + string.Join("\n  ", missing.Distinct()));
        }

        [Test]
        public void TheCoreKeysTheWindowsUse_AreInTheTablesOfTheCore()
        {
            // "core.common.close" is the one core string the windows use; the architecture test checks every core key in the code, this one names the dependency
            string all = Source("UI", "ChallengesWindow.cs") + Source("UI", "AchievementsWindow.cs");
            Assert.True(all.Contains("core.common.close"));
        }
    }
}
