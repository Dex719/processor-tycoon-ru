using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Modifiers;

namespace PTMod.Tests.Modifiers
{
    /// <summary>Which features are enabled; the registry under test asks it. Everything is enabled until <see cref="Disable"/>.</summary>
    public sealed class FeatureSwitch
    {
        private readonly HashSet<string> disabled = new HashSet<string>(StringComparer.Ordinal);

        public bool IsActive(string featureId)
        {
            return !disabled.Contains(featureId);
        }

        public void Disable(string featureId)
        {
            disabled.Add(featureId);
        }

        public void Enable(string featureId)
        {
            disabled.Remove(featureId);
        }
    }

    /// <summary>Shared helpers of the modifier tests: the 27 channels of core.json (design.md, "Контент ядра"), registry and query builders.</summary>
    public static class ModifierFixtures
    {
        public static readonly DateTime Start = new DateTime(1990, 1, 1);

        public const string Player = "player";
        public const string Ai3 = "ai:3";

        public static ChannelSpec Spec(string name, float min, float max, float balanceMin = 0f, float balanceMax = 0f,
            bool cacheable = true, string owner = null)
        {
            return new ChannelSpec
            {
                Name = name,
                Min = min,
                Max = max,
                BalanceMin = balanceMin,
                BalanceMax = balanceMax,
                Cacheable = cacheable,
                OwnerFeature = owner,
                PerCompany = true
            };
        }

        /// <summary>The 27 declarations of core.json as design.md lists them.</summary>
        public static List<ChannelSpec> CoreChannels()
        {
            return new List<ChannelSpec>
            {
                Spec("research.speed", 0.25f, 4f),
                Spec("research.cost", 0.25f, 4f),
                Spec("factory.operatingCost", 0.25f, 4f),
                Spec("construction.cost", 0.25f, 4f),
                Spec("development.cost", 0.25f, 4f),
                Spec("development.cost.design", 0.25f, 4f, cacheable: false),
                Spec("taxes", 0f, 4f),
                Spec("bank.interest", 0f, 4f),
                Spec("bank.creditLimit", 0.1f, 10f),
                Spec("production.unitCost", 0.25f, 4f),
                Spec("production.output", 0f, 3f),
                Spec("production.memoryCost", 0.5f, 2f, owner: "market-shocks"),
                Spec("production.waferCost", 0.6f, 2f, owner: "market-shocks"),
                Spec("cpu.failureRate", 0f, 10f),
                Spec("market.demand.highend", 0.1f, 3f),
                Spec("market.demand.midrange", 0.1f, 3f),
                Spec("market.demand.lowend", 0.1f, 3f),
                Spec("market.demand.industries", 0.1f, 3f),
                Spec("market.rating.cpu", 0f, 5f, 0.5f, 2f),
                Spec("popularity.gain", 0f, 5f, 0.5f, 2f),
                Spec("contract.reward", 0.25f, 4f),
                Spec("contract.signChance", 0f, 5f, cacheable: false),
                Spec("gpu.demand", 0.1f, 3f, owner: "gpu-product-line"),
                Spec("gpu.demand.*", 0.1f, 3f, owner: "gpu-product-line"),
                Spec("gpu.rating", 0.25f, 4f, owner: "gpu-product-line"),
                Spec("gpu.vramCost", 0.5f, 2f, owner: "gpu-product-line"),
                Spec("acquisition.price", 0.5f, 3f, owner: "rival-acquisitions")
            };
        }

        /// <summary>A registry on 1990-01-01 with every feature enabled (or as <paramref name="features"/> says) and the core channels declared.</summary>
        public static ChannelRegistry Registry(FeatureSwitch features = null, bool declareCore = true)
        {
            var registry = new ChannelRegistry(features == null ? (Func<string, bool>)null : features.IsActive, Start);
            if (declareCore)
            {
                foreach (ChannelSpec spec in CoreChannels()) registry.Declare(spec);
            }
            return registry;
        }

        public static ModifierQuery Q(string company, int cpu = -1, Segment segment = Segment.Any, int contract = -1)
        {
            return new ModifierQuery(company, cpu, segment, contract);
        }

        /// <summary>Collects the warnings of a registry as "feature: text".</summary>
        public static List<string> CaptureWarnings(ChannelRegistry registry)
        {
            var warnings = new List<string>();
            registry.Warning += (feature, text) => warnings.Add((feature ?? "-") + ": " + text);
            return warnings;
        }
    }

    /// <summary>
    /// ModModifiers engine (FR-12, TSK-017): products, the six scopes and segments, the safety fuse, expiry by date,
    /// disabled features, providers and their failures, the one-day cache, saved entries.
    /// </summary>
    [Covers(typeof(ChannelRegistry), typeof(ChannelInfo), typeof(ModifierEntry), typeof(ModifierScope), typeof(ScopeKind),
        typeof(ModifierQuery), typeof(Segment))]
    public class ModifierTests
    {
        // ---- the plain product ----

        [Test]
        public void Get_NothingRegistered_IsOne()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            ModifierQuery q = ModifierFixtures.Q(ModifierFixtures.Player);

            Assert.Equal(1f, registry.Get("research.speed", q));
            Assert.Equal(1f, registry.Get("never.heard.of", q));
            Assert.Equal(1f, registry.Get(null, q));
            Assert.False(registry.HasAny("research.speed"));
            Assert.False(registry.HasAny(null));
        }

        [Test]
        public void Get_SingleEntry_MultipliesExactly()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("alpha", "research.speed", ModifierScope.Player(), 1.5f);

            Assert.True(registry.HasAny("research.speed"));
            Assert.Near(1.5, registry.Get("research.speed", ModifierFixtures.Q(ModifierFixtures.Player)), 1e-6);
            Assert.Equal(1f, registry.Get("research.speed", ModifierFixtures.Q(ModifierFixtures.Ai3)));
        }

        [Test]
        public void Ac122_RatingMultipliersOfTwoFeaturesMultiply()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("feature-a", "market.rating.cpu", ModifierScope.AllCompanies(), 1.2f);
            registry.Add("feature-b", "market.rating.cpu", ModifierScope.AllCompanies(), 0.5f);

            ModifierQuery q = ModifierFixtures.Q(ModifierFixtures.Player, 5, Segment.MidRange);
            Assert.Near(0.6, registry.Get("market.rating.cpu", q), 1e-6);

            ModifierBreakdown breakdown = registry.Explain("market.rating.cpu", q);
            Assert.Equal(2, breakdown.Count);
            Assert.Near(0.6, breakdown.Raw, 1e-6);
            Assert.False(breakdown.IsBalanceCapped, "0.6 is inside [0.5; 2.0]");
        }

        [Test]
        public void Product_ManyEntriesAndProviders()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "research.cost", ModifierScope.Global(), 1.1f);
            registry.Add("b", "research.cost", ModifierScope.Global(), 1.2f);
            registry.RegisterProvider("c", "research.cost", q => 1.25f);

            Assert.Near(1.1 * 1.2 * 1.25, registry.Get("research.cost", ModifierFixtures.Q(null)), 1e-5);
        }

        // ---- expiry (AC-12.4) ----

        [Test]
        public void Ac124_TemporaryEntryExpiresOnItsDate()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            ModifierEntry entry = registry.Add("shock", "market.demand.lowend", ModifierScope.Global(), 0.7f, 90);

            Assert.Equal(new DateTime(1990, 4, 1), entry.ExpiresOn);
            Assert.True(entry.IsTemporary);
            Assert.Near(0.7, registry.Get("market.demand.lowend", ModifierFixtures.Q(null)), 1e-6);

            IReadOnlyList<ModifierEntry> removed = registry.SetToday(new DateTime(1990, 3, 31));
            Assert.Equal(0, removed.Count);
            Assert.Near(0.7, registry.Get("market.demand.lowend", ModifierFixtures.Q(null)), 1e-6);
            Assert.Equal(1, registry.EntryCount);

            removed = registry.SetToday(new DateTime(1990, 4, 1));
            Assert.Equal(1, removed.Count);
            Assert.Equal(entry.Id, removed[0].Id);
            Assert.Equal(1f, registry.Get("market.demand.lowend", ModifierFixtures.Q(null)));
            Assert.Equal(0, registry.EntryCount);
            Assert.False(registry.HasAny("market.demand.lowend"));
        }

        [Test]
        public void Expiry_IsDecidedByDateNotByRemoval()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            ModifierEntry entry = registry.Add("shock", "market.demand.lowend", ModifierScope.Global(), 0.7f, 30);

            Assert.False(entry.IsExpiredOn(new DateTime(1990, 1, 30)));
            Assert.True(entry.IsExpiredOn(new DateTime(1990, 1, 31)), "the end date itself is already outside");
            Assert.True(entry.IsExpiredOn(new DateTime(1991, 1, 1)));
            Assert.False(registry.Add("x", "taxes", ModifierScope.Global(), 1.1f).IsExpiredOn(new DateTime(2100, 1, 1)), "unlimited");
        }

        [Test]
        public void Expiry_UnlimitedEntrySurvivesAnyDate()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "taxes", ModifierScope.Global(), 1.1f);
            registry.SetToday(new DateTime(2030, 6, 1));

            Assert.Equal(1, registry.EntryCount);
            Assert.Near(1.1, registry.Get("taxes", ModifierFixtures.Q(null)), 1e-6);
        }

        [Test]
        public void Expiry_DurationCountsFromToday()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.SetToday(new DateTime(1985, 6, 15, 13, 45, 0));

            Assert.Equal(new DateTime(1985, 6, 15), registry.Today);
            ModifierEntry entry = registry.Add("a", "taxes", ModifierScope.Global(), 1.1f, 10);
            Assert.Equal(new DateTime(1985, 6, 25), entry.ExpiresOn);
        }

        [Test]
        public void Expiry_EntryOfDisabledFeatureStillExpires()
        {
            var features = new FeatureSwitch();
            ChannelRegistry registry = ModifierFixtures.Registry(features);
            registry.Add("shock", "taxes", ModifierScope.Global(), 1.5f, 10);
            features.Disable("shock");

            registry.SetToday(new DateTime(1990, 1, 5));
            Assert.Equal(1, registry.EntryCount, "kept while disabled");
            registry.SetToday(new DateTime(1990, 1, 11));
            Assert.Equal(0, registry.EntryCount, "its own end date still counts");
        }

        [Test]
        public void SetToday_SameDayKeepsCache_NewDayClearsIt()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            int calls = 0;
            registry.RegisterProvider("p", "taxes", q => { calls++; return 1.1f; });
            ModifierQuery query = ModifierFixtures.Q(ModifierFixtures.Player);

            registry.Get("taxes", query);
            registry.Get("taxes", query);
            Assert.Equal(1, calls);

            registry.SetToday(ModifierFixtures.Start);
            registry.Get("taxes", query);
            Assert.Equal(1, calls, "same day: still cached");

            registry.SetToday(ModifierFixtures.Start.AddDays(1));
            registry.Get("taxes", query);
            Assert.Equal(2, calls, "new day: computed again");
        }

        // ---- the six scopes and segments ----

        private static bool Applies(ModifierScope scope, ModifierQuery query)
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("f", "taxes", scope, 2f);
            return registry.Get("taxes", query) == 2f;
        }

        [Test]
        public void Scope_Global_AppliesToEverything()
        {
            ModifierScope scope = ModifierScope.Global();
            Assert.True(Applies(scope, ModifierFixtures.Q(null)));
            Assert.True(Applies(scope, ModifierFixtures.Q(ModifierFixtures.Player)));
            Assert.True(Applies(scope, ModifierFixtures.Q(ModifierFixtures.Ai3, 7, Segment.LowEnd)));
        }

        [Test]
        public void Scope_AllCompanies_NeedsACompany()
        {
            ModifierScope scope = ModifierScope.AllCompanies();
            Assert.True(Applies(scope, ModifierFixtures.Q(ModifierFixtures.Player)));
            Assert.True(Applies(scope, ModifierFixtures.Q(ModifierFixtures.Ai3)));
            Assert.False(Applies(scope, ModifierFixtures.Q(null)));
        }

        [Test]
        public void Scope_Player_OnlyThePlayer()
        {
            ModifierScope scope = ModifierScope.Player();
            Assert.True(Applies(scope, ModifierFixtures.Q("player")));
            Assert.False(Applies(scope, ModifierFixtures.Q("ai:3")));
            Assert.False(Applies(scope, ModifierFixtures.Q("Player")), "keys are case-sensitive");
            Assert.False(Applies(scope, ModifierFixtures.Q(null)));
        }

        [Test]
        public void Scope_AIOnly_OnlyAiCompanies()
        {
            ModifierScope scope = ModifierScope.AIOnly();
            Assert.True(Applies(scope, ModifierFixtures.Q("ai:3")));
            Assert.True(Applies(scope, ModifierFixtures.Q("ai:0")));
            Assert.False(Applies(scope, ModifierFixtures.Q("player")));
            Assert.False(Applies(scope, ModifierFixtures.Q(null)));
            Assert.False(Applies(scope, ModifierFixtures.Q("AI:3")));
        }

        [Test]
        public void Scope_Company_OnlyThatKey()
        {
            ModifierScope scope = ModifierScope.Company("ai:3");
            Assert.True(Applies(scope, ModifierFixtures.Q("ai:3")));
            Assert.False(Applies(scope, ModifierFixtures.Q("ai:4")));
            Assert.False(Applies(scope, ModifierFixtures.Q("ai:33")));
            Assert.False(Applies(scope, ModifierFixtures.Q("player")));
            Assert.False(Applies(scope, ModifierFixtures.Q(null)));
            Assert.True(Applies(ModifierScope.Company("player"), ModifierFixtures.Q("player")));
        }

        [Test]
        public void Scope_Cpu_OnlyThatSaveId()
        {
            ModifierScope scope = ModifierScope.Cpu(17);
            Assert.True(Applies(scope, ModifierFixtures.Q("player", 17)));
            Assert.True(Applies(scope, ModifierFixtures.Q("ai:2", 17)));
            Assert.True(Applies(scope, ModifierFixtures.Q(null, 17)));
            Assert.False(Applies(scope, ModifierFixtures.Q("player", 18)));
            Assert.False(Applies(scope, ModifierFixtures.Q("player")), "a query without a CPU");
            Assert.True(Applies(ModifierScope.Cpu(0), ModifierFixtures.Q("player", 0)), "SaveID 0 is a CPU");
            Assert.False(Applies(ModifierScope.Cpu(0), ModifierFixtures.Q("player")));
        }

        [Test]
        public void Scope_SegmentFilter()
        {
            ModifierScope lowEnd = ModifierScope.Player(Segment.LowEnd);
            Assert.True(Applies(lowEnd, ModifierFixtures.Q("player", 1, Segment.LowEnd)));
            Assert.False(Applies(lowEnd, ModifierFixtures.Q("player", 1, Segment.MidRange)));
            Assert.False(Applies(lowEnd, ModifierFixtures.Q("player", 1, Segment.Any)), "a query without a segment has none to match");
            Assert.False(Applies(lowEnd, ModifierFixtures.Q("ai:3", 1, Segment.LowEnd)), "scope still decides");

            ModifierScope anySegment = ModifierScope.Player(Segment.Any);
            foreach (Segment segment in new[] { Segment.Any, Segment.HighEnd, Segment.MidRange, Segment.LowEnd, Segment.Industries })
            {
                Assert.True(Applies(anySegment, ModifierFixtures.Q("player", 1, segment)), "any segment: " + segment);
            }

            Assert.True(Applies(ModifierScope.Cpu(9, Segment.Industries), ModifierFixtures.Q(null, 9, Segment.Industries)));
            Assert.False(Applies(ModifierScope.Cpu(9, Segment.Industries), ModifierFixtures.Q(null, 9, Segment.HighEnd)));
            Assert.True(Applies(ModifierScope.Global(Segment.HighEnd), ModifierFixtures.Q(null, -1, Segment.HighEnd)));
            Assert.True(Applies(ModifierScope.AllCompanies(Segment.MidRange), ModifierFixtures.Q("ai:1", -1, Segment.MidRange)));
            Assert.True(Applies(ModifierScope.AIOnly(Segment.MidRange), ModifierFixtures.Q("ai:1", -1, Segment.MidRange)));
            Assert.True(Applies(ModifierScope.Company("ai:1", Segment.MidRange), ModifierFixtures.Q("ai:1", -1, Segment.MidRange)));
        }

        [Test]
        public void Scope_Structure()
        {
            Assert.Equal(ScopeKind.Global, default(ModifierScope).Kind);
            Assert.Equal(Segment.Any, default(ModifierScope).Segment);
            Assert.Equal(-1, default(ModifierScope).CpuSaveId);
            Assert.Null(default(ModifierScope).CompanyKey);
            Assert.Equal(ModifierScope.Global(), default(ModifierScope));

            ModifierScope company = ModifierScope.Company("ai:3", Segment.LowEnd);
            Assert.Equal(ScopeKind.Company, company.Kind);
            Assert.Equal("ai:3", company.CompanyKey);
            Assert.Equal(Segment.LowEnd, company.Segment);
            Assert.Equal(-1, company.CpuSaveId);

            ModifierScope cpu = ModifierScope.Cpu(17);
            Assert.Equal(17, cpu.CpuSaveId);
            Assert.Null(cpu.CompanyKey);

            Assert.True(ModifierScope.Player().Equals(ModifierScope.Player()));
            Assert.False(ModifierScope.Player().Equals(ModifierScope.AIOnly()));
            Assert.False(ModifierScope.Player(Segment.LowEnd).Equals(ModifierScope.Player()));
            Assert.False(ModifierScope.Cpu(1).Equals(ModifierScope.Cpu(2)));
            Assert.False(ModifierScope.Company("a").Equals(ModifierScope.Company("b")));
            Assert.False(ModifierScope.Player().Equals((object)"Player"));
            Assert.Equal(ModifierScope.Company("ai:3").GetHashCode(), ModifierScope.Company("ai:3").GetHashCode());

            Assert.Equal("Player", ModifierScope.Player().ToString());
            Assert.Equal("Company(ai:3)[LowEnd]", company.ToString());
            Assert.Equal("Cpu(17)", cpu.ToString());
        }

        [Test]
        public void Scope_Validate_AndTryCreate()
        {
            string error;
            Assert.True(ModifierScope.Player().Validate(out error));
            Assert.Null(error);
            Assert.False(ModifierScope.Company(null).Validate(out error));
            Assert.True(error.Contains("company key"));
            Assert.False(ModifierScope.Company("").Validate(out error));
            Assert.False(ModifierScope.Company("AI:3").Validate(out error), "company keys are case-sensitive");
            Assert.False(ModifierScope.Company("ai:").Validate(out error));
            Assert.False(ModifierScope.Company("ai:3\n").Validate(out error), "no trailing line break");
            Assert.True(ModifierScope.Company("player").Validate(out error));
            Assert.True(ModifierScope.Company("ai:0").Validate(out error));
            Assert.False(ModifierScope.Cpu(-1).Validate(out error));
            Assert.True(error.Contains("SaveID"));
            Assert.False(ModifierScope.Global((Segment)9).Validate(out error));
            Assert.True(error.Contains("segment"));

            ModifierScope scope;
            Assert.True(ModifierScope.TryCreate("Player", null, -1, -1, out scope, out error));
            Assert.Equal(ModifierScope.Player(), scope);
            Assert.True(ModifierScope.TryCreate("allcompanies", null, -1, 2, out scope, out error), "kind names are case-insensitive");
            Assert.Equal(ModifierScope.AllCompanies(Segment.LowEnd), scope);
            Assert.True(ModifierScope.TryCreate("AIOnly", "junk", 5, -1, out scope, out error), "unused fields are ignored");
            Assert.Equal(ModifierScope.AIOnly(), scope);
            Assert.True(ModifierScope.TryCreate("Company", "ai:7", -1, 0, out scope, out error));
            Assert.Equal(ModifierScope.Company("ai:7", Segment.HighEnd), scope);
            Assert.True(ModifierScope.TryCreate("Cpu", null, 42, 3, out scope, out error));
            Assert.Equal(ModifierScope.Cpu(42, Segment.Industries), scope);

            Assert.False(ModifierScope.TryCreate("Everyone", null, -1, -1, out scope, out error));
            Assert.True(error.Contains("Everyone"));
            Assert.False(ModifierScope.TryCreate("7", null, -1, -1, out scope, out error), "a number is not a kind name");
            Assert.False(ModifierScope.TryCreate("", null, -1, -1, out scope, out error));
            Assert.False(ModifierScope.TryCreate(null, null, -1, -1, out scope, out error));
            Assert.False(ModifierScope.TryCreate("Player", null, -1, 4, out scope, out error), "segment 4 does not exist");
            Assert.False(ModifierScope.TryCreate("Player", null, -1, -2, out scope, out error));
            Assert.False(ModifierScope.TryCreate("Company", null, -1, -1, out scope, out error), "Company needs a key");
            Assert.False(ModifierScope.TryCreate("Company", "rival", -1, -1, out scope, out error), "the key has to be player or ai:<id>");
            Assert.False(ModifierScope.TryCreate("Cpu", null, -1, -1, out scope, out error), "Cpu needs a SaveID");
            Assert.Equal(default(ModifierScope), scope, "a failed parse leaves the default");
        }

        [Test]
        public void Query_DefaultAndWithers()
        {
            ModifierQuery none = default(ModifierQuery);
            Assert.Null(none.CompanyKey);
            Assert.Equal(-1, none.CpuSaveId);
            Assert.Equal(-1, none.ContractId);
            Assert.Equal(Segment.Any, none.Segment);
            Assert.False(none.HasCpu);
            Assert.False(none.HasContract);
            Assert.False(none.IsDesign);
            Assert.Null(none.Context);

            var host = new object();
            ModifierQuery q = new ModifierQuery("ai:3", 0, Segment.LowEnd, 0, true, host);
            Assert.True(q.HasCpu, "SaveID 0 is a CPU");
            Assert.True(q.HasContract, "ContractID 0 is a contract");
            Assert.Equal(0, q.CpuSaveId);
            Assert.Equal(0, q.ContractId);
            Assert.True(q.IsDesign);
            Assert.True(ReferenceEquals(host, q.Context));

            ModifierQuery b = ModifierFixtures.Q("player");
            Assert.Equal(5, b.WithCpu(5).CpuSaveId);
            Assert.Equal("ai:9", b.WithCompany("ai:9").CompanyKey);
            Assert.Equal(Segment.Industries, b.WithSegment(Segment.Industries).Segment);
            Assert.Equal(3, b.WithContract(3).ContractId);
            Assert.True(b.AsDesign().IsDesign);
            Assert.True(ReferenceEquals(host, b.WithContext(host).Context));
            Assert.Equal(-1, b.WithCpu(-5).CpuSaveId, "any negative number is 'no CPU'");
            Assert.Equal(7, b.WithCpu(7).WithSegment(Segment.LowEnd).WithContract(2).CpuSaveId, "withers keep the other fields");
            Assert.True(b.WithCpu(7).ToString().Contains("cpu=7"));
        }

        // ---- the fuse ----

        [Test]
        public void Fuse_CutsTheProduct()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "research.speed", ModifierScope.Global(), 3f);
            registry.Add("b", "research.speed", ModifierScope.Global(), 3f);
            ModifierQuery q = ModifierFixtures.Q(null);

            Assert.Equal(4f, registry.Get("research.speed", q));
            ModifierBreakdown breakdown = registry.Explain("research.speed", q);
            Assert.Near(9.0, breakdown.Raw, 1e-5);
            Assert.Equal(4f, breakdown.Total);
            Assert.True(breakdown.IsFuseCapped);
            Assert.False(breakdown.IsBalanceCapped);
        }

        [Test]
        public void Fuse_RaisesTooSmallProducts()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "research.speed", ModifierScope.Global(), 0.1f);
            registry.Add("b", "research.speed", ModifierScope.Global(), 0f);
            registry.Add("c", "production.output", ModifierScope.Global(), 0f);

            Assert.Equal(0.25f, registry.Get("research.speed", ModifierFixtures.Q(null)));
            Assert.Equal(0f, registry.Get("production.output", ModifierFixtures.Q(null)), "this channel's fuse starts at 0");
        }

        [Test]
        public void Fuse_FailureRateChannelAllowsTenfold()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "cpu.failureRate", ModifierScope.Global(), 3f);
            Assert.Near(3.0, registry.Get("cpu.failureRate", ModifierFixtures.Q(null)), 1e-6);
            registry.Add("b", "cpu.failureRate", ModifierScope.Global(), 20f);
            Assert.Equal(10f, registry.Get("cpu.failureRate", ModifierFixtures.Q(null)));
        }

        // ---- disabled features ----

        [Test]
        public void DisabledFeature_EntryIsNotAppliedAndNotRemoved()
        {
            var features = new FeatureSwitch();
            ChannelRegistry registry = ModifierFixtures.Registry(features);
            ModifierEntry entry = registry.Add("alpha", "research.speed", ModifierScope.Player(), 1.5f, 365);
            ModifierQuery q = ModifierFixtures.Q(ModifierFixtures.Player);
            Assert.Near(1.5, registry.Get("research.speed", q), 1e-6);

            features.Disable("alpha");
            registry.FeatureStateChanged("alpha");
            Assert.Equal(1f, registry.Get("research.speed", q));
            Assert.Equal(1, registry.EntryCount, "kept");
            Assert.True(ReferenceEquals(entry, registry.Find(entry.Id)));
            Assert.Equal(new DateTime(1991, 1, 1), registry.Find(entry.Id).ExpiresOn, "the original end date is untouched");
            Assert.True(registry.HasAny("research.speed"), "something is still registered");

            features.Enable("alpha");
            registry.FeatureStateChanged("alpha");
            Assert.Near(1.5, registry.Get("research.speed", q), 1e-6);
            Assert.Equal(entry.Id, registry.Entries[0].Id, "the same entry came back, not a new one");
        }

        [Test]
        public void DisabledFeature_ProviderIsSkippedNotRemoved()
        {
            var features = new FeatureSwitch();
            ChannelRegistry registry = ModifierFixtures.Registry(features);
            int calls = 0;
            registry.RegisterProvider("alpha", "taxes", q => { calls++; return 1.5f; });
            ModifierQuery query = ModifierFixtures.Q(ModifierFixtures.Player);
            Assert.Near(1.5, registry.Get("taxes", query), 1e-6);

            features.Disable("alpha");
            registry.FeatureStateChanged("alpha");
            int before = calls;
            Assert.Equal(1f, registry.Get("taxes", query));
            Assert.Equal(before, calls, "a disabled feature's provider is not called");
            Assert.Equal(1, registry.ProvidersOf("alpha"));

            features.Enable("alpha");
            registry.FeatureStateChanged("alpha");
            Assert.Near(1.5, registry.Get("taxes", query), 1e-6);
            Assert.Equal(1, registry.ProvidersOf("alpha"), "still one provider, not two");
        }

        [Test]
        public void FeatureStateChanged_OnlyAffectsChannelsOfThatFeature()
        {
            var features = new FeatureSwitch();
            ChannelRegistry registry = ModifierFixtures.Registry(features);
            int calls = 0;
            registry.RegisterProvider("other", "bank.interest", q => { calls++; return 1.1f; });
            registry.Add("alpha", "taxes", ModifierScope.Global(), 1.5f);
            registry.Get("bank.interest", ModifierFixtures.Q(null));
            Assert.Equal(1, calls);

            var changed = new List<string>();
            registry.ChannelChanged += changed.Add;
            features.Disable("alpha");
            registry.FeatureStateChanged("alpha");

            Assert.Equal(new[] { "taxes" }, changed.ToArray());
            registry.Get("bank.interest", ModifierFixtures.Q(null));
            Assert.Equal(1, calls, "the other channel's cache survived");

            changed.Clear();
            registry.FeatureStateChanged();
            Assert.Equal(new[] { "bank.interest", "taxes" }, changed.ToArray());
            registry.Get("bank.interest", ModifierFixtures.Q(null));
            Assert.Equal(2, calls, "null drops every cache");
        }

        [Test]
        public void FeatureActive_Setter_EmptiesCache()
        {
            var features = new FeatureSwitch();
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("alpha", "taxes", ModifierScope.Global(), 2f);
            Assert.Equal(2f, registry.Get("taxes", ModifierFixtures.Q(null)));

            features.Disable("alpha");
            registry.FeatureActive = features.IsActive;
            Assert.Equal(1f, registry.Get("taxes", ModifierFixtures.Q(null)));
            Assert.NotNull(registry.FeatureActive);
        }

        // ---- providers ----

        [Test]
        public void Provider_ReceivesTheQueryAndItsContext()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            var host = new object();
            ModifierQuery seen = default(ModifierQuery);
            registry.RegisterProvider("p", "market.rating.cpu", q => { seen = q; return 1.1f; });

            registry.Get("market.rating.cpu", new ModifierQuery("ai:4", 12, Segment.HighEnd, 3, false, host));

            Assert.Equal("ai:4", seen.CompanyKey);
            Assert.Equal(12, seen.CpuSaveId);
            Assert.Equal(Segment.HighEnd, seen.Segment);
            Assert.Equal(3, seen.ContractId);
            Assert.True(ReferenceEquals(host, seen.Context));
        }

        [Test]
        public void Provider_Exception_CountsAsOneAndIsReported()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            var reports = new List<string>();
            registry.ProviderFailed += (feature, channel, ex) => reports.Add(feature + "|" + channel + "|" + ex.Message);
            registry.RegisterProvider("broken", "taxes", q => { throw new InvalidOperationException("boom"); });
            registry.RegisterProvider("fine", "taxes", q => 1.25f);

            Assert.Near(1.25, registry.Get("taxes", ModifierFixtures.Q(ModifierFixtures.Player)), 1e-6);
            Assert.Equal(new[] { "broken|taxes|boom" }, reports.ToArray());
            Assert.Equal(1, registry.ProviderFaults("broken"));
            Assert.Equal(0, registry.ProviderFaults("fine"));
            Assert.Equal(0, registry.ProviderFaults(null));
        }

        [Test]
        public void Provider_UnusableNumbers_CountAsOne()
        {
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.5f })
            {
                ChannelRegistry registry = ModifierFixtures.Registry();
                var reports = new List<Exception>();
                registry.ProviderFailed += (feature, channel, ex) => reports.Add(ex);
                float value = bad;
                registry.RegisterProvider("p", "taxes", q => value);
                registry.Add("e", "taxes", ModifierScope.Global(), 2f);

                Assert.Equal(2f, registry.Get("taxes", ModifierFixtures.Q(null)), "result of " + bad);
                Assert.Equal(1, reports.Count);
                Assert.Equal(1, registry.ProviderFaults("p"));
            }
        }

        [Test]
        public void Provider_FailureCounterGrowsOnEveryFailedEvaluation()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.RegisterProvider("broken", "development.cost.design", q => { throw new InvalidOperationException(); });

            // The design channel is never cached, so each call evaluates (and fails) again.
            for (int i = 0; i < 3; i++) Assert.Equal(1f, registry.Get("development.cost.design", ModifierFixtures.Q("player")));
            Assert.Equal(3, registry.ProviderFaults("broken"));
        }

        [Test]
        public void Provider_FailureHandlerThatThrowsDoesNotBreakGet()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.ProviderFailed += (feature, channel, ex) => { throw new InvalidOperationException("log sink down"); };
            registry.RegisterProvider("broken", "taxes", q => { throw new InvalidOperationException("boom"); });

            Assert.Equal(1f, registry.Get("taxes", ModifierFixtures.Q(null)));
            Assert.Equal(1, registry.ProviderFaults("broken"));
        }

        [Test]
        public void Provider_ReadingItsOwnChannel_IsCutOff()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            List<string> warnings = ModifierFixtures.CaptureWarnings(registry);
            registry.RegisterProvider("loop", "development.cost.design", q => 1.5f * registry.Get("development.cost.design", q));

            float result = registry.Get("development.cost.design", ModifierFixtures.Q("player"));

            Assert.Equal(4f, result, "the product is cut by the fuse");
            Assert.True(warnings.Any(w => w.Contains("nested too deeply")), "a warning explains why");
            Assert.Equal(1f, registry.Get("research.speed", ModifierFixtures.Q("player")), "the registry still works");
        }

        [Test]
        public void Provider_MayReadAnotherChannel()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "bank.interest", ModifierScope.Global(), 2f);
            registry.RegisterProvider("p", "taxes", q => 1.5f * registry.Get("bank.interest", q));

            Assert.Near(3.0, registry.Get("taxes", ModifierFixtures.Q(null)), 1e-6);
        }

        [Test]
        public void Provider_MayChangeTheRegistryWhileItRuns()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            bool added = false;
            registry.RegisterProvider("p", "taxes", q =>
            {
                if (!added)
                {
                    added = true;
                    registry.Add("late", "taxes", ModifierScope.Global(), 2f);
                }
                return 1.5f;
            });

            // The evaluation in progress works on the arrays it started with; the entry is seen by the next call.
            Assert.Near(1.5, registry.Get("taxes", ModifierFixtures.Q(null)), 1e-6);
            Assert.Near(3.0, registry.Get("taxes", ModifierFixtures.Q(null)), 1e-6);
            Assert.Equal(1, registry.EntryCount);
        }

        [Test]
        public void Provider_DuplicateRegistration_IsIgnored()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            Func<ModifierQuery, float> provider = q => 1.5f;

            Assert.True(registry.RegisterProvider("alpha", "taxes", provider));
            Assert.False(registry.RegisterProvider("alpha", "taxes", provider), "same feature, channel and delegate");
            Assert.True(registry.RegisterProvider("beta", "taxes", provider), "another feature may register it");
            Assert.True(registry.RegisterProvider("alpha", "bank.interest", provider), "so may the same feature on another channel");
            Assert.Equal(3, registry.ProviderCount);
            Assert.Near(2.25, registry.Get("taxes", ModifierFixtures.Q(null)), 1e-6);

            var key = new object();
            Assert.True(registry.RegisterProvider("gamma", "research.cost", q => 1.1f, null, key));
            Assert.False(registry.RegisterProvider("gamma", "research.cost", q => 1.1f, null, key), "same identity, a fresh delegate");
            Assert.True(registry.RegisterReasonedProvider("gamma", "research.cost", q => 1.2f));
            Assert.Equal(2, registry.ProvidersOf("gamma", "research.cost"));
        }

        [Test]
        public void Provider_Registration_IsValidated()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            List<string> warnings = ModifierFixtures.CaptureWarnings(registry);

            Assert.False(registry.RegisterProvider("alpha", "taxes", null));
            Assert.False(registry.RegisterReasonedProvider("alpha", "taxes", null));
            Assert.False(registry.RegisterProvider("", "taxes", q => 1f));
            Assert.False(registry.RegisterProvider("alpha", "gpu.demand.*", q => 1f), "a family is a declaration, not a channel");
            Assert.False(registry.RegisterProvider("alpha", "bad name", q => 1f));
            Assert.False(registry.RegisterProvider("alpha", null, q => 1f));

            Assert.Equal(0, registry.ProviderCount);
            Assert.Equal(6, warnings.Count);
        }

        [Test]
        public void Provider_RemoveProviders()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.RegisterProvider("alpha", "taxes", q => 1.5f);
            registry.RegisterProvider("alpha", "bank.interest", q => 1.5f);
            registry.RegisterProvider("beta", "taxes", q => 2f);
            Assert.Near(3.0, registry.Get("taxes", ModifierFixtures.Q(null)), 1e-6);

            Assert.Equal(1, registry.RemoveProviders("alpha", "taxes"));
            Assert.Near(2.0, registry.Get("taxes", ModifierFixtures.Q(null)), 1e-6);
            Assert.Equal(1, registry.RemoveProviders("alpha"));
            Assert.Equal(0, registry.RemoveProviders("alpha"));
            Assert.Equal(0, registry.RemoveProviders(null));
            Assert.Equal(1, registry.ProviderCount);
            Assert.False(registry.HasAny("bank.interest"));
        }

        // ---- the cache ----

        [Test]
        public void Cache_IsResetByAddAndRemove()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            int calls = 0;
            registry.RegisterProvider("p", "taxes", q => { calls++; return 1.1f; });
            ModifierQuery query = ModifierFixtures.Q(ModifierFixtures.Player);

            registry.Get("taxes", query);
            registry.Get("taxes", query);
            Assert.Equal(1, calls);

            ModifierEntry entry = registry.Add("a", "taxes", ModifierScope.Player(), 2f);
            Assert.Near(2.2, registry.Get("taxes", query), 1e-6);
            Assert.Equal(2, calls, "Add dropped the cached value");

            registry.Get("taxes", query);
            Assert.Equal(2, calls);

            Assert.True(registry.Remove(entry.Id));
            Assert.Near(1.1, registry.Get("taxes", query), 1e-6);
            Assert.Equal(3, calls, "Remove dropped it too");
        }

        [Test]
        public void Cache_IsResetByRegisterProvider()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "taxes", ModifierScope.Global(), 2f);
            ModifierQuery query = ModifierFixtures.Q(ModifierFixtures.Player);
            Assert.Equal(2f, registry.Get("taxes", query));

            registry.RegisterProvider("p", "taxes", q => 1.5f);
            Assert.Near(3.0, registry.Get("taxes", query), 1e-6);
        }

        [Test]
        public void Cache_OtherChannelsSurviveAChange()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            int calls = 0;
            registry.RegisterProvider("p", "bank.interest", q => { calls++; return 1.1f; });
            registry.Get("bank.interest", ModifierFixtures.Q(null));

            registry.Add("a", "taxes", ModifierScope.Global(), 2f);
            registry.Get("bank.interest", ModifierFixtures.Q(null));

            Assert.Equal(1, calls);
        }

        [Test]
        public void Cache_ServesRepeatedQueries()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "market.rating.cpu", ModifierScope.Global(), 1.1f);
            ModifierQuery query = ModifierFixtures.Q("player", 4, Segment.LowEnd);

            registry.Get("market.rating.cpu", query);
            int misses = registry.Cache.Misses;
            int hits = registry.Cache.Hits;
            for (int i = 0; i < 10; i++) registry.Get("market.rating.cpu", query);

            Assert.Equal(misses, registry.Cache.Misses);
            Assert.Equal(hits + 10, registry.Cache.Hits);
            Assert.Equal(1, registry.Cache.Count);
        }

        // ---- no allocation ----

        [Test]
        public void Get_WithoutEntries_DoesNotAllocate()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "research.speed", ModifierScope.Player(), 1.5f); // another channel has entries; this one has none
            ModifierQuery query = ModifierFixtures.Q(ModifierFixtures.Player, 3, Segment.MidRange);

            long grew = MeasureAllocation(() =>
            {
                float sum = 0f;
                for (int i = 0; i < 100000; i++) sum += registry.Get("market.demand.lowend", query);
                GC.KeepAlive(sum);
            });

            Assert.True(grew <= 16 * 1024, "Get without entries allocated " + grew + " bytes in 100000 calls");
        }

        [Test]
        public void Get_UnknownAndNullChannel_DoNotAllocate()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            ModifierQuery query = ModifierFixtures.Q(ModifierFixtures.Player);

            long grew = MeasureAllocation(() =>
            {
                float sum = 0f;
                for (int i = 0; i < 100000; i++)
                {
                    sum += registry.Get("not.a.channel", query);
                    sum += registry.Get(null, query);
                }
                GC.KeepAlive(sum);
            });

            Assert.True(grew <= 16 * 1024, "allocated " + grew + " bytes");
        }

        [Test]
        public void Get_CachedValue_DoesNotAllocate()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "market.rating.cpu", ModifierScope.AllCompanies(), 1.3f);
            ModifierQuery query = ModifierFixtures.Q(ModifierFixtures.Player, 3, Segment.MidRange);
            registry.Get("market.rating.cpu", query);

            long grew = MeasureAllocation(() =>
            {
                float sum = 0f;
                for (int i = 0; i < 100000; i++) sum += registry.Get("market.rating.cpu", query);
                GC.KeepAlive(sum);
            });

            Assert.True(grew <= 16 * 1024, "a cache hit allocated " + grew + " bytes");
        }

        [Test]
        public void Get_AfterTheLastEntryIsRemoved_DoesNotAllocate()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            ModifierEntry entry = registry.Add("a", "taxes", ModifierScope.Global(), 2f);
            registry.Remove(entry.Id);
            ModifierQuery query = ModifierFixtures.Q(ModifierFixtures.Player);

            Assert.False(registry.HasAny("taxes"));
            long grew = MeasureAllocation(() =>
            {
                float sum = 0f;
                for (int i = 0; i < 100000; i++) sum += registry.Get("taxes", query);
                GC.KeepAlive(sum);
            });

            Assert.True(grew <= 16 * 1024, "allocated " + grew + " bytes");
        }

        /// <summary>
        /// Bytes allocated while the action ran: the larger of the <c>GC.GetTotalMemory</c> difference (the measure of the spec) and
        /// the exact count of this thread (a collection during the run would hide garbage from the first one). The action is run
        /// once beforehand so that JIT work is not counted.
        /// </summary>
        private static long MeasureAllocation(Action action)
        {
            action();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long before = GC.GetTotalMemory(true);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            action();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            long grown = GC.GetTotalMemory(false) - before;
            return Math.Max(grown, allocated);
        }

        // ---- entries: validation, ids, save and restore ----

        [Test]
        public void Add_RejectsInvalidInput()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            List<string> warnings = ModifierFixtures.CaptureWarnings(registry);
            ModifierScope player = ModifierScope.Player();

            Assert.Null(registry.Add("a", "taxes", player, float.NaN));
            Assert.Null(registry.Add("a", "taxes", player, float.PositiveInfinity));
            Assert.Null(registry.Add("a", "taxes", player, -1f));
            Assert.Null(registry.Add("", "taxes", player, 1.1f));
            Assert.Null(registry.Add(null, "taxes", player, 1.1f));
            Assert.Null(registry.Add("a", "", player, 1.1f));
            Assert.Null(registry.Add("a", null, player, 1.1f));
            Assert.Null(registry.Add("a", "gpu.demand.*", player, 1.1f));
            Assert.Null(registry.Add("a", "taxes", ModifierScope.Company(null), 1.1f));
            Assert.Null(registry.Add("a", "taxes", ModifierScope.Cpu(-3), 1.1f));
            Assert.Null(registry.Add("a", "taxes", player, 1.1f, -1));
            Assert.Null(registry.Add("a", "taxes", player, 1.1f, ChannelRegistry.MaxDurationDays + 1));

            Assert.Equal(12, warnings.Count);
            Assert.Equal(0, registry.EntryCount);
            Assert.True(warnings[0].StartsWith("a: "), "the warning names the feature: " + warnings[0]);
        }

        [Test]
        public void Add_ZeroMultiplierIsAllowed()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            ModifierEntry entry = registry.Add("a", "production.output", ModifierScope.Global(), 0f);

            Assert.NotNull(entry);
            Assert.Equal(0f, registry.Get("production.output", ModifierFixtures.Q(null)));
        }

        [Test]
        public void Add_StoresAllFieldsAndCopiesArguments()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            string[] args = { "recession" };
            ModifierEntry entry = registry.Add("event-system", "market.demand.lowend", ModifierScope.Global(Segment.LowEnd), 0.8f, 30, "ms.event.recession", args);
            args[0] = "changed later";

            Assert.Equal("event-system", entry.FeatureId);
            Assert.Equal("market.demand.lowend", entry.Channel);
            Assert.Equal(ModifierScope.Global(Segment.LowEnd), entry.Scope);
            Assert.Equal(0.8f, entry.Multiplier);
            Assert.Equal("ms.event.recession", entry.ReasonKey);
            Assert.Equal(new[] { "recession" }, entry.ReasonArgs);
            Assert.Equal(new DateTime(1990, 1, 31), entry.ExpiresOn);
            Assert.True(entry.ToString().Contains("market.demand.lowend"));

            ModifierEntry plain = registry.Add("a", "taxes", ModifierScope.Global(), 1.1f, 0, "", null);
            Assert.Null(plain.ReasonKey, "an empty key means no reason");
            Assert.NotNull(plain.ReasonArgs);
            Assert.Equal(0, plain.ReasonArgs.Length);
            Assert.Null(plain.ExpiresOn);
            Assert.False(plain.IsTemporary);
        }

        [Test]
        public void Ids_AreUniqueAndStable()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            ModifierEntry first = registry.Add("a", "taxes", ModifierScope.Global(), 1.1f);
            ModifierEntry second = registry.Add("a", "taxes", ModifierScope.Global(), 1.2f);
            registry.Remove(first.Id);
            ModifierEntry third = registry.Add("a", "taxes", ModifierScope.Global(), 1.3f);

            Assert.Equal("m1", first.Id);
            Assert.Equal("m2", second.Id);
            Assert.Equal("m3", third.Id, "ids are not reused");
        }

        [Test]
        public void Restore_KeepsIdAndEndDateAndMovesTheCounter()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            ModifierEntry restored = registry.Restore("m41", "alpha", "taxes", ModifierScope.Company("ai:3"), 1.4f,
                new DateTime(1990, 6, 1), "why", new[] { "x" });

            Assert.NotNull(restored);
            Assert.Equal("m41", restored.Id);
            Assert.Equal(new DateTime(1990, 6, 1), restored.ExpiresOn);
            Assert.Equal("why", restored.ReasonKey);
            Assert.Near(1.4, registry.Get("taxes", ModifierFixtures.Q("ai:3")), 1e-6);

            ModifierEntry fresh = registry.Add("alpha", "taxes", ModifierScope.Global(), 1.1f);
            Assert.Equal("m42", fresh.Id);

            ModifierEntry foreign = registry.Restore("legacy-7", "alpha", "taxes", ModifierScope.Global(), 1.1f, null);
            Assert.Equal("legacy-7", foreign.Id, "ids of another form are accepted as they are");
            Assert.Equal("m43", registry.Add("alpha", "taxes", ModifierScope.Global(), 1.1f).Id);
        }

        [Test]
        public void Restore_RejectsDuplicatesInvalidDataAndFinishedEntries()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            List<string> warnings = ModifierFixtures.CaptureWarnings(registry);
            registry.SetToday(new DateTime(1990, 5, 1));

            Assert.NotNull(registry.Restore("m1", "alpha", "taxes", ModifierScope.Global(), 1.1f, null));
            Assert.Null(registry.Restore("m1", "alpha", "taxes", ModifierScope.Global(), 1.2f, null), "id taken");
            Assert.Null(registry.Restore("", "alpha", "taxes", ModifierScope.Global(), 1.2f, null), "no id");
            Assert.Null(registry.Restore(null, "alpha", "taxes", ModifierScope.Global(), 1.2f, null));
            Assert.Null(registry.Restore("m2", "alpha", "taxes", ModifierScope.Global(), float.NaN, null), "bad multiplier");
            Assert.Null(registry.Restore("m3", "", "taxes", ModifierScope.Global(), 1.2f, null), "no feature");
            Assert.Equal(5, warnings.Count);

            warnings.Clear();
            Assert.Null(registry.Restore("m4", "alpha", "taxes", ModifierScope.Global(), 1.2f, new DateTime(1990, 5, 1)), "over today");
            Assert.Null(registry.Restore("m5", "alpha", "taxes", ModifierScope.Global(), 1.2f, new DateTime(1989, 1, 1)));
            Assert.Equal(0, warnings.Count, "a finished entry is dropped without a warning");
            Assert.NotNull(registry.Restore("m6", "alpha", "taxes", ModifierScope.Global(), 1.2f, new DateTime(1990, 5, 2)));
            Assert.Equal(2, registry.EntryCount);
        }

        [Test]
        public void SaveAndLoad_RoundTrip()
        {
            ChannelRegistry source = ModifierFixtures.Registry();
            source.Add("alpha", "research.speed", ModifierScope.Player(), 1.25f, 90, "boost");
            source.Add("beta", "market.rating.cpu", ModifierScope.Cpu(12, Segment.HighEnd), 1.5f);
            source.Add("beta", "taxes", ModifierScope.Company("ai:5"), 0.9f, 10, "why", new[] { "a", "b" });

            ChannelRegistry loaded = ModifierFixtures.Registry();
            loaded.SetToday(source.Today);
            foreach (ModifierEntry entry in source.Entries)
            {
                // What the side file keeps: id, feature, channel, scope parts, multiplier, end date, reason.
                ModifierScope scope;
                string error;
                Assert.True(ModifierScope.TryCreate(entry.Scope.Kind.ToString(), entry.Scope.CompanyKey, entry.Scope.CpuSaveId, (int)entry.Scope.Segment, out scope, out error), error);
                Assert.NotNull(loaded.Restore(entry.Id, entry.FeatureId, entry.Channel, scope, entry.Multiplier, entry.ExpiresOn, entry.ReasonKey, entry.ReasonArgs));
            }

            Assert.Equal(3, loaded.EntryCount);
            for (int i = 0; i < 3; i++)
            {
                ModifierEntry a = source.Entries[i];
                ModifierEntry b = loaded.Entries[i];
                Assert.Equal(a.Id, b.Id);
                Assert.Equal(a.FeatureId, b.FeatureId);
                Assert.Equal(a.Channel, b.Channel);
                Assert.Equal(a.Scope, b.Scope);
                Assert.Equal(a.Multiplier, b.Multiplier);
                Assert.Equal(a.ExpiresOn, b.ExpiresOn);
                Assert.Equal(a.ReasonKey, b.ReasonKey);
                Assert.Equal(a.ReasonArgs, b.ReasonArgs);
            }
            Assert.Equal(source.Get("market.rating.cpu", ModifierFixtures.Q("player", 12, Segment.HighEnd)),
                loaded.Get("market.rating.cpu", ModifierFixtures.Q("player", 12, Segment.HighEnd)));
        }

        [Test]
        public void Remove_RemoveAll_ClearEntries()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            ModifierEntry a1 = registry.Add("alpha", "taxes", ModifierScope.Global(), 1.1f);
            registry.Add("alpha", "bank.interest", ModifierScope.Global(), 1.2f);
            registry.Add("alpha", "taxes", ModifierScope.Player(), 1.3f);
            registry.Add("beta", "taxes", ModifierScope.Global(), 1.4f);

            Assert.False(registry.Remove("nope"));
            Assert.False(registry.Remove(null));
            Assert.True(registry.Remove(a1.Id));
            Assert.False(registry.Remove(a1.Id), "already gone");
            Assert.Equal(3, registry.EntryCount);

            Assert.Equal(1, registry.RemoveAll("alpha", "taxes"));
            Assert.Equal(2, registry.EntryCount);
            Assert.Equal(1, registry.RemoveAll("alpha"));
            Assert.Equal(0, registry.RemoveAll("alpha"));
            Assert.Equal(0, registry.RemoveAll(null));
            Assert.Equal(1, registry.EntryCount);
            Assert.Near(1.4, registry.Get("taxes", ModifierFixtures.Q(null)), 1e-6);

            registry.RegisterProvider("p", "taxes", q => 2f);
            Assert.Equal(1, registry.ClearEntries());
            Assert.Equal(0, registry.EntryCount);
            Assert.Equal(1, registry.ProviderCount, "providers are not entries");
            Assert.Equal("m1", registry.Add("alpha", "taxes", ModifierScope.Global(), 1.1f).Id, "the counter starts again");
            Assert.Equal(1, registry.ClearEntries());
        }

        [Test]
        public void Entries_AreSnapshots()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "taxes", ModifierScope.Global(), 1.1f);
            registry.Add("a", "bank.interest", ModifierScope.Global(), 1.2f);
            registry.Add("b", "taxes", ModifierScope.Global(), 1.3f);

            IReadOnlyList<ModifierEntry> snapshot = registry.Entries;
            IReadOnlyList<ModifierEntry> taxes = registry.EntriesOf("taxes");
            registry.RemoveAll("a");

            Assert.Equal(3, snapshot.Count, "later changes do not touch a snapshot");
            Assert.Equal(new[] { "m1", "m2", "m3" }, snapshot.Select(e => e.Id).ToArray(), "in the order of adding");
            Assert.Equal(2, taxes.Count);
            Assert.Equal(1, registry.EntriesOf("taxes").Count);
            Assert.Equal(0, registry.EntriesOf("nope").Count);
            Assert.Equal(0, registry.EntriesOf(null).Count);
            Assert.Null(registry.Find("zzz"));
            Assert.Null(registry.Find(null));
        }

        // ---- notifications ----

        [Test]
        public void ChannelChanged_RaisedForEveryKindOfChange()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            var changed = new List<string>();
            registry.ChannelChanged += changed.Add;

            ModifierEntry entry = registry.Add("a", "taxes", ModifierScope.Global(), 1.1f);
            registry.RegisterProvider("a", "bank.interest", q => 1.1f);
            registry.Remove(entry.Id);
            registry.Add("a", "research.speed", ModifierScope.Global(), 1.1f, 5);
            registry.SetToday(ModifierFixtures.Start.AddDays(5));

            Assert.Equal(new[] { "taxes", "bank.interest", "taxes", "research.speed", "research.speed" }, changed.ToArray());

            changed.Clear();
            registry.RemoveAll("a", "research.speed");
            registry.RemoveProviders("a");
            Assert.Equal(new[] { "bank.interest" }, changed.ToArray(), "removing nothing raises nothing");
        }

        [Test]
        public void ChannelChanged_ThrowingHandlerIsIsolated()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            List<string> warnings = ModifierFixtures.CaptureWarnings(registry);
            var seen = new List<string>();
            registry.ChannelChanged += c => { throw new InvalidOperationException("ui is gone"); };
            registry.ChannelChanged += seen.Add;

            Assert.NotNull(registry.Add("a", "taxes", ModifierScope.Global(), 1.1f));

            Assert.Equal(new[] { "taxes" }, seen.ToArray(), "the second handler still ran");
            Assert.True(warnings.Any(w => w.Contains("ui is gone")));
            Assert.Near(1.1, registry.Get("taxes", ModifierFixtures.Q(null)), 1e-6);
        }

        [Test]
        public void ThrowingWarningHandler_DoesNotBreakTheCall()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Warning += (feature, text) => { throw new InvalidOperationException("sink"); };

            Assert.Null(registry.Add("a", "taxes", ModifierScope.Global(), float.NaN));
        }

        [Test]
        public void Version_GrowsWithEveryChangeThatCanAlterAResult()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            int version = registry.Version;

            ModifierEntry entry = registry.Add("a", "taxes", ModifierScope.Global(), 1.1f);
            Assert.True(registry.Version > version);
            version = registry.Version;

            registry.RegisterProvider("a", "taxes", q => 1f);
            Assert.True(registry.Version > version);
            version = registry.Version;

            registry.Remove(entry.Id);
            Assert.True(registry.Version > version);
            version = registry.Version;

            registry.Invalidate("taxes");
            Assert.True(registry.Version > version);
            version = registry.Version;

            registry.SetToday(registry.Today);
            Assert.Equal(version, registry.Version, "the same day changes nothing");
            registry.SetToday(registry.Today.AddDays(1));
            Assert.True(registry.Version > version);
            version = registry.Version;

            registry.Get("taxes", ModifierFixtures.Q(null));
            Assert.Equal(version, registry.Version, "reading changes nothing");
        }

        [Test]
        public void Counts_EntriesAndProviders()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            Assert.Equal(0, registry.EntryCount);
            Assert.Equal(0, registry.ProviderCount);

            registry.Add("a", "taxes", ModifierScope.Global(), 1.1f);
            registry.Add("a", "taxes", ModifierScope.Global(), 1.1f);
            registry.RegisterProvider("a", "taxes", q => 1f);

            Assert.Equal(2, registry.EntryCount);
            Assert.Equal(1, registry.ProviderCount);
            Assert.Equal(1, registry.ProvidersOf("a"));
            Assert.Equal(0, registry.ProvidersOf("b"));
            Assert.Equal(0, registry.ProvidersOf("a", "bank.interest"));
        }
    }
}
