using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Modifiers;
using PTMod.Features.GpuProductLine;
using PTMod.Tests.Architecture;
using PTMod.Tests.Conditions;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The face of the feature for the other features (TSK-043, TSK-044; FR-18, FR-12): the questions of <c>GpuApi</c> on company keys (the division, the cards, the share, the synergy, the segments, a
    /// research node) and their neutral answers while the feature is off; the transfer of a vendor's lineup to the player when its company is bought (AC-18.3: the cards come with their stock and
    /// popularity, 0 lines, the vendor is merged; AC-18.4: no division - it is founded for nothing on the date of the deal) and its refusals; the event of a released card; the four predicates of
    /// <c>ModConditions</c> with their parameters, their errors, their names (the convention of the core) and their answers on the boundary values (AC-18.2: 25 % of the market); the channel
    /// <c>gpu.demand</c> that raises the buyers of every segment for 90 days (AC-18.1).
    /// </summary>
    [Covers(typeof(GpuApiService), typeof(IGpuQueries), typeof(GpuPredicates), typeof(GpuPredicateNames))]
    public class GpuApiTests
    {
        private const string Player = FakeGpuGame.Player;
        private static readonly DateTime March2007 = new DateTime(2007, 3, 1);

        // ---- the world ----

        /// <summary>2007 in the market of the eras: AMD (<c>amd.gpu</c>, bound to <c>ai:1</c>) has cards in its segments, Intel (<c>ai:0</c>) is not on the market yet; the division is founded when asked.</summary>
        private static GpuWorld Eras2007(bool division = true)
        {
            var world = new GpuWorld(March2007, 42, GpuWorld.ShippedEras());
            world.Game.AddTimelineNodes();
            world.Game.FrontierKHz = 250000f;
            world.RivalsOn = true;
            world.Game.AddCompany("ai:1", "AMD");
            world.Game.AddCompany("ai:0", "Intel");
            if (division) world.Founded();
            world.Market.SeedForYear(2007);
            return world;
        }

        private static GpuProductSave PlayerCard(GpuWorld world, string id, bool sell = true, string status = GpuState.StatusSelling)
        {
            var card = new GpuProductSave
            {
                id = id, designId = string.Empty, owner = GpuState.OwnerPlayer, vendorId = string.Empty, companyKey = Player, name = "Card " + id,
                status = status, price = 250f, sell = sell, stock = 40, node = "90nm", die = 200f, busWidth = 128, vramMB = 128f,
                stats = new GpuStatsSave { effectivePerf = 1234d, powerW = 55f, features = new List<string> { "3d" } },
                released = new GpuDate { y = 2007, m = 2, d = 1 }, segIds = new List<string> { "gaming.mid" }, pop = new List<float> { 0.4f },
            };
            world.State.Data.products.Add(card);
            return card;
        }

        private static List<GpuProductSave> AmdCards(GpuWorld world)
        {
            return world.Rivals.AI.CardsOf("amd.gpu");
        }

        // ---- the questions ----

        [Test]
        public void TheDivision_IsThePlayersOnceFounded_AndAnAiCompanysWhenAVendorIsBoundToIt()
        {
            GpuWorld world = Eras2007(division: false);
            Assert.False(world.Api.IsDivisionFounded(Player), "not founded yet");
            Assert.True(world.Api.IsDivisionFounded("ai:1"), "AMD has a vendor on the market");
            Assert.False(world.Api.IsDivisionFounded("ai:0"), "Intel has none in 2007");
            Assert.False(world.Api.IsDivisionFounded("ai:99"), "a company that is not in the game");
            Assert.False(world.Api.IsDivisionFounded(null));
            Assert.False(world.Api.IsDivisionFounded(string.Empty));

            world.Division.FoundFree();
            Assert.True(world.Api.IsDivisionFounded(Player));
        }

        [Test]
        public void TheCardsOfACompany_AreItsCardsThatAreNotRetired_AsViews()
        {
            GpuWorld world = Eras2007();
            GpuProductSave a = PlayerCard(world, "g-9001");
            PlayerCard(world, "g-9002", status: GpuState.StatusRetired);
            PlayerCard(world, "g-9003", status: GpuState.StatusReady);

            IReadOnlyList<GpuProductView> mine = world.Api.ProductsOf(Player);
            Assert.Equal(new[] { "g-9001", "g-9003" }, mine.Select(v => v.Id).ToArray(), "the retired card is not listed");
            GpuProductView view = mine[0];
            Assert.Equal("Card g-9001", view.Name);
            Assert.Equal(GpuState.StatusSelling, view.Status);
            Assert.Equal(Player, view.OwnerKey);
            Assert.Equal(250f, view.Price);
            Assert.Equal(40, view.Stock);
            Assert.Equal(1234d, view.EffectivePerf);
            Assert.Equal(55f, view.PowerW);
            Assert.Equal(0, view.SoldLastMonth, "no day of sales yet");
            Assert.Equal(new[] { "gaming.mid" }, view.SegmentIds.ToArray());
            Assert.Equal(new[] { 0.4f }, view.Popularity.ToArray());
            Assert.Equal(new DateTime(2007, 2, 1), view.Released.Value);
            view.SegmentIds.Add("x");
            Assert.Equal(1, a.segIds.Count, "the view is a copy");

            IReadOnlyList<GpuProductView> amd = world.Api.ProductsOf("ai:1");
            Assert.Equal(AmdCards(world).Count, amd.Count, "the cards of the vendor bound to AMD");
            Assert.True(amd.Count > 0);
            Assert.True(amd.All(v => v.OwnerKey == "ai:1" && v.Status == GpuState.StatusSelling));
            Assert.Equal(0, world.Api.ProductsOf("ai:0").Count, "Intel has none");
            Assert.Equal(0, world.Api.ProductsOf(null).Count);
            Assert.Equal(0, world.Api.ProductsOf("ai:77").Count);
        }

        [Test]
        public void TheShare_OfThePlayer_IsTheOneInTheSection_AndOfAnAiCompany_ThatOfItsVendors()
        {
            GpuWorld world = Eras2007();
            Assert.Equal(0f, world.Api.MarketShare(Player, 30), "nothing sold");

            world.State.Data.market.playerShareDaily = Enumerable.Repeat(0.25f, 30).ToList();
            Assert.Near(0.25, world.Api.MarketShare(Player, 30), 1e-6, "right after a load: the mean of the saved ring");
            Assert.Near(0.25, world.Api.MarketShare(Player, 90), 1e-6, "a longer window than the ring keeps is the ring");
            Assert.Near(0.25, world.Api.MarketShare(Player, 0), 1e-6, "no window means 30 days");

            world.Game.Companies["ai:1"].CpuShare = 0.2f;
            for (int i = 0; i < 12; i++) world.RunDay();
            float amd = world.Api.MarketShare("ai:1", 7);
            Assert.True(amd > 0f && amd <= 1f, "AMD sells cards: " + amd);
            float sum = 0f;
            foreach (GpuVendorRuntime vendor in world.Rivals.BoundTo("ai:1")) sum += world.Market.ShareOf("vendor:" + vendor.Id, 7);
            Assert.Near(sum, amd, 1e-6, "the sum over the vendors bound to the company");
            Assert.Equal(0f, world.Api.MarketShare("ai:0", 30), "no vendor, no share");
            Assert.Equal(0f, world.Api.MarketShare(null, 30));
        }

        [Test]
        public void TheOpenSegments_AndTheResearch_AreTheOnesOfTheMarketAndOfTheLab()
        {
            GpuWorld world = Eras2007();
            IReadOnlyList<string> ids = world.Api.OpenSegmentIds();
            Assert.Equal(world.Market.OpenSegments.Select(s => s.Id).ToArray(), ids.ToArray());
            Assert.True(ids.Contains("gaming.high") && ids.Contains("gaming.mid") && ids.Contains("gaming.low"), "the segments of 2007: " + string.Join(", ", ids.ToArray()));
            Assert.False(ids.Contains("ai"), "the segment of the AI opens in 2016");
            Assert.Equal(world.Api.OpenSegments().Count, ids.Count, "the views and the ids list the same segments");

            Assert.False(world.Api.IsResearched("arch.3d"));
            world.State.Data.research.completed.Add("arch.3d");
            Assert.True(world.Api.IsResearched("arch.3d"));
            Assert.False(world.Api.IsResearched("arch.nonexistent"));
            Assert.False(world.Api.IsResearched(null));
            Assert.False(world.Api.IsResearched(string.Empty));
        }

        [Test]
        public void TheSynergyView_IsTheOneOfTheService_AndZerosForAnUnknownCompany()
        {
            GpuWorld world = Eras2007();
            PlayerCard(world, "g-9001");
            world.State.Data.synergy.entries.Add(new GpuSynergyEntry
            {
                companyKey = Player, gpuShareDaily = Enumerable.Repeat(0.4f, 30).ToList(), cpuShareDaily = Enumerable.Repeat(0.2f, 30).ToList(),
            });
            world.Synergy.MonthTick();

            GpuSynergyView view = world.Api.Synergy(Player);
            Assert.True(view.Active);
            Assert.Near(0.08, view.CpuBonus, 1e-6);
            Assert.Near(0.05, view.GpuBonus, 1e-6);
            Assert.Near(0.4, view.GpuShare30, 1e-6);
            Assert.Near(0.2, view.CpuShare30, 1e-6);
            Assert.False(world.Api.Synergy("ai:77").Active);
            Assert.False(world.Api.Synergy(null).Active);
        }

        // ---- the feature is off ----

        [Test]
        public void WhileTheFeatureIsOff_EveryQuestionHasTheNeutralAnswer_AndTheTransferDoesNothing()
        {
            GpuWorld world = Eras2007();
            PlayerCard(world, "g-9001");
            world.State.Data.research.completed.Add("arch.3d");
            world.State.Data.market.playerShareDaily = Enumerable.Repeat(0.5f, 30).ToList();
            Assert.True(world.Api.IsAvailable);
            Assert.True(world.Api.IsDivisionFounded(Player) && world.Api.HasProductOnMarket(Player) && world.Api.IsResearched("arch.3d"));

            world.ApiAvailable = false;
            Assert.False(world.Api.IsAvailable);
            Assert.False(world.Api.IsDivisionFounded(Player));
            Assert.False(world.Api.IsDivisionFounded("ai:1"));
            Assert.False(world.Api.HasProductOnMarket(Player));
            Assert.False(world.Api.IsResearched("arch.3d"));
            Assert.Equal(0f, world.Api.MarketShare(Player, 30));
            Assert.Equal(0, world.Api.ProductsOf(Player).Count);
            Assert.Equal(0, world.Api.OpenSegmentIds().Count);
            Assert.Equal(0, world.Api.OpenSegments().Count);
            Assert.False(world.Api.Synergy(Player).Active);

            int amdBefore = AmdCards(world).Count;
            Assert.False(world.Api.TransferVendorProducts("ai:1", Player), "off: refused");
            Assert.Equal(amdBefore, AmdCards(world).Count, "nothing moved");
            Assert.Equal(GpuState.VendorActive, world.Rivals.Find("amd.gpu").State);
        }

        // ---- the transfer (AC-18.3, AC-18.4) ----

        [Test]
        public void Ac183_TheLineupOfTheBoughtCompany_PassesToThePlayer_WithItsStockAndPopularity()
        {
            GpuWorld world = Eras2007();
            world.RunDay();   // the vendors fill their stock before the market sells: the cards have stock and the market moved their popularity
            List<GpuProductSave> cards = AmdCards(world);
            Assert.True(cards.Count >= 3, "AMD has a lineup: " + cards.Count);
            var before = cards.Select(c => new { c.id, c.name, c.price, c.stock, pop = c.pop.ToArray(), seg = c.segIds.ToArray(), perf = c.stats.effectivePerf }).ToList();
            Assert.True(before.Any(c => c.stock > 0), "stock to keep");
            foreach (GpuProductSave c in cards)
            {
                // whatever the card had when its company was bought, the card of the player starts with no lines, no cards a day, no automatic mode, and on sale
                c.lines = 7;
                c.unitsPerDay = 5;
                c.auto = true;
                c.sell = false;
            }
            int productsBefore = world.State.Data.products.Count;

            Assert.True(world.Api.TransferVendorProducts("ai:1", Player));

            Assert.Equal(productsBefore, world.State.Data.products.Count, "the cards change owner where they stand");
            Assert.Equal(0, AmdCards(world).Count, "no card of the vendor is left");
            foreach (var b in before)
            {
                GpuProductSave card = world.State.Data.products.Find(p => p.id == b.id);
                Assert.NotNull(card, b.id);
                Assert.Equal(GpuState.OwnerPlayer, card.owner, b.id);
                Assert.Equal(Player, card.companyKey);
                Assert.Equal(string.Empty, card.vendorId);
                Assert.Equal(GpuState.StatusSelling, card.status, "on sale");
                Assert.True(card.sell);
                Assert.Equal(0, card.lines, "lines 0");
                Assert.Equal(0, card.unitsPerDay);
                Assert.False(card.auto);
                Assert.Equal(b.stock, card.stock, "the stock is kept");
                Assert.Equal(b.pop, card.pop.ToArray(), "so is the popularity");
                Assert.Equal(b.seg, card.segIds.ToArray());
                Assert.Equal(b.name, card.name);
                Assert.Equal(b.price, card.price);
                Assert.Equal(b.perf, card.stats.effectivePerf);
            }

            GpuVendorRuntime amd = world.Rivals.Find("amd.gpu");
            Assert.Equal(GpuState.VendorMerged, amd.State, "AMD is merged");
            Assert.Equal(Player, amd.Save.mergedInto);
            Assert.Equal(string.Empty, amd.CompanyKey, "bound to nothing");
            Assert.False(world.Api.IsDivisionFounded("ai:1"), "no vendor is bound to the company any more");
            Assert.Equal(before.Count, world.Api.ProductsOf(Player).Count);

            var check = new GpuState(world.Content);
            check.Sanitize(world.State.Data);
            Assert.Equal(0, check.Warnings.Count, "the section is sound: " + string.Join("; ", check.Warnings.ToArray()));
        }

        [Test]
        public void TheTransferredCards_SellForThePlayer_AndAmdIsPaidNothingForThem()
        {
            GpuWorld world = Eras2007();
            world.RunDay();
            foreach (GpuProductSave card in AmdCards(world)) card.stock = 5000;   // what the vendor had in stock when the company was bought
            Assert.True(world.Api.TransferVendorProducts("ai:1", Player));
            float amdBefore = world.Game.EarnedOn(GpuRevenue.Sales, "ai:1");
            float playerBefore = world.Game.EarnedOn(GpuRevenue.Sales, Player);

            for (int i = 0; i < 5; i++) world.RunDay();
            Assert.True(world.Game.EarnedOn(GpuRevenue.Sales, Player) > playerBefore, "the cards earn for the player");
            Assert.Equal(amdBefore, world.Game.EarnedOn(GpuRevenue.Sales, "ai:1"), "AMD sells no card any more");
            Assert.True(world.Api.ProductsOf(Player).Any(v => v.SoldLastMonth > 0), "sold over the last days");
        }

        [Test]
        public void Ac184_WithoutADivision_ItIsFoundedForNothing_OnTheDateOfTheDeal()
        {
            GpuWorld world = Eras2007(division: false);
            world.RunDay();
            Assert.False(world.Division.IsFounded);
            float cash = world.Game.Cash;
            int spent = world.Game.Spent.Count;
            DateTime deal = world.Game.Date;

            Assert.True(world.Api.TransferVendorProducts("ai:1", Player));

            Assert.True(world.Division.IsFounded, "founded");
            Assert.Equal(deal.Year, world.Division.FoundedOn.y);
            Assert.Equal(deal.Month, world.Division.FoundedOn.m);
            Assert.Equal(deal.Day, world.Division.FoundedOn.d, "on the date of the deal");
            Assert.Equal(cash, world.Game.Cash, "for nothing");
            Assert.Equal(spent, world.Game.Spent.Count, "no expense was booked");
            Assert.True(world.State.Data.research.completed.Count > 0, "the nodes of the catch-up are open");
            Assert.True(world.Api.ProductsOf(Player).Count >= 3, "the cards are the player's");
        }

        [Test]
        public void TheTransfer_Refuses_WhenThereIsNothingToTake_OrTheTargetIsNotThePlayer()
        {
            GpuWorld world = Eras2007(division: false);
            int amd = AmdCards(world).Count;

            Assert.False(world.Api.TransferVendorProducts("ai:1", "ai:0"), "the target must be the player");
            Assert.False(world.Api.TransferVendorProducts("ai:0", Player), "Intel has no vendor on the market in 2007");
            Assert.False(world.Api.TransferVendorProducts("ai:99", Player), "a company that is not in the game");
            Assert.False(world.Api.TransferVendorProducts(Player, Player), "the player to the player");
            Assert.False(world.Api.TransferVendorProducts(null, Player));
            Assert.False(world.Api.TransferVendorProducts(string.Empty, Player));
            Assert.False(world.Api.TransferVendorProducts("ai:1", null));
            Assert.False(world.Division.IsFounded, "a refused transfer founds nothing");
            Assert.Equal(amd, AmdCards(world).Count, "and moves nothing");

            Assert.True(world.Api.TransferVendorProducts("ai:1", Player));
            Assert.False(world.Api.TransferVendorProducts("ai:1", Player), "the second call: AMD is merged, nothing is bound to the company");
        }

        [Test]
        public void ARivalWithNoCards_StillMerges_AndTheTransferCountsZero()
        {
            GpuWorld world = Eras2007();
            world.State.Data.products.RemoveAll(p => p.owner == GpuState.OwnerVendor && p.vendorId == "amd.gpu");
            Assert.Equal(0, world.Rivals.TransferProducts("ai:1", Player), "0 cards passed");
            Assert.Equal(GpuState.VendorMerged, world.Rivals.Find("amd.gpu").State);

            GpuWorld again = Eras2007();
            Assert.Equal(-1, again.Rivals.TransferProducts("ai:1", "ai:0"), "-1 when the target is not the player");
            Assert.Equal(-1, again.Rivals.TransferProducts("ai:5", Player), "-1 when no vendor is bound");
            Assert.Equal(GpuState.VendorActive, again.Rivals.Find("amd.gpu").State, "nothing changed");
            Assert.Equal(1, again.Rivals.BoundTo("ai:1").Count);
            Assert.Equal(0, again.Rivals.BoundTo(null).Count);
        }

        // ---- the event ----

        [Test]
        public void TheEvent_FiresForACardThePlayerReleases_AndForACardAVendorLaunches()
        {
            GpuWorld world = Eras2007();
            var seen = new List<GpuProductView>();
            world.Api.ProductReleased += seen.Add;

            PlayerCard(world, "g-9010", status: GpuState.StatusReady);
            Assert.True(world.Lineup.Release("g-9010", 300f, true, 2));
            Assert.Equal(1, seen.Count);
            Assert.Equal("g-9010", seen[0].Id);
            Assert.Equal(Player, seen[0].OwnerKey);
            Assert.Equal(GpuState.StatusSelling, seen[0].Status);
            Assert.Equal(300f, seen[0].Price);

            GpuVendorRuntime amd = world.Rivals.Find("amd.gpu");
            GpuSegmentDto segment = world.Content.Segment(world.Content.ActiveSegmentIds(amd.Def).First(id => GpuEras.IsOpen(world.Content.Segment(id), world.Game.Date)));
            GpuProductSave launched = world.Rivals.AI.Launch(amd, segment, world.Game.Date, world.Game.Date, false);
            Assert.NotNull(launched, "the vendor launched a card");
            Assert.Equal(2, seen.Count);
            Assert.Equal(launched.id, seen[1].Id);
            Assert.Equal("ai:1", seen[1].OwnerKey, "the company the vendor is bound to");
            Assert.Equal(new[] { segment.id }, seen[1].SegmentIds.ToArray());

            world.ApiAvailable = false;
            world.Rivals.AI.Launch(amd, segment, world.Game.Date.AddMonths(30), world.Game.Date.AddMonths(30), false);
            Assert.Equal(2, seen.Count, "while the feature is off the event is silent");
        }

        [Test]
        public void TheSeedOfTheMarket_RaisesNoEvent()
        {
            var world = new GpuWorld(March2007, 42, GpuWorld.ShippedEras());
            world.Game.AddTimelineNodes();
            world.Game.FrontierKHz = 250000f;
            world.RivalsOn = true;
            world.Game.AddCompany("ai:1", "AMD");
            int raised = 0;
            world.Api.ProductReleased += v => raised++;
            world.Market.SeedForYear(2007);
            Assert.Equal(0, raised, "the history before the game is no news");
        }

        // ---- AC-18.1 ----

        [Test]
        public void Ac181_TheDemandChannel_RaisesTheBuyersOfEverySegment_ForExactlyNinetyDays()
        {
            GpuWorld world = Eras2007();
            ChannelRegistryLink link = ChannelRegistryLink.Attach(world);
            var baseline = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (GpuSegmentView segment in world.Market.OpenSegments) baseline[segment.Id] = segment.DailyBuyers;
            Assert.True(baseline.Count >= 3 && baseline["gaming.mid"] > 100f, "buyers to count: " + baseline["gaming.mid"]);

            // "Криптобум 2017 года": gpu.demand x1.5 for 90 days (ModifierTimed); the engine of the core answers on the date
            link.Registry.Add("historical-events", "gpu.demand", ModifierScope.Global(), 1.5f, 90);
            foreach (GpuSegmentView segment in world.Market.OpenSegments)
                Assert.Near(1.5 * baseline[segment.Id], segment.DailyBuyers, Math.Max(2.0, baseline[segment.Id] * 2e-3), segment.Id + ": x1.5 in every open segment");

            DateTime start = world.Game.Date;
            link.SetDate(start.AddDays(89));
            foreach (GpuSegmentView segment in world.Market.OpenSegments)
                Assert.Near(1.5 * baseline[segment.Id], segment.DailyBuyers, Math.Max(2.0, baseline[segment.Id] * 2e-3), segment.Id + ": the 90th day still has it");
            link.SetDate(start.AddDays(90));
            foreach (GpuSegmentView segment in world.Market.OpenSegments)
                Assert.Near(baseline[segment.Id], segment.DailyBuyers, Math.Max(2.0, baseline[segment.Id] * 2e-3), segment.Id + ": after 90 days it is gone");

            // a second entry on one segment multiplies, the others do not feel it
            link.SetDate(start);
            link.Registry.Add("event-system", "gpu.demand.gaming.mid", ModifierScope.Global(), 1.2f);
            Assert.Near(1.2 * baseline["gaming.mid"], world.Market.OpenSegments.First(s => s.Id == "gaming.mid").DailyBuyers, Math.Max(2.0, baseline["gaming.mid"] * 2e-3), "one segment: its own channel");
            Assert.Near(baseline["gaming.low"], world.Market.OpenSegments.First(s => s.Id == "gaming.low").DailyBuyers, Math.Max(2.0, baseline["gaming.low"] * 2e-3), "another segment: untouched");
        }

        // ---- the predicates ----

        private static JsonObject Parse(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json.Replace('\'', '"'), out node, out error), "test JSON is invalid: " + error);
            return (JsonObject)node;
        }

        private static PredicateRegistry RegistryFor(GpuWorld world, Func<IGpuQueries> source = null)
        {
            PredicateRegistry registry = Predicates.CreateRegistry();
            Func<IGpuQueries> queries = source ?? (() => world.Api);
            foreach (var pair in GpuPredicates.CreateFactories(queries))
                Assert.True(registry.Register(pair.Key, "gpu-product-line", pair.Value), "refused: " + pair.Key);
            return registry;
        }

        private static ICondition Compile(PredicateRegistry registry, string json, List<string> errors = null)
        {
            var list = errors ?? new List<string>();
            ICondition condition = new ConditionParser(registry).Parse(Parse(json), list, "$");
            if (errors == null) Assert.NotNull(condition, "parse failed: " + string.Join("; ", list.ToArray()));
            return condition;
        }

        private static FakeGameState State()
        {
            var state = new FakeGameState();
            state.AddCompany("ai:1");
            state.AddCompany("ai:0");
            return state;
        }

        private static bool Eval(PredicateRegistry registry, string json, string subject = null)
        {
            return ConditionEvaluator.Evaluate(Compile(registry, json), State().Context(March2007, subject));
        }

        [Test]
        public void ThePredicateNames_AreTheFourOfFR18_AndFollowTheConventionOfTheCore()
        {
            Assert.Equal(new[] { "gpu.divisionFounded", "gpu.hasProductOnMarket", "gpu.marketShareAtLeast", "gpu.researched" }, GpuPredicateNames.All);
            foreach (string name in GpuPredicateNames.All)
            {
                Assert.True(name.StartsWith("gpu.", StringComparison.Ordinal), name);
                Assert.False(new[] { "gpu.demand", "gpu.rating", "gpu.vramCost" }.Contains(name), "the spaces of predicates and of channels are apart: " + name);
            }

            var problems = new List<string>();
            try
            {
                string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", "mod-framework", "core.json");
                JsonNode node;
                JsonError error;
                Assert.True(ModJson.TryParse(SourceScan.ReadText(path), out node, out error), "core.json: " + error);
                Assert.True(ModIds.Load(((JsonObject)node).GetObject("prefixes"), problems) > 0, string.Join("; ", problems.ToArray()));
                Assert.True(ModIds.PrefixesOf("gpu-product-line").Contains("gpu"), "gpu is a prefix of the feature");
                foreach (string name in GpuPredicateNames.All) Assert.True(ModIds.IsPredicateNameAllowed("gpu-product-line", name), name + " passes ModIds.IsPredicateNameAllowed");
                foreach (string name in GpuPredicateNames.All) Assert.False(ModIds.IsPredicateNameAllowed("event-system", name), "another feature may not take " + name);

                PredicateRegistry mine = Predicates.CreateRegistry(ModIds.IsPredicateNameAllowed);
                foreach (var pair in GpuPredicates.CreateFactories(() => null))
                {
                    string refusal;
                    Assert.True(mine.TryRegister(pair.Key, "gpu-product-line", pair.Value, out refusal), pair.Key + ": " + refusal);
                    Assert.Equal("gpu-product-line", mine.OwnerOf(pair.Key));
                }
                Assert.Equal(0, mine.Rejected.Count);
            }
            finally
            {
                ModIds.Clear();
            }
            Assert.Throws<ArgumentNullException>(() => GpuPredicates.CreateFactories(null));
        }

        [Test]
        public void TheRegistrationInTheFeature_IsOncePerSession_WithTheOwner()
        {
            string src = GpuFiles.FeatureSourceDir;
            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(src, "GpuFeature.cs")), true);
            Assert.True(feature.Contains("GpuConditions.Register();"), "the feature registers the predicates in Register");
            string conditions = SourceScan.Strip(SourceScan.ReadText(Path.Combine(src, "GpuConditions.cs")), true);
            Assert.True(conditions.Contains("ModConditions.Register(pair.Key, GpuFeature.FeatureId, pair.Value)"), "ModConditions.Register(type, owner, factory)");
            Assert.True(conditions.Contains("if (_registered) return 0;"), "once");
            Assert.True(Regex.IsMatch(conditions, @"feature == null \? null : feature\.Api"), "the answers come from the services of the running feature");
        }

        [Test]
        public void Ac182_TheGoalOfAScenario_IsMetWhenThePlayerReachesQuarterOfTheMarket()
        {
            GpuWorld world = Eras2007();
            PredicateRegistry registry = RegistryFor(world);
            const string goal = "{'type':'gpu.marketShareAtLeast','share':0.25}";

            world.State.Data.market.playerShareDaily = Enumerable.Repeat(0.20f, 30).ToList();
            Assert.False(Eval(registry, goal), "20 %");
            world.State.Data.market.playerShareDaily = Enumerable.Repeat(0.2499f, 30).ToList();
            Assert.False(Eval(registry, goal), "24.99 %");
            world.State.Data.market.playerShareDaily = Enumerable.Repeat(0.25f, 30).ToList();
            Assert.True(Eval(registry, goal), "exactly 25 %: reached");
            world.State.Data.market.playerShareDaily = Enumerable.Repeat(0.40f, 30).ToList();
            Assert.True(Eval(registry, goal), "40 %");
        }

        [Test]
        public void TheShare_Predicate_WorksOnTheBoundaries_AndForOtherCompanies()
        {
            GpuWorld world = Eras2007();
            PredicateRegistry registry = RegistryFor(world);
            world.State.Data.market.playerShareDaily = Enumerable.Repeat(0.3f, 30).ToList();

            Assert.True(Eval(registry, "{'type':'gpu.marketShareAtLeast','share':0}"), "0 is always reached");
            Assert.True(Eval(registry, "{'type':'gpu.marketShareAtLeast','share':0.3}"), "0.3 in the JSON and the 0.3 of the game are the same number");
            Assert.False(Eval(registry, "{'type':'gpu.marketShareAtLeast','share':0.31}"));
            Assert.False(Eval(registry, "{'type':'gpu.marketShareAtLeast','share':1}"), "the whole market is not reached");
            world.State.Data.market.playerShareDaily = Enumerable.Repeat(1f, 30).ToList();
            Assert.True(Eval(registry, "{'type':'gpu.marketShareAtLeast','share':1}"), "the whole market");

            // an AI company: the share of its vendors
            world.Game.Companies["ai:1"].CpuShare = 0.1f;
            for (int i = 0; i < 12; i++) world.RunDay();
            world.State.Data.synergy.entries.RemoveAll(e => e.companyKey == "ai:1");
            Assert.True(Eval(registry, "{'type':'gpu.marketShareAtLeast','share':0,'company':'ai:1'}"), "ai:1 has a vendor");
            Assert.False(Eval(registry, "{'type':'gpu.marketShareAtLeast','share':0.99,'company':'ai:1'}"), "AMD does not hold the market");
            Assert.True(Eval(registry, "{'type':'gpu.marketShareAtLeast','share':0.001,'company':'anyAI'}"), "some AI company holds a thousandth of the market");
            Assert.False(Eval(registry, "{'type':'gpu.marketShareAtLeast','share':0.001,'company':'ai:0'}"), "Intel has no card on the market");
        }

        [Test]
        public void TheDivisionPredicate_AsksThePlayerByDefault_AndAnyCompanyByKey()
        {
            GpuWorld world = Eras2007(division: false);
            PredicateRegistry registry = RegistryFor(world);

            Assert.False(Eval(registry, "{'type':'gpu.divisionFounded'}"), "not founded");
            Assert.False(Eval(registry, "{'type':'gpu.divisionFounded','company':'player'}"));
            Assert.True(Eval(registry, "{'type':'gpu.divisionFounded','company':'ai:1'}"), "AMD has a vendor");
            Assert.False(Eval(registry, "{'type':'gpu.divisionFounded','company':'ai:0'}"), "Intel has not");
            Assert.True(Eval(registry, "{'type':'gpu.divisionFounded','company':'anyAI'}"), "anyAI: one of them has");
            Assert.True(Eval(registry, "{'type':'gpu.divisionFounded'}", "ai:1"), "the subject of the context");

            world.Division.FoundFree();
            Assert.True(Eval(registry, "{'type':'gpu.divisionFounded'}"));
            Assert.True(Eval(registry, "{'not':{'type':'gpu.divisionFounded','company':'ai:0'}}"), "predicates combine with the core's");
            Assert.True(Eval(registry, "{'all':[{'type':'gpu.divisionFounded'},{'type':'dateAtLeast','year':2000}]}"));
        }

        [Test]
        public void TheProductPredicate_WantsACardOnSale()
        {
            GpuWorld world = Eras2007();
            PredicateRegistry registry = RegistryFor(world);
            const string json = "{'type':'gpu.hasProductOnMarket'}";

            Assert.False(Eval(registry, json), "no card yet");
            GpuProductSave card = PlayerCard(world, "g-9001", status: GpuState.StatusReady);
            Assert.False(Eval(registry, json), "a card that is ready but not released");
            card.status = GpuState.StatusSelling;
            Assert.True(Eval(registry, json), "on sale");
            card.sell = false;
            Assert.False(Eval(registry, json), "off the market");
            card.sell = true;
            card.status = GpuState.StatusRetired;
            Assert.False(Eval(registry, json), "retired");

            Assert.True(Eval(registry, "{'type':'gpu.hasProductOnMarket','company':'ai:1'}"), "the cards of the vendor bound to AMD");
            Assert.False(Eval(registry, "{'type':'gpu.hasProductOnMarket','company':'ai:0'}"));
        }

        [Test]
        public void TheResearchPredicate_IsAboutThePlayersTree()
        {
            GpuWorld world = Eras2007();
            PredicateRegistry registry = RegistryFor(world);
            const string json = "{'type':'gpu.researched','node':'arch.3d'}";

            Assert.False(Eval(registry, json));
            world.State.Data.research.completed.Add("arch.3d");
            Assert.True(Eval(registry, json));
            Assert.False(Eval(registry, "{'type':'gpu.researched','node':'arch.tensor'}"), "another node");
            Assert.False(Eval(registry, "{'type':'gpu.researched','node':'no.such.node'}"));
            Assert.False(Eval(registry, json, "ai:1"), "the tree is the player's: a condition about another company is false");
        }

        [Test]
        public void EveryPredicate_IsFalse_WhileTheFeatureIsOff_OrWithoutServices()
        {
            GpuWorld world = Eras2007();
            world.State.Data.research.completed.Add("arch.3d");
            PlayerCard(world, "g-9001");
            world.State.Data.market.playerShareDaily = Enumerable.Repeat(0.5f, 30).ToList();
            PredicateRegistry registry = RegistryFor(world);
            string[] all =
            {
                "{'type':'gpu.divisionFounded'}", "{'type':'gpu.hasProductOnMarket'}", "{'type':'gpu.marketShareAtLeast','share':0.25}", "{'type':'gpu.researched','node':'arch.3d'}",
                "{'type':'gpu.divisionFounded','company':'anyAI'}",
            };
            foreach (string json in all) Assert.True(Eval(registry, json), "true to begin with: " + json);

            world.ApiAvailable = false;
            foreach (string json in all) Assert.False(Eval(registry, json), "off: " + json);

            PredicateRegistry none = RegistryFor(world, () => null);
            foreach (string json in all) Assert.False(Eval(none, json), "no services yet: " + json);
        }

        [Test]
        public void AParameterThatIsMissingMistypedOrUnknown_IsAnErrorWithItsPath()
        {
            GpuWorld world = Eras2007();
            PredicateRegistry registry = RegistryFor(world);

            var errors = new List<string>();
            Assert.Null(Compile(registry, "{'type':'gpu.marketShareAtLeast'}", errors));
            Assert.True(errors.Any(e => e.Contains("share")), "share is required: " + string.Join("; ", errors.ToArray()));

            errors.Clear();
            Assert.Null(Compile(registry, "{'type':'gpu.marketShareAtLeast','share':1.5}", errors));
            Assert.True(errors.Any(e => e.Contains("share")), "above 1");
            errors.Clear();
            Assert.Null(Compile(registry, "{'type':'gpu.marketShareAtLeast','share':-0.1}", errors));
            errors.Clear();
            Assert.Null(Compile(registry, "{'type':'gpu.marketShareAtLeast','share':'a quarter'}", errors));
            Assert.True(errors.Count > 0);

            errors.Clear();
            Assert.Null(Compile(registry, "{'type':'gpu.researched'}", errors));
            Assert.True(errors.Any(e => e.Contains("node")), "node is required");
            errors.Clear();
            Assert.Null(Compile(registry, "{'type':'gpu.divisionFounded','colour':'red'}", errors));
            Assert.True(errors.Any(e => e.Contains("colour")), "a parameter nobody knows is a typo, not a silent false: " + string.Join("; ", errors.ToArray()));
            errors.Clear();
            Assert.Null(Compile(registry, "{'type':'gpu.hasProductOnMarket','company':'someone'}", errors));
            Assert.True(errors.Count > 0, "a company that is not a key");

            ICondition ok = Compile(registry, "{'type':'gpu.marketShareAtLeast','share':0.25,'company':'ai:1'}");
            Assert.True(ok.Describe().Contains("gpu.marketShareAtLeast"), ok.Describe());
        }
    }

    /// <summary>
    /// The channel <c>gpu.demand</c> of the core's own engine wired to the demand a fake game gives the market, the way the bridge wires <c>ModModifiers</c> to it in the game: the market asks
    /// <see cref="FakeGpuGame.DemandOf"/>, which reads <c>gpu.demand</c> x <c>gpu.demand.&lt;segment&gt;</c> from the registry on the date the test sets.
    /// </summary>
    internal sealed class ChannelRegistryLink
    {
        public PTMod.Core.Logic.Modifiers.ChannelRegistry Registry;
        private GpuWorld _world;

        public static ChannelRegistryLink Attach(GpuWorld world)
        {
            var link = new ChannelRegistryLink { Registry = GpuCoreRegistry.Create(world.Game.Date), _world = world };
            var names = new GpuChannelNames
            {
                UnitCost = "production.unitCost", Output = "production.output", VramCost = "gpu.vramCost", Rating = "gpu.rating",
                PopularityGain = "popularity.gain", Demand = "gpu.demand",
            };
            var reader = new GpuChannelReader((channel, key) => link.Registry.Get(channel, GpuChannelReader.QueryFor(key)), names);
            world.Game.DemandOf = segmentId => reader.Demand(segmentId);
            return link;
        }

        public void SetDate(DateTime date)
        {
            _world.Game.Date = date;
            Registry.SetToday(date);
        }
    }
}
