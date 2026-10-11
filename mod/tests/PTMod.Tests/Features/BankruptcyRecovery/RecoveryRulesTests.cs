using System;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-006 (FR-1, FR-6, FR-8, FR-9, FR-11): the decisions of the default cycle - when the bankruptcy counter is held for the last-chance offer,
    /// when the warning letter is sent and forgotten, how the streak of the early exit counts and whether a bridge loan may be offered.
    /// </summary>
    [Covers(typeof(RecoveryRules), typeof(BankruptcyView), typeof(PreWarningDecision), typeof(Eligibility))]
    public class RecoveryRulesTests
    {
        private static readonly DateTime Today = new DateTime(1977, 5, 1);
        private static readonly RecoveryBalance Balance = RecoveryBalance.Defaults();

        // The player of the 1977 example: D = 24.3 million, the game's limit of 120 days.
        private static BankruptcyView View(double money = -30000000.0, double debtLimit = 24300000.0, bool inDefault = true, int days = 119,
            bool isPlayer = true, bool bankrupt = false, int limit = 120, DateTime? today = null)
        {
            return new BankruptcyView(money, debtLimit, inDefault, bankrupt, days, limit, today ?? Today, isPlayer);
        }

        // ---- BankruptcyView ----

        [Test]
        public void BankruptcyView_CarriesItsFields_AndTheThresholdIsTheNegativeLimit()
        {
            BankruptcyView v = View(-5.0, 100.0, true, 7, true, false, 120, new DateTime(1980, 2, 3));
            Assert.Near(-5.0, v.Money, 0);
            Assert.Near(100.0, v.DebtLimit, 0);
            Assert.Near(-100.0, v.Threshold, 0);
            Assert.True(v.InDefault && v.IsPlayer && !v.IsBankrupt);
            Assert.Equal(7, v.DaysInDefault);
            Assert.Equal(120, v.DefaultTimeLimit);
            Assert.Equal(new DateTime(1980, 2, 3), v.Today);
        }

        [Test]
        public void BankruptcyView_HasThreshold_NeedsAPositiveFiniteLimit()
        {
            Assert.True(View(debtLimit: 1.0).HasThreshold);
            Assert.False(View(debtLimit: 0.0).HasThreshold);
            Assert.False(View(debtLimit: -5.0).HasThreshold);
            Assert.False(View(debtLimit: double.NaN).HasThreshold);
            Assert.False(View(debtLimit: double.PositiveInfinity).HasThreshold);
            Assert.False(default(BankruptcyView).HasThreshold);
        }

        [Test]
        public void BankruptcyView_IsBelowThreshold_IsStrict()
        {
            Assert.True(View(money: -100.5, debtLimit: 100.0).IsBelowThreshold);
            Assert.False(View(money: -100.0, debtLimit: 100.0).IsBelowThreshold, "exactly at the threshold the game does not start a default");
            Assert.False(View(money: 5.0, debtLimit: 100.0).IsBelowThreshold);
            Assert.False(View(money: double.NaN, debtLimit: 100.0).IsBelowThreshold);
            Assert.False(View(money: -500.0, debtLimit: 0.0).IsBelowThreshold, "no threshold: never below it");
        }

        // ---- MustHoldForBailout ----

        [Test]
        public void MustHold_The1977Example_AC_8_1()
        {
            // D = 24.3 million, balance -30 million, DaysInDefault = 119, no offers used, normal difficulty (1 allowed).
            Assert.True(RecoveryRules.MustHoldForBailout(View(), 0, 1, false));
        }

        [Test]
        public void MustHold_OnlyOnTheLastDay_DaysPlusOneReachesTheLimit()
        {
            Assert.False(RecoveryRules.MustHoldForBailout(View(days: 1), 0, 1, false));
            Assert.False(RecoveryRules.MustHoldForBailout(View(days: 117), 0, 1, false));
            Assert.False(RecoveryRules.MustHoldForBailout(View(days: 118), 0, 1, false), "118 + 1 = 119 < 120");
            Assert.True(RecoveryRules.MustHoldForBailout(View(days: 119), 0, 1, false), "119 + 1 = 120");
            Assert.True(RecoveryRules.MustHoldForBailout(View(days: 120), 0, 1, false));
            Assert.True(RecoveryRules.MustHoldForBailout(View(days: int.MaxValue), 0, 1, false), "no overflow");
        }

        [Test]
        public void MustHold_FollowsTheLimitOfTheGame()
        {
            Assert.True(RecoveryRules.MustHoldForBailout(View(days: 59, limit: 60), 0, 1, false));
            Assert.False(RecoveryRules.MustHoldForBailout(View(days: 58, limit: 60), 0, 1, false));
            Assert.False(RecoveryRules.MustHoldForBailout(View(days: 119, limit: 0), 0, 1, false), "no limit read: do not meddle");
            Assert.False(RecoveryRules.MustHoldForBailout(View(days: 119, limit: -1), 0, 1, false));
        }

        [Test]
        public void MustHold_NeverForAnotherCompany()
        {
            Assert.False(RecoveryRules.MustHoldForBailout(View(isPlayer: false), 0, 2, false), "an AI company in the same state");
        }

        [Test]
        public void MustHold_NeedsTheDefaultAndTheBalanceBelowTheThreshold()
        {
            Assert.False(RecoveryRules.MustHoldForBailout(View(inDefault: false), 0, 1, false), "not in default");
            Assert.False(RecoveryRules.MustHoldForBailout(View(bankrupt: true), 0, 1, false), "already bankrupt");
            Assert.False(RecoveryRules.MustHoldForBailout(View(money: -24300000.0), 0, 1, false), "exactly at the threshold: the game cancels the default");
            Assert.True(RecoveryRules.MustHoldForBailout(View(money: -24300001.0), 0, 1, false), "one dollar below");
            Assert.False(RecoveryRules.MustHoldForBailout(View(money: -10000000.0), 0, 1, false), "back above the threshold");
            Assert.False(RecoveryRules.MustHoldForBailout(View(money: 1000000.0), 0, 1, false));
            Assert.False(RecoveryRules.MustHoldForBailout(View(money: double.NaN), 0, 1, false));
        }

        [Test]
        public void MustHold_NoUsableThreshold_NeverHolds()
        {
            Assert.False(RecoveryRules.MustHoldForBailout(View(debtLimit: 0.0), 0, 1, false));
            Assert.False(RecoveryRules.MustHoldForBailout(View(debtLimit: double.NaN), 0, 1, false));
            Assert.False(RecoveryRules.MustHoldForBailout(default(BankruptcyView), 0, 1, false));
        }

        [Test]
        public void MustHold_StopsWhenTheOffersAreUsedUp_AC_8_4()
        {
            Assert.True(RecoveryRules.MustHoldForBailout(View(), 0, 1, false));
            Assert.False(RecoveryRules.MustHoldForBailout(View(), 1, 1, false), "normal difficulty, the offer was used");
            Assert.False(RecoveryRules.MustHoldForBailout(View(), 2, 1, false));
        }

        [Test]
        public void MustHold_VeryEasy_AllowsASecondOffer_AC_8_6()
        {
            int allowed = BailoutMath.Allowed(Balance.bailout, 0);
            Assert.True(RecoveryRules.MustHoldForBailout(View(), 1, allowed, false), "one used of two");
            Assert.False(RecoveryRules.MustHoldForBailout(View(), 2, allowed, false), "two used of two");
        }

        [Test]
        public void MustHold_Impossible_NeverHolds_AC_8_5()
        {
            int allowed = BailoutMath.Allowed(Balance.bailout, 5);
            Assert.Equal(0, allowed);
            Assert.False(RecoveryRules.MustHoldForBailout(View(), 0, allowed, false));
        }

        [Test]
        public void MustHold_AfterDecline_ThePathIsVanilla_AC_8_3()
        {
            Assert.False(RecoveryRules.MustHoldForBailout(View(), 0, 1, true));
        }

        [Test]
        public void MustHold_HoldingTheCounter_KeepsTheGameFromDeclaringTheBankruptcy()
        {
            // The game adds one to DaysInDefault each tick and declares the bankruptcy when it reaches the limit. P1 sets DaysInDefault to
            // limit - 2 whenever the rule says hold: the counter stays below the limit for as long as the rule holds.
            int days = 119;
            for (int tick = 0; tick < 30; tick++)
            {
                if (RecoveryRules.MustHoldForBailout(View(days: days), 0, 1, false)) days = 120 - 2;
                days++;
                Assert.True(days < 120, "tick " + tick + ": the bankruptcy must not come while the offer is open");
            }
        }

        [Test]
        public void MustHold_WithoutTheHold_TheGameWouldReachTheLimit()
        {
            int days = 119;
            days++;
            Assert.False(days < 120, "this is the vanilla bankruptcy the hold prevents");
        }

        // ---- PreWarning ----

        [Test]
        public void PreWarning_AC_1_1_SendsBelowThreeQuartersOfTheThreshold()
        {
            // D = 10 million; -7.6 million is below -7.5 million; -7.0 million is not.
            Assert.Equal(PreWarningDecision.None, RecoveryRules.PreWarning(View(-7000000.0, 10000000.0, false, 0), false, Balance.preWarning));
            Assert.Equal(PreWarningDecision.Send, RecoveryRules.PreWarning(View(-7600000.0, 10000000.0, false, 0), false, Balance.preWarning));
        }

        [Test]
        public void PreWarning_ExactlyAtTheLine_DoesNotSend()
        {
            Assert.Equal(PreWarningDecision.None, RecoveryRules.PreWarning(View(-7500000.0, 10000000.0, false, 0), false, Balance.preWarning));
            Assert.Equal(PreWarningDecision.Send, RecoveryRules.PreWarning(View(-7500000.5, 10000000.0, false, 0), false, Balance.preWarning));
        }

        [Test]
        public void PreWarning_AC_1_2_NoRepeatWhileTheEpisodeLasts()
        {
            Assert.Equal(PreWarningDecision.None, RecoveryRules.PreWarning(View(-8000000.0, 10000000.0, false, 0), true, Balance.preWarning));
            Assert.Equal(PreWarningDecision.None, RecoveryRules.PreWarning(View(-9000000.0, 10000000.0, false, 0), true, Balance.preWarning));
            Assert.Equal(PreWarningDecision.None, RecoveryRules.PreWarning(View(-6000000.0, 10000000.0, false, 0), true, Balance.preWarning),
                "between the two lines the episode goes on");
        }

        [Test]
        public void PreWarning_AC_1_3_ResetsAboveHalfTheThreshold_ThenSendsAgain()
        {
            // Sent; the balance rises to -4 million (above -5 million): reset; falls to -8 million: send again.
            PreWarningDecision up = RecoveryRules.PreWarning(View(-4000000.0, 10000000.0, false, 0), true, Balance.preWarning);
            Assert.Equal(PreWarningDecision.Reset, up);

            PreWarningDecision down = RecoveryRules.PreWarning(View(-8000000.0, 10000000.0, false, 0), false, Balance.preWarning);
            Assert.Equal(PreWarningDecision.Send, down);
        }

        [Test]
        public void PreWarning_ResetLine_IsInclusive()
        {
            Assert.Equal(PreWarningDecision.Reset, RecoveryRules.PreWarning(View(-5000000.0, 10000000.0, false, 0), true, Balance.preWarning));
            Assert.Equal(PreWarningDecision.None, RecoveryRules.PreWarning(View(-5000000.5, 10000000.0, false, 0), true, Balance.preWarning));
        }

        [Test]
        public void PreWarning_NothingToResetWhenNothingWasSent()
        {
            Assert.Equal(PreWarningDecision.None, RecoveryRules.PreWarning(View(5000000.0, 10000000.0, false, 0), false, Balance.preWarning));
        }

        [Test]
        public void PreWarning_FullEpisode_SendOnceThenResetThenSendAgain()
        {
            double[] balances = { -3, -7.6, -8.0, -9.0, -6.0, -4.0, -4.5, -8.0, -8.5 };
            bool sent = false;
            int letters = 0;
            foreach (double million in balances)
            {
                PreWarningDecision d = RecoveryRules.PreWarning(View(million * 1e6, 10000000.0, false, 0), sent, Balance.preWarning);
                if (d == PreWarningDecision.Send) { sent = true; letters++; }
                else if (d == PreWarningDecision.Reset) sent = false;
            }
            Assert.Equal(2, letters, "one letter per fall");
            Assert.True(sent);
        }

        [Test]
        public void PreWarning_NotInDefault_OnlyForSending()
        {
            Assert.Equal(PreWarningDecision.None, RecoveryRules.PreWarning(View(-9000000.0, 10000000.0, true, 3), false, Balance.preWarning),
                "in default the letter of the default replaces the warning");
            Assert.Equal(PreWarningDecision.Reset, RecoveryRules.PreWarning(View(-1000000.0, 10000000.0, true, 3), true, Balance.preWarning));
        }

        [Test]
        public void PreWarning_OnlyForThePlayer_AndNotForTheBankrupt()
        {
            Assert.Equal(PreWarningDecision.None, RecoveryRules.PreWarning(View(-9000000.0, 10000000.0, false, 0, isPlayer: false), false, Balance.preWarning));
            Assert.Equal(PreWarningDecision.None, RecoveryRules.PreWarning(View(-9000000.0, 10000000.0, false, 0, bankrupt: true), false, Balance.preWarning));
            Assert.Equal(PreWarningDecision.None, RecoveryRules.PreWarning(View(-4000000.0, 10000000.0, false, 0, isPlayer: false), true, Balance.preWarning));
        }

        [Test]
        public void PreWarning_NoUsableThreshold_IsSilent()
        {
            Assert.Equal(PreWarningDecision.None, RecoveryRules.PreWarning(View(-9000000.0, 0.0, false, 0), false, Balance.preWarning));
            Assert.Equal(PreWarningDecision.None, RecoveryRules.PreWarning(View(-9000000.0, double.NaN, false, 0), false, Balance.preWarning));
            Assert.Equal(PreWarningDecision.None, RecoveryRules.PreWarning(View(double.NaN, 10000000.0, false, 0), false, Balance.preWarning));
        }

        [Test]
        public void PreWarning_UsesTheSharesOfTheConfig_AndDefaultsForNull()
        {
            var tight = new PreWarningConfig { thresholdDebtLimitShare = 0.5, resetDebtLimitShare = 0.25 };
            Assert.Equal(PreWarningDecision.Send, RecoveryRules.PreWarning(View(-5500000.0, 10000000.0, false, 0), false, tight));
            Assert.Equal(PreWarningDecision.Reset, RecoveryRules.PreWarning(View(-2500000.0, 10000000.0, false, 0), true, tight));
            Assert.Equal(PreWarningDecision.Send, RecoveryRules.PreWarning(View(-7600000.0, 10000000.0, false, 0), false, null));
        }

        // ---- NextRecoveryStreak ----

        [Test]
        public void Streak_CountsDaysAtOrAboveTheThreshold()
        {
            Assert.Equal(1, RecoveryRules.NextRecoveryStreak(View(money: -10000000.0), 0));
            Assert.Equal(6, RecoveryRules.NextRecoveryStreak(View(money: -10000000.0), 5));
            Assert.Equal(1, RecoveryRules.NextRecoveryStreak(View(money: -24300000.0), 0), "exactly at the threshold counts");
        }

        [Test]
        public void Streak_ResetsBelowTheThreshold_AC_9_2()
        {
            Assert.Equal(0, RecoveryRules.NextRecoveryStreak(View(money: -24300001.0), 5));
            Assert.Equal(0, RecoveryRules.NextRecoveryStreak(View(money: -30000000.0), 5));
            Assert.Equal(0, RecoveryRules.NextRecoveryStreak(View(money: double.NaN), 5));
        }

        [Test]
        public void Streak_IsZeroOutsideTheDefault_AndForOthers()
        {
            Assert.Equal(0, RecoveryRules.NextRecoveryStreak(View(money: 1000000.0, inDefault: false), 5), "no default to leave");
            Assert.Equal(0, RecoveryRules.NextRecoveryStreak(View(money: -10000000.0, isPlayer: false), 5));
            Assert.Equal(0, RecoveryRules.NextRecoveryStreak(View(money: -10000000.0, bankrupt: true), 5));
            Assert.Equal(0, RecoveryRules.NextRecoveryStreak(View(money: -10000000.0, debtLimit: 0.0), 5), "no usable threshold");
        }

        [Test]
        public void Streak_NegativeInput_CountsAsZero_AndNeverOverflows()
        {
            Assert.Equal(1, RecoveryRules.NextRecoveryStreak(View(money: -10000000.0), -4));
            Assert.Equal(int.MaxValue, RecoveryRules.NextRecoveryStreak(View(money: -10000000.0), int.MaxValue));
        }

        [Test]
        public void Streak_AC_9_1_SevenDaysInARow_CompleteTheExit()
        {
            int streak = 0;
            int completedOn = -1;
            for (int day = 1; day <= 10; day++)
            {
                streak = RecoveryRules.NextRecoveryStreak(View(money: -10000000.0, days: 20 + day), streak);
                if (completedOn < 0 && RecoveryRules.IsStreakComplete(streak, Balance.earlyExit)) completedOn = day;
            }
            Assert.Equal(7, completedOn);
        }

        [Test]
        public void Streak_ADipBreaksTheSeries()
        {
            int streak = 0;
            double[] balances = { -1, -1, -1, -1, -1, -30, -1, -1 };   // millions; the threshold is -24.3 million
            foreach (double million in balances)
            {
                streak = RecoveryRules.NextRecoveryStreak(View(money: million * 1e6), streak);
            }
            Assert.Equal(2, streak);
            Assert.False(RecoveryRules.IsStreakComplete(streak, Balance.earlyExit));
        }

        [Test]
        public void IsStreakComplete_FollowsTheConfig()
        {
            Assert.False(RecoveryRules.IsStreakComplete(6, Balance.earlyExit));
            Assert.True(RecoveryRules.IsStreakComplete(7, Balance.earlyExit));
            Assert.True(RecoveryRules.IsStreakComplete(8, Balance.earlyExit));
            var three = new EarlyExitConfig { streakDays = 3 };
            Assert.False(RecoveryRules.IsStreakComplete(2, three));
            Assert.True(RecoveryRules.IsStreakComplete(3, three));
            Assert.False(RecoveryRules.IsStreakComplete(0, new EarlyExitConfig { streakDays = 0 }), "at least one day");
            Assert.True(RecoveryRules.IsStreakComplete(1, new EarlyExitConfig { streakDays = 0 }));
            Assert.True(RecoveryRules.IsStreakComplete(7, null), "a missing config gives the defaults");
            Assert.False(RecoveryRules.IsStreakComplete(6, null));
        }

        // ---- CanTakeBridge ----

        [Test]
        public void Bridge_BelowThreeQuartersOfTheThreshold_IsOffered()
        {
            Eligibility e = RecoveryRules.CanTakeBridge(View(-7000000.0, 8400000.0, false, 0), false, null, Balance.bridgeLoan);
            Assert.True(e.Ok, "AC-6.1: -7 million against -6.3 million");
            Assert.Null(e.ReasonKey);
        }

        [Test]
        public void Bridge_FarFromTheThreshold_IsNotNeeded()
        {
            Eligibility e = RecoveryRules.CanTakeBridge(View(-2000000.0, 8400000.0, false, 0), false, null, Balance.bridgeLoan);
            Assert.False(e.Ok);
            Assert.Equal(RecoveryRules.ReasonNotNeeded, e.ReasonKey);
        }

        [Test]
        public void Bridge_ExactlyAtTheLine_IsNotYetNeeded()
        {
            Assert.False(RecoveryRules.CanTakeBridge(View(-6300000.0, 8400000.0, false, 0), false, null, Balance.bridgeLoan).Ok);
            Assert.True(RecoveryRules.CanTakeBridge(View(-6300001.0, 8400000.0, false, 0), false, null, Balance.bridgeLoan).Ok);
        }

        [Test]
        public void Bridge_InDefault_IsOffered_EvenAboveTheLine()
        {
            Assert.True(RecoveryRules.CanTakeBridge(View(-1000000.0, 8400000.0, true, 12), false, null, Balance.bridgeLoan).Ok);
            Assert.True(RecoveryRules.CanTakeBridge(View(-1000000.0, 0.0, true, 12), false, null, Balance.bridgeLoan).Ok, "the default itself is the reason");
        }

        [Test]
        public void Bridge_AnActiveLoan_BlocksANewOne_AC_6_2()
        {
            Eligibility e = RecoveryRules.CanTakeBridge(View(-7000000.0, 8400000.0, false, 0), true, null, Balance.bridgeLoan);
            Assert.False(e.Ok);
            Assert.Equal(RecoveryRules.ReasonActive, e.ReasonKey);
            Assert.Equal("rec.bridge.unavailable.active", e.ReasonKey, "the key of the design table");
        }

        [Test]
        public void Bridge_Cooldown_Is180Days()
        {
            BankruptcyView v = View(-7000000.0, 8400000.0, false, 0, today: new DateTime(1977, 5, 1));

            Eligibility early = RecoveryRules.CanTakeBridge(v, false, new DateTime(1977, 5, 1), Balance.bridgeLoan);
            Assert.False(early.Ok);
            Assert.Equal(RecoveryRules.ReasonCooldown, early.ReasonKey);
            Assert.Equal(180, early.CooldownDaysLeft);

            DateTime closed = new DateTime(1977, 5, 1).AddDays(-179);
            Eligibility oneShort = RecoveryRules.CanTakeBridge(v, false, closed, Balance.bridgeLoan);
            Assert.False(oneShort.Ok, "179 days");
            Assert.Equal(1, oneShort.CooldownDaysLeft);

            Eligibility enough = RecoveryRules.CanTakeBridge(v, false, new DateTime(1977, 5, 1).AddDays(-180), Balance.bridgeLoan);
            Assert.True(enough.Ok, "180 days");
            Assert.Equal(0, enough.CooldownDaysLeft);

            Assert.True(RecoveryRules.CanTakeBridge(v, false, new DateTime(1970, 1, 1), Balance.bridgeLoan).Ok, "long ago");
        }

        [Test]
        public void Bridge_CooldownCountsDates_NotTimes()
        {
            BankruptcyView v = View(-7000000.0, 8400000.0, false, 0, today: new DateTime(1977, 5, 1, 0, 0, 0));
            Assert.True(RecoveryRules.CanTakeBridge(v, false, new DateTime(1976, 11, 2, 23, 59, 0), Balance.bridgeLoan).Ok, "180 days by date");
        }

        [Test]
        public void Bridge_FollowsTheCooldownOfTheConfig()
        {
            var none = new BridgeConfig { cooldownDays = 0 };
            BankruptcyView v = View(-7000000.0, 8400000.0, false, 0);
            Assert.True(RecoveryRules.CanTakeBridge(v, false, Today, none).Ok, "closed today, no pause");
            var year = new BridgeConfig { cooldownDays = 365 };
            Assert.False(RecoveryRules.CanTakeBridge(v, false, Today.AddDays(-300), year).Ok);
        }

        [Test]
        public void Bridge_ReasonsComeInOrder_BankruptThenActiveThenCooldownThenNeed()
        {
            BankruptcyView rich = View(5000000.0, 8400000.0, false, 0);
            Assert.Equal(RecoveryRules.ReasonBankrupt, RecoveryRules.CanTakeBridge(View(bankrupt: true), true, Today, Balance.bridgeLoan).ReasonKey);
            Assert.Equal(RecoveryRules.ReasonActive, RecoveryRules.CanTakeBridge(rich, true, Today, Balance.bridgeLoan).ReasonKey);
            Assert.Equal(RecoveryRules.ReasonCooldown, RecoveryRules.CanTakeBridge(rich, false, Today, Balance.bridgeLoan).ReasonKey);
            Assert.Equal(RecoveryRules.ReasonNotNeeded, RecoveryRules.CanTakeBridge(rich, false, null, Balance.bridgeLoan).ReasonKey);
        }

        [Test]
        public void Bridge_NotForAnotherCompany()
        {
            Eligibility e = RecoveryRules.CanTakeBridge(View(isPlayer: false), false, null, Balance.bridgeLoan);
            Assert.False(e.Ok);
            Assert.Null(e.ReasonKey);
        }

        [Test]
        public void Bridge_NoThresholdAndNotInDefault_IsNotNeeded()
        {
            Eligibility e = RecoveryRules.CanTakeBridge(View(-7000000.0, 0.0, false, 0), false, null, Balance.bridgeLoan);
            Assert.False(e.Ok);
            Assert.Equal(RecoveryRules.ReasonNotNeeded, e.ReasonKey);
        }

        [Test]
        public void Bridge_UsesTheShareOfTheConfig_AndDefaultsForNull()
        {
            var wide = new BridgeConfig { eligibleDebtLimitShare = 0.25 };
            BankruptcyView v = View(-2500000.0, 8400000.0, false, 0);
            Assert.True(RecoveryRules.CanTakeBridge(v, false, null, wide).Ok, "-2.5 million is below -2.1 million");
            Assert.False(RecoveryRules.CanTakeBridge(v, false, null, Balance.bridgeLoan).Ok);
            Assert.False(RecoveryRules.CanTakeBridge(v, false, null, null).Ok, "a missing config gives the defaults");
        }

        [Test]
        public void Eligibility_Factories_AndDefault()
        {
            Eligibility yes = Eligibility.Yes();
            Assert.True(yes.Ok);
            Assert.Null(yes.ReasonKey);
            Eligibility no = Eligibility.No("rec.x");
            Assert.False(no.Ok);
            Assert.Equal("rec.x", no.ReasonKey);
            Assert.False(default(Eligibility).Ok);
            Assert.Equal(PreWarningDecision.None, default(PreWarningDecision));
        }

        [Test]
        public void ReasonKeys_AreKeysOfTheFeatureWithAnEnglishAndARussianText()
        {
            string[] keys =
            {
                RecoveryRules.ReasonActive, RecoveryRules.ReasonBankrupt, RecoveryRules.ReasonCooldown, RecoveryRules.ReasonNotNeeded,
            };
            foreach (string language in new[] { "ru", "en" })
            {
                var table = RecoveryTestData.ParseObject(RecoveryTestData.ReadStrings(language));
                foreach (string key in keys)
                {
                    Assert.True(key.StartsWith("rec.bridge.unavailable.", StringComparison.Ordinal), key);
                    Assert.True(table.ContainsKey(key), language + " is missing " + key);
                }
            }
        }
    }
}
