using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// Where the race for a CPU stands, as the tracker shows it: the fastest CPU the player has released and the fastest one of the AI companies, both in kHz
    /// (the unit of <c>Cpu.Frequency</c>). The game-side layer fills it; 0 means "none yet".
    /// </summary>
    public sealed class RaceStanding
    {
        /// <summary>The highest frequency among the CPUs the player released (kHz); 0 when there is none.</summary>
        public double PlayerBest;

        /// <summary>The highest frequency among the CPUs the AI companies released (kHz); 0 when there is none.</summary>
        public double RivalBest;
    }

    /// <summary>One goal on the tracker: its title, where it stands, and the detail line under it (progress, the race, the date it was done).</summary>
    public sealed class TrackerGoalLine
    {
        public string GoalId;

        /// <summary>The title; a goal that is not required carries "(optional)" after it.</summary>
        public string Title;

        public bool Required;

        public GoalStatus Status;

        /// <summary>How far the goal is, 0..1 (1 once it is done, 0 once it is failed).</summary>
        public double Progress;

        /// <summary>The line under the title: "9.40% / 15.00%", the race, "done 12.03.1978"; empty when the goal has nothing to tell.</summary>
        public string Detail;

        public UiTone Tone;
    }

    /// <summary>What the goal tracker shows (TSK-024, FR-10): the goals, the deadline, the score so far and the badges of the run.</summary>
    public sealed class TrackerView
    {
        public readonly List<TrackerGoalLine> Goals = new List<TrackerGoalLine>();

        /// <summary>The run has a scenario to show (a free-play run has none).</summary>
        public bool HasScenario;

        /// <summary>The title of the scenario.</summary>
        public string Title = string.Empty;

        /// <summary>"Deadline: 01.07.1976, 142 days left"; null once the scenario is over or has no deadline.</summary>
        public string Deadline;

        /// <summary>Days from today to the deadline; -1 without a deadline.</summary>
        public int DaysLeft = -1;

        /// <summary>"Score now: 500" while the scenario goes; null otherwise.</summary>
        public string Score;

        /// <summary>"Scenario complete" or "Scenario failed" once the scenario is over; null while it goes.</summary>
        public string Outcome;

        /// <summary>The scenario is over (won or lost).</summary>
        public bool IsFinished;

        /// <summary>The badges of the run: why it is assisted, that the scenario was changed.</summary>
        public readonly List<UiLine> Badges = new List<UiLine>();

        /// <summary>An open race is among the goals: the game-side layer then reads the fastest CPU of the rivals (it is not free).</summary>
        public bool NeedsRaceStanding;
    }

    /// <summary>
    /// The model of the goal tracker (TSK-024; FR-10, FR-7 item 4): turns the run, the last snapshot of the metrics and the race standing into the lines of the panel.
    /// The numbers are formatted by the <see cref="IUiTexts"/> of the caller, so the model is the same in the game and in a test. Nothing is changed.
    /// </summary>
    public static class TrackerBuilder
    {
        /// <param name="run">The run in play; null or a free-play run gives a view without a scenario.</param>
        /// <param name="metrics">The metrics after the last pass of the engine; null before the first one.</param>
        /// <param name="race">The standing of the open races; null: no data (the race line then shows dashes).</param>
        /// <param name="featureName">The display name of a feature by id, for the badge "a feature was switched off"; null: the id itself.</param>
        /// <param name="challenges">The definitions of the run's challenges (their bonus is part of the score so far).</param>
        public static TrackerView Build(RunState run, MetricsSnapshot metrics, DateTime today, RaceStanding race, IUiTexts texts,
                                        Func<string, string> featureName = null, IList<ChallengeDefinition> challenges = null)
        {
            if (texts == null) throw new ArgumentNullException("texts");

            var view = new TrackerView();
            if (run == null || !run.HasScenario) return view;

            ScenarioDefinition definition = run.Scenario;
            ScenarioState state = run.ScenarioState;
            view.HasScenario = true;
            view.Title = texts.Text(definition.TitleKey);
            view.IsFinished = state.Status == ScenarioStatus.Won || state.Status == ScenarioStatus.Lost;

            var evaluator = new GoalEvaluator();
            if (definition.Goals != null)
            {
                foreach (GoalSpec goal in definition.Goals)
                {
                    if (goal == null) continue;
                    view.Goals.Add(GoalLine(goal, state, evaluator, metrics, today, race, texts, view));
                }
            }

            DateTime deadline;
            if (!view.IsFinished && definition.TryGetDeadline(out deadline))
            {
                int left = Math.Max(0, (deadline.Date - today.Date).Days);
                view.DaysLeft = left;
                view.Deadline = texts.Text("scn.tracker.deadline", texts.Date(deadline), texts.Plural("scn.tracker.daysLeft", left, left));
            }

            if (view.IsFinished)
            {
                bool won = state.Status == ScenarioStatus.Won;
                view.Outcome = texts.Text(won ? "scn.result.won" : "scn.result.lost");
                if (state.Result != null) view.Score = texts.Text("scn.result.score", state.Result.Score);
            }
            else
            {
                ScoreBreakdown now = new ScoreCalculator().Compute(definition, state, today, challenges);
                view.Score = texts.Text("scn.tracker.score", now.Score);
            }

            BadgeLines.Fill(view.Badges, run, texts, featureName);
            return view;
        }

        private static TrackerGoalLine GoalLine(GoalSpec goal, ScenarioState state, GoalEvaluator evaluator, MetricsSnapshot metrics, DateTime today,
                                                RaceStanding race, IUiTexts texts, TrackerView view)
        {
            GoalState goalState = state.FindGoal(goal.Id) ?? new GoalState();
            string title = texts.Text(goal.TitleKey);
            if (!goal.Required) title += " (" + texts.Text("scn.goal.optional") + ")";

            var line = new TrackerGoalLine
            {
                GoalId = goal.Id,
                Title = title,
                Required = goal.Required,
                Status = goalState.Status,
                Progress = evaluator.Progress(goal, goalState, metrics),
                Detail = string.Empty,
                Tone = UiTone.Normal,
            };

            bool isRace = goal.Check != null && goal.Check.Kind == CheckSpec.KindRace;
            switch (goalState.Status)
            {
                case GoalStatus.Done:
                    line.Tone = UiTone.Good;
                    line.Detail = texts.Text("scn.tracker.done", texts.Date(goalState.DoneOn ?? today));
                    break;

                case GoalStatus.Failed:
                    line.Tone = UiTone.Bad;
                    line.Detail = FailedDetail(goal, state, goalState, texts);
                    break;

                default:
                    if (isRace)
                    {
                        view.NeedsRaceStanding = true;
                        line.Detail = RaceLine(race, texts);
                    }
                    else
                    {
                        line.Detail = ProgressLine(goal, metrics, texts, out line.Tone);
                    }
                    break;
            }
            return line;
        }

        // A lost race tells who won it and when; any other failed goal says "failed".
        private static string FailedDetail(GoalSpec goal, ScenarioState state, GoalState goalState, IUiTexts texts)
        {
            RaceEntry entry = state.Races == null ? null : state.Races.Get(goal.Id);
            if (entry != null && entry.Status == RaceStatus.Lost && !string.IsNullOrEmpty(entry.WinnerName))
            {
                string when = entry.WonOn.HasValue ? texts.Date(entry.WonOn.Value) : string.Empty;
                return texts.Text(ScenarioRuntime.KeyRaceLost, entry.WinnerName, when);
            }
            return texts.Text("scn.tracker.failed");
        }

        private static string RaceLine(RaceStanding race, IUiTexts texts)
        {
            string mine = race != null && race.PlayerBest > 0 ? texts.Frequency(race.PlayerBest) : Dash;
            string theirs = race != null && race.RivalBest > 0 ? texts.Frequency(race.RivalBest) : Dash;
            return texts.Text("scn.tracker.race", mine, theirs);
        }

        // "9.40% / 15.00%" for a goal with a progress line; "unavailable" when the content names a metric this build does not know; nothing for a goal without one.
        private static string ProgressLine(GoalSpec goal, MetricsSnapshot metrics, IUiTexts texts, out UiTone tone)
        {
            tone = UiTone.Normal;
            ProgressSpec spec = goal.Progress;
            if (spec == null) return string.Empty;

            if (!ContentVocabulary.IsKnownMetric(spec.Metric))
            {
                tone = UiTone.Muted;
                return texts.Text("scn.tracker.unavailable");
            }

            double value;
            string current = metrics != null && metrics.TryGet(spec.Metric, out value) && !double.IsNaN(value) ? MetricFormat.Format(spec.Format, value, texts) : Dash;
            return texts.Text("scn.tracker.progress", current, MetricFormat.Format(spec.Format, spec.Target, texts));
        }

        /// <summary>The text of an unknown number.</summary>
        public const string Dash = "—";
    }

    /// <summary>The lines about the assisted flag and about changed content, shared by the tracker, the briefing and the results.</summary>
    public static class BadgeLines
    {
        /// <summary>Appends one line per reason the run is assisted (cheats, a feature switched off, another reason) and one for changed content.</summary>
        public static void Fill(IList<UiLine> lines, RunState run, IUiTexts texts, Func<string, string> featureName = null)
        {
            if (lines == null) throw new ArgumentNullException("lines");
            if (run == null || texts == null) return;

            if (run.Assisted)
            {
                if (run.AssistedReasons.Count == 0) lines.Add(new UiLine(texts.Text("scn.badge.assisted"), UiTone.Warn));
                foreach (string reason in run.AssistedReasons) lines.Add(new UiLine(ReasonText(reason, texts, featureName), UiTone.Warn));
            }
            if (run.Custom) lines.Add(new UiLine(texts.Text("scn.badge.custom"), UiTone.Warn));
        }

        /// <summary>The words for one reason: <c>cheats.enabled</c>, <c>feature.disabled:id</c> (with the name of the feature), or the reason as it is.</summary>
        public static string ReasonText(string reason, IUiTexts texts, Func<string, string> featureName)
        {
            if (string.Equals(reason, AssistedRegistry.ReasonCheats, StringComparison.Ordinal)) return texts.Text("scn.badge.assisted.cheats");

            const string featurePrefix = "feature.disabled:";
            if (reason != null && reason.StartsWith(featurePrefix, StringComparison.Ordinal))
            {
                string id = reason.Substring(featurePrefix.Length);
                string name = featureName == null ? null : featureName(id);
                return texts.Text("scn.badge.assisted.feature", string.IsNullOrEmpty(name) ? id : name);
            }
            return texts.Text("scn.badge.assisted.other", reason ?? string.Empty);
        }
    }

    /// <summary>How the numbers of a metric are written: money, a share, a frequency or a plain count.</summary>
    public static class MetricFormat
    {
        public const string Money = "money";
        public const string Percent = "percent";
        public const string Count = "count";
        public const string Frequency = "frequency";

        /// <summary>The way the metrics of the vocabulary are shown when the content does not say (the achievements window has no <c>progress.format</c>).</summary>
        public static string Of(string metricId)
        {
            if (metricId == null) return Count;
            if (metricId == "money" || metricId == "money.min" || metricId == "profit.lastMonth" || metricId.StartsWith("revenue.", StringComparison.Ordinal)) return Money;
            if (metricId.StartsWith("share.", StringComparison.Ordinal) || metricId == "popularity.max") return Percent;
            if (metricId == "cpu.maxFrequency") return Frequency;
            return Count;
        }

        /// <summary>A value in a format (<see cref="Money"/>, <see cref="Percent"/>, <see cref="Frequency"/>; anything else is a count).</summary>
        public static string Format(string format, double value, IUiTexts texts)
        {
            switch (format)
            {
                case Money: return texts.Money(value);
                case Percent: return texts.Percent(value);
                case Frequency: return texts.Frequency(value);
                default: return texts.Number(value);
            }
        }
    }
}
