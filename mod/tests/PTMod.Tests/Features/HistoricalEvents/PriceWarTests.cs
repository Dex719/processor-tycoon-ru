using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.HistoricalEvents;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The price wars (TSK-012; FR-8, design.md formula 7): through the API of <c>rival-personalities</c> while it runs, with no price of an AI company written by the feature
    /// (AC-8.2), and by a fallback cut with a floor and the original targets put back otherwise (AC-8.1); the player's own prices, the marketing, the end of the war, the
    /// switch-off of the feature and the bankruptcy of a company.
    /// </summary>
    [Covers(typeof(WarActions), typeof(WarParams), typeof(MarketCpu), typeof(IMarketPrices), typeof(IPriceCutApi), typeof(PriceWarFallback), typeof(PriceWarCore))]
    public class PriceWarTests
    {
        private const string Player = "player";
        private const string Amd = "ai:1";
        private const string Intel = "ai:2";

        private sealed class FakePrices : IMarketPrices
        {
            public readonly List<MarketCpu> Cpus = new List<MarketCpu>();
            public readonly List<string> Writes = new List<string>();

            public IReadOnlyList<MarketCpu> MarketCpus()
            {
                return new List<MarketCpu>(Cpus);
            }

            public MarketCpu Find(int saveId)
            {
                return Cpus.Find(c => c.SaveId == saveId);
            }

            public void SetPrice(int saveId, float price)
            {
                MarketCpu cpu = Find(saveId);
                if (cpu == null) return;
                cpu.Price = price;
                Writes.Add("price " + saveId + " " + price);
            }

            public void SetTargetPrice(int saveId, float target)
            {
                MarketCpu cpu = Find(saveId);
                if (cpu == null) return;
                cpu.TargetPrice = target;
                Writes.Add("target " + saveId + " " + target);
            }

            public MarketCpu Add(int id, string company, Segment segment, float price, float runit, float target = float.NaN)
            {
                var cpu = new MarketCpu
                {
                    SaveId = id, CompanyKey = company, IsPlayer = company == Player, Segment = segment, Price = price,
                    TargetPrice = float.IsNaN(target) ? price : target, RealUnitCost = runit,
                };
                Cpus.Add(cpu);
                return cpu;
            }
        }

        private sealed class FakeApi : IPriceCutApi
        {
            public bool Enabled;
            public int CutsPerCall = 1;
            public readonly List<string> Calls = new List<string>();
            public readonly List<string> Restores = new List<string>();

            public bool IsEnabled
            {
                get { return Enabled; }
            }

            public int ApplyPriceCut(string reactionId, string rivalKey, Segment segment, float depth, float floorMargin)
            {
                Calls.Add(reactionId + "|" + rivalKey + "|" + segment + "|" + depth.ToString("0.00") + "|" + floorMargin.ToString("0.00"));
                return CutsPerCall;
            }

            public void RestorePriceCut(string reactionId)
            {
                Restores.Add(reactionId);
            }
        }

        private sealed class Env
        {
            public readonly HistSaveData Data = new HistSaveData();
            public readonly FakeEconomy Economy = new FakeEconomy();
            public readonly FakeModifiers Mods = new FakeModifiers();
            public readonly FakePrices Prices = new FakePrices();
            public readonly FakeApi Api = new FakeApi();
            public readonly List<string> Log = new List<string>();
            public IReadOnlyList<string> Ranking = new string[0];
            public PriceWarCore Core;

            public Env()
            {
                Economy.Revenue[Player] = 10e9f;
                Core = new PriceWarCore(() => Data, Economy, Mods, Api, Prices, segments => Ranking, Log.Add);
            }

            public static WarParams War(string json = "{}")
            {
                WarParams p = WarParams.Read(HistFixture.Obj(json), null, null);
                if (p.WarId.Length == 0) p.WarId = "core2";
                return p;
            }
        }

        // ---- AC-8.1: the fallback ----

        [Test]
        public void Ac81_WithoutRivalPersonalities_AnAiCpuIsCut_AndItsOriginalTargetComesBack()
        {
            var env = new Env();
            MarketCpu amd = env.Prices.Add(10, Amd, Segment.HighEnd, 500f, 120f);
            WarParams war = Env.War("{'segments':['highend'],'cut':0.20,'followCut':0.10,'durationDays':180,'floorMargin':0.05}");

            Assert.True(env.Core.Start(war, Amd));
            Assert.Near(400, amd.Price, 1e-3, "the price falls to 400");
            Assert.Near(400, amd.TargetPrice, 1e-3, "and so does the target");
            Assert.Equal(0, env.Api.Calls.Count, "rival-personalities is not asked");

            PriceWarDto dto = env.Core.Find("core2");
            Assert.False(dto.viaRivalApi);
            Assert.Equal(1, dto.restore.Count);
            Assert.Near(500, dto.restore[0].originalTargetPrice, 1e-3);

            for (int day = 1; day < 180; day++)
            {
                env.Economy.Today = 14000 + day;
                env.Core.OnDay();
            }
            Assert.Equal(1, env.Core.Wars.Count, "the war runs 180 days");

            env.Economy.Today = 14000 + 180;
            env.Core.OnDay();
            Assert.Equal(0, env.Core.Wars.Count);
            Assert.Near(500, amd.TargetPrice, 1e-3, "after 180 days the target is 500 again");
            Assert.Near(400, amd.Price, 1e-3, "the price is left to the AI's own logic (ChangePrices), the war does not move it");
        }

        [Test]
        public void TheFallbackCut_KeepsTheFloor_AndNeverRaisesAPrice()
        {
            var env = new Env();
            MarketCpu dear = env.Prices.Add(10, Amd, Segment.HighEnd, 500f, 450f);
            MarketCpu loss = env.Prices.Add(11, Amd, Segment.HighEnd, 300f, 450f);
            env.Core.Start(Env.War("{'segments':['highend'],'cut':0.20,'floorMargin':0.05}"), Amd);

            Assert.Near(472.5, dear.Price, 1e-3, "500 x 0.8 = 400 is below the floor 450 x 1.05");
            Assert.Near(300, loss.Price, 1e-3, "a CPU sold below the floor is left alone: the war does not raise it");
            Assert.Near(472.5, dear.TargetPrice, 1e-3);
        }

        [Test]
        public void TheOtherAiCompanies_FollowByTheSmallerCut_AndOnlyInTheSegmentsOfTheWar()
        {
            var env = new Env();
            MarketCpu aggressor = env.Prices.Add(10, Amd, Segment.HighEnd, 500f, 100f);
            MarketCpu follower = env.Prices.Add(11, Intel, Segment.HighEnd, 600f, 100f);
            MarketCpu elsewhere = env.Prices.Add(12, Intel, Segment.LowEnd, 100f, 40f);
            env.Core.Start(Env.War("{'segments':['highend'],'cut':0.20,'followCut':0.10}"), Amd);

            Assert.Near(400, aggressor.Price, 1e-3);
            Assert.Near(540, follower.Price, 1e-3);
            Assert.Near(100, elsewhere.Price, 1e-3, "LowEnd is not in the war");
        }

        // ---- AC-8.2: through rival-personalities ----

        [Test]
        public void Ac82_WithRivalPersonalities_TheCutsGoThroughItsApi_AndNoAiPriceIsWritten()
        {
            var env = new Env();
            env.Api.Enabled = true;
            MarketCpu amd = env.Prices.Add(10, Amd, Segment.HighEnd, 500f, 120f);
            MarketCpu intel = env.Prices.Add(11, Intel, Segment.HighEnd, 600f, 120f);
            env.Core.Start(Env.War("{'warId':'core2','segments':['highend'],'cut':0.20,'followCut':0.10,'floorMargin':0.05}"), Amd);

            CollectionAssert(env.Api.Calls, "hist:core2|ai:1|HighEnd|0.20|0.05", "the aggressor cuts by 0.20");
            CollectionAssert(env.Api.Calls, "hist:core2|ai:2|HighEnd|0.10|0.05", "the other AI company by 0.10");
            Assert.Equal(0, env.Prices.Writes.Count, "the feature writes no price itself");
            Assert.Near(500, amd.Price, 1e-3);
            Assert.Near(600, intel.Price, 1e-3);
            Assert.True(env.Core.Find("core2").viaRivalApi);
            Assert.Equal(0, env.Core.Find("core2").restore.Count, "nothing to restore itself: rival-personalities keeps the originals");

            env.Economy.Today = 14000 + 180;
            env.Core.OnDay();
            Assert.Equal(new[] { "hist:core2" }, env.Api.Restores.ToArray(), "RestorePriceCut(\"hist:<warId>\") at the end");
            Assert.Equal(0, env.Prices.Writes.Count, "and still no price written");
        }

        [Test]
        public void TheReactionIdIsHistColonTheWarId()
        {
            Assert.Equal("hist:core2", PriceWarCore.ReactionId("core2"));
        }

        [Test]
        public void WhenTheApiDeclines_NothingIsWrittenDirectly()
        {
            var env = new Env();
            env.Api.Enabled = true;
            env.Api.CutsPerCall = 0;
            MarketCpu amd = env.Prices.Add(10, Amd, Segment.HighEnd, 500f, 120f);
            env.Core.Start(Env.War("{'segments':['highend'],'cut':0.20}"), Amd);

            Assert.Near(500, amd.Price, 1e-3, "rival-personalities owns the AI prices while it runs, whatever it answered");
            Assert.Equal(0, env.Prices.Writes.Count);
            Assert.False(env.Core.Find("core2").viaRivalApi, "and there is nothing to give back");
        }

        // ---- the player ----

        [Test]
        public void ThePlayerAggressor_CutsItsOwnCpusInTheWarsSegments_ByThePrice_Once()
        {
            var env = new Env();
            MarketCpu mine = env.Prices.Add(1, Player, Segment.HighEnd, 450f, 100f);
            MarketCpu mineLow = env.Prices.Add(2, Player, Segment.LowEnd, 100f, 40f);
            MarketCpu rival = env.Prices.Add(10, Amd, Segment.HighEnd, 500f, 120f);
            env.Core.Start(Env.War("{'segments':['highend'],'cut':0.20,'followCut':0.10}"), Player);

            Assert.Near(360, mine.Price, 1e-3, "450 x 0.8");
            Assert.Near(100, mineLow.Price, 1e-3, "the main segment of the CPU decides: LowEnd is not at war");
            Assert.Near(450, rival.Price, 1e-3, "the AI company follows by 0.10");
            Assert.Near(450, rival.Price, 1e-3);
            PriceWarDto dto = env.Core.Find("core2");
            Assert.True(dto.restore.All(r => r.cpuSaveId != 1), "the player's price is never restored");
        }

        [Test]
        public void ThePlayerWhoJoins_CutsByTheFollowCut_AndNobodyElse()
        {
            var env = new Env();
            MarketCpu mine = env.Prices.Add(1, Player, Segment.MidRange, 250f, 80f);
            MarketCpu rival = env.Prices.Add(10, Amd, Segment.MidRange, 250f, 80f);

            int cut = env.Core.Join(Env.War("{'segments':['midrange'],'cut':0.20,'followCut':0.10}"), Player);

            Assert.Equal(1, cut);
            Assert.Near(225, mine.Price, 1e-3);
            Assert.Near(250, rival.Price, 1e-3, "joining does not start the war for the AI");
            Assert.NotNull(env.Core.Find("core2"), "but the war is there");
            Assert.Equal(0, env.Core.Join(Env.War("{'segments':['midrange']}"), Amd), "only the player joins");
        }

        [Test]
        public void ThePlayerWhoHolds_ChangesNoPrice()
        {
            var env = new Env();
            MarketCpu mine = env.Prices.Add(1, Player, Segment.LowEnd, 100f, 40f);
            env.Core.Hold(Env.War());
            Assert.Near(100, mine.Price, 1e-3);
            Assert.NotNull(env.Core.Find("core2"));
            Assert.Equal(0, env.Prices.Writes.Count);
        }

        [Test]
        public void Marketing_SpendsAShareOfTheRevenue_AndRaisesPopularityGainForAHundredAndEightyDays()
        {
            var env = new Env();
            Assert.True(env.Core.Marketing(Env.War("{'marketingCostPct':0.01,'marketingPopularityGain':1.10,'marketingDays':180}"), Player));

            Assert.Near(0.01 * 10e9, env.Economy.Spent(Player, "Development"), 1e3, "1 % of the revenue from the development budget");
            FakeModifiers.Entry bonus = env.Mods.Entries.Single();
            Assert.Equal("popularity.gain", bonus.Channel);
            Assert.Equal(Player, bonus.Company);
            Assert.Equal(1.10f, bonus.Multiplier);
            Assert.Equal(180, bonus.Days);
            Assert.False(env.Core.Marketing(Env.War(), "nobody"), "a company that is not there cannot");
        }

        [Test]
        public void APlayerWhoDeclinesToStartTheWar_HandsItToTheBiggestAiSellerInTheSegments()
        {
            var env = new Env();
            env.Ranking = new[] { Player, Intel, Amd };
            MarketCpu mine = env.Prices.Add(1, Player, Segment.HighEnd, 450f, 100f);
            MarketCpu amd = env.Prices.Add(10, Amd, Segment.HighEnd, 500f, 120f);
            MarketCpu intel = env.Prices.Add(11, Intel, Segment.HighEnd, 600f, 120f);

            string next = env.Core.Decline(Env.War("{'segments':['highend'],'cut':0.20,'followCut':0.10}"), Player);

            Assert.Equal(Intel, next, "the first of the ranking that sells there, and is not the player");
            Assert.Near(480, intel.Price, 1e-3, "the new aggressor cuts by 0.20");
            Assert.Near(450, amd.Price, 1e-3, "the other AI company follows by 0.10");
            Assert.Near(450, mine.Price, 1e-3, "the player is untouched");
        }

        [Test]
        public void AnAiAggressorThatDeclines_StartsNothing()
        {
            var env = new Env();
            env.Prices.Add(10, Amd, Segment.HighEnd, 500f, 120f);
            Assert.Null(env.Core.Decline(Env.War("{'segments':['highend']}"), Amd));
            Assert.Equal(0, env.Core.Wars.Count);
            Assert.Equal(0, env.Prices.Writes.Count);
        }

        [Test]
        public void ADeclineWithNoOneLeftToStartIt_IsLogged()
        {
            var env = new Env();
            Assert.Null(env.Core.Decline(Env.War("{'segments':['highend']}"), Player));
            Assert.True(env.Log.Any(l => l.Contains("no AI company is left")));
        }

        // ---- the end ----

        [Test]
        public void ASecondStartOfTheSameWar_IsIgnored()
        {
            var env = new Env();
            env.Prices.Add(10, Amd, Segment.HighEnd, 500f, 120f);
            Assert.True(env.Core.Start(Env.War("{'segments':['highend'],'cut':0.20}"), Amd));
            Assert.False(env.Core.Start(Env.War("{'segments':['highend'],'cut':0.20}"), Amd));
            Assert.Equal(1, env.Core.Wars.Count);
            Assert.False(env.Core.Start(new WarParams(), Amd), "a war needs an id");
        }

        [Test]
        public void SwitchingTheFeatureOff_GivesThePricesBack_AtOnce()
        {
            var env = new Env();
            MarketCpu amd = env.Prices.Add(10, Amd, Segment.HighEnd, 500f, 120f);
            env.Core.Start(Env.War("{'segments':['highend'],'cut':0.20}"), Amd);
            Assert.Near(400, amd.TargetPrice, 1e-3);

            env.Core.OnFeatureDisabled();
            Assert.Near(500, amd.TargetPrice, 1e-3);
            Assert.Equal(0, env.Core.Wars.Count);
            Assert.Equal(0, env.Economy.Notes(Player).Count(), "no letter about a war ended by a switch");

            var viaApi = new Env();
            viaApi.Api.Enabled = true;
            viaApi.Prices.Add(10, Amd, Segment.HighEnd, 500f, 120f);
            viaApi.Core.Start(Env.War("{'segments':['highend']}"), Amd);
            viaApi.Core.OnFeatureDisabled();
            Assert.Equal(new[] { "hist:core2" }, viaApi.Api.Restores.ToArray());
        }

        [Test]
        public void ABankruptCompany_GetsItsSavedTargetBack_AndALostCpuIsPassedOver()
        {
            var env = new Env();
            MarketCpu amd = env.Prices.Add(10, Amd, Segment.HighEnd, 500f, 120f);
            MarketCpu intel = env.Prices.Add(11, Intel, Segment.HighEnd, 600f, 120f);
            env.Core.Start(Env.War("{'segments':['highend'],'cut':0.20,'followCut':0.10}"), Amd);
            Assert.Equal(2, env.Core.Find("core2").restore.Count);

            env.Economy.Dead.Add(Amd);
            env.Core.OnCompaniesChanged();
            Assert.Near(500, amd.TargetPrice, 1e-3, "the bankrupt company's target is put back");
            Assert.Equal(1, env.Core.Find("core2").restore.Count);

            env.Prices.Cpus.Remove(intel);
            env.Core.OnFeatureDisabled();
            Assert.Equal(0, env.Core.Wars.Count, "a CPU that is gone is passed over and the war still ends");
        }

        [Test]
        public void AWarThatEnds_TellsThePlayer()
        {
            var env = new Env();
            env.Core.Hold(Env.War("{'durationDays':30}"));
            env.Economy.Today += 31;
            env.Core.OnDay();
            Assert.Equal(1, env.Economy.Notes(Player).Count(n => n.Origin == PriceWarCore.NoteEnded));
        }

        [Test]
        public void AWarThatNeverStarted_NeverCostsAnything_OnTheDailyPass()
        {
            var env = new Env();
            env.Core.OnDay();
            Assert.Equal(0, env.Prices.Writes.Count + env.Api.Calls.Count + env.Api.Restores.Count);
        }

        // ---- the numbers ----

        [Test]
        public void TheNumbers_TakeTheEffectFirst_ThenThePriceWarDefaults_ThenTheCode()
        {
            JsonObject defaults = HistFixture.Obj("{'floorMargin':0.05,'marketingCostPct':0.01,'marketingPopularityGain':1.10,'marketingDays':180}");
            WarParams p = WarParams.Read(HistFixture.Obj("{'warId':'w','segments':['lowend','midrange'],'cut':0.15,'followCut':0.08,'durationDays':270,'marketingCostPct':0.02}"), defaults, null);

            Assert.Equal("w", p.WarId);
            Assert.Equal(new[] { Segment.LowEnd, Segment.MidRange }, p.Segments.ToArray());
            Assert.Equal(270, p.DurationDays);
            Assert.Equal(0.02f, p.MarketingCostPct, "the effect's own number");
            Assert.Equal(0.05f, p.FloorMargin, "the default");
            Assert.Equal(1.10f, p.MarketingGain);

            WarParams code = WarParams.Read(new JsonObject(), null, null);
            Assert.Equal(0.15f, code.Cut);
            Assert.Equal(180, code.DurationDays);
            Assert.Equal(WarActions.Start, WarParams.ActionOf(null));
            Assert.Equal(WarActions.Join, WarParams.ActionOf(HistFixture.Obj("{'action':'join'}")));

            foreach (string bad in new[] { "{'cut':0.9}", "{'segments':['moon']}", "{'segments':[]}", "{'durationDays':1}", "{'action':'surrender'}", "{'floorMargin':-1}", "{'marketingPopularityGain':3}" })
            {
                var lines = new List<string>();
                WarParams.Read(HistFixture.Obj(bad), null, lines);
                Assert.True(lines.Count > 0, bad + " must be a problem");
            }
        }

        [Test]
        public void TheWarsOfSeveralEntries_AreToldApartByTheExtraArgumentsOfTheFire()
        {
            WarParams p = WarParams.Read(new JsonObject(), null, null);
            var problems = new List<string>();
            p.ApplyArgs(new[] { "hist.1998.sub1000", "Intel", "", "", "sub1000", "lowend|midrange", "0.15", "0.08", "270" }, problems);

            Assert.Equal(0, problems.Count);
            Assert.Equal("sub1000", p.WarId);
            Assert.Equal(new[] { Segment.LowEnd, Segment.MidRange }, p.Segments.ToArray());
            Assert.Equal(0.15f, p.Cut);
            Assert.Equal(0.08f, p.FollowCut);
            Assert.Equal(270, p.DurationDays);

            WarParams keep = WarParams.Read(HistFixture.Obj("{'warId':'mine','segments':['highend'],'cut':0.20}"), null, null);
            keep.ApplyArgs(new[] { "e", "n", "", "", "other" }, problems);
            Assert.Equal("mine", keep.WarId, "the effect's own id wins");
            Assert.Equal(0.20f, keep.Cut, "arguments that are missing change nothing");

            WarParams bad = WarParams.Read(new JsonObject(), null, null);
            var lines = new List<string>();
            bad.ApplyArgs(new[] { "e", "n", "", "", "w", "moon", "lots", "-1", "x" }, lines);
            Assert.Equal(4, lines.Count, string.Join(" | ", lines));
            Assert.Equal(0.15f, bad.Cut, "a bad number is ignored");
            Assert.Equal(new[] { Segment.LowEnd }, bad.Segments.ToArray());
        }

        [Test]
        public void TheWarOfAnEntry_CanBeGivenInTwoExtraArguments_LeavingRoomForTheNotesOfTheLetter()
        {
            // The calendar passes [warId, "segments;cut;followCut;durationDays"]: the next two positions of the arguments hold the source line and the reference (HistNotes).
            WarParams p = WarParams.Read(new JsonObject(), null, null);
            var problems = new List<string>();
            p.ApplyArgs(new[] { "hist.1998.sub1000", "Intel", "", "", "sub1000", "lowend|midrange;0.15;0.08;270" }, problems);

            Assert.Equal(0, problems.Count, string.Join(" | ", problems));
            Assert.Equal("sub1000", p.WarId);
            Assert.Equal(new[] { Segment.LowEnd, Segment.MidRange }, p.Segments.ToArray());
            Assert.Equal(0.15f, p.Cut);
            Assert.Equal(0.08f, p.FollowCut);
            Assert.Equal(270, p.DurationDays);

            // A part may be left out; the notes in the next positions are not read as numbers.
            WarParams partial = WarParams.Read(HistFixture.Obj("{'cut':0.2,'followCut':0.1,'durationDays':100}"), null, null);
            partial.ApplyArgs(new[] { "e", "n", "", "", "w", "highend;;;", "\nИсточник: wikipedia.org", "Справка" }, problems);
            Assert.Equal(new[] { Segment.HighEnd }, partial.Segments.ToArray());
            Assert.Equal(0.2f, partial.Cut, "empty parts keep the value of the event");
            Assert.Equal(100, partial.DurationDays);
            Assert.Equal(0, problems.Count, string.Join(" | ", problems));

            WarParams bad = WarParams.Read(new JsonObject(), null, null);
            var lines = new List<string>();
            bad.ApplyArgs(new[] { "e", "n", "", "", "w", "moon;lots;-1;x" }, lines);
            Assert.Equal(4, lines.Count, string.Join(" | ", lines));
        }

        [Test]
        public void TheWarsOfTheShippedCalendar_AreGivenInThePackedForm_AndReadBackAsDesigned()
        {
            string path = System.IO.Path.Combine(PTMod.Tests.Architecture.SourceScan.SrcDirOrFail(), "Content", "historical-events", "calendar.json");
            var records = HistFixture.ObjFromFile(PTMod.Tests.Architecture.SourceScan.ReadText(path)).GetArray("records");
            var expected = new Dictionary<string, KeyValuePair<Segment[], float[]>>
            {
                { "dram1985", new KeyValuePair<Segment[], float[]>(new[] { Segment.LowEnd }, new[] { 0.15f, 0.08f, 180f }) },
                { "sub1000", new KeyValuePair<Segment[], float[]>(new[] { Segment.LowEnd, Segment.MidRange }, new[] { 0.15f, 0.08f, 270f }) },
                { "core2006", new KeyValuePair<Segment[], float[]>(new[] { Segment.HighEnd, Segment.MidRange }, new[] { 0.20f, 0.10f, 180f }) },
                { "corewar2017", new KeyValuePair<Segment[], float[]>(new[] { Segment.HighEnd }, new[] { 0.10f, 0.05f, 180f }) },
            };

            int fires = 0;
            foreach (JsonObject entry in records.OfType<JsonObject>())
            {
                foreach (JsonObject stage in entry.GetArray("stages").OfType<JsonObject>())
                {
                    foreach (JsonObject fire in stage.GetArray("fires").OfType<JsonObject>())
                    {
                        if (!fire.GetString("eventId").StartsWith("hist.decide.pricewar.", System.StringComparison.Ordinal)) continue;

                        string[] extra = fire.GetArray("extraArgs").Select(n => ((PTMod.Core.Logic.Json.JsonValue)n).StringValue).ToArray();
                        Assert.Equal(2, extra.Length, entry.GetString("id") + ": the war is two extra arguments");

                        WarParams p = WarParams.Read(new JsonObject(), null, null);
                        var problems = new List<string>();
                        p.ApplyArgs(new[] { entry.GetString("id"), "Intel", "", "", extra[0], extra[1] }, problems);

                        KeyValuePair<Segment[], float[]> want = expected[extra[0]];
                        Assert.Equal(0, problems.Count, string.Join(" | ", problems));
                        Assert.Equal(want.Key, p.Segments.ToArray(), extra[0]);
                        Assert.Near(want.Value[0], p.Cut, 1e-6, extra[0] + " cut");
                        Assert.Near(want.Value[1], p.FollowCut, 1e-6, extra[0] + " followCut");
                        Assert.Equal((int)want.Value[2], p.DurationDays, extra[0] + " days");
                        fires++;
                    }
                }
            }
            Assert.Equal(8, fires, "an aggressor and a follower for each of the four wars");
        }

        [Test]
        public void TheFallbackArithmetic_IsTheDesignFormula()
        {
            Assert.Near(126, PriceWarFallback.Floor(120f, 0.05f), 1e-3);
            Assert.Near(400, PriceWarFallback.CutValue(500f, 0.2f, 126f), 1e-3);
            Assert.Near(126, PriceWarFallback.CutValue(150f, 0.5f, 126f), 1e-3, "the floor holds");
            Assert.Near(100, PriceWarFallback.CutValue(100f, 0.5f, 126f), 1e-3, "below the floor already: unchanged");
            Assert.Near(-1, PriceWarFallback.CutValue(-1f, 0.5f, 126f), 1e-3, "no price: no change");

            float price, target;
            PriceWarFallback.Cut(500f, 500f, 120f, 0.2f, 0.05f, out price, out target);
            Assert.Near(400, price, 1e-3);
            Assert.Near(400, target, 1e-3);

            PriceWarFallback.Cut(500f, -1f, 120f, 0.2f, 0.05f, out price, out target);
            Assert.Near(400, price, 1e-3);
            Assert.Near(-1, target, 1e-3, "a target that was never set stays unset");
        }

        private static void CollectionAssert(IEnumerable<string> calls, string expected, string message)
        {
            Assert.True(calls.Contains(expected), message + ": expected " + expected + " among [" + string.Join(", ", calls) + "]");
        }
    }
}
