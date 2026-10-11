using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The strings the feature asks <c>ModL10n</c> for (TSK-029, NFR-5): what the self-check at the start looks for in the Russian table. Three sources: the keys written in the code of the
    /// windows, the models and the runtime (<see cref="Fixed"/>, kept in step with the sources by a test that scans them); the families the code builds from an id
    /// (<c>scn.medal.&lt;medal&gt;</c>, <c>scn.difficulty.&lt;level&gt;</c>, <c>ach.category.&lt;category&gt;</c>, the labels of the three parameters); and the texts of the records of the content
    /// (<see cref="Required"/>). A record of the player's own files (<c>IsCustom</c>) is left out: its texts are the player's business and the validator already warns about them.
    /// Pure data; no game types.
    /// </summary>
    public static class ScenarioTextKeys
    {
        /// <summary>Plural forms a key may be stored under (<c>core.mail.daysLeft.few</c>): a key counts as present when it or one of its forms is in the table.</summary>
        private static readonly string[] PluralSuffixes = { ".one", ".few", ".many", ".other" };

        /// <summary>
        /// The keys the code of the feature writes as a literal (windows, models, mails, the runtime). A new literal in the code must be added here: the test
        /// <c>SelfCheckTests.EveryKeyTheCodeWrites_IsInTheListOfTheSelfCheck</c> reads the sources and fails otherwise.
        /// </summary>
        public static readonly IReadOnlyList<string> Fixed = new[]
        {
            "ach.badge.assisted",
            "ach.button",
            "ach.category.count",
            "ach.hidden", "ach.hidden.desc",
            "ach.progress",
            "ach.records.attempts", "ach.records.best", "ach.records.challenge", "ach.records.challenge.none", "ach.records.challenges",
            "ach.records.none", "ach.records.scenarios", "ach.records.stats", "ach.records.years",
            "ach.reset", "ach.reset.confirm1", "ach.reset.confirm2",
            "ach.tab.achievements", "ach.tab.records",
            "ach.toast", "ach.toast.assisted", "ach.toast.more",
            "ach.unlocked",
            "ach.window.title",
            "chl.bonus",
            "chl.button",
            "chl.none",
            "chl.reason.company", "chl.reason.scenario",
            "chl.row",
            "chl.toast.failed", "chl.toast.passed",
            "chl.window.hint", "chl.window.title",
            "scn.badge.assisted", "scn.badge.assisted.cheats", "scn.badge.assisted.feature", "scn.badge.assisted.other", "scn.badge.custom",
            "scn.best.none", "scn.best.value",
            "scn.briefing.button", "scn.briefing.challenges", "scn.briefing.title",
            "scn.button.continue", "scn.button.menu",
            "scn.career.intro", "scn.career.noAchievements", "scn.career.noChallenges", "scn.career.scenario.lost", "scn.career.scenario.won",
            "scn.career.scenario.wonShort", "scn.career.span", "scn.career.spanLegacy", "scn.career.title", "scn.career.years",
            "scn.challenge.active", "scn.challenge.failed", "scn.challenge.line", "scn.challenge.passed",
            "scn.companyType.fabless", "scn.companyType.foundry", "scn.companyType.idm",
            "scn.deadline",
            "scn.difficulty.locked",
            "scn.fail.bankrupt", "scn.fail.deadline", "scn.fail.goal", "scn.fail.raceLost",
            "scn.feature.desc", "scn.feature.name",
            "scn.goal.optional", "scn.goal.points", "scn.goal.required",
            "scn.goals.title",
            "scn.hub.achievements", "scn.hub.career", "scn.hub.catalog", "scn.hub.noResults", "scn.hub.noScenario", "scn.hub.profile",
            "scn.hub.results", "scn.hub.title", "scn.hub.tracker",
            "scn.mail.briefing.body", "scn.mail.result.body",
            "scn.menu.confirm",
            "scn.mode.custom", "scn.mode.free", "scn.mode.label",
            "scn.preview.lines", "scn.preview.mismatch", "scn.preview.summary", "scn.preview.technology", "scn.preview.title",
            "scn.result.achievements", "scn.result.challenges", "scn.result.date", "scn.result.goal.done", "scn.result.goal.failedOn",
            "scn.result.goal.open", "scn.result.lost", "scn.result.mailSubject", "scn.result.medal", "scn.result.reason", "scn.result.score",
            "scn.result.table", "scn.result.won",
            "scn.score.challenges", "scn.score.goals", "scn.score.timeBonus", "scn.score.total",
            "scn.table.bankrupt", "scn.table.company", "scn.table.cpus", "scn.table.frequency", "scn.table.money", "scn.table.note",
            "scn.table.revenue", "scn.table.share", "scn.table.yes",
            "scn.toast.goalDone", "scn.toast.goalFailed", "scn.toast.startFailed",
            "scn.tracker.collapse", "scn.tracker.daysLeft", "scn.tracker.deadline", "scn.tracker.done", "scn.tracker.expand", "scn.tracker.failed",
            "scn.tracker.progress", "scn.tracker.race", "scn.tracker.score", "scn.tracker.title", "scn.tracker.unavailable",
        };

        /// <summary>The keys the code builds from an id: the medals, the company types, the difficulties, the technologies, the categories, the labels of the parameters.</summary>
        public static IEnumerable<string> Families()
        {
            foreach (string type in ContentVocabulary.CompanyTypes) yield return "scn.companyType." + type;
            for (int level = 0; level <= 5; level++) yield return "scn.difficulty." + level;
            for (int tech = 0; tech <= 4; tech++) yield return "scn.tech." + tech;
            foreach (string medal in ContentVocabulary.Medals) yield return "scn.medal." + medal;
            yield return "scn.medal.none";
            foreach (string category in ContentVocabulary.Categories) yield return "ach.category." + category;
            foreach (ParamSpec spec in ScenarioParamSpecs.All())
            {
                yield return spec.LabelKey;
                yield return spec.TooltipKey;
            }
        }

        /// <summary>The texts a scenario names: the title, the description, the briefing, the title of every goal and the reason of every rule of loss.</summary>
        public static void AddKeysOf(ScenarioDefinition scenario, ICollection<string> keys)
        {
            if (scenario == null || keys == null) return;
            Add(keys, scenario.TitleKey);
            Add(keys, scenario.DescKey);
            Add(keys, scenario.BriefingKey);
            if (scenario.Goals != null)
            {
                foreach (GoalSpec goal in scenario.Goals)
                {
                    if (goal != null) Add(keys, goal.TitleKey);
                }
            }
            AddFailKeys(scenario.Fail, keys);
        }

        /// <summary>The texts a challenge names: the title, the description, the reason of every rule of loss, and the reason it is unavailable when it needs the per-company credit.</summary>
        public static void AddKeysOf(ChallengeDefinition challenge, ICollection<string> keys)
        {
            if (challenge == null || keys == null) return;
            Add(keys, challenge.TitleKey);
            Add(keys, challenge.DescKey);
            AddFailKeys(challenge.Fail, keys);
            if (challenge.RequiresPerCompanyCredit && !string.IsNullOrEmpty(challenge.Id)) Add(keys, challenge.UnavailableKey);
        }

        /// <summary>The texts an achievement names: the title and the description.</summary>
        public static void AddKeysOf(AchievementDefinition achievement, ICollection<string> keys)
        {
            if (achievement == null || keys == null) return;
            Add(keys, achievement.TitleKey);
            Add(keys, achievement.DescKey);
        }

        /// <summary>
        /// Every key the self-check expects in the Russian table: <see cref="Fixed"/>, <see cref="Families"/> and the texts of the built-in records of the content, without repeats,
        /// in ordinal order. The records the player changed or added (<c>IsCustom</c>) are not asked for.
        /// </summary>
        public static List<string> Required(IEnumerable<ScenarioDefinition> scenarios, IEnumerable<ChallengeDefinition> challenges, IEnumerable<AchievementDefinition> achievements)
        {
            var keys = new HashSet<string>(Fixed, StringComparer.Ordinal);
            foreach (string key in Families()) keys.Add(key);

            if (scenarios != null)
            {
                foreach (ScenarioDefinition scenario in scenarios)
                {
                    if (scenario != null && !scenario.IsCustom) AddKeysOf(scenario, keys);
                }
            }
            if (challenges != null)
            {
                foreach (ChallengeDefinition challenge in challenges)
                {
                    if (challenge != null && !challenge.IsCustom) AddKeysOf(challenge, keys);
                }
            }
            if (achievements != null)
            {
                foreach (AchievementDefinition achievement in achievements)
                {
                    if (achievement != null && !achievement.IsCustom) AddKeysOf(achievement, keys);
                }
            }

            var list = new List<string>(keys);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>The key, or one of its plural forms, is in <paramref name="table"/> (the keys of a language).</summary>
        public static bool IsPresent(ISet<string> table, string key)
        {
            if (table == null || string.IsNullOrEmpty(key)) return false;
            if (table.Contains(key)) return true;
            foreach (string suffix in PluralSuffixes)
            {
                if (table.Contains(key + suffix)) return true;
            }
            return false;
        }

        /// <summary>The key belongs to the feature (it starts with one of the prefixes the core gave it).</summary>
        public static bool IsOwnKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            foreach (string prefix in ContentVocabulary.KeyPrefixes)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static void AddFailKeys(FailSpec[] fails, ICollection<string> keys)
        {
            if (fails == null) return;
            foreach (FailSpec fail in fails)
            {
                if (fail != null) Add(keys, fail.TextKey);
            }
        }

        private static void Add(ICollection<string> keys, string key)
        {
            if (!string.IsNullOrEmpty(key)) keys.Add(key);
        }
    }
}
