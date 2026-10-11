using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The milestones of the ecosystem (FR-15 item 3): the first time the share of a family in the sales passes 25 %, 50 % or 75 % (the thresholds of
    /// <c>ecosystem.milestones</c>) the player gets one letter about it. Pure: it decides which thresholds are new; the letters are sent by <c>ArchNotifier</c>
    /// and the thresholds already announced are kept in the save section (<c>milestones</c>), so that a milestone is announced once per game, also after a load.
    /// </summary>
    public static class EcosystemMilestones
    {
        /// <summary>The prefix of the tag of the letters of the feature (<c>arch:eco:&lt;familyId&gt;:&lt;threshold&gt;</c>); the prefix <c>arch</c> belongs to the feature (FR-25).</summary>
        public const string TagPrefix = "arch:eco:";

        private const float Tolerance = 1e-3f;   // a share of 0.25 reaches the 25 % threshold whatever the rounding of the division

        /// <summary>The tag of the letter of a milestone, <c>arch:eco:x86:50</c>.</summary>
        public static string Tag(string familyId, int threshold)
        {
            return TagPrefix + familyId + ":" + threshold.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The share has reached the threshold (a percent).</summary>
        public static bool Reaches(float share, int threshold)
        {
            return !float.IsNaN(share) && share * 100f + Tolerance >= threshold;
        }

        /// <summary>
        /// The thresholds crossed this month: reached now, not reached the month before, and not announced yet, in ascending order. A negative
        /// <paramref name="previousShare"/> means "not known" - nothing is crossed then (see <see cref="Passed"/>).
        /// </summary>
        public static List<int> Crossed(float previousShare, float currentShare, IReadOnlyList<int> thresholds, Func<int, bool> announced)
        {
            var crossed = new List<int>();
            if (thresholds == null || previousShare < 0f || float.IsNaN(previousShare)) return crossed;

            for (int i = 0; i < thresholds.Count; i++)
            {
                int threshold = thresholds[i];
                if (Reaches(previousShare, threshold) || !Reaches(currentShare, threshold)) continue;
                if (announced != null && announced(threshold)) continue;
                crossed.Add(threshold);
            }
            crossed.Sort();
            return crossed;
        }

        /// <summary>
        /// The thresholds the share stands above and that are not announced: what is recorded without a letter when the share is seen for the first time (a loaded
        /// game, the feature switched on in the middle of a game) so that a long-past milestone does not turn into a letter now.
        /// </summary>
        public static List<int> Passed(float currentShare, IReadOnlyList<int> thresholds, Func<int, bool> announced)
        {
            var passed = new List<int>();
            if (thresholds == null) return passed;

            for (int i = 0; i < thresholds.Count; i++)
            {
                int threshold = thresholds[i];
                if (!Reaches(currentShare, threshold)) continue;
                if (announced != null && announced(threshold)) continue;
                passed.Add(threshold);
            }
            passed.Sort();
            return passed;
        }

        /// <summary>
        /// The families with the largest shares, largest first (a tie goes to the id), as <c>(id, whole percent)</c>; at most <paramref name="count"/>, only those
        /// that have at least one percent. For the line "market now" of the letter.
        /// </summary>
        public static List<KeyValuePair<string, int>> Top(IReadOnlyDictionary<string, float> shares, int count)
        {
            var top = new List<KeyValuePair<string, int>>();
            if (shares == null || count <= 0) return top;

            var all = new List<KeyValuePair<string, float>>();
            foreach (KeyValuePair<string, float> pair in shares)
            {
                if (pair.Key != null && !float.IsNaN(pair.Value) && pair.Value > 0f) all.Add(pair);
            }
            all.Sort((a, b) =>
            {
                int byShare = b.Value.CompareTo(a.Value);
                return byShare != 0 ? byShare : string.CompareOrdinal(a.Key, b.Key);
            });

            foreach (KeyValuePair<string, float> pair in all)
            {
                if (top.Count >= count) break;
                int percent = (int)Math.Round(pair.Value * 100.0, MidpointRounding.AwayFromZero);
                if (percent < 1) continue;
                top.Add(new KeyValuePair<string, int>(pair.Key, percent));
            }
            return top;
        }
    }
}
