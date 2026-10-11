using System;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-015 (FR-8 item 2, AC-8.1, AC-8.2): what the window of the "last chance" offer says. The numbers are the offer the engine builds for the 1977 example
    /// (<c>D = 24.3 million</c>, balance -30 million): the sum 36 075 000, the balance after +6 075 000, the rescue loan for 36 months at 40 percent, the investor share of 30 percent
    /// for 60 months, at most three times the sum.
    /// </summary>
    [Covers(typeof(OfferTerms), typeof(OfferTexts))]
    public class OfferTermsTests
    {
        private static readonly IMenuText Text = new FakeMenuText();

        private static BailoutOffer Offer(Action<RecoveryRig> arrange = null)
        {
            var rig = new RecoveryRig();
            rig.Finance.TodayValue = new DateTime(1977, 5, 1);
            rig.Finance.DebtLimit = 24300000.0;
            rig.Finance.MoneyValue = -30000000.0;
            rig.Finance.InDefault = true;
            rig.Finance.Days = 119;
            if (arrange != null) arrange(rig);
            var engine = new BailoutEngine(rig.Env, new LoanBook(rig.Env));
            BailoutOffer offer = engine.BuildOffer();
            Assert.NotNull(offer, "the example has an offer");
            return offer;
        }

        [Test]
        public void The1977Example_IsWrittenWithItsSum_ItsBalanceAfter_AndBothWays()
        {
            OfferTexts texts = OfferTerms.Describe(Text, Offer(), -10.0);

            Assert.Equal("rec.bailout.amount($36075000)", texts.Amount);
            Assert.Equal("rec.confirm.after($6075000.00)", texts.BalanceAfter);
            Assert.Equal("rec.bailout.used(0|1)", texts.Used);

            Assert.Equal("rec.bailout.loan($36075000|36|40)", texts.LoanTerms);
            double payment = LoanMath.AnnuityPayment(36075000.0, 0.40, 36);
            Assert.Equal("rec.bailout.loan.payment(" + Text.Money(payment) + ")", texts.LoanPayment);
            Assert.Near(1735574.0, payment, 1.0, "AC-8.2: about 1 735 600 a month");

            Assert.Equal("rec.bailout.investor(30|60|$108225000)", texts.InvestorTerms);
            Assert.Null(texts.InvestorUnavailable, "the investor way is open when no contract runs");
            Assert.Equal("rec.bailout.reputation(-10)", texts.Reputation);
        }

        [Test]
        public void TheRateShown_IsTheOneThePlayerPays()
        {
            // the channel bank.interest halves the rate: 40 percent of the file is 20 percent paid
            OfferTexts texts = OfferTerms.Describe(Text, Offer(rig => rig.Finance.Multiplier = 0.5), -10.0);

            Assert.Equal("rec.bailout.loan($36075000|36|20)", texts.LoanTerms);
        }

        [Test]
        public void ARunningInvestorContract_ClosesTheSecondWay_WithAReason()
        {
            OfferTexts texts = OfferTerms.Describe(Text, Offer(rig => rig.State.investorShare.active = true), -10.0);

            Assert.Equal("rec.bailout.investor.unavailable", texts.InvestorUnavailable);
            Assert.NotNull(texts.LoanTerms, "the loan stays on offer");
        }

        [Test]
        public void TheOffersUsed_AreCountedAgainstTheDifficulty()
        {
            // difficulty 0 allows two rescues; one is used
            OfferTexts texts = OfferTerms.Describe(Text, Offer(rig =>
            {
                rig.Finance.DifficultyLevel = 0;
                rig.State.bailoutsUsed = 1;
            }), -10.0);

            Assert.Equal("rec.bailout.used(1|2)", texts.Used);
        }

        [Test]
        public void TheReputation_IsShownAsAWholeNumber()
        {
            Assert.Equal("rec.bailout.reputation(-10)", OfferTerms.Describe(Text, Offer(), -10.4).Reputation);
            Assert.Equal("rec.bailout.reputation(0)", OfferTerms.Describe(Text, Offer(), 0.0).Reputation);
        }

        [Test]
        public void WithoutAnOffer_TheTextsAreEmpty_AndNothingThrows()
        {
            OfferTexts texts = OfferTerms.Describe(Text, null, -10.0);

            Assert.Null(texts.Amount);
            Assert.Null(texts.LoanTerms);
            Assert.Null(texts.InvestorTerms);
        }
    }
}
