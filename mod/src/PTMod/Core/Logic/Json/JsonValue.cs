using System;
using System.Globalization;
using System.Text;

namespace PTMod.Core.Logic.Json
{
    /// <summary>
    /// A JSON scalar: null, bool, number or string. Immutable; <see cref="Null"/>, <see cref="True"/> and
    /// <see cref="False"/> are shared instances. Numbers are doubles and always finite (JSON has no NaN or
    /// infinity). Integer literals of 16 or more digits (ids, 64-bit counters) additionally keep their exact
    /// text, so they survive a parse/write round trip and <see cref="TryGetInt64"/> / <see cref="TryGetUInt64"/>
    /// read them without losing precision.
    /// </summary>
    public sealed class JsonValue : JsonNode
    {
        public static readonly JsonValue Null = new JsonValue(JsonKind.Null, false, 0, null, null);
        public static readonly JsonValue True = new JsonValue(JsonKind.Bool, true, 0, null, null);
        public static readonly JsonValue False = new JsonValue(JsonKind.Bool, false, 0, null, null);

        // Below this many digits an integer literal is exact in a double (2^53 is about 9.007e15).
        private const int ExactIntegerDigits = 15;

        private readonly JsonKind _kind;
        private readonly bool _bool;
        private readonly double _number;
        private readonly string _string;
        private readonly string _raw;   // exact text of a long integer literal; null for all other numbers

        private JsonValue(JsonKind kind, bool boolValue, double number, string stringValue, string raw)
        {
            _kind = kind;
            _bool = boolValue;
            _number = number;
            _string = stringValue;
            _raw = raw;
        }

        public override JsonKind Kind => _kind;

        /// <summary>The bool of a <see cref="JsonKind.Bool"/> value; false for every other kind.</summary>
        public bool BoolValue => _bool;

        /// <summary>The number of a <see cref="JsonKind.Number"/> value; 0 for every other kind.</summary>
        public double NumberValue => _number;

        /// <summary>The text of a <see cref="JsonKind.String"/> value; null for every other kind.</summary>
        public string StringValue => _string;

        // ---- factories ----

        public static JsonValue Of(bool value)
        {
            return value ? True : False;
        }

        /// <summary>A null string becomes JSON null.</summary>
        public static JsonValue Of(string value)
        {
            return value == null ? Null : new JsonValue(JsonKind.String, false, 0, value, null);
        }

        /// <summary>NaN and infinities cannot be written in JSON: they become JSON null.</summary>
        public static JsonValue Of(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return Null;
            return new JsonValue(JsonKind.Number, false, value, null, null);
        }

        /// <summary>
        /// A float is written in its own shortest text: 0.1f becomes 0.1 (not 0.10000000149011612), so the value reads back
        /// as the same float. NaN and infinities become JSON null.
        /// </summary>
        public static JsonValue Of(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return Null;

            // 7, 8 or 9 significant digits are enough to identify a float ("R" on .NET Framework skips the 8-digit form).
            string text = value.ToString("G7", CultureInfo.InvariantCulture);
            for (int digits = 8; digits <= 9 && !ParsesBackToFloat(text, value); digits++)
                text = value.ToString("G" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

            double shortest = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            return new JsonValue(JsonKind.Number, false, shortest, null, null);
        }

        public static JsonValue Of(long value)
        {
            double d = value;
            string raw = Math.Abs((double)value) >= 1e15 ? value.ToString(CultureInfo.InvariantCulture) : null;
            return new JsonValue(JsonKind.Number, false, d, null, raw);
        }

        public static JsonValue Of(ulong value)
        {
            double d = value;
            string raw = value >= 1000000000000000UL ? value.ToString(CultureInfo.InvariantCulture) : null;
            return new JsonValue(JsonKind.Number, false, d, null, raw);
        }

        /// <summary>For the parser: the value of a validated number literal; raw is set only for long integer literals.</summary>
        internal static JsonValue ParsedNumber(double value, string raw)
        {
            return new JsonValue(JsonKind.Number, false, value, null, raw);
        }

        internal static bool KeepsRawText(int integerDigits)
        {
            return integerDigits > ExactIntegerDigits;
        }

        // ---- reading numbers ----

        /// <summary>True when this is a number with no fractional part that fits in an Int64 (exactly, for long literals).</summary>
        public bool TryGetInt64(out long value)
        {
            value = 0;
            if (_kind != JsonKind.Number) return false;
            if (_raw != null) return long.TryParse(_raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

            double d = _number;
            if (d != Math.Floor(d)) return false;
            if (d < -9223372036854775808.0 || d >= 9223372036854775808.0) return false;
            value = (long)d;
            return true;
        }

        public bool TryGetUInt64(out ulong value)
        {
            value = 0;
            if (_kind != JsonKind.Number) return false;
            if (_raw != null) return ulong.TryParse(_raw, NumberStyles.None, CultureInfo.InvariantCulture, out value);

            double d = _number;
            if (d != Math.Floor(d)) return false;
            if (d < 0 || d >= 18446744073709551616.0) return false;
            value = (ulong)d;
            return true;
        }

        public bool TryGetInt32(out int value)
        {
            value = 0;
            long wide;
            if (!TryGetInt64(out wide) || wide < int.MinValue || wide > int.MaxValue) return false;
            value = (int)wide;
            return true;
        }

        // ---- text of a number ----

        /// <summary>The JSON text of a number node: the exact literal for long integers, otherwise the shortest round-trip form.</summary>
        internal string NumberText()
        {
            return _raw ?? FormatNumber(_number);
        }

        /// <summary>
        /// Shortest text that parses back to the same double: integers without exponent up to 1e15, otherwise
        /// 15, 16 or 17 significant digits with a lowercase exponent without sign padding ("1e-7"). Negative zero is "-0".
        /// </summary>
        internal static string FormatNumber(double d)
        {
            if (d == 0) return 1.0 / d < 0 ? "-0" : "0";   // 1/-0 = -infinity; .NET Framework prints negative zero as "0"
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.InvariantCulture);

            string text = d.ToString("G15", CultureInfo.InvariantCulture);
            if (!ParsesBackTo(text, d))
            {
                text = d.ToString("G16", CultureInfo.InvariantCulture);
                if (!ParsesBackTo(text, d)) text = d.ToString("G17", CultureInfo.InvariantCulture);
            }
            return NormalizeExponent(text);
        }

        private static bool ParsesBackToFloat(string text, float expected)
        {
            float back;
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out back) && back == expected;
        }

        private static bool ParsesBackTo(string text, double expected)
        {
            double back;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out back) && back == expected;
        }

        /// <summary>"1E-07" -> "1e-7", "1.5E+20" -> "1.5e20": still valid JSON, shorter, and the same on every runtime.</summary>
        private static string NormalizeExponent(string text)
        {
            int e = text.IndexOf('E');
            if (e < 0) return text;

            int i = e + 1;
            bool negative = false;
            if (i < text.Length && (text[i] == '+' || text[i] == '-'))
            {
                negative = text[i] == '-';
                i++;
            }
            while (i < text.Length - 1 && text[i] == '0') i++;   // keep at least one digit

            var sb = new StringBuilder(text.Length);
            sb.Append(text, 0, e).Append('e');
            if (negative) sb.Append('-');
            sb.Append(text, i, text.Length - i);
            return sb.ToString();
        }

        // ---- node contract ----

        internal override JsonNode CloneCore(int depth)
        {
            return this;   // immutable
        }

        internal override bool EqualsCore(JsonNode other, int depth)
        {
            var value = other as JsonValue;
            if (value == null || value._kind != _kind) return false;
            switch (_kind)
            {
                case JsonKind.Null:
                    return true;
                case JsonKind.Bool:
                    return _bool == value._bool;
                case JsonKind.String:
                    return string.Equals(_string, value._string, StringComparison.Ordinal);
                default:
                    if (_raw == null && value._raw == null) return _number == value._number;   // -0 == 0
                    if (_raw != null && value._raw != null) return string.Equals(_raw, value._raw, StringComparison.Ordinal);

                    // One side is an exact long literal, the other a plain double: equal only if both are the same integer.
                    long a, b;
                    if (TryGetInt64(out a) && value.TryGetInt64(out b)) return a == b;
                    ulong ua, ub;
                    return TryGetUInt64(out ua) && value.TryGetUInt64(out ub) && ua == ub;
            }
        }

        internal override int HashCore(int depth)
        {
            switch (_kind)
            {
                case JsonKind.Null:
                    return 17;
                case JsonKind.Bool:
                    return _bool ? 19 : 23;
                case JsonKind.String:
                    return _string.GetHashCode();
                default:
                    return _number == 0 ? 0 : _number.GetHashCode();   // 0 and -0 are equal, so they hash alike
            }
        }
    }
}
