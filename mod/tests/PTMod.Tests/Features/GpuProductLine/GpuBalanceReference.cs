using System;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The CPU business of the reference game of NFR-7 ("IDM, start 1990, Normal, the division founded in 1995"), the figure the revenue of the cards is set against: the feature does not
    /// model processors, so the test needs a number for "the total revenue of the player" and takes it from the game's own market. The four segments of the CPU market of the game
    /// (<c>PopulationData</c>: High End, Mid Range, Low End, Industries) have a base number of buyers, a budget of a buyer and a multiplier for each decade from the 1970s to the 2030s;
    /// the buyers of a year are <c>baseSize x Lerp(multiplier of the decade, multiplier of the next, t)</c> with <c>t</c> the part of the decade gone (<c>PopulationData.GetCurrentSize</c>,
    /// <c>DateHelper.DecadeTransitionValue</c>), and a buyer pays at most the budget. The pool is what all the buyers of a year can pay for CPUs; the reference IDM holds
    /// <see cref="ReferenceShare"/> of the money (a mid-size IDM; a player who keeps his CPUs competitive holds 10-30 % in the game, 15 % is a company of the middle) and sells at <see cref="PriceToBudget"/> of the
    /// budget, the proposal of the release dialog of the cards (0.9). The numbers are from the decompiled game (<c>CODEMAP.md</c>, MarketSystem), not from the feature.
    /// </summary>
    internal static class GpuBalanceReference
    {
        /// <summary>The share of the CPU money the reference IDM holds.</summary>
        public const double ReferenceShare = 0.15;

        /// <summary>The price against the budget the reference IDM sells at.</summary>
        public const double PriceToBudget = 0.9;

        private static readonly double[] BaseSize = { 4e6, 4e6, 8e6, 40e6 };
        private static readonly double[] Budget = { 599, 249, 149, 79 };

        // the multipliers of the decades 1970s, 1980s, ..., 2030s
        private static readonly double[][] Multipliers =
        {
            new[] { 0.3, 1.5, 3, 7, 9, 13, 15 },
            new[] { 0.1, 1, 2, 6, 10, 15, 18 },
            new[] { 0.01, 0.05, 0.3, 2, 4, 7, 9 },
            new[] { 0.05, 0.225, 0.5, 1.5, 2.5, 3.0, 3.6 },
        };

        /// <summary>The buyers of the CPUs in a year (all four segments), the middle of the year.</summary>
        public static double Buyers(int year)
        {
            double sum = 0;
            for (int s = 0; s < BaseSize.Length; s++) sum += BaseSize[s] * Multiplier(s, year);
            return sum;
        }

        /// <summary>What the buyers of the CPUs of a year can pay at the budget: the pool of the CPU market, money of the year.</summary>
        public static double Pool(int year)
        {
            double sum = 0;
            for (int s = 0; s < BaseSize.Length; s++) sum += BaseSize[s] * Multiplier(s, year) * Budget[s];
            return sum;
        }

        /// <summary>The revenue of the reference IDM from the CPUs in a year (the budgets of the CPU market of the game are constants, like the budgets of the content of the cards: both sides are in the same money).</summary>
        public static double CpuRevenue(int year, double share = ReferenceShare)
        {
            return Pool(year) * share * PriceToBudget;
        }

        private static double Multiplier(int segment, int year)
        {
            int decade = Math.Max(0, Math.Min(Multipliers[segment].Length - 1, (year - 1970) / 10));
            int next = Math.Min(Multipliers[segment].Length - 1, decade + 1);
            double t = Math.Max(0, Math.Min(1, ((year - 1970) % 10 + 0.5) / 10.0));
            return Multipliers[segment][decade] + (Multipliers[segment][next] - Multipliers[segment][decade]) * t;
        }
    }
}
