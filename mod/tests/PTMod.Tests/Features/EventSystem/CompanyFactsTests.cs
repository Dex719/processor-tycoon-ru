using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The rules behind the game facade and the company directory (TSK-004; FR-3, NFR-1, E3): the quarterly revenue with fewer than three reports is 0 and
    /// nothing throws, bankrupt companies are not in the list, the ranking by market share counts the AI only, and the feature never scans the scene
    /// for companies (<c>DataFinder.FindAllCompanies</c>).
    /// </summary>
    [Covers(typeof(CompanyFacts), typeof(MarketRanking), typeof(CompanyRoster))]
    public class CompanyFactsTests
    {
        [Test]
        public void QuarterlyRevenue_WithFewerThanThreeReports_IsZero_AndNothingThrows()
        {
            Assert.Equal(0f, CompanyFacts.QuarterlyRevenue(null));
            Assert.Equal(0f, CompanyFacts.QuarterlyRevenue(new List<float>()));
            Assert.Equal(0f, CompanyFacts.QuarterlyRevenue(new List<float> { 100f, 200f }), "two reports: the game's method would index out of range");
            Assert.Equal(600f, CompanyFacts.QuarterlyRevenue(new List<float> { 100f, 200f, 300f }));
            Assert.Equal(900f, CompanyFacts.QuarterlyRevenue(new List<float> { 5f, 100f, 200f, 300f, 400f }) - 0f, "the last three: 200 + 300 + 400");
            Assert.Equal(0f, CompanyFacts.QuarterlyRevenue(new List<float> { 1f, float.NaN, 2f }), "never NaN");

            Assert.False(CompanyFacts.HasQuarter(2));
            Assert.True(CompanyFacts.HasQuarter(3));
        }

        [Test]
        public void RevenueHistoryAndFab_FollowTheDesign()
        {
            Assert.True(CompanyFacts.HasRevenueHistory(3, 1f));
            Assert.False(CompanyFacts.HasRevenueHistory(2, 1e6f), "fewer than three reports");
            Assert.False(CompanyFacts.HasRevenueHistory(12, 0f), "no revenue");

            Assert.True(CompanyFacts.HasFab(1, false));
            Assert.False(CompanyFacts.HasFab(0, false), "no lines");
            Assert.False(CompanyFacts.HasFab(40, true), "fabless");
        }

        [Test]
        public void TheRoster_LeavesOutBankruptCompanies_AndKeepsTheOrder()
        {
            var bankrupt = new HashSet<string> { "Beta" };
            var keys = new Dictionary<string, string> { { "You", "player" }, { "Alpha", "ai:0" }, { "Beta", "ai:1" }, { "Gamma", "ai:2" }, { "Junk", null }, { "Twin", "ai:2" } };

            List<KeyValuePair<string, string>> alive = CompanyRoster.Alive(new[] { "You", "Alpha", "Beta", null, "Gamma", "Junk", "Twin" },
                name => keys[name], name => bankrupt.Contains(name));

            var names = new List<string>();
            foreach (KeyValuePair<string, string> entry in alive) names.Add(entry.Key + "=" + entry.Value);
            Assert.Equal("player=You,ai:0=Alpha,ai:2=Gamma", string.Join(",", names), "no bankrupt, no keyless, no duplicate key");
            Assert.Equal(0, CompanyRoster.Alive<string>(null, k => k, null).Count);
        }

        [Test]
        public void IsTopByMarketShare_RanksTheAiOnly()
        {
            var shares = new Dictionary<string, float> { { "player", 0.6f }, { "ai:0", 0.1f }, { "ai:1", 0.2f }, { "ai:2", 0.05f }, { "ai:3", 0.05f } };
            var companies = new List<string> { "player", "ai:0", "ai:1", "ai:2", "ai:3" };
            System.Func<string, float> share = key => shares[key];

            Assert.True(MarketRanking.IsTop(companies, share, "ai:1", 1), "the player's 60 % does not push ai:1 out of the top 1");
            Assert.False(MarketRanking.IsTop(companies, share, "ai:0", 1));
            Assert.True(MarketRanking.IsTop(companies, share, "ai:0", 2));
            Assert.True(MarketRanking.IsTop(companies, share, "ai:2", 3), "equal shares keep the order of UniqueID");
            Assert.False(MarketRanking.IsTop(companies, share, "ai:3", 3));
            Assert.False(MarketRanking.IsTop(companies, share, CompanyKey.Player, 5), "the player is never ranked");
            Assert.False(MarketRanking.IsTop(companies, share, "ai:9", 5), "unknown company");
            Assert.False(MarketRanking.IsTop(companies, share, "ai:1", 0), "a top of 0");
        }

        [Test]
        public void TheFeature_ReadsCompaniesFromTheCoreCache_NeverByScanningTheScene()
        {
            string src = SourceScan.SrcDirOrFail();
            string folder = Path.Combine(src, "Features", "EventSystem");
            var scan = new Regex(@"\bFindAllCompanies\b|\bFindAllAICompanies\b|\bFindObjectsByType\b|\bFindObjectsOfType\b|\bFindObjectOfType\b");
            var violations = new List<string>();
            foreach (string path in SourceScan.Files(folder, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in scan.Matches(code)) violations.Add(SourceScan.Relative(src, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
            }
            Assert.True(violations.Count == 0, "the company list comes from ModCompanies.All (NFR-1):\n  " + string.Join("\n  ", violations));

            string facade = SourceScan.ReadText(Path.Combine(folder, "Game", "GameFacade.cs"));
            Assert.True(facade.Contains("CompanyFacts.HasQuarter(finance.Reports.Count)"), "QuarterlyRevenue is guarded by the count of reports");
            Assert.True(facade.Contains("ModCompanies.All"), "the companies come from ModCompanies");
            string directory = SourceScan.ReadText(Path.Combine(folder, "Game", "CompanyDirectory.cs"));
            Assert.True(directory.Contains("ModCompanies.Resolve"), "keys are resolved by ModCompanies");
        }
    }
}
