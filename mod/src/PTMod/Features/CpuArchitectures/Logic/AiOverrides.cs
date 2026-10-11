using System;
using System.Collections.Generic;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>What <see cref="AiOverrides.Set"/> did.</summary>
    public enum AiOverrideResult
    {
        /// <summary>The weight was put in place of the one of the record (or the earlier override).</summary>
        Set = 0,

        /// <summary>The override of the company for the family was taken away (the weight asked for was null); there was one.</summary>
        Removed = 1,

        /// <summary>Nothing to take away: the company had no override for the family.</summary>
        NothingToRemove = 2,

        /// <summary>The company id is not an id, or the family id is not a family id.</summary>
        BadId = 3,

        /// <summary>The weight is not a number.</summary>
        BadWeight = 4,

        /// <summary>The list is full (<c>ArchRegistry.MaxOverrides</c>).</summary>
        Full = 5,
    }

    /// <summary>
    /// The weights that another feature put over the preference record of an AI company (<c>ArchitectureApi.SetAiWeightOverride</c>, FR-14; <c>rival-personalities</c>
    /// makes a company favour an architecture). The overrides live in the save section (<c>aiOverrides</c>: company, family, weight). The AI decides on the record
    /// with the overrides applied (<see cref="Apply"/>), so the choice of the family to research and of the active family both follow them. Pure.
    /// </summary>
    public static class AiOverrides
    {
        /// <summary>
        /// Sets (or, for a null weight, takes away) the override of a company for a family. A weight outside 0..<see cref="AiPreference.MaxWeight"/> is held to it. Returns
        /// what happened; the list is changed only for <see cref="AiOverrideResult.Set"/> and <see cref="AiOverrideResult.Removed"/>.
        /// </summary>
        public static AiOverrideResult Set(List<AiOverrideSave> overrides, int companyId, string familyId, float? weight, int limit)
        {
            if (overrides == null) throw new ArgumentNullException("overrides");
            if (companyId < 0 || !ArchIds.IsFamilyId(familyId)) return AiOverrideResult.BadId;

            int index = overrides.FindIndex(o => o != null && o.company == companyId && o.arch == familyId);
            if (!weight.HasValue)
            {
                if (index < 0) return AiOverrideResult.NothingToRemove;
                overrides.RemoveAt(index);
                return AiOverrideResult.Removed;
            }

            float value = weight.Value;
            if (float.IsNaN(value) || float.IsInfinity(value)) return AiOverrideResult.BadWeight;
            value = Math.Max(0f, Math.Min(AiPreference.MaxWeight, value));

            if (index >= 0)
            {
                overrides[index].weight = value;
                return AiOverrideResult.Set;
            }
            if (overrides.Count >= limit) return AiOverrideResult.Full;

            overrides.Add(new AiOverrideSave { company = companyId, arch = familyId, weight = value });
            return AiOverrideResult.Set;
        }

        /// <summary>The override of a company for a family, or null when there is none.</summary>
        public static float? Get(IReadOnlyList<AiOverrideSave> overrides, int companyId, string familyId)
        {
            if (overrides == null) return null;
            for (int i = 0; i < overrides.Count; i++)
            {
                AiOverrideSave o = overrides[i];
                if (o != null && o.company == companyId && o.arch == familyId) return o.weight;
            }
            return null;
        }

        /// <summary>
        /// The record the company decides on: <paramref name="baseline"/> itself when the company has no override, otherwise a copy of it with the overridden weights in
        /// place of the record's (a family the record does not mention gets the override as its weight). The record of the table is never changed.
        /// </summary>
        public static AiPreference Apply(AiPreference baseline, int companyId, IReadOnlyList<AiOverrideSave> overrides)
        {
            if (baseline == null || overrides == null || overrides.Count == 0) return baseline;

            AiPreference copy = null;
            for (int i = 0; i < overrides.Count; i++)
            {
                AiOverrideSave o = overrides[i];
                if (o == null || o.company != companyId || !ArchIds.IsFamilyId(o.arch)) continue;
                if (float.IsNaN(o.weight) || float.IsInfinity(o.weight)) continue;

                if (copy == null)
                {
                    copy = new AiPreference { Id = baseline.Id, UniqueId = baseline.UniqueId, IsDefault = baseline.IsDefault, LagYears = baseline.LagYears };
                    foreach (KeyValuePair<string, float> entry in baseline.Weights) copy.Weights[entry.Key] = entry.Value;
                }
                copy.Weights[o.arch] = Math.Max(0f, Math.Min(AiPreference.MaxWeight, o.weight));
            }
            return copy ?? baseline;
        }
    }
}
