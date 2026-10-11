using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>The events of the pack as a set of ids.</summary>
    internal sealed class FakeEvents : IEventCatalog
    {
        private readonly HashSet<string> _ids;

        public FakeEvents(params string[] ids)
        {
            _ids = new HashSet<string>(ids, StringComparer.Ordinal);
        }

        public bool Contains(string eventId)
        {
            return eventId != null && _ids.Contains(eventId);
        }
    }

    /// <summary>The string tables: every key has a text, or only the listed ones.</summary>
    internal sealed class FakeKeys : IL10nKeys
    {
        private readonly HashSet<string> _keys;

        public FakeKeys(params string[] keys)
        {
            _keys = keys.Length == 0 ? null : new HashSet<string>(keys, StringComparer.Ordinal);
        }

        public bool Has(string key)
        {
            return _keys == null || _keys.Contains(key);
        }
    }

    /// <summary>The outcome of one validation: the verdict and the lines.</summary>
    internal sealed class Verdict
    {
        public bool Ok;
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();

        public bool HasError(string fragment)
        {
            return Errors.Any(e => e.IndexOf(fragment, StringComparison.Ordinal) >= 0);
        }

        public bool HasWarning(string fragment)
        {
            return Warnings.Any(e => e.IndexOf(fragment, StringComparison.Ordinal) >= 0);
        }

        public string Text
        {
            get { return "errors: [" + string.Join(" | ", Errors) + "] warnings: [" + string.Join(" | ", Warnings) + "]"; }
        }
    }

    /// <summary>Entries, registries and the identifier table for the tests of the calendar.</summary>
    internal static class HistFixture
    {
        // ---- JSON ----

        public static JsonNode Node(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text.Replace('\'', '"'), out node, out error), "test JSON is invalid: " + error);
            return node;
        }

        public static JsonObject Obj(string text)
        {
            return (JsonObject)Node(text);
        }

        /// <summary>
        /// A JSON file as it is on disk: no quote substitution (<see cref="Node"/> turns every apostrophe into a quote, which is a convenience for the small samples of
        /// the tests and breaks a real text such as "the regulator's fine").
        /// </summary>
        public static JsonObject ObjFromFile(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "the file is invalid JSON: " + error);
            Assert.True(node is JsonObject, "the file must hold an object");
            return (JsonObject)node;
        }

        // ---- entries as the design writes them ----

        /// <summary>Example 1 of design.md: a boom with a catch-up effect, no roles, no conditions.</summary>
        public const string Plain =
            "{ 'id': 'hist.1995.win95', '_note': 'Windows 95, 24.08.1995', 'category': 'boom', 'major': false, 'catchUp': true," +
            "  'anchor': '1995-08-24', 'window': { 'from': '1995-08-01', 'to': '1996-03-31' }, 'jitterDays': 10," +
            "  'roles': [], 'conditions': []," +
            "  'stages': [ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.1995.win95', 'role': 'global' } ] } ]," +
            "  'fallback': 'skip'," +
            "  'history': { 'titleKey': 'hist.1995.win95.title', 'factKey': 'hist.1995.win95.fact'," +
            "               'sources': ['https://en.wikipedia.org/wiki/Windows_95'] } }";

        /// <summary>Example 2 of design.md: an antitrust case against the leader, with a condition on the role.</summary>
        public const string Fine =
            "{ 'id': 'hist.2009.eufine', 'category': 'antitrust', 'major': true, 'catchUp': false," +
            "  'anchor': '2009-05-13', 'window': { 'from': '2009-05-01', 'to': '2010-12-31' }, 'jitterDays': 15," +
            "  'roles': [ { 'name': 'target', 'kind': 'leader', 'segments': ['all'], 'months': 36, 'minShare': 0.55 } ]," +
            "  'conditions': [ { 'type': 'hist.ShareAtLeast', 'role': 'target', 'value': 0.55, 'months': 36 } ]," +
            "  'stages': [ { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.decide.antitrust.fine', 'role': 'target', 'extraArgs': ['eu2009'] } ] } ]," +
            "  'fallback': 'news'," +
            "  'history': { 'titleKey': 'hist.2009.eufine.title', 'factKey': 'hist.2009.eufine.fact'," +
            "               'sources': ['https://gulfnews.com/a', 'https://www.news4jax.com/b'] } }";

        /// <summary>Example 3 of design.md: a named company with a fallback chain, two stages and a flag.</summary>
        public const string Jiji =
            "{ 'id': 'hist.1999.jiji', 'category': 'disaster', 'major': true, 'catchUp': false," +
            "  'anchor': '1999-09-21', 'window': { 'from': '1999-09-01', 'to': '2000-03-31' }, 'jitterDays': 0," +
            "  'roles': [ { 'name': 'target', 'kind': 'named', 'uniqueId': 9, 'requireFoundry': true, 'fallback': [ { 'kind': 'foundryLeader' } ] } ]," +
            "  'conditions': []," +
            "  'stages': [" +
            "    { 'offsetDays': 0,  'fires': [ { 'eventId': 'hist.1999.jiji.hit', 'role': 'target' }, { 'eventId': 'hist.1999.jiji.market', 'role': 'global' } ] }," +
            "    { 'offsetDays': 30, 'fires': [ { 'eventId': 'hist.1999.jiji.recovery', 'role': 'target' } ], 'skipIfFlag': 'recovered' } ]," +
            "  'fallback': 'news'," +
            "  'history': { 'titleKey': 'hist.1999.jiji.title', 'factKey': 'hist.1999.jiji.fact', 'sources': ['https://en.wikipedia.org/wiki/1999_Jiji_earthquake'] } }";

        /// <summary>Example 4 of design.md: a group role and an onlyIf condition on each fire.</summary>
        public const string Export =
            "{ 'id': 'hist.2022.exportcontrol', 'category': 'export', 'major': true, 'catchUp': true," +
            "  'anchor': '2022-10-07', 'window': { 'from': '2022-10-01', 'to': '2023-06-30' }, 'jitterDays': 0," +
            "  'roles': [], 'conditions': []," +
            "  'stages': [" +
            "    { 'offsetDays': 0, 'fires': [ { 'eventId': 'hist.2022.export.rule', 'role': 'global', 'extraArgs': ['us2022'] }," +
            "        { 'eventId': 'hist.decide.export', 'role': 'allCpuCompanies', 'extraArgs': ['us2022']," +
            "          'onlyIf': { 'type': 'hist.PlayerHasCpuAbove', 'ruleId': 'us2022', 'anyCompany': true } } ] }," +
            "    { 'offsetDays': 375, 'fires': [ { 'eventId': 'hist.2023.export.update', 'role': 'global', 'extraArgs': ['us2022'] } ] } ]," +
            "  'fallback': 'skip'," +
            "  'history': { 'titleKey': 'hist.2022.exportcontrol.title', 'factKey': 'hist.2022.exportcontrol.fact', 'sources': ['https://www.csis.org/analysis/x'] } }";

        /// <summary>Every event the four examples fire.</summary>
        public static FakeEvents AllEvents()
        {
            return new FakeEvents(
                "hist.1995.win95", "hist.decide.antitrust.fine", "hist.1999.jiji.hit", "hist.1999.jiji.market", "hist.1999.jiji.recovery",
                "hist.2022.export.rule", "hist.decide.export", "hist.2023.export.update");
        }

        // ---- the registry of predicates ----

        /// <summary>The core predicates plus the ones the design names for the feature and the engine, and one of another feature (<c>ms.quake</c>).</summary>
        public static PredicateRegistry Registry()
        {
            PredicateRegistry registry = Predicates.CreateRegistry();
            foreach (string name in new[] { "hist.ShareAtLeast", "hist.HasCpuSince", "hist.CaseActive", "hist.PlayerHasCpuAbove", "hist.IsFabless" })
                Assert.True(registry.Register(name, HistIds.Feature, Factory(name)), name);
            foreach (string name in new[] { "es.hasFab", "es.activeInCategory" })
                Assert.True(registry.Register(name, "event-system", Factory(name)), name);
            Assert.True(registry.Register("ms.quake", "market-shocks", Factory("ms.quake")), "ms.quake");
            return registry;
        }

        private static Func<JsonObject, List<string>, string, ICondition> Factory(string name)
        {
            return (node, errors, path) => new DelegateCondition(name, ctx => true);
        }

        public static Func<JsonNode, List<string>, string, ICondition> Parser()
        {
            var parser = new ConditionParser(Registry());
            return parser.Parse;
        }

        // ---- mapping and judging ----

        /// <summary>Maps a record with the test registry; the lines (errors and warnings of the mapper) go to <paramref name="messages"/>.</summary>
        public static CalendarEntry Map(JsonObject json, List<string> messages)
        {
            return CalendarEntry.FromJson(json, messages, Parser());
        }

        /// <summary>Maps a record that must map; unknown-member warnings are allowed.</summary>
        public static CalendarEntry Entry(string json, Action<JsonObject> tweak = null)
        {
            JsonObject obj = Obj(json);
            if (tweak != null) tweak(obj);

            var messages = new List<string>();
            CalendarEntry entry = Map(obj, messages);
            Assert.NotNull(entry, "the record must map: " + string.Join(" | ", messages));
            return entry;
        }

        /// <summary>The first record of a test pair: the entry and what the validator says about it (all events exist, all texts exist unless given).</summary>
        public static Verdict Judge(string json, Action<JsonObject> tweak = null, IEventCatalog events = null, IL10nKeys keys = null)
        {
            return Judge(Entry(json, tweak), events ?? AllEvents(), keys);
        }

        public static Verdict Judge(CalendarEntry entry, IEventCatalog events, IL10nKeys keys)
        {
            var verdict = new Verdict();
            WithIds(() => { verdict.Ok = CalendarValidator.Validate(entry, events, keys, verdict.Errors, verdict.Warnings); });
            return verdict;
        }

        // ---- the table of identifiers ----

        /// <summary>The <c>prefixes</c> object of the shipped <c>core.json</c>.</summary>
        public static JsonObject CorePrefixes()
        {
            string text = SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Content", "mod-framework", "core.json"));
            JsonObject core = Obj(text);
            JsonObject prefixes = core.GetObject("prefixes");
            Assert.NotNull(prefixes, "core.json lacks 'prefixes'");
            return prefixes;
        }

        /// <summary>Loads the real table of identifiers, runs the body and empties the table again (it is global state).</summary>
        public static void WithIds(Action body)
        {
            var problems = new List<string>();
            try
            {
                ModIds.Load(CorePrefixes(), problems);
                Assert.Equal(0, problems.Count, "the table of identifiers must load: " + string.Join(" | ", problems));
                body();
            }
            finally
            {
                ModIds.Clear();
            }
        }
    }
}
