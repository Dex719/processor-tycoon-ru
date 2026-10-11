using PTMod.Core.Logic.Json;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// <see cref="IEffectParams"/> over the <c>params</c> object of an effect (FR-1, FR-6): a missing key, a JSON null and a key of the wrong kind
    /// all give the fallback, so a handler reads its parameters without type checks. Nothing here validates: the validator
    /// (<see cref="StandardEffectRules"/>, <c>IEventEffectHandler.Validate</c>) rejects the wrong kinds when the content is loaded, the adapter only
    /// keeps a handler from crashing on content that slipped through (a saved effect never reads its params again, so this is for <c>Resolve</c> only).
    /// </summary>
    public sealed class EffectParams : IEffectParams
    {
        private readonly JsonObject _raw;

        /// <summary>An adapter over <paramref name="raw"/>; null is an empty object (an effect without <c>params</c>).</summary>
        public EffectParams(JsonObject raw)
        {
            _raw = raw ?? new JsonObject();
        }

        public JsonObject Raw
        {
            get { return _raw; }
        }

        public bool Has(string key)
        {
            JsonNode node = Find(key);
            return node != null;
        }

        /// <summary>A number as a float; a number that does not fit a float (beyond about 3.4e38) is not a float and gives the fallback.</summary>
        public float GetFloat(string key, float fallback)
        {
            double number;
            if (!TryNumber(Find(key), out number)) return fallback;
            float value = (float)number;
            return float.IsInfinity(value) ? fallback : value;
        }

        /// <summary>A whole number inside the Int32 range; <c>1.5</c> is not an int and gives the fallback.</summary>
        public int GetInt(string key, int fallback)
        {
            int value;
            return _raw.TryGetInt32(key, out value) ? value : fallback;
        }

        public bool GetBool(string key, bool fallback)
        {
            bool value;
            return _raw.TryGetBool(key, out value) ? value : fallback;
        }

        public string GetString(string key, string fallback)
        {
            string value;
            return _raw.TryGetString(key, out value) ? value : fallback;
        }

        /// <summary>A number (an interval of one point) or an array of exactly two numbers <c>[lo, hi]</c>, taken as written (no reordering).</summary>
        public FloatRange GetRange(string key, FloatRange fallback)
        {
            double lo, hi;
            if (!TryRange(Find(key), out lo, out hi)) return fallback;

            float low = (float)lo;
            float high = (float)hi;
            if (float.IsInfinity(low) || float.IsInfinity(high)) return fallback;
            return new FloatRange(low, high);
        }

        /// <summary>
        /// The number or the pair <c>[lo, hi]</c> in <paramref name="node"/> as doubles; false for anything else (a longer or shorter array, a
        /// non-number element). Shared with <see cref="StandardEffectRules"/>, which needs the same reading before it rounds to float.
        /// </summary>
        internal static bool TryRange(JsonNode node, out double lo, out double hi)
        {
            lo = 0;
            hi = 0;
            if (node == null) return false;

            if (TryNumber(node, out lo))
            {
                hi = lo;
                return true;
            }

            var pair = node as JsonArray;
            if (pair == null || pair.Count != 2) return false;
            return TryNumber(pair[0], out lo) && TryNumber(pair[1], out hi);
        }

        private JsonNode Find(string key)
        {
            if (key == null) return null;
            JsonNode node = _raw[key];
            return node == null || node.IsNull ? null : node;
        }

        private static bool TryNumber(JsonNode node, out double number)
        {
            return RecordReader.TryNumber(node, out number);
        }
    }
}
