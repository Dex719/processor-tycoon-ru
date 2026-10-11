using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.HistoricalEvents;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The export rule (TSK-011; FR-7, design.md formula 6): the percentile of the market, the threshold indexed to the average speed, the multiplier of a CPU above it in the
    /// segments of the rule (AC-7.1), the licence and the lobbying (AC-7.2), the second stage, the range of the contribution [0.80, 1.00], the invalidation of the channel
    /// on every change, and the provider reading a rule restored by a load.
    /// </summary>
    [Covers(typeof(ExportActions), typeof(ExemptionKinds), typeof(ExportKeys), typeof(ExportParams), typeof(ExportQuote), typeof(ExportRuleMath), typeof(ExportCore))]
    public class ExportRuleMathTests
    {
        private const string Player = "player";

        private sealed class Env
        {
            public HistSaveData Data = new HistSaveData();
            public readonly FakeWorld World = new FakeWorld();
            public readonly FakeEconomy Economy = new FakeEconomy();
            public readonly FakeModifiers Mods = new FakeModifiers();
            public readonly List<string> Log = new List<string>();
            public ExportCore Core;

            public Env(int cpus = 11)
            {
                World.AddPlayer();
                World.AddAi(1, "AMD");
                World.AverageIps = 100f;
                Economy.Revenue[Player] = 10e9f;
                Economy.Revenue["ai:1"] = 5e9f;
                Economy.Today = 18541;

                // The market: Ips 1.0e9 ... 3.0e9 in steps of 0.2e9 (11 CPUs), so the 90th percentile is exactly the tenth of them, 2.8e9... see Percentile().
                for (int i = 0; i < cpus; i++)
                {
                    int index = i;
                    World.AddCpu(World.Find(i % 2 == 0 ? Player : "ai:1"), Segment.HighEnd, new[] { 100, 100 }, null, c => c.Ips = 1.0e9f + index * 0.2e9f);
                }
                Core = new ExportCore(() => Data, Economy, Mods, World, ips => (ips / 1e9f).ToString("0.0") + "G", Log.Add);
            }

            public ExportParams P(string json = "{}")
            {
                var p = ExportParams.Read(HistFixture.Obj(json), null);
                if (p.RuleId.Length == 0) p.RuleId = "us2022";
                return p;
            }

            public bool Contribution(float ips, Segment segment, string company, out float multiplier)
            {
                string text;
                return Core.Contribution(ips, segment, company, out multiplier, out text);
            }
        }

        [Test]
        public void Percentile_InterpolatesBetweenRanks_AndIsExactOnARank()
        {
            var eleven = new List<float>();
            for (int i = 0; i <= 10; i++) eleven.Add(i * 1.0e8f);

            Assert.Near(9e8, ExportRuleMath.Percentile(eleven, 0.90f), 1e3, "n - 1 = 10, p = 0.9: exactly the tenth");
            Assert.Near(5e8, ExportRuleMath.Percentile(eleven, 0.50f), 1e3);
            Assert.Near(0f, ExportRuleMath.Percentile(eleven, 0f), 1e-3);
            Assert.Near(1e9, ExportRuleMath.Percentile(eleven, 1f), 1e3);
            Assert.Near(1.5, ExportRuleMath.Percentile(new List<float> { 1f, 2f }, 0.5f), 1e-6, "between two ranks");
            Assert.Equal(0f, ExportRuleMath.Percentile(new List<float>(), 0.9f));
            Assert.Equal(0f, ExportRuleMath.Percentile(null, 0.9f));
            Assert.Equal(4f, ExportRuleMath.Percentile(new List<float> { 4f, float.NaN, float.PositiveInfinity }, 0.9f), "numbers that are not finite are left out");
        }

        [Test]
        public void TheThreshold_FollowsTheAverageSpeedOfTheMarket_ByTheIndexingPower()
        {
            Assert.Near(2e9, ExportRuleMath.Threshold(2e9f, 100f, 100f, 0.8f), 1e3, "unchanged market, unchanged threshold");
            Assert.Near(2e9 * System.Math.Pow(2, 0.8), ExportRuleMath.Threshold(2e9f, 100f, 200f, 0.8f), 1e4, "a market twice as fast, a threshold 2 ^ 0.8 higher");
            Assert.Near(4e9, ExportRuleMath.Threshold(2e9f, 100f, 200f, 1f), 1e4, "full indexing");
            Assert.Near(2e9, ExportRuleMath.Threshold(2e9f, 100f, 200f, 0f), 1e3, "none");
            Assert.Equal(2e9f, ExportRuleMath.Threshold(2e9f, 0f, 200f, 0.8f), "no base average: it stays");
            Assert.Equal(2e9f, ExportRuleMath.Threshold(2e9f, 100f, float.NaN, 0.8f), "no current average: it stays");
        }

        [Test]
        public void TheMultiplier_IsOneMinusTheBlockedShareTimesTheLobbyFactor_InTheRangeOfTheDesign()
        {
            Assert.Near(0.85, ExportRuleMath.Multiplier(0.15f, 1f), 1e-6);
            Assert.Near(0.80, ExportRuleMath.Multiplier(0.20f, 1f), 1e-6);
            Assert.Near(0.925, ExportRuleMath.Multiplier(0.15f, 0.5f), 1e-6, "AC-7.2");
            Assert.Near(0.90, ExportRuleMath.Multiplier(0.20f, 0.5f), 1e-6);
            Assert.Equal(1f, ExportRuleMath.Multiplier(0.15f, 0f), "a fully lobbied company is not touched");
            Assert.Near(0.80, ExportRuleMath.Multiplier(0.90f, 1f), 1e-6, "a share above the 0.20 ceiling is cut to it: never below 0.80");
            Assert.Equal(1f, ExportRuleMath.Multiplier(-1f, 1f), "and never above 1");

            for (float share = 0f; share <= 0.2001f; share += 0.01f)
            {
                for (float factor = 0f; factor <= 1.001f; factor += 0.25f)
                {
                    float m = ExportRuleMath.Multiplier(share, factor);
                    Assert.True(m >= 0.8f - 1e-6f && m <= 1f, "the contribution stays in [0.80, 1.00]: " + m);
                }
            }
        }

        [Test]
        public void Ac71_AFastCpuInTheRulesSegmentsLosesFifteenPercentOfItsRating_ACutDownOneDoesNot()
        {
            var env = new Env();
            // Make the 90th percentile exactly 2.0e9: eleven CPUs 0.0 ... 2.5e9 would not do, so set them by hand.
            env.World.CpuList.Clear();
            for (int i = 0; i <= 10; i++)
            {
                int n = i;
                env.World.AddCpu(env.World.Find(Player), Segment.HighEnd, new[] { 1 }, null, c => c.Ips = n == 9 ? 2.0e9f : n < 9 ? 1.0e9f + n * 0.1e9f : 2.6e9f);
            }

            Assert.True(env.Core.Open(env.P("{'percentile':0.9,'blockedShare':0.15,'segments':['highend']}")));
            Assert.Near(2.0e9, env.Core.ThresholdOf("us2022").Value, 1e3, "P90 = 2.0e9");

            float multiplier;
            Assert.True(env.Contribution(2.4e9f, Segment.HighEnd, Player, out multiplier));
            Assert.Near(0.85, multiplier, 1e-6, "2.4e9 in HighEnd: Overall x 0.85");

            Assert.False(env.Contribution(1.8e9f, Segment.HighEnd, Player, out multiplier), "the cut-down variant of 1.8e9 is below the threshold");
            Assert.Equal(1f, multiplier);

            Assert.False(env.Contribution(2.4e9f, Segment.LowEnd, Player, out multiplier), "a segment the rule does not name");
            Assert.False(env.Contribution(2.4e9f, Segment.Any, Player, out multiplier), "a question without a segment");
        }

        [Test]
        public void Ac72_ASuccessfulLobbying_HalvesTheBlockedShare_ForTwentyFourMonths()
        {
            var env = new Env();
            env.Core.Open(env.P("{'blockedShare':0.15,'segments':['highend']}"));
            float multiplier;
            Assert.True(env.Contribution(5e9f, Segment.HighEnd, Player, out multiplier));
            Assert.Near(0.85, multiplier, 1e-6);

            ExportParams p = env.P("{'lobbyCostPct':0.01,'lobbySuccess':0.5,'lobbyFactor':0.5,'lobbyMonths':24}");
            Assert.True(env.Core.Lobby("us2022", Player, p, 0.2f));
            Assert.Near(0.01 * 10e9, env.Economy.Spent(Player, "Fines"), 1e3, "1 % of the revenue, paid whatever the answer");

            Assert.True(env.Contribution(5e9f, Segment.HighEnd, Player, out multiplier));
            Assert.Near(0.925, multiplier, 1e-6, "1 - 0.15 x 0.5");
            Assert.True(env.Contribution(5e9f, Segment.HighEnd, "ai:1", out multiplier));
            Assert.Near(0.85, multiplier, 1e-6, "another company is not helped");

            env.Economy.Today += HistTime.MonthsToDays(24) + 1;
            Assert.True(env.Contribution(5e9f, Segment.HighEnd, Player, out multiplier));
            Assert.Near(0.85, multiplier, 1e-6, "after 24 months the provider no longer honours it, even before the monthly pass drops the entry");
        }

        [Test]
        public void ALicence_ExemptsTheCompany_AndAFailedOneStillCosts()
        {
            var env = new Env();
            env.Core.Open(env.P("{'blockedShare':0.15,'segments':['highend']}"));
            ExportParams p = env.P("{'licenseCostPct':0.02,'licenseSuccess':0.6,'licenseMonths':18}");

            Assert.True(env.Core.License("us2022", Player, p, 0.95f), "denied");
            Assert.Near(0.02 * 10e9, env.Economy.Spent(Player), 1e3, "but paid");
            float multiplier;
            Assert.True(env.Contribution(5e9f, Segment.HighEnd, Player, out multiplier));
            Assert.True(env.Economy.Notes(Player).Any(n => n.Origin == ExportKeys.NoteLicenseDenied));

            Assert.True(env.Core.License("us2022", Player, p, 0.3f), "granted");
            Assert.False(env.Contribution(5e9f, Segment.HighEnd, Player, out multiplier), "exempt");
            Assert.Equal(1f, multiplier);
            Assert.True(env.Economy.Notes(Player).Any(n => n.Origin == ExportKeys.NoteLicenseGranted));
            Assert.True(env.Contribution(5e9f, Segment.HighEnd, "ai:1", out multiplier), "the others are not");

            env.Economy.Today += HistTime.MonthsToDays(18) + 1;
            Assert.True(env.Contribution(5e9f, Segment.HighEnd, Player, out multiplier), "and the licence runs out");
        }

        [Test]
        public void AnAnswerToARuleThatIsNotInForce_CostsNothing()
        {
            var env = new Env();
            Assert.False(env.Core.License("none", Player, env.P(), 0.1f));
            Assert.False(env.Core.Lobby("none", Player, env.P(), 0.1f));
            Assert.Equal(0, env.Economy.Flows.Count);
        }

        [Test]
        public void TheSecondStage_TightensTheSameRule_AndTheBoughtExemptionsStay()
        {
            var env = new Env();
            env.Core.Open(env.P("{'percentile':0.9,'blockedShare':0.15,'segments':['highend']}"));
            float first = env.Core.ThresholdOf("us2022").Value;
            env.Core.Lobby("us2022", Player, env.P("{'lobbySuccess':1,'lobbyFactor':0.5,'lobbyMonths':24}"), 0.0f);

            env.World.AverageIps = 150f;
            Assert.True(env.Core.Update(env.P("{'percentile':0.75,'blockedShare':0.20,'segments':['highend']}")));

            Assert.Equal(1, env.Core.Rules.Count, "the same rule");
            ExportRuleDto rule = env.Core.Find("us2022");
            Assert.Near(150f, rule.avgIps0, 1e-3, "the average is re-based to the day of the update");
            Assert.True(rule.threshold < first, "the 75th percentile is lower than the 90th: " + rule.threshold + " < " + first);
            Assert.Equal(0.20f, rule.blockedShare);

            float multiplier;
            Assert.True(env.Contribution(5e9f, Segment.HighEnd, "ai:1", out multiplier));
            Assert.Near(0.80, multiplier, 1e-6, "0.20 on the second stage");
            Assert.True(env.Contribution(5e9f, Segment.HighEnd, Player, out multiplier));
            Assert.Near(0.90, multiplier, 1e-6, "and the lobbying still halves it: 1 - 0.20 x 0.5");
        }

        [Test]
        public void AnUpdateOfARuleThatIsNotThere_IntroducesIt()
        {
            var env = new Env();
            Assert.True(env.Core.Update(env.P("{'percentile':0.75,'blockedShare':0.2}")));
            Assert.NotNull(env.Core.Find("us2022"));
        }

        [Test]
        public void TheMonthlyPass_IndexesTheThreshold_DropsWhatRanOut_AndEndsTheRuleOnItsDay()
        {
            var env = new Env();
            env.Core.Open(env.P("{'percentile':0.9,'indexing':1.0,'durationDays':400,'segments':['highend']}"));
            float start = env.Core.ThresholdOf("us2022").Value;

            env.World.AverageIps = 200f;
            env.Economy.Today += 30;
            env.Core.OnMonth();
            Assert.Near(start * 2, env.Core.ThresholdOf("us2022").Value, start * 1e-4, "the market doubled, the threshold with it");

            int before = env.Mods.InvalidationsOf("market.rating.cpu");
            env.Economy.Today += 30;
            env.Core.OnMonth();
            Assert.Equal(before + 1, env.Mods.InvalidationsOf("market.rating.cpu"), "every pass invalidates the channel");

            env.Economy.Today += 400;
            env.Core.OnMonth();
            Assert.Null(env.Core.ThresholdOf("us2022"), "the rule ended");
            float multiplier;
            Assert.False(env.Contribution(9e9f, Segment.HighEnd, Player, out multiplier), "and no longer touches anyone");
        }

        [Test]
        public void EveryChangeOfTheState_InvalidatesTheRatingChannel()
        {
            var env = new Env();
            env.Core.Open(env.P());
            Assert.Equal(1, env.Mods.InvalidationsOf("market.rating.cpu"), "introduced");
            env.Core.Update(env.P("{'percentile':0.75,'blockedShare':0.2}"));
            Assert.Equal(2, env.Mods.InvalidationsOf("market.rating.cpu"), "tightened");
            env.Core.License("us2022", Player, env.P("{'licenseSuccess':1}"), 0f);
            Assert.Equal(3, env.Mods.InvalidationsOf("market.rating.cpu"), "a licence granted");
            env.Core.Lobby("us2022", Player, env.P("{'lobbySuccess':1}"), 0f);
            Assert.Equal(4, env.Mods.InvalidationsOf("market.rating.cpu"), "lobbying succeeded");
            env.Core.OnMonth();
            Assert.Equal(5, env.Mods.InvalidationsOf("market.rating.cpu"), "the threshold recomputed");

            env.Core.License("us2022", Player, env.P("{'licenseSuccess':0.1}"), 0.9f);
            Assert.Equal(5, env.Mods.InvalidationsOf("market.rating.cpu"), "a refusal changes nothing");
        }

        [Test]
        public void AfterALoad_TheProviderReadsTheRestoredRule()
        {
            var env = new Env();
            env.Core.Open(env.P("{'blockedShare':0.15,'segments':['highend']}"));
            float multiplier;
            Assert.True(env.Contribution(9e9f, Segment.HighEnd, Player, out multiplier));

            // A load replaces the object of the section: the flat copy must be built again from it, not kept.
            var loaded = new HistSaveData();
            loaded.exportRules.Add(new ExportRuleDto { ruleId = "restored", threshold0 = 5e9f, avgIps0 = 100f, threshold = 5e9f, blockedShare = 0.2f, endDay = 30000, segments = { (int)Segment.HighEnd } });
            env.Data = loaded;

            Assert.False(env.Contribution(9e9f, Segment.LowEnd, Player, out multiplier));
            Assert.True(env.Contribution(9e9f, Segment.HighEnd, Player, out multiplier));
            Assert.Near(0.80, multiplier, 1e-6, "the restored rule: 20 %");
            Assert.False(env.Contribution(4e9f, Segment.HighEnd, Player, out multiplier), "below the restored threshold");
            Assert.Equal(5e9f, env.Core.ThresholdOf("restored").Value);
            Assert.Null(env.Core.ThresholdOf("us2022"), "the rule of the old state is gone");
        }

        [Test]
        public void TheReasonCarriesTheThreshold_AndTheStrongestRuleWins()
        {
            var env = new Env();
            env.Core.Open(env.P("{'ruleId':'a','blockedShare':0.10,'segments':['highend']}"));
            env.Core.Open(env.P("{'ruleId':'b','blockedShare':0.20,'segments':['highend']}"));

            float multiplier;
            string text;
            Assert.True(env.Core.Contribution(9e9f, Segment.HighEnd, Player, out multiplier, out text));
            Assert.Near(0.80, multiplier, 1e-6, "the stronger of the two, not their product (the range stays [0.80, 1.00])");
            Assert.True(text.EndsWith("G"), "the threshold text for the reason: " + text);
        }

        [Test]
        public void ARuleOnAMarketWithoutCpus_StartsAboveTheAverage_NotAtZero()
        {
            var env = new Env(0);
            Assert.True(env.Core.Open(env.P()));
            Assert.Near(200f, env.Core.ThresholdOf("us2022").Value, 1e-3, "twice the average: a threshold of 0 would block every CPU that ever appears");
            Assert.True(env.Log.Any(l => l.Contains("twice the average")));
        }

        [Test]
        public void ARuleNeedsAnId_AndTheSameIdIsNotIntroducedTwice()
        {
            var env = new Env();
            Assert.False(env.Core.Open(new ExportParams()), "no id");
            Assert.True(env.Core.Open(env.P()));
            Assert.False(env.Core.Open(env.P()), "twice");
            Assert.Equal(1, env.Core.Rules.Count);
        }

        [Test]
        public void TheQuote_ShowsTheCostsOfTheAnswers()
        {
            ExportParams p = ExportParams.Read(HistFixture.Obj("{'licenseCostPct':0.02,'licenseSuccess':0.6,'licenseMonths':18,'lobbyCostPct':0.01,'lobbySuccess':0.5,'lobbyFactor':0.5,'lobbyMonths':24}"), null);

            ExportQuote license = ExportRuleMath.Quote(ExportActions.License, p, 10e9f);
            Assert.Near(2e8, license.Upfront, 1e3);
            Assert.Equal(0.6f, license.Success);
            Assert.Equal(18, license.Months);

            ExportQuote lobby = ExportRuleMath.Quote(ExportActions.Lobby, p, 10e9f);
            Assert.Near(1e8, lobby.Upfront, 1e3);
            Assert.Equal(0.5f, lobby.Factor);

            Assert.Equal(0f, ExportRuleMath.Quote(ExportActions.Comply, p, 10e9f).Upfront, "complying is free");
            Assert.Equal("hist.fx.export.open", ExportRuleMath.Quote(ExportActions.Open, p, 0f).TextKey);
        }

        [Test]
        public void TheNumbers_ReadWithTheDefaults_AndRejectWhatIsOutOfRange()
        {
            var problems = new List<string>();
            ExportParams d = ExportParams.Read(new JsonObject(), problems);
            Assert.Equal(0, problems.Count);
            Assert.Equal(0.90f, d.Percentile);
            Assert.Equal(0.15f, d.BlockedShare);
            Assert.Equal(ExportActions.Open, ExportParams.ActionOf(null));

            ExportParams two = ExportParams.Read(HistFixture.Obj("{'segments':['highend','all','highend']}"), null);
            Assert.Equal(4, two.Segments.Count, "all adds the four, once each");

            foreach (string bad in new[]
            {
                "{'blockedShare':0.5}", "{'percentile':1.0}", "{'percentile':0}", "{'segments':['moon']}", "{'segments':[]}", "{'durationDays':1}", "{'lobbyFactor':2}",
                "{'licenseSuccess':-0.1}", "{'action':'bribe'}", "{'indexing':'x'}",
            })
            {
                var lines = new List<string>();
                ExportParams.Read(HistFixture.Obj(bad), lines);
                Assert.True(lines.Count > 0, bad + " must be a problem");
            }
        }

        [Test]
        public void TheMasks_NameTheSegments_WithoutAllocating()
        {
            Assert.Equal(1, ExportRuleMath.Bit(Segment.HighEnd));
            Assert.Equal(8, ExportRuleMath.Bit(Segment.Industries));
            Assert.Equal(0, ExportRuleMath.Bit(Segment.Any));
            Assert.Equal(3, ExportRuleMath.MaskOf(new[] { Segment.HighEnd, Segment.MidRange }));
            Assert.Equal(5, ExportRuleMath.MaskOfNumbers(new[] { 0, 2 }));
            Assert.Equal(0, ExportRuleMath.MaskOf(null));
        }
    }
}
