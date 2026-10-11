using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The live values of the game that a pass of the engine needs and the logic cannot read: today's date, the player's company (its type and the
    /// name the player gave it), the difficulty now and the setting <c>strictAssisted</c>. The director builds it fresh for every pass.
    /// </summary>
    public struct LiveRun
    {
        public DateTime Today;

        /// <summary><c>idm</c>, <c>fabless</c> or <c>foundry</c>; null when it is not known.</summary>
        public string CompanyType;

        public string CompanyName;

        public int Difficulty;

        public bool StrictAssisted;
    }

    /// <summary>
    /// What the engine writes outside the run and the game's own services read: the profile, and the records of <c>ModModifiers</c> of a challenge
    /// that failed. The game-side layer (<c>GameRunPorts</c>) implements it over <c>ProfileGate</c> and <c>ModifierBinder</c>; the tests bring a
    /// fake over a real <see cref="ProfileModel"/>. Every member must tolerate being called often and must not throw (the engine isolates a fault
    /// to a block, but the profile should not be lost to one).
    /// </summary>
    public interface IRunPorts
    {
        /// <summary>The read side of the profile (what is earned, the records); null when the profile cannot be read (nothing is judged then).</summary>
        ProfileView Profile { get; }

        /// <summary>Puts an achievement into the profile; false when it was there already or could not be written.</summary>
        bool UnlockAchievement(string achievementId, AchievementEntry entry);

        /// <summary>Records how the scenario ended (the history, the counters, the best result).</summary>
        void RecordScenarioResult(ScenarioResult result, RunInfo run);

        /// <summary>Records a challenge that was passed after some whole years.</summary>
        void RecordChallengePassed(string challengeId, int years);

        /// <summary>Takes the records of <c>ModModifiers</c> away (a challenge failed in free play).</summary>
        void RemoveModifierEntries(IList<string> entryIds);
    }

    /// <summary>
    /// What one pass of the <see cref="RunEngine"/> brought: the goals and the end of the scenario, the challenges that failed or passed, the
    /// achievements that were unlocked (already in the profile), and the result when the scenario ended in this pass. The director shows it
    /// (notifications, the tracker, the results window); the lists are empty, never null.
    /// </summary>
    public sealed class RunPassResult
    {
        private static readonly ScenarioOutcomeEvent[] NoOutcomes = new ScenarioOutcomeEvent[0];
        private static readonly ChallengeEvent[] NoChallenges = new ChallengeEvent[0];
        private static readonly string[] NoAchievements = new string[0];

        private List<ScenarioOutcomeEvent> _outcomes;
        private List<ChallengeEvent> _challenges;
        private List<string> _achievements;

        /// <summary>Goals done or failed, the win or the loss, in the order they happened.</summary>
        public IList<ScenarioOutcomeEvent> ScenarioEvents
        {
            get { return _outcomes ?? (IList<ScenarioOutcomeEvent>)NoOutcomes; }
        }

        /// <summary>Challenges that failed or passed.</summary>
        public IList<ChallengeEvent> ChallengeEvents
        {
            get { return _challenges ?? (IList<ChallengeEvent>)NoChallenges; }
        }

        /// <summary>The ids of the achievements unlocked in this pass, in the order they were unlocked.</summary>
        public IList<string> Achievements
        {
            get { return _achievements ?? (IList<string>)NoAchievements; }
        }

        /// <summary>The result of the scenario when it was won or lost in this pass; null otherwise.</summary>
        public ScenarioResult Finished { get; internal set; }

        /// <summary>The pass brought nothing to show.</summary>
        public bool IsEmpty
        {
            get { return _outcomes == null && _challenges == null && _achievements == null && Finished == null; }
        }

        internal void Add(ScenarioOutcomeEvent outcome)
        {
            if (_outcomes == null) _outcomes = new List<ScenarioOutcomeEvent>();
            _outcomes.Add(outcome);
        }

        internal void Add(ChallengeEvent challenge)
        {
            if (_challenges == null) _challenges = new List<ChallengeEvent>();
            _challenges.Add(challenge);
        }

        internal void AddAchievement(string id)
        {
            if (_achievements == null) _achievements = new List<string>();
            _achievements.Add(id);
        }
    }

    /// <summary>
    /// The brain of a run (TSK-019; design.md "Дневная оценка"): given the run in play, it takes what the game reports - a day passed, a month
    /// closed, a CPU was released or transferred, a contract was signed, a company went bankrupt - and carries it through the metrics, the
    /// scenario, the challenges and the achievements, in this order, each block apart: a fault in one (the <c>guard</c> catches it and the block is
    /// skipped) does not stop the others. It writes the achievements and the result into the profile through <see cref="IRunPorts"/> and
    /// hands back a <see cref="RunPassResult"/> for the director to show. It reads no game state itself, so a whole run can be played through
    /// in a test: the samples and the events come in as arguments.
    /// <list type="bullet">
    /// <item><b>Order that matters.</b> When the scenario ends, the result goes into the profile first and only then the events
    /// <c>scenario.won</c>, <c>scenario.gold</c>, <c>scenario.lost</c> are judged, because a rule of the achievements ("gold in every scenario")
    /// reads the profile.</item>
    /// <item><b>A pass whose metrics failed</b> judges nothing: the snapshot would be the one of the day before, and a goal that counts days
    /// would count one twice.</item>
    /// <item><b>Challenges</b> fail the scenario they run inside (<see cref="ScenarioRuntime.Lose"/>); in free play, and once the scenario is
    /// over (the game goes on as free play, D13), they take their <c>ModModifiers</c> records away and may be passed.</item>
    /// <item><b>Unlocking</b> keeps the <see cref="RunState.AchievementsThisRun"/> list of the run; an achievement the profile already has is
    /// neither judged nor reported.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public sealed class RunEngine
    {
        private readonly RunState _run;
        private readonly List<ChallengeDefinition> _challengeDefinitions = new List<ChallengeDefinition>();
        private readonly AchievementEvaluator _achievements;
        private readonly IConditionAdapter _conditions;
        private readonly IRunPorts _ports;
        private readonly Func<string, Action, bool> _guard;
        private readonly Func<DateTime> _utcNow;
        private readonly ChallengeRuntime _challengeRuntime = new ChallengeRuntime();
        private readonly int _challengeYears;
        private readonly EventBook _events;
        private readonly ScenarioRuntime _scenario;

        /// <param name="run">The run in play.</param>
        /// <param name="challenges">The definitions of the run's challenges (the ones the catalog knows); also what the score bonus is made of.</param>
        /// <param name="achievements">The evaluator of the achievements of the catalog.</param>
        /// <param name="conditions">Evaluates <c>condition</c> checks; null: no such check can be true.</param>
        /// <param name="ports">The profile and the modifier records.</param>
        /// <param name="guard">Runs a block (its name, its body) and returns false when it threw; null: a plain try/catch that swallows the exception.</param>
        /// <param name="utcNow">The real clock, for the moment of an unlock; null: <see cref="DateTime.UtcNow"/>.</param>
        /// <param name="challengeYears">The length of a challenge in free play, in years (<c>config.json</c>).</param>
        public RunEngine(RunState run, IEnumerable<ChallengeDefinition> challenges, AchievementEvaluator achievements, IConditionAdapter conditions,
                         IRunPorts ports, Func<string, Action, bool> guard = null, Func<DateTime> utcNow = null,
                         int challengeYears = ChallengeRuntime.DefaultYears)
        {
            if (run == null) throw new ArgumentNullException("run");
            if (achievements == null) throw new ArgumentNullException("achievements");
            if (ports == null) throw new ArgumentNullException("ports");

            _run = run;
            _achievements = achievements;
            _conditions = conditions ?? NullConditionAdapter.Instance;
            _ports = ports;
            _guard = guard ?? SwallowingGuard;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _challengeYears = challengeYears < 1 ? ChallengeRuntime.DefaultYears : challengeYears;

            if (challenges != null)
            {
                foreach (ChallengeDefinition definition in challenges)
                {
                    if (definition != null) _challengeDefinitions.Add(definition);
                }
            }

            _events = new EventBook(run.Metrics);
            if (run.HasScenario)
            {
                _scenario = new ScenarioRuntime(run.Scenario, run.ScenarioState, new GoalEvaluator(), new FailEvaluator(), new ScoreCalculator(),
                    null, _challengeDefinitions, _conditions);
            }
            Snapshot = MetricsCalculator.Snapshot(run.Metrics, null, null);
        }

        public RunState Run
        {
            get { return _run; }
        }

        /// <summary>The counts of the events of the run and the journal of the player's releases.</summary>
        public EventBook Events
        {
            get { return _events; }
        }

        /// <summary>The runtime of the scenario (progress of the goals for the tracker); null in free play.</summary>
        public ScenarioRuntime Scenario
        {
            get { return _scenario; }
        }

        /// <summary>The definitions of the run's challenges.</summary>
        public IList<ChallengeDefinition> ChallengeDefinitions
        {
            get { return _challengeDefinitions.AsReadOnly(); }
        }

        /// <summary>The metrics as the last pass left them (state-only values before the first day).</summary>
        public MetricsSnapshot Snapshot { get; private set; }

        /// <summary>The last day sample that was folded in; null before the first day.</summary>
        public DaySample LastDay { get; private set; }

        /// <summary>The last month sample; null before the first month.</summary>
        public MonthSample LastMonth { get; private set; }

        /// <summary>Sets the month sample a load found, without folding it into the streaks (the state has counted its months already).</summary>
        public void SetLastMonth(MonthSample month)
        {
            LastMonth = month;
            Snapshot = MetricsCalculator.Snapshot(_run.Metrics, LastDay, LastMonth);
        }

        // ---- the passes ----

        /// <summary>
        /// The pass of a game day (<c>TickPhase.DayPassed</c>): <paramref name="sampleDay"/> reads the day off the game inside the block of the
        /// metrics; then the goals, the fail rules and the deadline of the scenario, the challenges, the achievements of the cadence <c>daily</c>.
        /// </summary>
        public RunPassResult OnDay(Func<DaySample> sampleDay, LiveRun live)
        {
            var result = new RunPassResult();
            bool measured = false;

            Block("metrics", () =>
            {
                DaySample day = sampleDay();
                if (day == null) return;
                if (day.Date == default(DateTime)) day.Date = live.Today;
                day.RunDays = Math.Max(0, (int)(day.Date.Date - _run.StartDate.Date).TotalDays);
                MetricsCalculator.ApplyDay(_run.Metrics, day);
                LastDay = day;
                Snapshot = MetricsCalculator.Snapshot(_run.Metrics, LastDay, LastMonth);
                measured = true;
            });
            if (!measured) return result;

            Block("scenario", () =>
            {
                if (_scenario == null || !_run.IsScenarioActive) return;
                Absorb(result, _scenario.EvaluateDay(Snapshot, live.Today, _events), live);
            });

            Block("challenges", () => EvaluateChallenges(result, live));

            Block("achievements", () => Unlock(result, _achievements.Evaluate(CheckSpec.CadenceDaily, Snapshot, _events, _ports.Profile, Info(live), _conditions), live));
            return result;
        }

        /// <summary>
        /// The pass of a closed month (<c>TickPhase.LateMonthPassed</c>): <paramref name="sampleMonth"/> reads the last full month off the game; the
        /// streak of profitable months, the goals with a monthly check, the achievements of the cadence <c>monthly</c>.
        /// </summary>
        public RunPassResult OnMonth(Func<MonthSample> sampleMonth, LiveRun live)
        {
            var result = new RunPassResult();
            bool measured = false;

            Block("metrics", () =>
            {
                MonthSample month = sampleMonth();
                if (month == null) return;
                MetricsCalculator.ApplyMonth(_run.Metrics, month);
                LastMonth = month;
                Snapshot = MetricsCalculator.Snapshot(_run.Metrics, LastDay, LastMonth);
                measured = true;
            });
            if (!measured) return result;

            Block("scenario", () =>
            {
                if (_scenario == null || !_run.IsScenarioActive) return;
                Absorb(result, _scenario.EvaluateMonth(Snapshot, live.Today, _events), live);
            });

            Block("achievements", () => Unlock(result, _achievements.Evaluate(CheckSpec.CadenceMonthly, Snapshot, _events, _ports.Profile, Info(live), _conditions), live));
            return result;
        }

        /// <summary>
        /// The run starts or is loaded: the achievements that wait for an event are swept once, because an event may have been missed (the game was
        /// loaded, the mod was off).
        /// </summary>
        public RunPassResult OnStart(LiveRun live)
        {
            var result = new RunPassResult();
            Block("achievements", () => Unlock(result, _achievements.Evaluate(CheckSpec.CadenceEvent, Snapshot, _events, _ports.Profile, Info(live), _conditions), live));
            return result;
        }

        /// <summary>
        /// A CPU was released (<c>ModEvents.CpuReleased</c>, not during the fast-forward): the release is counted (a CPU that came to the player by
        /// a transfer is not one), the races and the goals on releases are settled, the event <c>cpu.released.player</c> or <c>.ai</c> is judged.
        /// <paramref name="cpu"/> carries the type of the company and the date of the release.
        /// </summary>
        public RunPassResult OnCpuReleased(CpuInfo cpu, bool isPlayer, string companyName, DateTime date, LiveRun live)
        {
            var result = new RunPassResult();
            bool counted = false;
            Block("release", () => { counted = _events.RecordCpuRelease(cpu, isPlayer); });
            if (!counted) return result;

            Block("scenario", () =>
            {
                if (_scenario == null || !_run.IsScenarioActive) return;
                Absorb(result, _scenario.OnCpuReleased(cpu, isPlayer, companyName, date, _events), live);
            });

            Block("achievements", () => JudgeEvent(result, isPlayer ? "cpu.released.player" : "cpu.released.ai", live));
            return result;
        }

        /// <summary>A CPU came to the player by a transfer (<c>ModEvents.CpuTransferred</c> to the player): it is no release and stays out of the metrics of characteristics.</summary>
        public void OnCpuTransferredToPlayer(int cpuSaveId)
        {
            Block("transfer", () => _events.RecordTransfer(cpuSaveId));
        }

        /// <summary>An OEM contract was signed for a CPU of the player: the metric <c>oem.signed.total</c> and the event <c>oem.signed</c>.</summary>
        public RunPassResult OnOemSigned(LiveRun live)
        {
            var result = new RunPassResult();
            bool counted = false;
            Block("event", () => { _events.RecordOemSigned(); Refresh(); counted = true; });
            if (counted) Block("achievements", () => JudgeEvent(result, "oem.signed", live));
            return result;
        }

        /// <summary>
        /// An AI company went bankrupt: counted for <c>rivals.bankruptCount</c> and the event <c>rival.bankrupt</c> unless the player absorbed it
        /// (<paramref name="absorbedByPlayer"/>, FR-12 item 7).
        /// </summary>
        public RunPassResult OnRivalBankrupt(bool absorbedByPlayer, LiveRun live)
        {
            var result = new RunPassResult();
            bool counted = false;
            Block("event", () => { counted = _events.RecordRivalBankrupt(absorbedByPlayer); if (counted) Refresh(); });
            if (counted) Block("achievements", () => JudgeEvent(result, "rival.bankrupt", live));
            return result;
        }

        /// <summary>The player's company went bankrupt: the scenario is lost (FR-7 item 1) and the event <c>player.bankrupt</c> is judged.</summary>
        public RunPassResult OnPlayerBankrupt(LiveRun live)
        {
            var result = new RunPassResult();
            Block("event", () => _events.RecordPlayerBankrupt());

            Block("scenario", () =>
            {
                if (_scenario == null || !_run.IsScenarioActive) return;
                Absorb(result, _scenario.OnPlayerBankrupt(live.Today), live);
            });

            Block("achievements", () => JudgeEvent(result, "player.bankrupt", live));
            return result;
        }

        /// <summary>The player signed a foundry contract (<c>BusinessContractManager.OnPlayerSignedContract</c>): the event <c>foundry.signed</c>.</summary>
        public RunPassResult OnBusinessContractSigned(LiveRun live)
        {
            return OnEvent("foundry.signed", live);
        }

        /// <summary>
        /// Counts an internal event that has no special handling (<c>onboarding.completed.clean</c>, <c>endyear.reached</c>, ...) and judges the
        /// achievements that wait for it. An id that is not an event of the feature is counted all the same: the checks only look at known ones.
        /// </summary>
        public RunPassResult OnEvent(string eventId, LiveRun live)
        {
            var result = new RunPassResult();
            if (string.IsNullOrEmpty(eventId)) return result;

            Block("event", () => _events.Record(eventId));
            Block("achievements", () => JudgeEvent(result, eventId, live));
            return result;
        }

        // ---- scenario, challenges, achievements ----

        // What happened to the scenario: every event goes to the result; a win or a loss is settled (profile, events, achievements).
        private void Absorb(RunPassResult result, IList<ScenarioOutcomeEvent> outcomes, LiveRun live)
        {
            if (outcomes == null) return;
            foreach (ScenarioOutcomeEvent outcome in outcomes)
            {
                result.Add(outcome);
                if ((outcome.Kind == ScenarioOutcomeKind.Won || outcome.Kind == ScenarioOutcomeKind.Lost) && outcome.Result != null)
                {
                    Finish(result, outcome.Result, live);
                }
            }
        }

        // The scenario ended: the profile first (a rule of the achievements reads it), then the events of the end, then the achievements they open.
        private void Finish(RunPassResult result, ScenarioResult scenarioResult, LiveRun live)
        {
            result.Finished = scenarioResult;
            RunInfo info = Info(live);

            Block("profile", () => _ports.RecordScenarioResult(scenarioResult, info));

            string[] ids = scenarioResult.EventIds();
            Block("event", () =>
            {
                foreach (string id in ids) _events.Record(id);
            });
            Block("achievements", () =>
            {
                foreach (string id in ids) JudgeEvent(result, id, live);
            });
        }

        private void EvaluateChallenges(RunPassResult result, LiveRun live)
        {
            if (_challengeDefinitions.Count == 0) return;

            bool freePlay = !_run.IsScenarioActive;
            IList<ChallengeEvent> events = _challengeRuntime.EvaluateDay(_challengeDefinitions, _run.ChallengeStates, Snapshot, freePlay,
                _challengeYears, live.Today, _conditions, _events);

            foreach (ChallengeEvent challengeEvent in events)
            {
                result.Add(challengeEvent);
                ChallengeEvent current = challengeEvent;

                Block("challenge.effects", () =>
                {
                    _events.Record(current.EventId);
                    if (current.Kind == ChallengeEventKind.Failed)
                    {
                        if (current.RemoveEntryIds != null && current.RemoveEntryIds.Length > 0) _ports.RemoveModifierEntries(current.RemoveEntryIds);
                        if (current.LosesScenario && _scenario != null && _run.IsScenarioActive)
                        {
                            Absorb(result, _scenario.Lose("challenge:" + current.ChallengeId, current.TextKey, null, current.Date), live);
                        }
                    }
                    else
                    {
                        _ports.RecordChallengePassed(current.ChallengeId, current.Years);
                    }
                });
                Block("achievements", () => JudgeEvent(result, current.EventId, live));
            }
        }

        private void JudgeEvent(RunPassResult result, string eventId, LiveRun live)
        {
            Unlock(result, _achievements.OnEvent(eventId, _events, Snapshot, _ports.Profile, Info(live), _conditions), live);
        }

        // Puts the earned achievements into the profile; only the ones that were written are reported and listed in the run.
        private void Unlock(RunPassResult result, IList<string> ids, LiveRun live)
        {
            if (ids == null) return;
            foreach (string id in ids)
            {
                var entry = new AchievementEntry
                {
                    UnlockedAt = _utcNow(),
                    GameDate = live.Today,
                    Company = live.CompanyName,
                    RunId = _run.RunId,
                    Assisted = _run.Assisted,
                };
                if (!_ports.UnlockAchievement(id, entry)) continue;

                if (!_run.AchievementsThisRun.Contains(id)) _run.AchievementsThisRun.Add(id);
                result.AddAchievement(id);
            }
        }

        // ---- helpers ----

        private RunInfo Info(LiveRun live)
        {
            return RunInfo.From(_run, live.Today, live.CompanyType, live.CompanyName, live.Difficulty, live.StrictAssisted);
        }

        // The metrics that an event changes at once (the counters) are read again.
        private void Refresh()
        {
            Snapshot = MetricsCalculator.Snapshot(_run.Metrics, LastDay, LastMonth);
        }

        private void Block(string name, Action body)
        {
            _guard(name, body);
        }

        private static bool SwallowingGuard(string name, Action body)
        {
            try
            {
                body();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
