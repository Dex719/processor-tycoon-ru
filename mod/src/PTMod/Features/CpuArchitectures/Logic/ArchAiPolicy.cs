using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The decisions of the AI companies about architectures (FR-7; design.md Component 9, "ИИ"): which family a company researches next, when it may
    /// start, when it must wait or pause, and which family it designs on. Pure and deterministic - no random numbers, ties are settled by the year and
    /// then the id, so the same snapshot always gives the same decision. <c>ArchitectureAi</c> reads the game into an <see cref="AiView"/> once a
    /// month and carries the <see cref="AiDecision"/> out.
    /// <para>
    /// MVP (stage 1), per company that is neither a foundry nor bankrupt:
    /// </para>
    /// <list type="number">
    /// <item><b>Active family</b> - the researched family with the largest weight of the preference record (weight above 0); of two of the same weight the
    /// newer one (FR-7 item 5). A new active family is only reported when it differs from the current one.</item>
    /// <item><b>Pause</b> - a running research pauses while the money of the company is negative (FR-7 item 4); nothing starts then.</item>
    /// <item><b>Start</b> - when no architecture is in the lab: the available, not researched family with the largest weight whose year plus the lag
    /// of the company has come (a tie goes to the older family, then to the smaller id). It starts when the money covers <c>reserveMonths</c> months of
    /// its cost times the cost modifier M4, and either the lab is free or the technology in it has more than <c>noInterruptTimeLeftDays</c> days
    /// left; otherwise the decision waits (<see cref="AiDecision.DeferInterrupt"/>). The funding is the entry of the difficulty table.</item>
    /// </list>
    /// <para>
    /// Full version (stage 2, FR-14; <see cref="DecideFull"/>) reacts to the market: the families are weighed by their attractiveness
    /// <code>
    /// att_a = w_a × Σ_s share_s × A_a[s] × fade_a(Y, s) × f_eco(E_a) × f_sup(S_a)
    /// </code>
    /// (<see cref="Attractiveness"/>; <c>share_s</c> are the shares of the company's sales by segment, <see cref="AiView.SegmentShare"/>). The company moves to the
    /// best family when <c>att_best ≥ switchMargin × att_active</c> (1.10) and the support of the best is at least <c>switchMinSupport</c> (35). The research
    /// that starts is the one with the largest <c>w_a × (1 + E_a)</c>. The support tiers: the active family is "normal" when the company has twelve months of
    /// that upkeep, otherwise "low"; so is every researched family the company likes at least as much as the one it designs on (<see cref="IsPursued"/>: it researched it to
    /// move to it, and a newcomer left at the 30 of its start, with no ecosystem, would lose to the supported incumbent and decay - the balance simulation of TSK-029 showed
    /// Texas Instruments, IBM and AMD researching ARM, RISC and x86-64 for nothing); a family with an ecosystem of 0.2 or more that the company likes less is "low" when it is
    /// the candidate of the move; the rest are "off".
    /// </para>
    /// </summary>
    public static class ArchAiPolicy
    {
        /// <summary>
        /// The preference record of a company: the one with its <c>UniqueId</c>, otherwise the record marked <c>default</c>; the built-in fallback
        /// (<see cref="AiPreferenceTable.Fallback"/>) when the list has no default record. Never null.
        /// </summary>
        public static AiPreference Match(IReadOnlyList<AiPreference> prefs, int uniqueId)
        {
            AiPreference fallback = null;
            if (prefs != null)
            {
                for (int i = 0; i < prefs.Count; i++)
                {
                    AiPreference pref = prefs[i];
                    if (pref == null) continue;
                    if (pref.IsDefault)
                    {
                        if (fallback == null) fallback = pref;
                        continue;
                    }
                    if (pref.UniqueId == uniqueId) return pref;
                }
            }
            return fallback ?? AiPreferenceTable.Fallback();
        }

        /// <summary>
        /// The family a company with these researched families designs on: the one with the largest weight (above 0), a tie going to the newer family
        /// (then to the smaller id). The default family when none of them has a weight. Never null for a catalog with the default family.
        /// </summary>
        public static string PickActive(ArchCatalog cat, AiPreference pref, ISet<string> researched)
        {
            if (cat == null) throw new ArgumentNullException("cat");
            if (pref == null) pref = AiPreferenceTable.Fallback();

            ArchFamily best = null;
            float bestWeight = 0f;
            foreach (ArchFamily family in cat.Families)
            {
                if (researched == null || !researched.Contains(family.Id)) continue;
                float weight = pref.WeightOf(family.Id);
                if (!(weight > 0f)) continue;
                if (best == null || IsBetterActive(weight, family, bestWeight, best))
                {
                    best = family;
                    bestWeight = weight;
                }
            }
            return best != null ? best.Id : ArchCatalog.DefaultId;
        }

        /// <summary>
        /// The same for the families of a company state: researched ones, and the ones researched at once (<c>proprietary</c>) when their requirements are met
        /// (<see cref="ArchResearchMath.CountsAsResearched"/>).
        /// </summary>
        public static string PickActive(ArchCatalog cat, AiPreference pref, IReadOnlyDictionary<string, FamilyState> families)
        {
            if (cat == null) throw new ArgumentNullException("cat");

            var researched = new HashSet<string>(StringComparer.Ordinal);
            foreach (ArchFamily family in cat.Families)
            {
                if (ArchResearchMath.CountsAsResearched(family, families)) researched.Add(family.Id);
            }
            return PickActive(cat, pref, researched);
        }

        /// <summary>
        /// The family a company researches next at stage 1: available (requirements researched, not researched itself), weight above 0, year plus lag
        /// not after <paramref name="year"/>; the largest weight wins, a tie goes to the older family, then to the smaller id (FR-7 item 1, AC-7.1).
        /// Null when there is none.
        /// </summary>
        public static ArchFamily PickCandidate(ArchCatalog cat, AiPreference pref, IReadOnlyDictionary<string, FamilyState> families, int year)
        {
            return PickCandidate(cat, pref, families, year, null);
        }

        /// <summary>
        /// The family a company researches next (FR-7 item 1; at stage 2 FR-14 item 4): the same rules with the weight of a family multiplied by
        /// <c>1 + E</c>, E being its share of the ecosystem (a family the map does not mention, or no map, counts as 0). A larger value wins, a tie goes to the older
        /// family, then to the smaller id.
        /// </summary>
        public static ArchFamily PickCandidate(ArchCatalog cat, AiPreference pref, IReadOnlyDictionary<string, FamilyState> families, int year,
                                               IReadOnlyDictionary<string, float> ecosystem)
        {
            if (cat == null) throw new ArgumentNullException("cat");
            if (pref == null) pref = AiPreferenceTable.Fallback();

            ArchFamily best = null;
            float bestWeight = 0f;
            foreach (ArchFamily family in cat.Families)
            {
                float weight = pref.WeightOf(family.Id);
                if (!(weight > 0f) || !ArchSeedPolicy.ExistsBy(family, pref.LagYears, year)) continue;
                if (ArchResearchMath.CountsAsResearched(family, families) || !ArchResearchMath.IsAvailable(family, families)) continue;

                weight *= 1f + Math.Max(0f, EcosystemOf(ecosystem, family.Id));
                if (best == null || IsBetterCandidate(weight, family, bestWeight, best))
                {
                    best = family;
                    bestWeight = weight;
                }
            }
            return best;
        }

        /// <summary>The stage 1 decision of one company this month (see the class description). Never null; an empty decision for a foundry, a bankrupt or an unknown company.</summary>
        public static AiDecision DecideMvp(AiView ai, AiPreference pref, ArchCatalog cat, AiConfig cfg)
        {
            var decision = new AiDecision();
            if (ai == null || cat == null || ai.IsFoundry || ai.IsBankrupt) return decision;
            if (pref == null) pref = AiPreferenceTable.Fallback();
            if (cfg == null) cfg = new AiConfig();

            CompanyArchState state = ai.State;
            IReadOnlyDictionary<string, FamilyState> families = state == null ? null : state.Families;

            // 1. The active family follows the weights of what the company has researched.
            string active = PickActive(cat, pref, families);
            if (!string.Equals(active, state == null ? null : state.Active, StringComparison.Ordinal)) decision.NewActive = active;

            // 2-3. The research: pause, wait or start.
            PlanResearch(decision, ai, pref, cat, cfg, null);
            return decision;
        }

        /// <summary>
        /// The stage 2 decision of one company this month (FR-14; see the class description): the research as at stage 1 with the weights times <c>1 + E</c>, the
        /// active family by attractiveness, and the support tiers. <paramref name="ecosystem"/> is the share of every family in the sales (null: not measured, the
        /// factors are ×1); <paramref name="rating"/> gives the shape of <c>f_eco</c> and <c>f_sup</c>, <paramref name="support"/> the upkeep for the "normal" tier.
        /// Never null; an empty decision for a foundry, a bankrupt or an unknown company. Deterministic: no random numbers, ties by year and id.
        /// </summary>
        public static AiDecision DecideFull(AiView ai, AiPreference pref, ArchCatalog cat, AiConfig cfg, IReadOnlyDictionary<string, float> ecosystem,
                                            RatingConfig rating, SupportConfig support = null)
        {
            var decision = new AiDecision();
            if (ai == null || cat == null || ai.IsFoundry || ai.IsBankrupt) return decision;
            if (pref == null) pref = AiPreferenceTable.Fallback();
            if (cfg == null) cfg = new AiConfig();
            if (rating == null) rating = new RatingConfig();
            if (support == null) support = new SupportConfig();

            string newActive;
            string candidate;
            PlanActive(ai, pref, cat, cfg, ecosystem, rating, out newActive, out candidate);
            decision.NewActive = newActive;

            PlanResearch(decision, ai, pref, cat, cfg, ecosystem);

            CompanyArchState state = ai.State;
            string active = newActive ?? (state == null ? null : state.Active);
            decision.Tiers = PlanTiers(ai, pref, cat, cfg, ecosystem, support, active, candidate);
            return decision;
        }

        /// <summary>
        /// The attractiveness of a family for a company (FR-14 item 1): <c>w × Σ_s share_s × A[s] × fade(Y, s) × f_eco(E) × f_sup(S)</c>. The shares of the segments
        /// come in the order of <see cref="Segment"/> (an unusable array counts as a quarter each), <paramref name="ecosystem"/> is the share of the family in the sales
        /// (negative: not measured, ×1), <paramref name="support"/> the company's own support for the family. 0 for a missing family and a weight that is not above 0.
        /// </summary>
        public static float Attractiveness(ArchFamily family, float weight, float[] segmentShare, int year, float ecosystem, float support, RatingConfig rating)
        {
            if (family == null || !(weight > 0f)) return 0f;
            if (rating == null) rating = new RatingConfig();
            float[] shares = SegmentShares.IsUsable(segmentShare) ? segmentShare : SegmentShares.Equal();

            double sum = 0;
            double total = 0;
            for (int s = 0; s < SegmentShares.Count; s++)
            {
                var segment = (Segment)s;
                sum += shares[s] * (double)ArchRatingMath.Affinity(family, segment) * ArchRatingMath.Fade(family.Fade, year, segment);
                total += shares[s];
            }
            if (total > 0) sum /= total;   // the producers normalise; a sum that is not 1 must not weigh a family more

            double att = weight * sum * ArchRatingMath.EcosystemFactor(ecosystem, rating) * ArchRatingMath.SupportFactor(support, rating);
            return double.IsNaN(att) || double.IsInfinity(att) || att < 0 ? 0f : (float)att;
        }

        // ---- the parts of the decision ----

        /// <summary>
        /// The research part, shared by both stages (FR-7 items 1-4): a running research only pauses when the money is negative; with none running, the best candidate
        /// starts if the reserve is there and the technology in the lab may be interrupted. Fills <c>Pause</c>, <c>DeferInterrupt</c>, <c>StartResearch</c>, <c>Funding</c>.
        /// </summary>
        private static void PlanResearch(AiDecision decision, AiView ai, AiPreference pref, ArchCatalog cat, AiConfig cfg, IReadOnlyDictionary<string, float> ecosystem)
        {
            CompanyArchState state = ai.State;
            IReadOnlyDictionary<string, FamilyState> families = state == null ? null : state.Families;

            // A research in the lab: it only pauses when the money is negative; nothing else starts meanwhile.
            if (state != null && state.CurrentResearch != null)
            {
                if (ai.Money < 0f) decision.Pause = true;
                return;
            }
            if (ai.Money < 0f) return;

            // Nothing is researched: the best candidate, if the money and the lab allow.
            ArchFamily candidate = PickCandidate(cat, pref, families, ai.Year, ecosystem);
            if (candidate == null) return;

            if (ai.Money < Reserve(cfg.reserveMonths, candidate.Research, ai.CostModifier)) return;
            if (!LabRules.CanInterruptForAi(ai.TechnologyDaysLeft, cfg.noInterruptTimeLeftDays))
            {
                decision.DeferInterrupt = true;
                return;
            }

            decision.StartResearch = candidate.Id;
            decision.Funding = LabRules.AiFunding(ai.Difficulty, cfg.fundingByDifficulty);
        }

        /// <summary>
        /// The active family of stage 2 (FR-14 items 1-2). <paramref name="newActive"/> is the family to move to (null: stay); <paramref name="candidate"/> is the
        /// family the company would move to if its support allowed (it passes the margin, but its support is below <c>switchMinSupport</c>), null otherwise.
        /// A company whose active family is unknown or not researched takes the most attractive one at once - it has to design on something it has.
        /// </summary>
        private static void PlanActive(AiView ai, AiPreference pref, ArchCatalog cat, AiConfig cfg, IReadOnlyDictionary<string, float> ecosystem, RatingConfig rating,
                                       out string newActive, out string candidate)
        {
            newActive = null;
            candidate = null;

            CompanyArchState state = ai.State;
            IReadOnlyDictionary<string, FamilyState> families = state == null ? null : state.Families;
            string activeId = state == null ? null : state.Active;
            ArchFamily current = activeId == null ? null : cat.Get(activeId);
            bool currentValid = current != null && ArchResearchMath.CountsAsResearched(current, families);

            ArchFamily best = null;
            float bestAtt = 0f;
            float currentAtt = 0f;
            foreach (ArchFamily family in cat.Families)
            {
                if (!ArchResearchMath.CountsAsResearched(family, families)) continue;
                float weight = pref.WeightOf(family.Id);
                if (!(weight > 0f)) continue;

                float att = Attractiveness(family, weight, ai.SegmentShare, ai.Year, EcosystemOf(ecosystem, family.Id), SupportOf(state, family.Id), rating);
                if (currentValid && string.Equals(family.Id, current.Id, StringComparison.Ordinal)) currentAtt = att;
                if (best == null || IsBetterActive(att, family, bestAtt, best))
                {
                    best = family;
                    bestAtt = att;
                }
            }

            if (best == null)
            {
                // Nothing the company has is liked: the default family, like at stage 1.
                if (!string.Equals(activeId, ArchCatalog.DefaultId, StringComparison.Ordinal)) newActive = ArchCatalog.DefaultId;
                return;
            }

            if (!currentValid)
            {
                if (!string.Equals(activeId, best.Id, StringComparison.Ordinal)) newActive = best.Id;
                return;
            }
            if (string.Equals(best.Id, current.Id, StringComparison.Ordinal)) return;

            if (bestAtt > 0f && bestAtt >= cfg.switchMargin * currentAtt)
            {
                if (SupportOf(state, best.Id) >= cfg.switchMinSupport) newActive = best.Id;
                else candidate = best.Id;
            }
        }

        /// <summary>
        /// The support tiers of stage 2 (FR-14 item 3), only those that differ from the tier the company has now: the active family "normal" when the money covers
        /// <c>normalTierReserveMonths</c> months of that upkeep, otherwise "low"; the same for every family the company likes at least as much as the one it designs on now
        /// (<see cref="IsPursued"/>, the condition of FR-7 item 5: it researched the family to move to it, and without the funding a freshly researched family, which starts at 30
        /// with no ecosystem, would lose to the supported incumbent for ever; the move itself is still decided by the attractiveness with its margin and the support of 35);
        /// the candidate the company likes less "low" when its ecosystem is at least <c>candidateEcoForLowTier</c>; every other researched family "off". Null when nothing changes.
        /// </summary>
        private static Dictionary<string, SupportTier> PlanTiers(AiView ai, AiPreference pref, ArchCatalog cat, AiConfig cfg, IReadOnlyDictionary<string, float> ecosystem,
                                                                  SupportConfig support, string activeId, string candidateId)
        {
            CompanyArchState state = ai.State;
            if (state == null) return null;

            Dictionary<string, SupportTier> tiers = null;
            foreach (ArchFamily family in cat.Families)
            {
                FamilyState own = state.GetFamily(family.Id);
                if (own == null || !own.Researched) continue;

                SupportTier want;
                if (string.Equals(family.Id, activeId, StringComparison.Ordinal))
                {
                    want = NormalIfAffordable(ai, cfg, family, support);
                }
                else if (IsPursued(pref, family.Id, activeId))
                {
                    // A family the company researched and likes at least as much as the one it designs on: it is brought up to the level of the active one, so that the
                    // comparison of the attractiveness is between two supported families (a newcomer left at 30 would lose to the incumbent and never be given a chance).
                    want = NormalIfAffordable(ai, cfg, family, support);
                }
                else if (string.Equals(family.Id, candidateId, StringComparison.Ordinal) && EcosystemOf(ecosystem, family.Id) >= cfg.candidateEcoForLowTier)
                {
                    want = SupportTier.Low;
                }
                else
                {
                    want = SupportTier.Off;
                }

                if (own.Tier == want) continue;
                if (tiers == null) tiers = new Dictionary<string, SupportTier>(StringComparer.Ordinal);
                tiers[family.Id] = want;
            }
            return tiers;
        }

        /// <summary>"Normal" when the money covers <c>normalTierReserveMonths</c> months of that upkeep of the family, otherwise "low".</summary>
        private static SupportTier NormalIfAffordable(AiView ai, AiConfig cfg, ArchFamily family, SupportConfig support)
        {
            float upkeep = SupportMath.MonthlyUpkeep(family.Support, SupportTier.Normal, ai.Year, ai.Inflation, support);
            return ai.Money >= Math.Max(0, cfg.normalTierReserveMonths) * upkeep ? SupportTier.Normal : SupportTier.Low;
        }

        /// <summary>
        /// The company pursues a researched family it is not designing on: it weighs at least as much for it as for the active family (and above 0), the condition of FR-7 item 5.
        /// It researched the family to move to it; the move itself is decided by the attractiveness and the support (<see cref="DecideFull"/>).
        /// </summary>
        internal static bool IsPursued(AiPreference pref, string familyId, string activeId)
        {
            float weight = pref.WeightOf(familyId);
            return weight > 0f && weight >= pref.WeightOf(activeId);
        }

        /// <summary>The ecosystem share of a family for the policy: the share in the map, 0 for a family it does not mention, -1 (not measured) without a map.</summary>
        private static float EcosystemOf(IReadOnlyDictionary<string, float> ecosystem, string familyId)
        {
            return EcosystemMath.ShareOf(ecosystem, familyId);
        }

        /// <summary>The support the company has for a family (0 when it keeps no state for it).</summary>
        private static float SupportOf(CompanyArchState state, string familyId)
        {
            FamilyState own = state == null ? null : state.GetFamily(familyId);
            return own == null ? 0f : own.Support;
        }

        /// <summary>The money a company must have to start a research (FR-7 item 1): <c>reserveMonths × monthlyCost × M4</c>.</summary>
        public static float Reserve(int reserveMonths, ResearchDef research, float costModifier)
        {
            if (research == null) return 0f;
            float modifier = float.IsNaN(costModifier) || float.IsInfinity(costModifier) || costModifier < 0f ? 1f : costModifier;
            return Math.Max(0, reserveMonths) * research.MonthlyCost * modifier;
        }

        /// <summary>The active family: the larger weight, then the newer year, then the smaller id.</summary>
        internal static bool IsBetterActive(float weight, ArchFamily family, float bestWeight, ArchFamily best)
        {
            if (weight > bestWeight) return true;
            if (weight < bestWeight) return false;
            if (best == null) return true;
            if (family.Year != best.Year) return family.Year > best.Year;
            return string.CompareOrdinal(family.Id, best.Id) < 0;
        }

        /// <summary>The family to research: the larger weight, then the older year, then the smaller id.</summary>
        internal static bool IsBetterCandidate(float weight, ArchFamily family, float bestWeight, ArchFamily best)
        {
            if (weight > bestWeight) return true;
            if (weight < bestWeight) return false;
            if (best == null) return true;
            if (family.Year != best.Year) return family.Year < best.Year;
            return string.CompareOrdinal(family.Id, best.Id) < 0;
        }
    }
}
