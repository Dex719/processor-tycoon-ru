using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// What the player is told by pop-ups after a pass of the engine (TSK-027; FR-5 item 2, FR-12 items 3 and 4, NFR-6): a line per goal that was done or failed, per challenge
    /// that failed or was passed, then the achievements - at most three by name and, when there are more, one line "and N more". The end of the scenario is not here: its
    /// window opens instead. The lines are ready text in the order to show them; <c>ModNotify</c> does the rest (it holds them back while the game loads, fast-forwards or runs
    /// a month's batch, and caps a feature per day).
    /// </summary>
    public static class ToastPlanner
    {
        /// <param name="pass">What the pass brought.</param>
        /// <param name="assisted">The run is assisted: the achievements are marked "with help".</param>
        /// <param name="findAchievement">The achievement by id; an id the catalog does not know is left out.</param>
        /// <param name="findChallenge">The challenge by id; one the catalog does not know is called by its id.</param>
        /// <param name="maxAchievements">The most achievements named in one pass (3).</param>
        public static List<string> Plan(RunPassResult pass, bool assisted, Func<string, AchievementDefinition> findAchievement,
                                        Func<string, ChallengeDefinition> findChallenge, IUiTexts texts, int maxAchievements = AchievementEvaluator.MaxToasts)
        {
            if (texts == null) throw new ArgumentNullException("texts");

            var lines = new List<string>();
            if (pass == null) return lines;

            foreach (ScenarioOutcomeEvent outcome in pass.ScenarioEvents)
            {
                if (outcome == null) continue;
                if (outcome.Kind == ScenarioOutcomeKind.GoalDone) lines.Add(texts.Text("scn.toast.goalDone", texts.Text(outcome.TextKey)));
                else if (outcome.Kind == ScenarioOutcomeKind.GoalFailed) lines.Add(texts.Text("scn.toast.goalFailed", texts.Text(outcome.TextKey)));
            }

            foreach (ChallengeEvent challenge in pass.ChallengeEvents)
            {
                if (challenge == null) continue;
                ChallengeDefinition definition = findChallenge == null ? null : findChallenge(challenge.ChallengeId);
                string title = definition == null ? challenge.ChallengeId : texts.Text(definition.TitleKey);
                lines.Add(texts.Text(challenge.Kind == ChallengeEventKind.Failed ? "chl.toast.failed" : "chl.toast.passed", title));
            }

            AddAchievements(lines, pass.Achievements, assisted, findAchievement, texts, maxAchievements);
            return lines;
        }

        private static void AddAchievements(List<string> lines, IList<string> ids, bool assisted, Func<string, AchievementDefinition> find, IUiTexts texts, int max)
        {
            if (ids == null || ids.Count == 0) return;

            // an id the catalog does not know (content that was removed) is neither named nor counted
            var known = new List<AchievementDefinition>();
            foreach (string id in ids)
            {
                AchievementDefinition definition = find == null ? null : find(id);
                if (definition != null) known.Add(definition);
            }
            if (known.Count == 0) return;

            var idsOfKnown = new List<string>();
            foreach (AchievementDefinition definition in known) idsOfKnown.Add(definition.Id);
            AchievementToasts plan = AchievementEvaluator.PlanToasts(idsOfKnown, max);

            string key = assisted ? "ach.toast.assisted" : "ach.toast";
            foreach (string id in plan.Shown)
            {
                foreach (AchievementDefinition definition in known)
                {
                    if (!string.Equals(definition.Id, id, StringComparison.Ordinal)) continue;
                    lines.Add(texts.Text(key, texts.Text(definition.TitleKey)));
                    break;
                }
            }
            if (plan.More > 0) lines.Add(texts.Text("ach.toast.more", plan.More));
        }
    }
}
