using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.BankruptcyRecovery;
using GameAiCompany = ProcessorTycoon.CompanySystem.AICompany;
using GameFinance = ProcessorTycoon.CompanySystem.CompanyFinance;
using GamePlayer = ProcessorTycoon.Player;
using RevenueOrigin = ProcessorTycoon.RevenueOrigin;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>What came of a purchase of a stake: bought (the share now owned) or refused with the reason (<see cref="AcqKeys"/>).</summary>
    internal sealed class StakeOutcome
    {
        public bool Ok;
        public string ReasonKey;
        public string ReasonArg;

        /// <summary>The money paid into the company.</summary>
        public double Price;

        /// <summary>The share of the company the player owns after the purchase.</summary>
        public double ShareNow;
    }

    /// <summary>
    /// <c>StakeService</c> (TSK-014; FR-6; design.md "StakeService", D6, D7): the player's minority stakes in AI companies. The numbers are
    /// <c>StakeMath</c>'s, the records <c>StakeBook</c>'s; this class moves the money and talks to the game.
    /// <list type="bullet">
    /// <item><b>Purchase</b>: the price leaves the player (<c>CapitalLedger.CapitalOut</c>, capital) and goes into the company's account
    /// (<c>ICompany.set_MoneyAmount</c>): an injection that can bring the company out of default. +3 reputation for a company in default.</item>
    /// <item><b>Dividends</b> on <c>LateMonthPassed</c>: from the company's report of the month before (found by <c>Year</c>/<c>Month</c>, not by its place
    /// in the list), paid through <c>Company.EarnMoney(…, RevenueOrigin.Interest)</c> only when the company keeps 0 or more after it.</item>
    /// <item><b>Rescue order</b> (P3, a prefix of <c>AICompany.BankruptcyWarning</c>): when the order is active, the threshold is below 0, the company is in
    /// default and at or below the threshold, the injection <c>J</c> brings its balance to <c>rescueTargetDebtLimitShare × ModCredit.DebtLimitOf(company)</c>
    /// (strictly above the threshold) before the game's method would sell its lines, make it fabless or spin GlobalFoundries off: the original then
    /// returns at once and <c>TickBankruptcies</c> calls <c>CancelDefault</c>. A refusal (limit, money, share) is a mail with the reason, and the game goes on
    /// as without the mod.</item>
    /// <item><b>Write-off</b>: the stake and the order of a company that went bankrupt end, with a mail.</item>
    /// <item>A purchase of the whole company takes the stake into account (the price is lower by the share) and ends it: <c>DealExecution</c>.</item>
    /// </list>
    /// Every entry point does nothing while the feature is off. Main thread only.
    /// </summary>
    internal sealed class StakeService
    {
        private const string Feature = RivalAcquisitionsFeature.FeatureId;

        private readonly RivalAcquisitionsFeature _feature;

        public StakeService(RivalAcquisitionsFeature feature)
        {
            _feature = feature;
        }

        private AcquisitionsState State
        {
            get { return _feature.State; }
        }

        private AcquisitionsBalance Balance
        {
            get { return _feature.Balance; }
        }

        // ---- purchase ----

        /// <summary>Buys <paramref name="share"/> of <paramref name="target"/> (one of <c>stake.options</c> as the dialog offers them). Never throws.</summary>
        public StakeOutcome Buy(GameAiCompany target, double share)
        {
            var outcome = new StakeOutcome();
            if (!ModFeatures.IsActive(Feature) || target == null) return Refuse(outcome, AcqKeys.ReasonUnsupported, null);

            PlayerSnapshot p = GameSnapshotAdapter.Player();
            if (p == null) return Refuse(outcome, AcqKeys.ReasonUnsupported, null);

            AcquisitionsState s = State;
            AcquisitionsBalance b = Balance;
            CompanySnapshot t = GameSnapshotAdapter.Of(target, p, s, b.Valuation.CpuIncomeMonths);
            double channel = GameSnapshotAdapter.PriceChannelOf(target);
            DealQuote quote = DealRules.Quote(DealType.Stake, t, p, s, b, channel);
            if (!quote.Available && quote.ReasonKey != AcqKeys.ReasonNoCash) return Refuse(outcome, quote.ReasonKey, quote.ReasonArg);

            double owned = CompanyStatusRules.OwnedShare(s, t.UniqueId, t.SaveId);
            if (!StakeMath.FitsShare(owned, share, b.Stake)) return Refuse(outcome, AcqKeys.ReasonStakeMax, null);

            bool inDefault = t.Status == CompanyStatus.InDefault;
            double price = StakeMath.StakePrice(share, quote.Valuation, t.Money, inDefault, b.Stake, quote.Channel, b.Valuation.RoundTo);
            double after = p.Money - price;
            if (after < b.Deals.MinCashAfter)
            {
                return Refuse(outcome, AcqKeys.ReasonNoCash, Math.Ceiling(b.Deals.MinCashAfter - after).ToString("F0", CultureInfo.InvariantCulture));
            }
            if (!(price > 0.0)) return Refuse(outcome, AcqKeys.ReasonUnsupported, null);
            if (!CapitalLedger.IsAvailable) return Refuse(outcome, AcqKeys.ReasonLedgerMissing, null);

            DateTime today = p.Today;
            CapitalLedger.CapitalOut(Feature, price, "acq.stake", (target.Name ?? "") + ": " + AcquisitionMail.Percent(share) + "%");
            target.MoneyAmount = (float)(target.MoneyAmount + price);

            double now = StakeBook.AddShare(s, target.UniqueID, target.SaveID, share, price, today);
            DealRecord record = StakeBook.Record(DealType.Stake, target.UniqueID, target.SaveID, target.Name, today, price, share);
            s.AddDeal(record);

            if (inDefault && b.Reputation.Rescue != 0) AddReputation(b.Reputation.Rescue);
            RivalAcquisitionsApi.RaiseStakeChanged(target.UniqueID, now);
            RivalAcquisitionsApi.RaiseDealCompleted(record);
            ModLog.Info(Feature, "stake: " + AcquisitionMail.Percent(share) + "% of '" + target.Name + "' for " + price.ToString("F0", CultureInfo.InvariantCulture)
                                 + (inDefault ? " (in default)" : "") + "; owned now " + AcquisitionMail.Percent(now) + "%");

            outcome.Ok = true;
            outcome.Price = price;
            outcome.ShareNow = now;
            return outcome;
        }

        // ---- rescue orders ----

        /// <summary>
        /// Sets the rescue order of the company <paramref name="uniqueId"/> (its limit and whether it is on); raises <c>StakeChanged</c> with the
        /// share owned. False for a company that is not in the game, bankrupt, or while the feature is off.
        /// </summary>
        public bool SetRescueOrder(int uniqueId, double maxAmount, bool active)
        {
            if (!ModFeatures.IsActive(Feature)) return false;
            var company = ModCompanies.Resolve(CompanyKey.ForAi(uniqueId)) as GameAiCompany;
            if (company == null || company.IsBankrupt) return false;

            RescueOrderData before = StakeBook.Order(State, uniqueId, company.SaveID);
            bool wasActive = before != null && before.active;
            StakeBook.SetOrder(State, uniqueId, company.SaveID, maxAmount, active);
            RivalAcquisitionsApi.RaiseStakeChanged(uniqueId, CompanyStatusRules.OwnedShare(State, uniqueId, company.SaveID));

            // A slider dragged over many steps changes only the limit: the log keeps one line per switch and the steps go to the debug log.
            string line = "stake: rescue order for '" + company.Name + "' " + (active ? "on" : "off") + ", limit " + maxAmount.ToString("F0", CultureInfo.InvariantCulture);
            if (wasActive != active) ModLog.Info(Feature, line);
            else ModLog.Debug(Feature, line);
            return true;
        }

        /// <summary>
        /// P3 (prefix of <c>AICompany.BankruptcyWarning</c>): carries the rescue order out when it applies. True when the money went in (the game's method then
        /// returns at once, the company stays alive with its lines); false otherwise, and the game goes on as without the mod.
        /// </summary>
        public bool OnBankruptcyWarning(GameAiCompany target, float negativeBalanceLimit)
        {
            if (target == null || target.IsPlayer || target.IsBankrupt) return false;
            if (ModTicks.IsLoading || ModTicks.IsFastForwarding) return false;

            AcquisitionsState s = State;
            RescueOrderData order = StakeBook.Order(s, target.UniqueID, target.SaveID);
            if (order == null || !StakeBook.ShouldTryRescue(order.active, negativeBalanceLimit, target.IsInDefault, target.MoneyAmount)) return false;

            AcquisitionsBalance b = Balance;
            PlayerSnapshot p = GameSnapshotAdapter.Player();
            if (p == null) return false;

            CompanySnapshot t = GameSnapshotAdapter.Of(target, p, s, b.Valuation.CpuIncomeMonths);
            Valuation v = ValuationMath.Evaluate(t, p, b.Valuation);
            double channel = GameSnapshotAdapter.PriceChannelOf(target);
            double owned = CompanyStatusRules.OwnedShare(s, target.UniqueID, target.SaveID);
            double limit = ModCredit.DebtLimitOf(target);
            if (!(limit > 0.0)) limit = -negativeBalanceLimit;   // the argument is this company's threshold already (P10)

            RescueDecision decision = StakeMath.Rescue(target.MoneyAmount, limit, owned, v, p.Money, order.maxAmount, b.Stake, channel, b.Deals.MinCashAfter);
            if (decision.Ok && !CapitalLedger.IsAvailable) decision = new RescueDecision { Ok = false, ReasonKey = AcqKeys.RescueNoLedger };
            if (decision.Ok && !StakeMath.FitsShare(owned, decision.ShareBought, b.Stake)) decision.Ok = false;

            if (!decision.Ok)
            {
                ModLog.Info(Feature, "stake: the rescue of '" + target.Name + "' is refused (" + decision.ReasonKey + (decision.ReasonArg != null ? " " + decision.ReasonArg : "") + ")");
                AcquisitionMail.RescueFailed(target, decision);
                return false;
            }

            DateTime today = p.Today;
            CapitalLedger.CapitalOut(Feature, decision.Injection, "acq.rescue", (target.Name ?? "") + ": rescue order");
            target.MoneyAmount = (float)(target.MoneyAmount + decision.Injection);
            if (target.MoneyAmount <= negativeBalanceLimit)
            {
                ModLog.Warn(Feature, "stake: after the rescue '" + target.Name + "' is still at " + target.MoneyAmount.ToString("F0", CultureInfo.InvariantCulture)
                                     + ", at or below its threshold " + negativeBalanceLimit.ToString("F0", CultureInfo.InvariantCulture));
            }

            double now = StakeBook.AddShare(s, target.UniqueID, target.SaveID, decision.ShareBought, decision.Injection, today);
            DealRecord record = StakeBook.Record(DealType.RescueOrder, target.UniqueID, target.SaveID, target.Name, today, decision.Injection, decision.ShareBought);
            s.AddDeal(record);

            if (b.Reputation.Rescue != 0) AddReputation(b.Reputation.Rescue);
            RivalAcquisitionsApi.RaiseStakeChanged(target.UniqueID, now);
            RivalAcquisitionsApi.RaiseDealCompleted(record);
            AcquisitionMail.Rescue(record, now);
            ModLog.Info(Feature, "stake: rescue of '" + target.Name + "': " + decision.Injection.ToString("F0", CultureInfo.InvariantCulture) + " in, "
                                 + AcquisitionMail.Percent(decision.ShareBought) + "% bought, owned now " + AcquisitionMail.Percent(now) + "%");
            return true;
        }

        // ---- write-off ----

        /// <summary>The company went bankrupt (on its own): its stake and rescue order end; a mail when there was a stake (FR-6 item 5).</summary>
        public void OnRivalBankrupt(GameAiCompany company)
        {
            if (company == null) return;
            AcquisitionsState s = State;
            StakeData stake = StakeBook.Find(s, company.UniqueID, company.SaveID);
            bool hadOrder = StakeBook.Order(s, company.UniqueID, company.SaveID) != null;
            if (stake == null && !hadOrder) return;

            double share = stake != null ? StakeBook.RemoveStake(s, company.UniqueID) : 0.0;
            StakeBook.RemoveOrder(s, company.UniqueID);
            if (share > 0.0)
            {
                AcquisitionMail.StakeLost(company.UniqueID, company.Name, share);
                RivalAcquisitionsApi.RaiseStakeChanged(company.UniqueID, 0.0);
            }
            ModLog.Info(Feature, "stake: '" + company.Name + "' went bankrupt, the stake of " + AcquisitionMail.Percent(share) + "% is written off");
        }

        // ---- dividends ----

        /// <summary><c>LateMonthPassed</c>: the dividends of the month before, and the write-off of stakes in companies that went bankrupt unnoticed.</summary>
        public void OnMonth()
        {
            AcquisitionsState s = State;
            if (s.stakes.Count == 0) return;

            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return;

            int year;
            int month;
            StakeBook.PreviousMonth(today, out year, out month);

            foreach (StakeData stake in new List<StakeData>(s.stakes))
            {
                var company = ModCompanies.Resolve(CompanyKey.ForAi(stake.targetUniqueId)) as GameAiCompany;
                if (company == null || company.SaveID != stake.targetSaveId) continue;   // stale: ignored, kept
                if (company.IsBankrupt)
                {
                    OnRivalBankrupt(company);
                    continue;
                }
                PayDividend(company, stake, year, month);
            }
        }

        private void PayDividend(GameAiCompany company, StakeData stake, int year, int month)
        {
            GameFinance.Report report = ReportOf(company.Finance, year, month);
            if (report == null) return;

            double profit = (double)report.TotalRevenues - report.TotalExpenses;
            double dividend = StakeMath.Dividend(stake.share, profit, company.MoneyAmount, Balance.Stake);
            if (!(dividend > 0.0) || !CapitalLedger.IsAvailable) return;

            company.MoneyAmount = (float)(company.MoneyAmount - dividend);
            CapitalLedger.Revenue(Feature, dividend, RevenueOrigin.Interest, "acq.dividend", (company.Name ?? "") + " " + year.ToString(CultureInfo.InvariantCulture) + "-" + month.ToString("00", CultureInfo.InvariantCulture));
            StakeBook.RecordDividend(stake, dividend);
            ModNotify.ShowKey(Feature, AcqKeys.StakeDividend, company.Name ?? "", AcquisitionMail.Money(dividend));
        }

        /// <summary>
        /// The profit of the company in the last full month (its report found by <c>Year</c>/<c>Month</c>, the same one the dividend is paid from); 0 when the company has no
        /// such report or the date is unknown. The dialog of a stake shows the dividend it would have earned.
        /// </summary>
        public static double LastMonthProfit(GameAiCompany company)
        {
            if (company == null) return 0.0;
            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return 0.0;

            int year;
            int month;
            StakeBook.PreviousMonth(today, out year, out month);
            GameFinance.Report report = ReportOf(company.Finance, year, month);
            return report == null ? 0.0 : (double)report.TotalRevenues - report.TotalExpenses;
        }

        /// <summary>The report of the company for the month, found by <c>Year</c>/<c>Month</c> (the game adds the new month's report on the same tick).</summary>
        private static GameFinance.Report ReportOf(GameFinance finance, int year, int month)
        {
            if (finance == null || finance.Reports == null) return null;
            for (int i = finance.Reports.Count - 1; i >= 0; i--)
            {
                GameFinance.Report report = finance.Reports[i];
                if (report != null && report.Year == year && report.Month == month) return report;
            }
            return null;
        }

        // ---- helpers ----

        private static void AddReputation(int delta)
        {
            GamePlayer player = GamePlayer.Instance;
            if (player != null && player.Company != null) ModReputation.Add(player.Company, delta, AcqKeys.ReputationRescue);
        }

        private static StakeOutcome Refuse(StakeOutcome outcome, string key, string arg)
        {
            outcome.Ok = false;
            outcome.ReasonKey = key ?? AcqKeys.ReasonUnsupported;
            outcome.ReasonArg = arg;
            return outcome;
        }
    }
}
