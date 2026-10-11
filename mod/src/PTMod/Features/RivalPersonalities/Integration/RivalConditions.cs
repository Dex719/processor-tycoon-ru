using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The answers of the game to the predicates of the feature (<see cref="IRivalFacts"/>), read from the books of the running feature. No state of its own.
    /// A key that is not an AI company, or a company without a character, is unknown (null), so the predicate is false; before the feature has anything
    /// (no game yet) every answer is unknown.
    /// </summary>
    internal sealed class RivalPredicateFacts : IRivalFacts
    {
        public float? AttitudeOf(string companyKey)
        {
            int uid;
            RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
            if (feature == null || !CompanyKey.TryGetAiId(companyKey, out uid) || !feature.Attitudes.IsKnown(uid)) return null;
            return feature.Attitudes.Get(uid);
        }

        public int? ReactionsExecutedBy(string companyKey)
        {
            int uid;
            RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
            if (feature == null || !CompanyKey.TryGetAiId(companyKey, out uid) || !feature.Attitudes.IsKnown(uid)) return null;
            return feature.Reactions.ExecutedBy(uid);
        }

        public int ReactionsExecutedTotal()
        {
            RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
            return feature == null ? 0 : feature.Reactions.ExecutedTotal();
        }
    }

    /// <summary>
    /// Registers the predicates of the feature with <c>ModConditions</c> (FR-13 item 4, TSK-020): <c>rival.attitudeAtMost</c>, <c>rival.attitudeAtLeast</c>
    /// and <c>rival.reactionsExecuted</c>. A predicate of a feature is named <c>&lt;prefix&gt;.&lt;name&gt;</c>, and the prefix of this feature is <c>rival</c> (the
    /// table <c>prefixes</c> of <c>core.json</c>), so the core accepts the names; it wraps each registered condition so that it is false while the
    /// feature is not active and an exception it throws is counted against the feature. The meaning and the parameters are <see cref="RivalPredicates"/>'s
    /// (pure, tested); the facts come from <see cref="RivalPredicateFacts"/>. Registered once, from <c>Register</c> of the feature.
    /// </summary>
    internal static class RivalConditions
    {
        private static readonly RivalPredicateFacts Facts = new RivalPredicateFacts();

        private static bool _registered;

        /// <summary>
        /// Registers the three predicates (a second call does nothing: the catalog of predicates is shared and a name taken is a warning). Returns how many
        /// the core accepted; a refusal is the core's own warning in the log and the predicate stays unknown to the content that names it.
        /// </summary>
        public static int Register()
        {
            if (_registered) return 0;
            _registered = true;

            int accepted = 0;
            IList<KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>> factories = RivalPredicates.CreateFactories(Facts);
            foreach (KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>> pair in factories)
            {
                if (ModConditions.Register(pair.Key, RivalPersonalitiesFeature.FeatureId, pair.Value)) accepted++;
            }

            if (accepted != factories.Count)
                ModLog.Warn(RivalPersonalitiesFeature.FeatureId, "predicates: " + accepted + " of " + factories.Count + " rival.* predicates were accepted by the core");
            else
                ModLog.Debug(RivalPersonalitiesFeature.FeatureId, "predicates: " + accepted + " rival.* predicates registered");
            return accepted;
        }
    }
}
