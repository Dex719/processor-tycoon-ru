using System;
using System.Collections.Generic;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-022 (FR-12 item 6, D9): the marks a settlement leaves in the flags of the save. They stand between the core, which hands a feature that is switched on again
    /// the copy of its section it kept (the last save's, or none), and the player's balance: settled loans do not come back, a closed line stays closed, the settled
    /// investor contract does not come back, the counter of the rescues and the pause of the bridge loan do not fall back. Every mark only grows, and applying them
    /// twice changes nothing more.
    /// </summary>
    public class SettlementMarksTests
    {
        private static LoanData Loan(string id, string kind = LoanData.KindTerm)
        {
            LoanData loan = RecoveryTestData.GoodLoan(id);
            loan.kind = kind;
            return loan;
        }

        private static FakeSettlementFlags Settled(int loanSeq = 5, int bailouts = 1, string bridgeClosed = "", bool lineClosed = false)
        {
            var state = new RecoveryState { nextLoanSeq = loanSeq, bailoutsUsed = bailouts, lastBridgeClosedOn = bridgeClosed };
            var flags = new FakeSettlementFlags();
            SettlementMarks.Record(flags, state, lineClosed);
            return flags;
        }

        // ---- recording ----

        [Test]
        public void Record_WritesTheMarksOfTheState()
        {
            var state = new RecoveryState { nextLoanSeq = 4, bailoutsUsed = 2, lastBridgeClosedOn = "1976-03-01" };
            var flags = new FakeSettlementFlags();

            SettlementMarks.Record(flags, state, true);

            Assert.Equal(1, flags.GetInt(SettlementMarks.Settled));
            Assert.Equal(4, flags.GetInt(SettlementMarks.LoanSeq));
            Assert.Equal(2, flags.GetInt(SettlementMarks.Bailouts));
            Assert.Equal(1, flags.GetInt(SettlementMarks.LineClosed));
            Assert.Equal(SettlementMarks.DayNumber("1976-03-01"), flags.GetInt(SettlementMarks.BridgeDay));
        }

        [Test]
        public void Record_OnlyRaisesTheMarks()
        {
            var flags = Settled(loanSeq: 9, bailouts: 3, bridgeClosed: "1977-01-01", lineClosed: true);

            SettlementMarks.Record(flags, new RecoveryState { nextLoanSeq = 2, bailoutsUsed = 1, lastBridgeClosedOn = "1976-01-01" }, false);

            Assert.Equal(9, flags.GetInt(SettlementMarks.LoanSeq), "a smaller number never lowers the mark");
            Assert.Equal(3, flags.GetInt(SettlementMarks.Bailouts));
            Assert.Equal(SettlementMarks.DayNumber("1977-01-01"), flags.GetInt(SettlementMarks.BridgeDay));
            Assert.Equal(1, flags.GetInt(SettlementMarks.LineClosed), "a line that was closed stays marked");
        }

        [Test]
        public void Record_WithoutAFlagTableOrAState_DoesNothing()
        {
            SettlementMarks.Record(null, new RecoveryState(), false);
            var flags = new FakeSettlementFlags();
            SettlementMarks.Record(flags, null, false);
            Assert.Equal(0, flags.Values.Count);
        }

        [Test]
        public void Record_ABrokenDateOfThePause_IsNoDay()
        {
            var flags = new FakeSettlementFlags();
            SettlementMarks.Record(flags, new RecoveryState { lastBridgeClosedOn = "yesterday" }, false);
            Assert.Equal(0, flags.GetInt(SettlementMarks.BridgeDay));
        }

        // ---- applying ----

        [Test]
        public void Reconcile_WithoutTheMark_DoesNothing()
        {
            var state = RecoveryTestData.PopulatedState();
            string before = SettlementKit.Describe(state);

            bool changed = SettlementMarks.Reconcile(state, new FakeSettlementFlags(), null);

            Assert.False(changed);
            Assert.Equal(before, SettlementKit.Describe(state));
        }

        [Test]
        public void Reconcile_RemovesTheLoansThatWereSettled_AndKeepsTheLaterOnes()
        {
            var state = new RecoveryState { nextLoanSeq = 7, bailoutsUsed = 1 };
            state.loans.Add(Loan("bridge-1", LoanData.KindBridge));
            state.loans.Add(Loan("rescue-4", LoanData.KindRescueLoan));
            state.loans.Add(Loan("term-5"));       // the number of the mark: the first loan taken after the settlement
            state.loans.Add(Loan("term-6"));
            var notes = new List<string>();

            bool changed = SettlementMarks.Reconcile(state, Settled(loanSeq: 5), notes.Add);

            Assert.True(changed);
            Assert.Equal(2, state.loans.Count);
            Assert.Equal("term-5", state.loans[0].id);
            Assert.Equal("term-6", state.loans[1].id);
            Assert.Equal(2, notes.Count, "one note for each loan taken out");
            string all = string.Join("|", notes);
            Assert.True(all.Contains("bridge-1") && all.Contains("rescue-4"), all);
        }

        [Test]
        public void Reconcile_ALoanWithAnIdNobodyMakes_IsNotKept()
        {
            var state = new RecoveryState { nextLoanSeq = 3 };
            state.loans.Add(Loan("odd"));
            state.loans.Add(Loan("bridge-"));

            SettlementMarks.Reconcile(state, Settled(loanSeq: 3), null);

            Assert.Equal(0, state.loans.Count, "the book makes ids with a number, so these are not its loans");
        }

        [Test]
        public void Reconcile_ALineThatWasClosedByTheSwitch_StaysClosed()
        {
            var state = new RecoveryState();
            state.creditLine = new CreditLineData { state = CreditLineData.StateOpen, opensOn = "1975-01-01", closesOn = "1978-01-01", drawn = 4000000.0 };

            bool changed = SettlementMarks.Reconcile(state, Settled(lineClosed: true), null);

            Assert.True(changed);
            Assert.Equal(CreditLineData.StateClosed, state.creditLine.state);
            Assert.Equal(CreditLineData.ReasonDisabled, state.creditLine.closedReason);
            Assert.Near(0.0, state.creditLine.drawn, 0, "the debt of the old copy is not owed twice");
        }

        [Test]
        public void Reconcile_ALineThatWasNeverOpenedInTheCopy_IsClosedToo()
        {
            var state = new RecoveryState();   // the copy of a save that never had a line, or none at all

            SettlementMarks.Reconcile(state, Settled(lineClosed: true), null);

            Assert.Equal(CreditLineData.StateClosed, state.creditLine.state, "AC-12.6: the line does not open again");
        }

        [Test]
        public void Reconcile_ALineClosedByItsTerm_KeepsItsReason()
        {
            var state = new RecoveryState { nextLoanSeq = 5, bailoutsUsed = 1 };   // the counters already at the marks: the line is the only thing to look at
            state.creditLine = new CreditLineData { state = CreditLineData.StateClosed, closedReason = CreditLineData.ReasonExpired };

            bool changed = SettlementMarks.Reconcile(state, Settled(lineClosed: true), null);

            Assert.False(changed);
            Assert.Equal(CreditLineData.ReasonExpired, state.creditLine.closedReason);
        }

        [Test]
        public void Reconcile_ALineThatWasLeftOpen_IsLeftOpen()
        {
            var state = new RecoveryState();
            state.creditLine = new CreditLineData { state = CreditLineData.StateOpen, opensOn = "1975-01-01", closesOn = "1978-01-01", drawn = 0.0 };

            SettlementMarks.Reconcile(state, Settled(lineClosed: false), null);

            Assert.Equal(CreditLineData.StateOpen, state.creditLine.state, "a line the switch did not close is not closed by the marks");
        }

        [Test]
        public void Reconcile_TheContractOfASettledRescue_IsClosed_ALaterOneIsKept()
        {
            var old = new RecoveryState { bailoutsUsed = 1 };
            old.investorShare = new InvestorShareData { active = true, amount = 1e7, share = 0.3, monthsLeft = 40, paid = 0, cap = 3e7 };
            var later = new RecoveryState { bailoutsUsed = 2 };
            later.investorShare = new InvestorShareData { active = true, amount = 2e7, share = 0.3, monthsLeft = 58, paid = 0, cap = 6e7 };
            FakeSettlementFlags flags = Settled(bailouts: 1);

            SettlementMarks.Reconcile(old, flags, null);
            SettlementMarks.Reconcile(later, flags, null);

            Assert.False(old.investorShare.active, "the contract of the first rescue was settled");
            Assert.True(later.investorShare.active, "a contract of the second rescue was made after the settlement");
            Assert.Equal(2, later.bailoutsUsed);
        }

        [Test]
        public void Reconcile_TheCounterOfTheRescues_NeverFallsBelowTheMark()
        {
            var state = new RecoveryState { bailoutsUsed = 0 };   // a copy with no section at all

            bool changed = SettlementMarks.Reconcile(state, Settled(bailouts: 2), null);

            Assert.True(changed);
            Assert.Equal(2, state.bailoutsUsed, "switching off and on gives no new rescue");
        }

        [Test]
        public void Reconcile_TheNumberOfTheNextLoan_NeverFallsBelowTheMark()
        {
            var state = new RecoveryState { nextLoanSeq = 1 };

            SettlementMarks.Reconcile(state, Settled(loanSeq: 6), null);

            Assert.Equal(6, state.nextLoanSeq, "an id below the mark would look like a settled loan the next time");
        }

        [Test]
        public void Reconcile_ThePauseOfTheBridgeLoan_TakesTheLaterDate()
        {
            FakeSettlementFlags flags = Settled(bridgeClosed: "1977-03-15");

            var none = new RecoveryState();
            var earlier = new RecoveryState { lastBridgeClosedOn = "1976-01-01" };
            var later = new RecoveryState { lastBridgeClosedOn = "1978-01-01" };
            SettlementMarks.Reconcile(none, flags, null);
            SettlementMarks.Reconcile(earlier, flags, null);
            SettlementMarks.Reconcile(later, flags, null);

            Assert.Equal("1977-03-15", none.lastBridgeClosedOn, "AC-12.6: no new bridge loan until the end of the pause");
            Assert.Equal("1977-03-15", earlier.lastBridgeClosedOn);
            Assert.Equal("1978-01-01", later.lastBridgeClosedOn, "a later loan closed after the settlement stays");
        }

        [Test]
        public void Reconcile_NoPauseInTheMarks_LeavesTheDateAlone()
        {
            var state = new RecoveryState { lastBridgeClosedOn = "1976-01-01" };

            SettlementMarks.Reconcile(state, Settled(bridgeClosed: string.Empty), null);

            Assert.Equal("1976-01-01", state.lastBridgeClosedOn);
        }

        [Test]
        public void Reconcile_AppliedTwice_ChangesNothingTheSecondTime()
        {
            var state = RecoveryTestData.PopulatedState();
            FakeSettlementFlags flags = Settled(loanSeq: 9, bailouts: 3, bridgeClosed: "1977-05-01", lineClosed: true);

            bool first = SettlementMarks.Reconcile(state, flags, null);
            string after = SettlementKit.Describe(state);
            bool second = SettlementMarks.Reconcile(state, flags, null);

            Assert.True(first);
            Assert.False(second);
            Assert.Equal(after, SettlementKit.Describe(state));
        }

        [Test]
        public void Reconcile_ANullStateOrFlagTable_IsNotAnError()
        {
            Assert.False(SettlementMarks.Reconcile(null, new FakeSettlementFlags(), null));
            Assert.False(SettlementMarks.Reconcile(new RecoveryState(), null, null));
        }

        [Test]
        public void Reconcile_AStateWithMissingLists_IsRepairedNotCrashed()
        {
            var state = new RecoveryState { loans = null, creditLine = null, investorShare = null };

            bool changed = SettlementMarks.Reconcile(state, Settled(bailouts: 2), null);

            Assert.True(changed);
            Assert.Equal(2, state.bailoutsUsed);
        }

        // ---- the small helpers ----

        [Test]
        public void NumberOf_ReadsTheNumberAfterTheLastDash()
        {
            Assert.Equal(7, SettlementMarks.NumberOf("bridge-7"));
            Assert.Equal(12, SettlementMarks.NumberOf("rescue-12"));
            Assert.Equal(3, SettlementMarks.NumberOf("a-b-3"));
            Assert.Equal(-1, SettlementMarks.NumberOf(null));
            Assert.Equal(-1, SettlementMarks.NumberOf(""));
            Assert.Equal(-1, SettlementMarks.NumberOf("bridge"));
            Assert.Equal(-1, SettlementMarks.NumberOf("bridge-"));
            Assert.Equal(-1, SettlementMarks.NumberOf("bridge-1x"));
            Assert.Equal(-1, SettlementMarks.NumberOf("bridge-+1"));
            Assert.Equal(-1, SettlementMarks.NumberOf("bridge-99999999999"), "a number that does not fit is no number");
        }

        [Test]
        public void DayNumber_IsTheCountOfDays_AndComesBackAsTheSameDate()
        {
            Assert.Equal(0, SettlementMarks.DayNumber(""));
            Assert.Equal(0, SettlementMarks.DayNumber(null));
            Assert.Equal(0, SettlementMarks.DayNumber("1976-13-40"));
            Assert.Equal(1, SettlementMarks.DayNumber("0001-01-02"));
            Assert.Equal(SettlementMarks.DayNumber("1976-03-01") + 1, SettlementMarks.DayNumber("1976-03-02"));

            // a day of the calendar goes into the flags and comes back as the same text through the pause of the bridge loan
            foreach (string day in new[] { "1971-01-01", "1976-02-29", "2000-12-31", "2026-10-11" })
            {
                var state = new RecoveryState();
                var flags = new FakeSettlementFlags();
                flags.SetInt(SettlementMarks.Settled, 1);
                flags.SetInt(SettlementMarks.BridgeDay, SettlementMarks.DayNumber(day));
                SettlementMarks.Reconcile(state, flags, null);
                Assert.Equal(day, state.lastBridgeClosedOn);
            }
        }

        [Test]
        public void ANumberThatIsNoDay_IsIgnored()
        {
            var state = new RecoveryState();
            var flags = new FakeSettlementFlags();
            flags.SetInt(SettlementMarks.Settled, 1);
            flags.SetInt(SettlementMarks.BridgeDay, int.MaxValue);

            SettlementMarks.Reconcile(state, flags, null);
            Assert.Equal(string.Empty, state.lastBridgeClosedOn, "a mark from a damaged file does not become a date");

            flags.SetInt(SettlementMarks.BridgeDay, -5);
            SettlementMarks.Reconcile(state, flags, null);
            Assert.Equal(string.Empty, state.lastBridgeClosedOn);
        }
    }
}
