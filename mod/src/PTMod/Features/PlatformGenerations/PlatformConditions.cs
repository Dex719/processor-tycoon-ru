using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The answers of the game to the predicates of the feature (<see cref="IPlatformFacts"/>), read from the chronology and the installed base of the
    /// running feature. No state of its own. Before the feature has anything (no game yet) every answer is "no".
    /// </summary>
    internal sealed class PlatformFacts : IPlatformFacts
    {
        public bool IsLaunched(string genId)
        {
            PlatformGenerationsFeature feature = PlatformGenerationsFeature.Instance;
            if (feature == null || string.IsNullOrEmpty(genId)) return false;

            GameDate date;
            string by;
            return feature.Timeline.IsLaunched(genId, out date, out by);
        }

        public bool IsEarlyAdopter(string companyKey, string genId)
        {
            PlatformGenerationsFeature feature = PlatformGenerationsFeature.Instance;
            if (feature == null || string.IsNullOrEmpty(companyKey) || string.IsNullOrEmpty(genId)) return false;

            GameDate since;
            return feature.Timeline.IsEarlyAdopter(companyKey, genId, out since);
        }

        public double BestSocketShare(string companyKey)
        {
            PlatformGenerationsFeature feature = PlatformGenerationsFeature.Instance;
            return feature == null ? 0 : feature.InstalledBase.BestSocketShareOf(companyKey);
        }
    }

    /// <summary>
    /// Registers the predicates of the feature with <c>ModConditions</c> (FR-11 item 1): <c>pg.generationLaunched</c>, <c>pg.earlyAdopter</c> and
    /// <c>pg.installedBaseShare</c>. A predicate of a feature is named <c>&lt;prefix&gt;.&lt;name&gt;</c> and the prefix of this feature is <c>pg</c> (the table
    /// <c>prefixes</c> of <c>core.json</c>), so the core accepts the names; it wraps each registered condition so that it is false while the feature is
    /// not active and an exception it throws is counted against the feature. The meaning and the parameters are <see cref="PlatformPredicates"/>'s
    /// (pure, tested); the facts come from <see cref="PlatformFacts"/>. Registered once, from <c>Register</c> of the feature.
    /// </summary>
    internal static class PlatformConditions
    {
        private static readonly PlatformFacts Facts = new PlatformFacts();

        private static bool _registered;

        /// <summary>
        /// Registers the three predicates (a second call does nothing: the catalog of predicates is shared and a name taken is a warning). Returns how many
        /// the core accepted; a refusal is the core's own warning in the log and the predicate stays unknown to the scenarios that name it.
        /// </summary>
        public static int Register()
        {
            if (_registered) return 0;
            _registered = true;

            int accepted = 0;
            IList<KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>> factories = PlatformPredicates.CreateFactories(Facts);
            foreach (KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>> pair in factories)
            {
                if (ModConditions.Register(pair.Key, PlatformGenerationsFeature.FeatureId, pair.Value)) accepted++;
            }

            if (accepted != factories.Count)
                ModLog.Warn(PlatformGenerationsFeature.FeatureId, "predicates: " + accepted + " of " + factories.Count + " pg.* predicates were accepted by the core");
            else
                ModLog.Debug(PlatformGenerationsFeature.FeatureId, "predicates: " + accepted + " pg.* predicates registered");
            return accepted;
        }
    }
}
