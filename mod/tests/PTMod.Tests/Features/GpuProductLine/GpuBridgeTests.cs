using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Modifiers;
using PTMod.Features.GpuProductLine;

namespace PTMod.Tests.Features.GpuProductLine
{
    /// <summary>
    /// The reading half of <c>GpuModifierBridge</c> and the synergy table (TSK-012, FR-18, FR-15) on the core's own channel engine (<see cref="ChannelRegistry"/>) with
    /// the declarations of the built-in <c>core.json</c>: the GPU channels are declared by the core (the family <c>gpu.demand.*</c> covers every segment), a channel
    /// without contributions reads 1.0 and several contributions multiply, the question carries the company and nothing else (no CPU, any segment), no company is the
    /// global question, <c>gpu.demand</c> times <c>gpu.demand.&lt;segment&gt;</c> is the demand of a segment (AC-18.1: x1.5 for 90 days), and the providers of the synergy
    /// answer x1.0 when the switch is off or the company has no bonus.
    /// </summary>
    [Covers(typeof(GpuChannelReader), typeof(GpuChannelNames), typeof(IGpuChannels), typeof(GpuSynergyTable), typeof(SynergyBonus), typeof(IGpuGame),
        typeof(GpuExpense), typeof(GpuRevenue), typeof(GpuModifierKind))]
    public class GpuBridgeTests
    {
        private static readonly string[] GpuChannels = { "gpu.demand", "gpu.demand.*", "gpu.rating", "gpu.vramCost" };
        private static readonly string[] StandardChannels = { "production.unitCost", "production.output", "popularity.gain", "market.rating.cpu" };

        private static readonly DateTime Today = new DateTime(2017, 6, 1);

        /// <summary>The channels of the built-in core.json, declared in a fresh registry the way ModModifiers declares them at start.</summary>
        private static ChannelRegistry CoreRegistry()
        {
            Assert.NotNull(TestEnv.SrcDir, "source folder not found");
            string path = Path.Combine(TestEnv.SrcDir, "Content", "mod-framework", "core.json");
            JsonObject root = GpuFiles.ParseObject(File.ReadAllText(path, new UTF8Encoding(false)), "core.json");
            var registry = new ChannelRegistry(null, Today);
            foreach (JsonNode node in root.GetArray("channels"))
            {
                var entry = (JsonObject)node;
                var spec = new ChannelSpec
                {
                    Name = entry.GetString("name"),
                    Min = (float)entry.GetDouble("min"),
                    Max = (float)entry.GetDouble("max"),
                    BalanceMin = (float)entry.GetDouble("balanceMin"),
                    BalanceMax = (float)entry.GetDouble("balanceMax"),
                    Global = entry.GetBool("global"),
                    PerCompany = entry.GetBool("perCompany"),
                    PerCpu = entry.GetBool("perCpu"),
                    PerSegment = entry.GetBool("perSegment"),
                    OwnerFeature = entry.GetString("owner"),
                };
                Assert.True(registry.Declare(spec), "core.json declares " + spec.Name);
            }
            return registry;
        }

        private static GpuChannelNames Names()
        {
            return new GpuChannelNames
            {
                UnitCost = "production.unitCost", Output = "production.output", VramCost = "gpu.vramCost", Rating = "gpu.rating",
                PopularityGain = "popularity.gain", Demand = "gpu.demand",
            };
        }

        private static GpuChannelReader Reader(ChannelRegistry registry, List<ModifierQuery> asked = null)
        {
            return new GpuChannelReader((channel, key) =>
            {
                ModifierQuery query = GpuChannelReader.QueryFor(key);
                if (asked != null) asked.Add(query);
                return registry.Get(channel, query);
            }, Names());
        }

        [Test]
        public void TheGpuChannels_AreDeclaredByTheCore_ForTheFeature()
        {
            ChannelRegistry registry = CoreRegistry();
            foreach (string name in GpuChannels)
            {
                ChannelSpec spec = registry.SpecOf(name);
                Assert.NotNull(spec, name + " is in core.json");
                Assert.Equal("gpu-product-line", spec.OwnerFeature, name);
            }
            Assert.True(registry.IsDeclared("gpu.demand.consumer"), "the family covers the segments of the content");
            Assert.True(registry.IsDeclared(ChannelFamilies.GpuDemand("gaming.high")), "and those of stage 2");
            foreach (string name in StandardChannels) Assert.True(registry.IsDeclared(name), name);

            // the fuses of design.md
            Assert.Equal(0.1f, registry.SpecOf("gpu.demand").Min);
            Assert.Equal(3.0f, registry.SpecOf("gpu.demand.consumer").Max);
            Assert.Equal(0.25f, registry.SpecOf("gpu.rating").Min);
            Assert.Equal(4.0f, registry.SpecOf("gpu.rating").Max);
            Assert.Equal(0.5f, registry.SpecOf("gpu.vramCost").Min);
            Assert.Equal(2.0f, registry.SpecOf("gpu.vramCost").Max);
        }

        [Test]
        public void AChannelWithoutContributions_ReadsOne_AndSeveralMultiply()
        {
            ChannelRegistry registry = CoreRegistry();
            GpuChannelReader reader = Reader(registry);
            Assert.Equal(1f, reader.Rating("player"));
            Assert.Equal(1f, reader.UnitCost("ai:1"));
            Assert.Equal(1f, reader.Output(null));
            Assert.Equal(1f, reader.PopularityGain("player"));
            Assert.Equal(1f, reader.VramCost("player"));
            Assert.Equal(1f, reader.Demand("consumer"));

            registry.Add("historical-events", "gpu.rating", ModifierScope.Player(), 1.2f);
            registry.Add("market-shocks", "gpu.rating", ModifierScope.AllCompanies(), 1.5f);
            Assert.Near(1.8, reader.Rating("player"), 1e-5, "1.2 x 1.5");
            Assert.Near(1.5, reader.Rating("ai:3"), 1e-5, "only the entry for all companies");

            registry.Add("market-shocks", "gpu.rating", ModifierScope.Player(), 3f);
            Assert.Near(4.0, reader.Rating("player"), 1e-5, "the fuse of gpu.rating holds 5.4 to 4.0");
        }

        [Test]
        public void TheQuestion_CarriesTheCompany_AndNoCpuNorSegment()
        {
            ChannelRegistry registry = CoreRegistry();
            var asked = new List<ModifierQuery>();
            GpuChannelReader reader = Reader(registry, asked);

            registry.Add("press-reviews", "popularity.gain", ModifierScope.Cpu(42), 1.6f);
            registry.Add("press-reviews", "popularity.gain", ModifierScope.Player(Segment.HighEnd), 1.4f);
            registry.Add("press-reviews", "popularity.gain", ModifierScope.Player(), 1.25f);
            Assert.Near(1.25, reader.PopularityGain("player"), 1e-5, "entries of a CPU or of a CPU segment do not apply to the cards");

            ModifierQuery query = asked[asked.Count - 1];
            Assert.Equal("player", query.CompanyKey);
            Assert.Equal(ModifierQuery.None, query.CpuSaveId);
            Assert.Equal(Segment.Any, query.Segment);

            ModifierQuery global = GpuChannelReader.QueryFor(null);
            Assert.Null(global.CompanyKey, "no company: the global question");
            Assert.Null(GpuChannelReader.QueryFor(string.Empty).CompanyKey);
        }

        [Test]
        public void TheGlobalQuestion_SeesOnlyGlobalEntries()
        {
            ChannelRegistry registry = CoreRegistry();
            GpuChannelReader reader = Reader(registry);
            registry.Add("market-shocks", "gpu.vramCost", ModifierScope.Global(), 1.3f);
            registry.Add("market-shocks", "gpu.vramCost", ModifierScope.Player(), 1.5f);
            Assert.Near(1.3, reader.VramCost(null), 1e-5, "a virtual vendor or the background");
            Assert.Near(1.95, reader.VramCost("player"), 1e-5, "the player: global and own");
        }

        [Test]
        public void Ac181_TheDemandOfASegment_IsTheProductOfBothChannels_ForNinetyDays()
        {
            ChannelRegistry registry = CoreRegistry();
            GpuChannelReader reader = Reader(registry);
            registry.Add("historical-events", "gpu.demand", ModifierScope.Global(), 1.5f, 90);
            registry.Add("event-system", "gpu.demand.consumer", ModifierScope.Global(), 1.2f);

            Assert.Near(1.8, reader.Demand("consumer"), 1e-5, "1.5 x 1.2");
            Assert.Near(1.5, reader.Demand("gaming.high"), 1e-5, "another segment: only the common channel");

            registry.SetToday(Today.AddDays(89));
            Assert.Near(1.5, reader.Demand("gaming.high"), 1e-5, "the 90th day still has it");
            registry.SetToday(Today.AddDays(90));
            Assert.Near(1.0, reader.Demand("gaming.high"), 1e-5, "after 90 days it is gone");

            Assert.Near(1.2, reader.Demand("consumer"), 1e-5);
            Assert.Equal(1f, reader.Demand(null) / reader.Get("gpu.demand", null), "a segment without an id adds nothing");
            Assert.Equal(1f, reader.Demand("bad id with spaces"));
        }

        [Test]
        public void AnUnusableAnswer_CountsAsOne()
        {
            GpuChannelReader reader = new GpuChannelReader((channel, key) => channel == "gpu.rating" ? float.NaN : channel == "production.output" ? -2f : 0.5f, Names());
            Assert.Equal(1f, reader.Rating("player"), "NaN");
            Assert.Equal(1f, reader.Output("player"), "negative");
            Assert.Equal(0.5f, reader.UnitCost("player"));
            Assert.Equal(1f, reader.Get(null, "player"), "no channel name");
            Assert.Equal(1f, GpuChannelReader.Usable(float.PositiveInfinity));
            Assert.Equal(0f, GpuChannelReader.Usable(0f), "0 is a usable multiplier");
        }

        [Test]
        public void TheSynergyProviders_AnswerOneWhenOff_OrWithoutABonus()
        {
            var table = new GpuSynergyTable();
            var caps = new GpuBalanceDto.SynergyBlock();
            table.Rebuild(new[]
            {
                new GpuSynergyEntry { companyKey = "player", sCpu = 0.04f, sGpu = 0.02f },
                new GpuSynergyEntry { companyKey = "ai:1", sCpu = 0.5f, sGpu = 0.5f },
                new GpuSynergyEntry { companyKey = "ai:2", sCpu = 0f, sGpu = 0f },
                new GpuSynergyEntry { companyKey = "ai:3", sCpu = float.NaN, sGpu = -1f },
                null,
                new GpuSynergyEntry { companyKey = "", sCpu = 0.05f },
            }, caps);
            Assert.Equal(2, table.Count, "the player and ai:1; zeros, broken numbers and entries without a key are left out");

            ProviderResult cpu = GpuSynergyTable.CpuResult(table.Lookup("player"), true);
            Assert.Near(1.04, cpu.Multiplier, 1e-6);
            Assert.Equal("gpu.synergy.cpu", cpu.ReasonKey);
            Assert.Equal(new[] { "4" }, cpu.ReasonArgs);
            ProviderResult gpu = GpuSynergyTable.GpuResult(table.Lookup("player"), true);
            Assert.Near(1.02, gpu.Multiplier, 1e-6);
            Assert.Equal("gpu.synergy.gpu", gpu.ReasonKey);

            Assert.Near(1.08, GpuSynergyTable.CpuResult(table.Lookup("ai:1"), true).Multiplier, 1e-6, "held to synergy.cpuMax");
            Assert.Near(1.10, GpuSynergyTable.GpuResult(table.Lookup("ai:1"), true).Multiplier, 1e-6, "held to synergy.gpuMax");

            Assert.Equal(1f, GpuSynergyTable.CpuResult(table.Lookup("player"), false).Multiplier, "the switch synergy is off");
            Assert.Null(GpuSynergyTable.CpuResult(table.Lookup("player"), false).ReasonKey);
            Assert.Equal(1f, GpuSynergyTable.GpuResult(table.Lookup("ai:7"), true).Multiplier, "no entry");
            Assert.Equal(1f, GpuSynergyTable.CpuResult(table.Lookup(null), true).Multiplier, "no company");

            table.Rebuild(null, caps);
            Assert.Equal(0, table.Count);
        }

        [Test]
        public void TheSynergyProvider_InTheCoresEngine_IsRegisteredOnce_AndCapped()
        {
            ChannelRegistry registry = CoreRegistry();
            var table = new GpuSynergyTable();
            table.Rebuild(new[] { new GpuSynergyEntry { companyKey = "player", sCpu = 0.08f, sGpu = 0.1f } }, new GpuBalanceDto.SynergyBlock());
            bool enabled = true;
            Func<ModifierQuery, ProviderResult> provider = q => GpuSynergyTable.CpuResult(table.Lookup(q.CompanyKey), enabled);

            Assert.True(registry.RegisterReasonedProvider("gpu-product-line", "market.rating.cpu", provider));
            registry.RegisterReasonedProvider("gpu-product-line", "market.rating.cpu", provider);
            Assert.Equal(1, registry.ProvidersOf("gpu-product-line", "market.rating.cpu"), "the same delegate again is ignored by the core");

            var query = new ModifierQuery("player", 7, Segment.HighEnd);
            Assert.Near(1.08, registry.Get("market.rating.cpu", query), 1e-5);
            registry.Add("cpu-architectures", "market.rating.cpu", ModifierScope.Player(), 2f);
            registry.Invalidate();
            Assert.Near(2.0, registry.Get("market.rating.cpu", query), 1e-5, "with another feature's x2 the balance range of the core holds the product to 2.0");
            Assert.True(registry.Explain("market.rating.cpu", query).IsBalanceCapped);

            enabled = false;
            registry.Invalidate();
            Assert.Near(2.0, registry.Get("market.rating.cpu", query), 1e-5);
            registry.RemoveAll("cpu-architectures");
            Assert.Near(1.0, registry.Get("market.rating.cpu", query), 1e-5, "switched off: x1.0");
        }

        [Test]
        public void TheFakeGame_IsAFullGame_ForTheServices()
        {
            // the interface the services see: money by article, modifiers by kind, the numbers of the game's enums
            IGpuGame game = new FakeGpuGame();
            Assert.Equal(0, (int)GpuExpense.Factories);
            Assert.Equal(1, (int)GpuExpense.Outsourcing);
            Assert.Equal(2, (int)GpuExpense.Royalties);
            Assert.Equal(3, (int)GpuExpense.Research);
            Assert.Equal(4, (int)GpuExpense.Development);
            Assert.Equal(0, (int)GpuRevenue.Sales);
            Assert.Equal(1, (int)GpuRevenue.Contracts);
            Assert.Equal(0, (int)GpuModifierKind.ResearchSpeed);
            Assert.Equal(3, (int)GpuModifierKind.DevelopmentCost);
            Assert.Equal(4, (int)GpuModifierKind.ResearchCost);
            Assert.Equal("player", game.PlayerKey);
            Assert.NotNull(game.Channels);
            game.Spend("player", float.NaN, GpuExpense.Research);
            game.Spend("player", -5f, GpuExpense.Research);
            Assert.Equal(0, ((FakeGpuGame)game).Spent.Count, "only positive amounts move");
        }
    }
}
