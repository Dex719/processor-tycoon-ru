using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Tests.Features.ScenariosAchievements
{
    /// <summary>
    /// TSK-016: the table that compares the companies (<c>GameMetricsSource.BuildComparison</c>, FR-9, FR-17): the player first, then the best AI
    /// companies - not bankrupt before bankrupt, the larger share, the larger revenue, the name - at most six of them, always in the same order.
    /// </summary>
    [Covers(typeof(ComparisonBuilder), typeof(ComparisonRow))]
    public class ComparisonBuilderTests
    {
        private static ComparisonRow Rival(string name, double share = 0, double revenue = 0, bool bankrupt = false)
        {
            return new ComparisonRow { CompanyKey = "ai:" + name, Name = name, MarketShare = share, Revenue12m = revenue, IsBankrupt = bankrupt };
        }

        private static ComparisonRow Player()
        {
            return new ComparisonRow { CompanyKey = "player", Name = "Garage CPU", IsPlayer = true, MarketShare = 0.01 };
        }

        private static string[] Names(ComparisonRow[] table)
        {
            return table.Select(r => r.Name).ToArray();
        }

        [Test]
        public void ThePlayer_IsFirst_ThenTheBiggestShare()
        {
            ComparisonRow[] table = ComparisonBuilder.Build(Player(), new List<ComparisonRow>
            {
                Rival("Zilog", 0.05), Rival("Intel", 0.45), Rival("AMD", 0.20),
            });
            Assert.Equal(new[] { "Garage CPU", "Intel", "AMD", "Zilog" }, Names(table));
            Assert.True(table[0].IsPlayer);
        }

        [Test]
        public void AtMostSixRivals_AreTaken_TheBestOnes()
        {
            var rivals = new List<ComparisonRow>();
            for (int i = 0; i < 10; i++) rivals.Add(Rival("rival-" + i, share: i / 100.0));
            ComparisonRow[] table = ComparisonBuilder.Build(Player(), rivals);

            Assert.Equal(7, table.Length, "the player and six");
            Assert.Equal("rival-9", table[1].Name);
            Assert.Equal("rival-4", table[6].Name);
            Assert.Equal(ComparisonBuilder.DefaultMaxAi, 6);
        }

        [Test]
        public void TheLimit_CanBeChosen_AndZeroLeavesThePlayerAlone()
        {
            var rivals = new List<ComparisonRow> { Rival("a", 0.3), Rival("b", 0.2), Rival("c", 0.1) };
            Assert.Equal(new[] { "Garage CPU", "a", "b" }, Names(ComparisonBuilder.Build(Player(), rivals, 2)));
            Assert.Equal(new[] { "Garage CPU" }, Names(ComparisonBuilder.Build(Player(), rivals, 0)));
            Assert.Equal(new[] { "Garage CPU" }, Names(ComparisonBuilder.Build(Player(), rivals, -3)));
        }

        [Test]
        public void ABankruptCompany_ComesAfterTheOnesThatStillExist_WhateverItsShare()
        {
            ComparisonRow[] table = ComparisonBuilder.Build(Player(), new List<ComparisonRow>
            {
                Rival("Dead", 0.90, bankrupt: true), Rival("Alive", 0.01), Rival("Alive2", 0.02),
            });
            Assert.Equal(new[] { "Garage CPU", "Alive2", "Alive", "Dead" }, Names(table));
        }

        [Test]
        public void EqualShares_GoByRevenue_ThenByName_AndTheOrderDoesNotDependOnTheInput()
        {
            var rivals = new List<ComparisonRow>
            {
                Rival("beta", 0.1, 50), Rival("Alpha", 0.1, 50), Rival("gamma", 0.1, 90), Rival("delta", 0.1, 10),
            };
            string[] first = Names(ComparisonBuilder.Build(Player(), rivals));
            Assert.Equal(new[] { "Garage CPU", "gamma", "Alpha", "beta", "delta" }, first);

            rivals.Reverse();
            Assert.Equal(first, Names(ComparisonBuilder.Build(Player(), rivals)), "the same table whatever order the game lists the companies in");
        }

        [Test]
        public void NoPlayer_OrNoRivals_OrNulls_AreFine()
        {
            Assert.Equal(new[] { "a" }, Names(ComparisonBuilder.Build(null, new List<ComparisonRow> { Rival("a", 0.1), null })));
            Assert.Equal(new[] { "Garage CPU" }, Names(ComparisonBuilder.Build(Player(), null)));
            Assert.Equal(0, ComparisonBuilder.Build(null, null).Length);
        }

        [Test]
        public void ThePlayersRowInTheRivalList_IsNotShownTwice()
        {
            ComparisonRow player = Player();
            ComparisonRow[] table = ComparisonBuilder.Build(player, new List<ComparisonRow> { player, Rival("a", 0.2) });
            Assert.Equal(new[] { "Garage CPU", "a" }, Names(table));
        }

        [Test]
        public void TheShare_IsASumOverASum_ZeroWhenNothingWasSold()
        {
            Assert.Near(0.25, ComparisonBuilder.Share(250, 1000));
            Assert.Equal(0.0, ComparisonBuilder.Share(5, 0));
            Assert.Equal(0.0, ComparisonBuilder.Share(0, 100));
            Assert.Equal(0.0, ComparisonBuilder.Share(-4, 100));
            Assert.Equal(1.0, ComparisonBuilder.Share(500, 100), "a part larger than the whole is cut to the whole");
            Assert.Equal(0.0, ComparisonBuilder.Share(double.NaN, 100));
            Assert.Equal(0.0, ComparisonBuilder.Share(1, double.PositiveInfinity));
        }
    }
}
