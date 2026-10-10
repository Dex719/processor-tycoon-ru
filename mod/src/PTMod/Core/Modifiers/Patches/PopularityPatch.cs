using System;
using System.Collections.Generic;
using System.Globalization;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameMarket = ProcessorTycoon.MarketSystem.Market;

namespace PTMod.Core
{
    /// <summary>
    /// P5: <c>Market.UpdatePopularities()</c> (private), prefix + postfix (FR-12, the channel <c>popularity.gain</c>). Once a day, after the sales, the
    /// game gives every CPU on the market <c>Popularities[k] += b + g × SoldByK / Σ_k</c> for each segment k, then takes <c>0.0005</c> a day from the
    /// popularity of old CPUs and clamps every CPU to [0.05, 1] ([0, 1] for the old ones). The channel multiplies the gain and only the gain:
    /// <list type="bullet">
    /// <item><b>Prefix</b> copies <c>Popularities[0..3]</c> of every CPU on the market into one reusable buffer (<c>float[4 × N]</c>, grown only when
    /// the market gets more CPUs than ever before - no allocation on a normal day). Nothing is copied when the channel has no records.</item>
    /// <item><b>Postfix</b> compares: <c>Δ = after − before</c>. For <c>Δ &gt; 0</c> the popularity becomes <c>before + Δ × M(popularity.gain, company, cpu,
    /// k)</c>, clamped again to the game's own bounds - 0.05 below (0 for an old CPU, <c>Cpu.IsOld(-1, -1)</c> as the game asks), 1 above. A <c>Δ</c> that is
    /// zero or negative is the decay (or a CPU at the cap) and is left as the game made it: the decay is never multiplied.</item>
    /// <item><b>The range is the channel's.</b> <c>Get</c> returns the product held to the balance range [0.5; 2.0] per company × CPU × segment
    /// (AC-12.6) and to the fuse; the postfix applies the number as it is. A hype of ×2 doubles the day's gain, never more.</item>
    /// <item><b>Which CPUs.</b> The market's list of CPUs (<c>___cpus</c>), the one the game's gain loop walks; the CPUs that are not on the market only
    /// decay, so there is nothing to multiply. If the list changed between the prefix and the postfix (it cannot inside the game's method, but the
    /// patch does not rely on it) the postfix leaves the day alone.</item>
    /// <item><b>Failure</b> is the core's unit <c>P5</c>, shared by the prefix and the postfix: the exception is counted, the snapshot is dropped (a
    /// postfix without a valid snapshot does nothing), and after <c>hooks.faultThreshold</c> failures both stop until the game restarts.</item>
    /// </list>
    /// To check in the game (the owner, with <c>framework-sample</c> or temporary entries, TSK-053): an entry <c>popularity.gain ×1.5</c> for the
    /// player's CPU makes the rise of its popularity (the percent of the heart in the CPU's sales panel, <c>Cpu.AveragePopularity</c>, the mean of the
    /// four segments) 1.5 times faster on the days it sells than with the entry off; the popularity of an old CPU falls at the game's speed; no
    /// popularity is above 100 % or below 5 % (0 % for an old CPU); with no entry the numbers are the game's; with verbose log on, <c>Player.log</c> has
    /// <c>market: popularity.gain x… raises a popularity gain … (P5)</c> once and no <c>P5/prefix</c> or <c>P5/postfix</c> faults.
    /// </summary>
    internal static class PopularityPatch
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string PrefixSite = "P5/prefix";
        private const string PostfixSite = "P5/postfix";

        /// <summary>The game's bounds of a popularity: the floor of a CPU in its life, the floor of an old CPU, the cap.</summary>
        internal const float Floor = 0.05f;
        internal const float OldFloor = 0f;
        internal const float Cap = 1f;

        private const int Slots = SegmentMap.PopulationCount;
        private const int InitialCpus = 64;
        private const int NoSnapshot = -1;

        // The snapshot of the day: slot k of CPU i is _before[i * Slots + k], NaN for a slot the CPU does not have. _snapshotCpus is how many CPUs
        // it holds, or NoSnapshot when the prefix did not take one (no records, the unit is off, a failure): then the postfix does nothing.
        private static float[] _before = new float[Slots * InitialCpus];
        private static int _snapshotCpus = NoSnapshot;
        private static bool _announced;   // the first change that was made is written to the verbose log, once per start

        [ModPatch(typeof(GameMarket), "UpdatePopularities", PatchKind.Prefix)]
        private static void Prefix(List<GameCpu> ___cpus)
        {
            _snapshotCpus = NoSnapshot;

            if (___cpus == null || ___cpus.Count == 0) return;
            if (!ModModifiers.HasAny(ModChannels.PopularityGain)) return;
            if (!ModHooks.CanRun(CoreId, PrefixSite)) return;

            try
            {
                List<GameCpu> cpus = ___cpus;
                int count = cpus.Count;
                float[] buffer = BufferFor(count);
                for (int i = 0; i < count; i++)
                {
                    GameCpu cpu = cpus[i];
                    List<float> popularities = ReferenceEquals(cpu, null) ? null : cpu.Popularities;
                    Take(popularities, buffer, i * Slots);
                }
                _snapshotCpus = count;
            }
            catch (Exception ex)
            {
                _snapshotCpus = NoSnapshot;
                ModHooks.ReportFault(CoreId, PrefixSite, ex);
            }
        }

        [ModPatch(typeof(GameMarket), "UpdatePopularities", PatchKind.Postfix)]
        private static void Postfix(List<GameCpu> ___cpus)
        {
            int snapshot = _snapshotCpus;
            _snapshotCpus = NoSnapshot;   // used once: a later call without a prefix must not see this day's numbers

            if (snapshot == NoSnapshot || ___cpus == null || ___cpus.Count != snapshot) return;
            if (!ModHooks.CanRun(CoreId, PostfixSite)) return;

            try
            {
                List<GameCpu> cpus = ___cpus;
                float[] buffer = _before;
                for (int i = 0; i < snapshot; i++)
                {
                    GameCpu cpu = cpus[i];
                    if (ReferenceEquals(cpu, null)) continue;

                    List<float> popularities = cpu.Popularities;
                    if (popularities == null) continue;

                    bool floorKnown = false;
                    float floor = Floor;
                    int slots = Math.Min(Slots, popularities.Count);
                    for (int k = 0; k < slots; k++)
                    {
                        float before = buffer[i * Slots + k];
                        float after = popularities[k];
                        if (!(after > before)) continue;   // decay, a CPU at the cap, no change, a slot that was not taken (NaN)

                        float multiplier = ModModifiers.Evaluate(ModChannels.PopularityGain,
                            new ModifierContext { Company = cpu.Company, Cpu = cpu, Segment = SegmentMap.FromPopulationIndex(k) });
                        if (multiplier == 1f) continue;

                        if (!floorKnown)
                        {
                            floor = cpu.IsOld(-1, -1) ? OldFloor : Floor;
                            floorKnown = true;
                        }

                        float scaled = Scale(before, after, multiplier, floor);
                        if (scaled == after) continue;

                        Announce(multiplier, before, after, scaled, k);
                        popularities[k] = scaled;
                    }
                }
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, PostfixSite, ex);
            }
        }

        /// <summary>
        /// The popularity after the channel: <c>before + (after − before) × multiplier</c> for a gain (<c>after &gt; before</c>), then the game's
        /// clamp to [<paramref name="floor"/>, <see cref="Cap"/>]. <paramref name="after"/> as the game left it is returned for a gain that is
        /// not positive (the decay), a multiplier that is 1 and a multiplier that is not a usable number (NaN, infinity, negative).
        /// </summary>
        internal static float Scale(float before, float after, float multiplier, float floor)
        {
            float gain = after - before;
            if (!(gain > 0f) || multiplier == 1f || float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f) return after;

            float value = before + gain * multiplier;
            if (value <= floor) value = floor;
            if (value >= Cap) value = Cap;
            return value;
        }

        /// <summary>Copies up to four slots of <paramref name="popularities"/> to <paramref name="buffer"/> at <paramref name="offset"/>; NaN for a slot the list does not have.</summary>
        internal static void Take(List<float> popularities, float[] buffer, int offset)
        {
            int have = popularities == null ? 0 : popularities.Count;
            for (int k = 0; k < Slots; k++) buffer[offset + k] = k < have ? popularities[k] : float.NaN;
        }

        /// <summary>The reusable buffer, large enough for <paramref name="cpus"/> CPUs; replaced by a larger one (twice the need) only when the market outgrows it.</summary>
        private static float[] BufferFor(int cpus)
        {
            int needed = cpus * Slots;
            if (_before.Length < needed) _before = new float[Math.Max(needed, _before.Length * 2)];
            return _before;
        }

        private static void Announce(float multiplier, float before, float after, float scaled, int slot)
        {
            if (_announced) return;
            _announced = true;

            ModLog.Debug(CoreId, "market: " + ModChannels.PopularityGain + " x" + multiplier.ToString("0.###", CultureInfo.InvariantCulture)
                                 + " raises a popularity gain in " + SegmentMap.FromPopulationIndex(slot) + " from "
                                 + (after - before).ToString("0.#####", CultureInfo.InvariantCulture) + " to "
                                 + (scaled - before).ToString("0.#####", CultureInfo.InvariantCulture) + " (P5)");
        }
    }
}
