using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using GameAiCompany = ProcessorTycoon.CompanySystem.AICompany;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameDataFinder = ProcessorTycoon.DataFinder;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using ICompanyOwner = ProcessorTycoon.CompanySystem.ICompanyOwner;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// <c>RivalDistressMonitor</c> (TSK-013; FR-3, FR-11, FR-13; design.md "RivalDistressMonitor"): the troubles of the rivals. The rules (one mail per
    /// episode, the auctions, the backfill, what is not a bankruptcy of the rival's own) are <see cref="DistressRules"/>'s.
    /// <list type="bullet">
    /// <item><b>Default</b> (P1, postfix of <c>BankruptcyHandler.TriggerDefault</c>, AI only, not while a save loads or the new game fast-forwards): the
    /// mail "X is in default" once per episode, <c>RivalDefaulted</c>.</item>
    /// <item><b>Before the bankruptcy</b> (P2, prefix of <c>AICompany.TriggerBankruptcy</c>, not for the target of a deal): the <c>SaveID</c> of the CPUs still
    /// on sale, kept until the bankruptcy event of the same tick.</item>
    /// <item><b>Bankruptcy</b> (<c>BankruptcyHandler.OnCompanyWentBankrupt</c>): the auction for <c>deals.bankrupt.auctionDays</c> with those CPUs, the mail,
    /// <c>RivalBankrupt</c>, the write-off of the player's stake. Nothing for the player, the target of a deal, a company the player bought, and
    /// nothing while loading or fast-forwarding (FR-11 item 3).</item>
    /// <item><b>Backfill</b> on the first load of a save made before the feature: auctions for its bankrupt companies, without CPUs (FR-3 item 5).</item>
    /// <item><b>Monthly</b>: the expired auctions are closed, the episodes of the companies out of default end.</item>
    /// </list>
    /// </summary>
    internal sealed class RivalDistressMonitor
    {
        private const string Feature = RivalAcquisitionsFeature.FeatureId;

        private readonly RivalAcquisitionsFeature _feature;

        /// <summary>The CPUs on sale just before a bankruptcy (P2), by <c>SaveID</c> of the company, until the bankruptcy event of the same tick.</summary>
        private readonly Dictionary<int, List<int>> _pending = new Dictionary<int, List<int>>();

        public RivalDistressMonitor(RivalAcquisitionsFeature feature)
        {
            _feature = feature;
        }

        private AcquisitionsState State
        {
            get { return _feature.State; }
        }

        // ---- P1 ----

        public void OnAiDefault(GameAiCompany company)
        {
            if (company == null || company.IsPlayer || company.IsBankrupt) return;
            if (ModTicks.IsLoading || ModTicks.IsFastForwarding) return;

            AcquisitionsState s = State;
            if (DistressRules.ShouldMailDefault(s, company.SaveID))
            {
                DistressRules.MarkDefaultMailed(s, company.SaveID);
                bool withRescue = StakeBook.Find(s, company.UniqueID, company.SaveID) != null;
                AcquisitionMail.Default(company, withRescue);
            }

            ModLog.Debug(Feature, "distress: '" + company.Name + "' (ai:" + Id(company.UniqueID) + ") went into default");
            RivalAcquisitionsApi.RaiseRivalDefaulted(company.UniqueID);
        }

        // ---- P2 ----

        public void BeforeAiBankruptcy(GameAiCompany company)
        {
            if (company == null || ReferenceEquals(company, _feature.ExecutingTarget)) return;
            if (ModTicks.IsLoading || ModTicks.IsFastForwarding) return;

            var ids = new List<int>();
            ICompanyOwner owner = company.Owner;
            List<GameCpu> cpus = owner != null ? owner.GetCpus() : null;
            if (cpus != null)
            {
                foreach (GameCpu cpu in cpus)
                {
                    if (cpu != null && !cpu.IsRetired) ids.Add(cpu.SaveID);
                }
            }
            _pending[company.SaveID] = ids;
        }

        // ---- the bankruptcy event ----

        public void OnCompanyWentBankrupt(ICompany company)
        {
            var ai = company as GameAiCompany;
            if (ai == null) return;

            List<int> snapshot;
            _pending.TryGetValue(ai.SaveID, out snapshot);
            _pending.Remove(ai.SaveID);

            AcquisitionsState s = State;
            bool executing = ReferenceEquals(ai, _feature.ExecutingTarget);
            bool absorbed = DistressRules.IsAbsorbedPair(s, ai.UniqueID, ai.SaveID);
            if (DistressRules.IgnoresBankruptcy(ai.IsPlayer, executing, absorbed, ModTicks.IsLoading, ModTicks.IsFastForwarding)) return;

            DateTime today = ModTicks.Today;
            AuctionData auction = DistressRules.OpenAuction(s, ai.UniqueID, ai.SaveID, today, _feature.Balance.Deals.Bankrupt.AuctionDays, snapshot);
            ModLog.Info(Feature, "distress: '" + ai.Name + "' (ai:" + Id(ai.UniqueID) + ") went bankrupt; auction until " + auction.until
                                 + " with " + Id(auction.snapshotCpuSaveIds.Count) + " CPU(s)");

            AcquisitionMail.Auction(ai, auction);
            RivalAcquisitionsApi.RaiseRivalBankrupt(ai.UniqueID);
            _feature.Stakes.OnRivalBankrupt(ai);
        }

        // ---- backfill ----

        /// <summary>
        /// Opens the auctions of the companies that were bankrupt before the feature (FR-3 item 5), from today, without CPUs, and marks the backfill
        /// done. No mail: the save may have many of them. Returns how many were opened.
        /// </summary>
        public int BackfillAuctions()
        {
            AcquisitionsState s = State;
            DateTime today = ModTicks.Today;
            int opened = 0;
            if (today != default(DateTime))
            {
                foreach (ICompany company in GameDataFinder.FindAllAICompanies(true))
                {
                    var ai = company as GameAiCompany;
                    if (ai == null || !DistressRules.NeedsBackfill(s, ai.UniqueID, ai.SaveID, ai.IsBankrupt)) continue;
                    DistressRules.OpenAuction(s, ai.UniqueID, ai.SaveID, today, _feature.Balance.Deals.Bankrupt.AuctionDays, null);
                    opened++;
                }
            }
            s.backfillDone = true;
            if (opened > 0) ModLog.Info(Feature, "distress: auctions opened for " + Id(opened) + " compan(y/ies) bankrupt before the feature");
            return opened;
        }

        // ---- monthly ----

        public void OnMonth()
        {
            AcquisitionsState s = State;
            int closed = DistressRules.CloseExpired(s, ModTicks.Today);
            if (closed > 0) ModLog.Debug(Feature, "distress: " + Id(closed) + " auction(s) expired");

            if (s.defaultMailSentForSaveIds.Count > 0)
            {
                var inDefault = new HashSet<int>();
                foreach (ICompany company in GameDataFinder.FindAllAICompanies(false))
                {
                    if (company != null && !company.IsBankrupt && company.IsInDefault) inDefault.Add(company.SaveID);
                }
                DistressRules.EndEpisodes(s, inDefault.Contains);
            }
            _pending.Clear();
        }

        /// <summary>The scene goes: the CPUs kept for a bankruptcy belong to it.</summary>
        public void Clear()
        {
            _pending.Clear();
        }

        private static string Id(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
