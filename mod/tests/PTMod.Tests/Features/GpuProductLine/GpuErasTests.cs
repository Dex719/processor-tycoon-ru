using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The eras and the segments of the market (TSK-030, TSK-031; FR-11, F33, F34) on the shipped content of stage 2 and the fake game: which segments are open in a year
    /// (AC-8.1), what a segment asks for (AC-11.1...AC-11.3), the opening and the closing by the calendar - by the DIFFERENCE OF DATES, so a feature that was off catches up -
    /// the starting popularity of the cards in a segment that opens (0.05), the closed segment that sells nothing, and the switch of a save of stage 1 to the market of the eras.
    /// </summary>
    [Covers(typeof(GpuEras))]
    public class GpuErasTests
    {
        private static GpuWorld Eras(DateTime today, GpuContentDb content = null, uint seed = 42)
        {
            var world = new GpuWorld(today, seed, content ?? GpuWorld.ShippedEras());
            world.Game.AddTimelineNodes();
            world.Game.FrontierKHz = 250000f;
            return world;
        }

        private static string[] Ids(IEnumerable<GpuSegmentDto> segments)
        {
            return segments.Select(s => s.id).ToArray();
        }

        /// <summary>A card of the player on the market, made by hand: its features and video memory are what the tests look at.</summary>
        private static GpuProductSave Card(GpuWorld world, string id, string[] features, float vramMB, string[] segments, float[] pops, float price = 150f)
        {
            var card = new GpuProductSave
            {
                id = id, designId = string.Empty, owner = GpuState.OwnerPlayer, vendorId = string.Empty, companyKey = FakeGpuGame.Player, name = id,
                status = GpuState.StatusSelling, price = price, sell = true, stock = 1000000,
                stats = new GpuStatsSave { effectivePerf = 1000d, powerW = 50f, features = new List<string>(features) },
                node = "350nm", die = 100f, busWidth = 128, vramMB = vramMB,
                released = new GpuDate { y = world.Game.Date.Year, m = world.Game.Date.Month, d = world.Game.Date.Day },
                segIds = new List<string>(segments), pop = new List<float>(pops),
            };
            world.State.Data.products.Add(card);
            return card;
        }

        private static float FactorOf(GpuWorld world, string productId, string segmentId)
        {
            DateTime today = world.Game.Date;
            List<SegmentRuntime> segs = world.Market.BuildSegments(today);
            List<MarketItem> items = world.Market.BuildItems(segs, today);
            SegmentRuntime seg = segs.Find(s => s.Id == segmentId);
            Assert.NotNull(seg, segmentId + " is open");
            MarketItem item = items.Find(i => i.ProductId == productId);
            Assert.NotNull(item, productId + " is on the market");
            return item.SegmentFactor[seg.Index];
        }

        // ---- the calendar of the segments (FR-11 item 1, AC-8.1) ----

        [Test]
        public void Ac81_In2004_FourSegmentsAreOpen_WithTheShaderAndTheMemoryOfTheYear()
        {
            GpuWorld world = Eras(new DateTime(2004, 1, 1));
            world.Market.SeedForYear(2004);

            IReadOnlyList<GpuSegmentView> open = world.Market.OpenSegments;
            Assert.Equal(new[] { "office2d", "gaming.high", "gaming.mid", "gaming.low" }, open.Select(s => s.Id).ToArray(), "no compute before 2007, no AI before 2016");
            foreach (string id in new[] { "gaming.high", "gaming.mid", "gaming.low" })
            {
                GpuSegmentView view = open.First(s => s.Id == id);
                Assert.True(Array.IndexOf(view.RequiredFeatures, "shader") >= 0, id + " asks for the shader in 2004");
            }
            Assert.Equal(new[] { "2d" }, open.First(s => s.Id == "office2d").RequiredFeatures, "the office asks for 2D only");
            Assert.Near(256, open.First(s => s.Id == "gaming.high").VramRequirementMB, 0.5, "AC-8.1: the enthusiasts ask for 256 MB");
            Assert.Near(128, open.First(s => s.Id == "gaming.mid").VramRequirementMB, 0.5, "the mainstream for 128 MB");
            Assert.Near(64, open.First(s => s.Id == "gaming.low").VramRequirementMB, 0.5, "the budget segment for 64 MB");
        }

        [Test]
        public void TheSegments_OpenAndCloseOnThe1stOfJanuary()
        {
            GpuWorld world = Eras(new DateTime(2004, 1, 1));
            Assert.Equal(new[] { "office2d" }, Ids(world.Eras.OpenAt(new DateTime(1995, 12, 31))), "1995");
            Assert.Equal(new[] { "office2d", "gaming.high", "gaming.mid" }, Ids(world.Eras.OpenAt(new DateTime(1996, 1, 1))), "the 3D segments open on 1 January 1996");
            Assert.Equal(new[] { "office2d", "gaming.high", "gaming.mid", "gaming.low" }, Ids(world.Eras.OpenAt(new DateTime(1998, 1, 1))), "the budget segment in 1998");
            Assert.Equal(new[] { "office2d", "gaming.high", "gaming.mid", "gaming.low" }, Ids(world.Eras.OpenAt(new DateTime(2006, 12, 31))));
            Assert.Equal(new[] { "office2d", "gaming.high", "gaming.mid", "gaming.low", "compute" }, Ids(world.Eras.OpenAt(new DateTime(2007, 1, 1))), "compute in 2007");
            Assert.Equal(new[] { "office2d", "gaming.high", "gaming.mid", "gaming.low", "compute" }, Ids(world.Eras.OpenAt(new DateTime(2009, 12, 31))), "the office is open in 2009");
            Assert.Equal(new[] { "gaming.high", "gaming.mid", "gaming.low", "compute" }, Ids(world.Eras.OpenAt(new DateTime(2010, 1, 1))), "and closed on 1 January 2010");
            Assert.Equal(new[] { "gaming.high", "gaming.mid", "gaming.low", "compute", "ai" }, Ids(world.Eras.OpenAt(new DateTime(2016, 1, 1))), "AI in 2016");
            Assert.Equal(new string[0], Ids(world.Eras.OpenAt(new DateTime(1980, 12, 31))), "nothing before 1981");

            Assert.False(GpuEras.IsOpen(null, new DateTime(2000, 1, 1)));
            var forever = new GpuSegmentDto { opensYear = 2000, closesYear = 0 };
            Assert.True(GpuEras.IsOpen(forever, new DateTime(2999, 1, 1)), "closesYear 0: never");
            Assert.False(GpuEras.IsOpen(forever, new DateTime(1999, 12, 31)));
        }

        [Test]
        public void TheMarketService_AsksTheSameRule()
        {
            GpuWorld world = Eras(new DateTime(2009, 12, 31));
            Assert.Equal(Ids(world.Eras.OpenAt(new DateTime(2009, 12, 31))), Ids(world.Market.OpenSegmentsAt(new DateTime(2009, 12, 31))));
            Assert.Equal(Ids(world.Eras.OpenAt(new DateTime(2010, 1, 1))), Ids(world.Market.OpenSegmentsAt(new DateTime(2010, 1, 1))));
        }

        [Test]
        public void TheEra_IsTheLatestOneThatBegan_AndTheNextOneIsKnown()
        {
            GpuWorld world = Eras(new DateTime(2004, 1, 1));
            Assert.Null(world.Eras.CurrentEra(new DateTime(1980, 1, 1)));
            Assert.Equal("era.2d", world.Eras.CurrentEra(new DateTime(1990, 6, 1)).id);
            Assert.Equal("era.3d", world.Eras.CurrentEra(new DateTime(1996, 1, 1)).id);
            Assert.Equal("era.shader", world.Eras.CurrentEra(new DateTime(2006, 12, 31)).id);
            Assert.Equal("era.rt", world.Eras.CurrentEra(new DateTime(2030, 1, 1)).id);
            Assert.Equal(1996, world.Eras.NextEraYear(new DateTime(1990, 6, 1)));
            Assert.Equal(2003, world.Eras.NextEraYear(new DateTime(1996, 1, 1)));
            Assert.Equal(0, world.Eras.NextEraYear(new DateTime(2020, 1, 1)), "no era after the last");

            GpuWorld single = new GpuWorld(new DateTime(1996, 1, 1));
            Assert.False(single.Eras.IsErasMode);
            Assert.Null(single.Eras.CurrentEra(new DateTime(1996, 1, 1)), "the eras exist in the mode of the eras only");
            Assert.Equal(0, single.Eras.NextEraYear(new DateTime(1996, 1, 1)));
        }

        // ---- the requirements of a segment (FR-11 items 2-3, AC-11.1...AC-11.3; F33, F34) ----

        [Test]
        public void Ac111_In1996_ACardWithoutThe3D_GetsTheFactor035_InTheGamingSegments()
        {
            GpuWorld world = Eras(new DateTime(1996, 1, 1));
            world.Market.SeedForYear(1996);
            Card(world, "g-gui", new[] { "2d", "gui" }, 8f, new[] { "office2d", "gaming.high", "gaming.mid" }, new[] { 0.5f, 0.5f, 0.5f });
            Card(world, "g-3d", new[] { "2d", "gui", "3d" }, 8f, new[] { "office2d", "gaming.high", "gaming.mid" }, new[] { 0.5f, 0.5f, 0.5f });

            Assert.Near(0.35, FactorOf(world, "g-gui", "gaming.high"), 1e-5, "the 3D is missing: x 0.35");
            Assert.Near(0.35, FactorOf(world, "g-gui", "gaming.mid"), 1e-5);
            Assert.Near(1.0, FactorOf(world, "g-gui", "office2d"), 1e-5, "the office asks for the 2D only");
            Assert.Near(1.0, FactorOf(world, "g-3d", "gaming.high"), 1e-5, "with the 3D nothing is lost");

            // two missing features: the factor is taken twice
            world.Game.Date = new DateTime(2003, 1, 1);
            Card(world, "g-old", new[] { "2d", "gui", "3d" }, 512f, new[] { "gaming.high" }, new[] { 0.5f });
            Assert.Near(0.35 * 0.35, FactorOf(world, "g-old", "gaming.high"), 1e-5, "tnl and shader are missing in 2003");
        }

        [Test]
        public void Ac112_In2008_ACardWithoutCompute_HasNoRatingInTheComputeSegment()
        {
            GpuWorld world = Eras(new DateTime(2008, 1, 1));
            world.Market.SeedForYear(2008);
            string[] shader = { "2d", "gui", "3d", "tnl", "shader", "unified" };
            Card(world, "g-shader", shader, 4096f, new[] { "gaming.high", "compute" }, new[] { 0.5f, 0.5f });
            Card(world, "g-gpgpu", shader.Concat(new[] { "compute" }).ToArray(), 4096f, new[] { "gaming.high", "compute" }, new[] { 0.5f, 0.5f });

            Assert.Equal(0f, FactorOf(world, "g-shader", "compute"), "AC-11.2: no compute, no sales (missingFeatureFactor 0)");
            Assert.True(FactorOf(world, "g-gpgpu", "compute") > 0f, "with the compute units the card is rated");
            Assert.Near(1.0, FactorOf(world, "g-shader", "gaming.high"), 1e-5, "and the gaming segments are not affected");
        }

        [Test]
        public void Ac113_In2010_TheMainstreamAsks1024MB_ACardWith512Gets05()
        {
            GpuWorld world = Eras(new DateTime(2010, 1, 1));
            world.Market.SeedForYear(2010);
            string[] features = { "2d", "gui", "3d", "tnl", "shader", "unified" };
            Card(world, "g-512", features, 512f, new[] { "gaming.mid" }, new[] { 0.5f });
            Card(world, "g-4096", features, 4096f, new[] { "gaming.mid" }, new[] { 0.5f });
            Card(world, "g-100", features, 100f, new[] { "gaming.mid" }, new[] { 0.5f });

            Assert.Near(1024, world.Eras.VramRequirement(world.Content.Segment("gaming.mid"), 2010f), 0.5, "the mainstream in 2010");
            Assert.Near(0.5, FactorOf(world, "g-512", "gaming.mid"), 1e-4, "AC-11.3: 512 / 1024");
            Assert.Near(1.0, FactorOf(world, "g-4096", "gaming.mid"), 1e-5, "more memory than asked does not help");
            Assert.Near(0.25, FactorOf(world, "g-100", "gaming.mid"), 1e-5, "never below a quarter");
        }

        [Test]
        public void TheRequirements_AreTheOnesOfTheContent_AndLogLinearBetweenTheYears()
        {
            GpuWorld world = Eras(new DateTime(2004, 1, 1));
            GpuSegmentDto high = world.Content.Segment("gaming.high");
            Assert.Equal(new[] { "3d" }, world.Eras.RequiredFeatures(high, 1996));
            Assert.Equal(new[] { "3d", "tnl" }, world.Eras.RequiredFeatures(high, 2001));
            Assert.Equal(new[] { "3d", "tnl", "shader", "unified", "rt" }, world.Eras.RequiredFeatures(high, 2022));
            Assert.Equal(new string[0], world.Eras.RequiredFeatures(high, 1990), "nothing is asked before the first year");
            Assert.Equal(new string[0], world.Eras.RequiredFeatures(null, 2000));

            // 1996: 4 MB, 2000: 64 MB: halfway, 16 MB (the geometric mean)
            Assert.Near(16, world.Eras.VramRequirement(high, 1998f), 0.01);
            Assert.Near(256, world.Eras.VramRequirement(high, 2004f), 0.01, "a control year");
            Assert.Near(0, world.Eras.VramRequirement(null, 2004f), 1e-6);

            // the compute segment and the AI segment kill the cards without the feature, and ask for much memory
            Assert.Equal(0f, world.Content.Segment("compute").missingFeatureFactor);
            Assert.Equal(0f, world.Content.Segment("ai").missingFeatureFactor);
            Assert.Equal(new[] { "tensor" }, world.Eras.RequiredFeatures(world.Content.Segment("ai"), 2020));
            Assert.Equal(new[] { "compute" }, world.Eras.RequiredFeatures(world.Content.Segment("compute"), 2007));
        }

        // ---- the month: what the calendar did (FR-11 items 4-5) ----

        private sealed class Heard
        {
            public readonly List<string> Opened = new List<string>();
            public readonly List<string> Closed = new List<string>();
            public readonly List<string> Eras = new List<string>();

            public Heard(GpuEras eras)
            {
                eras.SegmentOpened += s => Opened.Add(s.id);
                eras.SegmentClosed += s => Closed.Add(s.id);
                eras.EraStarted += e => Eras.Add(e.id);
            }
        }

        [Test]
        public void ANewSegment_GivesTheCardsOnSaleTheStartingPopularity_AndIsAnnouncedOnce()
        {
            GpuWorld world = Eras(new DateTime(1995, 12, 1));
            world.Market.SeedForYear(1995);
            GpuProductSave card = Card(world, "g-1", new[] { "2d", "gui", "3d" }, 8f, new[] { "office2d" }, new[] { 0.4f });
            GpuProductSave onShelf = Card(world, "g-2", new[] { "2d", "gui", "3d" }, 8f, new[] { "office2d" }, new[] { 0.9f });
            onShelf.status = GpuState.StatusReady;   // not released yet: no popularity anywhere
            var heard = new Heard(world.Eras);

            world.Game.Date = new DateTime(1996, 1, 1);
            world.Eras.MonthTick();

            Assert.Equal(new[] { "gaming.high", "gaming.mid" }, heard.Opened.ToArray(), "the 3D segments open in January 1996");
            Assert.Equal(0, heard.Closed.Count);
            Assert.Equal(new[] { "era.3d" }, heard.Eras.ToArray(), "and the era of the 3D accelerators begins");
            Assert.Equal(new[] { "office2d", "gaming.high", "gaming.mid" }, card.segIds.ToArray());
            Assert.Equal(new[] { 0.4f, 0.05f, 0.05f }, card.pop.ToArray(), "FR-11 item 4: 0.05 in the new segments, the old popularity stays");
            Assert.Equal(new[] { "office2d" }, onShelf.segIds.ToArray(), "a card that is not on sale is not touched");

            // the same month again, and the next one: nothing is announced twice
            heard.Opened.Clear();
            heard.Eras.Clear();
            world.Eras.MonthTick();
            world.Game.Date = new DateTime(1996, 2, 1);
            world.Eras.MonthTick();
            Assert.Equal(0, heard.Opened.Count + heard.Eras.Count + heard.Closed.Count, "announced once");
            Assert.Equal(3, card.segIds.Count);
        }

        [Test]
        public void ACardThatAlreadyHasTheSegment_KeepsItsPopularity()
        {
            GpuWorld world = Eras(new DateTime(1995, 12, 1));
            world.Market.SeedForYear(1995);
            GpuProductSave card = Card(world, "g-1", new[] { "2d", "gui", "3d" }, 8f, new[] { "office2d", "gaming.mid" }, new[] { 0.4f, 0.7f });
            world.Game.Date = new DateTime(1996, 1, 1);
            world.Eras.MonthTick();
            Assert.Equal(new[] { "office2d", "gaming.mid", "gaming.high" }, card.segIds.ToArray());
            Assert.Equal(new[] { 0.4f, 0.7f, 0.05f }, card.pop.ToArray());
        }

        [Test]
        public void AClosedSegment_IsAnnounced_SellsNothing_AndKeepsItsCards()
        {
            GpuWorld world = Eras(new DateTime(2009, 12, 1));
            world.Market.SeedForYear(2009);
            GpuProductSave card = Card(world, "g-1", new[] { "2d", "gui", "3d", "tnl", "shader", "unified" }, 1024f, new[] { "office2d", "gaming.mid" }, new[] { 0.6f, 0.3f }, 40f);
            var heard = new Heard(world.Eras);

            world.Game.Date = new DateTime(2010, 1, 1);
            world.Eras.MonthTick();

            Assert.Equal(new[] { "office2d" }, heard.Closed.ToArray(), "FR-11 item 5");
            Assert.Equal(0, heard.Opened.Count);
            Assert.False(world.Market.OpenSegmentsAt(world.Game.Date).Exists(s => s.id == "office2d"), "the closed segment takes no part in the day");
            Assert.False(world.Market.BuildSegments(world.Game.Date).Exists(s => s.Id == "office2d"));
            Assert.True(world.State.Data.products.Contains(card), "the card is not deleted");
            Assert.Equal(new[] { "office2d", "gaming.mid" }, card.segIds.ToArray(), "and neither is its popularity in the segment that closed");

            world.Market.SellDay();
            Assert.Equal(new[] { "gaming.high", "gaming.mid", "gaming.low", "compute" }, world.State.Data.market.openSegIds.ToArray(), "the day was sold without the office");
        }

        [Test]
        public void AfterATimeTheFeatureWasOff_TheMonthCatchesUpWithTheCalendar()
        {
            GpuWorld world = Eras(new DateTime(1995, 1, 1));
            world.Market.SeedForYear(1995);
            var heard = new Heard(world.Eras);

            world.Game.Date = new DateTime(2005, 1, 1);   // ten years, no month ticks
            world.Eras.MonthTick();

            Assert.Equal(new[] { "gaming.high", "gaming.mid", "gaming.low" }, heard.Opened.ToArray(), "by the difference of dates: everything that opened since");
            Assert.Equal(0, heard.Closed.Count, "the office is still open");
            Assert.Equal(new[] { "era.3d", "era.shader" }, heard.Eras.ToArray(), "in the order of the years");

            heard.Opened.Clear();
            heard.Eras.Clear();
            world.Game.Date = new DateTime(2012, 1, 1);
            world.Eras.MonthTick();
            Assert.Equal(new[] { "compute" }, heard.Opened.ToArray());
            Assert.Equal(new[] { "office2d" }, heard.Closed.ToArray());
            Assert.Equal(new[] { "era.compute" }, heard.Eras.ToArray());
        }

        [Test]
        public void ASegmentThatOpenedAndClosedWhileNobodyLooked_IsNotAnnounced()
        {
            TextContentSource source = GpuFiles.Shipped().Edit("segments",
                "\"id\": \"gaming.low\", \"mode\": \"eras\", \"nameKey\": \"gpu.seg.gaming.low\", \"opensYear\": 1998, \"closesYear\": 0",
                "\"id\": \"gaming.low\", \"mode\": \"eras\", \"nameKey\": \"gpu.seg.gaming.low\", \"opensYear\": 2000, \"closesYear\": 2002");
            GpuWorld world = Eras(new DateTime(1999, 1, 1), GpuFiles.Load(source));
            world.Market.SeedForYear(1999);
            var heard = new Heard(world.Eras);

            world.Game.Date = new DateTime(2004, 1, 1);
            world.Eras.MonthTick();
            Assert.False(heard.Opened.Contains("gaming.low"), "it is closed now");
            Assert.False(heard.Closed.Contains("gaming.low"), "and it was not open when the last look was taken");
        }

        [Test]
        public void TheFirstLook_OnlySetsTheDate_AndTheSeedDoesNotAnnounceAnything()
        {
            GpuWorld world = Eras(new DateTime(2004, 6, 1));
            var heard = new Heard(world.Eras);
            world.Eras.MonthTick();
            Assert.Equal(0, heard.Opened.Count + heard.Closed.Count + heard.Eras.Count, "an unseeded market is not looked at");
            Assert.Null(world.State.Data.market.erasCheckedOn);

            world.Market.SeedForYear(2004);
            Assert.NotNull(world.State.Data.market.erasCheckedOn, "the seed notes the date");
            Assert.Equal(2004, world.State.Data.market.erasCheckedOn.y);
            Assert.Equal(6, world.State.Data.market.erasCheckedOn.m);
            Assert.Equal(0, heard.Opened.Count + heard.Closed.Count + heard.Eras.Count, "the history before the game is not announced");

            // a section that has no date (a save of stage 1): the first month only sets it
            world.State.Data.market.erasCheckedOn = null;
            world.Game.Date = new DateTime(2004, 7, 1);
            world.Eras.MonthTick();
            Assert.Equal(7, world.State.Data.market.erasCheckedOn.m);
            Assert.Equal(0, heard.Opened.Count + heard.Closed.Count + heard.Eras.Count);
        }

        [Test]
        public void InTheModeOfStageOne_TheCalendarIsQuiet()
        {
            var world = new GpuWorld(new DateTime(1995, 12, 1));
            world.Game.AddTimelineNodes();
            world.Game.FrontierKHz = 250000f;
            world.Market.SeedForYear(1995);
            var heard = new Heard(world.Eras);
            world.Game.Date = new DateTime(2012, 1, 1);
            world.Eras.MonthTick();
            Assert.Equal(0, heard.Opened.Count + heard.Closed.Count + heard.Eras.Count, "one segment that never closes, and no eras outside the mode of the eras");
        }

        // ---- the mode: a save of stage 1 in the market of the eras (FR-11 item 6) ----

        private static GpuWorld StageOneSave(DateTime today)
        {
            GpuWorld world = Eras(today);
            GpuSaveSection data = world.State.Data;
            data.marketModeApplied = "single";
            data.market.seeded = true;
            data.market.openSegIds = new List<string> { "consumer" };
            data.market.potentialSales = new List<float> { 1000f };
            data.market.lastBackgroundRefresh = new GpuDate { y = today.Year, m = today.Month, d = 1 };
            data.products.Add(new GpuProductSave
            {
                id = "bg-consumer", owner = GpuState.OwnerBackground, status = GpuState.StatusSelling, price = 249f, sell = true, stats = new GpuStatsSave { effectivePerf = 10d },
                segIds = new List<string> { "consumer" }, pop = new List<float> { 0.35f },
            });
            data.products.Add(new GpuProductSave
            {
                id = "g-77", owner = GpuState.OwnerVendor, vendorId = "someone", status = GpuState.StatusSelling, price = 249f, sell = true, stock = 5, stats = new GpuStatsSave { effectivePerf = 10d },
                segIds = new List<string> { "consumer" }, pop = new List<float> { 0.5f },
            });
            return world;
        }

        [Test]
        public void ASaveOfStageOne_CarriesThePopularityOfConsumer_IntoEveryOpenSegmentOfTheEras()
        {
            GpuWorld world = StageOneSave(new DateTime(2004, 1, 1));
            GpuProductSave card = Card(world, "g-1", new[] { "2d", "gui", "3d", "tnl", "shader" }, 256f, new[] { "consumer" }, new[] { 0.62f });
            GpuProductSave both = Card(world, "g-2", new[] { "2d", "gui", "3d" }, 16f, new[] { "consumer", "gaming.mid" }, new[] { 0.3f, 0.8f });
            GpuProductSave retired = Card(world, "g-3", new[] { "2d" }, 1f, new[] { "consumer" }, new[] { 0.9f });
            retired.status = GpuState.StatusRetired;

            Assert.True(world.Eras.ApplyModeSwitchIfNeeded(world.State), "the market of the save is the single one, the content's is the eras");

            Assert.Equal(new[] { "office2d", "gaming.high", "gaming.mid", "gaming.low" }, card.segIds.ToArray(), "every open segment of the eras");
            Assert.Equal(new[] { 0.62f, 0.62f, 0.62f, 0.62f }, card.pop.ToArray(), "FR-11 item 6: with the factor 1.0");
            Assert.False(card.segIds.Contains("consumer"), "the segment of the other mode is gone from the card");
            Assert.Equal(0.8f, both.pop[both.segIds.IndexOf("gaming.mid")], "where the card had a popularity already, the larger one stays");
            Assert.Equal(0.3f, both.pop[both.segIds.IndexOf("office2d")]);
            Assert.Equal(0.3f, both.pop[both.segIds.IndexOf("gaming.high")]);
            Assert.True(world.State.Data.products.Contains(retired), "the retired card stays in the archive");

            GpuSaveSection data = world.State.Data;
            Assert.Equal("eras", data.marketModeApplied);
            Assert.False(data.market.seeded, "the market is seeded again for the year");
            Assert.Null(data.products.Find(p => p.id == "bg-consumer"), "the old background goes");
            Assert.Null(data.products.Find(p => p.id == "g-77"), "and the cards of the vendors");
            Assert.False(world.Eras.ApplyModeSwitchIfNeeded(world.State), "once");
        }

        [Test]
        public void AfterTheSwitch_TheNextSessionSeedsTheMarketOfTheEras()
        {
            GpuWorld world = StageOneSave(new DateTime(2004, 1, 1));
            world.RivalsOn = true;
            GpuProductSave card = Card(world, "g-1", new[] { "2d", "gui", "3d", "tnl", "shader" }, 256f, new[] { "consumer" }, new[] { 0.62f });
            Assert.True(world.Eras.ApplyModeSwitchIfNeeded(world.State));

            world.Market.StartSession();   // what the feature does when the game is ready

            Assert.True(world.Market.IsSeeded);
            Assert.Equal(new[] { "bg-office2d", "bg-gaming.high", "bg-gaming.mid", "bg-gaming.low" },
                world.State.Data.products.Where(p => p.owner == GpuState.OwnerBackground).Select(p => p.id).ToArray(), "a background in each segment of the eras");
            Assert.True(world.State.Data.products.Exists(p => p.owner == GpuState.OwnerVendor), "and the vendors of the year");
            Assert.Equal(new[] { "office2d", "gaming.high", "gaming.mid", "gaming.low" }, world.State.Data.market.openSegIds.ToArray());
            Assert.Equal(0.62f, card.pop[0], "the card kept its popularity");
            Assert.Equal("eras", world.State.Data.marketModeApplied);

            world.Market.SellDay();   // and the day goes on with the cards of the player in the segments of the eras
            Assert.True(world.State.Data.products.Contains(card));
        }

        [Test]
        public void AGameThatWasNeverSeeded_JustTakesTheMode()
        {
            GpuWorld world = Eras(new DateTime(1990, 1, 1));
            Assert.Equal("single", world.State.Data.marketModeApplied, "a new section starts in the mode of stage 1");
            Assert.False(world.Eras.ApplyModeSwitchIfNeeded(world.State), "nothing to carry over");
            Assert.Equal("eras", world.State.Data.marketModeApplied);

            world.Market.SeedForYear(1990);
            Assert.Equal("eras", world.State.Data.marketModeApplied, "the seed notes the mode too");
            Assert.True(world.Market.IsSeeded);
        }

        [Test]
        public void TheSwitchBack_ToTheSingleMode_CarriesThePopularityTheOtherWay()
        {
            var world = new GpuWorld(new DateTime(2004, 1, 1));   // the content of stage 1
            world.Game.AddTimelineNodes();
            GpuSaveSection data = world.State.Data;
            data.marketModeApplied = "eras";
            data.market.seeded = true;
            GpuProductSave card = new GpuProductSave
            {
                id = "g-1", owner = GpuState.OwnerPlayer, companyKey = FakeGpuGame.Player, status = GpuState.StatusSelling, price = 200f, sell = true,
                stats = new GpuStatsSave { effectivePerf = 100d }, segIds = new List<string> { "gaming.high", "gaming.mid", "office2d" }, pop = new List<float> { 0.2f, 0.7f, 0.4f },
            };
            data.products.Add(card);

            Assert.True(world.Eras.ApplyModeSwitchIfNeeded(world.State));
            Assert.Equal(new[] { "consumer" }, card.segIds.ToArray());
            Assert.Equal(new[] { 0.7f }, card.pop.ToArray(), "the best popularity of the segments that are gone");
            Assert.Equal("single", data.marketModeApplied);
        }

        // ---- the state ----

        [Test]
        public void TheDateOfTheLastLook_IsKeptInTheSave()
        {
            GpuWorld world = Eras(new DateTime(2004, 6, 1));
            world.Market.SeedForYear(2004);
            world.Game.Date = new DateTime(2004, 7, 1);
            world.Eras.MonthTick();

            string text = ModJson.Write(world.State.Write(), false);
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "JSON: " + error);
            var loaded = new GpuState(world.Content);
            loaded.Read((JsonObject)node, GpuState.SchemaVersion);
            Assert.Equal(0, loaded.Warnings.Count, string.Join("; ", loaded.Warnings.ToArray()));
            Assert.Equal(7, loaded.Data.market.erasCheckedOn.m);
            Assert.Equal(2004, loaded.Data.market.erasCheckedOn.y);

            // a hand-made date that is not a date is dropped with a word
            JsonObject market = ((JsonObject)node).GetObject("market");
            market.Set("erasCheckedOn", new JsonObject().Set("y", 2004).Set("m", 13).Set("d", 40));
            var dirty = new GpuState(world.Content);
            dirty.Read((JsonObject)node, GpuState.SchemaVersion);
            Assert.Null(dirty.Data.market.erasCheckedOn);
            Assert.True(dirty.Warnings.Any(w => w.Contains("erasCheckedOn")), "the load says so");
        }
    }
}
