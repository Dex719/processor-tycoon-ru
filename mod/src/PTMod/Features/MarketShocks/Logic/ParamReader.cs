using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.MarketShocks
{
    /// <summary>
    /// The strict reading of the <c>params</c> of an effect or of the parameters of a predicate node (design.md, "Проверки типов пака"): a missing key, a
    /// key of the wrong kind, a fraction where a whole number is wanted and a number outside its range each give one message in English that names the
    /// effect and the key (<c>ModifierRamp.params.peak: ...</c>); the first error of a key stops the reading of that key. Pure.
    /// </summary>
    internal static class ParamReader
    {
        /// <summary>A required finite number in [<paramref name="min"/>, <paramref name="max"/>].</summary>
        public static bool Number(JsonObject p, string owner, string key, double min, double max, IList<string> errors, out double value)
        {
            value = 0;
            if (!Present(p, owner, key, errors)) return false;

            if (!p.TryGetDouble(key, out value) || double.IsNaN(value) || double.IsInfinity(value))
            {
                value = 0;
                errors.Add(Where(owner, key) + " must be a number");
                return false;
            }

            return InRange(owner, key, value, min, max, errors);
        }

        /// <summary>An optional finite number; <paramref name="fallback"/> when the key is absent. A key that is present must be valid.</summary>
        public static bool OptionalNumber(JsonObject p, string owner, string key, double fallback, double min, double max, IList<string> errors, out double value)
        {
            value = fallback;
            if (p == null || !p.ContainsKey(key)) return true;
            return Number(p, owner, key, min, max, errors, out value);
        }

        /// <summary>A required whole number in [<paramref name="min"/>, <paramref name="max"/>] (1.5 is not one).</summary>
        public static bool Int(JsonObject p, string owner, string key, int min, int max, IList<string> errors, out int value)
        {
            value = 0;
            if (!Present(p, owner, key, errors)) return false;

            if (!p.TryGetInt32(key, out value))
            {
                value = 0;
                errors.Add(Where(owner, key) + " must be a whole number");
                return false;
            }

            return InRange(owner, key, value, min, max, errors);
        }

        /// <summary>An optional whole number; <paramref name="fallback"/> when the key is absent. A key that is present must be valid.</summary>
        public static bool OptionalInt(JsonObject p, string owner, string key, int fallback, int min, int max, IList<string> errors, out int value)
        {
            value = fallback;
            if (p == null || !p.ContainsKey(key)) return true;
            return Int(p, owner, key, min, max, errors, out value);
        }

        /// <summary>A required non-empty text.</summary>
        public static bool Text(JsonObject p, string owner, string key, IList<string> errors, out string value)
        {
            value = null;
            if (!Present(p, owner, key, errors)) return false;

            string text;
            if (!p.TryGetString(key, out text) || string.IsNullOrWhiteSpace(text))
            {
                errors.Add(Where(owner, key) + " must be a non-empty string");
                return false;
            }

            value = text;
            return true;
        }

        /// <summary>An optional non-empty text; null when the key is absent. A key that is present must be a non-empty string.</summary>
        public static bool OptionalText(JsonObject p, string owner, string key, IList<string> errors, out string value)
        {
            value = null;
            if (p == null || !p.ContainsKey(key)) return true;
            return Text(p, owner, key, errors, out value);
        }

        /// <summary>An optional boolean; <paramref name="fallback"/> when the key is absent. A key that is present must be true or false.</summary>
        public static bool OptionalBool(JsonObject p, string owner, string key, bool fallback, IList<string> errors, out bool value)
        {
            value = fallback;
            if (p == null || !p.ContainsKey(key)) return true;

            if (!p.TryGetBool(key, out value))
            {
                value = fallback;
                errors.Add(Where(owner, key) + " must be true or false");
                return false;
            }

            return true;
        }

        /// <summary>The prefix of a message: <c>ModifierRamp.params.peak</c>.</summary>
        public static string Where(string owner, string key)
        {
            return owner + ".params." + key;
        }

        /// <summary>A number as it goes into a message: invariant culture, no trailing zeros.</summary>
        public static string Show(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static bool Present(JsonObject p, string owner, string key, IList<string> errors)
        {
            if (p != null && p.ContainsKey(key)) return true;

            errors.Add(Where(owner, key) + " is required");
            return false;
        }

        private static bool InRange(string owner, string key, double value, double min, double max, IList<string> errors)
        {
            if (value >= min && value <= max) return true;

            errors.Add(Where(owner, key) + " must be in [" + Show(min) + ", " + Show(max) + "], got " + Show(value));
            return false;
        }
    }
}
