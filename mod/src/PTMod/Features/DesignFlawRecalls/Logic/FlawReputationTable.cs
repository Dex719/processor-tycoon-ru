using System;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The L10n keys of the reasons a change of the reputation is written with (<c>ModReputation.Add(company, delta, reasonKey)</c>, design.md Component 10, D14).
    /// The id of the record is not part of the reason: it goes to the log.
    /// </summary>
    public static class FlawReputationKeys
    {
        /// <summary>The flaw became public.</summary>
        public const string Public = "dfr.rep.public";

        public const string Recall = "dfr.rep.recall";
        public const string Stepping = "dfr.rep.stepping";
        public const string Patch = "dfr.rep.patch";
        public const string Ignore = "dfr.rep.ignore";

        /// <summary>The monthly drain of a publicly ignored flaw.</summary>
        public const string Drain = "dfr.rep.drain";

        /// <summary>A cover-up leaked.</summary>
        public const string Leak = "dfr.rep.leak";
    }

    /// <summary>
    /// The table of the reputation (FR-10, design.md formula 7): what each event costs or earns the brand, in points, by severity. Pure: every number
    /// comes from <see cref="FlawBalance.Reputation"/>. The deltas are whole points (the content holds integers); a cover-up multiplies the sum of the
    /// base and the reaction and is rounded half away from zero.
    /// <list type="bullet">
    /// <item>The flaw became public: <c>publicBase</c> (-2 / -5 / -10).</item>
    /// <item>Recall <c>+1 / +2 / +4</c>; revision <c>0 / -1 / -3</c> when the flaw is public and <c>0 / 0 / 0</c> when it is quiet; patch <c>-1 / -2 / -4</c>;
    /// ignore (of a public flaw) <c>-3 / -8 / -15</c>.</item>
    /// <item>A cover-up that leaked: <c>(publicBase + reaction) * coverUpMultiplier</c> (AC-7.2: <c>(-5 + 0) * 2 = -10</c>).</item>
    /// </list>
    /// </summary>
    public static class FlawReputationTable
    {
        /// <summary>The flaw became public (<c>dfr.rep.public</c>).</summary>
        public static int PublicBase(FlawBalance b, FlawSeverity severity)
        {
            return At(b == null ? null : b.Reputation.PublicBase, severity);
        }

        /// <summary>The recall (<c>dfr.rep.recall</c>).</summary>
        public static int Recall(FlawBalance b, FlawSeverity severity)
        {
            return At(b == null ? null : b.Reputation.Recall, severity);
        }

        /// <summary>The revision (<c>dfr.rep.stepping</c>): <paramref name="quiet"/> - nobody outside the company knows about the flaw.</summary>
        public static int Stepping(FlawBalance b, FlawSeverity severity, bool quiet)
        {
            if (b == null) return 0;
            return At(quiet ? b.Reputation.SteppingQuiet : b.Reputation.SteppingPublic, severity);
        }

        /// <summary>The patch (<c>dfr.rep.patch</c>).</summary>
        public static int Patch(FlawBalance b, FlawSeverity severity)
        {
            return At(b == null ? null : b.Reputation.Patch, severity);
        }

        /// <summary>Ignoring a public flaw (<c>dfr.rep.ignore</c>).</summary>
        public static int IgnorePublic(FlawBalance b, FlawSeverity severity)
        {
            return At(b == null ? null : b.Reputation.IgnorePublic, severity);
        }

        /// <summary>
        /// The delta of a reaction by its kind: <see cref="FlawResponse.Recall"/>, <see cref="FlawResponse.Stepping"/>, <see cref="FlawResponse.Patch"/>,
        /// <see cref="FlawResponse.Ignore"/>; 0 for <see cref="FlawResponse.None"/>. <paramref name="quiet"/> matters for the revision only (an ignore
        /// of a quiet flaw has no immediate consequence, so the caller does not ask for it).
        /// </summary>
        public static int Reaction(FlawBalance b, FlawSeverity severity, FlawResponse response, bool quiet)
        {
            switch (response)
            {
                case FlawResponse.Recall: return Recall(b, severity);
                case FlawResponse.Stepping: return Stepping(b, severity, quiet);
                case FlawResponse.Patch: return Patch(b, severity);
                case FlawResponse.Ignore: return IgnorePublic(b, severity);
                default: return 0;
            }
        }

        /// <summary>The key of the reason a reaction is written with (<c>dfr.rep.recall</c> and so on); <c>null</c> for <see cref="FlawResponse.None"/>.</summary>
        public static string ReasonKey(FlawResponse response)
        {
            switch (response)
            {
                case FlawResponse.Recall: return FlawReputationKeys.Recall;
                case FlawResponse.Stepping: return FlawReputationKeys.Stepping;
                case FlawResponse.Patch: return FlawReputationKeys.Patch;
                case FlawResponse.Ignore: return FlawReputationKeys.Ignore;
                default: return null;
            }
        }

        /// <summary>The change of the reputation for one month of the drain of a publicly ignored flaw (a negative number; the content says -1).</summary>
        public static int Drain(FlawBalance b)
        {
            return b == null ? 0 : b.Responses.Ignore.DrainPerMonth;
        }

        /// <summary>
        /// A cover-up that leaked: <c>(base + reaction) * coverUpMultiplier</c> rounded half away from zero (FR-10 item 2). The multiplier of a content
        /// that is not usable (NaN, below 1) counts as 1: a cover-up is never milder than an open flaw.
        /// </summary>
        public static int CoverUp(FlawBalance b, int baseDelta, int reactionDelta)
        {
            double multiplier = b == null ? 1.0 : FlawMath.Exact(b.Reputation.CoverUpMultiplier);
            if (!FlawMath.IsFinite(multiplier) || multiplier < 1.0) multiplier = 1.0;

            double sum = (baseDelta + reactionDelta) * multiplier;
            return (int)Math.Round(sum, MidpointRounding.AwayFromZero);
        }

        private static int At(int[] values, FlawSeverity severity)
        {
            return FlawMath.At(values, FlawMath.SeverityIndex(severity), 0);
        }
    }
}
