using System.IO;
using System.Text;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Modifiers;
using PTMod.Features.CpuArchitectures;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The contribution of the feature in the channel of the core (FR-6 item 8, AC-6.3, NFR-4; TSK-006): the architecture's 1.30 (1.20 at the knob's 150 %)
    /// multiplied with the contributions of other features is held by the balance range of <c>market.rating.cpu</c> declared in <c>core.json</c>
    /// ([0.5; 2.0]); <c>Explain</c> marks it capped and lists the architecture with its reason. The channel engine is the core's own <c>ChannelRegistry</c>.
    /// </summary>
    [Covers(typeof(ArchRatingMath))]
    public class RatingStackingTests
    {
        private const string Channel = "market.rating.cpu";

        /// <summary>The declaration of <c>market.rating.cpu</c> as the built-in <c>core.json</c> has it.</summary>
        private static ChannelSpec CoreDeclaration()
        {
            Assert.NotNull(TestEnv.SrcDir, "source folder not found; run the tests through python mod/build_feature.py cpu-architectures --tests");
            string path = Path.Combine(TestEnv.SrcDir, "Content", "mod-framework", "core.json");
            JsonObject root = ArchTestKit.ParseObject(File.ReadAllText(path, new UTF8Encoding(false)));
            foreach (JsonNode node in root.GetArray("channels"))
            {
                var entry = (JsonObject)node;
                if (entry.GetString("name") != Channel) continue;
                return new ChannelSpec
                {
                    Name = Channel,
                    Min = (float)entry.GetDouble("min"),
                    Max = (float)entry.GetDouble("max"),
                    BalanceMin = (float)entry.GetDouble("balanceMin"),
                    BalanceMax = (float)entry.GetDouble("balanceMax"),
                    PerCompany = true,
                    PerCpu = true,
                    PerSegment = true,
                };
            }
            Assert.Fail("core.json does not declare " + Channel);
            return null;
        }

        /// <summary>The registry with the core's declaration, the architecture's provider at 150 % of a 1.20 multiplier, and two other features.</summary>
        private static ChannelRegistry Registry(float otherA, float otherB)
        {
            var registry = new ChannelRegistry();
            Assert.True(registry.Declare(CoreDeclaration()));

            float architecture = ArchRatingMath.ApplyStrength(1.20f, ArchRatingMath.MaxStrength);
            registry.RegisterReasonedProvider(ArchTestKit.FeatureId, Channel, q => new ProviderResult(architecture, "arch.reason.rating", new[] { "x86" }));
            registry.RegisterProvider("platform-generations", Channel, q => otherA);
            registry.RegisterProvider("historical-events", Channel, q => otherB);
            return registry;
        }

        private static ModifierQuery Query()
        {
            return new ModifierQuery("player", 1043, Segment.HighEnd);
        }

        [Test]
        public void CoreDeclaration_HasTheBalanceRange()
        {
            ChannelSpec spec = CoreDeclaration();
            Assert.Near(0.5, spec.BalanceMin, 1e-6);
            Assert.Near(2.0, spec.BalanceMax, 1e-6);
            Assert.True(spec.Max >= 2f, "the fuse is wider than the balance range");
        }

        [Test]
        public void Contribution130_WithOthers_IsCappedAt2()
        {
            ChannelRegistry registry = Registry(1.20f, 1.40f);

            // AC-6.3: 1.30 × 1.20 × 1.40 = 2.184 is held to 2.0.
            Assert.Near(2.0, registry.Get(Channel, Query()), 1e-6);
            ModifierBreakdown breakdown = registry.Explain(Channel, Query());
            Assert.Near(2.184, breakdown.Raw, 1e-3);
            Assert.True(breakdown.IsBalanceCapped, "Explain shows \"(capped)\"");
            Assert.Near(2.0, breakdown.Total, 1e-6);

            bool found = false;
            for (int i = 0; i < breakdown.Count; i++)
            {
                AppliedModifier applied = breakdown[i];
                if (applied.FeatureId != ArchTestKit.FeatureId) continue;
                found = true;
                Assert.Near(1.30, applied.Multiplier, 1e-6, "the architecture's own factor is listed unchanged");
                Assert.Equal("arch.reason.rating", applied.ReasonKey);
                Assert.Equal("x86", applied.ReasonArgs[0]);
            }
            Assert.True(found, "the architecture is in the breakdown");
        }

        [Test]
        public void Contribution130_UnderTheRange_IsNotCapped()
        {
            ChannelRegistry registry = Registry(1.20f, 1.25f);

            // 1.30 × 1.20 × 1.25 = 1.95: inside the range.
            Assert.Near(1.95, registry.Get(Channel, Query()), 1e-5);
            Assert.False(registry.Explain(Channel, Query()).IsBalanceCapped);
        }

        [Test]
        public void LowestContribution_WithOthers_IsRaisedTo05()
        {
            var registry = new ChannelRegistry();
            Assert.True(registry.Declare(CoreDeclaration()));
            float architecture = ArchRatingMath.ApplyStrength(0.80f, ArchRatingMath.MaxStrength);
            Assert.Near(0.70, architecture, 1e-6);
            registry.RegisterReasonedProvider(ArchTestKit.FeatureId, Channel, q => new ProviderResult(architecture, "arch.reason.rating", new[] { "proprietary" }));
            registry.RegisterProvider("historical-events", Channel, q => 0.6f);

            // 0.70 × 0.6 = 0.42 is held to 0.5.
            Assert.Near(0.5, registry.Get(Channel, Query()), 1e-6);
            Assert.True(registry.Explain(Channel, Query()).IsBalanceCapped);
        }
    }
}
