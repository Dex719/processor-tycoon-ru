using System;
using System.Collections.Generic;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// One offer of a contract as the maths sees it: who offered which CPU and how fast it is. Built by the adapter (<c>ContractInspector</c>)
    /// from <c>Contract.OffersIDs</c>; free of the game, so companies are the string keys of <c>ModCompanies.KeyOf</c> (D9).
    /// </summary>
    public struct OfferInput
    {
        /// <summary><c>ModCompanies.KeyOf(cpu.Company)</c>, or <c>sid:&lt;SaveID&gt;</c> when the company has no key.</summary>
        public string CompanyKey;

        /// <summary><c>Cpu.SaveID</c>.</summary>
        public int CpuId;

        /// <summary><c>Cpu.Ips</c>.</summary>
        public float Ips;

        /// <summary>
        /// The order in which the company first appeared in <c>OffersIDs</c> (the tie-break of the draw: at equal IPS the company that offered first
        /// comes first). <see cref="OddsMath.BestPerCompany"/> numbers the companies 0, 1, 2... in that order.
        /// </summary>
        public int FirstSeenIndex;

        public OfferInput(string companyKey, int cpuId, float ips, int firstSeenIndex)
        {
            CompanyKey = companyKey;
            CpuId = cpuId;
            Ips = ips;
            FirstSeenIndex = firstSeenIndex;
        }
    }

    /// <summary>One row of the breakdown table: the best offer of a company with its chances, in the order the game draws them.</summary>
    public struct OfferOdds
    {
        public string CompanyKey;
        public int CpuId;
        public float Ips;

        /// <summary><c>clamp(round(100 × Ips / ΣIps), 1, 100)</c>: the chance without any mod.</summary>
        public int VanillaChance;

        /// <summary>The chance <c>Contract.GetBestOffers</c> gave the offer (mods included); the vanilla chance when there is no data from the game.</summary>
        public int GameChance;

        /// <summary>0 for the offer the draw looks at first.</summary>
        public int DrawPosition;

        /// <summary>P<sub>i</sub>: the probability that the contract picks this offer (<see cref="OddsMath.SequentialProbabilities"/>), 0..1.</summary>
        public double ActualProbability;
    }

    /// <summary>
    /// The chances of a contract (FR-2, FR-11; design.md formulas F2-F5): the algorithm of <c>Contract.GetBestOffers</c>, <c>Contract.ChanceToSign</c> and
    /// <c>Contract.ChooseCpu</c> as pure functions. The game stays the source of truth for the chance of each offer (the adapter calls
    /// <c>GetBestOffers</c>, so mods are in); this class orders the offers like the game, copies the vanilla formula for the comparison column
    /// and turns the chances into the probability that the draw really picks each offer.
    /// </summary>
    public static class OddsMath
    {
        /// <summary>The smallest chance the game gives an offer (<c>CpuOffer.DefineChance</c> clamps to 1..100).</summary>
        public const int MinChance = 1;

        /// <summary>The largest chance of an offer.</summary>
        public const int MaxChance = 100;

        // ---- the game's own steps ----

        /// <summary>
        /// The best CPU of each company, like <c>Contract.GetBestOffers</c>: the company's CPU with the largest IPS (the first one offered when two are equal),
        /// the companies in the order of their first offer. The result is numbered: <see cref="OfferInput.FirstSeenIndex"/> is the position of the company
        /// in that order. The input is not changed.
        /// </summary>
        public static List<OfferInput> BestPerCompany(IReadOnlyList<OfferInput> offers)
        {
            var result = new List<OfferInput>();
            if (offers == null) return result;

            var slotOfCompany = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < offers.Count; i++)
            {
                OfferInput offer = offers[i];
                string key = offer.CompanyKey ?? string.Empty;

                int slot;
                if (!slotOfCompany.TryGetValue(key, out slot))
                {
                    slotOfCompany[key] = result.Count;
                    offer.FirstSeenIndex = result.Count;
                    result.Add(offer);
                }
                else if (offer.Ips > result[slot].Ips)
                {
                    offer.FirstSeenIndex = result[slot].FirstSeenIndex;
                    result[slot] = offer;
                }
            }
            return result;
        }

        /// <summary>
        /// The rule of <c>Contract.ChanceToSign</c>: every offer of the candidate's company is dropped (companies are compared by key) and the candidate
        /// goes last, so the candidate's company is the last one in the order of first offers. The input is not changed.
        /// </summary>
        public static List<OfferInput> WithCandidate(IReadOnlyList<OfferInput> offers, OfferInput candidate)
        {
            var result = new List<OfferInput>();
            string candidateKey = candidate.CompanyKey ?? string.Empty;

            int lastIndex = -1;
            if (offers != null)
            {
                for (int i = 0; i < offers.Count; i++)
                {
                    OfferInput offer = offers[i];
                    if (string.Equals(offer.CompanyKey ?? string.Empty, candidateKey, StringComparison.Ordinal)) continue;
                    result.Add(offer);
                    if (offer.FirstSeenIndex > lastIndex) lastIndex = offer.FirstSeenIndex;
                }
            }

            candidate.FirstSeenIndex = Math.Max(lastIndex + 1, result.Count);
            result.Add(candidate);
            return result;
        }

        /// <summary>
        /// The chance without mods, as <c>CpuOffer.DefineChance</c> computes it: <c>clamp(round(100 × ips / sumBestIps), 1, 100)</c>. The arithmetic is
        /// the game's (float, <c>Mathf.RoundToInt</c> rounds a half to the even number). A sum that is zero or not a number gives the minimum, as the
        /// game's conversion of a NaN does.
        /// </summary>
        public static int VanillaChance(float ips, float sumBestIps)
        {
            return ChanceOfShare(ips / sumBestIps, 1f);
        }

        /// <summary>The sum of the IPS of the offers, accumulated like <c>Enumerable.Sum</c> (in double, then cut to float): the <c>ΣIps</c> of the game.</summary>
        public static float SumIps(IReadOnlyList<OfferInput> offers)
        {
            double sum = 0;
            if (offers != null)
            {
                for (int i = 0; i < offers.Count; i++) sum += offers[i].Ips;
            }
            return (float)sum;
        }

        /// <summary>The largest IPS among the offers; 0 when there are none.</summary>
        public static float MaxIps(IReadOnlyList<OfferInput> offers)
        {
            float max = 0f;
            if (offers == null) return max;
            for (int i = 0; i < offers.Count; i++)
            {
                if (offers[i].Ips > max) max = offers[i].Ips;
            }
            return max;
        }

        /// <summary>The offers of every company except the one with this key (the competitors of a candidate), in their order.</summary>
        public static List<OfferInput> WithoutCompany(IReadOnlyList<OfferInput> offers, string companyKey)
        {
            var result = new List<OfferInput>();
            if (offers == null) return result;

            string key = companyKey ?? string.Empty;
            for (int i = 0; i < offers.Count; i++)
            {
                if (!string.Equals(offers[i].CompanyKey ?? string.Empty, key, StringComparison.Ordinal)) result.Add(offers[i]);
            }
            return result;
        }

        // ---- the draw ----

        /// <summary>
        /// The table of a contract in the order <c>Contract.ChooseCpu</c> looks at the offers: by IPS from the largest, offers with equal IPS by the order
        /// of their first appearance. <paramref name="gameChances"/> runs parallel to <paramref name="best"/> (the same index, not the draw order) and
        /// holds the chance the game gave each offer; null means no data from the game, so the vanilla chances stand in. The vanilla column is computed
        /// from the sum of the IPS of <paramref name="best"/>. Throws <see cref="ArgumentException"/> when the two lists differ in length.
        /// </summary>
        public static List<OfferOdds> DrawOrder(IReadOnlyList<OfferInput> best, IReadOnlyList<int> gameChances)
        {
            int count = best == null ? 0 : best.Count;
            if (gameChances != null && gameChances.Count != count)
                throw new ArgumentException("gameChances has " + gameChances.Count + " items, best has " + count, "gameChances");

            var result = new List<OfferOdds>(count);
            if (count == 0) return result;

            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            Array.Sort(order, (a, b) => CompareForDraw(best[a], a, best[b], b));

            float sum = SumIps(best);
            var chances = new int[count];
            for (int position = 0; position < count; position++)
            {
                OfferInput offer = best[order[position]];
                int vanilla = VanillaChance(offer.Ips, sum);
                int game = gameChances == null ? vanilla : gameChances[order[position]];
                chances[position] = game;
                result.Add(new OfferOdds
                {
                    CompanyKey = offer.CompanyKey,
                    CpuId = offer.CpuId,
                    Ips = offer.Ips,
                    VanillaChance = vanilla,
                    GameChance = game,
                    DrawPosition = position,
                });
            }

            double[] actual = SequentialProbabilities(chances);
            for (int position = 0; position < count; position++)
            {
                OfferOdds row = result[position];
                row.ActualProbability = actual[position];
                result[position] = row;
            }
            return result;
        }

        /// <summary>
        /// The probability that the draw picks each offer. The game takes the offers in order and takes offer <i>i</i> with the chance <c>q<sub>i</sub> = Chance<sub>i</sub> / 100</c>;
        /// when nothing is taken it goes round again, so <c>P<sub>i</sub> = q<sub>i</sub> × Π<sub>j&lt;i</sub>(1 − q<sub>j</sub>) / (1 − Π<sub>all</sub>(1 − q<sub>j</sub>))</c>.
        /// <paramref name="chancesInDrawOrder"/> are the chances in the order of <see cref="DrawOrder"/> (values outside 0..100 are cut to it). When every chance
        /// is 0 nothing can ever be taken and every probability is 0. The probabilities add up to 1 otherwise.
        /// </summary>
        public static double[] SequentialProbabilities(IReadOnlyList<int> chancesInDrawOrder)
        {
            int count = chancesInDrawOrder == null ? 0 : chancesInDrawOrder.Count;
            var result = new double[count];
            if (count == 0) return result;

            var q = new double[count];
            var miss = new double[count];
            double allMiss = 1.0;
            for (int i = 0; i < count; i++)
            {
                int chance = Math.Max(0, Math.Min(100, chancesInDrawOrder[i]));
                q[i] = chance / 100.0;
                miss[i] = (100 - chance) / 100.0;
                allMiss *= miss[i];
            }

            double denominator = 1.0 - allMiss;
            if (!(denominator > 0)) return result;

            double passed = 1.0;   // the probability that the offers before i were all passed over
            for (int i = 0; i < count; i++)
            {
                result[i] = q[i] * passed / denominator;
                passed *= miss[i];
            }
            return result;
        }

        // ---- hints ----

        /// <summary>
        /// The IPS a CPU needs to get the share <paramref name="targetChance01"/> (0..1, exclusive) against competitors whose best offers add up to
        /// <paramref name="sumOthersBestIps"/>: <c>s / (1 − s) × Σ</c> (50 % needs exactly the sum of the others). 0 for a target of 0 or less,
        /// positive infinity for 1 or more, NaN for a NaN.
        /// </summary>
        public static float IpsForChance(double targetChance01, float sumOthersBestIps)
        {
            if (double.IsNaN(targetChance01) || float.IsNaN(sumOthersBestIps)) return float.NaN;
            if (targetChance01 <= 0) return 0f;
            if (targetChance01 >= 1) return float.PositiveInfinity;
            return (float)(targetChance01 / (1.0 - targetChance01) * sumOthersBestIps);
        }

        /// <summary>
        /// The ids of the CPUs of the candidate's company that the review of the contract will not count: a company counts only its fastest CPU. The
        /// company's own offers plus the candidate are compared; the one with the largest IPS stays (the earlier of equal ones; the candidate is the
        /// last), every other id is returned in the order of the list. Empty when the company has one CPU in play.
        /// </summary>
        public static List<int> ShadowedOwn(IReadOnlyList<OfferInput> offers, OfferInput candidate)
        {
            List<OfferInput> own = OwnOffers(offers, candidate);
            int strongest = IndexOfStrongest(own);

            var result = new List<int>();
            for (int i = 0; i < own.Count; i++)
            {
                if (i != strongest) result.Add(own[i].CpuId);
            }
            return result;
        }

        /// <summary>The id of the CPU the review will count for the candidate's company (see <see cref="ShadowedOwn"/>); the candidate's id when it is alone.</summary>
        public static int StrongestOwnCpuId(IReadOnlyList<OfferInput> offers, OfferInput candidate)
        {
            List<OfferInput> own = OwnOffers(offers, candidate);
            return own[IndexOfStrongest(own)].CpuId;
        }

        /// <summary>
        /// The estimate of FR-11: the candidate's actual probability if every possible competitor in <paramref name="potential"/> also made an offer. The
        /// offers of <paramref name="best"/> (it holds the candidate, found by <paramref name="candidateCpuId"/>) and of <paramref name="potential"/>
        /// are merged to the best offer of each company, chances are the vanilla ones, and the draw is recomputed. A possible bidder from the
        /// candidate's own company is ignored. <paramref name="candidateChanceMultiplier"/> is the factor mods put on the candidate's chance (1 for none);
        /// the others get none, as nothing is known about them. Throws <see cref="ArgumentException"/> when the candidate is not in <paramref name="best"/>.
        /// </summary>
        public static double PessimisticProbability(
            IReadOnlyList<OfferInput> best, IReadOnlyList<OfferInput> potential,
            string candidateCompanyKey, int candidateCpuId, double candidateChanceMultiplier = 1.0)
        {
            var combined = new List<OfferInput>();
            if (best != null) combined.AddRange(best);

            string candidateKey = candidateCompanyKey ?? string.Empty;
            if (potential != null)
            {
                for (int i = 0; i < potential.Count; i++)
                {
                    if (string.Equals(potential[i].CompanyKey ?? string.Empty, candidateKey, StringComparison.Ordinal)) continue;
                    combined.Add(potential[i]);
                }
            }

            List<OfferInput> merged = BestPerCompany(combined);
            int candidateIndex = -1;
            for (int i = 0; i < merged.Count; i++)
            {
                if (merged[i].CpuId == candidateCpuId && string.Equals(merged[i].CompanyKey ?? string.Empty, candidateKey, StringComparison.Ordinal))
                {
                    candidateIndex = i;
                    break;
                }
            }
            if (candidateIndex < 0) throw new ArgumentException("the candidate CPU " + candidateCpuId + " is not among the best offers", "candidateCpuId");

            float multiplier = double.IsNaN(candidateChanceMultiplier) || double.IsInfinity(candidateChanceMultiplier) || candidateChanceMultiplier < 0
                ? 1f
                : (float)candidateChanceMultiplier;

            float sum = SumIps(merged);
            var chances = new int[merged.Count];
            for (int i = 0; i < merged.Count; i++)
            {
                chances[i] = ChanceOfShare(merged[i].Ips / sum, i == candidateIndex ? multiplier : 1f);
            }

            List<OfferOdds> table = DrawOrder(merged, chances);
            for (int i = 0; i < table.Count; i++)
            {
                if (table[i].CpuId == candidateCpuId) return table[i].ActualProbability;
            }
            return 0.0;   // unreachable: the candidate is in the table
        }

        // ---- private ----

        /// <summary>
        /// <c>clamp(round(share × 100 × multiplier), 1, 100)</c> in the game's float arithmetic (patch P8 of the core computes the chance with a mod this
        /// way: <c>ipsShare × 1e+02f × multiplier</c>). A result that is not a number gives the minimum.
        /// </summary>
        private static int ChanceOfShare(float share, float multiplier)
        {
            float scaled = (float)((float)(share * 1e+02f) * multiplier);
            if (float.IsNaN(scaled) || float.IsInfinity(scaled)) return MinChance;

            double rounded = Math.Round((double)scaled);   // to even on a half, like Mathf.RoundToInt
            if (rounded < MinChance) return MinChance;
            if (rounded > MaxChance) return MaxChance;
            return (int)rounded;
        }

        private static int CompareForDraw(OfferInput a, int indexA, OfferInput b, int indexB)
        {
            int byIps = b.Ips.CompareTo(a.Ips);   // larger IPS first
            if (byIps != 0) return byIps;
            int bySeen = a.FirstSeenIndex.CompareTo(b.FirstSeenIndex);
            return bySeen != 0 ? bySeen : indexA.CompareTo(indexB);
        }

        private static List<OfferInput> OwnOffers(IReadOnlyList<OfferInput> offers, OfferInput candidate)
        {
            var own = new List<OfferInput>();
            string key = candidate.CompanyKey ?? string.Empty;
            bool candidateListed = false;
            if (offers != null)
            {
                for (int i = 0; i < offers.Count; i++)
                {
                    if (!string.Equals(offers[i].CompanyKey ?? string.Empty, key, StringComparison.Ordinal)) continue;
                    own.Add(offers[i]);
                    if (offers[i].CpuId == candidate.CpuId) candidateListed = true;
                }
            }
            if (!candidateListed) own.Add(candidate);
            return own;
        }

        private static int IndexOfStrongest(List<OfferInput> own)
        {
            int strongest = 0;
            for (int i = 1; i < own.Count; i++)
            {
                if (own[i].Ips > own[strongest].Ips) strongest = i;
            }
            return strongest;
        }
    }
}
