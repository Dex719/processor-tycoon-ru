using System;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// What the quote of a flaw is computed from (design.md Component 7): the sales and the costs of the CPU, read from the game by the monitor at the
    /// moment of the discovery. A plain value, so the formulas can be tested without the game.
    /// </summary>
    public struct QuoteInput
    {
        /// <summary>How bad the flaw is.</summary>
        public FlawSeverity Severity;

        /// <summary>The type of the flaw (patch availability and the loss of the patch); <c>null</c> reads as "no patch".</summary>
        public FlawTypeDef Type;

        /// <summary>The units of the affected CPUs sold so far: the sum of <c>MarketData.Sold</c> and <c>SoldByContracts</c>.</summary>
        public long AffectedUnits;

        /// <summary>The units of the affected CPUs in stock (<c>MarketData.Stock</c>).</summary>
        public int StockUnits;

        /// <summary><c>Cpu.RealUnitCost</c> of the base CPU.</summary>
        public float RealUnitCost;

        /// <summary><c>CpuBase.Price</c> of the base CPU.</summary>
        public float Price;

        /// <summary>The highest price among the variants: used when <see cref="Price"/> is 0 (the base design is not sold itself).</summary>
        public float MaxVariantPrice;

        /// <summary><c>CpuBase.DevelopmentCost</c> of the base CPU (with the rush premium, if any).</summary>
        public float DevelopmentCost;

        /// <summary><c>InflationHandler.Instance.Inflation</c> now.</summary>
        public float InflationNow;

        /// <summary>The inflation at the release, stored in the record; 0 reads as "unknown" (no correction of the development cost).</summary>
        public float InflationAtRelease;

        /// <summary>The revenue of the owner over the 12 closed months before the current one (<c>CompanyFinance.Report.TotalRevenues</c>).</summary>
        public float Revenue12m;

        /// <summary>The current year of the game (patch availability: <c>patchFromYear</c> of the type).</summary>
        public int Year;

        /// <summary>
        /// The flaw is not public (found by the company's own engineers): a revision costs no reputation then (<c>steppingQuiet</c> instead of
        /// <c>steppingPublic</c>). The default (<c>false</c>) is the public flaw of the design's example.
        /// </summary>
        public bool Quiet;
    }

    /// <summary>
    /// The prices of the reactions to a flaw (FR-5..FR-9, design.md Component 7, formula 4). Pure; every coefficient comes from <see cref="FlawBalance"/>.
    /// Money is computed in double precision and rounded to whole units of currency, so the amount under the button is the amount that is taken.
    /// </summary>
    public static class ResponseCostCalculator
    {
        /// <summary>The quote for a flaw. Never throws; a broken number in the input (NaN, a negative cost) reads as 0.</summary>
        public static FlawQuote Quote(QuoteInput input, FlawBalance b)
        {
            int sev = FlawMath.SeverityIndex(input.Severity);
            FlawBalance.ResponsesBalance rs = b.Responses;
            FlawBalance.ReputationBalance rep = b.Reputation;

            double units = input.AffectedUnits > 0 ? input.AffectedUnits : 0;
            double stock = input.StockUnits > 0 ? input.StockUnits : 0;
            double unitCost = NonNegative(input.RealUnitCost);
            double price = input.Price > 0f && FlawMath.IsFinite(input.Price) ? input.Price : NonNegative(input.MaxVariantPrice);

            // Inflation now multiplies the fixed costs; the development cost is brought from the prices of the release to the prices of today.
            double inflation = input.InflationNow > 0f && FlawMath.IsFinite(input.InflationNow) ? X(input.InflationNow) : 1.0;
            double atRelease = input.InflationAtRelease > 0f && FlawMath.IsFinite(input.InflationAtRelease) ? X(input.InflationAtRelease) : inflation;
            double developmentNow = NonNegative(input.DevelopmentCost) * (inflation / atRelease);

            double returnRate = X(FlawMath.At(rs.Recall.ReturnRate, sev, 0f));
            double perUnit = unitCost + X(rs.Recall.LogisticsShareOfPrice) * price;

            // Recall: the returned units, the rework of the stock and a fixed cost; never more than a share of the revenue of the last 12 months.
            double recall = units * returnRate * perUnit + stock * X(rs.Recall.StockReworkShare) * unitCost + X(rs.Recall.FixedCostBase) * inflation;
            bool capped = false;
            if (input.Revenue12m > 0f && FlawMath.IsFinite(input.Revenue12m))
            {
                double ceiling = X(rs.Recall.CapShareOfRevenue12m) * input.Revenue12m;
                if (recall > ceiling)
                {
                    recall = ceiling;
                    capped = true;
                }
            }

            // Stepping: a share of the development cost over a number of days, and the claims left after a public revision.
            double steppingCost = FlawMath.RoundMoney(X(FlawMath.At(rs.Stepping.CostShareOfDevCost, sev, 0f)) * developmentNow);
            int steppingDays = Math.Max(1, FlawMath.At(rs.Stepping.Days, sev, 1));
            double residual = X(rs.Stepping.ResidualShareOfRecall) * units * returnRate * perUnit;

            // Patch: informative also when the type has no patch in this year (PatchAvailable tells).
            double patchCost = X(rs.Patch.CostShareOfDevCost) * developmentNow + X(rs.Patch.FixedCostBase) * inflation;
            FlawTypeDef type = input.Type;
            float ipsLoss = type == null ? 0f : (float)FlawMath.Clamp(FlawMath.At(type.IpsLoss, sev, 0f), 0.0, 1.0);

            return new FlawQuote
            {
                RecallCost = (float)FlawMath.RoundMoney(recall),
                RecallCapped = capped,
                SteppingCost = (float)steppingCost,
                SteppingDays = steppingDays,
                SteppingDailyCost = (float)(steppingCost / steppingDays),
                ResidualClaimsCost = (float)FlawMath.RoundMoney(residual),
                PatchAvailable = type != null && type.Patchable && input.Year >= type.PatchFromYear,
                PatchCost = (float)FlawMath.RoundMoney(patchCost),
                PatchIpsLoss = ipsLoss,
                PatchMonths = FlawMath.At(rs.Patch.Months, sev, 0),
                ClassActionCost = (float)FlawMath.RoundMoney(X(rs.Ignore.SuitShareOfRevenue) * units * price),
                OemCancelChance = FlawMath.At(rs.Ignore.OemCancel, sev, 0f),
                IgnoreSignChanceMultiplier = FlawMath.At(rs.Ignore.SignChance, sev, 1f),
                ReputationRecall = FlawMath.At(rep.Recall, sev, 0),
                ReputationStepping = FlawMath.At(input.Quiet ? rep.SteppingQuiet : rep.SteppingPublic, sev, 0),
                ReputationPatch = FlawMath.At(rep.Patch, sev, 0),
                ReputationIgnore = FlawMath.At(rep.IgnorePublic, sev, 0),
                AffectedUnits = input.AffectedUnits > 0 ? input.AffectedUnits : 0,
            };
        }

        private static double X(float coefficient)
        {
            return FlawMath.Exact(coefficient);
        }

        private static double NonNegative(float value)
        {
            return FlawMath.IsFinite(value) && value > 0f ? value : 0.0;
        }
    }
}
