using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// One CPU of a company as the predicates of the pack see it (FR-12): the flags that say whether it is on sale and the numbers they compare. The game
    /// adapter fills it from <c>CpuBase.SellOnMarket</c>, <c>IsReleased</c>, <c>IsRetired</c>, <c>SoldLastDay</c>, <c>Price</c>, <c>Year</c> and
    /// <c>Cpu.get_RealUnitCost()</c>.
    /// </summary>
    public readonly struct CpuSalesFacts
    {
        public CpuSalesFacts(bool sellOnMarket, bool isReleased, bool isRetired, int soldLastDay, float price, float realUnitCost, int year)
        {
            SellOnMarket = sellOnMarket;
            IsReleased = isReleased;
            IsRetired = isRetired;
            SoldLastDay = soldLastDay;
            Price = price;
            RealUnitCost = realUnitCost;
            Year = year;
        }

        public bool SellOnMarket { get; }

        public bool IsReleased { get; }

        public bool IsRetired { get; }

        /// <summary>Units sold on the last simulated day.</summary>
        public int SoldLastDay { get; }

        /// <summary>The price of the CPU on the market.</summary>
        public float Price { get; }

        /// <summary>The real cost of making one unit (0 when the source was told not to work it out).</summary>
        public float RealUnitCost { get; }

        /// <summary><c>CpuBase.Year</c>: the year the CPU was created.</summary>
        public int Year { get; }

        /// <summary>On sale: released, not retired, sold on the market.</summary>
        public bool IsOnMarket
        {
            get { return SellOnMarket && IsReleased && !IsRetired; }
        }
    }

    /// <summary>The list of the CPUs of a company for the predicates: the game's <c>CpuDataProvider</c> in the mod, a list in the tests.</summary>
    public interface IShockCpuSource
    {
        /// <summary>
        /// Fills <paramref name="into"/> (cleared first) with the CPUs of the company with this key. False when the data is not available (the CPU
        /// provider or the company does not exist), then the predicate is false. <paramref name="needUnitCost"/> asks for <see cref="CpuSalesFacts.RealUnitCost"/>,
        /// which costs a calculation per CPU - the source may leave it 0 for the CPUs that cannot qualify anyway (not on sale, nothing sold).
        /// </summary>
        bool TryGetCpus(string companyKey, bool needUnitCost, List<CpuSalesFacts> into);
    }

    /// <summary>
    /// The logic of the two predicates of the pack (FR-12, design.md, "Предикаты"), without the game: <c>ms.sellsBelowCost</c> and
    /// <c>ms.hasRecentCpu</c>. The parameters are on the top level of the node (<c>{ "type": "ms.sellsBelowCost", "priceRatio": 0.95 }</c>) and are
    /// checked on every call: a parameter of the wrong kind or out of range makes the predicate false (a broken condition never fires), and
    /// <see cref="Validate"/> reports it for the content checks. The market share comes from <c>IGameState.MarketShare</c>, which is null when
    /// <c>MarketDataProvider.Instance</c> is missing, so a missing source is false and never an exception (FR-12 item 3).
    /// </summary>
    public static class ShockPredicateLogic
    {
        /// <summary>The name of the predicate "sells below cost", with the prefix of the feature (<c>ModIds.IsPredicateNameAllowed</c>).</summary>
        public const string SellsBelowCostName = "ms.sellsBelowCost";

        /// <summary>The name of the predicate "has a recent CPU".</summary>
        public const string HasRecentCpuName = "ms.hasRecentCpu";

        /// <summary>The CPU sells below cost when its price is under this share of its real unit cost.</summary>
        public const double DefaultPriceRatio = 0.95;

        /// <summary>The company must hold at least this share of the market (0.05 is 5 %).</summary>
        public const double DefaultMinShare = 0.05;

        /// <summary>A CPU is recent when its year is at most this many years before the current one.</summary>
        public const int DefaultMaxAgeYears = 1;

        public const double MaxPriceRatio = 2.0;
        public const int MaxMaxAgeYears = 60;

        /// <summary>
        /// <c>ms.sellsBelowCost</c>: true when the company in question holds at least <c>minShare</c> of the market (the share of the last simulated day)
        /// and has a CPU on sale that sold something yesterday at a price under <c>realUnitCost · priceRatio</c>.
        /// </summary>
        public static bool SellsBelowCost(JsonObject args, ConditionContext ctx, IShockCpuSource cpus)
        {
            if (ctx == null || ctx.State == null || cpus == null) return false;

            double priceRatio, minShare;
            if (!ReadSellsBelowCost(args, null, out priceRatio, out minShare)) return false;

            double? share = ctx.State.MarketShare(ctx.SubjectKey, MarketWindow.Day);
            if (!share.HasValue || double.IsNaN(share.Value) || share.Value < minShare) return false;

            var list = new List<CpuSalesFacts>();
            if (!cpus.TryGetCpus(ctx.SubjectKey, true, list)) return false;

            for (int i = 0; i < list.Count; i++)
            {
                CpuSalesFacts cpu = list[i];
                if (!cpu.IsOnMarket || cpu.SoldLastDay <= 0) continue;
                if ((double)cpu.Price < (double)cpu.RealUnitCost * priceRatio) return true;
            }
            return false;
        }

        /// <summary>
        /// <c>ms.hasRecentCpu</c>: true when the company in question has a CPU on sale whose year is at most <c>maxAgeYears</c> before the current year
        /// (a CPU of 1994 is recent in 1995 and not in 1996 with the default of 1). A CPU without a year (0) never counts; no date, no answer.
        /// </summary>
        public static bool HasRecentCpu(JsonObject args, ConditionContext ctx, IShockCpuSource cpus)
        {
            if (ctx == null || !ctx.HasDate || cpus == null) return false;

            int maxAge;
            if (!ReadHasRecentCpu(args, null, out maxAge)) return false;

            var list = new List<CpuSalesFacts>();
            if (!cpus.TryGetCpus(ctx.SubjectKey, false, list)) return false;

            int year = ctx.Today.Year;
            for (int i = 0; i < list.Count; i++)
            {
                CpuSalesFacts cpu = list[i];
                if (!cpu.IsOnMarket || cpu.Year <= 0) continue;
                if (year - cpu.Year <= maxAge) return true;
            }
            return false;
        }

        /// <summary>
        /// Checks the parameters of a node of one of the two predicates for the content checks (the engine accepts any parameters of a predicate of a
        /// feature, this is where a typo is found). Adds one English line per problem to <paramref name="errors"/>; true when there is none. A node of
        /// another type is not this method's business (true).
        /// </summary>
        public static bool Validate(JsonObject node, IList<string> errors)
        {
            if (node == null) return true;

            string type = node.GetString("type", null);
            var problems = errors ?? new List<string>();
            int before = problems.Count;
            double a, b;
            int years;
            if (type == SellsBelowCostName) ReadSellsBelowCost(node, problems, out a, out b);
            else if (type == HasRecentCpuName) ReadHasRecentCpu(node, problems, out years);
            return problems.Count == before;
        }

        private static bool ReadSellsBelowCost(JsonObject args, IList<string> errors, out double priceRatio, out double minShare)
        {
            IList<string> problems = errors ?? new List<string>();
            int before = problems.Count;
            ParamReader.OptionalNumber(args, SellsBelowCostName, "priceRatio", DefaultPriceRatio, 0.0, MaxPriceRatio, problems, out priceRatio);
            ParamReader.OptionalNumber(args, SellsBelowCostName, "minShare", DefaultMinShare, 0.0, 1.0, problems, out minShare);
            if (problems.Count == before && !(priceRatio > 0.0)) problems.Add(ParamReader.Where(SellsBelowCostName, "priceRatio") + " must be above 0");
            return problems.Count == before;
        }

        private static bool ReadHasRecentCpu(JsonObject args, IList<string> errors, out int maxAgeYears)
        {
            IList<string> problems = errors ?? new List<string>();
            int before = problems.Count;
            ParamReader.OptionalInt(args, HasRecentCpuName, "maxAgeYears", DefaultMaxAgeYears, 0, MaxMaxAgeYears, problems, out maxAgeYears);
            return problems.Count == before;
        }
    }
}
