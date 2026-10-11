using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The decision of an AI company on the day its event fires (FR-4 item 6, FR-6 item 7, D14), the three steps in their order:
    /// <list type="number">
    /// <item>the chain of <c>EventAi</c> choosers by descending priority - the first answer that names an available choice wins and nobody after it is asked
    /// (<see cref="AiChooserChain"/>, AC-6.4);</item>
    /// <item>the default policy - <c>aiWeight</c> among the choices the company can afford (<see cref="DefaultAiChooser"/>);</item>
    /// <item>the <c>defaultChoice</c> of the event.</item>
    /// </list>
    /// </summary>
    public static class AiDecision
    {
        /// <param name="chain">The choosers in the order they are asked.</param>
        /// <param name="handles">Whether a chooser handles the event (<c>IEventAiChooserFilter</c>); null - all of them do.</param>
        /// <param name="ask">The answer of a chooser (a choice id, or null).</param>
        /// <param name="definition">The event.</param>
        /// <param name="options">The choices of the instance with their availability, cost and weight.</param>
        /// <param name="money">The cash of the company.</param>
        /// <param name="defaultRng">The generator of the default policy (made only when it is needed).</param>
        /// <param name="onFault">A chooser threw.</param>
        public static string Decide<T>(IList<T> chain, Func<T, bool> handles, Func<T, string> ask, EventDefinition definition, IList<ChoiceOption> options, float money,
            Func<IModRng> defaultRng, Action<T, Exception> onFault)
        {
            string chosen = AiChooserChain.Decide(chain, handles, ask, id => IsAvailable(options, id), onFault);
            if (chosen != null) return chosen;

            IModRng rng = defaultRng != null ? defaultRng() : null;
            return DefaultAiChooser.Choose(definition, options, money, rng) ?? (definition != null ? definition.defaultChoice : null);
        }

        /// <summary>
        /// The choices of an instance as a chooser sees them: whether the <c>requires</c> of the choice holds for the company (<paramref name="check"/>), the up-front
        /// spending of the choice (<see cref="EffectMath.ChoiceCost"/>), its <c>aiWeight</c>.
        /// </summary>
        public static ChoiceOption[] OptionsOf(EventInstance instance, Func<ICondition, bool> check)
        {
            var list = new List<ChoiceOption>();
            if (instance == null || instance.Data == null || instance.Data.options == null) return list.ToArray();

            foreach (ChoiceOptionDto option in instance.Data.options)
            {
                if (option == null) continue;
                EventChoice choice = ChoiceOf(instance.Definition, option.choiceId);
                list.Add(new ChoiceOption
                {
                    ChoiceId = option.choiceId,
                    Available = choice == null || choice.requires == null || check == null || check(choice.requires),
                    Cost = EffectMath.ChoiceCost(option),
                    AiWeight = choice != null ? choice.aiWeight : 1f,
                });
            }
            return list.ToArray();
        }

        private static EventChoice ChoiceOf(EventDefinition definition, string choiceId)
        {
            if (definition == null || definition.choices == null) return null;
            foreach (EventChoice choice in definition.choices)
            {
                if (choice != null && string.Equals(choice.id, choiceId, StringComparison.Ordinal)) return choice;
            }
            return null;
        }

        private static bool IsAvailable(IList<ChoiceOption> options, string choiceId)
        {
            if (options == null) return false;
            foreach (ChoiceOption option in options)
            {
                if (option != null && option.Available && string.Equals(option.ChoiceId, choiceId, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
