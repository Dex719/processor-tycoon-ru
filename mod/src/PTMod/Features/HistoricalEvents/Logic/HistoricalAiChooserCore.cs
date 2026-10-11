using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// One choice of a decision as the chooser of the AI weighs it: what it costs up front and whether the company may take it at all (the <c>requires</c> of the choice).
    /// </summary>
    public struct OptionCost
    {
        public string ChoiceId;

        /// <summary>The money the choice takes at once, in dollars (0 for a free one); see <see cref="HistoricalAiChooserCore.UpfrontCost"/>.</summary>
        public float UpfrontCost;

        /// <summary>False when the <c>requires</c> of the choice is false for the company: such a choice is never taken.</summary>
        public bool Available;

        public OptionCost(string choiceId, float upfrontCost, bool available = true)
        {
            ChoiceId = choiceId;
            UpfrontCost = upfrontCost;
            Available = available;
        }
    }

    /// <summary>
    /// What an effect of a choice costs, taken from the numbers the engine worked out when the instance was made (<c>AppliedEffect</c>): the type, the value (negative: money paid), and
    /// whether the effect counts (the chance hit, the effect did not fail). Plain data so that the rule below can be tested without the engine.
    /// </summary>
    public struct EffectCostItem
    {
        public string Type;
        public float Value;
        public bool Hit;
        public bool Failed;

        public EffectCostItem(string type, float value, bool hit = true, bool failed = false)
        {
            Type = type;
            Value = value;
            Hit = hit;
            Failed = failed;
        }
    }

    /// <summary>
    /// The weights of the choices of one event for the AI: <c>aiWeight</c> of the definition, replaced where <c>params.json</c> names one (<c>aiWeightOverrides</c>:
    /// event id, then choice id, then the weight). A weight that is not a finite number of 0 or more is ignored and the definition's one stays.
    /// </summary>
    public sealed class AiWeights
    {
        /// <summary>The largest weight an override may give (a guard against a typo, not a design limit).</summary>
        public const float MaxWeight = 1000f;

        private readonly Dictionary<string, float> _weights = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>The weight of the choice; 0 for a choice that is not known.</summary>
        public float Of(string choiceId)
        {
            float weight;
            return choiceId != null && _weights.TryGetValue(choiceId, out weight) ? weight : 0f;
        }

        public void Set(string choiceId, float weight)
        {
            if (choiceId != null) _weights[choiceId] = IsUsable(weight) ? weight : 0f;
        }

        /// <summary>The weights of the event: the definition's <paramref name="defined"/> (choice id, <c>aiWeight</c>), then the overrides of this event.</summary>
        public static AiWeights For(string eventId, IEnumerable<KeyValuePair<string, float>> defined, JsonObject overrides)
        {
            var weights = new AiWeights();
            if (defined != null)
            {
                foreach (KeyValuePair<string, float> choice in defined) weights.Set(choice.Key, choice.Value);
            }

            JsonObject mine = eventId == null || overrides == null ? null : overrides.GetObject(eventId);
            if (mine == null) return weights;

            foreach (KeyValuePair<string, JsonNode> pair in mine)
            {
                var number = pair.Value as JsonValue;
                if (number == null || number.Kind != JsonKind.Number) continue;

                double value = number.NumberValue;
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0 || value > MaxWeight) continue;
                weights._weights[pair.Key] = (float)value;
            }
            return weights;
        }

        private static bool IsUsable(float weight)
        {
            return !float.IsNaN(weight) && !float.IsInfinity(weight) && weight > 0f;
        }
    }

    /// <summary>
    /// The decision of an AI company in an event of this feature (design.md, Component 7; FR-13): the chooser registered with <c>EventAi.AddChooser(chooser, priority)</c> answers only for the
    /// events <c>hist.decide.*</c>, weighs the choices by <c>aiWeight</c>, drops those the company cannot pay for, and falls back to the <c>defaultChoice</c> of the event when nothing is left.
    /// The same rules for the AI and the player: nothing here depends on the difficulty or on who the company is. The chooser of rival-personalities (priority 100) is asked first and may answer null;
    /// then this one decides, then the default policy of the engine.
    /// </summary>
    public static class HistoricalAiChooserCore
    {
        /// <summary>The prefix of the events with a decision (the pack's <c>hist.decide.*</c>).</summary>
        public const string DecisionPrefix = HistIds.Prefix + "decide.";

        /// <summary>The effect type of the engine that spends money; its negative value counts as the cost of a choice like the types of this feature.</summary>
        public const string MoneyType = "Money";

        /// <summary>The event is one of this feature's decisions; for any other event the chooser answers null.</summary>
        public static bool IsOwnEvent(string eventId)
        {
            return !string.IsNullOrEmpty(eventId) && eventId.StartsWith(DecisionPrefix, StringComparison.Ordinal);
        }

        /// <summary>The effect type's value is money: the six services (their <c>value</c> is the money paid when the choice is applied) and the engine's <c>Money</c>.</summary>
        public static bool CountsAsCost(string type)
        {
            return string.Equals(type, MoneyType, StringComparison.Ordinal) || HistEffectRules.IsOwnType(type);
        }

        /// <summary>
        /// The money a choice takes at once: the sum of the negative values of its effects of the types of this feature and of <c>Money</c>, for the effects that took their chance and
        /// did not fail. A positive value (an income) is not a cost; a value that is not a finite number counts as 0.
        /// </summary>
        public static float UpfrontCost(IEnumerable<EffectCostItem> effects)
        {
            if (effects == null) return 0f;

            double cost = 0.0;
            foreach (EffectCostItem effect in effects)
            {
                if (!effect.Hit || effect.Failed || !CountsAsCost(effect.Type)) continue;
                if (float.IsNaN(effect.Value) || float.IsInfinity(effect.Value) || effect.Value >= 0f) continue;
                cost -= effect.Value;
            }
            return (float)cost;
        }

        /// <summary>
        /// The id of the choice the company takes. A choice that is not available, or whose up-front cost is above <paramref name="cashHeadroom"/> (the money plus the debt limit the game applies,
        /// <c>MoneyAmount - ModCredit.ThresholdOf(company)</c>), has weight 0; a free choice is never cut, even by a negative headroom. Of the rest the one whose share of the total weight
        /// contains <paramref name="roll01"/> wins (the same walk as the engine's own pick); when no weight is left the answer is <paramref name="defaultChoice"/>.
        /// </summary>
        /// <returns>The choice id; null only when there is no weight and the event names no default.</returns>
        public static string Choose(OptionCost[] options, AiWeights weights, float cashHeadroom, string defaultChoice, float roll01)
        {
            string fallback = string.IsNullOrEmpty(defaultChoice) ? null : defaultChoice;
            if (options == null || options.Length == 0) return fallback;

            var effective = new float[options.Length];
            double total = 0.0;
            int last = -1;
            for (int i = 0; i < options.Length; i++)
            {
                effective[i] = EffectiveWeight(options[i], weights, cashHeadroom);
                if (effective[i] <= 0f) continue;
                total += effective[i];
                last = i;
            }
            if (last < 0) return fallback;

            double roll = float.IsNaN(roll01) ? 0.0 : Math.Max(0.0, Math.Min(1.0, (double)roll01));
            double point = roll * total;
            double sum = 0.0;
            for (int i = 0; i < options.Length; i++)
            {
                if (effective[i] <= 0f) continue;
                sum += effective[i];
                if (point < sum) return options[i].ChoiceId;
            }
            return options[last].ChoiceId;
        }

        /// <summary>The weight a choice really has: its own, or 0 when it is unavailable or too dear for the company.</summary>
        public static float EffectiveWeight(OptionCost option, AiWeights weights, float cashHeadroom)
        {
            if (!option.Available || string.IsNullOrEmpty(option.ChoiceId) || weights == null) return 0f;

            float cost = option.UpfrontCost;
            bool pays = cost > 0f && !float.IsNaN(cost);
            if (pays && (float.IsNaN(cashHeadroom) || cost > cashHeadroom)) return 0f;

            float weight = weights.Of(option.ChoiceId);
            return float.IsNaN(weight) || float.IsInfinity(weight) || weight <= 0f ? 0f : weight;
        }
    }
}
