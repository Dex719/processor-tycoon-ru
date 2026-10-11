using System;
using System.Collections.Generic;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-022 (FR-12, D9): the safe switch-off without the game. The question shows the sum, the balance after it and the risk of a default (and nothing when
    /// nothing is owed); the settlement takes the sum from the balance in ONE operation, closes the loans, the credit line and the investor contract, starts the pause of
    /// the bridge loan, writes the record <c>disable.convert</c>, keeps the counter of the rescues, repeats safely, never gives the player money, and does nothing
    /// while the player's company or the ledger is not there.
    /// </summary>
    public class DisableSettlementEngineTests
    {
        private static DisableSettlementEngine Engine(RecoveryRig rig, FakeSettlementFlags flags = null)
        {
            return new DisableSettlementEngine(rig.Env, flags ?? new FakeSettlementFlags());
        }

        // ---- the question ----

        [Test]
        public void Quote_WhenNothingIsOwed_IsNull()
        {
            var rig = new RecoveryRig();
            Assert.Null(Engine(rig).Quote(), "the hub switches the feature off without a question");

            rig.State.creditLine = new CreditLineData { state = CreditLineData.StateOpen, opensOn = "1975-01-01", closesOn = "1978-01-01", drawn = 0.0 };
            Assert.Null(Engine(rig).Quote(), "an open line with nothing drawn owes nothing");

            rig.State.creditLine = new CreditLineData { state = CreditLineData.StateClosed, closedReason = CreditLineData.ReasonExpired, drawn = 0.0 };
            rig.State.investorShare = new InvestorShareData { active = true, amount = 1000000.0, share = 0.3, monthsLeft = 10, paid = 3000000.0, cap = 3000000.0 };
            Assert.Null(Engine(rig).Quote(), "a contract that has paid its ceiling has nothing left to pay");
        }

        [Test]
        public void Quote_AC_12_2_ShowsTheSumAndTheBalanceAfter()
        {
            RecoveryRig rig = SettlementKit.OneBridgeRig();

            DisablePlan plan = Engine(rig).Quote();

            Assert.NotNull(plan);
            Assert.Near(5000000.0, plan.Total, 0);
            Assert.Near(2000000.0, plan.Money, 0);
            Assert.Near(-3000000.0, plan.BalanceAfter, 0, "AC-12.2: the balance after is -3 000 000");
            Assert.False(plan.WouldDefault, "-3 000 000 is above the threshold of -8 400 000");
            Assert.Equal(1, plan.LoanCount);
        }

        [Test]
        public void Quote_WarnsWhenTheBalanceAfterIsBelowTheThreshold()
        {
            RecoveryRig rig = SettlementKit.OneBridgeRig();
            rig.Finance.DebtLimit = 2500000.0;

            DisablePlan plan = Engine(rig).Quote();

            Assert.True(plan.WouldDefault, "-3 000 000 is below -2 500 000: the confirmation warns about the default (edge case 17)");
        }

        [Test]
        public void Quote_SplitsTheSumBySource()
        {
            RecoveryRig rig = SettlementKit.PopulatedRig();

            DisablePlan plan = Engine(rig).Quote();

            Assert.Near(6527277.0 + 36075000.0, plan.Breakdown.Loans, 0);
            Assert.Near(2660000.0, plan.Breakdown.LineDrawn, 0);
            Assert.Near(105825000.0, plan.Breakdown.InvestorRemainder, 0, "the ceiling minus what was paid: never cheaper than the contract (D9)");
            Assert.Near(SettlementKit.PopulatedTotal, plan.Total, 0);
            Assert.Equal(2, plan.LoanCount);
        }

        [Test]
        public void Quote_ChangesNothing_AndFollowsTheLiveNumbers()
        {
            RecoveryRig rig = SettlementKit.PopulatedRig();
            DisableSettlementEngine engine = Engine(rig);
            string before = SettlementKit.Describe(rig.State);

            DisablePlan first = engine.Quote();
            rig.Finance.MoneyValue = 1000000.0;
            DisablePlan second = engine.Quote();

            Assert.Equal(before, SettlementKit.Describe(rig.State), "asking is not doing");
            Assert.Equal(0, rig.Finance.Added.Count + rig.Finance.Spent.Count + rig.Finance.Earned.Count);
            Assert.Near(200000000.0 - SettlementKit.PopulatedTotal, first.BalanceAfter, 0);
            Assert.Near(1000000.0 - SettlementKit.PopulatedTotal, second.BalanceAfter, 0, "the numbers are those of the moment the player flipped the switch");
        }

        [Test]
        public void Quote_WithoutThePlayersCompany_IsNull()
        {
            RecoveryRig rig = SettlementKit.PopulatedRig();
            rig.Finance.Available = false;

            Assert.Null(Engine(rig).Quote(), "in the menu there is no balance to show");
        }

        // ---- the settlement ----

        [Test]
        public void Settle_AC_12_2_TakesTheDebtFromTheBalance_AndWritesTheRecord()
        {
            RecoveryRig rig = SettlementKit.OneBridgeRig();

            SettleResult result = Engine(rig).Settle();

            Assert.Equal(SettleStatus.Done, result.Status);
            Assert.Near(5000000.0, result.Converted, 0);
            Assert.Near(-3000000.0, rig.Finance.MoneyValue, 0, "AC-12.2: the balance is -3 000 000");
            Assert.Equal(0, rig.State.loans.Count, "the loan is closed");

            List<JournalEntryData> records = rig.Entries("disable.convert");
            Assert.Equal(1, records.Count, "a record of the conversion");
            Assert.Near(-5000000.0, records[0].amount, 0, "signed from the point of view of the balance");
            Assert.Equal("1976-09-01", records[0].date);
            Assert.Equal("1 loan", records[0].details);
        }

        [Test]
        public void Settle_IsOneOperationForTheWholeSum_ThroughTheCapitalOnly()
        {
            RecoveryRig rig = SettlementKit.PopulatedRig();

            Engine(rig).Settle();

            Assert.Equal(1, rig.Finance.Added.Count, "ONE CapitalOut for the total (design: DisableSettlementService)");
            Assert.Near(-SettlementKit.PopulatedTotal, rig.Finance.Added[0], 0);
            Assert.Equal(0, rig.Finance.Spent.Count, "the conversion is not an expense of the report");
            Assert.Equal(0, rig.Finance.Earned.Count);
            Assert.Equal(1, rig.Entries("disable.convert").Count);
            Assert.Equal("2 loans, line, investor", rig.Entries("disable.convert")[0].details);
            Assert.Near(200000000.0 - SettlementKit.PopulatedTotal, rig.Finance.MoneyValue, 0);
        }

        [Test]
        public void Settle_ClosesTheLoansTheLineAndTheContract_AndKeepsTheRest()
        {
            RecoveryRig rig = SettlementKit.PopulatedRig();

            SettleResult result = Engine(rig).Settle();

            Assert.Equal(2, result.LoansClosed);
            Assert.True(result.LineClosed);
            Assert.True(result.InvestorClosed);
            Assert.Equal(0, rig.State.loans.Count);
            Assert.Equal(CreditLineData.StateClosed, rig.State.creditLine.state, "a closed line is never opened again");
            Assert.Equal(CreditLineData.ReasonDisabled, rig.State.creditLine.closedReason, "the reason: the switch-off, not the end of the term");
            Assert.Near(0.0, rig.State.creditLine.drawn, 0);
            Assert.False(rig.State.investorShare.active);
            Assert.Equal(1, rig.State.bailoutsUsed, "the counter of the rescues survives (FR-12 item 6)");
            Assert.Equal(3, rig.State.nextLoanSeq, "the ids are not handed out twice");
            Assert.Equal(3, rig.State.journal.Count - rig.Entries("disable.convert").Count, "what was in the journal stays");
        }

        [Test]
        public void Settle_AClosedBridgeLoan_StartsThePause_AC_12_6()
        {
            RecoveryRig rig = SettlementKit.OneBridgeRig();
            rig.Finance.MoneyValue = -7000000.0;   // close to the threshold: a bridge loan would be wanted
            var bridge = new BridgeLoanEngine(rig.Env, new LoanBook(rig.Env));

            SettleResult result = Engine(rig).Settle();

            Assert.True(result.BridgeClosed);
            Assert.Equal("1976-09-01", rig.State.lastBridgeClosedOn, "the pause counts from the day the loan was closed");
            Eligibility eligibility = bridge.Check();
            Assert.False(eligibility.Ok, "AC-12.6: no new bridge loan until the end of the pause");
            Assert.Equal(RecoveryRules.ReasonCooldown, eligibility.ReasonKey);
            Assert.Equal(180, eligibility.CooldownDaysLeft);
        }

        [Test]
        public void Settle_WithoutABridgeLoan_LeavesTheDateOfTheLastOne()
        {
            RecoveryRig rig = SettlementKit.PopulatedRig();
            rig.State.loans.RemoveAt(0);   // only the rescue loan is left
            rig.State.lastBridgeClosedOn = "1974-02-01";

            SettleResult result = Engine(rig).Settle();

            Assert.False(result.BridgeClosed);
            Assert.Equal("1974-02-01", rig.State.lastBridgeClosedOn);
        }

        [Test]
        public void Settle_RepeatedCall_DoesNothing()
        {
            RecoveryRig rig = SettlementKit.PopulatedRig();
            DisableSettlementEngine engine = Engine(rig);
            engine.Settle();
            double money = rig.Finance.MoneyValue;
            string state = SettlementKit.Describe(rig.State);

            SettleResult again = engine.Settle();

            Assert.Equal(SettleStatus.NothingToSettle, again.Status, "after the settlement nothing is owed");
            Assert.Near(money, rig.Finance.MoneyValue, 0, "no second charge");
            Assert.Equal(1, rig.Finance.Added.Count);
            Assert.Equal(state, SettlementKit.Describe(rig.State));
            Assert.Equal(1, rig.Entries("disable.convert").Count, "no second record");
        }

        [Test]
        public void Quote_AfterTheSettlement_IsNull()
        {
            RecoveryRig rig = SettlementKit.PopulatedRig();
            DisableSettlementEngine engine = Engine(rig);
            Assert.NotNull(engine.Quote());

            engine.Settle();

            Assert.Null(engine.Quote(), "nothing is owed any more: no question the next time the switch is flipped");
        }

        [Test]
        public void Settle_NeverLeavesThePlayerRicher()
        {
            // success criterion: switching off with obligations always leaves the balance no higher than before
            var cases = new List<RecoveryRig> { SettlementKit.PopulatedRig(), SettlementKit.OneBridgeRig() };

            var lineOnly = new RecoveryRig();
            lineOnly.State.creditLine = new CreditLineData { state = CreditLineData.StateOpen, opensOn = "1975-01-01", closesOn = "1978-01-01", drawn = 1.0 };
            cases.Add(lineOnly);

            var contractOnly = new RecoveryRig();
            contractOnly.State.investorShare = new InvestorShareData { active = true, amount = 5000000.0, share = 0.3, monthsLeft = 40, paid = 0.0, cap = 15000000.0 };
            cases.Add(contractOnly);

            var negative = SettlementKit.PopulatedRig();
            negative.Finance.MoneyValue = -50000000.0;
            cases.Add(negative);

            foreach (RecoveryRig rig in cases)
            {
                double before = rig.Finance.MoneyValue;
                Engine(rig).Settle();
                Assert.True(rig.Finance.MoneyValue <= before, "the balance rose from " + before + " to " + rig.Finance.MoneyValue);
                foreach (double amount in rig.Finance.Added) Assert.True(amount < 0.0, "the settlement only takes money");
            }
        }

        [Test]
        public void Settle_ABalanceThatFallsBelowZero_IsAllowed()
        {
            RecoveryRig rig = SettlementKit.PopulatedRig();
            rig.Finance.MoneyValue = 1000.0;

            Engine(rig).Settle();

            Assert.Near(1000.0 - SettlementKit.PopulatedTotal, rig.Finance.MoneyValue, 0, "the debt may go below zero and the bank charges its ordinary interest on it");
        }

        [Test]
        public void Settle_AContractWithNothingLeft_IsClosedWithoutMoney()
        {
            var rig = new RecoveryRig();
            rig.State.investorShare = new InvestorShareData { active = true, amount = 1000000.0, share = 0.3, monthsLeft = 10, paid = 3000000.0, cap = 3000000.0 };

            SettleResult result = Engine(rig).Settle();

            Assert.Equal(SettleStatus.Done, result.Status);
            Assert.True(result.InvestorClosed);
            Assert.Near(0.0, result.Converted, 0);
            Assert.False(rig.State.investorShare.active);
            Assert.Equal(0, rig.Finance.Added.Count, "nothing to take");
            Assert.Equal(0, rig.Entries("disable.convert").Count, "no record of a conversion of 0");
        }

        [Test]
        public void Settle_AnOpenLineWithoutDebt_StaysOpen()
        {
            var rig = new RecoveryRig();
            rig.State.creditLine = new CreditLineData { state = CreditLineData.StateOpen, opensOn = "1975-01-01", closesOn = "1978-01-01", drawn = 0.0 };

            SettleResult result = Engine(rig).Settle();

            Assert.Equal(SettleStatus.NothingToSettle, result.Status);
            Assert.Equal(CreditLineData.StateOpen, rig.State.creditLine.state, "a line that was never used is not taken away by the switch");
            Assert.Equal(0, rig.Finance.Added.Count);
        }

        [Test]
        public void Settle_TheLineThatWasClosedBefore_KeepsItsReason()
        {
            RecoveryRig rig = SettlementKit.OneBridgeRig();
            rig.State.creditLine = new CreditLineData { state = CreditLineData.StateClosed, opensOn = "1975-01-01", closesOn = "1978-01-01", closedReason = CreditLineData.ReasonExpired };

            Engine(rig).Settle();

            Assert.Equal(CreditLineData.ReasonExpired, rig.State.creditLine.closedReason, "the line closed by its term is not rewritten");
        }

        // ---- when the game is not there ----

        [Test]
        public void Settle_WithoutThePlayersCompany_ChangesNothing()
        {
            RecoveryRig rig = SettlementKit.PopulatedRig();
            var flags = new FakeSettlementFlags();
            string before = SettlementKit.Describe(rig.State);
            rig.Finance.Available = false;

            SettleResult result = Engine(rig, flags).Settle();

            Assert.Equal(SettleStatus.Unavailable, result.Status);
            Assert.Equal(before, SettlementKit.Describe(rig.State), "the obligations stay as they are; nothing is cleared without being charged");
            Assert.Equal(0, rig.Finance.Added.Count);
            Assert.Equal(0, flags.Values.Count, "no marks for a settlement that did not happen");
        }

        [Test]
        public void Settle_WithoutTheLedger_ChangesNothing()
        {
            RecoveryRig rig = SettlementKit.PopulatedRig();
            string before = SettlementKit.Describe(rig.State);
            rig.Money.Ledger = false;

            SettleResult result = Engine(rig).Settle();

            Assert.Equal(SettleStatus.Unavailable, result.Status);
            Assert.Equal(before, SettlementKit.Describe(rig.State));
            Assert.Near(200000000.0, rig.Finance.MoneyValue, 0);
        }

        [Test]
        public void Settle_WithNothingOwedAndNoCompany_StillKeepsTheCountersInTheMarks()
        {
            var rig = new RecoveryRig();
            rig.State.bailoutsUsed = 2;
            rig.State.lastBridgeClosedOn = "1976-01-01";
            rig.Finance.Available = false;
            var flags = new FakeSettlementFlags();

            SettleResult result = Engine(rig, flags).Settle();

            Assert.Equal(SettleStatus.NothingToSettle, result.Status);
            Assert.Equal(2, flags.GetInt(SettlementMarks.Bailouts), "the counter of the rescues outlives the switch");
            Assert.Equal(SettlementMarks.DayNumber("1976-01-01"), flags.GetInt(SettlementMarks.BridgeDay));
        }

        // ---- the marks ----

        [Test]
        public void Settle_WritesTheMarks()
        {
            RecoveryRig rig = SettlementKit.PopulatedRig();
            var flags = new FakeSettlementFlags();

            Engine(rig, flags).Settle();

            Assert.Equal(1, flags.GetInt(SettlementMarks.Settled));
            Assert.Equal(3, flags.GetInt(SettlementMarks.LoanSeq), "loans with a number below 3 were settled");
            Assert.Equal(1, flags.GetInt(SettlementMarks.Bailouts));
            Assert.Equal(1, flags.GetInt(SettlementMarks.LineClosed));
            Assert.Equal(SettlementMarks.DayNumber("1977-06-01"), flags.GetInt(SettlementMarks.BridgeDay), "the bridge loan was closed today");
        }

        // ---- after the switch ----

        [Test]
        public void AfterTheSwitchOff_TheLineDoesNotOpenAgain_AndTheRescuesAreNotGivenBack()
        {
            RecoveryRig rig = SettlementKit.PopulatedRig();
            Engine(rig).Settle();

            var line = new CreditLineEngine(rig.Env, new LoanBook(rig.Env));
            bool opened = line.EnsureOpened();

            Assert.False(opened, "AC-12.6: the line does not open again");
            Assert.Equal(CreditLineData.StateClosed, rig.State.creditLine.state);
            Assert.Equal(1, rig.State.bailoutsUsed, "the counter of the rescues is the same as before the switch");
        }

        [Test]
        public void ANewLoanAfterTheSwitch_IsSettledOnItsOwn()
        {
            RecoveryRig rig = SettlementKit.PopulatedRig();
            var flags = new FakeSettlementFlags();
            DisableSettlementEngine engine = Engine(rig, flags);
            engine.Settle();
            var loans = new LoanBook(rig.Env);
            LoanData fresh = loans.Open(LoanData.KindTerm, 3000000.0, 0.15, 24);
            Assert.Equal("term-3", fresh.id, "the numbering goes on: the ids of the settled loans are not reused");
            double before = rig.Finance.MoneyValue;

            SettleResult result = engine.Settle();

            Assert.Equal(SettleStatus.Done, result.Status);
            Assert.Near(3000000.0, result.Converted, 0, "only the new loan is charged");
            Assert.Near(before - 3000000.0, rig.Finance.MoneyValue, 0);
            Assert.Equal(4, flags.GetInt(SettlementMarks.LoanSeq), "the mark grows with it");
        }

        [Test]
        public void ADamagedLoan_CountsAsNothing_AndIsStillClosed()
        {
            RecoveryRig rig = SettlementKit.OneBridgeRig();
            rig.State.loans[0].outstanding = double.NaN;

            SettleResult result = Engine(rig).Settle();

            Assert.Equal(SettleStatus.Done, result.Status);
            Assert.Near(0.0, result.Converted, 0, "a number that is not one is no debt");
            Assert.Equal(0, rig.State.loans.Count);
            Assert.Equal(0, rig.Finance.Added.Count);
        }

        [Test]
        public void TheEngine_RefusesNothingButMissingParts()
        {
            var rig = new RecoveryRig();
            Assert.Throws<ArgumentNullException>(() => new DisableSettlementEngine(null, new FakeSettlementFlags()));
            Assert.Throws<ArgumentNullException>(() => new DisableSettlementEngine(rig.Env, null));
        }
    }
}
