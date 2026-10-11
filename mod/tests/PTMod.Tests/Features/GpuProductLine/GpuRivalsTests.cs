using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Json;
using PTMod.Features.GpuProductLine;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The named makers of the GPU market (TSK-033; FR-12, FR-9 item 3) on the shipped vendors and the fake game with AI companies: the seed of the year (AC-9.1), the entry of
    /// 3dfx (AC-12.1), the merger of ATI into AMD (AC-12.2) and of 3dfx into NVIDIA, the bankruptcy of the company a vendor is bound to (AC-12.3), the company that is not in the
    /// game (AC-12.4), the switch "Соперники", the state that comes back from the save. The cadence, the money and the stock of the vendors are <see cref="GpuRivalAITests"/>.
    /// </summary>
    [Covers(typeof(GpuRivals), typeof(GpuVendorRuntime))]
    public class GpuRivalsTests
    {
        private static GpuWorld World(DateTime today, bool amd = true, bool intel = true, uint seed = 42, bool rivals = true)
        {
            var world = new GpuWorld(today, seed, GpuWorld.ShippedEras());
            world.Game.AddTimelineNodes();
            world.Game.FrontierKHz = 250000f;
            world.RivalsOn = rivals;
            if (amd) world.Game.AddCompany("ai:1", "AMD");
            if (intel) world.Game.AddCompany("ai:0", "Intel");
            return world;
        }

        /// <summary>The 1st of every month after the current date up to <paramref name="last"/>, with the monthly handlers of the feature.</summary>
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

        private sealed class Heard
        {
            public readonly List<string> Entered = new List<string>();
            public readonly List<string> Exited = new List<string>();
            public readonly List<string> Merged = new List<string>();

            public Heard(GpuRivals rivals)
            {
                rivals.Entered += v => Entered.Add(v.Id);
                rivals.Exited += v => Exited.Add(v.Id);
                rivals.Merged += (v, into) => Merged.Add(v.Id + ">" + into.Id);
            }
        }

        private static string StateOf(GpuWorld world, string vendorId)
        {
            GpuVendorRuntime vendor = world.Rivals.Find(vendorId);
            Assert.NotNull(vendor, vendorId + " has a record");
            return vendor.State;
        }

        private static List<GpuProductSave> Cards(GpuWorld world, string vendorId)
        {
            return world.Rivals.AI.CardsOf(vendorId);
        }

        private static string[] SegmentsOf(IEnumerable<GpuProductSave> cards)
        {
            return cards.Select(p => p.segIds[0]).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        }

        // ---- the seed of the year (AC-9.1) ----

        [Test]
        public void TheSeed_PutsTheVendorsOfTheYearOnTheMarket_WithoutEventsOrMoney()
        {
            GpuWorld world = World(new DateTime(2003, 6, 1));
            var heard = new Heard(world.Rivals);
            world.Market.SeedForYear(2003);

            Assert.Equal(GpuState.VendorExited, StateOf(world, "s3"), "S3 left in 2003");
            Assert.Equal(GpuState.VendorActive, StateOf(world, "matrox"));
            Assert.Equal(GpuState.VendorActive, StateOf(world, "ati"));
            Assert.Equal(GpuState.VendorActive, StateOf(world, "nvidia"));
            Assert.Equal(GpuState.VendorMerged, StateOf(world, "3dfx"), "3dfx merged into NVIDIA in 2001");
            Assert.Equal("nvidia", world.Rivals.Find("3dfx").Save.mergedInto);
            Assert.Equal(GpuState.VendorPending, StateOf(world, "amd.gpu"), "AMD comes in 2006");
            Assert.Equal(GpuState.VendorPending, StateOf(world, "intel.gpu"), "Intel in 2022");
            Assert.Equal(7, world.Rivals.All.Count, "a record for every vendor of the content");
            Assert.Equal(new[] { "matrox", "ati", "nvidia" }, world.Rivals.Active.Select(v => v.Id).ToArray());

            Assert.Equal(new[] { "gaming.low", "office2d" }, SegmentsOf(Cards(world, "matrox")), "a card in every open segment of its list (consumer is not a segment of the eras)");
            Assert.Equal(new[] { "gaming.high", "gaming.low", "gaming.mid", "office2d" }, SegmentsOf(Cards(world, "ati")));
            Assert.Equal(new[] { "gaming.high", "gaming.low", "gaming.mid" }, SegmentsOf(Cards(world, "nvidia")), "compute opens in 2007");
            Assert.Equal(0, Cards(world, "s3").Count + Cards(world, "3dfx").Count + Cards(world, "amd.gpu").Count, "nobody else has cards");

            Assert.Equal(0, heard.Entered.Count + heard.Exited.Count + heard.Merged.Count, "the seed is the history before the game: no events");
            Assert.Equal(0, world.Game.Spent.Count + world.Game.Earned.Count, "and no money");
            Assert.Equal(9, world.Rivals.CardsOnSale);

            // the state passes the checks of a load without a word
            var check = new GpuState(world.Content);
            check.Sanitize(world.State.Data);
            Assert.Equal(0, check.Warnings.Count, string.Join("; ", new List<string>(check.Warnings).ToArray()));
        }

        [Test]
        public void TheSeed_MakesGoodCards_WithTheNamesPricesAndDatesOfAVendor()
        {
            GpuWorld world = World(new DateTime(2003, 6, 1));
            world.Market.SeedForYear(2003);

            foreach (GpuVendorRuntime vendor in world.Rivals.Active)
            {
                foreach (GpuProductSave card in Cards(world, vendor.Id))
                {
                    Assert.Equal(GpuState.OwnerVendor, card.owner);
                    Assert.Equal(vendor.Id, card.vendorId);
                    Assert.Equal(GpuState.StatusSelling, card.status);
                    Assert.True(card.name.StartsWith(vendor.Def.series + " ", StringComparison.Ordinal), card.name + " is <series> <n>");
                    Assert.True(card.stats.effectivePerf > 0d && card.stats.tempC <= 85.01f, card.name + ": a card that works");
                    Assert.True(card.price >= 1.3f * card.stats.unitCost - 0.01f, card.name + ": F23, the price is at least 1.3 x the cost");
                    Assert.True(card.stats.unitCost > 0f, "the cost of the card is kept for the books of a bound vendor");
                    Assert.Equal(1, card.segIds.Count);
                    Assert.Equal(1, card.pop.Count);
                    Assert.True(card.pop[0] >= world.Content.Balance.popularity.start && card.pop[0] <= 1f);
                    Assert.True(card.vramMB > 0f && card.die > 0f && card.busWidth > 0);
                    Assert.Equal(0, card.stock, "the stock comes with the first day");
                    Assert.True(world.Game.Nodes.ContainsKey(card.node), card.name + ": a node of the game, " + card.node);

                    int age = GpuMarketService.MonthsBetween(card.released, world.Game.Date);
                    Assert.True(age >= 0 && age < vendor.Def.designIntervalMonths, card.name + ": launched within the interval of the vendor, " + age + " months ago");
                }
            }

            // the strength of a vendor is its first popularity: NVIDIA (1.0) is better known than Matrox (0.6)
            float nvidia = Cards(world, "nvidia")[0].pop[0];
            float matrox = Cards(world, "matrox")[0].pop[0];
            float known = world.Content.Balance.background.popularity;
            Assert.Near(1.0 * known, nvidia, 1e-5, "strength 1.0 x background.popularity");
            Assert.Near(0.6 * known, matrox, 1e-5, "strength 0.6 x background.popularity");
        }

        [Test]
        public void TheSeed_IsDoneOnce_AndTheVendorsAreSeededWithTheMarket()
        {
            GpuWorld world = World(new DateTime(1996, 1, 1));
            world.Market.SeedForYear(1996);
            int cards = world.Rivals.CardsOnSale;
            Assert.True(cards > 0);

            world.Market.SeedForYear(1996);   // a second call does nothing: the market was seeded
            world.Rivals.StartSession();      // and a session finds the vendors in place
            Assert.Equal(cards, world.Rivals.CardsOnSale);
            Assert.Equal(7, world.Rivals.All.Count);
        }

        [Test]
        public void TheSeedOfANewGame_WhoseCompaniesAreNotReady_IsCompletedWhenTheGameIsReady()
        {
            GpuWorld world = World(new DateTime(2003, 1, 1));
            world.Game.MaxFreqKHz = 0f;      // the fast-forward is over, the companies are not there yet: no frontier frequency
            world.Game.FrontierKHz = 0f;
            var heard = new Heard(world.Rivals);
            world.Market.SeedForYear(2003);

            Assert.Equal(GpuState.VendorActive, StateOf(world, "nvidia"), "the vendors of the year are noted");
            Assert.Equal(0, world.Rivals.CardsOnSale, "but their cards cannot be made yet");
            Assert.Equal(0, world.Warnings.Count(w => w.StartsWith("rivals", StringComparison.Ordinal)), "and the vendors say nothing about it: it is expected");

            world.Game.MaxFreqKHz = 250000f;
            world.Game.FrontierKHz = 250000f;
            world.Rivals.StartSession();      // the game is ready
            Assert.Equal(9, world.Rivals.CardsOnSale, "the cards of the seed are made now");
            Assert.Equal(0, heard.Entered.Count + heard.Exited.Count, "without events");
            Assert.Equal(0, world.Game.Spent.Count, "and without money");

            world.Rivals.StartSession();      // once
            Assert.Equal(9, world.Rivals.CardsOnSale);
        }

        // ---- the entry, the exit, the merger (FR-12 items 2-3) ----

        [Test]
        public void Ac121_In1996_3dfxEnters_WithCardsInTheTwoGamingSegments()
        {
            GpuWorld world = World(new DateTime(1990, 1, 1));
            world.Market.SeedForYear(1990);
            var heard = new Heard(world.Rivals);
            Assert.Equal(GpuState.VendorPending, StateOf(world, "3dfx"));
            Assert.Equal(new[] { "matrox", "ati", "s3" }, world.Rivals.Active.Select(v => v.Id).ToArray(), "1990: the makers of the time");

            MonthsUntil(world, new DateTime(1995, 12, 1));
            Assert.Equal(new[] { "nvidia" }, heard.Entered.ToArray(), "NVIDIA came in 1995");
            Assert.Equal(0, Cards(world, "nvidia").Count, "with no gaming segment open yet, it has nothing to sell");
            Assert.Equal(0, Cards(world, "3dfx").Count);

            MonthsUntil(world, new DateTime(1996, 1, 1));
            Assert.Equal(new[] { "nvidia", "3dfx" }, heard.Entered.ToArray(), "AC-12.1: on 1 January 1996 3dfx comes");
            Assert.Equal(GpuState.VendorActive, StateOf(world, "3dfx"));
            Assert.Equal(new[] { "gaming.high", "gaming.mid" }, SegmentsOf(Cards(world, "3dfx")), "with a card in each of its open segments");
            Assert.Equal(new[] { "gaming.high", "gaming.mid" }, SegmentsOf(Cards(world, "nvidia")), "and so has NVIDIA: the segments opened");
            Assert.Equal(new[] { "Voodoo 1", "Voodoo 2" }, Cards(world, "3dfx").Select(c => c.name).OrderBy(n => n, StringComparer.Ordinal).ToArray(), "<series> <launch count>");
            foreach (GpuProductSave card in Cards(world, "3dfx"))
            {
                Assert.True(Array.IndexOf(card.stats.features.ToArray(), "3d") >= 0, "a 3D card");
                Assert.Equal(string.Empty, card.companyKey, "a virtual vendor: nobody's company");
            }
        }

        [Test]
        public void TheVendors_LeaveInTheirYear_AndTheirCardsGoOffTheMarket()
        {
            GpuWorld world = World(new DateTime(2002, 6, 1));
            world.Market.SeedForYear(2002);
            var heard = new Heard(world.Rivals);
            Assert.True(Cards(world, "s3").Count > 0);

            MonthsUntil(world, new DateTime(2003, 1, 1));
            Assert.Equal(new[] { "s3" }, heard.Exited.ToArray(), "exitYear 2003");
            Assert.Equal(GpuState.VendorExited, StateOf(world, "s3"));
            Assert.Equal(0, Cards(world, "s3").Count, "its cards are taken off");
            Assert.Equal(0, world.State.Data.products.Count(p => p.vendorId == "s3"), "and not kept in an archive");

            MonthsUntil(world, new DateTime(2005, 1, 1));
            Assert.Equal(new[] { "s3", "matrox" }, heard.Exited.ToArray(), "Matrox in 2005");
        }

        [Test]
        public void Ac122_In2006_AtiMergesIntoAmd_TheCardsPassWithTheirPopularity_AndTheirRevenueGoesToAmd()
        {
            GpuWorld world = World(new DateTime(2005, 6, 1));
            world.Market.SeedForYear(2005);
            var heard = new Heard(world.Rivals);
            MonthsUntil(world, new DateTime(2005, 12, 1));
            List<GpuProductSave> before = Cards(world, "ati");
            Assert.True(before.Count >= 4, "ATI has a card in each of its segments");
            GpuProductSave favourite = before.Last(c => c.segIds[0] == "gaming.mid");   // the newest: AMD launches its own at once, and the oldest of three goes
            favourite.pop[0] = 0.77f;
            string favouriteId = favourite.id;

            MonthsUntil(world, new DateTime(2006, 1, 1));

            Assert.Equal(new[] { "amd.gpu" }, heard.Entered.ToArray(), "AMD enters in 2006");
            Assert.Equal(new[] { "ati>amd.gpu" }, heard.Merged.ToArray(), "and ATI merges into it");
            Assert.Equal(GpuState.VendorMerged, StateOf(world, "ati"));
            Assert.Equal("amd.gpu", world.Rivals.Find("ati").Save.mergedInto);
            Assert.Equal(0, Cards(world, "ati").Count, "no card is ATI's any more");

            GpuProductSave moved = world.State.Data.products.Find(p => p.id == favouriteId);
            Assert.NotNull(moved, "the card is still on the market");
            Assert.Equal("amd.gpu", moved.vendorId, "AC-12.2: it belongs to AMD");
            Assert.Equal("ai:1", moved.companyKey, "and its money to the company AMD");
            Assert.Equal(0.77f, moved.pop[0], "with its popularity");
            Assert.True(moved.name.StartsWith("Radeon", StringComparison.Ordinal), "and its name");

            // the revenue of the card is the revenue of the company AMD
            moved.stock = 100000;
            moved.pop[0] = 0.77f;
            world.Market.SellDay();
            float earned = world.Game.EarnedOn(GpuRevenue.Sales, "ai:1");
            Assert.True(earned > 0f, "AMD (ai:1) was paid for the sales: " + earned);
            Assert.Equal(0f, world.Game.EarnedOn(GpuRevenue.Sales, "player"), "not the player");
            Assert.Equal(0f, world.Game.EarnedOn(GpuRevenue.Sales, "ai:0"), "not Intel");

            // the series goes on: AMD numbers its cards after ATI's
            int numberOfMoved = int.Parse(moved.name.Substring("Radeon ".Length));
            Assert.True(world.Rivals.Find("amd.gpu").Save.launchCount >= numberOfMoved - 1 || numberOfMoved > 0, "the Radeon series continues");
        }

        [Test]
        public void The3dfx_MergesIntoNvidiaIn2001_AndItsCardsAreNvidiasWithTheirPopularity()
        {
            GpuWorld world = World(new DateTime(2000, 6, 1));
            world.Market.SeedForYear(2000);
            var heard = new Heard(world.Rivals);
            GpuProductSave voodoo = Cards(world, "3dfx").First(c => c.segIds[0] == "gaming.high");
            voodoo.pop[0] = 0.66f;
            string id = voodoo.id;
            string name = voodoo.name;

            MonthsUntil(world, new DateTime(2001, 1, 1));

            Assert.Equal(new[] { "3dfx>nvidia" }, heard.Merged.ToArray());
            Assert.Equal(GpuState.VendorMerged, StateOf(world, "3dfx"));
            GpuProductSave moved = world.State.Data.products.Find(p => p.id == id);
            Assert.NotNull(moved, "the best card of 3dfx stays on the market");
            Assert.Equal("nvidia", moved.vendorId);
            Assert.Equal(name, moved.name, "under its own name");
            Assert.Equal(0.66f, moved.pop[0]);
            Assert.Equal(string.Empty, moved.companyKey, "NVIDIA is a virtual vendor");
            Assert.Equal(MarketItem.VendorPrefix + "nvidia", GpuMarketService.OwnerKeyOf(moved), "the buyers it shares are NVIDIA's now");
        }

        [Test]
        public void TheCardsOfAnAbsorbedVendor_AreHeldToTheLimitOfTheSegment()
        {
            GpuWorld world = World(new DateTime(2000, 6, 1));
            world.Market.SeedForYear(2000);
            MonthsUntil(world, new DateTime(2001, 1, 1));
            foreach (GpuSegmentDto seg in world.Eras.OpenAt(world.Game.Date))
            {
                int n = Cards(world, "nvidia").Count(c => c.segIds[0] == seg.id);
                Assert.True(n <= world.Content.Balance.rivals.maxProductsPerSegment, "NVIDIA has " + n + " cards in " + seg.id);
            }
        }

        // ---- the binding to the companies of the game (FR-12 items 1, 4; AC-12.3, AC-12.4) ----

        [Test]
        public void ABoundVendor_TakesTheKeyOfItsCompany_AndTheNameFromTheGame()
        {
            GpuWorld world = World(new DateTime(2008, 1, 1));
            world.Game.Companies["ai:1"].Name = "Advanced Micro Devices";
            world.Market.SeedForYear(2008);

            GpuVendorRuntime amd = world.Rivals.Find("amd.gpu");
            Assert.True(amd.IsActive && amd.IsBound);
            Assert.Equal("ai:1", amd.CompanyKey);
            Assert.Equal("ai:1", world.Rivals.ResolveBinding(amd.Def));
            Assert.Equal("Advanced Micro Devices", world.Rivals.NameOf(amd, true), "the name of a bound vendor is the name the game gives its company");
            Assert.Equal("Advanced Micro Devices", world.Rivals.NameOf(amd, false));
            foreach (GpuProductSave card in Cards(world, "amd.gpu")) Assert.Equal("ai:1", card.companyKey);

            GpuVendorRuntime nvidia = world.Rivals.Find("nvidia");
            Assert.Null(world.Rivals.ResolveBinding(nvidia.Def), "a virtual vendor has no company");
            Assert.False(nvidia.IsBound);
            Assert.Equal("NVIDIA", world.Rivals.NameOf(nvidia, true));
            Assert.Equal("NVIDIA", world.Rivals.NameOf(nvidia, false));
            Assert.Equal("3dfx", world.Rivals.NameOf(world.Rivals.Find("3dfx"), true));
            Assert.Equal(string.Empty, world.Rivals.NameOf(null, true));

            Assert.Null(world.Rivals.Find("nobody"));
            Assert.Null(world.Rivals.ResolveBinding(null));
        }

        [Test]
        public void Ac123_WhenTheCompanyOfAVendorWithoutAFallbackGoesBankrupt_TheVendorLeaves()
        {
            GpuWorld world = World(new DateTime(2023, 1, 1));
            world.Market.SeedForYear(2023);
            var heard = new Heard(world.Rivals);
            Assert.Equal(GpuState.VendorActive, StateOf(world, "intel.gpu"));
            Assert.True(Cards(world, "intel.gpu").Count > 0, "Intel has cards in 2023");
            Assert.False(world.Content.Vendor("intel.gpu").fallbackVirtual);

            world.Rivals.CheckBoundCompanies();
            Assert.Equal(0, heard.Exited.Count, "a healthy company: nothing happens");

            world.Game.Companies["ai:0"].Bankrupt = true;   // in 2024
            world.Game.Date = new DateTime(2024, 3, 3);
            world.Rivals.CheckBoundCompanies();             // TickPhase.DayPassed

            Assert.Equal(new[] { "intel.gpu" }, heard.Exited.ToArray(), "AC-12.3: the vendor leaves");
            Assert.Equal(GpuState.VendorExited, StateOf(world, "intel.gpu"));
            Assert.Equal(0, Cards(world, "intel.gpu").Count, "its cards are taken off the market");
            Assert.True(Cards(world, "amd.gpu").Count > 0, "the others stay");

            world.Rivals.CheckBoundCompanies();
            Assert.Equal(1, heard.Exited.Count, "once");
        }

        [Test]
        public void AVendorWithAFallback_WhoseCompanyGoesBankrupt_BecomesAVirtualOne()
        {
            GpuWorld world = World(new DateTime(2008, 1, 1));
            world.Market.SeedForYear(2008);
            var heard = new Heard(world.Rivals);
            Assert.True(world.Content.Vendor("amd.gpu").fallbackVirtual);
            int cards = Cards(world, "amd.gpu").Count;
            Assert.True(cards > 0);

            world.Game.Companies["ai:1"].Bankrupt = true;
            world.Rivals.CheckBoundCompanies();

            Assert.Equal(0, heard.Exited.Count, "FR-12 item 4: it does not leave, it becomes virtual");
            GpuVendorRuntime amd = world.Rivals.Find("amd.gpu");
            Assert.True(amd.IsActive);
            Assert.False(amd.IsBound);
            Assert.Equal(cards, Cards(world, "amd.gpu").Count, "the cards stay");
            foreach (GpuProductSave card in Cards(world, "amd.gpu")) Assert.Equal(string.Empty, card.companyKey, "and are nobody's company's: no money to a bankrupt company");
            Assert.Equal("AMD", world.Rivals.NameOf(amd, true), "under the name of the content");
        }

        [Test]
        public void Ac124_WithoutTheCompany_AVendorWithAFallback_EntersAsAVirtualOne()
        {
            GpuWorld world = World(new DateTime(2005, 6, 1), false, true);   // no AMD in this game
            world.Market.SeedForYear(2005);
            var heard = new Heard(world.Rivals);
            Assert.False(world.Game.CompanyExists("ai:1"));

            MonthsUntil(world, new DateTime(2006, 1, 1));

            Assert.Equal(new[] { "amd.gpu" }, heard.Entered.ToArray(), "AC-12.4");
            GpuVendorRuntime amd = world.Rivals.Find("amd.gpu");
            Assert.True(amd.IsActive);
            Assert.False(amd.IsBound, "a virtual vendor");
            Assert.Equal("AMD", world.Rivals.NameOf(amd, true), "under the name \"AMD\"");
            Assert.True(Cards(world, "amd.gpu").Count > 0, "with cards");
            foreach (GpuProductSave card in Cards(world, "amd.gpu")) Assert.Equal(string.Empty, card.companyKey);
            Assert.Equal(0, world.Game.Spent.Count(m => m.Company == "ai:1") + world.Game.Earned.Count(m => m.Company == "ai:1"), "the finances of an AI company are not touched");
            Assert.Equal(0, world.Game.Companies.Count(c => c.Key == "ai:1"), "and no company was created");
        }

        [Test]
        public void AVendorWithoutAFallback_WaitsForItsCompany_AndEntersWhenItIsThere()
        {
            GpuWorld world = World(new DateTime(2021, 6, 1), true, false);   // no Intel
            world.Market.SeedForYear(2021);
            var heard = new Heard(world.Rivals);

            MonthsUntil(world, new DateTime(2022, 6, 1));
            Assert.Equal(GpuState.VendorPending, StateOf(world, "intel.gpu"), "bound, with no fallback, and the company is not there: it waits");
            Assert.Equal(0, heard.Entered.Count);
            Assert.Equal(1, world.Warnings.Count(w => w.Contains("intel.gpu")), "one line in the log, not one a month");

            world.Game.AddCompany("ai:0", "Intel");
            MonthsUntil(world, new DateTime(2022, 7, 1));
            Assert.Equal(new[] { "intel.gpu" }, heard.Entered.ToArray());
            Assert.Equal("ai:0", world.Rivals.Find("intel.gpu").CompanyKey);
        }

        [Test]
        public void ASeedWithoutTheCompanyOfAVendor_IsFixedWhenTheGameIsReady()
        {
            GpuWorld world = World(new DateTime(2023, 1, 1), false, false);   // neither AMD nor Intel
            world.Market.SeedForYear(2023);
            var heard = new Heard(world.Rivals);
            // the seed binds a vendor as it enters; the companies are looked at when the game is ready
            Assert.Equal("ai:0", world.Rivals.Find("intel.gpu").CompanyKey);

            world.Rivals.StartSession();
            Assert.Equal(GpuState.VendorExited, StateOf(world, "intel.gpu"), "no fallback: Intel is not on the market");
            Assert.Equal(new[] { "intel.gpu" }, heard.Exited.ToArray());
            Assert.True(world.Rivals.Find("amd.gpu").IsActive && !world.Rivals.Find("amd.gpu").IsBound, "AMD is virtual");
        }

        // ---- the switch "Соперники" (FR-12 item 5) ----

        [Test]
        public void WithTheSwitchOff_OnlyTheBackgroundIsOnTheMarket()
        {
            GpuWorld world = World(new DateTime(2003, 6, 1), true, true, 42, false);
            world.Market.SeedForYear(2003);
            Assert.Equal(0, world.Rivals.All.Count, "no vendors, no records");
            Assert.Equal(0, world.State.Data.products.Count(p => p.owner == GpuState.OwnerVendor));
            Assert.True(world.State.Data.products.Exists(p => p.owner == GpuState.OwnerBackground), "the background is there");

            world.Rivals.MonthTick();
            world.Rivals.RefillStock();
            world.Rivals.CheckBoundCompanies();
            Assert.Equal(0, world.Rivals.CardsOnSale);
            foreach (GpuSegmentDto seg in world.Market.OpenSegmentsAt(world.Game.Date))
            {
                foreach (GpuMarketEntry entry in world.Market.TopProducts(seg.id, 10)) Assert.Equal(MarketItem.BackgroundOwner, entry.OwnerKey, "the tab lists only the others");
            }
        }

        [Test]
        public void TurnedOnLater_TheVendorsAppearOnTheNextMonth_WithoutLettersAboutThePast()
        {
            GpuWorld world = World(new DateTime(2003, 6, 1), true, true, 42, false);
            world.Market.SeedForYear(2003);
            var heard = new Heard(world.Rivals);

            world.RivalsOn = true;
            world.Game.Date = new DateTime(2003, 7, 1);
            world.MonthTicks();

            Assert.Equal(new[] { "matrox", "ati", "nvidia" }, world.Rivals.Active.Select(v => v.Id).ToArray(), "the vendors of the year");
            Assert.Equal(9, world.Rivals.CardsOnSale);
            Assert.Equal(0, heard.Entered.Count + heard.Exited.Count + heard.Merged.Count, "the past is not announced");
        }

        [Test]
        public void TurnedOff_TheCardsOfTheVendorsStay_ButDoNotSell()
        {
            GpuWorld world = World(new DateTime(2003, 6, 1));
            world.Market.SeedForYear(2003);
            world.Rivals.RefillStock();
            int cards = world.Rivals.CardsOnSale;
            long stock = world.State.Data.products.Where(p => p.owner == GpuState.OwnerVendor).Sum(p => (long)p.stock);
            Assert.True(stock > 0, "the stock is filled");

            world.RivalsOn = false;
            world.Rivals.RefillStock();
            world.Market.SellDay();
            world.Market.DayTick();
            Assert.Equal(cards, world.Rivals.CardsOnSale, "the cards are in the section");
            Assert.Equal(stock, world.State.Data.products.Where(p => p.owner == GpuState.OwnerVendor).Sum(p => (long)p.stock), "and nothing was sold");
            Assert.Equal(0f, world.Market.ShareOf("vendor:nvidia"));

            world.RivalsOn = true;
            world.Market.SellDay();
            world.Market.DayTick();
            Assert.True(world.State.Data.products.Where(p => p.owner == GpuState.OwnerVendor).Sum(p => (long)p.stock) < stock, "on again, they sell");
        }

        // ---- the save (FR-9) ----

        [Test]
        public void TheStatesOfTheVendors_AreRestoredAfterALoad()
        {
            GpuWorld world = World(new DateTime(2004, 1, 1));
            world.Market.SeedForYear(2004);
            MonthsUntil(world, new DateTime(2007, 1, 1));
            world.Rivals.RefillStock();
            string text = ModJson.Write(world.State.Write(), false);

            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "JSON: " + error);
            var loaded = new GpuState(world.Content);
            loaded.Read((JsonObject)node, GpuState.SchemaVersion);
            Assert.Equal(0, loaded.Warnings.Count, "the section loads without a word:\n  " + string.Join("\n  ", loaded.Warnings.ToArray()));
            Assert.Equal(text, ModJson.Write(loaded.Write(), false), "write, read and write again give the same section");

            // a fresh session on the loaded section: the same vendors in the same states, no events, no spending
            var next = new GpuWorld(new DateTime(2007, 1, 1), 42, world.Content);
            next.Game.AddTimelineNodes();
            next.Game.FrontierKHz = 250000f;
            next.Game.AddCompany("ai:1", "AMD");
            next.Game.AddCompany("ai:0", "Intel");
            next.RivalsOn = true;
            next.State.Read((JsonObject)node, GpuState.SchemaVersion);
            var heard = new Heard(next.Rivals);
            next.Rivals.StartSession();

            Assert.Equal(world.Rivals.All.Select(v => v.Id + ":" + v.State + ":" + v.CompanyKey + ":" + v.Save.launchCount).ToArray(),
                next.Rivals.All.Select(v => v.Id + ":" + v.State + ":" + v.CompanyKey + ":" + v.Save.launchCount).ToArray(), "the states of the vendors");
            Assert.Equal(world.Rivals.CardsOnSale, next.Rivals.CardsOnSale, "the cards");
            Assert.Equal(0, heard.Entered.Count + heard.Exited.Count + heard.Merged.Count, "no events on a load");
            Assert.Equal(0, next.Game.Spent.Count, "no spending");
            Assert.True(next.Rivals.Find("ati").State == GpuState.VendorMerged && next.Rivals.Find("amd.gpu").IsActive, "the merger of 2006 is remembered");
        }

        [Test]
        public void ACardOfAVendorTheContentLost_IsTakenOffTheMarket()
        {
            GpuWorld world = World(new DateTime(2004, 1, 1));
            world.Market.SeedForYear(2004);
            world.State.Data.products.Add(new GpuProductSave
            {
                id = "g-900", owner = GpuState.OwnerVendor, vendorId = "gone", status = GpuState.StatusSelling, price = 100f, sell = true, stock = 10,
                stats = new GpuStatsSave { effectivePerf = 5d }, segIds = new List<string> { "gaming.mid" }, pop = new List<float> { 0.5f },
            });
            world.Game.Date = new DateTime(2004, 2, 1);
            world.MonthTicks();
            Assert.Null(world.State.Data.products.Find(p => p.id == "g-900"));
        }

        [Test]
        public void TheCardsOfASegmentThatClosed_AreTakenOff()
        {
            GpuWorld world = World(new DateTime(2009, 6, 1));
            world.Market.SeedForYear(2009);
            Assert.True(Cards(world, "ati").Count == 0 && Cards(world, "amd.gpu").Any(c => c.segIds[0] == "office2d" || true), "AMD holds the cards of the merger");
            int office = world.State.Data.products.Count(p => p.owner == GpuState.OwnerVendor && p.segIds[0] == "office2d");

            MonthsUntil(world, new DateTime(2010, 1, 1));
            Assert.Equal(0, world.State.Data.products.Count(p => p.owner == GpuState.OwnerVendor && p.segIds[0] == "office2d"), "no vendor card is left in the office segment (it had " + office + ")");
        }

        [Test]
        public void ACardOfAVendor_CompetesInItsOwnSegmentOnly_ACardOfThePlayerInEvery()
        {
            GpuWorld world = World(new DateTime(2004, 1, 1));
            world.Market.SeedForYear(2004);
            GpuProductSave mine = world.AddCard("g-p", 0);
            mine.segIds = new List<string> { "gaming.mid" };
            mine.pop = new List<float> { 0.3f };
            world.Rivals.RefillStock();
            world.Market.SellDay();
            world.Market.DayTick();

            foreach (GpuProductSave card in world.State.Data.products.Where(p => p.owner == GpuState.OwnerVendor || p.owner == GpuState.OwnerBackground))
                Assert.Equal(1, card.segIds.Count, card.name + " keeps its one segment: " + string.Join(",", card.segIds.ToArray()));
            Assert.Equal(4, mine.segIds.Count, "the card of the player joined the other three open segments");

            DateTime today = world.Game.Date;
            List<SegmentRuntime> segs = world.Market.BuildSegments(today);
            foreach (MarketItem item in world.Market.BuildItems(segs, today))
            {
                if (!item.OwnerKey.StartsWith(MarketItem.VendorPrefix, StringComparison.Ordinal)) continue;
                int competes = 0;
                for (int k = 0; k < segs.Count; k++)
                {
                    if (item.Popularity[k] > 0f) competes++;
                }
                Assert.Equal(1, competes, item.ProductId + " competes in one segment");
            }
        }

        [Test]
        public void TheNewFieldsOfTheSection_AreCheckedLikeTheOthers()
        {
            GpuWorld world = World(new DateTime(2004, 1, 1));
            var section = new GpuSaveSection();
            section.vendors.Add(new GpuVendorSave
            {
                id = "nvidia", state = GpuState.VendorActive,
                lastLaunch = new List<GpuSegLaunch>
                {
                    new GpuSegLaunch { seg = "gaming.high", date = new GpuDate { y = 2003, m = 5, d = 1 }, extraMonths = 99 },
                    new GpuSegLaunch { seg = "gaming.mid", date = new GpuDate { y = 2003, m = 6, d = 1 }, extraMonths = -2 },
                    new GpuSegLaunch { seg = "gaming.low", date = new GpuDate { y = 2003, m = 7, d = 1 }, extraMonths = 2 },
                },
            });
            section.products.Add(new GpuProductSave
            {
                id = "g-1", owner = GpuState.OwnerVendor, vendorId = "nvidia", status = GpuState.StatusSelling, stats = new GpuStatsSave { unitCost = -5f },
            });
            section.products.Add(new GpuProductSave
            {
                id = "g-2", owner = GpuState.OwnerVendor, vendorId = "nvidia", status = GpuState.StatusSelling, stats = new GpuStatsSave { unitCost = float.NaN },
            });
            section.products.Add(new GpuProductSave
            {
                id = "g-3", owner = GpuState.OwnerVendor, vendorId = "nvidia", status = GpuState.StatusSelling, stats = new GpuStatsSave { unitCost = 12.5f },
            });
            section.market.erasCheckedOn = new GpuDate { y = 2003, m = 2, d = 30 };

            var state = new GpuState(world.Content);
            state.Sanitize(section);

            Assert.Equal(new[] { 24, 0, 2 }, section.vendors[0].lastLaunch.Select(l => l.extraMonths).ToArray(), "0-3 is what the content draws; a hand-made number is cut");
            Assert.Equal(new[] { 0f, 0f, 12.5f }, section.products.Select(p => p.stats.unitCost).ToArray(), "the cost of a card is not negative and a number");
            Assert.Null(section.market.erasCheckedOn, "30 February is no date");
            Assert.True(state.Warnings.Count >= 4, "each change is told: " + string.Join("; ", state.Warnings.ToArray()));
        }

        // ---- no companies, no lines, no subscriptions (FR-12 item 6, D3) ----

        [Test]
        public void TheVendors_CreateNoCompanies_AndReserveNoLinesAtAnyFactory()
        {
            var sim = new GpuSimulation(new SimPlan
            {
                Name = "rivals only", StartYear = 2000, EndYear = 2003, FoundYear = 0, Rivals = true, Content = GpuWorld.ShippedEras(), CapacityLines = 60, CpuLines = 20,
            });
            int companies = sim.Game.Companies.Count;
            sim.Run();

            Assert.Equal(companies, sim.Game.Companies.Count, "the AI companies are the game's; the virtual vendors are not companies");
            Assert.Equal(0, sim.Game.Factory.Reservations, "a vendor reserves no lines (D3)");
            Assert.True(sim.World.Rivals.CardsOnSale > 0 && sim.VendorLaunches.Count > 0, "and still sells and launches");
            foreach (MoneyMove move in sim.Game.Spent.Concat(sim.Game.Earned))
                Assert.True(move.Company == "player" || move.Company == "ai:0" || move.Company == "ai:1", "money of an unknown company: " + move.Company);
        }

        [Test]
        public void TheRivalsCode_CreatesNoCompanyOfTheGame_AndHearsNoEventOfTheGame()
        {
            string folder = Path.Combine(GpuFiles.FeatureSourceDir, "Logic", "Rivals");
            var hits = new List<string>();
            foreach (string path in SourceScan.Files(folder, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in Regex.Matches(code, @"\bAICompany\b|\bSpawnCompany\b|\bCompanySpawner\b|\bnew\s+Company\b|\bBankruptcyHandler\b|\bOnCompanyWentBankrupt\b|\bTotalLinesAllocated\b|\bFactory\b"))
                    hits.Add(SourceScan.Relative(folder, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
            }
            Assert.True(hits.Count == 0, "the vendors are plain data: they create no company, touch no factory and subscribe to nothing:\n  " + string.Join("\n  ", hits));
        }
    }
}
