using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.PressReviews
{
    /// <summary>
    /// Typed reads of the members of a content object for the parsers of <c>outlets.json</c>, <c>review-config.json</c> and <c>notes.json</c>.
    /// A member that is absent (or JSON null) gives the default without a message. A member of the wrong type is an error in the report and gives
    /// the default; a number outside its legal range is clipped with a warning. Messages are English lines "path: reason".
    /// Internal on purpose: shared plumbing of the content parsers, not API.
    /// </summary>
    internal static class ContentJson
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

        public static string Text(double value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>The member, or null when the object is null, the member is absent or it is JSON null.</summary>
        public static JsonNode Get(JsonObject obj, string name)
        {
            if (obj == null) return null;
            JsonNode node;
            if (!obj.TryGetValue(name, out node) || node == null || node.IsNull) return null;
            return node;
        }

        public static bool TryNumber(JsonNode node, out double value)
        {
            value = 0;
            var scalar = node as JsonValue;
            if (scalar == null || scalar.Kind != JsonKind.Number) return false;
            double v = scalar.NumberValue;
            if (double.IsNaN(v) || double.IsInfinity(v)) return false;
            value = v;
            return true;
        }

        public static float Float(JsonObject obj, string name, string path, float def, float min, float max, ValidationReport report)
        {
            JsonNode node = Get(obj, name);
            if (node == null) return def;

            string where = Join(path, name);
            double v;
            if (!TryNumber(node, out v))
            {
                report.Errors.Add(where + ": expected a number, got " + KindName(node));
                return def;
            }
            return ClipNumber(v, where, min, max, report);
        }

        public static float ClipNumber(double v, string where, float min, float max, ValidationReport report)
        {
            if (v < min)
            {
                report.Warnings.Add(where + ": " + Text(v) + " is below " + Text(min) + ", clipped");
                return min;
            }
            if (v > max)
            {
                report.Warnings.Add(where + ": " + Text(v) + " is above " + Text(max) + ", clipped");
                return max;
            }
            return (float)v;
        }

        public static int Int(JsonObject obj, string name, string path, int def, int min, int max, ValidationReport report)
        {
            JsonNode node = Get(obj, name);
            if (node == null) return def;
            return IntOf(node, Join(path, name), def, min, max, report);
        }

        public static int IntOf(JsonNode node, string where, int def, int min, int max, ValidationReport report)
        {
            double v;
            if (!TryNumber(node, out v) || v != Math.Floor(v) || v < int.MinValue || v > int.MaxValue)
            {
                report.Errors.Add(where + ": expected a whole number, got " + (node.Kind == JsonKind.Number ? "the number " + node : KindName(node)));
                return def;
            }
            if (v < min)
            {
                report.Warnings.Add(where + ": " + Text(v) + " is below " + min.ToString(CultureInfo.InvariantCulture) + ", clipped");
                return min;
            }
            if (v > max)
            {
                report.Warnings.Add(where + ": " + Text(v) + " is above " + max.ToString(CultureInfo.InvariantCulture) + ", clipped");
                return max;
            }
            return (int)v;
        }

        public static bool Bool(JsonObject obj, string name, string path, bool def, ValidationReport report)
        {
            JsonNode node = Get(obj, name);
            if (node == null) return def;

            var scalar = node as JsonValue;
            if (scalar != null && scalar.Kind == JsonKind.Bool) return scalar.BoolValue;

            report.Errors.Add(Join(path, name) + ": expected true or false, got " + KindName(node));
            return def;
        }

        /// <summary>A string member; <paramref name="def"/> when absent or of the wrong type (an error then).</summary>
        public static string Str(JsonObject obj, string name, string path, string def, ValidationReport report)
        {
            JsonNode node = Get(obj, name);
            if (node == null) return def;

            var scalar = node as JsonValue;
            if (scalar != null && scalar.Kind == JsonKind.String) return scalar.StringValue;

            report.Errors.Add(Join(path, name) + ": expected a string, got " + KindName(node));
            return def;
        }

        public static JsonObject Obj(JsonObject obj, string name, string path, ValidationReport report)
        {
            JsonNode node = Get(obj, name);
            if (node == null) return null;

            var result = node as JsonObject;
            if (result == null) report.Errors.Add(Join(path, name) + ": expected an object, got " + KindName(node));
            return result;
        }

        public static JsonArray Arr(JsonObject obj, string name, string path, ValidationReport report)
        {
            JsonNode node = Get(obj, name);
            if (node == null) return null;

            var result = node as JsonArray;
            if (result == null) report.Errors.Add(Join(path, name) + ": expected an array, got " + KindName(node));
            return result;
        }

        /// <summary>Adds a warning for every member of <paramref name="obj"/> that is not in <paramref name="known"/>.</summary>
        public static void WarnUnknown(JsonObject obj, string path, ValidationReport report, params string[] known)
        {
            if (obj == null) return;
            foreach (KeyValuePair<string, JsonNode> pair in obj)
            {
                if (Array.IndexOf(known, pair.Key) < 0) report.Warnings.Add(Join(path, pair.Key) + ": unknown member is ignored");
            }
        }
    }
}
