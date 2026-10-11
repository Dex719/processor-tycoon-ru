using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The game side of TSK-011...TSK-016, checked on the source (it cannot be built into the unit tests): the feature declares no channel and keeps no channel names
    /// of its own (TSK-012), never reads the bank's limit (TSK-013), never asks the CPU's project formula (TSK-015), searches the scene for process nodes only when the
    /// cache is rebuilt - when the game is ready and once a month (TSK-011), subscribes to the days without the fast-forward (TSK-014), writes the two private counters
    /// of the factory without Harmony (TSK-016), and the self-check looks for six fields and twelve members (TSK-011).
    /// </summary>
    public class GpuServicesSourceTests
    {
        private static List<string> FeatureFiles()
        {
            List<string> files = SourceScan.Files(GpuFiles.FeatureSourceDir, ".cs");
            Assert.True(files.Count >= 20, "source files under " + GpuFiles.FeatureSourceDir + ": " + files.Count);
            return files;
        }

        /// <summary>Code without comments; string literals blanked.</summary>
        private static string Code(string path)
        {
            return SourceScan.Strip(SourceScan.ReadText(path), true);
        }

        /// <summary>Code without comments; string literals kept.</summary>
        private static string CodeWithStrings(string path)
        {
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static string File(params string[] parts)
        {
            string path = Path.Combine(GpuFiles.FeatureSourceDir, Path.Combine(parts));
            Assert.True(System.IO.File.Exists(path), "no " + path);
            return path;
        }

        private static List<string> Find(string pattern, bool withStrings = false)
        {
            var hits = new List<string>();
            foreach (string path in FeatureFiles())
            {
                string code = withStrings ? CodeWithStrings(path) : Code(path);
                foreach (Match match in Regex.Matches(code, pattern))
                    hits.Add(SourceScan.Relative(GpuFiles.FeatureSourceDir, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
            }
            return hits;
        }

        [Test]
        public void NoChannelIsDeclared_AndTheNamesComeFromModChannels()
        {
            List<string> declares = Find(@"\bDeclareChannel\b");
            Assert.True(declares.Count == 0, "the channels of the GPU are declared by the core (core.json):\n  " + string.Join("\n  ", declares));

            List<string> literals = Find("\"(?:gpu\\.demand(?:\\.[^\"]*)?|gpu\\.rating|gpu\\.vramCost|production\\.unitCost|production\\.output|popularity\\.gain|market\\.rating\\.cpu)\"", true);
            Assert.True(literals.Count == 0, "channel names are the constants of ModChannels:\n  " + string.Join("\n  ", literals));

            string bridge = Code(File("Bridges", "GpuModifierBridge.cs"));
            foreach (string constant in new[] { "ProductionUnitCost", "ProductionOutput", "GpuVramCost", "GpuRating", "PopularityGain", "GpuDemand", "MarketRatingCpu" })
                Assert.True(Regex.IsMatch(bridge, @"\bModChannels\s*\.\s*" + constant + @"\b"), "ModChannels." + constant);
        }

        [Test]
        public void TheBridge_AsksWithTheCompanyAndItsKey_AndNothingElse()
        {
            string bridge = Code(File("Bridges", "GpuModifierBridge.cs"));
            Assert.True(bridge.Contains("new ModifierContext { Company = company, CompanyKey = ModCompanies.KeyOf(company) }"), "{ Company, CompanyKey }");
            Assert.True(bridge.Contains("default(ModifierContext)"), "no company: the global question");
            Assert.False(Regex.IsMatch(bridge, @"\b(?:Cpu|Segment|Contract|Design)\s*="), "no CPU, no segment, no contract, no design in the question");
            Assert.Equal(2, Regex.Matches(bridge, @"ModModifiers\s*\.\s*RegisterReasonedProvider\(").Count, "market.rating.cpu and gpu.rating");
            Assert.True(bridge.Contains("if (lookup == null || _cpuProvider != null) return;"), "registered once");

            string feature = Code(File("GpuFeature.cs"));
            Assert.Equal(1, Regex.Matches(feature, @"\bRegisterSynergyProviders\(").Count, "one registration, in Register");
        }

        [Test]
        public void TheBankLimit_IsNeverReadDirectly()
        {
            List<string> hits = Find(@"\bCentralBank\b|\bDebtLimit\w*");
            Assert.True(hits.Count == 0, "the default threshold is ModCredit.ThresholdOf:\n  " + string.Join("\n  ", hits));
            Assert.True(Code(File("Bridges", "GpuGameAdapter.cs")).Contains("ModCredit.ThresholdOf(company)"));
        }

        [Test]
        public void TheProjects_DoNotUseTheCpuProjectFormula()
        {
            List<string> hits = Find(@"\b\w*CpuMath\s*\.\s*Project(?:Cost|Time)\b|\bDevelopmentCostDesign\b");
            Assert.True(hits.Count == 0, "CpuMath.ProjectCost / development.cost.design do not apply to the cards:\n  " + string.Join("\n  ", hits));
            Assert.True(Code(File("Bridges", "GpuGameAdapter.cs")).Contains("GameModifiers.GetModifier((GameModifierType)(int)kind, Resolve(companyKey))"),
                "the development modifier is Modifiers.GetModifier");
            Assert.True(Code(File("Logic", "Services", "GpuDesigner.cs")).Contains("_game.Modifier(GpuModifierKind.DevelopmentCost, player)"));
        }

        [Test]
        public void TheSceneIsSearched_OnlyWhenTheCacheOfNodesIsRebuilt()
        {
            Assert.Equal(0, Find(@"\bFindObjectsByType\b|\bFindObjectsOfType\b|\bFindObjectOfType\b").Count, "no search of the scene of its own");

            List<string> finds = Find(@"\bFindLithography\s*\(");
            Assert.Equal(1, finds.Count, string.Join("\n", finds));
            string adapter = Code(File("Bridges", "GpuGameAdapter.cs"));
            int refresh = adapter.IndexOf("public void RefreshNodes(", System.StringComparison.Ordinal);
            int next = adapter.IndexOf("public bool TryGetNode(", System.StringComparison.Ordinal);
            int call = adapter.IndexOf("FindLithography(", System.StringComparison.Ordinal);
            Assert.True(refresh >= 0 && next > refresh && call > refresh && call < next, "the only call is inside RefreshNodes");

            // RefreshNodes runs when the game is ready and once a month, never in a daily handler
            string feature = Code(File("GpuFeature.cs"));
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.MonthPassed, MonthTick);"));
            Assert.True(Regex.IsMatch(feature, @"private void MonthTick\(\)\s*\{\s*Timed\(GpuPerfMeter\.Month,\s*""\s*""\s*,\s*RefreshNodes\);\s*\}"), "MonthTick refreshes the nodes");
            Assert.True(Regex.IsMatch(feature, @"private void StartSession\(\)\s*\{\s*Production\.Reset\(\);\s*RefreshNodes\(\);"), "the session starts with the cache");
            foreach (string daily in new[] { "ProductionTick", "ResearchTick", "ProjectsTick" })
            {
                Match body = Regex.Match(feature, @"private void " + daily + @"\(\)\s*\{[^}]*\}");
                Assert.True(body.Success, daily);
                Assert.False(body.Value.Contains("RefreshNodes"), daily + " does not search the scene");
            }
        }

        [Test]
        public void TheDays_AreSubscribedWithoutTheFastForwardAndTheLoading()
        {
            string feature = CodeWithStrings(File("GpuFeature.cs"));
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.PreContractSellTick, ProductionTick);"), "the reserve before the CPUs");
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.Tick, ResearchTick);"), "the laboratory");
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.Tick, ProjectsTick);"), "the projects");
            Assert.Equal(0, Find(@"\bRunDuringFastForward\b|\bRunDuringLoading\b").Count, "no progress while a new game is fast-forwarded or a save loads");

            Assert.True(feature.Contains("Timed(GpuPerfMeter.Day, \"production\", Production.ProduceDay);"));
            Assert.True(feature.Contains("Timed(GpuPerfMeter.Day, \"research\", Research.DailyTick);"));
            Assert.True(feature.Contains("Timed(GpuPerfMeter.Day, \"projects\", Projects.DailyTick);"));
            Assert.True(feature.Contains("() => CpuPriority"), "the switch cpuPriority is read every day through ModSettings");
        }

        [Test]
        public void TheFactory_IsWrittenThroughItsTwoPrivateCounters()
        {
            string access = CodeWithStrings(File("Bridges", "FactoryAccess.cs"));
            Assert.True(access.Contains("\"totalAllocatedLines\""));
            Assert.True(access.Contains("\"totalDemandedLines\""));
            Assert.True(access.Contains("FieldInfo"), "System.Reflection, no Harmony in the feature");
            Assert.True(access.Contains("field.FieldType == typeof(int)"), "the type is checked too");
        }

        [Test]
        public void TheSelfCheck_LooksForSixFieldsTwelveMembersTwoPatchesAndThePorts()
        {
            string check = Code(File("GpuSelfCheck.cs"));
            Assert.Equal(6, Regex.Matches(check, @"\bbook\.Field\(").Count, "six fields");
            Assert.Equal(12, Regex.Matches(check, @"\bCall\(book,").Count, "twelve members");
            Assert.Equal(2, Regex.Matches(check, @"\bbook\.Patch\(").Count, "GP1 and GP2");
            Assert.Equal(3, Regex.Matches(check, @"\bbook\.Port\(").Count, "F1, F10, F11");
            Assert.True(check.Contains("ModHooks.Report"), "the state of the patches comes from ModHooks.Report");
            Assert.True(check.Contains("GameCompanyHelper.IsEMD(1)") && check.Contains("GameCompanyHelper.IsInlet(0)"));

            string feature = Code(File("GpuFeature.cs"));
            Assert.True(feature.Contains("ModLog.Info(FeatureId, check.Summary());"), "the line goes to Player.log");
            Assert.True(feature.Contains("BuildSelfCheckLines(page);"), "and to the page of the feature in the hub");
        }

        [Test]
        public void TheStageTwoHandlers_AreSubscribedInTheOrderOfTheDesign_AndTimed()
        {
            string feature = CodeWithStrings(File("GpuFeature.cs"));

            // the days: the vendors fill their stock before the market sells, and look at their companies at the end of the day
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.MarketSellTick, RivalsStockTick, RivalsStockPriority);"), "stock first");
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.MarketSellTick, MarketTick);"), "then the market");
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.DayPassed, RivalsDayTick);"), "the companies at the end of the day, not a subscription to the game's event");
            Assert.True(Regex.IsMatch(feature, @"internal const int RivalsStockPriority = (\d+);"), "the stock runs before the market (priority above the market's 0)");
            Assert.True(int.Parse(Regex.Match(feature, @"RivalsStockPriority = (\d+);").Groups[1].Value) > 0);

            // the months: the nodes (0), the calendar, the vendors, the market, the lineup - higher priority first
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.MonthPassed, ErasMonthTick, ErasMonthPriority);"));
            Assert.True(feature.Contains("ctx.OnTick(TickPhase.MonthPassed, RivalsMonthTick, RivalsMonthPriority);"));
            Match order = Regex.Match(feature, @"internal const int ErasMonthPriority = (-?\d+), RivalsMonthPriority = (-?\d+), MarketMonthPriority = (-?\d+), LineupMonthPriority = (-?\d+);");
            Assert.True(order.Success, "the four monthly priorities are declared together");
            int[] p = { int.Parse(order.Groups[1].Value), int.Parse(order.Groups[2].Value), int.Parse(order.Groups[3].Value), int.Parse(order.Groups[4].Value) };
            Assert.True(p[0] <= 0 && p[0] > p[1] && p[1] > p[2] && p[2] > p[3], "nodes (0), segments, vendors, market, lineup: " + string.Join(", ", p.Select(x => x.ToString()).ToArray()));

            // every handler is timed under its own name
            foreach (string site in new[] { "rivals/stock", "rivals/day" })
                Assert.True(feature.Contains("Timed(GpuPerfMeter.Day, \"" + site + "\", "), site);
            foreach (string site in new[] { "eras/month", "rivals/month" })
                Assert.True(feature.Contains("Timed(GpuPerfMeter.Month, \"" + site + "\", "), site);

            // the services: the vendors on their own stream, the switch read every day, the seed of the market seeds the vendors
            Assert.True(feature.Contains("new GpuRandom(ModRandom.Stream(GpuRandom.RivalsStream))"), "the stream gpu-product-line/rivals (the background has its own)");
            Assert.True(feature.Contains("Market.RivalsEnabled = () => RivalsEnabled;"), "the switch is asked every day");
            Assert.True(feature.Contains("Market.Seeded += OnMarketSeeded;"), "AC-9.1: the vendors of the year come with the seed");

            // a game that is ready: the mode of the market first, the market, then the vendors
            Match start = Regex.Match(feature, @"private void StartMarket\(\)\s*\{[^}]*\}");
            Assert.True(start.Success, "StartMarket");
            int mode = start.Value.IndexOf("Eras.ApplyModeSwitchIfNeeded(State)", StringComparison.Ordinal);
            int market = start.Value.IndexOf("Market.StartSession()", StringComparison.Ordinal);
            int rivals = start.Value.IndexOf("Rivals.StartSession()", StringComparison.Ordinal);
            Assert.True(mode >= 0 && market > mode && rivals > market, "the mode, the market, the vendors: " + start.Value);
        }

        [Test]
        public void TheVendors_AreOnlyData_NoCompanyIsCreated_NoFactoryIsTouched_InTheWholeFeature()
        {
            // the game side too: nothing in the feature spawns a company or writes the books of a company other than through EarnMoney / SpendMoney
            List<string> hits = Find(@"\bCompanySpawner\b|\bSpawnCompany\b|\bInstantiate\s*\(|\bnew\s+(?:AICompany|Company)\b");
            Assert.True(hits.Count == 0, "FR-12 item 6: the vendors create no company of the game:\n  " + string.Join("\n  ", hits));
            string adapter = Code(File("Bridges", "GpuGameAdapter.cs"));
            Assert.Equal(2, Regex.Matches(adapter, @"\.(?:EarnMoney|SpendMoney)\(").Count, "the money of a company moves in two places: Earn and Spend");
        }

        [Test]
        public void TheAdapter_UsesTheKeysOfModCompanies()
        {
            string adapter = Code(File("Bridges", "GpuGameAdapter.cs"));
            Assert.True(adapter.Contains("ModCompanies.KeyOf(company)"));
            Assert.True(adapter.Contains("ModCompanies.Resolve(companyKey)"));
            Assert.True(adapter.Contains("GameProductionMath.WaferMaterialCost(1f)"), "the wafer material is asked every day (the shock of production.waferCost)");
            Assert.True(adapter.Contains("ModConditions.Parse("), "the technologies of the game through the core's predicate");
        }
    }
}
