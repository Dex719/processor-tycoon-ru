using System;
using System.Diagnostics;
using PTMod.Core;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// The measurements of the debug build (NFR-1, TSK-019; the parameter <c>debug.verbose</c>, which does not exist in a release build): the daily work of the
    /// feature (the queue and the refresh of the hype) is timed, and once a month, on the first, the log gets the average time per game day, its worst day and
    /// whether it is within the 0.2 ms of the requirement; once a year, on 1 January, it gets the spread of the metascores of the year that has just ended - the
    /// median, the share at 85 or above and the share below 40 - with a note when they are outside the targets of the calibration (so the balance of 1980-2000
    /// can be judged from the log, scenario 12 of the manual check). With the parameter off every call is one read of a constant (a release build: nothing).
    /// The clock and the arithmetic are <see cref="TimingBook"/> and <see cref="ScoreDistribution"/> (pure, with the tests).
    /// </summary>
    internal static class PressTimings
    {
        private const string Feature = PressReviewsFeature.FeatureId;

        private static readonly TimingBook Book = new TimingBook();
        private static Func<PressState> stateOf;

        /// <summary>Tells where the state of the feature is (the yearly summary reads the archive).</summary>
        internal static void Track(Func<PressState> state)
        {
            stateOf = state;
        }

        /// <summary>The start of a measured stretch of the daily work: a timestamp, or 0 when the measuring is off. Pass it to <see cref="End"/>.</summary>
        internal static long Begin()
        {
            return PressReviewsFeature.DebugVerbose ? Stopwatch.GetTimestamp() : 0L;
        }

        /// <summary>The end of a stretch started with <see cref="Begin"/>; adds its time to the game day it happened on.</summary>
        internal static void End(long started)
        {
            if (started == 0L) return;

            double ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            DateTime today = ModTicks.Today;
            if (today != default(DateTime)) Book.Add(today, ms);
        }

        /// <summary><c>TickPhase.MonthPassed</c>: the line about the days of the month that has just ended.</summary>
        internal static void OnMonth()
        {
            if (!PressReviewsFeature.DebugVerbose) return;

            DateTime today = ModTicks.Today;
            TimingSummary summary = Book.Take();
            if (today == default(DateTime) || summary.Days == 0) return;

            string line = TimingBook.Line(PressDates.ToMonthText(DigestBuilder.PreviousMonth(today)), summary);
            if (TimingBook.IsOverBudget(summary)) ModLog.Warn(Feature, line);
            else ModLog.Info(Feature, line);
        }

        /// <summary><c>TickPhase.LateYearPassed</c>: the spread of the metascores of the year that has just ended.</summary>
        internal static void OnYear()
        {
            if (!PressReviewsFeature.DebugVerbose) return;

            DateTime today = ModTicks.Today;
            Func<PressState> source = stateOf;
            if (today == default(DateTime) || source == null) return;

            int year = today.Year - 1;
            ScoreSummary summary = ScoreDistribution.Summarize(ScoreDistribution.ScoresOfYear(source().reviews, year));
            if (summary.Count == 0) return;

            string line = ScoreDistribution.Line(year, summary);
            if (ScoreDistribution.IsWithinTargets(summary)) ModLog.Info(Feature, line);
            else ModLog.Warn(Feature, line);
        }
    }
}
