using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Content;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// The life of a run (TSK-019, design.md "Интеграция"; FR-16, FR-4 item 2, FR-1 item 4, NFR-2): the one place that connects the game's events and ticks to the
    /// <see cref="RunEngine"/> and tells the windows what happened. The engine knows the rules; this class knows the game.
    /// <list type="bullet">
    /// <item><b>A new game</b> (<c>ModTicks.GameReady(NewGame)</c>): the run is made from the pending run of the new-game window (<see cref="ScenarioStarter"/>) - its id, the
    /// snapshot of the scenario, the challenges, the assisted flag (cheats), the features that are on - and the records of <c>ModModifiers</c> of the scenario and of every
    /// challenge are added <b>once</b>; their ids go into the section of the run. Then <see cref="ScenarioStarter.AfterStart"/>, the profile counter, the sweep of the event
    /// achievements, and <see cref="RunReady"/> (the briefing, the tracker).</item>
    /// <item><b>A loaded game</b> (<c>GameReady(Loaded)</c>): the run is the one the section restored; a save without a section, or with one the core reset as "newer", gets a
    /// <c>legacy</c> run born in memory (no scenario, assisted if the game has cheats on, the day of the load as its start). The records of the modifiers are not added
    /// again: the core keeps and restores them. Checks start with the next day.</item>
    /// <item><b>The ticks</b> (<c>ctx.OnTick</c>): <c>DayPassed</c> (the day: metrics, goals, challenges, achievements), <c>LateMonthPassed</c> (the month), <c>YearPassed</c> (the
    /// last year of the game: the event <c>endyear.reached</c> and <see cref="CareerEnded"/> in a run with no scenario going). The core does not call them during the fast-forward
    /// and while a save loads, and in a month's batch run they run day by day; the windows of a result that was decided in a batch run, and the summary of the career when the last year of the game began in one,
    /// wait for <c>ModTicks.BatchEnded</c>.</item>
    /// <item><b>The events</b> come from <see cref="GameEventHooks"/> and the patch of <see cref="ScenarioPatches"/>; the scene's own events are subscribed on every
    /// <c>GameReady</c> and let go when the scene goes.</item>
    /// <item><b>Faults.</b> Every entry runs in <c>ModHooks.Guard</c>; inside the engine every block (metrics, scenario, challenges, achievements) has its own guard, so a fault in
    /// one costs that block of that pass only. The director keeps no counter of errors: the core counts them and switches the feature off after five.</item>
    /// <item><b>Timing</b> (TSK-030, NFR-1). With the debug flag <see cref="PerfFlag"/> in <c>ptmod_settings.json</c> the director times the day pass, the month pass and the table of the
    /// companies and writes the average and the maximum of each to <c>Player.log</c> once per game month and once for the whole session (<see cref="RunPerfMeter"/>); it also counts the
    /// entries during the fast-forward of a new game, which must do no work. Without the flag a pass pays for one read of the flag.</item>
    /// <item><b>The feature switched off:</b> the core stops the ticks and the events of the feature at once; the run and the records of the modifiers stay (the records do not
    /// apply meanwhile), and when the feature comes back in the middle of a game the run goes on from that day (E14).</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    internal sealed class RunDirector
    {
        /// <summary>The debug flag of the timing (the section <c>debug</c> of <c>ptmod_settings.json</c>, a debug build only): <c>"debug": { "scenarios-achievements.perf": true }</c>.</summary>
        internal const string PerfFlag = "scenarios-achievements.perf";

        private const string SitePrefix = "RunDirector.";
        private const string SampleSite = "framework-sample";

        private readonly GameMetricsSource _source = new GameMetricsSource();
        private readonly GameRunPorts _ports = new GameRunPorts();
        private readonly GameConditionAdapter _conditions = new GameConditionAdapter();
        private readonly Func<DaySample> _sampleDay;
        private readonly Func<MonthSample> _sampleMonth;
        private readonly HashSet<string> _missingChallenges = new HashSet<string>(StringComparer.Ordinal);
        private readonly RunPerfMeter _perf = new RunPerfMeter(() => ModSettings.Debug(PerfFlag), Stopwatch.GetTimestamp, Stopwatch.Frequency, WritePerf);

        private AchievementEvaluator _evaluator;
        private RunEngine _engine;
        private ScenarioResult _deferredResult;
        private bool _careerPending;
        private bool _installed;

        internal RunDirector()
        {
            _sampleDay = () => _source.SampleDay(_engine == null ? null : _engine.Run.Metrics.TransferredCpuIds);
            _sampleMonth = () => _source.SampleLastFullMonth();
        }

        /// <summary>The director of the session; null before <see cref="Install"/> made it.</summary>
        internal static RunDirector Instance { get; private set; }

        // ---- what the windows listen to ----

        /// <summary>
        /// The run is ready: a new game just started it or a loaded game just got its run back (a <c>legacy</c> run included). The briefing, the tracker and the
        /// letter listen to it. Raised through <c>ModHooks.Raise</c>: a handler that throws costs only itself.
        /// </summary>
        internal static event Action<RunState, GameReadyReason> RunReady;

        /// <summary>
        /// A pass of the engine brought something to show: the goals and the end of the scenario, the challenges, the achievements that are unlocked now. Raised after
        /// every day, month and event that changed something; never with an empty result.
        /// </summary>
        internal static event Action<RunPassResult> Evaluated;

        /// <summary>The scenario ended and its window may open: at once, or after <c>ModTicks.BatchEnded</c> when the end was decided inside a month's batch run.</summary>
        internal static event Action<ScenarioResult> ResultReady;

        /// <summary>
        /// The last year of the game began in a run with no scenario going (FR-17): the summary of the career may open - at once, or after <c>ModTicks.BatchEnded</c> when the year
        /// changed inside a month's batch run. The event <c>endyear.reached</c> was counted before it is raised, so the achievements of the game are complete.
        /// </summary>
        internal static event Action CareerEnded;

        // ---- the state, for the windows ----

        /// <summary>The run in play (the section of the save), or null in the menu and before the game is ready.</summary>
        internal RunState Run
        {
            get { return RunSaveParticipant.Instance.Run; }
        }

        /// <summary>The engine of the run in play (the runtime of the scenario for the tracker, the snapshot of the metrics); null while there is no run.</summary>
        internal RunEngine Engine
        {
            get
            {
                RunState run = RunSaveParticipant.Instance.Run;
                if (run == null)
                {
                    _engine = null;
                    return null;
                }
                if (_engine == null || !ReferenceEquals(_engine.Run, run)) _engine = BuildEngine(run);
                return _engine;
            }
        }

        /// <summary>The table that compares the companies for the window of the results and the career summary (FR-9, FR-17); built now, not on a tick.</summary>
        internal ComparisonRow[] BuildComparison(int maxAi = ComparisonBuilder.DefaultMaxAi)
        {
            long started = _perf.Begin();
            ComparisonRow[] rows = _source.BuildComparison(maxAi);
            _perf.End(RunPerfMeter.Comparison, started, ModTicks.Today);
            return rows;
        }

        /// <summary>The highest frequency (kHz) among the released CPUs of the AI companies, for the race line of the tracker; 0 when there are none or the read failed.</summary>
        internal double RivalBestFrequency()
        {
            return ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + "RivalBest", () => _source.RivalBestFrequency(), 0.0);
        }

        // ---- installation ----

        /// <summary>Makes the director of the session and subscribes it to the ticks and to the events of the core (once per session).</summary>
        internal static void Install(FeatureContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException("ctx");
            if (Instance != null && Instance._installed) return;

            var director = new RunDirector { _installed = true };
            Instance = director;

            ctx.OnTick(TickPhase.DayPassed, director.OnDayPassed);
            ctx.OnTick(TickPhase.LateMonthPassed, director.OnLateMonthPassed);
            ctx.OnTick(TickPhase.YearPassed, director.OnYearPassed);
            ModTicks.GameReady += director.OnGameReady;
            ModTicks.BatchEnded += director.OnBatchEnded;
            PTModHost.SceneLeaving += director.OnSceneLeaving;
            ScenariosFeature.Disabled += director.OnFeatureDisabled;
            RunSaveParticipant.Instance.RunReplaced += director.OnRunReplaced;
            OnboardingBridge.SubscribeCompleted(director.OnOnboardingCompleted);
        }

        // ---- the game is ready ----

        private void OnGameReady(GameReadyReason reason)
        {
            Guard("GameReady", () =>
            {
                if (!ScenariosFeature.IsActive) return;

                if (!GameEventHooks.Attach()) ModLog.Warn(ScenariosFeature.FeatureId, "events: the scene lacks the bankruptcy handler or the business contracts; those events are missed");

                // the fast-forward of the new game is over: it must have cost the run nothing (NFR-1)
                if (reason == GameReadyReason.NewGame) _perf.ReportFastForward();
                if (reason == GameReadyReason.NewGame) StartNewRun();
                else RestoreRun();
            });
        }

        /// <summary>The feature was switched on in the middle of a game (the core does not raise <c>GameReady</c> for it): pick the game up from today.</summary>
        internal void OnFeatureSwitchedOn()
        {
            Guard("SwitchedOn", () =>
            {
                if (!ScenariosFeature.IsActive || !ModTicks.IsGameReady) return;
                GameEventHooks.Attach();
                RestoreRun();
            });
        }

        // A new game: the run of what the player chose, the records of the modifiers (once), the first checks.
        private void StartNewRun()
        {
            PendingRun pending = ScenarioStarter.TakePending();
            var skipped = new List<string>();
            RunPlan plan = RunStart.Create(Guid.NewGuid().ToString("D"), ModTicks.Today, GameFacts.Difficulty(), GameFacts.CheatsEnabled(), pending,
                FeaturesAtStart(), ScenarioCatalog.FindChallenge, skipped);
            if (skipped.Count > 0) ModLog.Warn(ScenariosFeature.FeatureId, "run: the challenges " + string.Join(", ", skipped.ToArray()) + " are not in the catalog and were left out");

            RunState run = plan.Run;
            RunSaveParticipant.Instance.Run = run;
            AssistedRegistry.Reset(run);

            if (pending != null && pending.HasScenario)
            {
                run.ScenarioEntryIds.AddRange(ModifierBinder.Apply(pending.Scenario.TitleKey, pending.Scenario.Modifiers));
            }
            foreach (ChallengeDefinition challenge in plan.Challenges)
            {
                run.ChallengeStates.Add(challenge.Id, ModifierBinder.Apply(challenge.TitleKey, challenge.Modifiers));
            }

            ProfileGate.RecordRunStarted();
            ScenarioStarter.AfterStart(pending);

            ModLog.Info(ScenariosFeature.FeatureId, "run " + run.RunId + " started on " + ContentVocabulary.FormatDate(run.StartDate) + ": "
                                                    + (run.HasScenario ? "scenario '" + run.ScenarioId + "'" : "free play")
                                                    + ", challenges [" + string.Join(", ", run.Challenges.ToArray()) + "]"
                                                    + (run.Assisted ? ", assisted (" + string.Join(", ", run.AssistedReasons.ToArray()) + ")" : "")
                                                    + (run.Custom ? ", custom content" : ""));

            RunEngine engine = Engine;
            if (engine != null) Publish(engine.OnStart(Live()));
            ModHooks.Raise("RunDirector.RunReady", RunReady, run, GameReadyReason.NewGame);
        }

        // A loaded game (or a feature switched on): the run of the section, or a legacy run; no modifier is added.
        private void RestoreRun()
        {
            RunState run = RunSaveParticipant.Instance.Run;
            if (run == null)
            {
                run = RunState.CreateLegacy(Guid.NewGuid().ToString("D"), ModTicks.Today, GameFacts.Difficulty(), GameFacts.CheatsEnabled(), FeaturesAtStart());
                RunSaveParticipant.Instance.Run = run;
                ModLog.Info(ScenariosFeature.FeatureId, "run " + run.RunId + ": the save has no run of this feature; a legacy run starts on " + ContentVocabulary.FormatDate(run.StartDate)
                                                        + (run.Assisted ? " (cheats are on: assisted)" : string.Empty));
            }
            else
            {
                ModLog.Info(ScenariosFeature.FeatureId, "run " + run.RunId + " restored: " + (run.HasScenario ? "scenario '" + run.ScenarioId + "' " + run.ScenarioState.Status : "free play")
                                                        + ", started on " + ContentVocabulary.FormatDate(run.StartDate));
            }
            AssistedRegistry.Reset(run);

            RunEngine engine = Engine;
            if (engine != null)
            {
                Guard("LastMonth", () => engine.SetLastMonth(_source.SampleLastFullMonth()));
                Publish(engine.OnStart(Live()));
            }
            ModHooks.Raise("RunDirector.RunReady", RunReady, run, GameReadyReason.Loaded);
        }

        // ---- the ticks ----

        private void OnDayPassed()
        {
            Guard("Day", () =>
            {
                RunEngine engine;
                if (!Ready(out engine)) return;
                long started = _perf.Begin();
                RunPassResult result = engine.OnDay(_sampleDay, Live());
                _perf.End(RunPerfMeter.Day, started, ModTicks.Today);
                Publish(result);
            });
        }

        private void OnLateMonthPassed()
        {
            Guard("Month", () =>
            {
                RunEngine engine;
                if (!Ready(out engine)) return;
                long started = _perf.Begin();
                RunPassResult result = engine.OnMonth(_sampleMonth, Live());
                _perf.End(RunPerfMeter.Month, started, ModTicks.Today);
                Publish(result);
            });
        }

        private void OnYearPassed()
        {
            Guard("Year", () =>
            {
                RunEngine engine;
                if (!Ready(out engine)) return;
                if (ModTicks.Today.Year != GameFacts.EndYear() || engine.Run.IsScenarioActive) return;

                Publish(engine.OnEvent("endyear.reached", Live()));
                if (ModTicks.IsBatchRun) _careerPending = true;
                else ModHooks.Raise("RunDirector.CareerEnded", CareerEnded);
            });
        }

        private void OnBatchEnded()
        {
            Guard("BatchEnded", () =>
            {
                ScenarioResult result = _deferredResult;
                bool career = _careerPending;
                _deferredResult = null;
                _careerPending = false;
                if (!ScenariosFeature.IsActive) return;

                // the summary of the career first: the results of the scenario, which ended earlier, open last and stay on top
                if (career) ModHooks.Raise("RunDirector.CareerEnded", CareerEnded);
                if (result != null) ModHooks.Raise("RunDirector.ResultReady", ResultReady, result);
            });
        }

        // ---- the events of the game (from GameEventHooks and the patch) ----

        internal void OnCpuReleased(CpuInfo cpu, bool isPlayer, string companyName, DateTime date)
        {
            Guard("CpuReleased", () =>
            {
                RunEngine engine;
                if (!Ready(out engine)) return;
                Publish(engine.OnCpuReleased(cpu, isPlayer, companyName, date, Live()));
            });
        }

        internal void OnCpuTransferredToPlayer(int cpuSaveId)
        {
            Guard("CpuTransferred", () =>
            {
                RunEngine engine;
                if (!Ready(out engine)) return;
                engine.OnCpuTransferredToPlayer(cpuSaveId);
            });
        }

        internal void OnOemSigned()
        {
            Guard("OemSigned", () =>
            {
                RunEngine engine;
                if (!Ready(out engine)) return;
                Publish(engine.OnOemSigned(Live()));
            });
        }

        internal void OnPlayerBankrupt()
        {
            Guard("PlayerBankrupt", () =>
            {
                RunEngine engine;
                if (!Ready(out engine)) return;
                Publish(engine.OnPlayerBankrupt(Live()));
            });
        }

        internal void OnRivalBankrupt(bool absorbedByPlayer)
        {
            Guard("RivalBankrupt", () =>
            {
                RunEngine engine;
                if (!Ready(out engine)) return;
                Publish(engine.OnRivalBankrupt(absorbedByPlayer, Live()));
            });
        }

        internal void OnBusinessContractSigned()
        {
            Guard("BusinessSigned", () =>
            {
                RunEngine engine;
                if (!Ready(out engine)) return;
                Publish(engine.OnBusinessContractSigned(Live()));
            });
        }

        // The tutorial ended (first-year-onboarding): only a clean end (not skipped, not aborted) counts for "Tutorial graduate".
        private void OnOnboardingCompleted(bool clean)
        {
            Guard("OnboardingCompleted", () =>
            {
                RunEngine engine;
                if (!clean || !Ready(out engine)) return;
                Publish(engine.OnEvent("onboarding.completed.clean", Live()));
            });
        }

        // ---- the scene and the section ----

        private void OnSceneLeaving()
        {
            Guard("SceneLeaving", () =>
            {
                _perf.FlushSession();
                GameEventHooks.Detach();
                AssistedRegistry.Reset(null);
                _engine = null;
                _deferredResult = null;
                _careerPending = false;
            });
        }

        // The feature was switched off: what the timer holds is written now, since nothing will call it again until the feature is back.
        private void OnFeatureDisabled()
        {
            Guard("Disabled", () => _perf.FlushSession());
        }

        // A load or a new game replaced the run: what was built over the old one is out of date; the next entry builds the engine again.
        private void OnRunReplaced()
        {
            _engine = null;
            _deferredResult = null;
            _careerPending = false;
            AssistedRegistry.Reset(RunSaveParticipant.Instance.Run);
        }

        // ---- helpers ----

        // The run may be judged: the feature is on, the game is ready (not loading, not fast-forwarding) and a run exists.
        private bool Ready(out RunEngine engine)
        {
            engine = null;

            // NFR-1: nothing runs during the fast-forward of a new game. The core does not call the ticks then and the hooks of the events leave at their first line, so an entry
            // that gets here is counted (the debug timer reports it); one that would go on to work, and so must not exist, is counted separately.
            bool fastForward = ModTicks.IsFastForwarding;
            if (!ScenariosFeature.IsActive || !ModTicks.IsGameReady)
            {
                if (fastForward) _perf.NoteFastForward(false);
                return false;
            }

            engine = Engine;
            if (fastForward) _perf.NoteFastForward(engine != null);
            return engine != null;
        }

        private static void WritePerf(string line, bool overBudget)
        {
            if (overBudget) ModLog.Warn(ScenariosFeature.FeatureId, line);
            else ModLog.Info(ScenariosFeature.FeatureId, line);
        }

        private static LiveRun Live()
        {
            return GameFacts.Live(ModTicks.Today, ScenarioParams.StrictAssisted);
        }

        // Tells the windows what a pass brought; a result decided inside a month's batch run waits for its end.
        private void Publish(RunPassResult result)
        {
            if (result == null || result.IsEmpty) return;

            ModHooks.Raise("RunDirector.Evaluated", Evaluated, result);
            if (result.Finished == null) return;

            ModLog.Info(ScenariosFeature.FeatureId, "scenario '" + Run.ScenarioId + "' ended: " + result.Finished.Outcome + ", score " + result.Finished.Score
                                                    + ", medal " + result.Finished.Medal + (result.Finished.FailId == null ? string.Empty : ", reason " + result.Finished.FailId));
            if (ModTicks.IsBatchRun) _deferredResult = result.Finished;
            else ModHooks.Raise("RunDirector.ResultReady", ResultReady, result.Finished);
        }

        private RunEngine BuildEngine(RunState run)
        {
            if (_evaluator == null)
            {
                var standard = new List<string>();
                foreach (ScenarioDefinition scenario in ScenarioCatalog.Scenarios)
                {
                    if (!scenario.IsCustom) standard.Add(scenario.Id);
                }
                _evaluator = new AchievementEvaluator(ScenarioCatalog.Achievements, standard);
            }

            var challenges = new List<ChallengeDefinition>();
            foreach (string id in run.Challenges)
            {
                ChallengeDefinition definition = ScenarioCatalog.FindChallenge(id);
                if (definition != null) challenges.Add(definition);
                else if (_missingChallenges.Add(id)) ModLog.Warn(ScenariosFeature.FeatureId, "run: the challenge '" + id + "' of the run is not in the catalog; it is not watched");
            }

            return new RunEngine(run, challenges, _evaluator, _conditions, _ports, Block, null, ScenarioCatalog.ChallengeYears);
        }

        // The guard of a block of the engine: its own site, so the log says which block failed.
        private static bool Block(string name, Action body)
        {
            return ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + name, body);
        }

        private static void Guard(string site, Action body)
        {
            ModHooks.Guard(ScenariosFeature.FeatureId, SitePrefix + site, body);
        }

        private static List<string> FeaturesAtStart()
        {
            return ModFeatures.All.Where(info => info.Id != SampleSite && ModFeatures.IsActive(info.Id)).Select(info => info.Id).ToList();
        }
    }
}
