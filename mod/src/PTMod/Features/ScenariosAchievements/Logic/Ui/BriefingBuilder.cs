using System;
using System.Collections.Generic;
using System.Text;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// What the briefing of a scenario says (TSK-023, FR-4 item 2): the window and the letter are made of the same pieces - the title, the story, the starting
    /// parameters, the goals with their points, the deadline, the challenges that were chosen. Every piece is ready text; one that does not apply is null.
    /// </summary>
    public sealed class BriefingView
    {
        public string ScenarioTitle = string.Empty;

        /// <summary>"Briefing: Garage startup" - the title of the window and the subject of the letter.</summary>
        public string Title = string.Empty;

        /// <summary>The story of the scenario (the text <c>scn.&lt;id&gt;.briefing</c>).</summary>
        public string Story = string.Empty;

        /// <summary>The starting parameters: year, company, difficulty, money, lines, technology.</summary>
        public string Start = string.Empty;

        /// <summary>"Goals" with a bullet for every goal: its title, whether it is required, its points.</summary>
        public string Goals = string.Empty;

        /// <summary>"Deadline: until 01.07.1976"; null without a deadline.</summary>
        public string Deadline;

        /// <summary>"Challenges: A, B" and the bonus under it; null when no challenge was chosen.</summary>
        public string Challenges;

        /// <summary>Why the run is assisted, and that the scenario was changed.</summary>
        public readonly List<UiLine> Badges = new List<UiLine>();
    }

    /// <summary>
    /// The model of the briefing (TSK-023): the pieces above for a scenario, the challenges chosen and the run, and the arguments of the letter. The letter uses the keys
    /// <c>scn.briefing.title</c> and <c>scn.mail.briefing.body</c>; the core draws them again in the language of the moment (FR-14 item 14 of the core).
    /// </summary>
    public static class BriefingBuilder
    {
        public static BriefingView Build(ScenarioDefinition scenario, IList<string> challengeIds, Func<string, ChallengeDefinition> findChallenge, RunState run,
                                         IUiTexts texts, Func<string, string> featureName = null)
        {
            if (scenario == null) throw new ArgumentNullException("scenario");
            if (texts == null) throw new ArgumentNullException("texts");

            var view = new BriefingView();
            view.ScenarioTitle = texts.Text(scenario.TitleKey);
            view.Title = texts.Text("scn.briefing.title", view.ScenarioTitle);
            view.Story = texts.Text(scenario.BriefingKey);
            view.Start = StartText(scenario, texts);
            view.Goals = GoalsText(scenario, texts);

            DateTime deadline;
            if (scenario.TryGetDeadline(out deadline)) view.Deadline = texts.Text("scn.deadline", texts.Date(deadline));

            view.Challenges = ChallengesText(challengeIds, findChallenge, texts);
            BadgeLines.Fill(view.Badges, run, texts, featureName);
            return view;
        }

        /// <summary>
        /// The arguments of the two letters' texts: <c>{0}</c> the title of the scenario (the subject), <c>{1}</c> the story with the starting parameters, <c>{2}</c> the goals,
        /// <c>{3}</c> the deadline and the challenges. The body <c>scn.mail.briefing.body</c> uses <c>{1}</c>..<c>{3}</c>.
        /// </summary>
        public static string[] MailArgs(BriefingView view)
        {
            if (view == null) throw new ArgumentNullException("view");

            var tail = new StringBuilder();
            Append(tail, view.Deadline);
            Append(tail, view.Challenges);
            foreach (UiLine badge in view.Badges) Append(tail, badge.Text);

            return new[] { view.ScenarioTitle, Join(view.Story, view.Start), view.Goals, tail.ToString() };
        }

        /// <summary>"Goals", then a bullet per goal: "• Title - required, 300 points" (the same lines as the new-game panel shows).</summary>
        public static string GoalsText(ScenarioDefinition scenario, IUiTexts texts)
        {
            var text = new StringBuilder(texts.Text("scn.goals.title"));
            if (scenario.Goals == null) return text.ToString();

            foreach (GoalSpec goal in scenario.Goals)
            {
                if (goal == null) continue;
                text.Append("\n• ").Append(texts.Text(goal.TitleKey)).Append(" — ")
                    .Append(texts.Text(goal.Required ? "scn.goal.required" : "scn.goal.optional")).Append(", ")
                    .Append(texts.Plural("scn.goal.points", goal.Points, goal.Points));
            }
            return text.ToString();
        }

        /// <summary>The starting parameters in two lines: "1985, CPU Fabless, Normal, $125M, 0 lines, until 01.01.1990" and the technology.</summary>
        public static string StartText(ScenarioDefinition scenario, IUiTexts texts)
        {
            ScenarioStart start = scenario.Start;
            if (start == null) return string.Empty;

            DateTime deadline;
            string until = scenario.TryGetDeadline(out deadline) ? texts.Date(deadline) : string.Empty;
            string company = texts.Text("scn.companyType." + (start.CompanyType ?? "idm"));

            return texts.Text("scn.preview.title") + "\n"
                   + texts.Text("scn.preview.summary", start.Year, company, texts.Text("scn.difficulty." + start.DifficultyLevel), texts.Money(start.StartingFunds),
                       texts.Plural("scn.preview.lines", start.FactorySize, start.FactorySize), until) + "\n"
                   + texts.Text("scn.preview.technology", texts.Text("scn.tech." + start.StartingTechnology));
        }

        // "Challenges: A, B" and "Score bonus: +25%"; null when none of the ids is known.
        private static string ChallengesText(IList<string> ids, Func<string, ChallengeDefinition> find, IUiTexts texts)
        {
            if (ids == null || ids.Count == 0) return null;

            var titles = new List<string>();
            var found = new List<ChallengeDefinition>();
            foreach (string id in ids)
            {
                ChallengeDefinition challenge = find == null ? null : find(id);
                if (challenge == null) continue;
                titles.Add(texts.Text(challenge.TitleKey));
                found.Add(challenge);
            }
            if (titles.Count == 0) return null;

            return texts.Text("scn.briefing.challenges", string.Join(", ", titles.ToArray())) + "\n" + texts.Text("chl.bonus", ScoreCalculator.BonusPercent(found));
        }

        private static string Join(string first, string second)
        {
            if (string.IsNullOrEmpty(second)) return first ?? string.Empty;
            if (string.IsNullOrEmpty(first)) return second;
            return first + "\n\n" + second;
        }

        private static void Append(StringBuilder builder, string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(line);
        }
    }
}
