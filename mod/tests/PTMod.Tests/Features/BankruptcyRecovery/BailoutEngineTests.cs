using System;
using System.Collections.Generic;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-011 (FR-8, FR-11, NFR-2): the "last chance". The counter of the default is held only when the offer is, or will be, shown; the offer opens once, waits
    /// for the end of a batch run, comes back when its window was closed with the cross, and is not made again after a refusal or a window that failed. Accepting
    /// credits <c>B = 0.25 D - balance</c>, registers the rescue loan or the investor contract, counts the offer, lowers the reputation and cancels the default
    /// with the game's method. The investor takes a share of the profit of the last month every first of the month. The numbers are the 1977 examples
    /// (AC-8.1, AC-8.2).
    /// </summary>
    [Covers(typeof(BailoutEngine), typeof(BailoutOffer), typeof(BailoutChoice), typeof(IBailoutPresenter))]
    public class BailoutEngineTests
    {
        private sealed class FakePresenter : IBailoutPresenter
        {
            public int Opened;
            public BailoutOffer Last;
            public bool Result = true;
            public Exception Throws;

            public bool Open(BailoutOffer offer)
            {
                Opened++;
                Last = offer;
                if (Throws != null) throw Throws;
                return Result;
            }
        }

        private sealed class Setup
        {
            public readonly RecoveryRig Rig = new RecoveryRig();
            public readonly LoanBook Loans;
            public readonly BailoutEngine Bailout;
            public readonly FakePresenter Presenter = new FakePresenter();
            public readonly List<KeyValuePair<double, string>> Reputation = new List<KeyValuePair<double, string>>();
            public readonly List<BailoutChoice> Accepted = new List<BailoutChoice>();
            public int InvestorEnded;
            public bool Batch;
            public Exception ReputationThrows;

            // 1977, D = 24.3 million, balance -30 million, day 119 of a default, normal difficulty, no offers used (AC-8.1).
            public Setup()
            {
                Rig.Finance.TodayValue = new DateTime(1977, 5, 1);
                Rig.Finance.DebtLimit = 24300000.0;
                Rig.Finance.MoneyValue = -30000000.0;
                Rig.Finance.InDefault = true;
                Rig.Finance.Days = 119;
                Loans = new LoanBook(Rig.Env);
                Bailout = new BailoutEngine(Rig.Env, Loans, () => Batch, (delta, reason) =>
                {
                    if (ReputationThrows != null) throw ReputationThrows;
                    Reputation.Add(new KeyValuePair<double, string>(delta, reason));
                });
                Bailout.Presenter = Presenter;
                Bailout.Accepted += Accepted.Add;
                Bailout.InvestorEnded += () => InvestorEnded++;
            }

            public FakePlayerFinance Finance
            {
                get { return Rig.Finance; }
            }

            public RecoveryState State
            {
                get { return Rig.State; }
            }
        }

        // ---- the offer ----

        [Test]
        public void BuildOffer_The1977Example_AC_8_1()
        {
            var s = new Setup();

            BailoutOffer offer = s.Bailout.BuildOffer();

            Assert.NotNull(offer);
            Assert.Near(36075000.0, offer.Amount, 1e-6);
            Assert.Near(6075000.0, offer.BalanceAfter, 1e-6, "the balance after is +0.25 x D");
            Assert.Equal(0, offer.OffersUsed);
            Assert.Equal(1, offer.OffersAllowed);
            Assert.Equal(36, offer.LoanMonths);
            Assert.Near(0.40, offer.LoanAnnualRate, 0);
            Assert.Near(0.40, offer.LoanEffectiveRate, 1e-12);
            Assert.Near(1735574.0, offer.LoanPayment, 1.0, "AC-8.2: about 1 735 600 a month");
            Assert.True(offer.InvestorAvailable);
            Assert.Near(0.30, offer.InvestorShare, 0);
            Assert.Equal(60, offer.InvestorMonths);
            Assert.Near(108225000.0, offer.InvestorCap, 1e-6, "3 x B");
        }

        [Test]
        public void BuildOffer_TheLoanRateFollowsTheMultiplier()
        {
            var s = new Setup();
            s.Finance.Multiplier = 0.5;
            BailoutOffer offer = s.Bailout.BuildOffer();
            Assert.Near(0.40, offer.LoanAnnualRate, 0);
            Assert.Near(0.20, offer.LoanEffectiveRate, 1e-12);
            Assert.Near(LoanMath.AnnuityPayment(36075000.0, 0.20, 36), offer.LoanPayment, 1e-6);
        }

        [Test]
        public void BuildOffer_IsNullWhenThereIsNothingToOfferOrNoCompany()
        {
            var s = new Setup();
            s.Finance.MoneyValue = 6075000.0;   // already at the target
            Assert.Null(s.Bailout.BuildOffer());

            s.Finance.MoneyValue = -30000000.0;
            s.Finance.Available = false;
            Assert.Null(s.Bailout.BuildOffer());
        }

        [Test]
        public void BuildOffer_TheInvestorWayIsGoneWhileAContractRuns()
        {
            var s = new Setup();
            s.State.investorShare.active = true;
            Assert.False(s.Bailout.BuildOffer().InvestorAvailable);
        }

        [Test]
        public void OffersAllowed_FollowsTheDifficulty()
        {
            var s = new Setup();
            int[] expected = { 2, 1, 1, 1, 1, 0 };
            for (int level = 0; level < expected.Length; level++)
            {
                s.Finance.DifficultyLevel = level;
                Assert.Equal(expected[level], s.Bailout.OffersAllowed, "difficulty " + level);
            }
        }

        // ---- holding the counter ----

        [Test]
        public void TryHold_OpensTheOfferOnce()
        {
            var s = new Setup();

            Assert.True(s.Bailout.TryHold());
            Assert.True(s.Bailout.IsOfferOpen);
            Assert.Equal(1, s.Presenter.Opened);
            Assert.Near(36075000.0, s.Presenter.Last.Amount, 1e-6);

            Assert.True(s.Bailout.TryHold(), "every call while the window is open holds the counter");
            Assert.True(s.Bailout.TryHold());
            Assert.Equal(1, s.Presenter.Opened, "and the window is not opened again");
        }

        [Test]
        public void TryHold_WithoutAWindow_HoldsNothing()
        {
            var s = new Setup();
            s.Bailout.Presenter = null;

            Assert.False(s.Bailout.TryHold(), "no window: the game's own bankruptcy goes on");
            Assert.False(s.Bailout.IsOfferOpen);
            Assert.False(s.Bailout.IsSuppressed);
        }

        [Test]
        public void TryHold_AWindowThatDoesNotOpen_HoldsNothing_AndIsNotTriedAgainInTheEpisode()
        {
            var s = new Setup();
            s.Presenter.Result = false;

            Assert.False(s.Bailout.TryHold());
            Assert.True(s.Bailout.IsSuppressed);
            Assert.False(s.Bailout.IsOfferOpen);

            s.Presenter.Result = true;
            Assert.False(s.Bailout.TryHold(), "the window failed in this episode: the bankruptcy is the game's");
            Assert.Equal(1, s.Presenter.Opened);

            s.Bailout.EpisodeEnded();
            Assert.False(s.Bailout.IsSuppressed);
            Assert.True(s.Bailout.TryHold(), "a new episode, a new try");
            Assert.Equal(2, s.Presenter.Opened);
        }

        [Test]
        public void TryHold_AWindowThatThrows_IsReportedAndHoldsNothing()
        {
            var s = new Setup();
            s.Presenter.Throws = new InvalidOperationException("no template");

            Assert.False(s.Bailout.TryHold());

            Assert.True(s.Bailout.IsSuppressed);
            Assert.Equal(1, s.Rig.Warnings.Count);
            Assert.True(s.Rig.Warnings[0].Contains("no template"), s.Rig.Warnings[0]);
        }

        [Test]
        public void TryHold_WhenNothingCanBeOffered_HoldsNothing()
        {
            var s = new Setup();
            s.Finance.MoneyValue = 10000000.0;
            Assert.False(s.Bailout.TryHold());
            Assert.Equal(0, s.Presenter.Opened);
            Assert.False(s.Bailout.IsSuppressed, "nothing failed");
        }

        [Test]
        public void TryHold_InABatchRun_HoldsAndWaitsForTheEndOfTheBatch()
        {
            var s = new Setup();
            s.Batch = true;

            Assert.True(s.Bailout.TryHold(), "FR-8 item 7: the counter is held in a batch run");
            Assert.True(s.Bailout.TryHold());
            Assert.Equal(0, s.Presenter.Opened, "the window waits for the end of the batch");
            Assert.False(s.Bailout.IsOfferOpen);

            s.Batch = false;
            s.Bailout.OnBatchEnded();

            Assert.Equal(1, s.Presenter.Opened);
            Assert.True(s.Bailout.IsOfferOpen);

            s.Bailout.OnBatchEnded();
            Assert.Equal(1, s.Presenter.Opened, "the wait is over: nothing more to open");
        }

        [Test]
        public void OnBatchEnded_DoesNotOpenAnOfferThatIsNoLongerNeeded()
        {
            var s = new Setup();
            s.Batch = true;
            Assert.True(s.Bailout.TryHold());

            s.Batch = false;
            s.Finance.MoneyValue = 5000000.0;   // the company got out of the hole during the batch
            s.Bailout.OnBatchEnded();

            Assert.Equal(0, s.Presenter.Opened);

            // each reason on its own: declined, the part off, the offers used up, no longer in default
            foreach (int reason in new[] { 0, 1, 2, 3 })
            {
                var t = new Setup();
                t.Batch = true;
                Assert.True(t.Bailout.TryHold());
                t.Batch = false;
                if (reason == 0) t.State.episode.bailoutDeclined = true;
                if (reason == 1) t.Rig.Off.Add(SubFeature.Bailout);
                if (reason == 2) t.State.bailoutsUsed = 1;
                if (reason == 3) t.Finance.InDefault = false;
                t.Bailout.OnBatchEnded();
                Assert.Equal(0, t.Presenter.Opened, "reason " + reason);
            }
        }

        [Test]
        public void OnBatchEnded_WithoutAWait_DoesNothing()
        {
            var s = new Setup();
            s.Bailout.OnBatchEnded();
            Assert.Equal(0, s.Presenter.Opened);
        }

        [Test]
        public void TheWindowClosedWithTheCross_IsNotARefusal_TheOfferComesBack()
        {
            var s = new Setup();
            Assert.True(s.Bailout.TryHold());

            s.Bailout.OfferDismissed();

            Assert.False(s.Bailout.IsOfferOpen);
            Assert.False(s.State.episode.bailoutDeclined);
            Assert.True(s.Bailout.TryHold());
            Assert.Equal(2, s.Presenter.Opened);
        }

        // ---- accepting ----

        [Test]
        public void Accept_RescueLoan_AC_8_2()
        {
            var s = new Setup();
            s.Bailout.TryHold();

            bool done = s.Bailout.Accept(BailoutChoice.RescueLoan);

            Assert.True(done);
            Assert.Near(6075000.0, s.Finance.MoneyValue, 1e-6, "AC-8.2: the balance is +6 075 000");
            Assert.Equal(1, s.Finance.CancelCalls, "the game's own cancel of the default");
            Assert.False(s.Finance.InDefault);
            Assert.Equal(1, s.State.bailoutsUsed);
            Assert.False(s.Bailout.IsOfferOpen);

            Assert.Equal(1, s.Loans.Loans.Count);
            LoanData loan = s.Loans.Loans[0];
            Assert.Equal("rescue-1", loan.id);
            Assert.Equal(LoanData.KindRescueLoan, loan.kind);
            Assert.Near(36075000.0, loan.principal, 1e-6);
            Assert.Equal(36, loan.monthsTotal);
            Assert.Near(0.40, loan.annualRate, 0);
            Assert.Near(1735574.0, loan.payment, 1.0);

            Assert.Equal(1, s.Reputation.Count);
            Assert.Near(-10.0, s.Reputation[0].Key, 0);
            Assert.Equal("rec.reputation.bailout", s.Reputation[0].Value);
            Assert.Equal(1, s.Accepted.Count);
            Assert.Equal(BailoutChoice.RescueLoan, s.Accepted[0]);

            Assert.Equal(1, s.Rig.Entries("bailout.loan").Count);
            Assert.Near(36075000.0, s.Rig.Entries("bailout.loan")[0].amount, 1e-6);
            Assert.False(s.State.investorShare.active);
            Assert.Equal(0, s.Finance.Earned.Count + s.Finance.Spent.Count, "a capital operation: nothing in the report");
        }

        [Test]
        public void Accept_InvestorShare()
        {
            var s = new Setup();
            s.Bailout.TryHold();

            Assert.True(s.Bailout.Accept(BailoutChoice.InvestorShare));

            Assert.Near(6075000.0, s.Finance.MoneyValue, 1e-6);
            InvestorShareData contract = s.State.investorShare;
            Assert.True(contract.active);
            Assert.Near(36075000.0, contract.amount, 1e-6);
            Assert.Near(0.30, contract.share, 0);
            Assert.Equal(60, contract.monthsLeft);
            Assert.Near(0.0, contract.paid, 0);
            Assert.Near(108225000.0, contract.cap, 1e-6, "AC-8 example: the ceiling is about 108.2 million");
            Assert.Equal(0, s.Loans.Loans.Count, "no loan in this way");
            Assert.Equal(1, s.State.bailoutsUsed);
            Assert.Equal(1, s.Finance.CancelCalls);
            Assert.Equal(-10.0, s.Reputation[0].Key);
            Assert.Equal(BailoutChoice.InvestorShare, s.Accepted[0]);
            Assert.Equal(1, s.Rig.Entries("bailout.investor").Count);
        }

        [Test]
        public void Accept_AnInvestorContractIsNotOfferedTwice()
        {
            var s = new Setup();
            s.State.investorShare.active = true;
            s.State.investorShare.cap = 50.0;

            Assert.False(s.Bailout.Accept(BailoutChoice.InvestorShare));

            Assert.Near(-30000000.0, s.Finance.MoneyValue, 0);
            Assert.Near(50.0, s.State.investorShare.cap, 0);
            Assert.Equal(0, s.State.bailoutsUsed);
            Assert.Equal(0, s.Finance.CancelCalls);

            Assert.True(s.Bailout.Accept(BailoutChoice.RescueLoan), "the loan is still possible");
        }

        [Test]
        public void Accept_ClearsTheRefusalAndTheStreak_ForTheNextEpisode()
        {
            var s = new Setup();
            s.State.episode.bailoutDeclined = true;
            s.State.episode.recoveryStreak = 3;

            Assert.True(s.Bailout.Accept(BailoutChoice.RescueLoan));

            Assert.False(s.State.episode.bailoutDeclined);
            Assert.Equal(0, s.State.episode.recoveryStreak);
        }

        [Test]
        public void Accept_DoesNothing_WhenItCannot()
        {
            var s = new Setup();

            s.Finance.Available = false;
            Assert.False(s.Bailout.Accept(BailoutChoice.RescueLoan));
            s.Finance.Available = true;

            s.Finance.Bankrupt = true;
            Assert.False(s.Bailout.Accept(BailoutChoice.RescueLoan));
            s.Finance.Bankrupt = false;

            s.Rig.Money.Ledger = false;
            Assert.False(s.Bailout.Accept(BailoutChoice.RescueLoan));
            s.Rig.Money.Ledger = true;

            Assert.Near(-30000000.0, s.Finance.MoneyValue, 0);
            Assert.Equal(0, s.State.bailoutsUsed);
            Assert.Equal(0, s.Loans.Loans.Count);
            Assert.Equal(0, s.Finance.CancelCalls);
            Assert.Equal(0, s.Reputation.Count);
            Assert.Equal(0, s.Accepted.Count);
            Assert.Equal(0, s.State.journal.Count);
        }

        [Test]
        public void Accept_WhenThereIsNothingToOffer_ClosesTheWindowAndDoesNothing()
        {
            var s = new Setup();
            s.Bailout.TryHold();
            s.Finance.MoneyValue = 7000000.0;

            Assert.False(s.Bailout.Accept(BailoutChoice.RescueLoan));

            Assert.False(s.Bailout.IsOfferOpen);
            Assert.Equal(0, s.State.bailoutsUsed);
            Assert.Equal(0, s.State.loans.Count);
        }

        [Test]
        public void Accept_AFailingCancelOrReputation_DoesNotUndoTheRescue()
        {
            var s = new Setup();
            s.Finance.CancelThrows = new InvalidOperationException("no CancelDefault");
            s.ReputationThrows = new InvalidOperationException("no reputation");

            Assert.True(s.Bailout.Accept(BailoutChoice.RescueLoan));

            Assert.Near(6075000.0, s.Finance.MoneyValue, 1e-6);
            Assert.Equal(1, s.State.bailoutsUsed);
            Assert.Equal(1, s.Loans.Loans.Count);
            Assert.Equal(1, s.Accepted.Count, "the event is still raised");
            Assert.Equal(2, s.Rig.Warnings.Count);
            Assert.True(s.Rig.Warnings[0].Contains("reputation"), s.Rig.Warnings[0]);
            Assert.True(s.Rig.Warnings[1].Contains("cancel"), s.Rig.Warnings[1]);
        }

        [Test]
        public void Accept_WithoutAReputationHandler_JustWorks()
        {
            var s = new Setup();
            var bailout = new BailoutEngine(s.Rig.Env, s.Loans);
            Assert.True(bailout.Accept(BailoutChoice.RescueLoan));
            Assert.Equal(1, s.State.bailoutsUsed);
        }

        // ---- declining ----

        [Test]
        public void Decline_RemembersTheRefusal_AndClosesTheOffer()
        {
            var s = new Setup();
            s.Bailout.TryHold();

            s.Bailout.Decline();

            Assert.True(s.State.episode.bailoutDeclined);
            Assert.False(s.Bailout.IsOfferOpen);
            Assert.Near(-30000000.0, s.Finance.MoneyValue, 0, "no money");
            Assert.Equal(0, s.State.bailoutsUsed);
            Assert.Equal(0, s.Finance.CancelCalls);
        }

        // ---- the investor ----

        private static void Contract(Setup s, double paid = 0.0, int monthsLeft = 60)
        {
            s.State.investorShare = new InvestorShareData
            {
                active = true,
                amount = 36075000.0,
                share = 0.30,
                monthsLeft = monthsLeft,
                paid = paid,
                cap = 108225000.0,
            };
            s.Finance.TodayValue = new DateTime(1977, 3, 1);
            s.Finance.InDefault = false;
            s.Finance.MoneyValue = 5000000.0;
        }

        [Test]
        public void OnMonth_TheInvestorTakesAShareOfTheProfitOfTheLastMonth()
        {
            var s = new Setup();
            Contract(s);
            s.Finance.SetProfit(1977, 2, 4000000.0);
            s.Finance.SetProfit(1977, 3, 99000000.0);   // the month in progress is not the last month

            s.Bailout.OnMonth();

            Assert.Near(1200000.0, s.State.investorShare.paid, 1e-6, "30 % of 4 000 000");
            Assert.Equal(59, s.State.investorShare.monthsLeft);
            Assert.True(s.State.investorShare.active);
            Assert.Equal(1, s.Finance.Spent.Count, "an expense of the report");
            Assert.Near(1200000.0, s.Finance.Spent[0].Key, 1e-6);
            Assert.Equal(FakePlayerFinance.RoyaltiesItem, s.Finance.Spent[0].Value, "the item of the owner's decision: Royalties");
            Assert.Near(5000000.0 - 1200000.0, s.Finance.MoneyValue, 1e-6);
            Assert.Equal(1, s.Rig.Entries("investor.payment").Count);
            Assert.Near(-1200000.0, s.Rig.Entries("investor.payment")[0].amount, 1e-6);
            Assert.Equal("1977-02", s.Rig.Entries("investor.payment")[0].details);
        }

        [Test]
        public void OnMonth_TheItemComesFromTheBalanceFile()
        {
            var s = new Setup();
            Contract(s);
            s.Rig.Balance.bailout.investor.expenseOrigin = "Interest";
            s.Finance.SetProfit(1977, 2, 4000000.0);

            s.Bailout.OnMonth();

            Assert.Equal(FakePlayerFinance.InterestItem, s.Finance.Spent[0].Value);
        }

        [Test]
        public void OnMonth_ALossPaysNothing_AndTheTermGoesOn()
        {
            var s = new Setup();
            Contract(s);
            s.Finance.SetProfit(1977, 2, -2000000.0);

            s.Bailout.OnMonth();

            Assert.Near(0.0, s.State.investorShare.paid, 0);
            Assert.Equal(59, s.State.investorShare.monthsLeft, "edge case 7: the term goes on all the same");
            Assert.Equal(0, s.Finance.Spent.Count);

            s.Finance.TodayValue = new DateTime(1977, 4, 1);   // no report at all for March: 0
            s.Bailout.OnMonth();
            Assert.Equal(58, s.State.investorShare.monthsLeft);
            Assert.Equal(0, s.Finance.Spent.Count);
        }

        [Test]
        public void OnMonth_NeverPaysMoreThanTheCeiling_AndEndsThere()
        {
            var s = new Setup();
            Contract(s, 107000000.0);   // 1 225 000 are left of 108 225 000
            s.Finance.SetProfit(1977, 2, 10000000.0);   // 30 % would be 3 000 000

            s.Bailout.OnMonth();

            Assert.Near(1225000.0, s.Finance.Spent[0].Key, 1e-6);
            Assert.Near(108225000.0, s.State.investorShare.paid, 1e-6);
            Assert.False(s.State.investorShare.active);
            Assert.Equal(1, s.InvestorEnded);
        }

        [Test]
        public void OnMonth_EndsWithTheLastMonthOfTheTerm()
        {
            var s = new Setup();
            Contract(s, 0.0, 1);
            s.Finance.SetProfit(1977, 2, 1000000.0);

            s.Bailout.OnMonth();

            Assert.Near(300000.0, s.Finance.Spent[0].Key, 1e-6, "the last payment is still made");
            Assert.False(s.State.investorShare.active);
            Assert.Equal(0, s.State.investorShare.monthsLeft);
            Assert.Equal(1, s.InvestorEnded);

            s.Bailout.OnMonth();
            Assert.Equal(1, s.Finance.Spent.Count, "an ended contract takes no more");
            Assert.Equal(1, s.InvestorEnded);
        }

        [Test]
        public void OnMonth_WithoutAContract_OrWithoutTheCompany_DoesNothing()
        {
            var s = new Setup();
            s.Bailout.OnMonth();
            Assert.Equal(0, s.Finance.Spent.Count);

            Contract(s);
            s.Finance.SetProfit(1977, 2, 4000000.0);
            s.Finance.Available = false;
            s.Bailout.OnMonth();
            s.Finance.Available = true;
            s.Rig.Money.Ledger = false;
            s.Bailout.OnMonth();

            Assert.Equal(0, s.Finance.Spent.Count);
            Assert.Equal(60, s.State.investorShare.monthsLeft);
        }

        // ---- the session ----

        [Test]
        public void ResetSession_ForgetsWhatBelongsToTheSession()
        {
            var s = new Setup();
            s.Presenter.Result = false;
            s.Bailout.TryHold();
            Assert.True(s.Bailout.IsSuppressed);

            s.Bailout.ResetSession();

            Assert.False(s.Bailout.IsSuppressed);
            Assert.False(s.Bailout.IsOfferOpen);
            s.Presenter.Result = true;
            Assert.True(s.Bailout.TryHold());
            Assert.True(s.Bailout.IsOfferOpen);

            s.Bailout.ResetSession();
            Assert.False(s.Bailout.IsOfferOpen, "a load or a new game: the window flag is not saved");
        }

        [Test]
        public void Constructor_RefusesMissingParts()
        {
            var rig = new RecoveryRig();
            Assert.Throws<ArgumentNullException>(() => new BailoutEngine(null, new LoanBook(rig.Env)));
            Assert.Throws<ArgumentNullException>(() => new BailoutEngine(rig.Env, null));
        }
    }
}
