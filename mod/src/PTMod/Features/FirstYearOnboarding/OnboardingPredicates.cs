using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Json;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// Registers the twelve predicates <c>onb.*</c> of the tutorial with <c>ModConditions</c> (TSK-003, FR-4). The names, the parameters and the
    /// meaning are <see cref="OnboardingPredicateLibrary"/>'s (pure logic); the facts come from <see cref="GameOnboardingFacts"/>. A predicate of a
    /// feature is named <c>&lt;prefix&gt;.&lt;name&gt;</c>, the prefix of this feature is <c>onb</c>; the core wraps every registered condition so
    /// that it is false while the feature is not active and an exception is counted against the feature.
    /// </summary>
    internal static class OnboardingPredicates
    {
        /// <summary>The facts the predicates read; the director hands it the answer for <c>onb.stepDone</c>.</summary>
        internal static readonly GameOnboardingFacts Facts = new GameOnboardingFacts();

        private static bool _registered;

        /// <summary>
        /// Registers the predicates (once per session). Returns how many the core accepted; a refusal (a name taken, a name that breaks the
        /// convention) is a warning from the core and the predicate stays unknown, so the steps that use it are dropped by the checks.
        /// </summary>
        internal static int Register()
        {
            if (_registered) return 0;
            _registered = true;

            int accepted = 0;
            IList<KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>>> factories = OnboardingPredicateLibrary.CreateFactories(Facts);
            foreach (KeyValuePair<string, Func<JsonObject, List<string>, string, ICondition>> pair in factories)
            {
                if (ModConditions.Register(pair.Key, OnboardingFeature.FeatureId, pair.Value)) accepted++;
            }

            if (accepted != factories.Count)
                ModLog.Warn(OnboardingFeature.FeatureId, "predicates: " + accepted + " of " + factories.Count + " onb.* predicates were accepted by the core");
            else
                ModLog.Debug(OnboardingFeature.FeatureId, "predicates: " + accepted + " onb.* predicates registered");
            return accepted;
        }
    }
}
