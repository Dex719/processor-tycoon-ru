using System;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The numbers of the attitude's share in a negotiation with the player (FR-8): the divisors of the bonus to the interest and of the markup
    /// discount, and the largest discount. They are <c>attitude.negotiationDivisor</c>, <c>attitude.discountDivisor</c> and
    /// <c>attitude.discountMaxPoints</c> of <c>config.json</c> (5, 10, 10).
    /// </summary>
    public sealed class NegotiationNumbers
    {
        public NegotiationNumbers(int interestDivisor, int discountDivisor, int discountMaxPoints)
        {
            InterestDivisor = interestDivisor;
            DiscountDivisor = discountDivisor;
            DiscountMaxPoints = discountMaxPoints;
        }

        /// <summary>The attitude is divided by this to get the bonus to the interest of the AI side (5: +40 gives +8).</summary>
        public int InterestDivisor { get; private set; }

        /// <summary>The attitude is divided by this to get the markup discount of a foundry partner (10: +40 gives 4 points).</summary>
        public int DiscountDivisor { get; private set; }

        /// <summary>The largest markup discount, in points (10).</summary>
        public int DiscountMaxPoints { get; private set; }

        /// <summary>The numbers of the design: 5, 10 and 10.</summary>
        public static NegotiationNumbers Default
        {
            get { return new NegotiationNumbers(5, 10, 10); }
        }

        /// <summary>The numbers of a config; the design's numbers for a config that is missing.</summary>
        public static NegotiationNumbers From(RivalConfig config)
        {
            if (config == null || config.attitude == null) return Default;
            return new NegotiationNumbers(config.attitude.negotiationDivisor, config.attitude.discountDivisor, config.attitude.discountMaxPoints);
        }
    }

    /// <summary>What the attitude adds to the scores of the AI side of a negotiation: points of <c>Interest</c> and points of <c>Price</c>.</summary>
    public struct NegotiationBonus
    {
        public NegotiationBonus(int interest, int price)
        {
            Interest = interest;
            Price = price;
        }

        /// <summary>Added to the <c>Interest</c> of the AI side (-20..+20), unless the game vetoed the deal.</summary>
        public int Interest;

        /// <summary>Added to the <c>Price</c> of the AI provider (0..10): the markup discount of a foundry partner.</summary>
        public int Price;

        public bool IsZero
        {
            get { return Interest == 0 && Price == 0; }
        }
    }

    /// <summary>
    /// The arithmetic of FR-8 (formulas 5 and 8 of design.md): the AI side of a business-contract negotiation with the player gets
    /// <c>round(a / 5)</c> points of interest, and a foundry partner (an archetype with <c>negotiationDiscount</c>) gives <c>clamp(round(a / 10), 0, 10)</c>
    /// points of price. The game's veto (<c>Interest = -1000</c>) is never changed. Pure; the patches of <c>NegotiationPatches</c> only apply it.
    /// </summary>
    public static class NegotiationRules
    {
        /// <summary>The <c>Interest</c> the game gives to a side that cannot take the deal at all (a fabless provider, a client that is not fabless, difficulty 5).</summary>
        public const int Veto = -1000;

        /// <summary>The game forbids the deal on this side: the interest is the veto.</summary>
        public static bool IsVeto(int interest)
        {
            return interest <= Veto;
        }

        /// <summary>
        /// The bonus for the AI side. The interest bonus is for both roles; the price discount only for an AI provider whose archetype has
        /// <c>negotiationDiscount</c> (a hostile or neutral attitude gives none, and never a surcharge).
        /// </summary>
        public static NegotiationBonus Bonus(bool aiIsProvider, float attitude, bool hasDiscount, NegotiationNumbers numbers)
        {
            NegotiationNumbers n = numbers ?? NegotiationNumbers.Default;
            int interest = AttitudeMath.InterestBonus(attitude, n.InterestDivisor);
            int price = aiIsProvider && hasDiscount ? AttitudeMath.DiscountPoints(attitude, n.DiscountDivisor, n.DiscountMaxPoints) : 0;
            return new NegotiationBonus(interest, price);
        }

        /// <summary>The interest after the bonus: the veto stays as it is, anything else gets the bonus.</summary>
        public static int InterestAfter(int interest, int bonus)
        {
            return IsVeto(interest) ? interest : interest + bonus;
        }

        /// <summary>The score of the AI side after the bonus (the sum the game compares with 0 is the same as the game's, plus the bonus).</summary>
        public static void Apply(NegotiationBonus bonus, ref int interest, ref int price)
        {
            interest = InterestAfter(interest, bonus.Interest);
            price += bonus.Price;
        }

        /// <summary>A bonus as the panel writes it: <c>+8</c>, <c>-6</c>, <c>0</c>.</summary>
        public static string Signed(int value)
        {
            if (value > 0) return "+" + value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
