using System.IO;
using System.Text.RegularExpressions;
using PTMod.Features.GpuProductLine;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// Patch GP1 of the production screen (TSK-021; FR-5 item 8): the pure rule of the free lines (with a reserve of N lines the rows of the CPUs get
    /// <c>max(0, free - N)</c> in the default mode and the game's number with "CPU first"; without a reserve the screen is the game's), and the game side checked on
    /// the source (it cannot be built into the unit tests): the optional postfix on <c>ProductionWindow.UpdateAvailableProductionLines</c> with the four fields,
    /// declared by <c>ctx.Patch</c>, guarded, the label from the string tables; and the wiring of the market and the lineup of TSK-019/020 into the days of the core.
    /// </summary>
    [Covers(typeof(GpuProductionScreen))]
    public class GpuProductionScreenTests
    {
        private static string Code(params string[] parts)
        {
            string path = Path.Combine(GpuFiles.FeatureSourceDir, Path.Combine(parts));
            Assert.True(File.Exists(path), "no " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        [Test]
        public void WithAReserve_TheRowsOfTheCpus_GetTheFreeLinesMinusTheCards()
        {
            // AC-5.2 on the screen: 20 lines, CPUs on 10, the cards 5: the rows may take 5 more, not 10
            Assert.Equal(5, GpuProductionScreen.FreeForCpus(20, 10, 5, false));
            Assert.Equal(0, GpuProductionScreen.FreeForCpus(20, 18, 5, false), "never below 0");
            Assert.Equal(0, GpuProductionScreen.FreeForCpus(20, 25, 5, false), "the game's free lines are 0 already");
            Assert.True(GpuProductionScreen.ShowsCards(5), "\" (+5 GPU)\"");
        }

        [Test]
        public void CpuFirst_LeavesTheRowsAsTheGameHasThem()
        {
            // AC-5.5: the CPU rows are not cut
            Assert.Equal(2, GpuProductionScreen.FreeForCpus(20, 18, 5, true));
            Assert.Equal(10, GpuProductionScreen.FreeForCpus(20, 10, 5, true));
        }

        [Test]
        public void WithoutAReserve_TheScreenIsTheGames()
        {
            Assert.False(GpuProductionScreen.ShowsCards(0));
            Assert.False(GpuProductionScreen.ShowsCards(-3));
            Assert.Equal(10, GpuProductionScreen.FreeForCpus(20, 10, 0, false));
            Assert.Equal(GpuProductionScreen.GameFree(20, 10), GpuProductionScreen.FreeForCpus(20, 10, 0, false));
            Assert.Equal(0, GpuProductionScreen.GameFree(20, 30));
            Assert.Equal(20, GpuProductionScreen.GameFree(20, -4), "a negative count is no lines");
            Assert.Equal("gpu.productionUi.reserved", GpuProductionScreen.ReservedKey);
        }

        [Test]
        public void ThePatch_IsAnOptionalPostfixOnTheScreen_WithItsFourFields()
        {
            string patch = Code("Patches", "ProductionWindowPatch.cs");
            Assert.True(Regex.IsMatch(patch, @"\[ModPatch\(typeof\(GameProductionWindow\),\s*""UpdateAvailableProductionLines"",\s*PatchKind\.Postfix,\s*Optional\s*=\s*true\)\]"),
                "[ModPatch(ProductionWindow, \"UpdateAvailableProductionLines\", Postfix, Optional = true)]");
            Assert.True(patch.Contains("using GameProductionWindow = ProcessorTycoon.Production.UI.ProductionWindow;"));
            foreach (string field in new[] { "___linesUsedText", "___productionUIs", "___cpus", "___clientCpus" })
                Assert.True(patch.Contains(field), "the field " + field);
            Assert.True(patch.Contains("feature.Guard(Site,"), "the body runs under GpuFeature.Guard");
            Assert.True(patch.Contains("ModL10n.L(GpuProductionScreen.ReservedKey, lines)"), "\" (+N GPU)\" from the string tables");
            Assert.True(patch.Contains("GpuProductionScreen.FreeForCpus("), "the rule of the free lines");
            Assert.True(patch.Contains("row.UpdateAvailableProductionLines(free)"), "every row of the CPUs");
            Assert.True(patch.Contains("if (feature.CpuPriority"), "\"CPU first\": only the label");
            Assert.True(Regex.IsMatch(patch, @"if \(!GpuProductionScreen\.ShowsCards\(lines\)\) return;"), "no reserve: the game's screen");
            Assert.True(patch.Contains("cpu.IsOutsourced"), "the outsourced CPUs are left out, as the game counts");

            string feature = Code("GpuFeature.cs");
            Assert.True(feature.Contains("ctx.Patch(typeof(ProductionWindowPatch));"), "declared through ctx.Patch");
            Assert.True(Regex.IsMatch(feature, @"!IsAvailable\(GpuCapability\.ProductionUi\)"), "the ability ProductionUi gates the lines shown");
            Assert.True(feature.Contains("Production.FablessToday"), "a fabless company has no reserve to show");

            // the self-check knows the target of the patch by the same name
            Assert.True(Code("GpuSelfCheck.cs").Contains("public const string ProductionUiTarget = \"ProductionWindow.UpdateAvailableProductionLines\";"));
        }

        [Test]
        public void TheMarketAndTheLineup_AreOnTheDaysOfTheCore()
        {
            string feature = Code("GpuFeature.cs");
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.MarketSellTick, MarketTick);"), "the market sells on MarketSellTick");
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.DayPassed, MarketDayTick);"));
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.DayPassed, LineupDayTick);"), "storage on DayPassed");
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.MonthPassed, MarketMonthTick, MarketMonthPriority);"));
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.MonthPassed, LineupMonthTick, LineupMonthPriority);"));
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.FastForwardEnded, SeedTick);"), "a new game is seeded when the fast-forward ends");
            Assert.True(feature.Contains("Timed(GpuPerfMeter.Day, \"market\", Market.SellDay);"));
            Assert.True(feature.Contains("Timed(GpuPerfMeter.Day, \"lineup/day\", Lineup.DayTick);"));
            Assert.True(feature.Contains("Guard(\"gameReady/market\", StartMarket);"), "a save without the section is seeded when it is ready");
            Assert.True(Regex.IsMatch(feature, @"private void SeedNewGame\(\)\s*\{\s*RefreshNodes\(\);\s*Market\.SeedForYear\(Game\.Today\.Year\);"));
            Assert.True(feature.Contains("ModRandom.Stream(GpuRandom.BackgroundStream)"), "the background draws from its stream of ModRandom");
            Assert.True(GpuFeatureMonthOrder(), "the market renews the background after the cache of the nodes, the lineup after the market");

            // the market runs without the division: its handler has no check of the division
            Match market = Regex.Match(feature, @"private void MarketTick\(\)\s*\{[^}]*\}");
            Assert.True(market.Success);
            Assert.False(market.Value.Contains("divisionFounded"), "FR-6 item 8");
        }

        private static bool GpuFeatureMonthOrder()
        {
            string feature = Code("GpuFeature.cs");
            Match match = Regex.Match(feature, @"MarketMonthPriority\s*=\s*(-?\d+),\s*LineupMonthPriority\s*=\s*(-?\d+);");
            if (!match.Success) return false;
            int market = int.Parse(match.Groups[1].Value), lineup = int.Parse(match.Groups[2].Value);
            return market < 0 && lineup < market;   // the nodes are refreshed at priority 0
        }
    }
}
