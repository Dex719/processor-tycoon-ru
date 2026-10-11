using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Modifiers;
using PTMod.Features.GpuProductLine;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The platform synergy and the shared experience of the process nodes (TSK-040, TSK-041; FR-15, FR-16): the rings of the daily shares and the monthly count (AC-15.1: +8 % and +5 %),
    /// the bonuses of zero when nothing is on sale (AC-15.2), the AI companies a vendor is bound to, the switch that stops it all, the two providers in the core's own channel engine with the
    /// range of the core (AC-15.3: x1.08 stays x1.08 in the breakdown and the total is cut to 2.0), the table and the views, the block of the tab "Рынок" with its breakdown, and the lines of the
    /// cards that count into the learning of a node (AC-16.1). The arithmetic is <see cref="GpuSynergyMathTests"/>.
    /// </summary>
    [Covers(typeof(GpuSynergy), typeof(GpuSynergyView), typeof(GpuSharedLearning), typeof(GpuSynergyBlock), typeof(GpuSynergyChannel), typeof(GpuExplainRow), typeof(GpuExplainView))]
    public class GpuSynergyTests
    {
        private const string Player = FakeGpuGame.Player;
        private static readonly DateTime Day0 = new DateTime(2005, 3, 1);

        // ---- the world ----

        private static GpuWorld World(DateTime today, bool eras = false, bool rivals = false, uint seed = 42)
        {
            var world = new GpuWorld(today, seed, eras ? GpuWorld.ShippedEras() : null);
            world.Game.AddTimelineNodes();
            world.Game.FrontierKHz = 250000f;
            world.RivalsOn = rivals;
            if (rivals)
            {
                world.Game.AddCompany("ai:1", "AMD");
                world.Game.AddCompany("ai:0", "Intel");
            }
            return world;
        }

        private static GpuProductSave Background(GpuWorld world, string segment = "consumer")
        {
            return world.State.Data.products.Find(p => p.id == GpuMarketService.BackgroundIdPrefix + segment);
        }

        /// <summary>A card of the player on the market, made like the background's but <paramref name="perfFactor"/> times as fast, with a lot of stock and a strong name.</summary>
        private static GpuProductSave PlayerCard(GpuWorld world, string id, float perfFactor = 8f, float price = 150f, float pop = 0.9f)
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
                id = id, designId = string.Empty, owner = GpuState.OwnerPlayer, vendorId = string.Empty, companyKey = Player, name = id,
                status = GpuState.StatusSelling, price = price, sell = true, stock = 100000000, stats = stats, node = bg.node, die = bg.die, busWidth = bg.busWidth,
                vramMB = bg.vramMB, released = new GpuDate { y = world.Game.Date.Year, m = world.Game.Date.Month, d = world.Game.Date.Day },
                segIds = new List<string> { "consumer" }, pop = new List<float> { pop },
            };
            world.State.Data.products.Add(card);
            return card;
        }

        /// <summary>The market seeded and a strong card of the player on it; the division is founded.</summary>
        private static GpuWorld Playing(DateTime today)
        {
            GpuWorld world = World(today);
            world.Founded();
            world.Market.SeedForYear(today.Year);
            PlayerCard(world, "g-1");
            return world;
        }

        private static void Days(GpuWorld world, int n)
        {
            for (int i = 0; i < n; i++) world.RunDay();
        }

        private static GpuSynergyEntry EntryOf(GpuWorld world, string key)
        {
            return world.State.Data.synergy.entries.Find(e => e.companyKey == key);
        }

        // ---- AC-15.1: the daily rings and the monthly count ----

        [Test]
        public void Ac151_FortyPercentOfTheCards_AndTwentyOfTheCpus_GiveEightAndFivePercent_OnThe1st()
        {
            GpuWorld world = Playing(new DateTime(2005, 3, 20));
            world.State.Data.synergy.entries.Add(new GpuSynergyEntry
            {
                companyKey = Player,
                gpuShareDaily = Enumerable.Repeat(0.4f, 30).ToList(),
                cpuShareDaily = Enumerable.Repeat(0.2f, 30).ToList(),
            });

            world.Game.Date = new DateTime(2005, 4, 1);
            world.Synergy.MonthTick();

            GpuSynergyEntry entry = EntryOf(world, Player);
            Assert.Near(0.08, entry.sCpu, 1e-6, "s_cpu = min(0.08, 0.2 x 0.4)");
            Assert.Near(0.05, entry.sGpu, 1e-6, "s_gpu = min(0.10, 0.25 x 0.2)");

            GpuSynergyView view = world.Synergy.ViewFor(Player);
            Assert.True(view.Active, "the window says: active");
            Assert.True(view.Eligible);
            Assert.Near(0.08, view.CpuBonus, 1e-6);
            Assert.Near(0.05, view.GpuBonus, 1e-6);
            Assert.Near(0.4, view.GpuShare30, 1e-6);
            Assert.Near(0.2, view.CpuShare30, 1e-6);

            ProviderResult cpu = GpuSynergyTable.CpuResult(world.Synergy.Table.Lookup(Player), true);
            ProviderResult gpu = GpuSynergyTable.GpuResult(world.Synergy.Table.Lookup(Player), true);
            Assert.Near(1.08, cpu.Multiplier, 1e-6, "+8 % to the rating of the CPUs");
            Assert.Near(1.05, gpu.Multiplier, 1e-6, "+5 % to the rating of the cards");
            Assert.Equal(new[] { "8" }, cpu.ReasonArgs);
            Assert.Equal(new[] { "5" }, gpu.ReasonArgs);
        }

        [Test]
        public void TheDay_PutsTheShareOfTheCardsAndOfTheCpus_IntoTheRings_AndTheMonthCountsFromThem()
        {
            GpuWorld world = Playing(new DateTime(2005, 3, 1));
            world.Game.PlayerCpuShare = 0.15f;

            Days(world, 40);   // the 1st of April comes on the 32nd day, MonthTick has run once
            GpuSynergyEntry entry = EntryOf(world, Player);
            Assert.NotNull(entry, "the player has a ring");
            Assert.Equal(30, entry.gpuShareDaily.Count, "30 days");
            Assert.Equal(30, entry.cpuShareDaily.Count);
            Assert.True(entry.cpuShareDaily.All(v => Math.Abs(v - 0.15f) < 1e-6f), "the share of the CPUs the game gave, every day");
            float mean = GpuSynergyMath.Mean(entry.gpuShareDaily);
            Assert.True(mean > 0.2f, "the strong card holds a big part of the cards sold: " + mean);

            // the last value is the share of the day that just ended
            Assert.Near(world.Market.ShareOf("player", 1), entry.gpuShareDaily[29], 1e-6, "the share of the cards on the last day");

            Days(world, 22);   // 1 May: the count of the month uses the rings of the days before it
            world.Game.Date = new DateTime(2005, 5, 1);
            world.Synergy.MonthTick();
            float gpuMean = GpuSynergyMath.Mean(entry.gpuShareDaily);
            Assert.Near(Math.Min(0.08f, 0.2f * gpuMean), entry.sCpu, 1e-6, "F25: min(cpuMax, cpuK x the share of the cards)");
            Assert.Near(Math.Min(0.10f, 0.25f * 0.15f), entry.sGpu, 1e-6, "F25: min(gpuMax, gpuK x the share of the CPUs) = 0.0375");
            Assert.Near(0.0375, entry.sGpu, 1e-6);
        }

        [Test]
        public void ADayWithoutASale_AddsNothingToTheRingOfTheCards_ButTheCpuShareIsStillTaken()
        {
            GpuWorld world = World(new DateTime(2005, 3, 1));
            world.Founded();
            world.Game.PlayerCpuShare = 0.1f;
            world.Synergy.DayTick();   // no day of sales has closed: the market has no window

            GpuSynergyEntry entry = EntryOf(world, Player);
            Assert.NotNull(entry);
            Assert.Equal(0, entry.gpuShareDaily.Count, "no cards were sold: no share to speak of (not a share of 0)");
            Assert.Equal(1, entry.cpuShareDaily.Count);
            Assert.Near(0.1, entry.cpuShareDaily[0], 1e-6);
        }

        [Test]
        public void ADayThatClosedWithNoSale_IsNoShareOfZero_InTheRingOfTheCards()
        {
            GpuWorld world = World(new DateTime(2005, 3, 1));
            world.Founded();
            world.Game.PlayerCpuShare = 0.1f;
            world.Market.DayTick();   // a day closed, and nothing was sold in it
            float share;
            Assert.False(world.Market.TryLastDayShare("player", out share), "no card sold on the last day: no share");
            world.Synergy.DayTick();

            GpuSynergyEntry entry = EntryOf(world, Player);
            Assert.Equal(0, entry.gpuShareDaily.Count, "the ring of the cards got nothing");
            Assert.Equal(1, entry.cpuShareDaily.Count, "the ring of the CPUs got the share of the day");
        }

        // ---- AC-15.2 and the conditions ----

        [Test]
        public void Ac152_WhenThePlayerTakesAllTheCardsOff_BothBonusesAreOneOnThe1st()
        {
            GpuWorld world = Playing(new DateTime(2005, 3, 1));
            world.Game.PlayerCpuShare = 0.3f;
            Days(world, 45);
            world.Game.Date = new DateTime(2005, 5, 1);
            world.Synergy.MonthTick();
            Assert.True(world.Synergy.ViewFor(Player).Active, "a bonus to begin with");

            foreach (GpuProductSave p in world.State.Data.products.Where(x => x.owner == GpuState.OwnerPlayer).ToList()) world.Lineup.Retire(p.id);
            Days(world, 3);
            world.Game.Date = new DateTime(2005, 6, 1);
            world.Synergy.MonthTick();

            GpuSynergyView view = world.Synergy.ViewFor(Player);
            Assert.False(view.Active);
            Assert.False(view.Eligible, "no card on sale");
            Assert.Equal(0f, view.CpuBonus);
            Assert.Equal(0f, view.GpuBonus);
            Assert.Equal(1f, GpuSynergyTable.CpuResult(world.Synergy.Table.Lookup(Player), true).Multiplier, "the provider on market.rating.cpu answers x1.0");
            Assert.Equal(1f, GpuSynergyTable.GpuResult(world.Synergy.Table.Lookup(Player), true).Multiplier, "the provider on gpu.rating answers x1.0");
        }

        [Test]
        public void ACardThatDoesNotSell_OrNoCpuOnSale_GivesNoBonus()
        {
            GpuWorld world = Playing(new DateTime(2005, 3, 1));
            world.Game.PlayerCpuShare = 0.3f;
            Days(world, 45);
            world.Game.Date = new DateTime(2005, 5, 1);

            world.Game.PlayerHasCpu = false;   // FR-15 item 3: no CPU on sale
            world.Synergy.MonthTick();
            Assert.False(world.Synergy.ViewFor(Player).Active, "no CPU on sale");
            Assert.False(world.Synergy.ViewFor(Player).Eligible);

            world.Game.PlayerHasCpu = true;
            world.Synergy.MonthTick();
            Assert.True(world.Synergy.ViewFor(Player).Active, "the CPU is back");

            world.Lineup.SetSelling("g-1", false);   // the card is off the market: it is not on sale
            world.Synergy.MonthTick();
            Assert.False(world.Synergy.ViewFor(Player).Active, "the card does not sell");
            Assert.False(world.Synergy.HasCardOnSale(Player));
        }

        // ---- the AI companies ----

        [Test]
        public void ACompanyOfTheAi_WithABoundVendor_GetsTheSameRule()
        {
            GpuWorld world = World(new DateTime(2007, 1, 1), eras: true, rivals: true);
            world.Market.SeedForYear(2007);
            Assert.True(world.Rivals.Find("amd.gpu").IsActive, "AMD is on the market in 2007");
            Assert.Equal("ai:1", world.Rivals.Find("amd.gpu").CompanyKey);
            world.Game.Companies["ai:1"].CpuShare = 0.25f;
            world.Game.Companies["ai:1"].HasCpu = true;
            world.Game.Companies["ai:0"].HasCpu = true;   // Intel has no vendor on the market in 2007

            Days(world, 40);
            world.Game.Date = new DateTime(2007, 3, 1);
            world.Synergy.MonthTick();

            GpuSynergyEntry amd = EntryOf(world, "ai:1");
            Assert.NotNull(amd, "AMD has a ring: a vendor on the market is bound to it");
            Assert.Equal(30, amd.cpuShareDaily.Count);
            Assert.True(amd.cpuShareDaily.All(v => Math.Abs(v - 0.25f) < 1e-6f));
            float gpuMean = GpuSynergyMath.Mean(amd.gpuShareDaily);
            Assert.True(gpuMean > 0f, "AMD sells cards: " + gpuMean);
            Assert.Near(Math.Min(0.08f, 0.2f * gpuMean), amd.sCpu, 1e-6);
            Assert.Near(Math.Min(0.10f, 0.25f * 0.25f), amd.sGpu, 1e-6, "0.0625");
            Assert.Equal(1.0625f, GpuSynergyTable.GpuResult(world.Synergy.Table.Lookup("ai:1"), true).Multiplier, "the providers answer for the AI company too");

            Assert.Null(EntryOf(world, "ai:0"), "Intel has no vendor on the market: no ring, no bonus");
            Assert.Null(EntryOf(world, "ai:7"));
            Assert.False(world.Synergy.ViewFor("ai:0").Active);
        }

        [Test]
        public void WithTheSwitchRivalsOff_TheAiCompaniesHaveNoCardOnSale_AndNoBonus()
        {
            GpuWorld world = World(new DateTime(2007, 1, 1), eras: true, rivals: true);
            world.Market.SeedForYear(2007);
            world.Game.Companies["ai:1"].CpuShare = 0.25f;
            Days(world, 40);
            world.Game.Date = new DateTime(2007, 3, 1);
            world.Synergy.MonthTick();
            Assert.True(world.Synergy.ViewFor("ai:1").Active, "to begin with");

            world.RivalsOn = false;   // the cards of the vendors do not sell
            world.Synergy.MonthTick();
            Assert.False(world.Synergy.HasCardOnSale("ai:1"));
            Assert.False(world.Synergy.ViewFor("ai:1").Active);
            Assert.Null(EntryOf(world, "ai:1"), "the company is not one that takes part: its ring is dropped");
        }

        [Test]
        public void AVendorThatLeavesTheMarket_LosesTheRingOfItsCompany_OnThe1st()
        {
            GpuWorld world = World(new DateTime(2007, 1, 1), eras: true, rivals: true);
            world.Market.SeedForYear(2007);
            world.Game.Companies["ai:1"].CpuShare = 0.25f;
            Days(world, 5);
            Assert.NotNull(EntryOf(world, "ai:1"));

            world.Rivals.Find("amd.gpu").Save.state = GpuState.VendorExited;
            world.Game.Date = new DateTime(2007, 2, 1);
            world.Synergy.MonthTick();
            Assert.Null(EntryOf(world, "ai:1"));
            Assert.Equal(0, world.Synergy.Table.Lookup("ai:1").SCpu + world.Synergy.Table.Lookup("ai:1").SGpu);
        }

        // ---- the switch ----

        [Test]
        public void WithTheSwitchOff_NothingIsSampledOrCounted_AndTheProvidersAnswerOne()
        {
            GpuWorld world = Playing(new DateTime(2005, 3, 1));
            world.Game.PlayerCpuShare = 0.3f;
            Days(world, 45);
            world.Game.Date = new DateTime(2005, 5, 1);
            world.Synergy.MonthTick();
            GpuSynergyEntry entry = EntryOf(world, Player);
            float bonus = entry.sCpu;
            Assert.True(bonus > 0f);
            int days = entry.gpuShareDaily.Count;

            world.SynergyOn = false;
            Assert.False(world.Synergy.IsEnabled);
            world.Synergy.DayTick();
            world.Synergy.MonthTick();
            Assert.Equal(days, entry.gpuShareDaily.Count, "no sample while the switch is off");
            Assert.Equal(bonus, entry.sCpu, "the count of the month does not run");
            Assert.False(world.Synergy.ViewFor(Player).Active, "the window shows no bonus");
            Assert.Equal(0f, world.Synergy.ViewFor(Player).CpuBonus);
            Assert.Equal(1f, GpuSynergyTable.CpuResult(world.Synergy.Table.Lookup(Player), world.Synergy.IsEnabled).Multiplier, "x1.0 the same frame");
            Assert.Equal(1f, GpuSynergyTable.GpuResult(world.Synergy.Table.Lookup(Player), world.Synergy.IsEnabled).Multiplier);

            world.SynergyOn = true;
            world.Synergy.DayTick();   // the first day back: the rings begin anew
            Assert.Equal(1, entry.gpuShareDaily.Count, "the days before the switch went off are not the last 30 days");
            Assert.Equal(1, entry.cpuShareDaily.Count);
            Assert.True(world.Synergy.ViewFor(Player).Active, "the bonus of the last count comes back with the switch");
        }

        [Test]
        public void TheChangedEvent_IsRaisedByTheMonthlyCount_AndTheTableIsRebuiltFirst()
        {
            GpuWorld world = Playing(new DateTime(2005, 3, 1));
            world.Game.PlayerCpuShare = 0.3f;
            Days(world, 45);
            int raised = 0;
            float seen = -1f;
            world.Synergy.Changed += () =>
            {
                raised++;
                seen = world.Synergy.Table.Lookup(Player).SGpu;   // the table is already the new one
            };
            world.Game.Date = new DateTime(2005, 5, 1);
            world.Synergy.MonthTick();
            Assert.Equal(1, raised);
            Assert.Near(0.075, seen, 1e-6, "0.25 x 0.3");

            world.SynergyOn = false;
            world.Synergy.MonthTick();
            Assert.Equal(1, raised, "nothing was counted, nothing changed");
        }

        // ---- AC-15.3: the providers in the engine of the core ----

        private static ChannelRegistry CoreRegistry(DateTime today)
        {
            return GpuCoreRegistry.Create(today);
        }

        [Test]
        public void Ac153_TheSynergyStaysX108InTheBreakdown_WhileTheTotalIsCutToTwo()
        {
            GpuWorld world = Playing(new DateTime(2005, 3, 20));
            world.State.Data.synergy.entries.Add(new GpuSynergyEntry
            {
                companyKey = Player, gpuShareDaily = Enumerable.Repeat(0.9f, 30).ToList(), cpuShareDaily = Enumerable.Repeat(0.9f, 30).ToList(),
            });
            world.Game.Date = new DateTime(2005, 4, 1);
            world.Synergy.MonthTick();
            Assert.Near(0.08, world.Synergy.Table.Lookup(Player).SCpu, 1e-6, "the cap of the feature");

            ChannelRegistry registry = CoreRegistry(world.Game.Date);
            Func<ModifierQuery, ProviderResult> provider = q => GpuSynergyTable.CpuResult(world.Synergy.Table.Lookup(q.CompanyKey), world.Synergy.IsEnabled);
            registry.RegisterReasonedProvider("gpu-product-line", "market.rating.cpu", provider);
            registry.Add("cpu-architectures", "market.rating.cpu", ModifierScope.Player(), 1.90f);   // the other suppliers of the channel

            var query = new ModifierQuery(Player, 7, Segment.HighEnd);
            Assert.Near(2.0, registry.Get("market.rating.cpu", query), 1e-5, "1.90 x 1.08 = 2.052 is cut to the 2.0 of the core");
            ModifierBreakdown explain = registry.Explain("market.rating.cpu", query);
            Assert.True(explain.IsBalanceCapped, "the core says: capped");
            Assert.Near(2.0, explain.Total, 1e-5);

            AppliedModifier mine = default(AppliedModifier);
            foreach (AppliedModifier item in explain)
            {
                if (item.FeatureId == "gpu-product-line") mine = item;
            }
            Assert.Equal("gpu-product-line", mine.FeatureId);
            Assert.Near(1.08, mine.Multiplier, 1e-5, "the contribution of the synergy in the breakdown is still x1.08");
            Assert.Equal("gpu.synergy.cpu", mine.ReasonKey);

            // the switch goes off: the same frame after Invalidate, the contribution is gone
            world.SynergyOn = false;
            registry.Invalidate();
            Assert.Near(1.90, registry.Get("market.rating.cpu", query), 1e-5, "x1.0 from the synergy");
            Assert.True(registry.Explain("market.rating.cpu", query).All(i => i.FeatureId != "gpu-product-line"), "a provider that answers 1 is not in the breakdown");
        }

        [Test]
        public void TheContribution_ToGpuRating_IsAtMostX110_AndTheChannelHasNoBalanceRange()
        {
            GpuWorld world = Playing(new DateTime(2005, 3, 20));
            world.State.Data.synergy.entries.Add(new GpuSynergyEntry
            {
                companyKey = Player, gpuShareDaily = Enumerable.Repeat(1f, 30).ToList(), cpuShareDaily = Enumerable.Repeat(1f, 30).ToList(),
            });
            world.Game.Date = new DateTime(2005, 4, 1);
            world.Synergy.MonthTick();

            ChannelRegistry registry = CoreRegistry(world.Game.Date);
            registry.RegisterReasonedProvider("gpu-product-line", "gpu.rating", q => GpuSynergyTable.GpuResult(world.Synergy.Table.Lookup(q.CompanyKey), world.Synergy.IsEnabled));
            var query = new ModifierQuery(Player);
            Assert.Near(1.10, registry.Get("gpu.rating", query), 1e-5, "the whole market of the CPUs gives the cap, +10 %");
            registry.Add("historical-events", "gpu.rating", ModifierScope.Player(), 1.5f);
            Assert.Near(1.65, registry.Get("gpu.rating", query), 1e-5, "1.5 x 1.10: no balance range, the fuse is 0.25...4.0");
            Assert.False(registry.Explain("gpu.rating", query).IsBalanceCapped);
        }

        // ---- no entries of ModModifiers ----

        [Test]
        public void TheFeature_WritesNoEntry_NorDeclaresAChannel()
        {
            string src = GpuFiles.FeatureSourceDir;
            var offenders = new List<string>();
            foreach (string path in SourceScan.Files(src, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                if (Regex.IsMatch(code, @"ModModifiers\s*\.\s*(Add|Remove|RemoveAll|DeclareChannel)\s*\(")) offenders.Add(SourceScan.Relative(src, path));
            }
            Assert.Equal(0, offenders.Count, "FR-15: the providers are answers computed on request, the feature writes no entry: " + string.Join(", ", offenders.ToArray()));

            string bridge = SourceScan.Strip(SourceScan.ReadText(Path.Combine(src, "Bridges", "GpuModifierBridge.cs")), false);
            Assert.True(bridge.Contains("RegisterReasonedProvider(GpuFeature.FeatureId, ModChannels.MarketRatingCpu"), "the provider on market.rating.cpu");
            Assert.True(bridge.Contains("RegisterReasonedProvider(GpuFeature.FeatureId, ModChannels.GpuRating"), "the provider on gpu.rating");
        }

        // ---- the block of the tab "Рынок" (FR-15 item 5) ----

        private static GpuSynergyBlock Block(GpuWorld world, Func<GpuSynergyChannel, GpuExplainView> explain = null)
        {
            return GpuSynergyBlock.Build(world.Content, world.Synergy, world.Game, new TestGpuText(), explain);
        }

        [Test]
        public void TheBlock_SaysWhatTheSynergyGives_ForTheNumbersOfAc151()
        {
            GpuWorld world = Playing(new DateTime(2005, 3, 20));
            world.State.Data.synergy.entries.Add(new GpuSynergyEntry
            {
                companyKey = Player, gpuShareDaily = Enumerable.Repeat(0.4f, 30).ToList(), cpuShareDaily = Enumerable.Repeat(0.2f, 30).ToList(),
            });
            world.Game.Date = new DateTime(2005, 4, 1);
            world.Synergy.MonthTick();

            GpuSynergyBlock block = Block(world);
            Assert.True(block.Active && block.Enabled && block.Eligible);
            Assert.True(block.StatusText.Contains("+8") && block.StatusText.Contains("+5"), "the window says +8 % to the CPUs and +5 % to the cards: " + block.StatusText);
            Assert.True(block.CpuLine.Contains("40") && block.CpuLine.Contains("0.2") && block.CpuLine.Contains("8"), "the formula with the share of the cards, the factor and the cap: " + block.CpuLine);
            Assert.True(block.GpuLine.Contains("20") && block.GpuLine.Contains("0.25") && block.GpuLine.Contains("10"), block.GpuLine);
            Assert.True(block.FormulaText.Length > 20 && block.FormulaText.Contains("8") && block.FormulaText.Contains("10"));
            Assert.Equal(block.Title, new TestGpuText().L("gpu.market.synergy.title"));
        }

        [Test]
        public void TheBlock_TellsWhyThereIsNoBonus()
        {
            var text = new TestGpuText();
            GpuWorld world = Playing(new DateTime(2005, 3, 20));

            world.Game.PlayerHasCpu = false;
            Assert.Equal(text.L("gpu.market.synergy.status.needCpu"), Block(world).StatusText, "no CPU on sale");
            world.Game.PlayerHasCpu = true;

            world.Lineup.SetSelling("g-1", false);
            Assert.Equal(text.L("gpu.market.synergy.status.needCard"), Block(world).StatusText, "no card on sale");
            world.Lineup.SetSelling("g-1", true);

            Assert.Equal(text.L("gpu.market.synergy.status.zero"), Block(world).StatusText, "both on sale and nothing counted yet");

            world.SynergyOn = false;
            GpuSynergyBlock off = Block(world);
            Assert.Equal(text.L("gpu.market.synergy.status.off"), off.StatusText, "the switch is off");
            Assert.False(off.Enabled);
        }

        [Test]
        public void TheBreakdown_ListsEveryMultiplier_WithTheCappedMark()
        {
            var text = new TestGpuText();
            GpuWorld world = Playing(new DateTime(2005, 3, 20));
            var capped = new GpuExplainView { Total = 2.0f, Capped = true };
            capped.Rows.Add(new GpuExplainRow { FeatureId = "cpu-architectures", Multiplier = 1.9f });
            capped.Rows.Add(new GpuExplainRow { FeatureId = "gpu-product-line", ReasonKey = "gpu.synergy.cpu", ReasonArgs = new[] { "8" }, Multiplier = 1.08f });
            var plain = new GpuExplainView { Total = 1.1f };

            GpuSynergyBlock block = Block(world, channel => channel == GpuSynergyChannel.Cpu ? capped : plain);
            string[] cpu = block.CpuExplain.Split('\n');
            Assert.Equal(3, cpu.Length, "the total and two multipliers: " + block.CpuExplain);
            Assert.True(cpu[0].Contains("2") && cpu[0].Contains(text.L("gpu.market.synergy.explain.capped")), "the total is marked capped: " + cpu[0]);
            Assert.True(cpu[1].Contains("1.9") && cpu[1].Contains("cpu-architectures"), "a reason that has no text is the feature: " + cpu[1]);
            Assert.True(cpu[2].Contains("1.08") && cpu[2].Contains(text.L("gpu.synergy.cpu", "8")), "the synergy is x1.08 in the breakdown: " + cpu[2]);

            string[] gpu = block.GpuExplain.Split('\n');
            Assert.Equal(2, gpu.Length);
            Assert.False(gpu[0].Contains(text.L("gpu.market.synergy.explain.capped")), "not capped");
            Assert.Equal(text.L("gpu.market.synergy.explain.empty"), gpu[1], "no other multipliers");
        }

        [Test]
        public void TheBreakdown_IsWordsOnly_WhenTheCoreCannotGiveIt()
        {
            var text = new TestGpuText();
            GpuWorld world = Playing(new DateTime(2005, 3, 20));
            Assert.Equal(text.L("gpu.market.synergy.explain.none"), Block(world, null).CpuExplain, "no source of breakdowns");
            Assert.Equal(text.L("gpu.market.synergy.explain.none"), Block(world, c => null).GpuExplain, "a source that gives nothing");
            GpuSynergyBlock thrown = Block(world, c => { throw new InvalidOperationException("a provider of another feature failed"); });
            Assert.Equal(text.L("gpu.market.synergy.explain.none"), thrown.CpuExplain, "an exception does not break the tab");
        }

        [Test]
        public void TheTabModel_CarriesTheBlock_OnlyWhenAskedFor()
        {
            GpuWorld world = Playing(new DateTime(2005, 3, 20));
            var text = new TestGpuText();
            GpuMarketTabModel without = GpuMarketTabModel.Build(world.Content, world.Market, world.State, world.Game, text, world.Rivals, world.Eras);
            Assert.Null(without.Synergy, "the tab of the earlier stages");
            GpuMarketTabModel with = GpuMarketTabModel.Build(world.Content, world.Market, world.State, world.Game, text, world.Rivals, world.Eras, world.Synergy, c => new GpuExplainView());
            Assert.NotNull(with.Synergy);
            Assert.Equal(text.L("gpu.market.synergy.title"), with.Synergy.Title);
        }

        // ---- the section and the table ----

        [Test]
        public void TheRingsAndTheBonuses_AreInTheSection_AndComeBackFromASave()
        {
            GpuWorld world = Playing(new DateTime(2005, 3, 1));
            world.Game.PlayerCpuShare = 0.3f;
            Days(world, 45);
            world.Game.Date = new DateTime(2005, 5, 1);
            world.Synergy.MonthTick();
            GpuSynergyEntry before = EntryOf(world, Player);

            var loaded = new GpuState(world.Content);
            loaded.Read(world.State.Write(), GpuState.SchemaVersion);
            GpuSynergyEntry after = loaded.Data.synergy.entries.Find(e => e.companyKey == Player);
            Assert.NotNull(after, "the ring is saved");
            Assert.Equal(before.gpuShareDaily.Count, after.gpuShareDaily.Count);
            Assert.Near(before.sCpu, after.sCpu, 1e-6);
            Assert.Near(before.sGpu, after.sGpu, 1e-6);

            var table = new GpuSynergyTable();
            table.Rebuild(loaded.Data.synergy.entries, world.Content.Balance.synergy);
            Assert.Near(before.sCpu, table.Lookup(Player).SCpu, 1e-6, "the table the providers read comes back from the section after a load");
        }

        [Test]
        public void TheEntries_AreLimited_AndTheServiceNeedsItsParts()
        {
            GpuWorld world = World(new DateTime(2005, 3, 1));
            for (int i = 0; i < GpuSynergy.MaxEntries + 5; i++) world.State.Data.synergy.entries.Add(new GpuSynergyEntry { companyKey = "ai:" + (100 + i) });
            world.Synergy.DayTick();
            Assert.Null(EntryOf(world, Player), "the list is full: no ring for the player (the load cuts the section at 256)");

            Assert.Throws<ArgumentNullException>(() => new GpuSynergy(null, world.State, world.Game, world.Market, null, null));
            Assert.Throws<ArgumentNullException>(() => new GpuSynergy(world.Content, null, world.Game, world.Market, null, null));
            Assert.Throws<ArgumentNullException>(() => new GpuSynergy(world.Content, world.State, null, world.Market, null, null));
            Assert.Throws<ArgumentNullException>(() => new GpuSynergy(world.Content, world.State, world.Game, null, null, null));
            Assert.False(world.Synergy.ViewFor(null).Active);
            Assert.False(world.Synergy.ViewFor(string.Empty).Eligible);
            Assert.False(world.Synergy.IsEligible(null));
        }

        // ---- AC-16.1: the shared experience of a process node ----

        // The formula of the game: ProductionMath.NodeOptimizationRate = 0.1 x sqrt(lines / LearningRequirement) + 0.035 (before the learning progress of the node enters).
        private static double LearningRate(int lines, double requirement)
        {
            return 0.1 * Math.Sqrt(lines / requirement) + 0.035;
        }

        [Test]
        public void Ac161_FourLinesOfCpusAndFourOfCards_TeachTheNodeAsEight()
        {
            GpuWorld world = World(new DateTime(2005, 6, 1));
            world.Founded("arch.shader");
            world.Market.SeedForYear(2005);
            GpuProductSave card = world.AddCard("g-1", 4);
            card.node = "90nm";
            world.Production.ProduceDay();
            Assert.Equal(4, world.Production.ReservedOn("90nm"), "the cards took 4 lines on 90 nm today");

            int cpuLines = 4;
            int gameResult = cpuLines;   // what Company.TotalLinesAllocated gives: the lines of the CPUs
            int total = gameResult + world.SharedLearning.ExtraLines("90nm");
            Assert.Equal(8, total, "the learning is counted from 8 lines, not 4");

            const double requirement = 100.0;
            double learnt = LearningRate(total, requirement), alone = LearningRate(cpuLines, requirement);
            Assert.True(learnt > alone, "the node is learnt faster");
            Assert.Near(0.1 * Math.Sqrt(8 / requirement) + 0.035, learnt, 1e-12);
            Assert.Near(Math.Sqrt(2), (learnt - 0.035) / (alone - 0.035), 1e-9, "the root of the lines: 8 against 4 is x1.414");
        }

        [Test]
        public void TheSharedLines_AreOfTheNodeOfTheCards_AndOfToday_AndNeedTheSwitch()
        {
            GpuWorld world = World(new DateTime(2005, 6, 1));
            world.Founded("arch.shader");
            world.Market.SeedForYear(2005);
            GpuProductSave card = world.AddCard("g-1", 6);
            card.node = "90nm";
            world.Production.ProduceDay();

            Assert.Equal(6, world.SharedLearning.ExtraLines("90nm"));
            Assert.Equal(0, world.SharedLearning.ExtraLines("350nm"), "the cards made nothing on another node");
            Assert.Equal(0, world.SharedLearning.ExtraLines(null));
            Assert.Equal(0, world.SharedLearning.ExtraLines(string.Empty));

            world.SynergyOn = false;
            Assert.Equal(0, world.SharedLearning.ExtraLines("90nm"), "FR-16 item 2: the switch is off, the addition is 0");
            world.SynergyOn = true;

            world.NextDay();
            Assert.Equal(0, world.SharedLearning.ExtraLines("90nm"), "no reserve today yet: another day");
        }

        [Test]
        public void TheSharedLines_FollowTheReserveAfterTheCut_AndTheShareOfTheBalance()
        {
            GpuWorld world = World(new DateTime(2005, 6, 1));
            world.Founded("arch.shader");
            world.Market.SeedForYear(2005);
            world.Game.Factory.CapacityLines = 10;
            world.Game.Factory.Clients = 6;   // 4 lines are left
            GpuProductSave card = world.AddCard("g-1", 9);
            card.node = "90nm";
            world.Production.ProduceDay();
            Assert.Equal(4, world.Production.ReservedToday, "F28: the cards got what was left");
            Assert.Equal(4, world.SharedLearning.ExtraLines("90nm"), "the lines the cards really used count, not those they asked for");

            world.Content.Balance.synergy.gpuLearningShare = 0.5f;
            try
            {
                Assert.Equal(2, world.SharedLearning.ExtraLines("90nm"), "gpuLearningShare is a number of the balance");
            }
            finally
            {
                world.Content.Balance.synergy.gpuLearningShare = 1.0f;
            }
        }

        [Test]
        public void ThePatchGp2_IsOptional_ForThePlayerOnly_AndFailsOnlyItsOwnAbility()
        {
            string src = GpuFiles.FeatureSourceDir;
            string patch = SourceScan.Strip(SourceScan.ReadText(Path.Combine(src, "Patches", "TotalLinesAllocatedPatch.cs")), false);
            Assert.True(Regex.IsMatch(patch, @"\[ModPatch\(typeof\(GameCompany\), ""TotalLinesAllocated"", PatchKind\.Postfix, Optional = true\)\]"), "a postfix on Company.TotalLinesAllocated, optional");
            Assert.True(patch.Contains("ModCompanies.IsPlayer(__instance)"), "the player's company only");
            Assert.True(patch.Contains("ref int __result"), "the result of the game is the one that grows");
            Assert.True(patch.Contains("ModHooks.ReportFault(GpuFeature.FeatureId, Site, ex)"), "a failure is counted at the border of the hook");
            Assert.True(Regex.IsMatch(patch, @"__result\s*=\s*\(int\)Math\.Min\(int\.MaxValue, \(long\)__result \+ extra\)"), "the lines are added, without overflow");

            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(src, "GpuFeature.cs")), false);
            Assert.True(feature.Contains("ctx.Patch(typeof(TotalLinesAllocatedPatch))"), "registered with ctx.Patch");
            Assert.True(Regex.IsMatch(feature, @"new GpuSharedLearning\(Content, Production, \(\) => SynergyEnabled && IsAvailable\(GpuCapability\.SharedLearning\)\)"), "off with the switch and with the ability");

            string check = SourceScan.Strip(SourceScan.ReadText(Path.Combine(src, "GpuSelfCheck.cs")), false);
            Assert.True(check.Contains("GpuCapability.SharedLearning"), "a patch that is not applied switches off this ability only");
            Assert.True(check.Contains("Company.TotalLinesAllocated"), "the self-check looks for the target of GP2");
        }
    }
}
