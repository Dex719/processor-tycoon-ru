using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// A typed reader of one JSON object of a content record. Every accessor returns the fallback for a missing member (or a JSON null) unless
    /// the member is required, and reports a member of the wrong kind or an unusable value as an error ("path.key: message") and marks the
    /// reader <see cref="Failed"/>; the mapper then returns null and the record is skipped. Pure logic, no Unity.
    /// </summary>
    internal sealed class ArchJsonReader
    {
        private readonly JsonObject _object;
        private readonly List<string> _errors;
        private readonly string _path;

        public ArchJsonReader(JsonObject json, List<string> errors, string path = "")
        {
            _object = json ?? new JsonObject();
            _errors = errors ?? new List<string>();
            _path = path ?? string.Empty;
        }

        /// <summary>At least one member was missing (required) or unusable; the record must not be used.</summary>
        public bool Failed { get; private set; }

        /// <summary>The member exists and is not JSON null.</summary>
        public bool Has(string key)
        {
            JsonNode node = _object[key];
            return node != null && !node.IsNull;
        }

        public void Fail(string key, string message)
        {
            Failed = true;
            _errors.Add(_path + key + ": " + message);
        }

        public string String(string key, bool required, string fallback = null)
        {
            JsonNode node = _object[key];
            if (node == null || node.IsNull)
            {
                if (required) Fail(key, "is required");
                return fallback;
            }

            var value = node as JsonValue;
            if (value == null || value.Kind != JsonKind.String)
            {
                Fail(key, "expected a string");
                return fallback;
            }
            return value.StringValue;
        }

        public int Int(string key, bool required, int fallback = 0)
        {
            JsonNode node = _object[key];
            if (node == null || node.IsNull)
            {
                if (required) Fail(key, "is required");
                return fallback;
            }

            int result;
            var value = node as JsonValue;
            if (value == null || value.Kind != JsonKind.Number || !value.TryGetInt32(out result))
            {
                Fail(key, "expected a whole number");
                return fallback;
            }
            return result;
        }

        public float Float(string key, bool required, float fallback = 0f)
        {
            JsonNode node = _object[key];
            if (node == null || node.IsNull)
            {
                if (required) Fail(key, "is required");
                return fallback;
            }

            float result;
            if (!TryFloat(node, out result))
            {
                Fail(key, "expected a number");
                return fallback;
            }
            return result;
        }

        public bool Bool(string key, bool fallback)
        {
            JsonNode node = _object[key];
            if (node == null || node.IsNull) return fallback;

            var value = node as JsonValue;
            if (value == null || value.Kind != JsonKind.Bool)
            {
                Fail(key, "expected true or false");
                return fallback;
            }
            return value.BoolValue;
        }

        /// <summary>A reader of a nested object, or null when the member is missing (required: also an error) or is not an object.</summary>
        public ArchJsonReader Object(string key, bool required)
        {
            JsonNode node = _object[key];
            if (node == null || node.IsNull)
            {
                if (required) Fail(key, "is required");
                return null;
            }

            var child = node as JsonObject;
            if (child == null)
            {
                Fail(key, "expected an object");
                return null;
            }
            return new ArchJsonReader(child, _errors, _path + key + ".");
        }

        /// <summary>The members of a nested object, or null when it is missing (required: also an error) or is not an object.</summary>
        public JsonObject RawObject(string key, bool required)
        {
            JsonNode node = _object[key];
            if (node == null || node.IsNull)
            {
                if (required) Fail(key, "is required");
                return null;
            }

            var child = node as JsonObject;
            if (child == null) Fail(key, "expected an object");
            return child;
        }

        public string[] StringArray(string key, bool required)
        {
            JsonArray array = ArrayOf(key, required);
            if (array == null) return null;

            var result = new string[array.Count];
            for (int i = 0; i < array.Count; i++)
            {
                var value = array[i] as JsonValue;
                if (value == null || value.Kind != JsonKind.String)
                {
                    Fail(key + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", "expected a string");
                    return null;
                }
                result[i] = value.StringValue;
            }
            return result;
        }

        public int[] IntArray(string key, bool required)
        {
            JsonArray array = ArrayOf(key, required);
            if (array == null) return null;

            var result = new int[array.Count];
            for (int i = 0; i < array.Count; i++)
            {
                var value = array[i] as JsonValue;
                if (value == null || value.Kind != JsonKind.Number || !value.TryGetInt32(out result[i]))
                {
                    Fail(key + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", "expected a whole number");
                    return null;
                }
            }
            return result;
        }

        public float[] FloatArray(string key, bool required)
        {
            JsonArray array = ArrayOf(key, required);
            if (array == null) return null;

            var result = new float[array.Count];
            for (int i = 0; i < array.Count; i++)
            {
                if (!TryFloat(array[i], out result[i]))
                {
                    Fail(key + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", "expected a number");
                    return null;
                }
            }
            return result;
        }

        public JsonArray ArrayOf(string key, bool required)
        {
            JsonNode node = _object[key];
            if (node == null || node.IsNull)
            {
                if (required) Fail(key, "is required");
                return null;
            }

            var array = node as JsonArray;
            if (array == null) Fail(key, "expected an array");
            return array;
        }

        /// <summary>A finite number as a float; false for anything else (also a number too large for a float).</summary>
        public static bool TryFloat(JsonNode node, out float result)
        {
            result = 0f;
            var value = node as JsonValue;
            if (value == null || value.Kind != JsonKind.Number) return false;

            double number = value.NumberValue;
            if (double.IsNaN(number) || double.IsInfinity(number) || Math.Abs(number) > float.MaxValue) return false;
            result = (float)number;
            return true;
        }
    }
}
