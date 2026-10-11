using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The aggregator of the events' contributions to the channels of <c>ModModifiers</c> (FR-5 item 6, D4, D10, D13, D15, NFR-8): the product of the
    /// contributions of the company and the global ones, the limits of <c>channelLimits</c> (the narrowest template, <c>popularity.gain</c> [0.7; 1.6]), one
    /// provider per channel registered once, an invalidation on every change of a contribution (AC-5.4), freezing a pack, dropping everything, and the
    /// timed standard effects on top of it: AC-5.2 (×1.12 × ×1.10 = 1.232 through the core's channel engine, one line in <c>Explain</c>) and AC-5.5 (×0.55 at
    /// intensity ×2 is held to 0.4 and marked, the text and the channel show the same number).
    /// </summary>
    [Covers(typeof(ModifierAggregator), typeof(IModifierChannelSink), typeof(IEffectModifiers), typeof(ModifierTimedEffect), typeof(FactoryOutputEffect),
        typeof(MarketDemandEffect), typeof(TimedModifierEffect))]
    public class ModifierAggregatorTests
    {
        private static AppliedEffect E(string id)
        {
            return new AppliedEffect { id = id, instanceId = 1 };
        }

        [Test]
        public void TheProduct_IsTheCompanysContributionsTimesTheGlobalOnes_HeldToTheLimits()
        {
            var sink = new RecordingSink();
            var aggregator = new ModifierAggregator(sink, () => EffectsTestKitConfig.Design);

            aggregator.Set(E("1/a/0"), "research.speed", CompanyKey.Player, 1.12f);
            aggregator.Set(E("2/a/0"), "research.speed", CompanyKey.Player, 1.10f);
            aggregator.Set(E("3/a/0"), "research.speed", EventCompanyKeys.Global, 1.5f);
            aggregator.Set(E("4/a/0"), "research.speed", "ai:1", 0.5f);

            Assert.Near(1.12 * 1.10 * 1.5, aggregator.Current("research.speed", CompanyKey.Player), 1e-5);
            Assert.Near(0.5 * 1.5, aggregator.Current("research.speed", "ai:1"), 1e-5);
            Assert.Near(1.5, aggregator.Current("research.speed", "ai:2"), 1e-5, "only the global contribution");
            Assert.Near(1.5, aggregator.Current("research.speed", null), 1e-5, "a question without a company: the global ones");
            Assert.Equal(1f, aggregator.Current("taxes", CompanyKey.Player), "no contribution: x1");

            aggregator.Set(E("5/a/0"), "research.speed", CompanyKey.Player, 3f);
            Assert.Equal(4f, aggregator.Current("research.speed", CompanyKey.Player), "the product 5.5 is held to the default max 4");
            Assert.True(aggregator.WasClamped("research.speed", CompanyKey.Player));
            Assert.False(aggregator.WasClamped("research.speed", "ai:2"));
        }

        [Test]
        public void TheLimits_AreTheNarrowestTemplate_PopularityGainIsHeldTo_0_7_And_1_6()
        {
            var aggregator = new ModifierAggregator(new RecordingSink(), () => EffectsTestKitConfig.Design);

            Assert.Equal(0.7f, aggregator.LimitMin("popularity.gain"));
            Assert.Equal(1.6f, aggregator.LimitMax("popularity.gain"));
            Assert.Equal(0.1f, aggregator.LimitMin("production.output"));
            Assert.Equal(3f, aggregator.LimitMax("production.output"));
            Assert.Equal(0.4f, aggregator.LimitMin("market.demand.lowend"), "the family template");
            Assert.Equal(0.25f, aggregator.LimitMin("taxes"), "'*'");

            aggregator.Set(E("1/a/0"), "popularity.gain", CompanyKey.Player, 1.25f);
            aggregator.Set(E("2/a/0"), "popularity.gain", CompanyKey.Player, 1.10f);
            aggregator.Set(E("3/a/0"), "popularity.gain", CompanyKey.Player, 1.25f);
            Assert.Equal(1.6f, aggregator.Current("popularity.gain", CompanyKey.Player), "1.72 → 1.6 (NFR-8)");

            aggregator.Set(E("4/a/0"), "popularity.gain", "ai:1", 0.85f);
            aggregator.Set(E("5/a/0"), "popularity.gain", "ai:1", 0.75f);
            Assert.Equal(0.7f, aggregator.Current("popularity.gain", "ai:1"), "0.6375 → 0.7");
        }

        [Test]
        public void OneProviderPerChannel_IsRegisteredOnce_WithTheReasonOfTheEvents()
        {
            var sink = new RecordingSink();
            var aggregator = new ModifierAggregator(sink, () => EffectsTestKitConfig.Design, effect => null, effect => "Event " + effect.id);

            aggregator.Set(E("1/a/0"), "research.speed", CompanyKey.Player, 1.12f);
            aggregator.Set(E("2/a/0"), "research.speed", "ai:1", 1.10f);
            aggregator.Clear(E("1/a/0"));
            aggregator.Set(E("3/a/0"), "research.speed", CompanyKey.Player, 1.2f);
            aggregator.Set(E("4/a/0"), "taxes", EventCompanyKeys.Global, 0.9f);
            aggregator.ClearAll();
            aggregator.Set(E("5/a/0"), "research.speed", CompanyKey.Player, 1.3f);

            Assert.Equal("research.speed,taxes", string.Join(",", sink.Registered), "once per channel, never again after a removal or ClearAll");
            Assert.Equal("research.speed,taxes", string.Join(",", aggregator.Channels));

            aggregator.Set(E("6/a/0"), "research.speed", EventCompanyKeys.Global, 1.1f);
            ProviderResult result = sink.Providers["research.speed"](CompanyKey.Player);
            Assert.Near(1.3 * 1.1, result.Multiplier, 1e-5);
            Assert.Equal(ModifierAggregator.ReasonKey, result.ReasonKey);
            Assert.Equal("es.reason.events", result.ReasonKey);
            Assert.Equal("Event 5/a/0, Event 6/a/0", result.ReasonArgs[0], "the names of the events behind the product");
            Assert.Equal("Event 6/a/0", sink.Providers["research.speed"]("ai:1").ReasonArgs[0]);
        }

        [Test]
        public void EveryChangeOfAContribution_InvalidatesTheChannel_ForItsCompany_OrForAllWhenGlobal()
        {
            var sink = new RecordingSink();
            var aggregator = new ModifierAggregator(sink, () => EffectsTestKitConfig.Design, effect => effect.id.StartsWith("9/") ? "market-shocks" : null);
            AppliedEffect a = E("1/a/0");
            AppliedEffect g = E("2/common/0");
            AppliedEffect pack = E("9/common/0");

            aggregator.Set(a, "research.speed", CompanyKey.Player, 1.12f);           // new
            aggregator.Set(a, "research.speed", CompanyKey.Player, 1.12f);           // the same value: not a change
            aggregator.Set(a, "research.speed", CompanyKey.Player, 1.15f);           // a curve moved
            aggregator.Set(g, "market.demand.lowend", EventCompanyKeys.Global, 0.9f);
            aggregator.Clear(a);
            aggregator.Clear(E("unknown"));                                          // nothing to remove
            aggregator.Set(pack, "production.waferCost", "ai:2", 1.3f);
            aggregator.Suspend("market-shocks");
            aggregator.Suspend("market-shocks");                                      // already frozen
            aggregator.Resume("market-shocks");
            aggregator.ClearAll();

            Assert.Equal(string.Join(";", new[]
            {
                "research.speed|player", "research.speed|player", "market.demand.lowend|*", "research.speed|player", "production.waferCost|ai:2",
                "production.waferCost|ai:2", "production.waferCost|ai:2", "market.demand.lowend|*", "production.waferCost|ai:2",
            }), string.Join(";", sink.Invalidations));
        }

        [Test]
        public void Suspend_TakesThePacksContributionsOut_AndResume_PutsThemBack()
        {
            var aggregator = new ModifierAggregator(new RecordingSink(), () => EffectsTestKitConfig.Design,
                effect => effect.id.StartsWith("9/") ? "market-shocks" : "event-system");
            aggregator.Set(E("1/a/0"), "production.output", CompanyKey.Player, 0.6f);
            aggregator.Set(E("9/a/0"), "production.output", CompanyKey.Player, 0.5f);

            Assert.Near(0.3, aggregator.Current("production.output", CompanyKey.Player), 1e-6);
            aggregator.Suspend("market-shocks");
            Assert.True(aggregator.IsSuspended("market-shocks"));
            Assert.Near(0.6, aggregator.Current("production.output", CompanyKey.Player), 1e-6, "the other pack is not touched");
            Assert.Equal(2, aggregator.Count, "frozen contributions are kept");
            aggregator.Resume("market-shocks");
            Assert.Near(0.3, aggregator.Current("production.output", CompanyKey.Player), 1e-6);
        }

        [Test]
        public void ClearAll_DropsEveryContribution_AndTheProductIsBuiltAgainFromNewOnes()
        {
            var aggregator = new ModifierAggregator(new RecordingSink(), () => EffectsTestKitConfig.Design);
            aggregator.Set(E("1/a/0"), "research.speed", CompanyKey.Player, 1.12f);
            aggregator.Suspend("market-shocks");

            aggregator.ClearAll();
            Assert.Equal(0, aggregator.Count);
            Assert.Equal(1f, aggregator.Current("research.speed", CompanyKey.Player));
            Assert.True(aggregator.IsSuspended("market-shocks"), "the switches of packs are not part of a save");

            aggregator.Set(E("7/a/0"), "research.speed", CompanyKey.Player, 1.05f);
            Assert.Near(1.05, aggregator.Current("research.speed", CompanyKey.Player), 1e-6);
        }

        [Test]
        public void AC_5_2_TwoModifiers_1_12_And_1_10_Give_1_232_ThroughModModifiers_WithOneLineInExplain()
        {
            EventDefinition definition = Fx.Event("es.boost", null,
                Fx.Effect("ModifierTimed", "{ 'channel': 'research.speed', 'multiplier': 1.12 }", 365),
                Fx.Effect("ModifierTimed", "{ 'channel': 'research.speed', 'multiplier': 1.10 }", 180));
            var rig = new EffectsRig(new FakeCatalog(EffectsTestKitConfig.Design, definition));
            rig.World.Today = 7000;

            Assert.Equal(1f, rig.Channel("research.speed"), "nothing yet (the value is cached for the day now)");
            Assert.Equal(EventFireResult.Fired, rig.Fire("es.boost", CompanyKey.Player));

            Assert.Near(1.232, rig.Channel("research.speed"), 1e-5, "at once, after Invalidate");
            ModifierBreakdown explain = rig.Sink.Registry.Explain("research.speed", new ModifierQuery(CompanyKey.Player));
            Assert.Equal(1, explain.Count, "one line of the provider event-system");
            Assert.Equal("event-system", explain[0].FeatureId);
            Assert.Equal("es.reason.events", explain[0].ReasonKey);
            Assert.Equal("name of 1", explain[0].ReasonArgs[0]);
            Assert.Equal(1f, rig.Channel("research.speed", "ai:1"), "the player's contribution only");
        }

        [Test]
        public void AC_5_4_AChoiceMadeOnPause_IsSeenByTheChannelAtOnce_BecauseOfInvalidate()
        {
            EffectsRig rig = HireRig();
            EventInstanceDto instance = rig.Instance("es.star_engineer");

            Assert.Equal(1f, rig.Channel("research.speed"), "read by a formula this morning: cached for the day");
            int before = rig.Sink.Invalidations.Count;
            rig.Runtime.ResolveChoice(instance.id, "hire", false, 7000);   // the same day, on pause

            Assert.True(rig.Sink.Invalidations.Count > before, "the choice invalidated the channel");
            Assert.True(rig.Sink.Invalidations.Contains("research.speed|player"));
            Assert.Near(1.12, rig.Channel("research.speed"), 1e-6, "the same moment");
        }

        [Test]
        public void WithoutInvalidate_TheChannelWouldKeepTheOldValueUntilTheDayEnds()
        {
            // The control of AC-5.4: the cache of the core is real; only the invalidation makes the choice visible the same day.
            EffectsRig rig = HireRig();
            EventInstanceDto instance = rig.Instance("es.star_engineer");
            Assert.Equal(1f, rig.Channel("research.speed"));

            rig.Sink.ForwardInvalidate = false;
            rig.Runtime.ResolveChoice(instance.id, "hire", false, 7000);
            Assert.Equal(1f, rig.Channel("research.speed"), "the stale cached value");

            rig.Sink.Registry.Invalidate("research.speed", CompanyKey.Player);
            Assert.Near(1.12, rig.Channel("research.speed"), 1e-6);
        }

        /// <summary>The player's es.star_engineer on day 7000: its own effect puts a neutral contribution on research.speed (so the channel has its provider and a cached value), "hire" adds ×1.12.</summary>
        private static EffectsRig HireRig()
        {
            EventDefinition definition = Fx.Event("es.star_engineer",
                new[] { Fx.Choice("decline"), Fx.Choice("hire", Fx.Effect("ModifierTimed", "{ 'channel': 'research.speed', 'multiplier': 1.12 }", 365)) },
                Fx.Effect("ModifierTimed", "{ 'channel': 'research.speed', 'multiplier': 1.0 }", 365));
            var rig = new EffectsRig(new FakeCatalog(EffectsTestKitConfig.Design, definition));
            rig.World.Today = 7000;
            Assert.Equal(EventFireResult.Fired, rig.Fire("es.star_engineer", CompanyKey.Player));
            return rig;
        }

        [Test]
        public void AC_5_5_FactoryOutput_0_55_AtIntensity2_IsHeldTo_0_4_MarkedClamped_AndTheTextAndTheChannelAgree()
        {
            var pack = new PackConfig { PackId = "market-shocks" };
            pack.ChannelLimits.Add(new ChannelLimit { Pattern = "production.output", Min = 0.4f, Max = 1.5f, PackId = "market-shocks" });
            EventConfig config = EventConfig.Merge(new[] { pack }, line => { });

            EventDefinition definition = Fx.Event("ms.strike", null, Fx.Effect("FactoryOutput", "{ 'multiplier': 0.55, 'scalable': true }", 21));
            definition.packId = "market-shocks";
            var catalog = new FakeCatalog(config, definition).Pack("market-shocks", new PackOptions { Severity = 2f, SettingsFeatureId = "market-shocks" });
            var rig = new EffectsRig(catalog);
            rig.World.Today = 7000;

            Assert.Equal(EventFireResult.Fired, rig.Fire("ms.strike", CompanyKey.Player));
            AppliedEffect effect = rig.Runtime.ActiveEffects[0];
            Assert.Equal(0.4f, effect.value, "1 + (0.55 - 1) × 2 = 0.1 → the limit 0.4");
            Assert.True(effect.clamped, "marked 'ограничено'");
            Assert.Equal("es.fx.factory_output", effect.textKey);
            Assert.Equal("-60%", effect.textArgs[0], "the text shows the clamped number");
            Assert.Near(0.4, rig.Channel("production.output"), 1e-6, "the channel gets the same number");
            Assert.True(rig.Entries(EventLogKinds.Fired)[0].effects[0].clamped, "the journal keeps the mark");
        }

        [Test]
        public void ANonScalableModifier_IsNotScaled_ButStillHeldToItsChannelLimits()
        {
            var aggregator = new ModifierAggregator(new RecordingSink(), () => EffectsTestKitConfig.Design);
            var handler = new ModifierTimedEffect();
            var call = new EffectCall { CompanyKey = CompanyKey.Player, Severity = 1f, Contributions = aggregator };

            AppliedEffect effect = handler.Resolve(Fx.Effect("ModifierTimed", "{ 'channel': 'popularity.gain', 'multiplier': 1.25 }", 120), call);
            Assert.Equal(1.25f, effect.value);
            Assert.False(effect.clamped);
            Assert.Equal(120, effect.durationDays);
            Assert.Equal("es.fx.modifier", handler.Describe(effect).Key);
            Assert.Equal("popularity.gain,+25%,120", string.Join(",", handler.Describe(effect).Args));

            AppliedEffect tooMuch = handler.Resolve(Fx.Effect("ModifierTimed", "{ 'channel': 'popularity.gain', 'multiplier': 2.0 }", 10), call);
            Assert.Equal(1.6f, tooMuch.value, "held to [0.7; 1.6] so the text is the applied number");
            Assert.True(tooMuch.clamped);

            var scaled = new EffectCall { CompanyKey = CompanyKey.Player, Severity = 0.5f, Contributions = aggregator };
            AppliedEffect soft = handler.Resolve(Fx.Effect("ModifierTimed", "{ 'channel': 'research.speed', 'multiplier': 1.2 }", 10), scaled);
            Assert.Near(1.1, soft.value, 1e-6, "the intensity is in the call: 1 + 0.2 × 0.5");
        }

        [Test]
        public void MarketDemand_All_GoesToTheFourSegments_ForTheWholeMarket()
        {
            var sink = new RecordingSink();
            var aggregator = new ModifierAggregator(sink, () => EffectsTestKitConfig.Design);
            var handler = new MarketDemandEffect();
            var call = new EffectCall { CompanyKey = CompanyKey.Player, Severity = 1f, Contributions = aggregator };

            AppliedEffect all = handler.Resolve(Fx.Effect("MarketDemand", "{ 'segment': 'all', 'multiplier': 0.9 }", 90), call);
            Assert.Equal(EventCompanyKeys.Global, all.companyKey, "a market effect is global");
            Assert.Equal("market.demand.*", all.channel);
            all.id = "1/common/0";
            handler.Apply(all, call);
            foreach (string channel in MarketDemandEffect.SegmentChannels) Assert.Near(0.9, aggregator.Current(channel, "ai:3"), 1e-6, channel);
            Assert.Equal("all", handler.Describe(all).Args[0]);

            handler.Remove(all, call);
            Assert.Equal(1f, aggregator.Current("market.demand.highend", "ai:3"));

            AppliedEffect low = handler.Resolve(Fx.Effect("MarketDemand", "{ 'segment': 'lowend', 'multiplier': 3.0 }", 90), call);
            Assert.Equal("market.demand.lowend", low.channel);
            Assert.Equal(2.5f, low.value, "held to the family limit");
        }

        [Test]
        public void OnDisabled_RemovesNoProviderAndNoContribution()
        {
            // D10 and TSK-011: switching the feature off freezes it, the core skips its provider; nothing is removed or registered again.
            string src = SourceScan.SrcDirOrFail();
            string folder = Path.Combine(src, "Features", "EventSystem");
            var forbidden = new Regex(@"\bRemoveProviders\s*\(|ModModifiers\s*\.\s*RemoveAll\s*\(|ModModifiers\s*\.\s*Remove\s*\(");
            var found = new List<string>();
            foreach (string path in SourceScan.Files(folder, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in forbidden.Matches(code)) found.Add(SourceScan.Relative(src, path) + ": " + match.Value);
            }
            Assert.True(found.Count == 0, "the engine never removes providers or entries of ModModifiers:\n  " + string.Join("\n  ", found));

            // OnDisabled of the feature only passes the call on; the lifecycle only remembers the day (TSK-023): the core freezes the feature
            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(folder, "EventSystemFeature.cs")), true);
            Match body = Regex.Match(feature, @"public\s+void\s+OnDisabled\s*\(\s*\)\s*\{(?<body>[^}]*)\}");
            Assert.True(body.Success, "OnDisabled is there");
            string passed = Regex.Replace(body.Groups["body"].Value, @"\s+", " ").Trim();
            Assert.Equal("if (_engine == null) return; _engine.OnDisabled();", passed, "OnDisabled of the feature only passes the call on");

            string runtime = SourceScan.Strip(SourceScan.ReadText(Path.Combine(folder, "Logic", "Runtime", "EventRuntime.cs")), true);
            int from = runtime.IndexOf("public void OnDisabled()", StringComparison.Ordinal);
            int to = runtime.IndexOf("public void OnEnabled()", StringComparison.Ordinal);
            Assert.True(from > 0 && to > from, "the lifecycle has OnDisabled before OnEnabled");
            string disabled = runtime.Substring(from, to - from);
            Assert.False(Regex.IsMatch(disabled, @"(Clear|ClearAll|Remove|RemoveAll|Unregister|Invalidate|Suspend\w*|Resume\w*|Register\w*)\s*\("), "OnDisabled drops and creates nothing: " + disabled);
        }
    }

    /// <summary>The configs of the effect tests.</summary>
    internal static class EffectsTestKitConfig
    {
        /// <summary>The limits of the example in design.md.</summary>
        public static EventConfig Design
        {
            get { return Fx.DesignLimits(); }
        }
    }
}
