using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The behavior of the vendors (TSK-034; FR-13, F22-F24) on the shipped vendors and the fake game: the cadence of the launches (the interval and 0-3 months from the stream of
    /// the vendors, by the calendar), the same cards from the same seed (AC-13.1), the parts of <c>year - techLag</c> (AC-13.2), the company in default that launches nothing
    /// (AC-13.3), the balance that would go under the default threshold (AC-13.4), the money of a bound vendor (development at the launch, the cost of what it sold, the revenue),
    /// the price (F23), at most two cards in a segment, and the stock that is topped up to the capacity every day.
    /// </summary>
    [Covers(typeof(GpuRivalAI))]
    public class GpuRivalAITests
    {
        private static GpuWorld World(DateTime today, uint seed = 42, bool amd = true, bool intel = true)
        {
            var world = new GpuWorld(today, seed, GpuWorld.ShippedEras());
            world.Game.AddTimelineNodes();
            world.Game.FrontierKHz = 250000f;
            world.RivalsOn = true;
            if (amd) world.Game.AddCompany("ai:1", "AMD");
            if (intel) world.Game.AddCompany("ai:0", "Intel");
            return world;
        }

        private static void MonthsUntil(GpuWorld world, DateTime last)
        {
            DateTime d = new DateTime(world.Game.Date.Year, world.Game.Date.Month, 1);
            while (d < last)
            {
                d = d.AddMonths(1);
                world.Game.Date = d;
                world.MonthTicks();
            }
        }

        private sealed class Launch
        {
            public string Vendor, Segment, Name, Id;
            public DateTime Date;
            public float Price, UnitCost, ProjectCost;
            public double Perf;

            public override string ToString()
            {
                return Date.ToString("yyyy-MM-dd") + " " + Vendor + " " + Segment + " " + Name + " perf " + Perf.ToString("0.###") + " price " + Price.ToString("0.###");
            }
        }

        /// <summary>Every card a vendor launches from now on (not the seed), in order.</summary>
        private static List<Launch> Listen(GpuWorld world)
        {
            var list = new List<Launch>();
            world.Rivals.AI.Launched += (vendor, card) => list.Add(new Launch
            {
                Vendor = vendor.Id, Segment = card.segIds[0], Name = card.name, Id = card.id, Date = new DateTime(card.released.y, card.released.m, card.released.d),
                Price = card.price, UnitCost = card.stats.unitCost, ProjectCost = card.stats.projectCost, Perf = card.stats.effectivePerf,
            });
            return list;
        }

        private static List<GpuProductSave> Cards(GpuWorld world, string vendorId)
        {
            return world.Rivals.AI.CardsOf(vendorId);
        }

        private static int Months(DateTime from, DateTime to)
        {
            return (to.Year - from.Year) * 12 + (to.Month - from.Month);
        }

        // ---- the cadence (FR-13 item 1) ----

        [Test]
        public void TheLaunches_ComeEveryIntervalAndUpToThreeMonthsMore_InEachSegmentOfAVendor()
        {
            GpuWorld world = World(new DateTime(1990, 1, 1));
            world.Market.SeedForYear(1990);
            List<Launch> launches = Listen(world);
            MonthsUntil(world, new DateTime(2002, 1, 1));

            Assert.True(launches.Count > 60, "twelve years of seven vendors: " + launches.Count);
            var gaps = new List<int>();
            foreach (var group in launches.GroupBy(l => l.Vendor + "|" + l.Segment))
            {
                string[] key = group.Key.Split('|');
                GpuVendorDto def = world.Content.Vendor(key[0]);
                Launch[] each = group.OrderBy(l => l.Date).ToArray();
                for (int i = 1; i < each.Length; i++)
                {
                    int gap = Months(each[i - 1].Date, each[i].Date);
                    gaps.Add(gap - def.designIntervalMonths);
                    Assert.True(gap >= def.designIntervalMonths && gap <= def.designIntervalMonths + world.Content.Balance.rivals.intervalJitterMonths,
                        group.Key + ": " + gap + " months between two launches (interval " + def.designIntervalMonths + ")");
                }
                Assert.True(world.Content.ActiveSegmentIds(def).Contains(key[1]), group.Key + ": a vendor launches in the segments of its own list");
            }
            Assert.True(gaps.Count > 30);
            Assert.True(gaps.Distinct().Count() >= 3, "the months drawn on top differ: " + string.Join(",", gaps.Distinct().OrderBy(g => g).Select(g => g.ToString()).ToArray()));
            Assert.True(gaps.Min() >= 0 && gaps.Max() <= 3, "0-3 months on top");
        }

        [Test]
        public void ALaunch_ComesOnlyInAnOpenSegment_AndTheFirstOneOfAVendorComesAtOnce()
        {
            GpuWorld world = World(new DateTime(1994, 12, 1));
            world.Market.SeedForYear(1994);
            List<Launch> launches = Listen(world);
            MonthsUntil(world, new DateTime(1998, 6, 1));

            foreach (Launch launch in launches)
            {
                GpuSegmentDto seg = world.Content.Segment(launch.Segment);
                Assert.True(GpuEras.IsOpen(seg, launch.Date), launch + ": the segment is open");
            }
            Launch first3d = launches.First(l => l.Vendor == "3dfx");
            Assert.Equal(new DateTime(1996, 1, 1), first3d.Date, "3dfx launches the month it enters");
            Launch firstLow = launches.First(l => l.Segment == "gaming.low");
            Assert.Equal(new DateTime(1998, 1, 1), firstLow.Date, "and a segment that opens gets its cards on the 1st of January");
        }

        [Test]
        public void AfterATimeTheFeatureWasOff_EachDueLaunchComesOnceOnTheFirstMonth()
        {
            GpuWorld world = World(new DateTime(2000, 6, 1));
            world.Market.SeedForYear(2000);
            List<Launch> launches = Listen(world);

            world.Game.Date = new DateTime(2003, 1, 1);   // two and a half years, no month ticks
            world.Eras.MonthTick();
            world.Rivals.MonthTick();

            Assert.True(launches.Count > 0);
            Assert.Equal(launches.Count, launches.Select(l => l.Vendor + "|" + l.Segment).Distinct().Count(), "one card per vendor and segment, not one per missed period");
            foreach (var group in Cards(world, "nvidia").GroupBy(c => c.segIds[0]))
                Assert.True(group.Count() <= world.Content.Balance.rivals.maxProductsPerSegment, group.Key);
        }

        // ---- the same cards from the same seed (AC-13.1) ----

        private static string[] LaunchesOf(uint seed, DateTime from, int months)
        {
            GpuWorld world = World(from, seed);
            world.Market.SeedForYear(from.Year);
            List<Launch> launches = Listen(world);
            MonthsUntil(world, from.AddMonths(months));
            return launches.Select(l => l.ToString()).ToArray();
        }

        [Test]
        public void Ac131_TheSameSeedAndTwentyFourMonths_GiveTheSameLaunches()
        {
            string[] a = LaunchesOf(7, new DateTime(2001, 1, 1), 24);
            string[] b = LaunchesOf(7, new DateTime(2001, 1, 1), 24);
            string[] other = LaunchesOf(8, new DateTime(2001, 1, 1), 24);

            Assert.True(a.Length > 10, "launches in two years: " + a.Length);
            Assert.Equal(a, b, "NFR-6: from one seed, one history");
            Assert.False(a.SequenceEqual(other), "another seed, other cards");
        }

        [Test]
        public void TheCardsOfTheSeed_AreTheSameFromTheSameSeedToo()
        {
            GpuWorld a = World(new DateTime(2003, 1, 1), 5);
            GpuWorld b = World(new DateTime(2003, 1, 1), 5);
            a.Market.SeedForYear(2003);
            b.Market.SeedForYear(2003);
            Assert.Equal(a.State.Data.products.Select(p => p.id + " " + p.name + " " + p.price + " " + p.stats.effectivePerf + " " + p.released.m).ToArray(),
                b.State.Data.products.Select(p => p.id + " " + p.name + " " + p.price + " " + p.stats.effectivePerf + " " + p.released.m).ToArray());
        }

        // ---- the card (F22, F23; AC-13.2) ----

        [Test]
        public void Ac132_AVendorWithALagOfHalfAYear_MakesTheParts_OfTheYearBeforeTheLag()
        {
            GpuWorld world = World(new DateTime(2003, 3, 1));
            world.Market.SeedForYear(2003);

            GpuVendorRuntime nvidia = world.Rivals.Find("nvidia");
            Assert.Equal(0.5f, nvidia.Def.techLag);
            Assert.Near(2002.6667, GpuRivalGenerator.TechYear(new DateTime(2003, 3, 1), nvidia.Def.techLag), 1e-3, "techYear of 1 March 2003");
            GpuProductSave card = Cards(world, "nvidia").First(c => c.segIds[0] == "gaming.high");

            Assert.True(card.stats.features.Contains("shader"), "AC-13.2: arch.shader (2002 <= techYear)");
            Assert.False(card.stats.features.Contains("unified"), "not arch.unified (2006)");
            Assert.Equal("130nm", card.node, "the process node of the timeline in 2002");
            Assert.True(card.busWidth == 64 || card.busWidth == 128, "vram.ddr (1999), not GDDR3 (2004): its buses are 64 and 128, got " + card.busWidth);
            Assert.True(card.vramMB <= 128f, "and its memory is at most 128 MB: " + card.vramMB);

            // the card on the tab "Рынок" shows its node and its clock
            GpuMarketEntry entry = world.Market.TopProducts("gaming.high", 20).First(e => e.ProductId == card.id);
            Assert.Equal("130nm", entry.Node);
            Assert.True(entry.ClockMHz > 0f, "the clock: " + entry.ClockMHz);
            Assert.True(entry.ClockMHz <= card.stats.maxClockMHz * 1.001f, "within what the architecture can do");
            Assert.True(entry.ClockMHz >= 0.85f * card.stats.maxClockMHz, "about 95 % of the frontier: " + entry.ClockMHz + " of " + card.stats.maxClockMHz);
        }

        [Test]
        public void AVendorBoundToACompany_MakesItsCardsOnTheNodeAndTheClockOfTheCompany()
        {
            GpuWorld world = World(new DateTime(2007, 6, 1));
            world.Game.Companies["ai:1"].NodeName = "90nm";
            world.Game.Companies["ai:1"].MaxFreqKHz = 180000f;
            world.Market.SeedForYear(2007);

            Assert.True(Cards(world, "amd.gpu").Count > 0);
            foreach (GpuProductSave card in Cards(world, "amd.gpu"))
            {
                Assert.Equal("90nm", card.node, "the newest node of the company, not the one of the timeline");
                Assert.True(card.stats.maxClockMHz <= 180f * 0.5f + 1f, "no faster than the company's frequency allows: " + card.stats.maxClockMHz);
            }
            foreach (GpuProductSave card in Cards(world, "nvidia")) Assert.Equal("65nm", card.node, "a virtual vendor takes the node of the timeline (65nm from 2006)");
        }

        [Test]
        public void ThePrice_IsTheLargerOf13TimesTheCost_AndTheBudgetTimesTheFactor()
        {
            GpuWorld world = World(new DateTime(1990, 1, 1));
            world.Market.SeedForYear(1990);
            List<Launch> launches = Listen(world);
            MonthsUntil(world, new DateTime(2006, 1, 1));

            Assert.True(launches.Count > 80);
            int budgetWon = 0, costWon = 0;
            foreach (Launch launch in launches)
            {
                GpuSegmentDto seg = world.Content.Segment(launch.Segment);
                GpuVendorDto def = world.Content.Vendor(launch.Vendor);
                float budget = world.Market.BudgetOf(seg, launch.Date.Year);
                float expected = Math.Max(world.Content.Balance.rivals.priceFloorMarkup * launch.UnitCost, budget * def.priceFactor);
                Assert.Near(expected, launch.Price, expected * 1e-4 + 0.01, launch + ": F23");
                Assert.True(launch.Price >= 1.3f * launch.UnitCost - 0.01f, launch + ": never under 1.3 x the cost");
                if (launch.Price > budget * def.priceFactor + 0.01f) costWon++;
                else budgetWon++;
            }
            Assert.True(budgetWon > 0, "most prices follow the budget of the segment");
            Console.WriteLine("  rivals the price follows the budget in " + budgetWon + " launches and the cost in " + costWon);
        }

        [Test]
        public void ACardOfAVendor_StartsAtHalfThePopularityOfItsBest_OrWhatItsStrengthSays()
        {
            GpuWorld world = World(new DateTime(2003, 6, 1));
            world.Market.SeedForYear(2003);
            GpuVendorRuntime nvidia = world.Rivals.Find("nvidia");
            GpuSegmentDto mid = world.Content.Segment("gaming.mid");
            GpuProductSave first = Cards(world, "nvidia").First(c => c.segIds[0] == "gaming.mid");

            float known = world.Content.Balance.background.popularity;   // 0.8 after the calibration of TSK-045 (0.35 in the design)
            float carry = world.Content.Balance.popularity.brandCarry;
            Assert.Near(1.0 * known, world.Rivals.AI.StartPopularity(nvidia, mid, "none"), 1e-5, "brandCarry x the best card is under strength 1.0 x background.popularity");
            first.pop[0] = 1.0f;
            Assert.Near(Math.Max(carry * 1.0, 1.0 * known), world.Rivals.AI.StartPopularity(nvidia, mid, "none"), 1e-5, "the better of brandCarry x 1.0 and the strength");
            Assert.Near(1.0 * known, world.Rivals.AI.StartPopularity(nvidia, mid, first.id), 1e-5, "without the card itself: the strength");
            GpuVendorRuntime matrox = world.Rivals.Find("matrox");
            GpuSegmentDto office = world.Content.Segment("office2d");
            Assert.Near(0.6 * known, world.Rivals.AI.StartPopularity(matrox, office, "none"), 1e-5, "strength 0.6 x background.popularity");
            GpuProductSave card = Cards(world, "matrox").First(c => c.segIds[0] == "office2d");
            card.pop[0] = 1.0f;
            Assert.Near(Math.Max(carry * 1.0, 0.6 * known), world.Rivals.AI.StartPopularity(matrox, office, "none"), 1e-5, "a card as well known as it gets: brandCarry x 1.0 when that is more than the strength");
        }

        [Test]
        public void AVendorHoldsAtMostTwoCards_InASegment_TheOldestOfThreeGoes()
        {
            GpuWorld world = World(new DateTime(2003, 6, 1));
            world.Market.SeedForYear(2003);
            GpuVendorRuntime nvidia = world.Rivals.Find("nvidia");
            GpuSegmentDto mid = world.Content.Segment("gaming.mid");
            GpuProductSave a = Cards(world, "nvidia").Single(c => c.segIds[0] == "gaming.mid");
            Assert.Equal(2, world.Content.Balance.rivals.maxProductsPerSegment);

            GpuProductSave b = world.Rivals.AI.Launch(nvidia, mid, new DateTime(2003, 7, 1), new DateTime(2003, 7, 1), false);
            Assert.NotNull(b);
            Assert.Equal(2, Cards(world, "nvidia").Count(c => c.segIds[0] == "gaming.mid"), "two cards in the segment");
            GpuProductSave c2 = world.Rivals.AI.Launch(nvidia, mid, new DateTime(2003, 8, 1), new DateTime(2003, 8, 1), false);
            Assert.NotNull(c2);

            List<GpuProductSave> mids = Cards(world, "nvidia").Where(c => c.segIds[0] == "gaming.mid").ToList();
            Assert.Equal(2, mids.Count, "FR-13 item 7: the third takes the oldest off");
            Assert.False(mids.Contains(a), "the oldest is gone");
            Assert.True(mids.Contains(b) && mids.Contains(c2));
            Assert.Null(world.State.Data.products.Find(p => p.id == a.id), "not kept in an archive");
            Assert.Equal(1, Cards(world, "nvidia").Count(c => c.segIds[0] == "gaming.high"), "the other segments are not touched");
        }

        [Test]
        public void ACardThatCannotBeMade_IsTriedAgainNextMonth_AndSaidOnce()
        {
            GpuWorld world = World(new DateTime(2003, 6, 1));
            world.Market.SeedForYear(2003);
            GpuVendorRuntime nvidia = world.Rivals.Find("nvidia");
            GpuSegmentDto mid = world.Content.Segment("gaming.mid");
            int cards = world.Rivals.CardsOnSale;

            world.Game.MaxFreqKHz = 0f;
            world.Game.FrontierKHz = 0f;   // the frontier of the industry is not known
            Assert.Null(world.Rivals.AI.Launch(nvidia, mid, new DateTime(2003, 7, 1), new DateTime(2003, 7, 1), false));
            Assert.Null(world.Rivals.AI.Launch(nvidia, mid, new DateTime(2003, 8, 1), new DateTime(2003, 8, 1), false));
            Assert.Equal(cards, world.Rivals.CardsOnSale);
            Assert.Equal(1, world.Warnings.Count(w => w.Contains("'nvidia'")), "one line in the log");

            world.Game.MaxFreqKHz = 250000f;
            world.Game.FrontierKHz = 250000f;
            Assert.NotNull(world.Rivals.AI.Launch(nvidia, mid, new DateTime(2003, 9, 1), new DateTime(2003, 9, 1), false));
        }

        // ---- the money of a bound vendor (F24, FR-13 items 5-6) ----

        [Test]
        public void ABoundVendor_PaysForTheDevelopmentAtTheLaunch_UnderDevelopment()
        {
            GpuWorld world = World(new DateTime(2007, 1, 1));
            world.Market.SeedForYear(2007);
            world.Game.Companies["ai:1"].Cash = 1e10f;
            float start = world.Game.Companies["ai:1"].Cash;
            List<Launch> launches = Listen(world);

            MonthsUntil(world, new DateTime(2009, 1, 1));

            var mine = launches.Where(l => l.Vendor == "amd.gpu").ToList();
            Assert.True(mine.Count >= 4, "AMD launched cards in two years: " + mine.Count);
            float cost = mine.Sum(l => l.ProjectCost);
            Assert.True(cost > 0f);
            Assert.Near(cost, world.Game.SpentBy("ai:1", GpuExpense.Development), cost * 1e-4, "each launch costs the development of the card, F13");
            Assert.Near(start - cost, world.Game.Companies["ai:1"].Cash, start * 1e-5, "and nothing else is taken from the company");
            Assert.Equal(0f, world.Game.SpentBy("ai:0", GpuExpense.Development), "Intel was not asked");
            Assert.Equal(0f, world.Game.SpentBy("player", GpuExpense.Development), "nor the player");
            Assert.Equal(0, world.Game.Spent.Count(m => m.Company != "ai:1"), "a virtual vendor pays nobody");
        }

        [Test]
        public void ABoundVendor_PaysTheCostOfTheCardsItSold_UnderFactories_AndIsPaidTheRevenue()
        {
            GpuWorld world = World(new DateTime(2007, 1, 1));
            world.Market.SeedForYear(2007);
            world.Rivals.RefillStock();

            world.Market.SellDay();
            world.Market.DayTick();

            float cost = 0f, revenue = 0f;
            int sold = 0;
            foreach (GpuProductSave card in Cards(world, "amd.gpu"))
            {
                int n = world.Market.SoldLast(card.id, 1);
                sold += n;
                cost += card.stats.unitCost * n;
                revenue += card.price * n;
            }
            Assert.True(sold > 0, "AMD sold cards on the first day: " + sold);
            Assert.Near(cost, world.Game.SpentBy("ai:1", GpuExpense.Factories), cost * 1e-4 + 0.01, "F24: the cost of what it sold");
            Assert.Near(revenue, world.Game.EarnedOn(GpuRevenue.Sales, "ai:1"), revenue * 1e-4 + 0.01, "and the revenue of the sales");
            Assert.True(world.Game.EarnedOn(GpuRevenue.Sales, "ai:1") > world.Game.SpentBy("ai:1", GpuExpense.Factories), "at a price above the cost");
            Assert.Equal(0f, world.Game.EarnedOn(GpuRevenue.Sales, "ai:0"), "Intel does not sell yet");
            Assert.Equal(0, world.Game.Earned.Count(m => m.Company == string.Empty || m.Company == null), "no money for a virtual vendor");
        }

        [Test]
        public void Ac133_ACompanyInDefault_LaunchesNothing_ForTwelveMonths()
        {
            GpuWorld world = World(new DateTime(2006, 1, 1));
            world.Market.SeedForYear(2006);
            world.Game.Companies["ai:1"].InDefault = true;
            int launchCount = world.Rivals.Find("amd.gpu").Save.launchCount;
            List<Launch> launches = Listen(world);
            float cash = world.Game.Companies["ai:1"].Cash;

            MonthsUntil(world, new DateTime(2007, 1, 1));

            Assert.Equal(0, launches.Count(l => l.Vendor == "amd.gpu"), "AC-13.3: no new cards in twelve months");
            Assert.Equal(launchCount, world.Rivals.Find("amd.gpu").Save.launchCount);
            Assert.True(launches.Count(l => l.Vendor == "nvidia") > 0, "the others go on");
            Assert.Equal(0f, world.Game.SpentBy("ai:1", GpuExpense.Development), "and nothing was spent");

            world.Game.Companies["ai:1"].InDefault = false;
            world.Game.Date = new DateTime(2007, 2, 1);
            world.MonthTicks();
            Assert.True(launches.Count(l => l.Vendor == "amd.gpu") > 0, "out of default, the next month tick launches");
        }

        [Test]
        public void Ac134_AProjectThatTakesTheBalanceUnderTheThreshold_IsPutOff_AndTheBalanceDoesNotMove()
        {
            GpuWorld world = World(new DateTime(2006, 1, 1));
            world.Market.SeedForYear(2006);
            FakeCompany amd = world.Game.Companies["ai:1"];
            amd.Cash = amd.Threshold + 5e6f;   // 5 million above the threshold of the default
            float before = amd.Cash;
            List<Launch> launches = Listen(world);

            MonthsUntil(world, new DateTime(2006, 7, 1));

            Assert.Equal(0, launches.Count(l => l.Vendor == "amd.gpu"), "AC-13.4: the launch is put off");
            Assert.Equal(before, amd.Cash, "the balance of the company did not change");
            Assert.Equal(0, world.Game.Spent.Count(m => m.Company == "ai:1"));

            amd.Cash = 1e10f;   // the money comes
            world.Game.Date = new DateTime(2006, 8, 1);
            world.MonthTicks();
            Assert.True(launches.Count(l => l.Vendor == "amd.gpu") > 0, "and the launch follows on the next month tick");
            Assert.True(amd.Cash < 1e10f, "the development was paid");
        }

        [Test]
        public void TheFundsRule_IsTheBalanceAfterThePaymentAgainstTheThreshold()
        {
            GpuWorld world = World(new DateTime(2006, 1, 1));
            FakeCompany amd = world.Game.Companies["ai:1"];
            amd.Threshold = -100f;
            amd.Cash = 50f;
            Assert.True(world.Rivals.AI.CanAfford("ai:1", 150f), "50 - 150 = -100: exactly the threshold is allowed (the game defaults below it)");
            Assert.False(world.Rivals.AI.CanAfford("ai:1", 150.5f), "under it is not");
            Assert.True(world.Rivals.AI.CanAfford(string.Empty, 1e12f), "a virtual vendor has no balance");
            amd.InDefault = true;
            Assert.False(world.Rivals.AI.CanAfford("ai:1", 1f), "in default: nothing");
            amd.InDefault = false;
            amd.Bankrupt = true;
            Assert.False(world.Rivals.AI.CanAfford("ai:1", 1f), "bankrupt: nothing");
            Assert.False(world.Rivals.AI.CanAfford("ai:9", 1f), "a company that is not there: nothing");
        }

        // ---- the capacity (F24, FR-13 item 4) ----

        [Test]
        public void EveryDay_TheStockOfAVendorInASegment_IsToppedUpToTheShareOfTheBuyers()
        {
            GpuWorld world = World(new DateTime(2003, 6, 1));
            world.Market.SeedForYear(2003);
            DateTime today = world.Game.Date;
            Assert.Equal(0L, world.State.Data.products.Where(p => p.owner == GpuState.OwnerVendor).Sum(p => (long)p.stock), "the cards come without stock");

            for (int day = 0; day < 3; day++)
            {
                world.Rivals.RefillStock();
                foreach (GpuVendorRuntime vendor in world.Rivals.Active)
                {
                    foreach (string segmentId in world.Content.ActiveSegmentIds(vendor.Def))
                    {
                        GpuSegmentDto seg = world.Content.Segment(segmentId);
                        if (!GpuEras.IsOpen(seg, today)) continue;
                        long capacity = (long)Math.Round(vendor.Def.capacityShare * world.Market.DailyBuyersOf(seg, today.Year));
                        long stock = Cards(world, vendor.Id).Where(c => c.segIds[0] == segmentId).Sum(c => (long)c.stock);
                        Assert.Equal(capacity, stock, vendor.Id + " in " + segmentId + ", day " + day + ": capacityShare x the buyers of the day");
                    }
                }
                world.Market.SellDay();
                world.Market.DayTick();
            }
        }

        [Test]
        public void TheNewestCardGetsTheTopUp_TheOlderOneSellsWhatItHas()
        {
            GpuWorld world = World(new DateTime(2003, 6, 1));
            world.Market.SeedForYear(2003);
            GpuVendorRuntime nvidia = world.Rivals.Find("nvidia");
            GpuProductSave older = Cards(world, "nvidia").Single(c => c.segIds[0] == "gaming.mid");
            GpuProductSave newer = world.Rivals.AI.Launch(nvidia, world.Content.Segment("gaming.mid"), new DateTime(2003, 6, 1), new DateTime(2003, 6, 1), false);
            older.stock = 1000;

            world.Rivals.RefillStock();
            Assert.Equal(1000, older.stock, "the older card keeps its stock");
            long capacity = (long)Math.Round(nvidia.Def.capacityShare * world.Market.DailyBuyersOf(world.Content.Segment("gaming.mid"), 2003));
            Assert.Equal(capacity - 1000, (long)newer.stock, "the newer one gets the rest of the capacity");

            world.Rivals.RefillStock();
            Assert.Equal(capacity - 1000, (long)newer.stock, "and a second call the same day adds nothing");
        }

        [Test]
        public void TheVendors_ReserveNoLines_WhileAPlayersCardDoes()
        {
            var world = new GpuWorld(new DateTime(2005, 1, 1), 42, GpuWorld.ShippedEras());
            world.Game.AddTimelineNodes();
            world.Game.FrontierKHz = 250000f;
            world.Game.AddCompany("ai:1", "AMD");
            world.Game.AddCompany("ai:0", "Intel");
            world.RivalsOn = true;
            world.Founded("arch.shader", "vram.gddr3", "cool.fan");
            world.Market.SeedForYear(2005);
            world.AddCard("g-1", 4);

            for (int day = 0; day < 20; day++) world.RunDay();

            Assert.True(world.Rivals.CardsOnSale > 5, "vendors on the market");
            Assert.Equal(20, world.Game.Factory.Reservations, "one reserve a day: the card of the player");
            Assert.Equal(0, world.Game.Factory.AllocatedLines, "the factory is reset every day");
            Assert.Equal(4, world.Production.ReservedToday, "and what the card reserved was the lines of the card alone");
        }
    }
}
