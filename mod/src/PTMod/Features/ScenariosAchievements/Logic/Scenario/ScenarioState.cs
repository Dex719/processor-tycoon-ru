using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>The life of a scenario in a run: there is none, it is going, it was won, it was lost. After <c>Won</c> or <c>Lost</c> nothing changes (E8).</summary>
    public enum ScenarioStatus
    {
        None,
        Active,
        Won,
        Lost,
    }

    /// <summary>
    /// The state of the scenario of a run - the <c>scenario</c> block of the save section without the snapshot of the definition (which the codec
    /// keeps next to it): the status, the first day, the state of every goal, the races, the fail rules, the result. Plain fields for the codec.
    /// </summary>
    public sealed class ScenarioState
    {
        public string Id;

        public ScenarioStatus Status = ScenarioStatus.None;

        /// <summary>The first day of the run (<c>run.startDate</c>): the clock of the time bonus and the start of the races.</summary>
        public DateTime StartDate;

        /// <summary>The hash of the record the run started from (<see cref="ScenarioDefinition.ContentHash"/>).</summary>
        public string ContentHash;

        /// <summary>The state of every goal by goal id.</summary>
        public readonly Dictionary<string, GoalState> Goals = new Dictionary<string, GoalState>(StringComparer.Ordinal);

        public RaceBook Races = new RaceBook();

        public FailState Fail = new FailState();

        /// <summary>The result, once the scenario is won or lost; null before.</summary>
        public ScenarioResult Result;

        /// <summary>The state of a scenario that is just starting: active, every goal open, no races decided.</summary>
        public static ScenarioState Start(ScenarioDefinition definition, DateTime startDate)
        {
            if (definition == null) throw new ArgumentNullException("definition");
            var state = new ScenarioState
            {
                Id = definition.Id,
                Status = ScenarioStatus.Active,
                StartDate = startDate.Date,
                ContentHash = definition.ContentHash,
                Races = new RaceBook(startDate.Date),
            };
            if (definition.Goals != null)
            {
                foreach (GoalSpec goal in definition.Goals)
                {
                    if (goal != null && goal.Id != null) state.Goals[goal.Id] = new GoalState();
                }
            }
            return state;
        }

        /// <summary>The state of a goal; a goal that has none yet (a state read from an older save) gets an open one.</summary>
        public GoalState GoalOf(string goalId)
        {
            GoalState goal;
            if (!Goals.TryGetValue(goalId, out goal))
            {
                goal = new GoalState();
                Goals[goalId] = goal;
            }
            return goal;
        }

        /// <summary>The state of a goal, or null when there is none.</summary>
        public GoalState FindGoal(string goalId)
        {
            GoalState goal;
            return goalId != null && Goals.TryGetValue(goalId, out goal) ? goal : null;
        }
    }

    /// <summary>How a scenario ended: win or loss, the day, the score and the medal with the way it was made up, the reason of a loss.</summary>
    public sealed class ScenarioResult
    {
        /// <summary><see cref="ScenarioStatus.Won"/> or <see cref="ScenarioStatus.Lost"/>.</summary>
        public ScenarioStatus Outcome;

        public DateTime FinishedOn;

        /// <summary>0 for a loss.</summary>
        public int Score;

        /// <summary><c>gold</c>, <c>silver</c>, <c>bronze</c>, <c>completed</c> for a win; <see cref="ScoreCalculator.MedalNone"/> for a loss.</summary>
        public string Medal;

        public ScoreBreakdown Breakdown;

        /// <summary>The fail rule or reason that lost the scenario (<c>bankrupt</c>, <c>deadline</c>, <c>goal:g1</c>, <c>race:g1</c>, a rule id, <c>challenge:id</c>); null for a win.</summary>
        public string FailId;

        /// <summary>The L10n key of the reason of a loss (<c>scn.fail.deadline</c> and so on); null for a win.</summary>
        public string FailTextKey;

        /// <summary>The arguments of that text: plain strings, dates as <c>yyyy-MM-dd</c>.</summary>
        public string[] FailArgs = new string[0];

        public bool IsWin
        {
            get { return Outcome == ScenarioStatus.Won; }
        }

        /// <summary>
        /// The internal events of the feature that this result is (<see cref="ContentVocabulary.Events"/>): <c>scenario.won</c>, and
        /// <c>scenario.gold</c> besides for the gold medal; <c>scenario.lost</c> for a loss. What the director records for the achievements.
        /// </summary>
        public string[] EventIds()
        {
            if (Outcome == ScenarioStatus.Lost) return new[] { "scenario.lost" };
            if (Outcome != ScenarioStatus.Won) return new string[0];
            return string.Equals(Medal, "gold", StringComparison.Ordinal) ? new[] { "scenario.won", "scenario.gold" } : new[] { "scenario.won" };
        }
    }

    /// <summary>What happened in a pass of the scenario, for the notifications and the windows.</summary>
    public enum ScenarioOutcomeKind
    {
        GoalDone,
        GoalFailed,
        Won,
        Lost,
    }

    /// <summary>One thing that happened: a goal was done or failed, the scenario was won or lost.</summary>
    public sealed class ScenarioOutcomeEvent
    {
        public ScenarioOutcomeKind Kind;

        /// <summary>The goal, for <see cref="ScenarioOutcomeKind.GoalDone"/> and <see cref="ScenarioOutcomeKind.GoalFailed"/>.</summary>
        public string GoalId;

        /// <summary>The L10n key of the goal's title, or of the reason of a loss.</summary>
        public string TextKey;

        /// <summary>The arguments of <see cref="TextKey"/> (a loss only).</summary>
        public string[] TextArgs = new string[0];

        /// <summary>The points the goal brought (<see cref="ScenarioOutcomeKind.GoalDone"/>).</summary>
        public int Points;

        public DateTime Date;

        /// <summary>The result of the scenario, for <see cref="ScenarioOutcomeKind.Won"/> and <see cref="ScenarioOutcomeKind.Lost"/>.</summary>
        public ScenarioResult Result;
    }
}
