using System;
using System.Collections.Generic;
using System.Text;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>One goal on the results window: its title, whether it was required, its points, how it ended and when.</summary>
    public sealed class ResultGoalLine
    {
        public string GoalId;
        public string Title;
        public bool Required;
        public int Points;
        public GoalStatus Status;

        /// <summary>"done 12.03.1978, +300", "failed 01.07.1976" or "not done".</summary>
        public string Detail;

        public UiTone Tone;
    }

    /// <summary>What the results window of a scenario shows (TSK-025, FR-9 item 1): the outcome, the reason, the goals, the breakdown of the score, the medal, the flags.</summary>
    public sealed class ResultView
    {
        public string ScenarioTitle = string.Empty;

        public bool IsWin;

        /// <summary>"Scenario complete" or "Scenario failed".</summary>
        public string Headline = string.Empty;

        /// <summary>"Reason: Time is up"; null for a win.</summary>
        public string Reason;

        /// <summary>"Date: 01.01.1977".</summary>
        public string Date = string.Empty;

        public int Score;

        /// <summary>The medal id (<c>gold</c>, <c>silver</c>, <c>bronze</c>, <c>completed</c>); <see cref="ScoreCalculator.MedalNone"/> for a loss.</summary>
        public string Medal = ScoreCalculator.MedalNone;

        /// <summary>"Medal: Silver"; null for a loss.</summary>
        public string MedalText;

        /// <summary>"Score: 600".</summary>
        public string ScoreText = string.Empty;

        public readonly List<ResultGoalLine> Goals = new List<ResultGoalLine>();

        /// <summary>The parts of the score: the goals, the time bonus, the bonus of the challenges, the total.</summary>
        public readonly List<UiLine> Breakdown = new List<UiLine>();

        /// <summary>Why the run is assisted, and that the scenario was changed (such a result makes no record).</summary>
        public readonly List<UiLine> Flags = new List<UiLine>();

        /// <summary>The titles of the achievements earned in this run.</summary>
        public readonly List<string> Achievements = new List<string>();

        /// <summary>The challenges of the run and how each of them stands.</summary>
        public readonly List<UiLine> Challenges = new List<UiLine>();
    }

    /// <summary>
    /// The model of the results window and of the letter with the result (TSK-023, TSK-025; FR-9 items 1 and 5, FR-8): the finished scenario, its score taken apart, the
    /// goals with their dates, the flags, the achievements and the challenges of the run. The table of the companies is <see cref="ComparisonTableBuilder"/>'s.
    /// </summary>
    public static class ResultBuilder
    {
        /// <param name="run">The run the scenario belonged to.</param>
        /// <param name="result">How the scenario ended.</param>
        /// <param name="findAchievement">The achievement by id; one the catalog does not have is left out.</param>
        /// <param name="findChallenge">The challenge by id; one the catalog does not have is shown by its id.</param>
        /// <param name="featureName">The display name of a feature by id, for the badge "a feature was switched off"; null: the id itself.</param>
        public static ResultView Build(RunState run, ScenarioResult result, Func<string, AchievementDefinition> findAchievement,
                                       Func<string, ChallengeDefinition> findChallenge, IUiTexts texts, Func<string, string> featureName = null)
        {
            if (run == null) throw new ArgumentNullException("run");
            if (result == null) throw new ArgumentNullException("result");
            if (texts == null) throw new ArgumentNullException("texts");

            ScenarioDefinition definition = run.Scenario;
            ScenarioState state = run.ScenarioState;

            var view = new ResultView();
            view.ScenarioTitle = definition == null ? string.Empty : texts.Text(definition.TitleKey);
            view.IsWin = result.IsWin;
            view.Headline = texts.Text(result.IsWin ? "scn.result.won" : "scn.result.lost");
            view.Reason = result.IsWin ? null : texts.Text("scn.result.reason", ReasonText(result, definition, texts));
            view.Date = texts.Text("scn.result.date", texts.Date(result.FinishedOn));
            view.Score = result.Score;
            view.Medal = string.IsNullOrEmpty(result.Medal) ? ScoreCalculator.MedalNone : result.Medal;
            view.ScoreText = texts.Text("scn.result.score", result.Score);
            view.MedalText = result.IsWin ? texts.Text("scn.result.medal", texts.Text("scn.medal." + view.Medal)) : null;

            if (definition != null && definition.Goals != null)
            {
                foreach (GoalSpec goal in definition.Goals)
                {
                    if (goal == null) continue;
                    GoalState goalState = state == null ? null : state.FindGoal(goal.Id);
                    view.Goals.Add(GoalLine(goal, goalState ?? new GoalState(), texts));
                }
            }

            AddBreakdown(view, result.Breakdown, texts);
            BadgeLines.Fill(view.Flags, run, texts, featureName);

            view.Achievements.AddRange(AchievementTitles(run, findAchievement, texts));
            view.Challenges.AddRange(ChallengeLines(run, findChallenge, texts));
            return view;
        }

        /// <summary>
        /// The titles of the achievements earned in the run, in the order they came; marked "with help" when the run is assisted. An id the catalog does not know is left out.
        /// The results of a scenario and the summary of the career (TSK-028) show the same list.
        /// </summary>
        public static List<string> AchievementTitles(RunState run, Func<string, AchievementDefinition> findAchievement, IUiTexts texts)
        {
            if (run == null) throw new ArgumentNullException("run");
            if (texts == null) throw new ArgumentNullException("texts");

            var titles = new List<string>();
            foreach (string id in run.AchievementsThisRun)
            {
                AchievementDefinition achievement = findAchievement == null ? null : findAchievement(id);
                if (achievement == null) continue;
                string title = texts.Text(achievement.TitleKey);
                titles.Add(run.Assisted ? title + " (" + texts.Text("ach.badge.assisted") + ")" : title);
            }
            return titles;
        }

        /// <summary>The challenges of the run and how each of them stands (in progress, passed, failed); one that the catalog does not know is shown by its id.</summary>
        public static List<UiLine> ChallengeLines(RunState run, Func<string, ChallengeDefinition> findChallenge, IUiTexts texts)
        {
            if (run == null) throw new ArgumentNullException("run");
            if (texts == null) throw new ArgumentNullException("texts");

            var lines = new List<UiLine>();
            foreach (string id in run.Challenges) lines.Add(ChallengeLine(id, run, findChallenge, texts));
            return lines;
        }

        /// <summary>
        /// The arguments of the letter with the result: <c>{0}</c> the title of the scenario (the subject, <c>scn.result.mailSubject</c>), <c>{1}</c> the outcome with the reason,
        /// the date, the score and the medal, <c>{2}</c> the goals, <c>{3}</c> the parts of the score and the flags. The body <c>scn.mail.result.body</c> uses <c>{1}</c>..<c>{3}</c>.
        /// </summary>
        public static string[] MailArgs(ResultView view, IUiTexts texts)
        {
            if (view == null) throw new ArgumentNullException("view");
            if (texts == null) throw new ArgumentNullException("texts");

            var head = new StringBuilder();
            Append(head, view.Headline);
            Append(head, view.Reason);
            Append(head, view.Date);
            Append(head, view.ScoreText);
            Append(head, view.MedalText);

            var goals = new StringBuilder(texts.Text("scn.goals.title"));
            foreach (ResultGoalLine goal in view.Goals) goals.Append("\n• ").Append(goal.Title).Append(" — ").Append(goal.Detail);

            var rest = new StringBuilder();
            foreach (UiLine line in view.Breakdown) Append(rest, line.Text);
            foreach (UiLine line in view.Flags) Append(rest, line.Text);

            return new[] { view.ScenarioTitle, head.ToString(), goals.ToString(), rest.ToString() };
        }

        /// <summary>
        /// The words of the reason of a loss. The goal that was failed comes as the key of its title and is looked up; the date of a lost race is written the way the player
        /// reads dates; every other reason is its key with its arguments.
        /// </summary>
        public static string ReasonText(ScenarioResult result, ScenarioDefinition definition, IUiTexts texts)
        {
            if (result == null || string.IsNullOrEmpty(result.FailTextKey)) return string.Empty;

            string[] args = result.FailArgs ?? new string[0];
            var shown = new object[args.Length];
            for (int i = 0; i < args.Length; i++) shown[i] = args[i];

            if (string.Equals(result.FailTextKey, ScenarioRuntime.KeyGoal, StringComparison.Ordinal) && args.Length > 0)
            {
                shown[0] = texts.Text(args[0]);
            }
            else if (string.Equals(result.FailTextKey, ScenarioRuntime.KeyRaceLost, StringComparison.Ordinal) && args.Length > 1)
            {
                DateTime when;
                if (ContentVocabulary.TryParseDate(args[1], out when)) shown[1] = texts.Date(when);
            }
            return texts.Text(result.FailTextKey, shown);
        }

        private static ResultGoalLine GoalLine(GoalSpec goal, GoalState goalState, IUiTexts texts)
        {
            var line = new ResultGoalLine
            {
                GoalId = goal.Id,
                Title = texts.Text(goal.TitleKey),
                Required = goal.Required,
                Points = goal.Points,
                Status = goalState.Status,
            };
            switch (goalState.Status)
            {
                case GoalStatus.Done:
                    line.Tone = UiTone.Good;
                    line.Detail = texts.Text("scn.result.goal.done", goalState.DoneOn.HasValue ? texts.Date(goalState.DoneOn.Value) : string.Empty, goal.Points);
                    break;
                case GoalStatus.Failed:
                    line.Tone = UiTone.Bad;
                    line.Detail = goalState.FailedOn.HasValue ? texts.Text("scn.result.goal.failedOn", texts.Date(goalState.FailedOn.Value)) : texts.Text("scn.tracker.failed");
                    break;
                default:
                    line.Tone = UiTone.Muted;
                    line.Detail = texts.Text("scn.result.goal.open");
                    break;
            }
            return line;
        }

        private static void AddBreakdown(ResultView view, ScoreBreakdown breakdown, IUiTexts texts)
        {
            if (breakdown == null) return;

            view.Breakdown.Add(new UiLine(texts.Text("scn.score.goals", breakdown.GoalPoints)));
            view.Breakdown.Add(new UiLine(texts.Text("scn.score.timeBonus", breakdown.TimeBonus)));
            if (breakdown.ChallengeBonusPercent > 0) view.Breakdown.Add(new UiLine(texts.Text("scn.score.challenges", breakdown.ChallengeBonusPercent)));
            view.Breakdown.Add(new UiLine(texts.Text("scn.score.total", breakdown.Score), view.IsWin ? UiTone.Good : UiTone.Bad));
        }

        private static UiLine ChallengeLine(string id, RunState run, Func<string, ChallengeDefinition> findChallenge, IUiTexts texts)
        {
            ChallengeDefinition challenge = findChallenge == null ? null : findChallenge(id);
            string title = challenge == null ? id : texts.Text(challenge.TitleKey);

            ChallengeState state = run.ChallengeStates == null ? null : run.ChallengeStates.Get(id);
            ChallengeStatus status = state == null ? ChallengeStatus.Active : state.Status;
            switch (status)
            {
                case ChallengeStatus.Failed: return new UiLine(texts.Text("scn.challenge.line", title, texts.Text("scn.challenge.failed")), UiTone.Bad);
                case ChallengeStatus.Passed: return new UiLine(texts.Text("scn.challenge.line", title, texts.Text("scn.challenge.passed")), UiTone.Good);
                default: return new UiLine(texts.Text("scn.challenge.line", title, texts.Text("scn.challenge.active")));
            }
        }

        private static void Append(StringBuilder builder, string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(line);
        }
    }

    /// <summary>One company of the comparison table, every cell ready text.</summary>
    public sealed class ComparisonCells
    {
        public bool IsPlayer;
        public bool IsBankrupt;
        public string Name = string.Empty;
        public string Money = string.Empty;
        public string Revenue = string.Empty;
        public string Share = string.Empty;
        public string Cpus = string.Empty;
        public string Frequency = string.Empty;
        public string Bankrupt = string.Empty;
    }

    /// <summary>
    /// The text of the table that compares the companies (FR-9 item 2, FR-17; design.md "UI"): the header and a row per company - name, money, the revenue of twelve months,
    /// the share of the market in the last full month, the number of CPUs, the top frequency, the bankruptcy mark. Money, shares and frequencies are written by the formats
    /// of the game (<see cref="IUiTexts"/>). The rows come from <see cref="ComparisonBuilder"/> already in order.
    /// </summary>
    public static class ComparisonTableBuilder
    {
        /// <summary>The captions of the seven columns, in the order of <see cref="ComparisonCells"/>.</summary>
        public static string[] Header(IUiTexts texts)
        {
            return new[]
            {
                texts.Text("scn.table.company"), texts.Text("scn.table.money"), texts.Text("scn.table.revenue"), texts.Text("scn.table.share"),
                texts.Text("scn.table.cpus"), texts.Text("scn.table.frequency"), texts.Text("scn.table.bankrupt"),
            };
        }

        public static List<ComparisonCells> Rows(IList<ComparisonRow> rows, IUiTexts texts)
        {
            var list = new List<ComparisonCells>();
            if (rows == null) return list;

            foreach (ComparisonRow row in rows)
            {
                if (row == null) continue;
                list.Add(new ComparisonCells
                {
                    IsPlayer = row.IsPlayer,
                    IsBankrupt = row.IsBankrupt,
                    Name = row.Name ?? string.Empty,
                    Money = texts.Money(row.Money),
                    Revenue = texts.Money(row.Revenue12m),
                    Share = texts.Percent(row.MarketShare),
                    Cpus = texts.Number(row.CpuCount),
                    Frequency = row.MaxFrequency > 0 ? texts.Frequency(row.MaxFrequency) : TrackerBuilder.Dash,
                    Bankrupt = row.IsBankrupt ? texts.Text("scn.table.yes") : string.Empty,
                });
            }
            return list;
        }
    }
}
