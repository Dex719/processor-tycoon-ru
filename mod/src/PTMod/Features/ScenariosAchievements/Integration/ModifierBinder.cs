using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// The records of <c>ModModifiers</c> of a run (TSK-019, FR-4 item 2, FR-11, D15): the modifiers of the scenario and of every chosen challenge are
    /// added once, when the run starts, through the standard channels of the core, owned by this feature; the ids of the records go into the section of
    /// the run, so a challenge that fails in free play can take its records away. The core keeps the records in the save and restores them on a load, so a
    /// loaded run never adds them again; a feature that is switched off keeps its records, which do not apply meanwhile (FR-12 item 8 of the core).
    /// </summary>
    internal static class ModifierBinder
    {
        /// <summary>
        /// Adds a record for every modifier and returns their ids (a modifier the core refuses is left out with a warning, the others are added). The
        /// <paramref name="reasonKey"/> is the title of the scenario or challenge: it is what the core's "explain" shows for the record.
        /// </summary>
        internal static IList<string> Apply(string reasonKey, IEnumerable<ModifierSpec> specs)
        {
            var ids = new List<string>();
            if (specs == null) return ids;

            foreach (ModifierSpec spec in specs)
            {
                if (spec == null || string.IsNullOrEmpty(spec.Channel)) continue;

                string id = ModModifiers.Add(ScenariosFeature.FeatureId, spec.Channel, ModifierScopes.Of(spec.AppliesTo), spec.Multiplier, 0, reasonKey);
                if (id == null)
                {
                    ModLog.Warn(ScenariosFeature.FeatureId, "modifiers: the core refused " + spec.Channel + " x" + spec.Multiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                                            + " for '" + reasonKey + "'");
                    continue;
                }
                ids.Add(id);
            }
            return ids;
        }

        /// <summary>Takes the records away (a challenge failed in free play). Returns how many were there.</summary>
        internal static int Remove(IList<string> entryIds)
        {
            int removed = 0;
            if (entryIds == null) return removed;

            foreach (string id in entryIds)
            {
                if (!string.IsNullOrEmpty(id) && ModModifiers.Remove(id)) removed++;
            }
            ModLog.Debug(ScenariosFeature.FeatureId, "modifiers: " + removed + " of " + entryIds.Count + " record(s) removed");
            return removed;
        }
    }
}
