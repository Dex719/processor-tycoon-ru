using System.Globalization;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>The texts of the window of the "last chance" offer (FR-8 item 2), all in the language of the player.</summary>
    internal sealed class OfferTexts
    {
        /// <summary>The sum the offer gives, <c>B</c>.</summary>
        public string Amount { get; set; }

        /// <summary>The balance after the offer is accepted.</summary>
        public string BalanceAfter { get; set; }

        /// <summary>How many offers were accepted so far and how many the difficulty allows.</summary>
        public string Used { get; set; }

        /// <summary>The terms of the rescue loan: the sum, the months, the rate.</summary>
        public string LoanTerms { get; set; }

        /// <summary>The monthly payment of the rescue loan.</summary>
        public string LoanPayment { get; set; }

        /// <summary>The terms of the investor share: the part of the profit, the months, the ceiling.</summary>
        public string InvestorTerms { get; set; }

        /// <summary>Why the investor share cannot be chosen; null when it can.</summary>
        public string InvestorUnavailable { get; set; }

        /// <summary>What accepting does to the reputation.</summary>
        public string Reputation { get; set; }
    }

    /// <summary>
    /// What the window of the "last chance" offer says (design.md "UI", key table): <c>rec.bailout.loan</c> and <c>rec.bailout.investor</c> with the numbers of the
    /// <see cref="BailoutOffer"/> the engine built. The rate shown is the one the player will pay (the rate of the file times the channel <c>bank.interest</c>).
    /// </summary>
    internal static class OfferTerms
    {
        public static OfferTexts Describe(IMenuText t, BailoutOffer offer, double reputationDelta)
        {
            var texts = new OfferTexts();
            if (offer == null) return texts;

            texts.Amount = t.L("rec.bailout.amount", t.Money(offer.Amount));
            texts.BalanceAfter = t.L("rec.confirm.after", t.MoneyExact(offer.BalanceAfter));
            texts.Used = t.L("rec.bailout.used", Num(offer.OffersUsed), Num(offer.OffersAllowed));
            texts.LoanTerms = t.L("rec.bailout.loan", t.Money(offer.Amount), Num(offer.LoanMonths), t.Percent(offer.LoanEffectiveRate));
            texts.LoanPayment = t.L("rec.bailout.loan.payment", t.Money(offer.LoanPayment));
            texts.InvestorTerms = t.L("rec.bailout.investor", t.Percent(offer.InvestorShare), Num(offer.InvestorMonths), t.Money(offer.InvestorCap));
            texts.InvestorUnavailable = offer.InvestorAvailable ? null : t.L("rec.bailout.investor.unavailable");
            texts.Reputation = t.L("rec.bailout.reputation", Num((int)System.Math.Round(reputationDelta)));
            return texts;
        }

        private static string Num(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
