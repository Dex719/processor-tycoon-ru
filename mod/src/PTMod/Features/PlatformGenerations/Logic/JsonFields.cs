using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// Typed reads of the members of a content object (<c>generations.json</c> records, <c>balance.json</c>). Every reader returns false and adds one
    /// English line "path: reason" to <c>errors</c> when the member is present but has the wrong type, or is missing while <c>required</c> is set;
    /// a JSON null counts as absent. An absent optional member returns true and leaves <c>value</c> at its default.
    /// Internal on purpose: it is the shared plumbing of the model files, not API.
    /// </summary>
    internal static class JsonFields
    {
        public static string Join(string parent, string name)
        {
            return string.IsNullOrEmpty(parent) ? name : parent + "." + name;
        }

        public static string KindName(JsonNode node)
        {
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

        private static bool Missing(string path, string name, bool required, List<string> errors)
        {
            if (required && errors != null) errors.Add(Join(path, name) + ": is required");
            return !required;
        }

        public static bool Int(JsonObject obj, string name, string path, bool required, out int value, List<string> errors)
        {
            value = 0;
            JsonNode node = Get(obj, name);
            if (node == null) return Missing(path, name, required, errors);

            var scalar = node as JsonValue;
            if (scalar != null && scalar.Kind == JsonKind.Number && scalar.TryGetInt32(out value)) return true;

            value = 0;
            if (errors != null) errors.Add(Join(path, name) + ": expected a whole number, got " + Describe(node));
            return false;
        }

        public static bool Number(JsonObject obj, string name, string path, bool required, out double value, List<string> errors)
        {
            value = 0;
            JsonNode node = Get(obj, name);
            if (node == null) return Missing(path, name, required, errors);

            var scalar = node as JsonValue;
            if (scalar != null && scalar.Kind == JsonKind.Number && !double.IsNaN(scalar.NumberValue) && !double.IsInfinity(scalar.NumberValue))
            {
                value = scalar.NumberValue;
                return true;
            }

            if (errors != null) errors.Add(Join(path, name) + ": expected a number, got " + Describe(node));
            return false;
        }

        public static bool Bool(JsonObject obj, string name, string path, bool required, out bool value, List<string> errors)
        {
            value = false;
            JsonNode node = Get(obj, name);
            if (node == null) return Missing(path, name, required, errors);

            var scalar = node as JsonValue;
            if (scalar != null && scalar.Kind == JsonKind.Bool)
            {
                value = scalar.BoolValue;
                return true;
            }

            if (errors != null) errors.Add(Join(path, name) + ": expected true or false, got " + Describe(node));
            return false;
        }

        public static bool String(JsonObject obj, string name, string path, bool required, out string value, List<string> errors)
        {
            value = null;
            JsonNode node = Get(obj, name);
            if (node == null) return Missing(path, name, required, errors);

            var scalar = node as JsonValue;
            if (scalar != null && scalar.Kind == JsonKind.String)
            {
                value = scalar.StringValue;
                return true;
            }

            if (errors != null) errors.Add(Join(path, name) + ": expected a string, got " + Describe(node));
            return false;
        }

        /// <summary>A member that must be an object; null with an error (when wrong or required and missing), null without one (optional and absent).</summary>
        public static JsonObject Object(JsonObject obj, string name, string path, bool required, List<string> errors)
        {
            JsonNode node = Get(obj, name);
            if (node == null)
            {
                Missing(path, name, required, errors);
                return null;
            }

            var result = node as JsonObject;
            if (result == null && errors != null) errors.Add(Join(path, name) + ": expected an object, got " + Describe(node));
            return result;
        }

        /// <summary>A member that must be an array; null with an error (when wrong or required and missing), null without one (optional and absent).</summary>
        public static JsonArray Array(JsonObject obj, string name, string path, bool required, List<string> errors)
        {
            JsonNode node = Get(obj, name);
            if (node == null)
            {
                Missing(path, name, required, errors);
                return null;
            }

            var result = node as JsonArray;
            if (result == null && errors != null) errors.Add(Join(path, name) + ": expected an array, got " + Describe(node));
            return result;
        }

        /// <summary>The names of <paramref name="obj"/> that are not in <paramref name="known"/>, in file order.</summary>
        public static List<string> Unknown(JsonObject obj, params string[] known)
        {
            var result = new List<string>();
            foreach (KeyValuePair<string, JsonNode> pair in obj)
            {
                if (System.Array.IndexOf(known, pair.Key) < 0) result.Add(pair.Key);
            }
            return result;
        }

        private static string Describe(JsonNode node)
        {
            var scalar = node as JsonValue;
            if (scalar != null && scalar.Kind == JsonKind.Number) return "the number " + node;
            return KindName(node);
        }
    }
}
