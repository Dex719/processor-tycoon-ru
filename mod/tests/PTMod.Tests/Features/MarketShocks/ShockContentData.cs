using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.MarketShocks;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// Shared helpers of the content tests of market-shocks (TSK-009 ... TSK-014): the shipped files <c>Content/market-shocks/config.json</c> and
    /// <c>shocks.json</c>, the strings <c>L10n/{ru,en}/market-shocks.json</c>, <c>core.json</c> (the prefixes and the channels), a parser of conditions over the
    /// core's registry with the predicates of the event system stubbed (the real ones live in the engine, which this build does not include), and the
    /// one-line "signature" of an effect that the catalog test compares with the table of design.md. Not a test and not a logic type.
    /// </summary>
    internal static class ShockContentData
    {
        public const string Feature = "market-shocks";

        private static string SrcFile(params string[] parts)
        {
            string path = SourceScan.SrcDirOrFail();
            foreach (string part in parts) path = Path.Combine(path, part);
            Assert.True(File.Exists(path), "no file " + path);
            return path;
        }

        public static JsonObject ReadObject(params string[] parts)
        {
            string path = SrcFile(parts);
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(SourceScan.ReadText(path), out node, out error), path + " is not valid JSON: " + error);
            var obj = node as JsonObject;
            Assert.NotNull(obj, path + ": the root must be a JSON object");
            return obj;
        }

        /// <summary><c>Content/market-shocks/config.json</c>.</summary>
        public static JsonObject Config()
        {
            return ReadObject("Content", Feature, "config.json");
        }

        /// <summary>The records of <c>Content/market-shocks/shocks.json</c>, in the order of the file.</summary>
        public static List<JsonObject> Records()
        {
            JsonObject file = ReadObject("Content", Feature, "shocks.json");
            JsonArray array = file.GetArray("records");
            Assert.NotNull(array, "shocks.json has no 'records'");
            var list = new List<JsonObject>();
            foreach (JsonNode node in array)
            {
                var record = node as JsonObject;
                Assert.NotNull(record, "a record of shocks.json is not an object");
                list.Add(record);
            }
            return list;
        }

        public static JsonObject Record(string id)
        {
            foreach (JsonObject record in Records())
            {
                if (record.GetString("id", null) == id) return record;
            }
            Assert.Fail("no record " + id + " in shocks.json");
            return null;
        }

        /// <summary><c>Content/mod-framework/core.json</c> (the prefixes of the features and the channels of the core).</summary>
        public static JsonObject Core()
        {
            return ReadObject("Content", "mod-framework", "core.json");
        }

        /// <summary>The names of the channels the core declares in <c>core.json</c>.</summary>
        public static HashSet<string> CoreChannels()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            JsonArray channels = Core().GetArray("channels");
            Assert.NotNull(channels, "core.json has no 'channels'");
            foreach (JsonNode node in channels)
            {
                var channel = node as JsonObject;
                if (channel != null) names.Add(channel.GetString("name", string.Empty));
            }
            return names;
        }

        /// <summary>The strings of the feature in one language: key to text, in the order of the file.</summary>
        public static Dictionary<string, string> Strings(string language)
        {
            JsonObject file = ReadObject("L10n", language, Feature + ".json");
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonNode> pair in file)
            {
                var value = pair.Value as JsonValue;
                Assert.True(value != null && value.Kind == JsonKind.String, language + ":" + pair.Key + " is not a string");
                strings[pair.Key] = value.StringValue;
            }
            return strings;
        }

        /// <summary>Runs a body with the real prefix table loaded (<c>ModIds</c> is global state) and empties the table afterwards.</summary>
        public static void WithPrefixes(Action body)
        {
            try
            {
                ModIds.Load(Core().GetObject("prefixes"));
                body();
            }
            finally
            {
                ModIds.Clear();
            }
        }

        // ---- the effects of a record ----

        /// <summary>The effects of the event itself, then those of each choice in order; the second element is the choice id ("" for the event's own).</summary>
        public static List<KeyValuePair<string, JsonObject>> AllEffects(JsonObject record)
        {
            var list = new List<KeyValuePair<string, JsonObject>>();
            AddEffects(list, string.Empty, record.GetArray("effects"));
            JsonArray choices = record.GetArray("choices");
            if (choices != null)
            {
                foreach (JsonNode node in choices)
                {
                    var choice = (JsonObject)node;
                    AddEffects(list, choice.GetString("id", string.Empty), choice.GetArray("effects"));
                }
            }
            return list;
        }

        private static void AddEffects(List<KeyValuePair<string, JsonObject>> into, string owner, JsonArray effects)
        {
            if (effects == null) return;
            foreach (JsonNode node in effects) into.Add(new KeyValuePair<string, JsonObject>(owner, (JsonObject)node));
        }

        public static JsonObject ParamsOf(JsonObject effect)
        {
            return effect.GetObject("params") ?? new JsonObject();
        }

        // ---- signatures ----

        /// <summary>A number the way the signatures write it: no trailing zeros, always a point.</summary>
        public static string Num(double value)
        {
            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        /// <summary>A number or a pair <c>[lo,hi]</c> of the JSON node.</summary>
        public static string Range(JsonNode node)
        {
            var array = node as JsonArray;
            if (array != null)
            {
                var parts = new List<string>();
                foreach (JsonNode item in array) parts.Add(Range(item));
                return "[" + string.Join(",", parts.ToArray()) + "]";
            }

            var value = node as JsonValue;
            Assert.True(value != null && value.Kind == JsonKind.Number, "a number was expected, got " + (node == null ? "nothing" : node.Kind.ToString()));
            return Num(value.NumberValue);
        }

        /// <summary>
        /// One line that carries every number of an effect, in the form the catalog test writes them: <c>Ramp production.memoryCost 1.6 30/90/90 Global S</c>
        /// (the trailing <c>S</c> is <c>scalable: true</c>). The numbers are read from the file, not from a table of the code.
        /// </summary>
        public static string Signature(JsonObject effect)
        {
            string type = effect.GetString("type", string.Empty);
            JsonObject p = ParamsOf(effect);
            int days = effect.GetInt32("durationDays", 0);
            var sb = new StringBuilder();

            switch (type)
            {
                case "Money":
                    sb.Append("Money ").Append(p.GetString("direction", "?")).Append(' ').Append(p.GetString("origin", "?"))
                      .Append(" f=").Append(Range(p["revenueFraction"]))
                      .Append(' ').Append(Num(p.GetDouble("min"))).Append("..").Append(Num(p.GetDouble("max")));
                    break;
                case "FactoryOutput":
                    sb.Append("FactoryOutput x").Append(Num(p.GetDouble("multiplier"))).Append(' ').Append(days).Append('d');
                    break;
                case "ModifierTimed":
                    sb.Append("Timed ").Append(p.GetString("channel", "?")).Append(" x").Append(Num(p.GetDouble("multiplier"))).Append(' ').Append(days).Append('d');
                    break;
                case "ModifierRamp":
                    sb.Append("Ramp ").Append(p.GetString("channel", "?")).Append(' ').Append(Num(p.GetDouble("peak"))).Append(' ')
                      .Append(p.GetInt32("rampIn")).Append('/').Append(p.GetInt32("hold")).Append('/').Append(p.GetInt32("rampOut"));
                    break;
                case "ModifierWave":
                    sb.Append("Wave ").Append(p.GetString("channel", "?")).Append(" A").Append(Num(p.GetDouble("amplitude"))).Append(" P").Append(p.GetInt32("period"))
                      .Append(' ').Append(PhaseText(p["phase"])).Append(' ').Append(p.GetString("phaseGroup", "-"));
                    break;
                case "InventoryLoss":
                    sb.Append("Loss ").Append(Range(p["fraction"]));
                    break;
                case "FireEvent":
                    sb.Append("Fire ").Append(p.GetString("eventId", "?")).Append(" +").Append(Range(p["delayDays"])).Append(" p").Append(Num(p.GetDouble("chance", 1.0)))
                      .Append(' ').Append(p.GetString("scope", "Target")).Append(' ').Append(p.GetString("target", "?"));
                    return sb.ToString();
                case "Popularity":
                    sb.Append("Popularity +").Append(Range(p["delta"])).Append(' ').Append(p.GetString("cpuSelector", "?"));
                    break;
                default:
                    sb.Append(type).Append(" (unknown type)");
                    break;
            }

            if (type != "FireEvent" && p.ContainsKey("chance")) sb.Append(" p").Append(Num(p.GetDouble("chance")));
            if (type != "FireEvent" && p.GetString("scope", null) != null) sb.Append(' ').Append(p.GetString("scope", string.Empty));
            if (p.GetBool("scalable", false)) sb.Append(" S");
            return sb.ToString();
        }

        private static string PhaseText(JsonNode phase)
        {
            var value = phase as JsonValue;
            if (value == null) return "0";
            return value.Kind == JsonKind.String ? value.StringValue : Num(value.NumberValue);
        }

        // ---- conditions ----

        /// <summary>
        /// A parser over the core's registry (the core predicates) with the three predicates of the event system that the pack uses stubbed (they are true when
        /// asked, <c>es.activeInCategory</c> is false: nothing historical is running) and the two predicates of the pack with their own parameter checks.
        /// The prefix table must be loaded (<see cref="WithPrefixes"/>): the registry applies the naming rule.
        /// </summary>
        public static ConditionParser Parser()
        {
            PredicateRegistry registry = Predicates.CreateRegistry(ModIds.IsPredicateNameAllowed);
            Assert.True(registry.Register("es.hasRevenueHistory", "event-system", Flag("es.hasRevenueHistory", true)), "registry refused es.hasRevenueHistory");
            Assert.True(registry.Register("es.hasFab", "event-system", Flag("es.hasFab", true)), "registry refused es.hasFab");
            Assert.True(registry.Register("es.activeInCategory", "event-system", (node, errors, path) =>
            {
                var args = new ConditionArgs(node, errors, path);
                args.RequireString("category");
                args.OptionalChoice("scope", "subject", "subject", "any");
                return args.Finish() ? new DelegateCondition("es.activeInCategory", ctx => false) : null;
            }), "registry refused es.activeInCategory");

            foreach (string name in ShockRegistration.PredicateNames)
            {
                string type = name;
                Assert.True(registry.Register(type, Feature, (node, errors, path) =>
                {
                    var problems = new List<string>();
                    if (!ShockPredicateLogic.Validate(node, problems))
                    {
                        foreach (string problem in problems) errors.Add(path + ": " + problem);
                        return null;
                    }
                    return new DelegateCondition(type, ctx => true);
                }), "registry refused " + type);
            }
            return new ConditionParser(registry);
        }

        private static Func<JsonObject, List<string>, string, ICondition> Flag(string type, bool value)
        {
            return (node, errors, path) =>
            {
                var args = new ConditionArgs(node, errors, path);
                return args.Finish() ? new DelegateCondition(type, ctx => value) : null;
            };
        }

        /// <summary>Parses a condition node of a record; the errors are the test's failure.</summary>
        public static ICondition Parse(ConditionParser parser, JsonNode node, string where)
        {
            var errors = new List<string>();
            ICondition condition = parser.Parse(node, errors, where);
            Assert.True(condition != null && errors.Count == 0, where + ": " + string.Join("; ", errors.ToArray()));
            return condition;
        }

        /// <summary>The condition of a record evaluated on a date, from the point of view of the player (null condition: true).</summary>
        public static bool HoldsOn(ConditionParser parser, JsonObject record, DateTime date)
        {
            JsonNode node;
            if (!record.TryGetValue("conditions", out node)) return true;
            ICondition condition = Parse(parser, node, "$." + record.GetString("id", "?") + ".conditions");
            return condition.Evaluate(new ConditionContext(null, date, "player"));
        }
    }
}
