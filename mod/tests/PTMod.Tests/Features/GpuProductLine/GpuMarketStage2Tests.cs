using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Features.GpuProductLine;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The windows of stage 2 (TSK-037; FR-8, FR-11, FR-12; AC-8.1, AC-11.2): the tab "Рынок" with the segments of the eras, the requirements of the year, the era that runs and the year of
    /// the next one, the shares of the makers in each segment (under their names and in their colours), and the marks "Нет: &lt;функция&gt;" of the cards that lack a function a segment asks
    /// for - in the tab, in the lineup. The designer's table says the same for a card not made yet (<c>GpuDesignerModelTests</c>). The services behind the shares are checked here too.
    /// </summary>
    [Covers(typeof(GpuFeatureMarks), typeof(GpuFeatureMark), typeof(GpuMarketMarkRow), typeof(GpuOwnerShare))]
    public class GpuMarketStage2Tests
    {
        private static GpuWorld Eras(DateTime date, bool vendors = true)
        {
            var world = new GpuWorld(date, 42, GpuWorld.ShippedEras()).Founded("arch.shader", "vram.gddr3", "cool.fan");
            world.Game.AddTimelineNodes();
            world.Game.FrontierKHz = 3400000f;
            world.RivalsOn = vendors;
            if (vendors)
            {
                world.Game.AddCompany("ai:1", "AMD Corp");
                world.Game.AddCompany("ai:0", "Intel");
            }
            world.Market.SeedForYear(date.Year);
            return world;
        }

        private static GpuMarketTabModel Build(GpuWorld world, string language = "ru")
        {
            return GpuMarketTabModel.Build(world.Content, world.Market, world.State, world.Game, new TestGpuText(language), world.Rivals, world.Eras);
        }

        /// <summary>A card of the player on sale in the given segments, made by hand: the functions of its architecture are what the marks look at.</summary>
        private static GpuProductSave Card(GpuWorld world, string id, string[] features, string[] segments, string status = GpuState.StatusSelling)
        {
            var card = new GpuProductSave
            {
                id = id, designId = string.Empty, owner = GpuState.OwnerPlayer, vendorId = string.Empty, companyKey = FakeGpuGame.Player, name = id,
                status = status, price = 150f, sell = true, stock = 1000000,
                stats = new GpuStatsSave { effectivePerf = 1000d, powerW = 50f, features = new List<string>(features) },
                node = "350nm", die = 100f, busWidth = 128, vramMB = 2048f,
                released = new GpuDate { y = world.Game.Date.Year, m = world.Game.Date.Month, d = world.Game.Date.Day },
                segIds = new List<string>(segments), pop = segments.Select(s => 0.3f).ToList(),
            };
            world.State.Data.products.Add(card);
            return card;
        }

        private static readonly string[] Unified = { "2d", "gui", "3d", "tnl", "shader", "unified" };

        private static GpuMarketSegmentBlock Block(GpuMarketTabModel model, string id)
        {
            GpuMarketSegmentBlock block = model.Segments.Find(b => b.Id == id);
            Assert.NotNull(block, "the segment " + id + " is on the tab: " + string.Join(", ", model.Segments.Select(b => b.Id).ToArray()));
            return block;
        }

        // ---- AC-8.1: the segments of 2004 ----

        [Test]
        public void Ac81_In2004_TheTabShowsTheFourSegments_WithTheShaderAndTheMemoryOfTheYear_AndNoCompute()
        {
            GpuWorld world = Eras(new DateTime(2004, 1, 1));
            GpuMarketTabModel model = Build(world);

            Assert.Equal(new[] { "office2d", "gaming.high", "gaming.mid", "gaming.low" }, model.Segments.Select(s => s.Id).ToArray(), "no compute before 2007");
            Assert.Equal(new[] { "Офисные 2D-ускорители", "Игровые: энтузиасты", "Игровые: массовые", "Игровые: бюджетные" }, model.Segments.Select(s => s.Name).ToArray());

            foreach (string id in new[] { "gaming.high", "gaming.mid", "gaming.low" })
                Assert.True(Block(model, id).RequirementText.Contains("шейдеры"), id + " asks for the shader: " + Block(model, id).RequirementText);
            Assert.True(Block(model, "gaming.high").RequirementText.Contains("не меньше 256 МБ"), Block(model, "gaming.high").RequirementText);
            Assert.True(Block(model, "gaming.mid").RequirementText.Contains("не меньше 128 МБ"), Block(model, "gaming.mid").RequirementText);
            Assert.True(Block(model, "gaming.low").RequirementText.Contains("не меньше 64 МБ"), Block(model, "gaming.low").RequirementText);
            Assert.True(Block(model, "office2d").RequirementText.Contains("2D"), Block(model, "office2d").RequirementText);
            foreach (GpuMarketSegmentBlock block in model.Segments)
            {
                Assert.True(block.BuyersText.StartsWith("Покупателей в месяц: "), block.BuyersText);
                Assert.True(block.BudgetText.StartsWith("Бюджет покупателя: "), block.BudgetText);
            }
        }

        // ---- the era that runs and the next one (FR-11) ----

        [Test]
        public void TheEra_IsNamed_AndTheYearOfTheNextOneIsGiven()
        {
            GpuWorld world = Eras(new DateTime(2004, 6, 1));
            GpuMarketTabModel model = Build(world);
            Assert.Equal("era.shader", model.EraId);
            Assert.Equal("Эпоха: Эпоха шейдеров", model.EraText);
            Assert.Equal(2007, model.NextEraYear);
            Assert.Equal("Следующая эпоха — «Эпоха вычислений на видеокартах» — начнётся в 2007 году", model.NextEraText);

            GpuMarketTabModel en = Build(world, "en");
            Assert.Equal("Era: The era of shaders", en.EraText);
            Assert.Equal("The next era, The era of compute on graphics cards, begins in 2007", en.NextEraText);
        }

        [Test]
        public void AfterTheLastEra_TheTabSaysNoneIsLeft_AndInTheSingleMarketThereIsNoEraAtAll()
        {
            GpuWorld late = Eras(new DateTime(2021, 5, 1), false);
            GpuMarketTabModel model = Build(late);
            Assert.Equal("era.rt", model.EraId);
            Assert.Equal(0, model.NextEraYear);
            Assert.Equal("Новых эпох в календаре больше нет.", model.NextEraText);

            var single = new GpuWorld(new DateTime(1996, 1, 1));
            single.Market.SeedForYear(1996);
            GpuMarketTabModel stage1 = Build(single);
            Assert.Equal(string.Empty, stage1.EraText, "the market of the single segment has no eras");
            Assert.Equal(string.Empty, stage1.NextEraText);
            Assert.Equal(1, stage1.Segments.Count);
        }

        // ---- AC-11.2: "Нет: <функция>" ----

        [Test]
        public void Ac112_ACardWithoutTheComputeFunction_HasARatingOfZeroInTheComputeSegment_AndTheMark()
        {
            GpuWorld world = Eras(new DateTime(2008, 3, 1));
            GpuProductSave card = Card(world, "Vortex", Unified, new[] { "compute", "gaming.high" });

            // the rating in the compute segment is zero: the factor of the market (F34, 0 for the compute segment) is
            DateTime today = world.Game.Date;
            List<SegmentRuntime> segs = world.Market.BuildSegments(today);
            MarketItem item = world.Market.BuildItems(segs, today).Find(i => i.ProductId == "Vortex");
            Assert.NotNull(item);
            Assert.Equal(0f, item.SegmentFactor[segs.Find(s => s.Id == "compute").Index], "no function, no sales");
            Assert.True(item.SegmentFactor[segs.Find(s => s.Id == "gaming.high").Index] > 0f);

            GpuMarketTabModel model = Build(world);
            GpuMarketSegmentBlock compute = Block(model, "compute");
            Assert.Equal(1, compute.Marks.Count);
            GpuMarketMarkRow mark = compute.Marks[0];
            Assert.Equal("Vortex", mark.CardName);
            Assert.Equal("Vortex", mark.ProductId);
            Assert.Equal("Нет: вычисления (GPGPU)", mark.Text, "AC-11.2: «Нет: compute»");
            Assert.Equal("рейтинг 0: в сегменте не продаётся", mark.Effect);
            Assert.True(mark.NoSales);
            Assert.Equal("Ваша карта «Vortex»: Нет: вычисления (GPGPU) — рейтинг 0: в сегменте не продаётся", mark.Line);
            Assert.Equal(0, Block(model, "gaming.high").Marks.Count, "it has everything the enthusiasts ask for in 2008");
            Assert.Equal(0, Block(model, "gaming.mid").Marks.Count, "nor in the other segments: it has what every gaming segment asks for in 2008");
            Assert.Equal(0, Block(model, "office2d").Marks.Count);

            card.stats.features = new List<string>(Unified) { "compute" };
            Assert.Equal(0, Block(Build(world), "compute").Marks.Count, "with the function there is no mark");
        }

        [Test]
        public void ACardOfThePlayer_IsMarkedInEverySegmentOpen_NotOnlyInTheOnesItIsListedIn_AndNotWhenItIsNotOffered()
        {
            GpuWorld world = Eras(new DateTime(2008, 3, 1));
            GpuProductSave card = Card(world, "Vortex", Unified, new[] { "gaming.high" });   // listed in one segment; the market gives it the others

            GpuMarketTabModel model = Build(world);
            Assert.Equal(1, Block(model, "compute").Marks.Count, "it competes in the compute segment too and lacks the function there");
            Assert.Equal("Vortex", Block(model, "compute").Marks[0].CardName);
            Assert.Equal(0, Block(model, "gaming.high").Marks.Count);

            card.sell = false;   // not offered: it competes nowhere
            Assert.Equal(0, Block(Build(world), "compute").Marks.Count);
            card.sell = true;
            card.status = GpuState.StatusRetired;
            Assert.Equal(0, Block(Build(world), "compute").Marks.Count, "a card that was retired is not on the market");
        }

        [Test]
        public void ACardThatLacksAFunctionOfAGamingSegment_IsMarkedWithTheFactorOfTheRating()
        {
            GpuWorld world = Eras(new DateTime(2008, 3, 1));
            Card(world, "Old", new[] { "2d", "gui", "3d", "tnl", "shader" }, new[] { "gaming.mid" });   // no unified shaders, which 2008 asks for

            GpuMarketSegmentBlock mid = Block(Build(world), "gaming.mid");
            Assert.Equal(1, mid.Marks.Count);
            Assert.Equal("Нет: унифицированные шейдеры", mid.Marks[0].Text);
            Assert.Equal("рейтинг ×0,35", mid.Marks[0].Effect.Replace('.', ','), "0.35 for each function the card lacks");
            Assert.False(mid.Marks[0].NoSales);

            GpuMarketSegmentBlock en = Block(Build(world, "en"), "gaming.mid");
            Assert.Equal("Missing: unified shaders", en.Marks[0].Text);
            Assert.Equal("Your card \"Old\": Missing: unified shaders — rating ×0.35", en.Marks[0].Line.Replace(',', '.'));
        }

        [Test]
        public void TheLineup_MarksACardInEverySegmentOpenNow_AndNotOneThatWaitsOrIsPaused()
        {
            GpuWorld world = Eras(new DateTime(2012, 3, 1), false);
            Card(world, "Vortex", Unified, new[] { "gaming.high", "office2d" });   // office2d closed in 2010; the card competes in the compute segment all the same
            Card(world, "Waits", new string[0], new string[0], GpuState.StatusReady);          // waiting for its release: in no segment yet

            GpuLineupModel lineup = GpuLineupModel.Build(world.Lineup, world.Projects, world.Production, world.Market, world.Content, world.Game, false, new TestGpuText());
            GpuProductRow row = lineup.Products.Find(r => r.Id == "Vortex");
            Assert.NotNull(row);
            Assert.Equal(1, row.Marks.Count, "the compute segment, which the card is not listed in (a card of the player competes in every open segment); the gaming ones ask only for what it has");
            Assert.Equal("compute", row.Marks[0].SegmentId);
            Assert.Equal("Нет: вычисления (GPGPU) — «Вычисления»: рейтинг 0: в сегменте не продаётся", row.MarksText);
            Assert.True(row.Marks[0].NoSales);

            GpuProductRow ready = lineup.Products.Find(r => r.Id == "Waits");
            Assert.Equal(string.Empty, ready.MarksText);

            world.State.Data.products.Find(p => p.id == "Vortex").sell = false;   // not offered: it competes nowhere
            GpuLineupModel paused = GpuLineupModel.Build(world.Lineup, world.Projects, world.Production, world.Market, world.Content, world.Game, false, new TestGpuText());
            Assert.Equal(string.Empty, paused.Products.Find(r => r.Id == "Vortex").MarksText);
            world.State.Data.products.Find(p => p.id == "Vortex").sell = true;

            GpuLineupModel en = GpuLineupModel.Build(world.Lineup, world.Projects, world.Production, world.Market, world.Content, world.Game, false, new TestGpuText("en"));
            Assert.Equal("Missing: compute (GPGPU) — \"Compute\": rating 0: it does not sell in the segment", en.Products.Find(r => r.Id == "Vortex").MarksText);
        }

        [Test]
        public void TheLineup_MarksNothingOnTheMarketOfStage1_AndForACardThatHasEverything()
        {
            var single = new GpuWorld(new DateTime(2005, 3, 10));
            GpuProductSave one = single.AddCard("g-1", 5);
            one.segIds = new List<string> { "consumer" };
            one.pop = new List<float> { 0.2f };
            GpuLineupModel lineup = GpuLineupModel.Build(single.Lineup, single.Projects, single.Production, single.Market, single.Content, single.Game, false, new TestGpuText());
            Assert.Equal(string.Empty, lineup.Products[0].MarksText, "the segment of stage 1 asks for no function");

            GpuWorld eras = Eras(new DateTime(2008, 3, 1), false);
            Card(eras, "Full", Unified.Concat(new[] { "compute" }).ToArray(), new[] { "compute", "gaming.high" });
            GpuLineupModel full = GpuLineupModel.Build(eras.Lineup, eras.Projects, eras.Production, eras.Market, eras.Content, eras.Game, false, new TestGpuText());
            Assert.Equal(string.Empty, full.Products.Find(r => r.Id == "Full").MarksText);
        }

        // ---- the marks as a service of the windows ----

        [Test]
        public void TheMarks_AreForTheSegmentsAskedForAndOpen_AndAreWrittenInWords()
        {
            GpuWorld world = Eras(new DateTime(2008, 3, 1), false);
            var text = new TestGpuText();
            IReadOnlyList<GpuSegmentView> open = world.Market.OpenSegments;
            string[] two = { "2d", "gui" };

            List<GpuFeatureMark> all = GpuFeatureMarks.For(two, null, open, world.Content, text);
            Assert.True(all.Count >= 3, "a card with 2D only lacks something in every gaming segment and in compute: " + all.Count);
            Assert.True(all.All(m => m.Missing.Length > 0 && m.Line.Length > 0));

            List<GpuFeatureMark> some = GpuFeatureMarks.For(two, GpuFeatureMarks.SegmentSet(new[] { "compute", "gone", "" }), open, world.Content, text);
            Assert.Equal(new[] { "compute" }, some.Select(m => m.SegmentId).ToArray(), "only the segments asked for, only the open ones");
            Assert.Equal(new[] { "compute" }, some[0].Missing);
            Assert.Equal(0f, some[0].Factor);

            Assert.Equal(0, GpuFeatureMarks.For(two, null, null, world.Content, text).Count, "no open segments: no marks");
            Assert.Equal(1, GpuFeatureMarks.For(null, new[] { "office2d" }, open, world.Content, text).Count, "a card with no known function lacks 2D in the office segment, which is open until 2010");
            Assert.Equal(0, GpuFeatureMarks.For(null, new[] { "office2d" }, Eras(new DateTime(2012, 1, 1), false).Market.OpenSegments, world.Content, text).Count, "and in 2012 the segment is closed: not a word");
        }

        [Test]
        public void ATextOfAMark_ShowsTheFactorWithTwoDecimals_AndZeroAsNoSales()
        {
            GpuWorld world = Eras(new DateTime(2008, 3, 1), false);
            var text = new TestGpuText();
            GpuFeatureMark partial = GpuFeatureMarks.Describe("gaming.mid", world.Content, text, new[] { "3d", "tnl" }, 0.1225f);
            Assert.Equal("Нет: 3D, T&L (геометрия)", partial.Text);
            Assert.Equal("рейтинг ×" + GpuUiMath.Plain(0.12f), partial.Effect);
            Assert.False(partial.NoSales);
            Assert.Equal("Нет: 3D, T&L (геометрия) — «Игровые: массовые»: рейтинг ×" + GpuUiMath.Plain(0.12f), partial.Line);

            GpuFeatureMark none = GpuFeatureMarks.Describe("compute", world.Content, text, new[] { "compute" }, 0f);
            Assert.True(none.NoSales);
            Assert.Equal(partial.Line + "\n" + none.Line, GpuFeatureMarks.Lines(new[] { partial, none }));
            Assert.Equal(string.Empty, GpuFeatureMarks.Lines(null));
            Assert.Equal(string.Empty, GpuFeatureMarks.Lines(new List<GpuFeatureMark>()));
        }

        // ---- the shares of the makers (FR-8 item 1, FR-12) ----

        private static void SellFor(GpuWorld world, int days)
        {
            for (int i = 0; i < days; i++) world.RunDay();
        }

        [Test]
        public void TheSharesInASegment_AreOfTheMakersUnderTheirNames_InTheirColours_TheBiggestFirst()
        {
            GpuWorld world = Eras(new DateTime(2004, 1, 1));
            GpuMarketTabModel before = Build(world);
            Assert.True(before.Segments.All(b => b.Shares.Count == 0), "right after a load the window of 30 days is empty");

            SellFor(world, 5);
            GpuMarketTabModel model = Build(world);
            foreach (GpuMarketSegmentBlock block in model.Segments)
            {
                Assert.True(block.Shares.Count >= 2, block.Id + ": the background and at least one maker sell");
                Assert.Near(1.0, block.Shares.Sum(s => s.Share), 1e-3, block.Id + ": the shares of the segment add up to one");
                for (int i = 1; i < block.Shares.Count; i++)
                    Assert.True(block.Shares[i - 1].Share >= block.Shares[i].Share, block.Id + ": the biggest first");
                foreach (GpuShareRow row in block.Shares)
                {
                    Assert.True(row.ShareText.EndsWith("%"), row.ShareText);
                    if (row.OwnerKey == MarketItem.BackgroundOwner)
                    {
                        Assert.Equal("Прочие производители", row.Name);
                        Assert.Equal(string.Empty, row.ColorHex, "the others have no colour of their own");
                    }
                    else
                    {
                        GpuVendorDto def = world.Content.Vendor(row.OwnerKey.Substring(MarketItem.VendorPrefix.Length));
                        Assert.NotNull(def, row.OwnerKey);
                        Assert.Equal(def.name.ru, row.Name, "the vendor under its name");
                        Assert.Equal(def.color, row.ColorHex, "and in its colour");
                        Assert.True(Regex.IsMatch(row.ColorHex, "^#[0-9A-Fa-f]{6}$"));
                    }
                }
            }

            // the whole market together: the same makers, one row each
            Assert.Near(1.0, model.Shares.Sum(s => s.Share), 1e-3);
            Assert.True(model.Shares.Any(s => s.Name == "NVIDIA"), string.Join(", ", model.Shares.Select(s => s.Name).ToArray()));
            Assert.Equal(model.Shares.Count, model.Shares.Select(s => s.OwnerKey).Distinct().Count());
        }

        [Test]
        public void ThePlayersCard_IsInTheSharesOfItsSegments_UnderYou_AndTheNamesFollowTheLanguage()
        {
            GpuWorld world = Eras(new DateTime(2004, 1, 1));
            GpuProductSave mineCard = world.AddCard("Mine", 0);   // arch.shader, the design of AC-3.1
            mineCard.segIds = new List<string> { "gaming.mid" };
            mineCard.pop = new List<float> { 0.5f };
            mineCard.stock = 1000000;
            mineCard.price = 0.9f * world.Market.BudgetOf(world.Content.Segment("gaming.mid"), 2004);
            SellFor(world, 5);

            GpuMarketSegmentBlock mid = Block(Build(world), "gaming.mid");
            GpuShareRow mine = mid.Shares.Find(s => s.IsPlayer);
            Assert.NotNull(mine, string.Join(", ", mid.Shares.Select(s => s.Name).ToArray()));
            Assert.Equal("Вы", mine.Name);
            Assert.Equal(MarketItem.PlayerOwner, mine.OwnerKey);
            Assert.Equal(string.Empty, mine.ColorHex);

            Assert.Equal("You", Block(Build(world, "en"), "gaming.mid").Shares.Find(s => s.IsPlayer).Name);
        }

        [Test]
        public void TheMakerOfACard_IsUnderTheNameOfTheContent_OrTheNameOfTheGameForABoundOne()
        {
            GpuWorld world = Eras(new DateTime(2010, 6, 1));
            var ru = new TestGpuText();
            var en = new TestGpuText("en");

            Assert.Equal("Вы", GpuMarketTabModel.MakerOf(ru, world.Content, world.Rivals, MarketItem.PlayerOwner, ""));
            Assert.Equal("Прочие производители", GpuMarketTabModel.MakerOf(ru, world.Content, world.Rivals, MarketItem.BackgroundOwner, ""));
            Assert.Equal("NVIDIA", GpuMarketTabModel.MakerOf(ru, world.Content, world.Rivals, "vendor:nvidia", "nvidia"));
            Assert.Equal("AMD Corp", GpuMarketTabModel.MakerOf(ru, world.Content, world.Rivals, "vendor:amd.gpu", "amd.gpu"), "bound to a company of the game: its name");
            Assert.Equal("AMD Corp", GpuMarketTabModel.MakerOf(en, world.Content, world.Rivals, "vendor:amd.gpu", "amd.gpu"));
            Assert.Equal("NVIDIA", GpuMarketTabModel.MakerOf(en, world.Content, null, "vendor:nvidia", "nvidia"), "without the services: the content's name");
            Assert.Equal("ghost", GpuMarketTabModel.MakerOf(ru, world.Content, world.Rivals, "vendor:ghost", "ghost"), "a vendor the content does not know: its id");
            Assert.Equal(string.Empty, GpuMarketTabModel.MakerOf(ru, world.Content, world.Rivals, "vendor:", ""));
        }

        [Test]
        public void TheTopOfASegment_NamesTheVendorsOfTheCards()
        {
            GpuWorld world = Eras(new DateTime(2004, 1, 1));
            SellFor(world, 3);
            GpuMarketTabModel model = Build(world);
            var makers = new HashSet<string>(model.Segments.SelectMany(s => s.Top).Select(r => r.Maker));
            Assert.True(makers.Contains("NVIDIA") && makers.Contains("ATI"), "the vendors of 2004 are in the tops under their names: " + string.Join(", ", makers.ToArray()));
            Assert.False(makers.Contains("nvidia") || makers.Contains("ati"), "not under their ids");
        }

        // ---- the services behind the shares ----

        [Test]
        public void TheSegmentShares_ComeFromTheSalesOfTheLast30Days_AndStartAnewWithTheSession()
        {
            GpuWorld world = Eras(new DateTime(2004, 1, 1));
            Assert.Equal(0, world.Market.SegmentShares("gaming.mid").Count, "nothing sold yet");
            Assert.Equal(0, world.Market.OwnerShares().Count);
            Assert.Equal(0f, world.Market.ShareOf("vendor:nvidia", "gaming.mid", 30));

            SellFor(world, 4);
            List<GpuOwnerShare> mid = world.Market.SegmentShares("gaming.mid");
            Assert.True(mid.Count >= 2);
            Assert.Near(1.0, mid.Sum(s => s.Share), 1e-4);
            Assert.Near(mid[0].Share, world.Market.ShareOf(mid[0].OwnerKey, "gaming.mid", 30), 1e-6, "the share of one owner is the row of the list");
            Assert.Equal(0f, world.Market.ShareOf("vendor:nobody", "gaming.mid", 30));
            Assert.Equal(0f, world.Market.ShareOf(null, "gaming.mid", 30));
            Assert.Equal(0, world.Market.SegmentShares("compute").Count, "a segment that is not open sold nothing");
            Assert.Equal(0, world.Market.SegmentShares(null).Count);

            // the whole market is the sum of the segments
            List<GpuOwnerShare> all = world.Market.OwnerShares();
            int soldAll = all.Sum(s => s.Sold);
            int soldSegments = 0;
            foreach (string id in new[] { "office2d", "gaming.high", "gaming.mid", "gaming.low" }) soldSegments += world.Market.SegmentShares(id).Sum(s => s.Sold);
            Assert.Equal(soldAll, soldSegments, "every card sold is sold in one segment");

            // one day back sees less than all of them
            Assert.True(world.Market.SegmentShares("gaming.mid", 1).Sum(s => s.Sold) < mid.Sum(s => s.Sold));

            world.Market.Reset();
            Assert.Equal(0, world.Market.SegmentShares("gaming.mid").Count, "a new session starts with empty windows");
        }

        [Test]
        public void TheWindowOfTheSegments_KeepsThirtyDays()
        {
            GpuWorld world = Eras(new DateTime(2004, 1, 1));
            SellFor(world, 40);
            int thirty = world.Market.SegmentShares("gaming.mid", 30).Sum(s => s.Sold);
            int forty = world.Market.SegmentShares("gaming.mid", 40).Sum(s => s.Sold);
            Assert.Equal(thirty, forty, "the window holds thirty days, older days are gone");
            Assert.True(thirty > 0);
        }

        // ---- the refresh of the window (NFR-1: at most 2 ms with the segments of the eras) ----

        [Test]
        public void TheTabIsBuilt_InUnderTwoMilliseconds_WithTheMostSegmentsOpenAtOnce()
        {
            GpuWorld world = Eras(new DateTime(2018, 6, 1));
            Card(world, "Mine", Unified.Concat(new[] { "compute", "rt" }).ToArray(), new[] { "gaming.high", "gaming.mid", "gaming.low", "compute" });
            SellFor(world, 6);
            Assert.Equal(5, world.Market.OpenSegments.Count, "from 2016 to 2025 five segments are open: the office segment closed in 2010");

            GpuMarketTabModel warm = Build(world);   // the first build compiles the code
            Assert.Equal(5, warm.Segments.Count);
            double best = double.MaxValue;
            for (int run = 0; run < 6; run++)
            {
                var watch = Stopwatch.StartNew();
                const int builds = 20;
                for (int i = 0; i < builds; i++) Build(world);
                watch.Stop();
                best = Math.Min(best, watch.Elapsed.TotalMilliseconds / builds);
            }
            Assert.True(best < 2.0, "building the tab of the market took " + best.ToString("0.000") + " ms (budget 2 ms for the refresh of the window)");
            Console.WriteLine("  perf  the model of the tab 'Рынок', 5 segments, " + world.State.Data.products.Count + " cards: " + best.ToString("0.000") + " ms");
        }

        // ---- the game side, on the source ----

        private static string UiFile(string name)
        {
            string path = Path.Combine(GpuFiles.FeatureSourceDir, "Ui", name);
            Assert.True(File.Exists(path), "no " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        [Test]
        public void TheTabs_DrawTheMarks_TheSharesWithColours_AndTheEra_AndRebuildWhenTheShapeChanges()
        {
            string market = UiFile("GpuMarketTab.cs");
            Assert.True(Regex.IsMatch(market, @"GpuMarketTabModel\.Build\(_feature\.Content, _feature\.Market, _feature\.State, _feature\.Game, GpuUi\.Text, _feature\.Rivals, _feature\.Eras,\s*_feature\.Synergy, _feature\.ExplainSynergy\)"), "the makers, the eras and the synergy are given");
            Assert.True(market.Contains("GpuSwatch.Create("), "the square of the colour of the vendor");
            Assert.True(market.Contains("DrawEra()") && market.Contains("EraText") && market.Contains("NextEraText"), "the era and the year of the next one");
            Assert.True(Regex.IsMatch(market, @"segment\.Shares\.Count\)\.Append\(':'\)\.Append\(segment\.Marks\.Count\)"), "the shape of the tab includes the shares and the marks");
            Assert.True(market.Contains("GpuWindow.RebuildLater()"), "a new shape is built again");
            Assert.True(market.Contains("Marks[row]") || market.Contains(".Marks["), "the marks are drawn from the model");

            string lineup = UiFile("GpuLineupTab.cs");
            Assert.True(lineup.Contains("row.MarksText") && lineup.Contains("widgets.Marks"), "the lineup draws the marks of a card");

            string ui = UiFile("GpuUi.cs");
            Assert.True(Regex.IsMatch(ui, @"class GpuSwatch"), "the swatch is an image, not a glyph of a font");
            Assert.True(ui.Contains("if (!string.Equals(text.text, next, StringComparison.Ordinal)) text.text = next;"), "a text that did not change is not set again");
        }
    }
}
