using System;
using System.Collections.Generic;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>Why an AI CPU may bid on a contract, or why it may not (<see cref="AiBidRules.Check"/>).</summary>
    public enum AiBidVerdict
    {
        /// <summary>The CPU can be offered: the AI may send it before the review date.</summary>
        Qualifies = 0,

        /// <summary>The CPU is withdrawn from production.</summary>
        Retired = 1,

        /// <summary>The CPU is not for sale on contracts (<c>SellOnContracts</c> is off).</summary>
        NotForContracts = 2,

        /// <summary>The CPU fails a requirement of the contract (<c>Contract.FailsAtAnyRequirement</c>): the game would ignore the offer.</summary>
        FailsRequirements = 3,

        /// <summary>The margin on the contract's price is below the AI's threshold (<c>ai.contractMarginThreshold</c>).</summary>
        MarginTooLow = 4,
    }

    /// <summary>
    /// What the estimate of FR-11 needs to know about one CPU of an AI company: who owns it, how fast it is and the answers to the questions of <see cref="AiBidRules.Check"/>. The questions that cost
    /// a computation in the game (the check of the requirements, the cost of a unit at the contract's price) are functions, asked only when the cheaper questions have not ruled the CPU out.
    /// </summary>
    public sealed class AiCpuFact
    {
        /// <summary>The key of the company (<c>ai:&lt;id&gt;</c>).</summary>
        public string CompanyKey;

        /// <summary><c>Cpu.SaveID</c>.</summary>
        public int CpuId;

        /// <summary><c>Cpu.Ips</c>.</summary>
        public float Ips;

        /// <summary><c>Cpu.IsRetired</c>.</summary>
        public bool IsRetired;

        /// <summary><c>Cpu.SellOnContracts</c>.</summary>
        public bool SellOnContracts = true;

        /// <summary><c>Contract.FailsAtAnyRequirement(cpu)</c>; null counts as passing.</summary>
        public Func<bool> FailsRequirements;

        /// <summary>The price per unit of the contract (<c>Contract.PaymentPerUnit</c>).</summary>
        public float PaymentPerUnit;

        /// <summary><c>Cpu.RealUnitCostWithNewPrice(PaymentPerUnit)</c>; null counts as a cost of 0.</summary>
        public Func<float> UnitCostAtPayment;
    }

    /// <summary>
    /// Which CPUs of the AI companies may still bid on a contract (FR-11 item 1): the rule of <c>AIBehaviourContract.OnTick</c> (the margin, a copy of its literal 0.45 kept in
    /// <c>rules.json</c>) together with the conditions the player's panel names: the CPU passes the requirements, is not withdrawn and is for sale on contracts. The estimate takes every such CPU,
    /// not only the one the AI would pick this month, and ignores the AI's focus on kinds of contracts, so it is pessimistic: that is why the panel calls it an estimate.
    /// </summary>
    public static class AiBidRules
    {
        /// <summary>
        /// The margin of the AI's test: <c>(payment - cost) / payment</c> in the game's float arithmetic. Zero payment gives a value that is not a number or an infinity; the test of the game lets a
        /// NaN through (<see cref="MarginPasses"/>).
        /// </summary>
        public static float Margin(float paymentPerUnit, float unitCost)
        {
            return (paymentPerUnit - unitCost) / paymentPerUnit;
        }

        /// <summary>
        /// The test of <c>AIBehaviourContract.OnTick</c>: the CPU is skipped when <c>margin &lt; 0.45f</c>. Written the same way, so a margin that is not a number is not skipped (as in the game); equal
        /// to the threshold passes. The threshold is cut to float like the literal of the game.
        /// </summary>
        public static bool MarginPasses(float margin, double threshold)
        {
            return !(margin < (float)threshold);
        }

        /// <summary>The verdict on one CPU, cheapest question first; the first answer that rules the CPU out is the verdict.</summary>
        public static AiBidVerdict Check(AiCpuFact fact, double marginThreshold)
        {
            if (fact == null) throw new ArgumentNullException("fact");
            if (fact.IsRetired) return AiBidVerdict.Retired;
            if (!fact.SellOnContracts) return AiBidVerdict.NotForContracts;
            if (fact.FailsRequirements != null && fact.FailsRequirements()) return AiBidVerdict.FailsRequirements;

            float cost = fact.UnitCostAtPayment == null ? 0f : fact.UnitCostAtPayment();
            return MarginPasses(Margin(fact.PaymentPerUnit, cost), marginThreshold) ? AiBidVerdict.Qualifies : AiBidVerdict.MarginTooLow;
        }

        /// <summary>
        /// The best CPU of each company among those that qualify, as maths input: one row per company (the one with the largest IPS, the first of equal ones), the companies in the order of
        /// their first qualifying CPU. A company without a qualifying CPU is not in the result. Null or an empty list give an empty result. A check that throws (the game could not answer for
        /// that CPU) passes the exception to <paramref name="onError"/> and the CPU is left out; without <paramref name="onError"/> the exception goes up.
        /// </summary>
        public static List<OfferInput> BestQualifying(IEnumerable<AiCpuFact> facts, double marginThreshold, Action<AiCpuFact, Exception> onError = null)
        {
            var qualifying = new List<OfferInput>();
            if (facts == null) return qualifying;

            foreach (AiCpuFact fact in facts)
            {
                if (fact == null) continue;

                AiBidVerdict verdict;
                try
                {
                    verdict = Check(fact, marginThreshold);
                }
                catch (Exception ex)
                {
                    if (onError == null) throw;
                    onError(fact, ex);
                    continue;
                }

                if (verdict != AiBidVerdict.Qualifies) continue;
                qualifying.Add(new OfferInput(fact.CompanyKey, fact.CpuId, fact.Ips, qualifying.Count));
            }
            return OddsMath.BestPerCompany(qualifying);
        }
    }

    /// <summary>
    /// The months of the AI's moves before a contract is reviewed (FR-11 item 1). The AI offers its CPUs on the first day of every month, late in the day (<c>OnLateMonthPassed</c>); the game
    /// draws a contract on the early tick of the first day of the review month (<c>ContractManager</c> on <c>OnEarlyTick</c>), before that day's AI move. So the moves that can still change a
    /// contract are the first days of the months strictly after today and strictly before the review month.
    /// </summary>
    public static class AiMoveCalendar
    {
        /// <summary>
        /// How many monthly moves of the AI are left before the contract of the review month is drawn. 0 when the review month is the current one, the next one (its move comes after the draw) or past.
        /// Today's own move counts as done.
        /// </summary>
        public static int MovesBefore(DateTime today, int reviewYear, int reviewMonth)
        {
            if (reviewMonth < 1 || reviewMonth > 12 || reviewYear < 1) return 0;

            int todayIndex = today.Year * 12 + (today.Month - 1);
            int reviewIndex = reviewYear * 12 + (reviewMonth - 1);
            return Math.Max(0, reviewIndex - todayIndex - 1);
        }
    }

    /// <summary>The estimate of FR-11: who could still join and what that does to the player's probability.</summary>
    public sealed class PotentialEstimate
    {
        /// <summary>The AI offers that would change the contract: a company without an offer yet, or a CPU faster than the company's present offer. In the order of <c>potential</c>.</summary>
        public List<OfferInput> NewBidders = new List<OfferInput>();

        /// <summary>The probability that the draw picks the candidate now, as the game's chances give it.</summary>
        public double CurrentActual;

        /// <summary>The probability if every one of <see cref="NewBidders"/> also made its offer (<see cref="OddsMath.PessimisticProbability"/>); equal to <see cref="CurrentActual"/> when there are none.</summary>
        public double PessimisticActual;
    }

    /// <summary>
    /// Which of the possible AI offers matter, and the probability with all of them in (FR-11 item 2, AC-11.1). The game keeps only the best offer of each company (<c>GetBestOffers</c>), so an AI CPU
    /// that is not faster than what its company already offers changes nothing and is not listed.
    /// </summary>
    public static class PotentialBidders
    {
        /// <summary>
        /// The possible offers that would change the picture: those of a company that has no offer in <paramref name="best"/> and those faster than the offer the company has. Offers of the candidate's own
        /// company are never possible bidders. The result keeps the order of <paramref name="potential"/>.
        /// </summary>
        public static List<OfferInput> Relevant(IReadOnlyList<OfferInput> best, IReadOnlyList<OfferInput> potential, string candidateCompanyKey)
        {
            var result = new List<OfferInput>();
            if (potential == null) return result;

            string candidateKey = candidateCompanyKey ?? string.Empty;
            for (int i = 0; i < potential.Count; i++)
            {
                OfferInput bidder = potential[i];
                string key = bidder.CompanyKey ?? string.Empty;
                if (string.Equals(key, candidateKey, StringComparison.Ordinal)) continue;

                bool present = false;
                bool faster = true;
                if (best != null)
                {
                    for (int j = 0; j < best.Count; j++)
                    {
                        if (!string.Equals(best[j].CompanyKey ?? string.Empty, key, StringComparison.Ordinal)) continue;
                        present = true;
                        if (!(bidder.Ips > best[j].Ips)) faster = false;
                    }
                }

                if (!present || faster) result.Add(bidder);
            }
            return result;
        }

        /// <summary>
        /// The estimate: the relevant bidders and the probability of the candidate with them in. <paramref name="best"/> holds the best offer of each company including the candidate's CPU;
        /// <paramref name="currentActual"/> is the probability the panel shows now; <paramref name="candidateChanceMultiplier"/> is the factor mods put on the candidate's chance (1 for none).
        /// </summary>
        public static PotentialEstimate Estimate(
            IReadOnlyList<OfferInput> best, IReadOnlyList<OfferInput> potential, string candidateCompanyKey, int candidateCpuId, double currentActual, double candidateChanceMultiplier)
        {
            var estimate = new PotentialEstimate
            {
                NewBidders = Relevant(best, potential, candidateCompanyKey),
                CurrentActual = currentActual,
            };

            estimate.PessimisticActual = estimate.NewBidders.Count == 0
                ? currentActual
                : OddsMath.PessimisticProbability(best, estimate.NewBidders, candidateCompanyKey, candidateCpuId, candidateChanceMultiplier);
            return estimate;
        }
    }
}
