#if PTMOD_DEBUG
using System;
using System.Collections.Generic;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-019 (Acceptance; design.md "Ручные сценарии в игре", M1-M10): the arithmetic behind the debug buttons of the hub page - "Balance = k × D" (k = -0.8, -0.95, -1.1, -1.2, +0.1) and
    /// "DaysInDefault = N" (N = 1, 118). The buttons set the scenarios up, so the tests check that each factor really puts the player in the state its scenario needs (the warning, the auto
    /// draw, the default, the last day, the recovery), that every press is in the journal with the mark <c>debug</c>, and that nothing else of the state is touched. The whole file is the debug
    /// build's: the engine does not exist in a release build.
    /// </summary>
    [Covers(typeof(RecoveryDebugEngine), typeof(DebugOutcome), typeof(DebugStatus), typeof(DebugKind))]
    public class RecoveryDebugEngineTests
    {
        private const double Limit = 8400000.0;

        // A player with the threshold of the example of the design (D = 8.4 million) and a balance of 1 million.
        private static RecoveryRig Rig(double money = 1000000.0)
        {
            var rig = new RecoveryRig();
            rig.Finance.DebtLimit = Limit;
            rig.Finance.MoneyValue = money;
            return rig;
        }

        private static RecoveryDebugEngine Engine(RecoveryRig rig)
        {
            return new RecoveryDebugEngine(rig.Env);
        }

        // The money operations are refused without a word (a ledger that is there but does nothing).
        private sealed class DeafMoney : IRecoveryMoney
        {
            public bool IsAvailable
            {
                get { return true; }
            }

            public void CapitalIn(double amount, string op, string details)
            {
            }

            public void CapitalOut(double amount, string op, string details)
            {
            }

            public void Interest(double amount, string op, string details)
            {
            }

            public void InvestorPayment(double amount, string op, string details)
            {
            }
        }

        // ---- the buttons ----

        [Test]
        public void TheButtons_AreExactlyThoseOfTheAcceptance()
        {
            Assert.Equal(5, RecoveryDebugEngine.BalanceShares.Count);
            double[] shares = { -0.8, -0.95, -1.1, -1.2, 0.1 };
            for (int i = 0; i < shares.Length; i++) Assert.Near(shares[i], RecoveryDebugEngine.BalanceShares[i], 0, "k of the button " + i);

            Assert.Equal(2, RecoveryDebugEngine.DefaultDays.Count);
            Assert.Equal(1, RecoveryDebugEngine.DefaultDays[0]);
            Assert.Equal(118, RecoveryDebugEngine.DefaultDays[1]);
        }

        [Test]
        public void TheCaptions_ShowTheFactorWithItsSign_AndTheDecimalCommaOfRussian()
        {
            Assert.Equal("-0,8", RecoveryDebugEngine.ShareText(-0.8, true));
            Assert.Equal("-0.95", RecoveryDebugEngine.ShareText(-0.95, false));
            Assert.Equal("-1,1", RecoveryDebugEngine.ShareText(-1.1, true));
            Assert.Equal("-1.2", RecoveryDebugEngine.ShareText(-1.2, false));
            Assert.Equal("+0,1", RecoveryDebugEngine.ShareText(0.1, true), "the recovery has a plus");
            Assert.Equal("0", RecoveryDebugEngine.ShareText(0.0, false));
            Assert.Equal("0", RecoveryDebugEngine.ShareText(double.NaN, true), "a number that is not one is shown as 0");
            Assert.True(RecoveryDebugEngine.ShareText(-0.95, true).IndexOf('−') < 0, "a plain minus: the font of the game may lack the typographic one");
        }

        // ---- Balance = k × D ----

        [Test]
        public void EveryBalanceButton_PutsTheBalanceOnKTimesD_WithOneRecordOfTheJournal()
        {
            foreach (double share in RecoveryDebugEngine.BalanceShares)
            {
                RecoveryRig rig = Rig();

                DebugOutcome outcome = Engine(rig).SetBalanceShare(share);

                Assert.Equal(DebugStatus.Done, outcome.Status, "k = " + share);
                Assert.Equal(DebugKind.Balance, outcome.Kind);
                Assert.Near(share * Limit, rig.Finance.MoneyValue, 0.01, "k = " + share);
                Assert.Near(1000000.0, outcome.Before, 0);
                Assert.Near(share * Limit, outcome.After, 0.01, "the outcome shows the balance read back");
                List<JournalEntryData> records = rig.Entries(RecoveryDebugEngine.BalanceOp);
                Assert.Equal(1, records.Count, "every press is in the journal, once");
                Assert.Equal(1, rig.State.journal.Count, "and nothing else is");
                Assert.Near(share * Limit - 1000000.0, records[0].amount, 0.01, "signed from the point of view of the balance");
            }
        }

        [Test]
        public void TheRecord_CarriesTheMarkDebug_InTheCodeAndInTheNote()
        {
            RecoveryRig rig = Rig();

            Engine(rig).SetBalanceShare(-0.8);
            Engine(rig).SetDaysInDefault(118);

            foreach (JournalEntryData entry in rig.State.journal)
            {
                Assert.True(entry.op.StartsWith("debug.", StringComparison.Ordinal), "the code starts with 'debug.': " + entry.op);
                Assert.True(entry.details.StartsWith(RecoveryDebugEngine.Mark + ": ", StringComparison.Ordinal), "the note starts with the mark: " + entry.details);
            }
            Assert.Equal("debug: balance 1000000 -> -6720000 (-0.8 x D, D = 8400000)", rig.Entries(RecoveryDebugEngine.BalanceOp)[0].details);
            Assert.Equal("debug: DaysInDefault 0 -> 118", rig.Entries(RecoveryDebugEngine.DaysOp)[0].details);
            Assert.NotNull(JournalLines.KeyOf(RecoveryDebugEngine.BalanceOp), "the tab has a name for the record");
            Assert.NotNull(JournalLines.KeyOf(RecoveryDebugEngine.DaysOp), "and for the other one");
        }

        [Test]
        public void TheBalance_MovesAsACapitalOperation_UpOrDown_NeverThroughTheReport()
        {
            RecoveryRig down = Rig(1000000.0);
            Engine(down).SetBalanceShare(-1.2);
            Assert.Equal(1, down.Finance.Added.Count);
            Assert.True(down.Finance.Added[0] < 0.0, "from above the mark the balance is taken down");

            RecoveryRig up = Rig(-20000000.0);
            Engine(up).SetBalanceShare(0.1);
            Assert.Equal(1, up.Finance.Added.Count);
            Assert.True(up.Finance.Added[0] > 0.0, "from below the mark the balance is lifted");

            foreach (RecoveryRig rig in new[] { down, up })
            {
                Assert.Equal(0, rig.Finance.Spent.Count, "an expense of the report would be taxed and shown in the profit: not for a test");
                Assert.Equal(0, rig.Finance.Earned.Count);
            }
        }

        [Test]
        public void ABalanceAlreadyOnTheMark_MovesNothing_ButIsStillInTheJournal()
        {
            RecoveryRig rig = Rig(-0.8 * Limit);

            DebugOutcome outcome = Engine(rig).SetBalanceShare(-0.8);

            Assert.Equal(DebugStatus.Done, outcome.Status);
            Assert.Equal(0, rig.Finance.Added.Count, "no operation of 0 dollars");
            List<JournalEntryData> records = rig.Entries(RecoveryDebugEngine.BalanceOp);
            Assert.Equal(1, records.Count, "the press is in the journal all the same");
            Assert.Near(0.0, records[0].amount, 0);
        }

        [Test]
        public void TheBalanceButton_RefusesWhatItCannotDo_AndChangesNothing()
        {
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                RecoveryRig rig = Rig();
                DebugOutcome outcome = Engine(rig).SetBalanceShare(bad);
                Assert.Equal(DebugStatus.Rejected, outcome.Status, "k = " + bad);
                Assert.Equal("bad-number", outcome.Reason);
                Assert.Near(1000000.0, rig.Finance.MoneyValue, 0);
                Assert.Equal(0, rig.State.journal.Count);
            }

            RecoveryRig noLimit = Rig();
            noLimit.Finance.DebtLimit = 0.0;
            Assert.Equal("no-limit", Engine(noLimit).SetBalanceShare(-1.0).Reason, "without a threshold there is no D to count in");
            Assert.Near(1000000.0, noLimit.Finance.MoneyValue, 0);

            RecoveryRig bankrupt = Rig();
            bankrupt.Finance.Bankrupt = true;
            Assert.Equal("bankrupt", Engine(bankrupt).SetBalanceShare(-1.0).Reason, "the game is over");
            Assert.Equal(0, bankrupt.Finance.Added.Count);

            RecoveryRig deaf = Rig();
            var env = new RecoveryEnvironment(deaf.Finance, new DeafMoney(), deaf.Journal, () => deaf.State, () => deaf.Balance);
            DebugOutcome refused = new RecoveryDebugEngine(env).SetBalanceShare(-1.0);
            Assert.Equal(DebugStatus.Rejected, refused.Status, "a ledger that did nothing is not reported as done");
            Assert.Equal("ledger", refused.Reason);
        }

        [Test]
        public void WithoutThePlayersCompany_OrTheLedger_NothingIsDone()
        {
            RecoveryRig noCompany = Rig();
            noCompany.Finance.Available = false;
            Assert.Equal(DebugStatus.Unavailable, Engine(noCompany).SetBalanceShare(-0.8).Status);
            Assert.Equal(DebugStatus.Unavailable, Engine(noCompany).SetDaysInDefault(118).Status);
            Assert.Equal(0, noCompany.Finance.Added.Count);
            Assert.Equal(0, noCompany.Finance.Days, "the counter is not written");
            Assert.Equal(0, noCompany.State.journal.Count, "and nothing is recorded");

            RecoveryRig noLedger = Rig();
            noLedger.Money.Ledger = false;
            Assert.Equal(DebugStatus.Unavailable, Engine(noLedger).SetBalanceShare(-0.8).Status);
            Assert.Near(1000000.0, noLedger.Finance.MoneyValue, 0);
        }

        // ---- DaysInDefault = N ----

        [Test]
        public void EveryDaysButton_SetsTheCounter_AndRecordsIt()
        {
            foreach (int days in RecoveryDebugEngine.DefaultDays)
            {
                RecoveryRig rig = Rig();

                DebugOutcome outcome = Engine(rig).SetDaysInDefault(days);

                Assert.Equal(DebugStatus.Done, outcome.Status, "N = " + days);
                Assert.Equal(DebugKind.Days, outcome.Kind);
                Assert.Equal(days, rig.Finance.Days);
                Assert.Near(0.0, outcome.Before, 0);
                Assert.Near(days, outcome.After, 0);
                List<JournalEntryData> records = rig.Entries(RecoveryDebugEngine.DaysOp);
                Assert.Equal(1, records.Count);
                Assert.Near(0.0, records[0].amount, 0, "the counter moves no money");
                Assert.Equal(0, rig.Finance.Added.Count);
            }
        }

        [Test]
        public void TheCounter_NeverReachesTheLimitOfTheGame_TheButtonIsNotAWayToEndTheGame()
        {
            RecoveryRig rig = Rig();

            Assert.Equal(119, (int)Engine(rig).SetDaysInDefault(120).After, "the limit is 120: one day short of it");
            Assert.Equal(119, rig.Finance.Days);
            Assert.True(rig.Entries(RecoveryDebugEngine.DaysOp)[0].details.Contains("asked 120"), "the note says what was asked");

            Assert.Equal(119, (int)Engine(rig).SetDaysInDefault(5000).After);
            Assert.Equal(119, (int)Engine(rig).SetDaysInDefault(119).After, "the last day that is allowed");
            Assert.False(rig.Entries(RecoveryDebugEngine.DaysOp)[2].details.Contains("asked"), "an allowed count is written as it is");

            RecoveryRig unknown = Rig();
            unknown.Finance.TimeLimit = 0;   // the private field could not be read: the rules are off, and so is the cut
            Assert.Equal(500, (int)Engine(unknown).SetDaysInDefault(500).After, "without a limit there is nothing to hold the number below");
        }

        [Test]
        public void TheDaysButton_RefusesWhatItCannotDo()
        {
            RecoveryRig negative = Rig();
            Assert.Equal("bad-number", Engine(negative).SetDaysInDefault(-1).Reason);
            Assert.Equal(0, negative.Finance.Days);

            RecoveryRig bankrupt = Rig();
            bankrupt.Finance.Bankrupt = true;
            Assert.Equal("bankrupt", Engine(bankrupt).SetDaysInDefault(1).Reason);
            Assert.Equal(0, bankrupt.Finance.Days);
            Assert.Equal(0, bankrupt.State.journal.Count);
        }

        [Test]
        public void TheCounter_IsTheOnlyThingTheDaysButtonWrites()
        {
            RecoveryRig rig = Rig();
            rig.Finance.InDefault = true;
            rig.Finance.Days = 40;

            Engine(rig).SetDaysInDefault(118);

            Assert.Equal(0, rig.Finance.CancelCalls, "the default is not cancelled");
            Assert.True(rig.Finance.InDefault, "and not started: the flag of the fake is its own");
            Assert.Equal(0, rig.Finance.Added.Count);
            Assert.Equal(0, rig.Finance.Spent.Count);
            Assert.Equal("debug: DaysInDefault 40 -> 118", rig.Entries(RecoveryDebugEngine.DaysOp)[0].details);
        }

        // ---- the threshold line (TSK-003, the runtime part of the spike) ----

        [Test]
        public void TheThresholdLine_NamesTheDate_D_TheBaseOfTheLimits_AndTheInflation()
        {
            RecoveryRig rig = Rig();
            rig.Finance.TodayValue = new DateTime(1975, 1, 1);
            rig.Finance.InflationValue = 1.25;

            Assert.Equal("debug: threshold on 1975-01-01: D = 8400000, D_L = 8400000, inflation = 1.25", Engine(rig).ThresholdLine(), "D_L = max(8.4M, 2M x 1.25)");

            rig.Finance.DebtLimit = 1000000.0;   // the floor of the bank: the base of the limits is the floor of the feature
            rig.Finance.TodayValue = new DateTime(1985, 1, 1);
            rig.Finance.InflationValue = 2.0;
            Assert.Equal("debug: threshold on 1985-01-01: D = 1000000, D_L = 4000000, inflation = 2", Engine(rig).ThresholdLine(), "AC-7.6: D_L = max(1M, 2M x 2.0)");
        }

        [Test]
        public void TheThresholdLine_IsEmptyWithoutThePlayersCompany_AndChangesNothing()
        {
            RecoveryRig rig = Rig();
            rig.Finance.Available = false;

            Assert.Equal(string.Empty, Engine(rig).ThresholdLine());
            Assert.Equal(0, rig.State.journal.Count, "a line for the log, not a record of the journal");
            Assert.Equal(0, rig.Finance.Added.Count);
        }

        // ---- nothing else is touched ----

        [Test]
        public void TheState_ExceptTheJournal_IsTheSameAfterEveryButton()
        {
            RecoveryRig rig = Rig();
            rig.State = RecoveryTestData.PopulatedState();
            rig.State.journal.Clear();
            string before = SettlementKit.Describe(rig.State);

            foreach (double share in RecoveryDebugEngine.BalanceShares) Engine(rig).SetBalanceShare(share);
            foreach (int days in RecoveryDebugEngine.DefaultDays) Engine(rig).SetDaysInDefault(days);

            Assert.Equal(RecoveryDebugEngine.BalanceShares.Count + RecoveryDebugEngine.DefaultDays.Count, rig.State.journal.Count, "one record for each press");
            rig.State.journal.Clear();
            Assert.Equal(before, SettlementKit.Describe(rig.State), "the loans, the line, the investor and the counters are as they were");
        }

        // ---- the buttons reach the states of the scenarios ----

        [Test]
        public void M1_MinusPointEight_IsInTheWarningBand_AndNotYetInDefault()
        {
            RecoveryRig rig = Rig();
            Engine(rig).SetBalanceShare(-0.8);

            BankruptcyView view = rig.Finance.View();

            Assert.False(view.IsBelowThreshold, "above -D: not in default");
            Assert.Equal(PreWarningDecision.Send, RecoveryRules.PreWarning(view, false, rig.Balance.preWarning), "below -0.75 D: the letter 'default risk' on the next day");
        }

        [Test]
        public void M2_MinusOnePointOne_IsBelowTheThreshold()
        {
            RecoveryRig rig = Rig();
            Engine(rig).SetBalanceShare(-1.1);

            Assert.True(rig.Finance.View().IsBelowThreshold, "the next day the game declares the default and shows the warning window");
        }

        [Test]
        public void M5_MinusPointNinetyFive_IsBelowTheAutoDrawTrigger_ButAboveTheThreshold()
        {
            RecoveryRig rig = Rig();
            Engine(rig).SetBalanceShare(-0.95);

            BankruptcyView view = rig.Finance.View();

            Assert.False(view.IsBelowThreshold, "the auto draw has to lift the balance before the game's check, so no default");
            Assert.True(view.Money < -rig.Balance.startupCredit.autoDrawTriggerShare * Limit, "below -0.9 D: the line draws");
        }

        [Test]
        public void M6_TheLastChance_ComesOnTheSecondDayAfterTheTwoButtons()
        {
            // "Balance = -1.2 × D" and "DaysInDefault = 118": the game adds a day before it looks (118 -> 119), the offer is made when the next step would reach the limit
            RecoveryRig rig = Rig();
            Engine(rig).SetBalanceShare(-1.2);
            Engine(rig).SetDaysInDefault(118);
            rig.Finance.InDefault = true;   // the game's own flag is "the counter is 1 or more"; the fake keeps it apart

            Assert.False(RecoveryRules.MustHoldForBailout(rig.Finance.View(), 0, 1, false), "the first day only takes the counter to 119: no offer yet");

            rig.Finance.Days++;   // the game's step of the first day

            Assert.True(RecoveryRules.MustHoldForBailout(rig.Finance.View(), 0, 1, false), "the second day: the counter is held and the window opens");
        }

        [Test]
        public void M7_PlusPointOne_IsAboveTheThreshold_TheRecoveryStreakCounts()
        {
            RecoveryRig rig = Rig(-1.1 * Limit);
            rig.Finance.InDefault = true;
            Assert.True(rig.Finance.View().IsBelowThreshold);

            Engine(rig).SetBalanceShare(0.1);

            BankruptcyView view = rig.Finance.View();
            Assert.False(view.IsBelowThreshold);
            Assert.Equal(1, RecoveryRules.NextRecoveryStreak(view, 0), "a day at or above the threshold in default: the streak of seven days starts");
        }
    }
}
#endif
