using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Modifiers;

namespace PTMod.Tests.Modifiers
{
    /// <summary>
    /// Balance range of market.rating.cpu and popularity.gain (FR-12 item 18, AC-12.6, TSK-059): the product is clamped to
    /// [0.5; 2.0] per company x CPU x segment before the fuse, a zero factor is lifted to the lower bound, Explain reports it.
    /// Also the declaration type (ChannelSpec) the range comes from.
    /// </summary>
    [Covers(typeof(BalanceRange), typeof(ChannelSpec))]
    public class BalanceTests
    {
        private static readonly ModifierQuery Rating = ModifierFixtures.Q("player", 10, Segment.MidRange);

        // ---- BalanceRange ----

        [Test]
        public void Range_None_IsTransparent()
        {
            BalanceRange none = BalanceRange.None;

            Assert.False(none.IsDefined);
            Assert.False(default(BalanceRange).IsDefined);
            Assert.Equal(7.5f, none.Clamp(7.5f));
            Assert.Equal(0f, none.Clamp(0f));
            Assert.False(none.IsCapped(100f));
            Assert.Equal("none", none.ToString());
        }

        [Test]
        public void Range_Clamp_And_IsCapped()
        {
            var range = new BalanceRange(0.5f, 2f);

            Assert.True(range.IsDefined);
            Assert.Equal(0.5f, range.Clamp(0.3f));
            Assert.Equal(2f, range.Clamp(3.744f));
            Assert.Equal(1.2f, range.Clamp(1.2f));
            Assert.Equal(0.5f, range.Clamp(0.5f), "the bounds themselves are inside");
            Assert.Equal(2f, range.Clamp(2f));
            Assert.True(range.IsCapped(0.3f));
            Assert.True(range.IsCapped(3.744f));
            Assert.False(range.IsCapped(1f));
            Assert.False(range.IsCapped(0.5f));
            Assert.False(range.IsCapped(2f));
            Assert.Equal("[0.5; 2]", range.ToString());
        }

        [Test]
        public void Range_FromSpec_AndEquality()
        {
            ChannelSpec spec = ModifierFixtures.Spec("market.rating.cpu", 0f, 5f, 0.5f, 2f);
            BalanceRange range = BalanceRange.FromSpec(spec);

            Assert.Equal(0.5f, range.Min);
            Assert.Equal(2f, range.Max);
            Assert.False(BalanceRange.FromSpec(ModifierFixtures.Spec("taxes", 0f, 4f)).IsDefined);
            Assert.False(BalanceRange.FromSpec(null).IsDefined);
            Assert.True(range.Equals(new BalanceRange(0.5f, 2f)));
            Assert.False(range.Equals(new BalanceRange(0.5f, 3f)));
            Assert.False(range.Equals((object)"x"));
            Assert.True(range.Equals((object)new BalanceRange(0.5f, 2f)));
            Assert.Equal(range.GetHashCode(), new BalanceRange(0.5f, 2f).GetHashCode());
            Assert.True(BalanceRange.None.Equals(default(BalanceRange)));
        }

        // ---- AC-12.6 ----

        [Test]
        public void Ac126_ProductAboveTheRange_IsCappedAtTwo()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.RegisterProvider("feature-a", "market.rating.cpu", q => 1.8f);
            registry.RegisterProvider("feature-b", "market.rating.cpu", q => 1.6f);
            registry.RegisterProvider("feature-c", "market.rating.cpu", q => 1.3f);

            Assert.Equal(2f, registry.Get("market.rating.cpu", Rating));

            ModifierBreakdown breakdown = registry.Explain("market.rating.cpu", Rating);
            Assert.Near(3.744, breakdown.Raw, 1e-4);
            Assert.Equal(2f, breakdown.Total);
            Assert.True(breakdown.IsBalanceCapped);
            Assert.False(breakdown.IsFuseCapped, "2.0 is inside the fuse [0; 5]");
            Assert.True(breakdown.HasBalanceRange);
            Assert.Equal(0.5f, breakdown.BalanceMin);
            Assert.Equal(2f, breakdown.BalanceMax);
            Assert.Equal(3, breakdown.Count);
            Assert.Equal("core.explain.capped", ModifierBreakdown.CappedKey);
        }

        [Test]
        public void Ac126_ProductBelowTheRange_IsRaisedToHalf()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "market.rating.cpu", ModifierScope.Global(), 0.6f);
            registry.Add("b", "market.rating.cpu", ModifierScope.Global(), 0.5f);

            Assert.Equal(0.5f, registry.Get("market.rating.cpu", Rating));
            ModifierBreakdown breakdown = registry.Explain("market.rating.cpu", Rating);
            Assert.Near(0.3, breakdown.Raw, 1e-6);
            Assert.Equal(0.5f, breakdown.Total);
            Assert.True(breakdown.IsBalanceCapped);
        }

        [Test]
        public void Range_ProductInside_IsNotCapped()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "market.rating.cpu", ModifierScope.Global(), 1.25f);
            registry.Add("b", "market.rating.cpu", ModifierScope.Global(), 1.2f);

            ModifierBreakdown breakdown = registry.Explain("market.rating.cpu", Rating);
            Assert.Near(1.5, breakdown.Total, 1e-6);
            Assert.Near(breakdown.Raw, breakdown.Total, 1e-9);
            Assert.False(breakdown.IsBalanceCapped);
            Assert.False(breakdown.IsFuseCapped);
        }

        [Test]
        public void Range_BoundariesAreInside()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "market.rating.cpu", ModifierScope.Cpu(1), 2f);
            registry.Add("b", "market.rating.cpu", ModifierScope.Cpu(2), 0.5f);

            Assert.False(registry.Explain("market.rating.cpu", ModifierFixtures.Q("player", 1)).IsBalanceCapped);
            Assert.False(registry.Explain("market.rating.cpu", ModifierFixtures.Q("player", 2)).IsBalanceCapped);
        }

        [Test]
        public void Range_PopularityGain_HasTheSameRange()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "popularity.gain", ModifierScope.Global(), 3f);
            Assert.Equal(2f, registry.Get("popularity.gain", Rating));

            registry.ClearEntries();
            registry.Add("a", "popularity.gain", ModifierScope.Global(), 0.1f);
            Assert.Equal(0.5f, registry.Get("popularity.gain", Rating));
        }

        [Test]
        public void Range_AppliesPerCompanyCpuAndSegment()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            // Cpu 1 collects 1.8 x 1.6 x 1.3; Cpu 2 only 1.2. The range is applied to each product separately.
            foreach (float m in new[] { 1.8f, 1.6f, 1.3f }) registry.Add("f", "market.rating.cpu", ModifierScope.Cpu(1), m);
            registry.Add("f", "market.rating.cpu", ModifierScope.Cpu(2), 1.2f);
            registry.Add("g", "market.rating.cpu", ModifierScope.Player(Segment.LowEnd), 1.9f);
            registry.Add("g", "market.rating.cpu", ModifierScope.Player(Segment.LowEnd), 1.9f);

            Assert.Equal(2f, registry.Get("market.rating.cpu", ModifierFixtures.Q("player", 1, Segment.MidRange)));
            Assert.Near(1.2, registry.Get("market.rating.cpu", ModifierFixtures.Q("player", 2, Segment.MidRange)), 1e-6);
            Assert.Equal(2f, registry.Get("market.rating.cpu", ModifierFixtures.Q("player", 3, Segment.LowEnd)), "1.9 x 1.9 = 3.61 is capped");
            Assert.Equal(1f, registry.Get("market.rating.cpu", ModifierFixtures.Q("player", 3, Segment.HighEnd)));
            Assert.Equal(1f, registry.Get("market.rating.cpu", ModifierFixtures.Q("ai:3", 3, Segment.LowEnd)));
        }

        [Test]
        public void Range_ComesBeforeTheFuse()
        {
            var registry = new ChannelRegistry();
            registry.Declare(ModifierFixtures.Spec("test.both", 0.8f, 1.5f, 0.5f, 2f));
            registry.Add("a", "test.both", ModifierScope.Global(), 3f);

            ModifierBreakdown breakdown = registry.Explain("test.both", ModifierFixtures.Q(null));
            Assert.Near(3.0, breakdown.Raw, 1e-6);
            Assert.True(breakdown.IsBalanceCapped);
            Assert.True(breakdown.IsFuseCapped, "2.0 from the range, then 1.5 from the fuse");
            Assert.Equal(1.5f, breakdown.Total);
            Assert.Equal(1.5f, registry.Get("test.both", ModifierFixtures.Q(null)));

            // A product inside the range but below the fuse: only the fuse acts.
            registry.ClearEntries();
            registry.Add("a", "test.both", ModifierScope.Global(), 0.6f);
            breakdown = registry.Explain("test.both", ModifierFixtures.Q(null));
            Assert.False(breakdown.IsBalanceCapped);
            Assert.True(breakdown.IsFuseCapped);
            Assert.Equal(0.8f, breakdown.Total);
        }

        // ---- the zero factor ----

        [Test]
        public void Zero_Provider_ProductIsRaisedToTheLowerBound()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.RegisterProvider("p", "market.rating.cpu", q => 0f);

            Assert.Equal(0.5f, registry.Get("market.rating.cpu", Rating), "a CPU cannot be zeroed out");
            ModifierBreakdown breakdown = registry.Explain("market.rating.cpu", Rating);
            Assert.Equal(1, breakdown.Count);
            Assert.Equal(0f, breakdown[0].Multiplier, "the list shows the factor as the feature gave it");
            Assert.Near(0.0, breakdown.Raw, 1e-9);
            Assert.True(breakdown.IsBalanceCapped, "the product 0 is below the range");
            Assert.Equal(0.5f, breakdown.Total);
        }

        [Test]
        public void Zero_Entry_ZeroesTheProductBeforeTheRange()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "market.rating.cpu", ModifierScope.Global(), 0f);
            registry.Add("b", "market.rating.cpu", ModifierScope.Global(), 1.8f);

            // FR-12 item 2: the range limits the product (0 x 1.8 = 0), it does not rewrite the single factors.
            Assert.Equal(0.5f, registry.Get("market.rating.cpu", Rating));
            Assert.True(registry.Explain("market.rating.cpu", Rating).IsBalanceCapped);
        }

        [Test]
        public void Zero_WithoutARange_StaysZero()
        {
            ChannelRegistry registry = ModifierFixtures.Registry();
            registry.Add("a", "production.output", ModifierScope.Global(), 0f);
            registry.Add("b", "production.output", ModifierScope.Global(), 2f);

            Assert.Equal(0f, registry.Get("production.output", ModifierFixtures.Q("player")));
            ModifierBreakdown breakdown = registry.Explain("production.output", ModifierFixtures.Q("player"));
            Assert.False(breakdown.HasBalanceRange);
            Assert.Equal(0f, breakdown.BalanceMin);
            Assert.Equal(0f, breakdown.BalanceMax);
        }

        // ---- Explain agrees with Get ----

        [Test]
        public void Explain_Total_AlwaysEqualsGet()
        {
            var random = new Random(20261010);
            for (int round = 0; round < 300; round++)
            {
                ChannelRegistry registry = ModifierFixtures.Registry();
                int count = random.Next(0, 6);
                for (int i = 0; i < count; i++)
                {
                    float m = (float)(random.NextDouble() * 3.2);
                    if (random.Next(10) == 0) m = 0f;
                    if (random.Next(2) == 0) registry.Add("f" + i, "market.rating.cpu", ModifierScope.AllCompanies(), m);
                    else registry.RegisterProvider("f" + i, "market.rating.cpu", q => m);
                }

                ModifierQuery query = ModifierFixtures.Q("player", 5, Segment.LowEnd);
                ModifierBreakdown breakdown = registry.Explain("market.rating.cpu", query);
                float got = registry.Get("market.rating.cpu", query);

                Assert.Equal(got, breakdown.Total, "round " + round);
                Assert.InRange(got, 0.5f, 2f, "round " + round + " (or 1 when nothing is registered)");
                Assert.Equal(breakdown.Raw < 0.5f || breakdown.Raw > 2f, breakdown.IsBalanceCapped, "round " + round);
            }
        }

        // ---- ChannelSpec ----

        [Test]
        public void Spec_CoreTable_AllValid()
        {
            List<ChannelSpec> table = ModifierFixtures.CoreChannels();
            Assert.Equal(27, table.Count, "FR-12 item 15: 27 entries");
            Assert.Equal(27, table.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count());

            foreach (ChannelSpec spec in table)
            {
                string error;
                Assert.True(spec.Validate(out error), spec.Name + ": " + error);
                Assert.Null(error);
            }
            Assert.Equal(2, table.Count(s => s.HasBalanceRange));
            Assert.Equal(1, table.Count(s => !s.Cacheable));
            Assert.Equal(1, table.Count(s => s.IsFamily));
        }

        [Test]
        public void Spec_Validate_Rejects()
        {
            string error;

            Assert.False(ModifierFixtures.Spec("", 0.5f, 2f).Validate(out error));
            Assert.True(error.Contains("name"));
            Assert.False(ModifierFixtures.Spec(null, 0.5f, 2f).Validate(out error));
            Assert.False(ModifierFixtures.Spec("a b", 0.5f, 2f).Validate(out error));
            Assert.False(ModifierFixtures.Spec("a..b", 0.5f, 2f).Validate(out error));
            Assert.False(ModifierFixtures.Spec("a*", 0.5f, 2f).Validate(out error));
            Assert.False(ModifierFixtures.Spec("*.a", 0.5f, 2f).Validate(out error));

            Assert.False(ModifierFixtures.Spec("x.y", 3f, 2f).Validate(out error), "Min above Max");
            Assert.True(error.Contains("x.y"));
            Assert.False(ModifierFixtures.Spec("x.y", -1f, 2f).Validate(out error), "negative Min");
            Assert.False(ModifierFixtures.Spec("x.y", 0f, 0f).Validate(out error), "Max must be positive");
            Assert.False(ModifierFixtures.Spec("x.y", float.NaN, 2f).Validate(out error));
            Assert.False(ModifierFixtures.Spec("x.y", 0.5f, float.PositiveInfinity).Validate(out error));

            Assert.False(ModifierFixtures.Spec("x.y", 0f, 5f, 2f, 0.5f).Validate(out error), "BalanceMin above BalanceMax");
            Assert.True(error.Contains("balance"));
            Assert.False(ModifierFixtures.Spec("x.y", 0f, 5f, 0.5f, 0f).Validate(out error), "BalanceMax 0 with BalanceMin set");
            Assert.False(ModifierFixtures.Spec("x.y", 0f, 5f, -0.5f, 2f).Validate(out error));
            Assert.False(ModifierFixtures.Spec("x.y", 0f, 5f, float.NaN, 2f).Validate(out error));

            Assert.True(ModifierFixtures.Spec("x.y", 1f, 1f).Validate(out error), "a frozen channel is allowed");
            Assert.True(ModifierFixtures.Spec("x.*", 0.1f, 3f).Validate(out error), "a family");
            Assert.True(ModifierFixtures.Spec("taxes", 0f, 4f).Validate(out error), "one word");
            Assert.True(ModifierFixtures.Spec("x.y", 0f, 5f, 0f, 2f).Validate(out error), "only an upper balance bound");
        }

        [Test]
        public void Spec_CloneSameBoundsAndText()
        {
            ChannelSpec spec = ModifierFixtures.Spec("market.rating.cpu", 0f, 5f, 0.5f, 2f, owner: "mod-framework");
            spec.DescriptionKey = "core.channel.rating";
            spec.PerCpu = true;

            ChannelSpec copy = spec.Clone();
            Assert.False(ReferenceEquals(spec, copy));
            Assert.Equal("market.rating.cpu", copy.Name);
            Assert.Equal("mod-framework", copy.OwnerFeature);
            Assert.Equal("core.channel.rating", copy.DescriptionKey);
            Assert.True(copy.PerCpu);
            Assert.True(copy.PerCompany);
            Assert.True(copy.Cacheable);

            copy.Max = 9f;
            Assert.Equal(5f, spec.Max, "a clone is independent");

            Assert.True(spec.SameBounds(spec.Clone()));
            Assert.False(spec.SameBounds(copy));
            Assert.False(spec.SameBounds(null));
            Assert.False(spec.SameBounds(ModifierFixtures.Spec("market.rating.cpu", 0f, 5f, 0.5f, 2f, cacheable: false)));
            Assert.False(spec.SameBounds(ModifierFixtures.Spec("market.rating.cpu", 0f, 5f, 0.4f, 2f)));

            Assert.Equal("market.rating.cpu [0; 5] balance [0.5; 2]", spec.ToString());
            Assert.Equal("taxes [0; 4]", ModifierFixtures.Spec("taxes", 0f, 4f).ToString());
            Assert.True(ModifierFixtures.Spec("gpu.demand.*", 0.1f, 3f).IsFamily);
            Assert.False(ModifierFixtures.Spec("gpu.demand", 0.1f, 3f).IsFamily);
            Assert.False(new ChannelSpec().IsFamily);
            Assert.True(new ChannelSpec().Cacheable, "caching is on by default");
        }
    }
}
