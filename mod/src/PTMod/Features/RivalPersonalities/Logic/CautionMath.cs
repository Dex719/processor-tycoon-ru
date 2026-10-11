using System;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The numbers of the caution of an AI company after a public design flaw (design.md, formula 16; FR-17): the factor of the acceptable failure
    /// rate and the day the caution ends. Pure; <see cref="CautionBook"/> keeps the entries, <c>CautionPatch</c> multiplies the result of the game.
    /// </summary>
    public static class CautionMath
    {
        /// <summary>The severity levels of the flaw by number, as <c>design-flaw-recalls</c> numbers them (<c>FlawSeverity</c>): Minor 0, Major 1, Critical 2.</summary>
        public const int Minor = 0;

        public const int Major = 1;
        public const int Critical = 2;

        /// <summary>The factor when no caution is in force.</summary>
        public const float Neutral = 1f;

        /// <summary>
        /// The factor of the acceptable failure rate: <paramref name="cautionFactor"/> (<c>flawCaution.factor</c>, 0.85) while the caution is in force,
        /// 1 otherwise. A factor that is not a positive finite number counts as 1 (the config keeps it in 0.1..1; this is the last guard), and a
        /// factor above 1 is held to 1: the caution never makes the AI bolder.
        /// </summary>
        public static float Factor(bool active, float cautionFactor)
        {
            if (!active) return Neutral;
            if (float.IsNaN(cautionFactor) || float.IsInfinity(cautionFactor) || cautionFactor <= 0f) return Neutral;
            return cautionFactor > Neutral ? Neutral : cautionFactor;
        }

        /// <summary>
        /// The day the caution ends: <paramref name="start"/> plus <paramref name="months"/> calendar months (<c>flawCaution.months</c>, 24). The
        /// caution is in force while <c>today &lt; until</c>. A negative number of months counts as 0; a date past the end of the calendar is the last day.
        /// </summary>
        public static DateTime UntilDate(DateTime start, int months)
        {
            int count = months < 0 ? 0 : months;
            try
            {
                return start.Date.AddMonths(count);
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTime.MaxValue.Date;
            }
        }

        /// <summary>The number of a severity name (<c>Minor</c>, <c>Major</c>, <c>Critical</c>, exactly as the config spells it); -1 for anything else.</summary>
        public static int SeverityLevel(string name)
        {
            if (string.Equals(name, "Minor", StringComparison.Ordinal)) return Minor;
            if (string.Equals(name, "Major", StringComparison.Ordinal)) return Major;
            if (string.Equals(name, "Critical", StringComparison.Ordinal)) return Critical;
            return -1;
        }

        /// <summary>
        /// A flaw of this severity starts a caution: at least <c>flawCaution.minSeverity</c> (<c>Major</c> by default, so <c>Major</c> and <c>Critical</c>).
        /// A severity number outside 0..2 does not; a minimum that is not a severity name counts as <c>Major</c>.
        /// </summary>
        public static bool IsSevereEnough(int severity, string minSeverity)
        {
            if (severity < Minor || severity > Critical) return false;
            int minimum = SeverityLevel(minSeverity);
            if (minimum < 0) minimum = Major;
            return severity >= minimum;
        }

        /// <summary>
        /// The caution counts for the company now (FR-17 items 3 and 4): <c>design-flaw-recalls</c> is on, the company is not bankrupt or dormant, and
        /// the new game is not fast-forwarding. Then the factor is applied; otherwise the game's value stays.
        /// </summary>
        public static bool Applies(bool flawFeatureActive, bool bankrupt, bool dormant, bool fastForwarding)
        {
            return flawFeatureActive && !bankrupt && !dormant && !fastForwarding;
        }
    }
}
