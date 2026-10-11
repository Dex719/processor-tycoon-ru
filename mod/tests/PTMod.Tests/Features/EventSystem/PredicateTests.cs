using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The predicates of the engine (FR-6, D12; design.md "Условия"): registered in the shared catalog under the owner <c>event-system</c> with the prefix
    /// <c>es.</c>, evaluated for the target company; <c>es.activeInCategory</c> with <c>scope = subject</c> and <c>any</c> (AC-6.5); <c>es.hasRevenueHistory</c>,
    /// <c>es.hasFab</c>, the history predicates; the <c>all</c> / <c>any</c> / <c>not</c> syntax in <c>conditions</c>, <c>filter</c> and <c>requires</c>; an unknown
    /// type is an error of the record; a name without the prefix is refused.
    /// </summary>
    [Covers(typeof(BuiltinPredicates), typeof(PredicateRegistrar), typeof(IEventHistory), typeof(EventHistory), typeof(ActiveEventIndex),
        typeof(FiredRecord), typeof(ChoiceRecord), typeof(FlagRecord))]
    public class PredicateTests
    {
        private const int Today = 7000;

        /// <summary>A registry with the core predicates, the prefix rule of the real table, and the six predicates of the engine over the given history and world.</summary>
        private sealed class Catalog
        {
            public readonly PredicateRegistry Registry;
            public readonly ConditionParser Parser;
            public readonly EventHistory History = new EventHistory();
            public readonly FakeWorld World = FakeWorld.WithCompanies(3);
            public readonly int Accepted;

            public Catalog()
            {
                Registry = Predicates.CreateRegistry(ModIds.IsPredicateNameAllowed);
                Parser = new ConditionParser(Registry);
                World.Today = Today;
                Accepted = BuiltinPredicates.Register(Registry.Register, History, World);
            }

            public ICondition Parse(string json)
            {
                var errors = new List<string>();
                JsonNode node = EventTestKit.Obj(json);
                ICondition condition = Parser.Parse(node, errors);
                Assert.True(errors.Count == 0, "parse errors: " + string.Join("; ", errors));
                Assert.NotNull(condition);
                return condition;
            }

            public List<string> Errors(string json)
            {
                var errors = new List<string>();
                Parser.Parse(EventTestKit.Obj(json), errors);
                return errors;
            }

            public bool Eval(ICondition condition, string subject)
            {
                return ConditionEvaluator.Evaluate(condition, new ConditionContext(null, EventDays.ToDate(Today), subject));
            }

            public bool Eval(string json, string subject)
            {
                return Eval(Parse(json), subject);
            }
        }

        private static void WithTable(Action<Catalog> body)
        {
            EventTestKit.WithWorld(env => body(new Catalog()));
        }

        [Test]
        public void ThePredicates_AreRegisteredUnderEventSystem_WithThePrefixEs()
        {
            WithTable(c =>
            {
                Assert.Equal(6, c.Accepted, "all six are accepted");
                Assert.Equal(6, BuiltinPredicates.Names.Length);
                foreach (string name in BuiltinPredicates.Names)
                {
                    Assert.True(name.StartsWith("es.", StringComparison.Ordinal), name);
                    Assert.True(c.Registry.IsKnown(name), name);
                    Assert.Equal("event-system", c.Registry.OwnerOf(name), name);
                }
                Assert.Equal(0, BuiltinPredicates.Register(c.Registry.Register, c.History, c.World), "a second registration of the same names is refused");
            });
        }

        [Test]
        public void ANameWithoutThePrefix_OrWithAForeignPrefix_IsRefused()
        {
            WithTable(c =>
            {
                Func<JsonObject, List<string>, string, ICondition> factory = (node, errors, path) => new DelegateCondition("x", ctx => true);
                Assert.False(c.Registry.Register("activeInCategory", "event-system", factory), "no prefix");
                Assert.False(c.Registry.Register("ms.activeInCategory", "event-system", factory), "the prefix of market-shocks");
                Assert.True(c.Registry.Register("ms.ok", "market-shocks", factory), "its own prefix is fine");
            });
        }

        [Test]
        public void AC_6_5_ActiveInCategory_ScopeAnySeesThePlayersEvent_ScopeSubjectDoesNot()
        {
            WithTable(c =>
            {
                // A history event is going on for the player (a pending choice).
                c.History.Active.Set(31, 0, "he.moon_landing", "history", CompanyKey.Player, true, false);

                ICondition notAny = c.Parse("{ 'not': { 'type': 'es.activeInCategory', 'category': 'history', 'scope': 'any' } }");
                ICondition notSubject = c.Parse("{ 'not': { 'type': 'es.activeInCategory', 'category': 'history', 'scope': 'subject' } }");
                ICondition notDefault = c.Parse("{ 'not': { 'type': 'es.activeInCategory', 'category': 'history' } }");

                Assert.False(c.Eval(notAny, "ai:1"), "the shock for an AI target does not fit while history is going on anywhere");
                Assert.True(c.Eval(notSubject, "ai:1"), "with scope 'subject' and an AI target it fits");
                Assert.True(c.Eval(notDefault, "ai:1"), "'subject' is the default");
                Assert.False(c.Eval(notSubject, CompanyKey.Player), "the player has it");

                c.History.Active.Remove(31);
                Assert.True(c.Eval(notAny, "ai:1"), "nothing going on");
            });
        }

        [Test]
        public void ActiveInCategory_CountsRunningEffects_AndGlobalOnes()
        {
            WithTable(c =>
            {
                c.History.Active.Set(40, 40, "ms.memory_shock", "shocks", EventCompanyKeys.Global, false, true);
                Assert.True(c.Eval("{ 'type': 'es.activeInCategory', 'category': 'shocks', 'scope': 'any' }", CompanyKey.Player), "a global effect counts for 'any'");
                Assert.False(c.Eval("{ 'type': 'es.activeInCategory', 'category': 'shocks' }", CompanyKey.Player), "but is no company's own");

                c.History.Active.Set(41, 0, "es.strike", "universal", "ai:2", false, true);
                Assert.True(c.Eval("{ 'type': 'es.activeInCategory', 'category': 'universal' }", "ai:2"), "a running effect of the subject");
                c.History.Active.Set(41, 0, "es.strike", "universal", "ai:2", false, false);
                Assert.False(c.Eval("{ 'type': 'es.activeInCategory', 'category': 'universal' }", "ai:2"), "over: out of the index");
            });
        }

        [Test]
        public void HasRevenueHistory_AndHasFab_AskTheWorldAboutTheSubject()
        {
            WithTable(c =>
            {
                c.World.RevenueHistory.Add(CompanyKey.Player);
                c.World.Fabs.Add("ai:0");

                Assert.True(c.Eval("{ 'type': 'es.hasRevenueHistory' }", CompanyKey.Player));
                Assert.False(c.Eval("{ 'type': 'es.hasRevenueHistory' }", "ai:0"));
                Assert.True(c.Eval("{ 'type': 'es.hasFab' }", "ai:0"));
                Assert.False(c.Eval("{ 'type': 'es.hasFab' }", CompanyKey.Player));
            });
        }

        [Test]
        public void EventFired_ChoiceMade_EventFlag_ReadTheHistoryOfTheSubject()
        {
            WithTable(c =>
            {
                c.History.RecordFired("es.patent_troll", CompanyKey.Player, Today - 100);
                c.History.RecordChoice("es.patent_troll", "fight", CompanyKey.Player, Today - 90);
                c.History.SetFlag("patent_fought", CompanyKey.Player, Today - 90);

                Assert.True(c.Eval("{ 'type': 'es.eventFired', 'eventId': 'es.patent_troll' }", CompanyKey.Player));
                Assert.True(c.Eval("{ 'type': 'es.eventFired', 'eventId': 'es.patent_troll', 'withinDays': 100 }", CompanyKey.Player));
                Assert.False(c.Eval("{ 'type': 'es.eventFired', 'eventId': 'es.patent_troll', 'withinDays': 99 }", CompanyKey.Player), "longer ago");
                Assert.False(c.Eval("{ 'type': 'es.eventFired', 'eventId': 'es.patent_troll' }", "ai:0"), "another subject");

                Assert.True(c.Eval("{ 'type': 'es.choiceMade', 'eventId': 'es.patent_troll', 'choiceId': 'fight' }", CompanyKey.Player));
                Assert.False(c.Eval("{ 'type': 'es.choiceMade', 'eventId': 'es.patent_troll', 'choiceId': 'settle' }", CompanyKey.Player));

                Assert.True(c.Eval("{ 'type': 'es.eventFlag', 'flag': 'patent_fought' }", CompanyKey.Player));
                c.History.ClearFlag("patent_fought", CompanyKey.Player);
                Assert.False(c.Eval("{ 'type': 'es.eventFlag', 'flag': 'patent_fought' }", CompanyKey.Player));
            });
        }

        [Test]
        public void ParametersAreChecked_WhenTheConditionIsParsed()
        {
            WithTable(c =>
            {
                Assert.True(c.Errors("{ 'type': 'es.eventFired' }").Count > 0, "eventId is required");
                Assert.True(c.Errors("{ 'type': 'es.eventFired', 'eventId': 'es.x', 'withinDays': 0 }").Count > 0, "withinDays >= 1");
                Assert.True(c.Errors("{ 'type': 'es.activeInCategory', 'category': 'history', 'scope': 'world' }").Count > 0, "scope subject | any");
                Assert.True(c.Errors("{ 'type': 'es.hasFab', 'lines': 3 }").Count > 0, "an unknown parameter");
                Assert.True(c.Errors("{ 'type': 'es.choiceMade', 'eventId': 'es.x' }").Count > 0, "choiceId is required");
                Assert.True(c.Errors("{ 'type': 'es.noSuchThing' }").Count > 0, "an unknown type");
            });
        }

        [Test]
        public void AllAnyNot_WorkInConditionsFilterAndRequires_OfARecord()
        {
            EventTestKit.WithWorld(env =>
            {
                var c = new Catalog();
                c.World.RevenueHistory.Add(CompanyKey.Player);
                c.World.Fabs.Add("ai:1");
                var mapper = new DefinitionMapper(c.Parser.Parse);
                var messages = new List<string>();
                EventDefinition definition = mapper.Map(EventTestKit.Obj(EventTestKit.Record(
                    "'conditions': { 'all': [ { 'type': 'es.hasRevenueHistory' }, { 'not': { 'type': 'es.eventFlag', 'flag': 'done' } } ] }, "
                    + "'targets': { 'type': 'RandomCompany', 'filter': { 'any': [ { 'type': 'es.hasFab' }, { 'type': 'es.hasRevenueHistory' } ] } }, "
                    + "'timerDays': 7, 'defaultChoice': 'b', 'choices': [ { 'id': 'a', 'textKey': 'es.a', 'requires': { 'not': { 'type': 'es.hasFab' } } }, "
                    + "{ 'id': 'b', 'textKey': 'es.b' } ]")), messages);
                Assert.NotNull(definition, string.Join("; ", messages));

                Assert.True(c.Eval(definition.conditions, CompanyKey.Player));
                Assert.False(c.Eval(definition.conditions, "ai:1"), "no revenue history");
                c.History.SetFlag("done", CompanyKey.Player, Today);
                Assert.False(c.Eval(definition.conditions, CompanyKey.Player), "the 'not' part");

                Assert.True(c.Eval(definition.targets.filter, "ai:1"), "any: a fab");
                Assert.True(c.Eval(definition.targets.filter, CompanyKey.Player), "any: revenue");
                Assert.False(c.Eval(definition.targets.filter, "ai:0"), "neither");

                Assert.True(c.Eval(definition.choices[0].requires, CompanyKey.Player), "not: no fab");
                Assert.False(c.Eval(definition.choices[0].requires, "ai:1"));
            });
        }

        [Test]
        public void ARecordWithAnUnknownPredicate_IsRefused()
        {
            EventTestKit.WithWorld(env =>
            {
                var c = new Catalog();
                var mapper = new DefinitionMapper(c.Parser.Parse);
                var messages = new List<string>();
                EventDefinition definition = mapper.Map(EventTestKit.Obj(EventTestKit.Record("'conditions': { 'type': 'es.unknownThing' }")), messages);
                Assert.Null(definition, "the record is skipped");
                Assert.True(EventTestKit.AnyContains(messages, "es.unknownThing"), string.Join("; ", messages));
            });
        }

        [Test]
        public void TheIndexOfActiveInstances_CountsPendingChoicesAndGroups()
        {
            var index = new ActiveEventIndex();
            index.Set(1, 10, "es.all", "universal", CompanyKey.Player, true, false);
            index.Set(2, 10, "es.all", "universal", "ai:0", false, true);
            index.Set(3, 0, "es.solo", "universal", CompanyKey.Player, true, true);
            index.Set(4, 0, "es.other", "rivals", "ai:1", false, true);

            Assert.Equal(4, index.Count);
            Assert.Equal(2, index.PendingChoices(CompanyKey.Player));
            Assert.Equal(0, index.PendingChoices("ai:0"));
            Assert.Equal(2, index.ActiveInCategory("universal"), "the group counts once");
            Assert.Equal(1, index.ActiveInCategory("rivals"));
            Assert.Equal(0, index.ActiveInCategory(null));

            index.Set(3, 0, "es.solo", "universal", CompanyKey.Player, false, false);
            Assert.Equal(3, index.Count, "neither waiting nor running: removed");
            index.Clear();
            Assert.Equal(0, index.Count);
        }

        [Test]
        public void TheHistory_RoundTripsThroughItsSaveRecords()
        {
            var history = new EventHistory();
            history.RecordFired("es.a", CompanyKey.Player, 100);
            history.RecordFired("es.a", "ai:3", 120);
            history.RecordChoice("es.a", "x", "ai:3", 121);
            history.SetFlag("f", CompanyKey.Player, 130);
            history.Active.Set(5, 0, "es.a", "u", CompanyKey.Player, true, false);

            var dto = new EventSystemSaveDto();
            history.Export(dto);
            Assert.Equal(2, dto.fired.Count);
            Assert.Equal("ai:3", dto.fired[0].companyKey, "sorted by event, then company: 'ai:3' before 'player'");

            dto.fired.Add(new FiredRecord { eventId = "es.bad", companyKey = "bank", lastDay = 1 });
            dto.choices.Add(new ChoiceRecord { eventId = "es.a", choiceId = "", companyKey = CompanyKey.Player });
            dto.flags.Add(new FlagRecord { flag = "g", companyKey = null });
            var copy = new EventHistory();
            var problems = new List<string>();
            copy.Import(dto, problems);

            Assert.Equal(3, problems.Count, "three bad records dropped");
            Assert.True(copy.HasFired("es.a", "ai:3", 0, 200));
            Assert.True(copy.HasChosen("es.a", "x", "ai:3"));
            Assert.True(copy.HasFlag("f", CompanyKey.Player));
            Assert.False(copy.HasFired("es.bad", "bank", 0, 200));
            Assert.Equal(0, copy.Active.Count, "the index is not saved: the runtime rebuilds it");
        }
    }
}
