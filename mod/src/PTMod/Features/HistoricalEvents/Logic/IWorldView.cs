using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// A company of the world as the calendar sees it (design.md, Component 4): a plain copy of what the roles and conditions ask, made by the game
    /// adapter (<c>GameWorldView</c>) from <c>ICompany</c> and by the tests from nothing. The company is never named in the logic except through
    /// <see cref="Key"/> (<c>player</c>, <c>ai:&lt;UniqueID&gt;</c>); <see cref="Name"/> is the game's own name, only for the texts.
    /// </summary>
    public sealed class CompanyInfo
    {
        /// <summary>The key of <c>ModCompanies</c>: <c>player</c> or <c>ai:&lt;UniqueID&gt;</c>.</summary>
        public string Key { get; set; }

        /// <summary><c>AICompany.UniqueID</c>; -1 for the player. It orders the ties (the player first, then the smaller id).</summary>
        public int UniqueId { get; set; } = -1;

        /// <summary>The name the game shows (<c>ICompany.Name</c>).</summary>
        public string Name { get; set; }

        public bool IsPlayer { get; set; }
        public bool IsBankrupt { get; set; }
        public bool IsFabless { get; set; }
        public bool IsFoundry { get; set; }

        /// <summary>The company has the manufacturing division (<c>Divisions.Manufacturing</c>): it can make chips itself.</summary>
        public bool HasManufacturing { get; set; }

        /// <summary>The company has the CPU division (<c>ICompany.IsCpuCompany</c>).</summary>
        public bool IsCpuCompany { get; set; }

        /// <summary><c>Factory.ProductionCapacity</c>: the production lines.</summary>
        public int Capacity { get; set; }

        /// <summary><c>FoundationDate</c> of the company.</summary>
        public int FoundedYear { get; set; }

        public int FoundedMonth { get; set; }

        public override string ToString()
        {
            return Key ?? "(no key)";
        }
    }

    /// <summary>
    /// A CPU of the world as the calendar sees it (design.md, Component 4). The monthly journals of <c>CpuBase.MarketData</c> are handed over as they
    /// are (the lists of the game, not copies): the share cache reads them once a month. A journal that is null counts as empty.
    /// </summary>
    public sealed class CpuInfo
    {
        /// <summary><c>CpuBase.SaveID</c>.</summary>
        public int SaveId { get; set; }

        /// <summary>The company that designed it (<c>Cpu.Company</c>); null when it has none.</summary>
        public string CompanyKey { get; set; }

        /// <summary><c>CpuBase.Year</c>: the year of the design.</summary>
        public int Year { get; set; }

        public bool Released { get; set; }
        public bool Retired { get; set; }

        /// <summary><c>SellOnMarket</c>.</summary>
        public bool OnMarket { get; set; }

        public float Ips { get; set; }
        public float Power { get; set; }
        public float Frequency { get; set; }
        public float Price { get; set; }
        public float RealUnitCost { get; set; }
        public int Cores { get; set; }

        /// <summary>The main segment of the CPU by the rule of the core (D15, <see cref="WorldMath.PrimarySegment"/>); <see cref="PTMod.Core.Segment.Any"/> when it cannot be said.</summary>
        public Segment Segment { get; set; } = Segment.Any;

        // The journals: one number per month, the last element is the month that is not over yet.
        public IReadOnlyList<int> Sold { get; set; }
        public IReadOnlyList<int> SoldByContracts { get; set; }
        public IReadOnlyList<int> SoldByHighEnd { get; set; }
        public IReadOnlyList<int> SoldByMidRange { get; set; }
        public IReadOnlyList<int> SoldByLowEnd { get; set; }
        public IReadOnlyList<int> SoldByIndustries { get; set; }

        public override string ToString()
        {
            return "cpu " + SaveId + " of " + (CompanyKey ?? "nobody");
        }
    }

    /// <summary>
    /// The adapter between the calendar and the game (design.md, Component 4): the roles, the conditions and the share cache see the world only through
    /// it, so the tests drive them with a fake. The real one is <c>GameWorldView</c>. Main thread only; the lists are snapshots (a caller may keep
    /// iterating one), refreshed at most once per game day.
    /// </summary>
    public interface IWorldView
    {
        /// <summary>The companies, the player first, then the AI companies by <c>UniqueID</c>; bankrupt ones are in the list with <c>IsBankrupt</c>.</summary>
        IReadOnlyList<CompanyInfo> Companies { get; }

        /// <summary>Every CPU of the game.</summary>
        IReadOnlyList<CpuInfo> Cpus { get; }

        /// <summary><c>DateController.MonthsSinceStart</c>.</summary>
        int MonthsSinceStart { get; }

        /// <summary>Today; <c>default</c> (not set) outside a running game.</summary>
        SimpleDate Today { get; }

        /// <summary><c>Market.AverageIps</c>.</summary>
        float AverageIps { get; }

        /// <summary>The revenue of the company over the 12 months before the current one (<c>CompanyFinance.Reports</c>); 0 for an unknown company.</summary>
        float Revenue12m(string companyKey);

        /// <summary>
        /// The money the company can still spend before it goes into default: <c>MoneyAmount − ModCredit.ThresholdOf(company)</c>, i.e. its money plus the
        /// debt limit the game really applies (<see cref="WorldMath.CashHeadroom"/>). Negative when it is already below the threshold; 0 for an unknown company.
        /// </summary>
        float CashHeadroom(string companyKey);

        /// <summary><c>ModFeatures.IsActive</c> (rival-personalities, design-flaw-recalls, gpu-product-line).</summary>
        bool IsFeatureActive(string featureId);
    }

    /// <summary>The segments a share is counted in, as bits of the segments of the core (<see cref="PTMod.Core.Segment"/>, numbers 0..3).</summary>
    [Flags]
    public enum SegmentMask
    {
        None = 0,
        HighEnd = 1,
        MidRange = 2,
        LowEnd = 4,
        Industries = 8,

        /// <summary>Every segment: the share is then counted over <c>Sold</c> and <c>SoldByContracts</c>, not over the four segment journals.</summary>
        All = HighEnd | MidRange | LowEnd | Industries,
    }

    /// <summary>The small pure rules that sit between the game's numbers and the calendar (the segment of a CPU, the cash headroom, the age of a CPU).</summary>
    public static class WorldMath
    {
        /// <summary>The mask of a role's segments: <see cref="PTMod.Core.Segment.Any"/> (or an empty list) is every segment; unknown values add nothing.</summary>
        public static SegmentMask MaskOf(IEnumerable<Segment> segments)
        {
            if (segments == null) return SegmentMask.All;

            SegmentMask mask = SegmentMask.None;
            bool any = false;
            foreach (Segment segment in segments)
            {
                any = true;
                switch (segment)
                {
                    case Segment.Any: return SegmentMask.All;
                    case Segment.HighEnd: mask |= SegmentMask.HighEnd; break;
                    case Segment.MidRange: mask |= SegmentMask.MidRange; break;
                    case Segment.LowEnd: mask |= SegmentMask.LowEnd; break;
                    case Segment.Industries: mask |= SegmentMask.Industries; break;
                }
            }
            return any ? mask : SegmentMask.All;
        }

        /// <summary>The mask of one segment; <see cref="PTMod.Core.Segment.Any"/> is every segment.</summary>
        public static SegmentMask MaskOf(Segment segment)
        {
            return MaskOf(new[] { segment });
        }

        /// <summary>
        /// The money headroom (design.md, <c>IWorldView.CashHeadroom</c>): <c>money − threshold</c> where the threshold is the negative number
        /// <c>ModCredit.ThresholdOf(company)</c>, so a company with 10 and a debt limit of 100 has 110. A threshold that is not a finite number counts as 0.
        /// </summary>
        public static float CashHeadroom(float money, float defaultThreshold)
        {
            if (float.IsNaN(money) || float.IsInfinity(money)) return 0f;
            float threshold = float.IsNaN(defaultThreshold) || float.IsInfinity(defaultThreshold) ? 0f : defaultThreshold;
            return money - threshold;
        }

        /// <summary>
        /// The main segment of a CPU (FR-3 item 7, decision D15): for a CPU of an AI company <c>AIStrategy.TargetMarket</c> (0..3 are the four segments);
        /// for a CPU of the player the segment where it sold most in the last full month, and when it sold nothing the segment of its price. A target
        /// market outside 0..3 falls back to the price too. The calendar keeps no classifier of its own: both segments come from the core
        /// (<paramref name="soldMost"/> is <c>ModSegments.FromSales</c> of the last full month, <paramref name="byPrice"/> is <c>ModSegments.Classify(cpu)</c>).
        /// </summary>
        /// <param name="isPlayerCpu">The CPU belongs to the player's company.</param>
        /// <param name="targetMarket"><c>CpuBase.AIStrategy.TargetMarket</c>, or -1 when the CPU has no strategy.</param>
        /// <param name="soldMost">The segment where the CPU sold most in the last full month; <see cref="PTMod.Core.Segment.Any"/> when it sold nothing.</param>
        /// <param name="byPrice">The segment by price; called only when it is needed; null counts as <see cref="PTMod.Core.Segment.Any"/>.</param>
        public static Segment PrimarySegment(bool isPlayerCpu, int targetMarket, Segment soldMost, Func<Segment> byPrice)
        {
            if (!isPlayerCpu && targetMarket >= (int)Segment.HighEnd && targetMarket <= (int)Segment.Industries) return (Segment)targetMarket;
            if (isPlayerCpu && soldMost != Segment.Any) return soldMost;

            return byPrice == null ? Segment.Any : byPrice();
        }

        /// <summary>
        /// The CPU was designed within the last <paramref name="months"/> months (FR-3: "released in the last 36 months"). The game keeps only the year
        /// of a CPU, so the check is by the calendar year: a CPU counts when its year is not before the year of the date <paramref name="months"/> months
        /// ago. A CPU that is not released never counts.
        /// </summary>
        public static bool IsRecent(CpuInfo cpu, SimpleDate today, int months)
        {
            if (cpu == null || !cpu.Released || !today.IsSet) return false;
            int monthIndex = today.Year * 12 + (today.Month - 1) - Math.Max(0, months);
            int cutoffYear = monthIndex / 12;
            return cpu.Year >= cutoffYear;
        }

        /// <summary>The CPU is on sale: released, not retired and sold on the market.</summary>
        public static bool IsOnMarket(CpuInfo cpu)
        {
            return cpu != null && cpu.Released && !cpu.Retired && cpu.OnMarket;
        }

        /// <summary>The month number of a foundation date (year * 12 + month − 1); bigger is later.</summary>
        public static int FoundedIndex(CompanyInfo company)
        {
            return company == null ? int.MinValue : company.FoundedYear * 12 + (company.FoundedMonth - 1);
        }
    }
}
