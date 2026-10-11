using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.EventSystem;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The AI chooser of the feature (design.md, Component 7; FR-13): decides for an AI company in the events <c>hist.decide.*</c>. It is registered with
    /// <c>EventAi.AddChooser(chooser, priority)</c>, the priority is <c>aiChooserPriority</c> of <c>params.json</c> (10 by default; the chooser of rival-personalities has 100, is asked first and
    /// may answer null to leave the decision here). For any other event, and while the feature is off, it is not asked at all (<see cref="IEventAiChooserFilter"/>) or answers null, and the next
    /// chooser or the engine's default policy decides.
    /// <para>
    /// The weights are <c>aiWeight</c> of the choices (with the overrides of <c>params.json</c>); a choice that costs more than the company can pay now - its money plus the debt limit the game
    /// applies, <c>MoneyAmount - ModCredit.ThresholdOf(company)</c> - has weight 0, and when no weight is left the <c>defaultChoice</c> of the event decides. The cost is read from the numbers the
    /// engine worked out when the instance was made (<c>AppliedEffect.value</c> of the types of this feature, and of <c>Money</c>). The player's rules and the AI's are the same: nothing here depends on
    /// the difficulty. The rest is <see cref="HistoricalAiChooserCore"/>, which is tested without the game.
    /// </para>
    /// </summary>
    internal sealed class HistoricalAiChooser : IEventAiChooser, IEventAiChooserFilter
    {
        private readonly Func<HistContentParams> _params;

        internal HistoricalAiChooser(Func<HistContentParams> contentParams)
        {
            _params = contentParams;
        }

        /// <summary>The decisions of this feature, while the feature is active.</summary>
        public bool Handles(EventInstance instance)
        {
            string eventId = instance == null || instance.Data == null ? null : instance.Data.eventId;
            return HistoricalAiChooserCore.IsOwnEvent(eventId) && ModFeatures.IsActive(HistIds.Feature);
        }

        public string Choose(EventInstance instance, ChoiceOption[] options, ICompany company, IModRng rng)
        {
            return ModHooks.Guard<string>(HistIds.Feature, "ai/choose", () => ChooseCore(instance, options, company, rng), null);
        }

        private string ChooseCore(EventInstance instance, ChoiceOption[] options, ICompany company, IModRng rng)
        {
            if (!Handles(instance) || company == null || options == null || options.Length == 0) return null;

            string eventId = instance.Data.eventId;
            float roll = rng == null ? 0.5f : rng.Value();   // exactly one number per decision, whatever the data (D5)

            var costs = new OptionCost[options.Length];
            var defined = new List<KeyValuePair<string, float>>(options.Length);
            for (int i = 0; i < options.Length; i++)
            {
                ChoiceOption option = options[i];
                costs[i] = new OptionCost(option.ChoiceId, UpfrontCostOf(instance.Data, option.ChoiceId), option.Available);
                defined.Add(new KeyValuePair<string, float>(option.ChoiceId, option.AiWeight));
            }

            HistContentParams content = _params == null ? null : _params();
            AiWeights weights = AiWeights.For(eventId, defined, content == null ? null : content.AiWeightOverrides);
            float headroom = WorldMath.CashHeadroom(company.MoneyAmount, ModCredit.ThresholdOf(company));
            string defaultChoice = instance.Definition == null ? null : instance.Definition.defaultChoice;

            string choice = HistoricalAiChooserCore.Choose(costs, weights, headroom, defaultChoice, roll);
            ModLog.Debug(HistIds.Feature, "ai: " + eventId + " for " + instance.Data.companyKey + " takes " + (choice ?? "(nothing)") + " (headroom " + headroom.ToString("0") + ")");
            return choice;
        }

        /// <summary>The up-front cost of a choice from the numbers saved in the instance (the engine's own <c>ChoiceOption.Cost</c> counts <c>Money</c> only).</summary>
        private static float UpfrontCostOf(EventInstanceDto data, string choiceId)
        {
            if (data == null || data.options == null) return 0f;

            foreach (ChoiceOptionDto option in data.options)
            {
                if (option == null || !string.Equals(option.choiceId, choiceId, StringComparison.Ordinal) || option.effects == null) continue;

                var items = new List<EffectCostItem>(option.effects.Count);
                foreach (AppliedEffect effect in option.effects)
                {
                    if (effect == null) continue;
                    items.Add(new EffectCostItem(effect.type, effect.value, effect.hit, string.Equals(effect.state, AppliedEffectStates.Failed, StringComparison.Ordinal)));
                }
                return HistoricalAiChooserCore.UpfrontCost(items);
            }
            return 0f;
        }
    }
}
