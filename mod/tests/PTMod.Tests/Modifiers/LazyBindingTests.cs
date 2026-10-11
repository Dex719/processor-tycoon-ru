using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Modifiers;

namespace PTMod.Tests.Modifiers
{
    /// <summary>
    /// Channel registry v0.2 (FR-12 items 12-17, AC-12.7, 12.8, 12.10, 12.11, TSK-059): lazy binding of entries and providers to
    /// channels declared later or never, the families (gpu.demand.*), declaration rules, invalidation, cache keys (contract id,
    /// company, CPU, segment), uncached design queries, and the helpers around names.
    /// </summary>
    [Covers(typeof(ChannelFamilies), typeof(ModifierCache), typeof(ChannelRegistry), typeof(ChannelInfo))]
    public class LazyBindingTests
    {
        // ---- AC-12.10: declaration order does not matter ----

        [Test]
        public void Ac1210_ProviderBeforeTheDeclaration_IsBoundLater()
        {
            var registry = new ChannelRegistry(null, ModifierFixtures.Start);
            var changed = new List<string>();
            registry.ChannelChanged += changed.Add;

            // rival-personalities registers first; rival-acquisitions has not declared the channel yet.
            Assert.True(registry.RegisterProvider("rival-personalities", "acquisition.price", q => 20f));
            Assert.False(registry.IsDeclared("acquisition.price"));
            Assert.Equal(10f, registry.Get("acquisition.price", ModifierFixtures.Q("ai:3")), "undeclared: the default fuse [0.1; 10]");
            Assert.Equal(new[] { "acquisition.price" }, registry.UndeclaredChannels().ToArray());

            changed.Clear();
            Assert.True(registry.Declare(ModifierFixtures.Spec("acquisition.price", 0.5f, 3f, owner: "rival-acquisitions")));

            Assert.True(registry.IsDeclared("acquisition.price"));
            Assert.Equal(3f, registry.Get("acquisition.price", ModifierFixtures.Q("ai:3")), "the provider now meets the declared fuse");
            Assert.Equal(0, registry.UndeclaredChannels().Count);
            Assert.Equal(new[] { "acquisition.price" }, changed.ToArray(), "listeners learn that the channel changed");
            Assert.True(registry.Explain("acquisition.price", ModifierFixtures.Q("ai:3")).IsDeclared);
        }

        [Test]
        public void Ac1210_CoreDeclaresFirst_NothingIsUndeclared()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();

            registry.RegisterProvider("rival-personalities", "acquisition.price", q => 1.3f);
            registry.Add("event-system", "production.waferCost", ModifierScope.Global(), 1.4f, 30);
            registry.RegisterProvider("gpu-product-line", "gpu.demand.mobile", q => 1.1f);

            Assert.Equal(0, registry.UndeclaredChannels().Count);
            Assert.Near(1.3, registry.Get("acquisition.price", ModifierFixtures.Q("ai:1")), 1e-6);
        }

        [Test]
        public void Ac1210_TypoChannel_IsReported()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();

            registry.RegisterProvider("some-feature", "typo.channel", q => 1.5f);
            registry.Add("other-feature", "market.demand.lowends", ModifierScope.Global(), 1.5f);

            Assert.Equal(new[] { "market.demand.lowends", "typo.channel" }, registry.UndeclaredChannels().ToArray());
            Assert.Near(1.5, registry.Get("typo.channel", ModifierFixtures.Q(null)), 1e-6, "an undeclared channel still works");
            Assert.True(registry.HasAny("typo.channel"));
        }

        [Test]
        public void Undeclared_ChannelUsesTheDefaultFuseAtBothEnds()
        {
            var registry = new ChannelRegistry(null, ModifierFixtures.Start);
            registry.RegisterProvider("a", "new.channel", q => 0.01f);
            Assert.Equal(0.1f, registry.Get("new.channel", ModifierFixtures.Q(null)));

            registry.RegisterProvider("b", "new.channel", q => 5000f);
            Assert.Equal(10f, registry.Get("new.channel", ModifierFixtures.Q(null)));

            registry.RegisterProvider("c", "other.channel", q => 2f);
            Assert.Near(2.0, registry.Get("other.channel", ModifierFixtures.Q(null)), 1e-6);
            Assert.Equal(0.1f, ChannelRegistry.DefaultFuseMin);
            Assert.Equal(10f, ChannelRegistry.DefaultFuseMax);
        }

        [Test]
        public void Undeclared_EntryBeforeTheDeclaration_IsBoundLater()
        {
            var registry = new ChannelRegistry(null, ModifierFixtures.Start);
            registry.Add("a", "market.rating.cpu", ModifierScope.Global(), 3f);
            Assert.Equal(3f, registry.Get("market.rating.cpu", ModifierFixtures.Q(null)), "no balance range yet");

            registry.Declare(ModifierFixtures.Spec("market.rating.cpu", 0f, 5f, 0.5f, 2f));

            Assert.Equal(2f, registry.Get("market.rating.cpu", ModifierFixtures.Q(null)), "the range arrived with the declaration");
        }

        // ---- families ----

        [Test]
        public void Family_GpuDemand_CoversAnySegmentName()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();

            Assert.True(registry.IsDeclared("gpu.demand"));
            Assert.True(registry.IsDeclared("gpu.demand.mobile"));
            Assert.True(registry.IsDeclared("gpu.demand.desktop.highend"));
            Assert.False(registry.IsDeclared("gpu.demandx"));
            Assert.False(registry.IsDeclared("gpu.demand."));
            Assert.False(registry.IsDeclared("gpu"));
            Assert.False(registry.IsDeclared(null));

            registry.RegisterProvider("market-shocks", "gpu.demand.mobile", q => 5f);
            registry.RegisterProvider("market-shocks", "gpu.demand", q => 0.01f);
            Assert.Equal(3f, registry.Get("gpu.demand.mobile", ModifierFixtures.Q(null)), "the family's fuse [0.1; 3]");
            Assert.Equal(0.1f, registry.Get("gpu.demand", ModifierFixtures.Q(null)));
            Assert.Equal(0, registry.UndeclaredChannels().Count);
        }

        [Test]
        public void Family_SpecOf_ReturnsTheFamilyDeclaration()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();

            ChannelSpec member = registry.SpecOf("gpu.demand.mobile");
            Assert.NotNull(member);
            Assert.Equal("gpu.demand.*", member.Name);
            Assert.Equal("gpu-product-line", member.OwnerFeature);
            Assert.Equal("gpu.demand", registry.SpecOf("gpu.demand").Name);
            Assert.Equal("gpu.demand.*", registry.SpecOf("gpu.demand.*").Name);
            Assert.Null(registry.SpecOf("gpu.demandx"));
            Assert.Null(registry.SpecOf("typo.channel"));
            Assert.Null(registry.SpecOf(null));
        }

        [Test]
        public void Family_ExactDeclarationWinsOverTheFamily()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Declare(ModifierFixtures.Spec("gpu.demand.special", 0.5f, 1.5f));
            registry.Add("a", "gpu.demand.special", ModifierScope.Global(), 2f);
            registry.Add("a", "gpu.demand.other", ModifierScope.Global(), 2f);

            Assert.Equal(1.5f, registry.Get("gpu.demand.special", ModifierFixtures.Q(null)));
            Assert.Equal(2f, registry.Get("gpu.demand.other", ModifierFixtures.Q(null)));
        }

        [Test]
        public void Family_LongestPrefixWins()
        {
            var registry = new ChannelRegistry(null, ModifierFixtures.Start);
            registry.Declare(ModifierFixtures.Spec("a.*", 0.1f, 3f));
            registry.Declare(ModifierFixtures.Spec("a.b.*", 0.2f, 1.5f));
            registry.Add("x", "a.b.c", ModifierScope.Global(), 5f);
            registry.Add("x", "a.x", ModifierScope.Global(), 5f);

            Assert.Equal(1.5f, registry.Get("a.b.c", ModifierFixtures.Q(null)));
            Assert.Equal(3f, registry.Get("a.x", ModifierFixtures.Q(null)));

            // Order of declaration does not change that.
            var reversed = new ChannelRegistry(null, ModifierFixtures.Start);
            reversed.Declare(ModifierFixtures.Spec("a.b.*", 0.2f, 1.5f));
            reversed.Declare(ModifierFixtures.Spec("a.*", 0.1f, 3f));
            reversed.Add("x", "a.b.c", ModifierScope.Global(), 5f);
            Assert.Equal(1.5f, reversed.Get("a.b.c", ModifierFixtures.Q(null)));
        }

        [Test]
        public void Family_DeclaredAfterTheProvider_Rebinds()
        {
            var registry = new ChannelRegistry(null, ModifierFixtures.Start);
            registry.RegisterProvider("gpu-product-line", "gpu.demand.mobile", q => 5f);
            Assert.Equal(5f, registry.Get("gpu.demand.mobile", ModifierFixtures.Q(null)), "default fuse allows 5");
            Assert.Equal(1, registry.UndeclaredChannels().Count);

            var changed = new List<string>();
            registry.ChannelChanged += changed.Add;
            registry.Declare(ModifierFixtures.Spec("gpu.demand.*", 0.1f, 3f));

            Assert.Equal(3f, registry.Get("gpu.demand.mobile", ModifierFixtures.Q(null)));
            Assert.Equal(0, registry.UndeclaredChannels().Count);
            Assert.Equal(new[] { "gpu.demand.mobile" }, changed.ToArray());
        }

        // ---- declaring ----

        [Test]
        public void Declare_RepeatWithTheSameBounds_IsHarmless()
        {
            var registry = new ChannelRegistry(null, ModifierFixtures.Start);
            List<string> warnings = ModifierFixtures.CaptureWarnings(registry);
            int version = 0;

            Assert.True(registry.Declare(ModifierFixtures.Spec("a.b", 0.5f, 2f)));
            version = registry.Version;
            Assert.True(registry.Declare(ModifierFixtures.Spec("a.b", 0.5f, 2f, owner: "another-feature")), "owner and text do not matter");

            Assert.Equal(version, registry.Version, "nothing changed");
            Assert.Equal(0, warnings.Count);
            Assert.Equal(1, registry.DeclaredChannels().Count);
        }

        [Test]
        public void Declare_RepeatWithOtherBounds_IsRejected()
        {
            var registry = new ChannelRegistry(null, ModifierFixtures.Start);
            List<string> warnings = ModifierFixtures.CaptureWarnings(registry);
            registry.Declare(ModifierFixtures.Spec("a.b", 0.5f, 2f));
            registry.Add("x", "a.b", ModifierScope.Global(), 5f);

            Assert.False(registry.Declare(ModifierFixtures.Spec("a.b", 0.5f, 3f, owner: "greedy-feature")));
            Assert.False(registry.Declare(ModifierFixtures.Spec("a.b", 0.5f, 2f, 0.5f, 1f)));
            Assert.False(registry.Declare(ModifierFixtures.Spec("a.b", 0.5f, 2f, cacheable: false)));

            Assert.Equal(3, warnings.Count);
            Assert.True(warnings[0].StartsWith("greedy-feature: "), warnings[0]);
            Assert.True(warnings[0].Contains("a.b"));
            Assert.Equal(2f, registry.Get("a.b", ModifierFixtures.Q(null)), "the first declaration stays");
        }

        [Test]
        public void Declare_InvalidSpecs_AreRejected()
        {
            var registry = new ChannelRegistry(null, ModifierFixtures.Start);
            List<string> warnings = ModifierFixtures.CaptureWarnings(registry);

            Assert.False(registry.Declare(null));
            Assert.False(registry.Declare(ModifierFixtures.Spec("bad name", 0.5f, 2f)));
            Assert.False(registry.Declare(ModifierFixtures.Spec("a.b", 2f, 0.5f)));
            Assert.False(registry.Declare(ModifierFixtures.Spec("a.b", 0f, 5f, 2f, 0.5f)));

            Assert.Equal(4, warnings.Count);
            Assert.Equal(0, registry.DeclaredChannels().Count);
        }

        [Test]
        public void Declare_KeepsItsOwnCopy()
        {
            var registry = new ChannelRegistry(null, ModifierFixtures.Start);
            ChannelSpec spec = ModifierFixtures.Spec("a.b", 0.5f, 2f);
            registry.Declare(spec);
            registry.Add("x", "a.b", ModifierScope.Global(), 5f);

            spec.Max = 100f; // later edit of the instance that was passed in
            registry.SpecOf("a.b").Max = 200f; // an edit of a returned copy
            registry.DeclaredChannels()[0].Max = 300f;

            Assert.Equal(2f, registry.Get("a.b", ModifierFixtures.Q(null)));
            Assert.Equal(2f, registry.SpecOf("a.b").Max);
        }

        [Test]
        public void CoreTable_DeclaresInAnyOrder()
        {
            List<ChannelSpec> table = ModifierFixtures.CoreChannels();
            table.Reverse();
            var registry = new ChannelRegistry(null, ModifierFixtures.Start);
            foreach (ChannelSpec spec in table) Assert.True(registry.Declare(spec), spec.Name);

            IReadOnlyList<ChannelSpec> declared = registry.DeclaredChannels();
            Assert.Equal(27, declared.Count);
            Assert.Equal(declared.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray(), declared.Select(s => s.Name).ToArray());
            Assert.Equal("market-shocks", registry.SpecOf("production.memoryCost").OwnerFeature);
            Assert.False(registry.SpecOf("development.cost.design").Cacheable);
        }

        // ---- AC-12.11: Invalidate ----

        [Test]
        public void Ac1211_Invalidate_DropsTheChannelCache()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            float architectureBonus = 1.2f;
            registry.RegisterProvider("cpu-architectures", "market.rating.cpu", q => architectureBonus);
            ModifierQuery query = ModifierFixtures.Q("player", 7, Segment.HighEnd);

            Assert.Near(1.2, registry.Get("market.rating.cpu", query), 1e-6);
            architectureBonus = 1.5f; // the player changed the architecture in the middle of the day
            Assert.Near(1.2, registry.Get("market.rating.cpu", query), 1e-6, "still the cached day value");

            registry.Invalidate("market.rating.cpu");

            Assert.Near(1.5, registry.Get("market.rating.cpu", query), 1e-6);
        }

        [Test]
        public void Invalidate_OneChannelLeavesTheOthers()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            int ratingCalls = 0, taxCalls = 0;
            registry.RegisterProvider("p", "market.rating.cpu", q => { ratingCalls++; return 1.1f; });
            registry.RegisterProvider("p", "taxes", q => { taxCalls++; return 1.1f; });
            ModifierQuery query = ModifierFixtures.Q("player", 7, Segment.HighEnd);
            registry.Get("market.rating.cpu", query);
            registry.Get("taxes", query);

            registry.Invalidate("market.rating.cpu");
            registry.Get("market.rating.cpu", query);
            registry.Get("taxes", query);

            Assert.Equal(2, ratingCalls);
            Assert.Equal(1, taxCalls);
        }

        [Test]
        public void Invalidate_Null_DropsEveryChannel()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            int calls = 0;
            registry.RegisterProvider("p", "market.rating.cpu", q => { calls++; return 1.1f; });
            registry.RegisterProvider("p", "taxes", q => { calls++; return 1.1f; });
            registry.Get("market.rating.cpu", ModifierFixtures.Q("player"));
            registry.Get("taxes", ModifierFixtures.Q("player"));
            Assert.Equal(2, calls);

            registry.Invalidate();

            registry.Get("market.rating.cpu", ModifierFixtures.Q("player"));
            registry.Get("taxes", ModifierFixtures.Q("player"));
            Assert.Equal(4, calls);
            Assert.Equal(0, registry.Cache.CountOf("bank.interest"));
        }

        [Test]
        public void Invalidate_ByCompany_KeepsOtherCompanies()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            var values = new Dictionary<string, float> { { "player", 1.1f }, { "ai:3", 1.3f } };
            registry.RegisterProvider("p", "taxes", q => values[q.CompanyKey]);
            registry.RegisterProvider("p", "bank.interest", q => values[q.CompanyKey]);
            foreach (string company in values.Keys.ToList())
            {
                registry.Get("taxes", ModifierFixtures.Q(company));
                registry.Get("bank.interest", ModifierFixtures.Q(company));
            }

            values["player"] = 1.6f;
            values["ai:3"] = 1.8f;
            registry.Invalidate("taxes", "player");

            Assert.Near(1.6, registry.Get("taxes", ModifierFixtures.Q("player")), 1e-6, "dropped");
            Assert.Near(1.3, registry.Get("taxes", ModifierFixtures.Q("ai:3")), 1e-6, "kept");
            Assert.Near(1.1, registry.Get("bank.interest", ModifierFixtures.Q("player")), 1e-6, "another channel is not touched");

            registry.Invalidate(null, "player");
            Assert.Near(1.6, registry.Get("bank.interest", ModifierFixtures.Q("player")), 1e-6, "all channels of one company");
            Assert.Near(1.3, registry.Get("bank.interest", ModifierFixtures.Q("ai:3")), 1e-6);
        }

        [Test]
        public void Invalidate_RaisesChannelChanged()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "taxes", ModifierScope.Global(), 1.1f);
            registry.Add("a", "bank.interest", ModifierScope.Global(), 1.1f);
            var changed = new List<string>();
            registry.ChannelChanged += changed.Add;

            registry.Invalidate("research.speed");
            registry.Invalidate("taxes", "player");
            Assert.Equal(new[] { "research.speed", "taxes" }, changed.ToArray());

            changed.Clear();
            registry.Invalidate();
            Assert.Equal(new[] { "bank.interest", "taxes" }, changed.ToArray(), "the channels that have something registered");

            changed.Clear();
            registry.Invalidate(null, "player");
            Assert.Equal(new[] { "bank.interest", "taxes" }, changed.ToArray());
        }

        // ---- cache keys ----

        [Test]
        public void Ac127_ContractIdIsPartOfTheCacheKey()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            var asked = new List<int>();
            registry.RegisterProvider("platform-generations", "contract.signChance", q =>
            {
                asked.Add(q.ContractId);
                return q.ContractId == 11 ? 1.5f : 0.8f;
            });

            // One CPU on 1 January sits in two contracts.
            ModifierQuery first = ModifierFixtures.Q("ai:2", 40, Segment.MidRange, 11);
            ModifierQuery second = first.WithContract(12);

            Assert.Near(1.5, registry.Get("contract.signChance", first), 1e-6);
            Assert.Near(0.8, registry.Get("contract.signChance", second), 1e-6);
            Assert.Near(1.5, registry.Get("contract.signChance", first), 1e-6, "the interface asks again: the draw and the window agree");
            Assert.Near(0.8, registry.Get("contract.signChance", second), 1e-6);
            Assert.Equal(new[] { 11, 12, 11, 12 }, asked.ToArray(), "contract.signChance is not cached: every question reaches the provider");
        }

        [Test]
        public void SignChance_TwoContractsOfOneTemplate_AreNotMixedUpByTheCache()
        {
            // ContractID is the index of the decade's template, so two offers from different clients can share it on one day.
            ChannelRegistry registry = ModifierFixtures.Registry();
            float perContract = 1.5f;
            registry.RegisterProvider("platform-generations", "contract.signChance", q => perContract);
            ModifierQuery query = ModifierFixtures.Q("player", 40, Segment.MidRange, 7);

            Assert.Near(1.5, registry.Get("contract.signChance", query), 1e-6);
            perContract = 0.8f;   // the second contract with the same ContractID prefers another platform
            Assert.Near(0.8, registry.Get("contract.signChance", query), 1e-6, "the value of the first contract must not leak to the second");
        }

        [Test]
        public void CacheKey_ZeroContractIsNotNoContract()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            int calls = 0;
            registry.RegisterProvider("p", "contract.signChance", q => { calls++; return q.HasContract ? 2f : 1.5f; });
            ModifierQuery none = ModifierFixtures.Q("player", 40);

            Assert.Near(1.5, registry.Get("contract.signChance", none), 1e-6);
            Assert.Near(2.0, registry.Get("contract.signChance", none.WithContract(0)), 1e-6);
            Assert.Equal(2, calls);
        }

        [Test]
        public void CacheKey_CompanyCpuAndSegmentAreSeparate()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            int calls = 0;
            registry.RegisterProvider("p", "market.rating.cpu", q => { calls++; return 1.1f; });
            ModifierQuery basic = ModifierFixtures.Q("player", 1, Segment.LowEnd);

            registry.Get("market.rating.cpu", basic);
            registry.Get("market.rating.cpu", basic.WithCompany("ai:3"));
            registry.Get("market.rating.cpu", basic.WithCompany(null));
            registry.Get("market.rating.cpu", basic.WithCpu(2));
            registry.Get("market.rating.cpu", basic.WithCpu(-1));
            registry.Get("market.rating.cpu", basic.WithSegment(Segment.HighEnd));
            registry.Get("market.rating.cpu", basic.WithSegment(Segment.Any));
            Assert.Equal(7, calls, "seven different keys");

            registry.Get("market.rating.cpu", basic);
            registry.Get("market.rating.cpu", basic.WithCompany("ai:3"));
            registry.Get("market.rating.cpu", basic.WithCompany(null));
            registry.Get("market.rating.cpu", basic.WithCpu(2));
            Assert.Equal(7, calls, "and all of them are cached");
            Assert.Equal(7, registry.Cache.CountOf("market.rating.cpu"));
        }

        [Test]
        public void CacheKey_HostContextIsNotPartOfIt()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            int calls = 0;
            registry.RegisterProvider("p", "taxes", q => { calls++; return 1.1f; });

            registry.Get("taxes", ModifierFixtures.Q("player").WithContext("one"));
            registry.Get("taxes", ModifierFixtures.Q("player").WithContext("two"));

            Assert.Equal(1, calls);
        }

        [Test]
        public void Ac128_DesignQueries_AreNotCached()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            int calls = 0;
            registry.RegisterProvider("cpu-architectures", "development.cost.design", q =>
            {
                calls++;
                return (q.Context as string) == "immature node" ? 1.15f : 1f;
            });
            registry.Add("event-system", "development.cost", ModifierScope.Global(), 1.2f);

            ModifierQuery player = ModifierFixtures.Q("player").AsDesign().WithContext("immature node");
            ModifierQuery ai = ModifierFixtures.Q("ai:4").AsDesign().WithContext("immature node");
            ModifierQuery mature = ModifierFixtures.Q("player").AsDesign().WithContext("mature node");

            Assert.Near(1.15, registry.Get("development.cost.design", player), 1e-6, "the slider moves");
            Assert.Near(1.15, registry.Get("development.cost.design", player), 1e-6);
            Assert.Near(1.15, registry.Get("development.cost.design", ai), 1e-6, "the AI evaluates its own project");
            Assert.Equal(1f, registry.Get("development.cost.design", mature));
            Assert.Equal(4, calls, "every call computed, none came from the cache");
            Assert.Equal(0, registry.Cache.Count);

            // The tier-3 channel is a different channel: its multiplier is applied once, by itself.
            Assert.Near(1.2, registry.Get("development.cost", player), 1e-6);
        }

        [Test]
        public void DesignFlag_TurnsTheCacheOffOnAnyChannel()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            int calls = 0;
            registry.RegisterProvider("p", "taxes", q => { calls++; return 1.1f; });
            ModifierQuery plain = ModifierFixtures.Q("player");

            registry.Get("taxes", plain);
            registry.Get("taxes", plain.AsDesign());
            registry.Get("taxes", plain.AsDesign());
            Assert.Equal(3, calls, "design queries compute every time");

            registry.Get("taxes", plain);
            Assert.Equal(3, calls, "and do not disturb the cached plain value");
        }

        [Test]
        public void NonCacheableChannel_IsNeverCached()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            int calls = 0;
            registry.RegisterProvider("p", "development.cost.design", q => { calls++; return 1.1f; });

            registry.Get("development.cost.design", ModifierFixtures.Q("player"));
            registry.Get("development.cost.design", ModifierFixtures.Q("player"));

            Assert.Equal(2, calls);
            Assert.Equal(0, registry.Cache.Count);
        }

        // ---- ModifierCache on its own ----

        [Test]
        public void CacheKey_EqualityAndOf()
        {
            var a = new ModifierCache.Key("player", 1, Segment.LowEnd, 5);

            Assert.True(a.Equals(new ModifierCache.Key("player", 1, Segment.LowEnd, 5)));
            Assert.False(a.Equals(new ModifierCache.Key("ai:1", 1, Segment.LowEnd, 5)));
            Assert.False(a.Equals(new ModifierCache.Key("player", 2, Segment.LowEnd, 5)));
            Assert.False(a.Equals(new ModifierCache.Key("player", 1, Segment.HighEnd, 5)));
            Assert.False(a.Equals(new ModifierCache.Key("player", 1, Segment.LowEnd, 6)));
            Assert.True(new ModifierCache.Key(null, -1, Segment.Any, -1).Equals(new ModifierCache.Key(null, -1, Segment.Any, -1)));
            Assert.False(new ModifierCache.Key(null, -1, Segment.Any, -1).Equals(new ModifierCache.Key("", -1, Segment.Any, -1)));
            Assert.False(a.Equals((object)"player"));
            Assert.True(a.Equals((object)new ModifierCache.Key("player", 1, Segment.LowEnd, 5)));
            Assert.Equal(a.GetHashCode(), new ModifierCache.Key("player", 1, Segment.LowEnd, 5).GetHashCode());

            ModifierCache.Key fromQuery = ModifierCache.Key.Of(new ModifierQuery("player", 1, Segment.LowEnd, 5, true, "host"));
            Assert.Equal(a, fromQuery);
            Assert.Equal("player", fromQuery.CompanyKey);
            Assert.Equal(1, fromQuery.CpuSaveId);
            Assert.Equal(Segment.LowEnd, fromQuery.Segment);
            Assert.Equal(5, fromQuery.ContractId);
        }

        [Test]
        public void Cache_StoreAndGet()
        {
            var cache = new ModifierCache();
            var key = new ModifierCache.Key("player", 1, Segment.Any, -1);
            float value;

            Assert.False(cache.TryGet("taxes", key, out value));
            Assert.Equal(0f, value);
            Assert.Equal(1, cache.Misses);

            cache.Store("taxes", key, 1.75f);
            Assert.True(cache.TryGet("taxes", key, out value));
            Assert.Equal(1.75f, value);
            Assert.Equal(1, cache.Hits);
            Assert.False(cache.TryGet("other", key, out value), "another channel");
            Assert.False(cache.TryGet(null, key, out value));

            cache.Store("taxes", key, 2f);
            cache.TryGet("taxes", key, out value);
            Assert.Equal(2f, value, "Store overwrites");
            Assert.Equal(1, cache.Count);
            Assert.Equal(1, cache.CountOf("taxes"));
            Assert.Equal(0, cache.CountOf("other"));
            Assert.Equal(0, cache.CountOf(null));

            cache.Store(null, key, 5f); // ignored
            Assert.Equal(1, cache.Count);
        }

        [Test]
        public void Cache_InvalidateChannelCompanyAndClear()
        {
            var cache = new ModifierCache();
            foreach (string channel in new[] { "a", "b" })
            {
                cache.Store(channel, new ModifierCache.Key("player", 1, Segment.Any, -1), 1f);
                cache.Store(channel, new ModifierCache.Key("player", 2, Segment.Any, -1), 1f);
                cache.Store(channel, new ModifierCache.Key("ai:1", 1, Segment.Any, -1), 1f);
                cache.Store(channel, new ModifierCache.Key(null, 1, Segment.Any, -1), 1f);
            }
            Assert.Equal(8, cache.Count);

            cache.Invalidate("a", "player");
            Assert.Equal(2, cache.CountOf("a"));
            Assert.Equal(4, cache.CountOf("b"));

            cache.Invalidate("a", null);
            Assert.Equal(1, cache.CountOf("a"), "the key null is a company of its own");

            cache.Invalidate(null, "ai:1");
            Assert.Equal(0, cache.CountOf("a"));
            Assert.Equal(3, cache.CountOf("b"));

            cache.Invalidate("b");
            Assert.Equal(0, cache.Count);

            cache.Store("a", new ModifierCache.Key("x", 1, Segment.Any, -1), 1f);
            cache.Invalidate(null);
            Assert.Equal(1, cache.Count, "a null channel names nothing");
            cache.Invalidate("zzz");
            cache.Invalidate("zzz", "player");
            cache.Clear();
            Assert.Equal(0, cache.Count);
        }

        [Test]
        public void Cache_IsBoundedPerChannel()
        {
            var cache = new ModifierCache();
            for (int i = 0; i < ModifierCache.MaxEntriesPerChannel; i++) cache.Store("a", new ModifierCache.Key("player", i, Segment.Any, -1), 1f);
            cache.Store("b", new ModifierCache.Key("player", 0, Segment.Any, -1), 1f);
            Assert.Equal(ModifierCache.MaxEntriesPerChannel, cache.CountOf("a"));

            cache.Store("a", new ModifierCache.Key("player", 0, Segment.Any, -1), 2f); // an existing key does not overflow
            Assert.Equal(ModifierCache.MaxEntriesPerChannel, cache.CountOf("a"));

            cache.Store("a", new ModifierCache.Key("player", -5, Segment.Any, -1), 3f); // one key too many: the channel starts over
            Assert.Equal(1, cache.CountOf("a"));
            Assert.Equal(1, cache.CountOf("b"), "other channels are not affected");
        }

        // ---- names and families ----

        [Test]
        public void Families_NamesAndMatching()
        {
            Assert.True(ChannelFamilies.IsFamilyName("gpu.demand.*"));
            Assert.True(ChannelFamilies.IsFamilyName("a.*"));
            Assert.True(ChannelFamilies.IsFamilyName(ChannelFamilies.GpuDemandFamily));
            Assert.False(ChannelFamilies.IsFamilyName("gpu.demand"));
            Assert.False(ChannelFamilies.IsFamilyName(".*"));
            Assert.False(ChannelFamilies.IsFamilyName("*"));
            Assert.False(ChannelFamilies.IsFamilyName("a*"));
            Assert.False(ChannelFamilies.IsFamilyName("a.*.b"));
            Assert.False(ChannelFamilies.IsFamilyName("a.**"));
            Assert.False(ChannelFamilies.IsFamilyName("a..*"));
            Assert.False(ChannelFamilies.IsFamilyName(null));
            Assert.False(ChannelFamilies.IsFamilyName(""));

            Assert.Equal("gpu.demand.", ChannelFamilies.PrefixOf("gpu.demand.*"));
            Assert.Null(ChannelFamilies.PrefixOf("gpu.demand"));
            Assert.Null(ChannelFamilies.PrefixOf(null));

            Assert.True(ChannelFamilies.Matches("gpu.demand.*", "gpu.demand.mobile"));
            Assert.True(ChannelFamilies.Matches("gpu.demand.*", "gpu.demand.mobile.low"));
            Assert.False(ChannelFamilies.Matches("gpu.demand.*", "gpu.demand"), "the family does not cover its own root");
            Assert.False(ChannelFamilies.Matches("gpu.demand.*", "gpu.demand."));
            Assert.False(ChannelFamilies.Matches("gpu.demand.*", "gpu.demandx"));
            Assert.False(ChannelFamilies.Matches("gpu.demand.*", "gpu.demand.*"), "a family name is not a member");
            Assert.False(ChannelFamilies.Matches("gpu.demand.*", "GPU.demand.mobile"), "names are case-sensitive");
            Assert.False(ChannelFamilies.Matches("gpu.demand.*", null));
            Assert.False(ChannelFamilies.Matches("gpu.demand", "gpu.demand.mobile"), "an exact name is not a family");
            Assert.False(ChannelFamilies.Matches(null, "gpu.demand.mobile"));
        }

        [Test]
        public void Families_ChannelNameSyntax()
        {
            foreach (string good in new[] { "taxes", "research.speed", "development.cost.design", "bank.creditLimit", "a-b.c_d", "x1.y2" })
            {
                Assert.True(ChannelFamilies.IsValidChannelName(good, false), good);
            }
            foreach (string bad in new[] { "", null, ".a", "a.", "a..b", "a b", "a/b", "a:b", "gpu.demand.*", "ключ", new string('a', 129) })
            {
                Assert.False(ChannelFamilies.IsValidChannelName(bad, false), bad);
            }
            Assert.True(ChannelFamilies.IsValidChannelName("gpu.demand.*", true));
            Assert.False(ChannelFamilies.IsValidChannelName("gpu.*.demand", true));
            Assert.False(ChannelFamilies.IsValidChannelName("*", true));
            Assert.True(ChannelFamilies.IsValidChannelName(new string('a', 128), false));
        }

        [Test]
        public void Families_MarketDemandAndGpuDemandNames()
        {
            Assert.Equal("market.demand.highend", ChannelFamilies.MarketDemand(Segment.HighEnd));
            Assert.Equal("market.demand.midrange", ChannelFamilies.MarketDemand(Segment.MidRange));
            Assert.Equal("market.demand.lowend", ChannelFamilies.MarketDemand(Segment.LowEnd));
            Assert.Equal("market.demand.industries", ChannelFamilies.MarketDemand(Segment.Industries));
            Assert.Null(ChannelFamilies.MarketDemand(Segment.Any));
            Assert.Null(ChannelFamilies.MarketDemand((Segment)7));

            Assert.Equal("gpu.demand.mobile", ChannelFamilies.GpuDemand("mobile"));
            Assert.Equal("gpu.demand.high-end_2", ChannelFamilies.GpuDemand("high-end_2"));
            Assert.Null(ChannelFamilies.GpuDemand(""));
            Assert.Null(ChannelFamilies.GpuDemand(null));
            Assert.Null(ChannelFamilies.GpuDemand("a b"));
            Assert.Null(ChannelFamilies.GpuDemand("a.*"));
            Assert.Null(ChannelFamilies.GpuDemand(".a"));

            ChannelRegistry registry = ModifierFixtures.Registry();
            foreach (Segment segment in new[] { Segment.HighEnd, Segment.MidRange, Segment.LowEnd, Segment.Industries })
            {
                Assert.True(registry.IsDeclared(ChannelFamilies.MarketDemand(segment)), segment.ToString());
            }
            Assert.True(registry.IsDeclared(ChannelFamilies.GpuDemand("mobile")));
        }

        // ---- diagnostics ----

        [Test]
        public void Describe_ListsDeclaredAndUndeclaredChannels()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "market.rating.cpu", ModifierScope.Global(), 1.1f);
            registry.Add("b", "market.rating.cpu", ModifierScope.Global(), 1.2f);
            registry.RegisterProvider("c", "market.rating.cpu", q => 1f);
            registry.RegisterProvider("gpu-product-line", "gpu.demand.mobile", q => 1f);
            registry.RegisterProvider("gpu-product-line", "gpu.demand.desktop", q => 1f);
            registry.RegisterProvider("some-feature", "typo.channel", q => 1f);

            IReadOnlyList<ChannelInfo> rows = registry.Describe();

            Assert.Equal(28, rows.Count, "27 declared channels and one undeclared name");
            Assert.Equal(rows.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray(), rows.Select(r => r.Name).ToArray());

            ChannelInfo rating = rows.Single(r => r.Name == "market.rating.cpu");
            Assert.True(rating.IsDeclared);
            Assert.True(rating.HasBalanceRange);
            Assert.Equal(2, rating.EntryCount);
            Assert.Equal(1, rating.ProviderCount);
            Assert.Equal(0.5f, rating.Spec.BalanceMin);

            ChannelInfo family = rows.Single(r => r.Name == "gpu.demand.*");
            Assert.Equal(2, family.ProviderCount, "the family row adds up its members");
            Assert.Equal(0, rows.Single(r => r.Name == "gpu.demand").ProviderCount);

            ChannelInfo typo = rows.Single(r => r.Name == "typo.channel");
            Assert.False(typo.IsDeclared);
            Assert.False(typo.HasBalanceRange);
            Assert.Null(typo.Spec);
            Assert.Equal(1, typo.ProviderCount);

            ChannelInfo idle = rows.Single(r => r.Name == "taxes");
            Assert.Equal(0, idle.EntryCount);
            Assert.Equal(0, idle.ProviderCount);
            Assert.False(idle.HasBalanceRange);
        }

        [Test]
        public void Describe_IgnoresEmptiedChannels()
        {
            var registry = new ChannelRegistry(null, ModifierFixtures.Start);
            ModifierEntry entry = registry.Add("a", "typo.channel", ModifierScope.Global(), 1.1f);
            registry.Remove(entry.Id);

            Assert.Equal(0, registry.Describe().Count, "a name without entries and providers is not listed");
            Assert.Equal(0, registry.UndeclaredChannels().Count);
        }
    }
}
