using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>What the summary of the career shows (TSK-028, FR-17, design.md "UI"): the words above the table of the companies, the flags, the achievements and the challenges of the run.</summary>
    public sealed class CareerSummaryView
    {
        /// <summary>"The year 2030 has come: the game has reached the end of the era. This is how your career went."</summary>
        public string Intro = string.Empty;

        /// <summary>"Career: from 01.01.1985 to 01.01.2030 (45 years)", or - for a run that began with a load - "Counted from the day of the load: 15.03.2029".</summary>
        public string Span = string.Empty;

        /// <summary>How the scenario of this game ended, when the run had one that is over ("Scenario ... complete: score 600, medal - Silver"); null for plain free play.</summary>
        public string Scenario;

        public UiTone ScenarioTone = UiTone.Normal;

        /// <summary>Why the run is assisted, and that the scenario was changed (the same lines as on the results of a scenario).</summary>
        public readonly List<UiLine> Flags = new List<UiLine>();

        public string AchievementsHeading = string.Empty;

        /// <summary>The titles of the achievements earned in this game; empty when there are none (<see cref="NoAchievements"/> says so).</summary>
        public readonly List<string> Achievements = new List<string>();

        /// <summary>"No achievements were earned in this game"; null when there are some.</summary>
        public string NoAchievements;

        public string ChallengesHeading = string.Empty;

        /// <summary>The challenges of the run and how each stands; empty when none was chosen (<see cref="NoChallenges"/> says so).</summary>
        public readonly List<UiLine> Challenges = new List<UiLine>();

        /// <summary>"No challenges were chosen for this game"; null when there are some.</summary>
        public string NoChallenges;
    }

    /// <summary>
    /// The model of the window "Career summary" (TSK-028, FR-17): what a free game shows on 1 January of the last year of the game (<c>GameDefines.EndYear</c>) - the achievements of this
    /// game, the challenges of the run, the flags and a few words about the career. The table that compares the companies is <see cref="ComparisonTableBuilder"/>'s, the same one the results
    /// of a scenario use. The window has no score: a free game has none.
    /// </summary>
    public static class CareerSummaryBuilder
    {
        /// <param name="run">The run in play; it has no scenario going (a scenario that is over may be in it: the career goes on as a free game).</param>
        /// <param name="today">The game date the summary opens on.</param>
        /// <param name="findAchievement">The achievement by id; one the catalog does not have is left out.</param>
        /// <param name="findChallenge">The challenge by id; one the catalog does not have is shown by its id.</param>
        /// <param name="featureName">The display name of a feature by id, for the badge "a feature was switched off"; null: the id itself.</param>
        public static CareerSummaryView Build(RunState run, DateTime today, Func<string, AchievementDefinition> findAchievement,
                                              Func<string, ChallengeDefinition> findChallenge, IUiTexts texts, Func<string, string> featureName = null)
        {
            if (run == null) throw new ArgumentNullException("run");
            if (texts == null) throw new ArgumentNullException("texts");

            var view = new CareerSummaryView();
            view.Intro = texts.Text("scn.career.intro", today.Year);
            view.Span = SpanText(run, today, texts);
            AddScenario(view, run, texts);

            BadgeLines.Fill(view.Flags, run, texts, featureName);

            view.AchievementsHeading = texts.Text("scn.result.achievements");
            view.Achievements.AddRange(ResultBuilder.AchievementTitles(run, findAchievement, texts));
            if (view.Achievements.Count == 0) view.NoAchievements = texts.Text("scn.career.noAchievements");

            view.ChallengesHeading = texts.Text("scn.result.challenges");
            view.Challenges.AddRange(ResultBuilder.ChallengeLines(run, findChallenge, texts));
            if (view.Challenges.Count == 0) view.NoChallenges = texts.Text("scn.career.noChallenges");
            return view;
        }

        /// <summary>
        /// The whole years from <paramref name="start"/> to <paramref name="today"/> (a year is complete on its anniversary); 0 for a date that is not after the start.
        /// </summary>
        public static int YearsBetween(DateTime start, DateTime today)
        {
            if (today.Date <= start.Date) return 0;

            int years = today.Year - start.Year;
            if (today.Month < start.Month || (today.Month == start.Month && today.Day < start.Day)) years--;
            return years < 0 ? 0 : years;
        }

        // A run that began with a new game knows its whole history; one made from a save without the section counts from the day of the load.
        private static string SpanText(RunState run, DateTime today, IUiTexts texts)
        {
            if (run.IsLegacy) return texts.Text("scn.career.spanLegacy", texts.Date(run.StartDate));

            int years = YearsBetween(run.StartDate, today);
            return texts.Text("scn.career.span", texts.Date(run.StartDate), texts.Date(today), texts.Plural("scn.career.years", years, years));
        }

        // A scenario of this game that ended earlier: the player kept playing as a free game, and the summary says how the scenario went.
        private static void AddScenario(CareerSummaryView view, RunState run, IUiTexts texts)
        {
            if (!run.HasScenario) return;

            ScenarioState state = run.ScenarioState;
            string title = texts.Text(run.Scenario.TitleKey);
            ScenarioResult result = state.Result;
            if (state.Status == ScenarioStatus.Won)
            {
                string medal = result == null || string.IsNullOrEmpty(result.Medal) ? ScoreCalculator.MedalNone : result.Medal;
                view.Scenario = result == null
                    ? texts.Text("scn.career.scenario.wonShort", title)
                    : texts.Text("scn.career.scenario.won", title, result.Score, texts.Text("scn.medal." + medal));
                view.ScenarioTone = UiTone.Good;
            }
            else if (state.Status == ScenarioStatus.Lost)
            {
                view.Scenario = texts.Text("scn.career.scenario.lost", title);
                view.ScenarioTone = UiTone.Bad;
            }
        }
    }
}
