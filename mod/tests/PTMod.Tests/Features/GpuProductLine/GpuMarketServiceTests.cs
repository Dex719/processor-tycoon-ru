using System;
using System.Collections.Generic;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The GPU market of stage 1 (TSK-019; FR-6, FR-9 items 2-3; F19-F23) on the shipped content and the fake game: the seed of the year - once, on a new game and on a
    /// save without the section (AC-9.1); the market without the division (FR-6 item 8); AC-6.1 (the first pass of a card twice as fast as the background), AC-6.2
    /// (three budgets: no sales, no rating), AC-6.3 (an old card loses 0.05 in 100 days), the background renewed every 12 months, the channels, the royalty of a
    /// fabless company, the books and the windows of the tab.
    /// </summary>
    [Covers(typeof(GpuMarketService), typeof(GpuSegmentView), typeof(GpuMarketEntry))]
    public class GpuMarketServiceTests
    {
        private static GpuWorld World(DateTime today, float frontierKHz = 250000f, uint seed = 42)
        {
            var world = new GpuWorld(today, seed);
            world.Game.AddTimelineNodes();
            world.Game.FrontierKHz = frontierKHz;
            return world;
        }

        private static GpuProductSave Background(GpuWorld world, string segment = "consumer")
        {
            return world.State.Data.products.Find(p => p.id == GpuMarketService.BackgroundIdPrefix + segment);
        }

        /// <summary>A card of the player on the market, made like the background's but <paramref name="perfFactor"/> times as fast.</summary>
        private static GpuProductSave PlayerCard(GpuWorld world, string id, float perfFactor, float price, int stock = 1000000, float pop = 0.05f)
        {
            GpuProductSave bg = Background(world);
            Assert.NotNull(bg, "the market is seeded");
            var stats = new GpuStatsSave
            {
                effectivePerf = bg.stats.effectivePerf * perfFactor,
                powerW = bg.stats.powerW,
                failureRate = bg.stats.failureRate,
                features = new List<string>(bg.stats.features),
            };
            var card = new GpuProductSave
            {
                id = id, designId = string.Empty, owner = GpuState.OwnerPlayer, vendorId = string.Empty, companyKey = FakeGpuGame.Player, name = id,
                status = GpuState.StatusSelling, price = price, sell = true, stock = stock, stats = stats, node = bg.node, die = bg.die, busWidth = bg.busWidth,
                vramMB = bg.vramMB, released = new GpuDate { y = world.Game.Date.Year, m = world.Game.Date.Month, d = world.Game.Date.Day },
                segIds = new List<string> { "consumer" }, pop = new List<float> { pop },
            };
            world.State.Data.products.Add(card);
            return card;
        }

        [Test]
        public void TheSeed_PutsTheBackgroundOfTheYearOnTheMarket()
        {
            GpuWorld world = World(new DateTime(1997, 6, 15));
            Assert.False(world.Market.IsSeeded);
            world.Market.SeedForYear(1997);

            Assert.True(world.Market.IsSeeded, "market.seeded");
            GpuProductSave bg = Background(world);
            Assert.NotNull(bg, "the background of the segment");
            Assert.Equal(GpuState.OwnerBackground, bg.owner);
            Assert.Equal(GpuState.StatusSelling, bg.status);
            Assert.Equal(string.Empty, bg.companyKey, "nobody's company");
            Assert.Equal(249f * world.Content.Balance.background.priceFactor, bg.price, "the price is the budget of the segment (1990s, t = 0) times background.priceFactor");
            Assert.Equal(new[] { "consumer" }, bg.segIds.ToArray());
            Assert.Equal(new[] { world.Content.Balance.background.popularity }, bg.pop.ToArray(), "background.popularity");
            Assert.Equal("arch.3d", world.Content.LatestArch(1995.4167f).id, "two years of lag: 1995");
            Assert.True(bg.stats.effectivePerf > 0d && bg.stats.tempC <= 85f);
            Assert.Equal("350nm", bg.node, "the node of the timeline in 1995");
            Assert.Equal(1997, world.State.Data.market.lastBackgroundRefresh.y);
            Assert.Equal(6, world.State.Data.market.lastBackgroundRefresh.m);
            Assert.Equal(new[] { "consumer" }, world.State.Data.market.openSegIds.ToArray());
            Assert.Equal(1, world.State.Data.market.potentialSales.Count);

            // the state passes the checks of a load without a word
            var check = new GpuState(world.Content);
            check.Sanitize(world.State.Data);
            Assert.Equal(0, check.Warnings.Count, string.Join("; ", new List<string>(check.Warnings).ToArray()));
        }

        [Test]
        public void TheSeed_IsDoneOnce()
        {
            GpuWorld world = World(new DateTime(1997, 6, 15));
            int seeded = 0;
            world.Market.Seeded += year => seeded++;
            world.Market.SeedForYear(1997);
            GpuProductSave bg = Background(world);
            float perf = (float)bg.stats.effectivePerf;
            int products = world.State.Data.products.Count;

            world.Game.FrontierKHz = 900000f;   // another frontier would give another card
            world.Market.SeedForYear(1997);
            Assert.False(world.Market.SeedIfNeeded(), "seeded already");
            world.Market.StartSession();
            Assert.Equal(1, seeded, "Seeded is raised once");
            Assert.Equal(products, world.State.Data.products.Count);
            Assert.Equal(perf, (float)Background(world).stats.effectivePerf, "the background was not made again");
        }

        [Test]
        public void Ac91_ASaveWithoutTheSection_IsSeededForItsYear_WithoutTheDivision()
        {
            GpuWorld world = World(new DateTime(2003, 4, 2), 3000000f);
            world.State.ResetToDefaults();   // ModSave: the save has no section
            world.Market.StartSession();     // GameReady(Loaded)

            Assert.False(world.State.Data.divisionFounded, "the division is not founded");
            Assert.True(world.Market.IsSeeded);
            GpuProductSave bg = Background(world);
            Assert.NotNull(bg, "the tab \"Рынок\" has the background");
            Assert.Equal(2003, bg.released.y);
            Assert.Equal("arch.tnl", world.Content.Arch(ArchOf(world, bg)).id, "2001 with two years of lag: hardware T&L");
        }

        // the architecture of a background card: the one whose features are the card's
        private static string ArchOf(GpuWorld world, GpuProductSave card)
        {
            foreach (GpuArchitectureDto arch in world.Content.Architectures)
            {
                if (arch.features.Count == card.stats.features.Count) return arch.id;
            }
            return null;
        }

        [Test]
        public void ANewGame_WhoseCompaniesAreNotReady_GetsItsBackgroundWhenTheGameIsReady()
        {
            GpuWorld world = World(new DateTime(1990, 1, 1), 0f);   // FastForwardEnded: no frontier frequency yet
            world.Market.SeedForYear(1990);
            Assert.True(world.Market.IsSeeded, "seeded once all the same");
            Assert.Null(Background(world), "no card could be made");
            Assert.Equal(1, world.Warnings.Count, "said once in the log");

            world.Game.FrontierKHz = 33000f;   // GameReady(NewGame): the companies are there
            world.Market.StartSession();
            Assert.NotNull(Background(world), "the background is there now");
        }

        [Test]
        public void TheMarket_RunsWithoutTheDivision()
        {
            GpuWorld world = World(new DateTime(1997, 6, 15));
            Assert.False(world.State.Data.divisionFounded);
            world.Market.SellDay();   // seeds on its first day if nobody did
            world.Market.DayTick();

            Assert.True(world.Market.IsSeeded);
            Assert.True(world.Market.SoldLast(Background(world).id) > 0, "the background sells");
            Assert.Equal(1f, world.Market.ShareOf(MarketItem.BackgroundOwner), "all of the market");
            Assert.Equal(0f, world.Market.ShareOf(MarketItem.PlayerOwner));
            Assert.Equal(0, world.Game.Earned.Count, "nobody is paid for the background");
            Assert.Equal(0, Background(world).stock, "the stock of the background never runs out");
        }

        [Test]
        public void Ac61_ACardTwiceAsFastAsTheBackground_AtTheBudget()
        {
            GpuWorld world = World(new DateTime(1997, 6, 15)).Founded();
            world.Market.SeedForYear(1997);
            GpuProductSave card = PlayerCard(world, "g-1", 2f, 249f);

            // the first pass of the day, by hand on the items the market builds
            List<SegmentRuntime> segs = world.Market.BuildSegments(world.Game.Date);
            List<MarketItem> items = world.Market.BuildItems(segs, world.Game.Date);
            SegmentRuntime seg = segs[0];
            Assert.Equal(249f, seg.Budget);
            double perfSum = 0;
            foreach (MarketItem item in items) perfSum += item.Perf;
            GpuMarketMath.CalculateRatings(seg, items, perfSum, false);
            GpuMarketMath.Normalize(seg, items);
            GpuMarketMath.Dilute(items);
            GpuMarketMath.PotentialBuyers(seg, items);
            MarketItem player = items.Find(i => i.ProductId == "g-1");
            MarketItem bg = items.Find(i => i.IsBackground);
            Assert.True(player.Overall > bg.Overall, "O_p > O_b");
            int expected = GpuDesignMath.RoundToInt(seg.Remaining * 0.05f * (player.Overall / (player.Overall + bg.Overall)));
            Assert.Equal(expected, player.PotentialBuyers, "round(N x 0.05 x O_p / (O_p + O_b))");
            Assert.True(expected > 0);

            // the whole day: at least the first pass is sold, the income is paid under "Sales" and booked
            world.Market.SellDay();
            world.Market.DayTick();
            int sold = world.Market.SoldLast("g-1");
            Assert.True(sold >= expected, "sold " + sold + " of at least " + expected);
            Assert.Near(249.0 * sold, world.Game.EarnedOn(GpuRevenue.Sales), 1.0, "EarnMoney(..., Sales)");
            Assert.Equal(sold, card.ledger.sold[0]);
            Assert.Near(249.0 * sold, card.ledger.income[0], 1.0);
            Assert.Equal(1000000 - sold, card.stock);

            // 60 days of sales: the popularity is above the start
            for (int day = 0; day < 60; day++)
            {
                world.NextDay();
                world.Market.SellDay();
                world.Market.DayTick();
            }
            Assert.True(card.pop[0] > 0.05f, "after 60 days: " + card.pop[0]);
            Assert.Equal(world.Content.Balance.background.popularity, Background(world).pop[0], "the background keeps its popularity (background.popularity)");
        }

        [Test]
        public void Ac62_APriceOfThreeBudgets_SellsNothing()
        {
            GpuWorld world = World(new DateTime(1997, 6, 15)).Founded();
            world.Market.SeedForYear(1997);
            GpuProductSave card = PlayerCard(world, "g-1", 4f, 3f * 249f);

            List<SegmentRuntime> segs = world.Market.BuildSegments(world.Game.Date);
            List<MarketItem> items = world.Market.BuildItems(segs, world.Game.Date);
            GpuMarketMath.CalculateRatings(segs[0], items, 1d, false);
            Assert.Equal(0f, items.Find(i => i.ProductId == "g-1").Overall, "rating 0");

            world.Market.SellDay();
            world.Market.DayTick();
            Assert.Equal(0, world.Market.SoldLast("g-1"), "no sales");
            Assert.Equal(1000000, card.stock);
            Assert.Equal(0, world.Game.Earned.Count);
        }

        [Test]
        public void Ac63_AnOldCardWithoutSales_Loses0_05In100Days_AndNeverGoesBelow0()
        {
            GpuWorld world = World(new DateTime(2006, 3, 1), 3600000f).Founded();
            world.Market.SeedForYear(2006);
            GpuProductSave card = PlayerCard(world, "g-1", 1f, 300f, 1000, 0.3f);
            card.released = new GpuDate { y = 2000, m = 5, d = 1 };
            card.sell = false;   // no sales
            GpuProductSave low = PlayerCard(world, "g-2", 1f, 300f, 1000, 0.03f);
            low.released = new GpuDate { y = 2000, m = 5, d = 1 };
            low.sell = false;

            for (int day = 0; day < 100; day++)
            {
                world.Market.SellDay();
                world.Market.DayTick();
                world.NextDay();
            }
            Assert.Near(0.25, card.pop[0], 1e-4, "0.3 - 100 x 0.0005");
            Assert.Equal(0f, low.pop[0], "an old card has no floor but 0");
            Assert.Equal(1000, card.stock, "it did not sell");
        }

        [Test]
        public void TheBackground_IsRenewedEvery12Months()
        {
            GpuWorld world = World(new DateTime(1997, 1, 10));
            world.Market.SeedForYear(1997);
            GpuProductSave bg = Background(world);
            double perf = bg.stats.effectivePerf;
            world.Game.FrontierKHz = 600000f;   // the industry gets faster

            for (int month = 2; month <= 12; month++)
            {
                world.Game.Date = new DateTime(1997, month, 1);
                world.Market.MonthTick();
                Assert.Equal(1, world.State.Data.market.lastBackgroundRefresh.m, "not renewed in month " + month);
                Assert.Equal(perf, Background(world).stats.effectivePerf);
            }

            world.Game.Date = new DateTime(1998, 1, 1);
            world.Market.MonthTick();
            GpuDate refresh = world.State.Data.market.lastBackgroundRefresh;
            Assert.Equal(1998, refresh.y, "renewed after 12 months");
            Assert.Equal(1, refresh.m);
            Assert.True(Background(world).stats.effectivePerf > perf, "a faster card");
            Assert.Equal(1998, Background(world).released.y);
            Assert.Equal(1, world.State.Data.products.FindAll(p => p.owner == GpuState.OwnerBackground).Count, "renewed in place");

            // months of a feature that was off count too: the next month tick after a long pause renews it
            world.Game.Date = new DateTime(2001, 7, 1);
            world.Market.MonthTick();
            Assert.Equal(2001, world.State.Data.market.lastBackgroundRefresh.y);
        }

        [Test]
        public void TheChannels_OfDemandRatingAndPopularity_Apply()
        {
            GpuWorld world = World(new DateTime(1997, 6, 15)).Founded();
            world.Market.SeedForYear(1997);
            GpuSegmentDto seg = world.Content.Segment("consumer");
            float buyers = world.Market.DailyBuyersOf(seg, 1997);
            Assert.Equal((float)Math.Floor(3e6 * 4.0 / 365.0), buyers, "F19: baseSize x size of the 1990s / 365");

            world.Game.DemandMul = 2f;
            Assert.Near(2.0 * buyers, world.Market.DailyBuyersOf(seg, 1997), 1.0, "gpu.demand x gpu.demand.consumer");
            world.Game.DemandMul = 1f;

            world.Game.RatingMul = 1.5f;
            PlayerCard(world, "g-1", 1f, 249f);
            List<SegmentRuntime> segs = world.Market.BuildSegments(world.Game.Date);
            List<MarketItem> items = world.Market.BuildItems(segs, world.Game.Date);
            Assert.Equal(1.5f, items.Find(i => i.ProductId == "g-1").RatingMul, "gpu.rating of the owner company");

            // popularity.gain: twice the gain grows the popularity faster
            GpuWorld fast = World(new DateTime(1997, 6, 15)).Founded();
            fast.Market.SeedForYear(1997);
            fast.Game.GainMul = 2f;
            GpuProductSave a = PlayerCard(fast, "g-1", 2f, 249f);
            GpuWorld slow = World(new DateTime(1997, 6, 15)).Founded();
            slow.Market.SeedForYear(1997);
            GpuProductSave b = PlayerCard(slow, "g-1", 2f, 249f);
            fast.Market.SellDay();
            slow.Market.SellDay();
            Assert.True(b.pop[0] > 0.05f && b.pop[0] < 1f, "a day of sales grows the popularity: " + b.pop[0]);
            double baseline = 0.00005 * 1.5;   // the baseline of the 1990s is not multiplied by the channel
            Assert.Near(2.0 * (b.pop[0] - 0.05f - baseline) + baseline, a.pop[0] - 0.05f, 1e-5, "popularity.gain x2: twice the gain of the first day");
        }

        [Test]
        public void AFablessPlayer_PaysTheRoyaltyOfItsSales()
        {
            GpuWorld world = World(new DateTime(1997, 6, 15)).Founded();
            world.Game.Fabless = true;
            world.Market.SeedForYear(1997);
            GpuProductSave card = PlayerCard(world, "g-1", 2f, 249f);
            world.Market.SellDay();

            float income = world.Game.EarnedOn(GpuRevenue.Sales);
            Assert.True(income > 0f);
            Assert.Near(0.05 * income, world.Game.SpentOn(GpuExpense.Royalties), 0.01, "5 % under Royalties");
            Assert.Near(0.05 * income, card.ledger.cost[0], 0.01, "in the books of the card");
        }

        [Test]
        public void TheDemandOfTheDay_GoesToTheAutomaticMode()
        {
            GpuWorld world = World(new DateTime(1997, 6, 15)).Founded();
            world.Market.SeedForYear(1997);
            PlayerCard(world, "g-1", 2f, 249f, 10);   // little stock: missed sales
            world.Market.SellDay();
            world.Market.DayTick();
            float demand = world.Production.AverageDemand("g-1");
            Assert.True(demand > 10f, "sold and missed: " + demand);
        }

        [Test]
        public void TheTabs_ReadTheSegments_TheTopAndTheShares()
        {
            GpuWorld world = World(new DateTime(1997, 6, 15)).Founded();
            world.Market.SeedForYear(1997);
            PlayerCard(world, "g-1", 2f, 249f);
            PlayerCard(world, "g-2", 1f, 249f).sell = false;
            for (int day = 0; day < 3; day++)
            {
                world.Market.SellDay();
                world.Market.DayTick();
                world.NextDay();
            }

            IReadOnlyList<GpuSegmentView> segments = world.Market.OpenSegments;
            Assert.Equal(1, segments.Count);
            GpuSegmentView seg = segments[0];
            Assert.Equal("consumer", seg.Id);
            Assert.Equal("gpu.seg.consumer", seg.NameKey);
            Assert.Equal(249f, seg.Budget);
            Assert.Equal(seg.DailyBuyers * 30f, seg.MonthlyBuyers);
            Assert.True(seg.VramRequirementMB > 2f && seg.VramRequirementMB < 32f, "between 1996 and 2000: " + seg.VramRequirementMB);
            Assert.Equal(0, seg.RequiredFeatures.Length);

            IReadOnlyList<GpuMarketEntry> top = world.Market.TopProducts("consumer");
            Assert.Equal(3, top.Count, "the player's two cards and the background");
            Assert.True(top[0].Sold30 >= top[1].Sold30 && top[1].Sold30 >= top[2].Sold30, "by the sales of 30 days");
            Assert.Equal("g-2", top[2].ProductId, "the card that does not sell is last");
            GpuMarketEntry background = new List<GpuMarketEntry>(top).Find(e => e.OwnerKey == MarketItem.BackgroundOwner);
            Assert.NotNull(background);
            Assert.True(background.ClockMHz > 0f, "the clock of a generated card");
            Assert.Equal("350nm", background.Node);
            Assert.Equal(1, world.Market.TopProducts("consumer", 1).Count);
            Assert.Equal(0, world.Market.TopProducts("gaming.high").Count, "no such segment in stage 1");

            float player = world.Market.ShareOf(MarketItem.PlayerOwner);
            float others = world.Market.ShareOf(MarketItem.BackgroundOwner);
            Assert.Near(1.0, player + others, 1e-5, "the shares add up");
            Assert.True(player > 0f);
            Assert.Equal(3, world.State.Data.market.playerShareDaily.Count, "a value a day (saved)");
            Assert.Near(player, world.Market.PlayerShare, 0.05, "the saved window agrees");
            Assert.Equal(world.Market.SoldLast("g-1") + world.Market.SoldLast("g-2"), world.State.Data.market.playerSoldMonthly[0], "the month of the player");

            world.Game.Date = new DateTime(1997, 7, 1);
            world.Market.MonthTick();
            Assert.Equal(2, world.State.Data.market.playerSoldMonthly.Count, "a new month");
            Assert.Equal(0, world.State.Data.market.playerSoldMonthly[1]);

            world.Market.Reset();
            Assert.Equal(0, world.Market.SoldLast("g-1"), "the windows belong to the session");
            Assert.Equal(0f, world.Market.ShareOf(MarketItem.PlayerOwner));
        }

        [Test]
        public void TheMonthsOfThePlayer_KeepTwoYears_AndTheShareWindowThirtyDays()
        {
            GpuWorld world = World(new DateTime(1997, 6, 15));
            world.Market.SeedForYear(1997);
            for (int i = 0; i < 40; i++)
            {
                world.Market.DayTick();
                world.Market.MonthTick();
            }
            Assert.Equal(GpuMarketService.PlayerMonthsKept + 1, world.State.Data.market.playerSoldMonthly.Count, "24 months and the month that runs");
            Assert.Equal(GpuMarketService.ShareWindowDays, world.State.Data.market.playerShareDaily.Count);
            Assert.Equal(12, GpuMarketService.MonthsBetween(new GpuDate { y = 1997, m = 1, d = 31 }, new DateTime(1998, 1, 1)));
            Assert.Equal(int.MaxValue, GpuMarketService.MonthsBetween(null, new DateTime(1998, 1, 1)));
        }

        [Test]
        public void ACardWithoutAnEntryForAnOpenSegment_StartsThereAtTheStartValue()
        {
            GpuWorld world = World(new DateTime(1997, 6, 15)).Founded();
            world.Market.SeedForYear(1997);
            GpuProductSave card = PlayerCard(world, "g-1", 1f, 249f);
            card.segIds.Clear();
            card.pop.Clear();
            world.Market.SellDay();
            Assert.Equal(new[] { "consumer" }, card.segIds.ToArray());
            Assert.Equal(1, card.pop.Count);
            Assert.True(card.pop[0] >= 0.05f);
        }

        [Test]
        public void TheSameWorldSeed_GivesTheSameBackground()
        {
            GpuWorld a = World(new DateTime(2004, 2, 1), 3400000f, 77);
            GpuWorld b = World(new DateTime(2004, 2, 1), 3400000f, 77);
            a.Market.SeedForYear(2004);
            b.Market.SeedForYear(2004);
            Assert.Equal(Background(a).die, Background(b).die);
            Assert.Equal(Background(a).stats.effectivePerf, Background(b).stats.effectivePerf);
            Assert.Equal(Background(a).price, Background(b).price);
        }

        [Test]
        public void TheMarketService_RefusesMissingParts()
        {
            GpuWorld world = World(new DateTime(1997, 1, 1));
            var rng = new GpuRandom(new PTMod.Core.Logic.Rng.Xoshiro128(1));
            Assert.Throws<ArgumentNullException>(() => new GpuMarketService(null, world.State, world.Game, null, rng, null));
            Assert.Throws<ArgumentNullException>(() => new GpuMarketService(world.Content, null, world.Game, null, rng, null));
            Assert.Throws<ArgumentNullException>(() => new GpuMarketService(world.Content, world.State, null, null, rng, null));
            Assert.Throws<ArgumentNullException>(() => new GpuMarketService(world.Content, world.State, world.Game, null, null, null));
            Assert.Equal("player", GpuMarketService.OwnerKeyOf(new GpuProductSave { owner = GpuState.OwnerPlayer }));
            Assert.Equal("vendor:ati", GpuMarketService.OwnerKeyOf(new GpuProductSave { owner = GpuState.OwnerVendor, vendorId = "ati" }));
            Assert.Equal("background", GpuMarketService.OwnerKeyOf(new GpuProductSave { owner = GpuState.OwnerBackground }));
        }
    }
}
