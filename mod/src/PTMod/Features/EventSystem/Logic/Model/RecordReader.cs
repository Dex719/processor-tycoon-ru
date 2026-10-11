using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// Typed reading of one JSON object of a content file for <see cref="DefinitionMapper"/> and <see cref="PackConfigReader"/>: every accessor
    /// takes the field, checks its kind and range, and on a mistake adds one English line to the error list and gives the default. Every field that
    /// was asked for is marked, so <see cref="ReportUnknown"/> can warn about the rest (a typo such as <c>timerDay</c> would otherwise be ignored silently).
    /// The lines read <c>problem (path.to.field)</c>: the problem first, so a log line stays <c>skip 'id': problem (where)</c>.
    /// </summary>
    internal sealed class RecordReader
    {
        private readonly JsonObject _node;
        private readonly string _path;
        private readonly List<string> _errors;
        private readonly List<string> _warnings;
        private readonly HashSet<string> _used = new HashSet<string>(StringComparer.Ordinal);

        public RecordReader(JsonObject node, string path, List<string> errors, List<string> warnings)
        {
            _node = node ?? new JsonObject();
            _path = path ?? string.Empty;
            _errors = errors;
            _warnings = warnings;
        }

        public string Path
        {
            get { return _path; }
        }

        /// <summary>The path of a field of this object: <c>choices[0].effects</c> for the field <c>effects</c> of <c>choices[0]</c>.</summary>
        public string PathOf(string field)
        {
            return _path.Length == 0 ? field : _path + "." + field;
        }

        public static string IndexPath(string path, int index)
        {
            return path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
        }

        /// <summary>The field is there and is not JSON null.</summary>
        public bool Has(string name)
        {
            JsonNode node = _node[name];
            return node != null && !node.IsNull;
        }

        public void Error(string field, string problem)
        {
            _errors.Add(problem + " (" + PathOf(field) + ")");
        }

        public void Warn(string field, string problem)
        {
            _warnings.Add(problem + " (" + PathOf(field) + ")");
        }

        /// <summary>The raw node of a field (marks it as used); null when it is missing or JSON null.</summary>
        public JsonNode Node(string name)
        {
            _used.Add(name);
            JsonNode node = _node[name];
            return node == null || node.IsNull ? null : node;
        }

        /// <summary>A string field. A missing field gives <paramref name="defaultValue"/> (an error when <paramref name="required"/>); an empty string counts as missing.</summary>
        public string Str(string name, bool required, string defaultValue = null)
        {
            JsonNode node = Node(name);
            if (node == null)
            {
                if (required) Error(name, "'" + name + "' is required");
                return defaultValue;
            }

            var value = node as JsonValue;
            if (value == null || value.Kind != JsonKind.String)
            {
                Error(name, "'" + name + "' must be a string, got " + KindName(node));
                return defaultValue;
            }
            if (value.StringValue.Length == 0)
            {
                if (required) Error(name, "'" + name + "' must not be empty");
                return defaultValue;
            }
            return value.StringValue;
        }

        public bool Bool(string name, bool defaultValue)
        {
            JsonNode node = Node(name);
            if (node == null) return defaultValue;

            var value = node as JsonValue;
            if (value == null || value.Kind != JsonKind.Bool)
            {
                Error(name, "'" + name + "' must be true or false, got " + KindName(node));
                return defaultValue;
            }
            return value.BoolValue;
        }

        public int Int(string name, int defaultValue, int min, int max)
        {
            JsonNode node = Node(name);
            if (node == null) return defaultValue;

            var value = node as JsonValue;
            int number;
            if (value == null || value.Kind != JsonKind.Number || !value.TryGetInt32(out number))
            {
                Error(name, "'" + name + "' must be a whole number from " + Num(min) + " to " + Num(max) + ", got " + Describe(node));
                return defaultValue;
            }
            if (number < min || number > max)
            {
                Error(name, "'" + name + "' must be from " + Num(min) + " to " + Num(max) + ", got " + Num(number));
                return defaultValue;
            }
            return number;
        }

        public float Float(string name, float defaultValue, float min, float max)
        {
            JsonNode node = Node(name);
            if (node == null) return defaultValue;

            double number;
            if (!TryNumber(node, out number))
            {
                Error(name, "'" + name + "' must be a number from " + Num(min) + " to " + Num(max) + ", got " + Describe(node));
                return defaultValue;
            }
            if (number < min || number > max)
            {
                Error(name, "'" + name + "' must be from " + Num(min) + " to " + Num(max) + ", got " + Num(number));
                return defaultValue;
            }
            return (float)number;
        }

        /// <summary>
        /// An enum field by name (case-insensitive; numbers are not names). A missing field gives <paramref name="defaultValue"/>, or an error when
        /// <paramref name="required"/>. Returns false when the field is required and not usable.
        /// </summary>
        public bool EnumValue<T>(string name, bool required, T defaultValue, out T result) where T : struct
        {
            result = defaultValue;
            JsonNode node = Node(name);
            if (node == null)
            {
                if (required) Error(name, "'" + name + "' is required (" + Names<T>() + ")");
                return !required;
            }

            string text = (node as JsonValue) != null && ((JsonValue)node).Kind == JsonKind.String ? ((JsonValue)node).StringValue : null;
            T parsed;
            if (text == null || !TryParseName(text, out parsed))
            {
                Error(name, "'" + name + "' must be one of " + Names<T>() + ", got " + Describe(node));
                return false;
            }
            result = parsed;
            return true;
        }

        /// <summary>An object field; null when missing (an error when <paramref name="required"/>) or of the wrong kind (always an error).</summary>
        public JsonObject Obj(string name, bool required)
        {
            JsonNode node = Node(name);
            if (node == null)
            {
                if (required) Error(name, "'" + name + "' is required");
                return null;
            }

            var obj = node as JsonObject;
            if (obj == null)
            {
                Error(name, "'" + name + "' must be an object, got " + KindName(node));
                return null;
            }
            return obj;
        }

        /// <summary>An array field; null when missing or of the wrong kind (an error in the second case).</summary>
        public JsonArray Arr(string name)
        {
            JsonNode node = Node(name);
            if (node == null) return null;

            var array = node as JsonArray;
            if (array == null)
            {
                Error(name, "'" + name + "' must be an array, got " + KindName(node));
                return null;
            }
            return array;
        }

        /// <summary>A text field: <c>{ "key": ... }</c> or <c>{ "ru": ..., "en": ... }</c> (<see cref="LocText.FromJson"/>).</summary>
        public LocText Loc(string name, bool required)
        {
            JsonNode node = Node(name);
            if (node == null)
            {
                if (required) Error(name, "'" + name + "' is required");
                return null;
            }

            var problems = new List<string>();
            LocText text = LocText.FromJson(node, PathOf(name), problems);
            if (text == null)
            {
                // LocText.FromJson says "path: reason"; keep the mapper's own "reason (path)" shape.
                foreach (string line in problems) _errors.Add(Reshape(line));
            }
            return text;
        }

        /// <summary>
        /// Warns about every field nobody asked for. Fields the merger or an author may add on purpose (<c>disabled</c>, <c>comment</c>, anything that
        /// starts with an underscore) are not reported.
        /// </summary>
        public void ReportUnknown(params string[] alsoAllowed)
        {
            foreach (KeyValuePair<string, JsonNode> pair in _node)
            {
                string key = pair.Key;
                if (_used.Contains(key)) continue;
                if (key.StartsWith("_", StringComparison.Ordinal) || string.Equals(key, "comment", StringComparison.Ordinal)) continue;
                if (alsoAllowed != null && System.Array.IndexOf(alsoAllowed, key) >= 0) continue;
                Warn(key, "unknown field '" + key + "' is ignored");
            }
        }

        // ---- shared helpers ----

        public static bool TryNumber(JsonNode node, out double number)
        {
            number = 0;
            var value = node as JsonValue;
            if (value == null || value.Kind != JsonKind.Number) return false;
            number = value.NumberValue;
            return !double.IsNaN(number) && !double.IsInfinity(number);
        }

        public static string KindName(JsonNode node)
        {
            if (node == null) return "nothing";
            switch (node.Kind)
            {
                case JsonKind.Null: return "null";
                case JsonKind.Bool: return "a boolean";
                case JsonKind.Number: return "a number";
                case JsonKind.String: return "a string";
                case JsonKind.Array: return "an array";
                default: return "an object";
            }
        }

        public static string Num(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        /// <summary>A short quote of a node for a message: the text in quotes, the number, or the kind.</summary>
        public static string Describe(JsonNode node)
        {
            var value = node as JsonValue;
            if (value != null)
            {
                if (value.Kind == JsonKind.String) return "'" + Clip(value.StringValue) + "'";
                if (value.Kind == JsonKind.Number) return Num(value.NumberValue);
                if (value.Kind == JsonKind.Bool) return value.BoolValue ? "true" : "false";
            }
            return KindName(node);
        }

        private static string Clip(string text)
        {
            return text.Length <= 40 ? text : text.Substring(0, 40) + "...";
        }

        private static string Names<T>() where T : struct
        {
            return string.Join(", ", System.Enum.GetNames(typeof(T)));
        }

        private static bool TryParseName<T>(string text, out T result) where T : struct
        {
            foreach (string name in System.Enum.GetNames(typeof(T)))
            {
                if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
                {
                    result = (T)System.Enum.Parse(typeof(T), name);
                    return true;
                }
            }
            result = default(T);
            return false;
        }

        /// <summary>"path: reason" (the shape of the core's messages) to "reason (path)".</summary>
        private static string Reshape(string line)
        {
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            if (colon <= 0) return line;
            return line.Substring(colon + 2) + " (" + line.Substring(0, colon) + ")";
        }
    }
}
