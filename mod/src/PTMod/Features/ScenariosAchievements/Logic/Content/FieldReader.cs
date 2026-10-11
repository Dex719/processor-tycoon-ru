using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// Typed reader of the members of one JSON object of the content, used by <see cref="ContentMapper"/>. Every read says what was wrong in
    /// <c>path.member: …</c> form and keeps going; <see cref="Finish"/> reports the members nobody asked for, so a typo in a file is an error
    /// instead of a silently ignored field. A member that is missing or JSON null counts as absent.
    /// </summary>
    internal sealed class FieldReader
    {
        private readonly JsonObject _node;
        private readonly string _path;
        private readonly List<string> _errors;
        private readonly HashSet<string> _used = new HashSet<string>(StringComparer.Ordinal);

        public FieldReader(JsonObject node, string path, List<string> errors)
        {
            _node = node;
            _path = path ?? string.Empty;
            _errors = errors;
        }

        public static string Join(string parent, string name)
        {
            return string.IsNullOrEmpty(parent) ? name : parent + "." + name;
        }

        public string PathOf(string name)
        {
            return Join(_path, name);
        }

        public bool Has(string name)
        {
            JsonNode node = _node[name];
            return node != null && !node.IsNull;
        }

        public string Str(string name, bool required)
        {
            JsonNode node = Take(name, required);
            if (node == null) return null;
            var value = node as JsonValue;
            if (value == null || value.Kind != JsonKind.String)
            {
                _errors.Add(PathOf(name) + ": expected a string");
                return null;
            }
            return value.StringValue;
        }

        public int Int(string name, bool required, int defaultValue)
        {
            JsonNode node = Take(name, required);
            if (node == null) return defaultValue;
            var value = node as JsonValue;
            int result;
            if (value == null || !value.TryGetInt32(out result))
            {
                _errors.Add(PathOf(name) + ": expected a whole number");
                return defaultValue;
            }
            return result;
        }

        public double Num(string name, bool required, double defaultValue)
        {
            JsonNode node = Take(name, required);
            if (node == null) return defaultValue;
            var value = node as JsonValue;
            if (value == null || value.Kind != JsonKind.Number)
            {
                _errors.Add(PathOf(name) + ": expected a number");
                return defaultValue;
            }
            return value.NumberValue;
        }

        public float Float(string name, bool required, float defaultValue)
        {
            double number = Num(name, required, defaultValue);
            return (float)number;
        }

        public bool Bool(string name, bool defaultValue)
        {
            JsonNode node = Take(name, false);
            if (node == null) return defaultValue;
            var value = node as JsonValue;
            if (value == null || value.Kind != JsonKind.Bool)
            {
                _errors.Add(PathOf(name) + ": expected true or false");
                return defaultValue;
            }
            return value.BoolValue;
        }

        public JsonObject Obj(string name, bool required)
        {
            JsonNode node = Take(name, required);
            if (node == null) return null;
            var obj = node as JsonObject;
            if (obj == null) _errors.Add(PathOf(name) + ": expected an object");
            return obj;
        }

        public JsonArray Arr(string name, bool required)
        {
            JsonNode node = Take(name, required);
            if (node == null) return null;
            var array = node as JsonArray;
            if (array == null) _errors.Add(PathOf(name) + ": expected an array");
            return array;
        }

        /// <summary>Reports every member nobody read (and none of <paramref name="allowed"/>) as an unknown field.</summary>
        public void Finish(params string[] allowed)
        {
            foreach (string key in _node.Keys)
            {
                if (_used.Contains(key)) continue;
                if (allowed != null && Array.IndexOf(allowed, key) >= 0) continue;
                _errors.Add(PathOf(key) + ": unknown field");
            }
        }

        private JsonNode Take(string name, bool required)
        {
            _used.Add(name);
            JsonNode node = _node[name];
            if (node == null || node.IsNull)
            {
                if (required) _errors.Add(PathOf(name) + ": is required");
                return null;
            }
            return node;
        }
    }
}
