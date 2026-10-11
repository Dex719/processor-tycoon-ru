using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Reputation;

namespace PTMod.Features.PressReviews
{
    /// <summary>What a reason of a reputation change written to <c>ModReputation.History</c> is about (FR-7: the reasons are turned into words).</summary>
    public enum ReasonKind
    {
        /// <summary>A reason the feature does not know: "other".</summary>
        Other = 0,

        /// <summary><c>press.review:&lt;cpuSaveId&gt;</c>: the review of a CPU.</summary>
        Review = 1,

        /// <summary><c>press.award:&lt;…&gt;</c>: the yearly awards.</summary>
        Award = 2,

        /// <summary><c>dfr.…</c>: <c>design-flaw-recalls</c> (a recall, a flaw made public).</summary>
        Recall = 3,

        /// <summary><c>rec.…</c>: <c>bankruptcy-recovery</c>.</summary>
        Recovery = 4,

        /// <summary><c>acq.…</c>: <c>rival-acquisitions</c>.</summary>
        Acquisition = 5,

        /// <summary><c>event:&lt;id&gt;</c>: an event of <c>event-system</c> or <c>historical-events</c>.</summary>
        Event = 6,
    }

    /// <summary>The levels of the reputation by the thresholds of the configuration (FR-7 item 1: cult from 80, respected 65–79, ordinary 36–64, dubious 21–35, toxic 20 and below).</summary>
    public static class ReputationLevels
    {
        /// <summary>The key of the level a reputation is in: the first level from the top whose minimum is not above the value (the lowest level for a lower one).</summary>
        public static string KeyFor(float reputation, IList<ReputationLevel> levels)
        {
            if (levels == null || levels.Count == 0) return string.Empty;
            if (float.IsNaN(reputation)) reputation = ReputationStore.Default;

            ReputationLevel lowest = null;
            ReputationLevel match = null;
            for (int i = 0; i < levels.Count; i++)
            {
                ReputationLevel level = levels[i];
                if (level == null) continue;
                if (lowest == null || level.Min < lowest.Min) lowest = level;
                if (reputation >= level.Min && (match == null || level.Min > match.Min)) match = level;
            }
            ReputationLevel chosen = match ?? lowest;
            return chosen == null ? string.Empty : chosen.Key;
        }

        /// <summary>The key of the level with the numbers of the configuration; the built-in thresholds when the configuration is null.</summary>
        public static string KeyFor(float reputation, ReviewConfig config)
        {
            return KeyFor(reputation, (config ?? ReviewConfig.CreateDefault()).ReputationLevels);
        }
    }

    /// <summary>
    /// Turns the reason of a change of the reputation into words (FR-7, design.md "Окно «Пресса»"). The reasons are the keys of the strings of the
    /// feature that wrote them: <c>press.review:&lt;id&gt;</c> becomes "review of K6-300 +1", <c>press.award:…</c> "awards of the year +4", <c>dfr.…</c>
    /// "recall of a CPU −8"; <c>event:&lt;id&gt;</c> is "event", anything else "other". Pure.
    /// </summary>
    public static class ReputationReasons
    {
        /// <summary>What a reason is about and the argument after its colon (the id of the CPU, of the event); empty without one.</summary>
        public static ReasonKind Classify(string reason, out string argument)
        {
            argument = string.Empty;
            if (string.IsNullOrEmpty(reason)) return ReasonKind.Other;

            string head = reason;
            int colon = reason.IndexOf(':');
            if (colon >= 0)
            {
                head = reason.Substring(0, colon);
                argument = reason.Substring(colon + 1);
            }

            if (string.Equals(head, "event", StringComparison.Ordinal)) return ReasonKind.Event;
            if (head.StartsWith("press.review", StringComparison.Ordinal)) return ReasonKind.Review;
            if (head.StartsWith("press.award", StringComparison.Ordinal)) return ReasonKind.Award;
            if (head.StartsWith("dfr.", StringComparison.Ordinal)) return ReasonKind.Recall;
            if (head.StartsWith("rec.", StringComparison.Ordinal)) return ReasonKind.Recovery;
            if (head.StartsWith("acq.", StringComparison.Ordinal)) return ReasonKind.Acquisition;
            return ReasonKind.Other;
        }

        /// <summary>The key of the words for a kind of reason; the text takes the change as <c>{0}</c> (and for a review the CPU as <c>{1}</c>, see <see cref="Describe"/>).</summary>
        public static string KeyOf(ReasonKind kind)
        {
            switch (kind)
            {
                case ReasonKind.Review: return "press.rep.reason.review";
                case ReasonKind.Award: return "press.rep.reason.award";
                case ReasonKind.Recall: return "press.rep.reason.recall";
                case ReasonKind.Recovery: return "press.rep.reason.recovery";
                case ReasonKind.Acquisition: return "press.rep.reason.acquisition";
                case ReasonKind.Event: return "press.rep.reason.event";
                default: return "press.rep.reason.misc";
            }
        }

        /// <summary>
        /// One entry of the history in words: "review of K6-300 +1". The change is the whole number nearest to the entry's delta; the CPU of a review
        /// comes from <paramref name="cpuName"/> (the archive), and a review of a CPU nobody remembers reads "review of a CPU".
        /// </summary>
        public static string Describe(ReputationChange change, Func<int, string> cpuName, PressText text)
        {
            if (text == null) throw new ArgumentNullException("text");

            string argument;
            ReasonKind kind = Classify(change.Reason, out argument);
            int delta = PressWords.Whole(change.Delta);

            if (kind == ReasonKind.Review)
            {
                int cpuSaveId;
                string name = null;
                if (int.TryParse(argument, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out cpuSaveId) && cpuName != null)
                    name = cpuName(cpuSaveId);
                if (string.IsNullOrEmpty(name)) return text("press.rep.reason.review.unknown", delta);
                return text(KeyOf(kind), PressWords.Plain(name), delta);
            }
            return text(KeyOf(kind), delta);
        }
    }

    /// <summary>A company in the table of the reputation: its key, its name and whether it is the player's.</summary>
    public sealed class ReputationCompany
    {
        public string Key = string.Empty;
        public string Name = string.Empty;
        public bool IsPlayer;
    }

    /// <summary>One row of the table of the reputation (FR-7 item 1).</summary>
    public sealed class ReputationRow
    {
        public string CompanyKey = string.Empty;
        public string CompanyName = string.Empty;
        public bool IsPlayer;

        /// <summary>The reputation, 0…100.</summary>
        public float Value;

        public string LevelKey = string.Empty;
        public string Level = string.Empty;

        /// <summary>The change over the last 12 months, in points.</summary>
        public float Change;

        /// <summary>The effect on popularity (<c>1.12</c> for 70).</summary>
        public float Effect;

        /// <summary>The line of the table: <c>68 Respected, +8 over the year, ×1.11</c>.</summary>
        public string Line = string.Empty;

        /// <summary>The latest reasons of change, the newest first, in words.</summary>
        public List<string> Reasons = new List<string>();
    }

    /// <summary>
    /// The table of the reputation of the companies (FR-7, AC-7.1): the value and the level, the change over the last 12 months, the effect on
    /// popularity and the latest reasons. The change and the reasons come from <c>ModReputation.History</c> only (<c>Add</c> and <c>Set</c>; the
    /// core's monthly pull towards 50 is not in it), the feature keeps no copy. Pure: the history, the values and the texts come in as arguments.
    /// </summary>
    public static class ReputationTable
    {
        /// <summary>How many reasons a row shows (FR-7: three).</summary>
        public const int ReasonsShown = 3;

        /// <summary>The months the change is counted over.</summary>
        public const int ChangeMonths = 12;

        /// <summary>
        /// The reputation now minus the reputation 12 months ago. The old value is the one the last change on or before that day left; when the
        /// history starts later it is the value before its first change, and a company without any change has not moved. Held to the scale 0…100.
        /// </summary>
        public static float ChangeOverYear(IReadOnlyList<ReputationChange> history, float current, DateTime today)
        {
            if (history == null || history.Count == 0) return 0f;

            DateTime cutoff = today.AddMonths(-ChangeMonths);
            bool found = false;
            float before = current;
            for (int i = 0; i < history.Count; i++)
            {
                ReputationChange entry = history[i];
                if (entry.Date <= cutoff)
                {
                    before = entry.Value;
                    found = true;
                }
            }

            if (!found)
            {
                ReputationChange first = history[0];
                before = Math.Max(ReputationStore.Min, Math.Min(ReputationStore.Max, first.Value - first.Delta));
            }
            return current - before;
        }

        /// <summary>
        /// The reputation before and after the change that was written with this reason (the newest such entry of the history); null when the history no
        /// longer has it (it keeps the latest changes only). A change that the scale 0…100 cut shows the requested size, not the effective one.
        /// </summary>
        public static ReputationStep StepOf(IReadOnlyList<ReputationChange> history, string reason)
        {
            if (history == null || string.IsNullOrEmpty(reason)) return null;

            for (int i = history.Count - 1; i >= 0; i--)
            {
                ReputationChange entry = history[i];
                if (!string.Equals(entry.Reason, reason, StringComparison.Ordinal)) continue;

                float before = Math.Max(ReputationStore.Min, Math.Min(ReputationStore.Max, entry.Value - entry.Delta));
                return new ReputationStep { Before = PressWords.Whole(before), After = PressWords.Whole(entry.Value) };
            }
            return null;
        }

        /// <summary>The latest reasons in words, the newest first.</summary>
        public static List<string> LastReasons(IReadOnlyList<ReputationChange> history, int count, Func<int, string> cpuName, PressText text)
        {
            var reasons = new List<string>();
            if (history == null) return reasons;

            for (int i = history.Count - 1; i >= 0 && reasons.Count < count; i--)
            {
                reasons.Add(ReputationReasons.Describe(history[i], cpuName, text));
            }
            return reasons;
        }

        /// <summary>
        /// The rows for the companies in the order given (the player first when the caller puts it first). <paramref name="effectOf"/> is the effect on
        /// popularity the feature really applies to the company (1 for an AI company when the parameter "reputation affects the AI" is off).
        /// </summary>
        public static List<ReputationRow> Build(IList<ReputationCompany> companies, Func<string, float> reputationOf,
            Func<string, IReadOnlyList<ReputationChange>> historyOf, Func<string, float> effectOf, DateTime today, ReviewConfig config,
            Func<int, string> cpuName, PressText text)
        {
            if (reputationOf == null) throw new ArgumentNullException("reputationOf");
            if (text == null) throw new ArgumentNullException("text");

            var rows = new List<ReputationRow>();
            if (companies == null) return rows;

            ReviewConfig cfg = config ?? ReviewConfig.CreateDefault();
            for (int i = 0; i < companies.Count; i++)
            {
                ReputationCompany company = companies[i];
                if (company == null || string.IsNullOrEmpty(company.Key)) continue;

                float value = reputationOf(company.Key);
                IReadOnlyList<ReputationChange> history = historyOf == null ? null : historyOf(company.Key);
                float effect = effectOf == null ? EffectMath.ReputationEffect(value, cfg.ReputationEffect) : effectOf(company.Key);

                var row = new ReputationRow
                {
                    CompanyKey = company.Key,
                    CompanyName = PressWords.Plain(string.IsNullOrEmpty(company.Name) ? company.Key : company.Name),
                    IsPlayer = company.IsPlayer,
                    Value = value,
                    LevelKey = ReputationLevels.KeyFor(value, cfg),
                    Change = ChangeOverYear(history, value, today),
                    Effect = effect,
                };
                row.Level = PressWords.TextOrEmpty(text, row.LevelKey);
                row.Reasons = LastReasons(history, ReasonsShown, cpuName, text);
                row.Line = Line(row, text);
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>The line of a row: value, level, change over the year and the effect (<c>68 Respected, +8 over the year, ×1.11</c>).</summary>
        public static string Line(ReputationRow row, PressText text)
        {
            if (row == null) throw new ArgumentNullException("row");
            if (text == null) throw new ArgumentNullException("text");

            int change = PressWords.Whole(row.Change);
            string changeText = change == 0 ? text("press.rep.change.none") : text("press.rep.change", change);
            return text("press.rep.row.line", PressWords.Whole(row.Value), row.Level, changeText, PressWords.Multiplier(row.Effect));
        }

        /// <summary>The line "Reputation: 58 — Ordinary" of the hub and of <c>PressReviewsApi.FormatReputation</c> (value and level).</summary>
        public static string Short(float reputation, ReviewConfig config, PressText text)
        {
            if (text == null) throw new ArgumentNullException("text");
            string level = PressWords.TextOrEmpty(text, ReputationLevels.KeyFor(reputation, config));
            return text("press.rep.short", PressWords.Whole(reputation), level);
        }
    }
}
