using System;
using System.Collections.Generic;
using System.IO;
using PTMod.Core;
using PTMod.Features.EventSystem;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The pure model of the public API of event-system (<c>Features/EventSystem/Api/Model</c>): the defaults the content relies on, and the strings that saved
    /// data and content carry (design.md, "Data Models"; the requests file, section 4). A value that changes here changes the meaning of a saved game or of
    /// a content file, so it must change on purpose.
    /// </summary>
    [Covers(typeof(EventDefinition), typeof(EventTrigger), typeof(EventTargetSpec), typeof(EventChoice), typeof(EventEffect), typeof(PackOptions), typeof(FloatRange),
        typeof(EventTriggerType), typeof(EventTargetType), typeof(TargetWeighting),
        typeof(EventInstanceKinds), typeof(EventInstanceStates), typeof(AppliedEffectStates), typeof(EventLogKinds), typeof(EventCompanyKeys),
        typeof(EffectParamKeys), typeof(EffectScopes))]
    public class ApiModelContractTests
    {
        [Test]
        public void EventDefinition_StartsWithTheDefaultsOfTheContract()
        {
            var definition = new EventDefinition { id = "es.sample" };

            Assert.Equal("es.sample", ((IContentRecord)definition).Id, "the id is the key of ModContent");
            Assert.Equal("es.sample", definition.Id);
            Assert.Equal(1f, definition.weight);
            Assert.Equal(0, definition.cooldownDays);
            Assert.False(definition.oncePerGame);
            Assert.False(definition.important);
            Assert.False(definition.notifyPlayer);
            Assert.Equal(0, definition.timerDays);
            Assert.NotNull(definition.choices);
            Assert.Equal(0, definition.choices.Length);
            Assert.NotNull(definition.effects);
            Assert.Equal(0, definition.effects.Length);
            Assert.Null(definition.conditions, "no conditions - always true");
            Assert.Null(definition.packId, "the registry fills it");
        }

        [Test]
        public void EventTrigger_StartsWithTheDefaultsOfTheContract()
        {
            var trigger = new EventTrigger();

            Assert.Equal(31, trigger.graceDays);
            Assert.Equal(1f, trigger.chance);
            Assert.Equal("Month", trigger.checkEvery);
            Assert.False(trigger.edge);
        }

        [Test]
        public void EventChoice_StartsWithTheDefaultsOfTheContract()
        {
            var choice = new EventChoice();

            Assert.Equal(1f, choice.aiWeight);
            Assert.Equal(0f, choice.aiMinCashMultiple);
            Assert.NotNull(choice.effects);
            Assert.Equal(0, choice.effects.Length);
            Assert.Null(choice.requires, "no requires - always available");
        }

        [Test]
        public void EventEffect_And_TargetSpec_AreWhatTheMapperFills()
        {
            var effect = new EventEffect { type = "Money", durationDays = 7 };
            Assert.Equal("Money", effect.type);
            Assert.Equal(7, effect.durationDays);
            Assert.Null(effect.@params, "the mapper gives every loaded effect a params object");

            var targets = new EventTargetSpec { type = EventTargetType.Specific, companyKey = "ai:4", weighting = TargetWeighting.MarketShare };
            Assert.Equal("ai:4", targets.companyKey);
            Assert.Null(targets.filter);
        }

        [Test]
        public void PackOptions_StartsWithTheDefaultsOfTheContract()
        {
            var options = new PackOptions();

            Assert.Null(options.SettingsFeatureId);
            Assert.Equal(1f, options.Frequency);
            Assert.Equal(1f, options.Severity);
            Assert.Equal("events", options.EventsFile);
            Assert.Null(options.ConfigFile, "null means config.json");
            Assert.Equal(0, options.RivalNewsTopN);
        }

        [Test]
        public void FloatRange_KeepsTheTwoEnds()
        {
            var range = new FloatRange(0.5f, 2f);
            Assert.Equal(0.5f, range.Lo);
            Assert.Equal(2f, range.Hi);
        }

        [Test]
        public void Enums_KeepTheNamesTheContentWrites()
        {
            Assert.Equal("Date,DailyChance,Condition,Manual", string.Join(",", Enum.GetNames(typeof(EventTriggerType))));
            Assert.Equal("Player,RandomAI,AllCompanies,Specific,RandomCompany", string.Join(",", Enum.GetNames(typeof(EventTargetType))));
            Assert.Equal("Uniform,FactoryCapacity,MarketShare,QuarterlyRevenue", string.Join(",", Enum.GetNames(typeof(TargetWeighting))));
        }

        [Test]
        public void SavedStrings_AreTheOnesOfTheDataModel()
        {
            Assert.Equal("Event", EventInstanceKinds.Event);
            Assert.Equal("Direct", EventInstanceKinds.Direct);

            Assert.Equal("AwaitingChoice", EventInstanceStates.AwaitingChoice);
            Assert.Equal("Resolved", EventInstanceStates.Resolved);
            Assert.Equal("Expired", EventInstanceStates.Expired);
            Assert.Equal("Cancelled", EventInstanceStates.Cancelled);

            Assert.Equal("Pending,Active,Done,Skipped,Suspended,Failed,Clamped",
                string.Join(",", AppliedEffectStates.Pending, AppliedEffectStates.Active, AppliedEffectStates.Done, AppliedEffectStates.Skipped,
                    AppliedEffectStates.Suspended, AppliedEffectStates.Failed, AppliedEffectStates.Clamped));

            Assert.Equal("Fired,Resolved,AutoResolved,Cancelled,EffectEnded,EffectFailed",
                string.Join(",", EventLogKinds.Fired, EventLogKinds.Resolved, EventLogKinds.AutoResolved, EventLogKinds.Cancelled,
                    EventLogKinds.EffectEnded, EventLogKinds.EffectFailed));

            Assert.Equal("global", EventCompanyKeys.Global);
            Assert.Equal("*", EventCompanyKeys.AllCompaniesCooldown);
        }

        [Test]
        public void CommonParamKeys_AreTheOnesTheEngineReads()
        {
            Assert.Equal("scope", EffectParamKeys.Scope);
            Assert.Equal("chance", EffectParamKeys.Chance);
            Assert.Equal("exclusiveGroup", EffectParamKeys.ExclusiveGroup);
            Assert.Equal("scalable", EffectParamKeys.Scalable);
            Assert.Equal("Target", EffectScopes.Target);
            Assert.Equal("Global", EffectScopes.Global);
        }
    }
}
