using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// Typed reads of the members of a content object, with one English line per problem: <c>entry hist.x roles[0].kind: expected a string, got a number</c>.
    /// A member that is absent (or JSON null) reads as its default and is no problem here - whether it is needed is the validator's decision; a member
    /// of the wrong type is a problem. Internal plumbing of the model files, not API.
    /// </summary>
    internal sealed class HistJsonReader
    {
        private readonly List<string> _errors;
        private readonly string _subject;

        /// <param name="subject">What the lines are about, put in front of every line (<c>entry hist.x</c>, <c>params.json</c>).</param>
        /// <param name="errors">Where the lines go; may be null (the problems are still counted).</param>
        public HistJsonReader(string subject, List<string> errors)
        {
            _subject = subject ?? string.Empty;
            _errors = errors;
        }

        /// <summary>How many problems were found (warnings are not counted).</summary>
        public int Problems { get; private set; }

        public void Fail(string path, string message)
        {
            Problems++;
            if (_errors != null) _errors.Add(Line(path, message));
        }

        /// <summary>A line that does not make the object unusable (an unknown member).</summary>
        public void Warn(string path, string message)
        {
            if (_errors != null) _errors.Add(Line(path, message));
        }

        /// <summary>A problem found by someone else (the condition parser) whose text already says where.</summary>
        public void FailRaw(string message)
        {
            Problems++;
            if (_errors != null) _errors.Add(_subject + " " + message);
        }

        public static string Join(string parent, string name)
        {
            return string.IsNullOrEmpty(parent) ? name : parent + "." + name;
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
                case JsonKind.Object: return "an object";
                default: return "an array";
            }
        }

        /// <summary>The member, or null when it is absent or JSON null.</summary>
        public static JsonNode Get(JsonObject obj, string name)
        {
            JsonNode node = obj == null ? null : obj[name];
            return node == null || node.IsNull ? null : node;
        }

        /// <summary>A string; null when absent. A member of another type is a problem (and reads as null).</summary>
        public string Str(JsonObject obj, string name, string path)
        {
            JsonNode node = Get(obj, name);
            if (node == null) return null;
            var scalar = node as JsonValue;
            if (scalar != null && scalar.Kind == JsonKind.String) return scalar.StringValue;
            Fail(Join(path, name), "expected a string, got " + KindName(node));
            return null;
        }

        /// <summary>A whole number inside the Int32 range; <paramref name="defaultValue"/> when absent.</summary>
        public int Int(JsonObject obj, string name, string path, int defaultValue)
        {
            JsonNode node = Get(obj, name);
            if (node == null) return defaultValue;
            int value;
            var scalar = node as JsonValue;
            if (scalar != null && scalar.Kind == JsonKind.Number && scalar.TryGetInt32(out value)) return value;
            Fail(Join(path, name), "expected a whole number, got " + KindName(node));
            return defaultValue;
        }

        /// <summary>A finite number; <paramref name="defaultValue"/> when absent.</summary>
        public double Num(JsonObject obj, string name, string path, double defaultValue)
        {
            JsonNode node = Get(obj, name);
            if (node == null) return defaultValue;
            var scalar = node as JsonValue;
            if (scalar != null && scalar.Kind == JsonKind.Number && !double.IsNaN(scalar.NumberValue) && !double.IsInfinity(scalar.NumberValue))
                return scalar.NumberValue;
            Fail(Join(path, name), "expected a number, got " + KindName(node));
            return defaultValue;
        }

        public bool Bool(JsonObject obj, string name, string path, bool defaultValue)
        {
            JsonNode node = Get(obj, name);
            if (node == null) return defaultValue;
            var scalar = node as JsonValue;
            if (scalar != null && scalar.Kind == JsonKind.Bool) return scalar.BoolValue;
            Fail(Join(path, name), "expected true or false, got " + KindName(node));
            return defaultValue;
        }

        public JsonObject Obj(JsonObject obj, string name, string path)
        {
            JsonNode node = Get(obj, name);
            if (node == null) return null;
            var result = node as JsonObject;
            if (result != null) return result;
            Fail(Join(path, name), "expected an object, got " + KindName(node));
            return null;
        }

        public JsonArray Arr(JsonObject obj, string name, string path)
        {
            JsonNode node = Get(obj, name);
            if (node == null) return null;
            var result = node as JsonArray;
            if (result != null) return result;
            Fail(Join(path, name), "expected an array, got " + KindName(node));
            return null;
        }

        /// <summary>An array of strings (a single string is taken as an array of one); empty when absent. Items of another type are problems.</summary>
        public List<string> Strings(JsonObject obj, string name, string path)
        {
            var result = new List<string>();
            JsonNode node = Get(obj, name);
            if (node == null) return result;

            var single = node as JsonValue;
            if (single != null && single.Kind == JsonKind.String)
            {
                result.Add(single.StringValue);
                return result;
            }

            var array = node as JsonArray;
            if (array == null)
            {
                Fail(Join(path, name), "expected an array of strings, got " + KindName(node));
                return result;
            }

            for (int i = 0; i < array.Count; i++)
            {
                var item = array[i] as JsonValue;
                if (item == null || item.Kind != JsonKind.String)
                {
                    Fail(Join(path, name) + "[" + i + "]", "expected a string, got " + KindName(array[i]));
                    continue;
                }
                result.Add(item.StringValue);
            }
            return result;
        }

        /// <summary>
        /// Warns about every member that is neither in <paramref name="known"/> nor a comment (a name starting with an underscore, like <c>_note</c>).
        /// A typo in an optional member would otherwise be ignored without a word.
        /// </summary>
        public void WarnUnknown(JsonObject obj, string path, params string[] known)
        {
            if (obj == null) return;
            foreach (KeyValuePair<string, JsonNode> member in obj)
            {
                if (member.Key.StartsWith("_", StringComparison.Ordinal)) continue;
                if (Array.IndexOf(known, member.Key) >= 0) continue;
                Warn(Join(path, member.Key), "unknown member is ignored");
            }
        }

        private string Line(string path, string message)
        {
            return string.IsNullOrEmpty(path) ? _subject + ": " + message : _subject + " " + path + ": " + message;
        }
    }
}
