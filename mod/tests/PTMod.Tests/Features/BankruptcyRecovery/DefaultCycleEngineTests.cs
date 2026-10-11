using System;
using System.Collections.Generic;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-010 (FR-8, FR-9, FR-11): the two moments of the game's bankruptcy cycle the patches P1 and P2 take part in. Before the game's tick (P1): on the last day
    /// of the player's default the counter is put to <c>T - 2</c> and the offer is asked for, but only when the rules say so and the window is there. After it
    /// (P2): seven days in a row at or above the threshold cancel the default with the game's method, a fall below the threshold starts the count again, and when
    /// the default is over the flags of the episode go back to their start. The numbers are the examples AC-8.1 to AC-8.6 and AC-9.1, AC-9.2.
    /// </summary>
    [Covers(typeof(DefaultCycleEngine))]
    public class DefaultCycleEngineTests
    {
        private sealed class Presenter : IBailoutPresenter
        {
            public int Opened;

            public bool Open(BailoutOffer offer)
            {
                Opened++;
                return true;
            }
        }

        private sealed class Setup
        {
            public readonly RecoveryRig Rig = new RecoveryRig();
            public readonly BailoutEngine Bailout;
            public readonly DefaultCycleEngine Cycle;
            public readonly Presenter Window = new Presenter();

            // AC-8.1: 1977, D = 24.3 million, balance -30 million, day 119 of a default, normal difficulty, nothing used yet.
            public Setup()
            {
                Rig.Finance.TodayValue = new DateTime(1977, 5, 1);
                Rig.Finance.DebtLimit = 24300000.0;
                Rig.Finance.MoneyValue = -30000000.0;
                Rig.Finance.InDefault = true;
                Rig.Finance.Days = 119;
                Bailout = new BailoutEngine(Rig.Env, new LoanBook(Rig.Env));
                Bailout.Presenter = Window;
                Cycle = new DefaultCycleEngine(Rig.Env, Bailout);
            }

            public FakePlayerFinance Finance
            {
                get { return Rig.Finance; }
            }

            public RecoveryState State
            {
                get { return Rig.State; }
            }

            // One day of the game's TickBankruptcies for a player in default: the counter goes up by one.
            public void GameCountsOneDay()
            {
                Finance.Days++;
            }
        }

        // ---- P1: holding the counter ----

        [Test]
        public void BeforeTick_HoldsTheCounterOnTheLastDay_AC_8_1()
        {
            var s = new Setup();

            bool held = s.Cycle.BeforeTick();

            Assert.True(held);
            Assert.Equal(118, s.Finance.Days, "T - 2: the game's own increment brings it to 119, one day short of the limit");
            Assert.Equal(1, s.Window.Opened, "the offer window is asked for once");
            Assert.True(s.Bailout.IsOfferOpen);

            s.GameCountsOneDay();
            Assert.True(s.Finance.Days < s.Finance.TimeLimit, "so the game does not declare the bankruptcy");
        }

        [Test]
        public void BeforeTick_KeepsHoldingWhileTheOfferIsOpen_ButOpensItOnce()
        {
            var s = new Setup();
            for (int day = 0; day < 5; day++)
            {
                Assert.True(s.Cycle.BeforeTick(), "day " + day);
                s.GameCountsOneDay();
                Assert.Equal(119, s.Finance.Days);
            }
            Assert.Equal(1, s.Window.Opened);
        }

        [Test]
        public void BeforeTick_DoesNotTouchEarlierDays()
        {
            var s = new Setup();
            s.Finance.Days = 118;   // 118 + 1 = 119, not yet 120
            Assert.False(s.Cycle.BeforeTick());
            Assert.Equal(118, s.Finance.Days);
            Assert.Equal(0, s.Window.Opened);

            s.Finance.Days = 30;
            Assert.False(s.Cycle.BeforeTick());
            Assert.Equal(30, s.Finance.Days);
        }

        [Test]
        public void BeforeTick_AfterARefusal_TheGameGoesItsWay_AC_8_3()
        {
            var s = new Setup();
            Assert.True(s.Cycle.BeforeTick());
            s.GameCountsOneDay();

            s.Bailout.Decline();

            Assert.False(s.Cycle.BeforeTick(), "no hold in this episode");
            Assert.Equal(119, s.Finance.Days, "the counter is left to the game: the next tick takes it to 120");
            Assert.Equal(1, s.Window.Opened);
        }

        [Test]
        public void BeforeTick_OnceTheOffersAreUsed_TheGameGoesItsWay_AC_8_4()
        {
            var s = new Setup();
            s.State.bailoutsUsed = 1;   // normal difficulty allows one
            Assert.False(s.Cycle.BeforeTick());
            Assert.Equal(119, s.Finance.Days);
            Assert.Equal(0, s.Window.Opened);
        }

        [Test]
        public void BeforeTick_OnTheHardestDifficulty_ThereIsNoRescue_AC_8_5()
        {
            var s = new Setup();
            s.Finance.DifficultyLevel = 5;
            Assert.False(s.Cycle.BeforeTick());
            Assert.Equal(119, s.Finance.Days);
            Assert.Equal(0, s.Window.Opened);
        }

        [Test]
        public void BeforeTick_OnTheEasiestDifficulty_ASecondOfferIsMade_AC_8_6()
        {
            var s = new Setup();
            s.Finance.DifficultyLevel = 0;   // two offers
            s.State.bailoutsUsed = 1;

            Assert.True(s.Cycle.BeforeTick());
            Assert.Equal(118, s.Finance.Days);

            s.State.bailoutsUsed = 2;
            s.Bailout.ResetSession();
            s.Finance.Days = 119;
            Assert.False(s.Cycle.BeforeTick(), "and after that the limit is used up");
        }

        [Test]
        public void BeforeTick_NeedsThePart_TheDefaultAndABalanceBelowTheThreshold()
        {
            var off = new Setup();
            off.Rig.Off.Add(SubFeature.Bailout);
            Assert.False(off.Cycle.BeforeTick());
            Assert.Equal(119, off.Finance.Days);

            var healthy = new Setup();
            healthy.Finance.MoneyValue = -24000000.0;   // above -24.3 million: the game cancels the default itself
            Assert.False(healthy.Cycle.BeforeTick());

            var notInDefault = new Setup();
            notInDefault.Finance.InDefault = false;
            Assert.False(notInDefault.Cycle.BeforeTick());

            var away = new Setup();
            away.Finance.Available = false;
            Assert.False(away.Cycle.BeforeTick());

            var broke = new Setup();
            broke.Finance.Bankrupt = true;
            Assert.False(broke.Cycle.BeforeTick());

            var unknownLimit = new Setup();
            unknownLimit.Finance.TimeLimit = 0;   // the private field could not be read: the rules stay out of the way
            Assert.False(unknownLimit.Cycle.BeforeTick());
            Assert.Equal(119, unknownLimit.Finance.Days);
        }

        [Test]
        public void BeforeTick_WithoutAWindow_DoesNotHold()
        {
            var s = new Setup();
            s.Bailout.Presenter = null;

            Assert.False(s.Cycle.BeforeTick(), "no window to show: the vanilla bankruptcy");
            Assert.Equal(119, s.Finance.Days);
        }

        [Test]
        public void BeforeTick_AWindowThatFailed_DoesNotHoldAgainInTheEpisode()
        {
            var s = new Setup();
            s.Bailout.Presenter = new FailingWindow();

            Assert.False(s.Cycle.BeforeTick());
            Assert.Equal(119, s.Finance.Days);
            Assert.True(s.Bailout.IsSuppressed);

            s.Bailout.Presenter = s.Window;
            Assert.False(s.Cycle.BeforeTick(), "not tried again");
            Assert.Equal(0, s.Window.Opened);
        }

        private sealed class FailingWindow : IBailoutPresenter
        {
            public bool Open(BailoutOffer offer)
            {
                return false;
            }
        }

        [Test]
        public void BeforeTick_AfterThePlayerAccepted_TheDefaultIsGone_AndNothingIsHeld()
        {
            var s = new Setup();
            Assert.True(s.Cycle.BeforeTick());

            Assert.True(s.Bailout.Accept(BailoutChoice.RescueLoan));

            Assert.False(s.Finance.InDefault);
            Assert.False(s.Cycle.BeforeTick());
            Assert.Equal(0, s.Finance.Days);
        }

        // ---- P2: the early exit ----

        // The state of a player in default whose balance is back above the threshold.
        private static Setup Recovered()
        {
            var s = new Setup();
            s.Finance.Days = 20;
            s.Finance.MoneyValue = -1000000.0;   // above -24.3 million
            return s;
        }

        [Test]
        public void AfterTick_SevenDaysInARowAboveTheThreshold_CancelTheDefault_AC_9_1()
        {
            var s = Recovered();

            for (int day = 1; day <= 6; day++)
            {
                Assert.False(s.Cycle.AfterTick(), "day " + day);
                Assert.Equal(day, s.State.episode.recoveryStreak);
            }
            Assert.Equal(0, s.Finance.CancelCalls);

            Assert.True(s.Cycle.AfterTick(), "the seventh day");

            Assert.Equal(1, s.Finance.CancelCalls, "the game's own method");
            Assert.False(s.Finance.InDefault);
            Assert.Equal(0, s.State.episode.recoveryStreak);
        }

        [Test]
        public void AfterTick_AFallBelowTheThresholdStartsTheCountAgain_AC_9_2()
        {
            var s = Recovered();
            for (int day = 1; day <= 5; day++) s.Cycle.AfterTick();
            Assert.Equal(5, s.State.episode.recoveryStreak);

            s.Finance.MoneyValue = -25000000.0;   // below the threshold on the sixth day
            Assert.False(s.Cycle.AfterTick());
            Assert.Equal(0, s.State.episode.recoveryStreak);
            Assert.True(s.Finance.InDefault);

            s.Finance.MoneyValue = -1000000.0;
            for (int day = 1; day <= 6; day++) Assert.False(s.Cycle.AfterTick());
            Assert.Equal(0, s.Finance.CancelCalls, "six days are not enough after the restart");
            Assert.True(s.Cycle.AfterTick());
        }

        [Test]
        public void AfterTick_TheStreakLengthIsFromTheBalanceFile()
        {
            var s = Recovered();
            s.Rig.Balance.earlyExit.streakDays = 2;

            Assert.False(s.Cycle.AfterTick());
            Assert.True(s.Cycle.AfterTick());
        }

        [Test]
        public void AfterTick_WithThePartOff_KeepsTheVanillaBehaviour()
        {
            var s = Recovered();
            s.Rig.Off.Add(SubFeature.EarlyExit);
            s.State.episode.recoveryStreak = 5;

            for (int day = 0; day < 20; day++) Assert.False(s.Cycle.AfterTick());

            Assert.Equal(0, s.Finance.CancelCalls);
            Assert.Equal(0, s.State.episode.recoveryStreak);
            Assert.True(s.Finance.InDefault);
        }

        [Test]
        public void AfterTick_ABankruptCompanyCountsNothing()
        {
            var s = Recovered();
            s.Finance.Bankrupt = true;
            Assert.False(s.Cycle.AfterTick());
            Assert.Equal(0, s.State.episode.recoveryStreak);
            Assert.Equal(0, s.Finance.CancelCalls);
        }

        [Test]
        public void AfterTick_WithoutTheCompany_DoesNothing()
        {
            var s = Recovered();
            s.Finance.Available = false;
            s.State.episode.recoveryStreak = 3;
            Assert.False(s.Cycle.AfterTick());
            Assert.Equal(3, s.State.episode.recoveryStreak, "nothing is touched while the company is not there");
        }

        [Test]
        public void AfterTick_WhenTheDefaultIsOver_TheEpisodeStartsAgain()
        {
            var s = new Setup();
            s.Finance.InDefault = false;
            s.Finance.Days = 0;
            s.State.episode.defaultMailSent = true;
            s.State.episode.bailoutDeclined = true;
            s.State.episode.recoveryStreak = 4;
            s.State.episode.preWarningSent = true;   // its own rule resets this one (the balance above -0.5 D)

            Assert.False(s.Cycle.AfterTick());

            Assert.False(s.State.episode.defaultMailSent);
            Assert.False(s.State.episode.bailoutDeclined);
            Assert.Equal(0, s.State.episode.recoveryStreak);
            Assert.True(s.State.episode.preWarningSent, "the warning letter has its own hysteresis");
        }

        [Test]
        public void AfterTick_WhenTheDefaultIsOver_TheWindowMayTryAgainNextTime()
        {
            var s = new Setup();
            s.Bailout.Presenter = new FailingWindow();
            Assert.False(s.Cycle.BeforeTick());
            Assert.True(s.Bailout.IsSuppressed);

            s.Finance.InDefault = false;
            s.Cycle.AfterTick();
            Assert.False(s.Bailout.IsSuppressed);
        }

        [Test]
        public void AfterTick_ADefaultThatBeginsInTheSameTick_IsNotForgotten()
        {
            // the game's TriggerDefault ran inside the tick: the player is in default when the postfix looks
            var s = new Setup();
            s.Finance.Days = 1;
            s.Finance.MoneyValue = -30000000.0;
            s.State.episode.defaultMailSent = true;

            Assert.False(s.Cycle.AfterTick());

            Assert.True(s.State.episode.defaultMailSent);
            Assert.Equal(0, s.State.episode.recoveryStreak, "below the threshold: no streak");
        }

        [Test]
        public void Constructor_RefusesMissingParts()
        {
            var rig = new RecoveryRig();
            var bailout = new BailoutEngine(rig.Env, new LoanBook(rig.Env));
            Assert.Throws<ArgumentNullException>(() => new DefaultCycleEngine(null, bailout));
            Assert.Throws<ArgumentNullException>(() => new DefaultCycleEngine(rig.Env, null));
        }
    }
}
