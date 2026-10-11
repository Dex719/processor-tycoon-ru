using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>One choice of an event as the AI chooser of a rival sees it (the pure form of the event system's <c>ChoiceOption</c> plus the cash check).</summary>
    public struct EventChoiceCandidate
    {
        public EventChoiceCandidate(string id, bool available, bool affordable, float cost, float aiWeight)
        {
            Id = id;
            Available = available;
            Affordable = affordable;
            Cost = cost;
            AiWeight = aiWeight;
        }

        /// <summary>The id of the choice.</summary>
        public string Id;

        /// <summary><c>ChoiceOption.Available</c>: the <c>requires</c> of the choice holds for the company.</summary>
        public bool Available;

        /// <summary>The company has the cash the choice asks for (<c>aiMinCashMultiple × cost</c>), the rule of the default policy of the event system.</summary>
        public bool Affordable;

        /// <summary><c>ChoiceOption.Cost</c>: what the choice spends up front, in dollars (0 for a free choice).</summary>
        public float Cost;

        /// <summary><c>ChoiceOption.AiWeight</c>: the weight from the definition of the event.</summary>
        public float AiWeight;
    }

    /// <summary>
    /// How a rival chooses in the events of the event system by its character (design.md, Component 13; FR-14, TSK-026). The archetype has an
    /// <c>eventStyle</c>:
    /// <list type="bullet">
    /// <item><c>spend</c> multiplies the weight of the dearest available choice by <c>eventChooser.styleWeight</c> (1.5); <c>save</c> does the same for the
    /// cheapest one. Choices that tie for the dearest (or cheapest) cost are all multiplied, so the order of the list does not matter.</item>
    /// <item><c>neutral</c> and a style without a cost difference among the choices (all cost the same: nothing to spend or to save) give no answer, and the
    /// event system's default policy decides.</item>
    /// <item>A choice that is not available, that the company cannot afford, or whose weight is not a positive finite number is never picked.</item>
    /// <item>The pick is a weighted roll of the generator the engine hands in (<see cref="IModRng.WeightedIndex"/>, one draw).</item>
    /// </list>
    /// </summary>
    public static class EventChoiceMath
    {
        /// <summary>
        /// The weights of the choices after the style, in the order of <paramref name="options"/>: 0 for a choice that cannot be picked. All 0 when nothing
        /// can be picked.
        /// </summary>
        public static float[] Weights(EventStyle style, IList<EventChoiceCandidate> options, float styleWeight)
        {
            int count = options == null ? 0 : options.Count;
            var weights = new float[count];
            if (count == 0) return weights;

            bool any = false;
            bool haveCost = false;
            float lowest = 0f;
            float highest = 0f;
            for (int i = 0; i < count; i++)
            {
                if (!CanPick(options[i])) continue;
                weights[i] = options[i].AiWeight;
                any = true;

                float cost = CostOf(options[i]);
                if (!haveCost)
                {
                    lowest = cost;
                    highest = cost;
                    haveCost = true;
                }
                else
                {
                    if (cost < lowest) lowest = cost;
                    if (cost > highest) highest = cost;
                }
            }
            if (!any) return weights;

            float factor = float.IsNaN(styleWeight) || float.IsInfinity(styleWeight) || styleWeight <= 0f ? 1f : styleWeight;
            if (style == EventStyle.Neutral || factor == 1f) return weights;

            float target = style == EventStyle.Spend ? highest : lowest;
            for (int i = 0; i < count; i++)
            {
                if (weights[i] > 0f && CostOf(options[i]) == target) weights[i] *= factor;
            }
            return weights;
        }

        /// <summary>
        /// The id of the choice the rival takes, or null when it has no opinion (the next chooser or the default policy decides): a <c>neutral</c> style,
        /// no choice that can be picked, no difference in cost among them, or a roll that found nothing. Draws at most once from <paramref name="rng"/>.
        /// </summary>
        public static string Choose(EventStyle style, IList<EventChoiceCandidate> options, float styleWeight, IModRng rng)
        {
            if (style == EventStyle.Neutral || options == null || options.Count == 0 || rng == null) return null;
            if (!HasCostDifference(options)) return null;

            float[] weights = Weights(style, options, styleWeight);
            int index = rng.WeightedIndex(weights);
            if (index < 0 || index >= options.Count || !(weights[index] > 0f)) return null;
            return options[index].Id;
        }

        /// <summary>Two or more choices can be picked and they do not all cost the same: only then does "spend" or "save" mean anything.</summary>
        public static bool HasCostDifference(IList<EventChoiceCandidate> options)
        {
            if (options == null) return false;

            bool haveFirst = false;
            float first = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                if (!CanPick(options[i])) continue;
                float cost = CostOf(options[i]);
                if (!haveFirst)
                {
                    first = cost;
                    haveFirst = true;
                }
                else if (cost != first)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>A choice the rival may take: available, affordable, with a positive finite weight and an id.</summary>
        public static bool CanPick(EventChoiceCandidate option)
        {
            if (string.IsNullOrEmpty(option.Id) || !option.Available || !option.Affordable) return false;
            return option.AiWeight > 0f && !float.IsInfinity(option.AiWeight);
        }

        private static float CostOf(EventChoiceCandidate option)
        {
            float cost = option.Cost;
            return float.IsNaN(cost) || cost < 0f ? 0f : cost;
        }
    }
}
