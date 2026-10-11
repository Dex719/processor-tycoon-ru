using System;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-012 (FR-3, FR-4, FR-5): the small rules of the asset sale that repeat the game's own window of the production lines: how many lines may go, when the
    /// sale of the whole production is closed, the margin the CPU list is ordered by, the warning about the contracts. The prices are the game's and are asked on
    /// its side, so they are not here.
    /// </summary>
    [Covers(typeof(AssetMath))]
    public class AssetMathTests
    {
        // ---- the lines ----

        [Test]
        public void MaxSellableLines_AllButTheLastLineOfTheFactory()
        {
            Assert.Equal(19, AssetMath.MaxSellableLines(20, 20, 1, false), "all lines of the factory are of this size: one stays (as in the game's slider)");
            Assert.Equal(5, AssetMath.MaxSellableLines(5, 20, 1, false), "other sizes remain: all of this size may go");
            Assert.Equal(19, AssetMath.MaxSellableLines(19, 20, 1, false));
            Assert.Equal(2, AssetMath.MaxSellableLines(2, 3, 1, false));
        }

        [Test]
        public void MaxSellableLines_AC_3_2_OneLineCannotBeSold()
        {
            Assert.Equal(0, AssetMath.MaxSellableLines(1, 1, 1, false));
            Assert.Equal(0, AssetMath.MaxSellableLines(0, 1, 1, false));
        }

        [Test]
        public void MaxSellableLines_TheMinimumComesFromTheBalanceFile()
        {
            Assert.Equal(17, AssetMath.MaxSellableLines(20, 20, 3, false));
            Assert.Equal(2, AssetMath.MaxSellableLines(5, 5, 3, false));
            Assert.Equal(0, AssetMath.MaxSellableLines(3, 3, 3, false));
            Assert.Equal(19, AssetMath.MaxSellableLines(20, 20, 0, false), "a minimum below 1 is 1: the game keeps a line");
            Assert.Equal(19, AssetMath.MaxSellableLines(20, 20, -4, false));
        }

        [Test]
        public void MaxSellableLines_AFablessCompanyHasNothingToSell()
        {
            Assert.Equal(0, AssetMath.MaxSellableLines(20, 20, 1, true));
        }

        [Test]
        public void MaxSellableLines_GarbageGivesZero()
        {
            Assert.Equal(0, AssetMath.MaxSellableLines(-3, 20, 1, false));
            Assert.Equal(0, AssetMath.MaxSellableLines(5, 0, 1, false));
            Assert.Equal(0, AssetMath.MaxSellableLines(5, -1, 1, false));
            Assert.Equal(int.MaxValue - 1, AssetMath.MaxSellableLines(int.MaxValue, int.MaxValue, 1, false), "no overflow");
        }

        [Test]
        public void LinesBlock_NamesTheReason()
        {
            Assert.Null(AssetMath.LinesBlock(20, 19));
            Assert.Equal(AssetMath.ReasonMinLines, AssetMath.LinesBlock(1, 0), "AC-3.2: at least 1 line must remain");
            Assert.Equal(AssetMath.ReasonNoLines, AssetMath.LinesBlock(0, 0), "no lines of this size");
            Assert.Equal("rec.lines.minOne", AssetMath.ReasonMinLines);
        }

        [Test]
        public void ClampLines_HoldsTheNumberToWhatMayBeSold()
        {
            Assert.Equal(5, AssetMath.ClampLines(5, 19));
            Assert.Equal(19, AssetMath.ClampLines(50, 19));
            Assert.Equal(0, AssetMath.ClampLines(-2, 19));
            Assert.Equal(0, AssetMath.ClampLines(0, 19));
            Assert.Equal(0, AssetMath.ClampLines(5, 0));
            Assert.Equal(0, AssetMath.ClampLines(5, -3));
        }

        [Test]
        public void CapacityAfter_AC_3_1_TwentyBecomeFifteen()
        {
            Assert.Equal(15, AssetMath.CapacityAfter(20, 5));
            Assert.Equal(20, AssetMath.CapacityAfter(20, 0));
            Assert.Equal(20, AssetMath.CapacityAfter(20, -4), "a negative number sells nothing");
            Assert.Equal(0, AssetMath.CapacityAfter(3, 10), "never below zero");
        }

        [Test]
        public void ContractsAtRisk_AC_3_3_EighteenOfTwentyLinesAreContracted()
        {
            int after = AssetMath.CapacityAfter(20, 5);
            Assert.True(AssetMath.ContractsAtRisk(after, 18), "15 lines left, 18 are promised: fines are possible");
            Assert.False(AssetMath.ContractsAtRisk(AssetMath.CapacityAfter(20, 2), 18), "exactly 18 left is enough");
            Assert.False(AssetMath.ContractsAtRisk(0, 0), "no contracts, no risk");
            Assert.False(AssetMath.ContractsAtRisk(0, -1));
        }

        // ---- the whole production ----

        [Test]
        public void DivisionBlock_IsOpenForAnIdmCompanyWithLines_AC_5_1()
        {
            Assert.Null(AssetMath.DivisionBlock(false, 30, 2));
            Assert.Null(AssetMath.DivisionBlock(false, 1, 0));
        }

        [Test]
        public void DivisionBlock_IsClosedOnTheHardestDifficulty_AC_5_2()
        {
            Assert.Equal(AssetMath.ReasonImpossible, AssetMath.DivisionBlock(false, 30, 5));
            Assert.Equal(AssetMath.ReasonImpossible, AssetMath.DivisionBlock(true, 0, 5), "the difficulty comes first, as in the game's button");
            Assert.Equal(5, AssetMath.HardestDifficulty);
        }

        [Test]
        public void DivisionBlock_IsClosedForAFoundryAndForNoLines()
        {
            Assert.Equal(AssetMath.ReasonFoundry, AssetMath.DivisionBlock(true, 30, 2));
            Assert.Equal(AssetMath.ReasonNoProduction, AssetMath.DivisionBlock(false, 0, 2));
            Assert.Equal(AssetMath.ReasonNoProduction, AssetMath.DivisionBlock(true, 0, 2));
            Assert.Equal(AssetMath.ReasonNoProduction, AssetMath.DivisionBlock(false, -1, 2));
        }

        // ---- the CPUs ----

        [Test]
        public void DailyMargin_IsPriceMinusCostTimesTheSales()
        {
            Assert.Near(5000.0, AssetMath.DailyMargin(100.0, 50.0, 100), 1e-9);
            Assert.Near(-5000.0, AssetMath.DailyMargin(50.0, 100.0, 100), 1e-9, "a loss is negative: those CPUs come first in the list");
            Assert.Near(0.0, AssetMath.DailyMargin(100.0, 50.0, 0), 0);
        }

        [Test]
        public void DailyMargin_GarbageIsZero()
        {
            Assert.Near(0.0, AssetMath.DailyMargin(double.NaN, 50.0, 100), 0);
            Assert.Near(0.0, AssetMath.DailyMargin(100.0, double.PositiveInfinity, 100), 0);
            Assert.Near(0.0, AssetMath.DailyMargin(double.PositiveInfinity, 50.0, 100), 0);
        }
    }
}
