using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>The pop-ups for the achievements of one pass: the first few by name and one line "and N more" for the rest (FR-12 item 4).</summary>
    public sealed class AchievementToasts
    {
        /// <summary>The ids that get a pop-up each, in the order they were earned.</summary>
        public string[] Shown = new string[0];

        /// <summary>How many more were earned and are announced by one line (0 - no such line).</summary>
        public int More;
    }

    /// <summary>
    /// Finds the achievements that have just come true (design.md, "AchievementEvaluator"; FR-12): it looks only at the achievements the profile
    /// does not hold yet, takes the check of each with its cadence (<c>daily</c>, <c>monthly</c>, <c>event</c>) and answers with the ids that are
    /// satisfied now. It changes nothing: the caller puts the ids into the profile (<see cref="ProfileModel.Unlock"/>), notes them in the run and
    /// shows the pop-ups (<see cref="PlanToasts"/>). A pass is cheap - the earned ones are skipped before any check.
    /// <para>
    /// The kinds of check are those of <see cref="CheckEvaluator"/> (<c>event</c>, <c>metric</c>, <c>cpuReleased</c>, <c>condition</c>, <c>all</c>);
    /// the evaluator adds the two that need the run and the profile: <c>require</c> (difficulty, bankruptcy, company type, challenges) and
    /// <c>profile</c> (a medal in every standard scenario). The achievements of the setting <c>countsOnlyNewGames</c> do not count in a legacy
    /// run, and in a strict assisted run (<see cref="RunInfo.StrictAssisted"/>) nothing is earned at all. Metrics that are not in the snapshot
    /// keep an achievement closed.
    /// </para>
    /// </summary>
    public sealed class AchievementEvaluator
    {
        /// <summary>The most pop-ups for the achievements of one pass.</summary>
        public const int MaxToasts = 3;

        private const string EventCpuReleasedPlayer = "cpu.released.player";

        private readonly List<AchievementDefinition> _catalog = new List<AchievementDefinition>();
        private readonly List<string> _standardScenarioIds = new List<string>();

        /// <param name="catalog">The achievements to watch (the null ones and the ones without a check are skipped).</param>
        /// <param name="standardScenarioIds">The ids of the standard scenarios, for the rule <c>allScenarioMedals</c> (none: that rule is never true).</param>
        public AchievementEvaluator(IEnumerable<AchievementDefinition> catalog, IEnumerable<string> standardScenarioIds = null)
        {
            if (catalog == null) throw new ArgumentNullException("catalog");
            foreach (AchievementDefinition definition in catalog)
            {
                if (definition != null && definition.Check != null && !string.IsNullOrEmpty(definition.Id)) _catalog.Add(definition);
            }
            if (standardScenarioIds != null)
            {
                foreach (string id in standardScenarioIds)
                {
                    if (!string.IsNullOrEmpty(id) && !_standardScenarioIds.Contains(id)) _standardScenarioIds.Add(id);
                }
            }
        }

        /// <summary>The achievements the evaluator watches, in the order of the catalog.</summary>
        public IList<AchievementDefinition> Catalog
        {
            get { return _catalog.AsReadOnly(); }
        }

        /// <summary>
        /// The pass of a cadence: <c>daily</c> after the metrics of the day, <c>monthly</c> after the metrics of the month; <c>event</c> sweeps
        /// every achievement that waits for an event (for a start or a load, when events may have been missed). Returns the ids that are
        /// satisfied now and not yet in the profile, once each. Nothing is returned for a null profile or run (nothing can be judged).
        /// </summary>
        public IList<string> Evaluate(string cadence, MetricsSnapshot m, EventBook events, ProfileView profile, RunInfo run,
                                      IConditionAdapter conditions)
        {
            return Pass(def => string.Equals(def.Check.Cadence ?? CheckSpec.CadenceDaily, cadence, StringComparison.Ordinal),
                m, events, profile, run, conditions);
        }

        /// <summary>
        /// The pass of an event (the caller recorded it in the <paramref name="events"/> first): only the achievements that wait for events and
        /// can be changed by this one are looked at (<c>event</c> of that name, <c>cpuReleased</c> for a CPU of the player, <c>profile</c> for
        /// the end of a scenario, and a group that holds any of these; a check on a metric or a condition may change with any event).
        /// </summary>
        public IList<string> OnEvent(string eventId, EventBook events, MetricsSnapshot m, ProfileView profile, RunInfo run,
                                     IConditionAdapter conditions = null)
        {
            if (string.IsNullOrEmpty(eventId)) return new List<string>();
            return Pass(def => string.Equals(def.Check.Cadence, CheckSpec.CadenceEvent, StringComparison.Ordinal) && MayChangeOn(def.Check, eventId),
                m, events, profile, run, conditions);
        }

        /// <summary>
        /// The pop-ups for the ids of a pass: up to <paramref name="max"/> (3) by name and, when there are more, one count for the rest ("and N more").
        /// </summary>
        public static AchievementToasts PlanToasts(IList<string> ids, int max = MaxToasts)
        {
            var plan = new AchievementToasts();
            if (ids == null || ids.Count == 0) return plan;

            int shown = Math.Min(ids.Count, Math.Max(0, max));
            plan.Shown = new string[shown];
            for (int i = 0; i < shown; i++) plan.Shown[i] = ids[i];
            plan.More = ids.Count - shown;
            return plan;
        }

        // ---- internals ----

        private IList<string> Pass(Func<AchievementDefinition, bool> due, MetricsSnapshot m, EventBook events, ProfileView profile,
                                   RunInfo run, IConditionAdapter conditions)
        {
            var earned = new List<string>();
            if (profile == null || run == null) return earned;
            if (!AssistedRegistry.CanUnlockAchievements(run.Assisted, run.StrictAssisted)) return earned;

            CheckContext ctx = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AchievementDefinition def in _catalog)
            {
                if (profile.IsUnlocked(def.Id) || seen.Contains(def.Id)) continue;
                if (def.CountsOnlyNewGames && run.IsLegacy) continue;
                if (!due(def)) continue;

                if (ctx == null) ctx = MakeContext(m, events, profile, run, conditions);
                if (CheckEvaluator.Evaluate(def.Check, ctx))
                {
                    seen.Add(def.Id);
                    earned.Add(def.Id);
                }
            }
            return earned;
        }

        private CheckContext MakeContext(MetricsSnapshot m, EventBook events, ProfileView profile, RunInfo run, IConditionAdapter conditions)
        {
            return new CheckContext
            {
                Metrics = m,
                Today = run.GameDate,
                Conditions = conditions ?? NullConditionAdapter.Instance,
                Events = events,
                Require = spec => RequireHolds(spec, run, events),
                Profile = spec => ProfileRuleHolds(spec, profile),
            };
        }

        // The preconditions of a check (design.md, "require"). What cannot be verified is not true.
        private static bool RequireHolds(RequireSpec spec, RunInfo run, EventBook events)
        {
            if (spec.DifficultyLevel >= 0 && Math.Min(run.DifficultyAtStart, run.DifficultyLevel) < spec.DifficultyLevel) return false;
            if (spec.NotBankrupt && (events == null || events.Count("player.bankrupt") > 0)) return false;
            if (!string.IsNullOrEmpty(spec.CompanyType) && !string.Equals(run.CompanyType, spec.CompanyType, StringComparison.Ordinal)) return false;
            if (spec.MinChallenges > 0 && run.ChallengeCount < spec.MinChallenges) return false;
            return true;
        }

        private bool ProfileRuleHolds(ProfileSpec spec, ProfileView profile)
        {
            if (spec == null || profile == null) return false;
            switch (spec.Rule)
            {
                case ProfileSpec.RuleAllScenarioMedals:
                    return profile.HasMedalInAll(_standardScenarioIds, spec.Medal);
                default:
                    return false;
            }
        }

        // Can this event change the answer of the check? Kinds that read counters or journals say yes for their own events only;
        // a metric or a condition has no event of its own, so any event may be the moment it comes true.
        private static bool MayChangeOn(CheckSpec check, string eventId)
        {
            switch (check.Kind)
            {
                case CheckSpec.KindEvent:
                    return string.Equals(check.Event, eventId, StringComparison.Ordinal);

                case CheckSpec.KindCpuReleased:
                    return string.Equals(eventId, EventCpuReleasedPlayer, StringComparison.Ordinal);

                case CheckSpec.KindProfile:
                    return eventId == "scenario.won" || eventId == "scenario.gold" || eventId == "scenario.lost";

                case CheckSpec.KindAll:
                    if (check.All == null) return false;
                    foreach (CheckSpec child in check.All)
                    {
                        if (child != null && MayChangeOn(child, eventId)) return true;
                    }
                    return false;

                default:
                    return true;
            }
        }
    }
}
