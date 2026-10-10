using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;

namespace PTMod.Core.Logic.Conditions
{
    /// <summary>Which values the <c>company</c> parameter of a predicate accepts.</summary>
    public enum CompanyScope
    {
        /// <summary><c>player</c>, <c>ai:&lt;id&gt;</c> or <c>anyAI</c>; absent means the subject of the context (the player by default).</summary>
        PlayerOrAi = 0,

        /// <summary><c>ai:&lt;id&gt;</c> or <c>anyAI</c> (the predicate is about rivals); absent means <c>anyAI</c>.</summary>
        AiOrAnyAi = 1,

        /// <summary>Exactly one rival, <c>ai:&lt;id&gt;</c>; the parameter is required.</summary>
        AiKey = 2,
    }

    /// <summary>What a <see cref="CompanySelector"/> points at.</summary>
    public enum CompanySelectorKind
    {
        /// <summary>The subject of the <see cref="ConditionContext"/>.</summary>
        Context = 0,

        /// <summary>One company by key.</summary>
        Key = 1,

        /// <summary>Any AI company: the test is true when it is true for at least one of them.</summary>
        AnyAi = 2,
    }

    /// <summary>
    /// The parsed <c>company</c> parameter of a predicate: the context subject (no parameter), one company by key,
    /// or <c>anyAI</c>. <see cref="Test"/> applies a per-company check of <see cref="IGameState"/> accordingly.
    /// </summary>
    public sealed class CompanySelector
    {
        /// <summary>The subject of the context: the player unless the caller says otherwise.</summary>
        public static readonly CompanySelector Subject = new CompanySelector(CompanySelectorKind.Context, null);

        /// <summary>Any AI company.</summary>
        public static readonly CompanySelector AnyAi = new CompanySelector(CompanySelectorKind.AnyAi, null);

        private CompanySelector(CompanySelectorKind kind, string key)
        {
            Kind = kind;
            Key = key;
        }

        public CompanySelectorKind Kind { get; }

        /// <summary>The company key for <see cref="CompanySelectorKind.Key"/>, otherwise null.</summary>
        public string Key { get; }

        /// <summary>A selector for one company; throws for a string that is not a valid <see cref="CompanyKey"/>.</summary>
        public static CompanySelector ForKey(string key)
        {
            if (!CompanyKey.IsValid(key)) throw new ArgumentException("not a company key: " + key, nameof(key));
            return new CompanySelector(CompanySelectorKind.Key, key);
        }

        /// <summary>
        /// Runs <paramref name="test"/> for the selected company and returns whether it came back true. A null result of the
        /// test (source unavailable) counts as false. For <c>anyAI</c> the test runs over <see cref="IGameState.AiCompanyKeys"/>
        /// (null list: false) and stops at the first true; bankrupt companies are skipped unless <paramref name="includeBankrupt"/>
        /// is set (only the bankruptcy predicate wants them).
        /// </summary>
        public bool Test(ConditionContext ctx, Func<IGameState, string, bool?> test, bool includeBankrupt = false)
        {
            IGameState state = ctx.State;
            if (state == null) return false;

            switch (Kind)
            {
                case CompanySelectorKind.Key:
                    return test(state, Key) == true;

                case CompanySelectorKind.AnyAi:
                    IReadOnlyList<string> keys = state.AiCompanyKeys();
                    if (keys == null) return false;
                    for (int i = 0; i < keys.Count; i++)
                    {
                        string key = keys[i];
                        if (!includeBankrupt && state.IsBankrupt(key) == true) continue;
                        if (test(state, key) == true) return true;
                    }
                    return false;

                default:
                    return test(state, ctx.SubjectKey) == true;
            }
        }

        /// <summary>The text of the parameter: <c>player</c>, <c>ai:3</c>, <c>anyAI</c>; empty for the context subject.</summary>
        public override string ToString()
        {
            switch (Kind)
            {
                case CompanySelectorKind.Key: return Key;
                case CompanySelectorKind.AnyAi: return CompanyKey.AnyAi;
                default: return string.Empty;
            }
        }
    }

    /// <summary>
    /// Typed reader of the parameters of one predicate node, used by the factories of core predicates and available to features.
    /// Every accessor reports a missing, mistyped or out-of-range parameter as an error with the path
    /// <c>&lt;node path&gt;.&lt;name&gt;</c> (for example <c>$.all[1].year</c>) and returns the default so the factory can go on and report
    /// all problems of the node at once. <see cref="Finish"/> adds the unknown parameters (typos) and tells whether this node is clean.
    /// </summary>
    public sealed class ConditionArgs
    {
        private readonly JsonObject node;
        private readonly List<string> errors;
        private readonly int errorsAtStart;
        private readonly List<string> known = new List<string> { "type" };
        private readonly List<string> described = new List<string>();

        public ConditionArgs(JsonObject node, List<string> errors, string path)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            this.node = node;
            this.errors = errors;
            Path = path ?? "$";
            errorsAtStart = errors.Count;
        }

        /// <summary>Path of the node, for example <c>$.all[1]</c>.</summary>
        public string Path { get; }

        /// <summary>True while this node has produced no errors.</summary>
        public bool Ok
        {
            get { return errors.Count == errorsAtStart; }
        }

        /// <summary>True when the node has the parameter (useful for parameters whose absence has a meaning of its own).</summary>
        public bool Has(string name)
        {
            return node.ContainsKey(name);
        }

        /// <summary>Adds an error for the parameter <paramref name="name"/> (<c>path.name: message</c>); use it for checks across parameters.</summary>
        public void Fail(string name, string message)
        {
            errors.Add(Path + "." + name + ": " + message);
        }

        /// <summary>A required integer in [min, max].</summary>
        public int RequireInt(string name, int min, int max)
        {
            return ReadInt(name, false, 0, min, max);
        }

        /// <summary>An optional integer in [min, max]; <paramref name="defaultValue"/> when the parameter is absent.</summary>
        public int OptionalInt(string name, int defaultValue, int min, int max)
        {
            return ReadInt(name, true, defaultValue, min, max);
        }

        /// <summary>A required finite number in [min, max].</summary>
        public double RequireNumber(string name, double min, double max)
        {
            return ReadNumber(name, false, 0, min, max);
        }

        /// <summary>An optional finite number in [min, max]; <paramref name="defaultValue"/> when absent.</summary>
        public double OptionalNumber(string name, double defaultValue, double min, double max)
        {
            return ReadNumber(name, true, defaultValue, min, max);
        }

        /// <summary>An optional boolean.</summary>
        public bool OptionalBool(string name, bool defaultValue)
        {
            known.Add(name);
            JsonNode value;
            if (!node.TryGetValue(name, out value)) return defaultValue;

            var scalar = value as JsonValue;
            if (scalar == null || scalar.Kind != JsonKind.Bool)
            {
                Fail(name, "expected true or false, got " + KindName(value));
                return defaultValue;
            }
            return Noted(name, scalar.BoolValue);
        }

        /// <summary>A required non-empty string.</summary>
        public string RequireString(string name)
        {
            known.Add(name);
            JsonNode value;
            if (!node.TryGetValue(name, out value))
            {
                Fail(name, "required parameter is missing");
                return string.Empty;
            }
            return ReadString(name, value) ?? string.Empty;
        }

        /// <summary>An optional non-empty string; null when absent.</summary>
        public string OptionalString(string name)
        {
            known.Add(name);
            JsonNode value;
            if (!node.TryGetValue(name, out value)) return null;
            return ReadString(name, value);
        }

        /// <summary>A required string out of a fixed set (compared case-insensitively); returns the spelling from <paramref name="allowed"/>.</summary>
        public string RequireChoice(string name, params string[] allowed)
        {
            return ReadChoice(name, false, null, allowed);
        }

        /// <summary>An optional string out of a fixed set; <paramref name="defaultValue"/> (one of the allowed spellings) when absent.</summary>
        public string OptionalChoice(string name, string defaultValue, params string[] allowed)
        {
            return ReadChoice(name, true, defaultValue, allowed);
        }

        /// <summary>
        /// Reads the <c>company</c> parameter. Absent: the context subject for <see cref="CompanyScope.PlayerOrAi"/>,
        /// <c>anyAI</c> for <see cref="CompanyScope.AiOrAnyAi"/>, an error for <see cref="CompanyScope.AiKey"/>.
        /// </summary>
        public CompanySelector Company(CompanyScope scope)
        {
            const string name = "company";
            known.Add(name);
            JsonNode value;
            if (!node.TryGetValue(name, out value))
            {
                if (scope == CompanyScope.AiKey)
                {
                    Fail(name, "required parameter is missing (expected ai:<number>)");
                    return CompanySelector.Subject;
                }
                return scope == CompanyScope.AiOrAnyAi ? CompanySelector.AnyAi : CompanySelector.Subject;
            }

            var scalar = value as JsonValue;
            if (scalar == null || scalar.Kind != JsonKind.String)
            {
                Fail(name, "expected a company key, got " + KindName(value));
                return CompanySelector.Subject;
            }

            string text = scalar.StringValue;
            if (string.Equals(text, CompanyKey.AnyAi, StringComparison.Ordinal) && scope != CompanyScope.AiKey)
            {
                described.Add("company=" + text);
                return CompanySelector.AnyAi;
            }
            if (CompanyKey.IsValid(text) && (scope == CompanyScope.PlayerOrAi || CompanyKey.IsAi(text)))
            {
                described.Add("company=" + text);
                return CompanySelector.ForKey(text);
            }

            string expected = scope == CompanyScope.PlayerOrAi ? "player, ai:<number> or anyAI"
                : scope == CompanyScope.AiOrAnyAi ? "ai:<number> or anyAI"
                : "ai:<number>";
            Fail(name, "expected " + expected + ", got " + Quote(text));
            return CompanySelector.Subject;
        }

        /// <summary>
        /// Reports every parameter that no accessor asked for (a typo would otherwise be ignored silently) and returns
        /// whether this node is free of errors. Call it once, after reading all parameters.
        /// </summary>
        public bool Finish()
        {
            foreach (string key in node.Keys)
            {
                if (known.Contains(key)) continue;
                Fail(key, "unknown parameter (known: " + string.Join(", ", known.ToArray()) + ")");
            }
            return Ok;
        }

        /// <summary>The text for <see cref="ICondition.Describe"/>: <c>name(a=1, b=true)</c> with the parameters that were given, or just the name when there are none.</summary>
        public string Describe(string typeName)
        {
            if (described.Count == 0) return typeName;
            return typeName + "(" + string.Join(", ", described.ToArray()) + ")";
        }

        // ---- internals ----

        private int ReadInt(string name, bool optional, int defaultValue, int min, int max)
        {
            known.Add(name);
            JsonNode value;
            if (!node.TryGetValue(name, out value))
            {
                if (optional) return defaultValue;
                Fail(name, "required parameter is missing");
                return min;
            }

            var scalar = value as JsonValue;
            int number;
            if (scalar == null || scalar.Kind != JsonKind.Number)
            {
                Fail(name, "expected an integer, got " + KindName(value));
                return optional ? defaultValue : min;
            }
            if (!scalar.TryGetInt32(out number))
            {
                Fail(name, "expected an integer, got " + scalar);
                return optional ? defaultValue : min;
            }
            if (number < min || number > max)
            {
                Fail(name, "must be between " + min.ToString(CultureInfo.InvariantCulture) + " and "
                           + max.ToString(CultureInfo.InvariantCulture) + ", got " + number.ToString(CultureInfo.InvariantCulture));
                return optional ? defaultValue : min;
            }
            return Noted(name, number);
        }

        private double ReadNumber(string name, bool optional, double defaultValue, double min, double max)
        {
            known.Add(name);
            JsonNode value;
            if (!node.TryGetValue(name, out value))
            {
                if (optional) return defaultValue;
                Fail(name, "required parameter is missing");
                return min;
            }

            var scalar = value as JsonValue;
            if (scalar == null || scalar.Kind != JsonKind.Number)
            {
                Fail(name, "expected a number, got " + KindName(value));
                return optional ? defaultValue : min;
            }
            double number = scalar.NumberValue;
            if (number < min || number > max)
            {
                Fail(name, "must be between " + FormatNumber(min) + " and " + FormatNumber(max) + ", got " + FormatNumber(number));
                return optional ? defaultValue : min;
            }
            return Noted(name, number);
        }

        private string ReadString(string name, JsonNode value)
        {
            var scalar = value as JsonValue;
            if (scalar == null || scalar.Kind != JsonKind.String)
            {
                Fail(name, "expected a string, got " + KindName(value));
                return null;
            }
            if (scalar.StringValue.Length == 0)
            {
                Fail(name, "must not be empty");
                return null;
            }
            described.Add(name + "=" + Quote(scalar.StringValue));
            return scalar.StringValue;
        }

        private string ReadChoice(string name, bool optional, string defaultValue, string[] allowed)
        {
            known.Add(name);
            JsonNode value;
            if (!node.TryGetValue(name, out value))
            {
                if (optional) return defaultValue;
                Fail(name, "required parameter is missing (one of: " + string.Join(", ", allowed) + ")");
                return allowed.Length > 0 ? allowed[0] : string.Empty;
            }

            var scalar = value as JsonValue;
            if (scalar != null && scalar.Kind == JsonKind.String)
            {
                for (int i = 0; i < allowed.Length; i++)
                {
                    if (string.Equals(allowed[i], scalar.StringValue, StringComparison.OrdinalIgnoreCase))
                    {
                        described.Add(name + "=" + allowed[i]);
                        return allowed[i];
                    }
                }
                Fail(name, "expected one of " + string.Join(", ", allowed) + ", got " + Quote(scalar.StringValue));
            }
            else
            {
                Fail(name, "expected one of " + string.Join(", ", allowed) + ", got " + KindName(value));
            }
            return optional ? defaultValue : (allowed.Length > 0 ? allowed[0] : string.Empty);
        }

        private int Noted(string name, int value)
        {
            described.Add(name + "=" + value.ToString(CultureInfo.InvariantCulture));
            return value;
        }

        private double Noted(string name, double value)
        {
            described.Add(name + "=" + FormatNumber(value));
            return value;
        }

        private bool Noted(string name, bool value)
        {
            described.Add(name + "=" + (value ? "true" : "false"));
            return value;
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Quote(string text)
        {
            const int max = 40;
            string shown = text.Length > max ? text.Substring(0, max) + "..." : text;
            return "\"" + shown + "\"";
        }

        internal static string KindName(JsonNode node)
        {
            if (node == null) return "nothing";
            switch (node.Kind)
            {
                case JsonKind.Null: return "null";
                case JsonKind.Bool: return "a boolean";
                case JsonKind.Number: return "a number";
                case JsonKind.String: return "a string";
                case JsonKind.Object: return "an object";
                default: return "an array";
            }
        }
    }

    /// <summary>
    /// Turns the JSON of a condition into a tree of <see cref="ICondition"/> (FR-11 item 2). A node is an object with exactly one of:
    /// <c>type</c> (a registered predicate with its parameters next to it), <c>all</c> (array of nodes), <c>any</c> (array of nodes)
    /// or <c>not</c> (one node). Errors never throw; each one is a line <c>path: message</c> with the path in JSON-path style
    /// (<c>$.all[1].year</c>), and the parser goes on to collect every problem of the tree. When the call added any error it returns null,
    /// so a half-built condition is never used.
    /// </summary>
    public sealed class ConditionParser
    {
        /// <summary>Deepest nesting of condition nodes (a hand-built cyclic tree cannot hang the parser).</summary>
        public const int MaxDepth = 64;

        public ConditionParser(PredicateRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            Registry = registry;
        }

        public PredicateRegistry Registry { get; }

        /// <param name="node">The JSON node of the condition.</param>
        /// <param name="errors">Receives the error lines; null is allowed (the errors are then only reflected in the null result).</param>
        /// <param name="path">Path of the node in its document, <c>$</c> for the root; prefixes every error.</param>
        public ICondition Parse(JsonNode node, List<string> errors, string path = "$")
        {
            var sink = errors ?? new List<string>();
            int before = sink.Count;
            ICondition result = ParseNode(node, sink, path ?? "$", 0);
            return sink.Count > before ? null : result;
        }

        /// <summary>Parses JSON text first (a syntax error is reported as <c>$: invalid JSON: ... line L, column C</c>).</summary>
        public ICondition Parse(string json, List<string> errors)
        {
            var sink = errors ?? new List<string>();
            JsonNode node;
            JsonError error;
            if (!ModJson.TryParse(json, out node, out error))
            {
                sink.Add("$: invalid JSON: " + error);
                return null;
            }
            return Parse(node, sink, "$");
        }

        private ICondition ParseNode(JsonNode node, List<string> errors, string path, int depth)
        {
            if (depth > MaxDepth)
            {
                errors.Add(path + ": conditions are nested deeper than " + MaxDepth.ToString(CultureInfo.InvariantCulture) + " levels");
                return null;
            }

            var obj = node as JsonObject;
            if (obj == null)
            {
                errors.Add(path + ": expected an object, got " + ConditionArgs.KindName(node));
                return null;
            }

            bool hasType = obj.ContainsKey("type");
            bool hasAll = obj.ContainsKey("all");
            bool hasAny = obj.ContainsKey("any");
            bool hasNot = obj.ContainsKey("not");
            int count = (hasType ? 1 : 0) + (hasAll ? 1 : 0) + (hasAny ? 1 : 0) + (hasNot ? 1 : 0);
            if (count == 0)
            {
                errors.Add(path + ": a condition needs one of \"type\", \"all\", \"any\" or \"not\"");
                return null;
            }
            if (count > 1)
            {
                errors.Add(path + ": a condition must have only one of \"type\", \"all\", \"any\" and \"not\"");
                return null;
            }

            if (hasType) return ParsePredicate(obj, errors, path);
            if (hasNot) return ParseNot(obj, errors, path, depth);
            return ParseGroup(obj, hasAll ? "all" : "any", errors, path, depth);
        }

        private ICondition ParsePredicate(JsonObject obj, List<string> errors, string path)
        {
            var typeValue = obj["type"] as JsonValue;
            if (typeValue == null || typeValue.Kind != JsonKind.String || typeValue.StringValue.Length == 0)
            {
                errors.Add(path + ".type: expected the name of a predicate, got " + ConditionArgs.KindName(obj["type"]));
                return null;
            }
            return Registry.Create(typeValue.StringValue, obj, errors, path);
        }

        private ICondition ParseNot(JsonObject obj, List<string> errors, string path, int depth)
        {
            int before = errors.Count;
            ReportUnknownKeys(obj, "not", errors, path);
            ICondition inner = ParseNode(obj["not"], errors, path + ".not", depth + 1);
            if (inner == null || errors.Count > before) return null;
            return new NotCondition(inner);
        }

        private ICondition ParseGroup(JsonObject obj, string key, List<string> errors, string path, int depth)
        {
            int before = errors.Count;
            ReportUnknownKeys(obj, key, errors, path);

            var array = obj[key] as JsonArray;
            if (array == null)
            {
                errors.Add(path + "." + key + ": expected an array of conditions, got " + ConditionArgs.KindName(obj[key]));
                return null;
            }

            var children = new List<ICondition>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                string childPath = path + "." + key + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                ICondition child = ParseNode(array[i], errors, childPath, depth + 1);
                if (child != null) children.Add(child);
            }

            if (errors.Count > before) return null;
            return key == "all" ? (ICondition)new AllCondition(children) : new AnyCondition(children);
        }

        private static void ReportUnknownKeys(JsonObject obj, string allowedKey, List<string> errors, string path)
        {
            foreach (string name in obj.Keys)
            {
                if (name != allowedKey) errors.Add(path + "." + name + ": unknown key next to \"" + allowedKey + "\"");
            }
        }
    }
}
