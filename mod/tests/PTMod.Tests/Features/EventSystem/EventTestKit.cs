using System;
using System.Collections.Generic;
using System.IO;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.EventSystem
{
    // Shared by the test files of event-system. Everything here is internal, so the runner (which looks at public classes) never takes it for a test.

    /// <summary>The names of the fixtures and the helpers that read them.</summary>
    internal static class EventTestKit
    {
        /// <summary>
        /// The ten events of the starter set. Not a fixture any more: <see cref="Fixture"/> hands out the shipped <c>Content/event-system/events.json</c> for this name
        /// (TSK-025), so the tests that need "the ten events" read the file the game loads, and a copy cannot drift from it.
        /// </summary>
        public const string StarterEvents = "events.starter.json";
        public const string BrokenEvents = "events.broken.json";
        public const string OverrideEvents = "events.override.json";
        public const string ForeignPrefixEvents = "events.foreign_prefix.json";
        public const string MarketShocksConfig = "config.market-shocks.json";

        /// <summary>The text of a fixture from <c>tests/PTMod.Tests/Features/EventSystem/Fixtures</c>; for <see cref="StarterEvents"/> the shipped file of the starter set.</summary>
        public static string Fixture(string name)
        {
            if (string.Equals(name, StarterEvents, StringComparison.Ordinal)) return ShippedContent("event-system", "events.json");
            Assert.NotNull(TestEnv.ModDir, "mod folder not found; run the tests through python mod/build_feature.py event-system --tests");
            string path = Path.Combine(TestEnv.ModDir, "tests", "PTMod.Tests", "Features", "EventSystem", "Fixtures", name);
            Assert.True(File.Exists(path), "no fixture " + path);
            return SourceScan.ReadText(path);
        }

        /// <summary>The text of a file of the shipped content: <c>Content/event-system/config.json</c>.</summary>
        public static string ShippedContent(string folder, string file)
        {
            string src = SourceScan.SrcDirOrFail();
            string path = Path.Combine(src, "Content", folder, file);
            Assert.True(File.Exists(path), "no content file " + path);
            return SourceScan.ReadText(path);
        }

        /// <summary>
        /// A catalog with the shipped starter set read into it: <c>config.json</c> and <c>events.json</c> of <c>Content/event-system</c>, the pack <c>event-system</c> registered
        /// the way the engine registers its own.
        /// </summary>
        public static void WithStarterCatalog(Action<FakeEnvironment, LogCapture, PackCatalog> body)
        {
            WithCatalog((env, source, log, catalog) =>
            {
                source.Add("event-system", "config", ShippedContent("event-system", "config.json"));
                source.Add("event-system", "events", ShippedContent("event-system", "events.json"));
                catalog.RegisterPack("event-system", "event-system", new PackOptions());
                catalog.LoadPending();
                body(env, log, catalog);
            });
        }

        /// <summary>Parses a JSON object; single quotes stand for double quotes, so the test text stays readable.</summary>
        public static JsonObject Obj(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text.Replace('\'', '"'), out node, out error), "test JSON is invalid: " + error + "\n" + text);
            var obj = node as JsonObject;
            Assert.NotNull(obj, "test JSON is not an object: " + text);
            return obj;
        }

        /// <summary>The <c>prefixes</c> table of the shipped <c>core.json</c> (17 rows: <c>es</c>, <c>ms</c>, <c>dfr</c>...).</summary>
        public static JsonObject CorePrefixes()
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(ShippedContent("mod-framework", "core.json"), out node, out error), "core.json is not valid JSON: " + error);
            JsonObject prefixes = ((JsonObject)node).GetObject("prefixes");
            Assert.NotNull(prefixes, "core.json lacks 'prefixes'");
            return prefixes;
        }

        /// <summary>
        /// Runs a test with the real prefix table loaded (<c>ModIds</c> is global state) and a fake environment built on it, and empties the table again.
        /// </summary>
        public static void WithWorld(Action<FakeEnvironment> body)
        {
            try
            {
                ModIds.Load(CorePrefixes());
                body(new FakeEnvironment());
            }
            finally
            {
                ModIds.Clear();
            }
        }

        /// <summary>The same with a catalog over a fake source.</summary>
        public static void WithCatalog(Action<FakeEnvironment, FakeSource, LogCapture, PackCatalog> body)
        {
            WithWorld(env =>
            {
                var source = new FakeSource();
                var log = new LogCapture();
                body(env, source, log, new PackCatalog(source, env, log.Warn, log.Info));
            });
        }

        public static EventDefinition Map(FakeEnvironment env, string json, List<string> messages)
        {
            return new DefinitionMapper(env.ParseCondition).Map(Obj(json), messages);
        }

        /// <summary>A pack's validator over the fake world; every category is defined unless <paramref name="categories"/> says which are.</summary>
        public static ContentValidator Validator(FakeEnvironment env, string pack, params string[] categories)
        {
            var defined = new HashSet<string>(categories ?? new string[0], StringComparer.Ordinal);
            return new ContentValidator(env, pack, pack, categories == null || categories.Length == 0 ? (Func<string, bool>)null : defined.Contains);
        }

        /// <summary>Maps and validates one record of a pack. The messages are the errors when the result is false, the warnings otherwise.</summary>
        public static bool Check(FakeEnvironment env, string pack, string recordJson, List<string> messages, params string[] categories)
        {
            var mapped = new List<string>();
            EventDefinition definition = Map(env, recordJson, mapped);
            if (definition == null)
            {
                messages.AddRange(mapped);
                return false;
            }
            return Validator(env, pack, categories).Validate(definition, messages);
        }

        /// <summary>The messages joined, for a failure message and for <c>Contains</c> checks.</summary>
        public static string Join(IEnumerable<string> lines)
        {
            return string.Join("\n  ", lines);
        }

        public static bool AnyContains(IEnumerable<string> lines, string part)
        {
            foreach (string line in lines)
            {
                if (line.IndexOf(part, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        /// <summary>A record that is valid for the pack <c>event-system</c>, with the slot <c>{0}</c> for extra members.</summary>
        public static string Record(string extra)
        {
            return "{ 'id': 'es.sample', 'category': 'universal', 'title': { 'key': 'es.sample.title' }, 'text': { 'key': 'es.sample.text' }, "
                   + "'trigger': { 'type': 'DailyChance' }" + (string.IsNullOrEmpty(extra) ? string.Empty : ", " + extra) + " }";
        }
    }

    /// <summary>The log lines a catalog wrote.</summary>
    internal sealed class LogCapture
    {
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Infos = new List<string>();

        public void Warn(string line)
        {
            Warnings.Add(line);
        }

        public void Info(string line)
        {
            Infos.Add(line);
        }
    }

    /// <summary>
    /// What the running mod would say, from tables the test fills: effect types, channels, money origins, handler verdicts. Conditions are parsed by the
    /// core's own parser over a registry that holds the core predicates and two of the engine's (<c>es.hasRevenueHistory</c>, <c>es.hasFab</c>) registered the way
    /// <c>EventConditions.Register</c> does; the registry applies the prefix rule of <see cref="ModIds"/>, so the table must be loaded first (<see cref="EventTestKit.WithWorld"/>).
    /// </summary>
    internal sealed class FakeEnvironment : IContentEnvironment
    {
        public readonly HashSet<string> EffectTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Money", "ModifierTimed", "FactoryOutput", "MarketDemand", "Popularity", "Reputation", "ResearchProgress", "SendMail", "FireEvent", "Custom",
        };

        public readonly HashSet<string> Channels = new HashSet<string>(StringComparer.Ordinal)
        {
            "research.speed", "popularity.gain", "production.output", "factory.operatingCost", "market.demand.highend", "market.demand.lowend",
        };

        public readonly HashSet<string> ExpenseOrigins = new HashSet<string>(StringComparer.Ordinal)
        {
            "Factories", "Outsourcing", "Royalties", "Research", "Development", "Construction", "Interest", "Fines", "Taxes",
        };

        public readonly HashSet<string> RevenueOrigins = new HashSet<string>(StringComparer.Ordinal)
        {
            "Sales", "Contracts", "Clients", "Royalties", "Interest",
        };

        /// <summary>The verdict of the handler of every effect; the test replaces it.</summary>
        public Func<EventEffect, IList<string>, bool> HandlerVerdict = (effect, errors) => true;

        public readonly List<string> HandlerCalls = new List<string>();
        public readonly PredicateRegistry Registry;
        private readonly ConditionParser _parser;

        public FakeEnvironment()
        {
            Registry = Predicates.CreateRegistry(ModIds.IsPredicateNameAllowed);
            _parser = new ConditionParser(Registry);
            RegisterFlag("es.hasRevenueHistory");
            RegisterFlag("es.hasFab");
        }

        /// <summary>Registers a predicate without parameters; false when the registry refuses the name.</summary>
        public bool RegisterFlag(string type, string owner = "event-system")
        {
            return Registry.Register(type, owner, (node, errors, path) =>
            {
                var args = new ConditionArgs(node, errors, path);
                return args.Finish() ? new DelegateCondition(type, ctx => true) : null;
            });
        }

        public ICondition ParseCondition(JsonNode node, List<string> errors, string path)
        {
            return _parser.Parse(node, errors, path);
        }

        public bool IsEffectTypeRegistered(string type)
        {
            return type != null && EffectTypes.Contains(type);
        }

        public bool ValidateWithHandler(EventEffect effect, IList<string> errors)
        {
            HandlerCalls.Add(effect.type);
            return HandlerVerdict(effect, errors);
        }

        public bool IsChannelDeclared(string channel)
        {
            return channel != null && Channels.Contains(channel);
        }

        public bool IsOriginName(bool spend, string name)
        {
            return name != null && (spend ? ExpenseOrigins : RevenueOrigins).Contains(name);
        }
    }

    /// <summary>
    /// The files of the packs as text; the merge with the player's override is the core's own <see cref="ContentMerger"/>, the same code <c>ModContent</c> runs,
    /// so a test sees what the game would load.
    /// </summary>
    internal sealed class FakeSource : IPackContentSource
    {
        private readonly Dictionary<string, string> _builtin = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public readonly List<ContentReport> Reports = new List<ContentReport>();
        public readonly List<string> Loaded = new List<string>();

        /// <summary>When set, <c>LoadRecords</c> throws this (a source that fails).</summary>
        public Exception FailWith;

        /// <summary>Adds a built-in file: <c>Add("event-system", "events", text)</c> is the resource <c>content/event-system/events.json</c>.</summary>
        public FakeSource Add(string folder, string file, string text)
        {
            _builtin[Key(folder, file)] = text;
            return this;
        }

        /// <summary>Adds the player's file <c>PTMod/content/&lt;folder&gt;/&lt;file&gt;.json</c>.</summary>
        public FakeSource Override(string folder, string file, string text)
        {
            _overrides[Key(folder, file)] = text;
            return this;
        }

        public JsonObject LoadConfig(string folder, string file, int schema)
        {
            Loaded.Add(folder + "/" + file + ".json (config)");
            string text;
            if (!_builtin.TryGetValue(Key(folder, file), out text)) return null;

            string over;
            _overrides.TryGetValue(Key(folder, file), out over);
            ConfigLoad load = ContentMerger.MergeConfig(folder, file, text, over, schema);
            Reports.Add(load.Report);
            return load.Config;
        }

        public IReadOnlyList<T> LoadRecords<T>(string folder, string file, Func<JsonObject, List<string>, T> map,
            Func<T, List<string>, bool> validate, int schema) where T : class, IContentRecord
        {
            Loaded.Add(folder + "/" + file + ".json");
            if (FailWith != null) throw FailWith;

            string text, over;
            _builtin.TryGetValue(Key(folder, file), out text);
            _overrides.TryGetValue(Key(folder, file), out over);
            ContentLoad<T> load = ContentMerger.MergeRecords(folder, file, text, over, map, validate, schema);
            Reports.Add(load.Report);
            return load.Records;
        }

        private static string Key(string folder, string file)
        {
            return folder + "/" + ContentMerger.NormalizeFile(file);
        }
    }
}
