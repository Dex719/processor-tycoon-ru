using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;
using PTMod.Features.MarketShocks;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// market-shocks, TSK-015 (FR-2 item 5, FR-9, FR-11 item 6): the pack treats the player and the AI companies alike. The same numbers of an effect for
    /// the same quarterly revenue at any intensity (AC-9.3), nothing in the pack's rules or content that looks at the difficulty or at who the player is,
    /// the player a candidate on equal terms, and the news about rivals only for the five biggest by share (AC-2.3, AC-2.4).
    /// </summary>
    public class ParityTests
    {
        private static readonly string[] SingleCompanyShocks =
        {
            "ms.fab_fire", "ms.cleanroom_contamination", "ms.grid_failure", "ms.litho_breakdown", "ms.dumping_complaint", "ms.launch_hype",
        };

        // ---- AC-9.3: the same numbers ----

        private static List<SimResolved> ResolveAt(string eventId, string company, float severity, double inflation, double quarterlyRevenue)
        {
            var setup = new SimSetup { Start = new DateTime(1995, 1, 1), Severity = severity, Inflation = (float)inflation, KeepResolved = true, AiHasCash = false }.Quiet();
            var sim = new ShockSimulator(setup);
            // Equal revenue: the player and the first AI company have the same.
            sim.CompanyOf(CompanyKey.Player).QuarterlyRevenue = quarterlyRevenue;
            sim.CompanyOf(CompanyKey.AiPrefix + "1").QuarterlyRevenue = quarterlyRevenue;
            sim.CompanyOf(CompanyKey.Player).SellsBelowCost = true;
            sim.CompanyOf(CompanyKey.AiPrefix + "1").SellsBelowCost = true;

            sim.RunDays(100);   // es.hasRevenueHistory
            Assert.True(sim.Fire(eventId, company), eventId + " fired at " + company);
            sim.RunDays(40);    // the timers of the player's choices (3 to 14 days) run out
            return sim.Resolved;
        }

        private static string Describe(SimResolved r)
        {
            return r.EventId + "/" + (r.ChoiceId ?? "-") + " " + r.Type + " " + r.Channel + " hit=" + r.Hit + " amount=" + r.Amount + " value=" + r.Value + " days=" + r.Duration
                   + " clamped=" + r.Clamped;
        }

        [Test]
        public void SameRevenue_GivesThePlayerAndAnAiCompany_TheSameSumsMultipliersAndDurations_OnEveryIntensity()
        {
            foreach (string id in SingleCompanyShocks)
            {
                foreach (float severity in new[] { 0.5f, 1f, 1.5f, 2f })
                {
                    List<SimResolved> player = ResolveAt(id, CompanyKey.Player, severity, 3.0, 60000000);
                    List<SimResolved> ai = ResolveAt(id, CompanyKey.AiPrefix + "1", severity, 3.0, 60000000);

                    Assert.True(player.Count > 0, id + ": no effects resolved");
                    Assert.Equal(player.Count, ai.Count, id + " at " + severity + ": the number of effects");
                    for (int i = 0; i < player.Count; i++)
                        Assert.Equal(Describe(player[i]), Describe(ai[i]), id + " at " + severity + ", effect " + i + ": the player and the AI get the same numbers");
                }
            }
        }

        [Test]
        public void TheSums_FollowTheRevenueOfTheTarget_NotWhoItIs()
        {
            // Twice the revenue, twice the sum (inside the bounds of the effect): the revenue is the only thing that moves a sum.
            List<SimResolved> small = ResolveAt("ms.fab_fire", CompanyKey.AiPrefix + "1", 1f, 3.0, 40000000);
            List<SimResolved> big = ResolveAt("ms.fab_fire", CompanyKey.AiPrefix + "1", 1f, 3.0, 80000000);

            double a = FirstMoney(small), b = FirstMoney(big);
            Assert.True(a > 0 && b > 0, "both fires cost something");
            Assert.InRange(b, a * 2 - 1000, a * 2 + 1000, "40M -> " + a + ", 80M -> " + b);
        }

        private static double FirstMoney(List<SimResolved> list)
        {
            foreach (SimResolved r in list) if (r.Type == "Money" && r.Hit) return r.Amount;
            return 0;
        }

        // ---- nothing looks at the difficulty or at the player ----

        [Test]
        public void TheCodeOfThePack_DoesNotLookAtTheDifficulty_OrAtWhoThePlayerIs()
        {
            string src = SourceScan.SrcDirOrFail();
            string root = Path.Combine(Path.Combine(src, "Features"), "MarketShocks");
            var rule = new Regex(@"\b(?:DifficultyLevel|Difficulty|EnableCheats|IsPlayer|IsAi|IsPlayerCompany|PlayerCompany|CompanyKey\s*\.\s*Player)\b", RegexOptions.Compiled);

            var found = new List<string>();
            int files = 0;
            foreach (string path in SourceScan.Files(root, ".cs"))
            {
                files++;
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in rule.Matches(code))
                    found.Add(SourceScan.Relative(src, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
            }

            Assert.True(files >= 20, "too few files under Features/MarketShocks (" + files + ")");
            Assert.True(found.Count == 0, "AC-9.3, FR-9 item 5: the effects of the pack are the same for the player and the AI on any difficulty:\n  " + string.Join("\n  ", found.ToArray()));
        }

        [Test]
        public void TheContentOfThePack_OnlyUsesPredicatesThatAreBlindToWhoIsTheTarget()
        {
            // difficultyAtLeast and cheatsEnabled (core) would make a shock depend on the player's settings; the rest of the core's catalog is about the
            // state of the company asked about, which is the same for the player and for an AI company.
            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "dateAtLeast", "dateBefore", "cpuOnMarket", "es.hasRevenueHistory", "es.hasFab", "es.activeInCategory", "ms.sellsBelowCost", "ms.hasRecentCpu",
            };

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonObject record in ShockContentData.Records())
            {
                Collect(record.GetObject("conditions"), seen);
                JsonObject targets = record.GetObject("targets");
                if (targets != null) Collect(targets.GetObject("filter"), seen);
                Assert.False(targets != null && targets.ContainsKey("companyKey"), record.GetString("id", "?") + ": the pack names no company");
                Assert.False(targets != null && targets.GetString("type", string.Empty) == "RandomAI", record.GetString("id", "?") + ": a shock aimed at the AI alone is not fair play");
            }

            foreach (string type in seen) Assert.True(allowed.Contains(type), "the predicate '" + type + "' is not on the list of those blind to who the target is");
            Assert.True(seen.Count >= 6, "predicates seen: " + string.Join(", ", new List<string>(seen).ToArray()));
        }

        private static void Collect(JsonNode node, HashSet<string> into)
        {
            var obj = node as JsonObject;
            if (obj == null) return;
            string type;
            if (obj.TryGetString("type", out type)) into.Add(type);
            JsonArray list;
            foreach (string key in new[] { "all", "any" })
            {
                list = obj.GetArray(key);
                if (list == null) continue;
                foreach (JsonNode item in list) Collect(item, into);
            }
            Collect(obj.GetObject("not"), into);
        }

        // ---- the player is a candidate like any other (FR-9 item 2) ----

        [Test]
        public void ThePlayer_IsAmongTheTargets_OfEveryShockThatPicksOrGroupsCompanies()
        {
            var hits = new Dictionary<string, int>();
            foreach (ShockSimulator sim in BalanceSimulationTests.Share())
            {
                foreach (SimFiring firing in sim.Firings)
                {
                    if (Array.IndexOf(firing.Targets, CompanyKey.Player) < 0) continue;
                    int count;
                    hits.TryGetValue(firing.EventId, out count);
                    hits[firing.EventId] = count + 1;
                }
            }

            foreach (ShockSimulator.Def def in new ShockSimulator(new SimSetup()).Defs)
            {
                if (def.TargetType != "RandomCompany" && def.TargetType != "AllCompanies") continue;
                int count;
                hits.TryGetValue(def.Id, out count);
                Assert.True(count > 0, def.Id + " (" + def.TargetType + ") never had the player among its targets in 200 runs");
            }
        }

        [Test]
        public void TheGroupShocks_HitTheEveryCompanyThatPassesTheFilter_ThePlayerIncluded()
        {
            var setup = new SimSetup { Start = new DateTime(1995, 1, 1) }.Quiet();
            var sim = new ShockSimulator(setup);
            sim.CompanyOf(CompanyKey.AiPrefix + "4").Lines = 0;   // no fab
            Assert.True(sim.Fire("ms.earthquake"), "the earthquake fired");

            SimFiring firing = sim.Firings[0];
            Assert.True(firing.IsGroup, "AllCompanies is one group");
            Assert.True(Array.IndexOf(firing.Targets, CompanyKey.Player) >= 0, "the player is in the group");
            Assert.True(Array.IndexOf(firing.Targets, CompanyKey.AiPrefix + "4") < 0, "a company without a fab is not");
            Assert.Equal(9, firing.Targets.Length, "the player and eight of the nine AI companies");
        }

        // ---- AC-2.3, AC-2.4: the news about rivals ----

        [Test]
        public void RivalNews_Ac23_AFireAtARivalOfTheTopFive_ReachesThePlayer()
        {
            ShockSimulator sim = Quiet();
            Assert.True(sim.Fire("ms.fab_fire", CompanyKey.AiPrefix + "3"), "the fire fired");   // the third by share (13 %)

            Assert.Equal(1, sim.News.Count, "one news item");
            Assert.Equal(CompanyKey.AiPrefix + "3", sim.News[0].Company);
            Assert.Equal(3, sim.News[0].Rank, "the rank by share");
            Assert.True(sim.Journal.Contains("ms.fab_fire@" + CompanyKey.AiPrefix + "3"), "and the journal");
        }

        [Test]
        public void RivalNews_Ac24_AFireAtTheEighthByShare_IsOnlyInTheJournal()
        {
            ShockSimulator sim = Quiet();
            Assert.True(sim.Fire("ms.fab_fire", CompanyKey.AiPrefix + "8"), "the fire fired");   // the eighth by share (4 %)

            Assert.Equal(0, sim.News.Count, "no news");
            Assert.True(sim.Journal.Contains("ms.fab_fire@" + CompanyKey.AiPrefix + "8"), "the journal has it");
        }

        [Test]
        public void RivalNews_TheFifthIsIn_TheSixthIsOut()
        {
            ShockSimulator sim = Quiet();
            Assert.True(sim.Fire("ms.fab_fire", CompanyKey.AiPrefix + "5"));
            Assert.True(sim.Fire("ms.litho_breakdown", CompanyKey.AiPrefix + "6"));

            Assert.Equal(1, sim.News.Count, "the news items");
            Assert.Equal(5, sim.News[0].Rank);
            Assert.Equal(ShockRegistration.RivalNewsTopN, 5, "the pack asks the engine for the top five");
        }

        [Test]
        public void RivalNews_AFireAtThePlayer_IsALetter_NotANewsItem()
        {
            ShockSimulator sim = Quiet();
            Assert.True(sim.Fire("ms.fab_fire", CompanyKey.Player));
            Assert.Equal(0, sim.News.Count, "the news are about rivals");
        }

        [Test]
        public void RivalNews_InTheLongRun_NeverCoverBelowTheTopFive_AndTheJournalKeepsTheRest()
        {
            int news = 0, belowTop = 0;
            var journalRivals = new Dictionary<string, int>();
            foreach (ShockSimulator sim in BalanceSimulationTests.Share())
            {
                foreach (SimNews item in sim.News)
                {
                    news++;
                    Assert.True(item.Rank <= ShockRegistration.RivalNewsTopN, item.EventId + " at " + item.Company + " was news with rank " + item.Rank);
                }

                foreach (string entry in sim.Journal)
                {
                    int at = entry.IndexOf('@');
                    string company = entry.Substring(at + 1);
                    if (company == CompanyKey.Player) continue;
                    int count;
                    journalRivals.TryGetValue(company, out count);
                    journalRivals[company] = count + 1;
                }
            }

            // ai:6 ... ai:9 are the smaller four of the nine; the journal has them, the mailbox does not.
            foreach (string small in new[] { "ai:6", "ai:7", "ai:8", "ai:9" })
            {
                int count;
                journalRivals.TryGetValue(small, out count);
                Assert.True(count > 0, small + " has journal entries");
                belowTop += count;
            }
            Assert.True(news > 0 && belowTop > 0, "news " + news + ", journal entries about the smaller rivals " + belowTop);
        }

        [Test]
        public void OnlyTheShocksAtOneCompany_AskForNews_TheGroupsDoNot()
        {
            // One earthquake would otherwise make up to nine news items (D8: one instance per company of the group).
            foreach (JsonObject record in ShockContentData.Records())
            {
                string type = record.GetObject("targets").GetString("type", string.Empty);
                bool notify = record.GetBool("notifyPlayer", false);
                if (type == "AllCompanies" || type == "Player") Assert.False(notify, record.GetString("id", "?") + " (" + type + ") must not ask for news");
                if (notify) Assert.Equal("RandomCompany", type, record.GetString("id", "?") + ": news only for a shock at one random company");
            }
        }

        private static ShockSimulator Quiet()
        {
            var sim = BalanceSimulationTests.Quiet(1995);
            sim.RunDays(100);   // es.hasRevenueHistory
            return sim;
        }
    }
}
