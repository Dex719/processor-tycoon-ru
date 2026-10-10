using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PTMod.Core.Logic.Json
{
    /// <summary>
    /// The mod's own JSON: a strict RFC 8259 parser into a <see cref="JsonNode"/> tree, a writer, and a DTO binder
    /// (decision 0006: no JsonUtility, no external library). Everything the mod reads from disk goes through here.
    /// <list type="bullet">
    /// <item>Input is limited to <see cref="MaxBytes"/> (8 MiB of UTF-8) and <see cref="MaxDepth"/> (64 nested containers).</item>
    /// <item><see cref="TryParse"/> never throws: any bad input gives false and a <see cref="JsonError"/> with line and column.</item>
    /// <item>Object members keep their order and unknown members survive a parse/write round trip; a duplicate key is an error.</item>
    /// <item>Numbers must be finite (NaN, Infinity and out-of-range literals are rejected).</item>
    /// <item><see cref="Bind{T}"/> and <see cref="From(object)"/> map DTOs (public fields and properties) to and from the tree.</item>
    /// </list>
    /// No Unity or game types: the same file is compiled into the game and into the unit tests.
    /// </summary>
    public static class ModJson
    {
        /// <summary>Largest accepted input, in bytes of UTF-8 text.</summary>
        public const int MaxBytes = 8 * 1024 * 1024;

        /// <summary>Deepest accepted nesting of arrays and objects (the document root counts as level 1).</summary>
        public const int MaxDepth = 64;

        // ---- parse ----

        /// <summary>
        /// Parses a JSON document (any value, not only an object). On success returns true with the tree and a null
        /// error; otherwise false, a null node and the error with a 1-based line and column. A leading BOM is ignored.
        /// Never throws.
        /// </summary>
        public static bool TryParse(string text, out JsonNode node, out JsonError error)
        {
            node = null;
            error = null;
            try
            {
                if (text == null)
                {
                    error = new JsonError("the input is null", 1, 1, 0);
                    return false;
                }
                if (!FitsInLimit(text))
                {
                    error = new JsonError("the input is larger than 8 MiB (" + MaxBytes.ToString(CultureInfo.InvariantCulture) + " bytes)", 1, 1, 0);
                    return false;
                }

                var parser = new Parser(text);
                JsonNode result = parser.ParseDocument();
                if (result == null)
                {
                    error = parser.Error;
                    return false;
                }
                node = result;
                return true;
            }
            catch (Exception ex)
            {
                // Defensive: the parser has no known way to throw; whatever happens must not reach the game.
                node = null;
                error = new JsonError("internal parser error: " + ex.GetType().Name + ": " + ex.Message, 1, 1, 0);
                return false;
            }
        }

        private static bool FitsInLimit(string text)
        {
            if (text.Length > MaxBytes) return false;                    // a char takes at least one byte
            if ((long)text.Length * 3 <= MaxBytes) return true;          // and at most three (a lone surrogate)
            return Encoding.UTF8.GetByteCount(text) <= MaxBytes;
        }

        // ---- write ----

        /// <summary>
        /// JSON text of the tree: compact (<c>{"a":1}</c>) or indented by two spaces with "\n" line breaks.
        /// A null node is written as <c>null</c>. Throws <see cref="InvalidOperationException"/> if the tree is deeper
        /// than <see cref="MaxDepth"/> (a text the parser would refuse; also stops a cyclic hand-built tree).
        /// </summary>
        public static string Write(JsonNode node, bool pretty)
        {
            var sb = new StringBuilder();
            using (var writer = new StringWriter(sb, CultureInfo.InvariantCulture))
            {
                new Writer(writer, pretty).WriteNode(node, 0);
            }
            return sb.ToString();
        }

        /// <summary>Streaming form of <see cref="Write(JsonNode, bool)"/>: no intermediate string for big documents.</summary>
        public static void Write(JsonNode node, TextWriter writer, bool pretty)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            new Writer(writer, pretty).WriteNode(node, 0);
        }

        // ---- DTO mapping ----

        /// <summary>
        /// Builds a <typeparamref name="T"/> from the tree: public instance fields and properties with a public setter
        /// are filled by name (case-sensitive); unknown JSON members are ignored, missing ones keep the value the
        /// constructor and initializers gave. A member of the wrong kind or out of range keeps its default and adds an
        /// error "$.a[2].b: expected number, got string" to <paramref name="errors"/> (may be null). Always returns an object.
        /// Supported: bool, all integer types, float, double, decimal, string, char, enums (by name or number),
        /// Nullable, arrays, List, HashSet, Dictionary with string keys (and their interfaces), nested DTOs,
        /// <see cref="JsonNode"/> members (copied). A JSON null sets a reference or Nullable member to null and leaves a plain value member alone.
        /// </summary>
        public static T Bind<T>(JsonNode node, List<string> errors) where T : new()
        {
            return JsonBinder.Bind<T>(node, errors);
        }

        /// <summary>Non-generic <see cref="Bind{T}"/>; returns null when the type cannot be created at all (the error says why).</summary>
        public static object Bind(Type type, JsonNode node, List<string> errors)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return JsonBinder.Bind(type, node, errors);
        }

        /// <summary>
        /// Converts a DTO (or list, dictionary, scalar) to a tree: the inverse of <see cref="Bind{T}"/>, so
        /// <c>Bind&lt;T&gt;(From(x))</c> gives an equal object. C# null members are written as JSON null, NaN and infinities become JSON null.
        /// Never throws.
        /// </summary>
        public static JsonNode From(object dto)
        {
            return JsonBinder.From(dto, null);
        }

        /// <summary>Like <see cref="From(object)"/>, with a list that receives a description of everything that could not be converted.</summary>
        public static JsonNode From(object dto, List<string> errors)
        {
            return JsonBinder.From(dto, errors);
        }

        // ---- parser ----

        private sealed class Parser
        {
            private const char BomChar = (char)0xFEFF;

            private readonly string _s;
            private readonly int _origin;   // 1 when the text starts with a BOM: it is skipped and not counted in columns
            private int _p;
            private int _depth;

            public Parser(string text)
            {
                _s = text;
                _origin = text.Length > 0 && text[0] == BomChar ? 1 : 0;
                _p = _origin;
            }

            public JsonError Error { get; private set; }

            public JsonNode ParseDocument()
            {
                JsonNode value = ParseValue();
                if (value == null) return null;

                SkipWhitespace();
                if (_p < _s.Length)
                    return FailNode("unexpected character " + Describe(_s[_p]) + " after the end of the JSON value", _p);
                return value;
            }

            private JsonNode ParseValue()
            {
                SkipWhitespace();
                if (_p >= _s.Length) return FailNode("unexpected end of input, a JSON value is expected", _p);

                char c = _s[_p];
                switch (c)
                {
                    case '{':
                        return ParseObject();
                    case '[':
                        return ParseArray();
                    case '"':
                        {
                            string text = ParseString();
                            return text == null ? null : JsonValue.Of(text);
                        }
                    case 't':
                        return ParseLiteral("true", JsonValue.True);
                    case 'f':
                        return ParseLiteral("false", JsonValue.False);
                    case 'n':
                        return ParseLiteral("null", JsonValue.Null);
                    default:
                        if (c == '-' || IsDigit(c)) return ParseNumber();
                        return FailNode("unexpected character " + Describe(c), _p);
                }
            }

            private JsonNode ParseObject()
            {
                int start = _p;
                _p++;   // '{'
                if (++_depth > MaxDepth) return FailNode(DepthMessage(), start);

                var obj = new JsonObject();
                SkipWhitespace();
                if (_p < _s.Length && _s[_p] == '}')
                {
                    _p++;
                    _depth--;
                    return obj;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (_p >= _s.Length) return FailNode("unexpected end of input, a string key or '}' is expected", _p);
                    if (_s[_p] != '"')
                        return FailNode("a string key is expected in an object, found " + Describe(_s[_p]), _p);

                    int keyPosition = _p;
                    string key = ParseString();
                    if (key == null) return null;

                    SkipWhitespace();
                    if (_p >= _s.Length) return FailNode("unexpected end of input, ':' is expected after the key", _p);
                    if (_s[_p] != ':') return FailNode("':' is expected after the key, found " + Describe(_s[_p]), _p);
                    _p++;

                    JsonNode value = ParseValue();
                    if (value == null) return null;
                    if (!obj.TryAppend(key, value))
                        return FailNode("duplicate key \"" + Shorten(key) + "\"", keyPosition);

                    SkipWhitespace();
                    if (_p >= _s.Length) return FailNode("unexpected end of input, ',' or '}' is expected", _p);
                    char c = _s[_p];
                    if (c == ',')
                    {
                        _p++;
                        continue;
                    }
                    if (c == '}')
                    {
                        _p++;
                        _depth--;
                        return obj;
                    }
                    return FailNode("',' or '}' is expected, found " + Describe(c), _p);
                }
            }

            private JsonNode ParseArray()
            {
                int start = _p;
                _p++;   // '['
                if (++_depth > MaxDepth) return FailNode(DepthMessage(), start);

                var array = new JsonArray();
                SkipWhitespace();
                if (_p < _s.Length && _s[_p] == ']')
                {
                    _p++;
                    _depth--;
                    return array;
                }

                while (true)
                {
                    JsonNode value = ParseValue();
                    if (value == null) return null;
                    array.AppendParsed(value);

                    SkipWhitespace();
                    if (_p >= _s.Length) return FailNode("unexpected end of input, ',' or ']' is expected", _p);
                    char c = _s[_p];
                    if (c == ',')
                    {
                        _p++;
                        continue;
                    }
                    if (c == ']')
                    {
                        _p++;
                        _depth--;
                        return array;
                    }
                    return FailNode("',' or ']' is expected, found " + Describe(c), _p);
                }
            }

            private JsonNode ParseLiteral(string word, JsonValue value)
            {
                for (int i = 0; i < word.Length; i++)
                {
                    int position = _p + i;
                    if (position >= _s.Length)
                        return FailNode("unexpected end of input, '" + word + "' is incomplete", position);
                    if (_s[position] != word[i])
                        return FailNode("unexpected character " + Describe(_s[position]) + ", '" + word + "' is expected", position);
                }
                _p += word.Length;
                return value;
            }

            /// <summary>Parses a string token at <c>_p</c> (the opening quote). Returns null (with Error set) on failure.</summary>
            private string ParseString()
            {
                int start = _p;
                int length = _s.Length;
                int i = start + 1;

                // Fast path: no escapes, the common case.
                while (i < length)
                {
                    char c = _s[i];
                    if (c == '"')
                    {
                        _p = i + 1;
                        return _s.Substring(start + 1, i - start - 1);
                    }
                    if (c == '\\') break;
                    if (c < ' ') return FailString("control character " + Describe(c) + " must be escaped inside a string", i);
                    i++;
                }
                if (i >= length) return FailString("unterminated string, the closing '\"' is missing", start);

                var sb = new StringBuilder();
                sb.Append(_s, start + 1, i - start - 1);
                while (i < length)
                {
                    char c = _s[i];
                    if (c == '"')
                    {
                        _p = i + 1;
                        return sb.ToString();
                    }
                    if (c < ' ') return FailString("control character " + Describe(c) + " must be escaped inside a string", i);
                    if (c != '\\')
                    {
                        sb.Append(c);
                        i++;
                        continue;
                    }

                    if (i + 1 >= length) return FailString("unterminated string, the closing '\"' is missing", start);
                    char e = _s[i + 1];
                    switch (e)
                    {
                        case '"': sb.Append('"'); i += 2; break;
                        case '\\': sb.Append('\\'); i += 2; break;
                        case '/': sb.Append('/'); i += 2; break;
                        case 'b': sb.Append('\b'); i += 2; break;
                        case 'f': sb.Append('\f'); i += 2; break;
                        case 'n': sb.Append('\n'); i += 2; break;
                        case 'r': sb.Append('\r'); i += 2; break;
                        case 't': sb.Append('\t'); i += 2; break;
                        case 'u':
                            {
                                int code = 0;
                                for (int k = 0; k < 4; k++)
                                {
                                    int at = i + 2 + k;
                                    if (at >= length) return FailString("incomplete \\u escape, 4 hex digits are expected", i);
                                    int digit = HexValue(_s[at]);
                                    if (digit < 0)
                                        return FailString("invalid \\u escape, " + Describe(_s[at]) + " is not a hex digit", at);
                                    code = code * 16 + digit;
                                }
                                sb.Append((char)code);   // a surrogate pair written as two escapes simply becomes two chars
                                i += 6;
                                break;
                            }
                        default:
                            return FailString("invalid escape sequence \\" + Printable(e), i);
                    }
                }
                return FailString("unterminated string, the closing '\"' is missing", start);
            }

            private JsonNode ParseNumber()
            {
                int start = _p;
                int length = _s.Length;
                int p = start;

                bool negative = false;
                if (_s[p] == '-')
                {
                    negative = true;
                    p++;
                }

                int integerStart = p;
                if (p >= length || !IsDigit(_s[p])) return FailNode(NumberExpected("a digit", p), p);
                if (_s[p] == '0')
                {
                    p++;
                    if (p < length && IsDigit(_s[p])) return FailNode("invalid number, leading zeros are not allowed", p);
                }
                else
                {
                    while (p < length && IsDigit(_s[p])) p++;
                }
                int integerDigits = p - integerStart;

                bool isInteger = true;
                if (p < length && _s[p] == '.')
                {
                    isInteger = false;
                    p++;
                    int fractionStart = p;
                    while (p < length && IsDigit(_s[p])) p++;
                    if (p == fractionStart) return FailNode(NumberExpected("a digit after '.'", p), p);
                }
                if (p < length && (_s[p] == 'e' || _s[p] == 'E'))
                {
                    isInteger = false;
                    p++;
                    if (p < length && (_s[p] == '+' || _s[p] == '-')) p++;
                    int exponentStart = p;
                    while (p < length && IsDigit(_s[p])) p++;
                    if (p == exponentStart) return FailNode(NumberExpected("a digit in the exponent", p), p);
                }
                _p = p;

                if (isInteger && !JsonValue.KeepsRawText(integerDigits))
                {
                    // Up to 15 digits: exact in a double, no need for the general conversion.
                    long accumulated = 0;
                    for (int i = integerStart; i < integerStart + integerDigits; i++) accumulated = accumulated * 10 + (_s[i] - '0');
                    double exact = negative ? -(double)accumulated : accumulated;   // "-0" stays negative zero
                    return JsonValue.ParsedNumber(exact, null);
                }

                string literal = _s.Substring(start, p - start);
                double value;
                try
                {
                    value = double.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);
                }
                catch (OverflowException)
                {
                    return FailNode("the number is out of range", start);
                }
                if (double.IsNaN(value) || double.IsInfinity(value)) return FailNode("the number is out of range", start);
                // .NET Framework's double.Parse turns "-0.0" into +0; JSON text keeps the sign, so does the tree.
                if (negative && value == 0 && BitConverter.DoubleToInt64Bits(value) >= 0) value = -value;

                return JsonValue.ParsedNumber(value, isInteger ? literal : null);
            }

            private string NumberExpected(string what, int position)
            {
                return "invalid number, " + what + " is expected, "
                       + (position >= _s.Length ? "but the input ended" : "found " + Describe(_s[position]));
            }

            private void SkipWhitespace()
            {
                while (_p < _s.Length)
                {
                    char c = _s[_p];
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r') return;
                    _p++;
                }
            }

            private static bool IsDigit(char c)
            {
                return c >= '0' && c <= '9';
            }

            private static int HexValue(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }

            private static string DepthMessage()
            {
                return "nesting is deeper than " + MaxDepth.ToString(CultureInfo.InvariantCulture) + " levels";
            }

            /// <summary>'x' for a printable character, U+000A for control, surrogate and other unprintable ones.</summary>
            private static string Describe(char c)
            {
                return IsPrintable(c) ? "'" + c + "'" : "U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture);
            }

            private static string Printable(char c)
            {
                return IsPrintable(c) ? c.ToString() : "U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture);
            }

            private static bool IsPrintable(char c)
            {
                // Blanks other than the plain space (NBSP, U+2003, U+2028...) look like nothing in a message: show their code.
                return c >= 0x20 && !char.IsSurrogate(c) && !char.IsControl(c) && (c == ' ' || !char.IsWhiteSpace(c));
            }

            private static string Shorten(string key)
            {
                return key.Length <= 40 ? key : key.Substring(0, 40) + "...";
            }

            private JsonNode FailNode(string message, int position)
            {
                Fail(message, position);
                return null;
            }

            private string FailString(string message, int position)
            {
                Fail(message, position);
                return null;
            }

            /// <summary>Records the first error with its line and column (computed only now: the success path never counts lines).</summary>
            private void Fail(string message, int position)
            {
                if (Error != null) return;

                int end = Math.Min(position, _s.Length);
                int line = 1;
                int lineStart = _origin;
                for (int i = _origin; i < end; i++)
                {
                    char c = _s[i];
                    if (c == '\n')
                    {
                        line++;
                        lineStart = i + 1;
                    }
                    else if (c == '\r' && !(i + 1 < _s.Length && _s[i + 1] == '\n'))
                    {
                        line++;   // lone "\r"; in "\r\n" the "\n" ends the line
                        lineStart = i + 1;
                    }
                }
                Error = new JsonError(message, line, position - lineStart + 1, position);
            }
        }

        // ---- writer ----

        private sealed class Writer
        {
            private static readonly string[] Indents = new string[MaxDepth + 2];

            private readonly TextWriter _w;
            private readonly bool _pretty;
            private StringBuilder _escaped;

            public Writer(TextWriter writer, bool pretty)
            {
                _w = writer;
                _pretty = pretty;
            }

            /// <param name="depth">Number of containers around this node.</param>
            public void WriteNode(JsonNode node, int depth)
            {
                if (node == null)
                {
                    _w.Write("null");
                    return;
                }

                switch (node.Kind)
                {
                    case JsonKind.Null:
                        _w.Write("null");
                        break;
                    case JsonKind.Bool:
                        _w.Write(((JsonValue)node).BoolValue ? "true" : "false");
                        break;
                    case JsonKind.Number:
                        _w.Write(((JsonValue)node).NumberText());
                        break;
                    case JsonKind.String:
                        WriteString(((JsonValue)node).StringValue);
                        break;
                    case JsonKind.Array:
                        WriteArray((JsonArray)node, depth + 1);
                        break;
                    default:
                        WriteObject((JsonObject)node, depth + 1);
                        break;
                }
            }

            /// <param name="level">Nesting level of the array itself (the root container is 1).</param>
            private void WriteArray(JsonArray array, int level)
            {
                JsonNode.CheckDepth(level);
                if (array.Count == 0)
                {
                    _w.Write("[]");
                    return;
                }

                _w.Write('[');
                for (int i = 0; i < array.Count; i++)
                {
                    if (i > 0) _w.Write(',');
                    NewLine(level);
                    WriteNode(array[i], level);
                }
                NewLine(level - 1);
                _w.Write(']');
            }

            private void WriteObject(JsonObject obj, int level)
            {
                JsonNode.CheckDepth(level);
                if (obj.Count == 0)
                {
                    _w.Write("{}");
                    return;
                }

                _w.Write('{');
                bool first = true;
                foreach (KeyValuePair<string, JsonNode> member in obj)
                {
                    if (!first) _w.Write(',');
                    first = false;
                    NewLine(level);
                    WriteString(member.Key);
                    _w.Write(_pretty ? ": " : ":");
                    WriteNode(member.Value, level);
                }
                NewLine(level - 1);
                _w.Write('}');
            }

            private void NewLine(int level)
            {
                if (!_pretty) return;
                _w.Write('\n');
                if (level <= 0) return;
                string indent = Indents[level] ?? (Indents[level] = new string(' ', level * 2));
                _w.Write(indent);
            }

            private void WriteString(string text)
            {
                if (!NeedsEscaping(text))
                {
                    _w.Write('"');
                    _w.Write(text);
                    _w.Write('"');
                    return;
                }

                StringBuilder sb = _escaped ?? (_escaped = new StringBuilder());
                sb.Length = 0;
                sb.Append('"');
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        default:
                            if (c < ' ')
                            {
                                AppendUnicodeEscape(sb, c);
                            }
                            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                            {
                                sb.Append(c).Append(text[i + 1]);   // a valid pair stays as text
                                i++;
                            }
                            else if (char.IsSurrogate(c))
                            {
                                AppendUnicodeEscape(sb, c);         // a lone surrogate cannot be encoded as UTF-8: keep it as \uXXXX
                            }
                            else
                            {
                                sb.Append(c);
                            }
                            break;
                    }
                }
                sb.Append('"');
                _w.Write(sb.ToString());
            }

            private static bool NeedsEscaping(string text)
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c < ' ' || c == '"' || c == '\\') return true;
                    if (char.IsSurrogate(c))
                    {
                        if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                        {
                            i++;
                            continue;
                        }
                        return true;
                    }
                }
                return false;
            }

            private static void AppendUnicodeEscape(StringBuilder sb, char c)
            {
                sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            }
        }
    }
}
