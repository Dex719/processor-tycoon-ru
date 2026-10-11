using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// The game side of the snapshots (TSK-005). <c>GameSnapshotAdapter</c> needs Unity and the game, so it is not part of the test build; these tests read
    /// its source and check that it reads every source the Acceptance of TSK-005 and the "Источники" paragraph of design.md name, and nothing it must not
    /// (the bank's own debt limit, a write to the game). That the numbers are right in the running game is checked in the game (M1 of design.md).
    /// </summary>
    public class GameSnapshotAdapterTests
    {
        private static string Source()
        {
            Assert.NotNull(TestEnv.SrcDir, "source folder not found; run the tests through python mod/build.py --tests");
            string path = Path.Combine(TestEnv.SrcDir, "Features", "RivalAcquisitions", "GameSnapshotAdapter.cs");
            Assert.True(File.Exists(path), "missing " + path);
            return File.ReadAllText(path);
        }

        [Test]
        public void TheAdapterReadsEverySourceOfTheSnapshots()
        {
            string code = Source();
            var sources = new Dictionary<string, string>
            {
                { "money of the target", "MoneyAmount" },
                { "threshold of the target through ModCredit", "ModCredit.DebtLimitOf(" },
                { "threshold of the player through ModCredit", "ModCredit.PlayerDebtLimit(" },
                { "lines per wafer size", "AmountOfLines(" },
                { "sale cost of the lines", "SaleCost(" },
                { "value of the manufacturing division", "ManufacturingDivisionValue(" },
                { "technologies of the game", "FindAllTechnologies(" },
                { "progress of a technology", "TechnologyAsResearch(" },
                { "technology researched", "IsResearched" },
                { "experience of the nodes", "CurrentNodeLearningProgress(" },
                { "CPUs of the target", "GetCpus(" },
                { "income of a CPU", "Market.Income" },
                { "cost of a CPU", "RealUnitCost" },
                { "contracts of the businesses", "GetContracts(" },
                { "exclusive contract of the player", "HasAnyExclusivityDeal(" },
                { "foundry services flag", "FoundryServices" },
                { "market share", "MarketShare(" },
                { "market share of the foundry", "FoundryMarketShare(" },
                { "other foundries", "GetFoundryCompanies(" },
                { "OEM contracts", "ChosenCpuID" },
                { "the status rule", "CompanyStatusRules.Resolve(" },
                { "the open auctions", "CompanyStatusRules.OpenAuction(" },
                { "the game date", "ModTicks.Today" },
                { "the price channel", "ModChannels.AcquisitionPrice" },
                { "the time of the build in the debug log", "ModLog.Debug(" },
            };

            foreach (KeyValuePair<string, string> source in sources)
            {
                Assert.True(code.Contains(source.Value), "the adapter does not read: " + source.Key + " (" + source.Value + ")");
            }
        }

        [Test]
        public void TheAdapterNeverReadsTheBanksOwnLimit_AndNeverWritesToTheGame()
        {
            string code = Source();

            Assert.False(Regex.IsMatch(code, @"\.\s*DebtLimit\b"), "only ModCredit reads CentralBank.DebtLimit (FR-24 item 4)");
            foreach (string write in new[] { "set_MoneyAmount", ".MoneyAmount =", "EarnMoney(", "SpendMoney(", "ExpandFactory(", "RemoveProductionLines(", "OptimizeNode(", "AddCpu(", ".Progress =", "TriggerBankruptcy(" })
            {
                Assert.False(code.Contains(write), "the adapter only reads, but it contains " + write);
            }
        }

        [Test]
        public void TheDepthOfATechnology_IsPartOfTheGapSnapshot()
        {
            // The spike found chains of 26 dependent technologies: the plan unlocks them in the order of their depth.
            Assert.True(Regex.IsMatch(Source(), @"Depth\s*=\s*DepthOf\("), "TechSnapshot.Depth is filled from the dependency tree");
        }

        [Test]
        public void TheLogic_DoesNotReachForTheGameOrThePatchLayer()
        {
            string logic = Path.Combine(TestEnv.SrcDir, "Features", "RivalAcquisitions", "Logic");
            int files = 0;
            foreach (string path in Directory.GetFiles(logic, "*.cs", SearchOption.AllDirectories))
            {
                files++;
                string code = File.ReadAllText(path);
                Assert.False(Regex.IsMatch(code, @"\busing\s+(UnityEngine|ProcessorTycoon|HarmonyLib|TMPro)\b"), path + " must stay free of the engine and the game");
                Assert.False(Regex.IsMatch(code, @"\bPTMod\.Core\.(?!Logic)"), path + " must not use the core services (ModLog, ModHooks...): the logic is tested without them");
            }
            Assert.True(files >= 10, "the scan found the logic files: " + files);
        }
    }
}
