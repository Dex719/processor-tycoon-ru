using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>One stake the dialog offers: the share, what it costs, what the player has left, whether he can pay it and the dividend it would earn.</summary>
    public sealed class StakeOption
    {
        public double Share;

        /// <summary>P_σ: the price, which goes in full into the company's account (the capital injection).</summary>
        public double Price;

        public double MoneyAfter;

        /// <summary>The share the player owns after buying this one.</summary>
        public double ShareAfter;

        /// <summary>The player keeps at least <c>deals.minCashAfter</c> after paying.</summary>
        public bool Affordable;

        /// <summary>Why it cannot be bought (<see cref="AcqKeys"/>); null when it can.</summary>
        public string ReasonKey;

        public string ReasonArg;

        /// <summary>The dividend per month the stake after the purchase would have earned by last month's profit (0 for a loss, or when the company could not pay it).</summary>
        public double ExpectedDividend;
    }

    /// <summary>
    /// The scale of the slider of a rescue order's limit (design.md "Диалог доли"): money is in millions of dollars, the number of steps is held near 100
    /// whatever the era, so the same slider serves a company of 1985 and one of 2020. Pure.
    /// </summary>
    public sealed class RescueScale
    {
        public const double Unit = 1000000.0;
        public const double FallbackCreditLimit = 100000000.0;

        private static readonly double[] Steps = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000, 100000, 1000000 };

        /// <summary>The top of the slider in millions.</summary>
        public double MaxUnits { get; private set; }

        /// <summary>One step of the slider in millions.</summary>
        public double Step { get; private set; }

        /// <summary>The limit a new order starts with, in dollars: about one credit limit of the company.</summary>
        public double DefaultAmount { get; private set; }

        /// <summary>
        /// The scale for a company with the credit limit <paramref name="creditLimit"/> (D, 0 or less for none: a typical one is taken). The top reaches three
        /// credit limits, or the limit the order has now when that is more.
        /// </summary>
        public static RescueScale For(double creditLimit, double currentLimit)
        {
            double d = creditLimit > 0.0 && !double.IsInfinity(creditLimit) ? creditLimit : FallbackCreditLimit;
            double current = currentLimit > 0.0 && !double.IsInfinity(currentLimit) ? currentLimit : 0.0;
            double top = Math.Max(Math.Max(3.0 * d, current), Unit);

            double rough = Math.Ceiling(top / Unit);
            double step = Steps[Steps.Length - 1];
            for (int i = 0; i < Steps.Length; i++)
            {
                if (rough / Steps[i] <= 100.0)
                {
                    step = Steps[i];
                    break;
                }
            }

            double max = Math.Ceiling(rough / step) * step;
            var scale = new RescueScale { Step = step, MaxUnits = max };
            scale.DefaultAmount = scale.ToAmount(scale.ToUnits(d));
            if (!(scale.DefaultAmount > 0.0)) scale.DefaultAmount = step * Unit;
            return scale;
        }

        /// <summary>The slider value (millions, on the step grid, within 0 to the top) for an amount of dollars.</summary>
        public double ToUnits(double amount)
        {
            if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0.0) return 0.0;
            double units = Math.Round(amount / Unit / Step, MidpointRounding.AwayFromZero) * Step;
            return units > MaxUnits ? MaxUnits : units;
        }

        /// <summary>Dollars for a slider value.</summary>
        public double ToAmount(double units)
        {
            if (double.IsNaN(units) || double.IsInfinity(units) || units <= 0.0) return 0.0;
            return Math.Min(units, MaxUnits) * Unit;
        }
    }

    /// <summary>
    /// What the dialog of a stake shows (TSK-017; FR-6; design.md "Диалог доли"): the stakes the player can still buy with their price, the injection into the
    /// company, the dividend by last month's profit, and the rescue order (on/off and its limit). Built by the pure rules (<see cref="DealRules"/>,
    /// <see cref="StakeMath"/>), so the dialog decides nothing itself.
    /// </summary>
    public sealed class StakeDialogModel
    {
        public int UniqueId;
        public int SaveId;
        public string Name = "";
        public CompanyStatus Status;
        public bool InDefault;

        /// <summary>σ_p now.</summary>
        public double OwnedShare;

        public double TargetMoney;
        public double CreditLimit;
        public double PlayerMoney;

        /// <summary>Last month's profit of the company (the dividend is paid from it).</summary>
        public double LastMonthProfit;

        /// <summary>The dividend the stake the player owns now earns by that profit.</summary>
        public double CurrentDividend;

        /// <summary>A stake can be bought in this company (maybe not every option: see <see cref="StakeOption.Affordable"/>).</summary>
        public bool CanBuy;

        /// <summary>Why no stake can be bought (<see cref="AcqKeys"/>); null when <see cref="CanBuy"/>.</summary>
        public string ReasonKey;

        public string ReasonArg;

        public List<StakeOption> Options = new List<StakeOption>();

        /// <summary>A rescue order can be set for this company.</summary>
        public bool OrderAvailable;

        public string OrderReasonKey;
        public bool OrderActive;
        public double OrderMaxAmount;

        /// <summary>What a rescue would cost at the moment the company reaches its threshold: its balance is then <c>-D</c> and goes up to <c>rescueTargetDebtLimitShare × D</c>.</summary>
        public double RescueMinInjection;

        public RescueScale Scale;

        public static StakeDialogModel Build(CompanySnapshot t, PlayerSnapshot p, AcquisitionsState s, AcquisitionsBalance b, double channel, double lastMonthProfit)
        {
            if (b == null) b = AcquisitionsBalance.Defaults();
            if (s == null) s = new AcquisitionsState();
            if (p == null) p = new PlayerSnapshot();
            if (t == null) t = new CompanySnapshot();

            DealQuote stakeQuote = DealRules.Quote(DealType.Stake, t, p, s, b, channel);
            DealQuote orderQuote = DealRules.Quote(DealType.RescueOrder, t, p, s, b, channel);
            bool inDefault = t.Status == CompanyStatus.InDefault;

            var m = new StakeDialogModel
            {
                UniqueId = t.UniqueId,
                SaveId = t.SaveId,
                Name = t.Name ?? "",
                Status = t.Status,
                InDefault = inDefault,
                OwnedShare = stakeQuote.OwnedShare,
                TargetMoney = t.Money,
                CreditLimit = t.CreditLimit,
                PlayerMoney = p.Money,
                LastMonthProfit = lastMonthProfit,
                OrderAvailable = orderQuote.Available,
                OrderReasonKey = orderQuote.Available ? null : orderQuote.ReasonKey,
            };
            m.CurrentDividend = StakeMath.Dividend(m.OwnedShare, lastMonthProfit, t.Money, b.Stake);

            RescueOrderData order = StakeBook.Order(s, t.UniqueId, t.SaveId);
            m.OrderActive = order != null && order.active;
            m.OrderMaxAmount = order != null ? order.maxAmount : 0.0;
            m.Scale = RescueScale.For(t.CreditLimit, m.OrderMaxAmount);
            if (t.CreditLimit > 0.0) m.RescueMinInjection = Math.Max(0.0, t.CreditLimit * (1.0 + b.Stake.RescueTargetDebtLimitShare));

            // The options are listed whenever a stake can be bought at all; the money is judged per option.
            m.CanBuy = stakeQuote.Available || stakeQuote.ReasonKey == AcqKeys.ReasonNoCash;
            if (!m.CanBuy)
            {
                m.ReasonKey = stakeQuote.ReasonKey;
                m.ReasonArg = stakeQuote.ReasonArg;
                return m;
            }

            foreach (double share in StakeMath.FittingOptions(m.OwnedShare, b.Stake))
            {
                double price = StakeMath.StakePrice(share, stakeQuote.Valuation, t.Money, inDefault, b.Stake, stakeQuote.Channel, b.Valuation.RoundTo);
                double after = p.Money - price;
                var option = new StakeOption
                {
                    Share = share,
                    Price = price,
                    MoneyAfter = after,
                    ShareAfter = m.OwnedShare + share,
                    Affordable = after >= b.Deals.MinCashAfter && price > 0.0,
                };
                if (!option.Affordable)
                {
                    option.ReasonKey = price > 0.0 ? AcqKeys.ReasonNoCash : AcqKeys.ReasonUnsupported;
                    if (price > 0.0) option.ReasonArg = Math.Ceiling(b.Deals.MinCashAfter - after).ToString("F0", CultureInfo.InvariantCulture);
                }
                option.ExpectedDividend = StakeMath.Dividend(option.ShareAfter, lastMonthProfit, t.Money + price, b.Stake);
                m.Options.Add(option);
            }

            if (m.Options.Count == 0)
            {
                m.CanBuy = false;
                m.ReasonKey = AcqKeys.ReasonStakeMax;
            }
            return m;
        }

        /// <summary>The option of this share, or null.</summary>
        public StakeOption OptionOf(double share)
        {
            for (int i = 0; i < Options.Count; i++)
            {
                if (Math.Abs(Options[i].Share - share) < 1e-9) return Options[i];
            }
            return null;
        }
    }

    /// <summary>A line of the tab "My stakes": a stake, or a switched-on rescue order of a company the player owns nothing of.</summary>
    public sealed class StakeRow
    {
        public int UniqueId;
        public int SaveId;
        public string Name = "";
        public CompanyStatus Status;
        public double Share;
        public double Invested;
        public double DividendsPaid;
        public double LastDividend;
        public bool OrderActive;
        public double OrderMaxAmount;
        public double CreditLimit;
    }

    /// <summary>The model of the tab "My stakes" (TSK-015, FR-6): the stakes and the active rescue orders of the companies that are still in the game.</summary>
    public static class StakesModel
    {
        public static List<StakeRow> Build(AcquisitionsState s, IList<CompanySnapshot> companies)
        {
            var rows = new List<StakeRow>();
            if (s == null || companies == null) return rows;

            for (int i = 0; i < companies.Count; i++)
            {
                CompanySnapshot t = companies[i];
                if (t == null || t.IsBankrupt || t.Status == CompanyStatus.Absorbed) continue;   // the stake of such a company is written off

                StakeData stake = StakeBook.Find(s, t.UniqueId, t.SaveId);
                RescueOrderData order = StakeBook.Order(s, t.UniqueId, t.SaveId);
                bool hasStake = stake != null && AcquisitionsState.IsValidShare(stake.share);
                bool hasOrder = order != null && order.active;
                if (!hasStake && !hasOrder) continue;

                rows.Add(new StakeRow
                {
                    UniqueId = t.UniqueId,
                    SaveId = t.SaveId,
                    Name = t.Name ?? "",
                    Status = t.Status,
                    Share = hasStake ? stake.share : 0.0,
                    Invested = hasStake ? stake.invested : 0.0,
                    DividendsPaid = hasStake ? stake.dividendsPaid : 0.0,
                    LastDividend = hasStake ? stake.lastDividend : 0.0,
                    OrderActive = hasOrder,
                    OrderMaxAmount = order != null ? order.maxAmount : 0.0,
                    CreditLimit = t.CreditLimit,
                });
            }

            rows.Sort((a, b) =>
            {
                int byShare = b.Share.CompareTo(a.Share);
                if (byShare != 0) return byShare;
                int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : a.UniqueId.CompareTo(b.UniqueId);
            });
            return rows;
        }

        public static StakeRow Find(IList<StakeRow> rows, int uniqueId)
        {
            if (rows == null) return null;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].UniqueId == uniqueId) return rows[i];
            }
            return null;
        }
    }

    /// <summary>A line of the tab "History": a deal of the journal.</summary>
    public sealed class HistoryRow
    {
        public int Id;

        /// <summary>The day, <c>dd.MM.yyyy</c>.</summary>
        public string Date = "";

        /// <summary>The key of the kind of deal (<see cref="UiKeys"/>).</summary>
        public string TypeKey = "";

        public DealType Type;

        /// <summary><c>UniqueID</c> and <c>SaveID</c> of the company at the time of the deal.</summary>
        public int TargetUniqueId;

        public int TargetSaveId;
        public string TargetName = "";
        public double Price;
        public double Share;
        public bool Partial;
        public string Notes = "";
    }

    /// <summary>The model of the tab "History" (TSK-015, FR-14 item 2): the journal of the deals, the newest first, with the mark "partial".</summary>
    public static class HistoryModel
    {
        /// <summary>The most rows the tab shows (the journal itself keeps all).</summary>
        public const int MaxRows = 300;

        public static List<HistoryRow> Rows(IList<DealRecord> deals)
        {
            var rows = new List<HistoryRow>();
            if (deals == null) return rows;

            for (int i = deals.Count - 1; i >= 0 && rows.Count < MaxRows; i--)
            {
                DealRecord deal = deals[i];
                if (deal == null) continue;

                DealType type;
                if (!TryType(deal.type, out type)) continue;
                rows.Add(new HistoryRow
                {
                    Id = deal.id,
                    Date = DealsTable.FormatStored(deal.date),
                    Type = type,
                    TypeKey = TypeKey(type),
                    TargetUniqueId = deal.targetUniqueId,
                    TargetSaveId = deal.targetSaveId,
                    TargetName = deal.targetName ?? "",
                    Price = deal.price,
                    Share = deal.share,
                    Partial = deal.partial,
                    Notes = deal.notes ?? "",
                });
            }
            return rows;
        }

        public static string TypeKey(DealType type)
        {
            switch (type)
            {
                case DealType.BankruptAssets: return UiKeys.HistoryBankrupt;
                case DealType.Distressed: return UiKeys.HistoryDistressed;
                case DealType.Hostile: return UiKeys.HistoryHostile;
                case DealType.Stake: return UiKeys.HistoryStake;
                default: return UiKeys.HistoryRescue;
            }
        }

        private static bool TryType(string name, out DealType type)
        {
            switch (name)
            {
                case "BankruptAssets": type = DealType.BankruptAssets; return true;
                case "Distressed": type = DealType.Distressed; return true;
                case "Hostile": type = DealType.Hostile; return true;
                case "Stake": type = DealType.Stake; return true;
                case "RescueOrder": type = DealType.RescueOrder; return true;
                default: type = DealType.Hostile; return false;
            }
        }
    }
}
