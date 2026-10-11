using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>One achievement in the window: earned or not, how it reads, and how far the player is from it.</summary>
    public sealed class AchievementRow
    {
        public string Id;

        /// <summary>The title; "???" for a hidden achievement that is not earned.</summary>
        public string Title;

        /// <summary>The description; for a hidden achievement that is not earned, only the line that says it is hidden.</summary>
        public string Description;

        public bool Unlocked;

        public bool Hidden;

        /// <summary>It was earned in a run that was assisted ("with help").</summary>
        public bool Assisted;

        /// <summary>"Earned 12.03.2026" (the real date it was earned); null when it is not earned.</summary>
        public string UnlockedText;

        /// <summary>"7 / 12" for an achievement that counts towards a number and is not earned yet; null for the rest and outside a game.</summary>
        public string Progress;
    }

    /// <summary>The achievements of one category (a tab of the window): its name with how many are earned, and the rows.</summary>
    public sealed class AchievementCategory
    {
        public string Id;

        /// <summary>"Career: 3 / 5".</summary>
        public string Title;

        public int Unlocked;

        public int Total;

        public readonly List<AchievementRow> Rows = new List<AchievementRow>();
    }

    /// <summary>What the page "Achievements" of the window shows (TSK-026, FR-14 item 1).</summary>
    public sealed class AchievementsView
    {
        public int Unlocked;

        public int Total;

        /// <summary>"Achievements: 7 / 37" - the title of the window.</summary>
        public string Title = string.Empty;

        public readonly List<AchievementCategory> Categories = new List<AchievementCategory>();
    }

    /// <summary>The record of one scenario in the page "Records".</summary>
    public sealed class RecordRow
    {
        public string ScenarioId;

        public string Title;

        /// <summary>"Best: 600 - Silver (01.01.2026)", or "Best result: none yet".</summary>
        public string Best;

        public bool HasBest;

        /// <summary>"Attempts: 3, wins: 1".</summary>
        public string Attempts;
    }

    /// <summary>What the page "Records" of the window shows (FR-14 item 2): a record per scenario, the challenges that were passed, the counters of the profile.</summary>
    public sealed class RecordsView
    {
        public readonly List<RecordRow> Scenarios = new List<RecordRow>();

        public readonly List<UiLine> Challenges = new List<UiLine>();

        /// <summary>"Runs started: 4, scenarios won: 1, lost: 2".</summary>
        public string Stats = string.Empty;

        /// <summary>The profile has no result and no passed challenge yet.</summary>
        public bool IsEmpty;
    }

    /// <summary>
    /// The models of the window of the achievements and records (TSK-026; FR-14, FR-12 item 6): the catalog by category with what the profile has earned, "???" for a hidden
    /// achievement that is not earned, the mark "with help", the progress towards a counted one while a game goes; and the records of the scenarios and the challenges.
    /// Nothing is changed; the profile is read through <see cref="ProfileView"/>.
    /// </summary>
    public static class AchievementsViewBuilder
    {
        /// <param name="catalog">The achievements of the catalog.</param>
        /// <param name="profile">What the profile holds.</param>
        /// <param name="metrics">The metrics of the run in play; null outside a game (no progress is shown then).</param>
        /// <param name="events">The counters of events of the run in play; null outside a game.</param>
        public static AchievementsView Build(IEnumerable<AchievementDefinition> catalog, ProfileView profile, MetricsSnapshot metrics, EventBook events, IUiTexts texts)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if (texts == null) throw new ArgumentNullException("texts");

            var view = new AchievementsView();
            var byCategory = new Dictionary<string, AchievementCategory>(StringComparer.Ordinal);
            var order = new List<string>(ContentVocabulary.Categories);

            if (catalog != null)
            {
                foreach (AchievementDefinition definition in catalog)
                {
                    if (definition == null) continue;

                    string categoryId = string.IsNullOrEmpty(definition.Category) ? "meta" : definition.Category;
                    AchievementCategory category;
                    if (!byCategory.TryGetValue(categoryId, out category))
                    {
                        category = new AchievementCategory { Id = categoryId };
                        byCategory[categoryId] = category;
                        if (!order.Contains(categoryId)) order.Add(categoryId);
                    }

                    AchievementRow row = Row(definition, profile, metrics, events, texts);
                    category.Rows.Add(row);
                    category.Total++;
                    view.Total++;
                    if (row.Unlocked)
                    {
                        category.Unlocked++;
                        view.Unlocked++;
                    }
                }
            }

            foreach (string id in order)
            {
                AchievementCategory category;
                if (!byCategory.TryGetValue(id, out category)) continue;
                category.Title = texts.Text("ach.category.count", texts.Text("ach.category." + id), category.Unlocked, category.Total);
                view.Categories.Add(category);
            }

            view.Title = texts.Text("ach.window.title", view.Unlocked, view.Total);
            return view;
        }

        /// <summary>The page "Records": the standard scenarios in the order of the catalog, then the challenges (a scenario that was changed by the player makes no record and is not listed).</summary>
        public static RecordsView BuildRecords(IEnumerable<ScenarioDefinition> scenarios, IEnumerable<ChallengeDefinition> challenges, ProfileView profile, IUiTexts texts)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if (texts == null) throw new ArgumentNullException("texts");

            var view = new RecordsView();
            bool any = false;

            if (scenarios != null)
            {
                foreach (ScenarioDefinition scenario in scenarios)
                {
                    if (scenario == null || scenario.IsCustom) continue;

                    ProfileScenarioRecord record = profile.ScenarioRecordOf(scenario.Id);
                    int attempts = record == null ? 0 : record.Attempts;
                    int wins = record == null ? 0 : record.Wins;
                    ProfileBest best = record == null ? null : record.Best;
                    if (best != null || attempts > 0) any = true;

                    view.Scenarios.Add(new RecordRow
                    {
                        ScenarioId = scenario.Id,
                        Title = texts.Text(scenario.TitleKey),
                        HasBest = best != null,
                        Best = best == null
                            ? texts.Text("scn.best.none")
                            : texts.Text("ach.records.best", best.Score, texts.Text("scn.medal." + (string.IsNullOrEmpty(best.Medal) ? ScoreCalculator.MedalNone : best.Medal)), texts.Date(best.FinishedOn)),
                        Attempts = texts.Text("ach.records.attempts", attempts, wins),
                    });
                }
            }

            if (challenges != null)
            {
                foreach (ChallengeDefinition challenge in challenges)
                {
                    if (challenge == null) continue;

                    string title = texts.Text(challenge.TitleKey);
                    ProfileChallengeRecord record = profile.ChallengeRecordOf(challenge.Id);
                    if (record == null || record.Passed <= 0)
                    {
                        view.Challenges.Add(new UiLine(texts.Text("ach.records.challenge.none", title), UiTone.Muted));
                        continue;
                    }

                    any = true;
                    view.Challenges.Add(new UiLine(texts.Text("ach.records.challenge", title, record.Passed, texts.Plural("ach.records.years", record.BestYears, record.BestYears)), UiTone.Good));
                }
            }

            ProfileStats stats = profile.Stats;
            if (stats != null && (stats.RunsStarted > 0 || stats.ScenariosWon > 0 || stats.ScenariosLost > 0)) any = true;
            view.Stats = stats == null ? string.Empty : texts.Text("ach.records.stats", stats.RunsStarted, stats.ScenariosWon, stats.ScenariosLost);
            view.IsEmpty = !any;
            return view;
        }

        private static AchievementRow Row(AchievementDefinition definition, ProfileView profile, MetricsSnapshot metrics, EventBook events, IUiTexts texts)
        {
            bool unlocked = profile.IsUnlocked(definition.Id);
            AchievementEntry entry = unlocked ? profile.EntryOf(definition.Id) : null;
            bool hiddenNow = definition.Hidden && !unlocked;

            var row = new AchievementRow
            {
                Id = definition.Id,
                Hidden = definition.Hidden,
                Unlocked = unlocked,
                Title = hiddenNow ? texts.Text("ach.hidden") : texts.Text(definition.TitleKey),
                Description = hiddenNow ? texts.Text("ach.hidden.desc") : texts.Text(definition.DescKey),
                Assisted = entry != null && entry.Assisted,
                UnlockedText = unlocked ? texts.Text("ach.unlocked", entry == null ? string.Empty : texts.Date(entry.UnlockedAt)) : null,
            };
            if (!unlocked && !hiddenNow) row.Progress = ProgressText(definition.Check, metrics, events, texts);
            return row;
        }

        // "7 / 12" for a check that counts towards a number: a metric that has to reach a value above one (or a share, which is a fraction of one but is reached little by little),
        // or an event that has to happen several times. The check that is a plain yes or no ("the first CPU", "a profit", "one OEM contract") has no progress, and neither has a
        // combined one.
        private static string ProgressText(CheckSpec check, MetricsSnapshot metrics, EventBook events, IUiTexts texts)
        {
            if (check == null) return null;

            if (check.Kind == CheckSpec.KindMetric && metrics != null && (check.Op == ">=" || check.Op == ">") && IsGradual(check))
            {
                double value;
                if (!metrics.TryGet(check.Metric, out value) || double.IsNaN(value)) return null;
                string format = MetricFormat.Of(check.Metric);
                return texts.Text("ach.progress", MetricFormat.Format(format, value, texts), MetricFormat.Format(format, check.Value, texts));
            }

            if (check.Kind == CheckSpec.KindEvent && events != null && check.Count > 1)
            {
                return texts.Text("ach.progress", texts.Number(events.Count(check.Event)), texts.Number(check.Count));
            }
            return null;
        }

        private static bool IsGradual(CheckSpec check)
        {
            if (check.Value <= 0) return false;
            return check.Value > 1 || string.Equals(MetricFormat.Of(check.Metric), MetricFormat.Percent, StringComparison.Ordinal);
        }
    }
}
