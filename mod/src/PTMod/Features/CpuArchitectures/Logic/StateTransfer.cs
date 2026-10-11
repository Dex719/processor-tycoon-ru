using System;
using System.Collections.Generic;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The architectures of an absorbed company going to its buyer (design.md, "Migration and Compatibility", <c>rival-acquisitions</c>): what <c>ArchitectureApi.TransferCompanyState</c>
    /// does to the two states. The buyer gets every family the absorbed company has researched - what one team knows, the merged company knows - and with the best support of the two
    /// for it; whatever the buyer already has stays his. The research the absorbed company had not finished is not transferred (the lab belongs to the company, edge cases of the spec),
    /// and neither are its active family or its support tiers: the buyer keeps designing on what he designs on and pays for the support he chose. The bindings of the CPUs are by
    /// <c>Cpu.SaveID</c> and do not change. Pure; plain data in, plain data out.
    /// </summary>
    public static class StateTransfer
    {
        /// <summary>
        /// Merges <paramref name="from"/> into <paramref name="to"/>. With <paramref name="mergeKeepBest"/> the support of a family both have becomes the larger of the two; without it the
        /// buyer's own level stays. Families only the absorbed company has researched are added to the buyer researched (progress 1, the date of their research, the level of the absorbed
        /// company, the tier "off"); a family the buyer was still researching is finished by the merge (the lab is given back by the daily research tick). Returns how many families of the buyer changed. Nothing happens for a missing state,
        /// the same state twice, or a family the catalog does not know (its entry is carried over as it is: a user's file may bring it back).
        /// </summary>
        public static int Merge(CompanyArchState from, CompanyArchState to, bool mergeKeepBest)
        {
            if (from == null || to == null || ReferenceEquals(from, to)) return 0;

            // The families are walked in the order of their ids, so the result never depends on how the dictionary happens to be ordered.
            var ids = new List<string>(from.Families.Keys);
            ids.Sort(StringComparer.Ordinal);

            int changed = 0;
            foreach (string id in ids)
            {
                FamilyState source = from.Families[id];
                if (source == null || !source.Researched) continue;

                FamilyState own = to.GetFamily(id);
                if (own == null || !own.Researched)
                {
                    FamilyState added = own ?? to.GetOrAddFamily(id);
                    added.Researched = true;
                    added.Progress = 1f;
                    added.ResearchedYear = source.ResearchedYear;
                    added.ResearchedMonth = source.ResearchedMonth;
                    added.Support = SupportMath.Clamp(own == null ? source.Support : Math.Max(source.Support, own.Support));
                    added.Tier = SupportTier.Off;
                    changed++;   // a research of the buyer on this family is not touched here: the daily tick sees it researched and gives the lab back
                    continue;
                }

                if (mergeKeepBest && source.Support > own.Support)
                {
                    own.Support = SupportMath.Clamp(source.Support);
                    changed++;
                }
            }

            if (to.Active == null && from.Active != null && to.IsResearched(from.Active)) to.Active = from.Active;
            return changed;
        }
    }
}
