using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>One deal the table offers for a company: its price, whether it is open and, if not, why (FR-2 items 3, 4).</summary>
    public sealed class DealOffer
    {
        public DealType Type;
        public bool Available;

        /// <summary>A key of <see cref="AcqKeys"/> when the deal is not open; null when it is.</summary>
        public string ReasonKey;

        public string ReasonArg;

        /// <summary>The price shown on the button; 0 when the deal has none for this company (a wrong kind of target).</summary>
        public double Price;

        /// <summary>The whole quote, for the preview.</summary>
        public DealQuote Quote;

        /// <summary>True for the deals that open the dialog of a stake instead of the preview of a purchase.</summary>
        public bool IsStake
        {
            get { return Type == DealType.Stake; }
        }
    }

    /// <summary>One line of the table "Companies" with everything its details show (FR-2 item 2).</summary>
    public sealed class CompanyRow
    {
        public int UniqueId;
        public int SaveId;
        public string Name = "";
        public CompanyStatus Status;

        /// <summary>The key of the status text (<see cref="UiKeys"/>) and its argument ({0}: the days in default, the last day of the auction, the day of the takeover); null when it has none.</summary>
        public string StatusKey = UiKeys.StatusHealthy;

        public string StatusArg;
        public int DaysInDefault;

        public double Money;
        public double CreditLimit;
        public int Lines;
        public int ActiveCpus;
        public double MarketShare;

        /// <summary>E and its components (the table shows E; the details show the rest).</summary>
        public Valuation Valuation;

        /// <summary>σ_p: the share of the company the player owns.</summary>
        public double OwnedShare;

        /// <summary>The player has a rescue order for the company that is switched on.</summary>
        public bool RescueOrderActive;

        public CompanySnapshot Snapshot;

        public List<DealOffer> Offers = new List<DealOffer>();

        /// <summary>The company is in the game for a deal: not absorbed (nothing is offered for an absorbed one).</summary>
        public bool HasOffers
        {
            get { return Offers.Count > 0; }
        }

        /// <summary>At least one deal is open.</summary>
        public bool AnyAvailable
        {
            get
            {
                for (int i = 0; i < Offers.Count; i++)
                {
                    if (Offers[i].Available) return true;
                }
                return false;
            }
        }

        public DealOffer OfferOf(DealType type)
        {
            for (int i = 0; i < Offers.Count; i++)
            {
                if (Offers[i].Type == type) return Offers[i];
            }
            return null;
        }
    }

    /// <summary>
    /// The model of the tab "Companies" (TSK-015, FR-2): a row per company of the game with its status, money, lines, active CPUs, market share, valuation and
    /// the player's share, and the deals offered with their price or the reason they are closed. Built from the snapshots by the pure rules
    /// (<see cref="ValuationMath"/>, <see cref="DealRules"/>), so the window decides nothing itself. The order is the order of interest: auctions that are
    /// open, companies in default, healthy ones (the biggest first), bankrupt ones, absorbed ones.
    /// </summary>
    public static class DealsTable
    {
        /// <summary>The deals a company of this status is offered, in the order of the buttons (design.md "Окно «Сделки»"). The takeover of a company in default is shown closed, with its reason.</summary>
        public static DealType[] OfferTypes(CompanyStatus status)
        {
            switch (status)
            {
                case CompanyStatus.Healthy:
                    return new[] { DealType.Hostile, DealType.Stake };
                case CompanyStatus.InDefault:
                    return new[] { DealType.Distressed, DealType.Hostile, DealType.Stake };
                case CompanyStatus.BankruptAuction:
                case CompanyStatus.Bankrupt:
                    return new[] { DealType.BankruptAssets };
                default:
                    return new DealType[0];
            }
        }

        /// <summary>The CPUs of the company that are on sale: those not retired, or, for a bankrupt company, those that were on sale when it went bankrupt.</summary>
        public static int ActiveCpuCount(CompanySnapshot t)
        {
            if (t == null || t.Cpus == null) return 0;

            bool bankrupt = t.IsBankrupt || t.Status == CompanyStatus.Absorbed;
            int count = 0;
            for (int i = 0; i < t.Cpus.Count; i++)
            {
                CpuSnapshot cpu = t.Cpus[i];
                if (cpu == null) continue;
                if (bankrupt ? cpu.ActiveAtBankruptcy : !cpu.IsRetired) count++;
            }
            return count;
        }

        /// <summary>
        /// The rows for the companies, ordered. <paramref name="channelOf"/> gives <c>acquisition.price</c> of a company (null: 1 for all).
        /// A company of the list that is null is skipped.
        /// </summary>
        public static List<CompanyRow> Build(IList<CompanySnapshot> companies, PlayerSnapshot p, AcquisitionsState s, AcquisitionsBalance b, Func<CompanySnapshot, double> channelOf)
        {
            var rows = new List<CompanyRow>();
            if (companies == null) return rows;
            if (b == null) b = AcquisitionsBalance.Defaults();
            if (s == null) s = new AcquisitionsState();
            if (p == null) p = new PlayerSnapshot();

            for (int i = 0; i < companies.Count; i++)
            {
                CompanySnapshot t = companies[i];
                if (t == null) continue;
                rows.Add(RowOf(t, p, s, b, channelOf != null ? channelOf(t) : 1.0));
            }

            rows.Sort(Compare);
            return rows;
        }

        /// <summary>The row of one company.</summary>
        public static CompanyRow RowOf(CompanySnapshot t, PlayerSnapshot p, AcquisitionsState s, AcquisitionsBalance b, double channel)
        {
            var row = new CompanyRow
            {
                UniqueId = t.UniqueId,
                SaveId = t.SaveId,
                Name = t.Name ?? "",
                Status = t.Status,
                DaysInDefault = t.DaysInDefault,
                Money = t.Money,
                CreditLimit = t.CreditLimit,
                Lines = t.TotalLines,
                ActiveCpus = ActiveCpuCount(t),
                MarketShare = t.MarketShare,
                Valuation = ValuationMath.Evaluate(t, p, b.Valuation),
                OwnedShare = CompanyStatusRules.OwnedShare(s, t.UniqueId, t.SaveId),
                Snapshot = t,
            };

            RescueOrderData order = StakeBook.Order(s, t.UniqueId, t.SaveId);
            row.RescueOrderActive = order != null && order.active;

            SetStatusText(row, t, p, s);

            foreach (DealType type in OfferTypes(t.Status))
            {
                DealQuote quote = DealRules.Quote(type, t, p, s, b, channel);
                row.Offers.Add(new DealOffer
                {
                    Type = type,
                    Available = quote.Available,
                    ReasonKey = quote.Available ? null : quote.ReasonKey,
                    ReasonArg = quote.Available ? null : quote.ReasonArg,
                    Price = quote.Price,
                    Quote = quote,
                });
            }
            return row;
        }

        private static void SetStatusText(CompanyRow row, CompanySnapshot t, PlayerSnapshot p, AcquisitionsState s)
        {
            switch (t.Status)
            {
                case CompanyStatus.InDefault:
                    row.StatusKey = UiKeys.StatusDefault;
                    row.StatusArg = t.DaysInDefault.ToString(CultureInfo.InvariantCulture);
                    break;

                case CompanyStatus.BankruptAuction:
                {
                    row.StatusKey = UiKeys.StatusAuction;
                    AuctionData auction = CompanyStatusRules.OpenAuction(s, t.UniqueId, t.SaveId, p.Today);
                    row.StatusArg = FormatStored(auction != null ? auction.until : null);
                    break;
                }

                case CompanyStatus.Absorbed:
                    row.StatusArg = FormatStored(AbsorbedOn(s, t.UniqueId));
                    row.StatusKey = row.StatusArg.Length > 0 ? UiKeys.StatusAbsorbed : UiKeys.StatusAbsorbedPlain;
                    break;

                case CompanyStatus.Bankrupt:
                    row.StatusKey = UiKeys.StatusBankrupt;
                    row.StatusArg = null;
                    break;

                default:
                    row.StatusKey = UiKeys.StatusHealthy;
                    row.StatusArg = null;
                    break;
            }
        }

        /// <summary>The day (<c>yyyy-MM-dd</c>) the player bought the company, from the journal; null when there is no such deal.</summary>
        public static string AbsorbedOn(AcquisitionsState s, int uniqueId)
        {
            if (s == null || s.deals == null) return null;
            string day = null;
            for (int i = 0; i < s.deals.Count; i++)
            {
                DealRecord deal = s.deals[i];
                if (deal != null && deal.targetUniqueId == uniqueId && AcquisitionsState.IsAbsorbingType(deal.type)) day = deal.date;
            }
            return day;
        }

        /// <summary>A stored date as the windows show dates (<c>dd.MM.yyyy</c>); an empty text for no date.</summary>
        public static string FormatStored(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            DateTime date;
            if (DateTime.TryParseExact(stored, AcquisitionsState.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                return date.ToString(DealRules.DateFormat, CultureInfo.InvariantCulture);
            }
            return stored;
        }

        public static CompanyRow Find(IList<CompanyRow> rows, int uniqueId)
        {
            if (rows == null) return null;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].UniqueId == uniqueId) return rows[i];
            }
            return null;
        }

        public static int IndexOf(IList<CompanyRow> rows, int uniqueId)
        {
            if (rows == null) return -1;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].UniqueId == uniqueId) return i;
            }
            return -1;
        }

        private static int Rank(CompanyStatus status)
        {
            switch (status)
            {
                case CompanyStatus.BankruptAuction: return 0;
                case CompanyStatus.InDefault: return 1;
                case CompanyStatus.Healthy: return 2;
                case CompanyStatus.Bankrupt: return 3;
                default: return 4;
            }
        }

        private static int Compare(CompanyRow a, CompanyRow b)
        {
            int byRank = Rank(a.Status).CompareTo(Rank(b.Status));
            if (byRank != 0) return byRank;

            int byValue = b.Valuation.E.CompareTo(a.Valuation.E);
            if (byValue != 0) return byValue;

            int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.UniqueId.CompareTo(b.UniqueId);
        }
    }
}
