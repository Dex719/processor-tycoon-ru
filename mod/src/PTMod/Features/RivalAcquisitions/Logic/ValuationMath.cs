using System;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The valuation of a rival for the player (FR-1): the parts and the total. All in dollars, all <c>double</c>.
    /// <c>E = (AFab + ATech + ACpu) × (1 + kShare × Share)</c>, never below <c>EMin</c>.
    /// </summary>
    public struct Valuation
    {
        /// <summary><c>kFab × ManufacturingDivisionValue</c>: the factory.</summary>
        public double AFab;

        /// <summary><c>kTech × Σ Cost × Time / 30</c> over the technologies the player lacks.</summary>
        public double ATech;

        /// <summary><c>kCpu × Σ income of the last months × margin</c>: the products.</summary>
        public double ACpu;

        /// <summary>The market share <c>s</c> that was used: the larger of the market share and the foundry market share; 0 for a bankrupt company. A fraction, not money.</summary>
        public double Share;

        /// <summary>The premium for the market share in dollars: <c>(AFab + ATech + ACpu) × kShare × Share</c>.</summary>
        public double SharePremium;

        /// <summary>E: the operating assets for the player, at least <see cref="EMin"/>.</summary>
        public double E;

        /// <summary>E_min: <c>floorDebtLimitShare × D</c> of the target.</summary>
        public double EMin;

        /// <summary>The valuation was raised to <see cref="EMin"/> (the parts together were worth less).</summary>
        public bool FloorApplied;
    }

    /// <summary>
    /// <c>ValuationMath</c> (FR-1, design.md "Оценка"): what a rival's operating assets are worth to the player. Pure, in <c>double</c>, no game types.
    /// <list type="bullet">
    /// <item>Factory: <c>kFab × ManufacturingDivisionValue</c> (the game's sale cost of the lines; at <c>kFab</c> 2 a working factory is 30% of the cost of building it).</item>
    /// <item>Technologies: only the gap, what the target has and the player lacks, at <c>Cost × Time / 30</c> (decision D3: the player pays for what the player gets).</item>
    /// <item>CPUs: the income of the last months times the margin <c>clamp((Price - RealUnitCost) / Price, 0, 1)</c>; CPUs of a bankrupt company's snapshot count
    /// <c>bankruptSnapshotCpuFactor</c> of that (0.5), a retired CPU of a company still alive counts nothing.</item>
    /// <item>Share: <c>max(MarketShare, FoundryMarketShare)</c>, 0 for a bankrupt company, as a premium of <c>kShare</c>.</item>
    /// <item>Floor: <c>E_min = floorDebtLimitShare × D</c> of the target.</item>
    /// </list>
    /// Bad numbers in a snapshot (NaN, infinity, negative money) count as 0 so a broken read never gives a negative or NaN price.
    /// </summary>
    public static class ValuationMath
    {
        public static Valuation Evaluate(CompanySnapshot t, PlayerSnapshot p, ValuationConfig c)
        {
            var v = new Valuation();
            if (c == null) c = new ValuationConfig();
            if (t == null) return v;

            bool bankrupt = t.IsBankrupt;

            v.AFab = c.KFab * Clean(t.ManufacturingDivisionValue);
            v.ATech = c.KTech * TechGap(t);
            v.ACpu = c.KCpu * CpuIncome(t, bankrupt, c.BankruptSnapshotCpuFactor);

            double operating = v.AFab + v.ATech + v.ACpu;
            v.Share = bankrupt ? 0.0 : Math.Max(ClampUnit(t.MarketShare), ClampUnit(t.FoundryMarketShare));
            v.SharePremium = operating * c.KShare * v.Share;
            v.EMin = c.FloorDebtLimitShare * Clean(t.CreditLimit);

            double e = operating + v.SharePremium;
            if (e < v.EMin)
            {
                e = v.EMin;
                v.FloorApplied = true;
            }
            v.E = e;
            return v;
        }

        /// <summary><c>Σ Cost × Time / 30</c> over the technology gap.</summary>
        public static double TechGap(CompanySnapshot t)
        {
            double sum = 0.0;
            if (t == null || t.TechGap == null) return sum;
            for (int i = 0; i < t.TechGap.Count; i++)
            {
                TechSnapshot tech = t.TechGap[i];
                if (tech == null) continue;
                sum += Clean(tech.Cost) * Clean(tech.Time) / 30.0;
            }
            return sum;
        }

        /// <summary>The margin of a CPU: <c>clamp((Price - RealUnitCost) / Price, 0, 1)</c>; 0 for a price that is not positive.</summary>
        public static double Margin(double price, double realUnitCost)
        {
            if (!IsFinite(price) || !IsFinite(realUnitCost) || price <= 0.0) return 0.0;
            return ClampUnit((price - realUnitCost) / price);
        }

        private static double CpuIncome(CompanySnapshot t, bool bankrupt, double snapshotFactor)
        {
            double sum = 0.0;
            if (t.Cpus == null) return sum;
            for (int i = 0; i < t.Cpus.Count; i++)
            {
                CpuSnapshot cpu = t.Cpus[i];
                if (cpu == null) continue;

                double part;
                if (bankrupt)
                {
                    // Only the CPUs that were on sale when the company went under count; a base kept for a family is not an asset of its own.
                    if (!cpu.ActiveAtBankruptcy) continue;
                    part = Clean(cpu.Income12) * Margin(cpu.Price, cpu.RealUnitCost) * ClampUnit(snapshotFactor);
                }
                else
                {
                    if (cpu.IsRetired) continue;
                    part = Clean(cpu.Income12) * Margin(cpu.Price, cpu.RealUnitCost);
                }
                sum += part;
            }
            return sum;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>The value, or 0 when it is NaN, infinite or negative.</summary>
        private static double Clean(double value)
        {
            return IsFinite(value) && value > 0.0 ? value : 0.0;
        }

        private static double ClampUnit(double value)
        {
            if (!IsFinite(value)) return 0.0;
            return value < 0.0 ? 0.0 : (value > 1.0 ? 1.0 : value);
        }
    }
}
