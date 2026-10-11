using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The course of one scenario (design.md, "Цели, гонки, провалы, сценарий"; FR-5, FR-6, FR-7, FR-8): it takes the snapshot of the
    /// definition and the <see cref="ScenarioState"/> of the run, is told what happened - a day passed, a month closed, a CPU was released, the
    /// player went bankrupt - and answers with the list of what changed. It decides in this order: a fail rule that holds, a race that was lost
    /// with <c>failOnLose</c>, a required goal that failed, all required goals done (a win), the deadline (a loss "time is out"). Once the scenario
    /// is won or lost nothing changes any more (E8), however many more passes come. No clock and no game types: the caller brings the day and
    /// the snapshot of the metrics, and shows the events.
    /// </summary>
    public sealed class ScenarioRuntime
    {
        public const string FailBankrupt = "bankrupt";
        public const string FailDeadline = "deadline";
        public const string KeyBankrupt = "scn.fail.bankrupt";
        public const string KeyDeadline = "scn.fail.deadline";
        public const string KeyRaceLost = "scn.fail.raceLost";
        public const string KeyGoal = "scn.fail.goal";

        private readonly ScenarioDefinition _definition;
        private readonly ScenarioState _state;
        private readonly GoalEvaluator _goals;
        private readonly FailEvaluator _fails;
        private readonly ScoreCalculator _score;
        private readonly RaceResolver _races;
        private readonly IList<ChallengeDefinition> _challenges;
        private readonly IConditionAdapter _conditions;

        /// <summary>
        /// <paramref name="snapshot"/> is the definition the run keeps in its save; <paramref name="state"/> its state. The optional arguments:
        /// the race resolver (a default one), the challenges the run chose (their bonuses make the score), the evaluator of <c>condition</c> checks
        /// (none: such checks are not true).
        /// </summary>
        public ScenarioRuntime(ScenarioDefinition snapshot, ScenarioState state, GoalEvaluator ge, FailEvaluator fe, ScoreCalculator sc,
                               RaceResolver races = null, IList<ChallengeDefinition> challenges = null, IConditionAdapter conditions = null)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (state == null) throw new ArgumentNullException("state");
            _definition = snapshot;
            _state = state;
            _goals = ge ?? new GoalEvaluator();
            _fails = fe ?? new FailEvaluator();
            _score = sc ?? new ScoreCalculator();
            _races = races ?? new RaceResolver();
            _challenges = challenges ?? new ChallengeDefinition[0];
            _conditions = conditions ?? NullConditionAdapter.Instance;

            if (_state.Races == null) _state.Races = new RaceBook();
            if (_state.Fail == null) _state.Fail = new FailState();
            if (_state.Races.StartDate == default(DateTime)) _state.Races.StartDate = _state.StartDate;
            if (_definition.Goals != null)
            {
                foreach (GoalSpec goal in _definition.Goals)
                {
                    if (goal != null && goal.Id != null) _state.GoalOf(goal.Id);
                }
            }
        }

        public ScenarioDefinition Definition
        {
            get { return _definition; }
        }

        public ScenarioState State
        {
            get { return _state; }
        }

        public ScenarioStatus Status
        {
            get { return _state.Status; }
        }

        /// <summary>The result once the scenario is won or lost; null before.</summary>
        public ScenarioResult Result
        {
            get { return _state.Result; }
        }

        /// <summary>The progress of a goal for the tracker, 0..1 (1 for a done goal, 0 for a failed one).</summary>
        public double ProgressOf(GoalSpec goal, MetricsSnapshot m)
        {
            return goal == null ? 0.0 : _goals.Progress(goal, _state.FindGoal(goal.Id), m);
        }

        // ---- passes ----

        /// <summary>
        /// The pass of a game day (<c>TickPhase.DayPassed</c>): the goals that are checked daily or on events, the <c>by</c> date of every open goal,
        /// the fail rules, the win, the deadline. Returns what changed, in the order it happened.
        /// </summary>
        public IList<ScenarioOutcomeEvent> EvaluateDay(MetricsSnapshot m, DateTime today, EventBook events)
        {
            var list = new List<ScenarioOutcomeEvent>();
            if (_state.Status != ScenarioStatus.Active) return list;

            CheckContext ctx = MakeContext(m, today, events);
            foreach (GoalSpec goal in Goals())
            {
                GoalState gs = _state.GoalOf(goal.Id);
                if (gs.Status != GoalStatus.Open) continue;

                GoalStatus after = IsMonthly(goal)
                    ? _goals.CheckBy(goal, gs, today)      // a monthly goal is checked at the end of the month; its date still runs daily
                    : _goals.Evaluate(goal, gs, ctx);
                Report(list, goal, gs, after, today);
            }

            Conclude(list, ctx, today, true);
            return list;
        }

        /// <summary>
        /// The pass of a closed month (<c>TickPhase.LateMonthPassed</c>): the goals whose check is monthly (the financial ones), then the win.
        /// The fail rules and the deadline belong to the daily pass.
        /// </summary>
        public IList<ScenarioOutcomeEvent> EvaluateMonth(MetricsSnapshot m, DateTime today, EventBook events)
        {
            var list = new List<ScenarioOutcomeEvent>();
            if (_state.Status != ScenarioStatus.Active) return list;

            CheckContext ctx = MakeContext(m, today, events);
            foreach (GoalSpec goal in Goals())
            {
                GoalState gs = _state.GoalOf(goal.Id);
                if (gs.Status != GoalStatus.Open || !IsMonthly(goal)) continue;
                Report(list, goal, gs, _goals.Evaluate(goal, gs, ctx), today);
            }

            Conclude(list, ctx, today, false);
            return list;
        }

        /// <summary>
        /// A CPU was released (<c>ModEvents.CpuReleased</c>, not during a fast-forward, not while loading): the races it fits are decided, the
        /// goals that depend on releases are settled at once. The caller has put the release into the <paramref name="events"/> first
        /// (<see cref="EventBook.RecordCpuRelease"/>); a CPU that came to the player by a transfer is ignored.
        /// </summary>
        public IList<ScenarioOutcomeEvent> OnCpuReleased(CpuInfo cpu, bool isPlayer, string companyName, DateTime date, EventBook events)
        {
            var list = new List<ScenarioOutcomeEvent>();
            if (_state.Status != ScenarioStatus.Active || cpu == null) return list;
            if (isPlayer && events != null && events.IsTransferred(cpu.SaveId)) return list;

            var raceGoals = new List<GoalSpec>();
            foreach (GoalSpec goal in Goals())
            {
                if (goal.Check != null && goal.Check.Kind == CheckSpec.KindRace) raceGoals.Add(goal);
            }
            if (raceGoals.Count > 0) _races.OnCpuReleased(_state.Races, raceGoals, cpu, isPlayer, companyName, date);

            CheckContext ctx = MakeContext(null, date, events);
            foreach (GoalSpec goal in Goals())
            {
                GoalState gs = _state.GoalOf(goal.Id);
                if (gs.Status != GoalStatus.Open || !DependsOnReleases(goal)) continue;
                Report(list, goal, gs, _goals.Evaluate(goal, gs, ctx), date);
            }

            Conclude(list, ctx, date, false);
            return list;
        }

        /// <summary>
        /// The player went bankrupt (<c>BankruptcyHandler.OnCompanyWentBankrupt</c>): the scenario is lost with the text of the scenario's own
        /// bankruptcy rule when it has one, <c>scn.fail.bankrupt</c> otherwise. Does nothing once the scenario is over (E8).
        /// </summary>
        public IList<ScenarioOutcomeEvent> OnPlayerBankrupt(DateTime today)
        {
            string id = FailBankrupt;
            string key = KeyBankrupt;
            if (_definition.Fail != null)
            {
                foreach (FailSpec fail in _definition.Fail)
                {
                    if (fail != null && fail.Check != null && fail.Check.Kind == CheckSpec.KindEvent
                        && fail.Check.Event == "player.bankrupt" && !string.IsNullOrEmpty(fail.TextKey))
                    {
                        id = fail.Id;
                        key = fail.TextKey;
                        break;
                    }
                }
            }
            return Lose(id, key, null, today);
        }

        /// <summary>
        /// Loses the scenario now for a reason of the caller's (a challenge's fail rule inside a scenario, for instance). Does nothing once the
        /// scenario is over.
        /// </summary>
        public IList<ScenarioOutcomeEvent> Lose(string failId, string textKey, string[] textArgs, DateTime today)
        {
            var list = new List<ScenarioOutcomeEvent>();
            if (_state.Status != ScenarioStatus.Active) return list;
            Finish(list, ScenarioStatus.Lost, failId, textKey, textArgs, today);
            return list;
        }

        // ---- the decision ----

        // Decides win or loss after the goals of a pass were settled. `daily` also looks at the fail rules and the deadline.
        private void Conclude(List<ScenarioOutcomeEvent> list, CheckContext ctx, DateTime today, bool daily)
        {
            if (_state.Status != ScenarioStatus.Active) return;

            if (daily)
            {
                FailSpec triggered = _fails.FirstTriggered(_definition.Fail, _state.Fail, ctx);
                if (triggered != null)
                {
                    Finish(list, ScenarioStatus.Lost, triggered.Id, triggered.TextKey, null, today);
                    return;
                }
            }

            // a race that was lost and asked to lose the scenario with it
            foreach (GoalSpec goal in Goals())
            {
                if (goal.Check == null || goal.Check.Kind != CheckSpec.KindRace || !goal.Check.FailOnLose) continue;
                RaceEntry race = _state.Races.Get(goal.Id);
                if (race == null || race.Status != RaceStatus.Lost) continue;

                string date = race.WonOn.HasValue ? ContentVocabulary.FormatDate(race.WonOn.Value) : string.Empty;
                Finish(list, ScenarioStatus.Lost, "race:" + goal.Id, KeyRaceLost, new[] { race.WinnerName ?? string.Empty, date }, today);
                return;
            }

            // a required goal that can no longer be done
            foreach (GoalSpec goal in Goals())
            {
                if (!goal.Required) continue;
                if (_state.GoalOf(goal.Id).Status != GoalStatus.Failed) continue;
                Finish(list, ScenarioStatus.Lost, "goal:" + goal.Id, KeyGoal, new[] { goal.TitleKey ?? string.Empty }, today);
                return;
            }

            if (AllRequiredDone())
            {
                Finish(list, ScenarioStatus.Won, null, null, null, today);
                return;
            }

            DateTime deadline;
            if (daily && _definition.TryGetDeadline(out deadline) && today.Date >= deadline.Date)
            {
                Finish(list, ScenarioStatus.Lost, FailDeadline, KeyDeadline, null, today);
            }
        }

        private bool AllRequiredDone()
        {
            int required = 0;
            foreach (GoalSpec goal in Goals())
            {
                if (!goal.Required) continue;
                required++;
                if (_state.GoalOf(goal.Id).Status != GoalStatus.Done) return false;
            }
            return required > 0;
        }

        private void Finish(List<ScenarioOutcomeEvent> list, ScenarioStatus status, string failId, string textKey, string[] textArgs, DateTime today)
        {
            _state.Status = status;
            string[] args = textArgs ?? new string[0];

            if (status == ScenarioStatus.Lost)
            {
                _state.Fail.TriggeredId = failId ?? "lost";
                _state.Fail.TextKey = textKey;
                _state.Fail.TextArgs = args;
                _state.Fail.TriggeredOn = today.Date;
            }

            ScoreBreakdown breakdown = _score.Compute(_definition, _state, today, _challenges);
            var result = new ScenarioResult
            {
                Outcome = status,
                FinishedOn = today.Date,
                Score = breakdown.Score,
                Medal = status == ScenarioStatus.Won ? _score.Medal(_definition, breakdown.Score) : ScoreCalculator.MedalNone,
                Breakdown = breakdown,
                FailId = status == ScenarioStatus.Lost ? _state.Fail.TriggeredId : null,
                FailTextKey = status == ScenarioStatus.Lost ? textKey : null,
                FailArgs = status == ScenarioStatus.Lost ? args : new string[0],
            };
            _state.Result = result;

            list.Add(new ScenarioOutcomeEvent
            {
                Kind = status == ScenarioStatus.Won ? ScenarioOutcomeKind.Won : ScenarioOutcomeKind.Lost,
                TextKey = result.FailTextKey,
                TextArgs = result.FailArgs,
                Date = today.Date,
                Result = result,
            });
        }

        // ---- helpers ----

        private static void Report(List<ScenarioOutcomeEvent> list, GoalSpec goal, GoalState gs, GoalStatus after, DateTime today)
        {
            if (after == GoalStatus.Done)
            {
                list.Add(new ScenarioOutcomeEvent
                {
                    Kind = ScenarioOutcomeKind.GoalDone,
                    GoalId = goal.Id,
                    TextKey = goal.TitleKey,
                    Points = goal.Points,
                    Date = gs.DoneOn ?? today.Date,
                });
            }
            else if (after == GoalStatus.Failed)
            {
                list.Add(new ScenarioOutcomeEvent
                {
                    Kind = ScenarioOutcomeKind.GoalFailed,
                    GoalId = goal.Id,
                    TextKey = goal.TitleKey,
                    Date = gs.FailedOn ?? today.Date,
                });
            }
        }

        private CheckContext MakeContext(MetricsSnapshot m, DateTime today, EventBook events)
        {
            return new CheckContext
            {
                Metrics = m,
                Today = today,
                Conditions = _conditions,
                Events = events,
                Races = _state.Races,
            };
        }

        // The goals with an id and a check, in the order of the file.
        private IEnumerable<GoalSpec> Goals()
        {
            if (_definition.Goals == null) yield break;
            foreach (GoalSpec goal in _definition.Goals)
            {
                if (goal != null && goal.Id != null) yield return goal;
            }
        }

        private static bool IsMonthly(GoalSpec goal)
        {
            return goal.Check != null && string.Equals(goal.Check.Cadence, CheckSpec.CadenceMonthly, StringComparison.Ordinal);
        }

        private static bool DependsOnReleases(GoalSpec goal)
        {
            if (goal.Check == null) return false;
            string kind = goal.Check.Kind;
            return kind == CheckSpec.KindRace || kind == CheckSpec.KindCpuReleased || kind == CheckSpec.KindEvent;
        }
    }
}
