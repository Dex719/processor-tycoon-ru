using System;
using System.Collections.Generic;

namespace PTMod.Features.RivalAcquisitions
{
    // The snapshots (TSK-005, FR-1, FR-2): plain data that the game adapter fills once and the pure logic (ValuationMath, DealRules,
    // AcquisitionPlanBuilder, StakeMath) reads. They hold no game object and no Unity type, so the logic is tested without the game.
    // Money is double (the game keeps float; the logic computes in double). A snapshot is a picture of one moment: the executor takes a new
    // one right before it changes anything. Public fields so that the tests and the offline simulation can set them.

    /// <summary>Where a rival stands for the deals (design.md, "State: статус компании ИИ для сделок").</summary>
    public enum CompanyStatus
    {
        /// <summary>Not in default, not bankrupt: a hostile takeover or a stake is possible.</summary>
        Healthy,

        /// <summary>In default (<c>IsInDefault</c>), not yet bankrupt: it can be bought with its debts.</summary>
        InDefault,

        /// <summary>Bankrupt and the auction of its assets is open (FR-3).</summary>
        BankruptAuction,

        /// <summary>Bankrupt, no auction (never opened, bought elsewhere or expired).</summary>
        Bankrupt,

        /// <summary>Taken out of the game by a deal of the player (FR-10): bought assets, bought in default or taken over.</summary>
        Absorbed,
    }

    /// <summary>The lines of one wafer size of a factory (a company's or the player's) and what they are worth.</summary>
    public sealed class WaferLinesSnapshot
    {
        /// <summary><c>WaferSize.ID</c> (it is the <c>ID</c> of the technology of the wafer size).</summary>
        public int WaferId;

        /// <summary>The wafer diameter in mm (<c>WaferSize.Value</c>).</summary>
        public double Value;

        /// <summary>How many lines of this wafer size the factory has (0 for a wafer size the company knows but has no lines of).</summary>
        public int Lines;

        /// <summary><c>ProductionMath.SaleCost(Lines, wafer)</c>: what the game pays for selling these lines.</summary>
        public double SaleCost;

        /// <summary><c>WaferSize.MaintainanceCostMultiplier</c>: the monthly upkeep of a line of this wafer size is the base upkeep times this.</summary>
        public double MaintenanceMultiplier = 1.0;
    }

    /// <summary>A technology of the gap: the target has researched it and the player has not (FR-1 item 2).</summary>
    public sealed class TechSnapshot
    {
        /// <summary>The game technology type of a wafer size (<c>TechnologyType</c> 5). Wafer technologies decide where the lines can go.</summary>
        public const int WaferType = 5;

        /// <summary><c>Technology.ID</c>.</summary>
        public int Id;

        public string Name = "";

        /// <summary><c>Technology.Cost</c>: the monthly cost at funding 1.</summary>
        public double Cost;

        /// <summary><c>Technology.Time</c>: the days.</summary>
        public double Time;

        /// <summary>The game's <c>TechnologyType</c> as a number (0 package, 1 process node, 2 memory, 3 frequency, 4 cache, 5 wafer, 6 multicore).</summary>
        public int Type;

        /// <summary>
        /// How deep the technology sits in the dependency tree: 0 without dependencies, otherwise 1 + the deepest dependency. Unlocking in this
        /// order keeps one pass of <c>ResearchSector.Update(true)</c> per level (a pass opens one level of dependencies; the spike found chains of 26).
        /// </summary>
        public int Depth;

        public bool IsWafer
        {
            get { return Type == WaferType; }
        }

        /// <summary>The full price of the research, <c>Cost × Time / 30</c> (design.md, "Технологии").</summary>
        public double ResearchValue
        {
            get { return Cost * Time / 30.0; }
        }
    }

    /// <summary>A technology neither side has finished, but the target is further along (FR-7 item 1: the player's progress is raised to the target's).</summary>
    public sealed class TechProgressSnapshot
    {
        public int Id;
        public string Name = "";

        /// <summary>The target's progress, 0 to 1.</summary>
        public double TargetProgress;

        /// <summary>The player's progress, 0 to 1.</summary>
        public double PlayerProgress;
    }

    /// <summary>The experience with one process node (<c>NodeOptimization.Progress</c>, 0 to 1) of the target and of the player (FR-7 item 2).</summary>
    public sealed class NodeSnapshot
    {
        /// <summary><c>NodeOptimization.NodeID</c>.</summary>
        public int NodeId;

        /// <summary><c>NodeOptimization.VariantID</c> (-1 without a variant).</summary>
        public int VariantId = -1;

        public string Name = "";
        public double TargetExperience;
        public double PlayerExperience;

        /// <summary>What <c>OptimizeNode</c> has to add to bring the player up to the target; 0 when the player is ahead.</summary>
        public double Gap
        {
            get { return TargetExperience > PlayerExperience ? TargetExperience - PlayerExperience : 0.0; }
        }
    }

    /// <summary>One CPU of the target (or of a bankrupt target's snapshot, FR-3 item 3) with what the valuation and the transfer need.</summary>
    public sealed class CpuSnapshot
    {
        /// <summary><c>Cpu.SaveID</c>.</summary>
        public int SaveId;

        /// <summary><c>SaveID</c> of the base CPU of a variant; -1 for a base CPU (<c>Cpu.Base == null</c>).</summary>
        public int BaseSaveId = -1;

        public string Name = "";

        /// <summary>The CPU is retired now (<c>IsRetired</c>). Every CPU of a bankrupt company is, and so is a retired base that a live variant needs.</summary>
        public bool IsRetired;

        /// <summary>Bankrupt company only: the CPU was on sale just before the bankruptcy (it is in the snapshot of the auction) and can come back.</summary>
        public bool ActiveAtBankruptcy;

        /// <summary><c>Cpu.IsOutsourced</c>: another company makes it.</summary>
        public bool IsOutsourced;

        /// <summary><c>Cpu.OutsourcedID</c> (the <c>SaveID</c> of the maker), -1 when the owner makes it.</summary>
        public int OutsourcedId = -1;

        /// <summary>The name of the company that makes the CPU when it is outsourced (for the preview); empty otherwise.</summary>
        public string MakerName = "";

        /// <summary>The target makes the CPU in its own factory: the factory goes away with the lines when they are sold.</summary>
        public bool MadeInHouseByTarget;

        /// <summary>The player makes the CPU (a foundry contract of the player with the target): after the deal it is the owner's own production.</summary>
        public bool MadeByPlayer;

        public double Price;

        /// <summary><c>Cpu.RealUnitCost</c>.</summary>
        public double RealUnitCost;

        /// <summary>The income of the last <c>valuation.cpuIncomeMonths</c> full months (<c>CpuBase.MarketData.Income</c>); the month in progress is not counted.</summary>
        public double Income12;

        /// <summary>The active OEM contracts that chose this CPU (they follow the CPU, FR-8).</summary>
        public int OemContracts;

        public bool IsBase
        {
            get { return BaseSaveId < 0; }
        }
    }

    /// <summary>A foundry-services contract (<c>BusinessContract</c>) of the target, as a provider or as a client of a third party (FR-9).</summary>
    public sealed class ContractSnapshot
    {
        /// <summary>
        /// The place of the contract in the snapshot's own list of the game's contracts at the moment of the snapshot (the game has no id that tells
        /// two contracts of one type apart). The plan names contracts by it; only the snapshot it came from understands it.
        /// </summary>
        public int Id;

        /// <summary><c>SaveID</c> of the provider (the maker of the wafers).</summary>
        public int ProviderId;

        /// <summary><c>SaveID</c> of the client.</summary>
        public int ClientId;

        public string ProviderName = "";
        public string ClientName = "";

        public bool IsExclusive;

        /// <summary><c>BusinessContract.ProviderFines()</c>.</summary>
        public double ProviderFines;

        /// <summary>The share of the provider's capacity, <c>ContractValues[0]</c>.</summary>
        public int CapacityPercent;

        /// <summary>The markup over the cost in percent, <c>ContractValues[1]</c>.</summary>
        public int MarkupPercent;

        /// <summary>The month the contract ends, <c>yyyy-MM</c>.</summary>
        public string TerminatesOn = "";
    }

    /// <summary>What the logic needs to know about one AI company (design.md, "Снимки"). Built by <c>GameSnapshotAdapter</c>.</summary>
    public sealed class CompanySnapshot
    {
        /// <summary><c>AICompany.UniqueID</c>: the key other features use (0 Intel, 1 AMD, 3 Texas Instruments, 8 GlobalFoundries, 9 TSMC, 10 Cyrix).</summary>
        public int UniqueId;

        /// <summary><c>ICompany.SaveID</c>.</summary>
        public int SaveId;

        public string Name = "";

        public CompanyStatus Status;

        /// <summary><c>ICompany.DaysInDefault</c>; 0 when not in default.</summary>
        public int DaysInDefault;

        /// <summary>C: <c>MoneyAmount</c> (negative is a debt).</summary>
        public double Money;

        /// <summary>D: <c>ModCredit.DebtLimitOf(company)</c>, positive; the default threshold is <c>-D</c>. (Named so because only ModCredit may spell the bank's own member.)</summary>
        public double CreditLimit;

        /// <summary><c>ProductionMath.ManufacturingDivisionValue(company)</c>: the sale cost of all the lines.</summary>
        public double ManufacturingDivisionValue;

        /// <summary>The lines by wafer size (only the wafer sizes the company has lines of).</summary>
        public List<WaferLinesSnapshot> Lines = new List<WaferLinesSnapshot>();

        /// <summary>The technologies the target has researched and the player has not, in no particular order.</summary>
        public List<TechSnapshot> TechGap = new List<TechSnapshot>();

        /// <summary>The technologies neither has researched where the target is further along.</summary>
        public List<TechProgressSnapshot> PartialTech = new List<TechProgressSnapshot>();

        /// <summary>The process nodes where the target has experience and the player has the node.</summary>
        public List<NodeSnapshot> Nodes = new List<NodeSnapshot>();

        /// <summary>The CPUs that are not retired, or, for a bankrupt company, those of the auction's snapshot; plus the retired bases that a CPU of the list needs.</summary>
        public List<CpuSnapshot> Cpus = new List<CpuSnapshot>();

        /// <summary>The active contracts where the target is the provider (the client may be anyone but the player).</summary>
        public List<ContractSnapshot> ProviderContracts = new List<ContractSnapshot>();

        /// <summary>The active contracts where the target is the client of a third party (a contract with the player is not here: FR-9 item 5).</summary>
        public List<ContractSnapshot> ClientContracts = new List<ContractSnapshot>();

        /// <summary><c>SaveID</c> of the player's CPUs that the target makes in its factory.</summary>
        public List<int> PlayerCpuIdsMadeByTarget = new List<int>();

        /// <summary><c>MarketDataProvider.MarketShare(company)</c>, 0 to 1.</summary>
        public double MarketShare;

        /// <summary><c>MarketDataProvider.FoundryMarketShare(company)</c>, 0 to 1.</summary>
        public double FoundryMarketShare;

        public bool IsFoundry;
        public bool IsFabless;

        /// <summary>Another company (not the target) runs foundry services now.</summary>
        public bool HasOtherFoundry;

        /// <summary><c>Divisions.FoundryServices</c> of the target.</summary>
        public bool HadFoundryServices;

        public int PlayerCpusMadeByTarget
        {
            get { return PlayerCpuIdsMadeByTarget == null ? 0 : PlayerCpuIdsMadeByTarget.Count; }
        }

        public int TotalLines
        {
            get
            {
                int total = 0;
                if (Lines != null)
                {
                    for (int i = 0; i < Lines.Count; i++)
                    {
                        if (Lines[i] != null && Lines[i].Lines > 0) total += Lines[i].Lines;
                    }
                }
                return total;
            }
        }

        public bool IsBankrupt
        {
            get { return Status == CompanyStatus.BankruptAuction || Status == CompanyStatus.Bankrupt; }
        }
    }

    /// <summary>The player's side of a deal (design.md, "Снимки"). Built by <c>GameSnapshotAdapter.Player</c>.</summary>
    public sealed class PlayerSnapshot
    {
        /// <summary><c>ICompany.SaveID</c> of the player's company.</summary>
        public int SaveId;

        public double Money;

        /// <summary><c>ModCredit.PlayerDebtLimit()</c>, positive.</summary>
        public double CreditLimit;

        public double MarketShare;

        /// <summary>The player is in default: no deal is possible.</summary>
        public bool InDefault;

        public bool IsBankrupt;

        /// <summary><c>Divisions.Cpu</c>: the player develops CPUs and can take the target's.</summary>
        public bool IsCpuCompany;

        public bool IsFabless;
        public bool IsFoundry;

        /// <summary><c>Divisions.FoundryServices</c> of the player is on.</summary>
        public bool FoundryServicesEnabled;

        /// <summary>The player is the client of an exclusive contract already (<c>BusinessContractManager.HasAnyExclusivityDeal</c>).</summary>
        public bool HasExclusiveClientContract;

        /// <summary><c>Factory.ProductionCapacity</c>: the lines the player has.</summary>
        public int Capacity;

        /// <summary>The wafer sizes the player knows, with the player's lines of each (0 when none). Smallest first.</summary>
        public List<WaferLinesSnapshot> Wafers = new List<WaferLinesSnapshot>();

        public DateTime Today;

        /// <summary><c>Factory.OperatingCost</c> now (per month).</summary>
        public double OperatingCostPerMonth;

        /// <summary><c>ProductionMath.OperatingCost(1, player)</c>: the monthly upkeep of one line before the wafer size's multiplier.</summary>
        public double OperatingCostPerLine;

        /// <summary>GlobalFoundries (<c>UniqueID</c> 8) has appeared (FR-12 item 2).</summary>
        public bool GlobalFoundriesSpawned;

        public bool KnowsWafer(int waferId)
        {
            return FindWafer(waferId) != null;
        }

        public WaferLinesSnapshot FindWafer(int waferId)
        {
            if (Wafers == null) return null;
            for (int i = 0; i < Wafers.Count; i++)
            {
                if (Wafers[i] != null && Wafers[i].WaferId == waferId) return Wafers[i];
            }
            return null;
        }
    }

    /// <summary>
    /// Which status a company has for the deals: the one rule behind the "Status" of a snapshot (TSK-005 acceptance: the status comes from
    /// <c>IsBankrupt</c>, <c>IsInDefault</c>, the auctions and the journal). Pure, so the adapter and the deal rules agree.
    /// </summary>
    public static class CompanyStatusRules
    {
        /// <summary>
        /// A company bought by the player is <see cref="CompanyStatus.Absorbed"/> whatever else it is. A bankrupt one has an open auction when its
        /// record is not closed and its last day is not before <paramref name="today"/>; otherwise it is plain bankrupt. Then default, then healthy.
        /// </summary>
        public static CompanyStatus Resolve(bool isBankrupt, bool isInDefault, bool absorbed, AuctionData auction, int saveId, DateTime today)
        {
            if (absorbed) return CompanyStatus.Absorbed;
            if (isBankrupt) return IsAuctionOpen(auction, saveId, today) ? CompanyStatus.BankruptAuction : CompanyStatus.Bankrupt;
            return isInDefault ? CompanyStatus.InDefault : CompanyStatus.Healthy;
        }

        /// <summary>
        /// The auction record is usable for the company <paramref name="saveId"/> and is open on <paramref name="today"/>: not closed, the same
        /// company (a record of another <c>SaveID</c> is stale and ignored), and the day is not after <c>until</c>. An unknown today (default
        /// date) counts as open, so a missing clock never hides a deal.
        /// </summary>
        public static bool IsAuctionOpen(AuctionData auction, int saveId, DateTime today)
        {
            if (auction == null || auction.closed) return false;
            if (auction.targetSaveId != saveId) return false;

            DateTime until;
            if (!TryDate(auction.until, out until)) return false;
            return today == default(DateTime) || today.Date <= until.Date;
        }

        /// <summary>The auction record that is open for the company, or null (it may exist for the <c>UniqueID</c> yet be expired or stale).</summary>
        public static AuctionData OpenAuction(AcquisitionsState state, int uniqueId, int saveId, DateTime today)
        {
            if (state == null || state.auctions == null) return null;
            for (int i = 0; i < state.auctions.Count; i++)
            {
                AuctionData auction = state.auctions[i];
                if (auction != null && auction.targetUniqueId == uniqueId && IsAuctionOpen(auction, saveId, today)) return auction;
            }
            return null;
        }

        /// <summary>The share of the company the player owns for the stake record that belongs to this very company (the pair of ids matches); 0 for none or a stale record.</summary>
        public static double OwnedShare(AcquisitionsState state, int uniqueId, int saveId)
        {
            if (state == null || state.stakes == null) return 0.0;
            for (int i = 0; i < state.stakes.Count; i++)
            {
                StakeData stake = state.stakes[i];
                if (stake == null || stake.targetUniqueId != uniqueId) continue;
                if (stake.targetSaveId != saveId) return 0.0;   // a record of an earlier company with this UniqueID: stale
                return AcquisitionsState.IsValidShare(stake.share) ? stake.share : 0.0;
            }
            return 0.0;
        }

        private static bool TryDate(string text, out DateTime date)
        {
            return DateTime.TryParseExact(text, AcquisitionsState.DateFormat, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out date);
        }
    }
}
