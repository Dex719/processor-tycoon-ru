using System.Collections.Generic;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// TSK-001: <c>JsonObject</c> to <see cref="EventDefinition"/>. The names are the contract of design.md ("Контентная модель"); texts are
    /// <c>LocText</c> (a key or <c>{ ru, en }</c>, AC-1.? item 4 of FR-1); conditions come from the parser given to the mapper.
    /// </summary>
    [Covers(typeof(DefinitionMapper))]
    public class DefinitionMapperTests
    {
        /// <summary>The example of design.md ("Пример события с выбором, шансом и цепочкой"), complete.</summary>
        private const string PatentTroll = @"{
            'id': 'es.patent_troll', 'category': 'universal',
            'title': { 'key': 'es.patent_troll.title' }, 'text': { 'key': 'es.patent_troll.text' }, 'cause': { 'key': 'es.patent_troll.cause' },
            'trigger': { 'type': 'DailyChance' },
            'conditions': { 'all': [ { 'type': 'cpuOnMarket' }, { 'type': 'dateAtLeast', 'year': 1978 } ] },
            'weight': 6, 'cooldownDays': 1095, 'oncePerGame': false,
            'targets': { 'type': 'Player' },
            'timerDays': 14, 'defaultChoice': 'settle', 'important': true,
            'choices': [
                { 'id': 'settle', 'textKey': 'es.patent_troll.settle',
                  'effects': [ { 'type': 'Money', 'params': { 'direction': 'Spend', 'origin': 'Fines', 'revenueFraction': [0.02, 0.03], 'min': 30000, 'max': 10000000 } } ] },
                { 'id': 'fight', 'textKey': 'es.patent_troll.fight', 'aiWeight': 0.5, 'aiMinCashMultiple': 2,
                  'requires': { 'type': 'cpuOnMarket' },
                  'effects': [
                    { 'type': 'Money', 'params': { 'direction': 'Spend', 'origin': 'Fines', 'revenueFraction': [0.005, 0.01], 'min': 10000, 'max': 3000000 } },
                    { 'type': 'FireEvent', 'params': { 'eventId': 'es.patent_win', 'delayDays': [60, 120], 'chance': 0.6, 'exclusiveGroup': 'verdict' } },
                    { 'type': 'FireEvent', 'params': { 'eventId': 'es.patent_loss', 'delayDays': [60, 120], 'chance': 0.4, 'exclusiveGroup': 'verdict' } } ] } ]
        }";

        private static EventDefinition Map(FakeEnvironment env, string json, List<string> messages)
        {
            return EventTestKit.Map(env, json, messages);
        }

        // ---- a whole record ----

        [Test]
        public void Maps_TheCompleteExampleOfTheDesign()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                EventDefinition d = Map(env, PatentTroll, messages);

                Assert.NotNull(d, EventTestKit.Join(messages));
                Assert.Equal(0, messages.Count, EventTestKit.Join(messages));
                Assert.Equal("es.patent_troll", d.id);
                Assert.Equal("universal", d.category);
                Assert.Equal("es.patent_troll.title", d.title.Key);
                Assert.Equal("es.patent_troll.text", d.text.Key);
                Assert.Equal("es.patent_troll.cause", d.cause.Key);
                Assert.Equal(EventTriggerType.DailyChance, d.trigger.type);
                Assert.NotNull(d.conditions, "the conditions are parsed");
                Assert.Equal(6f, d.weight);
                Assert.Equal(1095, d.cooldownDays);
                Assert.False(d.oncePerGame);
                Assert.Equal(EventTargetType.Player, d.targets.type);
                Assert.Equal(14, d.timerDays);
                Assert.Equal("settle", d.defaultChoice);
                Assert.True(d.important);
                Assert.False(d.notifyPlayer);
                Assert.Equal(0, d.effects.Length);
                Assert.Null(d.packId, "the catalog fills the pack");

                Assert.Equal(2, d.choices.Length);
                Assert.Equal("settle", d.choices[0].id);
                Assert.Equal("es.patent_troll.settle", d.choices[0].textKey);
                Assert.Equal(1f, d.choices[0].aiWeight);
                Assert.Null(d.choices[0].requires);
                Assert.Equal(1, d.choices[0].effects.Length);

                EventChoice fight = d.choices[1];
                Assert.Equal(0.5f, fight.aiWeight);
                Assert.Equal(2f, fight.aiMinCashMultiple);
                Assert.NotNull(fight.requires);
                Assert.Equal(3, fight.effects.Length);
                Assert.Equal("FireEvent", fight.effects[1].type);
                Assert.Equal("es.patent_win", fight.effects[1].@params.GetString("eventId", null));
                FloatRange delay = fight.effects[1].@params.GetRange("delayDays", new FloatRange(0, 0));
                Assert.Equal(60f, delay.Lo);
                Assert.Equal(120f, delay.Hi);
                Assert.Equal("verdict", fight.effects[2].@params.GetString("exclusiveGroup", null));
            });
        }

        [Test]
        public void Maps_TheExampleWithoutChoices_AndATargetOfAnotherCompany()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                EventDefinition d = Map(env, @"{
                    'id': 'es.rival_scandal', 'category': 'universal',
                    'title': { 'key': 'es.rival_scandal.title' }, 'text': { 'key': 'es.rival_scandal.text' }, 'cause': { 'key': 'es.rival_scandal.cause' },
                    'trigger': { 'type': 'DailyChance' }, 'weight': 5, 'cooldownDays': 365, 'notifyPlayer': true,
                    'targets': { 'type': 'RandomAI', 'weighting': 'MarketShare', 'filter': { 'type': 'cpuOnMarket' } },
                    'effects': [
                        { 'type': 'Reputation', 'params': { 'delta': -6 } },
                        { 'type': 'ModifierTimed', 'params': { 'channel': 'popularity.gain', 'multiplier': 0.85 }, 'durationDays': 120 } ] }", messages);

                Assert.NotNull(d, EventTestKit.Join(messages));
                Assert.True(d.notifyPlayer);
                Assert.Equal(EventTargetType.RandomAI, d.targets.type);
                Assert.Equal(TargetWeighting.MarketShare, d.targets.weighting);
                Assert.NotNull(d.targets.filter);
                Assert.Equal(0, d.choices.Length);
                Assert.Equal(2, d.effects.Length);
                Assert.Equal(120, d.effects[1].durationDays);
                Assert.Equal(0, d.effects[0].durationDays);
                Assert.Equal(0.85f, d.effects[1].@params.GetFloat("multiplier", 0f));
            });
        }

        // ---- defaults ----

        [Test]
        public void Maps_AMinimalRecord_WithTheDefaultsOfTheContract()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                EventDefinition d = Map(env, "{ 'id': 'es.min', 'title': { 'key': 'k.t' }, 'text': { 'key': 'k.x' }, 'trigger': { 'type': 'Manual' } }", messages);

                Assert.NotNull(d, EventTestKit.Join(messages));
                Assert.Equal(0, messages.Count);
                Assert.Null(d.category);
                Assert.Null(d.cause);
                Assert.Null(d.conditions);
                Assert.Equal(1f, d.weight);
                Assert.Equal(0, d.cooldownDays);
                Assert.False(d.oncePerGame);
                Assert.Equal(EventTargetType.Player, d.targets.type, "no 'targets': the player");
                Assert.Equal(TargetWeighting.Uniform, d.targets.weighting);
                Assert.Null(d.targets.filter);
                Assert.Equal(0, d.choices.Length);
                Assert.Equal(0, d.effects.Length);
                Assert.Equal(0, d.timerDays);
                Assert.Null(d.defaultChoice);
                Assert.Equal(31, d.trigger.graceDays);
                Assert.Equal(1f, d.trigger.chance);
                Assert.Equal("Month", d.trigger.checkEvery);
                Assert.False(d.trigger.edge);
            });
        }

        [Test]
        public void Maps_TheFieldsOfEveryKindOfTrigger()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                EventDefinition date = Map(env, "{ 'id': 'es.d', 'title': { 'key': 'k.t' }, 'text': { 'key': 'k.x' }, "
                    + "'trigger': { 'type': 'Date', 'year': 1985, 'month': 6, 'day': 1, 'graceDays': 10 } }", messages);
                Assert.NotNull(date, EventTestKit.Join(messages));
                Assert.Equal(EventTriggerType.Date, date.trigger.type);
                Assert.Equal(1985, date.trigger.year);
                Assert.Equal(6, date.trigger.month);
                Assert.Equal(1, date.trigger.day);
                Assert.Equal(10, date.trigger.graceDays);

                EventDefinition condition = Map(env, "{ 'id': 'es.c', 'title': { 'key': 'k.t' }, 'text': { 'key': 'k.x' }, "
                    + "'trigger': { 'type': 'Condition', 'chance': 0.25, 'checkEvery': 'day', 'edge': true }, 'conditions': { 'type': 'cpuOnMarket' } }", messages);
                Assert.NotNull(condition, EventTestKit.Join(messages));
                Assert.Equal(EventTriggerType.Condition, condition.trigger.type);
                Assert.Equal(0.25f, condition.trigger.chance);
                Assert.Equal("Day", condition.trigger.checkEvery, "the spelling is normalized");
                Assert.True(condition.trigger.edge);
            });
        }

        // ---- texts ----

        [Test]
        public void Texts_AreAKeyOrInlineRuAndEn()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                EventDefinition d = Map(env, "{ 'id': 'es.t', 'title': { 'ru': 'Заголовок', 'en': 'Title' }, 'text': { 'key': 'es.t.text', 'en': 'Text' }, "
                    + "'cause': { 'ru': 'Причина' }, 'trigger': { 'type': 'Manual' } }", messages);

                Assert.NotNull(d, EventTestKit.Join(messages));
                Assert.Null(d.title.Key);
                Assert.Equal("Заголовок", d.title.Ru);
                Assert.Equal("Title", d.title.En);
                Assert.Equal("es.t.text", d.text.Key);
                Assert.Equal("Text", d.text.En);
                Assert.Equal("Причина", d.cause.Ru);
            });
        }

        // ---- mistakes ----

        [Test]
        public void ARecordThatIsNotAnObject_IsRejected()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                Assert.Null(new DefinitionMapper(env.ParseCondition).Map(null, messages));
                Assert.Equal(1, messages.Count);
            });
        }

        [Test]
        public void MissingRequiredFields_AreAllReportedAtOnce()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                EventDefinition d = Map(env, "{ 'id': 'es.x' }", messages);

                Assert.Null(d);
                Assert.True(EventTestKit.AnyContains(messages, "'title' is required"), EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "'text' is required"), EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "'trigger' is required"), EventTestKit.Join(messages));
            });
        }

        [Test]
        public void ValuesOfTheWrongKind_AreRejected_WithTheFieldInTheMessage()
        {
            EventTestKit.WithWorld(env =>
            {
                string[] bad =
                {
                    "'weight': 'heavy'",
                    "'weight': -1",
                    "'weight': 1e9",
                    "'cooldownDays': 1.5",
                    "'cooldownDays': -1",
                    "'timerDays': 'soon'",
                    "'oncePerGame': 1",
                    "'important': 'yes'",
                    "'defaultChoice': []",
                    "'choices': {}",
                    "'effects': 'none'",
                    "'targets': 'Player'",
                    "'targets': { 'type': 'Nobody' }",
                    "'targets': { }",
                    "'targets': { 'type': 'RandomAI', 'weighting': 'Random' }",
                    "'cause': 'why'",
                    "'cause': { }",
                };
                foreach (string member in bad)
                {
                    var messages = new List<string>();
                    EventDefinition d = Map(env, EventTestKit.Record(member), messages);
                    Assert.Null(d, member);
                    Assert.True(messages.Count >= 1, member);
                }

                var named = new List<string>();
                Map(env, EventTestKit.Record("'weight': 'heavy'"), named);
                Assert.True(EventTestKit.AnyContains(named, "(weight)"), "the message names the field: " + EventTestKit.Join(named));
            });
        }

        [Test]
        public void ACategoryThatIsNotAString_IsRejected()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                Assert.Null(Map(env, "{ 'id': 'es.t', 'category': 5, 'title': { 'key': 'k.t' }, 'text': { 'key': 'k.x' }, 'trigger': { 'type': 'Manual' } }", messages));
                Assert.True(EventTestKit.AnyContains(messages, "(category)"), EventTestKit.Join(messages));
            });
        }

        [Test]
        public void ATriggerOfAnUnknownTypeOrWithBadNumbers_IsRejected()
        {
            EventTestKit.WithWorld(env =>
            {
                string[] bad =
                {
                    "'trigger': { 'type': 'Sometimes' }",
                    "'trigger': { 'type': 5 }",
                    "'trigger': { }",
                    "'trigger': 'Manual'",
                    "'trigger': { 'type': 'Condition', 'chance': 2 }",
                    "'trigger': { 'type': 'Condition', 'checkEvery': 'Week' }",
                    "'trigger': { 'type': 'Date', 'year': 'soon' }",
                    "'trigger': { 'type': 'Date', 'graceDays': -1 }",
                };
                foreach (string trigger in bad)
                {
                    var messages = new List<string>();
                    string json = "{ 'id': 'es.t', 'title': { 'key': 'k.t' }, 'text': { 'key': 'k.x' }, " + trigger + " }";
                    Assert.Null(Map(env, json, messages), trigger);
                    Assert.True(messages.Count >= 1, trigger);
                }
            });
        }

        [Test]
        public void EnumNames_AreCaseInsensitive_AndNumbersAreNotNames()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                EventDefinition d = Map(env, "{ 'id': 'es.t', 'title': { 'key': 'k.t' }, 'text': { 'key': 'k.x' }, 'trigger': { 'type': 'dailychance' }, "
                    + "'targets': { 'type': 'randomai', 'weighting': 'FACTORYCAPACITY' } }", messages);
                Assert.NotNull(d, EventTestKit.Join(messages));
                Assert.Equal(EventTriggerType.DailyChance, d.trigger.type);
                Assert.Equal(EventTargetType.RandomAI, d.targets.type);
                Assert.Equal(TargetWeighting.FactoryCapacity, d.targets.weighting);

                messages.Clear();
                Assert.Null(Map(env, "{ 'id': 'es.t', 'title': { 'key': 'k.t' }, 'text': { 'key': 'k.x' }, 'trigger': { 'type': '1' } }", messages));
                Assert.Null(Map(env, "{ 'id': 'es.t', 'title': { 'key': 'k.t' }, 'text': { 'key': 'k.x' }, 'trigger': { 'type': 1 } }", messages));
            });
        }

        [Test]
        public void SeveralMistakes_AreAllCollected()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                Assert.Null(Map(env, EventTestKit.Record("'weight': 'x', 'cooldownDays': 'y', 'timerDays': 'z'"), messages));
                Assert.True(messages.Count >= 3, EventTestKit.Join(messages));
            });
        }

        // ---- conditions ----

        [Test]
        public void AnUnknownPredicate_RejectsTheRecord_ForEachOfTheThreeConditionNodes()
        {
            EventTestKit.WithWorld(env =>
            {
                var inConditions = new List<string>();
                Assert.Null(Map(env, EventTestKit.Record("'conditions': { 'type': 'nothingLikeThis' }"), inConditions));
                Assert.True(EventTestKit.AnyContains(inConditions, "conditions"), EventTestKit.Join(inConditions));
                Assert.True(EventTestKit.AnyContains(inConditions, "nothingLikeThis"), EventTestKit.Join(inConditions));

                var inFilter = new List<string>();
                Assert.Null(Map(env, EventTestKit.Record("'targets': { 'type': 'RandomAI', 'filter': { 'type': 'nothingLikeThis' } }"), inFilter));
                Assert.True(EventTestKit.AnyContains(inFilter, "targets.filter"), EventTestKit.Join(inFilter));

                var inRequires = new List<string>();
                Assert.Null(Map(env, EventTestKit.Record("'choices': [ { 'id': 'a', 'textKey': 'es.a', 'requires': { 'type': 'nothingLikeThis' } } ]"), inRequires));
                Assert.True(EventTestKit.AnyContains(inRequires, "choices[0].requires"), EventTestKit.Join(inRequires));
            });
        }

        [Test]
        public void AConditionOfTheEngine_IsParsed_ThroughTheRegisteredPredicate()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                EventDefinition d = Map(env, EventTestKit.Record("'conditions': { 'not': { 'type': 'es.hasFab' } }"), messages);
                Assert.NotNull(d, EventTestKit.Join(messages));
                Assert.NotNull(d.conditions);
            });
        }

        [Test]
        public void ABadParameterOfAPredicate_RejectsTheRecord()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                Assert.Null(Map(env, EventTestKit.Record("'conditions': { 'type': 'dateAtLeast', 'year': 'soon' }"), messages));
                Assert.True(messages.Count >= 1);
            });
        }

        // ---- choices and effects ----

        [Test]
        public void AChoice_NeedsAnIdAndAText_AndMustBeAnObject()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                Assert.Null(Map(env, EventTestKit.Record("'choices': [ { 'effects': [] } ]"), messages));
                Assert.True(EventTestKit.AnyContains(messages, "choices[0].id"), EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "choices[0].textKey"), EventTestKit.Join(messages));

                messages.Clear();
                Assert.Null(Map(env, EventTestKit.Record("'choices': [ 'hire' ]"), messages));
                Assert.True(EventTestKit.AnyContains(messages, "choices[0]"), EventTestKit.Join(messages));

                messages.Clear();
                Assert.Null(Map(env, EventTestKit.Record("'choices': [ { 'id': 'a', 'textKey': 'es.a', 'aiWeight': -1 } ]"), messages));
                Assert.True(EventTestKit.AnyContains(messages, "choices[0].aiWeight"), EventTestKit.Join(messages));
            });
        }

        [Test]
        public void AnEffect_NeedsAType_ParamsAreAnObject_DurationIsAWholeNumber()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                Assert.Null(Map(env, EventTestKit.Record("'effects': [ { 'params': {} } ]"), messages));
                Assert.True(EventTestKit.AnyContains(messages, "effects[0].type"), EventTestKit.Join(messages));

                messages.Clear();
                Assert.Null(Map(env, EventTestKit.Record("'effects': [ 7 ]"), messages));
                Assert.True(EventTestKit.AnyContains(messages, "effects[0]"), EventTestKit.Join(messages));

                messages.Clear();
                Assert.Null(Map(env, EventTestKit.Record("'effects': [ { 'type': 'Reputation', 'params': [1] } ]"), messages));
                Assert.True(EventTestKit.AnyContains(messages, "effects[0].params"), EventTestKit.Join(messages));

                messages.Clear();
                Assert.Null(Map(env, EventTestKit.Record("'effects': [ { 'type': 'Reputation', 'durationDays': -5 } ]"), messages));
                Assert.Null(Map(env, EventTestKit.Record("'effects': [ { 'type': 'Reputation', 'durationDays': 1.5 } ]"), messages));
            });
        }

        [Test]
        public void AnEffectWithoutParams_GetsAnEmptyParamsObject()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                EventDefinition d = Map(env, EventTestKit.Record("'effects': [ { 'type': 'Custom' } ]"), messages);

                Assert.NotNull(d, EventTestKit.Join(messages));
                Assert.NotNull(d.effects[0].@params, "never null for a loaded definition");
                Assert.False(d.effects[0].@params.Has("anything"));
                Assert.NotNull(d.effects[0].@params.Raw);
            });
        }

        // ---- unknown fields ----

        [Test]
        public void AnUnknownField_IsAWarning_NotAnError()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                EventDefinition d = Map(env, EventTestKit.Record("'timerDay': 14, 'trigger2': 1"), messages);

                Assert.NotNull(d, "a typo does not reject the record");
                Assert.Equal(2, messages.Count, EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "unknown field 'timerDay'"), EventTestKit.Join(messages));
            });
        }

        [Test]
        public void TheFieldsOfTheMerger_AndComments_AreNotReported()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                EventDefinition d = Map(env, EventTestKit.Record("'disabled': false, '_note': 'x', 'comment': 'x'"), messages);

                Assert.NotNull(d, EventTestKit.Join(messages));
                Assert.Equal(0, messages.Count, EventTestKit.Join(messages));
            });
        }

        [Test]
        public void AnUnknownFieldInAChoiceAnEffectOrATrigger_IsReportedWithItsPlace()
        {
            EventTestKit.WithWorld(env =>
            {
                var messages = new List<string>();
                EventDefinition d = Map(env, EventTestKit.Record(
                    "'choices': [ { 'id': 'a', 'textKey': 'es.a', 'cost': 5, 'effects': [ { 'type': 'Custom', 'duration': 3 } ] } ]"), messages);

                Assert.NotNull(d, EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "choices[0].cost"), EventTestKit.Join(messages));
                Assert.True(EventTestKit.AnyContains(messages, "choices[0].effects[0].duration"), EventTestKit.Join(messages));
            });
        }
    }
}
