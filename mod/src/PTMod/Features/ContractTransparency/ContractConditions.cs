using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// Registers the predicates of the feature with <c>ModConditions</c> (FR-14 item 3): <c>ct.offersSent</c> and <c>ct.tendersWon</c>, each with <c>atLeast</c>. A predicate of a feature is named
    /// <c>&lt;prefix&gt;.&lt;name&gt;</c>; the prefix of this feature is <c>ct</c> (the table <c>prefixes</c> of <c>core.json</c>), so the core accepts the names (<c>ModIds.IsPredicateNameAllowed</c>)
    /// and refuses them to any other feature. The core wraps each registered condition: it is false while the feature is not active and an exception it throws is counted against the feature. The
    /// meaning and the parameters are <see cref="ContractPredicates"/>'s (pure, tested); the numbers come from the log of the running feature.
    /// </summary>
    internal static class ContractConditions
    {
        /// <summary>The counters of the log of the running feature; 0 before the feature has a log (no game yet), so every predicate with a positive <c>atLeast</c> is false then.</summary>
        private sealed class LiveCounters : IContractCounters
        {
            public int OffersSent
            {
                get { return Model == null ? 0 : Model.OffersSent; }
            }

            public int TendersWon
            {
                get { return Model == null ? 0 : Model.TendersWon; }
            }

            private static ContractLogModel Model
            {
                get
                {
                    ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
                    return feature == null || feature.Log == null ? null : feature.Log.Model;
                }
            }
        }

        private static readonly LiveCounters Counters = new LiveCounters();

        private static bool _registered;

        /// <summary>
        /// Registers the two predicates (a second call does nothing: the catalog of predicates is shared and a name taken is a warning). Returns how many the core accepted; a refusal is the core's own
        /// warning in the log and the predicate stays unknown to the conditions that name it.
        /// </summary>
        internal static int Register()
        {
            if (_registered) return 0;
            _registered = true;

            int accepted = 0;
            IList<KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>> factories = ContractPredicates.CreateFactories(Counters);
            foreach (KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>> pair in factories)
            {
                if (ModConditions.Register(pair.Key, ContractTransparencyFeature.FeatureId, pair.Value)) accepted++;
            }

            if (accepted != factories.Count)
                ModLog.Warn(ContractTransparencyFeature.FeatureId, "predicates: " + accepted + " of " + factories.Count + " ct.* predicates were accepted by the core");
            else
                ModLog.Debug(ContractTransparencyFeature.FeatureId, "predicates: " + accepted + " ct.* predicates registered");
            return accepted;
        }
    }
}
