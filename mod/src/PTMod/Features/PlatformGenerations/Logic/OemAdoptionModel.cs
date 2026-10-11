using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// One generation as the OEM model sees it on a day (FR-8): launched or not, how long ago, its share of the installed base and whether it
    /// is obsolete on that day. A list of these, <b>newest generation first</b>, is the input of the assignment of preferences and of the market average.
    /// </summary>
    public struct GenAdoptionInput
    {
        public GenerationDef Gen;

        /// <summary>The first CPU of the generation has been released (by the day this input is built for).</summary>
        public bool Launched;

        /// <summary>Calendar months from the launch to the day (<c>m</c> of formula F7); meaningless when not launched.</summary>
        public int MonthsSinceLaunch;

        /// <summary><c>IBshare_G</c>: the share of the installed base on the sockets of the generation, 0..1.</summary>
        public double IbShareG;

        /// <summary>The generation is obsolete on the day (<c>obsoleteYear</c> has come): it takes no part in the choice.</summary>
        public bool ObsoleteAtT;
    }

    /// <summary>
    /// The OEM side of the feature (FR-8, formulas F7-F9): how far the OEM clients have moved to a generation, which generation a contract prefers,
    /// and by how much the signing chance of a CPU changes with its platform. Pure arithmetic over plain inputs; the numbers come from
    /// <see cref="PlatformBalance"/> and the curves of the generations.
    /// </summary>
    public static class OemAdoptionModel
    {
        // ---- the reasons the provider reports (D12); the texts are in the string tables ----

        /// <summary>The CPU is of the preferred generation or a newer one.</summary>
        public const string ReasonPrefers = "pg.reason.oemPrefers";

        /// <summary>The CPU is behind the generation the client prefers.</summary>
        public const string ReasonLag = "pg.reason.oemLag";

        /// <summary>The market average of the preferences ("market" mode).</summary>
        public const string ReasonAdoption = "pg.reason.oemAdoption";

        /// <summary>The loyalty of the OEM to a big installed base.</summary>
        public const string ReasonLoyalty = "pg.reason.oemLoyalty";

        // ---- F7 ----

        /// <summary>
        /// F7: <c>A_G(m) = min(maxAdoption, maxAdoption·clamp01((m − delayMonths)/rampMonths) + ibAccel·IBshare_G)</c>; 0 for a generation that
        /// is not launched, a missing curve, or a result below 0. <paramref name="ibShareG"/> is kept in [0, 1].
        /// </summary>
        public static double Adoption(OemCurveDef c, bool launched, int monthsSinceLaunch, double ibShareG)
        {
            if (c == null || !launched) return 0;

            double ramp = c.rampMonths > 0 ? c.rampMonths : 1;
            double progress = (monthsSinceLaunch - c.delayMonths) / ramp;
            if (!(progress > 0)) progress = 0;
            else if (progress > 1) progress = 1;

            double share = ibShareG;
            if (!(share > 0)) share = 0;
            else if (share > 1) share = 1;

            double max = c.maxAdoption;
            double a = max * progress + c.ibAccel * share;
            if (a > max) a = max;
            return a > 0 ? a : 0;
        }

        /// <summary>F7 for an input of the list.</summary>
        public static double Adoption(GenAdoptionInput input)
        {
            return input.Gen == null ? 0 : Adoption(input.Gen.oem, input.Launched, input.MonthsSinceLaunch, input.IbShareG);
        }

        /// <summary>
        /// Builds the list for a day, newest generation first: for every generation of the catalog whether it is launched by <paramref name="at"/>
        /// (the chronology says so and the launch is not in the future of the day), the months since, its share of the installed base and whether
        /// it is obsolete at <paramref name="at"/>. <paramref name="into"/> is cleared and filled (reuse it to avoid allocations); the same list is returned.
        /// </summary>
        public static List<GenAdoptionInput> BuildInputs(GenerationCatalog catalog, IPlatformHistory history, InstalledBaseLedger ib, GameDate at,
            List<GenAdoptionInput> into)
        {
            List<GenAdoptionInput> list = into ?? new List<GenAdoptionInput>();
            list.Clear();
            if (catalog == null) return list;

            IReadOnlyList<GenerationDef> generations = catalog.Generations;
            for (int i = generations.Count - 1; i >= 0; i--)
            {
                GenerationDef g = generations[i];

                GameDate launchedOn = GameDate.None;
                string by;
                bool launched = history != null && history.IsLaunched(g.id, out launchedOn, out by) && launchedOn.IsSet && (!at.IsSet || launchedOn <= at);

                double share = 0;
                if (ib != null && ib.Total > 0) share = ib.OfGeneration(g.id) / ib.Total;

                list.Add(new GenAdoptionInput
                {
                    Gen = g,
                    Launched = launched,
                    MonthsSinceLaunch = launched && at.IsSet ? launchedOn.MonthsTo(at) : 0,
                    IbShareG = share,
                    ObsoleteAtT = g.obsoleteYear > 0 && at.IsSet && at.Year >= g.obsoleteYear,
                });
            }
            return list;
        }

        // ---- F8 ----

        /// <summary>
        /// F8 (the "per contract" mode): goes through the generations from the newest to the oldest, skips the ones that are not launched or are
        /// obsolete on the day, and takes the first whose <see cref="Roll"/> is below its adoption. The generation id, or "" when no generation is
        /// chosen. The caller builds the inputs for <c>t_created = AcceptanceDate − 12 months</c> and only for contract types of <c>contractTypes</c>.
        /// </summary>
        public static string AssignPreference(string contractKey, uint worldSeed, IReadOnlyList<GenAdoptionInput> newestFirst)
        {
            if (string.IsNullOrEmpty(contractKey) || newestFirst == null) return string.Empty;

            for (int i = 0; i < newestFirst.Count; i++)
            {
                GenAdoptionInput input = newestFirst[i];
                if (input.Gen == null || !input.Launched || input.ObsoleteAtT) continue;

                double a = Adoption(input);
                if (a > 0 && Roll(contractKey, input.Gen.id, worldSeed) < a) return input.Gen.id;
            }
            return string.Empty;
        }

        /// <summary>
        /// The deterministic dice of F8: FNV-1a (64 bit) of the UTF-8 bytes of <c>contractKey + "#" + genId + "#" + worldSeed</c>, mixed by the
        /// finalizer of MurmurHash3 (<c>fmix64</c>), its top 53 bits as a number in [0, 1). The finalizer matters: plain FNV-1a changes the top
        /// bits very little when only the last characters differ (two world seeds that differ in the last digit would roll alike). The same
        /// key, generation and seed give the same number in every session and on every machine (NFR-4).
        /// </summary>
        public static double Roll(string contractKey, string genId, uint worldSeed)
        {
            string text = (contractKey ?? string.Empty) + "#" + (genId ?? string.Empty) + "#" + worldSeed.ToString(CultureInfo.InvariantCulture);
            byte[] bytes = Encoding.UTF8.GetBytes(text);

            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= 1099511628211UL;
            }

            // fmix64 of MurmurHash3: every input bit reaches every output bit.
            hash ^= hash >> 33;
            hash *= 0xff51afd7ed558ccdUL;
            hash ^= hash >> 33;
            hash *= 0xc4ceb9fe1a85ec53UL;
            hash ^= hash >> 33;

            return (hash >> 11) / 9007199254740992.0;   // the top 53 bits over 2^53
        }

        // ---- F9a ----

        /// <summary>
        /// F9a for a known preference, without the contract type (see the overload with it): <c>1 + adoptBonus</c> for a CPU of the preferred
        /// generation or a newer one (<paramref name="cpuGenOrder"/> ≥ <paramref name="prefGenOrder"/>), <c>1 − lagPenalty1</c> one generation
        /// behind, <c>1 − lagPenalty2</c> two or more behind; <c>+ fullPlatformBonus</c> for a full platform. 1 (and no reason) when the CPU or
        /// the preference has no generation. <paramref name="reasonKey"/> is <see cref="ReasonPrefers"/> or <see cref="ReasonLag"/>, null for 1.
        /// </summary>
        public static float PlatformMultiplier(int? cpuGenOrder, int? prefGenOrder, bool fullPlatform, PlatformBalance b, out string reasonKey)
        {
            reasonKey = null;
            if (!cpuGenOrder.HasValue || !prefGenOrder.HasValue) return 1f;

            PlatformBalance.OemBalance o = (b ?? PlatformBalance.Defaults()).Oem;
            int lag = prefGenOrder.Value - cpuGenOrder.Value;

            float baseValue;
            if (lag <= 0)
            {
                baseValue = 1f + o.AdoptBonus;
                reasonKey = ReasonPrefers;
            }
            else
            {
                baseValue = 1f - (lag == 1 ? o.LagPenalty1 : o.LagPenalty2);
                reasonKey = ReasonLag;
            }
            return fullPlatform ? baseValue + o.FullPlatformBonus : baseValue;
        }

        /// <summary>
        /// F9a with the type of the contract: 1 (no reason) when the type is not one of <c>contractTypes</c> (Calculator, 0, by default), otherwise
        /// <see cref="PlatformMultiplier(int?, int?, bool, PlatformBalance, out string)"/>.
        /// </summary>
        public static float PlatformMultiplier(int? cpuGenOrder, int? prefGenOrder, bool fullPlatform, int contractType, PlatformBalance b, out string reasonKey)
        {
            PlatformBalance balance = b ?? PlatformBalance.Defaults();
            if (!balance.Oem.IncludesContractType(contractType))
            {
                reasonKey = null;
                return 1f;
            }
            return PlatformMultiplier(cpuGenOrder, prefGenOrder, fullPlatform, balance, out reasonKey);
        }

        // ---- F9b ----

        /// <summary>
        /// F9b (the "market" mode): the multiplier a CPU of generation <paramref name="cpuGenOrder"/> is expected to get over the preferences of the
        /// market on the day: <c>c_avg = Σ_G p_G·c_plat(Gc, G, full) + p_none</c>, with <c>p_G = A_G·Π_{H newer}(1 − A_H)</c> and <c>p_none = Π(1 − A_H)</c> over the
        /// launched generations that are not obsolete on the day. 1 when the CPU has no generation or its order is below <c>marketAverageMinGenOrder</c>.
        /// </summary>
        public static float MarketAverageMultiplier(int? cpuGenOrder, bool fullPlatform, IReadOnlyList<GenAdoptionInput> newestFirst, PlatformBalance b)
        {
            if (!cpuGenOrder.HasValue || newestFirst == null) return 1f;

            PlatformBalance balance = b ?? PlatformBalance.Defaults();
            if (cpuGenOrder.Value < balance.Oem.MarketAverageMinGenOrder) return 1f;

            double none = 1.0;
            double sum = 0;
            for (int i = 0; i < newestFirst.Count; i++)
            {
                GenAdoptionInput input = newestFirst[i];
                if (input.Gen == null || !input.Launched || input.ObsoleteAtT) continue;

                double a = Clamp01(Adoption(input));
                double p = a * none;
                if (p > 0)
                {
                    string reason;
                    sum += p * PlatformMultiplier(cpuGenOrder, input.Gen.order, fullPlatform, balance, out reason);
                }
                none *= 1.0 - a;
            }
            return (float)(sum + none);
        }

        /// <summary>
        /// The preferences of the market on the day (the weights of F9b): <paramref name="p"/>[i] is <c>p_G</c> of <paramref name="newestFirst"/>[i]
        /// (0 for a generation that is not launched or is obsolete) and <paramref name="pNone"/> is <c>p_none</c>; the weights add up to 1.
        /// <paramref name="p"/> must have at least as many elements as the list.
        /// </summary>
        public static void MarketDistribution(IReadOnlyList<GenAdoptionInput> newestFirst, double[] p, out double pNone)
        {
            pNone = 1.0;
            if (newestFirst == null || p == null) return;

            for (int i = 0; i < newestFirst.Count; i++)
            {
                GenAdoptionInput input = newestFirst[i];
                if (input.Gen == null || !input.Launched || input.ObsoleteAtT)
                {
                    p[i] = 0;
                    continue;
                }
                double a = Clamp01(Adoption(input));
                p[i] = a * pNone;
                pNone *= 1.0 - a;
            }
        }

        // ---- F9c ----

        /// <summary>
        /// F9c: the platform multiplier times the loyalty of the OEM to a big installed base, <c>1 + oemLoyaltyBonus·min(1, effShare/ibSaturationShare)</c>,
        /// kept in <c>minChanceMult</c>..<c>maxChanceMult</c>.
        /// </summary>
        public static float WithLoyalty(float platformMult, double effShare, PlatformBalance b)
        {
            PlatformBalance balance = b ?? PlatformBalance.Defaults();
            PlatformBalance.OemBalance o = balance.Oem;

            double saturation = balance.Rating.IbSaturationShare > 0 ? balance.Rating.IbSaturationShare : 1.0;
            double loyalty = 1.0 + o.OemLoyaltyBonus * Math.Min(1.0, Math.Max(0.0, effShare) / saturation);

            double value = platformMult * loyalty;
            if (double.IsNaN(value)) return 1f;
            if (value < o.MinChanceMult) return o.MinChanceMult;
            if (value > o.MaxChanceMult) return o.MaxChanceMult;
            return (float)value;
        }

        private static double Clamp01(double value)
        {
            if (!(value > 0)) return 0;
            return value > 1 ? 1 : value;
        }
    }
}
