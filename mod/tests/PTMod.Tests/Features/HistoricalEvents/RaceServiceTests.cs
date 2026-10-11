using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.HistoricalEvents;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The attribute races (TSK-014; FR-11, design.md formula 9): the top N of the market by frequency or by cores (a tie goes to the faster CPU), the bonus in [1.00, 1.10] in the
    /// segments of the race (AC-11.1), the monthly recount with the invalidation of <c>market.rating.cpu</c>, the end of the race, the product of the rules and the races in the
    /// one provider, and the restored race after a load.
    /// </summary>
    [Covers(typeof(RaceAttributes), typeof(RaceParams), typeof(RaceMath), typeof(HistRatingMath), typeof(RaceCore))]
    public class RaceServiceTests
    {
        private const string Player = "player";

        private sealed class Env
        {
            public HistSaveData Data = new HistSaveData();
            public readonly FakeWorld World = new FakeWorld();
            public readonly FakeEconomy Economy = new FakeEconomy();
            public readonly FakeModifiers Mods = new FakeModifiers();
            public readonly List<string> Log = new List<string>();
            public RaceCore Core;

            public Env()
            {
                World.AddPlayer();
                World.AddAi(1, "AMD");
                Economy.Today = 11000;
                Core = new RaceCore(() => Data, Economy, Mods, World, Log.Add);
            }

            public CpuInfo Cpu(string owner, float frequency, int cores = 1, float ips = 100f, Segment segment = Segment.HighEnd)
            {
                return World.AddCpu(World.Find(owner), segment, new[] { 1, 1 }, null, c =>
                {
                    c.Frequency = frequency;
                    c.Cores = cores;
                    c.Ips = ips;
                });
            }

            public RaceParams P(string json = "{}")
            {
                RaceParams p = RaceParams.Read(HistFixture.Obj(json), null);
                if (p.RaceId.Length == 0) p.RaceId = "ghz";
                return p;
            }

            public float Multiplier(CpuInfo cpu, Segment segment)
            {
                float m;
                string reason;
                int top;
                Core.Contribution(cpu.SaveId, segment, out m, out reason, out top);
                return m;
            }
        }

        [Test]
        public void Ac111_TheHighestFrequencyCpuOfThePlayer_GetsTenPercentInHighMidAndLowEnd()
        {
            var env = new Env();
            CpuInfo fastest = env.Cpu(Player, 3800f);
            env.Cpu("ai:1", 3200f);
            env.Cpu("ai:1", 2800f);
            env.Cpu("ai:1", 2400f);

            Assert.True(env.Core.Start(env.P("{'attribute':'frequency','topN':3,'bonus':1.10,'segments':['highend','midrange','lowend'],'durationDays':1460}")));

            Assert.Near(1.10, env.Multiplier(fastest, Segment.HighEnd), 1e-6);
            Assert.Near(1.10, env.Multiplier(fastest, Segment.MidRange), 1e-6);
            Assert.Near(1.10, env.Multiplier(fastest, Segment.LowEnd), 1e-6);
            Assert.Equal(1f, env.Multiplier(fastest, Segment.Industries), "a segment the race does not name");
            Assert.Equal(1f, env.Multiplier(fastest, Segment.Any), "a question without a segment");
        }

        [Test]
        public void OnlyTheTopN_Gets_TheBonus()
        {
            var env = new Env();
            CpuInfo a = env.Cpu(Player, 3800f);
            CpuInfo b = env.Cpu("ai:1", 3200f);
            CpuInfo c = env.Cpu("ai:1", 2800f);
            CpuInfo d = env.Cpu("ai:1", 2400f);
            env.Core.Start(env.P("{'topN':3}"));

            Assert.Near(1.10, env.Multiplier(a, Segment.HighEnd), 1e-6);
            Assert.Near(1.10, env.Multiplier(b, Segment.HighEnd), 1e-6);
            Assert.Near(1.10, env.Multiplier(c, Segment.HighEnd), 1e-6);
            Assert.Equal(1f, env.Multiplier(d, Segment.HighEnd), "the fourth is out");
        }

        [Test]
        public void TheCoreRace_RanksByCores_AndATieGoesToTheFasterCpu_ThenToTheSmallerId()
        {
            var env = new Env();
            CpuInfo slowEight = env.Cpu(Player, 3000f, 8, 400f);
            CpuInfo fastEight = env.Cpu("ai:1", 3000f, 8, 500f);
            CpuInfo twinA = env.Cpu(Player, 3000f, 4, 300f);
            CpuInfo twinB = env.Cpu("ai:1", 3000f, 4, 300f);
            env.Cpu(Player, 5000f, 2, 900f);

            List<int> top = RaceMath.TopCpus(env.World.Cpus, RaceAttributes.CoreCount, 3);
            Assert.Equal(new[] { fastEight.SaveId, slowEight.SaveId, twinA.SaveId }, top.ToArray(), "cores first, then Ips, then the smaller SaveId");
            Assert.False(top.Contains(twinB.SaveId));

            Assert.Equal(0, RaceMath.TopCpus(env.World.Cpus, RaceAttributes.Frequency, 0).Count, "topN 0 is an empty top");
            Assert.Equal("hist.mod.race.cores", RaceAttributes.ReasonKey(RaceAttributes.CoreCount));
            Assert.Equal("hist.mod.race.frequency", RaceAttributes.ReasonKey(RaceAttributes.Frequency));
        }

        [Test]
        public void OnlyCpusOnTheMarketCount()
        {
            var env = new Env();
            CpuInfo retired = env.Cpu(Player, 9000f);
            retired.Retired = true;
            CpuInfo unreleased = env.Cpu(Player, 8000f);
            unreleased.Released = false;
            CpuInfo notForSale = env.Cpu(Player, 7000f);
            notForSale.OnMarket = false;
            CpuInfo normal = env.Cpu("ai:1", 3000f);

            List<int> top = RaceMath.TopCpus(env.World.Cpus, RaceAttributes.Frequency, 5);
            Assert.Equal(new[] { normal.SaveId }, top.ToArray());
        }

        [Test]
        public void TheMonthlyRecount_FollowsTheMarket_AndInvalidatesTheRatingChannel()
        {
            var env = new Env();
            CpuInfo old = env.Cpu(Player, 3000f);
            env.Core.Start(env.P("{'topN':1}"));
            int started = env.Mods.InvalidationsOf("market.rating.cpu");
            Assert.True(started >= 1, "the start invalidates");
            Assert.Near(1.10, env.Multiplier(old, Segment.HighEnd), 1e-6);

            CpuInfo faster = env.Cpu("ai:1", 3600f);
            Assert.Equal(1.10f, env.Multiplier(old, Segment.HighEnd), "nothing changes before the monthly pass");

            env.Economy.Today += 30;
            env.Core.OnMonth();
            Assert.Equal(1f, env.Multiplier(old, Segment.HighEnd), "the new leader took the bonus");
            Assert.Near(1.10, env.Multiplier(faster, Segment.HighEnd), 1e-6);
            Assert.Equal(started + 1, env.Mods.InvalidationsOf("market.rating.cpu"), "the recount invalidates the channel");
        }

        [Test]
        public void WhenTheRaceEnds_TheContributionGoes()
        {
            var env = new Env();
            CpuInfo cpu = env.Cpu(Player, 3000f);
            env.Core.Start(env.P("{'durationDays':100}"));
            Assert.Near(1.10, env.Multiplier(cpu, Segment.HighEnd), 1e-6);

            env.Economy.Today += 101;
            Assert.Equal(1f, env.Multiplier(cpu, Segment.HighEnd), "the provider stops honouring it on the day, before the monthly pass");

            int before = env.Mods.InvalidationsOf("market.rating.cpu");
            env.Core.OnMonth();
            Assert.Equal(0, env.Core.Races.Count, "the pass removes the race");
            Assert.Equal(before + 1, env.Mods.InvalidationsOf("market.rating.cpu"));
        }

        [Test]
        public void AfterALoad_TheProviderReadsTheRestoredRace()
        {
            var env = new Env();
            CpuInfo cpu = env.Cpu(Player, 3000f);
            env.Core.Start(env.P());
            Assert.Near(1.10, env.Multiplier(cpu, Segment.HighEnd), 1e-6);

            var loaded = new HistSaveData();
            loaded.races.Add(new RaceDto { raceId = "restored", attribute = RaceAttributes.CoreCount, topN = 2, bonus = 1.05f, endDay = 20000, segments = { (int)Segment.HighEnd }, topCpuIds = { cpu.SaveId } });
            env.Data = loaded;

            float m;
            string reason;
            int top;
            Assert.True(env.Core.Contribution(cpu.SaveId, Segment.HighEnd, out m, out reason, out top));
            Assert.Near(1.05, m, 1e-6);
            Assert.Equal("hist.mod.race.cores", reason);
            Assert.Equal(2, top);
            Assert.False(env.Core.Contribution(cpu.SaveId, Segment.LowEnd, out m, out reason, out top));
        }

        [Test]
        public void TheBonusInTheSave_IsHeldToTheRangeOfTheDesign()
        {
            var env = new Env();
            CpuInfo cpu = env.Cpu(Player, 3000f);
            var loaded = new HistSaveData();
            loaded.races.Add(new RaceDto { raceId = "x", attribute = RaceAttributes.Frequency, topN = 1, bonus = 5f, endDay = 20000, segments = { (int)Segment.HighEnd }, topCpuIds = { cpu.SaveId } });
            env.Data = loaded;

            Assert.Near(1.10, env.Multiplier(cpu, Segment.HighEnd), 1e-6, "a bonus above 1.10 is cut to it");
        }

        [Test]
        public void Start_NeedsAnId_AndTheSameRaceIsNotStartedTwice()
        {
            var env = new Env();
            env.Cpu(Player, 3000f);
            Assert.False(env.Core.Start(new RaceParams()), "no id");
            Assert.True(env.Core.Start(env.P()));
            Assert.False(env.Core.Start(env.P()));
            Assert.Equal(1, env.Core.Races.Count);
            Assert.NotNull(env.Core.Find("ghz"));
            Assert.Null(env.Core.Find("other"));
        }

        [Test]
        public void TheProviderCombinesTheRulesAndTheRaces_WithTheReasonOfTheLargerDeviation()
        {
            string[] exportArgs = { "2.0G" };
            string[] raceArgs = { "3" };

            ProviderResult both = HistRatingMath.Combine(0.85f, "hist.mod.export", exportArgs, 1.10f, "hist.mod.race.frequency", raceArgs);
            Assert.Near(0.85 * 1.10, both.Multiplier, 1e-6);
            Assert.Equal("hist.mod.export", both.ReasonKey, "the rule moves the rating by 15 %, the race by 10 %");
            Assert.Equal(exportArgs, both.ReasonArgs);

            ProviderResult raceWins = HistRatingMath.Combine(0.95f, "hist.mod.export", exportArgs, 1.10f, "hist.mod.race.cores", raceArgs);
            Assert.Equal("hist.mod.race.cores", raceWins.ReasonKey);

            ProviderResult onlyExport = HistRatingMath.Combine(0.80f, "hist.mod.export", exportArgs, 1f, null, null);
            Assert.Equal("hist.mod.export", onlyExport.ReasonKey);
            Assert.Near(0.80, onlyExport.Multiplier, 1e-6);

            ProviderResult onlyRace = HistRatingMath.Combine(1f, null, null, 1.10f, "hist.mod.race.cores", raceArgs);
            Assert.Equal("hist.mod.race.cores", onlyRace.ReasonKey);

            ProviderResult none = HistRatingMath.Combine(1f, "hist.mod.export", exportArgs, 1f, "hist.mod.race.cores", raceArgs);
            Assert.Equal(1f, none.Multiplier);
            Assert.Null(none.ReasonKey, "no reason when nothing applies");

            ProviderResult bad = HistRatingMath.Combine(float.NaN, null, null, 1.10f, "hist.mod.race.cores", raceArgs);
            Assert.Near(1.10, bad.Multiplier, 1e-6, "a number that is not finite counts as 1");
        }

        [Test]
        public void TheContribution_StaysInTheRangeOfTheDesign_ForEveryRuleAndRace()
        {
            // The worst case of the design: 0.80 from a rule with the full share, 1.10 from a race: the product is in [0.80, 1.10].
            for (float share = 0f; share <= 0.2001f; share += 0.05f)
            {
                float product = ExportRuleMath.Multiplier(share, 1f) * RaceParams.MaxBonus;
                Assert.True(product >= 0.80f * 1f - 1e-6f && product <= 1.10f + 1e-6f, "the product stays in [0.80, 1.10]: " + product);
            }
        }

        [Test]
        public void TheNumbers_ReadWithTheDefaults_AndRejectWhatIsOutOfRange()
        {
            var problems = new List<string>();
            RaceParams d = RaceParams.Read(new JsonObject(), problems);
            Assert.Equal(0, problems.Count);
            Assert.Equal(3, d.TopN);
            Assert.Equal(1.10f, d.Bonus);
            Assert.Equal(RaceAttributes.Frequency, d.Attribute);

            RaceParams p = RaceParams.Read(HistFixture.Obj("{'raceId':'cores','attribute':'coreCount','topN':3,'bonus':1.10,'segments':['highend','midrange'],'durationDays':1095}"), null);
            Assert.Equal("cores", p.RaceId);
            Assert.Equal(RaceAttributes.CoreCount, p.Attribute);
            Assert.Equal(2, p.Segments.Count);
            Assert.Equal(1095, p.DurationDays);

            foreach (string bad in new[] { "{'bonus':1.5}", "{'bonus':0.9}", "{'topN':0}", "{'topN':50}", "{'attribute':'color'}", "{'segments':['moon']}", "{'durationDays':1}" })
            {
                var lines = new List<string>();
                RaceParams.Read(HistFixture.Obj(bad), lines);
                Assert.True(lines.Count > 0, bad + " must be a problem");
            }
        }
    }
}
