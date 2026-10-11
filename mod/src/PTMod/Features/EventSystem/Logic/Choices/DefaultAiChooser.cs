using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The policy of an AI company when no chooser of the chain answers (FR-4 item 6, D14): one of the choices it can take, by <c>aiWeight</c>. A choice is a
    /// candidate when its <c>requires</c> holds (<see cref="ChoiceOption.Available"/>), its weight is above zero and, if the choice names
    /// <c>aiMinCashMultiple</c>, the company has at least that many times the cost of the choice in cash. The draw always takes exactly one number of the
    /// generator, so the count of numbers does not depend on the data (D5). With no candidate the <c>defaultChoice</c> of the event decides; it is never held back
    /// by money.
    /// </summary>
    public static class DefaultAiChooser
    {
        /// <param name="definition">The event (its choices carry <c>aiMinCashMultiple</c>, <c>defaultChoice</c> names the fallback).</param>
        /// <param name="options">The choices of the instance with their availability, cost and weight (<c>ChoiceOption</c>).</param>
        /// <param name="money">The cash of the company now.</param>
        /// <param name="rng">The generator derived for the decision.</param>
        /// <returns>The id of the choice; null only when the event has neither a candidate nor a default choice.</returns>
        public static string Choose(EventDefinition definition, IList<ChoiceOption> options, float money, IModRng rng)
        {
            float u = rng == null ? 0.5f : rng.Value();
            string fallback = definition != null ? definition.defaultChoice : null;
            if (options == null || options.Count == 0) return fallback;

            var candidates = new List<ChoiceOption>();
            var weights = new List<float>();
            foreach (ChoiceOption option in options)
            {
                if (option == null || !option.Available || !(option.AiWeight > 0f)) continue;
                if (!CanAfford(definition, option, money)) continue;
                candidates.Add(option);
                weights.Add(option.AiWeight);
            }

            int index = WeightedPick.Index(weights, u);
            return index < 0 ? fallback : candidates[index].ChoiceId;
        }

        /// <summary>The company has the cash a choice asks for (<c>aiMinCashMultiple</c> times its cost); a free choice or one without the rule is always affordable.</summary>
        public static bool CanAfford(EventDefinition definition, ChoiceOption option, float money)
        {
            if (option == null) return false;
            float multiple = MinCashMultiple(definition, option.ChoiceId);
            if (multiple <= 0f || option.Cost <= 0f) return true;
            return (double)money >= (double)multiple * option.Cost;
        }

        private static float MinCashMultiple(EventDefinition definition, string choiceId)
        {
            if (definition == null || definition.choices == null) return 0f;
            foreach (EventChoice choice in definition.choices)
            {
                if (choice != null && string.Equals(choice.id, choiceId, StringComparison.Ordinal)) return choice.aiMinCashMultiple;
            }
            return 0f;
        }
    }
}
