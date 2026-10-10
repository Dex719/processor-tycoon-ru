using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Modifiers;

namespace PTMod.Tests.Modifiers
{
    /// <summary>
    /// Explain (FR-12 items 9 and 14, AC-12.9, TSK-059): applied multipliers with source, reason and term, provider reasons
    /// (fixed and per call), what is left out, the list interface, Raw/Total.
    /// </summary>
    [Covers(typeof(ModifierBreakdown), typeof(AppliedModifier), typeof(ProviderResult))]
    public class ExplainTests
    {
        private static readonly ModifierQuery LowEnd = ModifierFixtures.Q("player", 4, Segment.LowEnd);

        /// <summary>A stand-in for ModL10n: key and arguments to text.</summary>
        private static string Localize(string key, string[] args)
        {
            var texts = new Dictionary<string, string>
            {
                { "press.mod.hype", "Ажиотаж" },
                { "ms.event.recession", "Событие: {0}" }
            };
            string text;
            return key != null && texts.TryGetValue(key, out text) ? string.Format(text, (object[])args) : key;
        }

        // ---- AC-12.9 ----

        [Test]
        public void Ac129_EntryAndProviderReasonsAreListedWithTheirSources()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.RegisterProvider("press-reviews", "market.demand.lowend", q => 1.15f, "press.mod.hype");
            registry.Add("market-shocks", "market.demand.lowend", ModifierScope.Global(), 0.8f, 60, "ms.event.recession", new[] { "рецессия" });

            ModifierBreakdown breakdown = registry.Explain("market.demand.lowend", LowEnd);

            Assert.Equal(2, breakdown.Count);
            AppliedModifier entry = breakdown[0];
            AppliedModifier provider = breakdown[1];

            Assert.Equal("market-shocks", entry.FeatureId);
            Assert.NotNull(entry.EntryId);
            Assert.False(entry.IsProvider);
            Assert.Equal("ms.event.recession", entry.ReasonKey);
            Assert.Equal(new[] { "рецессия" }, entry.ReasonArgs);
            Assert.Equal(0.8f, entry.Multiplier);
            Assert.Equal(new DateTime(1990, 3, 2), entry.ExpiresOn);
            Assert.Equal("Событие: рецессия", Localize(entry.ReasonKey, entry.ReasonArgs));

            Assert.Equal("press-reviews", provider.FeatureId);
            Assert.Null(provider.EntryId);
            Assert.True(provider.IsProvider);
            Assert.Equal("press.mod.hype", provider.ReasonKey);
            Assert.Equal(0, provider.ReasonArgs.Length);
            Assert.Equal(1.15f, provider.Multiplier);
            Assert.Null(provider.ExpiresOn);
            Assert.Equal("Ажиотаж", Localize(provider.ReasonKey, provider.ReasonArgs));

            Assert.Near(0.8 * 1.15, breakdown.Raw, 1e-6);
            Assert.Near(0.8 * 1.15, breakdown.Total, 1e-6);
        }

        [Test]
        public void ReasonedProvider_GivesAReasonPerCall()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.RegisterReasonedProvider("press-reviews", "market.demand.lowend", q =>
                q.CompanyKey == "player"
                    ? new ProviderResult(1.2f, "press.mod.hype", new[] { "player" })
                    : new ProviderResult(1.05f, "press.mod.hype", new[] { q.CompanyKey ?? "?" }));

            AppliedModifier forPlayer = registry.Explain("market.demand.lowend", ModifierFixtures.Q("player"))[0];
            AppliedModifier forAi = registry.Explain("market.demand.lowend", ModifierFixtures.Q("ai:3"))[0];

            Assert.Equal(1.2f, forPlayer.Multiplier);
            Assert.Equal(new[] { "player" }, forPlayer.ReasonArgs);
            Assert.Equal(1.05f, forAi.Multiplier);
            Assert.Equal(new[] { "ai:3" }, forAi.ReasonArgs);
            Assert.Near(1.2, registry.Get("market.demand.lowend", ModifierFixtures.Q("player")), 1e-6);
        }

        [Test]
        public void ReasonedProvider_ResultReasonOverridesTheFixedOne()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.RegisterReasonedProvider("p", "taxes", q => new ProviderResult(1.5f, "own.reason"));
            registry.RegisterReasonedProvider("p2", "taxes", q => 1.1f);                        // implicit: no reason
            registry.RegisterReasonedProvider("p3", "taxes", q => new ProviderResult(1.2f));    // explicit: no reason

            ModifierBreakdown breakdown = registry.Explain("taxes", ModifierFixtures.Q(null));

            Assert.Equal("own.reason", breakdown[0].ReasonKey);
            Assert.Equal(0, breakdown[0].ReasonArgs.Length, "null arguments become an empty array");
            Assert.Null(breakdown[1].ReasonKey);
            Assert.Null(breakdown[2].ReasonKey);
        }

        [Test]
        public void ReasonedProvider_FixedKeyIsTheFallback()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.RegisterProvider("p", "taxes", q => 1.5f, "fixed.reason");

            Assert.Equal("fixed.reason", registry.Explain("taxes", ModifierFixtures.Q(null))[0].ReasonKey);
        }

        [Test]
        public void Provider_WithoutReason_IsListedWithoutOne()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.RegisterProvider("p", "taxes", q => 1.5f);
            registry.Add("e", "taxes", ModifierScope.Global(), 1.1f);

            ModifierBreakdown breakdown = registry.Explain("taxes", ModifierFixtures.Q(null));

            Assert.Equal(2, breakdown.Count);
            Assert.Null(breakdown[0].ReasonKey);
            Assert.Null(breakdown[1].ReasonKey);
            Assert.NotNull(breakdown[0].ReasonArgs);
            Assert.NotNull(breakdown[1].ReasonArgs);
        }

        // ---- what is listed ----

        [Test]
        public void Explain_ListsOnlyWhatTookPart()
        {
            var features = new FeatureSwitch();
            ChannelRegistry registry = ModifierFixtures.Registry(features);
            registry.Add("applies", "market.rating.cpu", ModifierScope.Player(), 1.2f);
            registry.Add("other-company", "market.rating.cpu", ModifierScope.AIOnly(), 1.3f);
            registry.Add("other-segment", "market.rating.cpu", ModifierScope.Global(Segment.HighEnd), 1.4f);
            registry.Add("switched-off", "market.rating.cpu", ModifierScope.Global(), 1.5f);
            registry.Add("neutral", "market.rating.cpu", ModifierScope.Global(), 1f);
            registry.Add("ended", "market.rating.cpu", ModifierScope.Global(), 1.6f, 5);
            registry.RegisterProvider("switched-off-provider", "market.rating.cpu", q => 1.7f);
            registry.RegisterProvider("neutral-provider", "market.rating.cpu", q => 1f);
            registry.RegisterProvider("broken-provider", "market.rating.cpu", q => { throw new InvalidOperationException(); });
            features.Disable("switched-off");
            features.Disable("switched-off-provider");
            registry.SetToday(ModifierFixtures.Start.AddDays(5));

            ModifierBreakdown breakdown = registry.Explain("market.rating.cpu", LowEnd);

            Assert.Equal(new[] { "applies" }, breakdown.Select(m => m.FeatureId).ToArray());
            Assert.Near(1.2, breakdown.Total, 1e-6);
        }

        [Test]
        public void Explain_IsNeverCached_AndCallsProviders()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            int calls = 0;
            registry.RegisterProvider("p", "taxes", q => { calls++; return 1.5f; });

            registry.Get("taxes", ModifierFixtures.Q("player"));
            registry.Get("taxes", ModifierFixtures.Q("player"));
            Assert.Equal(1, calls);

            registry.Explain("taxes", ModifierFixtures.Q("player"));
            registry.Explain("taxes", ModifierFixtures.Q("player"));
            Assert.Equal(3, calls, "each Explain evaluates again");
            Assert.Equal(1, registry.Cache.CountOf("taxes"), "and leaves the cache alone");
        }

        [Test]
        public void Explain_ProviderFailureIsReportedAndLeftOut()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            var reports = new List<string>();
            registry.ProviderFailed += (feature, channel, ex) => reports.Add(feature);
            registry.RegisterProvider("broken", "taxes", q => { throw new InvalidOperationException(); });
            registry.RegisterProvider("fine", "taxes", q => 1.5f);

            ModifierBreakdown breakdown = registry.Explain("taxes", ModifierFixtures.Q(null));

            Assert.Equal(1, breakdown.Count);
            Assert.Equal("fine", breakdown[0].FeatureId);
            Assert.Equal(new[] { "broken" }, reports.ToArray());
        }

        [Test]
        public void Explain_EntriesComeInTheOrderTheyWereAdded_ThenProviders()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.RegisterProvider("prov-1", "taxes", q => 1.1f);
            registry.Add("entry-1", "taxes", ModifierScope.Global(), 1.2f);
            registry.RegisterProvider("prov-2", "taxes", q => 1.3f);
            registry.Add("entry-2", "taxes", ModifierScope.Global(), 1.4f);

            ModifierBreakdown breakdown = registry.Explain("taxes", ModifierFixtures.Q(null));

            Assert.Equal(new[] { "entry-1", "entry-2", "prov-1", "prov-2" }, breakdown.Select(m => m.FeatureId).ToArray());
        }

        [Test]
        public void Explain_NothingRegistered_IsNeutral()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();

            ModifierBreakdown breakdown = registry.Explain("market.rating.cpu", LowEnd);

            Assert.Equal("market.rating.cpu", breakdown.Channel);
            Assert.Equal(0, breakdown.Count);
            Assert.Equal(1f, breakdown.Raw);
            Assert.Equal(1f, breakdown.Total);
            Assert.False(breakdown.IsBalanceCapped);
            Assert.False(breakdown.IsFuseCapped);
            Assert.True(breakdown.IsDeclared);
            Assert.Equal(0.5f, breakdown.BalanceMin, "the declared bounds are still reported");
            Assert.Equal(2f, breakdown.BalanceMax);
            Assert.Equal(0f, breakdown.FuseMin);
            Assert.Equal(5f, breakdown.FuseMax);
        }

        [Test]
        public void Explain_UnknownAndNullChannel()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();

            ModifierBreakdown unknown = registry.Explain("typo.channel", LowEnd);
            Assert.False(unknown.IsDeclared);
            Assert.Equal(ChannelRegistry.DefaultFuseMin, unknown.FuseMin);
            Assert.Equal(ChannelRegistry.DefaultFuseMax, unknown.FuseMax);
            Assert.Equal(1f, unknown.Total);

            ModifierBreakdown none = registry.Explain(null, LowEnd);
            Assert.Null(none.Channel);
            Assert.Equal(1f, none.Total);
            Assert.Equal(0, none.Count);
        }

        [Test]
        public void Explain_FlagsTheFuseOfAnUndeclaredChannel()
        {
            var registry = new ChannelRegistry();
            registry.Add("a", "typo.channel", ModifierScope.Global(), 25f);

            ModifierBreakdown breakdown = registry.Explain("typo.channel", ModifierFixtures.Q(null));

            Assert.False(breakdown.IsDeclared);
            Assert.True(breakdown.IsFuseCapped);
            Assert.False(breakdown.IsBalanceCapped);
            Assert.Near(25.0, breakdown.Raw, 1e-6);
            Assert.Equal(10f, breakdown.Total);
        }

        // ---- the list interface ----

        [Test]
        public void Breakdown_IsAReadOnlyList()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "taxes", ModifierScope.Global(), 1.1f);
            registry.Add("b", "taxes", ModifierScope.Global(), 1.2f);
            registry.Add("c", "taxes", ModifierScope.Global(), 1.3f);

            IReadOnlyList<AppliedModifier> list = registry.Explain("taxes", ModifierFixtures.Q(null));

            Assert.Equal(3, list.Count);
            Assert.Equal("b", list[1].FeatureId);
            Assert.Equal(new[] { "a", "b", "c" }, list.Select(m => m.FeatureId).ToArray());
            Assert.Equal(3, list.Count(m => m.Multiplier > 1f));

            int viaNonGeneric = 0;
            foreach (object item in (System.Collections.IEnumerable)list) viaNonGeneric += item is AppliedModifier ? 1 : 0;
            Assert.Equal(3, viaNonGeneric);

            Assert.Throws<IndexOutOfRangeException>(() => { AppliedModifier x = list[3]; });
        }

        [Test]
        public void Breakdown_ForSimpleConsumers_SumsUpLikeTheProduct()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "taxes", ModifierScope.Global(), 1.25f);
            registry.Add("b", "taxes", ModifierScope.Global(), 0.8f);
            registry.RegisterProvider("c", "taxes", q => 1.5f);

            ModifierBreakdown breakdown = registry.Explain("taxes", ModifierFixtures.Q(null));
            double product = 1.0;
            foreach (AppliedModifier m in breakdown) product *= m.Multiplier;

            Assert.Near(product, breakdown.Raw, 1e-6);
            Assert.Near(1.5, breakdown.Total, 1e-6);
        }

        // ---- the small types ----

        [Test]
        public void ProviderResult_ConversionAndConstructor()
        {
            ProviderResult fromFloat = 1.25f;
            Assert.Equal(1.25f, fromFloat.Multiplier);
            Assert.Null(fromFloat.ReasonKey);
            Assert.Null(fromFloat.ReasonArgs);

            var full = new ProviderResult(0.5f, "why", new[] { "a", "b" });
            Assert.Equal(0.5f, full.Multiplier);
            Assert.Equal("why", full.ReasonKey);
            Assert.Equal(new[] { "a", "b" }, full.ReasonArgs);

            var withKeyOnly = new ProviderResult(2f, "why");
            Assert.Null(withKeyOnly.ReasonArgs);

            var initialized = new ProviderResult { Multiplier = 3f, ReasonKey = "k" };
            Assert.Equal(3f, initialized.Multiplier);
            Assert.Equal("k", initialized.ReasonKey);

            Assert.Equal(0f, default(ProviderResult).Multiplier, "default is a zero multiplier, not 1: always build the result");
        }

        [Test]
        public void AppliedModifier_Fields()
        {
            var item = new AppliedModifier("feature", "m7", "why", new[] { "x" }, 1.5f, new DateTime(1991, 2, 3));

            Assert.Equal("feature", item.FeatureId);
            Assert.Equal("m7", item.EntryId);
            Assert.Equal("why", item.ReasonKey);
            Assert.Equal(new[] { "x" }, item.ReasonArgs);
            Assert.Equal(1.5f, item.Multiplier);
            Assert.Equal(new DateTime(1991, 2, 3), item.ExpiresOn);
            Assert.False(item.IsProvider);

            var provider = new AppliedModifier("feature", null, null, null, 0.9f, null);
            Assert.True(provider.IsProvider);
            Assert.NotNull(provider.ReasonArgs);
            Assert.Equal(0, provider.ReasonArgs.Length);
            Assert.Null(provider.ExpiresOn);
        }
    }
}
