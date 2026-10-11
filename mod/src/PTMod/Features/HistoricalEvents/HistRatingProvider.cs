using PTMod.Core;
using GameCpu = ProcessorTycoon.Hardware.Cpu;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The one provider of the feature on the channel <c>market.rating.cpu</c> (design.md, "Каналы ModModifiers"; FR-7 item 3, FR-11 item 1), registered once in
    /// <c>Register</c> with <c>ModModifiers.RegisterReasonedProvider</c>. It multiplies the export rules (<see cref="ExportCore.Contribution"/>, in [0.80, 1.00]) and the
    /// races (<see cref="RaceCore.Contribution"/>, in [1.00, 1.10]) and gives the reason of the one that moves the rating more (<see cref="HistRatingMath.Combine"/>). It
    /// answers from the CPU and the segment of the question in O(rules + races): a comparison with the threshold and a set lookup, no allocation unless something applies.
    /// <list type="bullet">
    /// <item>It answers 1 while the feature is not active, for a question without a CPU and for a category the player switched off (<c>export</c>, <c>race</c>).</item>
    /// <item>The state it reads is the save section's, so it works from the restored rules and races after a load. Whoever changes the state invalidates the channel.</item>
    /// <item>An exception is the core's business: it counts 1 and a fault against the feature (<c>ModModifiers</c>).</item>
    /// </list>
    /// </summary>
    internal sealed class HistRatingProvider
    {
        private readonly ExportCore _export;
        private readonly RaceCore _race;

        internal HistRatingProvider(ExportCore export, RaceCore race)
        {
            _export = export;
            _race = race;
        }

        public ProviderResult Provide(ModifierContext ctx)
        {
            GameCpu cpu = ctx.Cpu;
            if (ReferenceEquals(cpu, null) || !ModFeatures.IsActive(HistIds.Feature)) return new ProviderResult(1f);

            Segment segment = ctx.Segment;

            float exportMultiplier;
            string threshold;
            string[] exportArgs = null;
            if (_export.Contribution(cpu.Ips, segment, ctx.CompanyKey, out exportMultiplier, out threshold) && HistSettings.IsCategoryEnabled(HistCategories.Export))
            {
                exportArgs = new[] { threshold };
            }
            else
            {
                exportMultiplier = 1f;
            }

            float raceMultiplier;
            string raceReason;
            int topN;
            string[] raceArgs = null;
            if (_race.Contribution(cpu.SaveID, segment, out raceMultiplier, out raceReason, out topN) && HistSettings.IsCategoryEnabled(HistCategories.Race))
            {
                raceArgs = new[] { topN.ToString() };
            }
            else
            {
                raceMultiplier = 1f;
            }

            return HistRatingMath.Combine(exportMultiplier, ExportKeys.ReasonExport, exportArgs, raceMultiplier, raceReason, raceArgs);
        }
    }
}
