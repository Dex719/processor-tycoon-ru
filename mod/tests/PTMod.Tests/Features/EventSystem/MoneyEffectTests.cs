using System.Collections.Generic;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The <c>Money</c> effect (FR-5, FR-12; design.md "Деньги"): <c>round_to_1000( clamp(f × QR, min × I, max × I) × s )</c>, the bounds times inflation, the
    /// rounding, a company without three reports (QR = 0), the sign, the origins, and AC-5.1 - the sum in the text, in the journal and in <c>SpendMoney</c> is one
    /// number, spent once.
    /// </summary>
    [Covers(typeof(MoneyEffect), typeof(EffectMath), typeof(EffectFormat), typeof(StandardEffect), typeof(EffectCall), typeof(IEffectWorld), typeof(EffectText))]
    public class MoneyEffectTests
    {
        [Test]
        public void TheDesignExample_1990_Revenue20M_f0052_Gives_Minus1040000()
        {
            // 1990, I ≈ 3.0, QR $20 000 000, es.star_engineer: revenueFraction [0.04, 0.06], min 50k, max 25M; f = 0.04 + 0.6 × 0.02 = 0.052.
            Assert.Equal(1040000f, EffectMath.MoneyAmount(0.052f, 20000000f, 50000f, 25000000f, 3.0f, 1f));

            var world = new FakeEffectWorld { InflationValue = 3.0f };
            world.Revenue[CompanyKey.Player] = 20000000f;
            var money = new MoneyEffect(world);
            EventEffect spec = Fx.Effect("Money", "{ 'direction': 'Spend', 'origin': 'Research', 'revenueFraction': [0.04, 0.06], 'min': 50000, 'max': 25000000 }");
            var rng = new ScriptedRng(0.6f);

            AppliedEffect effect = money.Resolve(spec, new EffectCall { CompanyKey = CompanyKey.Player, Rng = rng });
            Assert.Equal(-1040000f, effect.value, "a spending is negative");
            Assert.Equal(1, rng.Draws, "one number for the fraction");
            Assert.Equal("es.fx.money.spend", money.Describe(effect).Key);
            Assert.Equal("$1.04M", money.Describe(effect).Args[0]);
        }

        [Test]
        public void TheBounds_AreMultipliedByInflation_AndTheSumIsRoundedTo1000()
        {
            // f × QR = 1 234 → below min × I = 30 000 × 2.5 = 75 000.
            Assert.Equal(75000f, EffectMath.MoneyAmount(0.01f, 123400f, 30000f, 1000000f, 2.5f, 1f));
            // f × QR = 9 000 000 → above max × I = 1 000 000 × 2 = 2 000 000.
            Assert.Equal(2000000f, EffectMath.MoneyAmount(0.5f, 18000000f, 0f, 1000000f, 2f, 1f));
            // 1 041 499.9 → 1 041 000; 1 041 500 → 1 042 000 (halves away from zero).
            Assert.Equal(1041000f, EffectMath.RoundTo1000(1041499.9));
            Assert.Equal(1042000f, EffectMath.RoundTo1000(1041500.0));
            // No max: no upper bound.
            Assert.Equal(9000000f, EffectMath.MoneyAmount(0.5f, 18000000f, 0f, float.PositiveInfinity, 2f, 1f));
            // A broken inflation reads as 1.
            Assert.Equal(50000f, EffectMath.MoneyAmount(0f, 0f, 50000f, 100000f, float.NaN, 1f));
        }

        [Test]
        public void WithoutThreeReports_TheRevenueIsZero_AndTheMinimumIsTaken()
        {
            var world = new FakeEffectWorld { InflationValue = 2f };   // no revenue entry: QR 0, as GameFacade answers for Reports.Count < 3
            var money = new MoneyEffect(world);
            EventEffect spec = Fx.Effect("Money", "{ 'direction': 'Spend', 'revenueFraction': [0.02, 0.03], 'min': 30000, 'max': 10000000 }");

            AppliedEffect effect = money.Resolve(spec, new EffectCall { CompanyKey = CompanyKey.Player, Rng = new ScriptedRng(0.9f) });
            Assert.Equal(-60000f, effect.value, "min × I");
        }

        [Test]
        public void InflationFalse_KeepsTheBoundsAsWritten()
        {
            var world = new FakeEffectWorld { InflationValue = 5f };
            var money = new MoneyEffect(world);
            EventEffect spec = Fx.Effect("Money", "{ 'direction': 'Earn', 'min': 40000, 'max': 40000, 'inflation': false }");

            AppliedEffect effect = money.Resolve(spec, new EffectCall { CompanyKey = CompanyKey.Player, Rng = new ScriptedRng() });
            Assert.Equal(40000f, effect.value, "an income is positive and the bounds are not inflated");
            Assert.Equal("es.fx.money.earn", money.Describe(effect).Key);
        }

        [Test]
        public void Severity_MultipliesTheAmount_BeforeRounding()
        {
            // clamp(0.05 × 1 000 000, 0, ∞) = 50 000; × 1.5 = 75 000.
            Assert.Equal(75000f, EffectMath.MoneyAmount(0.05f, 1000000f, 0f, float.PositiveInfinity, 1f, 1.5f));
            // 12 345 × 0.5 = 6 172.5 → 6 000.
            Assert.Equal(6000f, EffectMath.MoneyAmount(1f, 12345f, 0f, float.PositiveInfinity, 1f, 0.5f));
            Assert.Equal(1f, EffectMath.SanitizeSeverity(float.NaN));
            Assert.Equal(EffectMath.MaxSeverity, EffectMath.SanitizeSeverity(1000f));
        }

        [Test]
        public void Apply_SpendsOnce_WithTheOriginOfTheParams_AndEarnDefaultsToInterest()
        {
            var world = new FakeEffectWorld();
            var money = new MoneyEffect(world);

            EventEffect spend = Fx.Effect("Money", "{ 'direction': 'Spend', 'origin': 'Taxes', 'min': 20000, 'max': 20000 }");
            var call = new EffectCall { CompanyKey = CompanyKey.Player, Rng = new ScriptedRng(), Spec = spend };
            AppliedEffect spent = money.Resolve(spend, call);
            money.Apply(spent, call);

            EventEffect earn = Fx.Effect("Money", "{ 'direction': 'Earn', 'min': 30000, 'max': 30000 }");
            var call2 = new EffectCall { CompanyKey = "ai:4", Rng = new ScriptedRng(), Spec = earn };
            AppliedEffect earned = money.Resolve(earn, call2);
            money.Apply(earned, call2);

            Assert.Equal(2, world.Money.Count);
            Assert.Equal(-20000f, world.Money[0].Value);
            Assert.Equal("Taxes", world.Origins[0]);
            Assert.Equal(30000f, world.Money[1].Value);
            Assert.Equal(MoneyEffect.DefaultEarnOrigin, world.Origins[1], "Earn without origin is RevenueOrigin.Interest (FR-5 item 10)");
            Assert.Equal("Interest", MoneyEffect.DefaultEarnOrigin);

            EventEffect noOrigin = Fx.Effect("Money", "{ 'direction': 'Spend', 'min': 1000, 'max': 1000 }");
            var call3 = new EffectCall { CompanyKey = CompanyKey.Player, Rng = new ScriptedRng(), Spec = noOrigin };
            money.Apply(money.Resolve(noOrigin, call3), call3);
            Assert.Equal(MoneyEffect.DefaultSpendOrigin, world.Origins[2]);
        }

        [Test]
        public void Apply_ToACompanyThatIsGone_OrToTheWholeMarket_IsSkipped()
        {
            var world = new FakeEffectWorld();
            world.Missing.Add("ai:9");
            var money = new MoneyEffect(world);
            EventEffect spec = Fx.Effect("Money", "{ 'direction': 'Spend', 'min': 1000, 'max': 1000 }");

            AppliedEffect gone = money.Resolve(spec, new EffectCall { CompanyKey = "ai:9", Rng = new ScriptedRng() });
            money.Apply(gone, new EffectCall { CompanyKey = "ai:9", Spec = spec });
            Assert.Equal(AppliedEffectStates.Skipped, gone.state);

            AppliedEffect global = money.Resolve(spec, new EffectCall { CompanyKey = EventCompanyKeys.Global, Rng = new ScriptedRng() });
            money.Apply(global, new EffectCall { CompanyKey = EventCompanyKeys.Global, Spec = spec });
            Assert.Equal(AppliedEffectStates.Skipped, global.state);
            Assert.Equal(0, world.Money.Count);
        }

        [Test]
        public void AC_5_1_TheSumInTheText_InTheJournal_AndInSpendMoney_IsOneNumber_SpentOnce()
        {
            // Revenue fraction [0.04; 0.06] of $20 000 000: the event fires for the player, the player chooses "hire".
            EventDefinition definition = Fx.Event("es.star_engineer",
                new[]
                {
                    Fx.Choice("decline"),
                    Fx.Choice("hire", Fx.Effect("Money", "{ 'direction': 'Spend', 'origin': 'Research', 'revenueFraction': [0.04, 0.06], 'min': 50000, 'max': 25000000 }")),
                });
            var rig = new EffectsRig(new FakeCatalog(null, definition));
            rig.Game.Revenue[CompanyKey.Player] = 20000000f;
            rig.Game.InflationValue = 3f;
            rig.World.Today = 7000;

            Assert.Equal(EventFireResult.Fired, rig.Fire("es.star_engineer", CompanyKey.Player));
            EventInstanceDto instance = rig.Instance("es.star_engineer");
            AppliedEffect shown = instance.options[1].effects[0];
            Assert.Equal(AppliedEffectStates.Pending, shown.state, "nothing is spent before the choice");
            Assert.Equal(0, rig.Game.Money.Count);
            Assert.InRange(-shown.value, 800000f, 1200000f, "4..6 % of 20M");
            Assert.Equal(0f, shown.value % 1000f, "rounded to 1000");

            string text = shown.textArgs[0];
            Assert.True(rig.Runtime.ResolveChoice(instance.id, "hire", false, 7003));

            Assert.Equal(1, rig.Game.Money.Count, "SpendMoney is called once");
            Assert.Equal(shown.value, rig.Game.Money[0].Value, "the balance changes by the shown number");
            Assert.Equal("Research", rig.Game.Origins[0]);
            Assert.Equal(EffectFormat.Money(-shown.value), text, "the text is the same number");

            List<EventLogEntry> resolved = rig.Entries(EventLogKinds.Resolved);
            Assert.Equal(1, resolved.Count);
            Assert.Equal(shown.value, resolved[0].effects[0].value, "the journal keeps the same number");
            Assert.False(rig.Runtime.ResolveChoice(instance.id, "hire", false, 7004), "a second answer is ignored");
            Assert.Equal(1, rig.Game.Money.Count);
        }

        [Test]
        public void TheFormats_AreTheOnesOfTheTexts()
        {
            Assert.Equal("$1.04M", EffectFormat.Money(1041000f));
            Assert.Equal("$950", EffectFormat.Money(950f));
            Assert.Equal("$15K", EffectFormat.Money(15000f));
            Assert.Equal("$2.5B", EffectFormat.Money(-2500000000f));
            Assert.Equal("+12%", EffectFormat.Percent(1.12f));
            Assert.Equal("-40%", EffectFormat.Percent(0.6f));
            Assert.Equal("0", EffectFormat.Signed(0.0001, 1));
            Assert.Equal("-3%", EffectFormat.Share(-0.03f));
            Assert.Equal("+20%", EffectFormat.Share(0.2f));
            Assert.Equal("365", EffectFormat.Days(365));
        }
    }
}
