using System;
using System.Globalization;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The terms of one deal as the window and the preview show them and the executor checks them (design.md, "DealQuote"). Money is in dollars,
    /// all <c>double</c>. When the deal is not available, <see cref="ReasonKey"/> says why (a <see cref="AcqKeys"/> key) and the price is still filled
    /// whenever the target is of the right kind, so the window can show the price next to the reason.
    /// </summary>
    public struct DealQuote
    {
        public DealType Type;

        public bool Available;

        /// <summary>A key of <see cref="AcqKeys"/> (<c>acq.reason.*</c>) when the deal is not available; null when it is.</summary>
        public string ReasonKey;

        /// <summary>The argument of the reason text ({0}); null when the reason has none.</summary>
        public string ReasonArg;

        /// <summary>The number behind <see cref="ReasonArg"/> when it is money (the shortfall), so the window can format it with the game's formatter.</summary>
        public double ReasonValue;

        public Valuation Valuation;

        /// <summary>The multiplier <c>m</c> of the deal that was used (0.4, 0.7, 1.5 or 1.75; for a stake <c>m_s</c>).</summary>
        public double Multiplier;

        /// <summary>The value of <c>acquisition.price</c> that was used (1 when the channel gave nothing usable).</summary>
        public double Channel;

        /// <summary>σ_p: the share of the target the player owns (0 to 0.49). A full purchase is cheaper by <c>1 - σ_p</c>.</summary>
        public double OwnedShare;

        /// <summary>P: the price, rounded up to <c>roundTo</c>. For a stake, the price of the smallest share the player can buy.</summary>
        public double Price;

        /// <summary>C that comes with the deal: the cash (positive) or the debt (negative) of the target; 0 when the deal does not take it over.</summary>
        public double AssumedCash;

        /// <summary><c>-P + AssumedCash</c>: what the deal does to the player's balance. Negative for every purchase that takes the target out.</summary>
        public double NetCash;

        /// <summary>The player's balance after the deal, <c>Money + NetCash</c>.</summary>
        public double MoneyAfter;

        /// <summary><c>C⁺ + E_min</c>: the lowest price of a deal that takes the cash over; 0 for the others.</summary>
        public double CashFloor;

        /// <summary>The price was raised to <see cref="CashFloor"/> (the formula gave less than the cash is worth): the preview says so.</summary>
        public bool CashFloorApplied;

        /// <summary>The change of the player's reputation after the deal, in points.</summary>
        public int ReputationDelta;
    }

    /// <summary>
    /// <c>DealRules</c> (FR-2…FR-6, design.md "Цены сделок"): whether a deal is open for a target and the player, and what it costs. Pure.
    /// <list type="bullet">
    /// <item><b>Assets of a bankrupt company</b> (auction open): <c>P = ceil(max(E_min, 0.4 × E) × Ch)</c>; its cash and debts stay with it.</item>
    /// <item><b>Company in default</b>: <c>P_f = max(E_min, 0.7 × (E + C⁺)) × Ch × (1 - σ_p)</c>, <c>P = ceil(max(P_f, C⁺ + E_min))</c>; the cash <c>C</c> (usually a debt) comes along.</item>
    /// <item><b>Hostile takeover</b> of a healthy company: the same with <c>1.5</c> (<c>1.75</c> when the target's share of the market is the larger), once a year.</item>
    /// <item><b>Stake</b> and <b>rescue order</b>: open for a company that is not bankrupt; the price of a stake is <c>StakeMath.StakePrice</c>.</item>
    /// </list>
    /// <b>The invariant</b> (design.md, "Инвариант"): for a company in default and for a hostile takeover <c>NetCash = C - P &lt; 0</c> for every <c>E</c>, <c>C</c>,
    /// <c>σ_p</c>, <c>Ch</c> and <c>D</c>. The price is never below <c>C⁺ + E_min</c> (at least one rounding step above the cash when <c>E_min</c> is 0), so a
    /// company cannot be bought for less than the cash it brings. The invariant is checked over a grid in the tests.
    /// <para>
    /// Order of the reasons a deal is refused: the player's own default, the status of the target, the cooldown, the money. Dates in reasons are
    /// <c>dd.MM.yyyy</c>.
    /// </para>
    /// </summary>
    public static class DealRules
    {
        public const string DateFormat = "dd.MM.yyyy";

        public static DealQuote Quote(DealType type, CompanySnapshot t, PlayerSnapshot p, AcquisitionsState s, AcquisitionsBalance b, double priceChannel)
        {
            var q = new DealQuote { Type = type, Channel = 1.0 };
            if (b == null) b = AcquisitionsBalance.Defaults();
            if (p == null) p = new PlayerSnapshot();
            if (t == null)
            {
                q.ReasonKey = AcqKeys.ReasonUnsupported;
                return q;
            }

            // A save of a newer mod: the books are empty by the core's rule and would not be saved, so nothing is offered (NFR-3).
            if (s != null && s.OffersClosed)
            {
                q.ReasonKey = AcqKeys.ReasonSaveNewer;
                return q;
            }

            double channel = IsUsableChannel(priceChannel) ? priceChannel : 1.0;
            q.Channel = channel;
            q.Valuation = ValuationMath.Evaluate(t, p, b.Valuation);
            q.OwnedShare = CompanyStatusRules.OwnedShare(s, t.UniqueId, t.SaveId);

            double cash = Finite(t.Money);
            double roundTo = b.Valuation.RoundTo;

            switch (type)
            {
                case DealType.BankruptAssets:
                    return Purchase(q, t, p, s, b, cash, roundTo, b.Deals.Bankrupt.Multiplier, b.Deals.Bankrupt.AssumeCash, false);

                case DealType.Distressed:
                    return Purchase(q, t, p, s, b, cash, roundTo, b.Deals.Distressed.Multiplier, b.Deals.Distressed.AssumeCash, true);

                case DealType.Hostile:
                {
                    double m = b.Deals.Hostile.Multiplier;
                    if (q.Valuation.Share > Clamp01(p.MarketShare)) m = b.Deals.Hostile.BiggerTargetMultiplier;
                    return Purchase(q, t, p, s, b, cash, roundTo, m, b.Deals.Hostile.AssumeCash, true);
                }

                case DealType.Stake:
                    return StakeQuote(q, t, p, b, cash, roundTo);

                case DealType.RescueOrder:
                    return RescueOrderQuote(q, t, p, b);

                default:
                    q.ReasonKey = AcqKeys.ReasonUnsupported;
                    return q;
            }
        }

        /// <summary>Rounds <paramref name="value"/> UP to a multiple of <paramref name="step"/> (a step below 1 leaves the value as it is). Never lowers a value.</summary>
        public static double RoundUp(double value, double step)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0.0;
            if (!(step > 1.0)) return value;
            return Math.Ceiling(value / step) * step;
        }

        // ---- the three purchases of a whole company ----

        private static DealQuote Purchase(DealQuote q, CompanySnapshot t, PlayerSnapshot p, AcquisitionsState s, AcquisitionsBalance b, double cash,
            double roundTo, double multiplier, bool assumeCash, bool discountForStake)
        {
            double cashPlus = cash > 0.0 ? cash : 0.0;
            double eMin = q.Valuation.EMin;

            // The base is the operating assets, plus the positive cash when the cash comes along (it is paid for then).
            double basis = q.Valuation.E + (assumeCash ? cashPlus : 0.0);
            double priceFormula = Math.Max(eMin, multiplier * basis) * q.Channel;
            if (discountForStake) priceFormula *= 1.0 - q.OwnedShare;

            double price = priceFormula;
            if (assumeCash)
            {
                // Never less than the cash, and at least one step more even when E_min is 0: no arbitrage, NetCash stays below 0.
                double minFee = Math.Max(eMin, roundTo > 1.0 ? roundTo : 1.0);
                q.CashFloor = cashPlus + minFee;
                if (price < q.CashFloor)
                {
                    price = q.CashFloor;
                    q.CashFloorApplied = true;
                }
            }

            q.Multiplier = multiplier;
            q.Price = RoundUp(price, roundTo);
            q.AssumedCash = assumeCash ? cash : 0.0;
            q.NetCash = -q.Price + q.AssumedCash;
            q.MoneyAfter = Finite(p.Money) + q.NetCash;
            q.ReputationDelta = ReputationOf(q.Type, b);

            // Is it open? The first reason that applies wins.
            string reason = null;
            string arg = null;
            double value = 0.0;

            if (p.InDefault || p.IsBankrupt)
            {
                reason = AcqKeys.ReasonPlayerDefault;
            }
            else
            {
                reason = StatusReason(q.Type, t, s, p, out arg);
                if (reason == null && q.Type == DealType.Hostile && CooldownDate(s, b, p.Today, out arg)) reason = AcqKeys.ReasonCooldown;
                if (reason == null && q.MoneyAfter < b.Deals.MinCashAfter)
                {
                    reason = AcqKeys.ReasonNoCash;
                    value = Math.Ceiling(b.Deals.MinCashAfter - q.MoneyAfter);
                    arg = value.ToString("F0", CultureInfo.InvariantCulture);
                }
            }

            if (reason == null)
            {
                q.Available = true;
                return q;
            }

            // A refusal because the target is of the wrong kind has no price to show.
            if (!KindFits(q.Type, t))
            {
                q.Price = 0.0;
                q.AssumedCash = 0.0;
                q.NetCash = 0.0;
                q.MoneyAfter = Finite(p.Money);
                q.Multiplier = 0.0;
                q.CashFloor = 0.0;
                q.CashFloorApplied = false;
                q.ReputationDelta = 0;
            }
            q.ReasonKey = reason;
            q.ReasonArg = arg;
            q.ReasonValue = value;
            return q;
        }

        /// <summary>The target is of the kind the deal is for (a bankrupt company for its assets, one in default for a purchase in default, a healthy one for a takeover).</summary>
        private static bool KindFits(DealType type, CompanySnapshot t)
        {
            switch (type)
            {
                case DealType.BankruptAssets: return t.Status == CompanyStatus.BankruptAuction;
                case DealType.Distressed: return t.Status == CompanyStatus.InDefault;
                case DealType.Hostile: return t.Status == CompanyStatus.Healthy;
                default: return false;
            }
        }

        /// <summary>The reason the status of the target does not allow the deal (and its argument), or null when it does.</summary>
        private static string StatusReason(DealType type, CompanySnapshot t, AcquisitionsState s, PlayerSnapshot p, out string arg)
        {
            arg = null;

            switch (t.Status)
            {
                case CompanyStatus.Absorbed:
                    return AcqKeys.ReasonAbsorbed;

                case CompanyStatus.BankruptAuction:
                    if (type != DealType.BankruptAssets) return AcqKeys.ReasonBankrupt;
                    // The snapshot says an auction is open; the books have the last word (the date may have moved on since the snapshot).
                    return AuctionReason(t, s, p, out arg);

                case CompanyStatus.Bankrupt:
                {
                    if (type != DealType.BankruptAssets) return AcqKeys.ReasonBankrupt;
                    AuctionData record = FindAuction(s, t);
                    if (record == null || record.closed) return AcqKeys.ReasonNoAuction;
                    arg = FormatDate(record.until);
                    return AcqKeys.ReasonAuctionExpired;
                }

                case CompanyStatus.InDefault:
                    if (type == DealType.BankruptAssets) return AcqKeys.ReasonNotBankrupt;
                    if (type == DealType.Hostile) return AcqKeys.ReasonUseDistressed;
                    return null;

                case CompanyStatus.Healthy:
                    if (type == DealType.BankruptAssets) return AcqKeys.ReasonNotBankrupt;
                    if (type == DealType.Distressed) return AcqKeys.ReasonNotInDefault;
                    return null;

                default:
                    return AcqKeys.ReasonUnsupported;
            }
        }

        /// <summary>Null when the company's auction is open today; otherwise why not (expired, with its last day, or no record at all).</summary>
        private static string AuctionReason(CompanySnapshot t, AcquisitionsState s, PlayerSnapshot p, out string arg)
        {
            arg = null;
            if (CompanyStatusRules.OpenAuction(s, t.UniqueId, t.SaveId, p.Today) != null) return null;

            AuctionData record = FindAuction(s, t);
            if (record == null || record.closed) return AcqKeys.ReasonNoAuction;

            arg = FormatDate(record.until);
            return AcqKeys.ReasonAuctionExpired;
        }

        /// <summary>The auction record of this very company (same <c>UniqueID</c> and <c>SaveID</c>), open or not; null when there is none.</summary>
        private static AuctionData FindAuction(AcquisitionsState s, CompanySnapshot t)
        {
            if (s == null || s.auctions == null) return null;
            for (int i = 0; i < s.auctions.Count; i++)
            {
                AuctionData auction = s.auctions[i];
                if (auction != null && auction.targetUniqueId == t.UniqueId && auction.targetSaveId == t.SaveId) return auction;
            }
            return null;
        }

        // ---- cooldown ----

        /// <summary>The hostile takeover is on cooldown: the argument is the first day it is open again. False (no cooldown) without a last takeover, with a zero cooldown or an unknown today.</summary>
        private static bool CooldownDate(AcquisitionsState s, AcquisitionsBalance b, DateTime today, out string available)
        {
            available = null;
            DateTime last;
            if (s == null || b.Deals.Hostile.CooldownDays <= 0 || today == default(DateTime) || !TryDate(s.lastHostileOn, out last)) return false;

            DateTime open = last.Date.AddDays(b.Deals.Hostile.CooldownDays);
            if (today.Date >= open) return false;

            available = open.ToString(DateFormat, CultureInfo.InvariantCulture);
            return true;
        }

        // ---- stakes and rescue orders ----

        private static DealQuote StakeQuote(DealQuote q, CompanySnapshot t, PlayerSnapshot p, AcquisitionsBalance b, double cash, double roundTo)
        {
            bool inDefault = t.Status == CompanyStatus.InDefault;
            double option = StakeMath.SmallestFittingOption(q.OwnedShare, b.Stake);

            q.Multiplier = inDefault ? b.Stake.DistressedMultiplier : b.Stake.HealthyMultiplier;
            if (option > 0.0)
            {
                q.Price = StakeMath.StakePrice(option, q.Valuation, cash, inDefault, b.Stake, q.Channel, roundTo);
                q.NetCash = -q.Price;
            }
            q.MoneyAfter = Finite(p.Money) + q.NetCash;
            q.ReputationDelta = inDefault ? b.Reputation.Rescue : 0;

            string reason = null;
            string arg = null;
            double value = 0.0;
            if (p.InDefault || p.IsBankrupt) reason = AcqKeys.ReasonPlayerDefault;
            else if (t.Status == CompanyStatus.Absorbed) reason = AcqKeys.ReasonAbsorbed;
            else if (t.IsBankrupt) reason = AcqKeys.ReasonBankrupt;
            else if (!b.Stake.StakesEnabled) reason = AcqKeys.ReasonStakesOff;
            else if (option <= 0.0) reason = AcqKeys.ReasonStakeMax;
            else if (q.MoneyAfter < b.Deals.MinCashAfter)
            {
                reason = AcqKeys.ReasonNoCash;
                value = Math.Ceiling(b.Deals.MinCashAfter - q.MoneyAfter);
                arg = value.ToString("F0", CultureInfo.InvariantCulture);
            }

            q.Available = reason == null;
            q.ReasonKey = reason;
            q.ReasonArg = arg;
            q.ReasonValue = value;
            return q;
        }

        private static DealQuote RescueOrderQuote(DealQuote q, CompanySnapshot t, PlayerSnapshot p, AcquisitionsBalance b)
        {
            q.Multiplier = b.Stake.DistressedMultiplier;
            q.MoneyAfter = Finite(p.Money);
            q.ReputationDelta = b.Reputation.Rescue;

            string reason = null;
            if (p.InDefault || p.IsBankrupt) reason = AcqKeys.ReasonPlayerDefault;
            else if (t.Status == CompanyStatus.Absorbed) reason = AcqKeys.ReasonAbsorbed;
            else if (t.IsBankrupt) reason = AcqKeys.ReasonBankrupt;
            else if (!b.Stake.StakesEnabled) reason = AcqKeys.ReasonStakesOff;
            else if (q.OwnedShare >= b.Stake.MaxShare) reason = AcqKeys.ReasonStakeMax;

            q.Available = reason == null;
            q.ReasonKey = reason;
            return q;
        }

        // ---- small helpers ----

        private static int ReputationOf(DealType type, AcquisitionsBalance b)
        {
            switch (type)
            {
                case DealType.Hostile: return b.Reputation.Hostile;
                case DealType.Distressed: return b.Reputation.Distressed;
                case DealType.BankruptAssets: return b.Reputation.Bankrupt;
                default: return 0;
            }
        }

        private static bool IsUsableChannel(double channel)
        {
            return !double.IsNaN(channel) && !double.IsInfinity(channel) && channel > 0.0;
        }

        private static double Finite(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : value;
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0.0;
            return value < 0.0 ? 0.0 : (value > 1.0 ? 1.0 : value);
        }

        private static bool TryDate(string text, out DateTime date)
        {
            return DateTime.TryParseExact(text, AcquisitionsState.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private static string FormatDate(string stored)
        {
            DateTime date;
            return TryDate(stored, out date) ? date.ToString(DateFormat, CultureInfo.InvariantCulture) : stored;
        }
    }
}
