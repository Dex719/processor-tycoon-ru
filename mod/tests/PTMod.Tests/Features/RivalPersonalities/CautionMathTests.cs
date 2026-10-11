using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The caution of an AI company after a public design flaw (FR-17, TSK-030): the numbers (<see cref="CautionMath"/>), the entries of the section
    /// (<see cref="CautionBook"/>) and the cleaning of the monthly pass. The patch itself and the subscription need the game and are read as text in
    /// <see cref="IntegrationWiringTests"/>.
    /// </summary>
    [Covers(typeof(CautionMath), typeof(CautionBook), typeof(CautionOutcome))]
    public class CautionMathTests
    {
        private static readonly DateTime Day = new DateTime(1984, 3, 1);

        private static CautionBook NewBook(RivalState state, RivalConfig config = null)
        {
            RivalConfig used = config ?? RivalConfig.Defaults();
            return new CautionBook(() => state, () => used);
        }

        // ---- the numbers ----

        [Test]
        public void Factor_IsTheConfiguredFactorWhileActive_AndOneOtherwise()
        {
            Assert.Equal(0.85f, CautionMath.Factor(true, 0.85f));
            Assert.Equal(1f, CautionMath.Factor(false, 0.85f), "no caution, the game's value");
            Assert.Equal(0.5f, CautionMath.Factor(true, 0.5f));
        }

        [Test]
        public void Factor_NeverMakesTheAiBolder_AndIgnoresUnusableNumbers()
        {
            Assert.Equal(1f, CautionMath.Factor(true, 1.4f), "above one is held to one");
            Assert.Equal(1f, CautionMath.Factor(true, 0f));
            Assert.Equal(1f, CautionMath.Factor(true, -0.3f));
            Assert.Equal(1f, CautionMath.Factor(true, float.NaN));
            Assert.Equal(1f, CautionMath.Factor(true, float.PositiveInfinity));
        }

        [Test]
        public void UntilDate_IsTheStartPlusTheMonths()
        {
            Assert.Equal(new DateTime(1986, 3, 1), CautionMath.UntilDate(new DateTime(1984, 3, 1), 24));
            Assert.Equal(new DateTime(1984, 3, 1), CautionMath.UntilDate(new DateTime(1984, 3, 1), 0));
            Assert.Equal(new DateTime(1984, 3, 1), CautionMath.UntilDate(new DateTime(1984, 3, 1), -5), "a negative count is zero");
            Assert.Equal(new DateTime(1984, 2, 29), CautionMath.UntilDate(new DateTime(1983, 12, 31), 2), "the end of a short month");
            Assert.Equal(new DateTime(1984, 3, 1), CautionMath.UntilDate(new DateTime(1984, 3, 1, 17, 30, 0), 0), "the time of day is dropped");
        }

        [Test]
        public void UntilDate_AtTheEndOfTheCalendar_IsTheLastDay()
        {
            Assert.Equal(DateTime.MaxValue.Date, CautionMath.UntilDate(new DateTime(9999, 6, 1), 24));
        }

        [Test]
        public void Severity_MajorAndCritical_StartTheCaution_ByDefault()
        {
            Assert.False(CautionMath.IsSevereEnough(CautionMath.Minor, "Major"));
            Assert.True(CautionMath.IsSevereEnough(CautionMath.Major, "Major"));
            Assert.True(CautionMath.IsSevereEnough(CautionMath.Critical, "Major"));
            Assert.False(CautionMath.IsSevereEnough(CautionMath.Major, "Critical"), "the minimum can be raised");
            Assert.True(CautionMath.IsSevereEnough(CautionMath.Minor, "Minor"), "or lowered");
        }

        [Test]
        public void Severity_OutOfRangeNumber_DoesNotStartIt_AndABadMinimumIsMajor()
        {
            Assert.False(CautionMath.IsSevereEnough(-1, "Minor"));
            Assert.False(CautionMath.IsSevereEnough(3, "Minor"));
            Assert.False(CautionMath.IsSevereEnough(CautionMath.Minor, "major"), "the config spells the names exactly");
            Assert.True(CautionMath.IsSevereEnough(CautionMath.Major, null));
            Assert.Equal(-1, CautionMath.SeverityLevel("Huge"));
            Assert.Equal(2, CautionMath.SeverityLevel("Critical"));
        }

        [Test]
        public void Applies_OnlyWhenTheFlawFeatureIsOn_TheCompanyIsAliveAndAwake_AndTheGameIsNotFastForwarding()
        {
            Assert.True(CautionMath.Applies(true, false, false, false));
            Assert.False(CautionMath.Applies(false, false, false, false), "design-flaw-recalls is off");
            Assert.False(CautionMath.Applies(true, true, false, false), "bankrupt");
            Assert.False(CautionMath.Applies(true, false, true, false), "dormant");
            Assert.False(CautionMath.Applies(true, false, false, true), "fast-forward");
        }

        // ---- the entries ----

        [Test]
        public void AMajorFlaw_StartsTheCaution_For24Months()
        {
            var state = new RivalState();
            CautionBook book = NewBook(state);

            CautionOutcome outcome = book.OnPublicFlaw(4, 7, CautionMath.Major, Day, Day);

            Assert.Equal(CautionOutcome.Started, outcome);
            Assert.Equal(1, state.cautions.Count);
            Assert.Equal(4, state.cautions[0].uid);
            Assert.Equal(7, state.cautions[0].flawId);
            Assert.Equal("1986-03-01", state.cautions[0].until);
            Assert.True(book.IsActive(4, Day));
            Assert.True(book.IsActive(4, new DateTime(1986, 2, 28)), "the last day of the caution");
            Assert.False(book.IsActive(4, new DateTime(1986, 3, 1)), "it ends on the day 'until'");
            Assert.False(book.IsActive(5, Day), "another company is not careful");
        }

        [Test]
        public void ACriticalFlaw_StartsIt_AMinorOneDoesNot()
        {
            var state = new RivalState();
            CautionBook book = NewBook(state);

            Assert.Equal(CautionOutcome.TooMild, book.OnPublicFlaw(4, 1, CautionMath.Minor, Day, Day));
            Assert.Equal(0, state.cautions.Count, "nothing is written for a minor flaw");
            Assert.Equal(CautionOutcome.Started, book.OnPublicFlaw(4, 2, CautionMath.Critical, Day, Day));
            Assert.Equal(1, state.cautions.Count);
        }

        [Test]
        public void TheSameFlawRecord_StartsTheCautionOnce()
        {
            var state = new RivalState();
            CautionBook book = NewBook(state);

            Assert.Equal(CautionOutcome.Started, book.OnPublicFlaw(4, 7, CautionMath.Major, Day, Day));
            Assert.Equal(CautionOutcome.Duplicate, book.OnPublicFlaw(4, 7, CautionMath.Critical, Day, Day.AddDays(40)), "a second notice of the same record");
            Assert.Equal(1, state.cautions.Count);
            Assert.Equal("1986-03-01", state.cautions[0].until, "the first term stands");
        }

        [Test]
        public void ADifferentFlaw_OfTheSameCompany_GetsItsOwnEntry_ButTheFactorIsNotStacked()
        {
            var state = new RivalState();
            CautionBook book = NewBook(state);

            book.OnPublicFlaw(4, 7, CautionMath.Major, Day, Day);
            book.OnPublicFlaw(4, 9, CautionMath.Major, Day.AddMonths(6), Day.AddMonths(6));

            Assert.Equal(2, state.cautions.Count);
            Assert.Equal(0.85f, book.FactorFor(4, Day.AddMonths(7)), "two entries in force, one factor");
            Assert.Equal(new DateTime(1986, 9, 1), book.UntilOf(4, Day.AddMonths(7)).Value, "the later of the two ends");
        }

        [Test]
        public void TheTermStartsWhenTheFlawFirstBecamePublic_NotWhenWeHeardOfIt()
        {
            var state = new RivalState();
            CautionBook book = NewBook(state);
            DateTime publicSince = new DateTime(1983, 3, 1);

            CautionOutcome outcome = book.OnPublicFlaw(4, 7, CautionMath.Major, publicSince, new DateTime(1984, 3, 1));

            Assert.Equal(CautionOutcome.Started, outcome, "a year into the term");
            Assert.Equal("1985-03-01", state.cautions[0].until);
        }

        [Test]
        public void AFlawThatWentPublicLongAgo_StartsNothing()
        {
            var state = new RivalState();
            CautionBook book = NewBook(state);

            CautionOutcome outcome = book.OnPublicFlaw(4, 7, CautionMath.Critical, new DateTime(1980, 1, 1), new DateTime(1984, 3, 1));

            Assert.Equal(CautionOutcome.AlreadyOver, outcome, "the term of 24 months ended in 1982");
            Assert.Equal(0, state.cautions.Count);
        }

        [Test]
        public void AnEntryThatEndedAndWasCleaned_IsNotMadeAgainByALateNotice()
        {
            var state = new RivalState();
            CautionBook book = NewBook(state);
            book.OnPublicFlaw(4, 7, CautionMath.Major, Day, Day);
            DateTime later = new DateTime(1986, 4, 1);
            Assert.Equal(1, CautionCleaner.RemoveExpired(state, later), "the monthly pass removes it");

            CautionOutcome outcome = book.OnPublicFlaw(4, 7, CautionMath.Major, Day, later);   // the flaw goes public again: PublicSinceDay stays the first day

            Assert.Equal(CautionOutcome.AlreadyOver, outcome);
            Assert.Equal(0, state.cautions.Count);
        }

        [Test]
        public void WithoutADate_TheDayOfTheNoticeIsTheStart()
        {
            var state = new RivalState();
            CautionBook book = NewBook(state);

            Assert.Equal(CautionOutcome.Started, book.OnPublicFlaw(4, 7, CautionMath.Major, default(DateTime), Day));
            Assert.Equal("1986-03-01", state.cautions[0].until);
        }

        [Test]
        public void ANegativeCompanyOrAnUnknownToday_IsInvalid()
        {
            var state = new RivalState();
            CautionBook book = NewBook(state);

            Assert.Equal(CautionOutcome.Invalid, book.OnPublicFlaw(-1, 7, CautionMath.Major, Day, Day));
            Assert.Equal(CautionOutcome.Invalid, book.OnPublicFlaw(4, 7, CautionMath.Major, Day, default(DateTime)));
            Assert.False(book.IsActive(4, default(DateTime)), "no date, no caution");
            Assert.Equal(0, state.cautions.Count);
        }

        [Test]
        public void TheConfigSetsTheMonthsTheFactorAndTheMinimum()
        {
            var state = new RivalState();
            RivalConfig config = RivalConfig.Defaults();
            config.flawCaution.months = 6;
            config.flawCaution.factor = 0.5f;
            config.flawCaution.minSeverity = "Critical";
            CautionBook book = NewBook(state, config);

            Assert.Equal(CautionOutcome.TooMild, book.OnPublicFlaw(4, 1, CautionMath.Major, Day, Day), "the minimum was raised");
            Assert.Equal(CautionOutcome.Started, book.OnPublicFlaw(4, 2, CautionMath.Critical, Day, Day));
            Assert.Equal("1984-09-01", state.cautions[0].until);
            Assert.Equal(0.5f, book.FactorFor(4, Day.AddDays(10)));
            Assert.Equal(1f, book.FactorFor(4, new DateTime(1984, 9, 1)), "the term is over");
        }

        [Test]
        public void ZeroMonths_MeansNoCaution()
        {
            var state = new RivalState();
            RivalConfig config = RivalConfig.Defaults();
            config.flawCaution.months = 0;

            Assert.Equal(CautionOutcome.AlreadyOver, NewBook(state, config).OnPublicFlaw(4, 1, CautionMath.Critical, Day, Day));
            Assert.Equal(0, state.cautions.Count);
        }

        [Test]
        public void TheSection_HoldsAtMostFiftyEntries_TheEndedAndTheEarliestGoFirst()
        {
            var state = new RivalState();
            CautionBook book = NewBook(state);

            // 60 flaws of 60 companies, one a day, all in force at the end
            for (int i = 0; i < 60; i++)
            {
                DateTime when = Day.AddDays(i);
                book.OnPublicFlaw(i, 100 + i, CautionMath.Major, when, when);
            }

            Assert.Equal(CautionBook.MaxEntries, state.cautions.Count);
            Assert.Equal(50, CautionBook.MaxEntries);
            Assert.False(state.cautions.Any(c => c.uid == 0), "the one that ends first is gone");
            Assert.True(state.cautions.Any(c => c.uid == 59), "the newest stays");
            Assert.True(book.IsActive(59, Day.AddDays(60)));
        }

        [Test]
        public void WhenTheListIsFull_AnEntryThatEndedGoesBeforeOnesInForce()
        {
            var state = new RivalState();
            CautionBook book = NewBook(state);
            for (int i = 0; i < 50; i++) state.cautions.Add(new CautionEntry { uid = i, flawId = i, until = "1990-01-01" });
            state.cautions[30].until = "1983-01-01";   // ended before the day of the notice

            book.OnPublicFlaw(77, 500, CautionMath.Major, Day, Day);

            Assert.Equal(50, state.cautions.Count);
            Assert.False(state.cautions.Any(c => c.uid == 30), "the ended entry made room");
            Assert.True(state.cautions.Any(c => c.uid == 0) && state.cautions.Any(c => c.uid == 49), "the ones in force stay");
            Assert.True(state.cautions.Any(c => c.uid == 77));
        }

        [Test]
        public void AnEntryWithAnUnreadableDate_GoesFirst_AndIsNeverInForce()
        {
            var state = new RivalState();
            CautionBook book = NewBook(state);
            for (int i = 0; i < 50; i++) state.cautions.Add(new CautionEntry { uid = i, flawId = i, until = "1990-01-01" });
            state.cautions[10].until = "soon";

            Assert.False(book.IsActive(10, Day), "an unreadable date is no caution");
            book.OnPublicFlaw(77, 500, CautionMath.Major, Day, Day);

            Assert.False(state.cautions.Any(c => c.uid == 10));
            Assert.Equal(50, state.cautions.Count);
        }

        [Test]
        public void TheEntries_SurviveTheSaveRoundTrip()
        {
            var state = new RivalState();
            CautionBook book = NewBook(state);
            book.OnPublicFlaw(4, 7, CautionMath.Major, Day, Day);

            RivalState back = RivalSaveCodec.Read(RivalSaveCodec.Write(state), null);
            CautionBook readBack = NewBook(back);

            Assert.Equal(1, back.cautions.Count);
            Assert.True(readBack.IsActive(4, new DateTime(1985, 1, 1)));
            Assert.Equal(CautionOutcome.Duplicate, readBack.OnPublicFlaw(4, 7, CautionMath.Major, Day, new DateTime(1985, 1, 1)), "the load does not start a second one");
        }

        [Test]
        public void TheMonthlyPass_RemovesTheEndedEntries_AndKeepsTheOnesInForce()
        {
            var rig = new AttitudeRig().AssignAll();
            rig.Today = new DateTime(1986, 4, 1);
            rig.State.cautions.Add(new CautionEntry { uid = 4, flawId = 1, until = "1986-03-01" });
            rig.State.cautions.Add(new CautionEntry { uid = 5, flawId = 2, until = "1987-01-01" });

            MonthlyResult result = rig.Monthly.Run(new MonthlyInput { Today = rig.Today });

            Assert.Equal(1, result.Expired);
            Assert.Equal(new List<int> { 5 }, rig.State.cautions.Select(c => c.uid).ToList());
        }

        [Test]
        public void ABookWithoutState_ReadsNothing_AndWritesNowhere()
        {
            var book = new CautionBook(null, null);

            Assert.False(book.IsActive(1, Day));
            Assert.Equal(1f, book.FactorFor(1, Day));
            Assert.Equal(0, book.Count);
            Assert.True(book.UntilOf(1, Day) == null);
            Assert.Equal(CautionOutcome.Started, book.OnPublicFlaw(1, 1, CautionMath.Major, Day, Day), "it works on a throwaway state");
        }
    }
}
