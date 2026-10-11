using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Features.ScenariosAchievements.Logic;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-016 .. TSK-021: the layer that talks to the game (<c>Features/ScenariosAchievements/Integration</c> and <c>UI</c>) needs Unity and the game and cannot run in the
    /// test exe, so its source is read as text, like the seam tests of the core do. What is checked is what the acceptance of each task says about the wiring: which game calls
    /// the day sample makes, which events are subscribed and where, what the guards cover, what runs only once, and that the pure arithmetic behind each class is the tested one.
    /// </summary>
    [Covers(typeof(ModifierScopes))]
    public class IntegrationSeamTests
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

        // ---- TSK-016: the samples ----

        [Test]
        public void TheDaySample_IsOnePassOverTheCpusOfTheGame_WithAReusedList()
        {
            string code = Source("Integration", "GameMetricsSource.cs");
            Assert.True(Regex.IsMatch(code, @"provider\.GetAllCpus\(\s*_cpus\s*\)"), "CpuDataProvider.GetAllCpus(reusableList) with a list that lives for the session");
            Assert.Equal(3, Regex.Matches(code, @"GetAllCpus\(").Count, "the day, the table and the best CPU of the rivals (the tracker's race line): one pass each");

            string day = Between(code, "private void ReadDay(", "private int CountOemContracts");
            Assert.Equal(1, Regex.Matches(day, @"for\s*\(").Count, "one loop in the day pass");
            Assert.False(day.Contains("new List<") || day.Contains("ToList(") || day.Contains("ToArray(") || day.Contains("new Dictionary"), "no allocation per CPU");
            Assert.True(day.Contains("_sampler.Begin(") && day.Contains("_sampler.Add(ref row)") && day.Contains("_sampler.End()"), "the arithmetic is the sampler's");
        }

        [Test]
        public void TheDaySample_ReadsThePlayersCompany_AndTheContractsAsTheDesignSays()
        {
            string code = Source("Integration", "GameMetricsSource.cs");
            foreach (string call in new[] { "player.MoneyAmount", "player.IsInDefault", "player.IsBankrupt", "ProductionCapacity", "GameContractHelper.PlayerContracts()",
                                            "manager.GetSignedContracts(player)", "gamePlayer.DifficultyLevel" })
            {
                Assert.True(code.Contains(call), "the day sample reads " + call);
            }
            Assert.True(code.Contains("contract.Provider") && code.Contains("contract.Client"), "foundry clients and contracts as a client are told apart");

            string cpus = Source("Integration", "GameCpus.cs");
            foreach (string field in new[] { "SoldLastDay", "SoldLastDayByHighEnd", "SoldLastDayByMidRange", "SoldLastDayByLowEnd", "SoldLastDayByIndustries", "MissedSalesLastDay", ".Ips", ".Popularities" })
            {
                Assert.True(cpus.Contains(field), "the row of a CPU reads " + field);
            }
            Assert.True(cpus.Contains("ReferenceEquals(cpu.Company, player)"), "a CPU is the player's by the company that designed it, as MarketDataProvider.MarketShare counts it");
        }

        [Test]
        public void TheTransferredCpus_AreHandedToTheSampler()
        {
            string director = Source("Integration", "RunDirector.cs");
            Assert.True(director.Contains("_source.SampleDay(_engine == null ? null : _engine.Run.Metrics.TransferredCpuIds)"), "the ids of the run's section reach the pass over the CPUs");
        }

        [Test]
        public void TheMonthSample_FindsTheReportByYearAndMonth_NotByIndex()
        {
            string code = Source("Integration", "GameMetricsSource.cs");
            Assert.True(code.Contains("MonthSampler.Build("), "the pure sampler looks the report up by (year, month)");
            Assert.True(code.Contains("player.Finance.Reports"), "CompanyFinance.Reports");
            Assert.True(code.Contains("_list[index].Year") && code.Contains("_list[index].Month") && code.Contains("TotalRevenues") && code.Contains("TotalExpenses") && code.Contains("RevenueByClients"),
                "the reports are read for what the metrics need");
            Assert.False(Regex.IsMatch(code, @"Reports\[\s*[^\]]*Count\s*-\s*2"), "no index of the last full month: the order of the handlers of the month must not matter");
        }

        [Test]
        public void TheComparison_UsesTheAiCompaniesOfTheGame_AndTheLastFullMonthOfTheSales()
        {
            string code = Source("Integration", "GameMetricsSource.cs");
            Assert.True(code.Contains("GameDataFinder.FindAllAICompanies(true)"), "DataFinder.FindAllAICompanies(true)");
            Assert.True(code.Contains("data.Sold[data.Sold.Count - 2]"), "CpuBase.Market.Sold[Count - 2]");
            Assert.True(code.Contains("ComparisonBuilder.Build("), "the table is picked by the tested builder");
            Assert.True(code.Contains("internal ComparisonRow[] BuildComparison(int maxAi)"));
        }

        // ---- TSK-017: the events ----

        [Test]
        public void TheCoreEvents_AreSubscribed_AndTheFastForwardIsIgnored()
        {
            string code = Source("Integration", "GameEventHooks.cs");
            Assert.True(code.Contains("ModEvents.CpuReleased += OnCpuReleased") && code.Contains("ModEvents.CpuTransferred += OnCpuTransferred"));

            string released = Between(code, "private static void OnCpuReleased(", "private static void OnCpuTransferred(");
            Assert.True(Regex.IsMatch(released, @"if\s*\(\s*duringFastForward\s*\)\s*return\s*;"), "FR-6 item 4");
            Assert.True(released.IndexOf("duringFastForward", StringComparison.Ordinal) < released.IndexOf("ModHooks.Guard", StringComparison.Ordinal), "before anything is read");

            string transferred = Between(code, "private static void OnCpuTransferred(", "internal static void OnContractPromoted(");
            Assert.True(transferred.Contains("ModCompanies.IsPlayer(to)") && transferred.Contains("OnCpuTransferredToPlayer(cpu.SaveID)"), "the SaveID of a CPU that moved to the player");
        }

        [Test]
        public void TheEventsOfTheScene_AreSubscribedOnGameReady_AndLetGoWithTheScene()
        {
            string hooks = Source("Integration", "GameEventHooks.cs");
            Assert.True(hooks.Contains("handler.OnCompanyWentBankrupt += _onBankrupt") && hooks.Contains("manager.OnPlayerSignedContract += _onSigned"));
            Assert.True(hooks.Contains("handler.OnCompanyWentBankrupt -= _onBankrupt") && hooks.Contains("manager.OnPlayerSignedContract -= _onSigned"), "let go of");
            Assert.True(Regex.IsMatch(hooks, @"static\s+bool\s+Attach\(\)\s*\{\s*Detach\(\);"), "never subscribed twice");

            string director = Source("Integration", "RunDirector.cs");
            string ready = Between(director, "private void OnGameReady(", "internal void OnFeatureSwitchedOn()");
            Assert.True(ready.Contains("GameEventHooks.Attach()"), "on every GameReady, because the game clears these events with the scene");
            string leaving = Between(director, "private void OnSceneLeaving(", "private void OnRunReplaced()");
            Assert.True(leaving.Contains("GameEventHooks.Detach()"));
        }

        [Test]
        public void TheBankruptcyHandler_IgnoresLoadingAndFastForward_SendsThePlayerToALoss_AndFiltersTheAbsorbed()
        {
            string code = Source("Integration", "GameEventHooks.cs");
            string body = Between(code, "private static void OnCompanyBankrupt(", "private static void OnBusinessSigned(");
            Assert.True(Regex.IsMatch(body, @"if\s*\(\s*ModTicks\.IsLoading\s*\|\|\s*ModTicks\.IsFastForwarding\s*\)\s*return\s*;"), "E13");
            Assert.True(body.Contains("ModCompanies.IsPlayer(company)") && body.Contains("director.OnPlayerBankrupt()"), "the player: a loss");
            Assert.True(body.Contains("director.OnRivalBankrupt(AcquisitionBridge.IsAbsorbedByPlayer(uniqueId))"), "an AI company: a rival, unless the player absorbed it (FR-12 item 7)");

            string signed = Between(code, "private static void OnBusinessSigned(", "RunDirector director = RunDirector.Instance;");
            Assert.True(signed.Contains("ModTicks.IsLoading"), "a contract of a loading save is no signing");
        }

        [Test]
        public void TheAcquisitionBridge_CallsThePublicApi_AndNeverThrows()
        {
            string code = Source("Integration", "AcquisitionBridge.cs");
            Assert.True(code.Contains("RivalAcquisitionsApi.IsAvailable && RivalAcquisitionsApi.IsAbsorbedByPlayer(uniqueId)"));
            Assert.True(code.Contains("catch (Exception"), "a broken API is no reason to lose a bankruptcy");
            Assert.False(code.Contains("RivalAcquisitions."), "only the public API of that feature");
        }

        // ---- TSK-019: the director ----

        [Test]
        public void TheDirector_SubscribesTheTicksTheCoreEventsAndTheSection()
        {
            string code = Source("Integration", "RunDirector.cs");
            Assert.True(code.Contains("ctx.OnTick(TickPhase.DayPassed, director.OnDayPassed)"));
            Assert.True(code.Contains("ctx.OnTick(TickPhase.LateMonthPassed, director.OnLateMonthPassed)"));
            Assert.True(code.Contains("ctx.OnTick(TickPhase.YearPassed, director.OnYearPassed)"));
            Assert.True(code.Contains("ModTicks.GameReady += director.OnGameReady"));
            Assert.True(code.Contains("ModTicks.BatchEnded += director.OnBatchEnded"));
            Assert.True(code.Contains("RunSaveParticipant.Instance.RunReplaced += director.OnRunReplaced"));
            Assert.False(Regex.IsMatch(code, @"TickOptions\.RunDuring"), "the ticks stay silent during the fast-forward and the loading");
        }

        [Test]
        public void TheModifiers_AreAddedOnce_OnlyForANewGame()
        {
            string code = Source("Integration", "RunDirector.cs");
            Assert.Equal(2, Regex.Matches(code, @"ModifierBinder\.Apply\(").Count, "the scenario's and the challenges' records");

            string start = Between(code, "private void StartNewRun()", "private void RestoreRun()");
            Assert.Equal(2, Regex.Matches(start, @"ModifierBinder\.Apply\(").Count, "both in the start of a new run");
            string restore = Between(code, "private void RestoreRun()", "private void OnDayPassed()");
            Assert.False(restore.Contains("ModifierBinder") || restore.Contains("ModModifiers"), "a loaded run never adds them again: the core restores the records");
            Assert.True(start.Contains("run.ScenarioEntryIds.AddRange(") && start.Contains("run.ChallengeStates.Add("), "the ids of the records go into the section of the run");

            string binder = Source("Integration", "ModifierBinder.cs");
            Assert.True(binder.Contains("ModModifiers.Add(ScenariosFeature.FeatureId, spec.Channel"), "owned by the feature");
            Assert.True(binder.Contains("ModModifiers.Remove(id)"), "taken away by id");
        }

        [Test]
        public void ALoadedGame_GetsALegacyRun_OnlyWhenTheSectionGaveNone()
        {
            string code = Source("Integration", "RunDirector.cs");
            string restore = Between(code, "private void RestoreRun()", "private void OnDayPassed()");
            Assert.True(Regex.IsMatch(restore, @"if\s*\(\s*run\s*==\s*null\s*\)"), "no run in the section: legacy");
            Assert.True(restore.Contains("RunState.CreateLegacy("));
            Assert.True(code.Contains("reason == GameReadyReason.NewGame) StartNewRun();") && code.Contains("else RestoreRun();"), "the two cases of GameReady");
        }

        [Test]
        public void EveryEntryOfTheDirector_RunsUnderTheGuard_AndEveryBlockOfTheEngineHasItsOwn()
        {
            string code = Source("Integration", "RunDirector.cs");
            Assert.True(Regex.IsMatch(code, @"private static bool Block\(string name, Action body\)\s*\{\s*return ModHooks\.Guard\(ScenariosFeature\.FeatureId, SitePrefix \+ name, body\);"),
                "the guard of a block of the engine");
            Assert.True(code.Contains("new RunEngine(run, challenges, _evaluator, _conditions, _ports, Block,"), "handed to the engine");
            foreach (string entry in new[] { "OnDayPassed", "OnLateMonthPassed", "OnYearPassed", "OnGameReady", "OnBatchEnded", "OnCpuReleased", "OnOemSigned", "OnPlayerBankrupt", "OnRivalBankrupt" })
            {
                Match match = Regex.Match(code, @"void " + entry + @"\([^)]*\)\s*\{\s*Guard\(");
                Assert.True(match.Success, entry + " runs in Guard(...)");
            }

            string engine = Source("Logic", "Run", "RunEngine.cs");
            foreach (string block in new[] { "\"metrics\"", "\"scenario\"", "\"challenges\"", "\"achievements\"" })
            {
                Assert.True(engine.Contains("Block(" + block), "the engine has the block " + block);
            }
        }

        [Test]
        public void TheDirector_HasNoCountersOfErrorsOfItsOwn()
        {
            string code = Source("Integration", "RunDirector.cs");
            Assert.False(Regex.IsMatch(code, @"\b(faultCount|_faults|errorCount|_errorCount|failures)\b"), "the core counts the faults and trips the breaker");
        }

        [Test]
        public void AResultDecidedInABatchRun_WaitsForTheEndOfTheBatch()
        {
            string code = Source("Integration", "RunDirector.cs");
            string publish = Between(code, "private void Publish(", "private RunEngine BuildEngine(");
            Assert.True(Regex.IsMatch(publish, @"if\s*\(\s*ModTicks\.IsBatchRun\s*\)\s*_deferredResult\s*=\s*result\.Finished;"), "kept while the month runs");
            string batch = Between(code, "private void OnBatchEnded()", "internal void OnCpuReleased(");
            Assert.True(batch.Contains("_deferredResult") && batch.Contains("RunDirector.ResultReady"), "shown when it ends");
        }

        [Test]
        public void TheEndOfTheGame_IsOnlyForARunWithNoScenarioGoing()
        {
            string code = Source("Integration", "RunDirector.cs");
            string year = Between(code, "private void OnYearPassed()", "private void OnBatchEnded()");
            Assert.True(year.Contains("GameFacts.EndYear()") && year.Contains("engine.Run.IsScenarioActive"));
            Assert.True(year.Contains("\"endyear.reached\"") && year.Contains("RunDirector.CareerEnded"));
        }

        [Test]
        public void TheChecksStop_WhenTheFeatureIsOff_AndWhenTheGameIsNotReady()
        {
            string code = Source("Integration", "RunDirector.cs");
            string ready = Between(code, "private bool Ready(out RunEngine engine)", "private static LiveRun Live()");
            Assert.True(ready.Contains("ScenariosFeature.IsActive") && ready.Contains("ModTicks.IsGameReady"));
        }

        [Test]
        public void TheOnboardingBridge_AsksForTheModeAndListensToTheEnd_OnlyThroughThePublicApi()
        {
            string code = Source("Integration", "OnboardingBridge.cs");
            Assert.True(code.Contains("OnboardingService.SetPendingMode(pending, source)"));
            Assert.True(code.Contains("OnboardingService.Completed += OnCompleted"));
            Assert.True(code.Contains("result.IsClean"), "skipped or aborted is not 'graduated'");
            Assert.True(code.Contains("PendingMode.ForcedOn") && code.Contains("PendingMode.ForcedOff") && code.Contains("PendingMode.Allowed"));
            Assert.True(code.Contains("\"scenario:\" + scenarioId"), "the source is scenario:<id>");

            string director = Source("Integration", "RunDirector.cs");
            Assert.True(director.Contains("OnboardingBridge.SubscribeCompleted(director.OnOnboardingCompleted)"));
            Assert.True(director.Contains("\"onboarding.completed.clean\""));
        }

        [Test]
        public void TheFeatureRegistersEverything_AndOneBrokenLinkDoesNotStopTheOthers()
        {
            string code = Source("ScenariosFeature.cs");
            foreach (string call in new[] { "RunDirector.Install(ctx)", "GameEventHooks.Install", "ScenarioStarter.Install", "ScenarioSetupSection.Install", "FeatureWatch.Install" })
            {
                Assert.True(code.Contains(call), "Register makes " + call);
            }
            Assert.True(Regex.IsMatch(code, @"private static void Guarded\(string what, Action link\)\s*\{\s*try"), "each link is guarded");
        }

        // ---- TSK-020: the start ----

        [Test]
        public void TheStart_IsWrittenInBeforeStart_AndNowhereElse()
        {
            string code = Source("Integration", "ScenarioStarter.cs");
            Assert.True(code.Contains("ModNewGame.BeforeStart += OnBeforeStart"));
            Assert.True(code.Contains("StartParameters.Apply(before, pending.Scenario.Start)"), "the tested arithmetic");
            Assert.Equal(1, Regex.Matches(code, @"\bWrite\(data, StartParameters\.Apply").Count, "the scenario's numbers are written once");

            foreach (string field in new[] { "data.Year =", "data.CompanyType =", "data.DifficultyLevel =", "data.StartingTechnology =", "data.StartingFunds =", "data.FactorySize =", "data.EnableCheats =" })
            {
                Assert.True(code.Contains(field), "the start sets " + field.TrimEnd(' ', '='));
            }
            foreach (string path in SourceScan.Files(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements"), ".cs"))
            {
                if (path.EndsWith("ScenarioStarter.cs", StringComparison.Ordinal)) continue;
                string other = SourceScan.Strip(SourceScan.ReadText(path), true);
                Assert.False(Regex.IsMatch(other, @"\bPlayerInitialData\b.*\.(Year|StartingFunds|FactorySize|CompanyType)\s*="), SourceScan.Relative(SourceScan.SrcDirOrFail(), path) + " must not change PlayerInitialData");
            }
        }

        [Test]
        public void AFailedStart_PutsTheFieldsBack_StartsVanilla_AndTellsThePlayer()
        {
            string code = Source("Integration", "ScenarioStarter.cs");
            string handler = Between(code, "internal static void OnBeforeStart(", "internal static PendingRun TakePending()");
            Assert.True(handler.Contains("StartFields before = Read(data);"), "the copy is taken before anything is changed");
            int catchAt = handler.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
            Assert.True(catchAt > 0);
            string failure = handler.Substring(catchAt);
            Assert.True(failure.Contains("Write(data, before)"), "the fields come back from the copy");
            Assert.True(failure.Contains("Pending = PendingRun.Failed()"), "the game goes on as free play");
            Assert.True(failure.Contains("ModHooks.ReportFault"), "counted against the feature");

            string after = Between(code, "internal static void AfterStart(", "private static void OnSceneReady(");
            Assert.True(after.Contains("pending.StartFailed") && after.Contains("ModNotify.ShowKey(ScenariosFeature.FeatureId, \"scn.toast.startFailed\")"), "'The scenario could not be started'");
        }

        [Test]
        public void AfterTheStart_TheTechnologiesAreUnlocked_AndTheMoneyAndLinesAreChecked()
        {
            string code = Source("Integration", "ScenarioStarter.cs");
            string after = Between(code, "internal static void AfterStart(", "private static void OnSceneReady(");
            Assert.True(after.Contains("start.TechYear > 0") && after.Contains("GameFacts.UnlockTechnologies(start.TechYear)"), "FR-4 item 3");
            Assert.True(after.Contains("StartParameters.Check(start, money, lines)") && after.Contains("ModLog.Warn"), "FR-4 item 5: a difference is a line in the log");

            string facts = Source("Integration", "GameFacts.cs");
            Assert.True(facts.Contains("ResearchSector.UnlockTechnologies(year, 0)"));
            Assert.True(facts.Contains("company.MoneyAmount") && facts.Contains("ProductionCapacity"));
        }

        [Test]
        public void TheStarter_TakesTheChoiceFromTheSelection_AndForgetsItWhenTheMenuIsShownAgain()
        {
            string code = Source("Integration", "ScenarioStarter.cs");
            Assert.True(code.Contains("Selection.ToPending(ScenarioCatalog.FindScenario)"));
            Assert.True(code.Contains("Selection.Revalidate("), "a challenge that no longer fits the final type is left out");
            string menu = Between(code, "private static void OnSceneReady(", "private static StartFields Read(");
            Assert.True(menu.Contains("PTModHost.MainMenuScene") && menu.Contains("Selection.Clear()") && menu.Contains("Pending = null"));
        }

        // ---- TSK-021: the section ----

        [Test]
        public void TheSection_IsRegisteredAtOrderTen_AndFollowsThePreview()
        {
            string code = Source("UI", "ScenarioSetupSection.cs");
            Assert.True(code.Contains("internal const int Order = 10;"));
            Assert.True(code.Contains("ModNewGame.RegisterSection(ScenariosFeature.FeatureId, Build, Order)"));
            Assert.True(code.Contains("ModNewGame.PreviewChanged += OnPreviewChanged"));
            string preview = Between(code, "private static void OnPreviewChanged()", "private static void RefreshTexts()");
            Assert.True(preview.Contains("OnSelectionChanged()") && !preview.Contains("MenuSetupLocks"), "only the texts (and the challenges that still fit) follow the settings of the window");
            Assert.True(code.Contains("var shown = ModNewGame.Preview;") && code.Contains("StartParameters.Matches(start,"), "FR-3 item 7: the texts are made from ModNewGame.Preview, read only");
            Assert.False(code.Contains("PlayerInitialData"), "the section never touches the data of the start; it is written in BeforeStart");
        }

        [Test]
        public void ChoosingAScenario_LocksTheListsAsksTheTutorial_AndFreePlayGivesThemBack()
        {
            string code = Source("UI", "ScenarioSetupSection.cs");
            string picked = Between(code, "private static void OnModePicked(int index)", "private static void OnSelectionChanged()");
            Assert.True(picked.Contains("ScenarioStarter.Selection.SelectScenario("));
            Assert.True(picked.Contains("MenuSetupLocks.Apply(picked.Start)") && picked.Contains("OnboardingBridge.ApplyScenarioMode(picked.Onboarding, picked.Id)"));
            Assert.True(picked.Contains("MenuSetupLocks.Release()") && picked.Contains("OnboardingBridge.ApplyScenarioMode(\"allowed\")"), "E1: the values of the player and the tutorial's choice come back");
            Assert.True(picked.Contains("ModHooks.Guard"));
        }

        [Test]
        public void TheLocks_ReadThePrivateFieldsThroughAccessTools_AndSkipWhenTheyAreNotFound()
        {
            string code = Source("UI", "MenuSetupLocks.cs");
            foreach (string field in new[] { "companyTypeDropdown", "difficultyDropdown", "startDateDropdown", "enableCheatsToggle" })
            {
                Assert.True(code.Contains("\"" + field + "\""), field);
            }
            Assert.True(code.Contains("AccessTools.Field(window,"));
            Assert.True(code.Contains("Interactable = false") && code.Contains("Interactable = true"));
            Assert.True(Regex.IsMatch(code, @"if\s*\(\s*!Resolve\(\)\s*\)\s*return;"), "a missing field: no lock");
            Assert.True(code.Contains("ModLog.Warn(") && code.Contains("were not all found"), "reported once");

            // the difficulty is set before the company type: "Impossible" rebuilds the list of types and switches the cheats by itself
            string apply = Between(code, "internal static bool Apply(", "internal static void Release()");
            Assert.True(apply.IndexOf("Difficulty.SetSelected", StringComparison.Ordinal) < apply.IndexOf("Type.SetSelected", StringComparison.Ordinal));
            Assert.True(apply.Contains("start.AllowCheats") && apply.Contains("Cheats.IsOn = false"), "FR-3 item 5");

        }

        [Test]
        public void TheStartYearsOfTheContent_AreTheIndexesTheLocksUse()
        {
            string code = Source("UI", "MenuSetupLocks.cs");
            Assert.True(Regex.IsMatch(code, @"Math\.Max\(0,\s*\(year\s*-\s*1975\)\s*/\s*5\)"), "1975 + 5 x index, as MenuGameSetup.UpdateStartingYear");
            for (int i = 0; i < ContentVocabulary.StartYears.Count; i++)
            {
                Assert.Equal(1975 + 5 * i, ContentVocabulary.StartYears[i]);
            }
        }

        [Test]
        public void TheButtons_OnlyAnnounceTheClick_AndAreInactiveWithoutAListener()
        {
            string code = Source("UI", "ScenarioSetupSection.cs");
            Assert.True(code.Contains("internal static event Action ChallengesRequested;") && code.Contains("internal static event Action AchievementsRequested;"));
            Assert.True(code.Contains("_challengesButton.Interactable = ChallengesRequested != null"));
            Assert.True(code.Contains("ModL10n.L(\"chl.button\"") && code.Contains("ModL10n.L(\"ach.button\")"), "'Challenges (N)...' and 'Achievements...'");
        }

        // ---- the modifier scopes ----

        [Test]
        public void TheScopeOfAModifier_FollowsAppliesTo()
        {
            Assert.Equal(PTMod.Core.ScopeKind.Player, ModifierScopes.Of("player").Kind);
            Assert.Equal(PTMod.Core.ScopeKind.AIOnly, ModifierScopes.Of("ai").Kind);
            Assert.Equal(PTMod.Core.ScopeKind.AllCompanies, ModifierScopes.Of("all").Kind);
            Assert.Equal(PTMod.Core.ScopeKind.Player, ModifierScopes.Of(null).Kind, "a safe default");
            Assert.Equal(PTMod.Core.ScopeKind.Player, ModifierScopes.Of("everybody").Kind);
        }

        [Test]
        public void EveryModifierOfTheContent_HasAScopeTheBinderMaps()
        {
            foreach (ScenarioDefinition scenario in ScenarioTestData.StandardScenarios())
            {
                foreach (ModifierSpec spec in scenario.Modifiers) Assert.True(ContentVocabulary.Contains(ContentVocabulary.Scopes, spec.AppliesTo), scenario.Id);
            }
            foreach (ChallengeDefinition challenge in ScenarioTestData.StandardChallenges())
            {
                foreach (ModifierSpec spec in challenge.Modifiers)
                {
                    Assert.True(ContentVocabulary.Contains(ContentVocabulary.Scopes, spec.AppliesTo), challenge.Id);
                    Assert.NotEqual(PTMod.Core.ScopeKind.Global, ModifierScopes.Of(spec.AppliesTo).Kind, challenge.Id);
                }
            }
        }

        // ---- the texts the layer asks for ----

        // A key has a text when it is a key of the table or the base of a plural group (key.one, ...).
        private static bool HasText(PTMod.Core.Logic.L10n.L10nTables tables, string language, string key)
        {
            return tables.Contains(language, key) || tables.Contains(language, key + ".one");
        }

        [Test]
        public void EveryKeyTheLayerAsksFor_IsInBothLanguages()
        {
            var tables = new PTMod.Core.Logic.L10n.L10nTables();
            foreach (string language in new[] { "ru", "en" })
            {
                var problems = new System.Collections.Generic.List<string>();
                tables.AddJson(language, "l10n/" + language + "/scenarios-achievements.json", ScenarioTestData.ReadStrings(language), problems);
                Assert.Equal(0, problems.Count, string.Join("; ", problems));
            }

            var keys = new System.Collections.Generic.List<string>();
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ScenariosAchievements");
            foreach (string folder in new[] { "Integration", "UI" })
            {
                foreach (string path in SourceScan.Files(Path.Combine(root, folder), ".cs"))
                {
                    string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                    foreach (Match match in Regex.Matches(code, @"""((?:scn|chl|ach)\.[A-Za-z0-9.\-]+)"""))
                    {
                        string key = match.Groups[1].Value;
                        if (!key.EndsWith(".", StringComparison.Ordinal)) keys.Add(key);
                    }
                }
            }
            Assert.True(keys.Count > 5, "the layer asks for texts");

            // keys that are built from a prefix and an id (scn.medal.<id>, scn.difficulty.<n>, ...) are covered by the tables of the content and by the ones below
            foreach (string key in keys.Distinct())
            {
                if (key.EndsWith(".title", StringComparison.Ordinal) && key.Split('.').Length > 3) continue;
                Assert.True(HasText(tables, "ru", key) && HasText(tables, "en", key), "no text for " + key);
            }
            for (int level = 0; level <= 5; level++) Assert.True(tables.Contains("ru", "scn.difficulty." + level) && tables.Contains("en", "scn.difficulty." + level), "difficulty " + level);
            for (int tech = 0; tech <= 4; tech++) Assert.True(tables.Contains("ru", "scn.tech." + tech) && tables.Contains("en", "scn.tech." + tech), "technology " + tech);
            foreach (string type in ContentVocabulary.CompanyTypes) Assert.True(tables.Contains("ru", "scn.companyType." + type) && tables.Contains("en", "scn.companyType." + type), type);
            foreach (string medal in ContentVocabulary.Medals) Assert.True(tables.Contains("ru", "scn.medal." + medal) && tables.Contains("en", "scn.medal." + medal), medal);
        }
    }
}
