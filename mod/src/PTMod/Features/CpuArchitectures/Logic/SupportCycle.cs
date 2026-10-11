using System;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>What <see cref="SupportCycle.RunMonth"/> did for one company in one month.</summary>
    public sealed class SupportMonthResult
    {
        /// <summary>The money to charge for the month (the upkeep of all the families that were paid for); 0 when nothing is paid.</summary>
        public float Upkeep;

        /// <summary>How many families took part (researched and known to the catalog).</summary>
        public int Families;

        /// <summary>How many families whose tier the company could not pay for and that decayed as "off" this month.</summary>
        public int Unfunded;

        /// <summary>Some support level changed (the cached rating of the company's CPUs is stale).</summary>
        public bool Changed;
    }

    /// <summary>The outcome of <see cref="SupportCycle.SetTier"/>.</summary>
    public enum SupportSetResult
    {
        /// <summary>The tier was changed; it counts from the next monthly update.</summary>
        Set = 0,

        /// <summary>The tier was the one asked for already.</summary>
        Unchanged = 1,

        /// <summary>The company has no state, or no state for the family.</summary>
        NoState = 2,

        /// <summary>The family is not in the catalog.</summary>
        UnknownFamily = 3,

        /// <summary>The company has not researched the family: there is nothing to support.</summary>
        NotResearched = 4,

        /// <summary>The company is in default: the support stays off until it is out (FR-10 item 6).</summary>
        InDefault = 5,

        /// <summary>The tier is not one of the four.</summary>
        BadTier = 6,

        /// <summary>The feature is off or fast-forwarding, the support is not played at this stage, or the part of the self-check is off (set by the service, not by the cycle).</summary>
        Unavailable = 7,
    }

    /// <summary>
    /// One month of the software support of one company (FR-10, stage 2; <c>SupportService.OnMonth</c> without the game): every researched family gets its
    /// upkeep and its support level moves towards the target of its tier (<see cref="SupportMath"/>). Also the rules of changing a tier: a family the
    /// company has not researched has none, a company in default cannot pay for any (FR-10 item 6). Pure: the callers pass the date, the inflation and the
    /// money the company may spend; the game's money is moved by the service.
    /// <para>
    /// Families are walked in the order of the catalog, so the result does not depend on how the dictionary of the state happens to be ordered.
    /// </para>
    /// </summary>
    public static class SupportCycle
    {
        /// <summary>The tiers in the order the panel lists them; the index is the value of <see cref="SupportTier"/>.</summary>
        public static readonly SupportTier[] Tiers = { SupportTier.Off, SupportTier.Low, SupportTier.Normal, SupportTier.High };

        /// <summary>The tier is one of the four.</summary>
        public static bool IsTier(SupportTier tier)
        {
            return (int)tier >= 0 && (int)tier < Tiers.Length;
        }

        /// <summary>
        /// One month for one company: the upkeep of every researched family at its tier is added up, then its support moves. A tier whose upkeep is more
        /// than what is left of <paramref name="budget"/> is not paid and works as "off" this month (the tier stays as the company set it): the player
        /// passes <see cref="float.PositiveInfinity"/> (he pays whatever his money is, FR-10 item 3), an AI company the money it has.
        /// </summary>
        public static SupportMonthResult RunMonth(CompanyArchState state, ArchCatalog catalog, SupportConfig config, int year, float inflation, float budget)
        {
            var result = new SupportMonthResult();
            if (state == null || catalog == null) return result;
            if (config == null) config = new SupportConfig();

            float left = float.IsNaN(budget) ? 0f : Math.Max(0f, budget);
            foreach (ArchFamily family in catalog.Families)
            {
                FamilyState own = state.GetFamily(family.Id);
                if (own == null || !own.Researched) continue;
                result.Families++;

                SupportTier tier = IsTier(own.Tier) ? own.Tier : SupportTier.Off;
                float upkeep = SupportMath.MonthlyUpkeep(family.Support, tier, year, inflation, config);
                if (upkeep > left)
                {
                    tier = SupportTier.Off;
                    upkeep = 0f;
                    result.Unfunded++;
                }
                else if (upkeep > 0f)
                {
                    result.Upkeep += upkeep;
                    if (!float.IsPositiveInfinity(left)) left -= upkeep;
                }

                float after = SupportMath.Next(own.Support, tier, family.Support, config);
                if (after != own.Support) result.Changed = true;
                own.Support = after;
            }
            return result;
        }

        /// <summary>Sets the tier of every family of the company to "off" (FR-10 item 6, the player in default). Returns how many tiers were not "off" before.</summary>
        public static int ForceOff(CompanyArchState state)
        {
            if (state == null) return 0;

            int changed = 0;
            foreach (FamilyState family in state.Families.Values)
            {
                if (family.Tier == SupportTier.Off) continue;
                family.Tier = SupportTier.Off;
                changed++;
            }
            return changed;
        }

        /// <summary>
        /// The player (or the AI) sets the tier of a family (FR-10 item 5). Only a researched family of the catalog has a tier; a company in default can set
        /// "off" only. The change counts from the next monthly update (the upkeep and the level are computed then); the cost the panel shows follows the tier at once.
        /// </summary>
        public static SupportSetResult SetTier(CompanyArchState state, ArchCatalog catalog, string familyId, SupportTier tier, bool inDefault)
        {
            if (!IsTier(tier)) return SupportSetResult.BadTier;
            if (state == null) return SupportSetResult.NoState;
            if (catalog == null || familyId == null || catalog.Get(familyId) == null) return SupportSetResult.UnknownFamily;

            FamilyState family = state.GetFamily(familyId);
            if (family == null || !family.Researched) return SupportSetResult.NotResearched;
            if (inDefault && tier != SupportTier.Off) return SupportSetResult.InDefault;
            if (family.Tier == tier) return SupportSetResult.Unchanged;

            family.Tier = tier;
            return SupportSetResult.Set;
        }
    }
}
