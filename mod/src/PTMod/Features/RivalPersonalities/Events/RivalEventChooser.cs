using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.EventSystem;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The choice of a rival in the events of the event system by its character (design.md, Component 13; FR-14, TSK-026). Registered once, in
    /// <c>Register</c>, with <c>EventAi.AddChooser(chooser, 100)</c>: the engine asks the choosers by descending priority and the first answer that is not
    /// null wins (<c>historical-events</c> has 10 and gets what this one leaves). The registration is kept by the event system while it is off.
    /// <list type="bullet">
    /// <item>The player's company, a company the feature does not know, a dormant one and an archetype of the style <c>neutral</c> give null: the next
    /// chooser or the default policy decides. So does a feature that is switched off (the engine does not look at the switch of the chooser's owner).</item>
    /// <item><c>spend</c> multiplies the weight of the dearest available choice by <c>eventChooser.styleWeight</c> (1.5), <c>save</c> that of the cheapest;
    /// a choice that is not available or that the company cannot afford (<c>aiMinCashMultiple × cost</c>, the rule of the default policy) is never picked.
    /// The pick is a weighted roll of the generator the engine hands in. The rules are <see cref="EventChoiceMath"/>'s (pure, tested).</item>
    /// </list>
    /// An exception is caught here (counted against the feature) and gives null.
    /// </summary>
    internal sealed class RivalEventChooser : IEventAiChooser, IEventAiChooserFilter
    {
        /// <summary>The priority the chooser is registered with (<c>eventChooser.priority</c> of <c>config.json</c> has the same default).</summary>
        public const int DefaultPriority = 100;

        private const string Feature = RivalPersonalitiesFeature.FeatureId;
        private const string Site = "RivalEventChooser/Choose";

        private readonly RivalPersonalitiesFeature _feature;

        public RivalEventChooser(RivalPersonalitiesFeature feature)
        {
            _feature = feature;
        }

        /// <summary>Skips the choosers quickly while the feature is off; the engine asks again for the next event.</summary>
        public bool Handles(EventInstance instance)
        {
            return ModFeatures.IsActive(Feature);
        }

        /// <inheritdoc />
        public string Choose(EventInstance instance, ChoiceOption[] options, ICompany company, IModRng rng)
        {
            return ModHooks.Guard<string>(Feature, Site, () => ChooseFor(instance, options, company, rng), null);
        }

        private string ChooseFor(EventInstance instance, ChoiceOption[] options, ICompany company, IModRng rng)
        {
            if (!ModFeatures.IsActive(Feature) || options == null || options.Length == 0) return null;

            int? uniqueId = RivalApiService.UidOf(company);   // null for the player: he chooses himself
            if (!uniqueId.HasValue || _feature.Book.IsDormant(uniqueId.Value)) return null;

            Archetype archetype = _feature.Book.ArchetypeOf(uniqueId.Value);
            if (archetype == null || archetype.eventStyle == EventStyle.Neutral) return null;

            float money = company.MoneyAmount;
            EventDefinition definition = instance != null ? instance.Definition : null;
            var candidates = new EventChoiceCandidate[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                ChoiceOption option = options[i];
                if (option == null)
                {
                    candidates[i] = new EventChoiceCandidate(null, false, false, 0f, 0f);
                    continue;
                }
                candidates[i] = new EventChoiceCandidate(option.ChoiceId, option.Available, CanAfford(definition, option, money), option.Cost, option.AiWeight);
            }

            return EventChoiceMath.Choose(archetype.eventStyle, candidates, _feature.Catalog.Config.eventChooser.styleWeight, rng);
        }

        /// <summary>The company has the cash a choice asks for: <c>aiMinCashMultiple</c> times its cost (a free choice, or one without the rule, is always affordable).</summary>
        private static bool CanAfford(EventDefinition definition, ChoiceOption option, float money)
        {
            if (definition == null || definition.choices == null || option.Cost <= 0f) return true;

            foreach (EventChoice choice in definition.choices)
            {
                if (choice == null || !string.Equals(choice.id, option.ChoiceId, StringComparison.Ordinal)) continue;
                return choice.aiMinCashMultiple <= 0f || (double)money >= (double)choice.aiMinCashMultiple * option.Cost;
            }
            return true;
        }
    }
}
