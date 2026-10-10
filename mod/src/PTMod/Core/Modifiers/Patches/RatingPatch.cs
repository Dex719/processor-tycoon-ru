using System;
using System.Collections.Generic;
using System.Globalization;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameCpuRatings = ProcessorTycoon.Hardware.CpuRatings;
using GameMarket = ProcessorTycoon.MarketSystem.Market;

namespace PTMod.Core
{
    /// <summary>
    /// P4: <c>Market.CalculateRatings(population, populationIndex, ignoreIfOutOfStock)</c>, postfix (FR-12, the channel <c>market.rating.cpu</c>).
    /// The game writes <c>Cpu.Ratings.Overall</c> of every CPU on the market from scratch, once per segment and pass (three passes of four segments a
    /// day), and only then runs <c>NormalizeRatings</c> (obsolescence), <c>DiluteRatings</c> (<c>Overall²/T</c> per company),
    /// <c>UpdateRatingSum</c> and <c>CalculatePotentialBuyers</c>. The postfix multiplies <c>Overall</c> by
    /// <c>M(market.rating.cpu, company, cpu, populationIndex)</c> in between, so the factor takes part in the normalisation, the dilution and the
    /// share of the buyers exactly as a better product would (a factor common to all CPUs of a company keeps the dilution linear:
    /// <c>(m·O)² / (m·T) = m·O²/T</c>).
    /// <list type="bullet">
    /// <item><b>Signature.</b> The population parameter of the game is a private nested type (<c>Market.Population</c>), which a patch cannot name;
    /// Harmony binds by name, so the postfix takes only <c>populationIndex</c> and the market's own list of CPUs (<c>___cpus</c>, the field the
    /// game's loop reads). The index is the segment (<see cref="SegmentMap.FromPopulationIndex"/>).</item>
    /// <item><b>Zero stays zero.</b> The game zeroes the rating of a CPU that is out of stock, priced at three budgets or more, free, or without
    /// popularity in the segment (and Industries for a consumer part and back, under the PGA rules); such a CPU is skipped, so a multiplier cannot
    /// bring it back.</item>
    /// <item><b>The range is the channel's.</b> <c>Get</c> returns the product already held to the balance range [0.5; 2.0] per company × CPU ×
    /// segment (AC-12.6) and to the fuse; the postfix applies the number as it is and does not limit it a second time.</item>
    /// <item><b>Fast path.</b> With nothing registered on the channel the postfix returns after a null check, a range check and one dictionary read.
    /// With records only the CPUs that have a rating are asked, each from the day's cache of the registry; no allocation, no LINQ.</item>
    /// <item><b>Failure</b> is the core's unit <c>P4</c>: the exception is counted, the CPUs after the failing one keep the game's rating for this
    /// pass, and after <c>hooks.faultThreshold</c> failures the patch stops answering until the game restarts.</item>
    /// </list>
    /// To check in the game (the owner, with <c>framework-sample</c> or temporary entries, TSK-053): an entry <c>market.rating.cpu ×1.2</c> for the
    /// player's CPU raises its share of the buyers of every segment where it sells (the more so the fewer competing CPUs; with two entries ×1.2 and
    /// ×0.5 the rating is ×0.6, AC-12.2), and ten entries ×1.3 give at most ×2.0 (M18 of the test plan); with no entry the sales are the game's;
    /// with verbose log on, <c>Player.log</c> has <c>market: market.rating.cpu x… changes the rating of a CPU … (P4)</c> once and no <c>P4/postfix</c> faults.
    /// </summary>
    internal static class RatingPatch
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string Site = "P4/postfix";

        private static bool _announced;   // the first change that was made is written to the verbose log, once per start

        [ModPatch(typeof(GameMarket), "CalculateRatings", PatchKind.Postfix)]
        private static void Postfix(int populationIndex, List<GameCpu> ___cpus)
        {
            if (___cpus == null || ___cpus.Count == 0) return;
            if ((uint)populationIndex >= (uint)SegmentMap.PopulationCount) return;   // not one of the four populations: no segment to ask about
            if (!ModModifiers.HasAny(ModChannels.MarketRatingCpu)) return;
            if (!ModHooks.CanRun(CoreId, Site)) return;

            try
            {
                Segment segment = SegmentMap.FromPopulationIndex(populationIndex);
                List<GameCpu> cpus = ___cpus;
                for (int i = 0; i < cpus.Count; i++)
                {
                    GameCpu cpu = cpus[i];
                    if (ReferenceEquals(cpu, null)) continue;

                    GameCpuRatings ratings = cpu.Ratings;
                    if (!(ratings.Overall > 0f)) continue;   // zero (or NaN) stays as the game left it

                    float multiplier = ModModifiers.Evaluate(ModChannels.MarketRatingCpu,
                        new ModifierContext { Company = cpu.Company, Cpu = cpu, Segment = segment });
                    float scaled = Scale(ratings.Overall, multiplier);
                    if (scaled == ratings.Overall) continue;

                    Announce(multiplier, ratings.Overall, scaled, segment);
                    ratings.Overall = scaled;
                    cpu.Ratings = ratings;
                }
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, Site, ex);
            }
        }

        /// <summary>
        /// <c>overall × multiplier</c>; the rating is returned unchanged for a multiplier that is 1 or not a usable number (NaN, infinity,
        /// negative) and for a rating that is not positive.
        /// </summary>
        internal static float Scale(float overall, float multiplier)
        {
            if (!(overall > 0f) || multiplier == 1f || float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f) return overall;

            return overall * multiplier;
        }

        private static void Announce(float multiplier, float from, float to, Segment segment)
        {
            if (_announced) return;
            _announced = true;

            ModLog.Debug(CoreId, "market: " + ModChannels.MarketRatingCpu + " x" + multiplier.ToString("0.###", CultureInfo.InvariantCulture)
                                 + " changes the rating of a CPU in " + segment + " from " + from.ToString("0.#####", CultureInfo.InvariantCulture)
                                 + " to " + to.ToString("0.#####", CultureInfo.InvariantCulture) + " (P4)");
        }
    }
}
