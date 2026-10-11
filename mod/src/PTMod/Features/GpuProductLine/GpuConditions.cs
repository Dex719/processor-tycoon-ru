using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// Registers the predicates of the feature with <c>ModConditions</c> (FR-18 item 4): <c>gpu.divisionFounded</c>, <c>gpu.hasProductOnMarket</c>, <c>gpu.marketShareAtLeast</c> and
    /// <c>gpu.researched</c>. A predicate of a feature is named <c>&lt;prefix&gt;.&lt;name&gt;</c>; the prefix of this feature is <c>gpu</c> (the table <c>prefixes</c> of <c>core.json</c>), so the
    /// core accepts the names (<c>ModIds.IsPredicateNameAllowed</c>) and refuses them to any other feature. The core wraps each registered condition: it is false while the feature
    /// is not active and an exception it throws is counted against the feature. What a predicate means is <see cref="GpuPredicates"/>'s (pure, tested); the answers come from the
    /// services of the running feature through <see cref="GpuApiService"/>.
    /// </summary>
    internal static class GpuConditions
    {
        private static bool _registered;

        /// <summary>The services behind the predicates: those of the feature of this session; null until it has registered.</summary>
        private static IGpuQueries Queries()
        {
            GpuFeature feature = GpuFeature.Instance;
            return feature == null ? null : feature.Api;
        }

        /// <summary>
        /// Registers the four predicates (a second call does nothing: the catalog of predicates is shared and a name taken is a warning). Returns how many the core accepted; a refusal is
        /// the core's own warning in the log and the predicate stays unknown to the conditions that name it.
        /// </summary>
        internal static int Register()
        {
            if (_registered) return 0;
            _registered = true;

            int accepted = 0;
            IList<KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>> factories = GpuPredicates.CreateFactories(Queries);
            foreach (KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>> pair in factories)
            {
                if (ModConditions.Register(pair.Key, GpuFeature.FeatureId, pair.Value)) accepted++;
            }

            if (accepted != factories.Count)
                ModLog.Warn(GpuFeature.FeatureId, "predicates: " + accepted + " of " + factories.Count + " gpu.* predicates were accepted by the core");
            else
                ModLog.Debug(GpuFeature.FeatureId, "predicates: " + accepted + " gpu.* predicates registered");
            return accepted;
        }
    }
}
