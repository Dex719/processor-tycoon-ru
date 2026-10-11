using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The one-off standard effects besides money (FR-5, FR-12; design.md "Популярность", "Прогресс исследования"): popularity held to [0.05; 1.0], research
    /// progress capped at 0.99 and <c>Skipped</c> without a research, reputation through <c>ModReputation.Add</c> with a reason key of the event, the intensity of a
    /// scalable delta.
    /// </summary>
    [Covers(typeof(PopularityEffect), typeof(ReputationEffect), typeof(ResearchProgressEffect))]
    public class StandardEffectTests
    {
        [Test]
        public void Popularity_IsHeldTo_0_05_And_1_0_WithoutMovingAgainstTheDelta()
        {
            Assert.Near(0.47, EffectMath.Popularity(0.5f, -0.03f), 1e-6);
            Assert.Equal(EffectMath.PopularityMin, EffectMath.Popularity(0.06f, -0.03f), "the floor of the game for a CPU that is not old");
            Assert.Equal(EffectMath.PopularityMax, EffectMath.Popularity(0.99f, 0.05f));
            Assert.Equal(0.02f, EffectMath.Popularity(0.02f, -0.03f), "an old CPU below the floor is not raised by a fall");
            Assert.Equal(0.05f, EffectMath.Popularity(0f, 0.01f), "a rise lifts it to the floor");
            Assert.Equal(0.3f, EffectMath.Popularity(0.3f, float.NaN));
        }

        [Test]
        public void ResearchProgress_IsCappedAt_0_99_AndNeverLowered()
        {
            Assert.Near(0.6, EffectMath.ResearchProgress(0.4f, 0.2f), 1e-6);
            Assert.Equal(EffectMath.ResearchCap, EffectMath.ResearchProgress(0.9f, 0.2f), "completion stays with ResearchSector.Update");
            Assert.Equal(0.995f, EffectMath.ResearchProgress(0.995f, 0.2f), "a progress above the cap is not lowered");
        }

        [Test]
        public void ThePopularityEffect_DrawsItsDeltaOnce_AndIsSkippedWithoutCpusOnTheMarket()
        {
            var world = new FakeEffectWorld();
            world.CpusOnMarket["ai:3"] = 0;
            var effect = new PopularityEffect(world);
            EventEffect spec = Fx.Effect("Popularity", "{ 'delta': [-0.04, -0.02], 'cpuSelector': 'Newest', 'segment': 'lowend' }");
            var rng = new ScriptedRng(0.5f);

            AppliedEffect resolved = effect.Resolve(spec, new EffectCall { CompanyKey = "ai:1", Rng = rng });
            Assert.Near(-0.03, resolved.value, 1e-6);
            Assert.Equal(1, rng.Draws);
            Assert.Equal("-3%", effect.Describe(resolved).Args[0]);

            effect.Apply(resolved, new EffectCall { CompanyKey = "ai:1", Spec = spec });
            Assert.Null(resolved.state, "applied (the engine sets Done)");
            Assert.True(world.Calls.Exists(c => c.StartsWith("popularity ai:1 ") && c.EndsWith(" Newest lowend")), string.Join("; ", world.Calls));

            AppliedEffect none = effect.Resolve(spec, new EffectCall { CompanyKey = "ai:3", Rng = new ScriptedRng(0.5f) });
            effect.Apply(none, new EffectCall { CompanyKey = "ai:3", Spec = spec });
            Assert.Equal(AppliedEffectStates.Skipped, none.state, "no CPU on the market");

            AppliedEffect scaled = effect.Resolve(spec, new EffectCall { CompanyKey = "ai:1", Rng = new ScriptedRng(0.5f), Severity = 2f });
            Assert.Near(-0.06, scaled.value, 1e-6, "a scalable delta times the intensity (the engine passes 1 when it is not scalable)");
        }

        [Test]
        public void TheResearchEffect_IsSkippedWithoutAResearch_AndForTheWholeMarket()
        {
            var world = new FakeEffectWorld();
            world.Research[CompanyKey.Player] = 0.9f;
            var effect = new ResearchProgressEffect(world);
            EventEffect spec = Fx.Effect("ResearchProgress", "{ 'delta': 0.2 }");

            AppliedEffect player = effect.Resolve(spec, new EffectCall { CompanyKey = CompanyKey.Player, Rng = new ScriptedRng() });
            Assert.Near(0.2, player.value, 1e-6);
            effect.Apply(player, new EffectCall { CompanyKey = CompanyKey.Player });
            Assert.Equal(EffectMath.ResearchCap, world.Research[CompanyKey.Player]);
            Assert.Equal("+20%", effect.Describe(player).Args[0]);

            AppliedEffect idle = effect.Resolve(spec, new EffectCall { CompanyKey = "ai:1", Rng = new ScriptedRng() });
            effect.Apply(idle, new EffectCall { CompanyKey = "ai:1" });
            Assert.Equal(AppliedEffectStates.Skipped, idle.state, "the company researches nothing");

            AppliedEffect global = effect.Resolve(spec, new EffectCall { CompanyKey = EventCompanyKeys.Global, Rng = new ScriptedRng() });
            effect.Apply(global, new EffectCall { CompanyKey = EventCompanyKeys.Global });
            Assert.Equal(AppliedEffectStates.Skipped, global.state);
        }

        [Test]
        public void TheReputationEffect_GivesTheTitleKeyOfTheEventAsTheReason()
        {
            var world = new FakeEffectWorld();
            var effect = new ReputationEffect(world);
            EventEffect spec = Fx.Effect("Reputation", "{ 'delta': -6 }");
            EventDefinition scandal = Fx.Event("es.rival_scandal", null, spec);

            AppliedEffect resolved = effect.Resolve(spec, new EffectCall { CompanyKey = "ai:2", Rng = new ScriptedRng() });
            effect.Apply(resolved, new EffectCall { CompanyKey = "ai:2", Definition = scandal });
            Assert.Equal(-6f, world.Reputation["ai:2"]);
            Assert.Equal("es.rival_scandal.title", world.ReputationReasons[0], "the core's rule: a reason is an L10n key");
            Assert.Equal("-6", effect.Describe(resolved).Args[0]);

            var inline = new EventDefinition { id = "es.inline", title = new LocText(null, "Скандал", "Scandal") };
            Assert.Equal(ReputationEffect.FallbackReason, ReputationEffect.ReasonOf(inline), "an inline title has no key");
            Assert.Equal("es.reason.event", ReputationEffect.ReasonOf(null), "a Direct effect has no event");

            world.Missing.Add("ai:9");
            AppliedEffect gone = effect.Resolve(spec, new EffectCall { CompanyKey = "ai:9", Rng = new ScriptedRng() });
            effect.Apply(gone, new EffectCall { CompanyKey = "ai:9" });
            Assert.Equal(AppliedEffectStates.Skipped, gone.state);
        }

        [Test]
        public void TheDrawOfARange_TakesOneNumber_AndTheDaysAreWhole()
        {
            var rng = new ScriptedRng(0f, 0.999f, 0.5f);
            Assert.Equal(60, EffectMath.DrawDays(new FloatRange(60f, 120f), rng));
            Assert.Equal(120, EffectMath.DrawDays(new FloatRange(60f, 120f), rng));
            Assert.Equal(7, EffectMath.DrawDays(new FloatRange(7f, 7f), rng), "a single day still takes its number");
            Assert.Equal(3, rng.Draws);
            Assert.Near(0.05, EffectMath.Draw(new FloatRange(0.04f, 0.06f), new ScriptedRng(0.5f)), 1e-6);
            Assert.Near(0.05, EffectMath.Draw(new FloatRange(0.06f, 0.04f), new ScriptedRng(0.5f)), 1e-6, "a reversed pair is read in order");

            bool clamped;
            Assert.Equal(0.4f, EffectMath.ScaleMultiplier(0.55f, 2f, 0.4f, 1.5f, out clamped));
            Assert.True(clamped);
            Assert.Near(1.1, EffectMath.ScaleMultiplier(1.2f, 0.5f, 0.25f, 4f, out clamped), 1e-6);
            Assert.False(clamped);
            Assert.Equal(1f, EffectMath.SeverityOf(Fx.Effect("Money", "{ 'scalable': true }"), null), "no pack");
            Assert.Equal(2f, EffectMath.SeverityOf(Fx.Effect("Money", "{ 'scalable': true }"), new PackOptions { Severity = 2f }));
            Assert.Equal(1f, EffectMath.SeverityOf(Fx.Effect("Money", "{}"), new PackOptions { Severity = 2f }), "not scalable");
            Assert.True(new EffectCall { CompanyKey = EventCompanyKeys.Global }.IsGlobal);
        }
    }
}
